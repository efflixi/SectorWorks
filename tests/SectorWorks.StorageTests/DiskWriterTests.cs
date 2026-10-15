using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class DiskWriterTests
{
    public static IEnumerable<object[]> FatRows() =>
        from container in new[] { "raw", "vhd-fixed", "vhd-dynamic", "vhdx-fixed", "vhdx-dynamic" }
        from layout in new[] { "filesystem", "mbr", "gpt" }
        from fat in new[] { "fat12", "fat16", "fat32" }
        from physical in container.StartsWith("vhdx", StringComparison.Ordinal) ? new[] { 512, 4096 } : new[] { 512 }
        select new object[] { container, layout, fat, physical };

    [Theory]
    [MemberData(nameof(FatRows))]
    public void QualifiedFatRowsPreserveVirtualBytesMetadataSourceAndVariant(string container, string layout, string fat, int physical)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, layout, fat, physical: physical);
        string output = work.PathFor("output.disk");
        string before = Hash(source);
        var writer = new DiskImageWriter();
        string id;
        long virtualLength;
        long sourceContainerLength = new FileInfo(source).Length;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            id = image.Volumes[0].Id;
            virtualLength = image.Device.Length;
            if (container.StartsWith("vhdx", StringComparison.Ordinal)) { Assert.Equal(physical, image.Device.PhysicalSectorSize); }
            Capability capability = writer.QualifyVolume(image, id);
            Assert.True(capability.Available, capability.Reason);
            Assert.True(writer.Qualify(image).Available);
            writer.ValidateCapacity(image, Operations(id), default);
        }
        File.Copy(source, output);
        writer.Apply(output, Operations(id), default);
        Assert.Equal(before, Hash(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, ".sectorworks-volume-*"));
        using (IImageSession image = new ImageOpener().OpenReadOnly(output))
        using (IImageSession original = new ImageOpener().OpenReadOnly(source))
        {
            writer.ValidateOutput(original, image, Operations(id), default);
            Assert.Equal(virtualLength, image.Device.Length);
            Assert.True(writer.QualifyVolume(image, id).Available);
            IReadOnlyFileSystem fs = image.Volumes[0].FileSystem;
            Assert.Contains(fs.ReadDirectory("/"), entry => entry.Name == "RENAMED");
            Assert.DoesNotContain(fs.ReadDirectory("/"), entry => entry.Name == "ADDED.TXT");
            using var text = new StreamReader(fs.OpenRead("/SENTINEL.TXT"));
            Assert.Equal("replacement bytes\n", text.ReadToEnd());
            using var nested = new StreamReader(fs.OpenRead("/RENAMED/CHILD.TXT"));
            Assert.Equal("nested bytes\n", nested.ReadToEnd());
        }
        if (container.Contains("dynamic", StringComparison.Ordinal))
        {
            Assert.Equal(sourceContainerLength, new FileInfo(output).Length);
        }
        string rawOutput = work.PathFor("independent.raw");
        if (container == "raw") { File.Copy(output, rawOutput); }
        else
        {
            DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f", container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vpc",
                "-O", "raw", output, rawOutput);
        }
        using (IImageSession independent = new ImageOpener().OpenReadOnly(rawOutput))
        using (IImageSession managed = new ImageOpener().OpenReadOnly(output))
        {
            Assert.Equal(DiskWriterFixtures.VirtualHash(independent.Device), DiskWriterFixtures.VirtualHash(managed.Device));
        }
        DiskWriterFixtures.CheckFat(work, rawOutput);
    }

    [Theory]
    [InlineData(1024, "outside the selected volumes")]
    [InlineData(1024 * 1024 + 3, "boot-sector bytes")]
    public void OutputValidationRejectsOpaqueAndBootByteChangesAfterApply(int offset, string expectedError)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "mbr", "fat12");
        string output = work.PathFor("corrupt-output.img");
        var writer = new DiskImageWriter();
        using IImageSession original = new ImageOpener().OpenReadOnly(source);
        IReadOnlyList<StagedWriteOperation> operations = Operations(original.Volumes[0].Id);
        File.Copy(source, output);
        writer.Apply(output, operations, default);
        using (var stream = new FileStream(output, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = offset;
            stream.WriteByte(0x7f);
            stream.Flush(true);
        }
        using IImageSession corrupted = new ImageOpener().OpenReadOnly(output);
        Assert.True(writer.Qualify(corrupted).Available);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => writer.ValidateOutput(original, corrupted, operations, default));
        Assert.Contains(expectedError, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleVolumesAreIndependentAndMayBeEditedTogether()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "vhdx-dynamic", "mbr", "fat12", twoFatVolumes: true);
        string output = work.PathFor("multi-output.vhdx");
        string hash = Hash(source);
        string[] ids;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            ids = image.Volumes.Select(volume => volume.Id).ToArray();
            Assert.Equal(2, ids.Length);
            Assert.All(ids, id => Assert.True(new DiskImageWriter().QualifyVolume(image, id).Available));
        }
        File.Copy(source, output);
        new DiskImageWriter().Apply(output, ids.SelectMany(Operations).ToArray(), default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(output))
        {
            foreach (ImageVolume volume in image.Volumes)
            {
                using var text = new StreamReader(volume.FileSystem.OpenRead("/SENTINEL.TXT"));
                Assert.Equal("replacement bytes\n", text.ReadToEnd());
            }
        }
        Assert.Equal(hash, Hash(source));
    }

    [Fact]
    public void ReadOnlyNtfsVolumeDoesNotDisableIndependentQualifiedFatVolume()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "vhdx-fixed", "gpt", "fat12", readOnlyNtfs: true);
        string output = work.PathFor("mixed-output.vhdx");
        string hash = Hash(source);
        var writer = new DiskImageWriter();
        string fatId;
        string ntfsId;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            fatId = image.Volumes.Single(volume => volume.FileSystem.Name == "FAT12").Id;
            ntfsId = image.Volumes.Single(volume => volume.FileSystem.Name == "NTFS").Id;
            Assert.True(writer.Qualify(image).Available);
            Assert.True(writer.QualifyVolume(image, fatId).Available);
            Capability ntfs = writer.QualifyVolume(image, ntfsId);
            Assert.False(ntfs.Available);
            Assert.Contains("Add/mkdir", ntfs.Reason, StringComparison.Ordinal);
            Assert.Contains("ACL inheritance (RawSecurityDescriptor on Linux)", ntfs.Reason, StringComparison.Ordinal);
            Assert.Contains("independently of formatting", ntfs.Reason, StringComparison.Ordinal);
            Assert.Contains("No NTFS writer is qualified", ntfs.Reason, StringComparison.Ordinal);
        }
        File.Copy(source, output);
        writer.Apply(output, Operations(fatId), default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(output))
        {
            using var text = new StreamReader(image.Volumes.Single(volume => volume.Id == ntfsId).FileSystem.OpenRead("/SENTINEL.TXT"));
            Assert.Equal(SectorWorks.DependencyProbe.FixtureCatalog.Sentinel, text.ReadToEnd());
        }
        Assert.Equal(hash, Hash(source));
    }

    [Theory]
    [InlineData("mbr")]
    [InlineData("gpt")]
    public void InconsistentHiddenSectorsAreRejectedWithoutRepair(string layout)
    {
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, "raw", layout, "fat12");
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = 1024 * 1024 + 28;
            stream.Write(new byte[4]);
        }
        string hash = Hash(path);
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        Capability capability = new DiskImageWriter().Qualify(image);
        Assert.False(capability.Available);
        Assert.Contains("hidden sectors", capability.Reason, StringComparison.Ordinal);
        Assert.Equal(hash, Hash(path));
    }

    [Fact]
    public void DamagedBackupGptRemainsBrowseableButCannotBeWritten()
    {
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, "raw", "gpt", "fat12");
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = stream.Length - 512;
            stream.WriteByte(0);
        }
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        Assert.Single(image.Volumes);
        Assert.False(new DiskImageWriter().Qualify(image).Available);
    }

    [Fact]
    public void CancellationAndUnknownVolumeLeaveCopyUntouchedAndNoScratch()
    {
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, "vhd-fixed", "mbr", "fat12");
        string hash = Hash(path);
        Assert.Throws<NotSupportedException>(() => new DiskImageWriter().Apply(path, Operations("missing"), default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new DiskImageWriter().Apply(path, Operations("partition-1"), cancellation.Token));
        Assert.Equal(hash, Hash(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".sectorworks-volume-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownVhdxMetadataAndFourKnRemainGated(bool fourKn)
    {
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, "vhdx-fixed", "filesystem", "fat16");
        if (fourKn)
        {
            DiskWriterFixtures.SetVhdxMetadata(path, new("8141bf1d-a96f-4709-ba47-f233a8faab5f"), 4096);
            DiskWriterFixtures.SetVhdxMetadata(path, new("cda348c7-445d-4471-9cc9-e9885251c556"), 4096);
        }
        else { DiskWriterFixtures.AddUnknownVhdxMetadata(path); }
        string hash = Hash(path);
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        Capability capability = new DiskImageWriter().Qualify(image);
        Assert.False(capability.Available);
        Assert.Contains(fourKn ? "4Kn" : "Unknown VHDX metadata", capability.Reason, StringComparison.Ordinal);
        Assert.Equal(hash, Hash(path));
    }

    [Fact]
    public void FailedVolumeWriterRemovesExtractionAndDoesNotPatchCopy()
    {
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, "vhdx-dynamic", "mbr", "fat12");
        string hash = Hash(path);
        int contentOpens = 0;
        StagedWriteOperation[] operations =
        [
            new(new(ImageChangeKind.AddFile, "partition-1", "/FAIL.TXT"),
                () => ++contentOpens == 1 ? Content("capacity preflight\n") : throw new IOException("injected payload I/O failure")),
        ];
        Assert.Throws<IOException>(() => new DiskImageWriter().Apply(path, operations, default));
        Assert.Equal(2, contentOpens);
        Assert.Equal(hash, Hash(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".sectorworks-volume-*"));
    }

    [Fact]
    public void PosixCleanupFailureReportsResidualScratchAndRetainsOriginalError()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, "raw", "mbr", "fat12");
        string hash = Hash(path);
        string? residual = null;
        int contentOpens = 0;
        var original = new IOException("injected payload failure before patching");
        StagedWriteOperation[] operations =
        [
            new(new(ImageChangeKind.AddFile, "partition-1", "/FAIL.TXT"), () =>
            {
                if (++contentOpens == 1) { return Content("capacity preflight\n"); }
                residual = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, ".sectorworks-volume-*"));
                File.Move(residual, residual + ".owned");
                Directory.CreateDirectory(residual);
                throw original;
            }),
        ];
        IOException error = Assert.Throws<IOException>(() => new DiskImageWriter().Apply(path, operations, default));
        Assert.NotNull(residual);
        Assert.Contains(residual, error.Message, StringComparison.Ordinal);
        Assert.Contains("residual temporary volume", error.Message, StringComparison.Ordinal);
        AggregateException failures = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Same(original, failures.InnerExceptions[0]);
        Assert.True(failures.InnerExceptions[1] is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(residual));
        Assert.True(File.Exists(residual + ".owned"));
        Assert.Equal(hash, Hash(path));
    }

    [Theory]
    [InlineData("vhd-dynamic", 0)]
    [InlineData("vhdx-dynamic", 0)]
    [InlineData("vhdx-dynamic", 2)]
    public void DynamicGrowthAllocatesOnlyRequiredPayloadBlocksAndReopensIndependently(string container, int zeroState)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, "mbr", "fat16");
        if (container == "vhdx-dynamic") { DiskWriterFixtures.SetVhdxSecondPayloadState(source, (ulong)zeroState); }
        string output = work.PathFor("grown.disk");
        long originalLength = new FileInfo(source).Length;
        string originalHash = Hash(source);
        byte[] payload = new byte[10 * 1024 * 1024];
        Array.Fill(payload, (byte)0x5a);
        StagedWriteOperation[] operations =
        [
            new(new(ImageChangeKind.AddFile, "partition-1", "/GROW.BIN"), () => new MemoryStream(payload, false)),
        ];
        File.Copy(source, output);
        new DiskImageWriter().Apply(output, operations, default);
        Assert.InRange(new FileInfo(output).Length, originalLength + 1, originalLength + 16 * 1024 * 1024);
        Assert.Equal(originalHash, Hash(source));
        using (IImageSession image = new ImageOpener().OpenReadOnly(output))
        using (Stream content = image.Volumes[0].FileSystem.OpenRead("/GROW.BIN"))
        {
            Assert.Equal(SHA256.HashData(payload), SHA256.HashData(content));
        }
        string raw = work.PathFor("grown.raw");
        DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f", container == "vhd-dynamic" ? "vpc" : "vhdx", "-O", "raw", output, raw);
        using (IImageSession independent = new ImageOpener().OpenReadOnly(raw))
        using (IImageSession managed = new ImageOpener().OpenReadOnly(output))
        {
            Assert.Equal(DiskWriterFixtures.VirtualHash(independent.Device), DiskWriterFixtures.VirtualHash(managed.Device));
        }
        DiskWriterFixtures.CheckFat(work, raw);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void UnqualifiedVhdxPayloadStatesRemainReadOnly(int state)
    {
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, "vhdx-dynamic", "mbr", "fat16");
        DiskWriterFixtures.SetVhdxSecondPayloadState(path, (ulong)state);
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        Assert.False(new DiskImageWriter().Qualify(image).Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingContainerPayloadAllocationIsRejectedBeforeWriting(bool vhdx)
    {
        using var work = new WorkDirectory();
        string path = DiskWriterFixtures.Create(work, vhdx ? "vhdx-dynamic" : "vhd-dynamic", "mbr", "fat16");
        DiskWriterFixtures.DuplicatePayloadAllocation(path, vhdx);
        string hash = Hash(path);
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        Capability capability = new DiskImageWriter().Qualify(image);
        Assert.False(capability.Available);
        Assert.Contains("overlap", capability.Reason, StringComparison.Ordinal);
        Assert.Equal(hash, Hash(path));
    }

    public static IEnumerable<object[]> LongNameRows() =>
        from container in new[] { "raw", "vhd-fixed", "vhd-dynamic", "vhdx-fixed", "vhdx-dynamic" }
        from layout in new[] { "filesystem", "mbr", "gpt" }
        select new object[] { container, layout };

    [Theory]
    [MemberData(nameof(LongNameRows))]
    public void LongNameVolumesUseSameBoundedWriterAndPreserveUntouchedAliases(string container, string layout)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, layout, "fat16", withLongNames: true);
        string output = work.PathFor("long-output.disk");
        string hash = Hash(source);
        string id;
        FileEntry original;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            id = image.Volumes[0].Id;
            original = image.Volumes[0].FileSystem.ReadDirectory("/").Single(entry => entry.Name == "Untouched long λ.txt");
            Assert.True(new DiskImageWriter().QualifyVolume(image, id).Available);
        }
        StagedWriteOperation[] operations =
        [
            new(new(ImageChangeKind.ReplaceFile, id, "/Existing long λ.txt"), () => Content("replacement\n")),
            new(new(ImageChangeKind.CreateDirectory, id, "/Folder λ"), null),
            new(new(ImageChangeKind.Rename, id, "/Folder λ", NewName: "Renamed λ"), null),
            new(new(ImageChangeKind.AddFile, id, "/Renamed λ/résumé mixed.txt"), () => Content("long content\n")),
            new(new(ImageChangeKind.AddFile, id, "/Delete me.txt"), () => Content("delete\n")),
            new(new(ImageChangeKind.Delete, id, "/Delete me.txt"), null),
        ];
        File.Copy(source, output);
        new DiskImageWriter().Apply(output, operations, default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(output))
        {
            IReadOnlyFileSystem fs = image.Volumes[0].FileSystem;
            FileEntry untouched = fs.ReadDirectory("/").Single(entry => entry.Name == original.Name);
            Assert.Equal(original.ShortName, untouched.ShortName);
            Assert.Equal(original.ModifiedUtc, untouched.ModifiedUtc);
            Assert.Equal(original.Attributes, untouched.Attributes);
            using var text = new StreamReader(fs.OpenRead("/Renamed λ/résumé mixed.txt"));
            Assert.Equal("long content\n", text.ReadToEnd());
            using var replaced = new StreamReader(fs.OpenRead("/Existing long λ.txt"));
            Assert.Equal("replacement\n", replaced.ReadToEnd());
        }
        Assert.Equal(hash, Hash(source));
    }

    private static StagedWriteOperation[] Operations(string id) =>
    [
        new(new(ImageChangeKind.AddFile, id, "/ADDED.TXT"), () => Content("added bytes\n")),
        new(new(ImageChangeKind.ReplaceFile, id, "/SENTINEL.TXT"), () => Content("replacement bytes\n")),
        new(new(ImageChangeKind.CreateDirectory, id, "/NEW"), null),
        new(new(ImageChangeKind.Rename, id, "/NEW", NewName: "RENAMED"), null),
        new(new(ImageChangeKind.AddFile, id, "/RENAMED/CHILD.TXT"), () => Content("nested bytes\n")),
        new(new(ImageChangeKind.Delete, id, "/ADDED.TXT"), null),
    ];
    private static MemoryStream Content(string value) => new(Encoding.ASCII.GetBytes(value), false);
}

