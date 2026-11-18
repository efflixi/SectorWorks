using System.Buffers.Binary;
using System.Security.Cryptography;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.Storage;

namespace SectorWorks.ServicesTests;

public sealed class MultiVolumeEditingTests
{
    [Theory]
    [InlineData("Copying image")]
    [InlineData("Applying staged changes")]
    [InlineData("Validating tree and SHA-256")]
    [InlineData("Rechecking source baseline")]
    public async Task MultiVolumeCancellationAtSaveBoundariesKeepsSourceAndHistory(string stage)
    {
        using var work = new EditingTests.EditDirectory();
        string source = CreateDisk(work);
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        foreach (VolumeInfo volume in info.Volumes)
        {
            await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/NEW"));
        }
        using var cancellation = new CancellationTokenSource();
        string output = Path.Combine(work.Path, "cancelled.img");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsAsync(info.Id, output,
            new InlineProgress(progress => { if (progress.Stage == stage) { cancellation.Cancel(); } }), cancellation.Token));
        Assert.False(File.Exists(output));
        Assert.Equal(baseline, Hash(source));
        Assert.Equal(2, (await service.GetEditingSnapshotAsync(info.Id)).Changes.Count);
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    public async Task InjectedWriteOrFlushFailureAfterMultiVolumeApplyCannotPublish(string stage)
    {
        using var work = new EditingTests.EditDirectory();
        string source = CreateDisk(work);
        string baseline = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new FailureAfterApply(stage), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        foreach (VolumeInfo volume in info.Volumes)
        {
            await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/NEW"));
        }
        string output = Path.Combine(work.Path, "failed.img");
        IOException failure = await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, output));
        Assert.Contains(stage, failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Equal(baseline, Hash(source));
        Assert.Equal(2, (await service.GetEditingSnapshotAsync(info.Id)).Changes.Count);
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
        Assert.Empty(Directory.GetDirectories(work.Path, ".sectorworks-volume-*"));
    }

    [Fact]
    public async Task LowSpaceForImportOrOutputRetainsSourceAndPendingChangesWithoutPublishing()
    {
        using var work = new EditingTests.EditDirectory();
        string source = CreateDisk(work);
        string baseline = Hash(source);
        var space = new ControlledSpace { Bytes = 0 };
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.Path, storageSpace: space);
        WorkspaceInfo info = await service.OpenAsync(source);
        string input = work.File("payload", "captured content");
        await Assert.ThrowsAsync<IOException>(() =>
            service.StageAsync(info.Id, new(ImageChangeKind.AddFile, info.Volumes[0].Id, "/TEST.TXT", input)));
        Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        space.Bytes = long.MaxValue;
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, info.Volumes[1].Id, "/TEST.TXT", input));
        space.Bytes = 0;
        string output = Path.Combine(work.Path, "low-space.img");
        IOException failure = await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, output));
        Assert.Contains("destination space", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
        Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        Assert.Equal(baseline, Hash(source));
        Assert.Contains(space.Paths, path => path.Contains(info.Id.ToString("N"), StringComparison.Ordinal));
        Assert.Contains(work.Path, space.Paths);
    }

    [Fact]
    public async Task SamePathsInDifferentVolumesHaveIndependentOverlayAndGlobalOrderedHistory()
    {
        using var work = new EditingTests.EditDirectory();
        string source = CreateDisk(work);
        string hash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.Equal(2, info.Volumes.Count);
        Assert.All(info.Volumes, volume => Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason));
        string first = info.Volumes[0].Id;
        string second = info.Volumes[1].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, first, "/SHARED"));
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, second, "/"), entry => entry.Name == "SHARED");
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, second, "/SHARED"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, first, "/SHARED/TEST.TXT", work.File("first", "first volume")));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, second, "/SHARED/TEST.TXT", work.File("second", "second volume")));
        Assert.Equal("first volume", (await service.PreviewAsync(info.Id, first, "/SHARED/TEST.TXT")).Text);
        Assert.Equal("second volume", (await service.PreviewAsync(info.Id, second, "/SHARED/TEST.TXT")).Text);
        Assert.Equal(2, (await service.SearchAsync(info.Id, "TEST.TXT")).Count);
        await service.UndoAsync(info.Id);
        Assert.Empty(await service.ReadDirectoryAsync(info.Id, second, "/SHARED"));
        Assert.Single(await service.ReadDirectoryAsync(info.Id, first, "/SHARED"));
        await service.RedoAsync(info.Id);
        string output = Path.Combine(work.Path, "saved.img");
        SaveImageResult result = await service.SaveAsAsync(info.Id, output);
        Assert.Equal(new FileInfo(output).Length, result.Length);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(output);
        Assert.Equal(info.Partitions, reopened.Partitions);
        using var firstText = new StreamReader(reopened.Volumes[0].FileSystem.OpenRead("/SHARED/TEST.TXT"));
        using var secondText = new StreamReader(reopened.Volumes[1].FileSystem.OpenRead("/SHARED/TEST.TXT"));
        Assert.Equal("first volume", firstText.ReadToEnd());
        Assert.Equal("second volume", secondText.ReadToEnd());
        Assert.Equal(hash, Hash(source));
        await service.DiscardAsync(info.Id);
        foreach (VolumeInfo volume in info.Volumes)
        {
            Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, volume.Id, "/"), entry => entry.Name == "SHARED");
        }
    }

    [Fact]
    public async Task UnqualifiedVolumeCannotInheritSiblingWriterOrOverlay()
    {
        using var work = new EditingTests.EditDirectory();
        string source = CreateDisk(work);
        using var service = new ExplorerService(new ImageOpener(), writer: new FirstVolumeWriter(), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.True(info.Capabilities.Modify.Available);
        Assert.True(info.Volumes[0].Capabilities!.Modify.Available);
        Assert.False(info.Volumes[1].Capabilities!.Modify.Available);
        string first = info.Volumes[0].Id;
        string second = info.Volumes[1].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, first, "/ONLYONE"));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, second, "/ONLYONE")));
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, second, "/"), entry => entry.Name == "ONLYONE");
        Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
    }

    private static string CreateDisk(EditingTests.EditDirectory work)
    {
        string fat = work.Golden("fat12.img");
        string path = Path.Combine(work.Path, "disk.img");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        output.SetLength(16 * 1024 * 1024);
        byte[] mbr = new byte[512];
        "PHASE2B-OPAQUE-MBR"u8.CopyTo(mbr);
        for (int index = 0; index < 2; index++)
        {
            int start = index == 0 ? 2048 : 8192;
            int entry = 446 + index * 16;
            mbr[entry + 4] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(entry + 8), checked((uint)start));
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(entry + 12), 2880);
            output.Position = start * 512L;
            using var input = File.OpenRead(fat);
            input.CopyTo(output);
            output.Position = start * 512L + 28;
            byte[] hidden = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(hidden, checked((uint)start));
            output.Write(hidden);
        }
        mbr[510] = 0x55;
        mbr[511] = 0xaa;
        output.Position = 0;
        output.Write(mbr);
        return path;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class FirstVolumeWriter : IStagedImageWriter
    {
        private readonly DiskImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public Capability QualifyVolume(IImageSession image, string volumeId) => volumeId == image.Volumes[0].Id
            ? inner.QualifyVolume(image, volumeId) : new(false, "Unqualified test volume.");
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateCapacity(image, operations, token);
        public void Apply(string path, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.Apply(path, operations, token);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateOutput(source, output, operations, token);
    }

    private sealed class ControlledSpace : IStorageSpace
    {
        public long Bytes { get; set; }
        public List<string> Paths { get; } = [];
        public long AvailableBytes(string directory)
        {
            Paths.Add(directory);
            return Bytes;
        }

    }

    private sealed class FailureAfterApply(string stage) : IStagedImageWriter
    {
        private readonly DiskImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public Capability QualifyVolume(IImageSession image, string volumeId) => inner.QualifyVolume(image, volumeId);
        public Capability QualifyCommitVolume(IImageSession image, string volumeId) => inner.QualifyCommitVolume(image, volumeId);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateCapacity(image, operations, token);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateOutput(source, output, operations, token);
        public void Apply(string path, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            inner.Apply(path, operations, token);
            throw new IOException("Injected output " + stage + " failure.");
        }
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
