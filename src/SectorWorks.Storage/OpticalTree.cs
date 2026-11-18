using System.Buffers.Binary;
using System.Security.Cryptography;
using SectorWorks.Core;

namespace SectorWorks.Storage;

// The rebuild reader shares one ordinary tree across independently parsed namespaces.
// Extents are never materialized in memory; only bounded metadata is retained.
internal sealed class OpticalNode(string name, bool directory, DateTime? modified, long length = 0)
{
    internal string Name { get; set; } = name;
    internal bool Directory { get; } = directory;
    internal DateTime? Modified { get; set; } = modified;
    internal long Length { get; set; } = length;
    internal long Offset { get; init; }
    internal bool Hidden { get; set; }
    internal int? FixedBootBlock { get; set; }
    internal Func<Stream>? Content { get; set; }
    internal List<OpticalNode> Children { get; } = [];
    internal Dictionary<int, IsoEntry> Iso { get; } = [];
    internal UdfEntry? Udf { get; set; }
    internal void DropIdentifiers()
    {
        foreach (IsoEntry entry in Iso.Values) { entry.Identifier = null; }
    }

    internal OpticalNode Clone()
    {
        var copy = new OpticalNode(Name, Directory, Modified, Length)
        {
            Offset = Offset,
            Hidden = Hidden,
            Content = Content,
            Udf = Udf,
            FixedBootBlock = FixedBootBlock
        };
        foreach ((int view, IsoEntry entry) in Iso) { copy.Iso.Add(view, entry.Clone()); }
        copy.Children.AddRange(Children.Select(child => child.Clone()));
        return copy;
    }
}

internal sealed class OpticalLayout
{
    internal const int SectorSize = 2048;
    internal const int MaximumEntries = 100_000;
    internal const int MaximumDepth = 32;
    internal const long MaximumBytes = 64L * 1024 * 1024 * 1024;
    private int next;
    internal OpticalLayout(int initial) => next = initial;
    internal int End => next;
    internal long Length => Offset(next);
    internal static long Offset(int block) => checked((long)block * SectorSize);
    internal static int Blocks(long bytes) => checked((int)((bytes + SectorSize - 1) / SectorSize));
    internal int Allocate(long bytes)
    {
        int start = next;
        next = checked(next + Blocks(bytes));
        if (Length > MaximumBytes) { throw new NotSupportedException("Optical rebuild exceeds the 64 GiB output bound."); }
        return start;
    }
    internal static void WriteAt(Stream output, int block, ReadOnlySpan<byte> bytes)
    {
        output.Position = Offset(block);
        output.Write(bytes);
    }
}

internal sealed class OpticalFileSystem(IReadOnlyBlockDevice device, OpticalNode root, IsoImage? iso, UdfImage? udf,
    string label, Dictionary<string, string> properties, Capability rebuild) : IReadOnlyFileSystem, IImageRebuildSource
{
    public string Name => iso is not null ? "ISO9660" : "UDF";
    public string VolumeLabel => label;
    public bool IsCaseSensitive => false;
    public IReadOnlyDictionary<string, string> Properties { get; } = properties.AsReadOnly();
    public Capability RebuildCapability => rebuild;
    internal OpticalNode Root => root;
    internal IsoImage? Iso => iso;
    internal UdfImage? Udf => udf;

    public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpticalNode node = Lookup(root, path);
        if (!node.Directory) { throw new DirectoryNotFoundException(path); }
        return node.Children.Select(child =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = (child.Directory ? FileAttributes.Directory : FileAttributes.Normal);
            if (child.Hidden) { attributes = (attributes & ~FileAttributes.Normal) | FileAttributes.Hidden; }
            return new FileEntry((path == "/" ? "" : path) + "/" + child.Name, child.Name, child.Directory,
                child.Directory ? 0 : child.Length, child.Modified, attributes.ToString());
        }).ToArray();
    }

    public Stream OpenRead(string path)
    {
        OpticalNode node = Lookup(root, path);
        if (node.Directory) { throw new FileNotFoundException("Optical path is not an ordinary file.", path); }
        return OpenExtent(device, node.Offset, node.Length);
    }

    internal static Stream OpenExtent(IReadOnlyBlockDevice device, long offset, long length)
    {
        if (length == 0) { return new MemoryStream([], false); }
        return new DeviceStream(new PartitionBlockDevice(device, offset, length));
    }

    internal static OpticalNode Lookup(OpticalNode start, string path)
    {
        string normalized = ImagePath.Normalize(path);
        if (normalized == "/") { return start; }
        foreach (string name in normalized[1..].Split('/'))
        {
            start = start.Children.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException("Optical path not found.", path);
        }
        return start;
    }

    public IImageRebuildPlan PlanRebuild(IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        if (!rebuild.Available) { throw new NotSupportedException(rebuild.Reason); }
        return new OpticalRebuildPlan(this, operations, cancellationToken);
    }

    public void Dispose() { }
}

