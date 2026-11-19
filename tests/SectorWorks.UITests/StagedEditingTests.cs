using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

[Collection("Headless UI")]
public sealed class StagedEditingTests
{
    [Fact]
    public async Task WindowDropOpensImageAndWritableFileGridDropStagesAFile()
    {
        using var files = new EditingFiles();
        string image = files.Golden("fat12.img");
        string payload = files.Write("dropped.img", "host file bytes");
        string sourceHash = files.Hash(image);
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var explorer = new ExplorerService(stagingDirectory: files.Path);
            var window = new MainWindow();
            using var main = new MainWindowViewModel(explorer, new AvaloniaPlatformDialogs(window), new MemoryLayout());
            window.DataContext = main;
            window.Show();
            try
            {
                await DropAsync(window, image);
                await main.OpenPathsCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(10));
                WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
                Assert.Equal(image, workspace.Info.Metadata.Path);
                Assert.True(workspace.CanAdd);
                Assert.Empty(window.OwnedWindows);
                window.UpdateLayout();
                DataGrid grid = Assert.Single(window.GetVisualDescendants().OfType<DataGrid>(), view => view.Name == "FileGrid");
                var modified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                workspace.PropertyChanged += (_, _) => { if (workspace.IsModified) { modified.TrySetResult(); } };
                await DropAsync(grid, payload);
                await modified.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Single(main.Workspaces);
                EditingSnapshot snapshot = await explorer.GetEditingSnapshotAsync(workspace.Info.Id);
                Assert.Equal("/dropped.img", Assert.Single(snapshot.Changes).Path);
                Assert.Equal(sourceHash, files.Hash(image));
                Assert.Empty(workspace.Error);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }
            return true;

