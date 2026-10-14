using System.Buffers.Binary;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal static class NtfsValidation
{
    private sealed record Run(long Offset, long Length);
    private sealed record Attribute(uint Type, byte[] Bytes);

    internal static void Validate(IReadOnlyBlockDevice device, byte[] boot, int sector, int cluster,
        CancellationToken token)
    {
        int recordSize = RecordSize(boot[64], cluster);
        int indexSize = RecordSize(boot[68], cluster);
        long firstOffset = checked((long)U64(boot, 48) * cluster);
        byte[] first = Bounds.Read(device, firstOffset, recordSize);
        List<Attribute> attributes = ValidateRecord(first, sector, cluster, device.Length);
        Attribute mftData = attributes.SingleOrDefault(a => a.Type == 0x80 && a.Bytes[9] == 0)
            ?? throw new ImageFormatException("NTFS MFT has no unnamed data attribute.");
        if (mftData.Bytes[8] != 1)
        {
            throw new ImageFormatException("Unsupported resident NTFS MFT.");
        }
        long initialized = checked((long)U64(mftData.Bytes, 56));
        if (initialized < recordSize || initialized > 128L * 1024 * 1024 || initialized % recordSize != 0)
        {
            throw new ImageFormatException("NTFS MFT exceeds the 128 MiB initialized-metadata safety cap or is misaligned.");
        }
        List<Run> mftRuns = Runs(mftData.Bytes, cluster, device.Length, false);
        var record = new byte[recordSize];
        long metadataBudget = 128L * 1024 * 1024;
        for (long offset = 0; offset < initialized; offset += recordSize)
        {
            token.ThrowIfCancellationRequested();
            ReadRuns(device, mftRuns, offset, record);
            if (!record.AsSpan(0, 4).SequenceEqual("FILE"u8))
            {
                if (record.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                {
                    continue;
                }
                throw new ImageFormatException("Invalid NTFS MFT file-record signature.");
            }
            List<Attribute> recordAttributes = ValidateRecord(record, sector, cluster, device.Length);
            var roots = new Dictionary<string, List<long>>();
            foreach (Attribute attribute in recordAttributes)
            {
                if (attribute.Type == 0x90 && attribute.Bytes[8] == 0)
                {
                    int valueOffset = U16(attribute.Bytes, 20);
                    int valueLength = checked((int)U32(attribute.Bytes, 16));
                    if (valueLength < 32)
                    {
                        throw new ImageFormatException("Truncated NTFS index root.");
                    }
                    roots[AttributeName(attribute.Bytes)] = ValidateIndex(attribute.Bytes.AsSpan(valueOffset, valueLength), 16);
                }
                else if (attribute.Type == 0xa0 && attribute.Bytes[8] == 1)
                {
                    long dataLength = checked((long)U64(attribute.Bytes, 56));
                    if (dataLength > 16 * 1024 * 1024 || dataLength % indexSize != 0)
                    {
                        throw new ImageFormatException("NTFS directory index exceeds the 16 MiB safety cap or is misaligned.");
                    }
                    metadataBudget -= dataLength;
                    if (metadataBudget < 0)
                    {
                        throw new ImageFormatException("NTFS allocated-index metadata exceeds the 128 MiB opening budget.");
                    }
                    List<Run> runs = Runs(attribute.Bytes, cluster, device.Length, false);
                    var index = new byte[indexSize];
                    var graph = new Dictionary<long, List<long>>();
                    for (long position = 0; position < dataLength; position += indexSize)
                    {
                        token.ThrowIfCancellationRequested();
                        ReadRuns(device, runs, position, index);
                        if (index.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                        {
                            continue;
                        }
                        if (!index.AsSpan(0, 4).SequenceEqual("INDX"u8))
                        {
                            throw new ImageFormatException("Invalid NTFS index-allocation signature.");
                        }
                        ApplyFixups(index, sector);
                        long vcn = checked((long)U64(index, 16));
                        if (!graph.TryAdd(vcn, ValidateIndex(index, 24)))
                        {
                            throw new ImageFormatException("Duplicate NTFS index node VCN.");
                        }
                    }
                    string name = AttributeName(attribute.Bytes);
                    Attribute root = recordAttributes.SingleOrDefault(a => a.Type == 0x90 && AttributeName(a.Bytes) == name)
                        ?? throw new ImageFormatException("NTFS allocated index has no matching root.");
                    int rootOffset = U16(root.Bytes, 20);
                    int rootLength = checked((int)U32(root.Bytes, 16));
                    List<long> children = ValidateIndex(root.Bytes.AsSpan(rootOffset, rootLength), 16);
                    ValidateGraph(graph, children);
                }
            }
            foreach (var root in roots)
            {
                if (root.Value.Count > 0 && !recordAttributes.Any(a => a.Type == 0xa0 && AttributeName(a.Bytes) == root.Key))
                {
                    throw new ImageFormatException("NTFS index root refers to a missing allocation stream.");
                }
            }
        }
    }

    private static List<Attribute> ValidateRecord(byte[] raw, int sector, int cluster, long volumeLength)
    {
        var bytes = (byte[])raw.Clone();
        if (!bytes.AsSpan(0, 4).SequenceEqual("FILE"u8))
        {
            throw new ImageFormatException("Invalid NTFS MFT bootstrap signature.");
        }
        ApplyFixups(bytes, sector);
        int position = U16(bytes, 20);
        uint used = U32(bytes, 24);
        if (position < 24 || position % 8 != 0 || used > bytes.Length || used < position + 4
            || U32(bytes, 28) != bytes.Length)
        {
            throw new ImageFormatException("Invalid NTFS MFT record bounds.");
        }
        var result = new List<Attribute>();
        for (int count = 0; count < 4096 && position <= used - 4; count++)
        {
            uint type = U32(bytes, position);
            if (type == uint.MaxValue)
            {
                return result;
            }
            if (position > used - 16)
            {
                throw new ImageFormatException("Truncated NTFS attribute header.");
            }
            uint size = U32(bytes, position + 4);
            bool nonresident = bytes[position + 8] == 1;
            if (bytes[position + 8] > 1 || size < (nonresident ? 64 : 24) || size % 8 != 0 || size > used - position)
            {
                throw new ImageFormatException("Invalid/zero NTFS attribute length (would not make parser progress).");
            }
            byte[] attribute = bytes.AsSpan(position, checked((int)size)).ToArray();
            int nameOffset = U16(attribute, 10);
            int nameLength = attribute[9] * 2;
            if (nameLength > 0 && (nameOffset < 16 || nameOffset > size || nameLength > size - nameOffset))
            {
                throw new ImageFormatException("NTFS attribute name exceeds its bounded record.");
            }
            if (type == 0x20)
            {
                throw new ImageFormatException("NTFS attribute-list/extension records are not supported in Phase 1.");
            }
            if (nonresident)
            {
                if (U64(attribute, 16) != 0 || U16(attribute, 34) > 4
                    || U64(attribute, 56) > U64(attribute, 48))
                {
                    throw new ImageFormatException("Unsupported NTFS continuation/compression or inconsistent data length.");
                }
                _ = Runs(attribute, cluster, volumeLength, true);
            }
            else
            {
                uint valueLength = U32(attribute, 16);
                int valueOffset = U16(attribute, 20);
                if (valueOffset < 24 || valueOffset > size || valueLength > size - valueOffset)
                {
                    throw new ImageFormatException("NTFS resident value exceeds its bounded attribute.");
                }
            }
            result.Add(new(type, attribute));
            position = checked(position + (int)size);
        }
        throw new ImageFormatException("NTFS attribute list has no bounded end marker.");
    }

    private static void ApplyFixups(byte[] bytes, int sector)
    {
        int offset = U16(bytes, 4);
        int count = U16(bytes, 6);
        if (bytes.Length % sector != 0 || count != bytes.Length / sector + 1
            || offset < 8 || offset > bytes.Length || count * 2 > bytes.Length - offset)
        {
            throw new ImageFormatException("Invalid NTFS multi-sector fixup bounds.");
        }
        ushort sequence = U16(bytes, offset);
        for (int i = 1; i < count; i++)
        {
            int end = i * sector - 2;
            if (U16(bytes, end) != sequence)
            {
                throw new ImageFormatException("NTFS multi-sector fixup mismatch (torn/corrupt record).");
            }
            bytes.AsSpan(offset + i * 2, 2).CopyTo(bytes.AsSpan(end, 2));
        }
    }

    private static List<Run> Runs(byte[] attribute, int cluster, long volumeLength, bool allowSparse)
    {
        int position = U16(attribute, 32);
        if (position < 64 || position >= attribute.Length)
        {
            throw new ImageFormatException("Invalid NTFS mapping-pairs offset.");
        }
        var runs = new List<Run>();
        long lcn = 0;
        long clusters = 0;
        while (position < attribute.Length)
        {
            byte header = attribute[position++];
            if (header == 0)
            {
                if (clusters == 0 || (ulong)(clusters - 1) != U64(attribute, 24))
                {
                    throw new ImageFormatException("NTFS mapping-pairs coverage disagrees with its VCN range.");
                }
                return runs;
            }
            int lengthBytes = header & 15;
            int offsetBytes = header >> 4;
            if (lengthBytes is < 1 or > 8 || offsetBytes > 8 || lengthBytes + offsetBytes > attribute.Length - position
                || (!allowSparse && offsetBytes == 0))
            {
                throw new ImageFormatException("Invalid or unsupported NTFS mapping-pairs encoding.");
            }
            ulong length = 0;
            for (int i = 0; i < lengthBytes; i++)
            {
                length |= (ulong)attribute[position++] << (i * 8);
            }
            long delta = 0;
            for (int i = 0; i < offsetBytes; i++)
            {
                delta |= (long)attribute[position++] << (i * 8);
            }
            if (offsetBytes is > 0 and < 8 && (attribute[position - 1] & 0x80) != 0)
            {
                delta |= -1L << (offsetBytes * 8);
            }
            if (length == 0 || length > (ulong)(volumeLength / cluster))
            {
                throw new ImageFormatException("NTFS data run has invalid cluster length.");
            }
            long bytes = checked((long)length * cluster);
            if (offsetBytes != 0)
            {
                lcn = checked(lcn + delta);
                long offset = checked(lcn * cluster);
                if (offset < 0 || offset > volumeLength || bytes > volumeLength - offset)
                {
                    throw new ImageFormatException("NTFS data run exceeds the bounded volume.");
                }
                runs.Add(new(offset, bytes));
            }
            else
            {
                runs.Add(new(-1, bytes));
            }
            clusters = checked(clusters + (long)length);
        }
        throw new ImageFormatException("NTFS mapping pairs have no bounded terminator.");
    }

    private static void ReadRuns(IReadOnlyBlockDevice device, List<Run> runs, long offset, Span<byte> output)
    {
        int written = 0;
        foreach (Run run in runs)
        {
            if (offset >= run.Length)
            {
                offset -= run.Length;
                continue;
            }
            if (run.Offset < 0)
            {
                throw new ImageFormatException("Sparse NTFS metadata is not supported.");
            }
            int count = (int)Math.Min(run.Length - offset, output.Length - written);
            Bounds.ReadExactly(device, checked(run.Offset + offset), output.Slice(written, count));
            written += count;
            offset = 0;
            if (written == output.Length)
            {
                return;
            }
        }
        throw new ImageFormatException("NTFS metadata stream is shorter than its declared initialized length.");
    }

    private static List<long> ValidateIndex(ReadOnlySpan<byte> bytes, int header)
    {
        if (bytes.Length < header + 16)
        {
            throw new ImageFormatException("Truncated NTFS index header.");
        }
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(bytes[header..]);
        uint used = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(header + 4)..]);
        uint allocated = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(header + 8)..]);
        if (start < 16 || start > used || used > allocated || allocated > bytes.Length - header)
        {
            throw new ImageFormatException("Invalid NTFS index node bounds.");
        }
        int position = checked(header + (int)start);
        int end = checked(header + (int)used);
        var children = new List<long>();
        while (position <= end - 16)
        {
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(position + 8)..]);
            ushort keyLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(position + 10)..]);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(position + 12)..]);
            if (length < 16 || length % 8 != 0 || length > end - position
                || keyLength > length - 16 || ((flags & 1) != 0 && (length < 24 || keyLength > length - 24)))
            {
                throw new ImageFormatException("Invalid/zero NTFS index-entry length.");
            }
            if ((flags & 2) != 0)
            {
                if ((flags & 1) != 0)
                {
                    children.Add(checked((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(position + length - 8)..])));
                }
                return children;
            }
            if ((flags & 1) != 0)
            {
                children.Add(checked((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(position + length - 8)..])));
            }
            position += length;
        }
        throw new ImageFormatException("NTFS index has no bounded terminal entry.");
    }

    private static string AttributeName(byte[] attribute) => attribute[9] == 0 ? ""
        : System.Text.Encoding.Unicode.GetString(attribute, U16(attribute, 10), attribute[9] * 2);

    private static void ValidateGraph(Dictionary<long, List<long>> graph, List<long> roots)
    {
        var visited = new HashSet<long>();
        var stack = new Stack<(long Node, int Depth)>();
        foreach (long child in roots)
        {
            stack.Push((child, 1));
        }
        while (stack.TryPop(out var item))
        {
            if (item.Depth > 256 || !visited.Add(item.Node) || !graph.TryGetValue(item.Node, out List<long>? children))
            {
                throw new ImageFormatException("NTFS index cycle, shared/missing node, or depth safety limit exceeded.");
            }
            foreach (long child in children)
            {
                stack.Push((child, item.Depth + 1));
            }
        }
    }

    private static int RecordSize(byte raw, int cluster)
    {
        int value = unchecked((sbyte)raw);
        return checked((int)(value < 0 ? 1L << -value : (long)value * cluster));
    }
}
