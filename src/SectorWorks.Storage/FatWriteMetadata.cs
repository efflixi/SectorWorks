using System.Buffers.Binary;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal static class FatWriteMetadata
{
    internal static bool HasConventionalMetadata(IImageSession image, byte[] boot, bool fat32)
    {
        int reserved = U16(boot, 14);
        long fatSectors = fat32 ? U32(boot, 36) : U16(boot, 22);
        long rootSector = reserved + boot[16] * fatSectors;
        if (fat32)
        {
            int info = U16(boot, 48);
            int backup = U16(boot, 50);
            if (info <= 0 || backup <= 0 || info >= reserved || backup + info >= reserved) { return false; }
            byte[] data = Read(image.Device, info * 512L, 512);
            if (U32(data, 0) != 0x41615252 || U32(data, 484) != 0x61417272 || U32(data, 508) != 0xaa550000
                || !Read(image.Device, backup * 512L, 512).AsSpan().SequenceEqual(boot)
                || !Read(image.Device, (backup + info) * 512L, 512).AsSpan().SequenceEqual(data)) { return false; }
            if (boot[13] == 0) { return false; }
            long clusters = (U32(boot, 32) - reserved - boot[16] * fatSectors) / boot[13];
            uint free = U32(data, 488);
            uint next = U32(data, 492);
            if ((free != uint.MaxValue && free > clusters)
                || (next != uint.MaxValue && (next < 2 || next >= clusters + 2))) { return false; }
            uint root = U32(boot, 44);
            rootSector += checked((root - 2L) * boot[13]);
        }
        byte[] label = boot.AsSpan(fat32 ? 71 : 43, 11).ToArray();
        bool unnamed = label.AsSpan().SequenceEqual("NO NAME    "u8) || label.All(value => value == 32);
        int rootBytes = fat32 ? boot[13] * 512 : U16(boot, 17) * 32;
        byte[] directory = Read(image.Device, rootSector * 512, rootBytes);
        bool found = false;
        for (int i = 0; i < directory.Length; i += 32)
        {
            if (directory[i] == 0) { break; }
            if (directory[i] != 0xe5 && directory[i + 11] == 8)
            {
                if (found || unnamed || !directory.AsSpan(i, 11).SequenceEqual(label)) { return false; }
                found = true;
            }
        }
        return unnamed || found;
    }

    internal static void UpdateFat32FreeSpace(Stream stream, CancellationToken token)
    {
        var boot = new byte[512];
        stream.Position = 0;
        stream.ReadExactly(boot);
        if (U16(boot, 22) != 0) { return; }
        uint sectors = U32(boot, 32);
        uint fatSectors = U32(boot, 36);
        long dataSectors = sectors - U16(boot, 14) - boot[16] * (long)fatSectors;
        long clusters = dataSectors / boot[13];
        stream.Position = U16(boot, 14) * 512L + 8;
        byte[] buffer = new byte[65536];
        uint free = 0;
        uint next = uint.MaxValue;
        long index = 2;
        while (index < clusters + 2)
        {
            token.ThrowIfCancellationRequested();
            int count = checked((int)Math.Min(buffer.Length / 4, clusters + 2 - index));
            stream.ReadExactly(buffer.AsSpan(0, count * 4));
            for (int i = 0; i < count; i++, index++)
            {
                if ((U32(buffer, i * 4) & 0x0fffffff) == 0)
                {
                    free++;
                    if (next == uint.MaxValue) { next = checked((uint)index); }
                }
            }
        }
        byte[] summary = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(summary, free);
        BinaryPrimitives.WriteUInt32LittleEndian(summary.AsSpan(4), next);
        foreach (long sector in new long[] { U16(boot, 48), U16(boot, 50) + U16(boot, 48) })
        {
            token.ThrowIfCancellationRequested();
            stream.Position = sector * 512 + 488;
            stream.Write(summary);
        }
    }

    private static byte[] Read(IReadOnlyBlockDevice device, long offset, int count)
    {
        var bytes = new byte[count];
        int total = 0;
        while (total < count)
        {
            int read = device.Read(offset + total, bytes.AsSpan(total, Math.Min(65536, count - total)));
            if (read == 0) { throw new EndOfStreamException(); }
            total += read;
        }
        return bytes;
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
