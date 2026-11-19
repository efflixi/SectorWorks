using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed class CueImageSession : IImageSession, ICdImageSession, IImageSetRebuildSource
{
    private readonly List<IDisposable> resources = [];
    private readonly List<TrackLayout> layout = [];
    private readonly List<ImageVolume> volumes = [];
    private readonly List<string> warnings = [];
    private readonly string directory;
    private readonly CueSheet sheet;
    private readonly bool hasSidecar;
    private bool disposed;

    private CueImageSession(string fullPath, FileBlockDevice catalog, CueSheet sheet, CancellationToken token)
    {
        this.sheet = sheet;
        resources.Add(catalog);
        Device = catalog;
        directory = Path.GetDirectoryName(fullPath)!;
        CatalogName = Path.GetFileName(fullPath);
        List<ImageSetMember> members = [CreateMember(CatalogName, catalog.Length)];
        hasSidecar = sheet.Sources.Select(source => source.Name).Prepend(CatalogName)
            .Any(name => new[] { ".sub", ".ccd", ".cdt" }.Any(extension =>
                File.Exists(Path.Combine(directory, Path.ChangeExtension(name, extension)))));
        var context = new ParserContext();
        int disc = 0;
        try
        {
            foreach (CueSource source in sheet.Sources)
            {
                token.ThrowIfCancellationRequested();
                if (source.Name.Equals(CatalogName, StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(source.Name).ToLowerInvariant() is not (".bin" or ".img" or ".iso" or ".raw" or ".pcm"))
                { throw new ImageFormatException("CUE FILE must reference a separate .bin, .img, .iso, .raw or .pcm sector file."); }
                if (source.Tracks.Any(t => t.SectorSize != source.Tracks[0].SectorSize))
                { throw new ImageFormatException("Mixed sector widths in one CUE FILE are unsupported; use separate files."); }
                int sectorSize = source.Tracks[0].SectorSize;
                var file = new FileBlockDevice(CuePath.OpenCompanion(directory, source.Name, CatalogName, out string actualName,
                    out bool caseMatchedSidecar), sectorSize);
                resources.Add(file);
                hasSidecar |= caseMatchedSidecar;
                if (file.Length == 0) { throw new ImageFormatException("CUE companion is empty: " + source.Name); }
                if (file.Length % sectorSize != 0)
                { throw new ImageFormatException($"CUE companion {source.Name} does not contain a whole number of {sectorSize}-byte sectors."); }
                if (file.Length / sectorSize > CueSheet.MaxDiscSectors)
                { throw new ImageFormatException("CUE companion exceeds the 120-minute sector limit: " + source.Name); }
                members.Add(CreateMember(source.Name, file.Length, actualName));
                int sectors = checked((int)(file.Length / sectorSize));
                int dataTracksInMember = source.Tracks.Count(t => t.Mode != CdTrackMode.Audio);
                for (int i = 0; i < source.Tracks.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    CueEntry track = source.Tracks[i];
                    if (track.Indexes.Count == 0 || track.Indexes.All(j => j.Number != 1))
                    { throw new ImageFormatException($"Track {track.Number:D2} requires INDEX 01."); }
                    int partitionStart = track.Indexes[0].FileSector;
                    int start = track.Indexes.Single(j => j.Number == 1).FileSector;
                    int end = i + 1 < source.Tracks.Count
                        ? source.Tracks[i + 1].Indexes.FirstOrDefault()?.FileSector ?? -1
                        : sectors;
                    if (partitionStart < 0 || (i == 0 && partitionStart != 0) || start >= end || end > sectors
                        || track.Indexes[^1].FileSector >= end)
                    { throw new ImageFormatException($"Track {track.Number:D2} has indexes outside its sector range in {source.Name}."); }
                    if (track.Pregap is not null && start != partitionStart)
                    { throw new ImageFormatException($"Track {track.Number:D2} defines both a PREGAP and a stored INDEX 00 gap."); }
                    int count = end - start;
                    int lba = checked(disc + start - partitionStart + (track.Pregap ?? 0));
                    disc = checked(lba + count + (track.Postgap ?? 0));
                    if (disc > CueSheet.MaxDiscSectors)
                    { throw new ImageFormatException("CUE disc exceeds the 120-minute sector bound."); }

                    OpticalFileSystem? rebuild = null;
                    string? volumeId = null;
                    string? refusal = null;
                    if (track.Mode != CdTrackMode.Audio)
                    {
                        var data = new CueTrackDevice(file, start, count, track.Mode);
                        int beforeTrack = warnings.Count;
                        try
                        {
                            IReadOnlyFileSystem? fs = context.Run(() => ImageOpener.OpenOpticalFileSystem(data, context, warnings, token,
                                standaloneCompatibility: track.Mode != CdTrackMode.Mode1Cooked), token);
                            if (fs is not null)
                            {
                                resources.Add(fs);
                                volumeId = $"track-{track.Number:D2}";
                                volumes.Add(new(volumeId, null, fs));
                                rebuild = fs as OpticalFileSystem;
                            }
                            else { warnings.Add($"Track {track.Number:D2}: no supported optical filesystem."); }
                        }
                        catch (Exception error) when (error is IOException or NotSupportedException or OverflowException)
                        { warnings.Add($"Track {track.Number:D2}: {error.Message}"); }
                        refusal = warnings.Skip(beforeTrack).FirstOrDefault(w => w.StartsWith("Optical rebuild unavailable", StringComparison.Ordinal)
                            || w.StartsWith("Read-only optical layout", StringComparison.Ordinal));
                    }

                    string? audioRestriction = track.Mode != CdTrackMode.Audio ? "This is a data track."
                        : track.Flags.Contains("4CH") ? "Four-channel CD audio is not supported."
                        : track.Flags.Contains("PRE") ? "CD audio marked for pre-emphasis is not supported."
                        : null;
                    var info = new CdTrack(track.Number, track.Mode, track.Title, track.Performer, source.Name,
                        sectorSize, lba, count, start - partitionStart, track.Pregap ?? 0, track.Postgap ?? 0,
                        track.Indexes.AsReadOnly(), volumeId, new(audioRestriction is null,
                            audioRestriction ?? "44.1 kHz, 16-bit stereo CD audio."), source.BigEndian);
                    bool raw = track.Mode is CdTrackMode.Mode1Raw or CdTrackMode.Mode2Raw;
                    Capability editing = new(false, rebuild switch
                    {
                        { Udf: not null } => "BIN/CUE rebuild does not support UDF in a data track.",
                        { Iso.Boot: not null } => "BIN/CUE rebuild cannot preserve the boot layout of this data track.",
                        _ => "This data track has no supported ISO filesystem layout for rebuild.",
                    });
                    if (!info.IsAudio && !hasSidecar && !sheet.HasOpaqueRemarks)
                    {
                        if (dataTracksInMember != 1 || track.Indexes.Count != 1
                            || !raw && (source.Tracks.Count != 1 || start != 0))
                        { editing = new(false, "Multiple data tracks in a member or extra data indexes are not qualified for rebuild."); }
                        else if (rebuild is { RebuildCapability.Available: true, Udf: null, Iso.Boot: null })
                        {
                            editing = rebuild.RebuildCapability;
                            if (raw)
                            {
                                try
                                {
                                    _ = new OpticalRebuildPlan(rebuild, [], token, (long)count * CdRawSector.UserBytes);
                                    CueRawRebuildPlan.ValidateSourceTrack(file, info, start, token);
                                }
                                catch (Exception error) when (error is IOException or NotSupportedException or OverflowException)
                                { editing = new(false, error.Message); }
                            }
                        }
                        else if (refusal is not null) { editing = new(false, refusal); }
                    }
                    layout.Add(new(info, actualName, start, file.Length, rebuild, editing));
                }
            }
            Members = members.AsReadOnly();
            if (hasSidecar) { warnings.Add("A companion subchannel or disc-metadata sidecar was found. It is not interpreted; rebuilding is disabled."); }
            Disc = new(sheet.Title, sheet.Performer, [new(1, layout.Select(t => t.Track).ToArray())]);
            Metadata = new(fullPath, "BIN/CUE", members.Sum(m => m.Length), disc * 2352L, 2352,
                new Dictionary<string, string>
                {
                    ["Sessions"] = "1",
                    ["Tracks"] = layout.Count.ToString(),
                    ["Sectors including gaps"] = disc.ToString(),
                    ["Source files"] = members.Count.ToString(),
                    ["Subchannels"] = "Not captured or supported",
                    ["Raw image hex"] = "CUE sheet bytes; select a track for its stored program area"
                })
            { HexLength = catalog.Length };
        }
        catch { Dispose(); throw; }
    }

    internal static CueImageSession Open(string path, CancellationToken token)
    {
        string fullPath = Path.GetFullPath(path);
        var catalog = new FileBlockDevice(CuePath.Open(Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath)), 1);
        try
        {
            using var input = new DeviceStream(catalog);
            return new(fullPath, catalog, CueSheet.Parse(input, token), token);
        }
        catch { catalog.Dispose(); throw; }
    }

    public ImageMetadata Metadata { get; }
    public IReadOnlyBlockDevice Device { get; }
    public IReadOnlyList<PartitionInfo> Partitions => [];
    public IReadOnlyList<ImageVolume> Volumes => volumes;
    public IReadOnlyList<string> Warnings => warnings;
    public CdDisc Disc { get; }
    public string CatalogName { get; }
    public IReadOnlyList<ImageSetMember> Members { get; }

    public Stream OpenAudioTrack(int trackNumber)
    {
        TrackLayout track = GetTrack(trackNumber);
        if (!track.Track.Playback.Available) { throw new NotSupportedException(track.Track.Playback.Reason); }
        return OpenRawTrack(track);
    }

    public Stream OpenRawTrack(int trackNumber) => OpenRawTrack(GetTrack(trackNumber));

    private TrackLayout GetTrack(int number)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return layout.SingleOrDefault(t => t.Track.Number == number) ?? throw new ArgumentOutOfRangeException(nameof(number));
    }

    private Stream OpenRawTrack(TrackLayout track)
    {
        var file = new FileBlockDevice(CuePath.Open(directory, track.SourcePath), track.Track.SectorSize);
        if (file.Length != track.MemberLength)
        {
            file.Dispose();
            throw new IOException("CUE track file length changed; reopen the CUE sheet.");
        }
        return new DeviceStream(new CueTrackDevice(file, track.FileStart, track.Track.SectorCount,
            track.Track.Mode, raw: true, ownsDevice: true), ownsDevice: true);
    }

    private ImageSetMember CreateMember(string name, long length, string? actualName = null) => new(name, length, () =>
        new FileStream(CuePath.Open(directory, actualName ?? name), FileAccess.Read));

    public Capability QualifyRebuild(string? volumeId)
    {
        if (hasSidecar)
        {
            const string sidecarReason = "The set has a disc-metadata sidecar (.CCD, .SUB or .CDT) that SectorWorks cannot validate or preserve.";
            return new(false, sheet.HasOpaqueRemarks ? sidecarReason + " " + OpaqueRemarksReason : sidecarReason);
        }
        if (sheet.HasOpaqueRemarks)
        { return new(false, OpaqueRemarksReason); }
        TrackLayout[] results = layout.Where(t => !t.Track.IsAudio && (volumeId is null || t.Track.VolumeId == volumeId)).ToArray();
        if (!results.Any(t => t.Editing.Available))
        {
            return results.FirstOrDefault()?.Editing ?? new(false, volumeId is null
                ? "This disc contains only audio tracks and has no file directory to edit."
                : "The selected volume does not belong to a data track in this disc.");
        }
        return new(true, "Rebuild to a new BIN/CUE folder; track lengths, indexes, gaps and untouched files are preserved.");
    }

    private const string OpaqueRemarksReason = "SectorWorks cannot verify that the CUE sheet's nonstandard REM directives would remain valid after rebuild.";

    private TrackLayout FindTrack(string volumeId) => layout.Single(t => t.Track.VolumeId == volumeId);

    public string MemberName(string volumeId) => FindTrack(volumeId).Track.SourceName;

    public IImageRebuildPlan PlanTrack(string volumeId, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
    {
        Capability capability = QualifyRebuild(volumeId);
        if (!capability.Available) { throw new NotSupportedException(capability.Reason); }
        TrackLayout track = FindTrack(volumeId);
        var cooked = new OpticalRebuildPlan(track.Rebuild!, operations, token, (long)track.Track.SectorCount * CdRawSector.UserBytes);
        return track.Track.Mode == CdTrackMode.Mode1Cooked ? cooked
            : new CueRawRebuildPlan(cooked, Members.Single(m => m.RelativeName == track.Track.SourceName), track.Track, track.FileStart);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        for (int i = resources.Count - 1; i >= 0; i--) { resources[i].Dispose(); }
    }

    private sealed record TrackLayout(CdTrack Track, string SourcePath, int FileStart, long MemberLength,
        OpticalFileSystem? Rebuild, Capability Editing);
}

