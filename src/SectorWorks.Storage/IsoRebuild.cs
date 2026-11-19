using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.OpticalBinary;
using static SectorWorks.Storage.OpticalLayout;

namespace SectorWorks.Storage;

internal sealed class IsoEntry(byte[]? identifier, byte[] header, byte[] susp, byte[]? continuation = null)
{
    internal byte[]? Identifier { get; set; } = identifier;
    internal byte[] Header { get; } = header;
    internal byte[] Susp { get; } = susp;
    internal byte[]? Continuation { get; } = continuation;
    internal IsoEntry? Self { get; set; }
    internal IsoEntry? Parent { get; set; }
    internal IsoEntry Clone() => new(Identifier, Header, Susp, Continuation) { Self = Self, Parent = Parent };
}

internal sealed record IsoView(int DescriptorBlock, byte[] Descriptor, OpticalNode Root, bool Joliet,
    bool RockRidge, bool Level1, bool Xa, byte[][] DocumentIds);

internal sealed class IsoImage(IsoView[] views, int[] duplicatePrimaries, int terminator, IsoBootLayout? boot,
    int trailingBlocks, byte[]? systemStamp, byte[]? masteringStamp)
{
    private static readonly Encoding JolietEncoding = new UnicodeEncoding(true, false, true);
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal IsoView[] Views { get; } = views;
    internal int[] DuplicatePrimaries { get; } = duplicatePrimaries;
    internal int Terminator { get; } = terminator;
    internal IsoBootLayout? Boot { get; } = boot;
    internal int TrailingBlocks { get; } = trailingBlocks;
    internal byte[]? SystemStamp { get; } = systemStamp;
    internal byte[]? MasteringStamp { get; } = masteringStamp;
    internal int DescriptorEnd => Terminator + (MasteringStamp is null ? 1 : 2);
    internal OpticalNode Root => Views.Last().Root;
    internal string Label => Encoding.ASCII.GetString(Views[0].Descriptor, 40, 32).TrimEnd(' ');
    internal string ActiveNaming => Views.Length == 2 ? "Joliet" : Views[0].RockRidge ? "RockRidge" : "Iso9660";
    internal string Naming => string.Join(" + ", Views.Select(v => v.Joliet ? "Joliet" :
        v.RockRidge ? "ISO9660 + Rock Ridge" : v.Level1 ? "ISO9660 level 1 names" : "ISO9660 level 2 names"));
    internal IsoOutput Plan(OpticalNode root, OpticalLayout layout) => new(this, root, layout);

    internal static IsoImage Read(IReadOnlyBlockDevice device, bool bridge, bool standaloneCompatibility,
        OpticalCoverage coverage, CancellationToken token)
    {
        var descriptors = new List<(int Position, byte[] Bytes, bool Joliet)>();
        var duplicatePrimaries = new List<int>();
        bool allowStandaloneVariants = standaloneCompatibility && !bridge;
        int terminator = 0;
        int declaredBlocks = 0;
        ushort declaredSetSize = 0;
        int physicalBlocks = checked((int)(device.Length / SectorSize));
        (int Block, byte[] Descriptor)? bootDescriptor = null;
        for (int block = 16; block < 26; block++)
        {
            token.ThrowIfCancellationRequested();
            byte[] data = Sector(device, block);
            if (!data.AsSpan(1, 5).SequenceEqual("CD001"u8) || data[6] != 1)
            { throw new NotSupportedException("Unknown or misplaced ISO descriptor; reconstruction would discard metadata."); }
            coverage.Add(block, 1);
            if (data[0] == 255)
            {
                if (data.AsSpan(7).ContainsAnyExcept((byte)0)) { throw new NotSupportedException("Opaque ISO terminator data."); }
                terminator = block;
                break;
            }
            if (data[0] == 0 && !bridge && descriptors.Count == 1 && bootDescriptor is null
                && data.AsSpan(7, 23).SequenceEqual("EL TORITO SPECIFICATION"u8)
                && !data.AsSpan(30, 41).ContainsAnyExcept((byte)0)
                && !data.AsSpan(75).ContainsAnyExcept((byte)0))
            {
                bootDescriptor = (block, data);
                continue;
            }
            bool joliet = data[0] == 2 && data[88] == 0x25 && data[89] == 0x2f && data[90] is 0x40 or 0x43 or 0x45;
            if (allowStandaloneVariants && data[0] == 1 && descriptors.Count == 1 && bootDescriptor is null
                && block == descriptors[0].Position + duplicatePrimaries.Count + 1
                && data.AsSpan().SequenceEqual(descriptors[0].Bytes))
            {
                duplicatePrimaries.Add(block);
                continue;
            }
            if (descriptors.Count != (joliet ? 1 : 0) || !joliet && data[0] != 1)
            { throw new NotSupportedException("El Torito/boot, enhanced, multiple or unknown ISO descriptors are not qualified for rebuild."); }
            uint blocks = Both32(data, 80);
            ReadOnlySpan<byte> geometry = data.AsSpan(120, 12);
            // Some single-volume producers leave one or all of these mirrored
            // constants blank. Extents and directory references still require
            // matching endian copies throughout the image.
            bool omittedMirror = allowStandaloneVariants && (geometry.SequenceEqual("\x01\0\0\0\x01\0\0\0\0\x08\0\0"u8)
                || geometry.SequenceEqual("\x01\0\0\x01\x01\0\0\0\0\x08\x08\0"u8));
            ushort setSize = omittedMirror ? (ushort)1 : Both16(data, 120);
            if (setSize != 1 && !(allowStandaloneVariants && setSize == 0)
                || descriptors.Count != 0 && setSize != declaredSetSize
                || !omittedMirror && (Both16(data, 124) != 1 || Both16(data, 128) != SectorSize)
                || data[881] != 1)
            { throw new NotSupportedException("ISO volume set, block size or file structure version is not qualified for rebuild."); }
            if (blocks <= block || blocks > physicalBlocks || declaredBlocks != 0 && blocks != declaredBlocks
                || (bridge || !standaloneCompatibility) && blocks != physicalBlocks)
            { throw new NotSupportedException("ISO declared volume length is invalid or inconsistent across descriptors."); }
            if (!allowStandaloneVariants && data.AsSpan(702, 111).ContainsAnyExcept((byte)0, (byte)32))
            { throw new NotSupportedException("ISO copyright, abstract or bibliographic descriptor references are not qualified for rebuild."); }
            if (!HasQualifiedApplicationFields(data, allowStandaloneVariants))
            { throw new NotSupportedException("Opaque ISO application or reserved descriptor data is not qualified for rebuild."); }
            declaredBlocks = (int)blocks;
            declaredSetSize = setSize;
            descriptors.Add((block, data, joliet));
        }
        if (terminator == 0 || descriptors.Count == 0)
        { throw new ImageFormatException("Missing primary ISO descriptor or terminator."); }
        var views = new List<IsoView>();
        foreach ((int block, byte[] data, bool joliet) in descriptors)
        {
            bool xa = standaloneCompatibility && !bridge && data.AsSpan(1024, 8).SequenceEqual("CD-XA001"u8);
            var reader = new IsoTreeReader(device, coverage, token, joliet, block, standaloneCompatibility, allowStandaloneVariants, xa);
            OpticalNode root = reader.ReadRoot(data);
            byte[][] documentIds = allowStandaloneVariants ? ReadDocumentIds(data, root, block, joliet) : [];
            views.Add(new(block, data, root, joliet, reader.RockRidge, reader.Level1, reader.HasXaRecords, documentIds));
        }
        // Pair variant trees by exact extents, not by trusting lossy primary aliases.
        if (views.Count == 2)
        {
            OpticalNamespace.Match(views[1].Root, views[0].Root, (target, alternate) =>
            {
                if (target.Hidden != alternate.Hidden) { throw new NotSupportedException("ISO namespace visibility flags disagree."); }
                if (views[0].RockRidge && target.Name != alternate.Name)
                { throw new NotSupportedException("Rock Ridge and Joliet names disagree."); }
                target.Iso.Add(views[0].DescriptorBlock, alternate.Iso[views[0].DescriptorBlock]);
            }, token);
        }
        if (bridge && terminator > 21) { throw new NotSupportedException("Bridge recognition sequence exceeds the reserved descriptor area."); }
        IsoBootLayout? boot = bootDescriptor is { } bootData
            ? IsoBootLayout.Read(device, bootData.Block, bootData.Descriptor, views.Last().Root, token)
            : null;
        byte[]? stamp = allowStandaloneVariants && boot is null ? ReadSystemStamp(device) : null;
        if (stamp is not null) { coverage.AddSystemStamp(); }
        byte[]? mastering = allowStandaloneVariants ? ReadMasteringStamp(device, terminator + 1, views[0]) : null;
        if (mastering is not null) { coverage.Add(terminator + 1, 1); }
        coverage.RequireCoveredWithin(declaredBlocks);
        return new(views.ToArray(), duplicatePrimaries.ToArray(), terminator, boot, physicalBlocks - declaredBlocks, stamp, mastering);
    }

    private static byte[]? ReadMasteringStamp(IReadOnlyBlockDevice device, int block, IsoView primary)
    {
        if (Offset(block + 1) > device.Length) { return null; }
        byte[] sector;
        try { sector = Sector(device, block); }
        catch (IOException) { return null; } // Leave unreadable sectors to the coverage gate.
        if (!sector.AsSpan().StartsWith("MKI "u8) || sector[28] != '\n') { return null; }
        ReadOnlySpan<byte> text = sector.AsSpan(0, 2045).TrimEnd((byte)0);
        if (text.Length < 30 || text[..28].ContainsAnyExceptInRange((byte)' ', (byte)'~')
            || text[29..].ContainsAnyExceptInRange((byte)' ', (byte)'~'))
        { return null; }
        int fingerprint = MasteringFingerprint(sector);
        if (fingerprint == 0) { return sector; }
        if (fingerprint != PrimaryFingerprint(primary.Descriptor) || primary.RockRidge) { return null; }

        // A nonzero fingerprint tells consumers that recorded inode/link
        // information is coherent. Only retain it for ordinary unshared files.
        var occupied = new HashSet<long>();
        var directories = new Stack<OpticalNode>();
        directories.Push(primary.Root);
        while (directories.TryPop(out OpticalNode? directory))
        {
            foreach (OpticalNode child in directory.Children)
            {
                if (child.Directory) { directories.Push(child); }
                else if (child.Length != 0 && !occupied.Add(child.Offset)) { return null; }
            }
        }
        return sector;
    }

    internal static int MasteringFingerprint(byte[] sector) => (sector[2045] << 16) | (sector[2046] << 8) | sector[2047];
    internal static int PrimaryFingerprint(byte[] descriptor) => descriptor.Sum(b => (int)b);

    private static byte[]? ReadSystemStamp(IReadOnlyBlockDevice device)
    {
        byte[] area = new byte[16 * SectorSize];
        Bounds.ReadExactly(device, 0, area);
        if (area.AsSpan(0, SectorSize).ContainsAnyExcept((byte)0)) { return null; }
        ReadOnlySpan<byte> first = area.AsSpan(SectorSize, SectorSize);
        if (first.TrimEnd((byte)0).SequenceEqual("The Personal RomMaker (PC RomMaker V1.53A)\n"u8)
            || first.TrimEnd((byte)0).SequenceEqual("The Personal RomMaker (Windows V3.01)"u8)
            || first.TrimEnd((byte)0).SequenceEqual("The Personal RomMaker (32-bit Windows 95 V4.20)"u8))
        {
            for (int block = 2; block < 16; block++)
            {
                if (!area.AsSpan(block * SectorSize, SectorSize).SequenceEqual(first)) { return null; }
            }
            return area;
        }
        return !area.AsSpan(SectorSize, 14 * SectorSize).ContainsAnyExcept((byte)0)
            && area.AsSpan(15 * SectorSize).TrimEnd((byte)0)
                .SequenceEqual("Produced by Meridian Data, Inc. Personal Scribe v1.2a  copyright 1991-1993"u8)
            ? area : null;
    }

    private static byte[][] ReadDocumentIds(byte[] descriptor, OpticalNode root, int block, bool joliet)
    {
        List<byte[]> documents = [];
        ReadOnlySpan<byte> publisher = TrimDescriptorText(descriptor.AsSpan(318, 128), joliet);
        ReadOnlySpan<byte> version = joliet ? "\0;\0\u0031"u8 : ";1"u8;
        for (int offset = 702; offset <= 776; offset += 37)
        {
            ReadOnlySpan<byte> field = descriptor.AsSpan(offset, 37);
            if (!field.ContainsAnyExcept((byte)0, (byte)32)) { continue; }
            ReadOnlySpan<byte> value = TrimDescriptorText(field, joliet);
            byte[]? matched = null;
            foreach (OpticalNode child in root.Children)
            {
                if (child.Directory || child.Iso[block].Identifier is not { } id) { continue; }
                if (!value.SequenceEqual(id) && !(id.AsSpan().EndsWith(version) && value.SequenceEqual(id.AsSpan()[..^version.Length])))
                { continue; }
                if (matched is not null)
                { throw new NotSupportedException("Ambiguous ISO copyright, abstract or bibliographic descriptor reference."); }
                matched = id;
            }
            if (matched is not null) { documents.Add(matched); }
            else if (!IsInlineDocumentText(value, publisher, joliet, offset == 702))
            { throw new NotSupportedException("ISO copyright, abstract or bibliographic descriptor reference does not resolve to a root file."); }
        }
        return documents.ToArray();
    }

    private static ReadOnlySpan<byte> TrimDescriptorText(ReadOnlySpan<byte> field, bool joliet)
    {
        if (!joliet)
        {
            while (!field.IsEmpty && field[^1] is 0 or 32) { field = field[..^1]; }
            return field;
        }
        // The text field is 37 bytes; the last odd byte is justification padding.
        if ((field.Length & 1) != 0 && field[^1] is 0 or 32) { field = field[..^1]; }
        while (field.EndsWith("\0 "u8) || field.EndsWith("\0\0"u8)) { field = field[..^2]; }
        return field;
    }

    private static bool IsInlineDocumentText(ReadOnlySpan<byte> value, ReadOnlySpan<byte> publisher, bool joliet, bool copyright)
    {
        if (value.IsEmpty || joliet && (value.Length & 1) != 0) { return false; }
        string text = (joliet ? JolietEncoding : Encoding.Latin1).GetString(value);
        if (text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not (' ' or ',' or '.' or '(' or ')' or '&' or '-')))
        { return false; }

        // Old authoring programs wrote the publisher itself here, truncating
        // Joliet to the field width. A matching ordinary root file takes priority.
        if (value.SequenceEqual(publisher) || joliet && value.Length == 36 && publisher.StartsWith(value)) { return true; }
        if (!copyright) { return false; }
        if (text.StartsWith("(c) ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Copr. (c) ", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith(", All Rights Reserved", StringComparison.OrdinalIgnoreCase))
        { return true; }
        // A plain multiword copyright label is not an ISO primary file identifier.
        if (!joliet && text.Contains(' ') && text.Split(' ').All(word => word.Length != 0 && word.All(char.IsAsciiLetter)))
        { return true; }
        return text.Length == 4 && int.TryParse(text, out int year) && year is >= 1900 and <= 2099;
    }

    private static bool HasQualifiedApplicationFields(byte[] descriptor, bool allowVersionStamps)
    {
        ReadOnlySpan<byte> application = descriptor.AsSpan(883, 512);
        ReadOnlySpan<byte> reserved = descriptor.AsSpan(1395);
        bool reservedBlank = !reserved.ContainsAnyExcept((byte)0);
        if (!application.ContainsAnyExcept((byte)0, (byte)32))
        { return reservedBlank || allowVersionStamps && IsPersonalDiscScribeStamp(reserved); }
        if (!allowVersionStamps) { return false; }
        // A label-only CD-XA marker has no addresses. Record-level XA data
        // still has to pass the directory system-use checks independently.
        if (reservedBlank && application.Slice(141, 8).SequenceEqual("CD-XA001"u8)
            && !application[..141].ContainsAnyExcept((byte)0, (byte)32)
            && !application[149..].ContainsAnyExcept((byte)0, (byte)32))
        { return true; }

        // Only fixed-position text stamps with the observed version syntax are
        // understood. Do not treat an arbitrary printable field as layout-free.
        ReadOnlySpan<byte> xcd = application[^12..];
        if (!application[..^12].ContainsAnyExcept((byte)0, (byte)32) && xcd.StartsWith("XCD"u8)
            && Digits(xcd[3..6]) && xcd[6] == '.' && Digits(xcd[7..8]))
        {
            return (xcd[8] == '.' && Digits(xcd[9..]) && reservedBlank)
                || (xcd.Slice(8, 2).SequenceEqual(" ("u8) && Digits(xcd[10..]) && Digits(reserved[..1])
                    && reserved[1] == ')' && !reserved[2..].ContainsAnyExcept((byte)0));
        }
        return reservedBlank && application.StartsWith("CD Rep "u8) && Digits(application[7..8]) && application[8] == '.'
            && Digits(application[9..10]) && application[10] == '.' && Digits(application[11..14])
            && !application[14..].ContainsAnyExcept((byte)0, (byte)32);

        static bool Digits(ReadOnlySpan<byte> value) => !value.ContainsAnyExceptInRange((byte)'0', (byte)'9');
    }

    private static bool IsPersonalDiscScribeStamp(ReadOnlySpan<byte> reserved)
    {
        // The observed version stamp has a fixed decimal code and a label at
        // the start of the reserved area. No binary suffix is admitted.
        ReadOnlySpan<byte> prefix = "PDSC IS 2.0"u8;
        if (!reserved.StartsWith(prefix)) { return false; }
        int position = prefix.Length;
        if (reserved[position] == '3') { position++; }
        ReadOnlySpan<byte> text = reserved[position..].TrimEnd((byte)0).TrimEnd((byte)' ');
        if (text.Length is < 11 or > 80 || !text.StartsWith("IS"u8)
            || text.Slice(2, 8).ContainsAnyExceptInRange((byte)'0', (byte)'9'))
        { return false; }
        foreach (byte character in text[10..])
        {
            if (character is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'
                or >= (byte)'0' and <= (byte)'9' or (byte)' ' or (byte)'.' or (byte)',' or (byte)'-' or (byte)'&'))
            { return false; }
        }
        return true;
    }

    private sealed class IsoTreeReader(IReadOnlyBlockDevice device, OpticalCoverage coverage, CancellationToken token,
        bool joliet, int view, bool compatibility, bool allowStandaloneVariants, bool xa)
    {
        private const long MetadataBudget = 32L * 1024 * 1024;
        private readonly HashSet<int> visited = [];
        private readonly Dictionary<int, (byte[] Id, int Parent)> directories = [];
        private int entries;
        private long remaining = MetadataBudget;
        internal bool RockRidge { get; private set; }
        internal bool Level1 { get; private set; } = true;
        internal bool HasXaRecords { get; private set; }

        internal OpticalNode ReadRoot(byte[] descriptor)
        {
            ReadOnlySpan<byte> rootRecord = descriptor.AsSpan(156, 34);
            int block = checked((int)Both32(rootRecord, 2));
            int bytes = checked((int)Both32(rootRecord, 10));
            bool xaRootLength = xa && rootRecord[0] == 48 && !descriptor.AsSpan(190, 14).ContainsAnyExcept((byte)0, (byte)32);
            bool rootId = rootRecord[32] == 1 && rootRecord[33] == 0
                || allowStandaloneVariants && rootRecord[0] == 34
                    && (rootRecord[32] == 1 && rootRecord[33] == 1 || rootRecord[32] == 0 && rootRecord[33] == 0);
            if (rootRecord[0] != 34 && !xaRootLength || rootRecord[25] != 2 || !rootId
                || rootRecord[1] != 0 || rootRecord[26] != 0 || rootRecord[27] != 0 || Both16(rootRecord, 28) != 1)
            { throw new ImageFormatException("Invalid root descriptor record."); }
            OpticalNode root = ReadDirectory(block, bytes, "", [], block, bytes, 0);
            if (RockRidge) { ValidatePosixGraph(root, root); }
            int tableSize = checked((int)Both32(descriptor, 132));
            if (tableSize < 10 || tableSize > 2 * 1024 * 1024)
            { throw new NotSupportedException("ISO path table is missing or exceeds the qualified bound."); }
            for (int i = 0; i < 4; i++)
            {
                bool big = i >= 2;
                int offset = 140 + i * 4;
                int table = checked((int)(big ? BinaryPrimitives.ReadUInt32BigEndian(descriptor.AsSpan(offset)) : U32(descriptor, offset)));
                if (table == 0 && i is 1 or 3) { continue; }
                ValidateTable(table, tableSize, big, block);
            }
            return root;
        }

        private void ValidatePosixGraph(OpticalNode node, OpticalNode parent)
        {
            token.ThrowIfCancellationRequested();
            IsoEntry entry = node.Iso[view];
            if (node != parent) { VerifyPx(entry, node); }
            if (!node.Directory) { return; }
            VerifyPx(entry.Self!, node);
            VerifyPx(entry.Parent!, parent);
            foreach (OpticalNode child in node.Children) { ValidatePosixGraph(child, node); }
        }

        private static void VerifyPx(IsoEntry entry, OpticalNode node)
        {
            ReadOnlySpan<byte> px = FindSystemUse(entry.Susp, "PX"u8);
            uint type = node.Directory ? 0x4000U : 0x8000U;
            uint links = node.Directory ? 2U + (uint)node.Children.Count(n => n.Directory) : 1U;
            if (px.IsEmpty || (Both32(px, 4) & 0xf000) != type || Both32(px, 12) != links)
            { throw new NotSupportedException("Rock Ridge POSIX file type, identity or link count is not qualified."); }
        }

        private void ValidateTable(int block, int size, bool big, int root)
        {
            coverage.Add(block, Blocks(size));
            byte[] data = Bounds.Read(device, Offset(block), Blocks(size) * SectorSize);
            if (data.AsSpan(size).ContainsAnyExcept((byte)0))
            { throw new NotSupportedException("Nonzero ISO path-table padding is not qualified."); }
            var listed = new List<int>();
            var seen = new HashSet<int>();
            for (int offset = 0; offset < size;)
            {
                token.ThrowIfCancellationRequested();
                int length = data[offset];
                if (allowStandaloneVariants && length == 0 && listed.Count == directories.Count
                    && !data.AsSpan(offset, size - offset).ContainsAnyExcept((byte)0))
                { break; }
                int record = 8 + length + (length & 1);
                if (offset > size - record || length == 0 || data[offset + 1] != 0)
                { throw new ImageFormatException("Invalid ISO path table record."); }
                int lba = checked((int)(big ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 2))
                    : U32(data, offset + 2)));
                int parent = big ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 6)) : U16(data, offset + 6);
                if (!directories.TryGetValue(lba, out var expected) || !seen.Add(lba)
                    || !data.AsSpan(offset + 8, length).SequenceEqual(expected.Id)
                    || (listed.Count == 0 ? lba != root || parent != 1
                        : parent < 1 || parent > listed.Count || listed[parent - 1] != expected.Parent)
                    || (length & 1) != 0 && data[offset + record - 1] != 0)
                { throw new ImageFormatException("ISO path table disagrees with directory records."); }
                listed.Add(lba);
                offset += record;
            }
            if (listed.Count != directories.Count) { throw new ImageFormatException("ISO path table omits directories."); }
        }

        private OpticalNode ReadDirectory(int block, int bytes, string name, byte[] id, int parent,
            int parentBytes, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > MaximumDepth || !visited.Add(block) || bytes is < 34 or > 16 * 1024 * 1024
                || !compatibility && bytes % SectorSize != 0)
            { throw new NotSupportedException("ISO directory graph/size exceeds the qualified bounds."); }
            int blocks = Blocks(bytes);
            int allocatedBytes = blocks * SectorSize;
            if ((remaining -= allocatedBytes) < 0)
            { throw new NotSupportedException("ISO directory graph/size exceeds the qualified bounds."); }
            coverage.Add(block, blocks);
            directories.Add(block, (depth == 0 ? [0] : id, parent));
            byte[] data = Bounds.Read(device, Offset(block), allocatedBytes);
            if (data.AsSpan(bytes).ContainsAnyExcept((byte)0))
            { throw new NotSupportedException("Nonzero bytes follow the declared ISO directory length."); }
            OpticalNode? result = null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            int special = 0;
            for (int position = 0; position < bytes;)
            {
                token.ThrowIfCancellationRequested();
                int length = data[position];
                if (length == 0)
                {
                    int padding = SectorSize - position % SectorSize;
                    if (data.AsSpan(position, padding).ContainsAnyExcept((byte)0))
                    { throw new NotSupportedException("Unrecognized data in ISO directory padding."); }
                    position += padding;
                    continue;
                }
                if (length < 34 || length > bytes - position || position % SectorSize + length > SectorSize || ++entries > MaximumEntries)
                { throw new ImageFormatException("Invalid ISO directory record or entry budget exceeded."); }
                ReadOnlySpan<byte> r = data.AsSpan(position, length);
                position += length;
                int n = r[32];
                int area = 33 + n + (n % 2 == 0 ? 1 : 0);
                if (n == 0 || area > length || (n % 2 == 0 && r[33 + n] != 0) || r[1] != 0
                    || (r[25] & ~3) != 0 || r[26] != 0 || r[27] != 0 || Both16(r, 28) != 1)
                { throw new NotSupportedException("Unqualified ISO flags, extended attributes, interleaving or multiextent data."); }
                byte[] identifier = r.Slice(33, n).ToArray();
                int location = checked((int)Both32(r, 2));
                long size = Both32(r, 10);
                bool directory = (r[25] & 2) != 0;
                bool pseudo = n == 1 && identifier[0] <= 1;
                DateTime? modified = Date(r[18..25], allowStandaloneVariants, pseudo && directory);
                byte[] susp = r[area..].ToArray();
                string? alternate = ReadSusp(susp, depth == 0 && special == 0 && pseudo, directory, out byte[]? continuation);
                var record = new IsoEntry(identifier, r[..33].ToArray(), susp, continuation);
                if (pseudo)
                {
                    // Some producers store one block in the redundant parent
                    // length. The graph, parent extent and path tables still
                    // have to agree, and rebuild writes the full parent size.
                    bool omittedParentLength = allowStandaloneVariants && !RockRidge && special == 1
                        && depth > 0 && size == SectorSize && parentBytes > SectorSize;
                    if (!directory || identifier[0] != special || special > 1
                        || location != (special == 0 ? block : parent)
                        || size != (special == 0 ? bytes : parentBytes) && !omittedParentLength)
                    { throw new ImageFormatException("Invalid ISO current/parent directory record."); }
                    if (special++ == 0)
                    {
                        result = new(name, true, modified) { Hidden = (r[25] & 1) != 0 };
                        result.Iso.Add(view, new IsoEntry(id, record.Header, []) { Self = record });
                    }
                    else { result!.Iso[view].Parent = record; }
                    continue;
                }
                if (special != 2 || result is null) { throw new ImageFormatException("Missing ISO current/parent directory records."); }
                string primary = DecodeIdentifier(identifier, directory, joliet);
                if (!joliet)
                {
                    bool level1 = IsPrimaryName(primary, directory, true);
                    if (!level1 && !IsPrimaryName(primary, directory, false)
                        && !(compatibility && IsExistingPrimaryName(primary)))
                    { throw new NotSupportedException("Nonstandard ISO9660 identifier is not qualified for rebuild: " + primary); }
                    Level1 &= level1;
                    if (depth >= 8 && directory) { throw new NotSupportedException("ISO9660 directory depth exceeds the qualified hierarchy."); }
                }
                string childName = alternate ?? primary;
                if (!NamesSupported(childName) || !names.Add(childName) || !identifiers.Add(Convert.ToHexString(identifier)))
                { throw new NotSupportedException("Unsafe or ambiguous ISO namespace identifier: " + childName); }
                OpticalNode child;
                if (directory)
                {
                    child = ReadDirectory(location, checked((int)size), childName, identifier, block, bytes, depth + 1);
                    IsoEntry prior = child.Iso[view];
                    record.Self = prior.Self;
                    record.Parent = prior.Parent;
                    if (child.Hidden != ((r[25] & 1) != 0))
                    {
                        if (!allowStandaloneVariants)
                        { throw new NotSupportedException("ISO directory visibility flags disagree."); }
                        // Use the parent listing's visibility in the browser.
                        // Rebuild still retains each record's own raw flags.
                        child.Hidden = (r[25] & 1) != 0;
                    }
                }
                else
                {
                    long start = Offset(location);
                    if (size != 0) { coverage.AddFile(location, Blocks(size), size); }
                    child = new(childName, false, modified, size)
                    {
                        Hidden = (r[25] & 1) != 0,
                        Offset = start,
                        Content = () => OpticalFileSystem.OpenExtent(device, start, size)
                    };
                }
                child.Iso[view] = record;
                child.Modified = modified;
                result.Children.Add(child);
            }
            if (special != 2 || result is null) { throw new ImageFormatException("Incomplete ISO directory."); }
            return result;
        }

        private string? ReadSusp(byte[] data, bool root, bool directory, out byte[]? continuation)
        {
            continuation = null;
            if (data.Length == 0) { return null; }
            if (allowStandaloneVariants && !RockRidge)
            {
                if (!data.AsSpan().ContainsAnyExcept((byte)0) || IsAppleFinderInfo(data)) { return null; }
                if (xa && data.Length >= 28 && data.AsSpan(6, 2).SequenceEqual("XA"u8) && IsAppleFinderInfo(data.AsSpan(14)))
                {
                    CheckXaRecord(data.AsSpan(0, 14), directory);
                    return null;
                }
            }
            if (xa && data.Length >= 14 && data.AsSpan(data.Length - 8, 2).SequenceEqual("XA"u8))
            {
                if (RockRidge || data.AsSpan(0, data.Length - 14).ContainsAnyExcept((byte)0))
                { throw new NotSupportedException("Combined XA and other directory system-use metadata is not qualified for rebuild."); }
                CheckXaRecord(data.AsSpan(data.Length - 14), directory);
                return null;
            }
            if (joliet) { throw new NotSupportedException("Unknown Joliet system-use metadata."); }
            string? name = null;
            bool ended = false;
            for (int position = 0; position < data.Length;)
            {
                if (!data.AsSpan(position).ContainsAnyExcept((byte)0)) { break; }
                if (ended || data.Length - position < 4 || data[position + 2] < 4
                    || data[position + 2] > data.Length - position || data[position + 3] != 1)
                { throw new ImageFormatException("Invalid SUSP entry in rebuild namespace."); }
                ReadOnlySpan<byte> record = data.AsSpan(position, data[position + 2]);
                position += record.Length;
                switch (Encoding.ASCII.GetString(record[..2]))
                {
                    case "SP" when root && !RockRidge && record.Length == 7 && record[4] == 0xbe && record[5] == 0xef && record[6] == 0:
                        RockRidge = true;
                        break;
                    case "RR" when RockRidge && record.Length == 5 && (record[4] & ~0x89) == 0:
                        break;
                    case "PX" when RockRidge && record.Length == 36:
                        uint mode = Both32(record, 4);
                        if ((mode & 0xf000) is not (0x4000 or 0x8000))
                        { throw new NotSupportedException("Rock Ridge links and special entries are read-only."); }
                        _ = Both32(record, 12);
                        _ = Both32(record, 20);
                        _ = Both32(record, 28);
                        break;
                    case "TF" when RockRidge && record.Length >= 5:
                        int stride = (record[4] & 0x80) == 0 ? 7 : 17;
                        if (record.Length != 5 + stride * System.Numerics.BitOperations.PopCount((uint)(record[4] & 0x7f)))
                        { throw new ImageFormatException("Rock Ridge timestamp entry has invalid length."); }
                        break;
                    case "NM" when RockRidge && record.Length > 5 && record[4] == 0 && name is null:
                        name = Utf8.GetString(record[5..]);
                        break;
                    case "CE" when RockRidge && root && continuation is null && record.Length == 28:
                        int lba = checked((int)Both32(record, 4));
                        int offset = checked((int)Both32(record, 12));
                        int length = checked((int)Both32(record, 20));
                        if (offset != 0 || length is < 8 or > SectorSize)
                        { throw new NotSupportedException("Rock Ridge continuation is not a bounded root extension reference."); }
                        coverage.Add(lba, 1);
                        byte[] block = Sector(device, lba);
                        continuation = block[..length];
                        if (block.AsSpan(length).ContainsAnyExcept((byte)0)
                            || !continuation.AsSpan(0, 2).SequenceEqual("ER"u8) || continuation[2] != length
                            || continuation[3] != 1 || continuation[4] + continuation[5] + continuation[6] != length - 8
                            || continuation[4] != 10 || !continuation.AsSpan(8, 10).SequenceEqual("RRIP_1991A"u8))
                        { throw new NotSupportedException("Unknown Rock Ridge continuation extension."); }
                        break;
                    case "ST" when RockRidge && record.Length == 4:
                        ended = true;
                        break;
                    default:
                        throw new NotSupportedException("Unknown or unqualified ISO SUSP/Rock Ridge metadata: " + Encoding.ASCII.GetString(record[..2]));
                }
            }
            if (root && RockRidge && continuation is null)
            { throw new NotSupportedException("Rock Ridge root extension identity was not verified."); }
            return name;
        }

        // Apple v2 Finder type, creator and flags are inline bytes. Associated
        // resource-fork records are still refused by the directory flags check.
        private static bool IsAppleFinderInfo(ReadOnlySpan<byte> data) => data.Length >= 14
            && data.StartsWith("AA\x0e\x02"u8) && !data[14..].ContainsAnyExcept((byte)0);

        private void CheckXaRecord(ReadOnlySpan<byte> record, bool directory)
        {
            int attributes = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            int form = directory ? 0x8800 : 0x0800;
            if (record[..4].ContainsAnyExcept((byte)0) || (attributes & ~0x0555) != form
                || record[8..].ContainsAnyExcept((byte)0))
            { throw new NotSupportedException("XA directory record contains nonordinary attributes, file number or reserved data."); }
            HasXaRecords = true;
        }
    }

    private static DateTime? Date(ReadOnlySpan<byte> data, bool allowUnknown, bool pseudoDirectory)
    {
        // Some mastering programs leave all six calendar components empty but
        // still set a timezone. The original record bytes are preserved.
        if (allowUnknown && unchecked((sbyte)data[6]) is >= -48 and <= 52
            && (!data[1..6].ContainsAnyExcept((byte)0)
                || pseudoDirectory && data[1] == 0 && data[2] is >= 1 and <= 31
                    && data[3] < 24 && data[4] < 60 && data[5] < 60))
        { return null; }
        try
        {
            return new DateTime(1900 + data[0], data[1], data[2], data[3], data[4], data[5], DateTimeKind.Utc)
                .AddMinutes(-15 * unchecked((sbyte)data[6]));
        }
        catch (ArgumentOutOfRangeException error) { throw new ImageFormatException("Invalid optical recording date.", error); }
    }

    internal static int FindSystemUseOffset(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        for (int offset = 0; offset + 4 <= data.Length && data[offset + 2] >= 4; offset += data[offset + 2])
        {
            if (data.Slice(offset, 2).SequenceEqual(signature)) { return offset; }
        }
        return -1;
    }

    private static ReadOnlySpan<byte> FindSystemUse(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        int index = FindSystemUseOffset(data, signature);
        return index < 0 ? [] : data.Slice(index, data[index + 2]);
    }

    internal static bool NamesSupported(string name) => ImageEditNames.IsSupportedName(name) && !name.Contains(';');

    internal static string DecodeIdentifier(byte[] bytes, bool directory, bool joliet)
    {
        if (!joliet && bytes.Any(b => b > 127)) { throw new NotSupportedException("Non-ASCII ISO primary identifier."); }
        string name = (joliet ? JolietEncoding : Encoding.ASCII).GetString(bytes);
        if (!directory && name.EndsWith(";1", StringComparison.Ordinal)) { name = name[..^2]; }
        if (!joliet && !directory && name.EndsWith('.')) { name = name[..^1]; }
        return name;
    }

    internal static bool IsPrimaryName(string name, bool directory, bool level1)
    {
        string[] pieces = name.Split('.');
        return name.Length > 0 && pieces.Length <= (directory ? 1 : 2)
            && pieces.All(piece => piece.Length > 0 && piece.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            && (level1 ? pieces[0].Length <= 8 && (pieces.Length == 1 || pieces[1].Length <= 3) : name.Length <= 30);
    }

    // DecodeIdentifier has already checked the original bytes are ASCII.
    // Keep safe existing aliases; author new identifiers by the normal rules.
    private static bool IsExistingPrimaryName(string name) => name.Length <= 64 && NamesSupported(name);

    internal static byte[] EncodeIdentifier(string name, bool directory, bool joliet) =>
        (joliet ? JolietEncoding : Encoding.ASCII).GetBytes(directory || joliet ? name : name + ";1");

    internal static void WriteDate(Span<byte> copy, DateTime? modified)
    {
        if (modified is not { } date)
        {
            copy.Clear();
            return;
        }
        copy[0] = checked((byte)(date.Year - 1900));
        copy[1] = (byte)date.Month; copy[2] = (byte)date.Day; copy[3] = (byte)date.Hour;
        copy[4] = (byte)date.Minute; copy[5] = (byte)date.Second; copy[6] = 0;
    }
}

