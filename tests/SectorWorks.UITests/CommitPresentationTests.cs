using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

[Collection("Headless UI")]
public sealed class CommitPresentationTests
{
    [Theory]
    [InlineData("fat12.img")]
    [InlineData("ntfs.img")]
    public async Task KeyboardCommitConfirmsRefreshesHistoryAndSupportsSaveAsThenRepeatedCommit(string fixture)
    {
        using var files = new EditingFiles();
        string source = files.Golden(fixture);
        string original = files.Hash(source);
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ExplorerService(stagingDirectory: files.Path);
            var dialogs = new EditingDialogs { Name = "FIRST", OutputPath = Path.Combine(files.Path, "saved.img") };
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { source });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                Assert.False(workspace.CanCommit);
                await workspace.NewDirectoryCommand.ExecuteAsync(null);
                Assert.True(workspace.CanCommit, workspace.CommitReason);
                dialogs.Confirm = false;
                await workspace.CommitCommand.ExecuteAsync(null);
                Assert.True(workspace.IsModified);
                Assert.Equal(original, files.Hash(source));
                Assert.Contains("cancelled", workspace.Status);
                await workspace.SaveAsCommand.ExecuteAsync(null);
                Assert.True(workspace.IsModified);
                Assert.Equal(original, files.Hash(source));
                dialogs.Confirm = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Commit") && b.IsEffectivelyEnabled);
                window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
                window.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
                await workspace.CommitCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                Assert.False(workspace.IsModified);
                Assert.False(workspace.CanUndo);
                Assert.False(workspace.CanRedo);
                Assert.False(workspace.CanCommit);
                Assert.NotEqual(original, files.Hash(source));
                Assert.Contains("Committed source", workspace.Validation);
                Assert.DoesNotContain("source unchanged", workspace.Status, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(workspace.Entries, e => e.Name == "FIRST" && !e.IsChanged);
                dialogs.Name = "SECOND";
                await workspace.NewDirectoryCommand.ExecuteAsync(null);
                await workspace.CommitCommand.ExecuteAsync(null);
                Assert.Empty(workspace.Error);
                Assert.False(workspace.IsModified);
                Assert.Contains(workspace.Entries, e => e.Name == "SECOND" && !e.IsChanged);
                Assert.True(await workspace.ConfirmCloseAsync());
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExternalMutationIsBlockedBySharingOrCommitRejectsItsChangedSource()
    {
        using var files = new EditingFiles();
        string source = files.Golden("fat12.img");
        using var service = new ExplorerService(stagingDirectory: files.Path);
        var dialogs = new EditingDialogs { Name = "PENDING" };
        WorkspaceInfo info = await service.OpenAsync(source);
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.NewDirectoryCommand.ExecuteAsync(null);
        if (OperatingSystem.IsWindows())
        {
            string original = files.Hash(source);
            Assert.Throws<IOException>(() => new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            Assert.Equal(original, files.Hash(source));
            Assert.True(workspace.IsModified);
            await workspace.CommitCommand.ExecuteAsync(null);
            Assert.Empty(workspace.Error);
            Assert.False(workspace.IsModified);
            Assert.NotEqual(original, files.Hash(source));
            return;
        }
        using (var external = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            external.Position = 100;
            external.WriteByte(0x5a);
        }
        string changed = files.Hash(source);
        await workspace.CommitCommand.ExecuteAsync(null);
        Assert.NotEmpty(workspace.Error);
        Assert.Contains("may already have been replaced", workspace.Validation);
        Assert.DoesNotContain("source remains read-only", workspace.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(changed, files.Hash(source));
        Assert.True(workspace.IsModified);
    }

    [Fact]
    public async Task CommitFailureKeepsPendingStateAndDoesNotPromiseAnUnchangedSource()
    {
        using var service = new FailedReopenExplorer(failReopen: false);
        var dialogs = new EditingDialogs { Confirm = true };
        WorkspaceInfo info = AllowCommit(await service.OpenAsync("failed-commit.img"));
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.CommitCommand.ExecuteAsync(null);
        Assert.NotEmpty(workspace.Error);
        Assert.Contains("may already have been replaced", workspace.Validation);
        Assert.DoesNotContain("source remains read-only", workspace.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(workspace.IsModified);
        Assert.True(workspace.CanCommit);
    }

    [Fact]
    public async Task ReadOnlyVolumeCommitStaysDisabledWithItsCapabilityReason()
    {
        using var files = new EditingFiles();
        using var service = new ExplorerService(stagingDirectory: files.Path);
        WorkspaceInfo info = await service.OpenAsync(files.Golden("joliet.iso"));
        using var workspace = new WorkspaceViewModel(service, new EditingDialogs(), info, new LayoutSettings());
        await workspace.InitializeAsync();
        Assert.False(workspace.CanCommit);
        Assert.False(workspace.CommitCommand.CanExecute(null));
        Assert.Contains(info.Capabilities.CommitInPlace.Reason, workspace.CommitReason);
    }

    [Fact]
    public async Task MissingCommitProviderStaysDisabledEvenWhenMetadataClaimsCommitEligibility()
    {
        using var service = new ControlledSaveExplorer();
        WorkspaceInfo info = AllowCommit(await service.OpenAsync("editable.img"));
        using var workspace = new WorkspaceViewModel(service, new EditingDialogs(), info, new LayoutSettings());
        await workspace.InitializeAsync();
        Assert.True(workspace.IsModified);
        Assert.True(workspace.CanSaveAs);
        Assert.False(workspace.CanCommit);
        Assert.Contains("service is unavailable", workspace.CommitReason);
    }

    [Fact]
    public async Task FailedCommitReopenDisablesEditingButAllowsConfirmedCloseWithoutDeletingRecoveryArtifacts()
    {
        using var service = new FailedReopenExplorer();
        var dialogs = new EditingDialogs { Confirm = true };
        WorkspaceInfo info = AllowCommit(await service.OpenAsync("failed-reopen.img"));
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        Assert.True(workspace.CanCommit);
        await workspace.CommitCommand.ExecuteAsync(null);
        Assert.Contains("may already have been replaced", workspace.Validation);
        Assert.False(workspace.CanCommit);
        Assert.False(workspace.CanAdd);
        Assert.Contains("Close it and reopen", workspace.AddReason);
        dialogs.Confirm = false;
        Assert.False(await workspace.ConfirmCloseAsync());
        dialogs.Confirm = true;
        Assert.True(await workspace.ConfirmCloseAsync());
    }

    private static WorkspaceInfo AllowCommit(WorkspaceInfo info)
    {
        WorkspaceCapabilities caps = info.Capabilities with { CommitInPlace = new(true, "Qualified test commit.") };
        return info with { Capabilities = caps, Volumes = info.Volumes.Select(v => v with { Capabilities = caps }).ToArray() };
    }

    private sealed class FailedReopenExplorer(bool failReopen = true) : ControlledSaveExplorer, IImageCommitService
    {
        private bool unavailable;
        public override Task<EditingSnapshot> GetEditingSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => unavailable && failReopen ? Task.FromException<EditingSnapshot>(new IOException("Workspace reopen failed."))
                : base.GetEditingSnapshotAsync(workspaceId, cancellationToken);
        public Task<CommitResult> CommitAsync(Guid workspaceId, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            unavailable = true;
            return Task.FromException<CommitResult>(new IOException("Commit bookkeeping failed after source replacement."));
        }
    }

    [Fact]
    public async Task RecoveryInterfaceKeepsArtifactsByDefaultAndRetainsFailedCleanup()
    {
        var service = new ControlledRecovery();
        var dialogs = new EditingDialogs { Confirm = false };
        var model = new RecoveryViewModel(service, dialogs);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Single(model.Items);
        Assert.True(model.CanClean);
        await model.CleanCommand.ExecuteAsync(null);
        Assert.Equal(0, service.CleanCalls);
        Assert.Single(model.Items);
        dialogs.Confirm = true;
        service.Fail = true;
        await model.CleanCommand.ExecuteAsync(null);
        Assert.Single(model.Items);
        Assert.Contains("retained", model.Error);
        service.Fail = false;
        await model.CleanCommand.ExecuteAsync(null);
        Assert.Empty(model.Items);
        Assert.Contains("not overwritten", model.Status);
    }

    private sealed class ControlledRecovery : ICommitRecoveryService
    {
        public bool Fail { get; set; }
        public int CleanCalls { get; private set; }
        public Task<RecoveryScan> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoveryScan([new("test", "/source", "/journal", "Validated", "hash", "Inspect source", true)], []));
        public Task CleanAsync(RecoveryItem item, CancellationToken cancellationToken = default)
        {
            CleanCalls++;
            return Fail ? Task.FromException(new IOException("artifacts retained")) : Task.CompletedTask;
        }
    }
}
