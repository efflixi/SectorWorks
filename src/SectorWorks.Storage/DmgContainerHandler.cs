using System.Buffers.Binary;
using System.Xml;
using System.Xml.Linq;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed class DmgContainerHandler : IReadOnlyContainerHandler
{
    private const uint Raw = 1, Zero = 2, Zlib = 0x80000005, Comment = 0x7ffffffe, End = 0xffffffff;
    private const int Sector = 512, MaximumRunBytes = 16 * 1024 * 1024;

    public FormatDetection? Detect(IReadOnlyBlockDevice source) =>
        source.Length >= Sector && Bounds.Read(source, source.Length - Sector, 4).AsSpan().SequenceEqual("koly"u8)
            ? new("DMG", 94, true, "Apple UDIF trailer") : null;

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        long trailer = source.Length - Sector;
        byte[] header = Bounds.Read(source, trailer, Sector);
        long fork = Long(header, 24), data = Long(header, 32), xml = Long(header, 216), xmlLength = Long(header, 224);
        if (U32(header, 4) != 4 || U32(header, 8) != Sector || U32(header, 56) != 1 || U32(header, 60) != 1
            || Long(header, 16) != 0 || Long(header, 48) != 0 || xmlLength is <= 0 or > 8 * 1024 * 1024
            || xml < fork + data)
        { throw new ImageFormatException("DMG requires a single-segment UDIF v4 image with a bounded XML block map."); }
        Bounds.Check(fork, data, trailer);
        Bounds.Check(xml, xmlLength, trailer);
        long length = Long(header, 492) * Sector;
        if (length <= 0) { throw new ImageFormatException("DMG virtual sector count is invalid."); }
        context.Consume((int)xmlLength);
        using var stream = new MemoryStream(Bounds.Read(source, xml, (int)xmlLength), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        XDocument plist;
        try { plist = XDocument.Load(reader); }
        catch (XmlException ex) { throw new ImageFormatException("DMG block-map property list is invalid XML.", ex); }
        XElement resource = Property(plist.Root?.Element("dict"), "resource-fork", "dict");
        XElement maps = Property(resource, "blkx", "array");
        var runs = new List<Run>();
        foreach (XElement map in maps.Elements())
        {
            if (map.Name != "dict") { throw new ImageFormatException("DMG block-map list contains an unexpected item."); }
            context.Consume(0);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(Property(map, "Data", "data").Value); }
            catch (FormatException ex) { throw new ImageFormatException("DMG block map is not valid base64.", ex); }
            if (bytes.Length < 244 || !bytes.AsSpan(0, 4).SequenceEqual("mish"u8) || U32(bytes, 4) != 1)
            { throw new ImageFormatException("DMG block table signature or version is invalid."); }
            long logical = Long(bytes, 8) * Sector, mapLength = Long(bytes, 16) * Sector, mapOffset = Long(bytes, 24);
            uint count = U32(bytes, 200);
            if (count == 0 || 204L + count * 40L != bytes.Length || runs.Count + count > 65536)
            { throw new ImageFormatException("DMG run count exceeds its metadata bounds."); }
            Bounds.Check(logical, mapLength, length);
            long next = 0;
            bool terminated = false;
            for (int i = 204; i < bytes.Length; i += 40)
            {
                context.Consume(0);
                uint type = U32(bytes, i);
                long start = Long(bytes, i + 8) * Sector, size = Long(bytes, i + 16) * Sector;
                long offset = Long(bytes, i + 24), stored = Long(bytes, i + 32);
                if (type is Comment or End)
                {
                    if (size != 0 || stored != 0 || type == End && i + 40 != bytes.Length)
                    { throw new ImageFormatException("DMG block table contains invalid terminator or comment entries."); }
                    terminated = type == End;
                    continue;
                }
                if (type is not (Raw or Zero or Zlib) || start != next || size <= 0)
                { throw new ImageFormatException("DMG uses unsupported codecs or non-contiguous block-map runs."); }
                Bounds.Check(start, size, mapLength);
                if (type == Zero && stored != 0 || type == Raw && stored != size
                    || type == Zlib && (stored is <= 0 or > MaximumRunBytes || size > MaximumRunBytes))
                { throw new ImageFormatException("A DMG run's stored and logical lengths are inconsistent or exceed the decoder cap."); }
                long dataOffset = mapOffset + offset;
                if (type != Zero) { Bounds.Check(dataOffset, stored, data); }
                runs.Add(new(logical + start, size, fork + dataOffset, stored, type));
                next += size;
            }
            if (!terminated || next != mapLength)
            { throw new ImageFormatException("DMG block table does not cover its declared logical length."); }
        }
        Run[] ordered = runs.OrderBy(run => run.Logical).ToArray();
        long covered = 0;
        foreach (Run run in ordered)
        {
            if (run.Logical != covered)
            { throw new ImageFormatException("DMG block tables overlap or omit a logical range."); }
            covered += run.Length;
        }
        if (covered != length) { throw new ImageFormatException("DMG block maps do not cover the virtual disk."); }
        var device = new DmgDevice(source, ordered, length);
        resources.Add(device);
        properties["DMG block map"] = "UDIF v4 raw, zero and zlib runs";
        return device;
    }

    private static XElement Property(XElement? dictionary, string key, string type)
    {
        XElement[] matches = dictionary?.Elements("key").Where(e => e.Value == key).ToArray() ?? [];
        XElement? value = matches.Length == 1 ? matches[0].ElementsAfterSelf().FirstOrDefault() : null;
        return value?.Name == type ? value : throw new ImageFormatException($"DMG property list key {key} is absent or ambiguous.");
    }

    private static uint U32(ReadOnlySpan<byte> source, int offset) => BinaryPrimitives.ReadUInt32BigEndian(source[offset..]);
    private static long Long(ReadOnlySpan<byte> source, int offset) => checked((long)BinaryPrimitives.ReadUInt64BigEndian(source[offset..]));
    private sealed record Run(long Logical, long Length, long Physical, long Stored, uint Type);

    private sealed class DmgDevice(IReadOnlyBlockDevice source, Run[] runs, long length) : IReadOnlyBlockDevice
    {
        private readonly object gate = new();
        private Run? cachedRun;
        private byte[]? cached;
        public long Length => length;
        public int LogicalSectorSize => Sector;
        public int? PhysicalSectorSize => null;
        public bool IsReadOnly => true;

        public int Read(long offset, Span<byte> buffer)
        {
            Bounds.Check(offset, buffer.Length, length);
            if (buffer.IsEmpty) { return 0; }
            int left = 0, right = runs.Length - 1;
            while (left < right)
            {
                int mid = left + (right - left + 1) / 2;
                if (runs[mid].Logical <= offset) { left = mid; }
                else { right = mid - 1; }
            }
            Run run = runs[left];
            long within = offset - run.Logical;
            int count = (int)Math.Min(buffer.Length, run.Length - within);
            if (run.Type == Zero) { buffer[..count].Clear(); }
            else if (run.Type == Raw) { Bounds.ReadExactly(source, run.Physical + within, buffer[..count]); }
            else
            {
                lock (gate)
                {
                    if (!ReferenceEquals(run, cachedRun))
                    {
                        byte[] decoded = new byte[(int)run.Length];
                        ExactZlib.Inflate("DMG", Bounds.Read(source, run.Physical, (int)run.Stored), decoded);
                        cached = decoded;
                        cachedRun = run;
                    }
                    cached!.AsSpan((int)within, count).CopyTo(buffer);
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
