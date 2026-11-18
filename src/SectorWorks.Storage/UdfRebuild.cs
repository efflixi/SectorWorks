using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.OpticalBinary;
using static SectorWorks.Storage.OpticalLayout;

namespace SectorWorks.Storage;

internal sealed record UdfEntry(byte[] FileEntry);

// ECMA-167 / UDF 1.02 DVD-ROM-style, single Type-1 physical partition.
// VAT, sparing, metadata partitions, extended entries/attributes, hard links,
// writable integrity state and unrecorded/continued data remain unsupported.
internal sealed class UdfImage(byte[][] volume, byte[][] reserveVolume, byte[] integrity, byte[] fileSet, OpticalNode root, string label)
{
    internal const int PartitionStart = 257;
    internal byte[][] Volume { get; } = volume;
    internal byte[][] ReserveVolume { get; } = reserveVolume;
    internal byte[] Integrity { get; } = integrity;
    internal byte[] FileSet { get; } = fileSet;
    internal OpticalNode Root { get; } = root;
    internal string Label { get; } = label;

    internal UdfOutput Plan(OpticalNode root, OpticalLayout layout, int recognition) => new(this, root, layout, recognition);

    internal static UdfImage Read(IReadOnlyBlockDevice device, int recognition, OpticalCoverage coverage, CancellationToken token)
    {
        int sectors = checked((int)(device.Length / SectorSize));
        if (sectors < 260) { throw new ImageFormatException("Truncated UDF optical medium."); }
        for (int i = 0; i < 3; i++)
        {
            byte[] vrs = Sector(device, recognition + i);
            ReadOnlySpan<byte> id = i switch { 0 => "BEA01"u8, 1 => "NSR02"u8, _ => "TEA01"u8 };
            if (vrs[0] != 0 || !vrs.AsSpan(1, 5).SequenceEqual(id) || vrs[6] != 1
                || vrs.AsSpan(7).ContainsAnyExcept((byte)0))
            { throw new NotSupportedException("Only the bounded UDF 1.02 NSR02 recognition sequence is qualified."); }
            coverage.Add(recognition + i, 1);
        }

        byte[] anchor = Sector(device, 256);
        Tag.Verify(anchor, 2, 256, 496);
        if (anchor.AsSpan(32).ContainsAnyExcept((byte)0))
        { throw new ImageFormatException("Unknown UDF anchor data."); }
        coverage.Add(256, 1);
        bool otherAnchor = false;
        for (int block = Math.Max(257, sectors - 257); block < sectors; block++)
        {
            token.ThrowIfCancellationRequested();
            byte[] candidate = Sector(device, block);
            if (U16(candidate, 0) != 2 || U16(candidate, 2) != 2 || U32(candidate, 12) != block) { continue; }
            Tag.Verify(candidate, 2, block, 496);
            if (!anchor.AsSpan(16).SequenceEqual(candidate.AsSpan(16)))
            { throw new ImageFormatException("Main and reserve UDF anchors disagree."); }
            otherAnchor |= block == sectors - 1 || block == sectors - 257;
            coverage.Add(block, 1);
        }
        if (!otherAnchor) { throw new NotSupportedException("No matching UDF anchor at a standard end-of-volume location."); }

        byte[][] main = Sequence(16);
        byte[][] reserve = Sequence(24);
        for (int i = 0; i < main.Length; i++)
        {
            // The independent author emits different Volume Set IDs in its
            // primary descriptors. Preserve both, rather than replacing either.
            bool same = i == 0
                ? main[i].AsSpan(16, 56).SequenceEqual(reserve[i].AsSpan(16, 56))
                  && main[i].AsSpan(200).SequenceEqual(reserve[i].AsSpan(200))
                : main[i].AsSpan(16).SequenceEqual(reserve[i].AsSpan(16));
            if (!same) { throw new NotSupportedException("UDF main and reserve volume descriptors disagree."); }
        }
        byte[] pd = main[2], lvd = main[3];
        if (U16(pd, 20) != 1 || U16(pd, 22) != 0 || !Entity(pd.AsSpan(24, 32), "+NSR02", 2)
            || pd.AsSpan(56, 128).ContainsAnyExcept((byte)0) || U32(pd, 184) != 1
            || U32(lvd, 212) != SectorSize || !Domain(lvd.AsSpan(216, 32))
            || U32(lvd, 264) != 6 || U32(lvd, 268) != 1
            || !lvd.AsSpan(440, 6).SequenceEqual(new byte[] { 1, 6, 1, 0, 0, 0 }))
        { throw new NotSupportedException("UDF must use a read-only Type-1 physical partition with UDF 1.02 domain and no space maps."); }
        int partition = checked((int)U32(pd, 188)), partitionLength = checked((int)U32(pd, 192));
        if (partition <= 256 || partitionLength < 3 || partition > sectors || partitionLength > sectors - partition)
        { throw new ImageFormatException("UDF partition bounds exceed the medium."); }
        if (U32(lvd, 432) != 4096) { throw new NotSupportedException("Unsupported UDF integrity sequence extent."); }
        int integrityBlock = checked((int)U32(lvd, 436));
        byte[] integrity = Sector(device, integrityBlock);
        Tag.Verify(integrity, 9, integrityBlock, 118);
        Tag.Verify(Sector(device, checked(integrityBlock + 1)), 8, integrityBlock + 1, 496);
        coverage.Add(integrityBlock, 2);
        if (U32(integrity, 28) != 1 || integrity.AsSpan(32, 8).ContainsAnyExcept((byte)0)
            || U32(integrity, 72) != 1 || U32(integrity, 76) != 46
            || U32(integrity, 84) != partitionLength || U16(integrity, 128) != 0x102
            || U16(integrity, 130) != 0x102 || U16(integrity, 132) != 0x102)
        { throw new NotSupportedException("UDF integrity state, revision, or partition size is not qualified."); }
        int fsd = ReadLongAddress(lvd.AsSpan(248, 16), 4096);
        if (fsd < 0 || fsd >= partitionLength - 1) { throw new ImageFormatException("UDF file set crosses the partition."); }
        byte[] fileSet = Sector(device, checked(partition + fsd));
        Tag.Verify(fileSet, 256, fsd, 496);
        Tag.Verify(Sector(device, checked(partition + fsd + 1)), 8, fsd + 1, 496);
        coverage.Add(partition + fsd, 2);
        if (!Domain(fileSet.AsSpan(416, 32)) || fileSet.AsSpan(448, 64).ContainsAnyExcept((byte)0)
            || !fileSet.AsSpan(48, 64).SequenceEqual(lvd.AsSpan(20, 64))
            || !fileSet.AsSpan(112, 128).SequenceEqual(lvd.AsSpan(84, 128)))
        { throw new NotSupportedException("Unsupported UDF file-set identity or file-set extensions."); }
        int rootBlock = ReadLongAddress(fileSet.AsSpan(400, 16), SectorSize);
        var reader = new TreeReader(device, coverage, partition, partitionLength, token);
        OpticalNode root = reader.ReadNode(rootBlock, rootBlock, "", null, 0);
        if (!root.Directory || reader.FileCount != U32(integrity, 120) || reader.DirectoryCount != U32(integrity, 124))
        { throw new ImageFormatException("UDF file-set root or recorded integrity counts disagree."); }
        string label = DecodeDString(lvd.AsSpan(84, 128));
        return new(main, reserve, integrity, fileSet, root, label);

        byte[][] Sequence(int offset)
        {
            int size = checked((int)U32(anchor, offset)), start = checked((int)U32(anchor, offset + 4));
            if (size != 32768) { throw new NotSupportedException("Only the bounded contiguous UDF descriptor sequence is qualified."); }
            coverage.Add(start, Blocks(size));
            ushort[] ids = [1, 4, 5, 6, 7, 8];
            var result = new byte[ids.Length][];
            for (int i = 0; i < Blocks(size); i++)
            {
                byte[] data = Sector(device, checked(start + i));
                if (i >= ids.Length)
                {
                    if (data.AsSpan().ContainsAnyExcept((byte)0))
                    { throw new NotSupportedException("Unknown data follows the UDF volume sequence."); }
                    continue;
                }
                int crc = ids[i] == 6 ? 430 : ids[i] == 7 ? 8 : 496;
                Tag.Verify(data, ids[i], start + i, crc);
                result[i] = data;
            }
            if (U32(result[4], 20) != 0)
            { throw new NotSupportedException("UDF unallocated-space descriptors are not qualified."); }
            return result;
        }
    }

