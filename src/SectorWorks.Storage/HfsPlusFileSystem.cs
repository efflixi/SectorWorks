using System.Buffers.Binary;
using System.Text;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed class HfsPlusFileSystemHandler : IReadOnlyFileSystemHandler
{
    public IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token)
    {
        if (device.Length < 1536) { return null; }
        byte[] header = Bounds.Read(device, 1024, 512);
        if (!header.AsSpan(0, 2).SequenceEqual("H+"u8) && !header.AsSpan(0, 2).SequenceEqual("HX"u8)) { return null; }
        return new HfsPlusReader(device, header, context, token).Open();
    }
}

internal sealed class HfsPlusReader
{
    private const int MaximumRecords = 131072;
    private readonly IReadOnlyBlockDevice device;
    private readonly byte[] header;
    private readonly ParserContext context;
    private readonly CancellationToken token;
    private readonly long blockSize;
    private readonly uint blocks;
    private readonly List<IndexedExtent> reserved = [];

    internal HfsPlusReader(IReadOnlyBlockDevice device, byte[] header, ParserContext context, CancellationToken token)
    {
        this.device = device;
        this.header = header;
        this.context = context;
        this.token = token;
        uint attributes = U32(header, 4);
        blockSize = U32(header, 40);
        blocks = U32(header, 44);
        if (!header.AsSpan(0, 4).SequenceEqual("H+\0\x04"u8)
            || (attributes & 0x100) == 0 || (attributes & 0x2800) != 0
            || blockSize is < 512 or > 65536 || (blockSize & (blockSize - 1)) != 0
            || blocks == 0 || blocks > device.Length / blockSize)
        { throw new ImageFormatException("HFS+ requires a clean non-journaled version 4 volume with bounded allocation blocks. HFSX is unsupported."); }
        reserved.Add(new(0, 0, 1536));
        reserved.Add(new(0, blocks * blockSize - 1024, 1024));
    }