internal static class DiskWriterFixtures
{
    internal static string Create(WorkDirectory work, string container, string layout, string fat, bool twoFatVolumes = false, int physical = 512, bool withLongNames = false, bool readOnlyNtfs = false)
    {
        string volume = work.Golden(fat + ".img");
        PrepareFat(volume, fat == "fat32");
        if (withLongNames)
        {
            StagedWriteOperation[] operations =
            [
                new(new(ImageChangeKind.AddFile, "volume-0", "/Existing long λ.txt"), () => new MemoryStream("original"u8.ToArray())),
                new(new(ImageChangeKind.AddFile, "volume-0", "/Untouched long λ.txt"), () => new MemoryStream("unchanged"u8.ToArray())),
            ];
            new RawFatImageWriter().Apply(volume, operations, default);
        }
        string raw = work.PathFor("layout.raw");
        if (layout == "filesystem") { File.Copy(volume, raw); }
        else { Partition(volume, raw, layout, fat, twoFatVolumes, readOnlyNtfs ? work.Golden("ntfs.img") : null); }
        if (container == "raw") { return raw; }
        string result = work.PathFor("source." + (container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vhd"));
        bool vhdx = container.StartsWith("vhdx", StringComparison.Ordinal);
        Run("qemu-img", "convert", "-q", "-f", "raw", "-O", vhdx ? "vhdx" : "vpc", "-o",
            "subformat=" + (container.EndsWith("fixed", StringComparison.Ordinal) ? "fixed" : "dynamic") + (vhdx ? "" : ",force_size=on"),
            raw, result);
        if (vhdx && physical != 512) { SetVhdxMetadata(result, new("cda348c7-445d-4471-9cc9-e9885251c556"), (uint)physical); }
        return result;
    }

    internal static void SetVhdxMetadata(string path, Guid id, uint value)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        long start = MetadataStart(file);
        byte[] metadata = new byte[65536];
        file.Position = start; file.ReadExactly(metadata);
        for (int i = 0; i < U16(metadata, 10); i++)
        {
            int entry = 32 + i * 32;
            if (new Guid(metadata.AsSpan(entry, 16)) != id) { continue; }
            file.Position = start + U32(metadata, entry + 16);
            byte[] bytes = new byte[4]; W32(bytes, 0, value); file.Write(bytes);
            return;
        }
        throw new InvalidDataException("VHDX metadata item not found.");
    }

    internal static void AddUnknownVhdxMetadata(string path)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        long start = MetadataStart(file);
        byte[] metadata = new byte[65536];
        file.Position = start; file.ReadExactly(metadata);
        int count = U16(metadata, 10);
        int entry = 32 + count * 32;
        Guid.NewGuid().TryWriteBytes(metadata.AsSpan(entry, 16));
        W32(metadata, entry + 16, 65580); W32(metadata, entry + 20, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(metadata.AsSpan(10), checked((ushort)(count + 1)));
        file.Position = start; file.Write(metadata);
    }

    internal static void DuplicatePayloadAllocation(string path, bool vhdx)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        long table;
        int entrySize;
        if (vhdx)
        {
            byte[] regions = new byte[65536];
            file.Position = 3 * 65536; file.ReadExactly(regions);
            int entry = Enumerable.Range(0, (int)U32(regions, 8)).Select(i => 16 + i * 32)
                .Single(offset => new Guid(regions.AsSpan(offset, 16)) == new Guid("2dc27766-f623-4200-9d64-115e9bfd4a08"));
            table = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(regions.AsSpan(entry + 16)));
            entrySize = 8;
        }
        else
        {
            byte[] footer = new byte[512];
            file.Position = file.Length - 512; file.ReadExactly(footer);
            byte[] header = new byte[1024];
            file.Position = checked((long)BinaryPrimitives.ReadUInt64BigEndian(footer.AsSpan(16))); file.ReadExactly(header);
            table = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16)));
            entrySize = 4;
        }
        byte[] first = new byte[entrySize];
        file.Position = table; file.ReadExactly(first);
        file.Position = table + entrySize;
        file.Write(first);
    }

    internal static void SetVhdxSecondPayloadState(string path, ulong state)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        byte[] regions = new byte[65536];
        file.Position = 3 * 65536; file.ReadExactly(regions);
        int entry = Enumerable.Range(0, (int)U32(regions, 8)).Select(i => 16 + i * 32)
            .Single(offset => new Guid(regions.AsSpan(offset, 16)) == new Guid("2dc27766-f623-4200-9d64-115e9bfd4a08"));
        long table = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(regions.AsSpan(entry + 16)));
        byte[] value = new byte[8];
        W64(value, 0, state);
        file.Position = table + 8;
        file.Write(value);
    }

    private static long MetadataStart(Stream file)
    {
        byte[] regions = new byte[65536];
        file.Position = 3 * 65536; file.ReadExactly(regions);
        for (int i = 0; i < U32(regions, 8); i++)
        {
            int entry = 16 + i * 32;
            if (new Guid(regions.AsSpan(entry, 16)) == new Guid("8b7ca206-4790-4b9a-b8fe-575f050f886e"))
            {
                return checked((long)BinaryPrimitives.ReadUInt64LittleEndian(regions.AsSpan(entry + 16)));
            }
        }
        throw new InvalidDataException("VHDX metadata region not found.");
    }

    internal static string VirtualHash(IReadOnlyBlockDevice device)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        for (long offset = 0; offset < device.Length;)
        {
            int count = device.Read(offset, buffer.AsSpan(0, (int)Math.Min(buffer.Length, device.Length - offset)));
            if (count == 0) { throw new EndOfStreamException(); }
            hash.AppendData(buffer, 0, count);
            offset += count;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static void Run(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(true); throw new IOException(command + " timed out."); }
        Assert.True(process.ExitCode == 0, command + " failed: " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    internal static void CheckFat(WorkDirectory work, string raw)
    {
        using IImageSession image = new ImageOpener().OpenReadOnly(raw);
        ImageVolume volume = image.Volumes[0];
        string extracted = work.PathFor("native-check.img");
        using (var file = File.Create(extracted))
        {
            byte[] buffer = new byte[65536];
            long length = volume.Partition?.Length ?? image.Device.Length;
            long start = volume.Partition?.Offset ?? 0;
            for (long offset = 0; offset < length;)
            {
                int count = image.Device.Read(start + offset, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)));
                file.Write(buffer, 0, count);
                offset += count;
            }
        }
        if (File.Exists("/usr/sbin/fsck.fat")) { Run("/usr/sbin/fsck.fat", "-n", extracted); }
        if (File.Exists("/usr/bin/mdir")) { Run("/usr/bin/mdir", "-i", extracted, "::/"); }
    }

    private static void PrepareFat(string path, bool fat32)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        byte[] boot = new byte[512];
        file.ReadExactly(boot);
        "DISK-STACK-OPAQUE-BOOT"u8.CopyTo(boot.AsSpan(400));
        file.Position = 0;
        file.Write(boot);
        int reserved = U16(boot, 14);
        uint fatSectors = fat32 ? U32(boot, 36) : U16(boot, 22);
        long root = reserved + boot[16] * (long)fatSectors;
        if (fat32) { root += (U32(boot, 44) - 2L) * boot[13]; }
        int rootLength = fat32 ? boot[13] * 512 : U16(boot, 17) * 32;
        byte[] entries = new byte[rootLength];
        file.Position = root * 512;
        file.ReadExactly(entries);
        int slot = Enumerable.Range(0, entries.Length / 32).First(index => entries[index * 32] == 0) * 32;
        boot.AsSpan(fat32 ? 71 : 43, 11).CopyTo(entries.AsSpan(slot));
        entries[slot + 11] = 8;
        file.Position = root * 512;
        file.Write(entries);
        if (!fat32) { return; }
        int backup = U16(boot, 50);
        file.Position = backup * 512L;
        file.Write(boot);
        byte[] fat = new byte[checked((int)fatSectors * 512)];
        file.Position = reserved * 512L;
        file.ReadExactly(fat);
        long clusters = (U32(boot, 32) - reserved - boot[16] * (long)fatSectors) / boot[13];
        uint free = 0;
        uint next = uint.MaxValue;
        for (uint cluster = 2; cluster < clusters + 2; cluster++)
        {
            if ((U32(fat, checked((int)cluster * 4)) & 0x0fffffff) != 0) { continue; }
            free++;
            if (next == uint.MaxValue) { next = cluster; }
        }
        byte[] info = new byte[512];
        W32(info, 0, 0x41615252); W32(info, 484, 0x61417272);
        W32(info, 488, free); W32(info, 492, next); W32(info, 508, 0xaa550000);
        foreach (long sector in new long[] { U16(boot, 48), backup + U16(boot, 48) })
        {
            file.Position = sector * 512;
            file.Write(info);
        }
    }

    private static void Partition(string volume, string raw, string layout, string fat, bool twoFat, string? readOnlyVolume)
    {
        const long start = 1024 * 1024;
        long length = new FileInfo(volume).Length;
        long second = ((start + length + 1024 * 1024 - 1) / (1024 * 1024)) * 1024 * 1024;
        long secondLength = readOnlyVolume is not null ? new FileInfo(readOnlyVolume).Length : twoFat ? length : 1024 * 1024;
        long total = ((second + secondLength + 2 * 1024 * 1024 - 1) / (1024 * 1024)) * 1024 * 1024;
        using var output = new FileStream(raw, FileMode.CreateNew, FileAccess.ReadWrite);
        output.SetLength(total);
        var mbr = new byte[512];
        "OPAQUE-MBR-BOOT-CODE"u8.CopyTo(mbr.AsSpan(32));
        mbr[510] = 0x55; mbr[511] = 0xaa;
        if (layout == "mbr")
        {
            byte fatType = fat == "fat12" ? (byte)0x01 : fat == "fat32" ? (byte)0x0c : (byte)0x06;
            MbrEntry(mbr, 446, fatType, start, length);
            MbrEntry(mbr, 462, readOnlyVolume is not null ? (byte)0x07 : twoFat ? fatType : (byte)0x83, second, secondLength);
        }
        else
        {
            MbrEntry(mbr, 446, 0xee, 512, total - 512);
            byte[] entries = new byte[16384];
            GuidEntry(entries, 0, start, length, "Primary FAT");
            GuidEntry(entries, 128, second, secondLength, "Untouched opaque");
            Guid diskId = Guid.NewGuid();
            output.Position = 2 * 512;
            output.Write(entries);
            output.Position = total - 33 * 512;
            output.Write(entries);
            output.Position = 512;
            output.Write(GptHeader(total, 1, total / 512 - 1, 2, entries, diskId));
            output.Position = total - 512;
            output.Write(GptHeader(total, total / 512 - 1, 1, total / 512 - 33, entries, diskId));
        }
        output.Position = 0; output.Write(mbr);
        WriteVolume(start);
        if (readOnlyVolume is not null)
        {
            output.Position = second;
            using var input = File.OpenRead(readOnlyVolume);
            input.CopyTo(output, 65536);
        }
        else if (twoFat) { WriteVolume(second); }
        else { output.Position = second; output.Write("UNCHANGED-OPAQUE-PARTITION"u8); }
        output.Position = second - 512; output.Write("UNCHANGED-UNALLOCATED-MARKER"u8);

        void WriteVolume(long offset)
        {
            output.Position = offset;
            using var input = File.OpenRead(volume);
            input.CopyTo(output, 65536);
            output.Position = offset;
            byte[] boot = new byte[512];
            output.ReadExactly(boot);
            W32(boot, 28, checked((uint)(offset / 512)));
            output.Position = offset; output.Write(boot);
            if (U16(boot, 22) == 0)
            {
                output.Position = offset + U16(boot, 50) * 512L;
                output.Write(boot);
            }
        }
    }

    private static void MbrEntry(byte[] mbr, int offset, byte type, long start, long length)
    {
        mbr[offset + 4] = type;
        W32(mbr, offset + 8, checked((uint)(start / 512)));
        W32(mbr, offset + 12, checked((uint)(length / 512)));
    }
    private static void GuidEntry(byte[] entries, int offset, long start, long length, string name)
    {
        new Guid("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7").TryWriteBytes(entries.AsSpan(offset, 16));
        Guid.NewGuid().TryWriteBytes(entries.AsSpan(offset + 16, 16));
        W64(entries, offset + 32, checked((ulong)(start / 512)));
        W64(entries, offset + 40, checked((ulong)((start + length) / 512 - 1)));
        W64(entries, offset + 48, 0x1000000000000001);
        Encoding.Unicode.GetBytes(name).CopyTo(entries, offset + 56);
    }
    private static byte[] GptHeader(long total, long current, long backup, long array, byte[] entries, Guid disk)
    {
        var header = new byte[512];
        "EFI PART"u8.CopyTo(header);
        W32(header, 8, 0x10000); W32(header, 12, 92);
        W64(header, 24, (ulong)current); W64(header, 32, (ulong)backup);
        W64(header, 40, 34); W64(header, 48, (ulong)(total / 512 - 34));
        disk.TryWriteBytes(header.AsSpan(56, 16));
        W64(header, 72, (ulong)array); W32(header, 80, 128); W32(header, 84, 128);
        W32(header, 88, PartitionTests.Crc(entries, 0xedb88320u));
        W32(header, 16, PartitionTests.Crc(header[..92], 0xedb88320u));
        return header;
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void W32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void W64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
}
