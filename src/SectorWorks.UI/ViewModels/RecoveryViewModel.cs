using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.UI.ViewModels;

public sealed partial class RecoveryViewModel(ICommitRecoveryService service, IImageEditingDialogs dialogs) : ViewModelBase
{
    [ObservableProperty] private IReadOnlyList<RecoveryItem> items = [];
    [ObservableProperty] private RecoveryItem? selectedItem;
    [ObservableProperty] private string warnings = "";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private string status = "Recovery artifacts are retained until you explicitly clean them.";
    [ObservableProperty] private bool isBusy;

    public bool CanClean => !IsBusy && SelectedItem?.CanClean == true;
    public string SelectionDetails => SelectedItem is not { } item ? "Select a transaction to inspect its source and journal."
        : $"Source: {item.SourcePath}\nJournal: {item.JournalPath}\nStage: {item.Stage}\n{item.Description}";

    partial void OnSelectedItemChanged(RecoveryItem? value)
    {
        OnPropertyChanged(nameof(SelectionDetails));
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) { return; }
        IsBusy = true;
        Error = "";
        try
        {
            RecoveryScan scan = await service.DiscoverAsync();
            Items = scan.Items;
            Warnings = string.Join("\n", scan.Warnings);
            SelectedItem = Items.FirstOrDefault();
            Status = $"{Items.Count} interrupted transactions; {scan.Warnings.Count} warnings. No source has been overwritten.";
        }
        catch (Exception error) { Error = error.Message; Status = "Recovery scan failed; artifacts retained."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!CanClean || SelectedItem is not { } item) { return; }
        IsBusy = true;
        Error = "";
        try
        {
            if (!await dialogs.ConfirmAsync("Clean interrupted commit artifacts",
                SelectionDetails + (item.CatalogOnly
                    ? "\n\nRemove only this stale catalog entry? All source, backup and staging files stay untouched. Cleanup refuses an active source lock or a newly appearing journal. "
                    : "\n\nRemove this transaction's retained backup, staging image and journal? ")
                + "The source image will never be restored or overwritten. A pre-finalize transaction must still match its original source and backup. "
                + "Keep artifacts if you need to inspect or copy them independently.", "Verify and clean artifacts"))
            {
                Status = "Artifacts retained.";
                return;
            }
            await service.CleanAsync(item);
            Items = Items.Where(row => row.Id != item.Id).ToArray();
            SelectedItem = Items.FirstOrDefault();
            Status = item.CatalogOnly ? "Stale catalog entry removed. All source/backup/staging files were left untouched."
                : "Recovery artifacts cleaned. The source image was not overwritten.";
        }
        catch (Exception error) { Error = error.Message; Status = "Cleanup did not complete; inspect retained artifacts and refresh."; }
        finally { IsBusy = false; }
    }
}
