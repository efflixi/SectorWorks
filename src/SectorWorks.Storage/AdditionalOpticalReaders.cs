using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal static class AdditionalOpticalReaders
{
    internal static IImageSession? TryOpenSet(string path, List<IDisposable> resources,
        CancellationToken token)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string fullPath = Path.GetFullPath(path);
        string? ccd = extension == ".ccd" ? fullPath : extension is ".img" or ".sub" ? FindSibling(fullPath, ".ccd") : null;
        if (ccd is not null) { return OpenCloneCd(ccd, resources, token); }
        if (extension is ".mds" or ".mdf")
        {
            string descriptor = extension == ".mds" ? fullPath : FindSibling(fullPath, ".mds")
                ?? throw new ImageFormatException("An MDF image requires a matching MDS descriptor for its stored track layout.");
            return OpenMds(descriptor, resources, token);
        }
        if (extension == ".sub") { throw new ImageFormatException("A CloneCD SUB file requires its CCD descriptor and IMG payload."); }
        return null;
    }

    private static string? FindSibling(string path, string extension)
    {
        string lower = Path.ChangeExtension(path, extension);
        string upper = Path.ChangeExtension(path, extension.ToUpperInvariant());
        bool hasLower = File.Exists(lower);
        bool hasUpper = !OperatingSystem.IsWindows() && File.Exists(upper);
        if (hasLower && hasUpper) { throw new ImageFormatException("Optical set has ambiguous same-stem companion filenames."); }
        return hasLower ? lower : hasUpper ? upper : null;
    }

    private static FileBlockDevice OpenMember(string path, List<IDisposable> resources)
    {
        var file = new FileBlockDevice(CuePath.Open(Path.GetDirectoryName(path)!, Path.GetFileName(path)), 1);
        resources.Add(file);
        return file;
    }

    private static IImageSession OpenCloneCd(string path, List<IDisposable> resources, CancellationToken token)
    {
        FileBlockDevice descriptor = OpenMember(path, resources);
        if (descriptor.Length is < 1 or > 512 * 1024)
        { throw new ImageFormatException("CloneCD descriptor size is outside the supported range."); }
        string content;
        try { content = new UTF8Encoding(false, true).GetString(Bounds.Read(descriptor, 0, (int)descriptor.Length)); }
        catch (DecoderFallbackException) { throw new ImageFormatException("CloneCD descriptor is not valid UTF-8 or ASCII text."); }
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? fields = null;
        foreach (string raw in content.TrimStart('\ufeff').Split('\n'))
        {
            token.ThrowIfCancellationRequested();
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') { continue; }
            if (line.Length > 1024 || line.Any(char.IsControl)) { throw InvalidCcd(); }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                fields = new(StringComparer.OrdinalIgnoreCase);
                if (sections.Count >= 256 || !sections.TryAdd(line[1..^1], fields)) { throw InvalidCcd(); }
            }
            else
            {
                int separator = line.IndexOf('=');
                if (fields is null || separator <= 0 || !fields.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim()))
                { throw InvalidCcd(); }
            }
        }

        if (!sections.TryGetValue("CloneCD", out var header) || ReadCcdInt(header, "Version") is not (2 or 3)
            || !sections.TryGetValue("Disc", out var disc) || ReadCcdInt(disc, "Sessions") != 1
            || ReadCcdInt(disc, "DataTracksScrambled") != 0)
        { throw new ImageFormatException("CloneCD requires an unscrambled single-session version 2 or 3 descriptor."); }

        string imagePath = FindSibling(path, ".img") ?? throw new ImageFormatException("CloneCD is missing its same-stem IMG payload.");
        FileBlockDevice image = OpenMember(imagePath, resources);
        if (image.Length % 2352 != 0 || image.Length / 2352 is < 1 or > CueSheet.MaxDiscSectors)
        { throw new ImageFormatException("CloneCD IMG length is not a supported number of 2352-byte sectors."); }
        int sectors = (int)(image.Length / 2352);
        string subchannels = "No SUB member.";
        if (FindSibling(path, ".sub") is { } subPath)
        {
            if (OpenMember(subPath, resources).Length != sectors * 96L)
            { throw new ImageFormatException("CloneCD SUB has a different sector count from the IMG payload."); }
            subchannels = "Separate 96-byte SUB records; not used for the program stream.";
        }
        var controls = new Dictionary<int, (int Lba, int Control)>();
        foreach (var entry in sections.Where(item => item.Key.StartsWith("Entry ", StringComparison.OrdinalIgnoreCase)).Select(item => item.Value))
        {
            int point = ReadCcdInt(entry, "Point");
            if (ReadCcdInt(entry, "Session") != 1) { throw InvalidCcd(); }
            if (point is >= 1 and <= 99 && !controls.TryAdd(point, (ReadCcdInt(entry, "PLBA"), ReadCcdInt(entry, "Control"))))
            { throw InvalidCcd(); }
        }

        var programs = new List<OpticalProgram>();
        int tracks = sections.Keys.Count(key => key.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase));
        for (int number = 1; number <= tracks; number++)
        {
            if (!sections.TryGetValue($"TRACK {number}", out var track)) { throw InvalidCcd(); }
            CdTrackMode mode = ReadCcdInt(track, "MODE") switch
            {
                0 => CdTrackMode.Audio,
                1 => CdTrackMode.Mode1Raw,
                2 => CdTrackMode.Mode2Raw,
                _ => throw new ImageFormatException("CloneCD contains an unsupported track mode.")
            };
            int start = ReadCcdInt(track, "INDEX 1");
            int pregapStart = track.ContainsKey("INDEX 0") ? ReadCcdInt(track, "INDEX 0") : start;
            int end = !sections.TryGetValue($"TRACK {number + 1}", out var next) ? sectors
                : ReadCcdInt(next, next.ContainsKey("INDEX 0") ? "INDEX 0" : "INDEX 1");
            if (start < 0 || end > sectors || end <= start || pregapStart < 0 || pregapStart > start
                || !controls.TryGetValue(number, out var control) || control.Lba != start
                || ((control.Control & 4) != 0) != (mode != CdTrackMode.Audio))
            { throw InvalidCcd(); }
            var indexes = new SortedDictionary<int, int>();
            foreach (string key in track.Keys.Where(key => key.StartsWith("INDEX ", StringComparison.OrdinalIgnoreCase)))
            {
                if (!int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    || index is < 0 or > 99 || !indexes.TryAdd(index, ReadCcdInt(track, key))) { throw InvalidCcd(); }
            }
            int previous = pregapStart - 1;
            foreach (int value in indexes.Values)
            {
                if (value <= previous || value >= end) { throw InvalidCcd(); }
                previous = value;
            }
            programs.Add(new(number, mode, start * 2352L, 2352, start, end - start, start - pregapStart,
                AudioRestriction: AudioControlRestriction(control.Control),
                Indexes: indexes.Select(pair => new CdTrackIndex(pair.Key, pair.Value)).ToArray()));
        }
        if (controls.Count != programs.Count) { throw InvalidCcd(); }
        return new ReadOnlyOpticalSession(path, "CloneCD", descriptor, image, Path.GetFileName(imagePath),
            programs.ToArray(), subchannels, resources, token);
    }

    private static int ReadCcdInt(Dictionary<string, string> fields, string key)
    {
        if (fields.TryGetValue(key, out var value))
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int hex)) { return hex; }
            }
            else if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)) { return number; }
        }
        throw InvalidCcd();
    }

    private static ImageFormatException InvalidCcd() => new("CloneCD descriptor fields, TOC entries or track ranges disagree.");

    private static IImageSession OpenMds(string path, List<IDisposable> resources, CancellationToken token)
    {
        FileBlockDevice descriptor = OpenMember(path, resources);
        if (descriptor.Length is < 88 or > 1024 * 1024) { throw InvalidMds(); }
        byte[] header = Bounds.Read(descriptor, 0, 88);
        if (!header.AsSpan(0, 16).SequenceEqual("MEDIA DESCRIPTOR"u8) || header[16] != 1
            || U16(header, 18) > 2 || U16(header, 20) is < 1 or > 8)
        { throw new ImageFormatException("MDS requires a supported version 1 CD descriptor. DVD and extended media layouts are unsupported."); }
        string imagePath = FindSibling(path, ".mdf") ?? throw new ImageFormatException("MDS is missing its same-stem MDF payload.");
        FileBlockDevice image = OpenMember(imagePath, resources);
        int sessionCount = U16(header, 20);
        long sessions = U32(header, 80);
        if (sessions < 88) { throw InvalidMds(); }
        var programs = new List<OpticalProgram>();
        bool hasSubchannels = false;
        for (int session = 1; session <= sessionCount; session++)
        {
            byte[] info = Bounds.Read(descriptor, sessions + (session - 1) * 24, 24);
            if (U16(info, 8) != session || info[10] is < 1 or > 103 || info[11] > info[10]
                || U16(info, 12) != programs.Count + 1 || U16(info, 14) < U16(info, 12) || U16(info, 14) > 99)
            { throw InvalidMds(); }
            long tracks = U32(info, 20);
            if (tracks < sessions + sessionCount * 24) { throw InvalidMds(); }
            for (int i = 0; i < info[10]; i++)
            {
                token.ThrowIfCancellationRequested();
                byte[] record = Bounds.Read(descriptor, tracks + i * 80L, 80);
                int number = record[4];
                if (number is < 1 or > 99) { continue; }
                if (number != programs.Count + 1 || number > U16(info, 14) || U32(record, 48) != 1) { throw InvalidMds(); }
                int width = U16(record, 16);
                if ((record[1], width) is not ((0, 2352) or (8, 2448) or (0, 2048))) { throw InvalidMds(); }
                CdTrackMode mode = (record[0], width) switch
                {
                    (0xa9 or 0xe9, 2352 or 2448) => CdTrackMode.Audio,
                    (0xaa or 0xea, 2352 or 2448) => CdTrackMode.Mode1Raw,
                    (0xaa or 0xea, 2048) => CdTrackMode.Mode1Cooked,
                    (0xab or 0xac or 0xeb or 0xec, 2352 or 2448) => CdTrackMode.Mode2Raw,
                    _ => throw new ImageFormatException("MDS contains an unsupported CD track mode or sector width.")
                };
                CheckMdfName(descriptor, U32(record, 52), Path.GetFileName(imagePath));
                byte[] extra = Bounds.Read(descriptor, U32(record, 12), 8);
                int start = checked((int)U32(record, 36));
                int declaredPregap = checked((int)U32(extra, 0));
                int count = checked((int)U32(extra, 4));
                long offset = checked((long)U64(record, 40));
                long previousEnd = programs.Count == 0 ? 0 : programs[^1].Offset + (long)programs[^1].Sectors * programs[^1].StoredBytes;
                long physicalGap = offset - previousEnd;
                int storedPregap = physicalGap == declaredPregap * (long)width ? declaredPregap : 0;
                long logicalGap = programs.Count == 0 ? 0 : (long)start - programs[^1].StartLba - programs[^1].Sectors;
                if ((physicalGap != 0 && storedPregap == 0) || declaredPregap > CueSheet.MaxDiscSectors
                    || (session == 1 && number != 1 && logicalGap != declaredPregap))
                { throw new ImageFormatException("MDS stored track offsets disagree with the descriptor's pregap layout."); }
                hasSubchannels |= width == 2448;
                programs.Add(new(number, mode, offset, width, start, count, storedPregap,
                    AudioRestriction: AudioControlRestriction(record[2] & 15), Session: session,
                    UnstoredPregap: declaredPregap - storedPregap));
            }
            if (programs.Count != U16(info, 14)) { throw InvalidMds(); }
        }
        OpticalProgram last = programs[^1];
        if (last.Offset + (long)last.Sectors * last.StoredBytes != image.Length) { throw InvalidMds(); }
        return new ReadOnlyOpticalSession(path, "MDS/MDF", descriptor, image, Path.GetFileName(imagePath),
            programs.ToArray(), hasSubchannels ? "Interleaved records omitted from the program stream." : "No interleaved subchannels.",
            resources, token);
    }

    private static void CheckMdfName(IReadOnlyBlockDevice source, long offset, string basename)
    {
        if (offset < 88) { throw InvalidMds(); }
        byte[] footer = Bounds.Read(source, offset, 16);
        long nameOffset = U32(footer, 0);
        uint wide = U32(footer, 4);
        if (nameOffset < 88 || nameOffset >= source.Length || wide > 1) { throw InvalidMds(); }
        byte[] data = Bounds.Read(source, nameOffset, (int)Math.Min(512, source.Length - nameOffset));
        int stride = wide == 1 ? 2 : 1;
        int end = 0;
        while (end + stride <= data.Length && (data[end] != 0 || stride == 2 && data[end + 1] != 0)) { end += stride; }
        if (end + stride > data.Length || end == 0) { throw InvalidMds(); }
        string name = (wide == 1 ? Encoding.Unicode : Encoding.Latin1).GetString(data, 0, end);
        if (!name.Equals("*.mdf", StringComparison.OrdinalIgnoreCase) && !name.Equals(basename, StringComparison.OrdinalIgnoreCase))
        { throw new ImageFormatException("MDS references a different payload. This reader accepts one same-stem MDF member."); }
    }

    private static ImageFormatException InvalidMds() => new("MDS session, track or companion fields exceed the supported descriptor and payload bounds.");

    internal static IImageSession? TryOpenNero(string path, IReadOnlyBlockDevice source, List<IDisposable> resources,
        CancellationToken token)
    {
        if (source.Length < 12) { return null; }
        byte[] footer = Bounds.Read(source, source.Length - 12, 12);
        bool newer = footer.AsSpan(0, 4).SequenceEqual("NER5"u8);
        if (!newer && !footer.AsSpan(4, 4).SequenceEqual("NERO"u8)) { return null; }
        long offset = checked((long)(newer ? Be64(footer, 4) : Be32(footer, 8)));
        long footerStart = source.Length - (newer ? 12 : 8);
        if (offset < 0 || footerStart - offset is < 8 or > 1024 * 1024) { throw InvalidNero(); }
        long dataEnd = offset;
        byte[]? layout = null;
        byte[]? cues = null;
        int sessionTracks = 0;
        bool ended = false;
        int widthOfRecord = 0;
        while (offset < footerStart)
        {
            token.ThrowIfCancellationRequested();
            byte[] chunk = Bounds.Read(source, offset, 8);
            uint length = Be32(chunk, 4);
            string id = Encoding.ASCII.GetString(chunk, 0, 4);
            offset += 8;
            if (length > footerStart - offset) { throw InvalidNero(); }
            switch (id)
            {
                case "DAOI" or "DAOX" when layout is null:
                    if (length > 22 + 99 * 42) { throw InvalidNero(); }
                    layout = Bounds.Read(source, offset, (int)length);
                    widthOfRecord = id == "DAOX" ? 42 : 30;
                    break;
                case "CUES" or "CUEX" when cues is null && id == (newer ? "CUEX" : "CUES"):
                    if (length is < 8 or > 32768 || length % 8 != 0) { throw InvalidNero(); }
                    cues = Bounds.Read(source, offset, (int)length);
                    break;
                case "SINF" when sessionTracks == 0 && length == 4:
                    uint count = Be32(Bounds.Read(source, offset, 4), 0);
                    if (count is < 1 or > 99) { throw InvalidNero(); }
                    sessionTracks = (int)count;
                    break;
                case "END!" when length == 0 && offset == footerStart:
                    ended = true;
                    break;
                case "MTYP" or "CDTX" or "DINF" or "TOCT" or "RELO":
                    break;
                default:
                    throw new ImageFormatException($"NRG chunk {id.Trim()} is unsupported. The reader accepts one CD disc-at-once layout.");
            }
            offset += length;
        }
        if (!ended || layout is null || cues is null || sessionTracks == 0 || layout.Length != 22 + sessionTracks * widthOfRecord
            || layout[20] != 1 || layout[21] != sessionTracks) { throw InvalidNero(); }
        var programs = new List<OpticalProgram>();
        for (int i = 0; i < sessionTracks; i++)
        {
            int entry = 22 + i * widthOfRecord;
            int stored = BinaryPrimitives.ReadUInt16BigEndian(layout.AsSpan(entry + 12));
            ushort format = U16(layout, entry + 14);
            CdTrackMode mode = (format, stored) switch
            {
                (0, 2048) => CdTrackMode.Mode1Cooked,
                (5, 2352) or (15, 2448) => CdTrackMode.Mode1Raw,
                (6, 2352) or (17, 2448) => CdTrackMode.Mode2Raw,
                (7 or 8, 2352) or (16, 2448) => CdTrackMode.Audio,
                _ => throw new ImageFormatException("NRG contains an unsupported CD track mode or sector width.")
            };
            long index0 = ReadNeroOffset(0), index1 = ReadNeroOffset(1), end = ReadNeroOffset(2);
            if (index0 > index1 || end <= index1 || end > dataEnd
                || (index1 - index0) % stored != 0 || (end - index1) % stored != 0) { throw InvalidNero(); }
            int track = i + 1;
            int bcdTrack = (track / 10 << 4) | track % 10;
            int cue = -1;
            for (int j = 0; j < cues.Length; j += 8)
            {
                if (cues[j + 1] != bcdTrack || cues[j + 2] != 1) { continue; }
                if (cue >= 0) { throw InvalidNero(); }
                cue = j;
            }
            if (cue < 0 || ((cues[cue] >> 4 & 4) != 0) != (mode != CdTrackMode.Audio)) { throw InvalidNero(); }
            int lba = newer ? BinaryPrimitives.ReadInt32BigEndian(cues.AsSpan(cue + 4))
                : (Bcd(cues[cue + 5]) * 60 + Bcd(cues[cue + 6])) * 75 + Bcd(cues[cue + 7]) - 150;
            programs.Add(new(track, mode, index1, stored, lba, checked((int)((end - index1) / stored)),
                checked((int)((index1 - index0) / stored)),
                AudioRestriction: AudioControlRestriction(cues[cue] >> 4)));

            long ReadNeroOffset(int index) => widthOfRecord == 42 ? checked((long)Be64(layout, entry + 18 + index * 8))
                : Be32(layout, entry + 18 + index * 4);
        }
        return new ReadOnlyOpticalSession(path, "NRG", source, source, Path.GetFileName(path), programs.ToArray(),
            programs.Any(track => track.StoredBytes == 2448) ? "Interleaved records omitted from the program stream." : "No interleaved subchannels.",
            resources, token);
    }

    private static string? AudioControlRestriction(int control) => (control & 8) != 0
        ? "Four-channel CD audio is not supported." : (control & 1) != 0 ? "CD audio marked for pre-emphasis is not supported." : null;
    private static int Bcd(byte value)
    {
        if ((value >> 4) > 9 || (value & 15) > 9) { throw InvalidNero(); }
        return (value >> 4) * 10 + (value & 15);
    }
    private static uint Be32(byte[] value, int offset) => BinaryPrimitives.ReadUInt32BigEndian(value.AsSpan(offset));
    private static ulong Be64(byte[] value, int offset) => BinaryPrimitives.ReadUInt64BigEndian(value.AsSpan(offset));
    private static ImageFormatException InvalidNero() => new("NRG footer, chunk table or CD track addresses are inconsistent.");
}
