using System.Net;
using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.Scraper.Tests;

public class BuildCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hots-cache-tests-" + Guid.NewGuid().ToString("N"));
    private readonly HeroSummary _anduin = new("anduin", "Anduin");

    public BuildCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Dir(string name) => Path.Combine(_root, name);

    private static HeroBuildSet SampleSet(string slug = "anduin", string name = "Anduin") => new()
    {
        HeroName = name,
        HeroSlug = slug,
        ScrapedAtUtc = DateTimeOffset.UtcNow,
        Builds = new[]
        {
            new HeroBuild
            {
                Name = "Lightwell Build",
                Role = "Recommended",
                Tiers = new[] { new TalentTier { Level = 1, OptionCount = 3, PickIndex = 2, PickName = "Lightwell" } },
            },
        },
    };

    private static Task<HeroBuildSet> Forbidden(HeroSummary _, CancellationToken __) =>
        throw new HttpRequestException("Response status code does not indicate success: 403 (Forbidden).", null, HttpStatusCode.Forbidden);

    private static Task<IReadOnlyList<HeroSummary>> ForbiddenIndex(CancellationToken _) =>
        throw new HttpRequestException("403 (Forbidden)", null, HttpStatusCode.Forbidden);

    [Fact]
    public void SaveThenTryLoad_RoundTripsABuildSet()
    {
        var cache = new BuildCache(Dir("a"));

        cache.Save(SampleSet());
        var loaded = cache.TryLoad("anduin");

        Assert.NotNull(loaded);
        Assert.Equal("Lightwell Build", loaded!.Builds[0].Name);
        Assert.Equal("Lightwell", loaded.Builds[0].Tiers[0].PickName);
        Assert.Equal(2, loaded.Builds[0].Tiers[0].PickIndex);
    }

    [Fact]
    public async Task FetchBlocked_FallsBackToSampleBuildsAndSaysSo()
    {
        var seed = new BuildCache(Dir("seed"));
        seed.Save(SampleSet());
        var cache = new BuildCache(Dir("live"), seedDir: Dir("seed"), fetchBuilds: Forbidden);

        var set = await cache.GetOrFetchAsync(_anduin);

        Assert.Equal("Lightwell", set.Builds[0].Tiers[0].PickName);
        Assert.Contains("403", cache.DataSourceNotice);
        Assert.Contains("bundled", cache.DataSourceNotice);
    }

    [Fact]
    public async Task FetchBlocked_PrefersPreviouslySavedBuildsOverSamples()
    {
        var seed = new BuildCache(Dir("seed"));
        seed.Save(SampleSet());
        var live = new BuildCache(Dir("live"));
        var stale = SampleSet();
        live.Save(new HeroBuildSet
        {
            HeroName = stale.HeroName, HeroSlug = stale.HeroSlug, Builds = stale.Builds,
            ScrapedAtUtc = DateTimeOffset.UtcNow.AddDays(-30), // expired, but still better than samples
        });
        var cache = new BuildCache(Dir("live"), seedDir: Dir("seed"), fetchBuilds: Forbidden);

        await cache.GetOrFetchAsync(_anduin);

        Assert.Contains("previously saved", cache.DataSourceNotice);
    }

    [Fact]
    public async Task FetchBlocked_WithNothingToFallBackOn_ThrowsAClearError()
    {
        var cache = new BuildCache(Dir("live"), seedDir: Dir("empty-seed"), fetchBuilds: Forbidden);

        var ex = await Assert.ThrowsAsync<BuildDataUnavailableException>(() => cache.GetOrFetchAsync(_anduin));

        Assert.Contains("Anduin", ex.Message);
    }

    [Fact]
    public async Task SuccessfulFetch_IsCachedAndClearsTheDataSourceNotice()
    {
        int calls = 0;
        var cache = new BuildCache(Dir("live"), fetchBuilds: (_, _) => { calls++; return Task.FromResult(SampleSet()); });

        await cache.GetOrFetchAsync(_anduin);
        await cache.GetOrFetchAsync(_anduin);

        Assert.Equal(1, calls);
        Assert.Null(cache.DataSourceNotice);
    }

    [Fact]
    public async Task HeroIndexBlocked_FallsBackToSampleRoster()
    {
        var seed = new BuildCache(Dir("seed"));
        seed.SaveHeroIndex(new[] { _anduin });
        var cache = new BuildCache(Dir("live"), seedDir: Dir("seed"), fetchIndex: ForbiddenIndex);

        var heroes = await cache.GetOrFetchHeroIndexAsync();

        Assert.Equal(new[] { _anduin }, heroes);
        Assert.Contains("bundled", cache.DataSourceNotice);
    }

    [Fact]
    public async Task NetworkDisabled_UsesBundledBuildsWithoutEverFetching()
    {
        var seed = new BuildCache(Dir("seed"));
        seed.Save(SampleSet());
        seed.SaveHeroIndex(new[] { _anduin });
        var cache = new BuildCache(Dir("live"), seedDir: Dir("seed"), allowNetwork: false,
            fetchBuilds: (_, _) => throw new InvalidOperationException("must not fetch"),
            fetchIndex: _ => throw new InvalidOperationException("must not fetch"));

        var heroes = await cache.GetOrFetchHeroIndexAsync();
        var set = await cache.GetOrFetchAsync(_anduin);

        Assert.Equal(new[] { _anduin }, heroes);
        Assert.Equal("Lightwell", set.Builds[0].Tiers[0].PickName);
        Assert.Contains("bundled builds collected", cache.DataSourceNotice);
        Assert.DoesNotContain("Couldn't reach", cache.DataSourceNotice);
    }

    [Fact]
    public async Task NetworkDisabled_WithNoDataForTheHero_ThrowsAClearError()
    {
        var cache = new BuildCache(Dir("live"), seedDir: Dir("empty-seed"), allowNetwork: false);

        var ex = await Assert.ThrowsAsync<BuildDataUnavailableException>(() => cache.GetOrFetchAsync(_anduin));

        Assert.Contains("Anduin", ex.Message);
    }

    [Fact]
    public async Task NoDataAnywhere_TellsTheUserHowToGenerateIt()
    {
        // A fresh clone of the public repo: no snapshot, nothing saved, network off.
        var cache = new BuildCache(Dir("live"), seedDir: Dir("empty-seed"), allowNetwork: false);

        var ex = await Assert.ThrowsAsync<BuildDataUnavailableException>(() => cache.GetOrFetchHeroIndexAsync());

        Assert.Contains("refresh-builds", ex.Message);
        Assert.Equal(BuildCache.NoDataMessage, ex.Message);
    }

    [Fact]
    public async Task Cancellation_IsNotTreatedAsAFetchFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cache = new BuildCache(Dir("live"), seedDir: Dir("seed"),
            fetchBuilds: (_, ct) => Task.FromCanceled<HeroBuildSet>(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrFetchAsync(_anduin, cts.Token));
    }
}
