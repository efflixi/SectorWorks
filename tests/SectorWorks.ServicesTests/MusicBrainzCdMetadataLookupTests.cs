using System.Net;
using SectorWorks.Core;
using SectorWorks.Services;

namespace SectorWorks.ServicesTests;

public sealed class MusicBrainzCdMetadataLookupTests
{
    // Public MusicBrainz TOC for Age of Empires II: The Age of Kings.
    // https://musicbrainz.org/release/4efabf72-de25-4800-bd13-229753a4b2e3
    private const string GameDiscId = "0QtivACewxmkx_TJSvhLQXj_ZdY-";

    [Fact]
    public async Task ExactDataFirstLookupUsesThePublishedIdAndOnlyTheMatchingMedium()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Json($$"""
            {"releases":[{"title":"Game album","media":[
              {"position":1,"discs":[{"id":"another-pressing"}],"tracks":[{"number":"1","title":"Wrong disc"},{"number":"2","title":"Wrong song"}]},
              {"position":2,"discs":[{"id":"{{GameDiscId}}"}],"tracks":[
                {"number":"2","title":"Soundtrack","artist-credit":[{"name":"Artist A","joinphrase":" & "},{"name":"Artist B"}]}
              ],"data-tracks":[{"number":"1","title":"[data track]"}]}
            ]}]}
            """)));
        using var http = new HttpClient(handler);
        var lookup = new MusicBrainzCdMetadataLookup(http);
        CdDisc disc = GameDisc();

        CdMetadataMatch match = Assert.Single(await lookup.LookupAsync(disc));
        Assert.False(match.IsApproximate);
        Assert.Equal(2, match.DiscNumber);
        Assert.Equal(new CdMetadataTrack(2, "Soundtrack", "Artist A & Artist B"), Assert.Single(match.Tracks));
        Assert.Equal(match, Assert.Single(await lookup.LookupAsync(disc)));
        Uri uri = Assert.Single(handler.Uris);
        Assert.Equal("/ws/2/discid/" + GameDiscId, uri.AbsolutePath);
        Assert.StartsWith("https://musicbrainz.org/", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("private", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactMissUsesTocFallbackAndRejectsUnsafeTrackAssignments()
    {
        using var handler = new StubHandler((uri, _) => Task.FromResult(uri.AbsolutePath.EndsWith("/-", StringComparison.Ordinal)
            ? Json("""
                {"releases":[
                  {"title":"Similar game","media":[{"position":1,"tracks":[
                    {"number":"1","title":"[data track]"},{"number":"2","title":"Music","length":1813626}]}]},
                  {"title":"Wrong duration","media":[{"position":1,"tracks":[
                    {"number":"1","title":"[data track]"},{"number":"2","title":"Music","length":1234}]}]},
                  {"title":"Audio-only reissue","media":[{"position":1,"tracks":[
                    {"number":"1","title":"Another song"},{"number":"2","title":"Music","length":1813626}]}]}
                ]}
                """)
            : new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var http = new HttpClient(handler);
        var lookup = new MusicBrainzCdMetadataLookup(http);

        CdMetadataMatch match = Assert.Single(await lookup.LookupAsync(GameDisc()));

        Assert.Equal("Similar game", match.Album);
        Assert.True(match.IsApproximate);
        Assert.Equal(2, Assert.Single(match.Tracks).Number);
        Assert.Equal(2, handler.Uris.Count);
        Assert.Contains("toc=1+2+304028+150+168006", handler.Uris[1].Query, StringComparison.Ordinal);
        Assert.True(handler.Times[1] - handler.Times[0] >= TimeSpan.FromMilliseconds(990));
    }

    [Fact]
    public async Task PublishedAudioTocUsesExactIdAndReportsRateLimitingWithoutFallback()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler);
        var lookup = new MusicBrainzCdMetadataLookup(http);
        int[] offsets = [0, 15213, 32164, 46442, 63264, 80339, 95312];
        CdDisc disc = new("", "", [new(1, offsets.SkipLast(1).Select((start, index) =>
            Track(index + 1, true, start, offsets[index + 1] - start)).ToArray())]);

        IOException error = await Assert.ThrowsAsync<IOException>(() => lookup.LookupAsync(disc));

        Assert.Contains("limiting requests", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("/49HHV7Eb8UKF3aQiNmu1GR8vKTY-", Assert.Single(handler.Uris).AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingAnInFlightLookupSendsNoFurtherRequests()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Json("{}");
        });
        using var http = new HttpClient(handler);
        using var stop = new CancellationTokenSource();
        var lookup = new MusicBrainzCdMetadataLookup(http);
        Task request = lookup.LookupAsync(GameDisc(), stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Single(handler.Uris);
    }

    private static CdDisc GameDisc() => new("", "", [new(1,
        [Track(1, false, 0, 167856), Track(2, true, 167856, 136022)])]);

    private static CdTrack Track(int number, bool audio, int start, int count) =>
        new(number, audio ? CdTrackMode.Audio : CdTrackMode.Mode1Raw, "", "", "private.bin",
            2352, start, count, 0, 0, 0, [new(1, start)], null, new(audio, ""));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = [];
        public List<DateTimeOffset> Times { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Assert.StartsWith("SectorWorks/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            Uris.Add(uri);
            Times.Add(DateTimeOffset.UtcNow);
            return respond(uri, cancellationToken);
        }
    }
}
