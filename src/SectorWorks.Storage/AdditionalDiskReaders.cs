using DiscUtils.Streams;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal sealed class VmdkContainerHandler : IReadOnlyContainerHandler
{
    public FormatDetection? Detect(IReadOnlyBlockDevice source)
    {
        if (source.Length < 4) { return null; }
        byte[] prefix = Bounds.Read(source, 0, (int)Math.Min(32, source.Length));
        return prefix.AsSpan().StartsWith("KDMV"u8) || prefix.AsSpan().StartsWith("COWD"u8)
            || prefix.AsSpan().StartsWith("# Disk DescriptorFile"u8)
            ? new("VMDK", 94, true, "VMware VMDK signature") : null;
    }

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        byte[] header = Bounds.Read(source, 0, 512);
        if (!header.AsSpan(0, 4).SequenceEqual("KDMV"u8) || U32(header, 4) != 1
            || (U32(header, 8) & ~3U) != 0 || header[72] != 0 || U16(header, 77) != 0)
        { throw new ImageFormatException("VMDK requires a clean standalone monolithic sparse version 1 image. Split, compressed and descriptor-only variants are unsupported."); }
        long capacity = checked((long)U64(header, 12));
        long grain = checked((long)U64(header, 20));
        long descriptorOffset = checked((long)U64(header, 28));
        long descriptorLength = checked((long)U64(header, 36));
        uint tableEntries = U32(header, 44);
        long directory = checked((long)U64(header, 56));
        long redundant = checked((long)U64(header, 48));
        long overhead = checked((long)U64(header, 64));
        if (capacity <= 0 || capacity > 134217728 || grain is < 8 or > 2048 || (grain & (grain - 1)) != 0
            || tableEntries is < 32 or > 4096 || (tableEntries & (tableEntries - 1)) != 0
            || descriptorOffset < 1 || descriptorLength is < 1 or > 128 || descriptorOffset + descriptorLength > overhead
            || directory < descriptorOffset + descriptorLength || directory >= overhead || overhead > source.Length / 512)
        { throw new ImageFormatException("VMDK sparse geometry, descriptor or grain metadata lies outside the supported bounds."); }
        long directoryEntries = 1 + (capacity - 1) / (grain * tableEntries);
        if (directoryEntries > 131072 || directory * 512 + directoryEntries * 4 > overhead * 512)
        { throw new ImageFormatException("VMDK grain directory exceeds the bounded metadata area."); }
        bool hasRedundant = (U32(header, 8) & 2) != 0;
        int directoryBytes = (int)directoryEntries * 4;
        int tableBytes = checked((int)tableEntries * 4);
        var metadata = new List<(long Start, long End)> { (0, (descriptorOffset + descriptorLength) * 512) };
        var allocations = new HashSet<long>();
        byte[] pointers = ReadMetadata(directory, directoryBytes);
        byte[]? redundantPointers = hasRedundant ? ReadMetadata(redundant, directoryBytes) : null;
        for (int i = 0; i < pointers.Length; i += 4)
        {
            context.Consume(0);
            long table = U32(pointers, i);
            long alternate = redundantPointers is null ? 0 : U32(redundantPointers, i);
            if (table == 0)
            {
                if (alternate != 0) { throw new ImageFormatException("VMDK primary and redundant grain directories disagree."); }
                continue;
            }
            byte[] grains = ReadMetadata(table, tableBytes);
            if (redundantPointers is not null && (alternate == 0 || !grains.AsSpan().SequenceEqual(ReadMetadata(alternate, tableBytes))))
            { throw new ImageFormatException("VMDK primary and redundant grain tables disagree."); }
            for (int slot = 0; slot < grains.Length; slot += 4)
            {
                long physical = U32(grains, slot);
                if (physical == 0) { continue; }
                long logical = ((i / 4L * tableEntries) + slot / 4) * grain;
                if (logical >= capacity || physical < overhead || (physical - overhead) % grain != 0
                    || physical + grain > source.Length / 512 || !allocations.Add(physical) || allocations.Count > 131072)
                { throw new ImageFormatException("VMDK grain is out of bounds, aliases another grain or exceeds the allocation budget."); }
            }
        }
        metadata.Sort();
        for (int i = 1; i < metadata.Count; i++)
        {
            if (metadata[i].Start < metadata[i - 1].End)
            { throw new ImageFormatException("VMDK grain tables or directories overlap reserved metadata."); }
        }

        byte[] ReadMetadata(long sector, int bytes)
        {
            long start = checked(sector * 512);
            long end = checked(start + bytes);
            if (sector < descriptorOffset + descriptorLength || end > overhead * 512)
            { throw new ImageFormatException("VMDK grain metadata exceeds its reserved area."); }
            context.Consume(bytes);
            metadata.Add((start, end));
            return Bounds.Read(source, start, bytes);
        }
        var disk = new DiscUtils.Vmdk.Disk(new DeviceStream(source, context), Ownership.Dispose);
        resources.Add(disk);
        if (disk.Capacity != capacity * 512)
        { throw new ImageFormatException("VMDK descriptor capacity disagrees with the sparse extent."); }
        var device = new StreamBlockDevice(disk.Content, context);
        resources.Add(device);
        properties["VMDK allocation"] = "Monolithic sparse";
        properties["VMDK grain size"] = (grain * 512).ToString();
        return device;
    }
}

