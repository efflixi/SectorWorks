using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.NtfsValidation;
using static SectorWorks.Storage.PartitionReader;
using Attribute = SectorWorks.Storage.NtfsValidation.Attribute;

namespace SectorWorks.Storage;

internal sealed record NtfsReadInfo(string Version, bool HasAttributeLists);
internal sealed record NtfsResolvedStream(uint Type, string Name, IReadOnlyList<Attribute> Extents,
    IReadOnlyList<NtfsValidation.Run> Runs);
internal sealed record NtfsResolvedFile(long Id, byte[] Bytes, IReadOnlyList<NtfsResolvedStream> Streams,
    Attribute? AttributeList);
internal sealed record NtfsResolvedMetadata(IReadOnlyDictionary<long, byte[]> PhysicalRecords,
    IReadOnlyList<NtfsResolvedFile> Files);

internal sealed class NtfsReadValidation(IReadOnlyBlockDevice device, int sector, int cluster,
    int recordSize, int indexSize, CancellationToken token, bool collectForWriter = false)
{
    private sealed record Record(long Id, byte[] Bytes, List<Attribute> Attributes)
    {
        internal ulong Reference => (ulong)Id | ((ulong)U16(Bytes, 16) << 48);
    }

    private sealed record StreamAttribute(uint Type, string Name, List<Attribute> Extents)
    {
        internal byte[] First => Extents[0].Bytes;
        internal bool Nonresident => First[8] == 1;
        internal long Length => Nonresident ? checked((long)U64(First, 48)) : U32(First, 16);
    }

    private static readonly Encoding Names = new UnicodeEncoding(false, false, true);
    private List<Run> _mftRuns = [];
    private long _initialized;
    private long _extensionBudget = 32L * 1024 * 1024;
    private long _indexBudget = 128L * 1024 * 1024;
    private long _listBudget = 32L * 1024 * 1024;
    private bool _hasLists;
    private readonly Dictionary<long, byte[]> _physical = [];
    private readonly List<NtfsResolvedFile> _files = [];
    private readonly HashSet<long> _ownedExtensions = [];

    internal static NtfsResolvedMetadata ResolveForWriter(IReadOnlyBlockDevice device, byte[] boot,
        int sector, int cluster, int recordSize, int indexSize, CancellationToken token)
    {
        var validator = new NtfsReadValidation(device, sector, cluster, recordSize, indexSize, token, collectForWriter: true);
        _ = validator.ValidateCore(boot);
        if (validator._physical.Any(p => U64(p.Value, 32) != 0 && !validator._ownedExtensions.Contains(p.Key)))
        { throw new ImageFormatException("NTFS allocated extension has no uniquely resolved base owner."); }
        return new(validator._physical, validator._files);
    }

    internal static NtfsReadInfo Validate(IReadOnlyBlockDevice device, byte[] boot, int sector,
        int cluster, CancellationToken token)
    {
        int Decode(byte value) => unchecked((sbyte)value) < 0
            ? checked((int)(1L << -unchecked((sbyte)value))) : checked(value * cluster);
        return new NtfsReadValidation(device, sector, cluster, Decode(boot[64]), Decode(boot[68]), token)
            .ValidateCore(boot);
    }

    private NtfsReadInfo ValidateCore(byte[] boot)
    {
        Record first = ParseRecord(0, Bounds.Read(device, checked((long)U64(boot, 48) * cluster), recordSize));
        if (first.Attributes.Any(a => a.Type == 0x20))
        {
            throw new ImageFormatException("NTFS MFT bootstrap attribute lists require separately qualified mapping support.");
        }
        Attribute mft = first.Attributes.SingleOrDefault(a => a.Type == 0x80 && a.Bytes[9] == 0)
            ?? throw new ImageFormatException("NTFS MFT has no unnamed data attribute.");
        if (mft.Bytes[8] != 1 || U64(mft.Bytes, 16) != 0 || U16(mft.Bytes, 12) != 0)
        {
            throw new ImageFormatException("Unsupported resident or continued NTFS MFT bootstrap.");
        }
        _initialized = checked((long)U64(mft.Bytes, 56));
        if (_initialized < 4L * recordSize || _initialized > 128L * 1024 * 1024
            || _initialized % recordSize != 0)
        {
            throw new ImageFormatException("NTFS MFT exceeds the 128 MiB initialized-metadata safety cap or is misaligned.");
        }
        _mftRuns = Runs(mft.Bytes, cluster, device.Length, false);
        string? version = null;
        for (long id = 0; id < _initialized / recordSize; id++)
        {
            token.ThrowIfCancellationRequested();
            var bytes = new byte[recordSize];
            ReadRuns(device, _mftRuns, id * recordSize, bytes);
            if (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            {
                continue;
            }
            Record record = ParseRecord(id, bytes);
            if (collectForWriter && (U16(record.Bytes, 22) & 1) != 0)
            {
                if (U16(record.Bytes, 16) == 0 || (U16(record.Bytes, 4) >= 48 && U32(record.Bytes, 44) != id))
                { throw new ImageFormatException("NTFS allocated record has a zero sequence or mismatched MFT identity."); }
                _physical.Add(id, record.Bytes);
            }
            if ((U16(record.Bytes, 22) & 1) == 0 || U64(record.Bytes, 32) != 0)
            {
                continue;
            }
            List<StreamAttribute> attributes = Resolve(record);
            if (collectForWriter)
            {
                _files.Add(new(id, record.Bytes, attributes.Select(a => new NtfsResolvedStream(a.Type, a.Name,
                    a.Extents.ToArray(), AttributeRuns(a, true))).ToArray(), record.Attributes.SingleOrDefault(a => a.Type == 0x20)));
            }
            if (id == 3)
            {
                StreamAttribute info = attributes.SingleOrDefault(a => a.Type == 0x70 && a.Name.Length == 0)
                    ?? throw new ImageFormatException("NTFS volume information is missing.");
                byte[] value = ReadMetadata(info, 1024);
                if (value.Length < 12 || (value[8], value[9]) is not ((1, 2) or (3, 0) or (3, 1)))
                {
                    throw new ImageFormatException("Unsupported NTFS version; the qualified reader targets 1.2, 3.0 and 3.1.");
                }
                version = $"{value[8]}.{value[9]}";
            }
            ValidateIndexes(attributes);
        }
        return new(version ?? throw new ImageFormatException("NTFS volume information record is not allocated."), _hasLists);
    }

    private Record ParseRecord(long id, byte[] bytes)
    {
        List<Attribute> attributes;
        try
        {
            attributes = ValidateRecord(bytes, sector, cluster, device.Length, allowExtensions: true);
        }
        catch (ImageFormatException error)
        {
            throw new ImageFormatException($"NTFS MFT record {id}: {error.Message}", error);
        }
        ApplyFixups(bytes, sector);
        foreach (Attribute attribute in attributes)
        {
            byte[] a = attribute.Bytes;
            if (a[9] != 0)
            {
                int minimum = a[8] == 1 ? 64 : 24;
                if (U16(a, 10) < minimum)
                {
                    throw new ImageFormatException("NTFS attribute name overlaps its header.");
                }
                _ = Names.GetString(a, U16(a, 10), a[9] * 2);
            }
        }
        return new(id, bytes, attributes);
    }

    private Record ReadExtension(ulong reference, Record owner)
    {
        long id = checked((long)(reference & 0x0000ffffffffffff));
        if (id >= _initialized / recordSize || id == owner.Id || reference >> 48 == 0)
        {
            throw new ImageFormatException("NTFS attribute-list reference is cyclic or outside the initialized MFT.");
        }
        _extensionBudget -= recordSize;
        if (_extensionBudget < 0)
        {
            throw new ImageFormatException("NTFS extension-record reads exceed the 32 MiB safety budget.");
        }
        var raw = new byte[recordSize];
        ReadRuns(device, _mftRuns, id * recordSize, raw);
        Record record = ParseRecord(id, raw);
        if (record.Reference != reference || (U16(record.Bytes, 22) & 1) == 0
            || U64(record.Bytes, 32) != owner.Reference
            || (collectForWriter && U16(record.Bytes, 18) != 0)
            || (U16(record.Bytes, 4) >= 48 && U32(record.Bytes, 44) != id)
            || (U16(record.Bytes, 22) & 2) != (U16(owner.Bytes, 22) & 2)
            || record.Attributes.Any(a => a.Type == 0x20))
        {
            throw new ImageFormatException("NTFS extension has a stale reference, wrong owner/type, or nested attribute list.");
        }
        if (collectForWriter && !_ownedExtensions.Add(id))
        { throw new ImageFormatException("NTFS extension is claimed by multiple logical owners."); }
        return record;
    }

    private List<StreamAttribute> Resolve(Record owner)
    {
        Attribute? list = owner.Attributes.SingleOrDefault(a => a.Type == 0x20);
        if (list is null)
        {
            var plain = owner.Attributes.Select(a => new StreamAttribute(a.Type, AttributeName(a.Bytes), [a])).ToList();
            foreach (StreamAttribute attribute in plain)
            {
                _ = AttributeRuns(attribute, true);
            }
            return plain;
        }
        _hasLists = true;
        if (list.Bytes[9] != 0)
        {
            throw new ImageFormatException("NTFS attribute lists must be unnamed.");
        }
        _listBudget -= new StreamAttribute(0x20, "", [list]).Length;
        if (_listBudget < 0)
        {
            throw new ImageFormatException("NTFS attribute-list metadata exceeds the 32 MiB opening budget.");
        }
        byte[] data = ReadMetadata(new(0x20, "", [list]), 1024 * 1024);
        var records = new Dictionary<long, Record> { [owner.Id] = owner };
        var used = new HashSet<(long Record, ushort Attribute)>();
        var result = new List<StreamAttribute>();
        StreamAttribute? current = null;
        uint previousType = 0;
        string previousName = "";
        for (int offset = 0, count = 0; offset < data.Length; count++)
        {
            token.ThrowIfCancellationRequested();
            if (count >= 4096 || data.Length - offset < 26)
            {
                throw new ImageFormatException("NTFS attribute list exceeds its bounded entry count or has a truncated entry.");
            }
            uint type = U32(data, offset);
            int length = U16(data, offset + 4);
            int nameLength = data[offset + 6] * 2;
            int nameOffset = data[offset + 7];
            if (type == 0x20 || length < 32 || length % 8 != 0 || length > data.Length - offset
                || (nameLength != 0 && (nameOffset < 26 || nameOffset > length || nameLength > length - nameOffset)))
            {
                throw new ImageFormatException("Invalid NTFS attribute-list entry bounds/type/name.");
            }
            string name = nameLength == 0 ? "" : Names.GetString(data, offset + nameOffset, nameLength);
            if (type < previousType || (type == previousType && string.CompareOrdinal(name, previousName) < 0))
            {
                throw new ImageFormatException("NTFS attribute-list entries are not ordered by type and name.");
            }
            previousType = type;
            previousName = name;
            ulong reference = U64(data, offset + 16);
            long id = checked((long)(reference & 0x0000ffffffffffff));
            if (!records.TryGetValue(id, out Record? record))
            {
                if (records.Count >= 256)
                {
                    throw new ImageFormatException("NTFS file exceeds the 256-record extension safety cap.");
                }
                record = ReadExtension(reference, owner);
                records.Add(id, record);
            }
            ushort attributeId = U16(data, offset + 24);
            Attribute? attribute = record.Attributes.SingleOrDefault(a => U16(a.Bytes, 14) == attributeId);
            ulong start = U64(data, offset + 8);
            if (record.Reference != reference || attribute is null || attribute.Type != type
                || AttributeName(attribute.Bytes) != name
                || start != (attribute.Bytes[8] == 1 ? U64(attribute.Bytes, 16) : 0)
                || !used.Add((id, attributeId)))
            {
                throw new ImageFormatException("NTFS attribute-list reference does not uniquely match its record, name, type and VCN.");
            }
            if (start == 0)
            {
                current = new(type, name, [attribute]);
                result.Add(current);
            }
            else if (current is null || current.Type != type || current.Name != name || !current.Nonresident)
            {
                throw new ImageFormatException("NTFS attribute continuation has no matching first extent.");
            }
            else
            {
                current.Extents.Add(attribute);
            }
            offset += length;
        }
        if (records.Values.Any(r => r.Attributes.Any(a => a.Type != 0x20 && !used.Contains((r.Id, U16(a.Bytes, 14))))))
        {
            throw new ImageFormatException("NTFS attribute list omits an attribute from its base/extension records.");
        }
        foreach (StreamAttribute attribute in result)
        {
            _ = AttributeRuns(attribute, true);
        }
        if (result.Where(a => a.Type != 0x30).GroupBy(a => (a.Type, a.Name)).Any(g => g.Count() != 1))
        {
            throw new ImageFormatException("NTFS attribute list defines duplicate logical streams.");
        }
        return result;
    }

    private List<Run> AttributeRuns(StreamAttribute attribute, bool allowSparse)
    {
        if (!attribute.Nonresident)
        {
            return [];
        }
        var result = new List<Run>();
        ulong next = 0;
        foreach (Attribute extent in attribute.Extents)
        {
            byte[] bytes = extent.Bytes;
            if (bytes[8] != 1 || U64(bytes, 16) != next || U16(bytes, 12) != U16(attribute.First, 12))
            {
                throw new ImageFormatException("NTFS attribute extents are overlapping, gapped or have inconsistent flags.");
            }
            result.AddRange(Runs(bytes, cluster, device.Length, allowSparse, allowContinuation: true));
            if (result.Count == 0 && attribute.Extents.Count == 1)
            {
                return result;
            }
            next = checked(U64(bytes, 24) + 1);
        }
        if ((ulong)attribute.Length > checked(next * (ulong)cluster))
        {
            throw new ImageFormatException("NTFS attribute extents do not cover the declared data length.");
        }
        return result;
    }

    private byte[] ReadMetadata(StreamAttribute attribute, int limit)
    {
        ushort flags = U16(attribute.First, 12);
        // A resident index root can mark a directory's default file compression.
        bool directoryDefault = attribute.Type == 0x90 && !attribute.Nonresident && flags == 1;
        if (attribute.Length > limit || (flags != 0 && !directoryDefault))
        {
            throw new ImageFormatException($"NTFS metadata 0x{attribute.Type:x} exceeds its safety cap or uses unsupported flags 0x{flags:x}.");
        }
        int length = checked((int)attribute.Length);
        if (!attribute.Nonresident)
        {
            return attribute.First.AsSpan(U16(attribute.First, 20), length).ToArray();
        }
        if ((ulong)length != U64(attribute.First, 56))
        {
            throw new ImageFormatException("NTFS metadata stream is not fully initialized.");
        }
        var bytes = new byte[length];
        List<Run> runs = AttributeRuns(attribute, false);
        if (length != 0)
        {
            ReadRuns(device, runs, 0, bytes);
        }
        return bytes;
    }

    private void ValidateIndexes(List<StreamAttribute> attributes)
    {
        foreach (StreamAttribute root in attributes.Where(a => a.Type == 0x90))
        {
            if (root.Nonresident || attributes.Count(a => a.Type == 0x90 && a.Name == root.Name) != 1)
            {
                throw new ImageFormatException("NTFS index root must be uniquely named and resident.");
            }
            byte[] rootBytes = ReadMetadata(root, 65536);
            List<long> children = ValidateIndex(rootBytes, 16);
            StreamAttribute? allocation = attributes.SingleOrDefault(a => a.Type == 0xa0 && a.Name == root.Name);
            if (allocation is null)
            {
                if (children.Count != 0)
                {
                    throw new ImageFormatException("NTFS index root refers to a missing allocation stream.");
                }
                continue;
            }
            long length = allocation.Length;
            if (!allocation.Nonresident || U16(allocation.First, 12) != 0
                || length > 16 * 1024 * 1024 || length % indexSize != 0
                || U64(allocation.First, 56) > (ulong)length)
            {
                throw new ImageFormatException("NTFS directory index exceeds the 16 MiB safety cap or is misaligned.");
            }
            _indexBudget -= length;
            if (_indexBudget < 0)
            {
                throw new ImageFormatException("NTFS allocated-index metadata exceeds the 128 MiB opening budget.");
            }
            StreamAttribute bitmap = attributes.SingleOrDefault(a => a.Type == 0xb0 && a.Name == root.Name)
                ?? throw new ImageFormatException("NTFS allocated index has no matching bitmap.");
            byte[] bits = ReadMetadata(bitmap, 65536);
            if (bits.LongLength * 8 < length / indexSize)
            {
                throw new ImageFormatException("NTFS index bitmap does not cover its allocation.");
            }
            List<Run> runs = AttributeRuns(allocation, false);
            var graph = new Dictionary<long, List<long>>();
            for (long position = 0; position < length; position += indexSize)
            {
                token.ThrowIfCancellationRequested();
                long bit = position / indexSize;
                if ((bits[bit / 8] & (1 << (int)(bit % 8))) == 0)
                {
                    continue;
                }
                var index = new byte[indexSize];
                if ((ulong)(position + indexSize) > U64(allocation.First, 56))
                {
                    throw new ImageFormatException("NTFS active index block exceeds initialized metadata.");
                }
                ReadRuns(device, runs, position, index);
                if (!index.AsSpan(0, 4).SequenceEqual("INDX"u8))
                {
                    throw new ImageFormatException("Invalid NTFS index-allocation signature.");
                }
                ApplyFixups(index, sector);
                long vcn = checked((long)U64(index, 16));
                if (vcn != position / (cluster <= indexSize ? cluster : sector)
                    || !graph.TryAdd(vcn, ValidateIndex(index, 24)))
                {
                    throw new ImageFormatException("Invalid/duplicate NTFS index node VCN.");
                }
            }
            ValidateGraph(graph, children);
        }
        if (attributes.Any(a => a.Type == 0xa0 && !attributes.Any(r => r.Type == 0x90 && r.Name == a.Name)))
        {
            throw new ImageFormatException("NTFS allocated index has no matching root.");
        }
    }
}
