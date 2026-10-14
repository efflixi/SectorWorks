namespace SectorWorks.Platform;

public interface IImageEditingDialogs
{
    Task<string?> PickImportFileAsync(CancellationToken cancellationToken = default);
    Task<string?> PickNewImagePathAsync(string suggestedName, CancellationToken cancellationToken = default);
    Task<string?> RequestNameAsync(string title, string initialName, CancellationToken cancellationToken = default);
    Task<bool> ConfirmAsync(string title, string message, string acceptLabel, CancellationToken cancellationToken = default);
}
