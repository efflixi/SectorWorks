using System.Buffers.Binary;
using System.Text;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal static class PartitionReader
{
    internal static IReadOnlyList<PartitionInfo> Read(IReadOnlyBlockDevice device, List<string> warnings,
        Dictionary<string, string> properties, CancellationToken token)
    {
        if (device.Length < 512)
        {
            return [];
        }
        byte[] mbr = Bounds.Read(device, 0, 512);
        if (mbr[510] != 0x55 || mbr[511] != 0xaa)
        {
            return [];
        }
        // A filesystem boot sector also has 55AA; its BPB takes precedence.
        if (FatFileSystem.LooksLikeFat(mbr) || mbr.AsSpan(3, 8).SequenceEqual("NTFS    "u8))
        {
            return [];
        }
        bool protective = Enumerable.Range(0, 4).Any(i => mbr[446 + i * 16 + 4] == 0xee);
        if (protective)
        {
            properties["Protective MBR"] = "Present";
            if (Enumerable.Range(0, 4).Any(i => mbr[446 + i * 16 + 4] is not (0 or 0xee)))
            {
                warnings.Add("Hybrid MBR/GPT detected; only the validated GPT partition view is exposed.");
            }
            return ReadGpt(device, warnings, properties, token);
        }
        var partitions = new List<PartitionInfo>();
        for (int i = 0; i < 4; i++)
        {
            token.ThrowIfCancellationRequested();
            int entry = 446 + i * 16;
            byte type = mbr[entry + 4];
            uint start = U32(mbr, entry + 8);
            uint count = U32(mbr, entry + 12);
            if (type == 0 && start == 0 && count == 0)
            {
                continue;
            }
            if (type == 0 || count == 0 || start == 0 || mbr[entry] is not (0 or 0x80))
            {
                throw new ImageFormatException($"Invalid MBR primary partition {i + 1}: type, active flag or extent.");
            }
            long offset = checked((long)start * device.LogicalSectorSize);
            long length = checked((long)count * device.LogicalSectorSize);
            CheckExtent(device, offset, length, $"MBR partition {i + 1}");
            partitions.Add(new(i + 1, "MBR", offset, length, $"0x{type:X2}", "", mbr[entry] == 0x80));
            if (type is 0x05 or 0x0f or 0x85)
            {
                warnings.Add($"MBR partition {i + 1} is an extended container; logical partition traversal is not supported.");
            }
        }
        ValidateOverlap(partitions);
        if (partitions.Count > 0)
        {
            Unallocated(device, partitions, 512, device.Length, properties);
        }
        return partitions.AsReadOnly();
    }

    private sealed record Header(ulong Current, ulong Backup, ulong First, ulong Last, Guid DiskId,
        ulong Entries, uint Count, uint EntrySize, uint EntriesCrc);

    private static Header ReadHeader(IReadOnlyBlockDevice device, ulong lba)
    {
        int sector = device.LogicalSectorSize;
        if (lba >= (ulong)(device.Length / sector))
        {
            throw new ImageFormatException("GPT header LBA is outside the device.");
        }
        byte[] bytes = Bounds.Read(device, checked((long)lba * sector), sector);
        if (!bytes.AsSpan(0, 8).SequenceEqual("EFI PART"u8))
        {
            throw new ImageFormatException("GPT signature is missing.");
        }
        uint size = U32(bytes, 12);
        if (U32(bytes, 8) != 0x00010000 || size < 92 || size > sector || U32(bytes, 20) != 0)
        {
            throw new ImageFormatException("Unsupported or invalid GPT header revision/size/reserved field.");
        }
        uint expected = U32(bytes, 16);
        bytes.AsSpan(16, 4).Clear();
        if (Crc32(bytes.AsSpan(0, (int)size)) != expected)
        {
            throw new ImageFormatException("GPT header CRC32 mismatch.");
        }
        var header = new Header(U64(bytes, 24), U64(bytes, 32), U64(bytes, 40), U64(bytes, 48),
            new Guid(bytes.AsSpan(56, 16)), U64(bytes, 72), U32(bytes, 80), U32(bytes, 84), U32(bytes, 88));
        ulong sectors = (ulong)(device.Length / sector);
        if (header.Current != lba || header.Backup >= sectors || header.Backup == lba
            || header.First > header.Last || header.First < 2 || header.Last >= sectors - 1
            || header.Count == 0 || header.Count > 16384 || header.EntrySize < 128 || header.EntrySize > 4096
            || (header.EntrySize & (header.EntrySize - 1)) != 0 || (ulong)header.Count * header.EntrySize > 16 * 1024 * 1024)
        {
            throw new ImageFormatException("Invalid GPT bounds/counts, or metadata exceeds the 16 MiB / 16384-entry safety cap.");
        }
        ulong arraySectors = ((ulong)header.Count * header.EntrySize + (uint)sector - 1) / (uint)sector;
        if (header.Entries < 2 || header.Entries >= sectors || arraySectors > sectors - header.Entries
            || (header.Entries <= header.Last && header.Entries + arraySectors > header.First)
            || (lba >= header.Entries && lba < header.Entries + arraySectors)
            || (header.Backup >= header.Entries && header.Backup < header.Entries + arraySectors)
            || (lba >= header.First && lba <= header.Last)
            || (header.Backup >= header.First && header.Backup <= header.Last))
        {
            throw new ImageFormatException("GPT metadata overlaps usable space or extends outside the image.");
        }
        return header;
    }

    private static byte[] ReadEntries(IReadOnlyBlockDevice device, Header header)
    {
        byte[] bytes = Bounds.Read(device, checked((long)header.Entries * device.LogicalSectorSize),
            checked((int)(header.Count * header.EntrySize)));
        if (Crc32(bytes) != header.EntriesCrc)
        {
            throw new ImageFormatException("GPT partition-entry array CRC32 mismatch.");
        }
        return bytes;
    }

    private static IReadOnlyList<PartitionInfo> ReadGpt(IReadOnlyBlockDevice device, List<string> warnings,
        Dictionary<string, string> properties, CancellationToken token)
    {
        Header header = ReadHeader(device, 1);
        byte[] entries = ReadEntries(device, header);
        properties["GPT disk GUID"] = header.DiskId.ToString();
        var partitions = new List<PartitionInfo>();
        var ids = new HashSet<Guid>();
        for (int i = 0; i < header.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> entry = entries.AsSpan(checked(i * (int)header.EntrySize), (int)header.EntrySize);
            Guid type = new(entry[..16]);
            if (type == Guid.Empty)
            {
                continue;
            }
            Guid id = new(entry.Slice(16, 16));
            ulong first = BinaryPrimitives.ReadUInt64LittleEndian(entry[32..]);
            ulong last = BinaryPrimitives.ReadUInt64LittleEndian(entry[40..]);
            if (id == Guid.Empty || !ids.Add(id) || first < header.First || last > header.Last || last < first)
            {
                throw new ImageFormatException($"Invalid GPT partition {i + 1}: unique ID or usable extent.");
            }
            long offset = checked((long)first * device.LogicalSectorSize);
            long length = checked((long)(last - first + 1) * device.LogicalSectorSize);
            CheckExtent(device, offset, length, $"GPT partition {i + 1}");
            string name = Encoding.Unicode.GetString(entry.Slice(56, 72)).TrimEnd('\0');
            partitions.Add(new(i + 1, "GPT", offset, length, type.ToString(), name, false, id));
            properties[$"GPT partition {i + 1} attributes"] =
                $"0x{BinaryPrimitives.ReadUInt64LittleEndian(entry[48..]):X16}";
        }
        ValidateOverlap(partitions);
        try
        {
            Header backup = ReadHeader(device, header.Backup);
            byte[] backupEntries = ReadEntries(device, backup);
            if (backup.Backup != 1 || backup.DiskId != header.DiskId || backup.First != header.First
                || backup.Last != header.Last || backup.Count != header.Count || backup.EntrySize != header.EntrySize
                || !backupEntries.AsSpan().SequenceEqual(entries) || header.Backup != (ulong)(device.Length / device.LogicalSectorSize - 1))
            {
                warnings.Add("Primary and backup GPT are inconsistent; browsing uses the validated primary only.");
            }
            else
            {
                properties["GPT backup"] = "Header and entry CRCs valid; matches primary.";
            }
        }
        catch (ImageFormatException ex)
        {
            warnings.Add($"Backup GPT is damaged: {ex.Message} Browsing uses the validated primary only.");
        }
        Unallocated(device, partitions, checked((long)header.First * device.LogicalSectorSize),
            checked(((long)header.Last + 1) * device.LogicalSectorSize), properties);
        return partitions.AsReadOnly();
    }

    private static void Unallocated(IReadOnlyBlockDevice device, List<PartitionInfo> partitions,
        long start, long end, Dictionary<string, string> properties)
    {
        int index = 0;
        foreach (PartitionInfo partition in partitions.OrderBy(p => p.Offset))
        {
            if (partition.Offset > start)
            {
                properties[$"Unallocated region {++index}"] = $"Offset {start}, length {partition.Offset - start} bytes";
            }
            start = checked(partition.Offset + partition.Length);
        }
        if (start < Math.Min(end, device.Length))
        {
            properties[$"Unallocated region {++index}"] = $"Offset {start}, length {end - start} bytes";
        }
        properties["Unallocated region count"] = index.ToString();
    }

    private static void ValidateOverlap(List<PartitionInfo> partitions)
    {
        long end = 0;
        foreach (PartitionInfo partition in partitions.OrderBy(p => p.Offset))
        {
            if (partition.Offset < end)
            {
                throw new ImageFormatException($"Overlapping {partition.Table} partition {partition.Index}.");
            }
            end = checked(partition.Offset + partition.Length);
        }
    }

    private static void CheckExtent(IReadOnlyBlockDevice device, long offset, long length, string label)
    {
        if (offset < 0 || length <= 0 || offset > device.Length || length > device.Length - offset)
        {
            throw new ImageFormatException($"{label} extends outside the image.");
        }
    }

    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }
        }
        return ~crc;
    }

    internal static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    internal static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    internal static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
}
