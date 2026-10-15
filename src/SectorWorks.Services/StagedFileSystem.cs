using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.Services;

internal sealed class StagedFileSystem(IReadOnlyFileSystem source, Dictionary<string, StagedNode> nodes) : IReadOnlyFileSystem
{
    public string Name => source.Name;
    public string VolumeLabel => source.VolumeLabel;
    public bool IsCaseSensitive => source.IsCaseSensitive;
    public WorkspaceCapabilities Capabilities => source.Capabilities;
    public IReadOnlyDictionary<string, string> Properties => source.Properties;
    public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (path != "/" && (!nodes.TryGetValue(path, out StagedNode? node) || !node.Entry.IsDirectory))
        {
            throw new DirectoryNotFoundException(path);
        }
        return nodes.Values.Where(n => Parent(n.Entry.Path).Equals(path, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Entry).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public Stream OpenRead(string path)
    {
        if (!nodes.TryGetValue(path, out StagedNode? node) || node.Entry.IsDirectory)
        {
            throw new FileNotFoundException("Staged file not found.", path);
        }
        return node.Content?.OpenRead() ?? source.OpenRead(node.OriginalPath!);
    }
    public void Dispose() { }
    internal static string Parent(string path) => path.LastIndexOf('/') is int slash && slash > 0 ? path[..slash] : "/";
}

internal sealed record StagedNode(FileEntry Entry, string? OriginalPath, StagedContent? Content);

internal sealed class EditingTree(Dictionary<string, Dictionary<string, StagedNode>> volumes)
{
    public Dictionary<string, Dictionary<string, StagedNode>> Volumes { get; } = volumes;
    public int Count => Volumes.Values.Sum(nodes => nodes.Count);
    public IEnumerable<StagedNode> Values => Volumes.Values.SelectMany(nodes => nodes.Values);
}

internal sealed class StagedContent : IDisposable
{
    private readonly FileStream owner;
    public long Length => owner.Length;
    public string Path => owner.Name;

    public StagedContent(Stream source, string stagingDirectory, long expectedLength, long maximumLength, CancellationToken token)
    {
        string path = System.IO.Path.Combine(stagingDirectory, "content-" + Guid.NewGuid().ToString("N"));
        owner = WorkspaceDataFiles.CreatePayload(path);
        try
        {
            if (expectedLength < 0 || expectedLength > maximumLength || source.Length != expectedLength)
            {
                throw new IOException("Imported file length changed since preflight or exceeds the staging limit.");
            }
            byte[] buffer = new byte[65536];
            while (owner.Length < expectedLength)
            {
                token.ThrowIfCancellationRequested();
                int read = source.Read(buffer, 0, checked((int)Math.Min(buffer.Length, expectedLength - owner.Length)));
                if (read == 0) { throw new EndOfStreamException("Imported file ended before its preflight length; import discarded."); }
                if (checked(owner.Length + read) > maximumLength || owner.Length + read > expectedLength)
                {
                    throw new IOException("Imported content exceeded its preflight length or staging limit.");
                }
                owner.Write(buffer, 0, read);
            }
            token.ThrowIfCancellationRequested();
            if (source.Length != expectedLength || source.ReadByte() != -1 || source.Length != expectedLength)
            {
                throw new IOException("Imported file length changed while staging; import discarded.");
            }
            owner.Flush();
            File.SetAttributes(owner.Name, File.GetAttributes(owner.Name) | FileAttributes.ReadOnly);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Stream OpenRead() => new FileStream(owner.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    public void Dispose()
    {
        try
        {
            if (File.Exists(owner.Name))
            {
                File.SetAttributes(owner.Name, File.GetAttributes(owner.Name) & ~FileAttributes.ReadOnly);
            }
        }
        finally { owner.Dispose(); }
    }
}

internal sealed class EditingState(string directory, Action<string> reportResidual) : IDisposable
{
    public string DirectoryPath { get; } = directory;
    public List<StagedWriteOperation> Operations { get; } = [];
    public List<StagedContent?> OperationContents { get; } = [];
    public List<EditingTree> States { get; } = [];
    public List<StagedContent> Contents { get; } = [];
    public int Position { get; set; }
    public EditingTree Current => States[Position];
    public long RetainedBytes => Contents.Sum(content => content.Length);
    public bool CleanupBlocked { get; private set; }
    public void VerifySpool()
    {
        var known = Contents.Select(content => content.Path).ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(DirectoryPath) && Directory.EnumerateFileSystemEntries(DirectoryPath).Any(path => !known.Contains(path)))
        {
            CleanupBlocked = true;
        }
        if (CleanupBlocked)
        {
            throw new IOException("Staging cleanup or ownership validation failed; close/discard this workspace and review retained data at " + DirectoryPath);
        }
    }
    public void ReleaseUnusedContents()
    {
        var retained = States.SelectMany(state => state.Values).Select(node => node.Content).ToHashSet();
        retained.UnionWith(OperationContents);
        foreach (StagedContent content in Contents.Where(content => !retained.Contains(content)).ToArray())
        {
            try { content.Dispose(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                CleanupBlocked = true;
                reportResidual("Obsolete staged payload could not be removed; retained workspace data: " + DirectoryPath + ". " + error.Message);
            }
            Contents.Remove(content);
        }
    }
    public void Dispose()
    {
        foreach (StagedContent content in Contents)
        {
            try { content.Dispose(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                reportResidual("Staged payload cleanup failed; retained workspace data: " + DirectoryPath + ". " + error.Message);
            }
        }
        Contents.Clear();
        try
        {
            if (Directory.Exists(DirectoryPath)) { Directory.Delete(DirectoryPath, false); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            reportResidual("Staging directory could not be removed; retained workspace data: " + DirectoryPath + ". " + error.Message);
        }
    }
}
