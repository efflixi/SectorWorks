using System.Buffers.Binary;
using System.Text;
using DiscUtils.Iso9660;
using DiscUtils.Ntfs;
using DiscUtils.Streams;
using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class FileSystemTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NtfsNonProgressingAttributesAndRunlistsFailBeforeDependencyParser(bool runlist)
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            var boot = new byte[512];
            file.ReadExactly(boot);
            int cluster = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11)) * boot[13];
            long mft = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(boot.AsSpan(48)) * cluster);
            file.Position = mft;
            var record = new byte[1024];
            file.ReadExactly(record);
            int attribute = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(20));
            if (runlist)
            {
                while (BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attribute)) != 0x80)
                {
                    attribute += (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attribute + 4));
                }
                int mapping = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attribute + 32));
                record[attribute + mapping] = 0x10;
            }
            else
            {
                record.AsSpan(attribute + 4, 4).Clear();
            }
            file.Position = mft;
            file.Write(record);
        }
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RockRidgeNamesAreDetectedAndNonProgressingSuspIsRejected(bool corrupt)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("rock-ridge.iso");
        var builder = new CDBuilder { UseJoliet = false, VolumeIdentifier = "ROCKRIDGE" };
        builder.AddFile("FILE.TXT", Encoding.UTF8.GetBytes("Rock Ridge sentinel"));
        using (SparseStream iso = builder.Build())
        using (FileStream file = File.Create(path))
        {
            iso.CopyTo(file);
        }
        AddRockRidgeRecords(path, corrupt);
        if (corrupt)
        {
            Assert.Contains("SUSP", Assert.Throws<ImageFormatException>(() =>
                new ImageOpener().OpenReadOnly(path)).Message, StringComparison.Ordinal);
            return;
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
        Assert.Equal("RockRidge", fs.Properties["Active naming variant"]);
        Assert.True(fs.IsCaseSensitive);
        Assert.Contains(fs.ReadDirectory("/"), e => e.Name == "Long-name.txt");
        using var text = new StreamReader(fs.OpenRead("/Long-name.txt"));
        Assert.Equal("Rock Ridge sentinel", text.ReadToEnd());
    }

    [Fact]
    public void NtfsUnicodeNameIsPreservedAndReadable()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        byte[] before = Encoding.Unicode.GetBytes("SENTINEL.TXT");
        byte[] after = Encoding.Unicode.GetBytes("SENTINEλ.TXT");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            var buffer = new byte[64 * 1024];
            int replacements = 0;
            for (long offset = 0; offset < file.Length; offset += buffer.Length - before.Length)
            {
                file.Position = offset;
                int count = file.Read(buffer);
                for (int i = 0; i <= count - before.Length; i++)
                {
                    if (buffer.AsSpan(i, before.Length).SequenceEqual(before))
                    {
                        after.CopyTo(buffer, i);
                        replacements++;
                    }
                }
                file.Position = offset;
                file.Write(buffer, 0, count);
            }
            Assert.True(replacements >= 2);
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        IReadOnlyFileSystem fs = session.Volumes[0].FileSystem;
        Assert.Contains(fs.ReadDirectory("/"), e => e.Name == "SENTINEλ.TXT");
        using var text = new StreamReader(fs.OpenRead("/SENTINEλ.TXT"));
        Assert.Equal(SectorWorks.DependencyProbe.FixtureCatalog.Sentinel, text.ReadToEnd());
    }

    [Theory]
    [InlineData(FileAttributes.Encrypted)]
    [InlineData(FileAttributes.SparseFile)]
    [InlineData(FileAttributes.Compressed)]
    public void UnsupportedNtfsFeaturesNeverReturnUnverifiedContent(FileAttributes feature)
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            // Disposable fixture authoring only; these APIs are not exposed by the production adapter.
#pragma warning disable CA1416
            using var ntfs = new NtfsFileSystem(file);
            ntfs.SetAttributes("SENTINEL.TXT", ntfs.GetAttributes("SENTINEL.TXT") | feature);
#pragma warning restore CA1416
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Throws<ImageFormatException>(() => session.Volumes[0].FileSystem.OpenRead("/SENTINEL.TXT"));
    }

    [Fact]
    public void NtfsReparsePointsAreListedButNeverFollowed()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
#pragma warning disable CA1416
            using var ntfs = new NtfsFileSystem(file);
            ntfs.SetReparsePoint("SENTINEL.TXT", new DiscUtils.ReparsePoint(unchecked((int)0xa000000c), new byte[32]));
