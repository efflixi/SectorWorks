using System.Buffers.Binary;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal static class DiskWriteValidation
{
    private const long MiB = 1024 * 1024;
    private static readonly Guid BatId = new("2dc27766-f623-4200-9d64-115e9bfd4a08");
    private static readonly Guid MetadataId = new("8b7ca206-4790-4b9a-b8fe-575f050f886e");
    private static readonly Guid ParametersId = new("caa16737-fa36-4d43-b3b6-33f0aa44e76b");
    private static readonly Guid SizeId = new("2fa54224-cd1b-4876-b211-5dbed83bf4b8");
    private static readonly Guid Page83Id = new("beca12ab-b2e6-4523-93ef-c309e000c746");
    private static readonly Guid LogicalId = new("8141bf1d-a96f-4709-ba47-f233a8faab5f");
    private static readonly Guid PhysicalId = new("cda348c7-445d-4471-9cc9-e9885251c556");

    internal static void Validate(IReadOnlyBlockDevice source, string format)
    {
        if (format == "VHD") { Vhd(source); }
        else { Vhdx(source); }
    }

    private static void Vhd(IReadOnlyBlockDevice source)
    {
        uint type = ContainerValidation.Vhd(source);
        byte[] footer = Bounds.Read(source, source.Length - 512, 512);
        long capacity = checked((long)Be64(footer, 48));
        if (type == 2)
        {
            if (source.Length != capacity + 512 || Be64(footer, 16) != ulong.MaxValue)
            {
                throw new ImageFormatException("Fixed VHD must have an exact bounded payload and conventional footer.");
            }
            return;
        }
        if (!Bounds.Read(source, 0, 512).AsSpan().SequenceEqual(footer))
        {
            throw new ImageFormatException("Dynamic VHD leading and trailing footers disagree.");
        }
        long headerOffset = checked((long)Be64(footer, 16));
        byte[] header = Bounds.Read(source, headerOffset, 1024);
        long table = checked((long)Be64(header, 16));
        int entries = checked((int)Be32(header, 28));
        long block = Be32(header, 32);
        if (headerOffset % 512 != 0 || table % 512 != 0 || entries != (capacity + block - 1) / block
            || header.AsSpan(40, 984).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new NotSupportedException("VHD writer requires conventional dynamic metadata without parent/unknown fields.");
        }
        long bitmap = ((block / 512 + 7) / 8 + 511) / 512 * 512;
        var extents = new List<(long Start, long End)> { (0, 512), (headerOffset, headerOffset + 1024),
            (table, checked(table + ((entries * 4L + 511) / 512 * 512))) };
        for (int first = 0; first < entries; first += 16384)
        {
            int count = Math.Min(16384, entries - first);
            byte[] bat = Bounds.Read(source, checked(table + first * 4L), count * 4);
            for (int i = 0; i < count; i++)
            {
                uint sector = Be32(bat, i * 4);
                if (sector == uint.MaxValue) { continue; }
                long start = sector * 512L;
                long end = checked(start + bitmap + block);
                if (end > source.Length - 512) { throw new ImageFormatException("VHD BAT payload exceeds its container."); }
                extents.Add((start, end));
            }
        }
        NoOverlap(extents);
    }

    private static void Vhdx(IReadOnlyBlockDevice source)
    {
        ContainerValidation.Vhdx(source);
        byte[]? selected = null;
        for (int i = 1; i <= 2; i++)
        {
            byte[] header = Bounds.Read(source, i * 65536L, 4096);
            if (!header.AsSpan(0, 4).SequenceEqual("head"u8) || !Crc(header)) { throw new ImageFormatException("Both VHDX headers must be valid for writing."); }
            if (U16(header, 64) != 0 || U16(header, 66) != 1 || new Guid(header.AsSpan(48, 16)) != Guid.Empty
                || header.AsSpan(80).IndexOfAnyExcept((byte)0) >= 0)
            {
                throw new NotSupportedException("VHDX active logs or unknown header versions are not writable.");
            }
            if (selected is null || U64(header, 8) > U64(selected, 8)) { selected = header; }
        }
        byte[] regions = Bounds.Read(source, 3 * 65536, 65536);
        byte[] second = Bounds.Read(source, 4 * 65536, 65536);
        if (!Crc(regions) || !Crc(second) || !regions.AsSpan().SequenceEqual(second)
            || !regions.AsSpan(0, 4).SequenceEqual("regi"u8) || U32(regions, 8) != 2)
        {
            throw new NotSupportedException("VHDX writer requires matching valid region tables with only BAT and metadata regions.");
        }
        var regionMap = new Dictionary<Guid, (long Offset, long Length)>();
        var extents = new List<(long Start, long End)> { (0, MiB) };
        for (int i = 0; i < 2; i++)
        {
            int offset = 16 + i * 32;
            Guid id = new(regions.AsSpan(offset, 16));
            long start = checked((long)U64(regions, offset + 16));
            uint length = U32(regions, offset + 24);
            if ((id != BatId && id != MetadataId) || !regionMap.TryAdd(id, (start, length))
                || U32(regions, offset + 28) > 1 || start % MiB != 0 || length % MiB != 0)
            {
                throw new NotSupportedException("Unknown or nonconventional VHDX regions are not writable.");
            }
            extents.Add((start, checked(start + length)));
        }
        long log = checked((long)U64(selected!, 72));
        uint logLength = U32(selected!, 68);
        if (log % MiB != 0 || logLength == 0 || logLength % MiB != 0
            || log > source.Length || logLength > source.Length - log)
        {
            throw new ImageFormatException("VHDX log extent is invalid.");
        }
        extents.Add((log, checked(log + logLength)));
        (long metadataStart, long metadataLength) = regionMap[MetadataId];
        byte[] metadata = Bounds.Read(source, metadataStart, 65536);
        if (U16(metadata, 10) != 5) { throw new NotSupportedException("Unknown VHDX metadata items are not write-qualified, even when optional."); }
        var values = new Dictionary<Guid, byte[]>();
        var metadataExtents = new List<(long Start, long End)> { (0, 65536) };
        for (int i = 0; i < 5; i++)
        {
            int offset = 32 + i * 32;
            Guid id = new(metadata.AsSpan(offset, 16));
            uint start = U32(metadata, offset + 16);
            uint length = U32(metadata, offset + 20);
            uint flags = U32(metadata, offset + 24);
            int expected = id == ParametersId || id == SizeId ? 8 : id == Page83Id ? 16 : id == LogicalId || id == PhysicalId ? 4 : 0;
            uint expectedFlags = id == ParametersId ? 4u : 6u;
            if (expected == 0 || length != expected || flags != expectedFlags || start > metadataLength || length > metadataLength - start
                || !values.TryAdd(id, Bounds.Read(source, checked(metadataStart + start), checked((int)length))))
            {
                throw new NotSupportedException("Unknown VHDX metadata or item flags are not write-qualified.");
            }
            metadataExtents.Add((start, (long)start + length));
        }
        NoOverlap(metadataExtents);
        uint block = U32(values[ParametersId], 0);
        uint fileFlags = U32(values[ParametersId], 4);
        uint logical = U32(values[LogicalId], 0);
        uint physical = U32(values[PhysicalId], 0);
        ulong capacity = U64(values[SizeId], 0);
        if (fileFlags > 1 || logical != 512 || physical is not (512 or 4096))
        {
            throw new NotSupportedException("VHDX writer supports parentless 512/512 and 512/4096 sector layouts only; 4Kn remains unqualified.");
        }
        ulong blocks = (capacity + block - 1) / block;
        ulong ratio = (1UL << 23) * logical / block;
        ulong entries = blocks + (blocks - 1) / ratio;
        (long batStart, long batLength) = regionMap[BatId];
        if (entries * 8 > (ulong)batLength) { throw new ImageFormatException("VHDX BAT is too short."); }
        for (ulong first = 0; first < entries; first += 8192)
        {
            int count = (int)Math.Min(8192, entries - first);
            byte[] bat = Bounds.Read(source, checked(batStart + (long)first * 8), count * 8);
            for (int i = 0; i < count; i++)
            {
                ulong entry = U64(bat, i * 8);
                bool bitmap = (first + (ulong)i + 1) % (ratio + 1) == 0;
                ulong state = entry & 7;
                ulong address = entry & 0xfffffffffff00000;
                if ((entry & 0xffff8) != 0 || (bitmap && entry != 0)
                    || (!bitmap && (state is not (0 or 2 or 6)
                        || (state != 6 && address != 0))))
                {
                    throw new NotSupportedException("VHDX undefined/unmapped, partially present, reserved or inconsistent BAT states are not write-qualified.");
                }
                if (state != 6) { continue; }
                long start = checked((long)address);
                long end = checked(start + block);
                if (start == 0 || end > source.Length) { throw new ImageFormatException("VHDX BAT payload is outside its container."); }
                extents.Add((start, end));
            }
        }
        NoOverlap(extents);
    }

    private static void NoOverlap(List<(long Start, long End)> extents)
    {
        long end = 0;
        foreach (var extent in extents.OrderBy(extent => extent.Start))
        {
            if (extent.Start < end || extent.End <= extent.Start) { throw new ImageFormatException("Container allocation/metadata extents overlap."); }
            end = extent.End;
        }
    }

    private static bool Crc(byte[] bytes)
    {
        byte[] copy = (byte[])bytes.Clone();
        uint expected = U32(copy, 4);
        copy.AsSpan(4, 4).Clear();
        uint crc = uint.MaxValue;
        foreach (byte value in copy)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) { crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0x82f63b78u); }
        }
        return ~crc == expected;
    }
    private static uint Be32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
    private static ulong Be64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset));
}