    private static bool Entity(ReadOnlySpan<byte> data, string name, byte permittedFlags = 0) => (data[0] & ~permittedFlags) == 0
        && Encoding.ASCII.GetString(data.Slice(1, 23)).TrimEnd('\0') == name;

    private static bool Domain(ReadOnlySpan<byte> data) =>
        Entity(data, "*OSTA UDF Compliant") && U16(data, 24) == 0x102 && (data[26] & ~3) == 0
        && !data[27..].ContainsAnyExcept((byte)0);

    private static int ReadLongAddress(ReadOnlySpan<byte> data, int length, bool fileIdentifier = false)
    {
        if (U32(data, 0) != length || U16(data, 8) != 0 || data.Slice(10, fileIdentifier ? 2 : 6).ContainsAnyExcept((byte)0))
        { throw new NotSupportedException("Unqualified UDF long allocation descriptor."); }
        return checked((int)U32(data, 4));
    }

    private static string DecodeDString(ReadOnlySpan<byte> data)
    {
        int length = data[^1];
        if (length == 0 && !data.ContainsAnyExcept((byte)0)) { return ""; }
        if (length == 0 || length >= data.Length || data[length..^1].ContainsAnyExcept((byte)0))
        { throw new ImageFormatException("Invalid UDF d-string bounds."); }
        return DecodeName(data[..length]);
    }

