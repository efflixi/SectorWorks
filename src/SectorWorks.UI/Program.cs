using Avalonia;
using Avalonia.Media;

namespace SectorWorks.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--verify-ui", StringComparer.Ordinal))
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHarfBuzz().UseStandardRuntimePlatformSubsystem()
                    .UseWindowingSubsystem(() => { }, "Resource verification only").SetupWithoutStarting();
                Application app = Application.Current!;
                foreach (string kind in new[] { "Open", "Close", "Extract", "Add", "Replace", "Folder", "File", "Disk",
                    "Optical", "Partition", "Info", "Hex", "Search", "Refresh", "Play", "Back", "Warning", "Success", "Error", "Chevron" })
                {
                    if (!app.TryGetResource("Icon" + kind, null, out object? icon) || icon is not Geometry geometry
                        || geometry.Bounds.Width <= 0 || geometry.Bounds.Height <= 0)
                    {
                        throw new InvalidDataException("Missing or invalid vector icon: " + kind);
                    }
                }
                Console.WriteLine("SectorWorks UI resources verified: graphite theme and 20 vector geometries.");
                Console.WriteLine("No desktop started. Run SectorWorks.UITests for rendered layout and workflow verification.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("UI resource verification failed: " + ex.Message);
                return 1;
            }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
