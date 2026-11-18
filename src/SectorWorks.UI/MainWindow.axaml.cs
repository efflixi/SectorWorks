using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UI;

public sealed partial class MainWindow : Window
{
    private readonly AsyncRelayCommand preferencesCommand;
    private readonly AsyncRelayCommand recoveryCommand;
    private bool closeApproved;
    private bool closePromptActive;
    public MainWindow()
    {
        InitializeComponent();
        // The default logical height can exceed a laptop's work area at 125%
        // scaling. Keep confirmation/status controls visible on first launch.
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is not { } screen || screen.Scaling <= 0) { return; }
            double width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - 24));
            double height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 40));
            if (width < Width || height < Height)
            {
                Width = width;
                Height = height;
                Position = new Avalonia.PixelPoint(
                    screen.WorkingArea.X + (int)Math.Max(0, (screen.WorkingArea.Width - (width + 24) * screen.Scaling) / 2),
                    screen.WorkingArea.Y + (int)Math.Max(0, (screen.WorkingArea.Height - (height + 40) * screen.Scaling) / 2));
            }
        };
        preferencesCommand = new AsyncRelayCommand(async () =>
        {
            if (DataContext is not MainWindowViewModel main) { return; }
            try
            {
                var model = new PreferencesViewModel(main.Preferences, main.SavePreferencesAsync);
                await new PreferencesWindow(model).ShowDialog(this);
            }
            catch (Exception ex) { main.Error = "Could not open Preferences: " + ex.Message; }
        });
        recoveryCommand = new AsyncRelayCommand(OpenRecoveryAsync);
        Closing += async (_, args) =>
        {
            if (closeApproved) { return; }
            if (DataContext is MainWindowViewModel main && (main.IsOpening || main.Workspaces.Count > 0))
            {
                args.Cancel = true;
                if (closePromptActive) { return; }
                closePromptActive = true;
                try
                {
                    if (!await main.ConfirmWindowCloseAsync()) { return; }
                    foreach (WorkspaceView view in this.GetVisualDescendants().OfType<WorkspaceView>()) { view.SaveLayout(); }
                    closeApproved = true;
                    Close();
                }
                catch (Exception ex) { main.Error = "Could not close: " + ex.Message; }
                finally { closePromptActive = false; }
                return;
            }
            foreach (WorkspaceView view in this.GetVisualDescendants().OfType<WorkspaceView>()) { view.SaveLayout(); }
        };
        AddHandler(KeyDownEvent, PasteFilesKeyDown, RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                SearchBox.Focus();
                e.Handled = true;
            }

        };
    }

    private async void PasteFilesKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.V || args.KeyModifiers != KeyModifiers.Control
            || args.Source is Visual focused && focused.GetSelfAndVisualAncestors().Any(control => control is TextBox)
            || DataContext is not MainWindowViewModel { ActiveWorkspace: { CanAdd: true } workspace }) { return; }
        args.Handled = true;
        await PasteFilesAsync(workspace);
    }

    private async void PasteFiles(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainWindowViewModel { ActiveWorkspace: { } workspace })
        {
            await PasteFilesAsync(workspace);
        }
    }

    private Task PasteFilesAsync(WorkspaceViewModel workspace) => workspace.ImportFilesAsync(async () =>
        Clipboard is { } clipboard && await clipboard.TryGetFilesAsync() is { } files
            ? files.Select(item => item is IStorageFile ? item.TryGetLocalPath() : null).ToArray() : []);

    private void OpenPreferences(object? sender, RoutedEventArgs args)
    {
        if (!preferencesCommand.IsRunning) { preferencesCommand.Execute(null); }
    }

    public async Task OpenRecoveryAsync()
    {
        if (DataContext is not MainWindowViewModel { RecoveryService: { } service } main) { return; }
        if (main.IsOpening || main.Workspaces.Count != 0)
        {
            main.Error = "Close image workspaces before recovery cleanup so their reader handles cannot conflict with recovery locking.";
            return;
        }
        try
        {
            var window = new RecoveryWindow(service);
            var model = (RecoveryViewModel)window.DataContext!;
            await model.RefreshCommand.ExecuteAsync(null);
            await window.ShowDialog(this);
            await main.CheckRecoveryAsync();
        }
        catch (Exception error) { main.Error = "Could not open recovery: " + error.Message; }
    }

    private void OpenRecovery(object? sender, RoutedEventArgs args)
    {
        if (!recoveryCommand.IsRunning) { recoveryCommand.Execute(null); }
    }

    private void CloseTab(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainWindowViewModel main && sender is Button { DataContext: WorkspaceViewModel workspace })
        {
            main.CloseCommand.Execute(workspace);
        }
        args.Handled = true;
    }
}
