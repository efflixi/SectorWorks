using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed partial class FatFileSystem
{
    internal static bool IsSafeAlias(string name)
    {
        string[] parts = name.Split('.');
        return parts.Length is 1 or 2 && parts[0].Length is >= 1 and <= 8
            && (parts.Length == 1 || parts[1].Length is >= 1 and <= 3)
            && parts.All(part => part.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                or '_' or '-' or '~' or '$' or '!' or '#' or '%' or '&' or '(' or ')' or '@' or '^' or '\'' or '`' or '{' or '}'));
    }

    internal string AliasPath(string path, CancellationToken token)
    {
        string result = "";
        string current = "";
        foreach (string component in ImagePath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + component;
            Entry entry = Resolve(current, token) ?? throw new ImageFormatException("Missing FAT alias target.");
            result += "\\" + entry.File.ShortName;
        }
        return result.Length == 0 ? "\\" : result;
    }

    internal byte[] EntryBytes(string path, CancellationToken token) =>
        Bounds.Read(_device, (Resolve(path, token) ?? throw new ImageFormatException("Missing FAT entry.")).DirectoryOffset, 32);

    internal string DisplayName(string path, CancellationToken token) =>
        (Resolve(path, token) ?? throw new ImageFormatException("Missing FAT entry.")).File.Name;

    internal string CanonicalPath(string path, CancellationToken token) =>
        path == "/" ? "/" : (Resolve(path, token) ?? throw new ImageFormatException("Missing FAT entry.")).File.Path;

    internal int NameSlotCount(string path, CancellationToken token) =>
        1 + (Resolve(path, token) ?? throw new ImageFormatException("Missing FAT entry.")).LongOffsets.Count;

    internal Dictionary<string, int> RootNameSlots(CancellationToken token) =>
        DirectoryEntries("/", _rootCluster, true, token, strictNames: true)
            .ToDictionary(e => e.File.Name, e => e.LongOffsets.Count + 1, StringComparer.OrdinalIgnoreCase);

    internal List<(long Offset, byte[] Bytes)> AncestorMetadata(string path, CancellationToken token)
    {
        var metadata = new List<(long, byte[])>();
        string parent = Parent(path);
        while (parent.Length != 0)
        {
            Entry entry = Resolve(parent, token) ?? throw new ImageFormatException("Missing FAT ancestor.");
            metadata.Add((entry.DirectoryOffset, Bounds.Read(_device, entry.DirectoryOffset, 32)));
            metadata.Add((ClusterOffset(entry.Cluster), Bounds.Read(_device, ClusterOffset(entry.Cluster), 64)));
            parent = Parent(parent);
        }
        return metadata;
    }

    internal static void RestoreAncestorMetadata(Stream output, List<(long Offset, byte[] Bytes)> metadata)
    {
        foreach (var entry in metadata)
        {
            output.Position = entry.Offset;
            output.Write(entry.Bytes);
        }
    }

    private void ValidateSpecialEntries(uint cluster, uint parentCluster)
    {
        byte[] entries = Bounds.Read(_device, ClusterOffset(cluster), 64);
        if (!entries.AsSpan(0, 11).SequenceEqual(".          "u8)
            || !entries.AsSpan(32, 11).SequenceEqual("..         "u8))
        {
            throw new ImageFormatException("FAT directory is missing its conventional special entries.");
        }
        for (int offset = 0; offset < 64; offset += 32)
        {
            uint first = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(offset + 26));
            ushort high = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(offset + 20));
            if (_bits == 32) { first |= (uint)high << 16; }
            if (first != (offset == 0 ? cluster : parentCluster)
                || (_bits != 32 && high != 0) || (_bits == 32 && (high & 0xf000) != 0)
                || entries[offset + 11] != 0x10 || (entries[offset + 12] & ~0x18) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(offset + 28)) != 0)
            {
                throw new ImageFormatException("FAT special-directory metadata is inconsistent or outside the qualified preservation subset.");
            }
        }
    }

    internal void RemoveLongName(Stream output, string path, CancellationToken token)
    {
        Entry entry = Resolve(path, token) ?? throw new ImageFormatException("Missing FAT long-name target.");
        foreach (long offset in entry.LongOffsets)
        {
            DeleteSlot(output, offset);
        }
    }

    internal string ChooseAlias(string parent, string name, string? excludedPath, CancellationToken token)
    {
        if (!FatEditNames.IsSupportedName(name)) { throw new NotSupportedException("Unsupported FAT filename; names must use the qualified NFC BMP subset."); }
        Entry? directory = parent.Length == 0 ? null : Resolve(parent, token);
        List<Entry> entries = DirectoryEntries(parent.Length == 0 ? "/" : parent, directory?.Cluster ?? _rootCluster,
            parent.Length == 0, token, strictNames: true);
        Entry[] others = entries.Where(e => !string.Equals(e.File.Path, excludedPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        bool Used(string candidate) => others.Any(e =>
            string.Equals(candidate, e.File.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, e.File.ShortName, StringComparison.OrdinalIgnoreCase));
        if (Used(name)) { throw new IOException("FAT filename conflicts with an existing display name or short alias."); }
        if (FatEditNames.IsValidShortName(name) && !Used(name.ToUpperInvariant())) { return name.ToUpperInvariant(); }
        int dot = name.LastIndexOf('.');
        string stem = dot > 0 ? name[..dot] : name;
        string extension = dot > 0 ? name[(dot + 1)..] : "";
        static string Clean(string value) => new(value.ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_').ToArray());
        stem = Clean(stem);
        if (stem.Length == 0) { stem = "FILE"; }
        extension = Clean(extension);
        if (extension.Length > 3) { extension = extension[..3]; }
        for (int number = 1; number <= 999999; number++)
        {
            token.ThrowIfCancellationRequested();
            string suffix = "~" + number.ToString(CultureInfo.InvariantCulture);
            string candidate = stem[..Math.Min(stem.Length, 8 - suffix.Length)] + suffix
                + (extension.Length == 0 ? "" : "." + extension);
            if (!Used(candidate)) { return candidate; }
        }
        throw new IOException("No unambiguous FAT short alias is available.");
    }

    internal void SetDisplayName(Stream output, string currentPath, string desiredName, string alias, CancellationToken token,
        byte[]? preservedMetadata = null, bool replacement = false, bool forceLongName = false, bool preserveAlias = false)
    {
        if (!FatEditNames.IsSupportedName(desiredName) || !IsSafeAlias(alias)) { throw new NotSupportedException("Unsupported FAT display name or alias."); }
        Entry entry = Resolve(currentPath, token) ?? throw new ImageFormatException("Missing FAT spelling target.");
        byte[] raw = Bounds.Read(_device, entry.DirectoryOffset, 32);
        if (preservedMetadata is not null)
        {
            raw[11] = preservedMetadata[11];
            preservedMetadata.AsSpan(13, 7).CopyTo(raw.AsSpan(13));
            if (!replacement) { preservedMetadata.AsSpan(20, 2).CopyTo(raw.AsSpan(20)); }
            preservedMetadata.AsSpan(22, 4).CopyTo(raw.AsSpan(22));
        }
        EncodeAlias(alias).CopyTo(raw, 0);
        raw[12] = 0;
        bool representableCase = desiredName.Split('.').All(part =>
            !part.Any(c => c is >= 'A' and <= 'Z') || !part.Any(c => c is >= 'a' and <= 'z'));
        bool shortOnly = !forceLongName && (FatEditNames.IsSupportedShortName(desiredName)
            || replacement && IsSafeAlias(desiredName) && representableCase)
            && desiredName.Equals(alias, StringComparison.OrdinalIgnoreCase);
        if (shortOnly)
        {
            string[] parts = desiredName.Split('.');
            if (parts[0].Any(c => c is >= 'a' and <= 'z')) { raw[12] |= 8; }
            if (parts.Length == 2 && parts[1].Any(c => c is >= 'a' and <= 'z')) { raw[12] |= 16; }
        }
        int longCount = shortOnly ? 0 : (desiredName.Length + 12) / 13;
        if ((replacement || preserveAlias) && longCount != 0 && preservedMetadata is not null) { raw[12] = preservedMetadata[12]; }
        if (longCount == entry.LongOffsets.Count)
        {
            WriteNames(output, entry.LongOffsets.Append(entry.DirectoryOffset).ToArray(), raw, desiredName, longCount);
            return;
        }
        foreach (long offset in entry.LongOffsets.Append(entry.DirectoryOffset))
        {
            DeleteSlot(output, offset);
        }
        List<long> slots = AllocateDirectorySlots(output, Parent(currentPath), longCount + 1, token);
        WriteNames(output, slots, raw, desiredName, longCount);
    }

    private static byte[] EncodeAlias(string alias)
    {
        string[] parts = alias.ToUpperInvariant().Split('.');
        return Encoding.ASCII.GetBytes(parts[0].PadRight(8) + (parts.Length == 2 ? parts[1] : "").PadRight(3));
    }

    private static void DeleteSlot(Stream output, long offset)
    {
        output.Position = offset;
        output.WriteByte(0xe5);
        // DiscUtils tests LFN attributes before the deleted marker when collecting reusable slots.
        output.Position = offset + 11;
        output.WriteByte(0);
    }

    private static void WriteNames(Stream output, IReadOnlyList<long> slots, byte[] raw, string name, int longCount)
    {
        byte checksum = Checksum(raw.AsSpan(0, 11));
        int[] characterOffsets = [1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30];
        for (int index = 0; index < longCount; index++)
        {
            int ordinal = longCount - index;
            byte[] record = new byte[32];
            record[0] = checked((byte)(ordinal | (index == 0 ? 0x40 : 0)));
            record[11] = 0x0f;
            record[13] = checksum;
            for (int character = 0; character < 13; character++)
            {
                int position = (ordinal - 1) * 13 + character;
                ushort value = position < name.Length ? name[position] : position == name.Length ? (ushort)0 : ushort.MaxValue;
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(characterOffsets[character]), value);
            }
            output.Position = slots[index];
            output.Write(record);
        }
        output.Position = slots[^1];
        output.Write(raw);
    }

    private List<long> AllocateDirectorySlots(Stream output, string parent, int required, CancellationToken token)
    {
        Entry? directory = parent.Length == 0 ? null : Resolve(parent, token);
        bool fixedRoot = parent.Length == 0 && _bits != 32;
        List<uint>? chain = fixedRoot ? null : Chain(directory?.Cluster ?? _rootCluster, token,
            Math.Min(MaxChainClusters, MaxDirectoryBytes / _clusterBytes));
        var run = new List<long>();
        bool end = false;
        int bytes = chain is null ? _rootBytes : checked(chain.Count * _clusterBytes);
        for (int position = 0; position < bytes; position += 32)
        {
            token.ThrowIfCancellationRequested();
            long offset = chain is null ? _rootOffset + position : ClusterOffset(chain[position / _clusterBytes]) + position % _clusterBytes;
            byte first = Bounds.Read(_device, offset, 1)[0];
            end |= first == 0;
            if (end || first == 0xe5) { run.Add(offset); }
            else { run.Clear(); }
            if (run.Count != required) { continue; }
            if (end && position + 32 < bytes)
            {
                long next = chain is null ? offset + 32 : ClusterOffset(chain[(position + 32) / _clusterBytes]) + (position + 32) % _clusterBytes;
                output.Position = next;
                output.WriteByte(0);
            }
            return run;
        }
        if (fixedRoot) { throw new IOException("The fixed FAT root directory has no contiguous slots for the long filename."); }
        while (run.Count < required)
        {
            token.ThrowIfCancellationRequested();
            if (checked(bytes + _clusterBytes) > MaxDirectoryBytes) { throw new IOException("FAT directory growth exceeds the metadata safety cap."); }
            uint allocated = AllocateCluster(output, token);
            WriteFatEntry(output, chain![^1], allocated);
            chain.Add(allocated);
            bytes += _clusterBytes;
            long offset = ClusterOffset(allocated);
            for (int i = 0; i < _clusterBytes && run.Count < required; i += 32) { run.Add(offset + i); }
        }
        return run;
    }

    private uint AllocateCluster(Stream output, CancellationToken token)
    {
        for (uint cluster = 2; cluster < _clusterCount + 2; cluster++)
        {
            token.ThrowIfCancellationRequested();
            if (NextCluster(cluster) != 0) { continue; }
            WriteFatEntry(output, cluster, _bits == 12 ? 0xfffu : _bits == 16 ? 0xffffu : 0x0fffffffu);
            output.Position = ClusterOffset(cluster);
            output.Write(new byte[_clusterBytes]);
            return cluster;
        }
        throw new IOException("No free FAT cluster remains for directory growth.");
    }

    private void WriteFatEntry(Stream output, uint cluster, uint value)
    {
        long relative = _bits == 12 ? cluster + cluster / 2 : cluster * (long)(_bits / 8);
        int count = _bits == 32 ? 4 : 2;
        byte[] bytes = Bounds.Read(_device, _fatOffset + relative, count);
        if (_bits == 12)
        {
            ushort old = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)((cluster & 1) == 0
                ? ((uint)old & 0xf000u) | value : ((uint)old & 0x000fu) | value << 4)));
        }
        else if (_bits == 16) { BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)value)); }
        else { BinaryPrimitives.WriteUInt32LittleEndian(bytes, (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 0xf0000000) | value); }
        for (int copy = 0; copy < 2; copy++)
        {
            output.Position = _fatOffset + copy * _fatBytes + relative;
            output.Write(bytes);
        }
    }

    private static string Parent(string path) => path[..path.LastIndexOf('/')];
}
