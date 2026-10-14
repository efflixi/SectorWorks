using System.Buffers.Binary;
using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class PartitionTests
{
    [Theory]
    [InlineData("overlap")]
    [InlineData("out-of-range")]
    [InlineData("active")]
    [InlineData("empty")]
    public void MalformedMbrIsRejected(string damage)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("mbr.raw");
        var bytes = new byte[64 * 512];
        bytes[510] = 0x55;
        bytes[511] = 0xaa;
        SetEntry(bytes, 0, 1, 16);
        switch (damage)
        {
            case "overlap":
                SetEntry(bytes, 1, 10, 16);
                break;
            case "out-of-range":
                SetEntry(bytes, 1, 60, 8);
                break;
            case "active":
                bytes[446] = 0x81;
                break;
            case "empty":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(458), 0);
                break;
        }
        File.WriteAllBytes(path, bytes);
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    [Fact]
    public void MbrActiveFlagTypeAndUnallocatedBoundsAreReported()
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("valid.raw");
        var bytes = new byte[64 * 512];
        bytes[510] = 0x55;
        bytes[511] = 0xaa;
        SetEntry(bytes, 0, 2, 16);
        bytes[446] = 0x80;
        File.WriteAllBytes(path, bytes);
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        PartitionInfo partition = Assert.Single(session.Partitions);
        Assert.Equal("0x06", partition.Type);
        Assert.True(partition.IsActive);
        Assert.Equal(1024, partition.Offset);
        Assert.Equal(8192, partition.Length);
        Assert.Equal("2", session.Metadata.Properties["Unallocated region count"]);
        using var view = new PartitionBlockDevice(session.Device, partition.Offset, partition.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Read(8192, new byte[1]));
    }

    [Theory]
    [InlineData("header-crc", "header CRC32")]
    [InlineData("entries-crc", "entry array CRC32")]
    [InlineData("overlap", "Overlapping")]
    [InlineData("extent", "usable extent")]
    [InlineData("count-cap", "safety cap")]
    [InlineData("metadata-overlap", "metadata overlaps")]
    public void DamagedGptPrimaryIsRejected(string damage, string diagnostic)
    {
        using var work = new WorkDirectory();
        string path = work.Golden("gpt.img");
        MutatePrimary(path, damage);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
        Assert.Contains(diagnostic, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GptBackupDamageDoesNotSilentlyLookConsistent(bool inconsistent)
    {
        using var work = new WorkDirectory();
        string path = work.Golden("gpt.img");
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = file.Length - 512;
            var backup = new byte[512];
            file.ReadExactly(backup);
            backup[56] ^= 0x01;
            if (inconsistent)
            {
                HeaderCrc(backup);
            }
            file.Position = file.Length - 512;
            file.Write(backup);
        }
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Single(session.Partitions);
        Assert.Contains(session.Warnings, s => s.Contains(inconsistent ? "inconsistent" : "damaged", StringComparison.Ordinal));
        Assert.Single(session.Volumes);
    }

    [Fact]
    public void GptMetadataAndValidBackupAreReported()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("gpt.img");
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        PartitionInfo partition = Assert.Single(session.Partitions);
        Assert.True(Guid.TryParse(partition.Type, out Guid type));
        Assert.NotEqual(Guid.Empty, type);
        Assert.NotNull(partition.UniqueId);
        Assert.Equal("GPT", partition.Table);
        Assert.Contains("matches primary", session.Metadata.Properties["GPT backup"], StringComparison.Ordinal);
        Assert.DoesNotContain(session.Warnings, s => s.Contains("GPT", StringComparison.Ordinal));
    }

    private static void SetEntry(byte[] bytes, int index, uint start, uint count)
    {
        int entry = 446 + index * 16;
        bytes[entry + 4] = 6;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), start);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 12), count);
    }

    private static void MutatePrimary(string path, string damage)
    {
        using FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        file.Position = 512;
        var header = new byte[512];
        file.ReadExactly(header);
        long entriesOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(72)) * 512);
        int size = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80))
            * BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84))));
        var entries = new byte[size];
        file.Position = entriesOffset;
        file.ReadExactly(entries);
        switch (damage)
        {
            case "header-crc":
                header[56] ^= 1;
                break;
            case "entries-crc":
                entries[56] ^= 1;
                break;
            case "overlap":
                entries.AsSpan(0, 128).CopyTo(entries.AsSpan(128));
                Guid.NewGuid().TryWriteBytes(entries.AsSpan(144, 16));
                break;
            case "extent":
                BinaryPrimitives.WriteUInt64LittleEndian(entries.AsSpan(32), 1);
                break;
            case "count-cap":
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), uint.MaxValue);
                break;
            case "metadata-overlap":
                BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(72),
                    BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(40)));
                break;
        }
        if (damage != "entries-crc")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(88), Crc(entries));
        }
        if (damage != "header-crc")
        {
            HeaderCrc(header);
        }
        file.Position = entriesOffset;
        file.Write(entries);
        file.Position = 512;
        file.Write(header);
    }

    internal static void HeaderCrc(byte[] header)
    {
        header.AsSpan(16, 4).Clear();
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), Crc(header.AsSpan(0, size)));
    }

    internal static uint Crc(ReadOnlySpan<byte> bytes, uint polynomial = 0xedb88320u)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? polynomial : 0);
            }
        }
        return ~crc;
    }
}
