namespace SectorWorks.Platform;

public interface IPlatformDialogs
{
    Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default);
    Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default);
}

public sealed record LayoutSettings(double LeftWidth = 260, double RightWidth = 300, double ToolHeight = 220, bool ToolVisible = true);

public interface ILayoutStore
{
    LayoutSettings Load();
    void Save(LayoutSettings settings);
}