internal static class OpticalNamespace
{
    internal static void Match(OpticalNode root, OpticalNode other, Action<OpticalNode, OpticalNode> match, CancellationToken token)
    {
        var signatures = new Dictionary<OpticalNode, string>();
        _ = Signature(root);
        _ = Signature(other);
        Pair(root, other);

        string Signature(OpticalNode node)
        {
            token.ThrowIfCancellationRequested();
            string key = !node.Directory ? node.Length == 0 ? "Empty" : $"{node.Offset}:{node.Length}"
                : Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('|',
                    node.Children.Select(Signature).Order(StringComparer.Ordinal)))));
            signatures.Add(node, key);
            return key;
        }
        void Pair(OpticalNode left, OpticalNode right)
        {
            token.ThrowIfCancellationRequested();
            if (left.Directory != right.Directory || signatures[left] != signatures[right]
                || left.Children.Count != right.Children.Count)
            { throw new NotSupportedException("Optical namespaces do not describe the same bounded content tree and payload extents."); }
            match(left, right);
            var byName = right.Children.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
            var remaining = new HashSet<OpticalNode>(right.Children);
            var unmatched = new List<OpticalNode>();
            foreach (OpticalNode child in left.Children)
            {
                if (byName.TryGetValue(child.Name, out OpticalNode? candidate) && signatures[child] == signatures[candidate]
                    && candidate.Directory == child.Directory && remaining.Remove(candidate))
                { Pair(child, candidate); }
                else { unmatched.Add(child); }
            }
            var groups = remaining.ToLookup(node => (node.Directory, signatures[node]));
            foreach (var group in unmatched.GroupBy(node => (node.Directory, signatures[node])))
            {
                var candidates = groups[group.Key].ToArray();
                if (candidates.Length != 1 || group.Count() != 1)
                { throw new NotSupportedException("Optical namespace aliases are ambiguous (for example different empty names or shared extents)."); }
                Pair(group.Single(), candidates[0]);
            }
        }
    }
}

