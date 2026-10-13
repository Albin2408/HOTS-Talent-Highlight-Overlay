using HotsOverlay.Core;
using HotsOverlay.Scraper;
using HotsOverlay.TestSupport;

namespace HotsOverlay.Scraper.Tests;

public class HeroBuildScraperTests
{
    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "real", name));

    [OptionalDataFact("Fixtures/real")]
    public void Parse_FindsAllBuildsWithSevenTiersEach()
    {
        var html = ReadFixture("tyrande-build-guide.html");
        var hero = new HeroSummary("tyrande", "Tyrande");

        var set = HeroBuildScraper.Parse(html, hero);

        Assert.Equal("tyrande", set.HeroSlug);
        Assert.Equal(3, set.Builds.Count);
        Assert.Equal(new[] { "Hunter Build", "Ranger Build", "ARAM Build" }, set.Builds.Select(b => b.Name));
        Assert.All(set.Builds, b => Assert.Equal(7, b.Tiers.Count));
        Assert.All(set.Builds, b => Assert.Equal(TierLevels.Levels, b.Tiers.Select(t => t.Level)));
    }

    [OptionalDataFact("Fixtures/real")]
    public void Parse_HunterBuild_MatchesKnownTalentPicks()
    {
        var html = ReadFixture("tyrande-build-guide.html");
        var hero = new HeroSummary("tyrande", "Tyrande");

        var set = HeroBuildScraper.Parse(html, hero);
        var hunter = set.Builds.Single(b => b.Name == "Hunter Build");

        // From the page's talent-calculator code "3232312": one digit per tier,
        // 1-based pick position. Cross-checked against the tier's visual dot markers.
        AssertTier(hunter, level: 1, optionCount: 3, pickIndex: 2, pickName: "Trueshot Aura");
        AssertTier(hunter, level: 4, optionCount: 3, pickIndex: 1, pickName: "Mark of Mending");
        AssertTier(hunter, level: 7, optionCount: 3, pickIndex: 2, pickName: "Huntress' Fury");
        AssertTier(hunter, level: 10, optionCount: 2, pickIndex: 1, pickName: "Starfall");
        AssertTier(hunter, level: 13, optionCount: 3, pickIndex: 2, pickName: "Ranger's Mark");
        AssertTier(hunter, level: 16, optionCount: 3, pickIndex: 0, pickName: "Celestial Attunement");
        AssertTier(hunter, level: 20, optionCount: 4, pickIndex: 1, pickName: "Celestial Wrath");
    }

    [OptionalDataFact("Fixtures/real")]
    public void Parse_HunterBuild_CollectsTheSituationalAlternates()
    {
        var html = ReadFixture("tyrande-build-guide.html");
        var set = HeroBuildScraper.Parse(html, new HeroSummary("tyrande", "Tyrande"));
        var hunter = set.Builds.Single(b => b.Name == "Hunter Build");

        // The page marks Shadowstalk "?" at level 10, and Empower + Darnassian Archery at level 16.
        Assert.Equal(new[] { "Shadowstalk" }, hunter.Tiers.Single(t => t.Level == 10).Alternates);
        Assert.Equal(new[] { "Empower", "Darnassian Archery" }, hunter.Tiers.Single(t => t.Level == 16).Alternates);
        Assert.Empty(hunter.Tiers.Single(t => t.Level == 1).Alternates);
        Assert.All(hunter.Tiers, t => Assert.DoesNotContain(t.PickName, t.Alternates));
    }

    [OptionalDataFact("Fixtures/real")]
    public void Parse_ReadsWhenTheGuideWasLastUpdated()
    {
        var html = ReadFixture("anduin-build-guide.html");

        var set = HeroBuildScraper.Parse(html, new HeroSummary("anduin", "Anduin"));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1783892100), set.GuideUpdatedUtc);
    }

    [OptionalDataFact("Fixtures/real")]
    public void TierAt_ReturnsHighestUnlockedTier()
    {
        var html = ReadFixture("tyrande-build-guide.html");
        var hero = new HeroSummary("tyrande", "Tyrande");
        var set = HeroBuildScraper.Parse(html, hero);
        var hunter = set.Builds.Single(b => b.Name == "Hunter Build");

        Assert.Equal(1, hunter.TierAt(1)?.Level);
        Assert.Equal(4, hunter.TierAt(6)?.Level);
        Assert.Equal(20, hunter.TierAt(25)?.Level);
        Assert.Null(hunter.TierAt(0));
    }

    private static void AssertTier(HeroBuild build, int level, int optionCount, int pickIndex, string pickName)
    {
        var tier = build.Tiers.Single(t => t.Level == level);
        Assert.Equal(optionCount, tier.OptionCount);
        Assert.Equal(pickIndex, tier.PickIndex);
        Assert.Equal(pickName, tier.PickName);
    }
}