            async Task DropAsync(Control target, string path)
            {
                IStorageFile file = (await window.StorageProvider.TryGetFileFromPathAsync(path))!;
                Assert.NotNull(file);
                using var data = new DataTransfer();
                data.Add(DataTransferItem.CreateFile(file));
                target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, target, default, KeyModifiers.None)
                { DragEffects = DragDropEffects.Copy });
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Raw", ".img")]
    [InlineData("VHD", ".vhd")]
    [InlineData("VHDX", ".vhdx")]
    public async Task SaveDialogSuggestsDetectedContainerExtensionRatherThanRawConversion(string format, string extension)
    {
        using var service = new ControlledSaveExplorer { ContainerFormat = format };
        var dialogs = new EditingDialogs();
        WorkspaceInfo info = await service.OpenAsync("misleading.img");
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.SaveAsCommand.ExecuteAsync(null);
        Assert.Equal("misleading-modified" + extension, dialogs.LastSuggestedName);
        Assert.Contains("cancelled", workspace.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.SaveEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task RenderedCommandsPreserveLowercaseNamesAndCaseOnlyRenamesThroughSave()
    {
        using var files = new EditingFiles();
        string source = files.Golden("fat12.img");
        string original = files.Hash(source);
        string output = Path.Combine(files.Path, "case-preserved.img");
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ExplorerService(stagingDirectory: files.Path);
            var dialogs = new EditingDialogs { ImportPath = files.Write("input", "lowercase UI contents"), OutputPath = output, Name = "lower" };
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { source });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Click(Toolbar(window, "New Folder"));
                await workspace.NewDirectoryCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                await workspace.NavigateAsync("/lower");
                dialogs.Name = "test.txt";
                Click(Toolbar(window, "Add"));
                await workspace.AddCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                Assert.Equal("test.txt", Assert.Single(workspace.Entries).Name);
                workspace.SelectEntries(workspace.Entries);
                await workspace.PreviewCommand.ExecutionTask!;
                dialogs.Name = "TEST.txt";
                Click(Toolbar(window, "Rename"));
                await workspace.RenameCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                Assert.Equal("TEST.txt", Assert.Single(workspace.Entries).Name);
                Click(Toolbar(window, "Undo"));
                await workspace.UndoCommand.ExecutionTask!;
                Assert.Equal("test.txt", Assert.Single(workspace.Entries).Name);
                Click(Toolbar(window, "Redo"));
                await workspace.RedoCommand.ExecutionTask!;
                Assert.Equal("TEST.txt", Assert.Single(workspace.Entries).Name);
                Click(Toolbar(window, "Save As"));
                await workspace.SaveAsCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                WorkspaceInfo saved = await service.OpenAsync(output);
                Assert.True(saved.Capabilities.Modify.Available);
                Assert.Equal("/lower/TEST.txt", Assert.Single(await service.ReadDirectoryAsync(saved.Id, saved.Volumes[0].Id, "/LOWER")).Path);
                Assert.Equal("lowercase UI contents", (await service.PreviewAsync(saved.Id, saved.Volumes[0].Id, "/LOWER/test.TXT")).Text);
                Assert.Equal(original, files.Hash(source));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("raw", "FAT12")]
    [InlineData("ntfs-raw", "NTFS")]
    [InlineData("ntfs-raw-mbr", "NTFS")]
    public async Task PhysicalSmokeGeneratesQualifiedDisposableFilesystemsAndRefusesDirectoryReuse(string variant, string fileSystem)
    {
        using var files = new EditingFiles();
        string directory = Path.Combine(files.Path, "physical-smoke");
        string source = Phase2DesktopSmoke.CreateFixture(directory, variant);
        string hash = files.Hash(source);
        using var service = new ExplorerService(stagingDirectory: files.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.True(info.Capabilities.AddFiles.Available, info.Capabilities.AddFiles.Reason);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.Equal(fileSystem, volume.FileSystem);
        Assert.Equal(3, (await service.ReadDirectoryAsync(info.Id, volume.Id, "/")).Count);
        Assert.Equal(Phase2DesktopSmoke.OriginalText, (await service.PreviewAsync(info.Id, volume.Id, "/README.TXT")).Text);
        Assert.Equal(Phase2DesktopSmoke.UnchangedText, (await service.PreviewAsync(info.Id, volume.Id, "/KEEP.TXT")).Text);
        Assert.Throws<IOException>(() => Phase2DesktopSmoke.CreateFixture(directory, variant));
        Assert.Equal(hash, files.Hash(source));
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task RenderedCommandsStageReviewUndoRedoSaveValidateAndDiscardRealFat(string fixture)
    {
        using var files = new EditingFiles();
        string source = files.Golden(fixture);
        string original = files.Hash(source);
        string import = files.Write("import.txt", "added through UI\n");
        string replacement = files.Write("replacement.txt", "replacement through UI\n");
        string output = Path.Combine(files.Path, "saved.img");
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ExplorerService(stagingDirectory: files.Path);
            var dialogs = new EditingDialogs { ImportPath = import, OutputPath = output, Name = "NEW" };
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { source });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.True(workspace.Info.Capabilities.Modify.Available, workspace.Info.Capabilities.Modify.Reason);
                Assert.True(workspace.CanAdd, workspace.AddReason);
                Assert.False(workspace.CanSaveAs);
                // Phase 3: direct commit reuses this same qualified Apply/ValidateOutput pipeline
                // wrapped with locking/backup/journal, so it is qualified wherever Save As is.
                Assert.True(workspace.Info.Capabilities.CommitInPlace.Available, workspace.Info.Capabilities.CommitInPlace.Reason);
                Button newFolder = Toolbar(window, "New Folder");
                Click(newFolder);
                await workspace.NewDirectoryCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                Assert.Contains(workspace.Entries, e => e.Name == "NEW" && e.IsDirectory && e.IsChanged);
                Assert.True(workspace.IsModified);
                Assert.EndsWith(" *", workspace.Title);
                Assert.Contains("staged, not committed", workspace.ContextStatus);
                Assert.Contains("Source read-only", workspace.ContextStatus);
                Assert.Equal(8, workspace.ToolIndex);

                await workspace.NavigateAsync("/NEW");
                dialogs.Name = "ADDED.TXT";
                Click(Toolbar(window, "Add"));
                await workspace.AddCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                FileEntry entry = Assert.Single(workspace.Entries);
                Assert.True(entry.IsChanged);
                Assert.Equal("[staged]", PresentationConverters.ChangeMarker.Convert(true, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
                Dispatcher.UIThread.RunJobs();
                WorkspaceView view = window.GetVisualDescendants().OfType<WorkspaceView>().Single(v => v.IsVisible);
                view.FindControl<TabControl>("ToolTabs")!.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "[staged]");

                workspace.SelectEntries([entry]);
                await workspace.PreviewCommand.ExecutionTask!;
                dialogs.ImportPath = replacement;
                Click(Toolbar(window, "Replace"));
                await workspace.ReplaceCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                Assert.Contains("replacement through UI", (await service.PreviewAsync(workspace.Info.Id, workspace.Volume!.Id, entry.Path)).Text);
                workspace.SelectEntries(workspace.Entries);
                await workspace.PreviewCommand.ExecutionTask!;
                dialogs.Name = "RENAMED.TXT";
                Click(Toolbar(window, "Rename"));
                await workspace.RenameCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                Assert.Equal("RENAMED.TXT", Assert.Single(workspace.Entries).Name);

                Click(Toolbar(window, "Undo"));
                await workspace.UndoCommand.ExecutionTask!;
                Assert.Equal("ADDED.TXT", Assert.Single(workspace.Entries).Name);
                Click(Toolbar(window, "Redo"));
                await workspace.RedoCommand.ExecutionTask!;
                Assert.Equal("RENAMED.TXT", Assert.Single(workspace.Entries).Name);
                workspace.SelectEntries(workspace.Entries);
                await workspace.PreviewCommand.ExecutionTask!;
                Click(Toolbar(window, "Delete"));
                await workspace.DeleteCommand.ExecutionTask!;
                Assert.Empty(workspace.Entries);
                Assert.Equal(ImageChangeKind.Delete, workspace.PendingChanges.Last().Kind);
                Assert.Equal(5, workspace.PendingChanges.Count);
                Click(Toolbar(window, "Undo"));
                await workspace.UndoCommand.ExecutionTask!;
                Assert.Equal("RENAMED.TXT", Assert.Single(workspace.Entries).Name);

                Click(Toolbar(window, "Changes"));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(8, workspace.ToolIndex);
                Assert.Contains(view.GetVisualDescendants().OfType<ListBox>(), l => l.ItemsSource == workspace.PendingChanges);
                Assert.Contains("Source remains read-only", workspace.PendingSummary);
                Click(Toolbar(window, "Save As"));
                await workspace.SaveAsCommand.ExecutionTask!;
                Assert.Empty(workspace.Error);
                Assert.True(File.Exists(output));
                Assert.Contains("Validated new image:", workspace.SaveResult);
                Assert.Contains("SHA-256", workspace.Validation);
                Assert.True(workspace.IsModified);
                Assert.Equal(original, files.Hash(source));
                WorkspaceInfo reopened = await service.OpenAsync(output);
                Assert.Contains("replacement through UI", (await service.PreviewAsync(reopened.Id, reopened.Volumes[0].Id, "/NEW/RENAMED.TXT")).Text);
                await service.CloseAsync(reopened.Id);

                await workspace.DiscardCommand.ExecuteAsync(null);
                Assert.Empty(workspace.Error);
                Assert.False(workspace.IsModified);
                Assert.Equal("/", workspace.CurrentPath);
                Assert.DoesNotContain(workspace.Entries, e => e.Name == "NEW");
                Assert.False(workspace.CanUndo);
                Assert.False(workspace.CanRedo);
                Assert.Equal(original, files.Hash(source));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExternalFilesStageIntoViewedFolderWithoutReplacingConflicts()
    {
        using var files = new EditingFiles();
        string source = files.Golden("fat12.img");
        string original = files.Hash(source);
        string first = files.Write("FIRST.TXT", "first import");
        string second = files.Write("SECOND.TXT", "second import");
        string conflict = Path.Combine(Directory.CreateDirectory(Path.Combine(files.Path, "other")).FullName, "FIRST.TXT");
        File.WriteAllText(conflict, "do not overwrite");
        using var service = new ExplorerService(stagingDirectory: files.Path);
        var dialogs = new EditingDialogs { Name = "TARGET", OutputPath = Path.Combine(files.Path, "saved.img") };
        WorkspaceInfo info = await service.OpenAsync(source);
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.NewDirectoryCommand.ExecuteAsync(null);
        await workspace.NavigateAsync("/TARGET");

        var incoming = new TaskCompletionSource<string?[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task import = workspace.ImportFilesAsync(() => incoming.Task);
        await workspace.NavigateAsync("/");
        incoming.SetResult([first, conflict, null, second]);
        await import;

        Assert.Equal("/", workspace.CurrentPath);
        Assert.Contains("2 staged, 2 not added", workspace.Status);
        Assert.Contains("FIRST.TXT: An entry already exists", workspace.Error);
        Assert.Contains("Only local files can be imported", workspace.Error);
        await workspace.NavigateAsync("/TARGET");
        Assert.Equal(new[] { "/TARGET/FIRST.TXT", "/TARGET/SECOND.TXT" }, workspace.Entries.Select(entry => entry.Path));
        Assert.Equal("first import", (await service.PreviewAsync(info.Id, workspace.Volume!.Id, "/TARGET/FIRST.TXT")).Text);
        await workspace.UndoCommand.ExecuteAsync(null);
        Assert.Equal("FIRST.TXT", Assert.Single(workspace.Entries).Name);
        await workspace.RedoCommand.ExecuteAsync(null);
        await workspace.SaveAsCommand.ExecuteAsync(null);
        Assert.Empty(workspace.Error);
        WorkspaceInfo saved = await service.OpenAsync(dialogs.OutputPath);
        Assert.Equal("second import", (await service.PreviewAsync(saved.Id, saved.Volumes[0].Id, "/TARGET/SECOND.TXT")).Text);
        Assert.Equal("do not overwrite", File.ReadAllText(conflict));
        Assert.Equal(original, files.Hash(source));
    }

    [Fact]
    public async Task UnsupportedImageRendersDisabledCommandsWithRealCapabilityReasons()
    {
        using var files = new EditingFiles();
        string source = files.Golden("joliet.iso");
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ExplorerService(stagingDirectory: files.Path);
            using var main = new MainWindowViewModel(service, new EditingDialogs(), new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { source });
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
                foreach (string label in new[] { "Add", "Replace", "Delete", "New Folder", "Rename", "Save As", "Undo", "Redo" })
                {
                    Assert.False(Toolbar(window, label).IsEffectivelyEnabled, label);
                }
                Assert.Contains(workspace.SelectedCapabilities.AddFiles.Reason, workspace.AddReason);
                Assert.False(workspace.Info.Capabilities.Rebuild.Available);
                await workspace.ImportFilesAsync(() => throw new InvalidOperationException("Must not read clipboard for a read-only image."));
                Assert.Empty(workspace.PendingChanges);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MinimumWindowPendingReviewShowsThreeFullRowsAfterSaveAndKeyboardScrollsToLast()
    {
        using var files = new EditingFiles();
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ExplorerService(stagingDirectory: files.Path);
            var dialogs = new EditingDialogs { OutputPath = Path.Combine(files.Path, "saved.img") };
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { files.Golden("fat12.img") });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            for (int index = 0; index < 12; index++)
            {
                dialogs.Name = $"DIR{index:00}";
                await workspace.NewDirectoryCommand.ExecuteAsync(null);
                Assert.Empty(workspace.Error);
            }
            var window = new MainWindow { DataContext = main, Width = 1000, Height = 640 };
            window.Show();
            try
            {
                await workspace.SaveAsCommand.ExecuteAsync(null);
                Assert.Empty(workspace.Error);
                Assert.Contains("Validated new image:", workspace.SaveResult);
                workspace.ReviewChangesCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                WorkspaceView view = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
                Assert.Equal(220, view.FindControl<Grid>("CenterGrid")!.RowDefinitions[2].ActualHeight, 1);
                ListBox list = view.FindControl<ListBox>("PendingChangesList")!;
                ScrollViewer scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                ScrollContentPresenter viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
                Assert.True(list.Bounds.Height >= 72, $"Pending list has only {list.Bounds.Height} pixels.");
                ListBoxItem[] fullRows = list.GetVisualDescendants().OfType<ListBoxItem>()
                    .Where(row => IsFullyVisible(row, viewport)).ToArray();
                Assert.True(fullRows.Length >= 3,
                    $"Only {fullRows.Length} complete rows rendered in {viewport.Bounds.Height}px viewport after Save As.");
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBox>(), box => box.IsVisible && box.Text == workspace.SaveResult);
                list.SelectedIndex = 0;
                Assert.True(list.GetVisualDescendants().OfType<ListBoxItem>().First().Focus());
                window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                window.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(11, list.SelectedIndex);
                Assert.True(scroll.Offset.Y > 0);
                Assert.Contains(list.GetVisualDescendants().OfType<ListBoxItem>(),
                    row => row.DataContext is PendingImageChange { Sequence: 12 } && IsFullyVisible(row, viewport));
                Click(view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Validation…")));
                Assert.Equal(4, workspace.ToolIndex);
                Assert.Equal(workspace.SaveResult, view.FindControl<TextBox>("ValidationResult")!.Text);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static bool IsFullyVisible(Control row, Control viewport)
    {
        Point? top = row.TranslatePoint(new Point(0, 0), viewport);
        return row.IsVisible && top is Point position && position.Y >= -0.5
            && position.Y + row.Bounds.Height <= viewport.Bounds.Height + 0.5;
    }

    [Fact]
    public async Task InvalidNameAndCancelledDialogsDoNotStageOrPublish()
    {
        using var files = new EditingFiles();
        using var service = new ExplorerService(stagingDirectory: files.Path);
        WorkspaceInfo info = await service.OpenAsync(files.Golden("fat12.img"));
        var dialogs = new EditingDialogs { Name = "../outside", ImportPath = files.Write("input", "data") };
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.NewDirectoryCommand.ExecuteAsync(null);
        Assert.Contains("not a path", workspace.Error);
        Assert.False(workspace.IsModified);
        dialogs.Name = null;
        await workspace.AddCommand.ExecuteAsync(null);
        Assert.False(workspace.IsModified);
        dialogs.Name = "NEW";
        await workspace.NewDirectoryCommand.ExecuteAsync(null);
        Assert.True(workspace.IsModified);
        dialogs.OutputPath = null;
        await workspace.SaveAsCommand.ExecuteAsync(null);
        Assert.Contains("pending changes retained", workspace.Status);
        Assert.True(workspace.IsModified);
        dialogs.Confirm = false;
        await workspace.DiscardCommand.ExecuteAsync(null);
        Assert.True(workspace.IsModified);
        Assert.False(await workspace.ConfirmCloseAsync());
        dialogs.Confirm = true;
        Assert.True(await workspace.ConfirmCloseAsync());
    }

    [Fact]
    public async Task NamingPromptsSuggestSafeDirectoryAndPreserveImportedNamesForExplicitEditing()
    {
        using var files = new EditingFiles();
        using var service = new ExplorerService(stagingDirectory: files.Path);
        WorkspaceInfo info = await service.OpenAsync(files.Golden("fat12.img"));
        var dialogs = new EditingDialogs { ImportPath = files.Write("import.txt", "data") };
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.NewDirectoryCommand.ExecuteAsync(null);
        Assert.Equal("NEWDIR", dialogs.LastInitialName);
        await workspace.AddCommand.ExecuteAsync(null);
        Assert.Equal("import.txt", dialogs.LastInitialName);
        Assert.False(workspace.IsModified);
    }

    [Fact]
    public async Task TabAndWindowCloseRequireExplicitDiscardWhileDisposeRemainsCleanup()
    {
        using var files = new EditingFiles();
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            var dialogs = new EditingDialogs { Name = "NEW" };
            using var service = new ExplorerService(stagingDirectory: files.Path);
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { files.Golden("fat12.img") });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            await workspace.NewDirectoryCommand.ExecuteAsync(null);
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                dialogs.Confirm = false;
                await main.CloseCommand.ExecuteAsync(workspace);
                Assert.Single(main.Workspaces);
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.IsVisible);
                Assert.True(workspace.IsModified);
                dialogs.Confirm = true;
                await main.CloseCommand.ExecuteAsync(workspace);
                Assert.Empty(main.Workspaces);
                Assert.True(window.IsVisible);
            }
            finally { window.Close(); }

            await main.OpenPathsCommand.ExecuteAsync(new[] { files.Golden("fat16.img") });
            workspace = Assert.Single(main.Workspaces);
            await workspace.NewDirectoryCommand.ExecuteAsync(null);
            var secondWindow = new MainWindow { DataContext = main };
            secondWindow.Show();
            secondWindow.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(secondWindow.IsVisible);
            int confirmations = dialogs.Confirmations;
            main.Dispose();
            Assert.Equal(confirmations, dialogs.Confirmations);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeInputAndConfirmationAreRenderedOwnerModalDialogsWithCancelDefault()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var dialogs = new AvaloniaPlatformDialogs(owner);
                Task<string?> name = dialogs.RequestNameAsync("Rename", "ORIGINAL.TXT");
                Dispatcher.UIThread.RunJobs();
                Window input = Assert.Single(owner.OwnedWindows);
                Assert.Contains(input.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text?.Contains("NFC (composed)", StringComparison.Ordinal) == true
                    && text.Text.Contains("The image format determines allowed names", StringComparison.Ordinal));
                TextBox box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
                box.Text = "RENAMED.TXT";
                Click(input.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Stage")));
                Assert.Equal("RENAMED.TXT", await name);
                Task<string?> lowerName = dialogs.RequestNameAsync("Lowercase name", "lower.txt");
                Dispatcher.UIThread.RunJobs();
                Window lowerInput = Assert.Single(owner.OwnedWindows);
                Click(lowerInput.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Stage")));
                Assert.Equal("lower.txt", await lowerName);
                Task<bool> confirmation = dialogs.ConfirmAsync("Discard", "Discard staged changes?", "Discard changes");
                Dispatcher.UIThread.RunJobs();
                Window confirm = Assert.Single(owner.OwnedWindows);
                Button cancel = confirm.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel"));
                Assert.True(cancel.IsDefault);
                Click(cancel);
                Assert.False(await confirmation);
                using var cancellation = new CancellationTokenSource();
                Task<string?> cancelledName = dialogs.RequestNameAsync("Cancelled prompt", "NAME", cancellation.Token);
                cancellation.Cancel();
                Dispatcher.UIThread.RunJobs();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledName);
                Assert.Empty(owner.OwnedWindows);
            }
            finally { owner.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAsRefusesSourceOrExistingOutputAndKeepsStaging(bool sourceDestination)
    {
        using var files = new EditingFiles();
        using var service = new ExplorerService(stagingDirectory: files.Path);
        string source = files.Golden("fat12.img");
        string sourceHash = files.Hash(source);
        string existing = files.Write("existing.img", "existing file must survive");
        string existingHash = files.Hash(existing);
        WorkspaceInfo info = await service.OpenAsync(source);
        var dialogs = new EditingDialogs { Name = "NEW", OutputPath = sourceDestination ? source : existing };
        using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.NewDirectoryCommand.ExecuteAsync(null);
        await workspace.SaveAsCommand.ExecuteAsync(null);
        Assert.NotEmpty(workspace.Error);
        Assert.Contains("No successful Save As result", workspace.SaveResult);
        Assert.True(workspace.IsModified);
        Assert.Equal(sourceHash, files.Hash(source));
        Assert.Equal(existingHash, files.Hash(existing));
    }

    [Fact]
    public async Task CancellingSecondWindowClosePromptPreservesEveryWorkspaceAndStagedChange()
    {
        using var files = new EditingFiles();
        var dialogs = new EditingDialogs { Name = "NEW" };
        using var service = new ExplorerService(stagingDirectory: files.Path);
        using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
        await main.OpenPathsCommand.ExecuteAsync(new[] { files.Golden("fat12.img"), files.Golden("fat16.img") });
        foreach (WorkspaceViewModel workspace in main.Workspaces) { await workspace.NewDirectoryCommand.ExecuteAsync(null); }
        dialogs.ConfirmationResponses.Enqueue(true);
        dialogs.ConfirmationResponses.Enqueue(false);
        Assert.False(await main.ConfirmWindowCloseAsync());
        Assert.Equal(2, main.Workspaces.Count);
        Assert.All(main.Workspaces, workspace => Assert.True(workspace.IsModified));
    }

    [Fact]
    public async Task SaveProgressCancellationAndFailureNeverClaimValidationAndGateBusyCommands()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ControlledSaveExplorer();
            var dialogs = new EditingDialogs { OutputPath = "new-output.img" };
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { "source.img" });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                Click(Toolbar(window, "Save As"));
                await service.SaveEntered.Task;
                Dispatcher.UIThread.RunJobs();
                Assert.True(workspace.IsBusy);
                Assert.Contains("Copying image", workspace.SaveProgress);
                Assert.Contains("123", workspace.SaveProgress);
                Assert.False(workspace.CanAdd);
                Assert.False(workspace.CanSaveAs);
                Assert.False(Toolbar(window, "Undo").IsEffectivelyEnabled);
                Assert.Contains("current operation", workspace.AddReason);
                int confirmations = dialogs.Confirmations;
                Assert.False(await workspace.ConfirmCloseAsync());
                Assert.Equal(confirmations, dialogs.Confirmations);
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.IsVisible);
                Assert.Contains("operation is active", main.Status);
                main.CancelCommand.Execute(null);
                await workspace.SaveAsCommand.ExecutionTask!;
                Assert.False(workspace.IsBusy);
                Assert.True(workspace.IsModified);
                Assert.Contains("cancelled", workspace.SaveProgress, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Validated new image:", workspace.SaveResult);
                Assert.Contains("pending changes retained", workspace.SaveResult);
                Assert.True(workspace.CanSaveAs);
                service.FailSave = true;
                await workspace.SaveAsCommand.ExecuteAsync(null);
                Assert.Contains("injected validation failure", workspace.Error);
                Assert.Contains("No successful Save As result", workspace.SaveResult);
                Assert.DoesNotContain("Validated new image:", workspace.SaveResult);
                Assert.Equal(workspace.SaveResult, workspace.Validation);
                Assert.True(workspace.IsModified);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CancelledStageResponseCannotHideBackendPendingChangesFromWindowOrTabClose()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ControlledSaveExplorer { StartUnmodified = true, CancelStageAfterApply = true };
            var dialogs = new EditingDialogs { Name = "NEWDIR", Confirm = false };
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { "source.img" });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            await workspace.NewDirectoryCommand.ExecuteAsync(null);
            Assert.False(workspace.IsBusy);
            Assert.False(workspace.IsModified);
            Assert.True((await service.GetEditingSnapshotAsync(workspace.Info.Id)).IsModified);
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.IsVisible);
                Assert.True(workspace.IsModified);
                Assert.Single(main.Workspaces);
                Assert.True(dialogs.Confirmations > 0);
                await main.CloseCommand.ExecuteAsync(workspace);
                Assert.Single(main.Workspaces);
                Assert.Contains("pending changes retained", main.Status);
                dialogs.Confirm = true;
                await main.CloseCommand.ExecuteAsync(workspace);
                Assert.Empty(main.Workspaces);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FileGridKeysAndContextMenuUseSameCapabilityGatedEditingCommands()
    {
        using var files = new EditingFiles();
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var service = new ExplorerService(stagingDirectory: files.Path);
            var dialogs = new EditingDialogs { Name = "BEFORE" };
            using var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
            await main.OpenPathsCommand.ExecuteAsync(new[] { files.Golden("fat12.img") });
            WorkspaceViewModel workspace = Assert.Single(main.Workspaces);
            await workspace.NewDirectoryCommand.ExecuteAsync(null);
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                WorkspaceView view = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
                DataGrid grid = view.FindControl<DataGrid>("FileGrid")!;
                grid.SelectedItem = workspace.Entries.Single(e => e.Name == "BEFORE");
                dialogs.Name = "AFTER";
                Assert.True(grid.Focus());
                window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
                window.KeyRelease(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
                await workspace.RenameCommand.ExecutionTask!;
                Assert.Contains(workspace.Entries, e => e.Name == "AFTER");
                Dispatcher.UIThread.RunJobs();
                grid.SelectedItem = workspace.Entries.Single(e => e.Name == "AFTER");
                grid.ContextMenu!.Open(grid);
                Dispatcher.UIThread.RunJobs();
                var menu = grid.ContextMenu.Items.OfType<MenuItem>().ToArray();
                Assert.Same(workspace.AddCommand, menu.Single(m => Equals(m.Header, "Add File…")).Command);
                Assert.Same(workspace.RenameCommand, menu.Single(m => Equals(m.Header, "Rename…")).Command);
                Assert.Same(workspace.DeleteCommand, menu.Single(m => Equals(m.Header, "Delete…")).Command);
                grid.ContextMenu.Close();
                Assert.True(grid.Focus());
                window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
                window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
                await workspace.DeleteCommand.ExecutionTask!;
                Assert.DoesNotContain(workspace.Entries, e => e.Name == "AFTER");
                Assert.Equal(ImageChangeKind.Delete, workspace.PendingChanges.Last().Kind);
                window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
                window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
                await workspace.UndoCommand.ExecutionTask!;
                Assert.Contains(workspace.Entries, e => e.Name == "AFTER");
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static Button Toolbar(MainWindow window, string label)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Button>().First(b =>
            b.Parent is StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal }
            && (Equals(b.Content, label) || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label)));
    }

    private static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus());
        var window = (Window)TopLevel.GetTopLevel(button)!;
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }
}

