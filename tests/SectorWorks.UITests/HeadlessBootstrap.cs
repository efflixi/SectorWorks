using Avalonia;
using Avalonia.Headless;
using SectorWorks.UI;

namespace SectorWorks.UITests;

public static class HeadlessBootstrap
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();
}
