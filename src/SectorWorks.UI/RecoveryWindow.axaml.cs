using Avalonia.Controls;
using Avalonia.Interactivity;
using SectorWorks.Core;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UI;

public sealed partial class RecoveryWindow : Window
{
    public RecoveryWindow() => InitializeComponent();

    public RecoveryWindow(ICommitRecoveryService service) : this() =>
        DataContext = new RecoveryViewModel(service, new AvaloniaPlatformDialogs(this));

    private void KeepArtifacts(object? sender, RoutedEventArgs args)
    {
        if (DataContext is RecoveryViewModel { IsBusy: true }) { return; }
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is RecoveryViewModel { IsBusy: true }) { e.Cancel = true; }
        base.OnClosing(e);
    }
}
