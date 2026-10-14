using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Platform;

namespace SectorWorks.UI.ViewModels;

public sealed partial class PreferencesViewModel : ViewModelBase
{
    private readonly Func<ApplicationPreferences, Task> save;

    public PreferencesViewModel(ApplicationPreferences preferences, Func<ApplicationPreferences, Task> save)
    {
        this.save = save;
        SortField = preferences.SortField;
        SortDirection = preferences.SortDirection;
        FoldersFirst = preferences.FoldersFirst;
    }

    public IReadOnlyList<BrowserSortField> SortFields { get; } = Enum.GetValues<BrowserSortField>();
    public IReadOnlyList<BrowserSortDirection> SortDirections { get; } = Enum.GetValues<BrowserSortDirection>();
    public event EventHandler? CloseRequested;
    [ObservableProperty] private BrowserSortField sortField;
    [ObservableProperty] private BrowserSortDirection sortDirection;
    [ObservableProperty] private bool foldersFirst;
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private string error = "";

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        Error = "";
        try
        {
            await save(new ApplicationPreferences(SortField, SortDirection, FoldersFirst));
            IsSaving = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Error = "Preferences were not saved or applied: " + ex.Message; }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsSaving) { CloseRequested?.Invoke(this, EventArgs.Empty); }
    }
}