internal sealed class EditingDialogs : IPlatformDialogs, IImageEditingDialogs
{
    public string? ImportPath { get; set; }
    public string? OutputPath { get; set; }
    public string? Name { get; set; }
    public string? LastInitialName { get; private set; }
    public string? LastSuggestedName { get; private set; }
    public bool Confirm { get; set; } = true;
    public int Confirmations { get; private set; }
    public Queue<bool> ConfirmationResponses { get; } = new();
    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task<string?> PickImportFileAsync(CancellationToken cancellationToken = default) => Task.FromResult(ImportPath);
    public Task<string?> PickNewImagePathAsync(string suggestedName, CancellationToken cancellationToken = default)
    {
        LastSuggestedName = suggestedName;
        return Task.FromResult(OutputPath);
    }
    public Task<string?> RequestNameAsync(string title, string initialName, CancellationToken cancellationToken = default)
    {
        LastInitialName = initialName;
        return Task.FromResult(Name);
    }
    public Task<bool> ConfirmAsync(string title, string message, string acceptLabel, CancellationToken cancellationToken = default)
    {
        Confirmations++;
        return Task.FromResult(ConfirmationResponses.TryDequeue(out bool response) ? response : Confirm);
    }
}

internal sealed class EditingFiles : IDisposable
{
    private readonly string root;
    public EditingFiles()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "SectorWorks.sln"))) { directory = directory.Parent; }
        root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        Path = System.IO.Path.Combine(root, "artifacts", "ui-editing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public string Golden(string name)
    {
        string destination = System.IO.Path.Combine(Path, name);
        using FileStream input = File.OpenRead(System.IO.Path.Combine(root, "fixtures", "golden", name + ".gz"));
        using var decompressed = new GZipStream(input, CompressionMode.Decompress);
        using (FileStream output = File.Create(destination)) { decompressed.CopyTo(output); }
        if (name is "fat12.img" or "fat16.img" or "fat32.img") { GeneratedFatFixtureMetadata.Complete(destination); }
        return destination;
    }
    public string Write(string name, string content)
    {
        string destination = System.IO.Path.Combine(Path, name);
        File.WriteAllText(destination, content);
        return destination;
    }
    public string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public void Dispose() => Directory.Delete(Path, true);
}

