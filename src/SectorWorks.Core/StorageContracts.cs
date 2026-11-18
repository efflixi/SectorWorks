namespace SectorWorks.Core;

public interface IReadOnlyBlockDevice : IDisposable
{
    long Length { get; }
    int LogicalSectorSize { get; }
    int? PhysicalSectorSize { get; }
    bool IsReadOnly => true;
    int Read(long offset, Span<byte> buffer);
    ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default);
    void Write(long offset, ReadOnlySpan<byte> buffer) => throw new NotSupportedException("This block device is read-only.");
    void Flush() => throw new NotSupportedException("A read-only block device cannot flush writes.");
}

public sealed record ImageMetadata(
    string Path, string Format, long ContainerLength, long VirtualLength,
    int SectorSize, IReadOnlyDictionary<string, string> Properties)
{
    public long? HexLength { get; init; }
    public bool HasOpticalPartitionLayout { get; init; }
}

public sealed record PartitionInfo(
    int Index, string Table, long Offset, long Length, string Type,
    string Name, bool IsActive, Guid? UniqueId = null);

// ModifiedUtc retains DateTimeKind.Unspecified for filesystems without timezone data.
public sealed record FileEntry(
    string Path, string Name, bool IsDirectory, long Length, DateTime? ModifiedUtc,
    string Attributes, string? ShortName = null, long? Offset = null, bool IsLink = false)
{
    public bool IsChanged { get; init; }
}

public interface IReadOnlyFileSystem : IDisposable
{
    string Name { get; }
    string VolumeLabel { get; }
    bool IsCaseSensitive { get; }
    WorkspaceCapabilities Capabilities => WorkspaceCapabilities.ReadOnlyFileSystem;
    IReadOnlyDictionary<string, string> Properties { get; }
    IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default);
    Stream OpenRead(string path);
}

public sealed record ImageVolume(string Id, PartitionInfo? Partition, IReadOnlyFileSystem FileSystem);

public interface IImageSession : IDisposable
{
    ImageMetadata Metadata { get; }
    IReadOnlyBlockDevice Device { get; }
    IReadOnlyList<PartitionInfo> Partitions { get; }
    IReadOnlyList<ImageVolume> Volumes { get; }
    IReadOnlyList<string> Warnings { get; }
}

public sealed record FormatDetection(string Format, int Priority, bool Recognized, string Reason);

public interface IImageOpener
{
    IImageSession OpenReadOnly(string path, CancellationToken cancellationToken = default);
}

public sealed class ImageFormatException : IOException
{
    public ImageFormatException(string message) : base(message) { }
    public ImageFormatException(string message, Exception innerException) : base(message, innerException) { }
}
