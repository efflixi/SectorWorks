using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var model = new MainWindowViewModel(new ExplorerService(), new AvaloniaPlatformDialogs(window),
                new JsonLayoutStore(), new JsonPreferencesStore());
            window.DataContext = model;
            desktop.MainWindow = window;
            window.Opened += (_, _) => model.OpenPathsCommand.Execute(
                desktop.Args?.Where(p => !p.StartsWith("--", StringComparison.Ordinal)).ToArray() ?? []);
            desktop.Exit += (_, _) => model.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
