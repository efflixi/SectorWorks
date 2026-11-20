using System.Buffers.Binary;
using System.Text;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed class ExFatFileSystemHandler : IReadOnlyFileSystemHandler
{
    public IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return device.Length >= 512 && Bounds.Read(device, 3, 8).AsSpan().SequenceEqual("EXFAT   "u8)
            ? new ExFatFileSystem(device, context) : null;
    }
}

internal sealed class ExFatFileSystem : IReadOnlyFileSystem
{
    private const int MaximumMetadata = 16 * 1024 * 1024, EndOfChain = -1;
    private readonly IReadOnlyBlockDevice device;
    private readonly ParserContext context;
    private readonly int clusterSize, clusterCount;
    private readonly long fat, heap;
    private readonly int root;
    private readonly byte[] bitmap;
    private readonly char[] upcase;

    internal ExFatFileSystem(IReadOnlyBlockDevice device, ParserContext context)
    {
        this.device = device;
        this.context = context;
        byte[] first = Bounds.Read(device, 0, 512);
        if (first[108] is < 9 or > 12 || first[109] > 25 - first[108] || first[110] != 1
            || U16(first, 104) != 0x0100 || (U16(first, 106) & 0x000f) != 0)
        { throw new ImageFormatException("exFAT requires a clean revision 1.0 volume with a single FAT and supported sector sizes."); }
        int sectorSize = 1 << first[108];
        int clusterSectors = 1 << first[109];
        clusterSize = sectorSize * clusterSectors;
        long sectors = checked((long)U64(first, 72));
        long fatStart = U32(first, 80), fatSectors = U32(first, 84), heapStart = U32(first, 88);
        clusterCount = checked((int)U32(first, 92));
        root = checked((int)U32(first, 96));
        if (sectors <= 24 || sectors > device.Length / sectorSize || fatStart < 24 || fatSectors == 0
            || heapStart < fatStart + fatSectors || clusterCount <= 0 || root < 2 || root - 2 >= clusterCount
            || (long)(clusterCount + 2) * 4 > fatSectors * sectorSize
            || heapStart + (long)clusterCount * clusterSectors > sectors
            || clusterSize > MaximumMetadata || (clusterCount + 7L) / 8 > MaximumMetadata)
        { throw new ImageFormatException("exFAT boot geometry exceeds the volume, FAT or allocation bitmap bounds."); }
        fat = fatStart * sectorSize;
        heap = heapStart * sectorSize;
        byte[] regions = Bounds.Read(device, 0, 24 * sectorSize);
        VerifyBootRegion(regions.AsSpan(0, 12 * sectorSize), sectorSize);
        VerifyBootRegion(regions.AsSpan(12 * sectorSize), sectorSize);
        if (!regions.AsSpan(64, 42).SequenceEqual(regions.AsSpan(12 * sectorSize + 64, 42))
            || !regions.AsSpan(108, 4).SequenceEqual(regions.AsSpan(12 * sectorSize + 108, 4)))
        { throw new ImageFormatException("The exFAT primary and backup geometry disagree."); }

        byte[] directory = LoadDirectory(root, null, false, verifyAllocation: false);
        VolumeLabel = "";
        byte[]? allocation = null, table = null;
        uint upcaseChecksum = 0;
        for (int pos = 0; pos < directory.Length && directory[pos] != 0; pos += 32)
        {
            ReadOnlySpan<byte> entry = directory.AsSpan(pos, 32);
            if (entry[0] == 0x81)
            {
                if (allocation is not null || entry[1] != 0 || U64(entry, 24) != (ulong)((clusterCount + 7L) / 8))
                { throw new ImageFormatException("Invalid exFAT allocation bitmap metadata."); }
                allocation = LoadSpecial(entry);
            }
            else if (entry[0] == 0x82)
            {
                if (table is not null) { throw new ImageFormatException("Duplicate exFAT up-case tables."); }
                upcaseChecksum = U32(entry, 4);
                table = LoadSpecial(entry);
            }
            else if (entry[0] == 0x83)
            {
                if (entry[1] > 11) { throw new ImageFormatException("Invalid exFAT volume label size."); }
                VolumeLabel = Encoding.Unicode.GetString(entry.Slice(2, 2 * entry[1]));
            }
        }
        bitmap = allocation ?? throw new ImageFormatException("The exFAT allocation bitmap is missing.");
        upcase = ParseUpcase(table ?? throw new ImageFormatException("The exFAT up-case table is missing."), upcaseChecksum);
        Properties = new Dictionary<string, string>
        {
            ["Access"] = "Read-only ordinary exFAT files.",
            ["Sector size"] = sectorSize.ToString(),
            ["Cluster size"] = clusterSize.ToString()
        }.AsReadOnly();
    }

