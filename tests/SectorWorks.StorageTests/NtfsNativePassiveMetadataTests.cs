using System.Buffers.Binary;
using System.IO.Compression;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsNativePassiveMetadataTests
{
    private const long VolumeLength = 133103616;

    [LocalNativeFixtureFact]
    public async Task NativeCleanPassiveSubsetPreservesSourceAndMetadataThroughSaveAsAndCommit()
    {
        using var work = new WorkDirectory();
        string input = Materialize(work);
        string original = Hash(input);
        NtfsWriteSnapshot before = CaptureVhd(input);
        Assert.Equal(3, before.PassiveMetadata.Count);
        using var service = new ExplorerService(stagingDirectory: work.PathFor("staging"));
        var workspace = await service.OpenAsync(input);
        var volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        Assert.True(volume.Capabilities.CommitInPlace.Available, volume.Capabilities.CommitInPlace.Reason);
        string addition = work.PathFor("addition.txt"), replacement = work.PathFor("replacement.txt");
        File.WriteAllText(addition, "SectorWorks staged addition.\n");
        File.WriteAllText(replacement, "SectorWorks staged replacement.\n");
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Created"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/added.txt", addition));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/resident.txt", replacement));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Delete, volume.Id, "/delete-me.txt"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/rename-me.txt", NewName: "renamed.txt"));
        string output = work.PathFor("saved.vhd");
        await service.SaveAsAsync(workspace.Id, output);
        Assert.Equal(original, Hash(input));
        AssertSamePassive(before, CaptureVhd(output));
        await service.CommitAsync(workspace.Id);
        Assert.NotEqual(original, Hash(input));
        AssertSamePassive(before, CaptureVhd(input));
        foreach (string path in new[] { input, output })
        {
            using IImageSession image = new ImageOpener().OpenReadOnly(path);
            IReadOnlyFileSystem fs = Assert.Single(image.Volumes).FileSystem;
            Assert.Contains(fs.ReadDirectory("/"), e => e.Name == "Created" && e.IsDirectory);
            Assert.DoesNotContain(fs.ReadDirectory("/"), e => e.Name is "delete-me.txt" or "rename-me.txt");
            using Stream file = fs.OpenRead("/resident.txt");
            Assert.Equal("SectorWorks staged replacement.\n", new StreamReader(file).ReadToEnd());
        }
    }

    [LocalNativeFixtureTheory]
    [InlineData("dirty")]
    [InlineData("txf-enabled")]
    [InlineData("repair-logging-enabled")]
    [InlineData("repair-config")]
    [InlineData("repair-data")]
    [InlineData("deleted-active")]
    [InlineData("unknown-child")]
    [InlineData("user-security-zero")]
    public void AdjacentUnqualifiedStatesRemainRejected(string mutation)
    {
        using var work = new WorkDirectory();
        string raw = MaterializeVolume(work);
        byte[] image = File.ReadAllBytes(raw);
        int cluster = U16(image, 11) * image[13], mft = checked((int)U64(image, 48) * cluster);
        int Value(int id, uint type, string name = "")
        {
            int attribute = FindAttribute(image, mft + id * 1024, type, name);
            return attribute + U16(image, attribute + 20);
        }
        switch (mutation)
        {
            case "dirty": image[Value(3, 0x70) + 10] |= 1; break;
            case "txf-enabled": image[Value(3, 0x70) + 11] = 0x10; break;
            case "repair-logging-enabled": image[Value(3, 0x70) + 11] = 4; break;
            case "repair-config": image[Value(28, 0x80, "$Config") + 4] = 1; break;
            case "repair-data":
                BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(FindAttribute(image, mft + 28 * 1024, 0x80, "") + 48), 1);
                break;
            case "deleted-active": image[Value(29, 0xb0, "$I30")] = 1; break;
            case "unknown-child": image[Value(28, 0x30) + 66 + 2] = (byte)'X'; break;
            case "user-security-zero":
                using (var stream = File.OpenRead(raw))
                using (var fs = new SectorWorks.ManagedNtfs.NtfsFileSystem(stream))
                {
                    int id = (int)(fs.GetFileId("resident.txt") & 0xffffffffffffL);
                    image.AsSpan(Value(id, 0x10) + 52, 4).Clear();
                }
                break;
        }
        File.WriteAllBytes(raw, image);
        using var device = new FileBlockDevice(raw);
        Assert.Throws<ImageFormatException>(() => NtfsWriteValidation.CaptureLegacyCandidate(device));
        if (mutation == "repair-data")
        { Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(raw)); }
        else
        {
            using IImageSession session = new ImageOpener().OpenReadOnly(raw);
            Assert.False(new NtfsImageWriter(includeCandidates: true).Qualify(session, 128).Available);
        }
    }

    [LocalNativeFixtureFact]
    public void OpaqueAllocatedBytesCannotChangeEvenWhenLogicalDeletedIndexIsEmpty()
    {
        using var work = new WorkDirectory();
        string source = MaterializeVolume(work), output = work.PathFor("altered.img");
        File.Copy(source, output);
        using (var stream = File.Open(output, FileMode.Open, FileAccess.ReadWrite))
        using (var fs = new SectorWorks.ManagedNtfs.NtfsFileSystem(stream))
        {
            var extent = Assert.Single(fs.GetFile(29).GetStream(SectorWorks.ManagedNtfs.AttributeType.IndexAllocation, "$I30").GetAbsoluteExtents());
            stream.Position = extent.Start + 4000;
            int value = stream.ReadByte();
            stream.Position--;
            stream.WriteByte((byte)(value ^ 1));
        }
        using (var a = new FileBlockDevice(source))
        using (var b = new FileBlockDevice(output))
        {
            NtfsWriteSnapshot original = NtfsWriteValidation.CaptureLegacyCandidate(a);
            NtfsWriteSnapshot modified = NtfsWriteValidation.CaptureLegacyCandidate(b);
            Assert.NotEqual(original.PassiveMetadata[29], modified.PassiveMetadata[29]);
        }
        using IImageSession originalImage = new ImageOpener().OpenReadOnly(source);
        using IImageSession alteredImage = new ImageOpener().OpenReadOnly(output);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => new NtfsImageWriter(includeCandidates: true)
            .ValidatePreservedMetadata(originalImage, alteredImage, [], default));
        Assert.Contains("passive repair/deleted", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, true)]
    [InlineData(0, ulong.MaxValue, 0, 0, 0, true)]
    [InlineData(1, 1, 0, 0, 0, false)]
    [InlineData(1, 0, 0, 0, 0, false)]
    [InlineData(0, 1, 0, 0, 0, false)]
    [InlineData(0, 0, 4096, 0, 0, false)]
    [InlineData(0, 0, 0, 1, 0, false)]
    [InlineData(0, 0, 0, 0, 1, false)]
    public void EmptyNonresidentRunsRequireExactEmptyLengthsAndVcnBounds(ulong first, ulong last,
        ulong allocated, ulong data, ulong initialized, bool accepted)
    {
        byte[] attribute = new byte[72];
        BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(32), 64);
        foreach (var (offset, value) in new[] { (16, first), (24, last), (40, allocated), (48, data), (56, initialized) })
        { BinaryPrimitives.WriteUInt64LittleEndian(attribute.AsSpan(offset), value); }
        if (accepted) { Assert.Empty(NtfsValidation.Runs(attribute, 4096, VolumeLength, false, allowEmpty: true)); }
        else { Assert.Throws<ImageFormatException>(() => NtfsValidation.Runs(attribute, 4096, VolumeLength, false, allowEmpty: true)); }
    }

    private static string Materialize(WorkDirectory work)
    {
        string path = work.PathFor("native.vhd");
        using (Stream source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "qualification", "native-notxf.vhd.gz")))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (Stream destination = File.Create(path)) { gzip.CopyTo(destination); }
        Assert.Equal("1DEAFC1F66560D676A66B1E3668A56D88B27B4DC50CFEC33F82BBE4469ABC50E", Hash(path));
        return path;
    }

    private static string MaterializeVolume(WorkDirectory work)
    {
        string path = work.PathFor("volume.img");
        using var disk = new DiscUtils.Vhd.Disk(Materialize(work), FileAccess.Read);
        using var volume = new DiscUtils.Streams.SubStream(disk.Content, DiscUtils.Streams.Ownership.None, 65536, VolumeLength);
        using Stream output = File.Create(path);
        volume.CopyTo(output);
        return path;
    }

    private static NtfsWriteSnapshot CaptureVhd(string path)
    {
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        using var volume = new PartitionBlockDevice(image.Device, 65536, VolumeLength);
        return NtfsWriteValidation.CaptureLegacyCandidate(volume);
    }

    private static void AssertSamePassive(NtfsWriteSnapshot source, NtfsWriteSnapshot output)
    {
        Assert.Equal(source.PassiveMetadata.Keys.Order(), output.PassiveMetadata.Keys.Order());
        foreach (var pair in source.PassiveMetadata)
        {
            Assert.Equal(pair.Value, output.PassiveMetadata[pair.Key]);
            Assert.Equal(source.SecurityDescriptors[pair.Key], output.SecurityDescriptors[pair.Key]);
        }
    }

    private static int FindAttribute(byte[] bytes, int record, uint type, string name)
    {
        int position = record + U16(bytes, record + 20);
        while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position)) != uint.MaxValue)
        {
            string actual = System.Text.Encoding.Unicode.GetString(bytes, position + U16(bytes, position + 10), bytes[position + 9] * 2);
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position)) == type && actual == name) { return position; }
            position += checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4)));
        }
        throw new InvalidDataException("Fixture attribute absent.");
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
}

// Absence is explicit unverified local qualification, never a synthetic pass.
public sealed class LocalNativeFixtureFactAttribute : FactAttribute
{
    public LocalNativeFixtureFactAttribute()
    {
        if (!LocalNativeFixtureAvailability.Exists) { Skip = LocalNativeFixtureAvailability.Reason; }
    }
}

public sealed class LocalNativeFixtureTheoryAttribute : TheoryAttribute
{
    public LocalNativeFixtureTheoryAttribute()
    {
        if (!LocalNativeFixtureAvailability.Exists) { Skip = LocalNativeFixtureAvailability.Reason; }
    }
}

internal static class LocalNativeFixtureAvailability
{
    internal const string Reason = "Local Windows-authored native qualification fixture is unavailable; native qualification is NOT VERIFIED.";
    internal static bool Exists => File.Exists(Path.Combine(AppContext.BaseDirectory, "fixtures", "qualification", "native-notxf.vhd.gz"));
}
