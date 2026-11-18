using System.Text;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using Xunit.Abstractions;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

// Final matrix uses the public product writer. Earlier retained candidate
// runs used explicit internal eligibility switches before product enablement.
public sealed class NtfsExpandedWorkflowTests(ITestOutputHelper log)
{
    public static IEnumerable<object[]> Features()
    {
        foreach ((byte major, byte minor) in new (byte, byte)[] { (1, 2), (3, 0), (3, 1) })
        {
            yield return [major, minor, "ordinary"];
            foreach (string residency in new[] { "resident", "nonresident" })
            {
                foreach (string feature in new[] { "moved-data", "split-data", "moved-index" })
                { yield return [major, minor, residency + ":" + feature]; }
            }
        }
    }

    public static IEnumerable<object[]> DiskRows()
    {
        foreach (object[] feature in Features())
        {
            foreach (object[] disk in NtfsStagedWorkflowTests.Rows())
            { yield return [.. feature, .. disk]; }
        }
    }

    [Theory]
    [MemberData(nameof(Features))]
    public Task RawCandidateStagesAndSavesWithIndependentChecks(byte major, byte minor, string feature)
        => Workflow(major, minor, feature, "raw", "filesystem", 512);

    [Theory]
    [MemberData(nameof(DiskRows))]
    public Task EveryCandidateDiskStagesAndSavesWithIndependentChecks(byte major, byte minor, string feature,
        string container, string layout, int physical) => Workflow(major, minor, feature, container, layout, physical);

    [Theory]
    [MemberData(nameof(DiskRows))]
    public Task EveryCandidateDiskDirectlyCommitsAfterValidatedSaveAs(byte major, byte minor, string feature,
        string container, string layout, int physical) => Workflow(major, minor, feature, container, layout, physical, commit: true);

    private async Task Workflow(byte major, byte minor, string feature, string container, string layout, int physical, bool commit = false)
    {
        using var work = new WorkDirectory();
        string row = ExpandedWriterHandoff.Row(major, minor, feature, container, layout, physical);
        string? source = ExpandedWriterHandoff.Import(work, row);
        if (source is null)
        {
            string volumePath = CreateCandidateVolume(work, major, minor, feature);
            source = DiskWriterFixtures.Create(work, container, layout, "ntfs", physical: physical, ntfsCandidateVolume: volumePath);
            ExpandedWriterHandoff.Export(row, source);
        }
        string original = Hash(source);
        await RunWorkflow(work, source, original, major, minor, feature, container, row, commit);
    }

    internal static string CreateCandidateVolume(WorkDirectory work, byte major, byte minor, string feature)
    {
        string volumePath = NtfsCompatibilityFixtures.Create(work, major, minor, forWriterCandidate: true).Path;
        if (feature != "ordinary")
        {
            _ = new NtfsAttributeListTests.ExtensionFixture(work, feature.StartsWith("nonresident:", StringComparison.Ordinal),
                feature.EndsWith(":moved-index", StringComparison.Ordinal), feature.EndsWith(":split-data", StringComparison.Ordinal),
                existingPath: volumePath);
        }
        return volumePath;
    }