public class AnduinNameConsistencyTests
{
    // The overlay finds a talent by matching the scraped name against text OCR'd from the game,
    // so Icy Veins' spelling must agree with the in-game cards (verified against a real
    // screenshot of Anduin's level 1 panel: Evenhanded Blessings / Power Word: Shield / Lightwell).
    [OptionalDataFact("Fixtures/real")]
    public void LevelOneRecommendedPicks_UseTheInGameTalentNames()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "real", "anduin-build-guide.html"));

        var set = HeroBuildScraper.Parse(html, new HeroSummary("anduin", "Anduin"));

        var inGameLevelOne = new[] { "Evenhanded Blessings", "Power Word: Shield", "Lightwell" };
        Assert.All(set.Builds, b => Assert.Contains(b.Tiers[0].PickName, inGameLevelOne));
        Assert.Contains(set.Builds, b => b.Name.Contains("Lightwell") && b.Tiers[0].PickName == "Lightwell");
    }
}

public class HeroBuildScraperGeneralizationTests
{
    // Different heroes have different build counts (2-4) and tier option counts (2-4) on
    // Icy Veins; these fixtures exercise that variance rather than assuming exactly 3 builds.
    [OptionalDataTheory("Fixtures/real")]
    [InlineData("abathur", "abathur-build-guide.html")]
    [InlineData("muradin", "muradin-build-guide.html")]
    [InlineData("li-ming", "li-ming-build-guide.html")]
    [InlineData("garrosh", "garrosh-build-guide.html")]
    public void Parse_ProducesStructurallyValidBuildsForVariedHeroes(string slug, string fixtureFile)
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "real", fixtureFile));
        var hero = new HeroSummary(slug, slug);

        var set = HeroBuildScraper.Parse(html, hero);

        Assert.True(set.Builds.Count >= 2, $"{slug}: expected at least 2 builds, got {set.Builds.Count}");
        foreach (var build in set.Builds)
        {
            Assert.Equal(7, build.Tiers.Count);
            Assert.Equal(TierLevels.Levels, build.Tiers.Select(t => t.Level));
            foreach (var tier in build.Tiers)
            {
                Assert.InRange(tier.OptionCount, 2, 6);
                Assert.InRange(tier.PickIndex, 0, tier.OptionCount - 1);
                Assert.False(string.IsNullOrWhiteSpace(tier.PickName));
                Assert.NotEqual("Unknown", tier.PickName);
            }
        }
    }
}

public class HeroIndexScraperTests
{
    [OptionalDataFact("Fixtures/real")]
    public void Parse_FindsFullRosterIncludingTyrande()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "real", "heroes-index.html"));

        var heroes = HeroIndexScraper.Parse(html);

        Assert.True(heroes.Count > 80, $"expected a full ~90-hero roster, got {heroes.Count}");
        Assert.Contains(heroes, h => h.Slug == "tyrande" && h.Name == "Tyrande");
        Assert.Contains(heroes, h => h.Slug == "e-t-c" && h.Name == "E.T.C.");
        Assert.Equal(heroes.Count, heroes.Select(h => h.Slug).Distinct().Count());
    }
}