internal sealed class OpticalCoverage(IReadOnlyBlockDevice device)
{
    private readonly List<(int Start, int End, long FileBytes)> spans = [];
    internal void AddSystemStamp() => spans.Add((0, 16, -1));
    internal void Add(int start, int blocks) => AddRange(start, blocks, -1);
    internal void AddFile(int start, int blocks, long length) => AddRange(start, blocks, length);
    internal void RequireCoveredWithin(int declaredBlocks)
    {
        if (spans.Any(span => span.End > declaredBlocks))
        { throw new ImageFormatException("An ISO extent crosses its declared volume length."); }
    }
    private void AddRange(int start, int blocks, long fileBytes)
    {
        if (start < 16 || blocks <= 0 || Offset(checked(start + blocks)) > device.Length)
        { throw new ImageFormatException("Optical extent crosses the bounded medium."); }
        spans.Add((start, start + blocks, fileBytes));
    }

    internal void Validate(CancellationToken token)
    {
        // No nonzero opaque system area, hidden appended sessions or untracked content.
        // Bound inspection of unused sectors; a large opaque/gapped layout stays read-only.
        int previous = 0;
        (int Start, int End, long FileBytes) last = default;
        long budget = 64L * 1024 * 1024;
        byte[] buffer = new byte[65536];
        int end = checked((int)(device.Length / SectorSize));
        foreach (var span in spans.OrderBy(s => s.Start).ThenBy(s => s.End)
            .Append((Start: end, End: end, FileBytes: -1L)))
        {
            token.ThrowIfCancellationRequested();
            if (span.Start < previous)
            {
                if (span != last || span.FileBytes <= 0)
                { throw new NotSupportedException("Optical metadata or different payload allocations overlap."); }
                continue;
            }
            RequireZero(Offset(previous), Offset(span.Start) - Offset(previous), previous == 0 ? "system area"
                : span.Start == end ? "trailing media" : "gap");
            if (span.FileBytes > 0)
            {
                long allocated = Offset(span.End - span.Start);
                RequireZero(Offset(span.Start) + span.FileBytes, allocated - span.FileBytes, "file padding", false);
            }
            previous = span.End;
            last = span;
        }

        void RequireZero(long start, long bytes, string region, bool countBudget = true)
        {
            if (countBudget && (budget -= bytes) < 0) { throw new NotSupportedException("Optical unallocated-area inspection exceeds the rebuild budget."); }
            while (bytes > 0)
            {
                token.ThrowIfCancellationRequested();
                int take = (int)Math.Min(bytes, buffer.Length);
                Bounds.ReadExactly(device, start, buffer.AsSpan(0, take));
                int first = buffer.AsSpan(0, take).IndexOfAnyExcept((byte)0);
                if (first >= 0)
                {
                    long offset = start + first;
                    throw new NotSupportedException($"Nonzero untracked {region} at ISO block {offset / SectorSize}, byte {offset % SectorSize}; rebuild would discard it.");
                }
                start += take;
                bytes -= take;
            }
        }
    }
}