    private static string DecodeName(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) { throw new ImageFormatException("Missing UDF CS0 identifier."); }
        return data[0] switch
        {
            8 => Encoding.Latin1.GetString(data[1..]),
            16 when data.Length % 2 == 1 => new UnicodeEncoding(true, false, true).GetString(data[1..]),
            _ => throw new NotSupportedException("Unsupported UDF compressed Unicode identifier.")
        };
    }

    internal static byte[] EncodeName(string name)
    {
        if (!IsoImage.NamesSupported(name)) { throw new NotSupportedException("Unsafe UDF filename: " + name); }
        bool latin = name.All(c => c <= 255);
        byte[] encoded = (latin ? Encoding.Latin1 : Encoding.BigEndianUnicode).GetBytes(name);
        if (encoded.Length > 254) { throw new NotSupportedException("UDF CS0 identifier exceeds 255 encoded bytes."); }
        return [(byte)(latin ? 8 : 16), .. encoded];
    }

    private static DateTime DecodeTime(ReadOnlySpan<byte> data)
    {
        int typeZone = U16(data, 0);
        int zone = typeZone & 4095;
        if (zone >= 2048) { zone -= 4096; }
        if ((typeZone >> 12) != 1 || zone is < -1440 or > 1440 || data[9] > 99 || data[10] > 99 || data[11] > 99)
        { throw new NotSupportedException("Unqualified UDF recording time or timezone."); }
        try
        {
            return new DateTime(U16(data, 2), data[4], data[5], data[6], data[7], data[8], DateTimeKind.Utc)
                .AddMinutes(-zone).AddTicks((data[9] * 10000 + data[10] * 100 + data[11]) * 10L);
        }
        catch (ArgumentOutOfRangeException error) { throw new ImageFormatException("Invalid UDF recording date.", error); }
    }

    internal static void WriteTime(Span<byte> data, DateTime time)
    {
        W16(data, 0, 0x1000);
        W16(data, 2, (ushort)time.Year);
        data[4] = (byte)time.Month; data[5] = (byte)time.Day; data[6] = (byte)time.Hour;
        data[7] = (byte)time.Minute; data[8] = (byte)time.Second;
    }

    private sealed class TreeReader(IReadOnlyBlockDevice device, OpticalCoverage coverage, int partition, int length,
        CancellationToken token)
    {
        private readonly HashSet<int> icbs = [];
        private readonly HashSet<ulong> uniqueIds = [];
        private long directoryBudget = 32L * 1024 * 1024;
        internal int FileCount { get; private set; }
        internal int DirectoryCount { get; private set; }

        internal OpticalNode ReadNode(int block, int parentBlock, string name, byte[]? identifier, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > MaximumDepth || icbs.Count >= MaximumEntries || !icbs.Add(block) || block < 0 || block >= length)
            { throw new ImageFormatException("UDF ICB graph has invalid bounds, a cycle, hard link, or exceeds the budget."); }
            int absolute = checked(partition + block);
            coverage.Add(absolute, 1);
            byte[] sector = Sector(device, absolute);
            uint ea = U32(sector, 168), ads = U32(sector, 172);
            bool directory = sector[27] == 4;
            if (ea != 0 || ads is not (0 or 8) || U16(sector, 20) != 4 || U16(sector, 24) != 1
                || sector[26] != 0 || sector[27] is not (4 or 5) || U16(sector, 34) != 0x230
                || sector.AsSpan(28, 6).ContainsAnyExcept((byte)0) || sector[50] != 0 || sector[51] != 0
                || U32(sector, 52) != 0 || sector.AsSpan(112, 16).ContainsAnyExcept((byte)0))
            { throw new NotSupportedException("UDF file entry is not an ordinary short-AD file/directory without extended attributes."); }
            Tag.Verify(sector, 261, block, 160 + (int)ads);
            long bytes = checked((long)U64(sector, 56));
            if (bytes > 0x3fffffff || U64(sector, 64) != (ulong)Blocks(bytes)
                || U32(sector, 176) != bytes || ads == 0 && bytes != 0)
            { throw new NotSupportedException("Only a single recorded UDF extent per entry is supported."); }
            int extent = checked((int)U32(sector, 180));
            if (bytes > 0 && (extent < 0 || extent > length || Blocks(bytes) > length - extent))
            { throw new ImageFormatException("UDF data extent exceeds partition bounds."); }
            ulong uniqueId = U64(sector, 160);
            if (!uniqueIds.Add(uniqueId) || uniqueId > uint.MaxValue || !directory && U16(sector, 48) != 1
                || identifier is not null && U32(identifier, 16) != uniqueId)
            { throw new NotSupportedException("UDF repeated or mismatched identity or nonordinary file link count."); }
            var info = new UdfEntry(sector.AsSpan(16, 160).ToArray());
            DateTime modified = DecodeTime(sector.AsSpan(84, 12));
            _ = DecodeTime(sector.AsSpan(72, 12));
            _ = DecodeTime(sector.AsSpan(96, 12));
            long start = Offset(checked(partition + extent));
            var node = new OpticalNode(name, directory, modified, directory ? 0 : bytes)
            {
                Offset = start,
                Hidden = identifier is not null && (identifier[2] & 1) != 0,
                Udf = info,
                Content = directory ? null : () => OpticalFileSystem.OpenExtent(device, start, bytes)
            };
            if (!directory)
            {
                FileCount++;
                if (bytes > 0) { coverage.AddFile(partition + extent, Blocks(bytes), bytes); }
                return node;
            }
            DirectoryCount++;
            if (bytes is < 40 or > 16 * 1024 * 1024 || (directoryBudget -= bytes) < 0)
            { throw new NotSupportedException("UDF directory exceeds the bounded metadata budget."); }
            coverage.Add(partition + extent, Blocks(bytes));
            byte[] contents = Bounds.Read(device, start, Blocks(bytes) * SectorSize);
            if (contents.AsSpan((int)bytes).ContainsAnyExcept((byte)0))
            { throw new NotSupportedException("UDF directory allocation padding is not zero."); }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int directories = 0;
            for (int cursor = 0; cursor < bytes;)
            {
                token.ThrowIfCancellationRequested();
                if (bytes - cursor < 38) { throw new ImageFormatException("Truncated UDF file identifier."); }
                ReadOnlySpan<byte> fid = contents.AsSpan(cursor, (int)bytes - cursor);
                int nameLength = fid[19];
                int record = checked((38 + nameLength + 3) & ~3);
                if (record > fid.Length || U16(fid, 16) != 1 || U16(fid, 36) != 0
                    || (fid[18] & ~11) != 0 || fid.Slice(38 + nameLength, record - 38 - nameLength).ContainsAnyExcept((byte)0))
                { throw new NotSupportedException("Unqualified UDF file identifier flags, version, implementation data, or bounds."); }
                Tag.Verify(fid[..record], 257, extent + cursor / SectorSize, record - 16);
                bool parent = (fid[18] & 8) != 0;
                bool childDirectory = (fid[18] & 2) != 0;
                int childBlock = ReadLongAddress(fid.Slice(20, 16), SectorSize, fileIdentifier: true);
                byte[] fileIdentifier = fid.Slice(16, 22).ToArray();
                if (parent)
                {
                    if (cursor != 0 || nameLength != 0 || fid[18] != 10 || childBlock != parentBlock)
                    { throw new ImageFormatException("Invalid UDF parent file identifier."); }
                }
                else
                {
                    if (cursor == 0) { throw new ImageFormatException("UDF parent file identifier missing."); }
                    string childName = DecodeName(fid.Slice(38, nameLength));
                    if (!IsoImage.NamesSupported(childName) || !names.Add(childName))
                    { throw new NotSupportedException("Unsafe or ambiguous UDF filename: " + childName); }
                    OpticalNode child = ReadNode(childBlock, block, childName, fileIdentifier, depth + 1);
                    if (child.Directory != childDirectory) { throw new ImageFormatException("UDF identifier and ICB file types disagree."); }
                    if (child.Directory) { directories++; }
                    node.Children.Add(child);
                }
                cursor += record;
            }
            if (U16(sector, 48) != directories + 1)
            { throw new NotSupportedException("Unqualified UDF directory link count."); }
            return node;
        }
    }
}

