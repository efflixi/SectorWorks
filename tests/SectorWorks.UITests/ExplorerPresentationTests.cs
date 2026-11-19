using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

[Collection("Headless UI")]
public sealed class ExplorerPresentationTests
{
    [Fact]
    public async Task ReadOnlyOpenShowsDistinctWrappingIssuesAndKeepsTheImageOpen()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var explorer = new FakeExplorer();
            WorkspaceInfo info = explorer.Create("read-only.img");
            Capability first = new(false, "The first volume's FAT copies disagree.");
            Capability second = new(false, "The other volume uses unsupported metadata. " + new string('X', 500));
            WorkspaceCapabilities capabilities = info.Capabilities with { Modify = first, SaveAs = first };
            explorer.SlowOpen = new();
            explorer.SlowOpen.SetResult(info with
            {
                Capabilities = capabilities,
                Volumes =
                [
                    info.Volumes[0] with { Capabilities = capabilities },
                    info.Volumes[0] with { Id = "other", Capabilities = capabilities with { Modify = second } },
                ],
            });
            var window = new MainWindow();
            using var main = new MainWindowViewModel(explorer, new AvaloniaPlatformDialogs(window), new MemoryLayout());
            window.DataContext = main;
            window.Show();
            try
            {
                Task opening = main.OpenPathsCommand.ExecuteAsync(new[] { "read-only.img" });
                Window notice = Assert.Single(window.OwnedWindows);
                notice.UpdateLayout();
                Assert.Contains(notice.GetVisualDescendants().OfType<TextBlock>(),
                    element => element.Text == "This image can't be edited or rebuilt because of the following issue(s):");
                TextBox explanation = Assert.Single(notice.GetVisualDescendants().OfType<TextBox>());
                Assert.True(explanation.IsReadOnly);
                Assert.Equal(TextWrapping.Wrap, explanation.TextWrapping);
                Assert.Equal("• " + first.Reason + "\n• " + second.Reason, explanation.Text);
                Assert.InRange(explanation.Bounds.Height, 1, 180);
                Assert.InRange(notice.Bounds.Height, 1, 340);
                Assert.Single(main.Workspaces);
                notice.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "OK"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await opening.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Empty(window.OwnedWindows);
                Assert.True(main.ActiveWorkspace!.Info.Capabilities.Browse.Available);
                Assert.Empty(main.Error);
                Assert.False(main.IsOpening);
            }
            finally
            {
                foreach (Window notice in window.OwnedWindows.ToArray()) { notice.Close(); }
                window.DataContext = null;
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void PhysicalSmokePrefersReadmeButRetainsGoldenSafeFileFallback()
    {
        var binary = new FileEntry("/COMMAND.COM", "COMMAND.COM", false, 200, null, "");
        var readme = new FileEntry("/README.TXT", "README.TXT", false, 50, null, "");
        Assert.Same(readme, DesktopSmoke.SelectPreviewFile([binary, readme]));
        FileEntry sentinel = FakeExplorer.Readme with { Name = "SENTINEL.TXT", Path = "/SENTINEL.TXT" };
        Assert.Same(sentinel, DesktopSmoke.SelectPreviewFile([sentinel, binary]));
        Assert.Same(binary, DesktopSmoke.SelectPreviewFile([readme with { IsLink = true }, binary]));
        Assert.Throws<InvalidOperationException>(() => DesktopSmoke.SelectPreviewFile([readme with { IsDirectory = true }]));
    }

    [Fact]
    public async Task IsoOpticalReservationIsInformationalAndDoesNotEnumerateOrEnableTracks()
    {
        using var fake = new FakeExplorer();
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("optical.iso"), new LayoutSettings());
        StructureNode reserved = workspace.Roots[0].Children.Single(n => n.Title.Contains("Optical sessions / tracks", StringComparison.Ordinal));
        Assert.Contains("sessions / tracks", reserved.Title);
        Assert.Contains("unavailable", reserved.Title);
        Assert.Null(reserved.VolumeId);
        Assert.Null(reserved.Path);
        Assert.Empty(reserved.Children);
        Assert.False(workspace.Info.Capabilities.PlayAudioTrack.Available);
        Assert.True(workspace.Info.Capabilities.Browse.Available);
        Assert.False(workspace.CanExtract);
        await reserved.LoadCommand.ExecuteAsync(null);
        await workspace.SelectNodeCommand.ExecuteAsync(reserved);
        Assert.Equal(0, fake.DirectoryReadCount);
        Assert.Empty(workspace.Entries);
        Assert.Contains("No sessions or tracks have been enumerated", workspace.Properties);
        Assert.Contains("Open a CUE sheet to browse its track layout or play supported CD audio.", workspace.Properties);
        Assert.Equal(2, workspace.ToolIndex);
        using var disk = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("disk.img"), new LayoutSettings());
        Assert.DoesNotContain(disk.Roots[0].Children, n => n.Title.Contains("Optical sessions / tracks", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified, "(timezone unknown)")]
    [InlineData(DateTimeKind.Utc, "UTC")]
    [InlineData(DateTimeKind.Local, "(local)")]
    public void ModifiedTimestampsPreserveSourceTimezoneKnowledge(DateTimeKind kind, string suffix)
    {
        var date = new DateTime(2026, 10, 13, 12, 34, 56, kind);
        string rendered = PresentationConverters.FormatModified(date);
        Assert.StartsWith("2026-10-13 12:34:56", rendered);
        Assert.EndsWith(suffix, rendered);
        if (kind != DateTimeKind.Utc) { Assert.DoesNotContain("UTC", rendered); }
        using var fake = new FakeExplorer();
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("dates.img"), new LayoutSettings());
        workspace.SelectEntries([FakeExplorer.Readme with { ModifiedUtc = date }]);
        Assert.Contains("Modified: " + rendered, workspace.Properties);
        Assert.DoesNotContain("Modified UTC:", workspace.Properties);
    }

    [Fact]
    public async Task InvalidLayoutUsesExplicitPersistentWarningEvenAfterCommandLineOpen()
    {
        using var fake = new FakeExplorer();
        using var main = new MainWindowViewModel(fake, new FakeDialogs(), new InvalidLayout());
        Assert.Equal(new LayoutSettings(), main.Layout);
        Assert.Contains("default pane dimensions", main.LayoutWarning);
        await main.OpenPathsCommand.ExecuteAsync(new[] { "startup.img" });
        Assert.Contains("invalid layout", main.LayoutWarning);
        main.SaveLayout(new LayoutSettings());
        Assert.Contains("Could not save layout", main.LayoutWarning);
    }

    [Fact]
    public void SelectedVolumeCapabilitiesOverrideAggregateWorkspaceAvailability()
    {
        using var fake = new FakeExplorer();
        WorkspaceInfo info = fake.Create("restricted-volume.img");
        var restricted = new WorkspaceCapabilities(new(true, "browse"), new(false, "volume extraction disabled"),
            new(false, "volume preview disabled"), new(false, "no full validator"));
        info = info with { Volumes = [info.Volumes[0] with { Capabilities = restricted }] };
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), info, new LayoutSettings());
        workspace.SelectEntries([FakeExplorer.Readme]);
        Assert.False(workspace.CanExtract);
        Assert.False(workspace.CanPreview);
        Assert.Contains("volume preview disabled", workspace.PreviewText);
        Assert.Contains("volume extraction disabled", workspace.Inspector);
        Assert.Contains("Add files: No", workspace.Inspector);
        Assert.Contains("Save as new image: No", workspace.Inspector);
    }

    [Fact]
    public async Task OpenBrowseSelectPreviewSearchExtractCloseAndPreserveTabs()
    {
        using var fake = new FakeExplorer();
        var dialogs = new FakeDialogs();
        var store = new MemoryLayout();
        using var main = new MainWindowViewModel(fake, dialogs, store);
        await main.OpenPathsCommand.ExecuteAsync(new[] { "first.img", "broken.img", "second.iso" });
        Assert.Equal(2, main.Workspaces.Count);
        Assert.Contains("broken fixture", main.Error);
        WorkspaceViewModel first = main.Workspaces[0];
        WorkspaceViewModel second = main.Workspaces[1];
        Assert.Same(second, main.ActiveWorkspace);
        await first.NavigateCommand.ExecuteAsync("/docs");
        first.SelectEntries([FakeExplorer.Readme]);
        await first.PreviewCommand.ExecutionTask!;
        Assert.Contains("bounded text", first.PreviewText);
        Assert.True(first.CanExtract);
        Assert.Contains("Modify: No — This image stack is not qualified", first.Inspector);
        await first.HexCommand.ExecuteAsync(null);
        Assert.Equal(1, first.ToolIndex);
        Assert.False(first.NextHexCommand.CanExecute(null));
        first.SelectEntries([FakeExplorer.Readme with { Length = 9000 }]);
        Assert.True(first.NextHexCommand.CanExecute(null));
        await first.NextHexCommand.ExecuteAsync(null);
        Assert.Equal(4096, fake.LastHexOffset);
        first.Query = "read";
        await first.SearchCommand.ExecuteAsync(null);
        Assert.Single(first.SearchResults);
        await first.NavigateHitCommand.ExecuteAsync(first.SearchResults[0]);
        Assert.Equal("/docs", first.CurrentPath);
        await first.ExtractCommand.ExecuteAsync(null);
        Assert.Equal("chosen-destination", fake.ExtractedTo);
        Assert.Single(fake.ExtractedEntries);
        main.ActiveWorkspace = second;
        main.ActiveWorkspace = first;
        Assert.Equal("/docs", first.CurrentPath);
        Assert.Single(first.Selection);
        await main.CloseCommand.ExecuteAsync(first);
        Assert.Single(main.Workspaces);
        Assert.Contains(first.Info.Id, fake.Closed);
        Assert.Same(second, main.ActiveWorkspace);
    }

    [Fact]
    public async Task NavigationRejectsLateResultsAndCloseCancelsOutstandingRequests()
    {
        using var fake = new FakeExplorer();
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("race.img"), new LayoutSettings());
        var slow = new TaskCompletionSource<IReadOnlyList<FileEntry>>();
        fake.SlowDirectory = slow;
        Task old = workspace.NavigateAsync("/slow");
        await workspace.NavigateAsync("/docs");
        slow.SetResult([new FileEntry("/stale", "stale", false, 1, null, "")]);
        await old;
        Assert.Equal("/docs", workspace.CurrentPath);
        Assert.StartsWith("Ready", workspace.Status);
        Assert.DoesNotContain(workspace.Entries, e => e.Name == "stale");
        var pending = new TaskCompletionSource<IReadOnlyList<FileEntry>>();
        fake.SlowDirectory = pending;
        Task closing = workspace.NavigateAsync("/slow");
        workspace.Dispose();
        pending.SetResult([FakeExplorer.Readme]);
        await closing;
        Assert.Equal("/docs", workspace.CurrentPath);
    }

    [Fact]
    public async Task PreviewRejectsLateSelectionAndErrorsAreExplicit()
    {
        using var fake = new FakeExplorer();
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("preview.img"), new LayoutSettings());
        var pending = new TaskCompletionSource<PreviewResult>();
        fake.SlowPreview = pending;
        workspace.SelectEntries([FakeExplorer.Readme]);
        Task preview = workspace.PreviewCommand.ExecutionTask!;
        workspace.SelectEntries([]);
        pending.SetResult(new PreviewResult("stale", false, "late"));
        await preview;
        Assert.DoesNotContain("stale", workspace.PreviewText);
        fake.FailDirectories = true;
        await workspace.NavigateAsync("/");
        Assert.Contains("directory failure", workspace.Error);
        Assert.False(workspace.IsBusy);
    }

    [Fact]
    public async Task ExtractionFailureProgressAndCancellationAreVisible()
    {
        using var fake = new FakeExplorer { FailExtraction = true };
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("extract.img"), new LayoutSettings());
        workspace.SelectEntries([FakeExplorer.Readme]);
        await workspace.ExtractCommand.ExecuteAsync(null);
        Assert.Contains("duplicate", workspace.Error);
        Assert.Contains("1 failures", workspace.Status);
        fake.SlowSearch = new TaskCompletionSource<IReadOnlyList<SearchHit>>();
        workspace.Query = "anything";
        Task search = workspace.SearchCommand.ExecuteAsync(null);
        workspace.Cancel();
        fake.SlowSearch.SetResult([]);
        await search;
        Assert.Contains("Cancelled", workspace.Status);
        Assert.False(workspace.IsBusy);
    }

    [Fact]
    public async Task CancelOpeningClosesLateWorkspaceWithoutPublishingTab()
    {
        using var fake = new FakeExplorer { SlowOpen = new TaskCompletionSource<WorkspaceInfo>() };
        using var main = new MainWindowViewModel(fake, new FakeDialogs(), new MemoryLayout());
        Task opening = main.OpenPathsCommand.ExecuteAsync(new[] { "slow.img" });
        Assert.True(main.IsOpening);
        main.CancelCommand.Execute(null);
        WorkspaceInfo info = fake.Create("slow.img");
        fake.SlowOpen.SetResult(info);
        await opening;
        Assert.Empty(main.Workspaces);
        Assert.Contains(info.Id, fake.Closed);
        Assert.False(main.IsOpening);
    }

    [Fact]
    public async Task FailedVolumeNavigationKeepsExistingRowsAssociatedWithOriginalVolume()
    {
        using var fake = new FakeExplorer();
        WorkspaceInfo info = fake.Create("two-volumes.img");
        info = info with { Volumes = [info.Volumes[0], new VolumeInfo("second", "NTFS", "OTHER", null, new Dictionary<string, string>())] };
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), info, new LayoutSettings());
        await workspace.InitializeAsync();
        IReadOnlyList<FileEntry> originalEntries = workspace.Entries;
        fake.FailDirectories = true;
        StructureNode second = workspace.Roots[0].Children.Single(n => n.VolumeId == "second");
        await workspace.SelectNodeCommand.ExecuteAsync(second);
        Assert.Equal("fs", workspace.Volume!.Id);
        Assert.Same(originalEntries, workspace.Entries);
        Assert.Contains("directory failure", workspace.Error);
    }

    [Fact]
    public async Task FailedLazyDirectoryExpansionCanBeRetried()
    {
        using var fake = new FakeExplorer { FailDirectories = true };
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("tree.img"), new LayoutSettings());
        StructureNode volume = workspace.Roots[0].Children.Single(n => n.VolumeId is not null);
        await volume.LoadCommand.ExecuteAsync(null);
        Assert.Contains("directory failure", workspace.Error);
        fake.FailDirectories = false;
        await volume.LoadCommand.ExecuteAsync(null);
        Assert.Equal("/docs", Assert.Single(volume.Children).Path);
    }

    [Fact]
    public async Task HeadlessShellRendersCompactMinimumLayoutAndVectorSizesAtAllScales()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            foreach (string foreground in new[] { "WorkbenchText", "WorkbenchMuted", "WorkbenchAccent", "WorkbenchWarning", "WorkbenchError", "WorkbenchSuccess" })
            {
                Assert.True(Application.Current!.TryGetResource(foreground, null, out object? foregroundResource));
                Assert.True(Application.Current.TryGetResource("WorkbenchSurface", null, out object? backgroundResource));
                Color fg = Assert.IsAssignableFrom<ISolidColorBrush>(foregroundResource).Color;
                Color bg = Assert.IsAssignableFrom<ISolidColorBrush>(backgroundResource).Color;
                double contrast = (Luminance(fg) + 0.05) / (Luminance(bg) + 0.05);
                Assert.True(contrast >= 4.5, $"{foreground}: {contrast:F2}");
            }
            using var fake = new FakeExplorer();
            var store = new MemoryLayout();
            using var main = new MainWindowViewModel(fake, new FakeDialogs(), store);
            await main.OpenPathsCommand.ExecuteAsync(new[] { "layout.img" });
            var window = new MainWindow { DataContext = main, Width = 1000, Height = 640 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(7, window.GetVisualDescendants().OfType<Menu>().Single().Items.Count);
            Assert.NotEmpty(window.GetVisualDescendants().OfType<DataGrid>());
            Assert.NotEmpty(window.GetVisualDescendants().OfType<TreeView>());
            WorkspaceView view = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
            ItemsControl breadcrumbs = view.FindControl<ItemsControl>("BreadcrumbBar")!;
            Assert.Single(main.ActiveWorkspace!.Breadcrumbs);
            Assert.Single(breadcrumbs.Items);
            Assert.True(breadcrumbs.Bounds.Width >= 20, breadcrumbs.Bounds.ToString());
            Button rootCrumb = breadcrumbs.GetVisualDescendants().OfType<Button>().Single();
            Assert.True(rootCrumb.Bounds.Width >= 20, rootCrumb.Bounds.ToString());
            Assert.True(rootCrumb.IsEffectivelyVisible);
            WorkspaceViewModel original = main.ActiveWorkspace!;
            DataGrid originalGrid = view.FindControl<DataGrid>("FileGrid")!;
            Assert.Equal("Modified", originalGrid.Columns[3].Header);
            DataGridColumnHeader nameHeader = originalGrid.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .Single(header => header.Content?.ToString() == "Name ↑");
            Point click = nameHeader.TranslatePoint(new Point(nameHeader.Bounds.Width / 2, nameHeader.Bounds.Height / 2), window)!.Value;
            window.MouseDown(click, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BrowserSortDirection.Descending, original.BrowserPreferences.SortDirection);
            Assert.Equal(original.Entries.Select(e => e.Path), originalGrid.CollectionView.Cast<FileEntry>().Select(e => e.Path));
            Assert.True(originalGrid.CollectionView.Cast<FileEntry>().First().IsDirectory);
            Assert.False(originalGrid.CollectionView.Cast<FileEntry>().Last().IsDirectory);
            originalGrid.Columns[0].ClearSort();
            Dispatcher.UIThread.RunJobs();
            originalGrid.Columns[0].Width = new DataGridLength(180);
            originalGrid.SelectedItem = original.Entries[0];
            originalGrid.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Assert.Equal("/docs", original.CurrentPath);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
            Assert.Equal("/", original.CurrentPath);
            Assert.Equal("/", ((StructureNode)view.FindControl<TreeView>("StructureTree")!.SelectedItem!).Path);
            originalGrid.SelectedItems.Add(original.Entries[0]);
            originalGrid.SelectedItems.Add(original.Entries[1]);
            Assert.Equal(2, original.Selection.Count);
            originalGrid.SelectedItems.Clear();
            originalGrid.SelectedItems.Add(original.Entries[1]);
            Assert.Single(original.Selection);
            StructureNode volumeNode = original.Roots[0].Children.Single(n => n.VolumeId is not null);
            await volumeNode.LoadCommand.ExecuteAsync(null);
            Assert.Single(volumeNode.Children);
            Assert.Equal("/docs", volumeNode.Children[0].Path);
            await main.OpenPathsCommand.ExecuteAsync(new[] { "another.iso" });
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            main.ActiveWorkspace = original;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            view = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
            Assert.Single(original.Selection);
            Assert.Single(view.FindControl<DataGrid>("FileGrid")!.SelectedItems);
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);
            await PreferencesAndNavigationTests.VerifyActualViewsAsync(window, main, fake);
            var iconSheet = new UniformGrid { Columns = 20 };
            foreach (int size in new[] { 16, 18, 20, 24 })
            {
                foreach (string kind in new[] { "Open", "Close", "Extract", "Add", "Replace", "Folder", "File", "Disk", "Optical", "Partition", "Info", "Hex", "Search", "Refresh", "Play", "Back", "Warning", "Success", "Error", "Chevron" })
                {
                    iconSheet.Children.Add(new PathIcon { Data = Icons.Get(kind), Width = size, Height = size, Foreground = Brushes.White });
                }
            }
            var iconWindow = new Window { Content = iconSheet, Width = 640, Height = 160 };
            iconWindow.Show();
            foreach (double scale in new[] { 1, 1.25, 1.5, 2 })
            {
                window.SetRenderScaling(scale);
                iconWindow.SetRenderScaling(scale);
                window.UpdateLayout();
                Assert.Equal(scale, window.RenderScaling);
                using var rendered = window.CaptureRenderedFrame();
                Assert.NotNull(rendered);
                Assert.True(rendered.PixelSize.Width >= 1000 * scale);
                using var renderedIcons = iconWindow.CaptureRenderedFrame();
                Assert.NotNull(renderedIcons);
                Assert.Equal(80, iconSheet.Children.Count);
                Assert.True(view.Bounds.Width > 0);
                Assert.True(view.GetVisualDescendants().OfType<DataGrid>().Single().Bounds.Height >= 100);
                foreach (int size in new[] { 16, 18, 20, 24 })
                {
                    foreach (string kind in new[] { "Open", "Close", "Extract", "Add", "Replace", "Folder", "File", "Disk", "Optical", "Partition", "Info", "Hex", "Search", "Refresh", "Play", "Back", "Warning", "Success", "Error", "Chevron" })
                    {
                        Geometry icon = Icons.Get(kind);
                        var path = new Avalonia.Controls.Shapes.Path { Data = icon, Width = size, Height = size, Stretch = Stretch.Uniform };
                        path.Measure(new Size(size, size));
                        path.Arrange(new Rect(0, 0, size, size));
                        Assert.True(icon.Bounds.Width > 0, kind);
                        Assert.Equal(size, path.Bounds.Width);
                    }
                }
            }
            WorkspaceViewModel workspace = main.ActiveWorkspace!;
            workspace.ToggleToolsCommand.Execute(null);
            window.UpdateLayout();
            Assert.False(workspace.ToolVisible);
            Assert.Equal(0, view.FindControl<Grid>("CenterGrid")!.RowDefinitions[2].ActualHeight);
            fake.Directories["/docs"] = Enumerable.Range(0, 50000)
                .Select(i => new FileEntry($"/docs/file{i}.txt", $"file{i}.txt", false, i, null, "")).ToArray();
            await original.NavigateCommand.ExecuteAsync("/docs");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(50000, original.Entries.Count);
            Assert.InRange(view.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 100);
            original.SelectEntries(original.Entries);
            Assert.Contains($"first 32 of {50000:N0}", original.Properties);
            Assert.True(original.Properties.Length < 20000);
            original.ApplyPreferences(new ApplicationPreferences(BrowserSortField.Name, BrowserSortDirection.Descending, true));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(50000, view.FindControl<DataGrid>("FileGrid")!.SelectedItems.Count);
            Assert.InRange(view.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 100);
            original.SelectEntries([original.Entries[100]]);
            original.ApplyPreferences(new ApplicationPreferences(BrowserSortField.Size, BrowserSortDirection.Descending, true));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(view.FindControl<DataGrid>("FileGrid")!.SelectedItems);
            Assert.InRange(view.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 100);
            Assert.True(original.ToolVisible);
            original.ToggleToolsCommand.Execute(null);
            window.UpdateLayout();
            window.Close();
            iconWindow.Close();
            Assert.False(store.Settings.ToolVisible);
            Assert.InRange(store.Settings.LeftWidth, 160, 600);
            return true;
        }, CancellationToken.None);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte component)
        {
            double value = component / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}