internal sealed class IsoOutput
{
    private readonly IsoImage source;
    private readonly List<ViewOutput> views = [];

    internal IsoOutput(IsoImage source, OpticalNode root, OpticalLayout layout)
    {
        this.source = source;
        foreach (IsoView view in source.Views)
        {
            views.Add(new(view, root, layout, source.Views.Length > 1 || view.RockRidge));
        }
    }

    internal void SetFile(OpticalNode node, int block)
    {
        foreach (ViewOutput view in views) { view.Entries[node].Block = block; }
    }

    internal void Write(Stream output, int end, CancellationToken token)
    {
        if (source.SystemStamp is { } systemStamp) { WriteAt(output, 0, systemStamp); }
        foreach (ViewOutput view in views)
        {
            byte[] descriptor = view.Write(output, end, token);
            if (!view.View.Joliet)
            {
                foreach (int block in source.DuplicatePrimaries) { WriteAt(output, block, descriptor); }
                if (source.MasteringStamp is { } stamp)
                {
                    byte[] recorded = stamp.ToArray();
                    if (IsoImage.MasteringFingerprint(stamp) != 0)
                    {
                        int fingerprint = IsoImage.PrimaryFingerprint(descriptor);
                        recorded[2045] = (byte)(fingerprint >> 16);
                        recorded[2046] = unchecked((byte)(fingerprint >> 8));
                        recorded[2047] = unchecked((byte)fingerprint);
                    }
                    WriteAt(output, source.Terminator + 1, recorded);
                }
            }
        }
        if (source.Boot is { } boot) { WriteAt(output, boot.DescriptorBlock, boot.Descriptor); }
        byte[] terminator = new byte[SectorSize];
        new byte[] { 255, 67, 68, 48, 48, 49, 1 }.CopyTo(terminator, 0);
        WriteAt(output, source.Terminator, terminator);
    }