internal static class Tag
{
    internal static void Verify(ReadOnlySpan<byte> data, ushort id, int position, int expectedCrcLength)
    {
        if (data.Length < 16 || expectedCrcLength > data.Length - 16
            || U16(data, 0) != id || U16(data, 2) != 2 || data[5] != 0
            || U16(data, 6) != 0 || U32(data, 12) != position || U16(data, 10) != expectedCrcLength
            || data[4] != Checksum(data) || U16(data, 8) != Crc(data.Slice(16, expectedCrcLength))
            || data[(16 + expectedCrcLength)..].ContainsAnyExcept((byte)0))
        { throw new ImageFormatException($"Invalid UDF tag {id} at block {position}: bounds, location, checksum, CRC or padding."); }
    }

    internal static void Write(Span<byte> data, ushort id, int position, int crcLength)
    {
        data[..16].Clear();
        W16(data, 0, id);
        W16(data, 2, 2);
        W16(data, 8, Crc(data.Slice(16, crcLength)));
        W16(data, 10, checked((ushort)crcLength));
        W32(data, 12, checked((uint)position));
        data[4] = Checksum(data);
    }

    private static byte Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < 16; i++)
        {
            if (i != 4) { sum += data[i]; }
        }
        return unchecked((byte)sum);
    }

    private static ushort Crc(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        foreach (byte value in data)
        {
            crc ^= value << 8;
            for (int bit = 0; bit < 8; bit++)
            { crc = ((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0)) & 0xffff; }
        }
        return (ushort)crc;
    }
}

