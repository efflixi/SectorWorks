using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SectorWorks.Core;

namespace SectorWorks.Services;

/// <summary>On-demand MusicBrainz lookups using the disc layout alone.</summary>
public sealed class MusicBrainzCdMetadataLookup : ICdMetadataLookup
{
    private const long MaxResponseBytes = 4 * 1024 * 1024;
    private const string Endpoint = "https://musicbrainz.org/ws/2/discid/";
    private const string Query = "?fmt=json&cdstubs=no&inc=recordings%2Bartist-credits";
    private static readonly HttpClient PublicClient = new(new SocketsHttpHandler { AllowAutoRedirect = false });
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower };
    private readonly HttpClient client;
    private readonly SemaphoreSlim requests = new(1);
    private readonly Dictionary<string, IReadOnlyList<CdMetadataMatch>> cache = [];
    private long lastRequest;

    public MusicBrainzCdMetadataLookup() : this(PublicClient) { }
    internal MusicBrainzCdMetadataLookup(HttpClient client) => this.client = client;

    public async Task<IReadOnlyList<CdMetadataMatch>> LookupAsync(CdDisc disc, CancellationToken cancellationToken = default)
    {
        Toc toc = Toc.FromDisc(disc);
        string key = toc.Id + string.Concat(toc.Tracks.Select(track => track.IsAudio ? 'A' : 'D'));
        await requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cache.TryGetValue(key, out IReadOnlyList<CdMetadataMatch>? cached)) { return cached; }
            ReleaseList? response = await GetAsync(toc.Id + Query, cancellationToken).ConfigureAwait(false);
            CdMetadataMatch[] matches = response is null
                ? FindMatches(await GetAsync("-" + Query + "&toc=" + toc.QueryValue
                    + (toc.Tracks.Any(track => !track.IsAudio) ? "&media-format=all" : ""), cancellationToken)
                    .ConfigureAwait(false), toc, approximate: true)
                : FindMatches(response, toc, approximate: false);
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Length == 0) { return matches; }

            if (cache.Count >= 32) { cache.Remove(cache.Keys.First()); }
            IReadOnlyList<CdMetadataMatch> result = Array.AsReadOnly(matches);
            cache.Add(key, result);
            return result;
        }
        finally { requests.Release(); }
    }

    private async Task<ReleaseList?> GetAsync(string query, CancellationToken cancellationToken)
    {
        if (lastRequest != 0)
        {
            TimeSpan remaining = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(lastRequest);
            if (remaining > TimeSpan.Zero)
            { await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)), cancellationToken).ConfigureAwait(false); }
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + query);
        request.Headers.UserAgent.ParseAdd($"SectorWorks/{typeof(MusicBrainzCdMetadataLookup).Assembly.GetName().Version} (https://github.com/efflixi/SectorWorks)");
        try
        {
            lastRequest = Stopwatch.GetTimestamp();
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) { return null; }
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            { throw new IOException("MusicBrainz is busy or limiting requests. Try again later."); }
            if (!response.IsSuccessStatusCode)
            { throw new IOException($"MusicBrainz returned HTTP {(int)response.StatusCode}. Try again later."); }
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            { throw new IOException("MusicBrainz returned too much metadata to display safely."); }
            await response.Content.LoadIntoBufferAsync(MaxResponseBytes, timeout.Token).ConfigureAwait(false);
            using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<ReleaseList>(body, Json, timeout.Token).ConfigureAwait(false)
                ?? throw new IOException("MusicBrainz returned an empty metadata response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new IOException("MusicBrainz did not respond in time. Try again later."); }
        catch (HttpRequestException error) when (error.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        { throw new IOException("MusicBrainz returned too much metadata to display safely.", error); }
        catch (HttpRequestException error)
        { throw new IOException("Could not read the response from MusicBrainz. Check your internet connection or try again later.", error); }
        catch (JsonException error)
        { throw new IOException("MusicBrainz returned unreadable metadata.", error); }
    }

    private static CdMetadataMatch[] FindMatches(ReleaseList? response, Toc toc, bool approximate)
    {
        var matches = new List<CdMetadataMatch>();
        foreach (Release release in (response?.Releases ?? []).OfType<Release>())
        {
            string album = Clean(release.Title);
            if (album.Length == 0) { continue; }
            string artist = ArtistName(release.ArtistCredit);
            string edition = string.Join(" · ", new[] { Clean(release.Disambiguation), Clean(release.Date),
                Clean(release.Country), Clean(release.Barcode) }.Where(value => value.Length > 0));
            foreach (Medium medium in (release.Media ?? []).OfType<Medium>())
            {
                if (!approximate && !(medium.Discs ?? []).Any(disc => disc?.Id == toc.Id)) { continue; }
                if (MatchTracks(medium, toc, approximate) is { Count: > 0 } tracks)
                {
                    matches.Add(new(album, artist, medium.Position, edition, approximate, tracks));
                }
            }
        }
        return matches.ToArray();
    }

    private static IReadOnlyList<CdMetadataTrack>? MatchTracks(Medium medium, Toc toc, bool approximate)
    {
        var numbered = new Dictionary<int, (Track Track, bool IsData)>();
        foreach ((Track track, bool isData) in (medium.Tracks ?? []).Select(track => (track, IsData: false))
            .Concat((medium.DataTracks ?? []).Select(track => (track, IsData: true))))
        {
            if (track is null || !int.TryParse(track.Number, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number < 1 || number > toc.Tracks.Count
                || !numbered.TryAdd(number, (track, isData || Clean(track.Title) == "[data track]"))) { return null; }
        }
        if (numbered.Count != toc.Tracks.Count) { return null; }

        var labels = new List<CdMetadataTrack>();
        foreach (CdTrack source in toc.Tracks)
        {
            (Track track, bool isData) = numbered[source.Number];
            if (source.IsAudio && isData || approximate && !source.IsAudio && !isData) { return null; }
            if (!source.IsAudio) { continue; }
            if (approximate)
            {
                int end = source.Number < toc.Tracks.Count ? toc.Offsets[source.Number] : toc.LeadOut;
                double duration = (end - toc.Offsets[source.Number - 1]) * (1000.0 / 75);
                if (track.Length is not int milliseconds || Math.Abs(milliseconds - duration) > 2000) { return null; }
            }
            string title = Clean(track.Title);
            if (title == "[unknown]") { title = ""; }
            string artist = ArtistName(track.ArtistCredit);
            if (title.Length > 0 || artist.Length > 0) { labels.Add(new(source.Number, title, artist)); }
        }
        return labels.AsReadOnly();
    }

    private static string ArtistName(ArtistCredit[]? credits) =>
        Clean(string.Concat((credits ?? []).OfType<ArtistCredit>().Select(credit => Clean(credit.Name) + credit.JoinPhrase)));

    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Take(256).Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();

    private sealed record Toc(string Id, IReadOnlyList<CdTrack> Tracks, int[] Offsets, int LeadOut)
    {
        public string QueryValue => FormattableString.Invariant($"1+{Offsets.Length}+{LeadOut}+")
            + string.Join("+", Offsets.Select(offset => offset.ToString(CultureInfo.InvariantCulture)));

        public static Toc FromDisc(CdDisc disc)
        {
            if (disc.Sessions is not [var session] || session.Tracks.Count is < 1 or > 99 || !session.Tracks[^1].IsAudio)
            { throw new NotSupportedException("MusicBrainz lookup requires a single-session layout that ends with an audio track."); }
            IReadOnlyList<CdTrack> tracks = session.Tracks;
            int[] offsets = new int[tracks.Count];
            const int leadIn = 150;
            long end = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                CdTrack track = tracks[i];
                if (track.Number != i + 1 || track.StartLba < end || track.SectorCount <= 0 || track.Postgap < 0)
                { throw new NotSupportedException("MusicBrainz lookup requires consecutive tracks with valid sector positions."); }
                end = (long)track.StartLba + track.SectorCount + track.Postgap;
                if (end > int.MaxValue - leadIn) { throw new NotSupportedException("The lead-out exceeds the MusicBrainz sector range."); }
                offsets[i] = track.StartLba + leadIn;
            }
            int leadOut = (int)end + leadIn;
            var hex = new StringBuilder(804);
            hex.Append(CultureInfo.InvariantCulture, $"01{tracks.Count:X2}{leadOut:X8}");
            for (int i = 0; i < 99; i++)
            { hex.Append(CultureInfo.InvariantCulture, $"{(i < offsets.Length ? offsets[i] : 0):X8}"); }
            string id = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(hex.ToString())))
                .Replace('+', '.').Replace('/', '_').Replace('=', '-');
            return new(id, tracks, offsets, leadOut);
        }
    }

    private sealed record ReleaseList(Release[]? Releases);
    private sealed record Release(string? Title, ArtistCredit[]? ArtistCredit, string? Disambiguation, string? Date,
        string? Country, string? Barcode, Medium[]? Media);
    private sealed record Medium(int Position, DiscReference[]? Discs, Track[]? Tracks, Track[]? DataTracks);
    private sealed record DiscReference(string? Id);
    private sealed record Track(string? Number, string? Title, int? Length, ArtistCredit[]? ArtistCredit);
    private sealed record ArtistCredit(string? Name, [property: JsonPropertyName("joinphrase")] string? JoinPhrase);
}
