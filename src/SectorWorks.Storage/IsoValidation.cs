using System.Buffers.Binary;
using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal static class IsoValidation
{
    internal static bool Validate(IReadOnlyBlockDevice device, CancellationToken token, out bool hasRelocation)
    {
        hasRelocation = false;
        bool unusedExtent = false;
        long budget = 128L * 1024 * 1024;
        var roots = new List<(uint Block, uint Length)>();
        for (int i = 16; i < 272 && (i + 1L) * 2048 <= device.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            byte[] descriptor = Bounds.Read(device, i * 2048L, 2048);
            if (!descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8))
            {
                if (i > 32)
                {
                    break;
                }
                continue;
            }
            if (descriptor[0] is 1 or 2)
            {
                roots.Add((U32(descriptor, 158), U32(descriptor, 166)));
            }
        }
        foreach ((uint block, uint length) in roots.Distinct())
        {
            var queue = new Queue<(uint Block, uint Length, int Depth)>();
            var seen = new HashSet<uint>();
            var physicalParents = new Dictionary<uint, uint>();
            var logicalParents = new Dictionary<uint, uint>();
            var parentLinks = new Dictionary<uint, uint>();
            var relocated = new HashSet<uint>();
            var invalidSelf = new HashSet<uint>();
            queue.Enqueue((block, length, 0));
            bool susp = false;
            int skip = 0;
            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                (uint directoryBlock, uint directoryLength, int depth) = queue.Dequeue();
                if (depth > 256 || !seen.Add(directoryBlock) || seen.Count > 131072
                    || directoryLength > 16 * 1024 * 1024 || directoryLength < 34)
                {
                    throw new ImageFormatException("ISO directory cycle, invalid extent, depth or metadata safety cap exceeded.");
                }
                budget -= directoryLength;
                if (budget < 0)
                {
                    throw new ImageFormatException("ISO directory metadata exceeds the 128 MiB opening budget.");
                }
                byte[] data = Bounds.Read(device, checked(directoryBlock * 2048L), checked((int)directoryLength));
                for (int position = 0; position < data.Length;)
                {
                    token.ThrowIfCancellationRequested();
                    int recordLength = data[position];
                    if (recordLength == 0)
                    {
                        position = checked((position / 2048 + 1) * 2048);
                        continue;
                    }
                    if (recordLength < 34 || recordLength > data.Length - position || position % 2048 + recordLength > 2048)
                    {
                        throw new ImageFormatException("ISO directory record exceeds its sector or directory extent.");
                    }
                    byte[] record = data.AsSpan(position, recordLength).ToArray();
                    int identifierLength = record[32];
                    int systemUse = 33 + identifierLength + (identifierLength % 2 == 0 ? 1 : 0);
                    if (identifierLength == 0 || systemUse > recordLength || record[1] != 0 || record[26] != 0 || record[27] != 0)
                    {
                        throw new ImageFormatException("Invalid ISO identifier or unsupported extended/interleaved allocation.");
                    }
                    uint extent = U32(record, 2);
                    uint bytes = U32(record, 10);
                    bool outside = checked((long)extent * 2048 + bytes) > device.Length;
                    bool emptyFile = bytes == 0 && (record[25] & 2) == 0;
                    if (extent != BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(6))
                        || bytes != BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(14))
                        || outside && !emptyFile)
                    {
                        throw new ImageFormatException("ISO directory entry has inconsistent endian fields or out-of-range extent.");
                    }
                    unusedExtent |= outside && emptyFile;
                    if ((record[25] & 0x80) != 0)
                    {
                        throw new ImageFormatException("ISO multi-extent files are not supported.");
                    }
                    bool special = identifierLength == 1 && record[33] is 0 or 1;
                    if (depth == 0 && position == 0 && systemUse + 7 <= recordLength
                        && record[systemUse] == 'S' && record[systemUse + 1] == 'P'
                        && record[systemUse + 2] == 7 && record[systemUse + 4] == 0xbe && record[systemUse + 5] == 0xef)
                    {
                        susp = true;
                        skip = record[systemUse + 6];
                    }
                    var relocation = new Relocation();
                    if (susp && systemUse + skip < recordLength)
                    {
                        var continuations = new HashSet<long>();
                        long continuationBudget = 1024 * 1024;
                        ValidateSusp(device, record.AsSpan(systemUse + skip), continuations, ref continuationBudget, token, relocation);
                    }
                    if (relocation.Child is { } child)
                    {
                        if (special || relocation.Parent is not null || relocation.Relocated
                            || (record[25] & 2) != 0 || bytes != 0 || !logicalParents.TryAdd(child, directoryBlock))
                        { throw InvalidRelocation(); }
                    }
                    if (relocation.Parent is { } parent)
                    {
                        if (!special || record[33] != 1 || relocation.Relocated || !parentLinks.TryAdd(directoryBlock, parent))
                        { throw InvalidRelocation(); }
                    }
                    if (relocation.Relocated && (special || (record[25] & 2) == 0 || !relocated.Add(extent)))
                    { throw InvalidRelocation(); }
                    if (!special && (record[25] & 2) != 0)
                    {
                        if (!physicalParents.TryAdd(extent, directoryBlock)) { throw InvalidRelocation(); }
                        queue.Enqueue((extent, bytes, depth + 1));
                    }
                    if (position == 0 && (relocation.Relocated || relocation.Parent is not null || relocation.Child is not null))
                    { throw InvalidRelocation(); }
                    if (position == 0 && (!special || record[33] != 0 || extent != directoryBlock || bytes != directoryLength))
                    {
                        if (invalidSelf.Count >= 131072) { throw InvalidRelocation(); }
                        invalidSelf.Add(directoryBlock);
                    }
                    position += recordLength;
                }
            }
            if (logicalParents.Count != 0 || parentLinks.Count != 0 || relocated.Count != 0)
            {
                hasRelocation = true;
                if (logicalParents.Count != relocated.Count || parentLinks.Count != relocated.Count)
                { throw InvalidRelocation(); }
                foreach (uint target in relocated)
                {
                    token.ThrowIfCancellationRequested();
                    if (!logicalParents.TryGetValue(target, out uint logical) || !physicalParents.ContainsKey(target)
                        || !parentLinks.TryGetValue(target, out uint actual) || actual != logical || invalidSelf.Contains(target))
                    { throw InvalidRelocation(); }
                }
                var depthCache = new Dictionary<uint, int> { [block] = 0 };
                foreach (uint directory in seen) { ValidateParents(directory, 0); }

                int ValidateParents(uint directory, int depth)
                {
                    token.ThrowIfCancellationRequested();
                    if (depth > 256) { throw InvalidRelocation(); }
                    if (depthCache.TryGetValue(directory, out int cached))
                    { return cached >= 0 ? cached : throw InvalidRelocation(); }
                    if (!physicalParents.TryGetValue(directory, out uint parent)) { throw InvalidRelocation(); }
                    depthCache.Add(directory, -1);
                    int length = ValidateParents(parent, depth + 1);
                    if (logicalParents.TryGetValue(directory, out uint logical))
                    { length = Math.Max(length, ValidateParents(logical, depth + 1)); }
                    if (++length > 256) { throw InvalidRelocation(); }
                    return depthCache[directory] = length;
                }
            }
        }
        return unusedExtent;
    }

    private static void ValidateSusp(IReadOnlyBlockDevice device, ReadOnlySpan<byte> data, HashSet<long> continuations,
        ref long budget, CancellationToken token, Relocation relocation)
    {
        for (int position = 0; position < data.Length;)
        {
            token.ThrowIfCancellationRequested();
            if (data[position] == 0)
            {
                return;
            }
            if (data.Length - position < 4 || data[position + 2] < 4
                || data[position + 2] > data.Length - position || data[position + 3] != 1)
            {
                throw new ImageFormatException("Invalid/zero ISO SUSP record length or version.");
            }
            ReadOnlySpan<byte> record = data.Slice(position, data[position + 2]);
            string signature = Encoding.ASCII.GetString(record[..2]);
            if (signature == "ST")
            {
                return;
            }
            if (signature is "CL" or "PL")
            {
                if (record.Length != 12) { throw InvalidRelocation(); }
                uint block = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
                if (block != BinaryPrimitives.ReadUInt32BigEndian(record[8..]) || block < 16 || block * 2048L + 2048 > device.Length)
                { throw InvalidRelocation(); }
                if (signature == "CL" && relocation.Child is null) { relocation.Child = block; }
                else if (signature == "PL" && relocation.Parent is null) { relocation.Parent = block; }
                else { throw InvalidRelocation(); }
            }
            if (signature == "RE")
            {
                if (record.Length != 4 || relocation.Relocated) { throw InvalidRelocation(); }
                relocation.Relocated = true;
            }
            if (signature == "CE")
            {
                if (record.Length < 28)
                {
                    throw new ImageFormatException("Truncated ISO SUSP continuation record.");
                }
                uint block = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
                long start = checked(block * 2048L + offset);
                budget -= length;
                if (offset >= 2048 || length == 0 || budget < 0 || continuations.Count >= 32 || !continuations.Add(start)
                    || start > device.Length || length > device.Length - start)
                {
                    throw new ImageFormatException("ISO SUSP continuation cycle, invalid bounds or 1 MiB/32-hop safety budget exceeded.");
                }
                byte[] continuation = Bounds.Read(device, start, checked((int)length));
                ValidateSusp(device, continuation, continuations, ref budget, token, relocation);
            }
            if ((signature == "SP" && record.Length < 7) || (signature == "ER" && record.Length < 8)
                || (signature == "NM" && record.Length < 5) || (signature == "SL" && record.Length < 5)
                || (signature == "PX" && record.Length < 36) || (signature == "TF" && record.Length < 5)
                || (signature == "ES" && record.Length < 5))
            {
                throw new ImageFormatException($"Truncated ISO SUSP {signature} record.");
            }
            if (signature == "ER" && 8 + record[4] + record[5] + record[6] > record.Length)
            {
                throw new ImageFormatException("ISO SUSP extension identifier exceeds its record.");
            }
            if (signature == "ES")
            {
                throw new ImageFormatException("ISO SUSP extension-selector records are not supported.");
            }
            if (signature == "SL")
            {
                for (int component = 5; component < record.Length;)
                {
                    if (record.Length - component < 2 || record[component + 1] > record.Length - component - 2)
                    {
                        throw new ImageFormatException("ISO symbolic-link component exceeds its record.");
                    }
                    component += 2 + record[component + 1];
                }
            }
            if (signature == "TF")
            {
                int timestamps = System.Numerics.BitOperations.PopCount((uint)(record[4] & 0x7f));
                int timestampLength = (record[4] & 0x80) != 0 ? 17 : 7;
                if (5 + timestamps * timestampLength > record.Length)
                {
                    throw new ImageFormatException("ISO timestamp fields exceed their SUSP record.");
                }
            }
            position += record.Length;
        }
    }

    private static ImageFormatException InvalidRelocation() =>
        new("Rock Ridge relocated-directory records have invalid links, bounds or directory relationships.");

    private sealed class Relocation
    {
        internal uint? Child;
        internal uint? Parent;
        internal bool Relocated;
    }

}