internal class ControlledSaveExplorer : IExplorerService, IImageEditingService
{
    private readonly FakeExplorer explorer = new();
    public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool FailSave { get; set; }
    public bool StartUnmodified { get; set; }
    public bool CancelStageAfterApply { get; set; }
    public string ContainerFormat { get; init; } = "Raw";
    private bool stageApplied;
    private static EditingSnapshot Snapshot => new([new(1, ImageChangeKind.CreateDirectory, "fs", "/NEW", null, 0)], true, false, 100000);
    public async Task<WorkspaceInfo> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        WorkspaceInfo info = await explorer.OpenAsync(path, cancellationToken);
        Capability allowed = new(true, "Qualified test editing stack.");
        WorkspaceCapabilities caps = info.Capabilities with
        {
            Modify = allowed,
            AddFiles = allowed,
            ReplaceFiles = allowed,
            DeleteFiles = allowed,
            RenameFiles = allowed,
            CreateDirectories = allowed,
            SaveAs = allowed
        };
        return info with
        {
            Metadata = info.Metadata with { Format = ContainerFormat },
            Capabilities = caps,
            Volumes = info.Volumes.Select(v => v with { Capabilities = caps }).ToArray()
        };
    }
    public Task CloseAsync(Guid workspaceId) => explorer.CloseAsync(workspaceId);
    public Task<IReadOnlyList<FileEntry>> ReadDirectoryAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default) =>
        explorer.ReadDirectoryAsync(workspaceId, volumeId, path, cancellationToken);
    public Task<PreviewResult> PreviewAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default) =>
        explorer.PreviewAsync(workspaceId, volumeId, path, cancellationToken);
    public Task<PreviewResult> ReadHexAsync(Guid workspaceId, string? volumeId, string? path, long offset, CancellationToken cancellationToken = default) =>
        explorer.ReadHexAsync(workspaceId, volumeId, path, offset, cancellationToken);
    public Task<IReadOnlyList<SearchHit>> SearchAsync(Guid workspaceId, string query, CancellationToken cancellationToken = default) =>
        explorer.SearchAsync(workspaceId, query, cancellationToken);
    public Task<ExtractionResult> ExtractAsync(Guid workspaceId, string volumeId, IReadOnlyList<FileEntry> entries, string destination, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        explorer.ExtractAsync(workspaceId, volumeId, entries, destination, progress, cancellationToken);
    public virtual Task<EditingSnapshot> GetEditingSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(StartUnmodified && !stageApplied ? new EditingSnapshot([], false, false, 100000) : Snapshot);
    public Task<EditingSnapshot> StageAsync(Guid workspaceId, ImageChangeRequest request, CancellationToken cancellationToken = default)
    {
        stageApplied = true;
        return CancelStageAfterApply ? Task.FromException<EditingSnapshot>(new OperationCanceledException(cancellationToken)) : Task.FromResult(Snapshot);
    }
    public Task<EditingSnapshot> UndoAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
    public Task<EditingSnapshot> RedoAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
    public Task<EditingSnapshot> DiscardAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new EditingSnapshot([], false, false, 100000));
    public async Task<SaveImageResult> SaveAsAsync(Guid workspaceId, string destination, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new("Copying image", destination, 123, 0));
        SaveEntered.TrySetResult();
        if (FailSave) { throw new IOException("injected validation failure"); }
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("A cancelled save cannot produce a result.");
    }
    public void Dispose() => explorer.Dispose();
}