internal sealed class ExtFileSystemHandler : IReadOnlyFileSystemHandler
{
    private const uint IncompatFileType = 0x2, IncompatExtents = 0x40, Incompat64Bit = 0x80, IncompatFlexGroups = 0x200;
    private const uint SupportedIncompatible = IncompatFileType | IncompatExtents | Incompat64Bit | IncompatFlexGroups;
    private const uint SupportedReadOnly = 0x47f;

    public IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (device.Length < 2048) { return null; }
        byte[] superblock = Bounds.Read(device, 1024, 1024);
        if (U16(superblock, 56) != 0xef53) { return null; }
        uint compatible = U32(superblock, 92), incompatible = U32(superblock, 96), readOnly = U32(superblock, 100);
        if ((incompatible & ~SupportedIncompatible) != 0 || (readOnly & ~SupportedReadOnly) != 0
            || U16(superblock, 58) != 1 || U32(superblock, 76) != 1 || U32(superblock, 24) > 2)
        { throw new ImageFormatException("EXT requires a clean revision 1 filesystem with supported feature flags and 1–4 KiB blocks."); }
        int blockSize = 1024 << (int)U32(superblock, 24);
        long count = U32(superblock, 4);
        if ((incompatible & Incompat64Bit) != 0)
        {
            count += (long)U32(superblock, 336) << 32;
            if (U16(superblock, 254) != 64)
            { throw new ImageFormatException("64-bit EXT group descriptors must be 64 bytes."); }
        }
        uint group = U32(superblock, 32), inodeCount = U32(superblock, 0), inodesPerGroup = U32(superblock, 40);
        int inodeSize = U16(superblock, 88);
        if (count <= 0 || count > device.Length / blockSize || group == 0 || group > blockSize * 8
            || 1 + (count - 1) / group > 32768 || inodeCount <= 2 || inodesPerGroup == 0
            || inodesPerGroup > blockSize * 8 || inodeCount > (1 + (count - 1) / group) * inodesPerGroup
            || inodeSize < 128 || inodeSize > blockSize || (inodeSize & (inodeSize - 1)) != 0
            || U32(superblock, 20) != (blockSize == 1024 ? 1U : 0U))
        { throw new ImageFormatException("EXT block or inode geometry exceeds the supported filesystem bounds."); }

        string format = (incompatible & (IncompatExtents | Incompat64Bit | IncompatFlexGroups)) != 0
            ? "ext4" : (compatible & 4) != 0 ? "ext3" : "ext2";
        var partition = new PartitionBlockDevice(device, 0, count * blockSize);
        var reader = new DiscUtils.Ext.ExtFileSystem(new DeviceStream(partition, context, ownsDevice: true));
        return new DiscFileSystemAdapter(reader, context, format, new()
        {
            ["Access"] = "Read-only ordinary files. Image links and special files are not extracted.",
            ["EXT block size"] = blockSize.ToString()
        }, caseSensitive: true);
    }
}

