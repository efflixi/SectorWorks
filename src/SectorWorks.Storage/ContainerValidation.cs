using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal static class ContainerValidation
{
    internal static uint Vhd(IReadOnlyBlockDevice source)
    {
        if (source.Length < 512)
        {
            throw new ImageFormatException("Truncated VHD footer.");
        }
        byte[] footer = Bounds.Read(source, source.Length - 512, 512);
        if (!footer.AsSpan(0, 8).SequenceEqual("conectix"u8))
        {
            throw new ImageFormatException("VHD trailing footer is missing.");
        }
        uint checksum = Be32(footer, 64);
        footer.AsSpan(64, 4).Clear();
        if (Checksum(footer) != checksum || Be32(footer, 12) != 0x00010000)
        {
            throw new ImageFormatException("VHD footer checksum/version is invalid.");
        }
        ulong capacity = Be64(footer, 48);
        uint type = Be32(footer, 60);
        if (capacity == 0 || capacity > long.MaxValue || capacity % 512 != 0 || type is not (2 or 3 or 4))
        {
            throw new ImageFormatException("Unsupported VHD type or virtual capacity.");
        }
        if (type == 4)
        {
            throw new ImageFormatException("Differencing VHD requires a parent image. Automatic external parent-path resolution is disabled.");
        }
        if (type == 2)
        {
            if (capacity > (ulong)(source.Length - 512))
            {
                throw new ImageFormatException("Fixed VHD virtual capacity exceeds the source payload.");
            }
            return type;
        }
        ulong headerOffset = Be64(footer, 16);
        if (headerOffset > (ulong)(source.Length - 1024))
        {
            throw new ImageFormatException("VHD dynamic header extends outside the source.");
        }
        byte[] header = Bounds.Read(source, (long)headerOffset, 1024);
        uint headerChecksum = Be32(header, 36);
        header.AsSpan(36, 4).Clear();
        ulong table = Be64(header, 16);
        uint entries = Be32(header, 28);
        uint block = Be32(header, 32);
        if (!header.AsSpan(0, 8).SequenceEqual("cxsparse"u8) || Checksum(header) != headerChecksum
            || Be32(header, 24) != 0x00010000 || block is < 512 or > 256 * 1024 * 1024
            || (block & (block - 1)) != 0 || entries == 0 || entries > 4 * 1024 * 1024
            || entries < (capacity + block - 1) / block || table > (ulong)source.Length
            || (ulong)entries * 4 > (ulong)source.Length - table)
        {
            throw new ImageFormatException("Invalid VHD dynamic metadata, checksum or BAT resource limits (16 MiB).");
        }
        return type;
    }

    internal static void Vhdx(IReadOnlyBlockDevice source)
    {
        if (source.Length < 320 * 1024)
        {
            throw new ImageFormatException("Truncated VHDX headers and region tables.");
        }
        byte[]? selected = null;
        for (int i = 1; i <= 2; i++)
        {
            byte[] header = Bounds.Read(source, i * 65536L, 4096);
            if (!header.AsSpan(0, 4).SequenceEqual("head"u8) || !ValidCrc32C(header))
            {
                continue;
            }
            if (selected is null || U64(header, 8) > U64(selected, 8))
            {
                selected = header;
            }
        }
        if (selected is null || U16(selected, 66) != 1)
        {
            throw new ImageFormatException("VHDX has no valid supported header/checksum.");
        }
        if (new Guid(selected.AsSpan(48, 16)) != Guid.Empty)
        {
            throw new ImageFormatException("VHDX requires transaction-log replay; recovery is not performed on read-only source images.");
        }
        bool validRegions = false;
        for (int i = 3; i <= 4; i++)
        {
            byte[] regions = Bounds.Read(source, i * 65536L, 65536);
            if (!regions.AsSpan(0, 4).SequenceEqual("regi"u8) || !ValidCrc32C(regions))
            {
                continue;
            }
            uint count = U32(regions, 8);
            if (count > 2047)
            {
                throw new ImageFormatException("VHDX region count exceeds table bounds.");
            }
            for (int entry = 0; entry < count; entry++)
            {
                int offset = 16 + entry * 32;
                ulong start = U64(regions, offset + 16);
                uint length = U32(regions, offset + 24);
                Guid id = new(regions.AsSpan(offset, 16));
                if (start > (ulong)source.Length || length > (ulong)source.Length - start
                    || length == 0 || length > 64 * 1024 * 1024 || start % (1024 * 1024) != 0)
                {
                    throw new ImageFormatException("VHDX region is out of bounds or exceeds the 64 MiB metadata cap.");
                }
                if (id == new Guid("8b7ca206-4790-4b9a-b8fe-575f050f886e"))
                {
                    ValidateVhdxMetadata(source, (long)start, length);
                }
            }
            validRegions = true;
        }
        if (!validRegions)
        {
            throw new ImageFormatException("VHDX has no valid region-table checksum.");
        }
    }

    private static void ValidateVhdxMetadata(IReadOnlyBlockDevice source, long start, uint length)
    {
        if (length < 65536)
        {
            throw new ImageFormatException("Truncated VHDX metadata region.");
        }
        byte[] metadata = Bounds.Read(source, start, 65536);
        int count = U16(metadata, 10);
        if (!metadata.AsSpan(0, 8).SequenceEqual("metadata"u8) || count > 2047)
        {
            throw new ImageFormatException("Invalid VHDX metadata table.");
        }
        uint? block = null;
        ulong? capacity = null;
        uint? logicalSector = null;
        uint? physicalSector = null;
        var ids = new HashSet<Guid>();
        for (int i = 0; i < count; i++)
        {
            int entry = 32 + i * 32;
            uint offset = U32(metadata, entry + 16);
            uint size = U32(metadata, entry + 20);
            if (offset < 65536 || offset > length || size > length - offset || size > 1024 * 1024)
            {
                throw new ImageFormatException("VHDX metadata item is outside its region or exceeds the 1 MiB safety cap.");
            }
            Guid id = new(metadata.AsSpan(entry, 16));
            if (!ids.Add(id))
            {
                throw new ImageFormatException("Duplicate VHDX metadata item.");
            }
            if (id == new Guid("caa16737-fa36-4d43-b3b6-33f0aa44e76b"))
            {
                if (size != 8)
                {
                    throw new ImageFormatException("Invalid VHDX file parameters.");
                }
                byte[] parameters = Bounds.Read(source, start + offset, 8);
                uint blockSize = U32(parameters, 0);
                block = blockSize;
                if (blockSize is < 1024 * 1024 or > 256 * 1024 * 1024 || (blockSize & (blockSize - 1)) != 0)
                {
                    throw new ImageFormatException("Unsupported VHDX block size.");
                }
                if ((U32(parameters, 4) & 2) != 0)
                {
                    throw new ImageFormatException("Differencing VHDX requires a parent image. Automatic external parent-path resolution is disabled.");
                }
            }
            else if (id == new Guid("2fa54224-cd1b-4876-b211-5dbed83bf4b8"))
            {
                if (size != 8)
                {
                    throw new ImageFormatException("Invalid VHDX disk-size metadata.");
                }
                capacity = U64(Bounds.Read(source, start + offset, 8), 0);
            }
            else if (id == new Guid("8141bf1d-a96f-4709-ba47-f233a8faab5f")
                || id == new Guid("cda348c7-445d-4471-9cc9-e9885251c556"))
            {
                if (size != 4)
                {
                    throw new ImageFormatException("Invalid VHDX sector-size metadata.");
                }
                uint sector = U32(Bounds.Read(source, start + offset, 4), 0);
                if (sector is not (512 or 4096))
                {
                    throw new ImageFormatException("Unsupported VHDX logical/physical sector size.");
                }
                if (id == new Guid("8141bf1d-a96f-4709-ba47-f233a8faab5f"))
                {
                    logicalSector = sector;
                }
                else
                {
                    physicalSector = sector;
                }
            }
        }
        if (block is null || capacity is null || logicalSector is null || physicalSector is null
            || capacity == 0 || capacity > 64UL * 1024 * 1024 * 1024 * 1024 || capacity % logicalSector != 0)
        {
            throw new ImageFormatException("Missing or inconsistent VHDX required metadata or virtual-size limit (64 TiB).");
        }
        ulong payloadBlocks = (capacity.Value + block.Value - 1) / block.Value;
        ulong chunkRatio = (1UL << 23) * logicalSector.Value / block.Value;
        ulong batEntries = payloadBlocks + (payloadBlocks - 1) / chunkRatio;
        if (batEntries > 64 * 1024 * 1024 / 8)
        {
            throw new ImageFormatException("VHDX virtual capacity requires a BAT larger than the 64 MiB safety cap.");
        }
    }

    private static bool ValidCrc32C(byte[] bytes)
    {
        uint expected = U32(bytes, 4);
        bytes.AsSpan(4, 4).Clear();
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0x82f63b78u);
            }
        }
        return ~crc == expected;
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        foreach (byte value in data)
        {
            sum += value;
        }
        return ~sum;
    }
    private static uint Be32(byte[] data, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    private static ulong Be64(byte[] data, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset));
}