internal sealed class CueTrackDevice(FileBlockDevice device, int first, int count, CdTrackMode mode, bool raw = false, bool ownsDevice = false) : IReadOnlyBlockDevice
{
    private int StoredSector => mode == CdTrackMode.Mode1Cooked ? 2048 : 2352;
    public int LogicalSectorSize => raw || mode == CdTrackMode.Audio ? StoredSector : 2048;
    public int? PhysicalSectorSize => StoredSector;
    public long Length => (long)count * LogicalSectorSize;

    public int Read(long offset, Span<byte> buffer)
    {
        Bounds.Check(offset, buffer.Length, Length);
        if (raw || mode is CdTrackMode.Audio or CdTrackMode.Mode1Cooked)
        { return device.Read((long)first * StoredSector + offset, buffer); }

        Span<byte> sector = stackalloc byte[2352];
        int written = 0;
        while (written < buffer.Length)
        {
            long address = offset + written;
            Bounds.ReadExactly(device, ((long)first + address / 2048) * 2352, sector);
            bool mode2 = mode == CdTrackMode.Mode2Raw;
            if (CdRawSector.DataHeaderIssue(sector, mode) is { } issue)
            { throw new ImageFormatException($"Track sector {address / 2048}: {issue}"); }
            int within = (int)(address % 2048);
            int length = Math.Min(buffer.Length - written, 2048 - within);
            sector.Slice((mode2 ? 24 : 16) + within, length).CopyTo(buffer[written..]);
            written += length;
        }
        return written;
    }

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(offset, buffer.Span));
    }

    public void Dispose() { if (ownsDevice) { device.Dispose(); } }
}