    internal IReadOnlyFileSystem Open()
    {
        IndexedExtent[] catalog = [];
        for (int offset = 112; offset < 512; offset += 80)
        {
            IndexedExtent[] extents = ReadFork(header, offset, out long length, out bool overflow);
            if (length > 128L * 1024 * 1024 || overflow) { throw Invalid("fragmented or oversized system fork"); }
            foreach (IndexedExtent extent in extents)
            {
                CheckReserved(extent);
                reserved.Add(extent);
            }
            if (offset == 272) { catalog = extents; }
        }
        long catalogLength = checked((long)U64(header, 272));
        if (catalog.Length == 0 || catalogLength < 512) { throw Invalid("catalog fork"); }
        byte[] first = ReadCatalog(catalog, 0, 512);
        int nodeSize = U16(first, 32);
        uint leafCount = U32(first, 20), firstLeaf = U32(first, 24), lastLeaf = U32(first, 28), nodeCount = U32(first, 36);
        if (first[8] != 1 || nodeSize is < 512 or > 32768 || (nodeSize & (nodeSize - 1)) != 0
            || leafCount is < 2 or > MaximumRecords || firstLeaf == 0 || lastLeaf == 0 || nodeCount < 2
            || nodeCount * (long)nodeSize > catalogLength || U16(first, 14) is < 1 or > 16)
        { throw Invalid("catalog B-tree geometry"); }

        var parents = new Dictionary<uint, List<HfsChild>>();
        var directoryIds = new HashSet<uint>();
        var visitedLeaves = new HashSet<uint>();
        var catalogIds = new HashSet<uint>();
        string? label = null;
        uint leaf = firstLeaf, previous = 0, encountered = 0;
        while (leaf != 0)
        {
            token.ThrowIfCancellationRequested();
            if (leaf >= nodeCount || !visitedLeaves.Add(leaf) || visitedLeaves.Count > MaximumRecords)
            { throw Invalid("catalog leaf cycle or address"); }
            byte[] node = ReadCatalog(catalog, leaf * (long)nodeSize, nodeSize);
            int count = U16(node, 10);
            if (node[8] != 0xff || node[9] != 1 || U32(node, 4) != previous
                || count < 1 || count > (nodeSize - 16) / 6 || encountered + count > leafCount)
            { throw Invalid("catalog leaf header or count"); }
            encountered += (uint)count;
            int limit = nodeSize - (count + 1) * 2;
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                int start = U16(node, nodeSize - 2 - i * 2), end = U16(node, nodeSize - 4 - i * 2);
                if (start < 14 || end > limit || end - start < 10) { throw Invalid("catalog record bounds"); }
                int keyBytes = U16(node, start), chars = U16(node, start + 6);
                if (chars > 255 || keyBytes != 6 + chars * 2 || start + 2 + keyBytes > end - 2)
                { throw Invalid("catalog key length"); }
                uint parent = U32(node, start + 2);
                int content = start + 2 + keyBytes;
                ushort recordType = U16(node, content);
                if (recordType is 3 or 4) { continue; }
                if (recordType is not (1 or 2) || chars == 0 || end - content < (recordType == 1 ? 88 : 248))
                { throw Invalid("catalog file or directory record"); }
                uint id = U32(node, content + 8);
                if (id < 2 || !catalogIds.Add(id)) { throw Invalid("catalog identifier"); }
                string name;
                try
                {
                    name = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true)
                        .GetString(node, start + 8, chars * 2);
                }
                catch (DecoderFallbackException ex) { throw new ImageFormatException("HFS+ catalog filename has invalid UTF-16.", ex); }
                bool directory = recordType == 1;
                int mode = U16(node, content + 42) >> 12;
                if (directory && mode is not (0 or 4)) { throw Invalid("catalog directory mode"); }
                if (directory) { directoryIds.Add(id); }
                if (id == 2 && directory && parent == 1) { label = name; continue; }

                // Private HFS+ hard-link directories are not paths in the user namespace.
                if (parent == 2 && (name.StartsWith("\0\0\0\0HFS+ Private Data", StringComparison.Ordinal)
                    || name == ".HFS+ Private Directory Data\r")) { continue; }
                ImagePath.CheckName(name);
                bool special = !directory && (mode is not (0 or 8)
                    || node.AsSpan(content + 48, 4).SequenceEqual("slnk"u8)
                    || node.AsSpan(content + 48, 4).SequenceEqual("hlnk"u8)
                    || node.AsSpan(content + 48, 4).SequenceEqual("fdrp"u8));
                long length = 0;
                string? restriction = null;
                IndexedExtent[] data = [];
                if (!directory && !special)
                {
                    data = ReadFork(node, content + 88, out length, out bool overflow);
                    if (overflow) { restriction = "HFS+ file data uses extent-overflow records."; }
                    else if ((node[content + 41] & 0x20) != 0)
                    { restriction = "HFS+ compressed data attributes are unsupported."; }
                    if (restriction is null) { foreach (IndexedExtent extent in data) { CheckReserved(extent); } }
                }
                if (!parents.TryGetValue(parent, out var children)) { parents.Add(parent, children = []); }
                DateTime modified = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(U32(node, content + 16));
                children.Add(new(id, new IndexedNode(name, id, directory, length, modified, special, restriction, data)));
            }
            previous = leaf;
            leaf = U32(node, 0);
        }
        if (previous != lastLeaf || encountered != leafCount || label is null) { throw Invalid("catalog leaf count, endpoint or root"); }

        var nodes = new Dictionary<ulong, IReadOnlyList<IndexedNode>>();
        var pending = new Queue<(uint Id, int Level)>();
        pending.Enqueue((2, 0));
        var seen = new HashSet<uint> { 2 };
        while (pending.TryDequeue(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (!directoryIds.Contains(item.Id) || item.Level > 256) { throw Invalid("directory parent or depth"); }
            var contents = new List<IndexedNode>();
            nodes.Add(item.Id, contents);
            if (!parents.TryGetValue(item.Id, out var children)) { continue; }
            foreach (HfsChild child in children)
            {
                contents.Add(child.Node);
                if (!child.Node.Directory) { continue; }
                if (!seen.Add(child.Id)) { throw Invalid("directory cycle"); }
                pending.Enqueue((child.Id, item.Level + 1));
            }
        }
        return new IndexedReadOnlyFileSystem(device, "HFS+", label, caseSensitive: false, 2, nodes,
            new()
            {
                ["Access"] = "Read-only ordinary data forks. Resource forks, hard links and overflow extents are unavailable.",
                ["HFS+ allocation block size"] = blockSize.ToString()
            });
    }

    private IndexedExtent[] ReadFork(byte[] data, int offset, out long length, out bool overflow)
    {
        length = checked((long)U64(data, offset));
        uint allocated = U32(data, offset + 12);
        if (allocated > blocks || length > allocated * blockSize) { throw Invalid("fork size or allocation count"); }
        var ranges = new List<IndexedExtent>();
        long logical = 0;
        bool tail = false;
        for (int position = offset + 16; position < offset + 80; position += 8)
        {
            uint start = U32(data, position), lengthInBlocks = U32(data, position + 4);
            if (lengthInBlocks == 0)
            {
                if (start != 0) { throw Invalid("unused fork extent"); }
                tail = true;
                continue;
            }
            long physical = start * blockSize;
            long bytes = lengthInBlocks * blockSize;
            if (tail || start == 0 || start >= blocks || lengthInBlocks > blocks - start
                || logical + bytes > allocated * blockSize || ranges.Any(extent => Overlap(extent, physical, bytes)))
            { throw Invalid("fork extent addresses or overlap"); }
            ranges.Add(new(logical, physical, bytes));
            logical += bytes;
        }
        overflow = logical < allocated * blockSize;
        return ranges.ToArray();
    }

    private byte[] ReadCatalog(IndexedExtent[] map, long start, int length)
    {
        context.Consume(length);
        var bytes = new byte[length];
        int read = 0;
        foreach (IndexedExtent extent in map)
        {
            long position = start + read - extent.Logical;
            if (position < 0 || position >= extent.Length) { continue; }
            int count = (int)Math.Min(length - read, extent.Length - position);
            Bounds.ReadExactly(device, extent.Physical + position, bytes.AsSpan(read, count));
            read += count;
            if (read == length) { return bytes; }
        }
        throw Invalid("catalog block is outside its mapped extents");
    }

    private void CheckReserved(IndexedExtent check)
    {
        if (reserved.Any(extent => Overlap(extent, check.Physical, check.Length))) { throw Invalid("fork overlaps reserved metadata"); }
    }

    private static bool Overlap(IndexedExtent extent, long start, long length) =>
        start < extent.Physical + extent.Length && extent.Physical < start + length;
    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    private static ulong U64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset));
    private static ImageFormatException Invalid(string field) => new($"HFS+ has an invalid or unsupported {field}.");
    private readonly record struct HfsChild(uint Id, IndexedNode Node);
}
