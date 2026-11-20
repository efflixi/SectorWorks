using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed class RawCdContainerHandler : IReadOnlyContainerHandler
{
    private const int MaximumSectors = 450_000;

    public FormatDetection? Detect(IReadOnlyBlockDevice source)
    {
        if (source.Length < 17L * CdRawSector.RawBytes || source.Length % CdRawSector.RawBytes != 0)
        { return null; }
        Span<byte> sector = stackalloc byte[CdRawSector.RawBytes];
        Bounds.ReadExactly(source, 16L * CdRawSector.RawBytes, sector);
        if (!TryMode(sector, out CdTrackMode mode)) { return null; }
        int payload = CdRawSector.PayloadOffset(mode);
        return sector.Slice(payload + 1, 5).SequenceEqual("CD001"u8)
            ? new("Raw CD", 85, true, "ISO descriptor in a framed raw CD sector") : null;
    }

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        long count = source.Length / CdRawSector.RawBytes;
        if (count > MaximumSectors)
        { throw new ImageFormatException("The raw CD track exceeds the supported CD sector count."); }
        Span<byte> sector = stackalloc byte[CdRawSector.RawBytes];
        Bounds.ReadExactly(source, 0, sector);
        int first = CdRawSector.Address(sector);
        if (!TryMode(sector, out CdTrackMode mode) || first < 0)
        { throw new ImageFormatException("The raw CD track has an invalid initial data sector or address."); }
        foreach (long index in new[] { 16L, count - 1 })
        {
            Bounds.ReadExactly(source, index * CdRawSector.RawBytes, sector);
            if (CdRawSector.DataHeaderIssue(sector, mode) is not null || CdRawSector.Address(sector) != first + index)
            { throw new ImageFormatException($"The raw CD track changes mode or sector address at sector {index}. A verified track descriptor is required."); }
        }
        var device = new CueTrackDevice(source, 0, (int)count, mode, initialAddress: first);
        resources.Add(device);
        properties["Track layout"] = mode == CdTrackMode.Mode1Raw ? "Mode 1 / 2352" : "Mode 2 Form 1 / 2352";
        properties["Stored sectors"] = count.ToString();
        return device;
    }

    private static bool TryMode(ReadOnlySpan<byte> sector, out CdTrackMode mode)
    {
        mode = sector[15] == 2 ? CdTrackMode.Mode2Raw : CdTrackMode.Mode1Raw;
        return CdRawSector.DataHeaderIssue(sector, mode) is null;
    }
}

internal sealed class ImageDiskContainerHandler : IReadOnlyContainerHandler
{
    private const int SectorBytes = 512;
    private const int MaximumComment = 65536;

