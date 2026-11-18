using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class CueImageTests
{
    [Fact]
    public async Task SharedRawAudioFileHasBoundedSeekableProgramAreas()
    {
        using var work = new WorkDirectory();
        byte[] raw = Enumerable.Range(0, 12 * 2352).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(work.PathFor("album.bin"), raw);
        string path = Write(work,
            """
            TITLE "Generated album"
            FILE "album.bin" MOTOROLA
              TRACK 01 AUDIO
                TITLE "First"
                INDEX 01 00:00:00
                INDEX 02 00:00:02
                POSTGAP 00:00:01
              TRACK 02 AUDIO
                INDEX 00 00:00:03
                INDEX 01 00:00:05
            """);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo result = await service.OpenAsync(path);
        Assert.False(result.Capabilities.Modify.Available);
        Assert.True(result.Capabilities.PlayAudioTrack.Available);
        CdSession disc = Assert.Single(result.OpticalDisc!.Sessions);
        Assert.Equal("Generated album", result.OpticalDisc.Title);
        Assert.Equal(3, disc.Tracks[0].SectorCount);
        Assert.Equal(2, disc.Tracks[1].StoredPregap);
        Assert.Equal(6, disc.Tracks[1].StartLba);
        Assert.Equal(7, disc.Tracks[1].SectorCount);
        Assert.True(disc.Tracks[1].BigEndianAudio);
        PreviewResult hex = await service.ReadTrackHexAsync(result.Id, 2, 4096);
        Assert.Contains("Track 02 raw program area", hex.Description);
        Assert.Contains("00001000", hex.Text);
        Assert.True(hex.Truncated);
        using Stream audio = await service.OpenAudioTrackAsync(result.Id, 2);
        audio.Position = 32;
        byte[] actual = new byte[54];
        audio.ReadExactly(actual);
        Assert.Equal(raw.AsSpan(5 * 2352 + 32, 54).ToArray(), actual);
        audio.Position = audio.Length;
        Assert.Equal(-1, audio.ReadByte());
        await service.CloseAsync(result.Id);
        audio.Position = 0;
        Assert.Equal((int)raw[5 * 2352], audio.ReadByte());
        Assert.Equal(raw, File.ReadAllBytes(work.PathFor("album.bin")));
    }

    [Theory]
    [InlineData("../outside.bin", "Unsafe")]
    [InlineData("x/../../outside.bin", "Unsafe")]
    [InlineData("C:\\temp\\outside.bin", "Unsafe")]
    [InlineData("/dev/null", "Unsafe")]
    [InlineData("CON.bin", "Unsafe")]
    [InlineData("not present.bin", "Cannot open")]
    public void RejectsEscapeDeviceAndMissingReferences(string file, string reason)
    {
        using var work = new WorkDirectory();
        string path = Write(work, $"FILE \"{file}\" BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n");
        IOException error = Assert.ThrowsAny<IOException>(() => new ImageOpener().OpenReadOnly(path));
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INDEX 01 00:00:75", 2352)]
    [InlineData("INDEX 00 00:00:00", 2352)]
    [InlineData("INDEX 01 00:00:01", 2352)]
    [InlineData("INDEX 01 00:00:00\nINDEX 02 00:00:00", 4704)]
    [InlineData("INDEX 01 00:00:00", 2353)]
    [InlineData("INDEX 01 00:00:00\nREM SESSION 2", 2352)]
    public void RejectsMalformedOrTruncatedTrack(string directives, int bytes)
    {
        using var work = new WorkDirectory();
        File.WriteAllBytes(work.PathFor("sound.bin"), new byte[bytes]);
        string path = Write(work, $"FILE sound.bin BINARY\nTRACK 01 AUDIO\n{directives}\n");
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    [Fact]
    public void RejectsSymlinkAndFifoWithoutOpeningTheirTargets()
    {
        if (!OperatingSystem.IsLinux()) { return; }
        using var work = new WorkDirectory();
        string real = work.PathFor("actual.bin");
        File.WriteAllBytes(real, new byte[2352]);
        File.CreateSymbolicLink(work.PathFor("sound.bin"), real);
        string path = Write(work, "FILE sound.bin BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n");
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
        File.Delete(work.PathFor("sound.bin"));
        _ = Run("mkfifo", work.PathFor("sound.bin"));
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    [IndependentOpticalTheory]
    [InlineData("single")]
    [InlineData("multi")]
    [InlineData("mixed")]
    [InlineData("case")]
    public async Task SharedEditingReopensAndPreservesTheEntireSet(string variant)
    {
        using var work = new WorkDirectory();
        string source = Author(work, variant == "case" ? "First Data.BIN" : "first data.bin");
        string cueText = "TITLE \"Phase five fixture\"\nFILE \"first data.bin\" BINARY\nTRACK 01 MODE1/2048\nINDEX 01 00:00:00\n";
        string? second = null;
        if (variant == "multi")
        {
            second = Author(work, "second.bin");
            cueText += "FILE second.bin BINARY\nTRACK 02 MODE1/2048\nPREGAP 00:00:02\nINDEX 01 00:00:00\n";
        }
        if (variant == "mixed")
        {
            second = work.PathFor("audio.bin");
            File.WriteAllBytes(second, Enumerable.Range(0, 4 * 2352).Select(i => (byte)(i % 251)).ToArray());
            cueText += "FILE audio.bin BINARY\nTRACK 02 AUDIO\nPREGAP 00:00:02\nINDEX 01 00:00:00\nPOSTGAP 00:00:01\n";
        }
        string cue = Write(work, cueText);
        string sourceHash = Hash(source);
        string? secondHash = second is null ? null : Hash(second);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "edited CD data");
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(cue);
        Assert.True(info.Capabilities.Rebuild.Available, info.Capabilities.Rebuild.Reason);
        Assert.False(info.Capabilities.CommitInPlace.Available);
        Assert.Equal(WorkspaceState.RebuildRequired, info.State);
        string id = info.Volumes[0].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, id, "/NEW"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, id, "/NEW/ADDED.TXT", payload));
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, id, "/ORIGINAL.TXT", payload));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, id, "/OLD.TXT", NewName: "RENAMED.TXT"));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, id, "/DELETE.TXT"));
        if (variant == "multi")
        { await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[1].Id, "/SECOND")); }
        _ = await service.UndoAsync(info.Id);
        _ = await service.RedoAsync(info.Id);
        Assert.Contains("edited CD data", (await service.PreviewAsync(info.Id, id, "/NEW/ADDED.TXT")).Text);
        Assert.Single((await service.SearchAsync(info.Id, "ADDED")));
        Assert.NotEmpty((await service.ReadHexAsync(info.Id, id, "/NEW/ADDED.TXT", 0)).Text);
        string outPath = work.PathFor("rebuilt.cue");
        SaveImageResult result = await service.SaveAsAsync(info.Id, outPath);
        Assert.False(File.Exists(outPath));
        Assert.EndsWith(Path.Combine("rebuilt.cue-set", "rebuilt.cue"), result.Path, StringComparison.Ordinal);
        Assert.Equal(cueText, File.ReadAllText(result.Path));
        WorkspaceInfo reopened = await service.OpenAsync(result.Path);
        Assert.Equal(info.OpticalDisc!.Sessions[0].Tracks.Select(t => (t.Number, t.StartLba, t.SectorCount, t.Mode)),
            reopened.OpticalDisc!.Sessions[0].Tracks.Select(t => (t.Number, t.StartLba, t.SectorCount, t.Mode)));
        string first = Path.Combine(Path.GetDirectoryName(result.Path)!, "first data.bin");
        Assert.Equal(new FileInfo(source).Length, new FileInfo(first).Length);
        Assert.Equal("edited CD data", Text(Run("isoinfo", "-i", first, "-x", "/NEW/ADDED.TXT;1")));
        Assert.Equal("edited CD data", Text(Run("isoinfo", "-i", first, "-x", "/ORIGINAL.TXT;1")));
        string names = Text(Run("isoinfo", "-i", first, "-f"));
        Assert.Contains("/RENAMED.TXT;1", names);
        Assert.DoesNotContain("/DELETE.TXT;1", names);
        if (variant == "multi")
        { Assert.Contains("/SECOND", Text(Run("isoinfo", "-i", Path.Combine(Path.GetDirectoryName(first)!, "second.bin"), "-f"))); }
        if (variant == "mixed")
        {
            Assert.Equal(secondHash, Hash(Path.Combine(Path.GetDirectoryName(first)!, "audio.bin")));
            Assert.True(reopened.Capabilities.PlayAudioTrack.Available);
        }
        Assert.Equal(sourceHash, Hash(source));
        if (second is not null) { Assert.Equal(secondHash, Hash(second)); }
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, outPath));
        await service.DiscardAsync(info.Id);
        Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
    }

    [Fact]
    public void CaseOnlyLookupDoesNotFollowLinksOrGuessBetweenCandidates()
    {
        if (!OperatingSystem.IsLinux()) { return; }
        using var work = new WorkDirectory();
        string real = work.PathFor("actual.bin");
        File.WriteAllBytes(real, new byte[2352]);
        string variant = work.PathFor("Sound.BIN");
        File.CreateSymbolicLink(variant, real);
        string cue = Write(work, "FILE SOUND.BIN BINARY\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n");
        Assert.Contains("regular files", Assert.Throws<ImageFormatException>(
            () => new ImageOpener().OpenReadOnly(cue)).Message, StringComparison.Ordinal);
        File.Delete(variant);
        File.WriteAllBytes(variant, new byte[2352]);
        File.WriteAllBytes(work.PathFor("sound.bin"), new byte[2352]);
        Assert.Contains("Ambiguous", Assert.Throws<ImageFormatException>(
            () => new ImageOpener().OpenReadOnly(cue)).Message, StringComparison.Ordinal);
    }

    [IndependentOpticalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawMode1AndMode2WithoutVerifiedChecksRemainBrowsableButNotEditable(bool mode2)
    {
        using var work = new WorkDirectory();
        byte[] iso = File.ReadAllBytes(Author(work, "data.iso"));
        using (var file = File.Create(work.PathFor("raw.bin")))
        {
            byte[] sector = new byte[2352];
            Array.Fill(sector, (byte)255, 1, 10);
            sector[15] = mode2 ? (byte)2 : (byte)1;
            for (int i = 0; i < iso.Length; i += 2048)
            {
                iso.AsSpan(i, 2048).CopyTo(sector.AsSpan(mode2 ? 24 : 16));
                file.Write(sector);
            }
        }
        string path = Write(work, $"FILE raw.bin BINARY\nTRACK 01 MODE{(mode2 ? 2 : 1)}/2352\nINDEX 01 00:00:00\n");
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(path);
        Assert.True(info.Capabilities.Browse.Available, string.Join("; ", info.Warnings));
        Assert.False(info.Capabilities.Rebuild.Available);
        Assert.Contains("unchanged", (await service.PreviewAsync(info.Id, info.Volumes[0].Id, "/ORIGINAL.TXT")).Text);
        string extract = work.PathFor("extract");
        Directory.CreateDirectory(extract);
        FileEntry item = (await service.ReadDirectoryAsync(info.Id, info.Volumes[0].Id, "/")).Single(x => x.Name == "ORIGINAL.TXT");
        Assert.Equal(1, (await service.ExtractAsync(info.Id, info.Volumes[0].Id, [item], extract)).FilesCompleted);
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(extract, item.Name)));
    }

    [Theory]
    [InlineData(CdTrackMode.Mode1Raw, "106e74a79154f54bc2af27c429337b5f2bf53d6f998c46fd9d0a216c7ca559b1")]
    [InlineData(CdTrackMode.Mode2Raw, "0b059265e3433915424ec81e65423e0be684ae8657ebb20ab52061e7b546e190")]
    public void ParityMatchesIndependentCdrdaoSectors(CdTrackMode mode, string expected)
    {
        // Full-sector SHA-256 from Cdrdao 7a57ad32993cda11a0fa55c97d5b81e82b508b93:
        // address 44137, repeated 0..250 payload and ordinary Form 1 subheader.
        byte[] data = Enumerable.Range(0, 2048).Select(index => (byte)(index % 251)).ToArray();
        byte[] sector = RawSector(mode, 44137, data);
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(sector)));
        Assert.Equal(44137, CdRawSector.Address(sector));
    }

    [IndependentOpticalTheory]
    [InlineData(CdTrackMode.Mode1Raw)]
    [InlineData(CdTrackMode.Mode2Raw)]
    public async Task VerifiedRawDataRebuildKeepsOneDigitCueAndSharedAudioIdentical(CdTrackMode mode)
    {
        using var work = new WorkDirectory();
        byte[] iso = File.ReadAllBytes(Author(work, "data.iso"));
        int dataFrames = iso.Length / 2048;
        byte[] audio = Enumerable.Range(0, 7 * 2352).Select(i => (byte)(i % 251)).ToArray();
        string bin = work.PathFor("disc.bin");
        using (var file = File.Create(bin))
        {
            for (int i = 0; i < dataFrames; i++)
            { file.Write(RawSector(mode, i + 150, iso.AsSpan(i * 2048, 2048))); }
            file.Write(audio);
        }
        string cueText = $"FILE disc.bin BINARY\nTRACK 1 {(mode == CdTrackMode.Mode1Raw ? "MODE1" : "MODE2")}/2352\nINDEX 1 00:00:00\n"
            + $"TRACK 2 AUDIO\nINDEX 0 {Timestamp(dataFrames)}\nINDEX 1 {Timestamp(dataFrames + 2)}\n";
        string path = Write(work, cueText);
        string before = Hash(bin);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo opened = await service.OpenAsync(path);
        Assert.True(opened.Capabilities.Rebuild.Available, opened.Capabilities.Rebuild.Reason);
        Assert.False(opened.Capabilities.CommitInPlace.Available);
        string volume = Assert.Single(opened.Volumes).Id;
        string payload = work.PathFor("payload");
        File.WriteAllText(payload, "new raw sector payload");
        await service.StageAsync(opened.Id, new(ImageChangeKind.AddFile, volume, "/ADDED.TXT", payload));
        SaveImageResult saved = await service.SaveAsAsync(opened.Id, work.PathFor("rebuilt.cue"));
        string outputDirectory = Path.GetDirectoryName(saved.Path)!;
        Assert.Equal(cueText, File.ReadAllText(saved.Path));
        Assert.DoesNotContain(Directory.EnumerateFiles(outputDirectory), f => Path.GetFileName(f).StartsWith(".sectorworks-", StringComparison.Ordinal));
        byte[] output = File.ReadAllBytes(Path.Combine(outputDirectory, "disc.bin"));
        Assert.Equal(dataFrames * 2352 + audio.Length, output.Length);
        Assert.Equal(audio, output.AsSpan(dataFrames * 2352).ToArray());
        string decoded = work.PathFor("decoded.iso");
        using (var stream = File.Create(decoded))
        {
            for (int i = 0; i < dataFrames; i++)
            {
                Assert.Equal(i + 150, CdRawSector.Address(output.AsSpan(i * 2352, 2352)));
                stream.Write(output.AsSpan(i * 2352 + CdRawSector.PayloadOffset(mode), 2048));
            }
        }
        Assert.Equal("new raw sector payload"u8.ToArray(), Run("isoinfo", "-i", decoded, "-x", "/ADDED.TXT;1"));
        Assert.Equal("unchanged"u8.ToArray(), Run("isoinfo", "-i", decoded, "-x", "/ORIGINAL.TXT;1"));
        using IImageSession reopened = new ImageOpener().OpenReadOnly(saved.Path);
        Assert.True(((IImageSetRebuildSource)reopened).QualifyRebuild(volume).Available);
        Assert.Equal(before, Hash(bin));
        Assert.Equal(cueText, File.ReadAllText(path));
    }

    private static byte[] RawSector(CdTrackMode mode, int address, ReadOnlySpan<byte> data)
    {
        var sector = new byte[2352];
        sector.AsSpan(1, 10).Fill(255);
        sector[12] = Bcd(address / 4500);
        sector[13] = Bcd(address / 75 % 60);
        sector[14] = Bcd(address % 75);
        sector[15] = mode == CdTrackMode.Mode2Raw ? (byte)2 : (byte)1;
        if (mode == CdTrackMode.Mode2Raw) { sector[18] = sector[22] = 8; }
        data.CopyTo(sector.AsSpan(CdRawSector.PayloadOffset(mode)));
        CdRawSector.Protect(sector, mode);
        return sector;

        static byte Bcd(int value) => (byte)((value / 10 << 4) | (value % 10));
    }

    private static string Timestamp(int frames) => $"{frames / 4500:D2}:{frames / 75 % 60:D2}:{frames % 75:D2}";

    [IndependentOpticalFact]
    public async Task CancellationRaceAndChangedSourceNeverPublishASet()
    {
        using var work = new WorkDirectory();
        string payload = Author(work, "data.bin");
        string cue = Write(work, "FILE data.bin BINARY\nTRACK 01 MODE1/2048\nINDEX 01 00:00:00\n");
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo info = await service.OpenAsync(cue);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/NEW"));
        using var stop = new CancellationTokenSource();
        string dest = work.PathFor("result.cue");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsAsync(info.Id, dest,
            new ImmediateProgress(step => { if (step.Stage == "Writing BIN/CUE member") { stop.Cancel(); } }), stop.Token));
        Assert.False(Directory.Exists(work.PathFor("result.cue-set")));
        Assert.Empty(Directory.GetDirectories(work.PathFor(""), ".sectorworks-cue-*"));

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, dest,
            new ImmediateProgress(step =>
            {
                if (step.Stage.StartsWith("Reopening", StringComparison.Ordinal))
                { Directory.CreateDirectory(work.PathFor("result.cue-set")); }
            })));
        Assert.Empty(Directory.GetFileSystemEntries(work.PathFor("result.cue-set")));
        Assert.Empty(Directory.GetDirectories(work.PathFor(""), ".sectorworks-cue-*"));

        string changed = work.PathFor("replacement.bin");
        File.Copy(payload, changed);
        using (FileStream output = File.OpenWrite(changed)) { output.WriteByte(9); }
        File.Move(changed, payload, overwrite: true);
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, work.PathFor("changed.cue")));
        Assert.False(Directory.Exists(work.PathFor("changed.cue-set")));
        Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
    }

    [Fact]
    public void StreamsSwappedAndScaledPcmFromTheRequestedFrame()
    {
        byte[] pcm = new byte[2352 * 3];
        for (int i = 0; i < pcm.Length; i += 4)
        {
            BinaryPrimitives.WriteInt16BigEndian(pcm.AsSpan(i), (short)(i % 8192));
            BinaryPrimitives.WriteInt16BigEndian(pcm.AsSpan(i + 2), (short)-(i % 8192));
        }
        using var sink = new AudioCollector();
        using var input = new MemoryStream(pcm);
        CdAudioPlayback.Run(input, sink, 3, 17, true, () => 0.5, null, default);
        byte[] data = sink.Bytes.ToArray();
        Assert.Equal((3 * 588 - 17) * 4, data.Length);
        Assert.Equal(34, BinaryPrimitives.ReadInt16LittleEndian(data));
        Assert.Equal(-34, BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(2)));
        Assert.True(sink.Drained);
        using var shortInput = new MemoryStream(pcm.AsSpan(1).ToArray());
        Assert.Throws<IOException>(() => CdAudioPlayback.Run(shortInput, sink, 3, 0, false, () => 1, null, default));
    }

    private static string Write(WorkDirectory work, string text)
    {
        string path = work.PathFor("disc.cue");
        File.WriteAllText(path, text);
        return path;
    }

    private static string Author(WorkDirectory work, string name)
    {
        string output = work.PathFor(name);
        string root = work.PathFor("fixture");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "ORIGINAL.TXT"), "unchanged");
        File.WriteAllText(Path.Combine(root, "OLD.TXT"), "rename");
        File.WriteAllText(Path.Combine(root, "DELETE.TXT"), "delete");
        _ = Run("genisoimage", "-quiet", "-V", "CUE_FIXTURE", "-o", output, root);
        return output;
    }

    private static string Hash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static byte[] Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in arguments) { start.ArgumentList.Add(arg); }
        using Process process = Process.Start(start) ?? throw new IOException(program);
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var bytes = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(bytes);
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new IOException(program + " timed out."); }
        copy.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, stderr.GetAwaiter().GetResult());
        return bytes.ToArray();
    }

    private sealed class ImmediateProgress(Action<OperationProgress> callback) : IProgress<OperationProgress>
    { public void Report(OperationProgress value) => callback(value); }

    private sealed class AudioCollector : ICdAudioOutput
    {
        public MemoryStream Bytes { get; } = new();
        public bool Drained { get; private set; }
        public long PlayedFrames => Bytes.Length / 4;
        public void Write(byte[] pcm, int count, CancellationToken token) => Bytes.Write(pcm, 0, count);
        public void Drain(CancellationToken token) => Drained = true;
        public void Dispose() => Bytes.Dispose();
    }
}