internal sealed class OpticalRebuildPlan : IImageRebuildPlan
{
    private readonly OpticalFileSystem source;
    private readonly OpticalNode tree;
    private readonly (OpticalNode Node, int Block)[] files;
    private readonly IsoOutput? iso;
    private readonly UdfOutput? udf;
    private readonly int end;
    private readonly Dictionary<OpticalNode, string> writtenHashes = [];

    internal OpticalRebuildPlan(OpticalFileSystem source, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token, long? fixedLength = null)
    {
        this.source = source;
        tree = source.Root.Clone();
        foreach (StagedWriteOperation operation in operations)
        {
            token.ThrowIfCancellationRequested();
            Apply(operation);
        }
        OpticalNode[] nodes = Enumerate(tree, token).ToArray();
        int firstBlock = source.Udf is not null ? 259
            : fixedLength is not null && source.Iso is { Boot: null } fixedIso ? fixedIso.DescriptorEnd
            : Math.Max(32, source.Iso?.Boot?.End ?? 0);
        var layout = new OpticalLayout(firstBlock);
        iso = source.Iso?.Plan(tree, layout);
        udf = source.Udf?.Plan(tree, layout, source.Iso is { } bridge ? bridge.Terminator + 1 : 16);
        files = nodes.Where(n => !n.Directory).Select(n => (n, n.FixedBootBlock ?? layout.Allocate(n.Length))).ToArray();
        foreach ((OpticalNode node, int block) in files)
        {
            iso?.SetFile(node, block);
            udf?.SetFile(node, block);
        }
        if (udf is not null) { layout.Allocate(257 * OpticalLayout.SectorSize); }
        long trailer = OpticalLayout.Offset(source.Iso?.TrailingBlocks ?? 0);
        if (fixedLength is long requested)
        {
            long padding = requested - layout.Length - trailer;
            if (udf is not null || padding < 0 || padding % OpticalLayout.SectorSize != 0 || padding > 32L * 1024 * 1024)
            { throw new NotSupportedException("Edited ISO must fit the existing CUE track, with at most 32 MiB of added padding."); }
            layout.Allocate(padding);
        }
        end = layout.End;
        layout.Allocate(trailer);
        OutputLength = layout.Length;
    }

    public long OutputLength { get; }

