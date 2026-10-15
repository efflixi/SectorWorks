using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Streams;
using SectorWorks.Core;
using SectorWorks.DependencyProbe;
using SectorWorks.Storage;
using FatFileSystem = DiscUtils.Fat.FatFileSystem;

namespace SectorWorks.StorageTests;

public sealed class StorageTests
{
    private static readonly string Goldens = Path.Combine(AppContext.BaseDirectory, "fixtures", "golden");

    public static IEnumerable<object[]> NormalGoldens() => FixtureCatalog.ReadManifest(Goldens)
        .Where(e => !e.Name.StartsWith("truncated", StringComparison.Ordinal) && e.Name != "corrupt-fat.img")
        .Select(e => new object[] { e.Name });

    [Theory]
    [MemberData(nameof(NormalGoldens))]
    public void GoldensOpenReadOnlyAndSourceHashesRemainUnchanged(string name)
    {
        using var work = new WorkDirectory();
        string path = work.Golden(name);
        string before = Hash(path);
        using (IImageSession session = new ImageOpener().OpenReadOnly(path))
        {
            Assert.True(session.Device.IsReadOnly);
            IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
            Assert.True(fs.Capabilities.Browse.Available);
            Assert.True(fs.Capabilities.Extract.Available);
            Assert.True(fs.Capabilities.Preview.Available);
            Assert.False(fs.Capabilities.Validate.Available);
            Assert.False(fs.Capabilities.Modify.Available);
            Assert.False(fs.Capabilities.CommitInPlace.Available);
            Assert.False(fs.Capabilities.Rebuild.Available);
            Assert.False(fs.Capabilities.PreserveBootability.Available);
            Assert.Contains(fs.ReadDirectory("/"), e => e.Name.Equals("SENTINEL.TXT", StringComparison.OrdinalIgnoreCase));
            using Stream file = fs.OpenRead("/SENTINEL.TXT");
            using var text = new StreamReader(file);
            Assert.Equal(FixtureCatalog.Sentinel, text.ReadToEnd());
            Assert.Throws<NotSupportedException>(() => file.WriteByte(1));
            Assert.Throws<NotSupportedException>(() => session.Device.Write(0, new byte[1]));
            Assert.Throws<NotSupportedException>(() => session.Device.Flush());
            if (name is "mbr.img" or "gpt.img")
            {
                Assert.Single(session.Partitions);
                Assert.Contains(session.Metadata.Properties.Keys, k => k.StartsWith("Unallocated", StringComparison.Ordinal));
            }
            if (name == "joliet.iso")
            {
                Assert.Equal("ISO", session.Metadata.Format);
                Assert.Contains(fs.ReadDirectory("/DOCS"), e => e.Name == "Unicode-λ.txt");
                using Stream unicode = fs.OpenRead("/DOCS/Unicode-λ.txt");
                Assert.Equal(FixtureCatalog.Sentinel, new StreamReader(unicode).ReadToEnd());
                Assert.Equal("Present", fs.Properties["Joliet"]);
            }
            if (name.StartsWith("dynamic", StringComparison.Ordinal))
            {
                Assert.Equal(32 * 1024 * 1024, session.Metadata.VirtualLength);
                Assert.NotEqual(session.Metadata.ContainerLength, session.Metadata.VirtualLength);
            }
            if (name == "ntfs.img")
            {
                using Stream large = fs.OpenRead("/LARGE.BIN");
                var content = new byte[8192];
                large.ReadExactly(content);
                Assert.Equal(Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)), content);
            }
        }
        Assert.Equal(before, Hash(path));
    }

    [Theory]
    [InlineData("truncated-fat.img")]
    [InlineData("corrupt-fat.img")]
    [InlineData("truncated.iso")]
    [InlineData("truncated.vhd")]
    [InlineData("truncated.vhdx")]
    public void MalformedGoldenNeverLeaksParserExceptions(string name)
    {
        using var work = new WorkDirectory();
        string path = work.Golden(name);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
        Assert.NotEmpty(error.Message);
        using FileStream unlocked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task BoundedRandomReadsViewsCancellationAndHugeSparseFile()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("huge.raw");
        const long length = 8L * 1024 * 1024 * 1024 * 1024;
        using (FileStream file = File.Create(path))
        {
            file.SetLength(length);
            file.WriteByte(0x11);
            file.Position = length / 2;
            file.WriteByte(0x22);
            file.Position = length - 1;
            file.WriteByte(0x33);
        }
        using var device = new FileBlockDevice(path);
        var buffer = new byte[1];
        Assert.Equal(1, device.Read(0, buffer));
        Assert.Equal(0x11, buffer[0]);
        Assert.Equal(1, await device.ReadAsync(length / 2, buffer));
        Assert.Equal(0x22, buffer[0]);
        Assert.Equal(1, device.Read(length - 1, buffer));
        Assert.Equal(0x33, buffer[0]);
        Assert.Equal(1024 * 1024, device.Read(0, new byte[2 * 1024 * 1024]));
        Assert.Equal(0, device.Read(length, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.Read(-1, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.Read(length, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.Read(long.MaxValue, buffer));
        using var view = new PartitionBlockDevice(device, length - 1024, 1024);
        Assert.Equal(1, view.Read(1023, buffer));
        Assert.Equal(0x33, buffer[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Read(1024, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartitionBlockDevice(device, length - 2, 3));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await device.ReadAsync(0, buffer, canceled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => new ImageOpener().OpenReadOnly(path, canceled.Token));
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Empty(session.Volumes);
        Assert.Contains(session.Warnings, s => s.Contains("Unknown layouts", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedContainersOpenThroughContentNotExtension(bool vhdx)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("not-an-image.txt");
        using (FileStream file = File.Create(path))
        using (VirtualDisk disk = vhdx
            ? DiscUtils.Vhdx.Disk.InitializeDynamic(file, Ownership.None, 4 * 1024 * 1024)
            : DiscUtils.Vhd.Disk.InitializeFixed(file, Ownership.None, 4 * 1024 * 1024))
        {
            if (vhdx)
            {
                disk.Content.Write(new byte[4 * 1024 * 1024]);
                disk.Content.Position = 0;
            }
            using var fat = FatFileSystem.FormatFloppy(disk.Content, FloppyDiskType.HighDensity, "TEST");
            using Stream sentinel = fat.OpenFile("SENTINEL.TXT", FileMode.Create);
            sentinel.Write(Encoding.UTF8.GetBytes(FixtureCatalog.Sentinel));
        }
        if (vhdx)
        {
            SetVhdxFileFlags(path, 1);
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Equal(vhdx ? "VHDX" : "VHD", session.Metadata.Format);
        Assert.Single(session.Volumes);
        Assert.Equal("Fixed", session.Metadata.Properties[vhdx ? "VHDX allocation" : "VHD allocation"]);
    }

    [Fact]
    public void SignatureWithoutValidBpbDoesNotClaimFatOrNtfs()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("false-fat.img");
        var bytes = new byte[4096];
        bytes[510] = 0x55;
        bytes[511] = 0xaa;
        File.WriteAllBytes(path, bytes);
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Empty(session.Volumes);
        Assert.Empty(session.Partitions);
        Assert.Equal("Raw", session.Metadata.Format);
    }

    [Fact]
    public void FatCyclesFailBeforeReturningRepeatedContent()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("fat12.img");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            var boot = new byte[512];
            file.ReadExactly(boot);
            int sector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
            int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
            int fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22));
            int rootOffset = (reserved + boot[16] * fatSectors) * sector;
            file.Position = rootOffset;
            var root = new byte[512];
            file.ReadExactly(root);
            int entry = Enumerable.Range(0, 16).Select(i => i * 32)
                .Single(i => Encoding.ASCII.GetString(root, i, 11) == "SENTINELTXT");
            ushort cluster = BinaryPrimitives.ReadUInt16LittleEndian(root.AsSpan(entry + 26));
            long fatOffset = reserved * sector + cluster + cluster / 2;
            file.Position = fatOffset;
            var link = new byte[2];
            file.ReadExactly(link);
            ushort old = BinaryPrimitives.ReadUInt16LittleEndian(link);
            ushort value = (ushort)((cluster & 1) == 0 ? (old & 0xf000) | cluster : (old & 0xf) | cluster << 4);
            BinaryPrimitives.WriteUInt16LittleEndian(link, value);
            file.Position = fatOffset;
            file.Write(link);
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Contains("cyclic", Assert.Throws<ImageFormatException>(() =>
            session.Volumes[0].FileSystem.OpenRead("/SENTINEL.TXT")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FatUnicodeEmptyFilesAndSubdirectoriesAreReadSafely()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("unicode.img");
        using (FileStream file = File.Create(path))
        using (var fat = FatFileSystem.FormatFloppy(file, FloppyDiskType.HighDensity, "TEST"))
        {
            fat.CreateDirectory("Empty");
            using Stream empty = fat.OpenFile("EMPTY.TXT", FileMode.Create);
        }
        AddFatLongName(path, "Long Unicode-λ-名.txt");
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        IReadOnlyFileSystem fs = session.Volumes[0].FileSystem;
        Assert.Empty(fs.ReadDirectory("/Empty"));
        FileEntry entry = Assert.Single(fs.ReadDirectory("/"), e => e.Name == "Long Unicode-λ-名.txt");
        Assert.Equal("Long Unicode-λ-名.txt", entry.Name);
        Assert.NotEmpty(entry.ShortName!);
        using Stream emptyFile = fs.OpenRead(entry.Path);
        Assert.Equal(0, emptyFile.Length);
        Assert.Equal(-1, emptyFile.ReadByte());
        Assert.Throws<ImageFormatException>(() => fs.ReadDirectory("/../bad"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => fs.ReadDirectory("/", cancel.Token));
    }

    internal static string Hash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    internal static void SetVhdxFileFlags(string path, uint flags)
    {
        using FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        file.Position = 3 * 65536;
        var regions = new byte[65536];
        file.ReadExactly(regions);
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(regions.AsSpan(8));
        for (int i = 0; i < count; i++)
        {
            int entry = 16 + i * 32;
            if (new Guid(regions.AsSpan(entry, 16)) != new Guid("8b7ca206-4790-4b9a-b8fe-575f050f886e"))
            {
                continue;
            }
            long start = (long)BinaryPrimitives.ReadUInt64LittleEndian(regions.AsSpan(entry + 16));
            file.Position = start;
            var metadata = new byte[65536];
            file.ReadExactly(metadata);
            int metadataCount = BinaryPrimitives.ReadUInt16LittleEndian(metadata.AsSpan(10));
            for (int j = 0; j < metadataCount; j++)
            {
                int item = 32 + j * 32;
                if (new Guid(metadata.AsSpan(item, 16)) == new Guid("caa16737-fa36-4d43-b3b6-33f0aa44e76b"))
                {
                    long offset = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(item + 16));
                    file.Position = start + offset + 4;
                    var value = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(value, flags);
                    file.Write(value);
                    return;
                }
            }
        }
        throw new InvalidDataException("File parameters not found in disposable VHDX.");
    }

    private static void AddFatLongName(string path, string name)
    {
        using FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        var boot = new byte[512];
        file.ReadExactly(boot);
        int sector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
        int fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22));
        long rootOffset = (reserved + boot[16] * fatSectors) * sector;
        byte[] shortName = Encoding.ASCII.GetBytes("LONGUN~1TXT");
        byte checksum = 0;
        foreach (byte value in shortName)
        {
            checksum = unchecked((byte)(((checksum & 1) << 7) + (checksum >> 1) + value));
        }
        file.Position = rootOffset;
        var root = new byte[sector];
        file.ReadExactly(root);
        int end = Enumerable.Range(0, sector / 32).First(i => root[i * 32] == 0) * 32;
        int parts = (name.Length + 12) / 13;
        for (int ordinal = parts; ordinal > 0; ordinal--)
        {
            var lfn = new byte[32];
            lfn[0] = (byte)(ordinal | (ordinal == parts ? 0x40 : 0));
            lfn[11] = 0x0f;
            lfn[13] = checksum;
            int[] positions = [1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30];
            for (int character = 0; character < 13; character++)
            {
                int index = (ordinal - 1) * 13 + character;
                ushort value = index < name.Length ? name[index] : index == name.Length ? (ushort)0 : (ushort)0xffff;
                BinaryPrimitives.WriteUInt16LittleEndian(lfn.AsSpan(positions[character]), value);
            }
            file.Position = rootOffset + end;
            file.Write(lfn);
            end += 32;
        }
        var entryBytes = new byte[32];
        shortName.CopyTo(entryBytes, 0);
        entryBytes[11] = 0x20;
        file.Write(entryBytes);
        file.Write(new byte[32]);
    }

    internal sealed class WorkDirectory : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(AppContext.BaseDirectory, "storage-work", Guid.NewGuid().ToString("N"));
        internal WorkDirectory() => Directory.CreateDirectory(_directory);
        internal string PathFor(string name) => System.IO.Path.Combine(_directory, name);
        internal string Golden(string name) => FixtureCatalog.Materialize(Goldens, _directory,
            FixtureCatalog.ReadManifest(Goldens).Single(e => e.Name == name));
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
