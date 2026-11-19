using System.Collections;
using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal sealed partial class FatFileSystem : IReadOnlyFileSystem
{
    private const int MaxChainClusters = 1_048_576;
    private const int MaxDirectoryBytes = 16 * 1024 * 1024;
    private readonly IReadOnlyBlockDevice _device;
    private readonly int _sector;
    private readonly int _clusterBytes;
    private readonly long _fatOffset;
    private readonly long _fatBytes;
    private readonly long _rootOffset;
    private readonly int _rootBytes;
    private readonly long _dataOffset;
    private readonly uint _clusterCount;
    private readonly uint _rootCluster;
    private readonly int _bits;
    private bool _disposed;
    private static readonly Encoding Oem = CreateOem();

    private sealed record Entry(FileEntry File, uint Cluster, long DirectoryOffset, IReadOnlyList<long> LongOffsets);

    internal static bool LooksLikeFat(byte[] boot) => boot.Length >= 90
        && (boot.AsSpan(54, 3).SequenceEqual("FAT"u8) || boot.AsSpan(82, 3).SequenceEqual("FAT"u8)
            || (boot[0] is 0xeb or 0xe9 && U16(boot, 11) is 512 or 1024 or 2048 or 4096
                && boot[13] != 0 && boot[16] is 1 or 2 && U16(boot, 14) != 0
                && (U16(boot, 22) != 0 || U32(boot, 36) != 0)));

    internal FatFileSystem(IReadOnlyBlockDevice device)
    {
        _device = device;
        byte[] boot = Bounds.Read(device, 0, 512);
        _sector = U16(boot, 11);
        int sectorsPerCluster = boot[13];
        int reserved = U16(boot, 14);
        int copies = boot[16];
        uint sectorsPerFat = U16(boot, 22);
        uint totalSectors = U16(boot, 19);
        if (totalSectors == 0)
        {
            totalSectors = U32(boot, 32);
        }
        if (sectorsPerFat == 0)
        {
            sectorsPerFat = U32(boot, 36);
        }
        if (_sector is not (512 or 1024 or 2048 or 4096) || sectorsPerCluster == 0
            || sectorsPerCluster > 128 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0
            || reserved == 0 || copies is not (1 or 2) || sectorsPerFat == 0 || totalSectors == 0)
        {
            throw new ImageFormatException("Invalid FAT BIOS parameter block or boot signature.");
        }
        _clusterBytes = checked(_sector * sectorsPerCluster);
        int rootEntries = U16(boot, 17);
        _rootBytes = checked(rootEntries * 32);
        long rootSectors = (_rootBytes + _sector - 1) / _sector;
        long dataSector = checked(reserved + (long)copies * sectorsPerFat + rootSectors);
        if (dataSector >= totalSectors || checked((long)totalSectors * _sector) > device.Length)
        {
            throw new ImageFormatException("FAT declared size or data area exceeds the bounded volume.");
        }
        _clusterCount = checked((uint)((totalSectors - dataSector) / sectorsPerCluster));
        _bits = _clusterCount < 4085 ? 12 : _clusterCount < 65525 ? 16 : 32;
        if (_clusterCount == 0 || _clusterCount > 0x0fffffed || (_bits == 32) != (U16(boot, 22) == 0)
            || (_bits == 32 ? rootEntries != 0 : rootEntries == 0))
        {
            throw new ImageFormatException("FAT type and cluster layout disagree.");
        }
        _fatBytes = checked((long)sectorsPerFat * _sector);
        long requiredFatBytes = checked(((long)_clusterCount + 2) * _bits + 7) / 8;
        if (requiredFatBytes > _fatBytes)
        {
            throw new ImageFormatException("FAT allocation table is too small for the declared cluster count.");
        }
        bool hasBootSignature = boot[510] == 0x55 && boot[511] == 0xaa;
        IsEarlyFloppy = HasEarlyFloppyBpb();
        if (!hasBootSignature && !IsEarlyFloppy)
        {
            throw new ImageFormatException("FAT boot signature is missing; this is not a validated legacy FAT12 floppy layout.");
        }
        int activeFat = 0;
        if (_bits == 32)
        {
            ushort flags = U16(boot, 40);
            activeFat = (flags & 0x80) != 0 ? flags & 0xf : 0;
            if (activeFat >= copies || U16(boot, 42) != 0)
            {
                throw new ImageFormatException("Invalid active FAT or unsupported FAT32 version.");
            }
        }
        _fatOffset = checked((long)reserved * _sector + activeFat * _fatBytes);
        _rootOffset = checked((reserved + (long)copies * sectorsPerFat) * _sector);
        _dataOffset = checked(dataSector * _sector);
        _rootCluster = _bits == 32 ? U32(boot, 44) & 0x0fffffff : 0;
        if (_bits == 32)
        {
            CheckCluster(_rootCluster);
        }
        byte[] first = Bounds.Read(device, _fatOffset, 3);
        if (first[0] != boot[21] || first[1] != 0xff || (_bits == 12 && first[2] != 0xff))
        {
            throw new ImageFormatException("Invalid FAT reserved allocation entries.");
        }
        if (!hasBootSignature && !Bounds.Read(device, _fatOffset, checked((int)_fatBytes)).AsSpan()
            .SequenceEqual(Bounds.Read(device, _fatOffset + _fatBytes, checked((int)_fatBytes))))
        {
            throw new ImageFormatException("Unsigned legacy FAT12 floppy allocation-table copies disagree.");
        }
        Name = $"FAT{_bits}";
        bool hasExtendedLabel = boot[_bits == 32 ? 66 : 38] == 0x29 && !IsEarlyFloppy;
        VolumeLabel = hasExtendedLabel ? Oem.GetString(boot, _bits == 32 ? 71 : 43, 11).Trim()
            : _bits == 32 ? string.Empty : ReadFixedRootLabel();
        Properties = new Dictionary<string, string>
        {
            ["Cluster size"] = _clusterBytes.ToString(),
            ["Cluster count"] = _clusterCount.ToString(),
            ["FAT copies"] = copies.ToString(),
            ["Boot signature"] = hasBootSignature ? "0x55AA present" : "Absent; legacy FAT12 floppy BPB, geometry and matching FAT copies validated.",
            ["Volume label source"] = hasExtendedLabel ? "Extended BIOS parameter block"
                : _bits == 32 ? "Unavailable (no extended label record)" : "Root-directory volume-label entry, if present",
            ["Timestamp interpretation"] = "FAT wall-clock time; original timezone is not recorded.",
            ["Access"] = "Read-only; bounded cluster-chain validation.",
        }.AsReadOnly();

        bool HasEarlyFloppyBpb()
        {
            int jump = boot[0] switch { 0xeb => boot[1] + 2, 0xe9 => U16(boot, 1) + 3, _ => -1 };
            int sectorsPerTrack = U16(boot, 24);
            int heads = U16(boot, 26);
            int sectorsPerCylinder = sectorsPerTrack * heads;
            return _bits == 12 && _sector == 512 && sectorsPerCluster is 1 or 2
                && reserved == 1 && copies == 2 && rootEntries is >= 64 and <= 224
                && _rootBytes % _sector == 0 && U16(boot, 19) != 0
                && checked((long)totalSectors * _sector) == device.Length
                && sectorsPerTrack is 8 or 9 or 15 or 18 or 36 && heads is 1 or 2
                && totalSectors % sectorsPerCylinder == 0 && totalSectors / sectorsPerCylinder is 40 or 80
                && boot[21] is 0xf0 or 0xf9 or 0xfc or 0xfd or 0xfe or 0xff
                && U16(boot, 28) == 0 && jump is >= 30 and <= 36;
        }
    }

    private string ReadFixedRootLabel()
    {
        var buffer = new byte[_sector];
        for (int position = 0; position < _rootBytes; position += buffer.Length)
        {
            int count = Math.Min(buffer.Length, _rootBytes - position);
            Bounds.ReadExactly(_device, _rootOffset + position, buffer.AsSpan(0, count));
            for (int entry = 0; entry < count; entry += 32)
            {
                if (buffer[entry] == 0)
                {
                    return string.Empty;
                }
                byte attributes = buffer[entry + 11];
                if (buffer[entry] != 0xe5 && attributes != 0x0f && (attributes & 0x18) == 0x08)
                {
                    return Oem.GetString(buffer, entry, 11).TrimEnd(' ');
                }
            }
        }
        return string.Empty;
    }

    public string Name { get; }
    internal bool IsEarlyFloppy { get; }
    public string VolumeLabel { get; }
    public bool IsCaseSensitive => false;
    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string normalized = ImagePath.Normalize(path);
        Entry? directory = Resolve(normalized, cancellationToken);
        if (directory is not null && !directory.File.IsDirectory)
        {
            throw new ImageFormatException($"Not a directory: {normalized}");
        }
        return DirectoryEntries(directory?.File.Path ?? "/", directory?.Cluster ?? _rootCluster, normalized == "/",
            cancellationToken).Select(e => e.File).ToArray();
    }

    public Stream OpenRead(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string normalized = ImagePath.Normalize(path);
        Entry entry = Resolve(normalized, default) ?? throw new ImageFormatException("Cannot read a directory as a file.");
        if (entry.File.IsDirectory)
        {
            throw new ImageFormatException("Cannot read a directory as a file.");
        }
        if (entry.File.Length == 0)
        {
            if (entry.Cluster != 0)
            {
                _ = Chain(entry.Cluster, default);
            }
            return new MemoryStream([], false);
        }
        int expectedClusters = checked((int)((entry.File.Length + _clusterBytes - 1) / _clusterBytes));
        List<uint> clusters = Chain(entry.Cluster, default, Math.Min(expectedClusters, MaxChainClusters));
        if (checked((long)clusters.Count * _clusterBytes) < entry.File.Length)
        {
            throw new ImageFormatException($"FAT file {normalized} has a truncated cluster chain.");
        }
        return new FatFileStream(this, clusters, entry.File.Length);
    }

    private Entry? Resolve(string normalized, CancellationToken token)
    {
        if (normalized == "/")
        {
            return null;
        }
        uint cluster = _rootCluster;
        string parent = "/";
        bool root = true;
        var ancestors = new HashSet<uint>();
        if (_rootCluster >= 2)
        {
            ancestors.Add(_rootCluster);
        }
        Entry? current = null;
        string[] parts = normalized[1..].Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            current = DirectoryEntries(parent, cluster, root, token).FirstOrDefault(e =>
                string.Equals(e.File.Name, parts[i], StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.File.ShortName, parts[i], StringComparison.OrdinalIgnoreCase))
                ?? throw new ImageFormatException($"FAT path not found: {normalized}");
            if (i < parts.Length - 1 && !current.File.IsDirectory)
            {
                throw new ImageFormatException($"FAT parent is not a directory: {current.File.Path}");
            }
            if (current.File.IsDirectory && !ancestors.Add(current.Cluster))
            {
                throw new ImageFormatException("Cyclic FAT directory ancestry detected.");
            }
            cluster = current.Cluster;
            parent = current.File.Path;
            root = false;
        }
        return current;
    }

    internal void CompleteChangedDirectoryDates(Stream output, IEnumerable<string> paths, CancellationToken token)
    {
        foreach (string path in paths)
        {
            token.ThrowIfCancellationRequested();
            Entry directory = Resolve(path, token) ?? throw new ImageFormatException("Changed directory was not found.");
            if (!directory.File.IsDirectory || directory.File.ModifiedUtc is not DateTime modified)
            {
                throw new ImageFormatException("Changed directory has no valid FAT modification date.");
            }
            long offset = ClusterOffset(directory.Cluster);
            byte[] entries = Bounds.Read(_device, offset, 64);
            if (!entries.AsSpan(0, 11).SequenceEqual(".          "u8)
                || !entries.AsSpan(32, 11).SequenceEqual("..         "u8))
            {
                throw new ImageFormatException("Changed directory has invalid special entries; refusing timestamp completion.");
            }
            for (int entry = 0; entry < 64; entry += 32)
            {
                if ((entries[entry + 11] & 0x10) == 0) { throw new ImageFormatException("Invalid FAT special-directory attributes."); }
                if (FatTime(U16(entries, entry + 24), U16(entries, entry + 22)) is not null) { continue; }
                ushort date = checked((ushort)(((modified.Year - 1980) << 9) | (modified.Month << 5) | modified.Day));
                ushort time = checked((ushort)((modified.Hour << 11) | (modified.Minute << 5) | (modified.Second / 2)));
                var encoded = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(encoded, time);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(2), date);
                output.Position = checked(offset + entry + 22);
                output.Write(encoded);
            }
        }
    }

    internal void SetShortNameCase(Stream output, string path, CancellationToken token)
    {
        string name = path[(path.LastIndexOf('/') + 1)..];
        if (!FatEditNames.IsSupportedShortName(name)) { throw new ImageFormatException("Filename case requires a FAT long-name entry."); }
        Entry entry = Resolve(path, token) ?? throw new ImageFormatException("Changed FAT entry was not found.");
        string[] parts = name.Split('.');
        byte flags = (byte)(parts[0].Any(c => c is >= 'a' and <= 'z') ? 0x08 : 0);
        if (parts.Length == 2 && parts[1].Any(c => c is >= 'a' and <= 'z')) { flags |= 0x10; }
        byte reserved = Bounds.Read(_device, entry.DirectoryOffset + 12, 1)[0];
        output.Position = entry.DirectoryOffset + 12;
        output.WriteByte((byte)((reserved & ~0x18) | flags));
    }

    internal void ValidateRebuiltSummary(FatFileSystem source)
    {
        if (_bits == 32) { FatWriteMetadata.ValidateRebuiltFat32Summary(source._device, _device); }
    }

    internal long ValidateWritableAllocation(CancellationToken token = default)
    {
        if (_fatBytes > RawFatImageWriter.MaximumFatTableBytes)
        { throw new NotSupportedException("FAT write validation requires an allocation table no larger than 8 MiB."); }
        int fatLength = checked((int)(((_clusterCount + 2L) * _bits + 7) / 8));
        byte[] allocation = Bounds.Read(_device, _fatOffset, fatLength);
        for (long offset = 0; offset < _fatBytes; offset += 65536)
        {
            token.ThrowIfCancellationRequested();
            int length = checked((int)Math.Min(65536, _fatBytes - offset));
            if (!Bounds.Read(_device, _fatOffset + offset, length).AsSpan()
                .SequenceEqual(Bounds.Read(_device, _fatOffset + _fatBytes + offset, length)))
            {
                throw new ImageFormatException("Mirrored FAT allocation tables differ.");
            }
        }
        var reachable = new BitArray(checked((int)_clusterCount + 2));
        int reachableCount = 0;
        var pending = new Stack<(string Path, uint Cluster, bool Root, int Depth, uint ParentCluster)>();
        pending.Push(("/", _rootCluster, true, 0, 0));
        int count = 0;
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (directory.Depth > 128) { throw new ImageFormatException("FAT write traversal exceeds depth limits."); }
            if (!directory.Root) { ValidateSpecialEntries(directory.Cluster, directory.ParentCluster); }
            if (!directory.Root || _bits == 32) { Claim(directory.Cluster); }
            foreach (Entry entry in DirectoryEntries(directory.Path, directory.Cluster, directory.Root, token, strictNames: true))
            {
                if (++count > 100000) { throw new ImageFormatException("FAT write traversal exceeds entry limits."); }
                if (entry.File.IsDirectory) { pending.Push((entry.File.Path, entry.Cluster, false, directory.Depth + 1, directory.Root ? 0 : directory.Cluster)); }
                else if (entry.Cluster != 0)
                {
                    int length = Claim(entry.Cluster);
                    if (length != (entry.File.Length + _clusterBytes - 1) / _clusterBytes)
                    {
                        throw new ImageFormatException("FAT file allocation does not match its declared length.");
                    }
                }
                else if (entry.File.Length != 0) { throw new ImageFormatException("Nonempty FAT file has no allocated cluster."); }
            }
        }
        int badClusters = 0;
        for (uint cluster = 2; cluster < _clusterCount + 2; cluster++)
        {
            token.ThrowIfCancellationRequested();
            uint value = EntryValue(cluster);
            bool claimed = reachable[(int)cluster];
            if (_bits == 12 && value == 0xff7 && !claimed)
            {
                badClusters++;
                continue;
            }
            if ((value != 0) != claimed)
            {
                throw new ImageFormatException("Unreferenced, cross-linked or inconsistent FAT cluster allocation.");
            }
        }
        long freeClusters = (long)_clusterCount - reachableCount - badClusters;
        if (_bits == 32)
        {
            byte[] boot = Bounds.Read(_device, 0, 512);
            byte[] info = Bounds.Read(_device, U16(boot, 48) * (long)_sector, _sector);
            uint declaredFree = U32(info, 488);
            uint nextFree = U32(info, 492);
            if (declaredFree != uint.MaxValue && declaredFree != freeClusters
                || nextFree != uint.MaxValue && (nextFree < 2 || nextFree >= _clusterCount + 2))
            {
                throw new ImageFormatException("FAT32 FSInfo free-space summary disagrees with allocation.");
            }
        }
        return checked(freeClusters * _clusterBytes);
        uint EntryValue(uint cluster)
        {
            int offset = checked((int)(cluster * (uint)_bits / 8));
            return _bits switch
            {
                12 => Fat12Entry(allocation, cluster),
                16 => U16(allocation, offset),
                _ => U32(allocation, offset) & 0x0fffffff
            };
        }

        int Claim(uint cluster)
        {
            uint end = _bits == 12 ? 0xff8u : _bits == 16 ? 0xfff8u : 0x0ffffff8u;
            uint reserved = end - 8;
            int length = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                CheckCluster(cluster);
                if (length == MaxChainClusters)
                { throw new ImageFormatException("FAT cluster chain exceeds the metadata safety budget."); }
                if (reachable[(int)cluster])
                { throw new ImageFormatException("Cyclic or cross-linked FAT cluster allocation."); }
                reachable[(int)cluster] = true;
                reachableCount++;
                length++;
                uint next = EntryValue(cluster);
                if (next >= end) { return length; }
                if (next < 2 || next >= reserved)
                { throw new ImageFormatException("FAT cluster chain refers to a free, reserved or bad cluster."); }
                cluster = next;
            }
        }
    }

    internal bool UsesExpandedAllocationBudget => _clusterCount > MaxChainClusters;

    private static uint Fat12Entry(byte[] table, uint cluster)
    {
        ushort pair = U16(table, checked((int)(cluster * 3 / 2)));
        return (uint)((cluster & 1) == 0 ? pair & 0xfff : pair >> 4);
    }

    internal Dictionary<uint, byte[]> CaptureFat12BadClusters(CancellationToken token)
    {
        var result = new Dictionary<uint, byte[]>();
        if (_bits != 12) { return result; }
        byte[] allocation = Bounds.Read(_device, _fatOffset, checked((int)(((_clusterCount + 2L) * 12 + 7) / 8)));
        for (uint cluster = 2; cluster < _clusterCount + 2; cluster++)
        {
            token.ThrowIfCancellationRequested();
            if (Fat12Entry(allocation, cluster) == 0xff7)
            {
                result.Add(cluster, Bounds.Read(_device, ClusterOffset(cluster), _clusterBytes));
            }
        }
        return result;
    }

    internal void ValidateFat12BadClusters(Dictionary<uint, byte[]> expected, CancellationToken token)
    {
        Dictionary<uint, byte[]> actual = CaptureFat12BadClusters(token);
        if (actual.Count != expected.Count || expected.Any(pair =>
            !actual.TryGetValue(pair.Key, out byte[]? bytes) || !bytes.AsSpan().SequenceEqual(pair.Value)))
        {
            throw new ImageFormatException("FAT output changed the bad-cluster map or its sector bytes.");
        }
    }

    private List<Entry> DirectoryEntries(string parent, uint cluster, bool root, CancellationToken token,
        bool strictNames = false, List<byte[]>? volumeLabels = null)
    {
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var longParts = new Dictionary<int, string>();
        int nextLong = 0;
        byte longChecksum = 0;
        var longOffsets = new List<long>();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<uint>? chain = root && _bits != 32 ? null : Chain(cluster, token,
            Math.Min(MaxChainClusters, MaxDirectoryBytes / _clusterBytes));
        long bytes = chain is null ? _rootBytes : checked((long)chain.Count * _clusterBytes);
        if (bytes > MaxDirectoryBytes)
        {
            throw new ImageFormatException("FAT directory exceeds the 16 MiB metadata safety cap.");
        }
        var buffer = new byte[chain is null ? Math.Min(_rootBytes, 64 * 1024) : _clusterBytes];
        for (long position = 0; position < bytes;)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, bytes - position);
            long offset = chain is null ? _rootOffset + position : ClusterOffset(chain[(int)(position / _clusterBytes)]);
            Bounds.ReadExactly(_device, offset, buffer.AsSpan(0, count));
            for (int i = 0; i < count; i += 32)
            {
                ReadOnlySpan<byte> raw = buffer.AsSpan(i, 32);
                if (raw[0] == 0)
                {
                    if (strictNames && longOffsets.Count != 0) { throw new ImageFormatException("Orphan FAT long-name sequence."); }
                    if (strictNames) { ValidateAliasCollisions(entries); }
                    return entries;
                }
                if (raw[0] == 0xe5)
                {
                    if (strictNames && longOffsets.Count != 0) { throw new ImageFormatException("Interrupted FAT long-name sequence."); }
                    longParts.Clear();
                    longOffsets.Clear();
                    nextLong = 0;
                    continue;
                }
                byte attributes = raw[11];
                if (attributes == 0x0f)
                {
                    int ordinal = raw[0] & 0x1f;
                    if ((raw[0] & 0x40) != 0)
                    {
                        if (strictNames && longOffsets.Count != 0) { throw new ImageFormatException("Overlapping FAT long-name sequence."); }
                        longParts.Clear();
                        longOffsets.Clear();
                        nextLong = ordinal;
                        longChecksum = raw[13];
                    }
                    if (ordinal is < 1 or > 20 || ordinal != nextLong || raw[13] != longChecksum || (raw[0] & 0xa0) != 0
                        || raw[12] != 0 || raw[26] != 0 || raw[27] != 0)
                    {
                        if (strictNames) { throw new ImageFormatException("Invalid FAT long-name ordinal, checksum or reserved fields."); }
                        longParts.Clear();
                        longOffsets.Clear();
                        nextLong = 0;
                        continue;
                    }
                    if (strictNames)
                    {
                        foreach (int characterOffset in new[] { 1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30 })
                        {
                            ushort character = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[characterOffset..]);
                            if (character is >= 0xd800 and <= 0xdfff)
                            {
                                throw new NotSupportedException("FAT writing supports BMP Unicode names without surrogate code units.");
                            }
                        }
                    }
                    longParts[ordinal] = Encoding.Unicode.GetString(raw.Slice(1, 10))
                        + Encoding.Unicode.GetString(raw.Slice(14, 12)) + Encoding.Unicode.GetString(raw.Slice(28, 4));
                    nextLong--;
                    longOffsets.Add(offset + i);
                    continue;
                }
                string shortName = ShortName(raw);
                string name = shortName;
                if (strictNames && longOffsets.Count != 0 && (nextLong != 0 || Checksum(raw[..11]) != longChecksum))
                {
                    throw new ImageFormatException("Incomplete FAT long-name sequence or short-alias checksum mismatch.");
                }
                if (longParts.Count > 0 && nextLong == 0 && Checksum(raw[..11]) == longChecksum)
                {
                    name = string.Concat(longParts.OrderBy(p => p.Key).Select(p => p.Value));
                    if (strictNames) { ValidateLongNamePadding(name); }
                    int nul = name.IndexOf('\0');
                    if (nul >= 0)
                    {
                        name = name[..nul];
                    }
                    name = name.TrimEnd('\uffff');
                }
                long[] entryLongOffsets = longOffsets.ToArray();
                longParts.Clear();
                longOffsets.Clear();
                nextLong = 0;
                if ((attributes & 0x08) != 0 || shortName is "." or "..")
                {
                    if (strictNames && entryLongOffsets.Length != 0) { throw new ImageFormatException("Long names cannot decorate FAT special or volume-label records."); }
                    bool label = attributes is 0x08 or 0x28;
                    if (strictNames && ((attributes & 0x08) != 0 ? !root || !label : root))
                    {
                        throw new NotSupportedException("Unexpected FAT volume-label or special-directory metadata.");
                    }
                    if (label && volumeLabels is not null) { volumeLabels.Add(raw.ToArray()); }
                    continue;
                }
                if (strictNames && (!FatEditNames.IsSupportedName(name) || !IsSafeAlias(shortName)
                    || (raw[12] & ~0x18) != 0 || (attributes & 0xc0) != 0))
                {
                    throw new NotSupportedException("FAT name, alias or entry metadata is outside the qualified safe naming subset.");
                }
                if (strictNames && (raw[13] > 199 || !SupportedModifiedDate(raw)
                    || !SupportedOptionalDate(raw, 16, 14) || !SupportedOptionalDate(raw, 18, -1)
                    || (_bits != 32 && (raw[20] != 0 || raw[21] != 0))
                    || (_bits == 32 && (raw[21] & 0xf0) != 0)))
                {
                    throw new NotSupportedException("FAT timestamps or reserved entry metadata are outside the qualified preservation subset.");
                }
                ImagePath.CheckName(name);
                if (!names.Add(name))
                {
                    throw new ImageFormatException($"Ambiguous duplicate FAT filename: {name}");
                }
                if (strictNames && !aliases.Add(shortName)) { throw new ImageFormatException("Duplicate FAT short alias."); }
                uint start = (uint)(raw[26] | raw[27] << 8);
                if (_bits == 32)
                {
                    start |= (uint)(raw[20] | raw[21] << 8) << 16;
                    start &= 0x0fffffff;
                }
                long length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw[28..]);
                bool directory = (attributes & 0x10) != 0;
                if (strictNames && directory && length != 0)
                {
                    throw new NotSupportedException("FAT directory length metadata is outside the qualified preservation subset.");
                }
                if (directory && (start == cluster || (_rootCluster >= 2 && start == _rootCluster)))
                {
                    throw new ImageFormatException("Cyclic FAT directory reference detected.");
                }
                if ((directory || length > 0) && (start < 2 || start > _clusterCount + 1))
                {
                    throw new ImageFormatException($"Invalid starting cluster for FAT entry {name}.");
                }
                string child = parent == "/" ? "/" + name : parent + "/" + name;
                ushort date = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[24..]);
                ushort time = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[22..]);
                var file = new FileEntry(child, name, directory, directory ? 0 : length, FatTime(date, time),
                    ((FileAttributes)attributes).ToString(), shortName);
                entries.Add(new(file, start, offset + i, entryLongOffsets));
            }
            position += count;
        }
        if (strictNames && longOffsets.Count != 0) { throw new ImageFormatException("Orphan FAT long-name sequence at directory boundary."); }
        if (strictNames)
        {
            ValidateAliasCollisions(entries);
        }
        return entries;
    }

    private static void ValidateLongNamePadding(string encoded)
    {
        int nul = encoded.IndexOf('\0');
        if (nul >= 0 && (nul == 0 || encoded[(nul + 1)..].Any(c => c != '\uffff'))
            || nul < 0 && encoded.Contains('\uffff'))
        {
            throw new ImageFormatException("Invalid FAT long-name terminator or padding.");
        }
        int length = nul < 0 ? encoded.Length : nul;
        if (length > 255 || (length + 12) / 13 != encoded.Length / 13)
        {
            throw new ImageFormatException("Nonminimal or oversized FAT long-name sequence.");
        }
    }

    private static bool SupportedOptionalDate(ReadOnlySpan<byte> raw, int dateOffset, int timeOffset)
    {
        ushort date = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[dateOffset..]);
        ushort time = timeOffset < 0 ? (ushort)0 : System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[timeOffset..]);
        return date == 0 ? time == 0 && (timeOffset < 0 || raw[13] == 0) : FatTime(date, time) is not null;
    }

    private static void ValidateAliasCollisions(List<Entry> entries)
    {
        var names = entries.ToDictionary(e => e.File.Name, StringComparer.OrdinalIgnoreCase);
        foreach (Entry entry in entries)
        {
            if (entry.File.ShortName is string alias && names.TryGetValue(alias, out Entry? other) && other != entry)
            {
                throw new ImageFormatException("FAT display name conflicts with another entry's short alias.");
            }
        }
    }

    private List<uint> Chain(uint first, CancellationToken token, int maxClusters = MaxChainClusters)
    {
        var clusters = new List<uint>();
        var seen = new HashSet<uint>();
        uint current = first;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            CheckCluster(current);
            if (clusters.Count >= maxClusters || !seen.Add(current))
            {
                throw new ImageFormatException("Invalid/cyclic FAT cluster chain, excess allocation, or metadata safety budget exceeded.");
            }
            clusters.Add(current);
            uint next = NextCluster(current);
            uint end = _bits == 12 ? 0xff8u : _bits == 16 ? 0xfff8u : 0x0ffffff8u;
            if (next >= end)
            {
                return clusters;
            }
            uint reserved = _bits == 12 ? 0xff0u : _bits == 16 ? 0xfff0u : 0x0ffffff0u;
            if (next < 2 || next >= reserved)
            {
                throw new ImageFormatException("FAT cluster chain refers to a free, reserved or bad cluster.");
            }
            current = next;
        }
    }

    private uint NextCluster(uint cluster)
    {
        long relative = _bits == 12 ? cluster + cluster / 2 : (long)cluster * (_bits / 8);
        int count = _bits == 32 ? 4 : 2;
        if (relative > _fatBytes - count)
        {
            throw new ImageFormatException("FAT cluster lookup extends outside the allocation table.");
        }
        byte[] value = Bounds.Read(_device, _fatOffset + relative, count);
        return _bits switch
        {
            12 => (uint)((cluster & 1) == 0 ? U16(value, 0) & 0xfff : U16(value, 0) >> 4),
            16 => U16(value, 0),
            _ => U32(value, 0) & 0x0fffffff,
        };
    }

    private void CheckCluster(uint cluster)
    {
        if (cluster < 2 || cluster > _clusterCount + 1)
        {
            throw new ImageFormatException($"FAT cluster {cluster} is outside the data area.");
        }
    }

    private long ClusterOffset(uint cluster) => checked(_dataOffset + ((long)cluster - 2) * _clusterBytes);

    private static string ShortName(ReadOnlySpan<byte> raw)
    {
        byte[] bytes = raw[..11].ToArray();
        if (bytes[0] == 0x05)
        {
            bytes[0] = 0xe5;
        }
        string name = Oem.GetString(bytes, 0, 8).TrimEnd(' ');
        string extension = Oem.GetString(bytes, 8, 3).TrimEnd(' ');
        if ((raw[12] & 0x08) != 0)
        {
            name = name.ToLowerInvariant();
        }
        if ((raw[12] & 0x10) != 0)
        {
            extension = extension.ToLowerInvariant();
        }
        return extension.Length == 0 ? name : name + "." + extension;
    }

    private static byte Checksum(ReadOnlySpan<byte> name)
    {
        byte sum = 0;
        foreach (byte value in name)
        {
            sum = unchecked((byte)(((sum & 1) << 7) + (sum >> 1) + value));
        }
        return sum;
    }

    private static DateTime? FatTime(ushort date, ushort time)
    {
        int year = 1980 + (date >> 9);
        int month = (date >> 5) & 15;
        int day = date & 31;
        int hour = time >> 11;
        int minute = (time >> 5) & 63;
        int second = (time & 31) * 2;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
    }

    private bool SupportedModifiedDate(ReadOnlySpan<byte> raw)
    {
        ushort date = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[24..]);
        ushort time = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw[22..]);
        return (IsEarlyFloppy && date == 0 && time == 0) || FatTime(date, time) is not null;
    }

    private static Encoding CreateOem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(437);
    }

    public void Dispose() => _disposed = true;

    private sealed class FatFileStream(FatFileSystem fs, List<uint> clusters, long length) : Stream
    {
        private long _position;
        private bool _closed;
        public override bool CanRead => !_closed;
        public override bool CanSeek => !_closed;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => _position;
            set
            {
                ObjectDisposedException.ThrowIf(_closed || fs._disposed, this);
                Bounds.Check(value, 0, Length);
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_closed || fs._disposed, this);
            int count = (int)Math.Min(Math.Min(buffer.Length, 1024 * 1024), Length - _position);
            int total = 0;
            while (total < count)
            {
                int index = (int)(_position / fs._clusterBytes);
                int inCluster = (int)(_position % fs._clusterBytes);
                int amount = Math.Min(count - total, fs._clusterBytes - inCluster);
                Bounds.ReadExactly(fs._device, fs.ClusterOffset(clusters[index]) + inCluster, buffer.Slice(total, amount));
                total += amount;
                _position += amount;
            }
            return total;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = checked(origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            });
            return Position;
        }
        public override void Flush() => throw new NotSupportedException("Read-only image.");
        public override void SetLength(long value) => throw new NotSupportedException("Read-only image.");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read-only image.");
        protected override void Dispose(bool disposing)
        {
            _closed = true;
            base.Dispose(disposing);
        }
    }
}

internal static class ImagePath
{
    internal static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!path.StartsWith('/') || path.Length > 32768 || path.Contains('\\') || path.Contains('\0'))
        {
            throw new ImageFormatException("Expected an absolute POSIX image path, not a host path.");
        }
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 256)
        {
            throw new ImageFormatException("Image path exceeds the 256-component safety limit.");
        }
        foreach (string part in parts)
        {
            CheckName(part);
        }
        return "/" + string.Join('/', parts);
    }

    internal static void CheckName(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0', ':']) >= 0)
        {
            throw new ImageFormatException("Unsafe or invalid filename in image metadata.");
        }
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsSurrogate(name[i]))
            {
                if (!char.IsHighSurrogate(name[i]) || i + 1 >= name.Length || !char.IsLowSurrogate(name[++i]))
                {
                    throw new ImageFormatException("Invalid Unicode filename in image metadata.");
                }
            }
        }
    }
}