    internal void Validate(IsoImage actual)
    {
        if (actual.Views.Length != views.Count || actual.Terminator != source.Terminator
            || actual.TrailingBlocks != source.TrailingBlocks
            || !actual.DuplicatePrimaries.SequenceEqual(source.DuplicatePrimaries)
            || !(actual.SystemStamp ?? []).AsSpan().SequenceEqual(source.SystemStamp ?? []))
        { throw new InvalidDataException("Rebuilt ISO descriptor sequence differs."); }
        if (source.MasteringStamp is { } stamp)
        {
            if (actual.MasteringStamp is not { } saved || !stamp.AsSpan(0, 2045).SequenceEqual(saved.AsSpan(0, 2045))
                || (IsoImage.MasteringFingerprint(stamp) == 0) != (IsoImage.MasteringFingerprint(saved) == 0))
            { throw new InvalidDataException("Rebuilt ISO mastering record differs."); }
        }
        else if (actual.MasteringStamp is not null) { throw new InvalidDataException("Unexpected ISO mastering record."); }
        if (source.Boot is { } expectedBoot)
        {
            if (actual.Boot is not { } actualBoot || expectedBoot.DescriptorBlock != actualBoot.DescriptorBlock
                || expectedBoot.End != actualBoot.End || !expectedBoot.Descriptor.SequenceEqual(actualBoot.Descriptor))
            { throw new InvalidDataException("El Torito descriptor or fixed boot allocation differs."); }
        }
        else if (actual.Boot is not null) { throw new InvalidDataException("Unexpected El Torito metadata in output."); }
        for (int i = 0; i < views.Count; i++)
        {
            byte[] expected = views[i].View.Descriptor;
            byte[] result = actual.Views[i].Descriptor;
            if (views[i].View.DescriptorBlock != actual.Views[i].DescriptorBlock
                || !expected.AsSpan(0, 80).SequenceEqual(result.AsSpan(0, 80))
                || !expected.AsSpan(88, 44).SequenceEqual(result.AsSpan(88, 44))
                || !expected.AsSpan(156, 2).SequenceEqual(result.AsSpan(156, 2))
                || !expected.AsSpan(174, 16).SequenceEqual(result.AsSpan(174, 16))
                || !expected.AsSpan(190).SequenceEqual(result.AsSpan(190)))
            { throw new InvalidDataException("ISO volume identifiers or retained descriptor metadata differ."); }
            byte[][] expectedDocuments = views[i].View.DocumentIds;
            byte[][] actualDocuments = actual.Views[i].DocumentIds;
            if (expectedDocuments.Length != actualDocuments.Length
                || !expectedDocuments.Zip(actualDocuments, (a, b) => a.AsSpan().SequenceEqual(b)).All(equal => equal))
            { throw new InvalidDataException("ISO descriptor document bindings differ."); }
        }
    }

