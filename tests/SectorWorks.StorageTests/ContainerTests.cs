using System.Buffers.Binary;
using DiscUtils.Streams;
using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class ContainerTests
{
    [Fact]
    public void RegisteredHandlersSelectHighestContentPriorityAndRawFallback()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("misleading.iso");
        var bytes = new byte[65536];
        "vhdxfile"u8.CopyTo(bytes);
        "conectix"u8.CopyTo(bytes.AsSpan(bytes.Length - 512));
        "CD001"u8.CopyTo(bytes.AsSpan(32769));
        File.WriteAllBytes(path, bytes);
        using (var device = new FileBlockDevice(path))
        {
            FormatDetection detection = new ImageOpener().Detect(device);
            Assert.Equal("VHDX", detection.Format);
            Assert.Equal(100, detection.Priority);
            Assert.True(detection.Recognized);
        }
        bytes.AsSpan(0, 8).Clear();
        File.WriteAllBytes(path, bytes);
        using (var device = new FileBlockDevice(path))
        {
            Assert.Equal("VHD", new ImageOpener().Detect(device).Format);
        }
        bytes.AsSpan(bytes.Length - 512, 8).Clear();
        File.WriteAllBytes(path, bytes);
        using (var device = new FileBlockDevice(path))
        {
            Assert.Equal("ISO", new ImageOpener().Detect(device).Format);
        }
        bytes.AsSpan(32769, 5).Clear();
        File.WriteAllBytes(path, bytes);
        using (var device = new FileBlockDevice(path))
        {
            FormatDetection detection = new ImageOpener().Detect(device);
            Assert.Equal("Raw", detection.Format);
            Assert.Equal(0, detection.Priority);
            Assert.False(detection.Recognized);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferencingParentIsReportedWithoutFollowingExternalPaths(bool vhdx)
    {
        using var work = new WorkDirectory();
        string path = work.Golden(vhdx ? "dynamic.vhdx" : "dynamic.vhd");
        if (vhdx)
        {
            SetVhdxFileFlags(path, 2);
        }
        else
        {
            using FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
            file.Position = file.Length - 512;
            var footer = new byte[512];
            file.ReadExactly(footer);
            BinaryPrimitives.WriteUInt32BigEndian(footer.AsSpan(60), 4);
            footer.AsSpan(64, 4).Clear();
            uint sum = 0;
            foreach (byte value in footer)
            {
                sum += value;
            }
            BinaryPrimitives.WriteUInt32BigEndian(footer.AsSpan(64), ~sum);
            file.Position = file.Length - 512;
            file.Write(footer);
        }
        string before = Hash(path);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
        Assert.Contains("requires a parent", error.Message, StringComparison.Ordinal);
        Assert.Contains("resolution is disabled", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Hash(path));
    }

    [Fact]
    public void TerabyteVhdxSupportsBoundedEndReadsWithoutExpansion()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("large.data");
        const long capacity = 1024L * 1024 * 1024 * 1024;
        using (FileStream file = File.Create(path))
        using (var disk = DiscUtils.Vhdx.Disk.InitializeDynamic(file, Ownership.None, capacity))
        {
            disk.Content.Position = capacity - 4096;
            disk.Content.WriteByte(0x5a);
        }
        long containerLength = new FileInfo(path).Length;
        Assert.True(containerLength < 64 * 1024 * 1024);
        using (IImageSession session = new ImageOpener().OpenReadOnly(path))
        {
            Assert.Equal(capacity, session.Device.Length);
            var buffer = new byte[16];
            for (int i = 0; i < 1500; i++)
            {
                Assert.Equal(16, session.Device.Read(capacity - 4096, buffer));
                Assert.Equal(0x5a, buffer[0]);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Device.Read(capacity - 1, buffer));
        }
        Assert.Equal(containerLength, new FileInfo(path).Length);
    }

    [Fact]
    public void VhdxActiveLogIsRejectedWithoutRepairingSource()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("dynamic.vhdx");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            for (int i = 1; i <= 2; i++)
            {
                file.Position = i * 65536L;
                var header = new byte[4096];
                file.ReadExactly(header);
                Guid.NewGuid().TryWriteBytes(header.AsSpan(48, 16));
                header.AsSpan(4, 4).Clear();
                uint crc = PartitionTests.Crc(header, 0x82f63b78u);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), crc);
                file.Position = i * 65536L;
                file.Write(header);
            }
        }
        string before = Hash(path);
        Assert.Contains("transaction-log replay", Assert.Throws<ImageFormatException>(() =>
            new ImageOpener().OpenReadOnly(path)).Message, StringComparison.Ordinal);
        Assert.Equal(before, Hash(path));
    }

    [Fact]
    public void ConcurrentParserStreamsHaveIndependentPositions()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        IReadOnlyFileSystem fs = session.Volumes[0].FileSystem;
        Parallel.For(0, 64, i =>
        {
            using Stream file = fs.OpenRead("/LARGE.BIN");
            int offset = i * 31;
            file.Position = offset;
            var buffer = new byte[31];
            file.ReadExactly(buffer);
            Assert.Equal(Enumerable.Range(offset, 31).Select(v => (byte)(v % 251)), buffer);
        });
    }
}
