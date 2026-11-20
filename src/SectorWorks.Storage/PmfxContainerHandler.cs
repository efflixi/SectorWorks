using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal sealed class PmfxContainerHandler : IReadOnlyContainerHandler
{
    private const int BlockSize = 1024 * 1024, TableEntries = 65_536, TableOffset = 0x1000;
    private const int PayloadStart = TableOffset + TableEntries * 16;

    public FormatDetection? Detect(IReadOnlyBlockDevice source) =>
        source.Length >= 4 && Bounds.Read(source, 0, 4).AsSpan().SequenceEqual("PMFX"u8)
            ? new("PMFX", 98, true, "DiskGenius PMFX header") : null;

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        byte[] header = Bounds.Read(source, 0, TableOffset);
        int setting = U16(header, 10);
        uint backupMode = U32(header, 0x10c);
        if (U32(header, 4) != 256 || U16(header, 8) != 256 || setting is not (1 or 9)
            || U32(header, 12) != 0 || U32(header, 16) != 512 || U32(header, 28) != 2048
            || U32(header, 32) != TableEntries || U32(header, 36) != 1 || U32(header, 40) != 0x518
            || U32(header, 0x54) != 0x418 || !header.AsSpan(0x58, 18).SequenceEqual("P\0m\0f\0x\0I\0m\0a\0g\0e\0"u8)
            || !header.AsSpan(0x100, 4).SequenceEqual("CLDK"u8)
            || backupMode is not (2 or 4) || U64(header, 0x518) != TableOffset)
        { throw new ImageFormatException("PMFX header uses an unsupported geometry, mode, flags, or table layout."); }
        long length = checked((long)U64(header, 20) * 512);
        if (length <= 0 || length > (long)BlockSize * TableEntries)
        { throw new ImageFormatException("PMFX logical size exceeds the single-table volume bounds."); }
        int blockCount = (int)(1 + (length - 1) / BlockSize);
        context.Consume(TableEntries * 16);
        byte[] table = Bounds.Read(source, TableOffset, TableEntries * 16);
        var blocks = new PmfxBlock[blockCount];
        var physical = new List<(long Start, long End)>();
        for (int i = 0; i < TableEntries; i++)
        {
            context.Consume(0);
            ReadOnlySpan<byte> entry = table.AsSpan(i * 16, 16);
            if (!entry.ContainsAnyExcept((byte)0)) { continue; }
            if (i >= blockCount)
            { throw new ImageFormatException("PMFX has a mapped block beyond logical EOF."); }
            long start = checked((long)U64(table, i * 16));
            uint encodedAllocation = U32(table, i * 16 + 8);
            long allocation = encodedAllocation & 0x7fffffff;
            int stored = checked((int)U32(table, i * 16 + 12));
            if (stored is < 1 or > BlockSize)
            { throw new ImageFormatException("PMFX block has an invalid stored length."); }
            AddRange(start, allocation);
            if ((encodedAllocation & 0x80000000) == 0)
            {
                if (stored > allocation)
                { throw new ImageFormatException("PMFX stored length exceeds its allocated block."); }
                blocks[i] = new(start, stored, 0, stored);
                continue;
            }
            if (allocation < 16)
            { throw new ImageFormatException("PMFX chained block has a truncated fragment header."); }
            context.Consume(16);
            byte[] fragment = Bounds.Read(source, start, 16);
            long fragmentOffset = checked((long)U64(fragment, 4));
            uint fragmentAllocation = U32(fragment, 12);
            long inline = allocation - 16;
            if (U32(fragment, 0) != 1 || inline >= stored || stored - inline > fragmentAllocation || stored == BlockSize)
            { throw new ImageFormatException("PMFX chained block is outside the supported single-fragment compressed layout."); }
            AddRange(fragmentOffset, fragmentAllocation);
            blocks[i] = new(start + 16, (int)inline, fragmentOffset, stored);
        }
        physical.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (int i = 1; i < physical.Count; i++)
        {
            if (physical[i - 1].End > physical[i].Start)
            { throw new ImageFormatException("PMFX payload or fragment allocations overlap."); }
        }
        var device = new PmfxDevice(source, blocks, length);
        resources.Add(device);
        properties["PMFX block size"] = BlockSize.ToString();
        properties["PMFX backup mode"] = backupMode.ToString();
        properties["PMFX setting"] = setting.ToString();
        return device;

        void AddRange(long offset, long capacity)
        {
            if (offset < PayloadStart || offset % 4096 != 0 || capacity <= 0 || capacity % 4096 != 0)
            { throw new ImageFormatException("PMFX payload allocation does not have a valid aligned data range."); }
            Bounds.Check(offset, capacity, source.Length);
            physical.Add((offset, offset + capacity));
        }
    }

    private readonly record struct PmfxBlock(long Offset, int Inline, long Fragment, int Stored);

    private sealed class PmfxDevice(IReadOnlyBlockDevice source, PmfxBlock[] blocks, long length) : IReadOnlyBlockDevice
    {
        private readonly object gate = new();
        private readonly byte[] cached = new byte[BlockSize];
        private int cachedIndex = -1;
        private bool disposed;

        public long Length => length;
        public int LogicalSectorSize => 512;
        public int? PhysicalSectorSize => null;
        public bool IsReadOnly => true;

        public int Read(long offset, Span<byte> buffer)
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                Bounds.Check(offset, buffer.Length, length);
                int within = (int)(offset % BlockSize);
                int count = Math.Min(buffer.Length, BlockSize - within);
                if (count == 0) { return 0; }
                int index = (int)(offset / BlockSize);
                if (cachedIndex != index) { Load(index); }
                cached.AsSpan(within, count).CopyTo(buffer);
                return count;
            }
        }

        private void Load(int index)
        {
            cachedIndex = -1;
            PmfxBlock block = blocks[index];
            if (block.Stored == 0) { cached.AsSpan().Clear(); }
            else if (block.Stored == BlockSize) { Bounds.ReadExactly(source, block.Offset, cached); }
            else
            {
                var stored = new byte[block.Stored];
                Bounds.ReadExactly(source, block.Offset, stored.AsSpan(0, block.Inline));
                if (block.Fragment != 0)
                { Bounds.ReadExactly(source, block.Fragment, stored.AsSpan(block.Inline)); }
                ExactZlib.Inflate("PMFX", stored, cached);
            }
            int meaningful = (int)Math.Min(BlockSize, length - (long)index * BlockSize);
            if (cached.AsSpan(meaningful).ContainsAnyExcept((byte)0))
            { throw new ImageFormatException("PMFX has nonzero padding beyond logical EOF."); }
            cachedIndex = index;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, buffer.Span));
        }

        public void Dispose() { lock (gate) { disposed = true; } }
    }
}

internal static class ExactZlib
{
    internal static void Inflate(string format, byte[] compressed, byte[] output)
    {
        if (InflateBounded(format, compressed, output) != output.Length)
        { throw new ImageFormatException($"{format} zlib member ends before its declared decoded length."); }
    }

    internal static int InflateBounded(string format, byte[] compressed, byte[] output)
    {
        var inflater = new Inflater();
        try
        {
            inflater.SetInput(compressed);
            int offset = 0;
            while (offset < output.Length)
            {
                int count = inflater.Inflate(output, offset, output.Length - offset);
                if (count == 0) { break; }
                offset += count;
            }
            byte[] extra = new byte[1];
            if (inflater.Inflate(extra) != 0 || !inflater.IsFinished || inflater.RemainingInput != 0)
            { throw new ImageFormatException($"{format} zlib member has missing termination, extra input, or extra decoded data."); }
            return offset;
        }
        catch (SharpZipBaseException ex)
        { throw new ImageFormatException($"{format} zlib member failed decode or checksum validation.", ex); }
    }
}