    private async Task RunWorkflow(WorkDirectory work, string source, string original, byte major, byte minor, string feature, string container, string row, bool commit)
    {
        using (IImageSession untouched = new ImageOpener().OpenReadOnly(source))
        {
            Capability eligibility = new DiskImageWriter().Qualify(untouched);
            Assert.True(eligibility.Available, eligibility.Reason);
        }
        var writer = new DiskImageWriter();
        using var service = new ExplorerService(new ImageOpener(), writer: writer, stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        string small = work.PathFor("small.txt");
        File.WriteAllText(small, "candidate shared overlay\n");
        string large = work.PathFor("large.bin");
        byte[] payload = Enumerable.Range(0, 19000).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(large, payload);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/New λ directory"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume.Id, "/New λ directory/Added.txt", small));
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume.Id, NtfsCompatibilityFixtures.ResidentPath, large));
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/LARGE.BIN", small));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume.Id, "/New λ directory", NewName: "Renamed λ directory"));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume.Id, "/Renamed λ directory/Added.txt", NewName: "Renamed.txt"));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume.Id, NtfsCompatibilityFixtures.UnicodePath));
        Assert.Equal(7, (await service.GetEditingSnapshotAsync(info.Id)).Changes.Count);
        await service.UndoAsync(info.Id);
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume.Id, "/Legacy"), e => e.Name == "Unicode-λ.txt");
        await service.RedoAsync(info.Id);
        const string staged = "/Renamed λ directory/Renamed.txt";
        Assert.Equal("candidate shared overlay\n", (await service.PreviewAsync(info.Id, volume.Id, staged)).Text);
        Assert.Contains("63 61 6E 64", (await service.ReadHexAsync(info.Id, volume.Id, staged, 0)).Text);
        SearchHit hit = Assert.Single(await service.SearchAsync(info.Id, "Renamed.txt"));
        string extracted = work.PathFor("extracted"); Directory.CreateDirectory(extracted);
        Assert.Empty((await service.ExtractAsync(info.Id, volume.Id, [hit.Entry], extracted)).Failures);
        Assert.Equal(File.ReadAllBytes(small), File.ReadAllBytes(Path.Combine(extracted, "Renamed.txt")));
        string output = work.PathFor("saved.disk");
        SaveImageResult result = await service.SaveAsAsync(info.Id, output);
        Assert.True(result.FilesValidated > 0);
        Assert.Equal(original, Hash(source));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        if (commit)
        {
            Assert.True(volume.Capabilities.CommitInPlace.Available, volume.Capabilities.CommitInPlace.Reason);
            await service.CommitAsync(info.Id);
            Assert.NotEqual(original, Hash(source));
            EditingSnapshot cleared = await service.GetEditingSnapshotAsync(info.Id);
            Assert.False(cleared.IsModified);
            Assert.False(cleared.CanUndo);
            Assert.False(cleared.CanRedo);
            string firstCommit = Hash(source);
            await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/SECOND COMMIT"));
            await service.CommitAsync(info.Id);
            Assert.NotEqual(firstCommit, Hash(source));
            Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
            Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume.Id, "/"), e => e.Name == "SECOND COMMIT");
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, "*.sectorworks-journal-*"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, "*.sectorworks-backup-*"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-commit-*"));
            output = source;
        }
        if (ExpandedWriterHandoff.Queue(source, output, row, commit ? "commit" : "saveas"))
        {
            if (!commit) { await service.DiscardAsync(info.Id); }
            Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
            if (!commit) { Assert.Equal(original, Hash(source)); }
            return;
        }
        string independentRaw = output;
        if (container != "raw")
        {
            independentRaw = work.PathFor("independent.raw");
            DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f",
                container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vpc", "-O", "raw", output, independentRaw);
        }
        string nativeVolume = work.PathFor("native-volume.img");
        using (IImageSession image = new ImageOpener().OpenReadOnly(independentRaw))
        using (FileStream copied = File.Create(nativeVolume))
        {
            ImageVolume actual = Assert.Single(image.Volumes);
            long start = actual.Partition?.Offset ?? 0;
            long length = actual.Partition?.Length ?? image.Device.Length;
            byte[] buffer = new byte[65536];
            for (long position = 0; position < length;)
            {
                int read = image.Device.Read(start + position, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - position)));
                Assert.True(read > 0); copied.Write(buffer, 0, read); position += read;
            }
        }
        foreach (string command in new[] { "ntfsfix", "ntfsinfo" })
        { log.WriteLine(Encoding.UTF8.GetString(NtfsStagedWorkflowTests.RunNative(command, command == "ntfsfix" ? "-n" : "-m", nativeVolume))); }
        Assert.Equal(payload, NtfsStagedWorkflowTests.RunNative("ntfscat", nativeVolume, NtfsCompatibilityFixtures.ResidentPath));
        Assert.Equal(File.ReadAllBytes(small), NtfsStagedWorkflowTests.RunNative("ntfscat", nativeVolume, "/LARGE.BIN"));
        Assert.Equal(File.ReadAllBytes(small), NtfsStagedWorkflowTests.RunNative("ntfscat", nativeVolume, staged));
        if (!commit) { Assert.Equal(original, Hash(source)); await service.DiscardAsync(info.Id); }
        Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
    }
}
