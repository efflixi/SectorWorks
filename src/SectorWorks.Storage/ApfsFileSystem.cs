using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal sealed class ApfsFileSystemHandler : IReadOnlyFileSystemHandler
{
    public IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token)
    {
        if (device.Length < 4096 || !Bounds.Read(device, 32, 4).AsSpan().SequenceEqual("NXSB"u8)) { return null; }
        return new ApfsReader(device, context, token).Open();
    }
}

internal sealed class ApfsReader
{
    private const int BlockSize = 4096;
    private const int MaximumRecords = 131072;
    private const ulong ObjectNumberMask = (1UL << 60) - 1;
    private readonly IReadOnlyBlockDevice device;
    private readonly ParserContext context;
    private readonly CancellationToken token;
    private readonly ulong blockCount;
    private readonly byte[] superblock;
    private readonly ulong checkpoint;
    private readonly HashSet<ulong> metadataBlocks = [];
    private int records;

    internal ApfsReader(IReadOnlyBlockDevice device, ParserContext context, CancellationToken token)
    {
        this.device = device;
        this.context = context;
        this.token = token;
        byte[] initial = Bounds.Read(device, 0, BlockSize);
        blockCount = U64(initial, 40);
        if (!ValidChecksum(initial) || blockCount < 8 || blockCount > (ulong)(device.Length / BlockSize)
            || U32(initial, 36) != BlockSize || (U64(initial, 64) & ~2UL) != 0 || ObjectType(initial) != 1)
        { throw Invalid("container checksum, features or 4096-byte block geometry"); }

        ulong ring = U64(initial, 112);
        uint ringLength = U32(initial, 104);
        if (ring == 0 || ring >= blockCount || ringLength is < 2 or > 256 || ringLength > blockCount - ring)
        { throw Invalid("checkpoint descriptor ring"); }
        metadataBlocks.Add(0);
        byte[]? selected = null;
        for (uint i = 0; i < ringLength; i++)
        {
            ulong address = ring + i;
            metadataBlocks.Add(address);
            byte[] block = ReadBlock(address);
            if (!block.AsSpan(32, 4).SequenceEqual("NXSB"u8) || !ValidChecksum(block)) { continue; }
            ulong transaction = U64(block, 16);
            if (selected is not null && transaction <= U64(selected, 16)) { continue; }
            if (!block.AsSpan(36, 36).SequenceEqual(initial.AsSpan(36, 36))
                || !block.AsSpan(72, 16).SequenceEqual(initial.AsSpan(72, 16))
                || ObjectType(block) != 1 || U64(block, 8) != 1 || U32(block, 104) != ringLength
                || U64(block, 112) != ring)
            { throw Invalid("newest checkpoint disagrees with container geometry or identity"); }
            uint start = U32(block, 136), count = U32(block, 140);
            if (start >= ringLength || count < 2 || count > ringLength
                || (start + count - 1) % ringLength != i)
            { throw Invalid("checkpoint descriptor ordering"); }
            bool finalMap = false;
            for (uint j = 0; j < count - 1; j++)
            {
                byte[] map = ReadBlock(ring + (start + j) % ringLength);
                if (ObjectType(map) != 12 || U64(map, 16) != transaction || !ValidChecksum(map)
                    || finalMap || (U32(map, 32) & ~1U) != 0 || U32(map, 36) > (BlockSize - 40) / 40)
                { throw Invalid("checkpoint mapping list"); }
                finalMap = U32(map, 32) == 1;
            }
            if (!finalMap) { throw Invalid("missing final checkpoint mapping block"); }
            selected = block;
        }
        if (selected is null || U64(selected, 16) < U64(initial, 16))
        { throw Invalid("no complete current checkpoint"); }
        superblock = selected;
        checkpoint = U64(superblock, 16);
    }

