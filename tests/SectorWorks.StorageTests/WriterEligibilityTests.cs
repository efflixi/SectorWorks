using System.Buffers.Binary;
using SectorWorks.Core;
using SectorWorks.Storage;

namespace SectorWorks.StorageTests;

public sealed class WriterEligibilityTests
{
    [Theory]
    [InlineData("CTMOUSE.EXE", true)]
    [InlineData("ctmouse.exe", true)]
    [InlineData("ctmouse.EXE", true)]
    [InlineData("CTMOUSE.exe", true)]
    [InlineData("a_1-2.txt", true)]
    [InlineData("123.456", true)]
    [InlineData("Mixed.txt", false)]
    [InlineData("NAME.tXt", false)]
    [InlineData("longfilename.txt", false)]
    [InlineData("NAME.", false)]
    [InlineData("caf\u00e9.txt", false)]
    public void ShortNamesPreserveRepresentableCaseWithoutRequiringLongNames(string name, bool supported)
    {
        Assert.Equal(supported, FatEditNames.IsSupportedShortName(name));
    }

    [Fact]
    public void ShortNameLookupsAllowMixedCaseWithoutClaimingItCanBeStored()
    {
        Assert.True(FatEditNames.IsValidShortName("CtMoUsE.eXe"));
        Assert.False(FatEditNames.IsSupportedShortName("CtMoUsE.eXe"));
        Assert.True(FatEditNames.IsSupported("CtMoUsE.eXe"));
    }

