using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal sealed class GhostContainerHandler : IReadOnlyContainerHandler
{
    private const int HeaderBytes = 512;
    private const int BlockBytes = 32768;
    private const uint RecordMagic = 0x012f18d8;

    public FormatDetection? Detect(IReadOnlyBlockDevice source)
    {
        if (source.Length < 4) { return null; }
        byte[] signature = Bounds.Read(source, 0, 4);
        return U16(signature, 0) == 0xeffe && signature[2] is 1 or 9 && signature[3] <= 9
            ? new("GHO", 94, true, "Norton Ghost image signature") : null;
    }

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        byte[] header = Bounds.Read(source, 0, HeaderBytes);
        byte compression = header[3];
        if (header[2] != 1 || compression is not (0 or >= 2 and <= 9) || header.AsSpan(8).ContainsAnyExcept((byte)0))
        { throw new ImageFormatException("Ghost requires an unencrypted, single-file image with the qualified linear partition envelope and verbatim, Fast LZ or zlib blocks. Legacy filesystem-aware and spanned layouts are unsupported."); }

        long position = HeaderBytes;
        byte[] record = Bounds.Read(source, position, 10);
        if (IsRecord(record, 6))
        {
            int length = U16(record, 8);
            byte[] track = Bounds.Read(source, position + 10, length);
            if (length < 518 || (length - 6) % 512 != 0 || track[0] != 6 || track[1] != (length - 6) / 512)
            { throw new ImageFormatException("Ghost Track 0 metadata length disagrees with its sector count."); }
            position += 10 + length;
            record = Bounds.Read(source, position, 10);
        }
        if (!IsRecord(record, 0x0603) || U16(record, 8) != 20
            || Bounds.Read(source, position + 10, 20).AsSpan().ContainsAnyExcept((byte)0))
        { throw new ImageFormatException("Ghost does not contain the supported single linear partition descriptor."); }

        position += 30;
        byte[] partition = Bounds.Read(source, position, HeaderBytes);
        if (U16(partition, 0) != 0xeffe || partition[2] != 2 || partition[3] != compression
            || U32(partition, 4) != U32(header, 4) || partition.AsSpan(8).ContainsAnyExcept((byte)0))
        { throw new ImageFormatException("Ghost partition header disagrees with the image compression, identifier or layout."); }
        position += HeaderBytes;

        var blocks = new List<GhostBlock>();
        int lastBytes = 0;
        bool ended = false;
        while (position + 10 <= source.Length)
        {
            context.Consume(10);
            record = Bounds.Read(source, position, 10);
            if (IsRecord(record, 0x23))
            {
                if (U16(record, 8) != 24 || source.Length - position != 34
                    || Bounds.Read(source, position + 10, 24).AsSpan().ContainsAnyExcept((byte)0))
                { throw new ImageFormatException("Ghost end record or trailing content is unsupported."); }
                ended = true;
                break;
            }
            int length = U16(record, 0) - 2;
            if (length is < 1 or > BlockBytes + 4 || length > source.Length - position - 2 || blocks.Count >= 65536)
            { throw new ImageFormatException("Ghost block length or block count exceeds the supported fork bounds."); }
            if (blocks.Count != 0 && lastBytes != BlockBytes)
            { throw new ImageFormatException("Only the final Ghost partition block may be shorter than 32 KiB."); }

            bool compressed = compression != 0 && record[2] == 0;
            int prefix = compression == 0 ? 0 : 4;
            if (length <= prefix || compression != 0 && (record[2] > 1 || record.AsSpan(3, 3).ContainsAnyExcept((byte)0)))
            { throw new ImageFormatException("Ghost contains an unsupported partition block encoding."); }
            var block = new GhostBlock(position + 2 + prefix, length - prefix, compressed);
            if (compressed)
            {
                // Full blocks are decoded on demand. Decode the final block once to determine logical EOF.
                lastBytes = BlockBytes;
            }
            else { lastBytes = block.Length; }
            blocks.Add(block);
            position += 2 + length;
        }
        if (!ended || blocks.Count == 0) { throw new ImageFormatException("Ghost is missing a partition payload or end record."); }
        if (blocks[^1].Compressed)
        {
            var buffer = new byte[BlockBytes];
            var last = blocks[^1];
            lastBytes = Decode(compression, Bounds.Read(source, last.Offset, last.Length), buffer);
        }
        if (lastBytes is < 512 or > BlockBytes || lastBytes % 512 != 0)
        { throw new ImageFormatException("Ghost partition length is not a whole number of sectors."); }
        properties["Ghost layout"] = "One linear partition payload; Track 0 remains descriptive only.";
        properties["Ghost compression"] = compression switch { 0 => "Verbatim", 2 => "Fast LZ and verbatim", _ => "Zlib and verbatim" };
        return new GhostDevice(source, blocks.ToArray(), compression, (blocks.Count - 1L) * BlockBytes + lastBytes);
    }

    private static int Decode(byte compression, byte[] encoded, byte[] output) => compression == 2
        ? DecodeFastLz(encoded, output) : ExactZlib.InflateBounded("Ghost", encoded, output);

    // Fast LZ block layout documented by nyarime/gho, MIT. See the retained notice.
    private static int DecodeFastLz(byte[] input, byte[] output)
    {
        Span<int> dictionary = stackalloc int[4096];
        dictionary.Fill(-1);
        int position = 0, written = 0, literalRun = 0;
        int control = 1;
        ReadOnlySpan<byte> initial = "123456789012345678"u8;
        while (position < input.Length)
        {
            if (control == 1)
            {
                if (input.Length - position < 3) { throw InvalidFastLz(); }
                control = U16(input, position) | 0x10000;
                position += 2;
            }
            if ((control & 1) == 0)
            {
                if (written == output.Length) { throw InvalidFastLz(); }
                output[written++] = input[position++];
                if (++literalRun == 3)
                {
                    dictionary[Hash(output, written - 3)] = written - 3;
                    literalRun = 2;
                }
            }
            else
            {
                if (input.Length - position < 2) { throw InvalidFastLz(); }
                int length = 3 + (input[position] & 15);
                int code = ((input[position] & 240) << 4) | input[position + 1];
                position += 2;
                int match = dictionary[code];
                if (length > output.Length - written || match >= written) { throw InvalidFastLz(); }
                int start = written;
                for (int i = 0; i < length; i++)
                { output[written++] = match == -1 ? initial[i] : output[match + i]; }
                if (literalRun > 0)
                {
                    int literalStart = start - literalRun;
                    dictionary[Hash(output, literalStart)] = literalStart;
                    if (literalRun == 2) { dictionary[Hash(output, literalStart + 1)] = literalStart + 1; }
                    literalRun = 0;
                }
                dictionary[code] = start;
            }
            control >>= 1;
        }
        return written;

        static int Hash(byte[] bytes, int start) =>
            unchecked((-24993 * (bytes[start + 2] ^ (16 * (bytes[start + 1] ^ (16 * bytes[start]))))) >> 4) & 4095;
        static ImageFormatException InvalidFastLz() => new("Ghost Fast LZ block has a truncated token or exceeds its output bound.");
    }

    private static bool IsRecord(byte[] record, uint type) => U32(record, 0) == type && U32(record, 4) == RecordMagic;
    private readonly record struct GhostBlock(long Offset, int Length, bool Compressed);

    private sealed class GhostDevice(IReadOnlyBlockDevice source, GhostBlock[] blocks, byte compression, long length) : IReadOnlyBlockDevice
    {
        private readonly object gate = new();
        private int cachedNumber = -1;
        private byte[]? cached;
        public long Length => length;
        public int LogicalSectorSize => 512;
        public int? PhysicalSectorSize => null;
        public bool IsReadOnly => true;

        public int Read(long offset, Span<byte> buffer)
        {
            Bounds.Check(offset, buffer.Length, Length);
            if (buffer.IsEmpty) { return 0; }
            int number = (int)(offset / BlockBytes);
            int within = (int)(offset % BlockBytes);
            int count = Math.Min(buffer.Length, BlockBytes - within);
            GhostBlock block = blocks[number];
            if (!block.Compressed) { Bounds.ReadExactly(source, block.Offset + within, buffer[..count]); }
            else
            {
                lock (gate)
                {
                    if (number != cachedNumber)
                    {
                        int outputBytes = (int)Math.Min(BlockBytes, Length - number * (long)BlockBytes);
                        var decoded = new byte[outputBytes];
                        if (Decode(compression, Bounds.Read(source, block.Offset, block.Length), decoded) != outputBytes)
                        { throw new ImageFormatException("Ghost block did not decode to its expected length."); }
                        cached = decoded;
                        cachedNumber = number;
                    }
                    cached.AsSpan(within, count).CopyTo(buffer);
                }
            }
            return count;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, buffer.Span));
        }
        public void Dispose() { }
    }
}
