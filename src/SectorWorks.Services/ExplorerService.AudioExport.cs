using System.Globalization;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.Services;

public sealed partial class ExplorerService
{
    public Task<CdAudioExportResult> ExportCdAudioAsync(Guid workspaceId, IReadOnlyList<int> trackNumbers, string destination,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<int, string>? trackTitles = null)
    {
        int[] selected = trackNumbers.Distinct().Order().ToArray();
        return RunAsync(workspaceId, "ExportCdAudio", lease =>
        {
            if (lease.Image is not ICdImageSession disc)
            { throw new NotSupportedException("This image has no CD audio tracks."); }
            if (selected.Length == 0)
            { throw new ArgumentException("Select at least one audio track.", nameof(trackNumbers)); }

            var tracks = disc.Disc.Sessions.SelectMany(session => session.Tracks).ToDictionary(track => track.Number);
            foreach (int number in selected)
            {
                if (!tracks.TryGetValue(number, out CdTrack? track) || !track.IsAudio)
                { throw new NotSupportedException($"Track {number:D2} is not an audio track in this image."); }
                if (!track.Playback.Available)
                { throw new NotSupportedException($"Track {number:D2}: {track.Playback.Reason}"); }
            }

            using var target = new SafeExtractionTarget(destination);
            var completed = new List<string>();
            var failures = new List<ExtractionFailure>();
            long bytes = 0;
            var files = selected.Select(number => (Number: number, Name: WaveName(number, trackTitles?.GetValueOrDefault(number)))).ToArray();
            foreach ((int number, string name) in files)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new("Exporting CD audio", name, bytes, completed.Count));
                    cancellationToken.ThrowIfCancellationRequested();
                    using Stream input = disc.OpenAudioTrack(number);
                    using ExtractionFile output = target.CreateAtomicFile(name);
                    long length = WriteCdWave(tracks[number], input, output.Stream, cancellationToken);
                    output.Complete(cancellationToken);
                    completed.Add(name);
                    bytes += length;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new CdAudioExportResult(completed, failures, Cancelled: true)
                    {
                        NotExported = files.Skip(completed.Count + failures.Count).Select(file => file.Name).ToArray()
                    };
                }
                catch (Exception error) when (IsExtractionFailure(error))
                {
                    failures.Add(new(name, error.Message));
                    log.Write("AudioExportFailure", name, error.Message, "Error");
                }
            }
            return new CdAudioExportResult(completed, failures, Cancelled: false);
        }, cancellationToken);
    }

    private static string WaveName(int number, string? title)
    {
        string prefix = FormattableString.Invariant($"Track {number:D2}");
        if (string.IsNullOrWhiteSpace(title)) { return prefix + ".wav"; }

        // Sixty scalars fit a 255-byte filename even with four-byte UTF-8
        // characters, the track-number prefix and the extension.
        var safe = new StringBuilder();
        foreach (Rune rune in title.EnumerateRunes().Take(60))
        {
            bool replace = Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                || rune.Value is '/' or '\\' or '<' or '>' or ':' or '"' or '|' or '?' or '*';
            safe.Append(replace ? " " : rune.ToString());
        }
        string label = string.Join(" ", safe.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        return label.Length == 0 ? prefix + ".wav" : $"{prefix} - {label}.wav";
    }

    private static long WriteCdWave(CdTrack track, Stream input, Stream output, CancellationToken token)
    {
        const int frameBytes = CdAudioPlayback.BytesPerFrame;
        const int sectorBytes = CdAudioPlayback.FramesPerSector * frameBytes;
        long dataBytes = checked(track.SectorCount * (long)sectorBytes);
        if (track.SectorSize != sectorBytes || dataBytes <= 0 || dataBytes > uint.MaxValue - 36 || input.Length != dataBytes)
        { throw new InvalidDataException("CD audio length does not match the expected WAV sample data."); }

        using (var header = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
        {
            header.Write("RIFF"u8);
            header.Write((uint)(36 + dataBytes));
            header.Write("WAVEfmt "u8);
            header.Write(16u);
            header.Write((ushort)1); // Integer PCM
            header.Write((ushort)2); // Stereo
            header.Write((uint)CdAudioPlayback.SampleRate);
            header.Write((uint)(CdAudioPlayback.SampleRate * frameBytes));
            header.Write((ushort)frameBytes);
            header.Write((ushort)16);
            header.Write("data"u8);
            header.Write((uint)dataBytes);
        }

        var buffer = new byte[65536];
        long remaining = dataBytes;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(remaining, buffer.Length);
            input.ReadExactly(buffer.AsSpan(0, count));
            if (track.BigEndianAudio)
            {
                for (int i = 0; i < count; i += 2)
                { (buffer[i], buffer[i + 1]) = (buffer[i + 1], buffer[i]); }
            }
            output.Write(buffer, 0, count);
            remaining -= count;
        }
        return 44 + dataBytes;
    }
}
