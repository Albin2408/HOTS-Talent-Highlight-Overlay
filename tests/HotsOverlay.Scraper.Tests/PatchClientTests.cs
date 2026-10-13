using System.Net;
using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.Scraper.Tests;

public class PatchClientTests : IDisposable
{
    // The shape the real endpoint returns (major patch -> every build seen in that cycle).
    private const string PatchesJson =
        """{"2.55":["2.55.17.98025","2.55.16.97039"],"2.57":["2.57.0.98348","2.57.0.98304","2.57.0.98285"]}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hots-patch-tests-" + Guid.NewGuid().ToString("N"));

    private string CachePath => Path.Combine(_dir, "latest_patch.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public void LatestFrom_PicksTheNewestBuild() =>
        Assert.Equal("2.57.0.98348", PatchClient.LatestFrom(PatchesJson));

    [Theory]
    [InlineData("<!DOCTYPE html><html>Not Found</html>")]
    [InlineData("")]
    [InlineData("{}")]
    public void LatestFrom_ReturnsNullForAnythingUnexpected(string body) =>
        Assert.Null(PatchClient.LatestFrom(body));

    [Fact]
    public async Task GetLatestPatch_FetchesOnceThenUsesTheDailyCache()
    {
        var handler = new FakeHandler(() => Ok(PatchesJson));
        var client = new PatchClient(new HttpClient(handler), CachePath);

        Assert.Equal("2.57.0.98348", await client.GetLatestPatchAsync());
        Assert.Equal("2.57.0.98348", await client.GetLatestPatchAsync());

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetLatestPatch_FallsBackToAnOldCachedAnswerWhenOffline()
    {
        var online = new PatchClient(new HttpClient(new FakeHandler(() => Ok(PatchesJson))), CachePath, ttl: TimeSpan.Zero);
        await online.GetLatestPatchAsync(); // fills the cache; ttl 0 means it is immediately "expired"

        var offline = new PatchClient(
            new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            CachePath, ttl: TimeSpan.Zero);

        Assert.Equal("2.57.0.98348", await offline.GetLatestPatchAsync());
    }

    [Fact]
    public async Task GetLatestPatch_IsNullWhenOfflineWithNothingCached()
    {
        var client = new PatchClient(
            new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.Forbidden))), CachePath);

        Assert.Null(await client.GetLatestPatchAsync());
    }

    [Fact]
    public void SnapshotStore_RoundTripsTheManifest()
    {
        var info = new SnapshotInfo(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), "2.57.0.98348");

        SnapshotStore.Write(_dir, info);

        Assert.Equal(info, SnapshotStore.TryRead(_dir));
        Assert.Null(SnapshotStore.TryRead(Path.Combine(_dir, "missing")));
    }
}