internal sealed class FakeDialogs : IPlatformDialogs
{
    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(["dialog.img"]);
    public Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("chosen-destination");
}

internal sealed class MemoryLayout : ILayoutStore
{
    public LayoutSettings Settings { get; private set; } = new();
    public LayoutSettings Load() => Settings;
    public void Save(LayoutSettings settings) => Settings = settings;
}

internal sealed class InvalidLayout : ILayoutStore
{
    public LayoutSettings Load() => throw new InvalidDataException("invalid layout");
    public void Save(LayoutSettings settings) => throw new IOException("layout save refused");
}

internal sealed class FakeExplorer : IExplorerService
{
    public Dictionary<string, IReadOnlyList<FileEntry>> Directories { get; } = [];
    public static FileEntry Readme { get; } = new("/docs/readme.txt", "readme.txt", false, 12, DateTime.UnixEpoch, "Archive");
    public List<Guid> Closed { get; } = [];
    public string? ExtractedTo { get; private set; }
    public IReadOnlyList<FileEntry> ExtractedEntries { get; private set; } = [];
    public long LastHexOffset { get; private set; }
    public int HexReadCount { get; private set; }
    public TaskCompletionSource<IReadOnlyList<FileEntry>>? SlowDirectory { get; set; }
    public TaskCompletionSource<WorkspaceInfo>? SlowOpen { get; set; }
    public TaskCompletionSource<PreviewResult>? SlowPreview { get; set; }
    public TaskCompletionSource<IReadOnlyList<SearchHit>>? SlowSearch { get; set; }
    public bool FailDirectories { get; set; }
    public bool FailExtraction { get; set; }
    public int DirectoryReadCount { get; private set; }
    public WorkspaceInfo Create(string path) => new(Guid.NewGuid(), WorkspaceState.ReadyReadOnly,
        new ImageMetadata(path, path.EndsWith(".iso", StringComparison.Ordinal) ? "ISO" : "Raw", 100000, 200000, 512, new Dictionary<string, string>()),
        [], [new VolumeInfo("fs", "FAT16", "TEST", null, new Dictionary<string, string>())],
        [], new WorkspaceCapabilities(new(true, "read"), new(true, "read"), new(true, "bounded"), new(false, "No full validator")));
    public Task<WorkspaceInfo> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        SlowOpen?.Task ?? (path == "broken.img" ? Task.FromException<WorkspaceInfo>(new IOException("broken fixture")) : Task.FromResult(Create(path)));
    public Task CloseAsync(Guid workspaceId) { Closed.Add(workspaceId); return Task.CompletedTask; }
    public Task<IReadOnlyList<FileEntry>> ReadDirectoryAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default)
    {
        DirectoryReadCount++;
        if (FailDirectories) { return Task.FromException<IReadOnlyList<FileEntry>>(new IOException("directory failure")); }
        if (path == "/slow" && SlowDirectory is not null) { return SlowDirectory.Task; }
        if (Directories.TryGetValue(path, out IReadOnlyList<FileEntry>? custom)) { return Task.FromResult(custom); }
        if (path == "/large")
        {
            return Task.FromResult<IReadOnlyList<FileEntry>>(Enumerable.Range(0, 50000)
                .Select(i => new FileEntry($"/large/file{i}.txt", $"file{i}.txt", false, i, null, "")).ToArray());
        }
        return Task.FromResult<IReadOnlyList<FileEntry>>(path == "/"
            ? [new FileEntry("/docs", "docs", true, 0, null, "Directory"), Readme] : [Readme]);
    }
    public Task<PreviewResult> PreviewAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default) =>
        SlowPreview?.Task ?? Task.FromResult(new PreviewResult("bounded text", false, "Plain text"));
    public Task<PreviewResult> ReadHexAsync(Guid workspaceId, string? volumeId, string? path, long offset, CancellationToken cancellationToken = default)
    {
        HexReadCount++;
        LastHexOffset = offset;
        return Task.FromResult(new PreviewResult("00000000  01 02 03", false, "Hex page"));
    }
    public Task<IReadOnlyList<SearchHit>> SearchAsync(Guid workspaceId, string query, CancellationToken cancellationToken = default) =>
        SlowSearch?.Task ?? Task.FromResult<IReadOnlyList<SearchHit>>([new SearchHit("fs", Readme)]);
    public Task<ExtractionResult> ExtractAsync(Guid workspaceId, string volumeId, IReadOnlyList<FileEntry> entries, string destination,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ExtractedTo = destination;
        ExtractedEntries = entries;
        progress?.Report(new OperationProgress("Extract", Readme.Path, 12, 1));
        return Task.FromResult(new ExtractionResult(1, 12, FailExtraction ? [new ExtractionFailure(Readme.Path, "duplicate refused")] : []));
    }
    public void Dispose() { }
}
