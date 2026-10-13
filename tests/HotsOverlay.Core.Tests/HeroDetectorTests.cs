using HotsOverlay.Core;

namespace HotsOverlay.Core.Tests;

public class HeroDetectorTests
{
    private static OcrLineBox Line(string text, double y = 100, double h = 20)
    {
        var words = new List<OcrWordBox>();
        double x = 100;
        foreach (var w in text.Split(' '))
        {
            words.Add(new OcrWordBox(w, new BoxF(x, y, w.Length * h * 0.55, h)));
            x += w.Length * h * 0.55 + 6;
        }
        return new OcrLineBox(words);
    }

    private static HeroBuildSet Hero(string slug, string name, params string[] picks) => new()
    {
        HeroSlug = slug,
        HeroName = name,
        ScrapedAtUtc = DateTimeOffset.UtcNow,
        Builds = new[]
        {
            new HeroBuild
            {
                Name = "Build",
                Tiers = picks.Select((p, i) => new TalentTier { Level = i * 3 + 1, OptionCount = 3, PickIndex = 0, PickName = p }).ToList(),
            },
        },
    };

    private static HeroDetector Detector() => new(new[]
    {
        Hero("anduin", "Anduin", "Lightwell", "Power Word: Shield", "Holy Nova"),
        Hero("tyrande", "Tyrande", "Trueshot Aura", "Mark of Mending", "Starfall"),
        Hero("rewinder-a", "Chromie", "Rewind Reality"),
        Hero("rewinder-b", "Zarya", "Rewind Reality"), // shares a name with Chromie on purpose
    });

    [Fact]
    public void Guess_IdentifiesTheHeroFromItsTalentCards()
    {
        var lines = new[] { Line("Evenhanded Blessings", 400), Line("Power Word: Shield", 493), Line("Lightwell", 586) };

        var guess = Detector().Guess(lines);

        Assert.NotNull(guess);
        Assert.Equal("anduin", guess!.Slug);
        Assert.Contains("lightwell", guess.MatchedTalents);
    }

    [Fact]
    public void Guess_ToleratesAnOcrTypo()
    {
        var guess = Detector().Guess(new[] { Line("Trueshot Aur4") });

        Assert.Equal("tyrande", guess?.Slug);
    }

    [Fact]
    public void Guess_IgnoresTalentNamesInsideLongDescriptions()
    {
        var guess = Detector().Guess(new[] { Line("Using the Trueshot Aura also heals nearby allies for a while") });

        Assert.Null(guess);
    }

    [Fact]
    public void Guess_AlsoSeesATitleWithAStrayBadgeWord()
    {
        var guess = Detector().Guess(new[] { Line("Starfall Q") });

        Assert.Equal("tyrande", guess?.Slug);
    }

    [Fact]
    public void Guess_IsInconclusiveWhenTheOnlyNameIsSharedByTwoHeroes()
    {
        Assert.Null(Detector().Guess(new[] { Line("Rewind Reality") }));
    }

    [Fact]
    public void Guess_ASharedNameIsResolvedByAnUnsharedOne()
    {
        var guess = Detector().Guess(new[] { Line("Rewind Reality", 100), Line("Lightwell", 200) });

        Assert.Equal("anduin", guess?.Slug);
    }

    [Fact]
    public void Guess_AlsoRecognizesAHeroBySituationalAlternates()
    {
        var withAlternates = new HeroDetector(new[]
        {
            new HeroBuildSet
            {
                HeroSlug = "fenix", HeroName = "Fenix", ScrapedAtUtc = DateTimeOffset.UtcNow,
                Builds = new[]
                {
                    new HeroBuild
                    {
                        Name = "B",
                        Tiers = new[]
                        {
                            new TalentTier
                            {
                                Level = 1, OptionCount = 3, PickIndex = 0, PickName = "Arsenal Synergy",
                                Alternates = new[] { "Plasma Cutter" },
                            },
                        },
                    },
                },
            },
        });

        Assert.Equal("fenix", withAlternates.Guess(new[] { Line("Plasma Cutter") })?.Slug);
    }

    [Fact]
    public void Guess_IgnoresOrdinaryScreenText()
    {
        var lines = new[] { Line("Reset Talents"), Line("Select Ally Hero"), Line("Talent Available"), Line("06:54") };

        Assert.Null(Detector().Guess(lines));
    }

    [Fact]
    public void Observe_NeedsConsecutiveAgreementBeforeReportingAHero()
    {
        var detector = Detector();
        var anduin = new[] { Line("Lightwell") };

        Assert.Null(detector.Observe(anduin));
        Assert.Equal("anduin", detector.Observe(anduin)?.Slug);
    }

    [Fact]
    public void Observe_ADifferentHeroRestartsTheCount()
    {
        var detector = Detector();

        Assert.Null(detector.Observe(new[] { Line("Lightwell") }));
        Assert.Null(detector.Observe(new[] { Line("Starfall") }));
        Assert.Equal("tyrande", detector.Observe(new[] { Line("Starfall") })?.Slug);
    }

    [Fact]
    public void Observe_AnEmptyScanDoesNotResetAPendingGuess()
    {
        var detector = Detector();

        Assert.Null(detector.Observe(new[] { Line("Lightwell") }));
        Assert.Null(detector.Observe(Array.Empty<OcrLineBox>()));
        Assert.Equal("anduin", detector.Observe(new[] { Line("Lightwell") })?.Slug);
    }
}