    internal IReadOnlyFileSystem Open()
    {
        uint slots = U32(superblock, 180);
        if (slots is < 1 or > 100) { throw Invalid("container filesystem table length"); }
        ulong[] volumes = Enumerable.Range(0, (int)slots).Select(slot => U64(superblock, 184 + slot * 8)).Where(oid => oid != 0).ToArray();
        if (volumes.Length != 1)
        { throw new ImageFormatException("APFS browsing currently requires one volume in the container."); }
        var containerMap = ReadObjectMap(U64(superblock, 160), checkpoint);
        ObjectMapEntry reference = Resolve(containerMap, volumes[0]);
        byte[] volume = ReadObject(reference.Address, 13, volumes[0], reference.Transaction);
        ulong incompatible = U64(volume, 56);
        if (!volume.AsSpan(32, 4).SequenceEqual("APSB"u8) || (incompatible & ~9UL) != 0
            || U64(volume, 48) != 0 || (U64(volume, 40) & ~15UL) != 0
            || (U64(volume, 264) & 1) == 0 || U64(volume, 216) != 0
            || U32(volume, 116) != 0x00000002)
        { throw new ImageFormatException("APFS requires an unencrypted ordinary volume with no snapshots or incompatible volume features."); }
        bool caseSensitive = (incompatible & 1) == 0;
        var filesystemMap = ReadObjectMap(U64(volume, 128), reference.Transaction);
        ulong treeOid = U64(volume, 136);
        var inodes = new Dictionary<ulong, Inode>();
        var directories = new Dictionary<ulong, List<DirectoryRecord>>();
        var extents = new Dictionary<ulong, List<IndexedExtent>>();
        var compressed = new HashSet<ulong>();
        WalkTree(treeOid, physical: false, filesystemMap, reference.Transaction, (key, value) =>
        {
            if (key.Length < 8) { throw Invalid("filesystem key length"); }
            ulong kindAndId = U64(key, 0);
            ulong id = kindAndId & ObjectNumberMask;
            switch (kindAndId >> 60)
            {
                case 3:
                    if (key.Length != 8 || value.Length < 92) { throw Invalid("inode record length"); }
                    ulong streamId = U64(value, 8);
                    long size = ReadStreamLength(value);
                    var data = new Inode(streamId, size, U16(value, 80) >> 12, U64(value, 24),
                        (U32(value, 68) & 0x20) != 0);
                    if (!inodes.TryAdd(id, data)) { throw Invalid("duplicate inode"); }
                    break;
                case 9:
                    if (value.Length < 18) { throw Invalid("directory record length"); }
                    int nameOffset = (incompatible & 9) == 0 ? 10 : 12;
                    string name = ReadName(key, nameOffset, nameOffset == 10 ? U16(key, 8) : (int)(U32(key, 8) & 1023));
                    ImagePath.CheckName(name);
                    if (!directories.TryGetValue(id, out var entries)) { directories.Add(id, entries = []); }
                    entries.Add(new(name, U64(value, 0), U16(value, 16) & 15));
                    break;
                case 8:
                    if (key.Length != 16 || value.Length != 24) { throw Invalid("extent record length"); }
                    long logical = checked((long)U64(key, 8));
                    long length = checked((long)U64(value, 0));
                    ulong address = U64(value, 8);
                    if (logical % BlockSize != 0 || length <= 0 || length >> 56 != 0 || length % BlockSize != 0
                        || address >= blockCount || (ulong)(length / BlockSize) > blockCount - address)
                    { throw Invalid("data extent addresses, flags or bounds"); }
                    if (!extents.TryGetValue(id, out var list)) { extents.Add(id, list = []); }
                    list.Add(new(logical, address == 0 ? -1 : checked((long)address * BlockSize), length));
                    break;
                case 4:
                    if (key.Length < 11) { throw Invalid("extended attribute name"); }
                    if (ReadName(key, 10, U16(key, 8)) == "com.apple.decmpfs") { compressed.Add(id); }
                    break;
            }
        });
        if (!inodes.TryGetValue(2, out Inode root) || root.Kind != 4) { throw Invalid("root directory inode"); }
        long[] reserved = metadataBlocks.Select(address => checked((long)address * BlockSize)).Order().ToArray();
        var nodes = new Dictionary<ulong, IReadOnlyList<IndexedNode>>();
        var visitedDirectories = new HashSet<ulong> { 2 };
        var pending = new Queue<(ulong Id, int Level)>();
        pending.Enqueue((2, 0));
        while (pending.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested();
            var children = new List<IndexedNode>();
            nodes.Add(current.Id, children);
            if (!directories.TryGetValue(current.Id, out var childrenOnDisk)) { continue; }
            foreach (DirectoryRecord child in childrenOnDisk)
            {
                if (!inodes.TryGetValue(child.Id, out Inode inode) || child.Kind != inode.Kind)
                { throw Invalid("directory entry refers to a missing or differently typed inode"); }
                bool directory = child.Kind == 4;
                bool special = child.Kind is not (4 or 8);
                IndexedExtent[] data = [];
                string? unavailable = inode.Compressed || compressed.Contains(child.Id)
                    ? "APFS compressed data attributes are unsupported." : null;
                if (directory)
                {
                    if (current.Level >= 256 || !visitedDirectories.Add(child.Id)) { throw Invalid("directory cycle or depth"); }
                    pending.Enqueue((child.Id, current.Level + 1));
                }
                else if (!special && unavailable is null && inode.Length > 0)
                {
                    if (!extents.TryGetValue(inode.Stream, out var mapped) || mapped.Count == 0)
                    { throw Invalid("file length has no stream extents"); }
                    data = mapped.OrderBy(extent => extent.Logical).ToArray();
                    long end = 0;
                    foreach (IndexedExtent extent in data)
                    {
                        if (extent.Logical != end || extent.Logical >= inode.Length)
                        { throw Invalid("overlapping, missing or unused file extent"); }
                        end = checked(extent.Logical + extent.Length);
                        if (extent.Physical < 0) { continue; }
                        int first = Array.BinarySearch(reserved, extent.Physical);
                        if (first < 0) { first = ~first; }
                        if (first < reserved.Length && reserved[first] - extent.Physical < extent.Length)
                        { throw Invalid("file extent overlaps active filesystem metadata"); }
                    }
                    if (end < inode.Length || end - inode.Length >= BlockSize) { throw Invalid("file size disagrees with its extents"); }
                }
                DateTime? modified = inode.Modified / 100 is > 0 and < 2534023007990000000
                    ? DateTime.UnixEpoch.AddTicks((long)(inode.Modified / 100)) : null;
                children.Add(new(child.Name, child.Id, directory, directory || special ? 0 : inode.Length,
                    modified, special, unavailable, data));
            }
        }
        int labelEnd = Array.IndexOf(volume, (byte)0, 704, 256);
        string label = labelEnd < 0 ? throw Invalid("volume name terminator") : Decode(volume.AsSpan(704, labelEnd - 704));
        return new IndexedReadOnlyFileSystem(device, "APFS", label, caseSensitive, 2, nodes,
            new()
            {
                ["Access"] = "Read-only, ordinary data streams; compressed attributes and special files are unavailable.",
                ["APFS checkpoint"] = checkpoint.ToString(),
                ["APFS block size"] = BlockSize.ToString()
            });
    }