internal sealed class UdfOutput
{
    private readonly UdfImage source;
    private readonly OpticalNode root;
    private readonly int recognition;
    private readonly Dictionary<OpticalNode, Entry> entries = [];
    private sealed record Entry(int Icb, int DirectoryBytes, byte[] Header, byte[] Name)
    {
        internal int DataBlock { get; set; }
    }

    internal UdfOutput(UdfImage source, OpticalNode root, OpticalLayout layout, int recognition)
    {
        this.source = source;
        this.root = root;
        this.recognition = recognition;
        // Preserve old unique IDs; add only IDs above all source IDs, never reuse
        // IDs from deleted entries in this rewrite.
        ulong next = Math.Max(U64(source.Integrity, 40), 16UL);
        if (next > uint.MaxValue - MaximumEntries)
        { throw new NotSupportedException("UDF unique identifier counter has insufficient safe headroom."); }
        Allocate(root);

        void Allocate(OpticalNode node)
        {
            int directoryBytes = node.Directory ? checked(40 + node.Children.Sum(child => IdentifierSize(UdfImage.EncodeName(child.Name)))) : 0;
            if (directoryBytes > 16 * 1024 * 1024 || node.Length > 0x3fffffff)
            { throw new NotSupportedException("UDF single-allocation entry exceeds the qualified file/directory size."); }
            byte[] header = node.Udf?.FileEntry.ToArray() ?? NewHeader(node, next++);
            int links = node.Directory ? checked(1 + node.Children.Count(child => child.Directory)) : 1;
            W16(header, 32, checked((ushort)links));
            int icb = layout.Allocate(SectorSize);
            var entry = new Entry(icb, directoryBytes, header, node == root ? [] : UdfImage.EncodeName(node.Name));
            if (node.Directory) { entry.DataBlock = layout.Allocate(directoryBytes); }
            entries.Add(node, entry);
            foreach (OpticalNode child in node.Children) { Allocate(child); }
        }
    }

