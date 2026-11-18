using System.Buffers.Binary;
using SectorWorks.Core;
using static SectorWorks.Storage.NtfsValidation;
using Attribute = SectorWorks.Storage.NtfsValidation.Attribute;

namespace SectorWorks.Storage;

/// <summary>In-memory logical views for independent validation. Never serialized
/// to an image: physical records, attribute lists and ownership remain intact.</summary>
internal static class NtfsLogicalAttributes
{
    internal static Attribute Combine(NtfsResolvedStream stream, int cluster)
    {
        Attribute first = stream.Extents[0];
        if (stream.Extents.Count == 1) { return first; }
        if (first.Bytes[8] != 1 || stream.Runs.Any(r => r.Offset < 0)
            || stream.Extents.Any(a => PartitionReader.U16(a.Bytes, 12) != 0 || PartitionReader.U16(a.Bytes, 34) != 0))
        { throw new ImageFormatException("Writer candidates require ordinary nonresident continuation extents."); }
        var mapping = new List<byte>();
        long previous = 0;
        long clusters = 0;
        foreach (Run run in stream.Runs)
        {
            long lcn = run.Offset / cluster;
            long length = run.Length / cluster;
            if (length <= 0 || run.Offset % cluster != 0 || run.Length % cluster != 0)
            { throw new ImageFormatException("Misaligned logical NTFS extent."); }
            long delta = checked(lcn - previous);
            int lengthBytes = 1;
            while (lengthBytes < 8 && (ulong)length >> (lengthBytes * 8) != 0) { lengthBytes++; }
            int deltaBytes = 1;
            while (deltaBytes < 8 && (delta >> (deltaBytes * 8 - 1)) is not (0 or -1)) { deltaBytes++; }
            mapping.Add(checked((byte)((deltaBytes << 4) | lengthBytes)));
            for (int i = 0; i < lengthBytes; i++) { mapping.Add(unchecked((byte)(length >> (8 * i)))); }
            for (int i = 0; i < deltaBytes; i++) { mapping.Add(unchecked((byte)(delta >> (8 * i)))); }
            clusters = checked(clusters + length);
            previous = lcn;
        }
        mapping.Add(0);
        int start = PartitionReader.U16(first.Bytes, 32);
        int size = checked((start + mapping.Count + 7) & ~7);
        if (size > 1024 * 1024) { throw new ImageFormatException("Logical NTFS mapping exceeds the bounded validation view."); }
        byte[] bytes = new byte[size];
        first.Bytes.AsSpan(0, start).CopyTo(bytes);
        mapping.CopyTo(bytes, start);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)size));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), checked((ulong)(clusters - 1)));
        return new(first.Type, bytes);
    }
}
