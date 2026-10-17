using System.Security.Cryptography;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.ServicesTests.EditingTests;

namespace SectorWorks.ServicesTests;

public sealed class CommitLifecycleTests
{
    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    [InlineData("ntfs.img")]
    public async Task SuccessfulCommitRefreshesReadsHistoryAndTheNextCommitAndSaveAsBaseline(string fixture)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/FIRST"));
        await service.CommitAsync(info.Id);
        EditingSnapshot committed = await service.GetEditingSnapshotAsync(info.Id);
        Assert.False(committed.IsModified);
        Assert.False(committed.CanUndo);
        Assert.False(committed.CanRedo);
        FileEntry first = Assert.Single(await service.ReadDirectoryAsync(info.Id, volume, "/"), entry => entry.Name == "FIRST");
        Assert.False(first.IsChanged);
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/FIRST/SECOND.TXT", work.File("input", "second commit")));
        await service.CommitAsync(info.Id);
        Assert.Equal("second commit", (await service.PreviewAsync(info.Id, volume, "/FIRST/SECOND.TXT")).Text);
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        string committedHash = Hash(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/THIRD"));
        string output = Path.Combine(work.Path, "saved.img");
        await service.SaveAsAsync(info.Id, output);
        Assert.Equal(committedHash, Hash(source));
        using IImageSession saved = new ImageOpener().OpenReadOnly(output);
        Assert.Contains(saved.Volumes[0].FileSystem.ReadDirectory("/"), entry => entry.Name == "THIRD");
        Assert.Empty(Directory.GetFiles(work.Path, "*.sectorworks-journal-*"));
        Assert.Empty(Directory.GetFiles(work.Path, "*.sectorworks-backup-*"));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-commit-*"));
    }

