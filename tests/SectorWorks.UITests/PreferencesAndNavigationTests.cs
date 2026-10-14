using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

public sealed class PreferencesAndNavigationTests
{
    [Fact]
    public async Task DefaultSortingIsFoldersFirstOrdinalIgnoreCaseWithDeterministicTies()
    {
        using var fake = SortingExplorer();
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("sort.img"), new LayoutSettings());
        await workspace.InitializeAsync();
        Assert.Equal(new[] { "alpha-dir", "Zulu-dir", "Alpha.txt", "alpha.txt", "zeta.bin" }, workspace.Entries.Select(e => e.Name));
        workspace.SortBy(BrowserSortField.Name);
        Assert.Equal(new[] { "Zulu-dir", "alpha-dir", "zeta.bin", "alpha.txt", "Alpha.txt" }, workspace.Entries.Select(e => e.Name));
    }

    [Theory]
    [InlineData(BrowserSortField.Name, "Alpha.txt", "zeta.bin")]
    [InlineData(BrowserSortField.Size, "zeta.bin", "alpha.txt")]
    [InlineData(BrowserSortField.Type, "zeta.bin", "alpha.txt")]
    [InlineData(BrowserSortField.Modified, "zeta.bin", "alpha.txt")]
    public async Task CommonSortingOptionsRespectDirectionAndOptionalGrouping(BrowserSortField field, string first, string last)
    {
        using var fake = SortingExplorer();
        fake.Directories["/"] = fake.Directories["/"].Where(e => !e.IsDirectory).ToArray();
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("sort.img"), new LayoutSettings());
        await workspace.InitializeAsync();
        workspace.ApplyPreferences(new ApplicationPreferences(field, BrowserSortDirection.Ascending, false));
        Assert.Equal(first, workspace.Entries[0].Name);
        Assert.Equal(last, workspace.Entries[^1].Name);
        workspace.ApplyPreferences(new ApplicationPreferences(field, BrowserSortDirection.Descending, false));
        Assert.Equal(last, workspace.Entries[0].Name);
    }

    [Fact]
    public async Task SaveReloadAppliesAcrossOpenTabsWithoutLosingSelectionAndCancelIsIsolated()
    {
        using var fake = SortingExplorer();
        var store = new MemoryPreferences();
        using var main = new MainWindowViewModel(fake, new FakeDialogs(), new MemoryLayout(), store);
        await main.OpenPathsCommand.ExecuteAsync(new[] { "one.img", "two.img" });
        foreach (WorkspaceViewModel workspace in main.Workspaces)
        {
            workspace.SelectEntries([workspace.Entries[2]]);
        }
        var cancelled = new PreferencesViewModel(main.Preferences, main.SavePreferencesAsync) { FoldersFirst = false };
        cancelled.CancelCommand.Execute(null);
        Assert.Equal(0, store.Saves);
        Assert.True(main.Preferences.FoldersFirst);
        var dialog = new PreferencesViewModel(main.Preferences, main.SavePreferencesAsync)
        {
            SortField = BrowserSortField.Size,
            SortDirection = BrowserSortDirection.Descending,
            FoldersFirst = false
        };
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.Equal("", dialog.Error);
        Assert.Equal(1, store.Saves);
        Assert.All(main.Workspaces, w =>
        {
            Assert.Equal(store.Settings, w.BrowserPreferences);
            Assert.Single(w.Selection);
            Assert.Equal("Alpha.txt", w.Selection[0].Name);
        });
        using var reloaded = new MainWindowViewModel(new FakeExplorer(), new FakeDialogs(), new MemoryLayout(), store);
        Assert.Equal(store.Settings, reloaded.Preferences);
        await main.OpenPathsCommand.ExecuteAsync(new[] { "new.img" });
        Assert.Equal(store.Settings, main.ActiveWorkspace!.BrowserPreferences);
    }

    [Fact]
    public async Task LoadAndSaveErrorsAreVisibleAndFailedSaveDoesNotApplyOrClose()
    {
        using var fake = SortingExplorer();
        var store = new MemoryPreferences { FailLoad = true, FailSave = true };
        using var main = new MainWindowViewModel(fake, new FakeDialogs(), new MemoryLayout(), store);
        Assert.Contains("Could not load preferences", main.PreferencesWarning);
        await main.OpenPathsCommand.ExecuteAsync(new[] { "one.img" });
        Assert.Contains("Could not load preferences", main.PreferencesWarning);
        var dialog = new PreferencesViewModel(main.Preferences, main.SavePreferencesAsync) { FoldersFirst = false };
        bool closed = false;
        dialog.CloseRequested += (_, _) => closed = true;
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.False(closed);
        Assert.Contains("not saved or applied", dialog.Error);
        Assert.Contains("Could not save preferences", main.PreferencesWarning);
        Assert.True(main.Preferences.FoldersFirst);
        Assert.True(main.ActiveWorkspace!.BrowserPreferences.FoldersFirst);
        store.FailSave = false;
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.True(closed);
        Assert.False(main.Preferences.FoldersFirst);
        Assert.Equal("", main.PreferencesWarning);
    }

    [Fact]
    public async Task DeepNavigationLoadsAncestryOnceAndFailedOrCancelledResultsKeepHighlight()
    {
        using var fake = new FakeExplorer();
        fake.Directories["/docs"] = [new FileEntry("/docs/nested", "nested", true, 0, null, "")];
        fake.Directories["/docs/nested"] = [];
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("tree.img"), new LayoutSettings());
        await workspace.InitializeAsync();
        await workspace.NavigateAsync("/docs/nested");
        Assert.Equal("/docs/nested", workspace.SelectedNode!.Path);
        Assert.True(workspace.SelectedNode.Parent!.IsExpanded);
        Assert.True(workspace.SelectedNode.Parent.Parent!.IsExpanded);
        int reads = fake.DirectoryReadCount;
        await workspace.SelectedNode.Parent.EnsureLoadedAsync();
        Assert.Equal(reads, fake.DirectoryReadCount);
        StructureNode selected = workspace.SelectedNode;
        fake.FailDirectories = true;
        await workspace.NavigateAsync("/missing");
        Assert.Same(selected, workspace.SelectedNode);
        Assert.Equal("/docs/nested", workspace.CurrentPath);
        fake.FailDirectories = false;
        fake.SlowDirectory = new TaskCompletionSource<IReadOnlyList<FileEntry>>();
        Task slow = workspace.NavigateAsync("/slow");
        workspace.Cancel();
        fake.SlowDirectory.SetResult([]);
        await slow;
        Assert.Same(selected, workspace.SelectedNode);
        Assert.Equal("/docs/nested", workspace.CurrentPath);
    }

    [Fact]
    public async Task LazyExpansionDeduplicatesConcurrentRequests()
    {
        using var fake = new FakeExplorer { SlowDirectory = new TaskCompletionSource<IReadOnlyList<FileEntry>>() };
        fake.Directories["/"] = [new FileEntry("/slow", "slow", true, 0, null, "")];
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("lazy.img"), new LayoutSettings());
        await workspace.InitializeAsync();
        StructureNode node = workspace.Roots[0].Children.Single(n => n.VolumeId is not null).Children.Single();
        int reads = fake.DirectoryReadCount;
        Task first = node.EnsureLoadedAsync();
        Task second = node.EnsureLoadedAsync();
        node.IsExpanded = true;
        Assert.Equal(reads + 1, fake.DirectoryReadCount);
        fake.SlowDirectory.SetResult([]);
        await Task.WhenAll(first, second);
        Assert.Empty(node.Children);
    }

    internal static async Task VerifyActualViewsAsync(MainWindow window, MainWindowViewModel main, FakeExplorer fake)
    {
        ApplicationPreferences previous = main.Preferences;
        MenuItem fileMenu = window.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().First();
        MenuItem preferencesMenu = fileMenu.Items.OfType<MenuItem>()
            .Single(item => item.Header?.ToString()?.Contains("Preferences", StringComparison.Ordinal) == true);
        preferencesMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        PreferencesWindow opened = window.OwnedWindows.OfType<PreferencesWindow>().Single(w => w.IsVisible);
        var cancelledPreferences = Assert.IsType<PreferencesViewModel>(opened.DataContext);
        cancelledPreferences.FoldersFirst = !previous.FoldersFirst;
        cancelledPreferences.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(opened.IsVisible);
        Assert.Equal(previous, main.Preferences);
        var failed = new PreferencesViewModel(previous, _ => Task.FromException(new IOException("settings write denied")));
        var failedWindow = new PreferencesWindow(failed);
        failedWindow.Show(window);
        await failed.SaveCommand.ExecuteAsync(null);
        failedWindow.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(failedWindow.IsVisible);
        Assert.True(failedWindow.FindControl<TextBox>("PreferencesError")!.IsEffectivelyVisible);
        Assert.Contains("settings write denied", failedWindow.FindControl<TextBox>("PreferencesError")!.Text);
        failed.CancelCommand.Execute(null);
        WorkspaceViewModel workspace = main.ActiveWorkspace!;
        WorkspaceView view = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
        TreeView tree = view.FindControl<TreeView>("StructureTree")!;
        fake.Directories["/docs"] =
        [
            new FileEntry("/docs/nested", "nested", true, 0, null, ""), FakeExplorer.Readme
        ];
        fake.Directories["/docs/nested"] = [];
        await workspace.NavigateAsync("/docs");
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(workspace.SelectedNode, tree.SelectedItem);
        Assert.Equal("/docs", ((StructureNode)tree.SelectedItem!).Path);
        Assert.True(tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Single(item => item.DataContext == workspace.SelectedNode).IsSelected);
        await workspace.NavigateAsync("/docs/nested");
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/docs/nested", ((StructureNode)tree.SelectedItem!).Path);
        Assert.True(tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Single(item => item.DataContext == workspace.SelectedNode).IsSelected);
        await workspace.UpCommand.ExecuteAsync(null);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Button rootCrumb = view.FindControl<ItemsControl>("BreadcrumbBar")!.GetVisualDescendants()
            .OfType<Button>().Single(b => b.DataContext is Breadcrumb { Path: "/" });
        rootCrumb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/", ((StructureNode)tree.SelectedItem!).Path);
        await workspace.NavigateHitCommand.ExecuteAsync(new SearchHit("fs", FakeExplorer.Readme));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/docs", ((StructureNode)tree.SelectedItem!).Path);
        fake.FailDirectories = true;
        await workspace.NavigateAsync("/missing");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/docs", ((StructureNode)tree.SelectedItem!).Path);
        int reads = fake.DirectoryReadCount;
        tree.SelectedItem = workspace.Roots[0].Children.Single(n => n.VolumeId is not null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(reads + 1, fake.DirectoryReadCount);
        Assert.Equal("/docs", ((StructureNode)tree.SelectedItem!).Path);
        fake.FailDirectories = false;
        fake.SlowDirectory = new TaskCompletionSource<IReadOnlyList<FileEntry>>();
        Task slow = workspace.NavigateAsync("/slow");
        await workspace.NavigateAsync("/");
        fake.SlowDirectory.SetResult([]);
        await slow;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/", ((StructureNode)tree.SelectedItem!).Path);
        await workspace.NavigateAsync("/docs");
        fake.SlowDirectory = new TaskCompletionSource<IReadOnlyList<FileEntry>>();
        Task cancelled = workspace.NavigateAsync("/slow");
        workspace.Cancel();
        fake.SlowDirectory.SetResult([]);
        await cancelled;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/docs", ((StructureNode)tree.SelectedItem!).Path);
        DataGrid grid = view.FindControl<DataGrid>("FileGrid")!;
        grid.SelectedItem = workspace.Entries.First(entry => !entry.IsDirectory);
        var preferences = new PreferencesViewModel(main.Preferences, main.SavePreferencesAsync)
        {
            SortField = BrowserSortField.Modified,
            FoldersFirst = false
        };
        var dialog = new PreferencesWindow(preferences);
        dialog.Show(window);
        Assert.NotNull(dialog.FindControl<ComboBox>("SortFieldBox"));
        await preferences.SaveCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.IsVisible);
        Assert.Single(workspace.Selection);
        Assert.Single(grid.SelectedItems);
        Assert.Equal("Modified ↑", grid.Columns[3].Header);
        Assert.Equal("/docs", ((StructureNode)tree.SelectedItem!).Path);
        await workspace.UpCommand.ExecuteAsync(null);
    }

    private static FakeExplorer SortingExplorer()
    {
        var fake = new FakeExplorer();
        fake.Directories["/"] =
        [
            new("/zeta.bin", "zeta.bin", false, 1, DateTime.UnixEpoch, ""),
            new("/Zulu-dir", "Zulu-dir", true, 0, null, ""),
            new("/alpha.txt", "alpha.txt", false, 30, DateTime.UnixEpoch.AddDays(2), ""),
            new("/Alpha.txt", "Alpha.txt", false, 20, DateTime.UnixEpoch.AddDays(1), ""),
            new("/alpha-dir", "alpha-dir", true, 0, null, "")
        ];
        return fake;
    }
}

internal sealed class MemoryPreferences : IPreferencesStore
{
    public ApplicationPreferences Settings { get; private set; } = new();
    public int Saves { get; private set; }
    public bool FailLoad { get; set; }
    public bool FailSave { get; set; }
    public ApplicationPreferences Load() => FailLoad ? throw new InvalidDataException("invalid preferences") : Settings;
    public void Save(ApplicationPreferences preferences)
    {
        if (FailSave) { throw new IOException("preferences write refused"); }
        Settings = preferences;
        Saves++;
    }
}
