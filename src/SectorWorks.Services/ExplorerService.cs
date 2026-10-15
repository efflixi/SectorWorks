using System.Collections.Concurrent;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.Services;

public sealed partial class ExplorerService : IExplorerService, IImageEditingService
{
    private const int MaximumEntries = 100000;
    private const int MaximumResults = 10000;
    private readonly IImageOpener opener;
    private readonly IExplorerLog log;
    private readonly IStagedImageWriter writer;
    private readonly IStorageSpace storageSpace;
    private readonly string stagingRoot;
    private readonly ConcurrentDictionary<Guid, SessionLease> sessions = new();
    private readonly object lifecycle = new();
    private bool disposed;

    public ExplorerService(string? stagingDirectory = null) : this(new SectorWorks.Storage.ImageOpener(), stagingDirectory: stagingDirectory) { }

    public ExplorerService(IImageOpener opener, IExplorerLog? log = null, IStagedImageWriter? writer = null,
        string? stagingDirectory = null, IStorageSpace? storageSpace = null)
    {
        this.opener = opener;
        this.log = log ?? new JsonExplorerLog();
        this.writer = writer ?? new SectorWorks.Storage.DiskImageWriter();
        this.storageSpace = storageSpace ?? new HostStorageSpace();
        string userData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        stagingRoot = stagingDirectory is not null ? Path.GetFullPath(stagingDirectory)
            : string.IsNullOrWhiteSpace(userData) ? throw new InvalidOperationException("No managed user-data directory is available for staged image content.")
            : Path.Combine(userData, "SectorWorks", "WorkspaceData");
    }

