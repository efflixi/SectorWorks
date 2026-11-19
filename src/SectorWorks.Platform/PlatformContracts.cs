using SectorWorks.Core;

namespace SectorWorks.Platform;

public interface IPlatformDialogs
{
    Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default);
    Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default);
}

public interface IReadOnlyImageDialogs
{
    Task ShowReadOnlyImageAsync(string imageName, IReadOnlyList<string> issues, CancellationToken cancellationToken = default);
}

public interface ICdAudioExportDialogs
{
    Task<IReadOnlyList<int>?> PickAudioTracksAsync(IReadOnlyList<CdTrack> audioTracks, int? selectedTrack,
        CancellationToken cancellationToken = default);
    Task<string?> PickAudioExportFolderAsync(CancellationToken cancellationToken = default);
}

public interface ICdMetadataDialogs
{
    Task<CdMetadataMatch?> PickCdMetadataAsync(IReadOnlyList<CdMetadataMatch> matches, CancellationToken cancellationToken = default);
}

public sealed record LayoutSettings(double LeftWidth = 260, double RightWidth = 300, double ToolHeight = 220, bool ToolVisible = true);

public interface ILayoutStore
{
    LayoutSettings Load();
    void Save(LayoutSettings settings);
}
