using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.Storage;
using Xunit.Abstractions;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsExpandedFailureTests(ITestOutputHelper log)
{
    public static IEnumerable<object[]> Failures()
    {
        foreach (object[] feature in NtfsExpandedWorkflowTests.Features())
        {
            foreach (string failure in new[] { "write", "flush", "validation", "cancel", "space" })
            { yield return [.. feature, failure]; }
        }
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task CandidateFailuresNeverPublishOutputAndRetainHistory(byte major, byte minor, string feature, string failure)
    {
        using var work = new WorkDirectory();
        string source = NtfsExpandedWorkflowTests.CreateCandidateVolume(work, major, minor, feature);
        string original = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new CandidateFaultWriter(failure),
            storageSpace: failure == "space" ? new NoSpace() : null, stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Pending λ"));
        string output = work.PathFor("must-not-publish.img");
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancel")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsAsync(info.Id, output,
                new ImmediateProgress(p => { if (p.Stage == "Applying staged changes") { cancellation.Cancel(); } }), cancellation.Token));
        }
        // Existing service policy wraps output-validation failures in
        // InvalidDataException (which is not an IOException). Keep that policy.
        else if (failure == "validation")
        { await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsAsync(info.Id, output)); }
        else { await Assert.ThrowsAnyAsync<IOException>(() => service.SaveAsAsync(info.Id, output)); }
        Assert.Equal(original, Hash(source));
        Assert.False(File.Exists(output));
        Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-save-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-volume-*"));
    }

    [Theory]
    [MemberData(nameof(NtfsExpandedWorkflowTests.Features), MemberType = typeof(NtfsExpandedWorkflowTests))]
    public async Task CandidateNamesAndCollisionsAreValidatedBeforeStaging(byte major, byte minor, string feature)
    {
        using var work = new WorkDirectory();
        string source = NtfsExpandedWorkflowTests.CreateCandidateVolume(work, major, minor, feature);
        string original = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new DiskImageWriter(includeNtfsCandidates: true),
            stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        string id = Assert.Single(info.Volumes).Id;
        foreach (string name in new[] { "bad.", "CON", "decomposed-e\u0301", "unsupported-\ud83d\ude00", "bad:name" })
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(info.Id,
                new(ImageChangeKind.CreateDirectory, id, "/" + name)));
        }
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.CreateDirectory, id, "/Legacy")));
        Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, id, "/New λ"));
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, id, "/New λ/Child"));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, id, "/New λ"));
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, id, "/"), e => e.Name == "New λ");
        await service.UndoAsync(info.Id);
        Assert.Single(await service.ReadDirectoryAsync(info.Id, id, "/New λ"));
        await service.RedoAsync(info.Id);
        await service.DiscardAsync(info.Id);
        Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        Assert.Equal(original, Hash(source));
    }

    [Theory]
    [MemberData(nameof(NtfsExpandedWorkflowTests.Features), MemberType = typeof(NtfsExpandedWorkflowTests))]
    public async Task CandidateSourceChangesAreDetectedOrPreventedByWindowsSharing(byte major, byte minor, string feature)
    {
        using var work = new WorkDirectory();
        string source = NtfsExpandedWorkflowTests.CreateCandidateVolume(work, major, minor, feature);
        string original = Hash(source);
        long offset;
        using (var read = File.OpenRead(source))
        using (var fs = new SectorWorks.ManagedNtfs.NtfsFileSystem(read))
        { offset = fs.PathToExtents(@"Legacy\PAYLOAD.BIN")[0].Start; }
        using var service = new ExplorerService(new ImageOpener(), writer: new DiskImageWriter(includeNtfsCandidates: true),
            stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, Assert.Single(info.Volumes).Id, "/Pending"));
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<IOException>(() =>
            {
                using var external = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            });
            Assert.Equal(original, Hash(source));
            Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
            log.WriteLine("Windows ordinary deny-write sharing prevented external mutation; no changed-source hash failure is claimed.");
            return;
        }
        using (var external = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        { external.Position = offset; external.WriteByte(0x5a); }
        string changed = Hash(source);
        Assert.NotEqual(original, changed);
        string output = work.PathFor("rejected.img");
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, output));
        Assert.False(File.Exists(output));
        Assert.Equal(changed, Hash(source));
    }

    private sealed class NoSpace : IStorageSpace { public long AvailableBytes(string path) => 0; }
    private sealed class ImmediateProgress(Action<OperationProgress> action) : IProgress<OperationProgress>
    { public void Report(OperationProgress value) => action(value); }

    private sealed class CandidateFaultWriter(string failure) : IStagedImageWriter
    {
        private readonly DiskImageWriter inner = new(includeNtfsCandidates: true);
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public Capability QualifyVolume(IImageSession image, string volumeId) => inner.QualifyVolume(image, volumeId);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
            => inner.ValidateCapacity(image, operations, token);
        public void Apply(string path, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            if (failure is not ("write" or "flush")) { inner.Apply(path, operations, token); return; }
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var fault = new NtfsStagedWorkflowTests.FaultStream(file, failure);
            new NtfsImageWriter(includeCandidates: true).ApplyToStream(fault, operations, token);
        }
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            inner.ValidateOutput(source, output, operations, token);
            if (failure == "validation") { throw new ImageFormatException("Injected expanded NTFS output-validation failure."); }
        }
    }
}
