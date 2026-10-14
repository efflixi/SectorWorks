namespace SectorWorks.Core;

public enum ImageChangeKind { AddFile, CreateDirectory, ReplaceFile, Delete, Rename, Metadata }

public sealed record ImageChangeRequest(ImageChangeKind Kind, string VolumeId, string Path,
    string? SourcePath = null, string? NewName = null);

public sealed record PendingImageChange(long Sequence, ImageChangeKind Kind, string VolumeId,
    string Path, string? NewName, long ContentLength);

public sealed record EditingSnapshot(IReadOnlyList<PendingImageChange> Changes, bool CanUndo, bool CanRedo,
    long EstimatedOutputBytes)
{
    public bool IsModified => Changes.Count != 0;
}

public sealed record SaveImageResult(string Path, long Length, int FilesValidated, string ValidationSummary);

public interface IImageEditingService
{
    Task<EditingSnapshot> GetEditingSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<EditingSnapshot> StageAsync(Guid workspaceId, ImageChangeRequest request, CancellationToken cancellationToken = default);
    Task<EditingSnapshot> UndoAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<EditingSnapshot> RedoAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<EditingSnapshot> DiscardAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<SaveImageResult> SaveAsAsync(Guid workspaceId, string destination,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record StagedWriteOperation(ImageChangeRequest Request, Func<Stream>? OpenContent);

public static class FatEditNames
{
    public static bool IsSupported(string name)
    {
        string[] parts = name.Split('.');
        return parts.Length is 1 or 2 && parts[0].Length is >= 1 and <= 8
            && (parts.Length == 1 || parts[1].Length is >= 1 and <= 3)
            && parts.All(part => part.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'));
    }
}

// Writers may only receive a disposable copy, never the open source image.
public interface IStagedImageWriter
{
    Capability Qualify(IImageSession image);
    void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken);
    void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken);
}