    internal void ValidateEntry(OpticalNode node, OpticalNode actual)
    {
        foreach (ViewOutput output in views)
        {
            Rendered entry = output.Entries[node];
            IsoEntry other = actual.Iso[output.View.DescriptorBlock];
            if (!entry.Id.SequenceEqual(node == output.Root ? new byte[] { 0 } : other.Identifier!)
                || !node.Directory && (node.Length != 0 && actual.Offset != Offset(entry.Block)))
            { throw new InvalidDataException("Rebuilt ISO identifier or allocation differs."); }
            if (node != output.Root) { SameRecord(entry.Record, other); }
            if (node.Directory)
            {
                SameRecord(entry.Self!, other.Self!);
                SameRecord(entry.Parent!, other.Parent!);
            }
        }
        static void SameRecord(IsoEntry a, IsoEntry b)
        {
            if (!a.Header.AsSpan(18, 14).SequenceEqual(b.Header.AsSpan(18, 14))
                || !a.Susp.SequenceEqual(b.Susp) || !(a.Continuation ?? []).SequenceEqual(b.Continuation ?? []))
            { throw new InvalidDataException("Rebuilt ISO recording date, flags or Rock Ridge metadata differ."); }
        }
    }

    private sealed class Rendered(byte[] id, IsoEntry record, ushort number, Rendered? parent)
    {
        internal byte[] Id { get; } = id;
        internal IsoEntry Record { get; } = record;
        internal ushort Number { get; } = number;
        internal Rendered? ParentDirectory { get; } = parent;
        internal IsoEntry? Self { get; set; }
        internal IsoEntry? Parent { get; set; }
        internal List<(OpticalNode Node, Rendered Entry)> Children { get; } = [];
        internal int Block { get; set; }
        internal int Bytes { get; set; }
    }