    public Task<WorkspaceInfo> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (lifecycle)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
            }
            cancellationToken.ThrowIfCancellationRequested();
            log.Write("Opening", path, "Opening source read-only.");
            IImageSession? image = null;
            try
            {
                image = opener.OpenReadOnly(path, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Guid id = Guid.NewGuid();
                bool browse = image.Volumes.Any(v => v.FileSystem.Capabilities.Browse.Available);
                bool extract = image.Volumes.Any(v => v.FileSystem.Capabilities.Extract.Available && v.FileSystem.Capabilities.Browse.Available);
                bool preview = image.Volumes.Any(v => v.FileSystem.Capabilities.Preview.Available && v.FileSystem.Capabilities.Browse.Available);
                var capabilities = new WorkspaceCapabilities(
                    new Capability(browse, browse ? "Supported read-only filesystem." : "No supported filesystem was found."),
                    new Capability(extract, extract ? "Streaming extraction; existing host files are never overwritten." : "No browsable filesystem permits ordinary file reads."),
                    new Capability(preview, preview ? "Bounded text/file hex preview." : "Only raw image hex inspection is available."),
                    new Capability(false, "Full filesystem validation is not implemented; opening performs bounded structural checks only."));
                Capability editing = writer.Qualify(image);
                string? sourceHash = editing.Available ? HashSourceFile(image.Metadata.Path, image.Metadata.ContainerLength, cancellationToken) : null;
                if (sourceHash is not null && HashSourceFile(image.Metadata.Path, image.Metadata.ContainerLength, cancellationToken) != sourceHash)
                {
                    throw new IOException("Source changed while establishing the Save As baseline; reopen the image.");
                }
                capabilities = WithEditing(capabilities, editing);
                var info = new WorkspaceInfo(id, editing.Available ? WorkspaceState.ReadyWritable : WorkspaceState.ReadyReadOnly, image.Metadata, image.Partitions,
                    image.Volumes.Select(v => new VolumeInfo(v.Id, v.FileSystem.Name, v.FileSystem.VolumeLabel, v.Partition, v.FileSystem.Properties,
                        WithEditing(v.FileSystem.Capabilities, writer.QualifyVolume(image, v.Id)))).ToArray(),
                    image.Warnings, capabilities);
                foreach (string warning in image.Warnings)
                {
                    log.Write("ValidationWarning", path, warning, "Warning");
                }
                log.Write("Capabilities", path, $"Source read-only; browse={browse}; extract={extract}; preview={preview}; stagedSaveAs={editing.Available}; direct writes and playback disabled.");
                log.Write(info.State.ToString(), path, info.Metadata.Format);
                lock (lifecycle)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    sessions[id] = new SessionLease(image, sourceHash, Path.Combine(stagingRoot, id.ToString("N")));
                    image = null;
                }
                return info;
            }
            catch (Exception error) when (IsOperationFailure(error))
            {
                log.Write("Failed", path, error.Message, "Error");
                throw;
            }
            finally
            {
                image?.Dispose();
            }
        }, cancellationToken);

    public async Task CloseAsync(Guid workspaceId)
    {
        if (!sessions.TryGetValue(workspaceId, out SessionLease? lease))
        {
            throw new InvalidOperationException("Workspace is not open.");
        }
        await lease.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureOpen(lease);
            string path = lease.Image.Metadata.Path;
            log.Write("Closed", path, "Closing read-only workspace.");
            lease.Closed = true;
            lease.Editing?.Dispose();
            lease.Image.Dispose();
            sessions.TryRemove(workspaceId, out _);
        }
        finally
        {
            lease.Gate.Release();
        }
    }

    public Task<IReadOnlyList<FileEntry>> ReadDirectoryAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "ReadDirectory", lease =>
            ReadDirectory(GetVolume(lease, volumeId).FileSystem, path, cancellationToken), cancellationToken);

    public Task<PreviewResult> PreviewAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "Preview", lease =>
        {
            IReadOnlyFileSystem fs = GetVolume(lease, volumeId).FileSystem;
            RequireCapability(fs.Capabilities.Preview);
            FileEntry entry = ResolveFile(fs, path, cancellationToken);
            using Stream input = fs.OpenRead(entry.Path);
            ValidateFileLength(input, entry);
            cancellationToken.ThrowIfCancellationRequested();
            return new TextPreviewHandler().Preview(input, entry.Name, 65536);
        }, cancellationToken);

    public Task<PreviewResult> ReadHexAsync(Guid workspaceId, string? volumeId, string? path, long offset, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "Hex", lease =>
        {
            byte[] bytes = new byte[4096];
            int count;
            long length;
            if (volumeId is not null && path is not null)
            {
                IReadOnlyFileSystem fs = GetVolume(lease, volumeId).FileSystem;
                RequireCapability(fs.Capabilities.Preview);
                FileEntry entry = ResolveFile(fs, path, cancellationToken);
                using Stream input = fs.OpenRead(entry.Path);
                ValidateFileLength(input, entry);
                length = input.Length;
                ValidateHexOffset(offset, length);
                if (!input.CanSeek)
                {
                    throw new NotSupportedException("This file stream does not support paginated hex access.");
                }
                input.Position = offset;
                count = ReadPage(input, bytes, checked((int)Math.Min(bytes.Length, length - offset)), cancellationToken);
            }
            else
            {
                if (volumeId is not null || path is not null)
                {
                    throw new ArgumentException("File hex requires both volume and file path.");
                }
                length = lease.Image.Device.Length;
                ValidateHexOffset(offset, length);
                count = ReadDevicePage(lease.Image.Device, offset, bytes,
                    checked((int)Math.Min(bytes.Length, length - offset)), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new PreviewResult(FormatHex(bytes.AsSpan(0, count), offset), offset + count < length,
                $"Read-only hex: {offset:N0} / {length:N0} bytes; page size 4 KiB.");
        }, cancellationToken);

    public Task<IReadOnlyList<SearchHit>> SearchAsync(Guid workspaceId, string query, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<SearchHit>>(workspaceId, "Search", lease =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length > 256)
            {
                throw new ArgumentException("Search requires 1-256 non-whitespace characters.", nameof(query));
            }
            var results = new List<SearchHit>();
            int count = 0;
            ImageVolume[] volumes = lease.Image.Volumes.Select(v => GetVolume(lease, v.Id)).Where(v => v.FileSystem.Capabilities.Browse.Available).ToArray();
            if (volumes.Length == 0)
            {
                throw new NotSupportedException("This workspace has no browsable filesystem.");
            }
            foreach (ImageVolume volume in volumes)
            {
                foreach (FileEntry entry in Walk(volume.FileSystem, "/", cancellationToken))
                {
                    if (++count > MaximumEntries)
                    {
                        throw new InvalidDataException("Search exceeds the 100000-entry resource limit; narrow the image content.");
                    }
                    if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        if (results.Count == MaximumResults)
                        {
                            throw new InvalidDataException("Search exceeds the 10000-result limit; use a more specific query.");
                        }
                        results.Add(new SearchHit(volume.Id, entry));
                    }
                }
            }
            return results;
        }, cancellationToken);

    public Task<ExtractionResult> ExtractAsync(Guid workspaceId, string volumeId, IReadOnlyList<FileEntry> entries,
        string destination, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "Extract", lease =>
        {
            if (entries.Count is 0 or > MaximumEntries)
            {
                throw new ArgumentException("Select between 1 and 100000 files/directories to extract.", nameof(entries));
            }
            IReadOnlyFileSystem fs = GetVolume(lease, volumeId).FileSystem;
            RequireCapability(fs.Capabilities.Extract);
            using var target = new SafeExtractionTarget(destination);
            int files = 0;
            long bytes = 0;
            int examined = 0;
            var failures = new List<ExtractionFailure>();
            var selectedPaths = new HashSet<string>(fs.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (FileEntry selected in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FileEntry actual = ResolveEntry(fs, selected.Path, cancellationToken);
                    if (!selectedPaths.Add(actual.Path))
                    {
                        continue;
                    }
                    string outputRoot = actual.Name;
                    ExtractOne(actual, outputRoot);
                    if (actual.IsDirectory && !actual.IsLink)
                    {
                        foreach (FileEntry child in Walk(fs, actual.Path, cancellationToken))
                        {
                            ExtractOne(child, outputRoot + child.Path[actual.Path.Length..]);
                        }
                    }
                }
                catch (Exception error) when (IsExtractionFailure(error))
                {
                    failures.Add(new ExtractionFailure(selected.Path, error.Message));
                    log.Write("ExtractionFailure", selected.Path, error.Message, "Error");
                }
            }
            log.Write("ExtractionFinished", destination, $"Files={files}; bytes={bytes}; failures={failures.Count}.");
            return new ExtractionResult(files, bytes, failures);

            void ExtractOne(FileEntry entry, string relative)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++examined > MaximumEntries)
                {
                    throw new InvalidDataException("Extraction exceeds the 100000-entry resource limit.");
                }
                try
                {
                    if (entry.IsLink)
                    {
                        throw new NotSupportedException("Image symbolic links/reparse points are not extracted or followed.");
                    }
                    SafeExtractionTarget.ValidateRelativePath(relative);
                    if (entry.IsDirectory)
                    {
                        target.CreateDirectory(relative);
                        return;
                    }
                    using Stream input = fs.OpenRead(entry.Path);
                    ValidateFileLength(input, entry);
                    using ExtractionFile output = target.CreateFile(relative);
                    byte[] buffer = new byte[65536];
                    long copied = 0;
                    while (copied < entry.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int read = input.Read(buffer, 0, checked((int)Math.Min(buffer.Length, entry.Length - copied)));
                        if (read == 0)
                        {
                            throw new EndOfStreamException("File data ended before its advertised length.");
                        }
                        output.Stream.Write(buffer, 0, read);
                        copied = checked(copied + read);
                        progress?.Report(new OperationProgress("Extracting", entry.Path, checked(bytes + copied), files));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Complete();
                    files++;
                    bytes = checked(bytes + copied);
                    progress?.Report(new OperationProgress("Extracted", entry.Path, bytes, files));
                }
                catch (Exception error) when (IsExtractionFailure(error))
                {
                    failures.Add(new ExtractionFailure(entry.Path, error.Message));
                    log.Write("ExtractionFailure", entry.Path, error.Message, "Error");
                }
            }
        }, cancellationToken);

    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
        }
        foreach ((Guid id, SessionLease lease) in sessions)
        {
            lease.Gate.Wait();
            try
            {
                if (!lease.Closed)
                {
                    lease.Closed = true;
                    lease.Editing?.Dispose();
                    lease.Image.Dispose();
                }
                sessions.TryRemove(id, out _);
            }
            finally
            {
                lease.Gate.Release();
            }
        }
    }

    private async Task<T> RunAsync<T>(Guid id, string stage, Func<SessionLease, T> operation, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!sessions.TryGetValue(id, out SessionLease? lease))
        {
            throw new InvalidOperationException("Workspace is not open.");
        }
        await lease.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            EnsureOpen(lease);
            return await Task.Run(() => operation(lease), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            log.Write(stage, lease.Image.Metadata.Path, "Operation cancelled.", "Information");
            throw;
        }
        catch (Exception error) when (IsOperationFailure(error))
        {
            log.Write(stage, lease.Image.Metadata.Path, error.Message, "Error");
            throw;
        }
        finally
        {
            lease.Gate.Release();
        }
    }

    private static void EnsureOpen(SessionLease lease)
    {
        if (lease.Closed)
        {
            throw new InvalidOperationException("Workspace is closed.");
        }
    }

    private static ImageVolume GetVolume(SessionLease lease, string id)
    {
        ImageVolume volume = lease.Image.Volumes.SingleOrDefault(v => v.Id == id)
            ?? throw new ArgumentException("The volume does not belong to this workspace.", nameof(id));
        return lease.Editing is null || !lease.Editing.Current.Volumes.TryGetValue(id, out Dictionary<string, StagedNode>? nodes) ? volume : volume with
        {
            FileSystem = new StagedFileSystem(volume.FileSystem, nodes)
        };
    }

    private static IReadOnlyList<FileEntry> ReadDirectory(IReadOnlyFileSystem fs, string path, CancellationToken token)
    {
        RequireCapability(fs.Capabilities.Browse);
        ValidateImagePath(path);
        IReadOnlyList<FileEntry> entries = fs.ReadDirectory(path, token);
        if (entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("Directory exceeds the 100000-entry resource limit.");
        }
        StringComparer comparer = fs.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(comparer);
        foreach (FileEntry entry in entries)
        {
            token.ThrowIfCancellationRequested();
            ValidateImagePath(entry.Path);
            string expected = (path == "/" ? string.Empty : path) + "/" + entry.Name;
            if (!comparer.Equals(entry.Path, expected) || entry.Name != entry.Path[(entry.Path.LastIndexOf('/') + 1)..]
                || entry.Name.Length == 0 || entry.Name.Contains('/') || entry.Name.Contains('\\')
                || entry.Name is "." or ".." || entry.Length < 0 || !seen.Add(entry.Path))
            {
                throw new InvalidDataException("Invalid, duplicate, or non-child directory entry: " + entry.Path);
            }
        }
        return entries;
    }

    private static void RequireCapability(Capability capability)
    {
        if (!capability.Available)
        {
            throw new NotSupportedException(capability.Reason);
        }
    }

    private static void ValidateImagePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!path.StartsWith('/') || path.Contains('\\') || path.Contains('\0')
            || (path != "/" && path.EndsWith('/')) || path.Length > 32768
            || path.Split('/').Skip(1).Any(p => p is "." or ".." || (path != "/" && p.Length == 0)))
        {
            throw new InvalidDataException("Invalid normalized image path: " + path);
        }
    }

    private static FileEntry ResolveEntry(IReadOnlyFileSystem fs, string path, CancellationToken token)
    {
        ValidateImagePath(path);
        if (path == "/")
        {
            throw new ArgumentException("Select the root directory's contents rather than the volume root itself.", nameof(path));
        }
        int slash = path.LastIndexOf('/');
        StringComparer comparer = fs.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        return ReadDirectory(fs, slash == 0 ? "/" : path[..slash], token).SingleOrDefault(e => comparer.Equals(e.Path, path))
            ?? throw new FileNotFoundException("The selected image entry was not found.", path);
    }

    private static FileEntry ResolveFile(IReadOnlyFileSystem fs, string path, CancellationToken token)
    {
        FileEntry entry = ResolveEntry(fs, path, token);
        if (entry.IsDirectory || entry.IsLink)
        {
            throw new NotSupportedException("Only ordinary files can be previewed.");
        }
        return entry;
    }

    private static IEnumerable<FileEntry> Walk(IReadOnlyFileSystem fs, string start, CancellationToken token)
    {
        var pending = new Stack<(string Path, int Depth)>();
        var visited = new HashSet<string>(fs.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        pending.Push((start, 0));
        int count = 0;
        while (pending.TryPop(out (string Path, int Depth) item))
        {
            token.ThrowIfCancellationRequested();
            if (item.Depth > 128 || !visited.Add(item.Path))
            {
                throw new InvalidDataException("Cyclic or excessively nested image directories.");
            }
            foreach (FileEntry entry in ReadDirectory(fs, item.Path, token))
            {
                if (++count > MaximumEntries)
                {
                    throw new InvalidDataException("Traversal exceeds the 100000-entry resource limit.");
                }
                yield return entry;
                if (entry.IsDirectory && !entry.IsLink)
                {
                    pending.Push((entry.Path, item.Depth + 1));
                }
            }
        }
    }

    private static void ValidateHexOffset(long offset, long length)
    {
        if (offset < 0 || offset > length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Hex offset is outside the selected content.");
        }
    }

    private static void ValidateFileLength(Stream input, FileEntry entry)
    {
        if (entry.Length < 0 || input.Length != entry.Length)
        {
            throw new InvalidDataException("File metadata length does not match its readable stream.");
        }
    }

    private static int ReadPage(Stream input, byte[] buffer, int count, CancellationToken token)
    {
        int total = 0;
        while (total < count)
        {
            token.ThrowIfCancellationRequested();
            int read = input.Read(buffer, total, count - total);
            if (read == 0)
            {
                throw new EndOfStreamException("File data ended before its advertised hex page length.");
            }
            total += read;
        }
        return total;
    }

    private static int ReadDevicePage(IReadOnlyBlockDevice device, long offset, byte[] buffer, int count, CancellationToken token)
    {
        int total = 0;
        while (total < count)
        {
            token.ThrowIfCancellationRequested();
            int read = device.Read(checked(offset + total), buffer.AsSpan(total, count - total));
            if (read == 0)
            {
                throw new EndOfStreamException("Image data ended before its advertised hex page length.");
            }
            total += read;
        }
        return total;
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes, long offset)
    {
        var text = new StringBuilder();
        for (int i = 0; i < bytes.Length; i += 16)
        {
            text.Append($"{offset + i:X12}  ");
            int count = Math.Min(16, bytes.Length - i);
            for (int j = 0; j < 16; j++)
            {
                text.Append(j < count ? $"{bytes[i + j]:X2} " : "   ");
            }
            text.Append(" ");
            for (int j = 0; j < count; j++)
            {
                byte value = bytes[i + j];
                text.Append(value is >= 32 and < 127 ? (char)value : '.');
            }
            text.AppendLine();
        }
        return text.ToString();
    }

    private static bool IsExtractionFailure(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException or OverflowException;

    private static bool IsOperationFailure(Exception error) =>
        IsExtractionFailure(error) || error is InvalidOperationException;

    private sealed class SessionLease(IImageSession image, string? sourceHash, string stagingPath)
    {
        public IImageSession Image { get; } = image;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Closed { get; set; }
        public EditingState? Editing { get; set; }
        public string? SourceHash { get; } = sourceHash;
        public string StagingPath { get; } = stagingPath;
    }
}
