using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UI;

public sealed partial class MainWindow : Window
{
    private readonly AsyncRelayCommand preferencesCommand;
    private bool closeApproved;
    private bool closePromptActive;
    public MainWindow()
    {
        InitializeComponent();
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
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                SearchBox.Focus();
                e.Handled = true;
            }

        };
    }

    private void OpenPreferences(object? sender, RoutedEventArgs args)
    {
        if (!preferencesCommand.IsRunning) { preferencesCommand.Execute(null); }
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
