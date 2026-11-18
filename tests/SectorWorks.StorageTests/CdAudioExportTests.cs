using System.Buffers.Binary;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class CdAudioExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportsSelectedOrAllAudioFromSharedBinWithoutDataOrGaps(bool all)
    {
        using var work = new WorkDirectory();
        string cue = WriteSharedDisc(work, out byte[] bin);
        string sourceCue = File.ReadAllText(cue);
        string output = Directory.CreateDirectory(work.PathFor("output")).FullName;
        using var service = new ExplorerService(stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(cue);
        Assert.Empty(info.Volumes);

        CdAudioExportResult result = await service.ExportCdAudioAsync(info.Id, all ? [3, 2] : [3], output);

        Assert.Equal(all ? ["Track 02.wav", "Track 03.wav"] : new[] { "Track 03.wav" }, result.Completed);
        Assert.False(result.Cancelled);
        Assert.Empty(result.Failures);
        Assert.Equal(result.Completed, Directory.GetFiles(output).Select(Path.GetFileName).Order());
        AssertWave(output, "Track 03.wav", bin[(34 * 2352)..]);
        if (all) { AssertWave(output, "Track 02.wav", bin[(3 * 2352)..(33 * 2352)]); }
        Assert.Equal(bin, File.ReadAllBytes(work.PathFor("shared.bin")));
        Assert.Equal(sourceCue, File.ReadAllText(cue));
    }

    [Fact]
    public async Task ExportsMotorolaTrackFromSeparateMemberWithoutAlteringSamples()
    {
        using var work = new WorkDirectory();
        const int frames = 40 * 588;
        var big = new byte[frames * 4];
        var little = new byte[frames * 4];
        for (int i = 0; i < frames * 2; i++)
        {
            short sample = (short)(i % 32768 - 16384);
            BinaryPrimitives.WriteInt16BigEndian(big.AsSpan(i * 2), sample);
            BinaryPrimitives.WriteInt16LittleEndian(little.AsSpan(i * 2), sample);
        }
        File.WriteAllBytes(work.PathFor("data.bin"), new byte[2048 * 20]);
        File.WriteAllBytes(work.PathFor("audio.bin"), big);
        string cue = work.PathFor("mixed.cue");
        File.WriteAllText(cue, """
            FILE data.bin BINARY
              TRACK 01 MODE1/2048
                INDEX 01 00:00:00
            FILE audio.bin MOTOROLA
              TRACK 02 AUDIO
                PREGAP 00:00:02
                INDEX 01 00:00:00
                POSTGAP 00:00:02
            """);
        using var service = new ExplorerService(stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(cue);
        string output = Directory.CreateDirectory(work.PathFor("output")).FullName;
        CdAudioExportResult result = await service.ExportCdAudioAsync(info.Id, [2], output);

        Assert.Equal("Track 02.wav", Assert.Single(result.Completed));
        Assert.Empty(result.Failures);
        AssertWave(output, "Track 02.wav", little);
        Assert.Equal(big, File.ReadAllBytes(work.PathFor("audio.bin")));
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(3, "FLAGS PRE")]
    [InlineData(3, "FLAGS 4CH")]
    [InlineData(99, "")]
    public async Task InvalidSelectionPublishesNothing(int invalid, string flags)
    {
        using var work = new WorkDirectory();
        string cue = WriteSharedDisc(work, out _, flags);
        string output = Directory.CreateDirectory(work.PathFor("output")).FullName;
        using var service = new ExplorerService(stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(cue);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.ExportCdAudioAsync(info.Id, [2, invalid], output));
        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Fact]
    public async Task CollisionKeepsExistingFileAndExportsOtherSelectedTracks()
    {
        using var work = new WorkDirectory();
        string cue = WriteSharedDisc(work, out byte[] bin);
        string output = Directory.CreateDirectory(work.PathFor("output")).FullName;
        string existing = Path.Combine(output, "Track 02.wav");
        File.WriteAllText(existing, "existing file");
        using var service = new ExplorerService(stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(cue);

        CdAudioExportResult result = await service.ExportCdAudioAsync(info.Id, [2, 3], output);

        Assert.Equal("Track 02.wav", Assert.Single(result.Failures).Path);
        Assert.Equal("Track 03.wav", Assert.Single(result.Completed));
        Assert.Equal("existing file", File.ReadAllText(existing));
        AssertWave(output, "Track 03.wav", bin[(34 * 2352)..]);
        Assert.Equal(2, Directory.GetFileSystemEntries(output).Length);
    }

    [Fact]
    public async Task CancellationReportsAlreadyCompletedTracks()
    {
        using var work = new WorkDirectory();
        string cue = WriteSharedDisc(work, out byte[] bin);
        string output = Directory.CreateDirectory(work.PathFor("output")).FullName;
        using var service = new ExplorerService(stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(cue);
        using var stop = new CancellationTokenSource();

        CdAudioExportResult result = await service.ExportCdAudioAsync(info.Id, [2, 3], output, new CancelAfterFirstTrack(stop), stop.Token);

        Assert.True(result.Cancelled);
        Assert.Empty(result.Failures);
        Assert.Equal("Track 02.wav", Assert.Single(result.Completed));
        Assert.Single(Directory.GetFileSystemEntries(output));
        AssertWave(output, "Track 02.wav", bin[(3 * 2352)..(33 * 2352)]);
    }

    [Fact]
    public void IncompleteAtomicExtractionNeverPublishesFinalFile()
    {
        using var work = new WorkDirectory();
        string output = Directory.CreateDirectory(work.PathFor("output")).FullName;
        using var target = new SafeExtractionTarget(output);
        using (ExtractionFile file = target.CreateAtomicFile("Track 02.wav"))
        {
            file.Stream.Write("incomplete"u8);
            Assert.False(File.Exists(Path.Combine(output, "Track 02.wav")));
            Assert.Throws<OperationCanceledException>(() => file.Complete(new CancellationToken(canceled: true)));
        }
        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    private static string WriteSharedDisc(WorkDirectory work, out byte[] bin, string flags = "")
    {
        bin = Enumerable.Range(0, 39 * 2352).Select(i => (byte)((i + i / 2352) % 251)).ToArray();
        File.WriteAllBytes(work.PathFor("shared.bin"), bin);
        string cue = work.PathFor("mixed.cue");
        File.WriteAllText(cue, $"""
            FILE shared.bin BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:02
                INDEX 01 00:00:03
                POSTGAP 00:00:01
              TRACK 03 AUDIO
                {flags}
                INDEX 00 00:00:33
                INDEX 01 00:00:34
            """);
        return cue;
    }

    private static void AssertWave(string output, string filename, byte[] pcm)
    {
        byte[] wave = File.ReadAllBytes(Path.Combine(output, filename));
        Assert.Equal(44 + pcm.Length, wave.Length);
        Assert.Equal("RIFF"u8.ToArray(), wave[..4]);
        Assert.Equal(wave.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(4)));
        Assert.Equal("WAVEfmt "u8.ToArray(), wave[8..16]);
        using var reader = new BinaryReader(new MemoryStream(wave));
        reader.BaseStream.Position = 16;
        Assert.Equal(16u, reader.ReadUInt32());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(2, reader.ReadUInt16());
        Assert.Equal(44100u, reader.ReadUInt32());
        Assert.Equal(176400u, reader.ReadUInt32());
        Assert.Equal(4, reader.ReadUInt16());
        Assert.Equal(16, reader.ReadUInt16());
        Assert.Equal("data"u8.ToArray(), reader.ReadBytes(4));
        Assert.Equal(pcm.Length, reader.ReadInt32());
        Assert.Equal(pcm, reader.ReadBytes(pcm.Length));
    }

    private sealed class CancelAfterFirstTrack(CancellationTokenSource stop) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            if (value.FilesCompleted == 1) { stop.Cancel(); }
        }
    }
}
