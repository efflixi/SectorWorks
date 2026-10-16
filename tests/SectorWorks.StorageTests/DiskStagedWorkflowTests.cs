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
        // Phase 3: direct commit reuses this same qualified Apply/ValidateOutput pipeline
        // wrapped with locking/backup/journal, so it is qualified wherever Save As is.
        Assert.True(volume.Capabilities.CommitInPlace.Available, volume.Capabilities.CommitInPlace.Reason);
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

    [Theory]
    [MemberData(nameof(DiskWriterTests.FatRows), MemberType = typeof(DiskWriterTests))]
    public async Task EveryQualifiedRowDirectlyCommitsInPlaceAndLeavesNoResidue(string container, string layout, string fat, int physical)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, layout, fat, physical: physical, withLongNames: true);
        string directory = Path.GetDirectoryName(source)!;
        using var service = new ExplorerService(stagingDirectory: directory);
        WorkspaceInfo info = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.True(volume.Capabilities!.CommitInPlace.Available, volume.Capabilities.CommitInPlace.Reason);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "committed bytes\n");
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/Existing long λ.txt", payload));

        CommitResult result = await service.CommitAsync(info.Id);
        Assert.True(result.FilesValidated > 0);
        Assert.Equal(source, result.Path);

        // No transaction artifacts should remain next to the source after a successful commit.
        Assert.Empty(Directory.GetFiles(directory, "*.sectorworks-journal-*"));
        Assert.Empty(Directory.GetFiles(directory, "*.sectorworks-backup-*"));
        Assert.Empty(Directory.GetFiles(directory, ".sectorworks-commit-*"));
        Assert.Empty(CommitRecovery.Discover(directory));

        string raw = work.PathFor("commit-independent.raw");
        if (container == "raw") { File.Copy(source, raw); }
        else
        {
            DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f",
                container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vpc", "-O", "raw", source, raw);
        }
        DiskWriterFixtures.CheckFat(work, raw);

        using IImageSession reopened = new ImageOpener().OpenReadOnly(source);
        using var text = new StreamReader(reopened.Volumes[0].FileSystem.OpenRead("/Existing long λ.txt"));
        Assert.Equal("committed bytes\n", text.ReadToEnd());
    }

    [Fact]
    public async Task InterruptedCommitBeforeFinalizePreservesSourceAndIsRecoverable()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem-only", "fat32", physical: 512, withLongNames: true);
        string directory = Path.GetDirectoryName(source)!;
        string originalHash = Hash(source);
        var failing = new FailureInjectingWriter(new DiskImageWriter(), failAfterApply: true);
        using var service = new ExplorerService(new ImageOpener(), writer: failing, stagingDirectory: directory);
        WorkspaceInfo info = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(info.Volumes);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "should not land\n");
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/Existing long λ.txt", payload));

        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id));

        // The source must be untouched by a commit that failed before the atomic finalize.
        Assert.Equal(originalHash, Hash(source));
        // Startup-style recovery runs with no live session/open handles on the source.
        await service.CloseAsync(info.Id);
        IReadOnlyList<CommitRecovery.RecoveryAction> pending = CommitRecovery.Discover(directory);
        CommitRecovery.RecoveryAction action = Assert.Single(pending);
        Assert.False(action.SourceWasModified);
        Assert.Equal(source, action.SourcePath);

        CommitRecovery.RestoreFromBackup(action);
        Assert.Equal(originalHash, Hash(source));
        Assert.Empty(CommitRecovery.Discover(directory));
        Assert.Empty(Directory.GetFiles(directory, "*.sectorworks-journal-*"));
        Assert.Empty(Directory.GetFiles(directory, "*.sectorworks-backup-*"));
    }

    [Fact]
    public void RecoveryNeverRestoresBackupOverAnAlreadyFinalizedCommit()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem-only", "fat32", physical: 512, withLongNames: true);
        string committedHash = Hash(source);
        string transactionId = Guid.NewGuid().ToString("N");
        string journalPath = CommitJournal.PathFor(source, transactionId);
        string backupPath = CommitJournal.BackupPathFor(source, transactionId);
        // Simulate an interruption that occurred after the atomic finalize (File.Move) but
        // before cleanup deleted the backup/journal: the backup still has the PRE-commit
        // bytes, but the source already holds the validated, committed bytes.
        File.Copy(source, backupPath);
        File.WriteAllBytes(backupPath, [.. File.ReadAllBytes(backupPath).Select(b => (byte)(b ^ 0xFF))]);
        CommitJournal.Write(journalPath, new TransactionRecord(transactionId, source, backupPath, source + ".stage", CommitStage.Finalized, DateTime.UtcNow));

        CommitRecovery.RecoveryAction action = Assert.Single(CommitRecovery.Discover(Path.GetDirectoryName(source)!));
        Assert.True(action.SourceWasModified);
        Assert.Throws<InvalidOperationException>(() => CommitRecovery.RestoreFromBackup(action));
        Assert.Equal(committedHash, Hash(source));

        CommitRecovery.Discard(action);
        Assert.Equal(committedHash, Hash(source));
        Assert.Empty(CommitRecovery.Discover(Path.GetDirectoryName(source)!));
    }

    private sealed class FailureInjectingWriter(IStagedImageWriter inner, bool failAfterApply) : IStagedImageWriter
    {
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public Capability QualifyVolume(IImageSession image, string volumeId) => inner.QualifyVolume(image, volumeId);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken) =>
            inner.ValidateCapacity(image, operations, cancellationToken);
        public void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
        {
            inner.Apply(copyPath, operations, cancellationToken);
            if (failAfterApply) { throw new IOException("Injected failure after Apply, before reopen/validate/finalize."); }
        }
        public void ValidateOutput(IImageSession source, IImageSession output,
            IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken) =>
            inner.ValidateOutput(source, output, operations, cancellationToken);
    }
}
