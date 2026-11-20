using System.Buffers.Binary;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal abstract class SparseDiskDevice(IReadOnlyBlockDevice source, long length, int blockSize) : IReadOnlyBlockDevice
{
    public long Length => length;
    public int LogicalSectorSize => 512;
    public int? PhysicalSectorSize => null;
    public bool IsReadOnly => true;
    protected int BlockSize => blockSize;

    protected abstract long PhysicalOffset(long block);

    public int Read(long offset, Span<byte> buffer)
    {
        Bounds.Check(offset, buffer.Length, Length);
        int written = 0;
        while (written < buffer.Length)
        {
            long position = offset + written;
            long physical = PhysicalOffset(position / blockSize);
            int within = (int)(position % blockSize);
            int count = Math.Min(buffer.Length - written, blockSize - within);
            Span<byte> destination = buffer.Slice(written, count);
            if (physical < 0) { destination.Clear(); }
            else { Bounds.ReadExactly(source, physical + within, destination); }
            written += count;
        }
        return written;
    }

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(offset, buffer.Span));
    }

    public void Dispose() { }
}

internal sealed class VdiContainerHandler : IReadOnlyContainerHandler
{
    private const uint Free = 0xffffffff, Zero = 0xfffffffe;

    public FormatDetection? Detect(IReadOnlyBlockDevice source) =>
        source.Length >= 68 && U32(Bounds.Read(source, 64, 4), 0) == 0xbeda107f
            ? new("VDI", 95, true, "VirtualBox VDI signature") : null;

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        byte[] header = Bounds.Read(source, 0, 456);
        uint type = U32(header, 76), flags = U32(header, 80), headerSize = U32(header, 72);
        if (U32(header, 68) != 0x00010001 || headerSize < 384 || type is not (1 or 2) || flags != 0
            || header.AsSpan(424, 32).ContainsAnyExcept((byte)0))
        { throw new ImageFormatException("VDI requires a standalone normal or fixed version 1.1 image without parent references or special flags."); }
        long tableOffset = U32(header, 340), dataOffset = U32(header, 344);
        long length = checked((long)U64(header, 368));
        int blockSize = checked((int)U32(header, 376));
        uint extra = U32(header, 380), count = U32(header, 384), allocated = U32(header, 388);
        if (length <= 0 || length % 512 != 0 || blockSize is < 512 or > 16 * 1024 * 1024
            || (blockSize & (blockSize - 1)) != 0 || U32(header, 360) != 512 || extra != 0
            || count != 1 + (length - 1) / blockSize || count > 1_048_576 || allocated > count
            || tableOffset < 72 + (long)headerSize || dataOffset < tableOffset + 4L * count
            || dataOffset % 512 != 0)
        { throw new ImageFormatException("VDI block geometry, sector size, or metadata bounds are invalid or unsupported."); }
        Bounds.Check(dataOffset, (long)allocated * blockSize, source.Length);
        context.Consume(checked((int)count * 4));
        byte[] map = Bounds.Read(source, tableOffset, checked((int)count * 4));
        var seen = new bool[allocated];
        for (int i = 0; i < map.Length; i += 4)
        {
            uint index = U32(map, i);
            if (type == 1 && index is Free or Zero) { continue; }
            if (index >= allocated || seen[index])
            { throw new ImageFormatException("VDI contains an out-of-range or duplicate physical block."); }
            seen[index] = true;
        }
        var device = new VdiDevice(source, length, blockSize, dataOffset, map);
        resources.Add(device);
        properties["VDI allocation"] = type == 2 ? "Fixed" : "Dynamic";
        properties["VDI block size"] = blockSize.ToString();
        return device;
    }

    private sealed class VdiDevice(IReadOnlyBlockDevice source, long length, int blockSize, long dataOffset, byte[] map)
        : SparseDiskDevice(source, length, blockSize)
    {
        protected override long PhysicalOffset(long block)
        {
            uint index = U32(map, (int)block * 4);
            return index is Free or Zero ? -1 : dataOffset + (long)index * BlockSize;
        }
    }
}

internal sealed class QcowContainerHandler : IReadOnlyContainerHandler
{
    private const ulong OffsetMask = 0x00fffffffffffe00UL;
    private const ulong Copied = 1UL << 63;
    private const ulong Zero = 1;
    private const int MaximumTableBytes = 32 * 1024 * 1024;

