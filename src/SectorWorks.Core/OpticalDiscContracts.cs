namespace SectorWorks.Core;

public enum CdTrackMode { Audio, Mode1Cooked, Mode1Raw, Mode2Raw }

public sealed record CdTrackIndex(int Number, int FileSector);

public sealed record CdTrack(int Number, CdTrackMode Mode, string Title, string Performer,
    string SourceName, int SectorSize, int StartLba, int SectorCount, int StoredPregap,
    int Pregap, int Postgap, IReadOnlyList<CdTrackIndex> Indexes, string? VolumeId,
    Capability Playback, bool BigEndianAudio = false)
{
    public bool IsAudio => Mode == CdTrackMode.Audio;
    public string ModeName => Mode switch
    {
        CdTrackMode.Audio => "AUDIO",
        CdTrackMode.Mode1Cooked => "MODE1/2048",
        CdTrackMode.Mode1Raw => "MODE1/2352",
        _ => "MODE2/2352"
    };
    public TimeSpan Duration => TimeSpan.FromSeconds(SectorCount / 75.0);
}

public sealed record CdSession(int Number, IReadOnlyList<CdTrack> Tracks);
public sealed record CdDisc(string Title, string Performer, IReadOnlyList<CdSession> Sessions);

public sealed record CdMetadataTrack(int Number, string Title, string Performer);

public sealed record CdMetadataMatch(string Album, string Artist, int DiscNumber, string Edition,
    bool IsApproximate, IReadOnlyList<CdMetadataTrack> Tracks);

public interface ICdMetadataLookup
{
    Task<IReadOnlyList<CdMetadataMatch>> LookupAsync(CdDisc disc, CancellationToken cancellationToken = default);
}

public interface ICdImageSession
{
    CdDisc Disc { get; }
    Stream OpenAudioTrack(int trackNumber);
    Stream OpenRawTrack(int trackNumber);
}

public interface ICdTrackSource
{
    Task<Stream> OpenAudioTrackAsync(Guid workspaceId, int trackNumber, CancellationToken cancellationToken = default);
    Task<PreviewResult> ReadTrackHexAsync(Guid workspaceId, int trackNumber, long offset, CancellationToken cancellationToken = default);
}

public sealed record CdAudioExportResult(IReadOnlyList<string> Completed, IReadOnlyList<ExtractionFailure> Failures, bool Cancelled)
{
    public IReadOnlyList<string> NotExported { get; init; } = [];
}

public interface ICdAudioExportService
{
    Task<CdAudioExportResult> ExportCdAudioAsync(Guid workspaceId, IReadOnlyList<int> trackNumbers, string destination,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<int, string>? trackTitles = null);
}

/// <summary>Read-only member of a source volume that spans more than one host file.</summary>
public sealed record ImageSetMember(string RelativeName, long Length, Func<Stream> OpenRead);

public interface IImageSetRebuildSource
{
    string CatalogName { get; }
    IReadOnlyList<ImageSetMember> Members { get; }
    Capability QualifyRebuild(string? volumeId);
    IImageRebuildPlan PlanTrack(string volumeId, IReadOnlyList<StagedWriteOperation> operations,
        CancellationToken cancellationToken);
    string MemberName(string volumeId);
}

public interface ICdAudioOutput : IDisposable
{
    /// <summary>Accepts signed 16-bit little-endian stereo frames at 44.1 kHz.</summary>
    void Write(byte[] pcm, int count, CancellationToken cancellationToken);
    void Drain(CancellationToken cancellationToken);
    long PlayedFrames { get; }
}