    private sealed class ViewOutput
    {
        internal IsoView View { get; }
        internal OpticalNode Root { get; }
        internal Dictionary<OpticalNode, Rendered> Entries { get; } = [];
        private readonly List<Rendered> directories = [];
        private readonly int[] pathBlocks = new int[4];
        private readonly int pathSize;
        private readonly int continuationBlock;

        internal ViewOutput(IsoView view, OpticalNode root, OpticalLayout layout, bool canAlias)
        {
            View = view;
            Root = root;
            var rootEntry = new Rendered([0], root.Iso[view.DescriptorBlock], 1, null);
            foreach (byte[] id in view.DocumentIds)
            {
                if (!root.Children.Any(node => !node.Directory && node.Iso.TryGetValue(view.DescriptorBlock, out IsoEntry? record)
                    && record.Identifier is { } identifier && identifier.AsSpan().SequenceEqual(id)))
                { throw new NotSupportedException("An ISO descriptor document must keep its original root file identifier."); }
            }
            Entries.Add(root, rootEntry);
            directories.Add(rootEntry);
            var waiting = new Queue<(OpticalNode Node, OpticalNode Parent, Rendered Entry, int Depth)>();
            waiting.Enqueue((root, root, rootEntry, 1));
            while (waiting.TryDequeue(out var item))
            {
                if (!view.Joliet && item.Depth > 8)
                { throw new NotSupportedException("ISO9660 rebuild accepts at most eight directory levels including the root."); }
                Rendered directory = item.Entry;
                directory.Self = directory.Record.Self ?? NewRecord(item.Node, [0], view, false);
                directory.Parent = directory.Record.Parent ?? NewRecord(item.Parent, [1], view, false);
                if (view.RockRidge)
                {
                    directory.Self = UpdateLinks(directory.Self, item.Node);
                    directory.Parent = UpdateLinks(directory.Parent, item.Parent);
                }
                var reserved = new HashSet<string>(StringComparer.Ordinal);
                foreach (OpticalNode child in item.Node.Children)
                {
                    if (child.Iso.TryGetValue(view.DescriptorBlock, out IsoEntry? previous) && previous.Identifier is not null
                        && !reserved.Add(Convert.ToHexString(previous.Identifier)))
                    { throw new NotSupportedException("Existing ISO primary aliases collide."); }
                }
                // Reserve names before assigning any fallback aliases.
                var descriptors = new List<(OpticalNode Node, byte[] Id, IsoEntry Info)>();
                int counter = 0;
                foreach (OpticalNode child in item.Node.Children)
                {
                    child.Iso.TryGetValue(view.DescriptorBlock, out IsoEntry? previous);
                    if (!IsoImage.NamesSupported(child.Name))
                    { throw new NotSupportedException("Unrepresentable ISO/Joliet/Rock Ridge name: " + child.Name); }
                    byte[] id = previous?.Identifier ?? NewIdentifier(child, reserved, ref counter);
                    IsoEntry info = previous is null ? NewRecord(child, id, view, true)
                        : previous.Identifier is null && view.RockRidge
                            ? new IsoEntry(id, previous.Header, RenameSusp(previous.Susp, child.Name))
                            { Self = previous.Self, Parent = previous.Parent }
                            : new IsoEntry(id, previous.Header, previous.Susp) { Self = previous.Self, Parent = previous.Parent };
                    if (view.RockRidge) { info = UpdateLinks(info, child); }
                    descriptors.Add((child, id, info));
                }
                foreach ((OpticalNode child, byte[] id, IsoEntry info) in descriptors.OrderBy(row => Convert.ToHexString(row.Id), StringComparer.Ordinal))
                {
                    if (child.Directory && directories.Count == ushort.MaxValue)
                    { throw new NotSupportedException("ISO path table exceeds 65535 directories."); }
                    var entry = new Rendered(id, info, child.Directory ? checked((ushort)(directories.Count + 1)) : (ushort)0, directory);
                    directory.Children.Add((child, entry));
                    Entries.Add(child, entry);
                    if (child.Directory)
                    {
                        directories.Add(entry);
                        waiting.Enqueue((child, item.Node, entry, item.Depth + 1));
                    }
                }

                byte[] NewIdentifier(OpticalNode child, HashSet<string> identifiers, ref int alias)
                {
                    if (view.Joliet && child.Name.Length > 64)
                    { throw new NotSupportedException("Joliet names are limited to 64 BMP code units."); }
                    bool representable = view.Joliet || IsoImage.IsPrimaryName(child.Name, child.Directory, view.Level1);
                    if (representable)
                    {
                        byte[] encoded = IsoImage.EncodeIdentifier(child.Name, child.Directory, view.Joliet);
                        if (identifiers.Add(Convert.ToHexString(encoded))) { return encoded; }
                    }
                    if (!canAlias || view.Joliet)
                    {
                        string level = view.Level1 ? "level 1 uppercase 8.3" : "level 2 uppercase single-extent";
                        throw new NotSupportedException($"Name {child.Name} cannot be represented without loss in this ISO {level} namespace.");
                    }
                    while (true)
                    {
                        byte[] encoded = IsoImage.EncodeIdentifier($"SW{++alias:D6}", child.Directory, false);
                        if (identifiers.Add(Convert.ToHexString(encoded))) { return encoded; }
                    }
                }
            }
            pathSize = directories.Sum(d => 8 + d.Id.Length + (d.Id.Length & 1));
            pathBlocks[0] = layout.Allocate(pathSize);
            if (U32(view.Descriptor, 144) != 0) { pathBlocks[1] = layout.Allocate(pathSize); }
            pathBlocks[2] = layout.Allocate(pathSize);
            if (BinaryPrimitives.ReadUInt32BigEndian(view.Descriptor.AsSpan(152)) != 0) { pathBlocks[3] = layout.Allocate(pathSize); }
            if (rootEntry.Self!.Continuation is not null)
            {
                continuationBlock = layout.Allocate(SectorSize);
                IsoEntry old = rootEntry.Self;
                byte[] susp = old.Susp.ToArray();
                for (int i = 0; i + 4 <= susp.Length && susp[i + 2] >= 4; i += susp[i + 2])
                {
                    if (susp.AsSpan(i, 2).SequenceEqual("CE"u8)) { WBoth32(susp, i + 4, continuationBlock); }
                }
                rootEntry.Self = new(old.Identifier, old.Header, susp, old.Continuation);
            }
            foreach (Rendered dir in directories)
            {
                int cursor = 0;
                Advance(RecordLength([0], dir.Self!));
                Advance(RecordLength([1], dir.Parent!));
                foreach ((_, Rendered entry) in dir.Children) { Advance(RecordLength(entry.Id, entry.Record)); }
                dir.Bytes = checked(Blocks(cursor) * SectorSize);
                if (dir.Bytes > 16 * 1024 * 1024) { throw new NotSupportedException("Optical directory exceeds its metadata bound."); }
                dir.Block = layout.Allocate(dir.Bytes);
                void Advance(int bytes)
                {
                    if (cursor % SectorSize + bytes > SectorSize) { cursor = checked(Blocks(cursor) * SectorSize); }
                    cursor = checked(cursor + bytes);
                }
            }
        }

