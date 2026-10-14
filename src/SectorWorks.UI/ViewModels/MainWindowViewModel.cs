using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IExplorerService explorer;
    private readonly IPlatformDialogs dialogs;
    private readonly ILayoutStore layoutStore;
    private readonly IPreferencesStore? preferencesStore;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<CancellationTokenSource> opens = [];
    private bool disposed;

    public MainWindowViewModel(IExplorerService explorer, IPlatformDialogs dialogs, ILayoutStore layoutStore,
        IPreferencesStore? preferencesStore = null)
    {
        this.explorer = explorer;
        this.dialogs = dialogs;
        this.layoutStore = layoutStore;
        this.preferencesStore = preferencesStore;
        try
        {
            ApplicationPreferences preferences = preferencesStore?.Load() ?? new ApplicationPreferences();
            preferences.Validate();
            Preferences = preferences;
        }
        catch (Exception ex) { PreferencesWarning = "Could not load preferences; defaults are being used: " + ex.Message; }
        try
        {
            Layout = layoutStore.Load();
        }
        catch (Exception ex)
        {
            Layout = new LayoutSettings();
            LayoutWarning = "Could not load layout; default pane dimensions are being used: " + ex.Message;
        }
    }

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = [];
    public LayoutSettings Layout { get; private set; }
    public ApplicationPreferences Preferences { get; private set; } = new();
    [ObservableProperty] private WorkspaceViewModel? activeWorkspace;
    [ObservableProperty] private string status = "Ready — open an image to inspect it read-only.";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private string layoutWarning = "";
    [ObservableProperty] private string preferencesWarning = "";
    [ObservableProperty] private bool isOpening;

    [RelayCommand]
    private async Task OpenAsync()
    {
        try
        {
            await OpenPathsAsync(await dialogs.PickImagesAsync(lifetime.Token));
        }
        catch (OperationCanceledException) { Status = "Open cancelled."; }
        catch (Exception ex) { Error = ex.Message; Status = "Open failed."; }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenPathsAsync(IEnumerable<string>? paths)
    {
        if (paths is null || disposed)
        {
            return;
        }
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        opens.Add(source);
        IsOpening = true;
        Error = "";
        foreach (string path in paths)
        {
            try
            {
                Status = "Opening " + Path.GetFileName(path) + "…";
                WorkspaceInfo info = await explorer.OpenAsync(path, source.Token);
                if (disposed || source.IsCancellationRequested)
                {
                    await explorer.CloseAsync(info.Id);
                    break;
                }
                var workspace = new WorkspaceViewModel(explorer, dialogs, info, Layout, Preferences);
                Workspaces.Add(workspace);
                ActiveWorkspace = workspace;
                await workspace.InitializeAsync();
                Status = "Ready — source opened read-only.";
            }
            catch (OperationCanceledException) { Status = "Open cancelled."; break; }
            catch (Exception ex)
            {
                Error += (Error.Length == 0 ? "" : "\n") + $"{path}: {ex.Message}";
                Status = "Open failed; other workspaces are unchanged.";
            }
        }
        opens.Remove(source);
        IsOpening = opens.Count > 0;
    }

    [RelayCommand]
    private async Task CloseAsync(WorkspaceViewModel? workspace)
    {
        workspace ??= ActiveWorkspace;
        if (workspace is null)
        {
            return;
        }
        try
        {
            if (!await workspace.ConfirmCloseAsync()) { Status = workspace.Status; return; }
            workspace.Dispose();
            Workspaces.Remove(workspace);
            if (ActiveWorkspace == workspace)
            {
                ActiveWorkspace = Workspaces.LastOrDefault();
            }
            await explorer.CloseAsync(workspace.Info.Id);
            Status = "Image closed.";
        }
        catch (Exception ex) { Error = ex.Message; Status = "Close failed."; }
    }

    public async Task<bool> ConfirmWindowCloseAsync()
    {
        if (IsOpening) { Status = "Cancel image opening or wait before closing."; return false; }
        if (Workspaces.Any(workspace => workspace.IsBusy))
        {
            Status = "An image operation is active. Cancel it or wait before closing the window.";
            return false;
        }
        foreach (WorkspaceViewModel workspace in Workspaces.ToArray())
        {
            if (!await workspace.ConfirmCloseAsync()) { Status = workspace.Status; return false; }
        }
        return true;
    }

    [RelayCommand]
    private void Cancel()
    {
        foreach (CancellationTokenSource source in opens)
        {
            source.Cancel();
        }
        ActiveWorkspace?.Cancel();
    }

    public void SaveLayout(LayoutSettings settings)
    {
        Layout = settings;
        try { layoutStore.Save(settings); LayoutWarning = ""; }
        catch (Exception ex) { LayoutWarning = "Could not save layout: " + ex.Message; }
    }

    public async Task SavePreferencesAsync(ApplicationPreferences preferences)
    {
        try
        {
            preferences.Validate();
            if (preferencesStore is not null) { await Task.Run(() => preferencesStore.Save(preferences)); }
            Preferences = preferences;
            OnPropertyChanged(nameof(Preferences));
            foreach (WorkspaceViewModel workspace in Workspaces) { workspace.ApplyPreferences(preferences); }
            PreferencesWarning = "";
        }
        catch (Exception ex)
        {
            PreferencesWarning = "Could not save preferences; existing settings remain active: " + ex.Message;
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        lifetime.Cancel();
        foreach (WorkspaceViewModel workspace in Workspaces)
        {
            workspace.Dispose();
        }
        explorer.Dispose();
        lifetime.Dispose();
    }
}
