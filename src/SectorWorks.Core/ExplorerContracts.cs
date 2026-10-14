namespace SectorWorks.Core;

public enum WorkspaceState { Opening, ReadyReadOnly, Failed, Closed, ReadyWritable, Modified }

public sealed record Capability(bool Available, string Reason);

public sealed record WorkspaceCapabilities(Capability Browse, Capability Extract, Capability Preview, Capability Validate)
{
    public static WorkspaceCapabilities ReadOnlyFileSystem { get; } = new(
        new Capability(true, "Read-only directory enumeration."),
        new Capability(true, "Ordinary file reads; unsupported per-file features are reported."),
        new Capability(true, "Bounded ordinary-file text/hex reads."),
        new Capability(false, "Full filesystem validation is not implemented."));
    public Capability PlayAudioTrack { get; } = new(false, "CD-DA playback belongs to Phase 6.");
    public Capability Modify { get; init; } = new(false, "This image stack is not qualified for staged writing.");
    public Capability AddFiles { get; init; } = new(false, "This image stack is not qualified for adding files.");
    public Capability ReplaceFiles { get; init; } = new(false, "This image stack is not qualified for replacing files.");
    public Capability DeleteFiles { get; init; } = new(false, "This image stack is not qualified for deleting files.");
    public Capability RenameFiles { get; init; } = new(false, "This image stack is not qualified for renaming files.");
    public Capability CreateDirectories { get; init; } = new(false, "This image stack is not qualified for creating directories.");
    public Capability EditMetadata { get; } = new(false, "Metadata changes are reserved but not implemented.");
    public Capability SaveAs { get; init; } = new(false, "This image stack is not qualified for Save As.");
    public Capability CommitInPlace { get; } = new(false, "Safe direct commit belongs to Phase 3.");
    public Capability Rebuild { get; } = new(false, "ISO rebuilding belongs to Phase 4; other rebuild workflows are not implemented.");
    public Capability PreserveBootability { get; } = new(false, "No bootability preservation guarantee is established.");
    public Capability PreserveExactLayout { get; } = new(false, "No image-writing or exact-layout preservation workflow is enabled.");
}

public sealed record VolumeInfo(
    string Id, string FileSystem, string Label, PartitionInfo? Partition,
    IReadOnlyDictionary<string, string> Properties, WorkspaceCapabilities? Capabilities = null);

public sealed record WorkspaceInfo(
    Guid Id, WorkspaceState State, ImageMetadata Metadata,
    IReadOnlyList<PartitionInfo> Partitions, IReadOnlyList<VolumeInfo> Volumes,
    IReadOnlyList<string> Warnings, WorkspaceCapabilities Capabilities);

public sealed record SearchHit(string VolumeId, FileEntry Entry);
public sealed record OperationProgress(string Stage, string Path, long BytesCompleted, int FilesCompleted);
public sealed record ExtractionFailure(string Path, string Error);
public sealed record ExtractionResult(int FilesCompleted, long BytesCompleted, IReadOnlyList<ExtractionFailure> Failures);
public sealed record PreviewResult(string Text, bool Truncated, string Description);

public interface ISafePreviewHandler
{
    PreviewResult Preview(Stream input, string name, int maximumBytes);
}

public interface IExplorerService : IDisposable
{
    Task<WorkspaceInfo> OpenAsync(string path, CancellationToken cancellationToken = default);
    Task CloseAsync(Guid workspaceId);
    Task<IReadOnlyList<FileEntry>> ReadDirectoryAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default);
    Task<PreviewResult> PreviewAsync(Guid workspaceId, string volumeId, string path, CancellationToken cancellationToken = default);
    Task<PreviewResult> ReadHexAsync(Guid workspaceId, string? volumeId, string? path, long offset, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchHit>> SearchAsync(Guid workspaceId, string query, CancellationToken cancellationToken = default);
    Task<ExtractionResult> ExtractAsync(Guid workspaceId, string volumeId, IReadOnlyList<FileEntry> entries,
        string destination, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
