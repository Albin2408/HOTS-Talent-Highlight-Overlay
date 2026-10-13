using HotsOverlay.Core;
using HotsOverlay.Scraper;
using HotsOverlay.TestSupport;

namespace HotsOverlay.Scraper.Tests;

/// <summary>Guards the build snapshot the app ships with (src/HotsOverlay.App/SeedBuilds).</summary>
public class BundledSeedDataTests
{
    private static readonly string SeedDir = Path.Combine(AppContext.BaseDirectory, "SeedBuilds");

    private static BuildCache Cache() => new(
        Path.Combine(Path.GetTempPath(), "hots-seed-test-" + Guid.NewGuid().ToString("N")),
        seedDir: SeedDir, allowNetwork: false);

    [OptionalDataFact("SeedBuilds")]
    public async Task EveryBundledHeroHasUsableBuilds()
    {
        var cache = Cache();
        var heroes = await cache.GetOrFetchHeroIndexAsync();
        Assert.True(heroes.Count >= 85, $"expected the full roster, got {heroes.Count} heroes");

        foreach (var hero in heroes)
        {
            var set = await cache.GetOrFetchAsync(hero);
            Assert.NotEmpty(set.Builds);
            foreach (var build in set.Builds)
            {
                Assert.Equal(7, build.Tiers.Count);
                Assert.All(build.Tiers, t => Assert.False(string.IsNullOrWhiteSpace(t.PickName), $"{hero.Name}/{build.Name}"));
                Assert.Equal(build.Tiers.Select(t => t.Level).OrderBy(l => l), build.Tiers.Select(t => t.Level));
            }
        }
    }

    [OptionalDataFact("SeedBuilds")]
    public async Task EveryHeroDefaultsToTheirOneRecommendedBuild()
    {
        var cache = Cache();

        foreach (var hero in await cache.GetOrFetchHeroIndexAsync())
        {
            var set = await cache.GetOrFetchAsync(hero);
            var recommended = set.Builds.Where(b => b.Role == HeroBuild.RecommendedRole).ToList();

            Assert.True(recommended.Count == 1, $"{hero.Name} has {recommended.Count} Recommended builds");
            Assert.Same(recommended[0], set.Builds[ActiveBuildState.PreferredIndex(set.Builds, null)]);
        }
    }

    [OptionalDataFact("SeedBuilds")]
    public async Task DiabloAndChromie_AreIncluded_ChromieKeepsHerOwnTalentLevels()
    {
        var cache = Cache();

        var diablo = await cache.GetOrFetchAsync(new HeroSummary("diablo", "Diablo"));
        var chromie = await cache.GetOrFetchAsync(new HeroSummary("chromie", "Chromie"));

        Assert.NotEmpty(diablo.Builds);
        Assert.Equal(new[] { 1, 2, 5, 8, 11, 14, 18 }, chromie.Builds[0].Tiers.Select(t => t.Level));
    }
}
