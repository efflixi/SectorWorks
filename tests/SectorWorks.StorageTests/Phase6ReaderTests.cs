using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class Phase6ReaderTests
{
    private const string FixtureText = "SectorWorks Phase 6 legal fixture.\n";

    [Theory]
    [InlineData("disk.vmdk", "VMDK", "FAT16")]
    [InlineData("disk.vdi", "VDI", "FAT16")]
    [InlineData("disk.qcow2", "QCOW2", "FAT16")]
    [InlineData("disk.dmg", "DMG", "FAT16")]
    [InlineData("disk.pmfx", "PMFX", "FAT16")]
    [InlineData("chain-fat32.pmfx", "PMFX", "FAT32")]
    [InlineData("disk.gho", "GHO", "FAT16")]
    [InlineData("floppy.imd", "ImageDisk IMD", "FAT12")]
    [InlineData("track.bin", "Raw CD", "ISO9660")]
    [InlineData("file-ext2.raw", "Raw", "ext2")]
    [InlineData("file-ext3.raw", "Raw", "ext3")]
    [InlineData("file-ext4.raw", "Raw", "ext4")]
    [InlineData("exfat.raw", "Raw", "exFAT")]
    [InlineData("hfs-content.raw", "Raw", "HFS+")]
    [InlineData("nero5.img", "NRG", "ISO9660")]
    [InlineData("nero4.nrg", "NRG", "ISO9660")]
    [InlineData("clone.ccd", "CloneCD", "ISO9660", "clone.img", "clone.sub")]
    [InlineData("clone.img", "CloneCD", "ISO9660", "clone.ccd", "clone.sub")]
    [InlineData("alcohol.mds", "MDS/MDF", "ISO9660", "alcohol.mdf")]
    [InlineData("alcohol.mdf", "MDS/MDF", "ISO9660", "alcohol.mds")]
    public async Task NewReaderExtractsThroughExplorerWithoutWriteCapabilities(string name, string format,
        string filesystem, string? companion = null, string? sub = null)
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, name);
        string[] members = new[] { path, companion is null ? null : Unpack(work, companion),
            sub is null ? null : Unpack(work, sub) }.OfType<string>().ToArray();
        string[] baseline = members.Select(Hash).ToArray();
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        Assert.Equal(format, workspace.Metadata.Format);
        Assert.False(workspace.Capabilities.Modify.Available);
        Assert.False(workspace.Capabilities.SaveAs.Available);
        Assert.False(workspace.Capabilities.CommitInPlace.Available);
        Assert.False(workspace.Capabilities.Rebuild.Available);
        var volume = Assert.Single(workspace.Volumes);
        Assert.Equal(filesystem, volume.FileSystem);
        var root = await service.ReadDirectoryAsync(workspace.Id, volume.Id, "/");
        FileEntry folder = Assert.Single(root, entry => entry.IsDirectory && entry.Name.Equals("nested", StringComparison.OrdinalIgnoreCase));
        var files = await service.ReadDirectoryAsync(workspace.Id, volume.Id, folder.Path);
        FileEntry proof = Assert.Single(files, entry => entry.Name.Equals("proof.txt", StringComparison.OrdinalIgnoreCase));
        string destination = work.PathFor("extracted");
        Directory.CreateDirectory(destination);
        await service.ExtractAsync(workspace.Id, volume.Id, [folder], destination);
        string written = Assert.Single(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
        Assert.Equal(FixtureText, File.ReadAllText(written));
        Assert.Equal(FixtureText.Length, proof.Length);
        await service.CloseAsync(workspace.Id);
        Assert.Equal(baseline, members.Select(Hash));
    }

    [Fact]
    public void PmfxReconstructsSparseVerbatimChainedAndPartialBlocksExactly()
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, "chain-fat32.pmfx");
        using var image = new ImageOpener().OpenReadOnly(path);
        Assert.Equal(67141632, image.Device.Length);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        for (long offset = 0; offset < image.Device.Length;)
        {
            int count = image.Device.Read(offset, buffer.AsSpan(0, (int)Math.Min(buffer.Length, image.Device.Length - offset)));
            Assert.True(count > 0);
            digest.AppendData(buffer, 0, count);
            offset += count;
        }
        Assert.Equal("1E56EDE22C2D3A66EF0F557B442A2044E3EFF7C7A4D650F60838452B451D01F2", Convert.ToHexString(digest.GetHashAndReset()));
    }

    [Fact]
    public void ApfsReadsAnIndependentlyAuthoredMultiLevelTreeAndKeepsLinksOpaque()
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, "apfs.img");
        string before = Hash(path);
        using (IImageSession session = new ImageOpener().OpenReadOnly(path))
        {
            IReadOnlyFileSystem volume = Assert.Single(session.Volumes).FileSystem;
            Assert.Equal("APFS", volume.Name);
            Assert.Equal("QNXPROBE", volume.VolumeLabel);
            Assert.True(Assert.Single(volume.ReadDirectory("/"), item => item.Name == "link.txt").IsLink);
            Assert.Equal(400, volume.ReadDirectory("/many").Count);
            Assert.Throws<ImageFormatException>(() => volume.OpenRead("/link.txt"));
            Assert.Throws<ImageFormatException>(() => volume.OpenRead("/decmpfs.txt"));
            using var file = volume.OpenRead("/dir/sub/deep.txt");
            Assert.Equal("AFD6FC1CDCFB26C92CA0C39B281828E0E6BDEDAFBBBB302D47E5CEA01731A9BA", Convert.ToHexString(SHA256.HashData(file)));
            using var sparse = volume.OpenRead("/sparse.bin");
            Assert.Equal("93065B3704FFE4193867A33B019B088F52F1D02DCBD239136F618499F4707261", Convert.ToHexString(SHA256.HashData(sparse)));
        }
        Assert.Equal(before, Hash(path));
    }

    [Fact]
    public void RockRidgeChildLinkResolvesTheLogicalDirectoryWithoutEnablingRebuild()
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, "rock-ridge.iso");
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        Assert.Contains(image.Warnings, warning => warning.Contains("Rock Ridge directory relocation", StringComparison.Ordinal));
        IReadOnlyFileSystem filesystem = Assert.Single(image.Volumes).FileSystem;
        using Stream data = filesystem.OpenRead("/01/02/03/04/05/06/07/08/09/10/proof.txt");
        Assert.Equal(FixtureText, new StreamReader(data).ReadToEnd());
        Assert.IsNotAssignableFrom<IImageRebuildSource>(filesystem);
    }

    [Fact]
    public void CookedIsoAllowsOnlyUnreferencedMissingTailAndStaysReadOnly()
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, "rock-ridge.iso");
        using (FileStream output = File.OpenWrite(path)) { output.SetLength(output.Length - 50 * 2048); }
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        Assert.Contains(image.Warnings, warning => warning.Contains("50 missing trailing blocks", StringComparison.Ordinal));
        Assert.False(Assert.Single(image.Volumes).FileSystem.Capabilities.Rebuild.Available);
    }

    [Fact]
    public void CombinedStoredAndUnstoredAudioGapsRemainSeparateAndDoNotShiftFileBytes()
    {
        using var work = new WorkDirectory();
        byte[] bytes = new byte[2352 * 9];
        bytes[4 * 2352] = 0x35;
        File.WriteAllBytes(work.PathFor("disc.bin"), bytes);
        string path = work.PathFor("disc.cue");
        File.WriteAllText(path,
            """
            FILE disc.bin BINARY
              TRACK 01 AUDIO
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                PREGAP 00:00:02
                INDEX 00 00:00:03
                INDEX 01 00:00:04
            """, Encoding.Unicode);
        using IImageSession image = new ImageOpener().OpenReadOnly(path);
        var album = Assert.IsAssignableFrom<ICdImageSession>(image);
        CdTrack track = Assert.Single(album.Disc.Sessions).Tracks[1];
        Assert.Equal(1, track.StoredPregap);
        Assert.Equal(2, track.Pregap);
        Assert.Equal(6, track.StartLba);
        using Stream stream = album.OpenAudioTrack(2);
        Assert.Equal(0x35, stream.ReadByte());
        Assert.False(Assert.IsAssignableFrom<IImageSetRebuildSource>(image).QualifyRebuild(null).Available);
    }

    [Theory]
    [InlineData("music.nrg", null)]
    [InlineData("music.mds", "music.mdf")]
    [InlineData("audio.ccd", "audio.img")]
    public async Task AlternateCdContainersExportTheOriginalPcmProgram(string descriptor, string? payload)
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, descriptor);
        if (payload is not null) { Unpack(work, payload); }
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        var track = Assert.Single(Assert.Single(workspace.OpticalDisc!.Sessions).Tracks);
        Assert.True(track.Playback.Available);
        Assert.False(track.BigEndianAudio);
        Assert.False(workspace.Capabilities.Rebuild.Available);
        string destination = work.PathFor("audio-export");
        Directory.CreateDirectory(destination);
        var results = await service.ExportCdAudioAsync(workspace.Id, [1], destination);
        Assert.Empty(results.Failures);
        string file = Assert.Single(Directory.EnumerateFiles(destination));
        byte[] wav = File.ReadAllBytes(file);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(Enumerable.Range(0, 2352).Select(index => (byte)(index & 255)), wav[44..]);
    }

    [Fact]
    public void CloneCdRejectsACompanionWithDifferentSectorCount()
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, "clone.ccd");
        Unpack(work, "clone.img");
        File.WriteAllBytes(work.PathFor("clone.sub"), new byte[96]);
        var error = Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
        Assert.Contains("different sector count", error.Message);
    }

    [Fact]
    public void RecognizedSparseContainerNeverFallsBackToRawAfterInvalidHeader()
    {
        using var work = new WorkDirectory();
        string path = Unpack(work, "disk.qcow2");
        using (FileStream output = File.OpenWrite(path))
        {
            output.Position = 7;
            output.WriteByte(7);
        }
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("trailing")]
    [InlineData("checksum")]
    [InlineData("expanded")]
    public void ExactZlibRejectsIncompleteOrAmbiguousMembers(string damage)
    {
        byte[] input = Encoding.ASCII.GetBytes("fixture");
        using var encoded = new MemoryStream();
        using (var compressor = new ZLibStream(encoded, CompressionLevel.Optimal, leaveOpen: true))
        { compressor.Write(input); }
        byte[] invalid = encoded.ToArray();
        if (damage == "short") { invalid = invalid[..^1]; }
        if (damage == "trailing") { invalid = [.. invalid, 0x11]; }
        if (damage == "checksum") { invalid[^1] ^= 1; }
        Assert.Throws<ImageFormatException>(() => ExactZlib.Inflate("test", invalid,
            new byte[input.Length - (damage == "expanded" ? 1 : 0)]));
    }

    private static string Unpack(WorkDirectory work, string name)
    {
        string path = work.PathFor(name);
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "phase6", name + ".gz"));
        using var decompressed = new GZipStream(input, CompressionMode.Decompress);
        using var output = File.Create(path);
        decompressed.CopyTo(output);
        return path;
    }
}