    [Theory]
    [InlineData("Staging changes")]
    [InlineData("Finalizing commit")]
    public async Task CancellationRetainsSourcePendingPayloadsAndUndoRedoForRetry(string cancelStage)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat16.img");
        string original = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/PENDING.TXT", work.File("input", "retained")));
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CommitAsync(info.Id,
            new InlineProgress(progress => { if (progress.Stage == cancelStage) { cancel.Cancel(); } }), cancel.Token));
        Assert.Equal(original, Hash(source));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Equal("retained", (await service.PreviewAsync(info.Id, volume, "/PENDING.TXT")).Text);
        Assert.False((await service.UndoAsync(info.Id)).IsModified);
        Assert.True((await service.RedoAsync(info.Id)).IsModified);
        await service.CommitAsync(info.Id);
        Assert.Equal("retained", (await service.PreviewAsync(info.Id, volume, "/PENDING.TXT")).Text);
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        CommitRecovery.RecoveryAction old = Assert.Single(CommitRecovery.Discover(work.Path));
        string afterCommit = Hash(source);
        Assert.Throws<IOException>(() => CommitRecovery.RestoreFromBackup(old));
        Assert.Equal(afterCommit, Hash(source));
        Assert.True(File.Exists(old.BackupPath));
        CommitRecovery.Discard(old);
    }

    [Fact]
    public async Task FailureAfterClosingTheSourceReopensTheWorkspaceWithoutDroppingChanges()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat16.img");
        string original = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/RETRY"));
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id,
            new InlineProgress(progress =>
            {
                if (progress.Stage == "Acquiring exclusive lock") { throw new IOException("Injected failure before locking."); }
            })));
        Assert.Equal(original, Hash(source));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume, "/"), entry => entry.Name == "RETRY");
        await service.CommitAsync(info.Id);
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
    }

    [Fact]
    public void RecoveryWillNotRollBackAReplacementWhoseFinalJournalWriteWasInterrupted()
    {
        using var work = new EditDirectory();
        string source = work.File("source", "committed output");
        string backup = work.File("backup", "original input");
        string journal = work.File("journal", "retained");
        var action = new CommitRecovery.RecoveryAction(source, journal, backup, CommitStage.Validated, work.File("staging", "staged"));
        Assert.Throws<IOException>(() => CommitRecovery.RestoreFromBackup(action));
        Assert.Equal("committed output", File.ReadAllText(source));
        Assert.True(File.Exists(backup));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public void JournalDeletionFailureRemainsExplicitAndPreservesTheUnexpectedDirectory()
    {
        using var work = new EditDirectory();
        string journal = Path.Combine(work.Path, "journal");
        Directory.CreateDirectory(journal);
        string sentinel = Path.Combine(journal, "retained");
        File.WriteAllText(sentinel, "do not remove");
        Exception? error = Record.Exception(() => CommitJournal.Delete(journal));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Cleanup unexpectedly succeeded.");
        Assert.Equal("do not remove", File.ReadAllText(sentinel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriterAndMetadataValidationFailuresPreserveSourcePayloadsAndRetry(bool failValidation)
    {
        using var work = new EditDirectory();
        string source = work.Golden("ntfs.img");
        string original = Hash(source);
        var writer = new FailingWriter(failValidation);
        using var service = new ExplorerService(new ImageOpener(), writer: writer, stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/PENDING.TXT", work.File("input", "preserve payload")));
        if (failValidation) { await Assert.ThrowsAsync<InvalidDataException>(() => service.CommitAsync(info.Id)); }
        else { await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id)); }
        Assert.Equal(original, Hash(source));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Equal("preserve payload", (await service.PreviewAsync(info.Id, volume, "/PENDING.TXT")).Text);
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-commit-*"));
        CommitRecovery.RecoveryAction interrupted = Assert.Single(CommitRecovery.Discover(work.Path));
        Assert.Equal(original, Hash(interrupted.BackupPath));
        writer.Fail = false;
        await service.CommitAsync(info.Id);
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Equal("preserve payload", (await service.PreviewAsync(info.Id, volume, "/PENDING.TXT")).Text);
        CommitRecovery.Discard(interrupted);
    }

    [Fact]
    public async Task ReopenFailureReportsCommittedSourceBlocksUseAndStillAllowsOwnedPayloadCleanup()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat16.img");
        var opener = new ReopenFailingOpener(source);
        using var service = new ExplorerService(opener, stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/COMMITTED.TXT", work.File("input", "committed")));
        string spool = Path.Combine(work.Path, info.Id.ToString("N"));
        Assert.True(Directory.Exists(spool));
        IOException error = await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id));
        Assert.Contains("Source was committed", error.Message, StringComparison.Ordinal);
        using (IImageSession actual = new ImageOpener().OpenReadOnly(source))
        {
            Assert.Contains(actual.Volumes[0].FileSystem.ReadDirectory("/"), entry => entry.Name == "COMMITTED.TXT");
        }
        IOException unavailable = await Assert.ThrowsAsync<IOException>(() => service.ReadDirectoryAsync(info.Id, volume, "/"));
        Assert.Contains("Close and reopen", unavailable.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(spool));
        await service.CloseAsync(info.Id);
        Assert.False(Directory.Exists(spool));
        opener.Fail = false;
        WorkspaceInfo fresh = await service.OpenAsync(source);
        Assert.False((await service.GetEditingSnapshotAsync(fresh.Id)).IsModified);
    }

    [Fact]
    public async Task PostFinalizeReportingFailureNeverClaimsTheSourceWasUnchanged()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat16.img");
        var log = new FailingCompletionLog();
        using var service = new ExplorerService(new ImageOpener(), log: log, stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/COMMITTED"));
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id));
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, info.Volumes[0].Id, "/"), entry => entry.Name == "COMMITTED");
        Assert.Contains(log.Messages, item => item.Stage == "CommitPostFinalizeFailure" && item.Message.Contains("Source was committed", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Messages, item => item.Message.Contains("source was not modified", StringComparison.OrdinalIgnoreCase));
    }

    private static string Hash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private sealed class FailingWriter(bool failValidation) : IStagedImageWriter
    {
        private readonly DiskImageWriter inner = new();
        public bool Fail { get; set; } = true;
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public Capability QualifyVolume(IImageSession image, string volumeId) => inner.QualifyVolume(image, volumeId);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateCapacity(image, operations, token);
        public void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            inner.Apply(copyPath, operations, token);
            if (Fail && !failValidation) { throw new IOException("Injected post-apply failure."); }
        }
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            inner.ValidateOutput(source, output, operations, token);
            if (Fail && failValidation) { throw new InvalidDataException("Injected metadata validation failure."); }
        }
    }

    private sealed class ReopenFailingOpener(string source) : IImageOpener
    {
        private readonly ImageOpener inner = new();
        private int sourceOpens;
        public bool Fail { get; set; } = true;
        public IImageSession OpenReadOnly(string path, CancellationToken token = default)
        {
            if (Path.GetFullPath(path) == source && ++sourceOpens > 1 && Fail)
            {
                throw new IOException("Injected workspace reopen failure.");
            }
            return inner.OpenReadOnly(path, token);
        }
    }

    private sealed class FailingCompletionLog : IExplorerLog
    {
        public List<(string Stage, string Message)> Messages { get; } = [];
        public void Write(string stage, string path, string message, string level = "Information")
        {
            if (stage == "CommitValidated") { throw new IOException("Injected completion reporting failure."); }
            Messages.Add((stage, message));
        }
    }
}