    private Dictionary<ulong, ObjectMapEntry> ReadObjectMap(ulong address, ulong transaction)
    {
        byte[] map = ReadObject(address, 11, address, transaction);
        if ((U32(map, 32) & ~1U) != 0 || U32(map, 36) != 0 || U64(map, 56) != 0)
        { throw Invalid("object-map snapshots or flags"); }
        var values = new Dictionary<ulong, ObjectMapEntry>();
        var keys = new HashSet<(ulong Id, ulong Transaction)>();
        WalkTree(U64(map, 48), physical: true, null, transaction, (key, value) =>
        {
            ulong oid = U64(key, 0), xid = U64(key, 8);
            uint flags = U32(value, 0);
            ulong block = U64(value, 8);
            if ((flags & ~1U) != 0 || oid == 0 || xid == 0 || !keys.Add((oid, xid))
                || (flags == 0 && (U32(value, 4) != BlockSize || block == 0 || block >= blockCount)))
            { throw Invalid("object-map entry"); }
            if (xid <= transaction && (!values.TryGetValue(oid, out var previous) || xid > previous.Transaction))
            { values[oid] = new(flags == 0 ? block : 0, xid); }
        });
        return values;
    }

    private static ObjectMapEntry Resolve(Dictionary<ulong, ObjectMapEntry> map, ulong oid) =>
        map.TryGetValue(oid, out var result) && result.Address != 0 ? result : throw Invalid("unresolved virtual object");

