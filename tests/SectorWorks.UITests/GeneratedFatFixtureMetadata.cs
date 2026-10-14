using System.Buffers.Binary;

namespace SectorWorks.UITests;

internal static class GeneratedFatFixtureMetadata
{
    // DiscUtils-generated read fixtures omit root labels and FAT32 auxiliary sectors.
    // Complete only disposable generated/copied test fixtures, never product source images.
    public static void Complete(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        byte[] boot = new byte[512];
        stream.ReadExactly(boot);
        bool fat32 = Read16(boot, 17) == 0;
        int reserved = Read16(boot, 14);
        uint fatSectors = fat32 ? Read32(boot, 36) : Read16(boot, 22);
        long rootSector = reserved + boot[16] * (long)fatSectors;
        if (fat32) { rootSector += (Read32(boot, 44) - 2L) * boot[13]; }
        int rootBytes = fat32 ? boot[13] * 512 : Read16(boot, 17) * 32;
        byte[] root = new byte[rootBytes];
        stream.Position = rootSector * 512;
        stream.ReadExactly(root);
        int slot = Enumerable.Range(0, rootBytes / 32).First(index => root[index * 32] == 0) * 32;
        boot.AsSpan(fat32 ? 71 : 43, 11).CopyTo(root.AsSpan(slot));
        root[slot + 11] = 8;
        stream.Position = rootSector * 512;
        stream.Write(root);
        if (fat32)
        {
            int backup = Read16(boot, 50);
            stream.Position = backup * 512L;
            stream.Write(boot);
            long clusters = (Read32(boot, 32) - reserved - boot[16] * (long)fatSectors) / boot[13];
            byte[] fat = new byte[checked((int)(fatSectors * 512))];
            stream.Position = reserved * 512L;
            stream.ReadExactly(fat);
            uint free = 0;
            uint next = uint.MaxValue;
            for (uint cluster = 2; cluster < clusters + 2; cluster++)
            {
                if ((Read32(fat, checked((int)cluster * 4)) & 0x0fffffff) == 0)
                {
                    free++;
                    if (next == uint.MaxValue) { next = cluster; }
                }
            }
            byte[] info = new byte[512];
            Write32(info, 0, 0x41615252);
            Write32(info, 484, 0x61417272);
            Write32(info, 488, free);
            Write32(info, 492, next);
            Write32(info, 508, 0xaa550000);
            foreach (long sector in new long[] { Read16(boot, 48), backup + Read16(boot, 48) })
            {
                stream.Position = sector * 512;
                stream.Write(info);
            }
        }
        stream.Flush(true);
    }

    private static ushort Read16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
