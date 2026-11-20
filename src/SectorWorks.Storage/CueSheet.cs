using System.Globalization;
using System.Text;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed class CueSheet
{
    internal const int MaxCatalogBytes = 256 * 1024;
    internal const int MaxDiscSectors = 120 * 60 * 75;
    internal string Title { get; private set; } = "";
    internal string Performer { get; private set; } = "";
    internal List<CueSource> Sources { get; } = [];
    // Nonstandard REM directives may describe data that this engine cannot interpret.
    internal bool HasOpaqueRemarks { get; private set; }
    internal bool UsesUtf16 { get; private set; }

    internal static CueSheet Parse(Stream input, CancellationToken token)
    {
        if (input.Length is < 1 or > MaxCatalogBytes)
        { throw new ImageFormatException("CUE sheet exceeds its bounded size or is empty."); }
        byte[] bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) { throw new ImageFormatException("CUE sheet changed during parsing."); }
        string text;
        bool littleEndian = bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe;
        bool bigEndian = bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff;
        bool usesUtf16 = littleEndian || bigEndian;
        try
        {
            text = usesUtf16 ? new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: true).GetString(bytes, 2, bytes.Length - 2)
                : new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException error)
        { throw new ImageFormatException("CUE requires UTF-8 or BOM-marked UTF-16 text. This file contains invalid text bytes.", error); }
        if (text.StartsWith('\uFEFF')) { text = text[1..]; }
        var sheet = new CueSheet { UsesUtf16 = usesUtf16 };
        CueSource? file = null;
        CueEntry? track = null;
        int previousTrack = 0;
        bool sessionDeclared = false;
        int lineNumber = 0;
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            lineNumber++;
            if (line.Length > 4096 || line.Any(c => char.IsControl(c) && c != '\t'))
            { throw new ImageFormatException($"CUE line {lineNumber}: control characters or an overlong line."); }
            List<string> words = Lex(line, lineNumber);
            if (words.Count == 0) { continue; }
            void Require(int count)
            {
                if (words.Count != count) { throw Failure("Wrong number of fields in directive."); }
            }
            string command = words[0].ToUpperInvariant();
            switch (command)
            {
                case "REM":
                    if (words.Count > 1 && words[1].Equals("SESSION", StringComparison.OrdinalIgnoreCase))
                    {
                        if (sessionDeclared || previousTrack != 0 || words.Count < 3
                            || words[2] is not ("1" or "01") || words.Count > 3 && !words[3].StartsWith(';'))
                        { throw Failure("Only one initial REM SESSION 01 marker is supported."); }
                        sessionDeclared = true;
                        break;
                    }
                    if (words.Count > 1 && words[1].Contains("SESSION", StringComparison.OrdinalIgnoreCase))
                    { throw Failure("Multisession CUE sheets are unsupported."); }
                    if (words.Count > 1 && words[1].ToUpperInvariant() is not ("GENRE" or "DATE" or "COMMENT" or "DISCID"))
                    { sheet.HasOpaqueRemarks = true; }
                    break;
                case "FILE":
                    Require(3);
                    if (file is { Tracks.Count: 0 }) { throw Failure("Previous FILE has no tracks."); }
                    if (words[2].ToUpperInvariant() is not ("BINARY" or "MOTOROLA"))
                    { throw Failure("Only BINARY and MOTOROLA sector files are supported."); }
                    string name = CuePath.Normalize(words[1]);
                    if (sheet.Sources.Count >= 99 || sheet.Sources.Any(source => source.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    { throw Failure("Duplicate, case-ambiguous or excessive FILE references."); }
                    file = new CueSource(name, words[2].Equals("MOTOROLA", StringComparison.OrdinalIgnoreCase));
                    sheet.Sources.Add(file);
                    track = null;
                    break;
                case "TRACK":
                    Require(3);
                    if (file is null) { throw Failure("TRACK requires a preceding FILE."); }
                    int number = Number(words[1], 1, 99, allowSingleDigit: true);
                    if (number != previousTrack + 1) { throw Failure("Tracks must be numbered consecutively from 01."); }
                    previousTrack = number;
                    CdTrackMode mode = words[2].ToUpperInvariant() switch
                    {
                        "AUDIO" => CdTrackMode.Audio,
                        "MODE1/2048" => CdTrackMode.Mode1Cooked,
                        "MODE1/2352" => CdTrackMode.Mode1Raw,
                        "MODE2/2352" => CdTrackMode.Mode2Raw,
                        _ => throw Failure("Unsupported track mode: " + words[2]),
                    };
                    if (file.BigEndian && mode != CdTrackMode.Audio)
                    { throw Failure("MOTOROLA is supported only for audio tracks."); }
                    track = new CueEntry(number, mode);
                    file.Tracks.Add(track);
                    break;
                case "INDEX":
                    Require(3);
                    CueEntry indexed = RequireTrack();
                    int index = Number(words[1], 0, 99, allowSingleDigit: true);
                    int sector = Sector(words[2]);
                    if ((indexed.Indexes.Count == 0 && index > 1)
                        || (indexed.Indexes.Count != 0 && (index != indexed.Indexes[^1].Number + 1
                            || sector <= indexed.Indexes[^1].FileSector)) || indexed.Postgap is not null)
                    { throw Failure("Indexes must be ordered and increase within the file."); }
                    indexed.Indexes.Add(new(index, sector));
                    break;
                case "PREGAP":
                    Require(2);
                    CueEntry before = RequireTrack();
                    if (before.Pregap is not null || before.Indexes.Count != 0 || before.Postgap is not null)
                    { throw Failure("PREGAP must occur once before track indexes."); }
                    before.Pregap = Sector(words[1]);
                    break;
                case "POSTGAP":
                    Require(2);
                    CueEntry after = RequireTrack();
                    if (after.Postgap is not null || after.Indexes.All(i => i.Number != 1))
                    { throw Failure("POSTGAP must follow INDEX 01 and occur only once."); }
                    after.Postgap = Sector(words[1]);
                    break;
                case "TITLE":
                case "PERFORMER":
                    Require(2);
                    if (words[1].Length > 256) { throw Failure("Track/disc text exceeds 256 characters."); }
                    if (command == "TITLE")
                    {
                        if (track is null) { sheet.Title = words[1]; }
                        else { track.Title = words[1]; }
                    }
                    else if (track is null) { sheet.Performer = words[1]; }
                    else { track.Performer = words[1]; }
                    break;
                case "FLAGS":
                    CueEntry flagged = RequireTrack();
                    if (words.Count < 2 || flagged.Flags.Count != 0)
                    { throw Failure("Invalid or duplicate FLAGS."); }
                    foreach (string flag in words.Skip(1).Select(w => w.ToUpperInvariant()))
                    {
                        if (flag is not ("DCP" or "SCMS" or "PRE" or "4CH") || !flagged.Flags.Add(flag))
                        { throw Failure("Unknown or repeated track flag: " + flag); }
                    }
                    break;
                case "CATALOG":
                case "SONGWRITER":
                case "ISRC":
                    Require(2);
                    // Bytes are retained verbatim; these directives do not describe sectors.
                    break;
                default:
                    throw Failure("Unsupported CUE directive: " + words[0]);
            }
        }
        if (sheet.Sources.Count == 0 || file is { Tracks.Count: 0 })
        { throw new ImageFormatException("CUE sheet contains no complete FILE/TRACK layout."); }
        return sheet;

        ImageFormatException Failure(string reason) => new($"CUE line {lineNumber}: {reason}");
        CueEntry RequireTrack() => track ?? throw Failure("Directive requires a preceding TRACK.");
        int Number(string value, int min, int max, bool allowSingleDigit = false) =>
            (value.Length == 2 || allowSingleDigit && value.Length == 1)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            && n >= min && n <= max ? n : throw Failure("Invalid numeric field.");
        int Sector(string value)
        {
            string[] parts = value.Split(':');
            if (parts.Length != 3) { throw Failure("Expected MM:SS:FF at 75 sectors per second."); }
            return (Number(parts[0], 0, 99) * 60 + Number(parts[1], 0, 59)) * 75 + Number(parts[2], 0, 74);
        }
    }

    private static List<string> Lex(string line, int number)
    {
        List<string> words = [];
        int p = 0;
        while (p < line.Length)
        {
            while (p < line.Length && char.IsWhiteSpace(line[p])) { p++; }
            if (p == line.Length) { break; }
            if (line[p] == '"')
            {
                int end = line.IndexOf('"', ++p);
                if (end < 0 || (end + 1 != line.Length && !char.IsWhiteSpace(line[end + 1])))
                { throw new ImageFormatException($"CUE line {number}: malformed quoted field."); }
                words.Add(line[p..end]);
                p = end + 1;
            }
            else
            {
                int start = p;
                while (p < line.Length && !char.IsWhiteSpace(line[p]))
                {
                    if (line[p++] == '"') { throw new ImageFormatException($"CUE line {number}: unexpected quote."); }
                }
                words.Add(line[start..p]);
            }
        }
        return words;
    }
}

internal sealed record CueSource(string Name, bool BigEndian)
{
    internal List<CueEntry> Tracks { get; } = [];
}

internal sealed record CueEntry(int Number, CdTrackMode Mode)
{
    internal string Title { get; set; } = "";
    internal string Performer { get; set; } = "";
    internal int? Pregap { get; set; }
    internal int? Postgap { get; set; }
    internal List<CdTrackIndex> Indexes { get; } = [];
    internal HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    internal int SectorSize => Mode == CdTrackMode.Mode1Cooked ? 2048 : 2352;
}
