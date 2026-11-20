using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed record OpticalProgram(int Number, CdTrackMode Mode, long Offset, int StoredBytes,
    int StartLba, int Sectors, int StoredPregap, bool BigEndian = false,
    string? AudioRestriction = null, int Session = 1, int UnstoredPregap = 0, IReadOnlyList<CdTrackIndex>? Indexes = null);

internal sealed class ReadOnlyOpticalSession : IImageSession, ICdImageSession
{
    private readonly List<IDisposable> resources;
    private readonly IReadOnlyBlockDevice backing;
    private readonly OpticalProgram[] programs;
    private readonly ParserContext context = new();
    private bool disposed;

    internal ReadOnlyOpticalSession(string path, string format, IReadOnlyBlockDevice catalog,
        IReadOnlyBlockDevice backing, string backingName, OpticalProgram[] programs, string subchannels,
        List<IDisposable> resources, CancellationToken token)
    {
        this.backing = backing;
        this.programs = programs;
        this.resources = resources;
        Device = catalog;
        var tracks = new List<(int Session, CdTrack Track)>();
        var volumes = new List<ImageVolume>();
        var warnings = new List<string>();
        long lastPhysical = 0;
        int lastLba = 0;
        for (int i = 0; i < programs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            OpticalProgram item = programs[i];
            if (item.Number != i + 1 || item.Sectors <= 0 || item.StoredPregap is < 0 or > CueSheet.MaxDiscSectors
                || item.StoredBytes is not (2048 or 2352 or 2448) || (item.StoredBytes == 2048) != (item.Mode == CdTrackMode.Mode1Cooked)
                || item.StartLba < lastLba || (long)item.StartLba + item.Sectors > CueSheet.MaxDiscSectors)
            { throw new ImageFormatException($"{format} contains unsupported numbering or overlapping or out-of-range track addresses."); }
            long physicalStart = item.Offset - (long)item.StoredPregap * item.StoredBytes;
            long length = (long)item.Sectors * item.StoredBytes;
            if (physicalStart < lastPhysical)
            { throw new ImageFormatException($"{format} track data or stored pregap ranges overlap."); }
            Bounds.Check(item.Offset, length, backing.Length);
            lastPhysical = item.Offset + length;
            lastLba = item.StartLba + item.Sectors;
            string? volumeId = null;
            if (item.Mode != CdTrackMode.Audio)
            {
                IReadOnlyBlockDevice data = TrackDevice(item, raw: false);
                resources.Add(data);
                try
                {
                    if (context.Run(() => ImageOpener.OpenOpticalFileSystem(data, context, warnings, token,
                            standaloneCompatibility: true, allowRebuild: false), token) is { } fs)
                    {
                        resources.Add(fs);
                        volumeId = $"track-{item.Number:D2}";
                        volumes.Add(new(volumeId, null, fs));
                    }
                    else { warnings.Add($"Track {item.Number:D2}: no supported optical filesystem."); }
                }
                catch (ImageFormatException error) { warnings.Add($"Track {item.Number:D2}: {error.Message}"); }
            }
            bool audio = item.Mode == CdTrackMode.Audio;
            int visibleSector = item.Mode == CdTrackMode.Mode1Cooked ? CdRawSector.UserBytes : CdRawSector.RawBytes;
            var indexes = new List<CdTrackIndex>();
            int fileStart = item.Offset % item.StoredBytes == 0 ? checked((int)(item.Offset / item.StoredBytes)) : 0;
            if (item.StoredPregap > 0 && fileStart >= item.StoredPregap)
            { indexes.Add(new(0, fileStart - item.StoredPregap)); }
            indexes.Add(new(1, fileStart));
            tracks.Add((item.Session, new CdTrack(item.Number, item.Mode, "", "", backingName, visibleSector, item.StartLba,
                item.Sectors, item.StoredPregap, item.UnstoredPregap, 0, item.Indexes ?? indexes.AsReadOnly(), volumeId,
                new(audio && item.AudioRestriction is null, !audio ? "This is a data track."
                    : item.AudioRestriction ?? "44.1 kHz, 16-bit stereo CD audio."), item.BigEndian)));
        }
        if (tracks.Count == 0) { throw new ImageFormatException($"{format} has no supported track layout."); }
        if (volumes.Count == 0 && tracks.All(item => !item.Track.IsAudio))
        { throw new ImageFormatException(warnings.FirstOrDefault() ?? $"{format} has no supported data filesystem."); }
        Metadata = new ImageMetadata(path, format, resources.OfType<FileBlockDevice>().Sum(file => file.Length),
            lastLba * 2352L, 2352, new Dictionary<string, string>
            {
                ["Sessions"] = tracks[^1].Session.ToString(),
                ["Tracks"] = tracks.Count.ToString(),
                ["Subchannels"] = subchannels,
                ["Access"] = "Read-only tracks and files.",
                ["Raw image hex"] = "Container or descriptor bytes; select a track for program bytes."
            }.AsReadOnly())
        { HexLength = catalog.Length };
        Disc = new("", "", tracks.GroupBy(item => item.Session).Select(group =>
            new CdSession(group.Key, group.Select(item => item.Track).ToArray())).ToArray());
        Volumes = volumes.AsReadOnly();
        Warnings = warnings.AsReadOnly();
    }

    public ImageMetadata Metadata { get; }
    public IReadOnlyBlockDevice Device { get; }
    public IReadOnlyList<PartitionInfo> Partitions => [];
    public IReadOnlyList<ImageVolume> Volumes { get; }
    public IReadOnlyList<string> Warnings { get; }
    public CdDisc Disc { get; }

    public Stream OpenAudioTrack(int trackNumber)
    {
        CdTrack track = Disc.Sessions.SelectMany(session => session.Tracks).Single(item => item.Number == trackNumber);
        if (!track.Playback.Available) { throw new NotSupportedException(track.Playback.Reason); }
        return OpenRawTrack(trackNumber);
    }

    public Stream OpenRawTrack(int trackNumber)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        OpticalProgram item = programs.Single(track => track.Number == trackNumber);
        return new DeviceStream(TrackDevice(item, raw: true), ownsDevice: true);
    }

    private IReadOnlyBlockDevice TrackDevice(OpticalProgram track, bool raw)
    {
        IReadOnlyBlockDevice device = track.StoredBytes == 2448
            ? new SubchannelStrideDevice(backing, track.Offset, track.Sectors)
            : new PartitionBlockDevice(backing, track.Offset, (long)track.Sectors * track.StoredBytes);
        return new CueTrackDevice(device, 0, track.Sectors, track.Mode, raw, ownsDevice: true);
    }

    public void Dispose()
    {
        lock (context.Gate)
        {
            if (disposed) { return; }
            disposed = true;
            for (int i = resources.Count - 1; i >= 0; i--) { resources[i].Dispose(); }
        }
    }

    private sealed class SubchannelStrideDevice(IReadOnlyBlockDevice source, long start, int sectors) : IReadOnlyBlockDevice
    {
        public long Length => (long)sectors * 2352;
        public int LogicalSectorSize => 2352;
        public int? PhysicalSectorSize => 2448;
        public bool IsReadOnly => true;
        public int Read(long offset, Span<byte> buffer)
        {
            Bounds.Check(offset, buffer.Length, Length);
            int within = (int)(offset % 2352);
            int count = Math.Min(buffer.Length, 2352 - within);
            if (count == 0) { return 0; }
            Bounds.ReadExactly(source, start + offset / 2352 * 2448 + within, buffer[..count]);
            return count;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, buffer.Span));
        }

        public void Dispose() { }
    }
}