    public FormatDetection? Detect(IReadOnlyBlockDevice source)
    {
        if (source.Length < 9) { return null; }
        Span<byte> prefix = stackalloc byte[9];
        Bounds.ReadExactly(source, 0, prefix);
        return prefix[..4].SequenceEqual("IMD "u8) && prefix[5] == (byte)'.' && prefix[8] == (byte)':'
            && Digit(prefix[4]) && Digit(prefix[6]) && Digit(prefix[7])
            ? new("ImageDisk IMD", 92, true, "ImageDisk version header") : null;
    }

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        byte[] prefix = Bounds.Read(source, 0, (int)Math.Min(MaximumComment, source.Length));
        int end = Array.IndexOf(prefix, (byte)0x1a);
        if (end < 9) { throw new ImageFormatException("The ImageDisk comment is missing its terminator within 64 KiB."); }
        long position = end + 1;
        var tracks = new SortedDictionary<(int Cylinder, int Head), ImdSector[]>();
        int cylinders = 0, heads = 0, sectors = 0;
        Span<byte> header = stackalloc byte[5];
        Span<byte> numberMap = stackalloc byte[64];
        Span<byte> optionalMap = stackalloc byte[64];
        Span<byte> coding = stackalloc byte[2];
        while (position < source.Length)
        {
            context.Consume(header.Length);
            Bounds.ReadExactly(source, position, header);
            position += header.Length;
            int cylinder = header[1], head = header[2] & 1, count = header[3];
            if (header[0] > 5 || cylinder >= 86 || (header[2] & 0x3e) != 0 ||
                count is < 1 or > 64 || header[4] != 2)
            { throw new ImageFormatException("The ImageDisk track uses an unsupported mode, geometry or sector size. The reader requires uniform 512-byte sectors."); }
            if (sectors != 0 && count != sectors)
            { throw new ImageFormatException("ImageDisk tracks have different sector counts. A uniform floppy layout is required."); }
            ReadMap(source, ref position, numberMap[..count]);
            if ((header[2] & 0x80) != 0)
            { RequireMap(source, ref position, optionalMap[..count], (byte)cylinder); }
            if ((header[2] & 0x40) != 0)
            { RequireMap(source, ref position, optionalMap[..count], (byte)head); }

            var track = new ImdSector[count];
            if (!tracks.TryAdd((cylinder, head), track))
            { throw new ImageFormatException("The ImageDisk image has a duplicate cylinder/head track."); }
            cylinders = Math.Max(cylinders, cylinder + 1);
            heads = Math.Max(heads, head + 1);
            sectors = count;
            for (int i = 0; i < count; i++)
            {
                context.Consume(1);
                int number = numberMap[i];
                if (number < 1 || number > count || track[number - 1].Offset != 0)
                { throw new ImageFormatException("The ImageDisk sector numbering map is missing, duplicate, or outside the track."); }
                Bounds.ReadExactly(source, position++, coding[..1]);
                if (coding[0] is not (1 or 2))
                { throw new ImageFormatException($"ImageDisk C{cylinder} H{head} S{number} is unavailable, deleted, or marked with a data error (sector type {coding[0]})."); }
                bool compressed = coding[0] == 2;
                Bounds.Check(position, compressed ? 1 : SectorBytes, source.Length);
                track[number - 1] = new(position, compressed);
                position += compressed ? 1 : SectorBytes;
            }
        }
        if (tracks.Count == 0 || tracks.Count != cylinders * heads)
        { throw new ImageFormatException("The ImageDisk track map does not cover a complete uniform floppy geometry."); }
        var device = new ImdDevice(source, tracks.Values.SelectMany(track => track).ToArray());
        resources.Add(device);
        properties["ImageDisk geometry"] = $"{cylinders} cylinders, {heads} heads, {sectors} sectors/track";
        return device;
    }

    private static bool Digit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static void ReadMap(IReadOnlyBlockDevice source, ref long position, Span<byte> map)
    {
        Bounds.ReadExactly(source, position, map);
        position += map.Length;
    }

    private static void RequireMap(IReadOnlyBlockDevice source, ref long position, Span<byte> map, byte physical)
    {
        ReadMap(source, ref position, map);
        if (map.ContainsAnyExcept(physical))
        { throw new ImageFormatException("ImageDisk maps sector addresses onto a different cylinder or head."); }
    }

    private readonly record struct ImdSector(long Offset, bool Compressed);

    private sealed class ImdDevice(IReadOnlyBlockDevice source, ImdSector[] sectors) : IReadOnlyBlockDevice
    {
        public long Length => (long)sectors.Length * SectorBytes;
        public int LogicalSectorSize => SectorBytes;
        public int? PhysicalSectorSize => SectorBytes;
        public bool IsReadOnly => true;

        public int Read(long offset, Span<byte> buffer)
        {
            Bounds.Check(offset, buffer.Length, Length);
            Span<byte> value = stackalloc byte[1];
            int written = 0;
            while (written < buffer.Length)
            {
                long position = offset + written;
                ImdSector sector = sectors[(int)(position / SectorBytes)];
                int within = (int)(position % SectorBytes);
                int length = Math.Min(buffer.Length - written, SectorBytes - within);
                Span<byte> destination = buffer.Slice(written, length);
                if (sector.Compressed)
                {
                    Bounds.ReadExactly(source, sector.Offset, value);
                    destination.Fill(value[0]);
                }
                else { Bounds.ReadExactly(source, sector.Offset + within, destination); }
                written += length;
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
}
