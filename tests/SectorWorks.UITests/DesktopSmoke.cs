using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

public static class DesktopSmoke
{
    public static string[] Arguments { get; private set; } = [];
    internal static bool IsPhase2 => Arguments.Length > 0 && Arguments[0] == "--phase2";

    internal static FileEntry SelectPreviewFile(IEnumerable<FileEntry> entries)
    {
        FileEntry[] safe = entries.Where(e => !e.IsDirectory && !e.IsLink).ToArray();
        return safe.FirstOrDefault(e => e.Name.Equals("README.TXT", StringComparison.OrdinalIgnoreCase)) ?? safe.First();
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is not (3 or 4))
        {
            Console.Error.WriteLine("Usage: SectorWorks.UITests <image path> <new extraction folder> <screenshot.png> [minimum]");
            Console.Error.WriteLine("   or: SectorWorks.UITests --phase2 <new generated-fixture folder> <screenshot.png> [minimum]");
            return 2;
        }
        Arguments = args;
        return AppBuilder.Configure<SmokeApplication>().UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime([]);
    }
}

public sealed class SmokeApplication : App
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new InvalidOperationException("Desktop smoke requires a physical desktop lifetime.");
        }
        if (DesktopSmoke.IsPhase2)
        {
            Phase2DesktopSmoke.Initialize(desktop);
            return;
        }
        bool minimum = DesktopSmoke.Arguments.Length == 4 && DesktopSmoke.Arguments[3] == "minimum";
        var window = new MainWindow { ShowActivated = false, Width = minimum ? 1000 : 1200, Height = minimum ? 640 : 800 };
        var preferencesStore = new JsonPreferencesStore(Path.Combine(DesktopSmoke.Arguments[1], ".sectorworks-smoke-preferences.json"));
        var main = new MainWindowViewModel(new ExplorerService(), new SmokeDialogs(DesktopSmoke.Arguments[1]),
            new MemoryLayout(), preferencesStore);
        window.DataContext = main;
        desktop.MainWindow = window;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        timeout.Tick += (_, _) =>
        {
            Console.Error.WriteLine("FAIL physical desktop: 45-second deadline exceeded.");
            main.Dispose();
            desktop.Shutdown(124);
        };
        timeout.Start();
        var smoke = new AsyncRelayCommand(async () =>
        {
            int exitCode = 1;
            try
            {
                await main.OpenPathsCommand.ExecuteAsync(new[] { DesktopSmoke.Arguments[0] });
                WorkspaceViewModel workspace = main.ActiveWorkspace ?? throw new InvalidOperationException(main.Error);
                FileEntry file = DesktopSmoke.SelectPreviewFile(workspace.Entries);
                workspace.SelectEntries([file]);
                if (!workspace.CanPreview) { throw new InvalidOperationException("Selected file preview is unavailable."); }
                if (workspace.PreviewCommand.ExecutionTask is Task preview) { await preview; }
                if (workspace.Error.Length > 0) { throw new InvalidOperationException(workspace.Error); }
                var hexView = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
                hexView.FindControl<TabControl>("ToolTabs")!.SelectedIndex = 1;
                if (workspace.HexCommand.ExecutionTask is not Task hexLoad)
                {
                    throw new InvalidOperationException("Selecting Hex View did not load the first page.");
                }
                await hexLoad;
                if (workspace.Error.Length > 0) { throw new InvalidOperationException(workspace.Error); }
                if (workspace.HexOffset != 0 || !workspace.HexText.Contains("Read-only hex:", StringComparison.Ordinal)
                    || workspace.ToolIndex != 1)
                {
                    throw new InvalidOperationException("Initial Hex View page was not displayed.");
                }
                await workspace.ExtractCommand.ExecuteAsync(null);
                if (workspace.Error.Length > 0) { throw new InvalidOperationException(workspace.Error); }
                string extracted = Path.Combine(DesktopSmoke.Arguments[1], file.Name);
                if (!File.Exists(extracted) || new FileInfo(extracted).Length != file.Length)
                {
                    throw new IOException("The selected file was not extracted at its reported length.");
                }
                string extractedHash;
                using (FileStream input = File.OpenRead(extracted)) { extractedHash = Convert.ToHexStringLower(SHA256.HashData(input)); }
                var preferences = new PreferencesViewModel(main.Preferences, main.SavePreferencesAsync)
                {
                    SortField = BrowserSortField.Name,
                    SortDirection = BrowserSortDirection.Descending,
                    FoldersFirst = true
                };
                var preferencesWindow = new PreferencesWindow(preferences) { ShowActivated = false };
                preferencesWindow.Show(window);
                await preferences.SaveCommand.ExecuteAsync(null);
                if (preferences.Error.Length > 0 || preferencesWindow.IsVisible
                    || preferencesStore.Load() != main.Preferences || workspace.Selection.Count != 1)
                {
                    throw new InvalidOperationException("Preferences save/reload or selection preservation failed: " + preferences.Error);
                }
                FileEntry? directory = workspace.Entries.FirstOrDefault(e => e.IsDirectory && !e.IsLink);
                if (directory is not null)
                {
                    await workspace.ActivateCommand.ExecuteAsync(directory);
                    if (workspace.Error.Length > 0) { throw new InvalidOperationException(workspace.Error); }
                    await workspace.UpCommand.ExecuteAsync(null);
                    if (workspace.Error.Length > 0) { throw new InvalidOperationException(workspace.Error); }
                }
                workspace.Query = file.Name;
                await workspace.SearchCommand.ExecuteAsync(null);
                if (workspace.Error.Length > 0) { throw new InvalidOperationException(workspace.Error); }
                if (workspace.SearchResults.Count == 0) { throw new InvalidOperationException("Search did not find the selected file."); }
                await workspace.NavigateHitCommand.ExecuteAsync(workspace.SearchResults[0]);
                if (workspace.Error.Length > 0) { throw new InvalidOperationException(workspace.Error); }
                await Task.Delay(300);
                window.UpdateLayout();
                var view = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
                if (view.Bounds.Width <= 0) { throw new InvalidOperationException("Workspace failed to lay out."); }
                view.FindControl<TabControl>("ToolTabs")!.SelectedIndex = 1;
                await workspace.HexCommand.ExecutionTask!;
                if (workspace.Error.Length > 0 || workspace.HexOffset != 0 || workspace.ToolIndex != 1
                    || (file.Length > 0 && !workspace.HexText.Contains("000000000000  ", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("Hex first page failed after search navigation: " + workspace.Error);
                }
                var tree = view.FindControl<Avalonia.Controls.TreeView>("StructureTree")!;
                if (tree.SelectedItem is not StructureNode selected || selected.Path != workspace.CurrentPath
                    || selected != workspace.SelectedNode)
                {
                    throw new InvalidOperationException("Actual TreeView selection does not match the browser directory.");
                }
                using (var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, window.RenderScaling),
                    new Vector(96 * window.RenderScaling, 96 * window.RenderScaling)))
                {
                    bitmap.Render(window);
                    bitmap.Save(DesktopSmoke.Arguments[2], PngBitmapEncoderOptions.Default);
                }
                await main.CloseCommand.ExecuteAsync(workspace);
                if (main.Error.Length > 0) { throw new InvalidOperationException(main.Error); }
                Console.WriteLine($"PASS physical desktop: {workspace.Title}; {workspace.Entries.Count} entries; preview, hex, search, extraction, preferences save/reload, synchronized TreeView; scaling={window.RenderScaling}; screenshot={DesktopSmoke.Arguments[2]}");
                Console.WriteLine($"PASS extraction: {file.Name}; {file.Length} bytes; SHA-256 {extractedHash}");
                exitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL physical desktop: " + ex); }
            finally { timeout.Stop(); main.Dispose(); desktop.Shutdown(exitCode); }
        });
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => smoke.Execute(null));
    }
}

internal sealed class SmokeDialogs(string destination) : IPlatformDialogs
{
    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destination);
        return Task.FromResult<string?>(destination);
    }
}