    [Theory]
    [InlineData(137438953472L, 8192U)]
    [InlineData(536870912L, 65536U)]
    [InlineData(2199023255040L, 4294967295U)]
    public void OversizedImagesAndDeclaredFatTablesAreRejectedBeforeAnyDependencyAllocation(long length, uint fatSectors)
    {
        var boot = new byte[512];
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11), 512);
        boot[13] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14), 32);
        boot[16] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(32), checked((uint)(length / 512)));
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(36), fatSectors);
        boot[66] = 0x29;
        boot[510] = 0x55;
        boot[511] = 0xaa;
        using var image = new GuardedSession(new GuardedDevice(length, boot));
        Capability capability = new RawFatImageWriter().Qualify(image);
        Assert.False(capability.Available);
        Assert.Contains("64 GiB", capability.Reason, StringComparison.Ordinal);
        Assert.Contains("8 MiB", capability.Reason, StringComparison.Ordinal);
        Assert.Equal(1, image.Guard.Reads);
        Assert.Equal(512, image.Guard.BytesRead);
        using var capacityImage = new GuardedSession(new GuardedDevice(length, boot));
        Assert.Throws<NotSupportedException>(() => new RawFatImageWriter().ValidateCapacity(capacityImage, [], default));
        Assert.Equal(1, capacityImage.Guard.Reads);
        Assert.Equal(512, capacityImage.Guard.BytesRead);
    }

    [Fact]
    public void ApplyRejectsOversizedDeclaredFatUsingSparseInputBeforeOpeningDependencyWriter()
    {
        using var work = new StorageTests.WorkDirectory();
        string path = work.PathFor("oversized-fat.img");
        var boot = new byte[512];
        "FAT32   "u8.CopyTo(boot.AsSpan(82));
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11), 512);
        boot[13] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14), 32);
        boot[16] = 2;
        boot[21] = 0xf8;
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(32), 1_048_576);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(36), 65_536);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(44), 2);
        boot[66] = 0x29;
        boot[510] = 0x55;
        boot[511] = 0xaa;
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            file.SetLength(536_870_912);
            file.Write(boot);
            file.Position = 32 * 512;
            file.Write([0xf8, 0xff, 0xff, 0xff]);
        }
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new RawFatImageWriter().Apply(path, [], default));
        Assert.Contains("8 MiB", failure.Message, StringComparison.Ordinal);
        using var unchanged = File.OpenRead(path);
        byte[] actual = new byte[512];
        unchanged.ReadExactly(actual);
        Assert.Equal(boot, actual);
        Assert.Equal(536_870_912, unchanged.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeFixedRootMetadataIsChunkedAndOptionalWriterFailureKeepsReadOnlyEligibility(bool rejectMetadata)
    {
        var boot = new byte[512];
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11), 512);
        boot[13] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14), 1);
        boot[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(17), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(32), 262144);
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(22), 512);
        boot[38] = 0x29;
        "SECTORWORKS"u8.CopyTo(boot.AsSpan(43));
        boot[510] = 0x55;
        boot[511] = 0xaa;
        var device = new ChunkLimitedDevice(boot, rejectMetadata);
        using var image = new LargeRootSession(device);
        Capability capability = new RawFatImageWriter().Qualify(image);
        Assert.False(capability.Available);
        Assert.InRange(device.LargestRead, 512, 65536);
        if (!rejectMetadata) { Assert.Equal(512 + ushort.MaxValue * 32, device.BytesRead); }
        Assert.True(image.Volumes[0].FileSystem.Capabilities.Browse.Available);
        Assert.Empty(image.Volumes[0].FileSystem.ReadDirectory("/"));
    }

    private sealed class ChunkLimitedDevice(byte[] boot, bool rejectMetadata) : IReadOnlyBlockDevice
    {
        public int LargestRead { get; private set; }
        public int BytesRead { get; private set; }
        public long Length => 134217728;
        public int LogicalSectorSize => 512;
        public int? PhysicalSectorSize => null;
        public int Read(long offset, Span<byte> buffer)
        {
            LargestRead = Math.Max(LargestRead, buffer.Length);
            if (buffer.Length > 65536) { throw new InvalidOperationException("Metadata read exceeds the device request budget."); }
            if (offset != 0 && rejectMetadata) { throw new InvalidOperationException("Optional writer metadata is unavailable."); }
            buffer.Clear();
            if (offset == 0) { boot.AsSpan(0, buffer.Length).CopyTo(buffer); }
            BytesRead += buffer.Length;
            return buffer.Length;
        }
        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(offset, buffer.Span));
        public void Dispose() { }
    }

    private sealed class LargeRootSession(ChunkLimitedDevice device) : IImageSession
    {
        public ImageMetadata Metadata { get; } = new("large-root.img", "Raw", device.Length, device.Length, 512, new Dictionary<string, string>());
        public IReadOnlyBlockDevice Device => device;
        public IReadOnlyList<PartitionInfo> Partitions => [];
        public IReadOnlyList<ImageVolume> Volumes { get; } = [new("volume", null, new GuardedFileSystem("FAT16", true))];
        public IReadOnlyList<string> Warnings => [];
        public void Dispose() => device.Dispose();
    }

    private sealed class GuardedDevice(long length, byte[] boot) : IReadOnlyBlockDevice
    {
        public int Reads { get; private set; }
        public int BytesRead { get; private set; }
        public long Length => length;
        public int LogicalSectorSize => 512;
        public int? PhysicalSectorSize => null;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset != 0 || buffer.Length != 512 || Reads != 0)
            {
                throw new InvalidOperationException("Writer eligibility must reject resource bombs before reading FAT/directory metadata.");
            }
            boot.CopyTo(buffer);
            Reads++;
            BytesRead += buffer.Length;
            return buffer.Length;
        }
        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(offset, buffer.Span));
        public void Dispose() { }
    }

    private sealed class GuardedSession(GuardedDevice device) : IImageSession
    {
        public GuardedDevice Guard => device;
        public ImageMetadata Metadata { get; } = new("guarded.img", "Raw", device.Length, device.Length, 512, new Dictionary<string, string>());
        public IReadOnlyBlockDevice Device => device;
        public IReadOnlyList<PartitionInfo> Partitions => [];
        public IReadOnlyList<ImageVolume> Volumes { get; } = [new("volume", null, new GuardedFileSystem())];
        public IReadOnlyList<string> Warnings => [];
        public void Dispose() => device.Dispose();
    }

    private sealed class GuardedFileSystem(string name = "FAT32", bool browseEmpty = false) : IReadOnlyFileSystem
    {
        public string Name => name;
        public string VolumeLabel => "";
        public bool IsCaseSensitive => false;
        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default) =>
            browseEmpty ? [] : throw new InvalidOperationException("A resource-bomb layout must not reach directory traversal.");
        public Stream OpenRead(string path) => throw new InvalidOperationException("A resource-bomb layout must not reach file reads.");
        public void Dispose() { }
    }
}