#pragma warning restore CA1416
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        IReadOnlyFileSystem fs = session.Volumes[0].FileSystem;
        Assert.True(Assert.Single(fs.ReadDirectory("/"), e => e.Name == "SENTINEL.TXT").IsLink);
        Assert.Throws<ImageFormatException>(() => fs.OpenRead("/SENTINEL.TXT"));
    }

    [Fact]
    public void ElToritoBootCatalogIsDescriptiveMetadataOnly()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("boot.data");
        var builder = new CDBuilder { UseJoliet = false, VolumeIdentifier = "BOOTTEST" };
        builder.SetBootImage(new MemoryStream(new byte[2048], false), BootDeviceEmulation.NoEmulation, 0);
        builder.AddFile("EMPTY.TXT", []);
        using (SparseStream iso = builder.Build())
        using (FileStream file = File.Create(path))
        {
            iso.CopyTo(file);
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
        Assert.Equal("BOOTTEST", fs.VolumeLabel);
        Assert.True(uint.TryParse(fs.Properties["El Torito catalog LBA"], out uint catalog));
        Assert.True(catalog > 0);
        Assert.Contains(session.Warnings, s => s.Contains("not guaranteed", StringComparison.Ordinal));
        using Stream empty = fs.OpenRead("/EMPTY.TXT");
        Assert.Equal(0, empty.Length);
    }

    [Fact]
    public void OversizedIsoRootIsRejectedBeforeParserAllocation()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("joliet.iso");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = 16 * 2048 + 166;
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, int.MaxValue);
            file.Write(bytes);
        }
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    [Fact]
    public void UdfRecognitionIsReportedWithoutClaimingBrowsing()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("udf.raw");
        var bytes = new byte[64 * 2048];
        "BEA01"u8.CopyTo(bytes.AsSpan(16 * 2048 + 1));
        "NSR03"u8.CopyTo(bytes.AsSpan(17 * 2048 + 1));
        "TEA01"u8.CopyTo(bytes.AsSpan(18 * 2048 + 1));
        File.WriteAllBytes(path, bytes);
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Empty(session.Volumes);
        Assert.Contains(session.Warnings, s => s.Contains("UDF", StringComparison.Ordinal)
            && s.Contains("not implemented", StringComparison.Ordinal));
    }

    [Fact]
    public void FatDirectorySelfReferenceIsRejected()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("fat12.img");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            var boot = new byte[512];
            file.ReadExactly(boot);
            int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
            int fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22));
            int rootCount = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
            long rootOffset = (reserved + boot[16] * fatSectors) * 512;
            file.Position = rootOffset;
            var root = new byte[rootCount * 32];
            file.ReadExactly(root);
            int entry = Enumerable.Range(0, rootCount).Select(i => i * 32)
                .Single(i => Encoding.ASCII.GetString(root, i, 11) == "DOCS       ");
            ushort cluster = BinaryPrimitives.ReadUInt16LittleEndian(root.AsSpan(entry + 26));
            long dataOffset = rootOffset + (root.Length + 511) / 512 * 512;
            long directoryOffset = dataOffset + (cluster - 2) * boot[13] * 512;
            file.Position = directoryOffset + 64;
            var self = new byte[32];
            Encoding.ASCII.GetBytes("LOOP       ").CopyTo(self, 0);
            self[11] = 0x10;
            BinaryPrimitives.WriteUInt16LittleEndian(self.AsSpan(26), cluster);
            file.Write(self);
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Contains("Cyclic FAT directory", Assert.Throws<ImageFormatException>(() =>
            session.Volumes[0].FileSystem.ReadDirectory("/DOCS")).Message, StringComparison.Ordinal);
    }

    private static void AddRockRidgeRecords(string path, bool corrupt)
    {
        using FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        file.Position = 16 * 2048 + 158;
        var field = new byte[4];
        file.ReadExactly(field);
        uint block = BinaryPrimitives.ReadUInt32LittleEndian(field);
        file.Position = block * 2048L;
        var root = new byte[2048];
        file.ReadExactly(root);
        var output = new byte[2048];
        int target = 0;
        for (int position = 0; root[position] != 0; position += root[position])
        {
            byte[] record = root.AsSpan(position, root[position]).ToArray();
            var systemUse = new List<byte>();
            bool directory = (record[25] & 2) != 0;
            if (position == 0)
            {
                systemUse.AddRange([0x53, 0x50, 7, 1, 0xbe, 0xef, 0]);
                systemUse.AddRange([0x45, 0x52, 18, 1, 10, 0, 0, 1]);
                systemUse.AddRange(Encoding.ASCII.GetBytes("RRIP_1991A"));
                if (corrupt)
                {
                    systemUse.AddRange([0x4e, 0x4d, 0, 1, 0]);
                }
            }
            var px = new byte[44];
            px[0] = 0x50;
            px[1] = 0x58;
            px[2] = 44;
            px[3] = 1;
            uint mode = directory ? 0x41edu : 0x81a4u;
            BinaryPrimitives.WriteUInt32LittleEndian(px.AsSpan(4), mode);
            BinaryPrimitives.WriteUInt32BigEndian(px.AsSpan(8), mode);
            BinaryPrimitives.WriteUInt32LittleEndian(px.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt32BigEndian(px.AsSpan(16), 1);
            systemUse.AddRange(px);
            if (!directory)
            {
                byte[] name = Encoding.UTF8.GetBytes("Long-name.txt");
                systemUse.AddRange([0x4e, 0x4d, (byte)(name.Length + 5), 1, 0]);
                systemUse.AddRange(name);
            }
            output[target] = checked((byte)(record.Length + systemUse.Count));
            record.AsSpan(1).CopyTo(output.AsSpan(target + 1));
            systemUse.ToArray().CopyTo(output, target + record.Length);
            target += output[target];
        }
        file.Position = block * 2048L;
        file.Write(output);
    }
}