    private static IEnumerable<OpticalNode> Enumerate(OpticalNode root, CancellationToken token)
    {
        var queue = new Queue<(OpticalNode Node, int Depth)>();
        queue.Enqueue((root, 0));
        int count = 0;
        while (queue.TryDequeue(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (++count > OpticalLayout.MaximumEntries || item.Depth > OpticalLayout.MaximumDepth)
            { throw new NotSupportedException("Optical tree exceeds the bounded entry/depth limit."); }
            yield return item.Node;
            foreach (OpticalNode child in item.Node.Children) { queue.Enqueue((child, item.Depth + 1)); }
        }
    }

    private void Apply(StagedWriteOperation operation)
    {
        string path = ImagePath.Normalize(operation.Request.Path);
        int split = path.LastIndexOf('/');
        if (path == "/") { throw new NotSupportedException("The optical root cannot be edited."); }
        OpticalNode parent = OpticalFileSystem.Lookup(tree, split == 0 ? "/" : path[..split]);
        if (!parent.Directory) { throw new DirectoryNotFoundException(path); }
        string name = path[(split + 1)..];
        OpticalNode? node = parent.Children.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (node is not null && ContainsBootMember(node))
        {
            throw new NotSupportedException("The boot catalog, boot-image files and their containing directories must remain unchanged.");
        }
        switch (operation.Request.Kind)
        {
            case ImageChangeKind.AddFile:
            case ImageChangeKind.CreateDirectory:
                if (node is not null) { throw new IOException("Duplicate optical name: " + path); }
                node = new OpticalNode(name, operation.Request.Kind == ImageChangeKind.CreateDirectory, DateTime.UtcNow);
                parent.Children.Add(node);
                if (!node.Directory) { SetContent(node, operation); }
                break;
            case ImageChangeKind.ReplaceFile:
                if (node is null || node.Directory) { throw new FileNotFoundException("Expected ordinary optical file.", path); }
                SetContent(node, operation);
                break;
            case ImageChangeKind.Delete:
                if (node is null) { throw new FileNotFoundException("Optical entry not found.", path); }
                parent.Children.Remove(node);
                break;
            case ImageChangeKind.Rename:
                if (node is null) { throw new FileNotFoundException("Optical entry not found.", path); }
                string newName = operation.Request.NewName ?? throw new ArgumentException("Rename requires a name.");
                if (parent.Children.Any(n => n != node && n.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                { throw new IOException("Optical rename collides with another entry."); }
                node.Name = newName;
                node.DropIdentifiers();
                break;
            default:
                throw new NotSupportedException("Optical metadata editing is not supported.");
        }
    }

    private static bool ContainsBootMember(OpticalNode node) =>
        node.FixedBootBlock.HasValue || node.Children.Any(ContainsBootMember);

    private static void SetContent(OpticalNode node, StagedWriteOperation operation)
    {
        node.Content = operation.OpenContent ?? throw new ArgumentException("Missing staged payload.");
        using Stream input = node.Content();
        node.Length = input.Length;
        if (node.Length < 0 || node.Length > uint.MaxValue)
        { throw new NotSupportedException("Optical file exceeds the single-extent file size bound."); }
    }

    public void Write(Stream output, IProgress<OperationProgress>? progress, CancellationToken cancellationToken,
        string? scratchDirectory = null)
    {
        output.SetLength(OutputLength);
        iso?.Write(output, end, cancellationToken);
        udf?.Write(output, end, cancellationToken);
        byte[] buffer = new byte[65536];
        long copied = 0;
        int count = 0;
        foreach ((OpticalNode node, int block) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Stream input = node.Content?.Invoke() ?? throw new InvalidDataException("Missing optical file extent.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            output.Position = OpticalLayout.Offset(block);
            long remaining = node.Length;
            while (remaining != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int take = (int)Math.Min(buffer.Length, remaining);
                input.ReadExactly(buffer.AsSpan(0, take));
                output.Write(buffer, 0, take);
                hash.AppendData(buffer, 0, take);
                remaining -= take;
                copied += take;
                progress?.Report(new("Writing optical payload", node.Name, copied, count));
            }
            if (input.ReadByte() != -1) { throw new IOException("Optical payload length changed during rebuild."); }
            writtenHashes.Add(node, Convert.ToHexString(hash.GetHashAndReset()));
            count++;
        }
    }

    public void Validate(IReadOnlyFileSystem reopened, CancellationToken cancellationToken)
    {
        if (reopened is not OpticalFileSystem actual || !actual.RebuildCapability.Available
            || (source.Iso is null) != (actual.Iso is null) || (source.Udf is null) != (actual.Udf is null)
            || source.VolumeLabel != actual.VolumeLabel)
        { throw new InvalidDataException("Optical output lost an expected namespace or volume identity."); }
        iso?.Validate(actual.Iso!);
        udf?.Validate(actual.Udf!);
        Compare(tree, actual.Root);
        void Compare(OpticalNode expected, OpticalNode actualNode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expected.Name != actualNode.Name || expected.Directory != actualNode.Directory || expected.Hidden != actualNode.Hidden
                || expected.Directory && expected.Children.Count != actualNode.Children.Count
                || !expected.Directory && expected.Length != actualNode.Length)
            { throw new InvalidDataException("Optical namespace/metadata differs after rebuild: " + expected.Name); }
            iso?.ValidateEntry(expected, actualNode);
            udf?.ValidateEntry(expected, actualNode);
            if (!expected.Directory)
            {
                using Stream input = actualNode.Content!();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536];
                int n;
                while ((n = input.Read(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, n);
                }
                if (Convert.ToHexString(hash.GetHashAndReset()) != writtenHashes[expected])
                { throw new InvalidDataException("Optical output content hash differs: " + expected.Name); }
                return;
            }
            var byName = actualNode.Children.ToDictionary(n => n.Name, StringComparer.Ordinal);
            foreach (OpticalNode child in expected.Children)
            {
                if (!byName.TryGetValue(child.Name, out OpticalNode? found))
                { throw new InvalidDataException("Optical output entry missing: " + child.Name); }
                Compare(child, found);
            }
        }
    }
}

internal static class OpticalBinary
{
    internal static ushort U16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b[o..]);
    internal static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
    internal static ulong U64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b[o..]);
    internal static void W16(Span<byte> b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b[o..], v);
    internal static void W32(Span<byte> b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b[o..], v);
    internal static void W64(Span<byte> b, int o, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b[o..], v);
    internal static uint Both32(ReadOnlySpan<byte> b, int o)
    {
        uint v = U32(b, o);
        if (v != BinaryPrimitives.ReadUInt32BigEndian(b[(o + 4)..]))
        { throw new ImageFormatException("Optical both-endian fields disagree."); }
        return v;
    }
    internal static ushort Both16(ReadOnlySpan<byte> b, int o)
    {
        ushort v = U16(b, o);
        if (v != BinaryPrimitives.ReadUInt16BigEndian(b[(o + 2)..]))
        { throw new ImageFormatException("Optical both-endian fields disagree."); }
        return v;
    }
    internal static void WBoth32(Span<byte> b, int o, int v)
    {
        W32(b, o, checked((uint)v));
        BinaryPrimitives.WriteUInt32BigEndian(b[(o + 4)..], (uint)v);
    }
    internal static void WBoth16(Span<byte> b, int o, ushort v)
    {
        W16(b, o, v);
        BinaryPrimitives.WriteUInt16BigEndian(b[(o + 2)..], v);
    }
    internal static byte[] Sector(IReadOnlyBlockDevice device, int block) => Bounds.Read(device, OpticalLayout.Offset(block), OpticalLayout.SectorSize);
}
