using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class LegacyFatTests
{
    [Fact]
    public async Task Fat12SaveAsPreservesBadClusterMarkersAndExistingApostropheAlias()
    {
        using var work = new WorkDirectory();
        byte[] image = CreateLegacyFloppy(2880, 18, 1, 9, legacy: false);
        const int root = 19 * 512;
        const int badData = 34 * 512;
        "DAILY'S TXT"u8.CopyTo(image.AsSpan(root));
        image[516] = 0x7f;
        image[517] = 0xff;
        image.AsSpan(512, 9 * 512).CopyTo(image.AsSpan(10 * 512));
        image.AsSpan(badData, 512).Fill(0x5a);
        string source = work.PathFor("bad-cluster.img");
        string output = work.PathFor("saved.img");
        string payload = work.PathFor("payload.bin");
        string replacement = work.PathFor("replacement.txt");
        File.WriteAllBytes(source, image);
        File.WriteAllBytes(payload, new byte[2048]);
        File.WriteAllText(replacement, "updated");
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("staging"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.SaveAs.Available, workspace.Capabilities.SaveAs.Reason);
        Assert.False(workspace.Capabilities.CommitInPlace.Available);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, workspace.Volumes[0].Id, "/ADDED.BIN", payload));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, workspace.Volumes[0].Id, "/DAILY'S.TXT", replacement));
        await service.SaveAsAsync(workspace.Id, output);
        byte[] saved = File.ReadAllBytes(output);
        Assert.Equal(image.AsSpan(badData, 512).ToArray(), saved.AsSpan(badData, 512).ToArray());
        Assert.Equal(0xff7, BinaryPrimitives.ReadUInt16LittleEndian(saved.AsSpan(516)) >> 4);
        Assert.Equal(0xff7, BinaryPrimitives.ReadUInt16LittleEndian(saved.AsSpan(10 * 512 + 4)) >> 4);
        Assert.Equal("DAILY'S TXT", Encoding.ASCII.GetString(saved.AsSpan(root, 11)));
        using (IImageSession reopened = new ImageOpener().OpenReadOnly(output))
        using (var reader = new StreamReader(reopened.Volumes[0].FileSystem.OpenRead("/DAILY'S.TXT")))
        { Assert.Equal("updated", reader.ReadToEnd()); }
        Assert.Equal(image, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyFloppyWithoutExtendedBpbUsesRootLabelAndQualifiesOnlySaveAs(bool signed)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("legacy.img");
        byte[] image = CreateLegacyFloppy();
        if (signed)
        {
            image[510] = 0x55;
            image[511] = 0xaa;
        }
        File.WriteAllBytes(path, image);
        byte[] before = SHA256.HashData(image);
        using (IImageSession session = new ImageOpener().OpenReadOnly(path))
        {
            Assert.Empty(session.Partitions);
            IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
            Assert.Equal("FAT12", fs.Name);
            Assert.Equal("LEGACY", fs.VolumeLabel);
            Assert.Equal("HELLO.TXT", Assert.Single(fs.ReadDirectory("/")).Name);
            using var text = new StreamReader(fs.OpenRead("/HELLO.TXT"));
            Assert.Equal("legacy FAT test\n", text.ReadToEnd());
            Assert.Equal(!signed, session.Warnings.Any(warning => warning.Contains("without a 0x55AA", StringComparison.Ordinal)));
            Assert.False(fs.Capabilities.Modify.Available);
            var writer = new RawFatImageWriter();
            Assert.True(writer.Qualify(session).Available, writer.Qualify(session).Reason);
            Assert.False(writer.QualifyCommitVolume(session, session.Volumes[0].Id).Available);
            Assert.Throws<NotSupportedException>(() => session.Device.Write(0, new byte[1]));
        }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData(720, 9, 2, 2, false, true)]
    [InlineData(1440, 9, 2, 3, false, true)]
    [InlineData(1440, 9, 2, 3, true, true)]
    [InlineData(1440, 9, 2, 3, true, false)]
    [InlineData(2400, 15, 1, 7, false, true)]
    [InlineData(2880, 18, 1, 9, false, true)]
    [InlineData(2880, 18, 1, 9, false, false)]
    [InlineData(3360, 21, 4, 3, false, true)]
    [InlineData(5760, 36, 2, 9, false, true)]
    public async Task FloppySaveAsPreservesBootRecordAndIndependentRootLabel(int sectors, int trackSectors,
        int sectorsPerCluster, int sectorsPerFat, bool legacy, bool rootLabeled)
    {
        using var work = new WorkDirectory();
        byte[] original = CreateLegacyFloppy(sectors, trackSectors, sectorsPerCluster, sectorsPerFat, legacy);
        int rootOffset = 512 * (1 + 2 * sectorsPerFat);
        int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(17));
        if (!rootLabeled) { original.AsSpan(rootOffset + 32, 32).Clear(); }
        string source = work.PathFor("source.img");
        string output = work.PathFor("saved.img");
        string content = work.PathFor("new.txt");
        File.WriteAllBytes(source, original);
        File.WriteAllText(content, "new bytes");
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.SaveAs.Available, workspace.Capabilities.SaveAs.Reason);
        Assert.Equal(!legacy && (sectors is 720 or 1440 or 2880), workspace.Capabilities.CommitInPlace.Available);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, workspace.Volumes[0].Id, "/NEW.TXT", content));
        await service.SaveAsAsync(workspace.Id, output);
        VerifyOutput(File.ReadAllBytes(output));
        Assert.Equal(original, File.ReadAllBytes(source));
        if (workspace.Capabilities.CommitInPlace.Available)
        {
            await service.CommitAsync(workspace.Id);
            VerifyOutput(File.ReadAllBytes(source));
        }

        void VerifyOutput(byte[] saved)
        {
            Assert.Equal(original.Length, saved.Length);
            Assert.Equal(original.AsSpan(0, 512).ToArray(), saved.AsSpan(0, 512).ToArray());
            ReadOnlySpan<byte> directory = saved.AsSpan(rootOffset, rootEntries * 32);
            byte[][] labels = Enumerable.Range(0, rootEntries).Select(i => saved.AsSpan(rootOffset + i * 32, 32).ToArray())
                .Where(entry => entry[0] is not (0 or 0xe5) && (entry[11] is 0x08 or 0x28)).ToArray();
            if (rootLabeled) { Assert.Equal(original.AsSpan(rootOffset + 32, 32).ToArray(), Assert.Single(labels)); }
            else { Assert.Empty(labels); }
            Assert.True(directory.IndexOf("NEW     TXT"u8) >= 0);
        }
    }

    [Theory]
    [InlineData("modern-jump")]
    [InlineData("jump-into-bpb")]
    [InlineData("invalid-geometry")]
    [InlineData("different-fat")]
    [InlineData("invalid-reserved-fat")]
    [InlineData("truncated")]
    public void UnsignedFloppyDoesNotBypassStructuralValidation(string damage)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("invalid-legacy.img");
        byte[] image = CreateLegacyFloppy();
        switch (damage)
        {
            case "modern-jump": image[1] = 0x3c; break;
            case "jump-into-bpb": image[1] = 0x16; break;
            case "invalid-geometry": Write16(image, 24, 10); break;
            case "different-fat": image[2048 + 10] = 1; break;
            case "invalid-reserved-fat": image[512] = image[2048] = 0; break;
            case "truncated": image = image[..^512]; break;
        }
        File.WriteAllBytes(path, image);
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    [Fact]
    public void WritableFloppyStillRejectsMultipleRootLabels()
    {
        using var work = new WorkDirectory();
        byte[] image = CreateLegacyFloppy(2880, 18, 1, 9, legacy: false);
        const int root = 19 * 512;
        image.AsSpan(root + 32, 32).CopyTo(image.AsSpan(root + 64, 32));
        string source = work.PathFor("ambiguity.img");
        File.WriteAllBytes(source, image);
        using IImageSession session = new ImageOpener().OpenReadOnly(source);
        Capability capability = new DiskImageWriter().Qualify(session);
        Assert.False(capability.Available);
        Assert.Contains("Multiple FAT root volume-label", capability.Reason, StringComparison.Ordinal);
    }

    private static byte[] CreateLegacyFloppy(int sectors = 1440, int trackSectors = 9,
        int sectorsPerCluster = 2, int sectorsPerFat = 3, bool legacy = true)
    {
        var image = new byte[sectors * 512];
        image[0] = 0xeb;
        image[1] = legacy ? (byte)0x1c : (byte)0x3c;
        image[2] = 0x90;
        Encoding.ASCII.GetBytes("TESTDOS ").CopyTo(image, 3);
        Write16(image, 11, 512);
        image[13] = (byte)sectorsPerCluster;
        Write16(image, 14, 1);
        image[16] = 2;
        ushort rootEntries = sectors == 3360 ? (ushort)16 : sectors >= 2400 ? (ushort)224 : (ushort)112;
        Write16(image, 17, rootEntries);
        Write16(image, 19, (ushort)sectors);
        image[21] = sectors == 720 ? (byte)0xfd : sectors <= 2400 ? (byte)0xf9 : (byte)0xf0;
        Write16(image, 22, (ushort)sectorsPerFat);
        Write16(image, 24, (ushort)trackSectors);
        Write16(image, 26, 2);
        if (legacy) { image.AsSpan(30, 60).Fill(0x7f); }
        else
        {
            image[38] = 0x29;
            "BOOTLABEL  "u8.CopyTo(image.AsSpan(43));
            image[510] = 0x55;
            image[511] = 0xaa;
        }
        image[512] = image[21];
        image[513] = image[514] = image[515] = 0xff;
        image[516] = 0x0f;
        image.AsSpan(512, sectorsPerFat * 512).CopyTo(image.AsSpan((1 + sectorsPerFat) * 512));
        int root = (1 + 2 * sectorsPerFat) * 512;
        Encoding.ASCII.GetBytes("HELLO   TXT").CopyTo(image, root);
        image[root + 11] = 0x20;
        if (!legacy) { Write16(image, root + 24, 0x5021); }
        Write16(image, root + 26, 2);
        byte[] contents = Encoding.UTF8.GetBytes("legacy FAT test\n");
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(root + 28), (uint)contents.Length);
        contents.CopyTo(image, root + rootEntries * 32);
        Encoding.ASCII.GetBytes("LEGACY     ").CopyTo(image, root + 32);
        image[root + 32 + 11] = legacy ? (byte)0x28 : (byte)0x08;
        return image;
    }

    private static void Write16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