    public FormatDetection? Detect(IReadOnlyBlockDevice source) =>
        source.Length >= 4 && Read32(Bounds.Read(source, 0, 4), 0) == 0x514649fb
            ? new("QCOW2", 96, true, "QEMU QCOW magic") : null;

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        byte[] header = Bounds.Read(source, 0, 104);
        uint version = Read32(header, 4), bits = Read32(header, 20);
        if (version is not (2 or 3) || bits is < 9 or > 21 || Read64(header, 8) != 0 || Read32(header, 16) != 0
            || Read32(header, 32) != 0 || Read32(header, 60) != 0 || Read64(header, 64) != 0
            || version == 3 && (Read64(header, 72) != 0 || Read64(header, 88) != 0
                || Read32(header, 96) != 4 || Read32(header, 100) is < 104 or > 512))
        { throw new ImageFormatException("QCOW2 requires a clean standalone v2/v3 image without encryption, backing files, snapshots or unsupported features."); }
        int cluster = 1 << (int)bits, l2Entries = cluster / 8;
        long length = checked((long)Read64(header, 24));
        uint l1Count = Read32(header, 36), refClusters = Read32(header, 56);
        if (length <= 0 || length % 512 != 0 || l1Count is 0 or > 131072 || refClusters == 0
            || l1Count < 1 + (length - 1) / ((long)cluster * l2Entries)
            || (long)refClusters * cluster > MaximumTableBytes)
        { throw new ImageFormatException("QCOW2 virtual size or the active table sizes are outside reader bounds."); }

        long l1Offset = checked((long)Read64(header, 40)), refOffset = checked((long)Read64(header, 48));
        var metadata = new HashSet<long> { 0 };
        int l1Clusters = checked((int)((8L * l1Count + cluster - 1) / cluster));
        Register(l1Offset, l1Clusters);
        Register(refOffset, (int)refClusters);
        byte[] l1 = ReadTable(l1Offset, checked((int)l1Count * 8));
        byte[] references = ReadTable(refOffset, checked((int)refClusters * cluster));
        for (int i = 0; i < references.Length; i += 8)
        {
            ulong entry = Read64(references, i);
            if (entry != 0) { Register(checked((long)entry), 1); }
        }

        var tables = new Dictionary<long, byte[]>();
        for (int i = 0; i < l1.Length; i += 8)
        {
            ulong entry = Read64(l1, i);
            if (entry == 0) { continue; }
            if ((entry & ~(OffsetMask | Copied)) != 0 || (long)(tables.Count + 1) * cluster > MaximumTableBytes)
            { throw new ImageFormatException("QCOW2 L1 flags or active L2 table budget are unsupported."); }
            long position = (long)(entry & OffsetMask);
            Register(position, 1);
            tables.Add(i / 8, ReadTable(position, cluster));
        }
        ulong supported = OffsetMask | Copied | (version == 3 ? Zero : 0);
        foreach (byte[] table in tables.Values)
        {
            for (int i = 0; i < table.Length; i += 8)
            {
                ulong entry = Read64(table, i);
                long position = (long)(entry & OffsetMask);
                if ((entry & ~supported) != 0 || (position != 0 && (position % cluster != 0 || metadata.Contains(position))))
                { throw new ImageFormatException("QCOW2 contains a compressed, unsupported, or metadata-overlapping data cluster."); }
                if (position != 0) { Bounds.Check(position, cluster, source.Length); }
            }
        }
        var device = new QcowDevice(source, length, cluster, l2Entries, tables);
        resources.Add(device);
        properties["QCOW2 version"] = version.ToString();
        properties["QCOW2 cluster size"] = cluster.ToString();
        return device;

        void Register(long position, int clusters)
        {
            if (position < cluster || position % cluster != 0)
            { throw new ImageFormatException("QCOW2 metadata has an invalid cluster offset."); }
            Bounds.Check(position, (long)clusters * cluster, source.Length);
            for (int i = 0; i < clusters; i++)
            {
                context.Consume(0);
                if (!metadata.Add(position + (long)i * cluster))
                { throw new ImageFormatException("QCOW2 metadata clusters overlap."); }
            }
        }

        byte[] ReadTable(long position, int count)
        {
            context.Consume(count);
            return Bounds.Read(source, position, count);
        }
    }

    private static uint Read32(ReadOnlySpan<byte> content, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(content[offset..]);

    private static ulong Read64(ReadOnlySpan<byte> content, int offset) =>
        BinaryPrimitives.ReadUInt64BigEndian(content[offset..]);

    private sealed class QcowDevice(IReadOnlyBlockDevice source, long length, int cluster, int l2Entries,
        Dictionary<long, byte[]> tables) : SparseDiskDevice(source, length, cluster)
    {
        protected override long PhysicalOffset(long block)
        {
            if (!tables.TryGetValue(block / l2Entries, out byte[]? table)) { return -1; }
            ulong entry = Read64(table, (int)(block % l2Entries) * 8);
            long position = (long)(entry & OffsetMask);
            return position == 0 || (entry & Zero) != 0 ? -1 : position;
        }
    }
}