    internal void SetFile(OpticalNode node, int block) => entries[node].DataBlock = block;

    private static int IdentifierSize(byte[] name) => (38 + name.Length + 3) & ~3;

    private static byte[] NewHeader(OpticalNode node, ulong id)
    {
        byte[] header = new byte[160];
        W16(header, 4, 4);
        W16(header, 8, 1);
        header[11] = (byte)(node.Directory ? 4 : 5);
        W16(header, 18, 0x230);
        W32(header, 20, uint.MaxValue);
        W32(header, 24, uint.MaxValue);
        W32(header, 28, node.Directory ? 0x000014a5U : 0x00001084U);
        DateTime modified = node.Modified ?? throw new InvalidDataException("A UDF record must have a recording date.");
        for (int offset = 56; offset <= 80; offset += 12) { UdfImage.WriteTime(header.AsSpan(offset, 12), modified); }
        "*SectorWorks"u8.CopyTo(header.AsSpan(113));
        W64(header, 144, id);
        W32(header, 156, 8);
        return header;
    }

    internal void Write(Stream output, int end, CancellationToken token)
    {
        string[] sequence = ["BEA01", "NSR02", "TEA01"];
        for (int i = 0; i < sequence.Length; i++)
        {
            var vrs = new byte[SectorSize];
            Encoding.ASCII.GetBytes(sequence[i]).CopyTo(vrs, 1); vrs[6] = 1;
            WriteAt(output, recognition + i, vrs);
        }
        const int partition = UdfImage.PartitionStart;
        int partitionLength = end - 257 - partition;
        ushort[] ids = [1, 4, 5, 6, 7, 8];
        foreach ((int start, byte[][] templates) in new[] { (32, source.Volume), (48, source.ReserveVolume) })
        {
            for (int i = 0; i < ids.Length; i++)
            {
                byte[] copy = templates[i].ToArray();
                if (ids[i] == 5)
                {
                    W32(copy, 188, partition);
                    W32(copy, 192, (uint)partitionLength);
                }
                if (ids[i] == 6)
                {
                    W32(copy, 252, 0);
                    W32(copy, 436, 64);
                }
                Tag.Write(copy, ids[i], start + i, U16(templates[i], 10));
                WriteAt(output, start + i, copy);
            }
        }
        byte[] integrity = source.Integrity.ToArray();
        W32(integrity, 84, (uint)partitionLength);
        W32(integrity, 120, (uint)entries.Keys.Count(node => !node.Directory));
        W32(integrity, 124, (uint)entries.Keys.Count(node => node.Directory));
        W64(integrity, 40, Math.Max(U64(integrity, 40), entries.Values.Max(entry => U64(entry.Header, 144)) + 1));
        Tag.Write(integrity, 9, 64, 118);
        WriteAt(output, 64, integrity);
        WriteEnd(65, 65);
        foreach (int block in new[] { 256, end - 257, end - 1 })
        {
            var anchor = new byte[SectorSize];
            W32(anchor, 16, 32768); W32(anchor, 20, 32);
            W32(anchor, 24, 32768); W32(anchor, 28, 48);
            Tag.Write(anchor, 2, block, 496);
            WriteAt(output, block, anchor);
        }
        byte[] fsd = source.FileSet.ToArray();
        W32(fsd, 404, (uint)(entries[root].Icb - partition));
        Tag.Write(fsd, 256, 0, 496);
        WriteAt(output, partition, fsd);
        WriteEnd(partition + 1, 1);
        WriteNode(root, root);

        void WriteEnd(int block, int position)
        {
            var data = new byte[SectorSize];
            Tag.Write(data, 8, position, 496);
            WriteAt(output, block, data);
        }

        void WriteNode(OpticalNode node, OpticalNode parent)
        {
            token.ThrowIfCancellationRequested();
            Entry entry = entries[node];
            byte[] fileEntry = new byte[SectorSize];
            entry.Header.CopyTo(fileEntry, 16);
            long bytes = node.Directory ? entry.DirectoryBytes : node.Length;
            W64(fileEntry, 56, (ulong)bytes);
            W64(fileEntry, 64, (ulong)Blocks(bytes));
            W32(fileEntry, 172, 8);
            W32(fileEntry, 176, (uint)bytes);
            W32(fileEntry, 180, bytes == 0 ? 0 : (uint)(entry.DataBlock - partition));
            Tag.Write(fileEntry, 261, entry.Icb - partition, 168);
            WriteAt(output, entry.Icb, fileEntry);
            if (!node.Directory) { return; }
            byte[] contents = new byte[entry.DirectoryBytes];
            int cursor = 0;
            WriteIdentifier(parent, [], 10);
            foreach (OpticalNode child in node.Children.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                WriteIdentifier(child, entries[child].Name, (byte)((child.Directory ? 2 : 0) | (child.Hidden ? 1 : 0)));
            }
            WriteAt(output, entry.DataBlock, contents);
            foreach (OpticalNode child in node.Children) { WriteNode(child, node); }

            void WriteIdentifier(OpticalNode target, byte[] name, byte flags)
            {
                int size = IdentifierSize(name);
                Span<byte> fid = contents.AsSpan(cursor, size);
                W16(fid, 16, 1);
                fid[18] = flags; fid[19] = (byte)name.Length;
                W32(fid, 20, SectorSize);
                W32(fid, 24, (uint)(entries[target].Icb - partition));
                W32(fid, 32, checked((uint)U64(entries[target].Header, 144)));
                name.CopyTo(fid[38..]);
                Tag.Write(fid, 257, entry.DataBlock - partition + cursor / SectorSize, size - 16);
                cursor += size;
            }
        }
    }

