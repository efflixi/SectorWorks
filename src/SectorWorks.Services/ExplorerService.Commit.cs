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
            string staging = CommitJournal.StagingPathFor(source, transactionId);
            bool stagingCreated = false;
            bool journalWritten = false;
            bool backupCreated = false;
            bool finalized = false;
            Exception? commitFailure = null;
            IImageSession? baseline = null;
            SourceWriteLock? writeLock = null;
            string? outputHash = null;
            long outputLength = length;
            string sourceHash = lease.SourceHash ?? throw new IOException("Direct commit requires a source baseline.");
            string[] changedVolumes = operations.Select(operation => operation.Request.VolumeId)
                .Distinct(StringComparer.Ordinal).ToArray();
            foreach (string volumeId in changedVolumes)
            { RequireCapability(CommitCapability(writer.QualifyCommitVolume(lease.Image, volumeId), source)); }
            ValidateCommitSource(source, transactionId);
            var expected = changedVolumes.ToDictionary(volumeId => volumeId,
                volumeId => Fingerprint(GetVolume(lease, volumeId).FileSystem, cancellationToken), StringComparer.Ordinal);
            int files = changedVolumes.Sum(volumeId => state.Current.Volumes[volumeId].Values.Count(node => !node.Entry.IsDirectory));

            DateTime createdUtc = DateTime.UtcNow;
            CommitIntent intent = CaptureCommitIntent(state, cancellationToken);
            TransactionRecord Record(CommitStage stage) => new(transactionId, source, backupPath, staging, stage, createdUtc)
            { SourceSha256 = sourceHash, SourceLength = length, Intent = intent };
            // Refuse an oversized/invalid journal before releasing the reader,
            // acquiring write access or creating any recovery artifact.
            _ = CommitJournal.Serialize(journalPath, Record(CommitStage.BackupTaken));

            lease.Image.Dispose();
            try
            {
                progress?.Report(new OperationProgress("Acquiring exclusive lock", source, 0, 0));
                writeLock = SourceWriteLock.Acquire(source);
                writeLock.VerifySource(sourceHash, length, cancellationToken);
                // Register while holding the source lock, before any backup or
                // journal artifact is created. Catalog-only cleanup can take the
                // same lock to distinguish a stopped transaction from a live one.
                recoveryCatalog?.Track(transactionId, journalPath);
                progress?.Report(new OperationProgress("Registered recovery", journalPath, 0, 0));

                progress?.Report(new OperationProgress("Creating recovery backup", backupPath, 0, 0));
                writeLock.CreateBackup(backupPath, sourceHash, length, cancellationToken);
                backupCreated = true;
                CommitJournal.Write(journalPath, Record(CommitStage.BackupTaken));
                journalWritten = true;
                baseline = opener.OpenReadOnly(backupPath, cancellationToken);

                progress?.Report(new OperationProgress("Staging changes", staging, 0, 0));
                File.Copy(backupPath, staging, overwrite: false);
                stagingCreated = true;
                writer.Apply(staging, operations, cancellationToken);
                using (var output = new FileStream(staging, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { output.Flush(true); }
                CommitJournal.Write(journalPath, Record(CommitStage.Staged));

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress("Validating tree and SHA-256", staging, length, 0));
                using (IImageSession reopened = opener.OpenReadOnly(staging, cancellationToken))
                {
                    if (reopened.Metadata.Format != baseline.Metadata.Format
                        || reopened.Device.Length != baseline.Device.Length
                        || reopened.Device.LogicalSectorSize != baseline.Device.LogicalSectorSize
                        || reopened.Device.PhysicalSectorSize != baseline.Device.PhysicalSectorSize
                        || !reopened.Partitions.SequenceEqual(baseline.Partitions)
                        || reopened.Volumes.Count != baseline.Volumes.Count
                        || reopened.Volumes.Where((volume, index) => volume.Id != baseline.Volumes[index].Id
                            || volume.FileSystem.Name != baseline.Volumes[index].FileSystem.Name
                            || volume.Partition != baseline.Volumes[index].Partition).Any())
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
                    try { writer.ValidateOutput(baseline, reopened, operations, cancellationToken); }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
                    {
                        throw new InvalidDataException("Staged output metadata/preservation validation failed: " + error.Message, error);
                    }
                }
                CommitJournal.Write(journalPath, Record(CommitStage.Validated));

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress("Rechecking source baseline", source, length, 0));
                writeLock.VerifySource(sourceHash, length, cancellationToken);
                outputLength = new FileInfo(staging).Length;
                outputHash = HashSourceFile(staging, outputLength, cancellationToken);

                progress?.Report(new OperationProgress("Finalizing commit", source, length, 0));
                cancellationToken.ThrowIfCancellationRequested();
                writeLock.ReplaceSource(staging);
                finalized = true;
                stagingCreated = false;
                progress?.Report(new OperationProgress("Source replaced", source, outputLength, files));
                // The source is now authoritative; a crash past this point must never restore the backup.
                CommitJournal.Write(journalPath, Record(CommitStage.Finalized));

                baseline.Dispose();
                baseline = null;
                File.Delete(backupPath);
                CommitJournal.Delete(journalPath);
                journalWritten = false;
                recoveryCatalog?.Forget(transactionId);

                log.Write("CommitValidated", source, $"Direct-committed {files} files; backup and journal removed after validated finalize.");
                return new CommitResult(source, outputLength, files,
                    "Locked source; retained backup until validated reopen; reopened image; container/partition identities and qualified volume allocation, metadata, complete directory trees, lengths and SHA-256 hashes verified; atomically finalized.",
                    transactionId);
            }
            catch (Exception error)
            {
                commitFailure = error;
                if (finalized && !journalWritten)
                {
                    log.Write("CommitPostFinalizeFailure", source,
                        "Source was committed, but post-finalize reporting or cleanup failed: " + error.Message, "Error");
                }
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
                        finalized
                            ? $"Source was committed, but post-finalize bookkeeping failed. Do not restore the old backup. Review retained backup {backupPath} and journal {journalPath}."
                            : $"Direct commit failed before finalize; source was not replaced. Backup retained at {backupPath}; journal retained at {journalPath} for recovery.",
                        "Warning");
                }
                else if (backupCreated)
                {
                    // Backup was taken but no journal references it yet (failure happened in the
                    // narrow window between copy and the first durable journal write); it is
                    // orphaned and safe to remove since no recovery scan can find it anyway.
                    baseline?.Dispose();
                    baseline = null;
                    try { File.Delete(backupPath); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                    {
                        log.Write("ResidualCommitBackup", backupPath, cleanup.Message, "Warning");
                    }
                }
                if (!journalWritten && !finalized)
                {
                    try { recoveryCatalog?.Forget(transactionId); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                    {
                        log.Write("ResidualRecoveryRegistration", journalPath, cleanup.Message, "Warning");
                    }
                }
                throw;
            }
            finally
            {
                baseline?.Dispose();
                writeLock?.Dispose();
                IImageSession? refreshed = null;
                try
                {
                    refreshed = opener.OpenReadOnly(source);
                    string actualHash = HashSourceFile(source, finalized ? outputLength : length, CancellationToken.None);
                    if (actualHash != (finalized ? outputHash : sourceHash))
                    {
                        throw new IOException("Source no longer matches the verified workspace baseline.");
                    }
                    lease.Image = refreshed;
                    refreshed = null;
                    lease.SourceHash = actualHash;
                    if (finalized)
                    {
                        lease.Editing = null;
                        state.Dispose();
                    }
                }
                catch (Exception error) when (IsOperationFailure(error))
                {
                    lease.UnavailableReason = error.Message;
                    log.Write("CommitWorkspaceUnavailable", source,
                        (finalized ? "Source was committed. " : "Commit did not finalize. ")
                        + "Workspace could not be reopened; pending payloads remain owned until close. " + error.Message, "Error");
                    if (commitFailure is null)
                    {
                        throw new IOException("Source was committed, but the workspace could not be reopened. Close and reopen it.", error);
                    }
                }
                finally { refreshed?.Dispose(); }
            }
        }, cancellationToken);
}
