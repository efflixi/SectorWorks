using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class DiskStagedWorkflowTests
{
    [Theory]
    [MemberData(nameof(DiskWriterTests.FatRows), MemberType = typeof(DiskWriterTests))]
    public async Task EveryQualifiedRowStagesReviewsAndValidatesNewOutput(string container, string layout, string fat, int physical)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, layout, fat, physical: physical, withLongNames: true);
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        Assert.False(volume.Capabilities.CommitInPlace.Available);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "overlay bytes\n");
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Mixed directory"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume.Id, "/Mixed directory/Added-λ.txt", payload));
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/Existing long λ.txt", payload));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume.Id, "/Mixed directory", NewName: "Renamed directory"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume.Id, "/Temporary λ.txt", payload));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume.Id, "/Temporary λ.txt"));
        EditingSnapshot pending = await service.GetEditingSnapshotAsync(info.Id);
        Assert.Equal(6, pending.Changes.Count);
        Assert.Equal(Enumerable.Range(1, 6).Select(sequence => (long)sequence), pending.Changes.Select(change => change.Sequence));
        await service.UndoAsync(info.Id);
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume.Id, "/"), entry => entry.Name == "Temporary λ.txt");
        await service.RedoAsync(info.Id);
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, volume.Id, "/"), entry => entry.Name == "Temporary λ.txt");
        const string stagedPath = "/Renamed directory/Added-λ.txt";
        Assert.Equal("overlay bytes\n", (await service.PreviewAsync(info.Id, volume.Id, stagedPath)).Text);
        Assert.Contains("6F 76 65 72", (await service.ReadHexAsync(info.Id, volume.Id, stagedPath, 0)).Text, StringComparison.Ordinal);
        SearchHit hit = Assert.Single(await service.SearchAsync(info.Id, "Added-λ"));
        Assert.True(hit.Entry.IsChanged);
        Assert.Equal(stagedPath, hit.Entry.Path);
        string extraction = work.PathFor("extracted");
        Directory.CreateDirectory(extraction);
        ExtractionResult extracted = await service.ExtractAsync(info.Id, volume.Id, [hit.Entry], extraction);
        Assert.Empty(extracted.Failures);
        Assert.Equal("overlay bytes\n", File.ReadAllText(Path.Combine(extraction, "Added-λ.txt")));

        string output = work.PathFor("saved.disk");
        SaveImageResult saved = await service.SaveAsAsync(info.Id, output);
        Assert.True(saved.FilesValidated > 0);
        using (IImageSession reopened = new ImageOpener().OpenReadOnly(output))
        {
            Assert.Equal(info.Metadata.Format, reopened.Metadata.Format);
            Assert.Equal(info.Metadata.VirtualLength, reopened.Device.Length);
            Assert.Equal(info.Partitions, reopened.Partitions);
            using var text = new StreamReader(reopened.Volumes[0].FileSystem.OpenRead(stagedPath));
            Assert.Equal("overlay bytes\n", text.ReadToEnd());
            using var replaced = new StreamReader(reopened.Volumes[0].FileSystem.OpenRead("/Existing long λ.txt"));
            Assert.Equal("overlay bytes\n", replaced.ReadToEnd());
            Assert.DoesNotContain(reopened.Volumes[0].FileSystem.ReadDirectory("/"), entry => entry.Name == "Temporary λ.txt");
        }
        string raw = work.PathFor("service-independent.raw");
        if (container == "raw") { File.Copy(output, raw); }
        else
        {
            DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f",
                container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vpc", "-O", "raw", output, raw);
        }
        DiskWriterFixtures.CheckFat(work, raw);
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        await service.DiscardAsync(info.Id);
        Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, volume.Id, "/"), entry => entry.Name == "Renamed directory");
        Assert.Equal("original", (await service.PreviewAsync(info.Id, volume.Id, "/Existing long λ.txt")).Text);
        Assert.Equal(baseline, Hash(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-save-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-volume-*"));
    }
}