    internal void Validate(UdfImage actual)
    {
        if (source.Label != actual.Label || !Except(source.FileSet, actual.FileSet, (404, 4))
            || !Except(source.Integrity, actual.Integrity, (40, 8), (84, 4), (120, 8)))
        { throw new InvalidDataException("Rebuilt UDF file-set or closed integrity metadata differs."); }
        ValidateSequence(source.Volume, actual.Volume);
        ValidateSequence(source.ReserveVolume, actual.ReserveVolume);

        static void ValidateSequence(byte[][] expected, byte[][] actual)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (!Except(expected[i], actual[i], i switch
                {
                    2 => [(188, 8)],
                    3 => [(252, 4), (436, 4)],
                    _ => []
                }))
                { throw new InvalidDataException("Rebuilt UDF volume descriptors differ."); }
            }
        }
    }

    internal void ValidateEntry(OpticalNode node, OpticalNode actual)
    {
        Entry entry = entries[node];
        if (actual.Udf is null || !Except(entry.Header, actual.Udf.FileEntry, (40, 16), (156, 4), skipTag: false)
            || !node.Directory && node.Length != 0 && actual.Offset != Offset(entry.DataBlock))
        { throw new InvalidDataException("Rebuilt UDF entry metadata, identity or data extent differs."); }
    }

    private static bool Except(byte[] a, byte[] b, (int Offset, int Length) x = default,
        (int Offset, int Length) y = default, (int Offset, int Length) z = default, bool skipTag = true) =>
        Except(a, b, [x, y, z], skipTag);

    private static bool Except(byte[] a, byte[] b, (int Offset, int Length)[] areas, bool skipTag = true)
    {
        if (a.Length != b.Length) { return false; }
        for (int i = skipTag ? 16 : 0; i < a.Length; i++)
        {
            if (a[i] != b[i] && !areas.Any(range => i >= range.Offset && i < range.Offset + range.Length)) { return false; }
        }
        return true;
    }
}
