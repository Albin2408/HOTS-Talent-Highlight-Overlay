using System.Net;
using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.Scraper.Tests;

public class HeroesProfileClientTests
{
    // Exactly the example in HeroesProfile's "Hero Talent Builds" docs (plus a second, weaker build).
    private const string DocsExample = """
        {
          "Abathur": [
            { "win_rate": 59.82,
              "build_talents": ["Pressurized Glands","Adrenal Overload","Networked Carapace","Ultimate Evolution","Soma Transference","Adrenaline Boost","Hivemind"] },
            { "win_rate": 61.4,
              "build_talents": ["Pressurized Glands","Adrenal Overload","Networked Carapace","Ultimate Evolution","Soma Transference","Adrenaline Boost","Mutating Biomass"] }
          ]
        }
        """;

    [Fact]
    public void ParseBuilds_ReadsWinRatesAndSevenTalentNames_BestWinRateFirst()
    {
        var heroes = HeroesProfileClient.ParseBuilds(DocsExample);

        var builds = heroes["Abathur"];
        Assert.Equal(2, builds.Count);
        Assert.Equal(61.4, builds[0].WinRate);   // sorted best first even though the API listed it second
        Assert.Equal(7, builds[0].Talents.Count);
        Assert.Equal("Mutating Biomass", builds[0].Talents[6]);
    }

    [Fact]
    public void ParseBuilds_SkipsBuildsWithMissingOrBlankTalents()
    {
        const string json = """{"Abathur":[{"win_rate":50,"build_talents":["A","","C"]},{"win_rate":51}]}""";

        Assert.Empty(HeroesProfileClient.ParseBuilds(json));
    }

    [Fact]
    public void ParseBuilds_AnHtmlPageMeansTheTokenWasNotAccepted()
    {
        var ex = Assert.Throws<HeroesProfileException>(() =>
            HeroesProfileClient.ParseBuilds("<!DOCTYPE html><html><body>Login</body></html>"));

        Assert.Contains("api_token", ex.Message);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task GetTopBuilds_SendsTheDocumentedRequestParameters()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DocsExample) });
        var client = new HeroesProfileClient("secret-token", new HttpClient(handler));

        var heroes = await client.GetTopBuildsAsync("2.57");

        Assert.Single(heroes);
        string query = handler.LastUri!.Query;
        Assert.Contains("mode=json", query);
        Assert.Contains("timeframe_type=major", query);
        Assert.Contains("timeframe=2.57", query);
        Assert.Contains("game_type=Storm%20League", query);
        Assert.Contains("api_token=secret-token", query);
    }

    [Fact]
    public async Task GetTopBuilds_NeverLeaksTheTokenIntoAnErrorMessage()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new HeroesProfileClient("secret-token", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HeroesProfileException>(() => client.GetTopBuildsAsync("2.57"));

        Assert.DoesNotContain("secret-token", ex.ToString());
    }
}

public class WinRateMergerTests
{
    private static HeroBuild Build(string name, params string[] picks) => new()
    {
        Name = name,
        Role = "Recommended",
        Tiers = picks.Select((p, i) => new TalentTier
        {
            Level = new[] { 1, 4, 7, 10, 13, 16, 20 }[i], OptionCount = 3, PickIndex = 0, PickName = p,
        }).ToList(),
    };

    private static readonly string[] A = { "a1", "a2", "a3", "a4", "a5", "a6", "a7" };
    private static readonly string[] B = { "b1", "b2", "b3", "b4", "b5", "b6", "b7" };
    private static readonly string[] C = { "c1", "c2", "c3", "c4", "c5", "c6", "c7" };

    private static HeroBuildSet Hero(params HeroBuild[] builds) => new()
    {
        HeroName = "Abathur", HeroSlug = "abathur", Builds = builds, ScrapedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void AnIdenticalBuildJustGainsItsWinRate()
    {
        var merged = WinRateMerger.Merge(Hero(Build("Symbiote Build", A)), new[] { new WinRateBuild(58.4, A) });

        var only = Assert.Single(merged.Builds);
        Assert.Equal("Symbiote Build", only.Name);
        Assert.Equal(58.4, only.WinRate);
    }

    [Fact]
    public void TheMatchIsCaseAndPunctuationInsensitive()
    {
        var hero = Hero(Build("X", "Huntress' Fury", "b", "c", "d", "e", "f", "g"));
        var top = new WinRateBuild(55, new[] { "huntress fury", "B", "C", "D", "E", "F", "G" });

        Assert.Equal(55, Assert.Single(WinRateMerger.Merge(hero, new[] { top }).Builds).WinRate);
    }

    [Fact]
    public void UncoveredWinRateBuildsAreAddedAfterTheGuideBuilds()
    {
        var merged = WinRateMerger.Merge(
            Hero(Build("Symbiote Build", A)),
            new[] { new WinRateBuild(61.0, B), new WinRateBuild(58.4, A), new WinRateBuild(57.0, C) });

        Assert.Equal(new[] { "Symbiote Build", "Top win rate #1", "Top win rate #3" }, merged.Builds.Select(b => b.Name));
        Assert.Equal(58.4, merged.Builds[0].WinRate);                       // A matched the guide build
        Assert.Equal(WinRateMerger.SourceLabel, merged.Builds[1].Role);
        Assert.Equal(new[] { "b1", "b2", "b3", "b4", "b5", "b6", "b7" }, merged.Builds[1].Tiers.Select(t => t.PickName));
        Assert.Equal(new[] { 1, 4, 7, 10, 13, 16, 20 }, merged.Builds[1].Tiers.Select(t => t.Level));
    }

    [Fact]
    public void AtMostThreeExtraBuildsAreAdded()
    {
        var many = Enumerable.Range(0, 5).Select(i => new WinRateBuild(60 - i, B.Select(x => x + i).ToArray())).ToList();

        var merged = WinRateMerger.Merge(Hero(Build("G", A)), many);

        Assert.Equal(1 + WinRateMerger.MaxExtraBuilds, merged.Builds.Count);
    }

    [Fact]
    public void AGuideBuildWithNoWinRateDataIsLeftAlone()
    {
        var merged = WinRateMerger.Merge(Hero(Build("G", A)), Array.Empty<WinRateBuild>());

        Assert.Null(Assert.Single(merged.Builds).WinRate);
    }

    [Theory]
    [InlineData("Lúcio", "lucio")]
    [InlineData("E.T.C.", "etc")]
    [InlineData("Anub'arak", "anubarak")]
    [InlineData("Li-Ming", "liming")]
    public void HeroKey_MakesNamesFromDifferentSourcesComparable(string name, string expected) =>
        Assert.Equal(expected, WinRateMerger.HeroKey(name));
}
