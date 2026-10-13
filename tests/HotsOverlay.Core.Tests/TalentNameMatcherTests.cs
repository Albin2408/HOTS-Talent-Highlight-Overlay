using HotsOverlay.Core;

namespace HotsOverlay.Core.Tests;

public class TalentNameMatcherTests
{
    // Coordinates mirror the user's real HOTS screenshot (Anduin, level 1 talent panel):
    // three stacked cards, each with a bold title line and a smaller description line.
    private static OcrLineBox Line(string text, double x, double y, double h, double charWidth = 0)
    {
        charWidth = charWidth > 0 ? charWidth : h * 0.55;
        var words = new List<OcrWordBox>();
        double cursor = x;
        foreach (var w in text.Split(' '))
        {
            double width = w.Length * charWidth;
            words.Add(new OcrWordBox(w, new BoxF(cursor, y, width, h)));
            cursor += width + charWidth * 0.5;
        }
        return new OcrLineBox(words);
    }

    private static IReadOnlyList<OcrLineBox> AnduinLevelOnePanel() => new[]
    {
        Line("Evenhanded Blessings", 103, 400, 20),
        Line("Refund Flash Heal cooldown on new targets", 103, 423, 16, 6.6),
        Line("Power Word: Shield", 103, 493, 20),
        Line("Divine Star grants Shields", 103, 516, 16, 6.6),
        Line("Lightwell", 103, 586, 20),
        Line("Activate to summon a healing Lightwell", 103, 609, 16, 6.6),
        Line("1 4 7 10 13 16 20", 60, 690, 22),
    };

    private static HeroBuild BuildPicking(int level, string pickName, params (int Level, string Name)[] others)
    {
        var tiers = others
            .Select(o => new TalentTier { Level = o.Level, OptionCount = 3, PickIndex = 0, PickName = o.Name })
            .Append(new TalentTier { Level = level, OptionCount = 3, PickIndex = 1, PickName = pickName })
            .OrderBy(t => t.Level)
            .ToList();
        return new HeroBuild { Name = "Test", Tiers = tiers };
    }

    [Fact]
    public void Find_LocatesRecommendedCardByName()
    {
        var build = BuildPicking(1, "Power Word: Shield", (4, "Holy Fire"));

        var match = Assert.Single(TalentNameMatcher.Find(AnduinLevelOnePanel(), build));

        Assert.Equal(1, match.Tier.Level);
        Assert.Equal(493, match.NameBox.Y, precision: 3);
        // The card box should wrap the title and its description line.
        Assert.True(match.CardBox.Y < 493 && match.CardBox.Bottom > 516 + 16);
        Assert.True(match.CardBox.X < 103 && match.CardBox.Right > 103 + 200);
    }

    [Fact]
    public void Find_IgnoresDescriptionThatOnlyMentionsAnotherTalentName()
    {
        // "Divine Star" is a different tier's pick, but it only appears inside a description.
        var build = BuildPicking(1, "Lightwell", (4, "Divine Star"));

        var matches = TalentNameMatcher.Find(AnduinLevelOnePanel(), build);

        var match = Assert.Single(matches);
        Assert.Equal("Lightwell", match.Tier.PickName);
    }

    [Fact]
    public void Find_ToleratesSmallOcrMistakes()
    {
        var lines = new[] { Line("Evenhanded B1essings", 103, 400, 20) };
        var build = BuildPicking(1, "Evenhanded Blessings");

        Assert.Single(TalentNameMatcher.Find(lines, build));
    }

    [Fact]
    public void Find_ToleratesStrayHotkeyBadgeOnTheTitleLine()
    {
        var lines = new[] { Line("Evenhanded Blessings Q", 103, 400, 20) };
        var build = BuildPicking(1, "Evenhanded Blessings");

        var match = Assert.Single(TalentNameMatcher.Find(lines, build));
        Assert.True(match.NameBox.Right < 103 + "Evenhanded Blessings".Length * 20 * 0.55 + 40);
    }

    [Fact]
    public void Find_ReturnsNothingWhenPickIsNotOnScreen()
    {
        var build = BuildPicking(1, "Holy Fire");

        Assert.Empty(TalentNameMatcher.Find(AnduinLevelOnePanel(), build));
    }

    private static HeroBuild BuildWithAlternates() => new()
    {
        Name = "Test",
        Tiers = new[]
        {
            new TalentTier
            {
                Level = 1, OptionCount = 3, PickIndex = 1, PickName = "Power Word: Shield",
                Alternates = new[] { "Lightwell" },
            },
        },
    };

    [Fact]
    public void FindAll_ReturnsTheAlternateAsWellAsThePick()
    {
        var matches = TalentNameMatcher.FindAll(AnduinLevelOnePanel(), BuildWithAlternates());

        var pick = Assert.Single(matches, m => !m.IsAlternate);
        var alternate = Assert.Single(matches, m => m.IsAlternate);
        Assert.Equal("Power Word: Shield", pick.TalentName);
        Assert.Equal("Lightwell", alternate.TalentName);
        Assert.True(alternate.NameBox.Y > pick.NameBox.Y, "Lightwell's card is below Power Word: Shield's");
        Assert.Equal(1, alternate.Tier.Level);
    }

    [Fact]
    public void Find_StillReturnsOnlyTheMainPick()
    {
        var matches = TalentNameMatcher.Find(AnduinLevelOnePanel(), BuildWithAlternates());

        Assert.Equal("Power Word: Shield", Assert.Single(matches).TalentName);
    }

    [Theory]
    [InlineData("Huntress' Fury", "huntress fury")]
    [InlineData("Huntress’ Fury", "huntress fury")]
    [InlineData("Power Word: Shield", "power word shield")]
    [InlineData("  E.T.C.  ", "e t c")]
    public void Normalize_StripsPunctuationAndCase(string input, string expected) =>
        Assert.Equal(expected, TalentNameMatcher.Normalize(input));
}
