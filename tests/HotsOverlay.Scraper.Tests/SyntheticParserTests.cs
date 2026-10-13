using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.Scraper.Tests;

/// <summary>The parser, exercised on invented pages so it needs no real site content (see SyntheticGuide).</summary>
public class SyntheticParserTests
{
    private static readonly HeroSummary Hero = new("sample-hero", "Sample Hero");

    private static HeroBuildSet Parse(IEnumerable<SyntheticBuild> builds, long? updated = null) =>
        HeroBuildScraper.Parse(SyntheticGuide.HeroPage(builds, updated), Hero);

    [Fact]
    public void FindsEveryBuildWithItsNameAndRole()
    {
        var set = Parse(new[]
        {
            SyntheticGuide.Build("Alpha Build", "Recommended", "A"),
            SyntheticGuide.Build("Beta Build", "Situational", "B"),
            SyntheticGuide.Build("ARAM Build", "ARAM", "C"),
        });

        Assert.Equal(new[] { "Alpha Build", "Beta Build", "ARAM Build" }, set.Builds.Select(b => b.Name));
        Assert.Equal(new[] { "Recommended", "Situational", "ARAM" }, set.Builds.Select(b => b.Role));
        Assert.All(set.Builds, b => Assert.Equal(7, b.Tiers.Count));
    }

    [Fact]
    public void HandlesDifferentNumbersOfBuilds()
    {
        Assert.Equal(2, Parse(new[] { SyntheticGuide.Build("A", "Recommended", "A"), SyntheticGuide.Build("B", "ARAM", "B") }).Builds.Count);
        Assert.Equal(4, Parse(Enumerable.Range(0, 4).Select(i => SyntheticGuide.Build($"B{i}", "Situational", $"P{i}"))).Builds.Count);
    }

    [Fact]
    public void ReadsTalentNamesLevelsAndStripsTheIconSuffix()
    {
        var tiers = Parse(new[] { SyntheticGuide.Build("A", "Recommended", "Alpha") }).Builds[0].Tiers;

        Assert.Equal(SyntheticGuide.StandardLevels, tiers.Select(t => t.Level));
        Assert.Equal("Alpha Talent 1", tiers[0].PickName);
        Assert.Equal("Alpha Talent 20", tiers[6].PickName);
    }

    [Fact]
    public void ReadsPickPositionsFromTheCalculatorCodeAndOptionCountsFromTheDots()
    {
        var build = new SyntheticBuild("A", "Recommended",
            new SyntheticTier(1, "One", OptionCount: 3, PickPosition: 3),
            new SyntheticTier(4, "Two", OptionCount: 3, PickPosition: 2),
            new SyntheticTier(7, "Three", OptionCount: 4, PickPosition: 1),
            new SyntheticTier(10, "Four", OptionCount: 2, PickPosition: 2),
            new SyntheticTier(13, "Five", OptionCount: 3, PickPosition: 3),
            new SyntheticTier(16, "Six", OptionCount: 3, PickPosition: 1),
            new SyntheticTier(20, "Seven", OptionCount: 4, PickPosition: 4));

        var tiers = Parse(new[] { build }).Builds[0].Tiers;

        Assert.Equal(new[] { 2, 1, 0, 1, 2, 0, 3 }, tiers.Select(t => t.PickIndex));
        Assert.Equal(new[] { 3, 3, 4, 2, 3, 3, 4 }, tiers.Select(t => t.OptionCount));
    }

    [Fact]
    public void TheCalculatorCodeWinsWhenTheDotsDisagree()
    {
        // Dots claim position 1; the calculator code (the primary source) says position 3.
        var html = SyntheticGuide.HeroPage(new[] { SyntheticGuide.Build("A", "Recommended", "A") })
            .Replace("#55.16!1111111", "#55.16!3333333");

        var tiers = HeroBuildScraper.Parse(html, Hero).Builds[0].Tiers;

        Assert.All(tiers, t => Assert.Equal(2, t.PickIndex));
    }

    [Fact]
    public void CollectsSituationalAlternatesWithoutDuplicatingThePick()
    {
        var build = new SyntheticBuild("A", "Recommended",
            new SyntheticTier(1, "Pick One"),
            new SyntheticTier(4, "Pick Two", 3, 2, "Other A", "Other B"),
            new SyntheticTier(7, "Pick Three", 3, 1, "Pick Three"),   // an "alternate" equal to the pick
            new SyntheticTier(10, "Pick Four"),
            new SyntheticTier(13, "Pick Five"),
            new SyntheticTier(16, "Pick Six"),
            new SyntheticTier(20, "Pick Seven"));

        var tiers = Parse(new[] { build }).Builds[0].Tiers;

        Assert.Empty(tiers[0].Alternates);
        Assert.Equal(new[] { "Other A", "Other B" }, tiers[1].Alternates);
        Assert.Empty(tiers[2].Alternates);
        Assert.All(tiers, t => Assert.DoesNotContain(t.PickName, t.Alternates));
    }

    [Fact]
    public void AcceptsHeroesWhoseTalentLevelsAreNotTheStandardOnes()
    {
        var levels = new[] { 1, 2, 5, 8, 11, 14, 18 };

        var tiers = Parse(new[] { SyntheticGuide.Build("A", "Recommended", "A", levels) }).Builds[0].Tiers;

        Assert.Equal(levels, tiers.Select(t => t.Level));
    }

    [Fact]
    public void ReadsWhenTheGuideWasLastUpdated()
    {
        var set = Parse(new[] { SyntheticGuide.Build("A", "Recommended", "A") }, updated: 1783892100);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1783892100), set.GuideUpdatedUtc);
    }

    [Fact]
    public void ALastUpdatedDateIsOptional() =>
        Assert.Null(Parse(new[] { SyntheticGuide.Build("A", "Recommended", "A") }).GuideUpdatedUtc);

    [Fact]
    public void APageWithoutBuildsYieldsNoBuildsInsteadOfThrowing() =>
        Assert.Empty(HeroBuildScraper.Parse("<html><body><p>Nothing here</p></body></html>", Hero).Builds);

    [Fact]
    public void HeroIndex_ListsHeroesOnceEachAndDecodesNames()
    {
        var html = SyntheticGuide.IndexPage(("alpha", "Alpha"), ("anubarak-like", "Anub&arak"), ("alpha", "Alpha"), ("e-t-c-like", "E.T.C."));

        var heroes = HeroIndexScraper.Parse(html);

        Assert.Equal(new[] { "alpha", "anubarak-like", "e-t-c-like" }, heroes.Select(h => h.Slug));
        Assert.Equal("Anub&arak", heroes[1].Name);
    }
}
