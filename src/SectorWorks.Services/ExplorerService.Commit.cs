using SectorWorks.Core;
using SectorWorks.Storage;

namespace SectorWorks.Services;

public sealed partial class ExplorerService
{
    /// <summary>
    /// Phase 3 safe direct commit: source -> lock -> identity recheck -> space check ->
    /// backup -> apply (to a disposable staged copy) -> flush -> reopen -> validate ->
    /// finalize (atomic replace of the source) -> cleanup -> unlock. The source is only
    /// ever modified by the single atomic finalize step; every earlier step operates on a
    /// disposable sibling temporary file, matching the already-qualified Save As pipeline.
    /// </summary>
    public Task<CommitResult> CommitAsync(Guid workspaceId, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "CommitInPlace", lease =>
        {
            Capability editing = writer.Qualify(lease.Image);
            RequireCapability(editing);
            if (!CommitCapability(editing).Available)
            {
                throw new NotSupportedException("This image stack is not qualified for direct commit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            string source = Path.GetFullPath(lease.Image.Metadata.Path);
            string directory = Path.GetDirectoryName(source)!;
            progress?.Report(new OperationProgress("Verifying source baseline", source, 0, 0));
            VerifySource(lease, cancellationToken);

            EditingState state = EnsureEditing(lease, cancellationToken);
            StagedWriteOperation[] operations = state.Operations.Take(state.Position).ToArray();
            if (operations.Length == 0)
            {
                throw new InvalidOperationException("There are no pending changes to commit.");
            }
            writer.ValidateCapacity(lease.Image, operations, cancellationToken);
            long length = lease.Image.Metadata.ContainerLength;
            long requiredSpace = checked(length * 2 + lease.Image.Device.Length);
            if (storageSpace.AvailableBytes(directory) < requiredSpace)
            {
                throw new IOException("Insufficient space next to the source for the direct-commit backup and bounded writer scratch space.");
            }

            string transactionId = Guid.NewGuid().ToString("N");
            string journalPath = CommitJournal.PathFor(source, transactionId);
            string backupPath = CommitJournal.BackupPathFor(source, transactionId);
            string staging = Path.Combine(directory, ".sectorworks-commit-" + transactionId + ".img");
            bool stagingCreated = false;
            bool journalWritten = false;
            bool backupCreated = false;

            progress?.Report(new OperationProgress("Acquiring exclusive lock", source, 0, 0));
            using SourceWriteLock writeLock = SourceWriteLock.Acquire(source);
            try
            {
                VerifySource(lease, cancellationToken);

                string[] changedVolumes = operations.Select(operation => operation.Request.VolumeId)
                    .Distinct(StringComparer.Ordinal).ToArray();
                var expected = changedVolumes.ToDictionary(volumeId => volumeId,
                    volumeId => Fingerprint(GetVolume(lease, volumeId).FileSystem, cancellationToken), StringComparer.Ordinal);

                progress?.Report(new OperationProgress("Creating recovery backup", backupPath, 0, 0));
                CommitBackup.Create(source, backupPath);
                backupCreated = true;
                CommitJournal.Write(journalPath, new TransactionRecord(transactionId, source, backupPath, staging, CommitStage.BackupTaken, DateTime.UtcNow));
                journalWritten = true;

                progress?.Report(new OperationProgress("Staging changes", staging, 0, 0));
                File.Copy(backupPath, staging, overwrite: false);
                stagingCreated = true;
                writer.Apply(staging, operations, cancellationToken);
                using (var output = new FileStream(staging, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { output.Flush(true); }
                CommitJournal.Write(journalPath, new TransactionRecord(transactionId, source, backupPath, staging, CommitStage.Staged, DateTime.UtcNow));

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress("Validating tree and SHA-256", staging, length, 0));
                using (IImageSession reopened = opener.OpenReadOnly(staging, cancellationToken))
                {
                    if (reopened.Metadata.Format != lease.Image.Metadata.Format
                        || reopened.Device.Length != lease.Image.Device.Length
                        || reopened.Device.LogicalSectorSize != lease.Image.Device.LogicalSectorSize
                        || reopened.Device.PhysicalSectorSize != lease.Image.Device.PhysicalSectorSize
                        || !reopened.Partitions.SequenceEqual(lease.Image.Partitions)
                        || reopened.Volumes.Count != lease.Image.Volumes.Count
                        || reopened.Volumes.Where((volume, index) => volume.Id != lease.Image.Volumes[index].Id
                            || volume.FileSystem.Name != lease.Image.Volumes[index].FileSystem.Name
                            || volume.Partition != lease.Image.Volumes[index].Partition).Any())
                    {
                        throw new InvalidDataException("Staged output container, partitions or filesystem identities differ from the source.");
                    }
                    foreach (string volumeId in changedVolumes)
                    {
                        Capability outputCapability = writer.QualifyVolume(reopened, volumeId);
                        if (!outputCapability.Available)
                        {
                            throw new InvalidDataException("Staged output structural validation failed: " + outputCapability.Reason);
                        }
                        var changedPaths = state.Current.Volumes[volumeId].Values.Where(node => node.Entry.IsChanged)
                            .Select(node => node.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var actual = Fingerprint(reopened.Volumes.Single(volume => volume.Id == volumeId).FileSystem, cancellationToken, changedPaths);
                        Dictionary<string, string> expectedTree = expected[volumeId];
                        if (expectedTree.Count != actual.Count || expectedTree.Any(pair => !actual.TryGetValue(pair.Key, out string? value) || value != pair.Value))
                        {
                            throw new InvalidDataException("Staged output validation failed: directory tree, file lengths or SHA-256 content hashes differ.");
                        }
                    }
                    try { writer.ValidateOutput(lease.Image, reopened, operations, cancellationToken); }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
                    {
                        throw new InvalidDataException("Staged output metadata/preservation validation failed: " + error.Message, error);
                    }
                }
                CommitJournal.Write(journalPath, new TransactionRecord(transactionId, source, backupPath, staging, CommitStage.Validated, DateTime.UtcNow));

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress("Rechecking source baseline", source, length, 0));
                VerifySource(lease, cancellationToken);

                progress?.Report(new OperationProgress("Finalizing commit", source, length, 0));
                File.Move(staging, source, overwrite: true);
                stagingCreated = false;
                // The source is now authoritative; a crash past this point must never restore the backup.
                CommitJournal.Write(journalPath, new TransactionRecord(transactionId, source, backupPath, staging, CommitStage.Finalized, DateTime.UtcNow));

                File.Delete(backupPath);
                CommitJournal.Delete(journalPath);
                journalWritten = false;

                int files = changedVolumes.Sum(volumeId => state.Current.Volumes[volumeId].Values.Count(node => !node.Entry.IsDirectory));
                long outputLength = new FileInfo(source).Length;
                log.Write("CommitValidated", source, $"Direct-committed {files} files; backup and journal removed after validated finalize.");
                return new CommitResult(source, outputLength, files,
                    "Locked source; retained backup until validated reopen; reopened image; container/partition identities and qualified volume allocation, metadata, complete directory trees, lengths and SHA-256 hashes verified; atomically finalized.",
                    transactionId);
            }
            catch (Exception)
            {
                if (stagingCreated)
                {
                    try { File.Delete(staging); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                    {
                        log.Write("ResidualCommitStaging", staging, "Incomplete staged output could not be removed: " + staging, "Warning");
                    }
                }
                if (journalWritten)
                {
                    log.Write("InterruptedCommit", source,
                        $"Direct commit failed before finalize; source was not modified. Backup retained at {backupPath}; journal retained at {journalPath} for recovery.",
                        "Warning");
                }
                else if (backupCreated)
                {
                    // Backup was taken but no journal references it yet (failure happened in the
                    // narrow window between copy and the first durable journal write); it is
                    // orphaned and safe to remove since no recovery scan can find it anyway.
                    try { File.Delete(backupPath); } catch (IOException) { }
                }
                throw;
            }
        }, cancellationToken);
}
