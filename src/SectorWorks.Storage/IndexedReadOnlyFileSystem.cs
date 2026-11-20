using System.Text;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal readonly record struct IndexedExtent(long Logical, long Physical, long Length);
internal sealed record IndexedNode(string Name, ulong Id, bool Directory, long Size, DateTime? Modified,
    bool Special, string? DataRestriction, IndexedExtent[] Extents);

internal sealed class IndexedReadOnlyFileSystem : IReadOnlyFileSystem
{
    private readonly IReadOnlyBlockDevice device;
    private readonly ulong root;
    private readonly Dictionary<ulong, Dictionary<string, IndexedNode>> directories = [];
    private readonly ParserContext context = new();
    private bool disposed;

    internal IndexedReadOnlyFileSystem(IReadOnlyBlockDevice device, string name, string label, bool caseSensitive, ulong root,
        Dictionary<ulong, IReadOnlyList<IndexedNode>> nodes, Dictionary<string, string> properties)
    {
        this.device = device;
        this.root = root;
        Name = name;
        VolumeLabel = label;
        IsCaseSensitive = caseSensitive;
        Properties = properties.AsReadOnly();
        var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        foreach (var pair in nodes)
        {
            var children = new Dictionary<string, IndexedNode>(comparer);
            foreach (IndexedNode child in pair.Value)
            {
                if (!children.TryAdd(NormalizeName(child.Name), child))
                { throw new ImageFormatException($"Ambiguous {name} filename: {child.Name}"); }
            }
            directories.Add(pair.Key, children);
        }
    }

    public string Name { get; }
    public string VolumeLabel { get; }
    public bool IsCaseSensitive { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default) =>
        context.Run<IReadOnlyList<FileEntry>>(() =>
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            string normalized = ImagePath.Normalize(path);
            IndexedNode? node = Find(normalized);
            if (node is { Directory: false }) { throw new DirectoryNotFoundException(normalized); }
            var children = directories[node?.Id ?? root];
            var result = new List<FileEntry>(children.Count);
            foreach (IndexedNode child in children.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = normalized == "/" ? "/" + child.Name : normalized + "/" + child.Name;
                result.Add(new FileEntry(fullPath, child.Name, child.Directory, child.Size, child.Modified,
                    child.DataRestriction ?? (child.Directory ? "Directory" : child.Special ? "Special file" : ""), IsLink: child.Special));
            }
            return result.AsReadOnly();
        }, cancellationToken);

    public Stream OpenRead(string path) => context.Run<Stream>(() =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IndexedNode? node = Find(ImagePath.Normalize(path));
        if (node is null || node.Directory) { throw new IOException("Select a regular file to read its contents."); }
        if (node.Special) { throw new NotSupportedException("Links and special files are not followed."); }
        if (node.DataRestriction is { } error) { throw new NotSupportedException(error); }
        return new DeviceStream(new DataFork(device, node), ownsDevice: true);
    });

    private IndexedNode? Find(string path)
    {
        if (path == "/") { return null; }
        ulong parent = root;
        IndexedNode? result = null;
        foreach (string part in path[1..].Split('/'))
        {
            if (!directories.TryGetValue(parent, out var children) || !children.TryGetValue(NormalizeName(part), out result))
            { throw new FileNotFoundException("No such image entry.", path); }
            parent = result.Directory ? result.Id : 0;
        }
        return result;
    }

    private static string NormalizeName(string name) => name.Normalize(NormalizationForm.FormD);

    public void Dispose() { lock (context.Gate) { disposed = true; } }

    private sealed class DataFork(IReadOnlyBlockDevice parent, IndexedNode file) : IReadOnlyBlockDevice
    {
        public long Length => file.Size;
        public bool IsReadOnly => true;
        public int LogicalSectorSize => parent.LogicalSectorSize;
        public int? PhysicalSectorSize => parent.PhysicalSectorSize;

        public int Read(long offset, Span<byte> buffer)
        {
            Bounds.Check(offset, buffer.Length, Length);
            if (buffer.IsEmpty) { return 0; }
            int low = 0, high = file.Extents.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                IndexedExtent extent = file.Extents[middle];
                if (offset < extent.Logical) { high = middle - 1; }
                else if (offset - extent.Logical >= extent.Length) { low = middle + 1; }
                else
                {
                    long within = offset - extent.Logical;
                    int count = (int)Math.Min(buffer.Length, extent.Length - within);
                    if (extent.Physical < 0) { buffer[..count].Clear(); }
                    else { Bounds.ReadExactly(parent, extent.Physical + within, buffer[..count]); }
                    return count;
                }
            }
            throw new ImageFormatException("File bytes are outside the validated data fork.");
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, buffer.Span));
        }
        public void Dispose() { }
    }
}
