using Avalonia.Controls;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UI;

public sealed partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
        Closing += (_, e) => e.Cancel = DataContext is PreferencesViewModel { IsSaving: true };
    }

    public PreferencesWindow(PreferencesViewModel model) : this()
    {
        DataContext = model;
        model.CloseRequested += (_, _) => Close();
    }
}