    private void WalkTree(ulong oid, bool physical, Dictionary<ulong, ObjectMapEntry>? map, ulong transaction,
        Action<byte[], byte[]> visit)
    {
        var seen = new HashSet<ulong>();
        Visit(oid, null);

        void Visit(ulong number, int? expectedLevel)
        {
            bool isRoot = expectedLevel is null;
            var entry = physical ? new ObjectMapEntry(number, transaction) : Resolve(map!, number);
            if (!seen.Add(entry.Address)) { throw Invalid("B-tree block alias or cycle"); }
            byte[] node = ReadObject(entry.Address, isRoot ? 2U : 3U, number, entry.Transaction);
            if (U32(node, 28) != (physical ? 11U : 14U)) { throw Invalid("B-tree subtype"); }
            ushort flags = U16(node, 32);
            int level = U16(node, 34);
            int tableBytes = U16(node, 42), table = 56 + U16(node, 40), firstKey = table + tableBytes;
            int end = BlockSize - (isRoot ? 40 : 0);
            bool leaf = (flags & 2) != 0;
            int stride = physical ? 4 : 8;
            uint count = U32(node, 36);
            if ((flags & ~7) != 0 || ((flags & 1) != 0) != isRoot || ((flags & 4) != 0) != physical
                || level > 16 || (!isRoot && level != expectedLevel) || leaf != (level == 0)
                || firstKey > end || count > tableBytes / (uint)stride || !leaf && count == 0)
            { throw Invalid("B-tree header, level or record count"); }
            var items = new List<(byte[] Key, byte[] Value)>((int)count);
            int lastKey = firstKey, firstValue = end;
            for (int i = 0; i < count; i++)
            {
                if (++records > MaximumRecords) { throw Invalid("B-tree record budget"); }
                int slot = table + i * stride;
                int keyOffset = firstKey + U16(node, slot);
                int keySize = physical ? 16 : U16(node, slot + 2);
                int valueOffset = end - U16(node, slot + (physical ? 2 : 4));
                int valueSize = physical ? (leaf ? 16 : 8) : U16(node, slot + 6);
                if (keySize < 8 || valueSize < 1 || keyOffset + keySize > end
                    || valueOffset < firstKey || valueOffset + valueSize > end || !leaf && valueSize != 8)
                { throw Invalid("B-tree record address or length"); }
                lastKey = Math.Max(lastKey, keyOffset + keySize);
                firstValue = Math.Min(firstValue, valueOffset);
                items.Add((node.AsSpan(keyOffset, keySize).ToArray(), node.AsSpan(valueOffset, valueSize).ToArray()));
            }
            if (lastKey > firstValue) { throw Invalid("B-tree key and value areas overlap"); }
            foreach (var item in items)
            {
                if (leaf) { visit(item.Key, item.Value); }
                else { Visit(U64(item.Value, 0), level - 1); }
            }
        }
    }

    private byte[] ReadObject(ulong address, uint type, ulong oid, ulong transaction)
    {
        byte[] block = ReadBlock(address);
        if (address == 0 || ObjectType(block) != type || U64(block, 8) != oid
            || U64(block, 16) == 0 || U64(block, 16) > transaction || !ValidChecksum(block))
        { throw Invalid("object identity, transaction, type or checksum"); }
        metadataBlocks.Add(address);
        return block;
    }

    private byte[] ReadBlock(ulong address)
    {
        token.ThrowIfCancellationRequested();
        if (address >= blockCount) { throw Invalid("physical block address"); }
        context.Consume(BlockSize);
        return Bounds.Read(device, checked((long)address * BlockSize), BlockSize);
    }

    private static bool ValidChecksum(byte[] data)
    {
        const ulong divisor = uint.MaxValue;
        ulong a = 0, b = 0;
        for (int offset = 8; offset < data.Length; offset += 4)
        {
            a = (a + U32(data, offset)) % divisor;
            b = (b + a) % divisor;
        }
        ulong low = (divisor - (a + b) % divisor) % divisor;
        ulong high = (divisor - (a + low) % divisor) % divisor;
        return U64(data, 0) == (high << 32 | low);
    }

    private static long ReadStreamLength(byte[] inode)
    {
        if (inode.Length == 92) { return 0; }
        int count = U16(inode, 92), bytes = U16(inode, 94);
        int start = 96 + count * 4;
        if (count > 64 || start > inode.Length || bytes != inode.Length - start)
        { throw Invalid("inode extended fields"); }
        long length = 0;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            int field = 96 + 4 * i;
            int size = U16(inode, field + 2);
            if (size > inode.Length - start || size == 0) { throw Invalid("inode extended field size"); }
            if (inode[field] == 8)
            {
                if (found || size != 40) { throw Invalid("inode data stream field"); }
                length = checked((long)U64(inode, start));
                found = true;
            }
            start += (size + 7) & ~7;
        }
        if (start != inode.Length) { throw Invalid("inode extended field padding"); }
        return length;
    }

    private static string ReadName(byte[] data, int start, int length)
    {
        if (length < 2 || start + length != data.Length || data[^1] != 0)
        { throw Invalid("name length or terminator"); }
        return Decode(data.AsSpan(start, length - 1));
    }

    private static string Decode(ReadOnlySpan<byte> data)
    {
        try { return new UTF8Encoding(false, true).GetString(data); }
        catch (DecoderFallbackException ex) { throw new ImageFormatException("APFS name is not valid UTF-8.", ex); }
    }

    private static uint ObjectType(byte[] block) => U32(block, 24) & 0xffff;
    private static ImageFormatException Invalid(string reason) => new($"APFS has an invalid or unsupported {reason}.");
    private readonly record struct ObjectMapEntry(ulong Address, ulong Transaction);
    private readonly record struct Inode(ulong Stream, long Length, int Kind, ulong Modified, bool Compressed);
    private readonly record struct DirectoryRecord(string Name, ulong Id, int Kind);
}
