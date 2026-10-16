using System.Security.Cryptography;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.Services;

public sealed partial class ExplorerService
{
    public const long MaximumRetainedStagingBytes = 8L * 1024 * 1024 * 1024;

    public Task<EditingSnapshot> GetEditingSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "PendingChanges", Snapshot, cancellationToken);

    public Task<EditingSnapshot> StageAsync(Guid workspaceId, ImageChangeRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "Stage", lease =>
        {
            ArgumentNullException.ThrowIfNull(request);
            _ = GetVolume(lease, request.VolumeId);
            RequireCapability(writer.QualifyVolume(lease.Image, request.VolumeId));
            if (request.Kind == ImageChangeKind.Metadata || !Enum.IsDefined(request.Kind))
            {
                throw new NotSupportedException("Metadata changes are reserved; only add, replace, delete, rename and create directory are implemented.");
            }
            ValidateEditPath(request.Path);
            EditingState state = EnsureEditing(lease, cancellationToken);
            state.VerifySpool();
            if (state.Position >= 1024 || state.States.Take(state.Position + 1).Sum(s => (long)s.Count) > 1_000_000)
            {
                throw new InvalidDataException("Undo history exceeds the bounded editing resource budget; save and reopen the new image before continuing.");
            }
            var next = new Dictionary<string, StagedNode>(state.Current.Volumes[request.VolumeId], StringComparer.OrdinalIgnoreCase);
            string path = request.Path;
            StagedNode? existing = next.GetValueOrDefault(path);
            bool adding = request.Kind is ImageChangeKind.AddFile or ImageChangeKind.CreateDirectory;
            if (adding && existing is not null)
            {
                throw new IOException("An entry already exists at this case-insensitive image path.");
            }
            if (!adding && existing is null)
            {
                throw new FileNotFoundException("The staged target does not exist.", path);
            }
            if (!adding) { path = existing!.Entry.Path; }
            string parent = StagedFileSystem.Parent(path);
            if (parent != "/")
            {
                if (!next.TryGetValue(parent, out StagedNode? parentNode) || !parentNode.Entry.IsDirectory)
                {
                    throw new DirectoryNotFoundException("The target's parent directory does not exist.");
                }
                parent = parentNode.Entry.Path;
            }
            if (adding)
            {
                path = (parent == "/" ? "" : parent) + "/" + request.Path[(request.Path.LastIndexOf('/') + 1)..];
                ValidateEditPath(path);
            }
            request = request with { Path = path };
            for (string ancestor = parent; ancestor != "/"; ancestor = StagedFileSystem.Parent(ancestor))
            {
                if (next[ancestor].Entry.Attributes.Split(", ").Contains("ReadOnly", StringComparer.Ordinal))
                {
                    throw new NotSupportedException("Changes inside read-only image directories are not supported.");
                }
            }
            if (!adding)
            {
                if (next.Values.Any(node => IsWithin(node.Entry.Path, path)
                    && node.Entry.Attributes.Split(", ").Contains("ReadOnly", StringComparer.Ordinal)))
                {
                    throw new NotSupportedException("Read-only image entries cannot be modified; metadata editing is not implemented.");
                }
            }
            StagedContent? content = null;
            try
            {
                switch (request.Kind)
                {
                    case ImageChangeKind.AddFile:
                    case ImageChangeKind.ReplaceFile:
                        if (existing?.Entry.IsDirectory == true || existing?.Entry.IsLink == true)
                        {
                            throw new NotSupportedException("Only ordinary files may be replaced.");
                        }
                        if (string.IsNullOrWhiteSpace(request.SourcePath))
                        {
                            throw new ArgumentException("Add/replace requires a host source file.");
                        }
                        long importLength = new FileInfo(request.SourcePath).Length;
                        ImageVolume targetVolume = lease.Image.Volumes.Single(volume => volume.Id == request.VolumeId);
                        long volumeLength = targetVolume.Partition?.Length ?? lease.Image.Device.Length;
                        long maximumFileLength = targetVolume.FileSystem.Name == "NTFS"
                            ? volumeLength : Math.Min(uint.MaxValue, volumeLength);
                        if (importLength > maximumFileLength)
                        {
                            throw new IOException("Imported file exceeds filesystem file-size or image-capacity limits.");
                        }
                        ValidateStagingBudget(state.RetainedBytes, importLength);
                        WorkspaceDataFiles.CreatePrivateDirectory(state.DirectoryPath);
                        if (storageSpace.AvailableBytes(state.DirectoryPath) < checked(importLength + 65536))
                        {
                            throw new IOException("Insufficient staging storage space to snapshot the imported file.");
                        }
                        using (var input = new FileStream(request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            content = new StagedContent(input, state.DirectoryPath, importLength, maximumFileLength, cancellationToken);
                        }
                        if (content.Length > maximumFileLength)
                        {
                            throw new IOException("Imported file exceeds filesystem file-size or image-capacity limits.");
                        }
                        next[path] = new StagedNode(new FileEntry(path, path[(path.LastIndexOf('/') + 1)..], false,
                            content.Length, existing?.Entry.ModifiedUtc, existing?.Entry.Attributes ?? "Archive")
                        { IsChanged = true }, null, content);
                        break;
                    case ImageChangeKind.CreateDirectory:
                        next.Add(path, new StagedNode(new FileEntry(path, path[(path.LastIndexOf('/') + 1)..], true,
                            0, null, "Directory")
                        { IsChanged = true }, null, null));
                        break;
                    case ImageChangeKind.Delete:
                        foreach (string deleted in next.Keys.Where(p => IsWithin(p, path)).ToArray()) { next.Remove(deleted); }
                        break;
                    case ImageChangeKind.Rename:
                        if (string.IsNullOrWhiteSpace(request.NewName) || request.NewName.Contains('/'))
                        {
                            throw new ArgumentException("Rename requires a single filename.");
                        }
                        string renamed = (parent == "/" ? "" : parent) + "/" + request.NewName;
                        ValidateEditPath(renamed);
                        if (renamed.Equals(path, StringComparison.Ordinal))
                        {
                            throw new IOException("The rename does not change the filename.");
                        }
                        if (next.ContainsKey(renamed) && !renamed.Equals(path, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException("The rename destination already exists.");
                        }
                        foreach (string oldPath in next.Keys.Where(p => IsWithin(p, path)).ToArray())
                        {
                            StagedNode node = next[oldPath];
                            string newPath = renamed + oldPath[path.Length..];
                            next.Remove(oldPath);
                            next.Add(newPath, node with
                            {
                                Entry = node.Entry with { Path = newPath, Name = newPath[(newPath.LastIndexOf('/') + 1)..], IsChanged = true }
                            });
                        }
                        break;
                }
                var nextVolumes = new Dictionary<string, Dictionary<string, StagedNode>>(state.Current.Volumes, StringComparer.Ordinal)
                {
                    [request.VolumeId] = next
                };
                var nextTree = new EditingTree(nextVolumes);
                if (nextTree.Count > MaximumEntries || next.Keys.Any(p => p.Count(c => c == '/') > 128))
                {
                    throw new InvalidDataException("Staged tree exceeds traversal resource limits.");
                }
                if (state.States.Take(state.Position + 1).Sum(s => (long)s.Count) + nextTree.Count > 1_000_000)
                {
                    throw new InvalidDataException("Undo history exceeds the 1000000-node snapshot budget; save and reopen the new image before continuing.");
                }
                Func<Stream>? open = content is null ? null : content.OpenRead;
                var operation = new StagedWriteOperation(request with { SourcePath = null }, open);
                StagedWriteOperation[] operations = state.Operations.Take(state.Position).Append(operation).ToArray();
                writer.ValidateCapacity(lease.Image, operations, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                state.Operations.RemoveRange(state.Position, state.Operations.Count - state.Position);
                state.OperationContents.RemoveRange(state.Position, state.OperationContents.Count - state.Position);
                state.States.RemoveRange(state.Position + 1, state.States.Count - state.Position - 1);
                state.Operations.Add(operation);
                state.OperationContents.Add(content);
                state.States.Add(nextTree);
                state.Position++;
                if (content is not null) { state.Contents.Add(content); content = null; }
                state.ReleaseUnusedContents();
                log.Write("Staged", path, request.Kind.ToString());
                return Snapshot(lease);
            }
            finally
            {
                content?.Dispose();
            }
        }, cancellationToken);

    internal static void ValidateStagingBudget(long retainedBytes, long incomingBytes)
    {
        if (retainedBytes < 0 || incomingBytes < 0 || retainedBytes > MaximumRetainedStagingBytes
            || incomingBytes > MaximumRetainedStagingBytes - retainedBytes)
        {
            throw new IOException("Retained staging content (including undo/redo history) would exceed the 8 GiB workspace budget. Discard changes or save and reopen the new image before continuing.");
        }
    }

    public Task<EditingSnapshot> UndoAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "Undo", lease =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.Editing is { Position: > 0 } state) { state.Position--; }
            return Snapshot(lease);
        }, cancellationToken);

    public Task<EditingSnapshot> RedoAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "Redo", lease =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.Editing is { } state && state.Position < state.Operations.Count) { state.Position++; }
            return Snapshot(lease);
        }, cancellationToken);

    public Task<EditingSnapshot> DiscardAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "Discard", lease =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lease.Editing?.Dispose();
            lease.Editing = null;
            return Snapshot(lease);
        }, cancellationToken);

    public Task<SaveImageResult> SaveAsAsync(Guid workspaceId, string destination,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "SaveAs", lease =>
        {
            RequireCapability(writer.Qualify(lease.Image));
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.GetFullPath(destination);
            if (target.Equals(Path.GetFullPath(lease.Image.Metadata.Path), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Save As cannot target the source image.");
            }
            if (File.Exists(target) || Directory.Exists(target)) { throw new IOException("Save As never overwrites an existing destination."); }
            string directory = Path.GetDirectoryName(target)!;
            if (!Directory.Exists(directory)) { throw new DirectoryNotFoundException(directory); }
            progress?.Report(new OperationProgress("Verifying source baseline", target, 0, 0));
            VerifySource(lease, cancellationToken);
            long length = lease.Image.Metadata.ContainerLength;
            long requiredSpace = checked(length + lease.Image.Device.Length * 2);
            if (storageSpace.AvailableBytes(directory) < requiredSpace) { throw new IOException("Insufficient destination space for the image copy and bounded writer scratch space."); }
            EditingState state = EnsureEditing(lease, cancellationToken);
            StagedWriteOperation[] operations = state.Operations.Take(state.Position).ToArray();
            writer.ValidateCapacity(lease.Image, operations, cancellationToken);
            string temporary = Path.Combine(directory, ".sectorworks-save-" + Guid.NewGuid().ToString("N") + ".img");
            bool created = false;
            try
            {
                string[] changedVolumes = operations.Select(operation => operation.Request.VolumeId)
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (changedVolumes.Length == 0)
                {
                    changedVolumes = lease.Image.Volumes.Where(volume => writer.QualifyVolume(lease.Image, volume.Id).Available)
                        .Select(volume => volume.Id).ToArray();
                }
                var expected = changedVolumes.ToDictionary(volumeId => volumeId,
                    volumeId => Fingerprint(GetVolume(lease, volumeId).FileSystem, cancellationToken), StringComparer.Ordinal);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
                {
                    created = true;
                    using var source = new FileStream(lease.Image.Metadata.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    byte[] buffer = new byte[65536];
                    using var sourceCopyHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long offset = 0;
                    while (offset < length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int read = source.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)));
                        if (read == 0) { throw new EndOfStreamException("Source image ended during Save As."); }
                        output.Write(buffer, 0, read);
                        sourceCopyHash.AppendData(buffer, 0, read);
                        offset = checked(offset + read);
                        progress?.Report(new OperationProgress("Copying image", target, offset, 0));
                    }
                    output.Flush(true);
                    if (Convert.ToHexString(sourceCopyHash.GetHashAndReset()) != lease.SourceHash)
                    {
                        throw new IOException("Source changed during Save As copying; output rejected. Reopen the source image.");
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress("Applying staged changes", target, length, 0));
                writer.Apply(temporary, operations, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                using (var output = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { output.Flush(true); }
                progress?.Report(new OperationProgress("Validating tree and SHA-256", target, length, 0));
                using (IImageSession reopened = opener.OpenReadOnly(temporary, cancellationToken))
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
                        throw new InvalidDataException("Output container, partitions or filesystem identities differ from the source.");
                    }
                    foreach (string volumeId in changedVolumes)
                    {
                        Capability outputCapability = writer.QualifyVolume(reopened, volumeId);
                        if (!outputCapability.Available)
                        {
                            throw new InvalidDataException("Output structural validation failed: " + outputCapability.Reason);
                        }
                        var changedPaths = state.Current.Volumes[volumeId].Values.Where(node => node.Entry.IsChanged)
                            .Select(node => node.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var actual = Fingerprint(reopened.Volumes.Single(volume => volume.Id == volumeId).FileSystem, cancellationToken, changedPaths);
                        Dictionary<string, string> expectedTree = expected[volumeId];
                        if (expectedTree.Count != actual.Count || expectedTree.Any(pair => !actual.TryGetValue(pair.Key, out string? value) || value != pair.Value))
                        {
                            throw new InvalidDataException("Output validation failed: directory tree, file lengths or SHA-256 content hashes differ.");
                        }
                    }
                    try { writer.ValidateOutput(lease.Image, reopened, operations, cancellationToken); }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
                    {
                        throw new InvalidDataException("Output metadata/preservation validation failed: " + error.Message, error);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress("Rechecking source baseline", target, length, 0));
                VerifySource(lease, cancellationToken);
                File.Move(temporary, target, false);
                created = false;
                int files = changedVolumes.Sum(volumeId => state.Current.Volumes[volumeId].Values.Count(node => !node.Entry.IsDirectory));
                long outputLength = new FileInfo(target).Length;
                log.Write("SaveAsValidated", target, $"Validated {files} files; source untouched.");
                return new SaveImageResult(target, outputLength, files, "Reopened image; container/partition identities and qualified volume allocation, metadata, complete directory trees, lengths and SHA-256 hashes verified. Source baseline copied and rechecked; source and staged changes retained.");
            }
            catch (Exception error)
            {
                if (created)
                {
                    try { File.Delete(temporary); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                    {
                        string message = "Save As failed. Incomplete output could not be removed; recovery file: " + temporary;
                        log.Write("ResidualSaveOutput", temporary, message, "Warning");
                        throw new IOException(message, new AggregateException(error, cleanup));
                    }
                }
                throw;
            }
        }, cancellationToken);

    private static WorkspaceCapabilities WithEditing(WorkspaceCapabilities capabilities, Capability editing) =>
        capabilities with
        {
            Modify = editing,
            AddFiles = editing,
            ReplaceFiles = editing,
            DeleteFiles = editing,
            RenameFiles = editing,
            CreateDirectories = editing,
            SaveAs = editing,
            CommitInPlace = CommitCapability(editing)
        };

    // Phase 3: direct commit reuses the same validated staged-editing/Apply/ValidateOutput
    // pipeline already qualified for Save As (Phase 2B), wrapped with locking, backup and a
    // durable transaction journal. A row that is not Save-As qualified (Modify.Available is
    // false, including Phase 2C read-only legacy/attribute-list NTFS variants) can never be
    // direct-commit qualified either.
    private static Capability CommitCapability(Capability editing) => editing.Available
        ? new(true, "Qualified staged editing with locked, journaled, backed-up direct commit: " +
            "source identity recheck, retained backup until validated reopen, atomic finalize, and interrupted-transaction recovery.")
        : new(false, editing.Reason);

    private static void VerifySource(SessionLease lease, CancellationToken token)
    {
        if (lease.SourceHash is null || HashSourceFile(lease.Image.Metadata.Path, lease.Image.Metadata.ContainerLength, token) != lease.SourceHash)
        {
            throw new IOException("Source changed since opening; Save As was aborted. Reopen the source image.");
        }
    }

    private static string HashSourceFile(string path, long expectedLength, CancellationToken token)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length != expectedLength) { throw new IOException("Source length changed since opening."); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long count = 0;
        int read;
        while ((read = source.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            count = checked(count + read);
            if (count > expectedLength) { throw new IOException("Source grew while validating its baseline."); }
            hash.AppendData(buffer, 0, read);
        }
        if (count != expectedLength || source.Length != expectedLength) { throw new IOException("Source length changed during validation."); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static EditingSnapshot Snapshot(SessionLease lease)
    {
        EditingState? state = lease.Editing;
        PendingImageChange[] changes = state?.Operations.Take(state.Position).Select((operation, index) =>
        {
            using Stream? content = operation.OpenContent?.Invoke();
            return new PendingImageChange(index + 1, operation.Request.Kind, operation.Request.VolumeId,
                operation.Request.Path, operation.Request.NewName, content?.Length ?? 0);
        }).ToArray() ?? [];
        return new EditingSnapshot(changes, state?.Position > 0, state is not null && state.Position < state.Operations.Count, lease.Image.Metadata.ContainerLength);
    }

    private EditingState EnsureEditing(SessionLease lease, CancellationToken token)
    {
        if (lease.Editing is not null) { return lease.Editing; }
        var volumes = new Dictionary<string, Dictionary<string, StagedNode>>(StringComparer.Ordinal);
        int count = 0;
        foreach (ImageVolume volume in lease.Image.Volumes)
        {
            if (!writer.QualifyVolume(lease.Image, volume.Id).Available) { continue; }
            var nodes = new Dictionary<string, StagedNode>(StringComparer.OrdinalIgnoreCase);
            foreach (FileEntry entry in Walk(volume.FileSystem, "/", token))
            {
                if (++count > MaximumEntries) { throw new InvalidDataException("Editable volumes exceed the workspace entry budget."); }
                if (entry.IsLink) { throw new NotSupportedException("Linked entries are not qualified for editing."); }
                nodes.Add(entry.Path, new StagedNode(entry, entry.Path, null));
            }
            volumes.Add(volume.Id, nodes);
        }
        var state = new EditingState(lease.StagingPath,
            message => log.Write("ResidualStagingData", lease.StagingPath, message, "Warning"));
        state.States.Add(new EditingTree(volumes));
        lease.Editing = state;
        return state;
    }

    private static Dictionary<string, string> Fingerprint(IReadOnlyFileSystem fs, CancellationToken token, ISet<string>? requireModifiedDates = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FileEntry entry in Walk(fs, "/", token))
        {
            if (requireModifiedDates?.Contains(entry.Path) == true && entry.ModifiedUtc is null)
            {
                throw new InvalidDataException("Changed output entry has an invalid filesystem modification date: " + entry.Path);
            }
            if (entry.IsLink) { throw new InvalidDataException("Linked entries cannot be output-validated."); }
            if (entry.IsDirectory) { result.Add(entry.Path, "Directory"); continue; }
            using Stream input = fs.OpenRead(entry.Path);
            ValidateFileLength(input, entry);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536];
            long total = 0;
            int read;
            while ((read = input.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested();
                total = checked(total + read);
                if (total > entry.Length) { throw new InvalidDataException("File exceeded its declared length."); }
                hash.AppendData(buffer, 0, read);
            }
            if (total != entry.Length) { throw new EndOfStreamException("File is shorter than its declared length."); }
            result.Add(entry.Path, $"{entry.Length}:{Convert.ToHexString(hash.GetHashAndReset())}");
        }
        return result;
    }

    private static bool IsWithin(string candidate, string root) =>
        candidate.Equals(root, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static void ValidateEditPath(string path)
    {
        ValidateImagePath(path);
        if (path == "/" || path.Length > 240) { throw new InvalidDataException("Image edit paths must be non-root and at most 240 characters."); }
        foreach (string name in path.Split('/').Skip(1))
        {
            if (!ImageEditNames.IsSupportedName(name))
            {
                throw new InvalidDataException("Image editing requires valid NFC BMP names without control/surrogate characters, reserved names, forbidden punctuation or leading/trailing spaces and trailing dots.");
            }
        }
    }
}
