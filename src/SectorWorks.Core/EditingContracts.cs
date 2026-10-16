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

/// <summary>Result of a Phase 3 safe direct commit. TransactionId can be used to locate
/// any residual backup/journal artifacts if cleanup itself failed after a successful commit.</summary>
public sealed record CommitResult(string Path, long Length, int FilesValidated, string ValidationSummary, string TransactionId);

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
    public static bool IsSupported(string name) => IsSupportedName(name);

    public static bool IsSupportedName(string name) => ImageEditNames.IsSupportedName(name);

    public static int RequiredDirectorySlots(string name)
    {
        if (!IsSupportedName(name)) { throw new ArgumentException("Unsupported FAT filename.", nameof(name)); }
        return IsSupportedShortName(name) ? 1 : 1 + (name.Length + 12) / 13;
    }

    public static bool IsSupportedShortName(string name)
    {
        return IsValidShortName(name) && name.Split('.').All(part =>
            !part.Any(c => c is >= 'A' and <= 'Z') || !part.Any(c => c is >= 'a' and <= 'z'));
    }

    public static bool IsValidShortName(string name)
    {
        string[] parts = name.Split('.');
        return parts.Length is 1 or 2 && parts[0].Length is >= 1 and <= 8
            && (parts.Length == 1 || parts[1].Length is >= 1 and <= 3)
            && parts.All(part => part.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'));
    }
}

public static class ImageEditNames
{
    public static bool IsSupportedName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255 || name is "." or ".."
            || name[0] == ' ' || name[^1] is ' ' or '.' || name.Any(char.IsSurrogate)
            || !name.IsNormalized(System.Text.NormalizationForm.FormC))
        {
            return false;
        }
        foreach (char c in name)
        {
            var category = char.GetUnicodeCategory(c);
            if (c is < ' ' or '\u007f' or '"' or '*' or '/' or ':' or '<' or '>' or '?' or '\\' or '|'
                || char.IsSurrogate(c) || c is '\ufffe' or '\uffff'
                || category is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.PrivateUse or System.Globalization.UnicodeCategory.OtherNotAssigned
                    or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }
        }
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CLOCK$")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] is >= '1' and <= '9');
    }
}

// Writers may only receive a disposable copy, never the open source image.
public interface IStagedImageWriter
{
    Capability Qualify(IImageSession image);
    Capability QualifyVolume(IImageSession image, string volumeId) => Qualify(image);
    void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken);
    void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken);
    void ValidateOutput(IImageSession source, IImageSession output,
        IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken);
}