    public string Name => "exFAT";
    public string VolumeLabel { get; }
    public bool IsCaseSensitive => false;
    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default) =>
        context.Run<IReadOnlyList<FileEntry>>(() =>
        {
            string normalized = ImagePath.Normalize(path);
            Node node = Resolve(normalized);
            if (!node.Directory) { throw new DirectoryNotFoundException(normalized); }
            return Entries(node).Select(entry => new FileEntry(
                normalized == "/" ? "/" + entry.Name : normalized + "/" + entry.Name,
                entry.Name, entry.Directory, entry.Directory ? 0 : entry.Length, null, entry.Directory ? "Directory" : "File"
            )).ToArray();
        }, cancellationToken);

    public Stream OpenRead(string path) => context.Run<Stream>(() =>
    {
        Node node = Resolve(ImagePath.Normalize(path));
        if (node.Directory) { throw new ImageFormatException("Cannot open an exFAT directory as a file."); }
        int[] chain = Chain(node.Cluster, node.Length, node.Contiguous, verifyAllocation: true);
        return new DeviceStream(new FileDevice(this, node, chain), ownsDevice: true);
    });

    private Node Resolve(string path)
    {
        Node node = new("", true, root, 0, 0, false);
        var ancestors = new HashSet<int> { root };
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.Directory) { throw new DirectoryNotFoundException(path); }
            string key = Fold(part);
            node = Entries(node).FirstOrDefault(entry => Fold(entry.Name) == key)
                ?? throw new FileNotFoundException("exFAT file or directory was not found.", path);
            if (node.Directory && !ancestors.Add(node.Cluster))
            { throw new ImageFormatException("exFAT directory cluster references an ancestor."); }
        }
        return node;
    }

    private List<Node> Entries(Node node)
    {
        byte[] directory = LoadDirectory(node.Cluster, node.Length == 0 ? null : node.Length, node.Contiguous, verifyAllocation: true);
        var output = new List<Node>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int pos = 0; pos < directory.Length && directory[pos] != 0; pos += 32)
        {
            context.Consume(0);
            byte type = directory[pos];
            if ((type & 0x80) == 0 || type is 0x81 or 0x82 or 0x83 || (type & 0x20) != 0) { continue; }
            if (type != 0x85)
            { throw new ImageFormatException($"Unsupported active exFAT directory entry 0x{type:X2}."); }
            int count = directory[pos + 1];
            if (count is < 2 or > 18 || pos + (count + 1) * 32 > directory.Length)
            { throw new ImageFormatException("Invalid exFAT file entry set size."); }
            ReadOnlySpan<byte> set = directory.AsSpan(pos, (count + 1) * 32);
            pos += count * 32;
            if (set[32] != 0xc0 || U16(set, 2) != SetChecksum(set) || (set[33] & ~3) != 0)
            { throw new ImageFormatException("Invalid exFAT file set checksum, stream flags or layout."); }
            int characters = set[35];
            if (characters == 0 || (characters + 14) / 15 != count - 1)
            { throw new ImageFormatException("Invalid exFAT filename entry count."); }
            var name = new char[characters];
            for (int i = 0; i < characters; i++)
            {
                int entry = 64 + i / 15 * 32;
                if (set[entry] != 0xc1)
                { throw new ImageFormatException("Invalid exFAT filename entry type."); }
                name[i] = (char)U16(set, entry + 2 + i % 15 * 2);
            }
            string text = new(name);
            ImagePath.CheckName(text);
            string folded = Fold(text);
            if (!names.Add(folded) || U16(set, 36) != NameHash(folded))
            { throw new ImageFormatException("Duplicate or incorrectly hashed exFAT filename."); }

            int first = checked((int)U32(set, 52));
            long valid = checked((long)U64(set, 40)), length = checked((long)U64(set, 56));
            bool isDirectory = (U16(set, 4) & 0x10) != 0;
            bool contiguous = (set[33] & 2) != 0;
            if (valid > length || length > (long)clusterCount * clusterSize
                || isDirectory && (length == 0 || length % clusterSize != 0 || valid != length)
                || length != 0 && (first < 2 || first - 2 >= clusterCount)
                || length == 0 && first != 0)
            { throw new ImageFormatException("exFAT file or directory length and cluster bounds disagree."); }
            output.Add(new(text, isDirectory, first, length, valid, contiguous));
            if (output.Count > 131072) { throw new ImageFormatException("exFAT directory exceeds the supported entry count."); }
        }
        return output;
    }

    private byte[] LoadSpecial(ReadOnlySpan<byte> entry)
    {
        long length = checked((long)U64(entry, 24));
        if (length is <= 0 or > MaximumMetadata)
        { throw new ImageFormatException("exFAT allocation or case metadata exceeds 16 MiB."); }
        int[] chain = Chain(checked((int)U32(entry, 20)), length, false, verifyAllocation: false);
        return LoadBytes(chain, (int)length);
    }

    private byte[] LoadDirectory(int start, long? length, bool contiguous, bool verifyAllocation)
    {
        if (length > MaximumMetadata)
        { throw new ImageFormatException("exFAT directory exceeds the 16 MiB metadata bound."); }
        int[] chain = Chain(start, length, contiguous, verifyAllocation, MaximumMetadata / clusterSize);
        return LoadBytes(chain, checked((int)(length ?? (long)chain.Length * clusterSize)));
    }

    private byte[] LoadBytes(int[] chain, int length)
    {
        var bytes = new byte[length];
        for (int i = 0, output = 0; output < bytes.Length; i++)
        {
            int count = Math.Min(clusterSize, length - output);
            context.Consume(count);
            Bounds.ReadExactly(device, Physical(chain[i]), bytes.AsSpan(output, count));
            output += count;
        }
        return bytes;
    }

    private int[] Chain(int start, long? length, bool contiguous, bool verifyAllocation, int maximum = 131072)
    {
        if (length == 0) { return []; }
        long required = length is { } size ? 1 + (size - 1) / clusterSize : 0;
        if (required > maximum || contiguous && required == 0)
        { throw new ImageFormatException("exFAT file or directory needs more clusters than the bounded reader supports."); }
        var result = new List<int>((int)required);
        var seen = new HashSet<int>();
        Span<byte> item = stackalloc byte[4];
        int cluster = start;
        for (int count = 0; count < maximum; count++)
        {
            int bit = cluster - 2;
            if (bit < 0 || bit >= clusterCount || !seen.Add(cluster)
                || verifyAllocation && (bitmap[bit / 8] & (1 << (bit % 8))) == 0)
            { throw new ImageFormatException("exFAT cluster chain is invalid, cyclic or refers to an unallocated cluster."); }
            result.Add(cluster);
            if (contiguous)
            {
                if (result.Count == required) { return result.ToArray(); }
                cluster++;
                continue;
            }
            context.Consume(4);
            Bounds.ReadExactly(device, fat + 4L * cluster, item);
            uint next = U32(item, 0);
            cluster = next == uint.MaxValue ? EndOfChain : checked((int)next);
            if (cluster == EndOfChain)
            {
                if (required != 0 && result.Count != required)
                { throw new ImageFormatException("exFAT cluster chain ends before the file length."); }
                return result.ToArray();
            }
            if (required != 0 && result.Count == required)
            { throw new ImageFormatException("exFAT cluster chain continues beyond its declared length."); }
        }
        throw new ImageFormatException("exFAT cluster chain exceeds the supported bounds.");
    }

    private static void VerifyBootRegion(ReadOnlySpan<byte> boot, int sectorSize)
    {
        if (!boot.Slice(3, 8).SequenceEqual("EXFAT   "u8) || U16(boot, 510) != 0xaa55
            || boot.Slice(11, 53).ContainsAnyExcept((byte)0))
        { throw new ImageFormatException("Invalid exFAT boot-region signature."); }
        uint checksum = 0;
        for (int i = 0; i < sectorSize * 11; i++)
        {
            if (i is not (106 or 107 or 112)) { checksum = Rotate32(checksum, boot[i]); }
        }
        for (int i = sectorSize * 11; i < boot.Length; i += 4)
        {
            if (U32(boot, i) != checksum) { throw new ImageFormatException("Invalid exFAT boot-region checksum."); }
        }
    }

    private static char[] ParseUpcase(byte[] table, uint expected)
    {
        uint checksum = 0;
        foreach (byte b in table) { checksum = Rotate32(checksum, b); }
        if (checksum != expected || table.Length % 2 != 0)
        { throw new ImageFormatException("The exFAT up-case table checksum or length is invalid."); }
        var result = new char[65536];
        int i = 0, code = 0;
        while (i < table.Length && code < result.Length)
        {
            ushort value = U16(table, i);
            i += 2;
            if (value == 0xffff && code != 0xffff)
            {
                if (i == table.Length) { throw new ImageFormatException("Truncated exFAT up-case run."); }
                int count = U16(table, i);
                i += 2;
                if (count == 0 || count > result.Length - code)
                { throw new ImageFormatException("Invalid exFAT up-case identity run."); }
                for (int end = code + count; code < end; code++) { result[code] = (char)code; }
            }
            else { result[code++] = (char)value; }
        }
        if (i != table.Length || code != result.Length)
        { throw new ImageFormatException("The exFAT up-case table does not cover UTF-16 code units exactly."); }
        return result;
    }

    private string Fold(string name) => string.Create(name.Length, (name, upcase), static (target, state) =>
    {
        for (int i = 0; i < target.Length; i++) { target[i] = state.upcase[state.name[i]]; }
    });

    private static ushort SetChecksum(ReadOnlySpan<byte> input)
    {
        ushort checksum = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (i is not (2 or 3)) { checksum = Rotate16(checksum, input[i]); }
        }
        return checksum;
    }

    private static ushort NameHash(string input)
    {
        ushort checksum = 0;
        foreach (char c in input)
        {
            checksum = Rotate16(checksum, unchecked((byte)c));
            checksum = Rotate16(checksum, (byte)(c >> 8));
        }
        return checksum;
    }

    private static uint Rotate32(uint sum, byte value) => unchecked(((sum >> 1) | (sum << 31)) + value);
    private static ushort Rotate16(ushort sum, byte value) => unchecked((ushort)(((sum >> 1) | ((sum & 1) << 15)) + value));
    private static ushort U16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    private static ulong U64(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt64LittleEndian(data[at..]);
    private long Physical(int cluster) => heap + (long)(cluster - 2) * clusterSize;
    public void Dispose() { }

    private sealed record Node(string Name, bool Directory, int Cluster, long Length, long ValidLength, bool Contiguous);

    private sealed class FileDevice(ExFatFileSystem fs, Node node, int[] chain) : IReadOnlyBlockDevice
    {
        public long Length => node.Length;
        public int LogicalSectorSize => fs.clusterSize;
        public int? PhysicalSectorSize => null;
        public bool IsReadOnly => true;

        public int Read(long offset, Span<byte> buffer)
        {
            Bounds.Check(offset, buffer.Length, Length);
            if (buffer.IsEmpty) { return 0; }
            int within = (int)(offset % fs.clusterSize);
            int count = Math.Min(buffer.Length, fs.clusterSize - within);
            int valid = (int)Math.Clamp(node.ValidLength - offset, 0, count);
            if (valid != 0)
            { Bounds.ReadExactly(fs.device, fs.Physical(chain[(int)(offset / fs.clusterSize)]) + within, buffer[..valid]); }
            buffer[valid..count].Clear();
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