        private static IsoEntry UpdateLinks(IsoEntry record, OpticalNode node)
        {
            int px = IsoImage.FindSystemUseOffset(record.Susp, "PX"u8);
            if (px < 0) { throw new InvalidDataException("Missing Rock Ridge POSIX metadata."); }
            int links = node.Directory ? 2 + node.Children.Count(child => child.Directory) : 1;
            byte[] data = record.Susp.ToArray();
            WBoth32(data, px + 12, links);
            return new(record.Identifier, record.Header, data, record.Continuation)
            { Self = record.Self, Parent = record.Parent };
        }

        private static int RecordLength(byte[] id, IsoEntry info)
        {
            int size = 33 + id.Length + (id.Length % 2 == 0 ? 1 : 0) + info.Susp.Length;
            if (size > 255) { throw new NotSupportedException("ISO directory identifier plus preserved Rock Ridge metadata exceeds one record."); }
            return size;
        }

        private static IsoEntry NewRecord(OpticalNode node, byte[] id, IsoView view, bool named)
        {
            var bytes = new byte[33];
            IsoImage.WriteDate(bytes.AsSpan(18, 7), node.Modified);
            bytes[25] = (byte)((node.Directory ? 2 : 0) | (node.Hidden ? 1 : 0));
            WBoth16(bytes, 28, 1);
            if (view.Xa)
            {
                var xa = new byte[14];
                BinaryPrimitives.WriteUInt16BigEndian(xa.AsSpan(4), node.Directory ? (ushort)0x8d55 : (ushort)0x0911);
                "XA"u8.CopyTo(xa.AsSpan(6));
                return new(id, bytes, xa);
            }
            if (!view.RockRidge) { return new(id, bytes, []); }
            byte[] px = new byte[36];
            "PX\u0024\u0001"u8.CopyTo(px);
            WBoth32(px, 4, node.Directory ? 0x41ed : 0x81a4);
            WBoth32(px, 12, node.Directory ? 2 : 1);
            byte[] tf = [84, 70, 12, 1, 2, .. bytes.AsSpan(18, 7)];
            return new(id, bytes, named ? [82, 82, 5, 1, 0x89, .. px, .. AlternateName(node.Name), .. tf]
                : [82, 82, 5, 1, 0x81, .. px, .. tf]);
        }

        private static byte[] AlternateName(string name)
        {
            byte[] payload = Encoding.UTF8.GetBytes(name);
            if (payload.Length > 250) { throw new NotSupportedException("Rock Ridge alternate name exceeds one record."); }
            return [78, 77, (byte)(payload.Length + 5), 1, 0, .. payload];
        }

        private static byte[] RenameSusp(byte[] susp, string name)
        {
            var changed = new List<byte>(susp.Length);
            bool replaced = false;
            for (int position = 0; position + 4 <= susp.Length && susp[position + 2] >= 4;)
            {
                int length = susp[position + 2];
                if (susp.AsSpan(position, 2).SequenceEqual("NM"u8))
                {
                    changed.AddRange(AlternateName(name));
                    replaced = true;
                }
                else if (susp.AsSpan(position, 2).SequenceEqual("ST"u8) && !replaced)
                {
                    changed.AddRange(AlternateName(name));
                    changed.AddRange(susp[position..(position + length)]);
                    replaced = true;
                }
                else
                {
                    byte[] record = susp[position..(position + length)];
                    if (record.AsSpan(0, 2).SequenceEqual("RR"u8)) { record[4] |= 8; }
                    changed.AddRange(record);
                }
                position += length;
            }
            if (!replaced) { changed.AddRange(AlternateName(name)); }
            return changed.ToArray();
        }

        internal byte[] Write(Stream output, int end, CancellationToken token)
        {
            Rendered root = directories[0];
            byte[] descriptor = View.Descriptor.ToArray();
            WBoth32(descriptor, 80, end);
            WBoth32(descriptor, 132, pathSize);
            for (int index = 0; index < pathBlocks.Length; index++)
            {
                int block = pathBlocks[index];
                bool big = index >= 2;
                if (big) { BinaryPrimitives.WriteInt32BigEndian(descriptor.AsSpan(140 + index * 4), block); }
                else { W32(descriptor, 140 + index * 4, (uint)block); }
                if (block != 0) { WritePathTable(output, block, big, token); }
            }
            byte[] cdRoot = descriptor.AsSpan(156, 34).ToArray();
            WBoth32(cdRoot, 2, root.Block);
            WBoth32(cdRoot, 10, root.Bytes);
            cdRoot.CopyTo(descriptor, 156);
            WriteAt(output, View.DescriptorBlock, descriptor);
            if (continuationBlock != 0) { WriteAt(output, continuationBlock, root.Self!.Continuation!); }
            foreach (Rendered directory in directories)
            {
                token.ThrowIfCancellationRequested();
                byte[] data = new byte[directory.Bytes];
                int cursor = 0;
                Append([0], directory.Self!, directory.Block, directory.Bytes);
                Rendered parent = directory.ParentDirectory ?? directory;
                Append([1], directory.Parent!, parent.Block, parent.Bytes);
                foreach ((OpticalNode node, Rendered entry) in directory.Children)
                {
                    Append(entry.Id, entry.Record, entry.Block, node.Directory ? entry.Bytes : node.Length);
                }
                WriteAt(output, directory.Block, data);

                void Append(byte[] id, IsoEntry record, int block, long bytes)
                {
                    int length = RecordLength(id, record);
                    if (cursor % SectorSize + length > SectorSize) { cursor = Blocks(cursor) * SectorSize; }
                    Span<byte> target = data.AsSpan(cursor, length);
                    record.Header.CopyTo(target);
                    target[0] = (byte)length;
                    WBoth32(target, 2, block);
                    W32(target, 10, checked((uint)bytes));
                    BinaryPrimitives.WriteUInt32BigEndian(target[14..], (uint)bytes);
                    target[32] = (byte)id.Length;
                    id.CopyTo(target[33..]);
                    record.Susp.CopyTo(target[(33 + id.Length + (id.Length % 2 == 0 ? 1 : 0))..]);
                    cursor += length;
                }
            }
            return descriptor;
        }

        private void WritePathTable(Stream output, int block, bool big, CancellationToken token)
        {
            byte[] table = new byte[pathSize];
            int cursor = 0;
            foreach (Rendered dir in directories)
            {
                token.ThrowIfCancellationRequested();
                table[cursor] = (byte)dir.Id.Length;
                uint start = (uint)dir.Block;
                ushort parent = dir.ParentDirectory?.Number ?? 1;
                if (big)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(cursor + 2), start);
                    BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(cursor + 6), parent);
                }
                else
                {
                    W32(table, cursor + 2, start);
                    W16(table, cursor + 6, parent);
                }
                dir.Id.CopyTo(table, cursor + 8);
                cursor += 8 + dir.Id.Length + (dir.Id.Length & 1);
            }
            WriteAt(output, block, table);
        }
    }
}
