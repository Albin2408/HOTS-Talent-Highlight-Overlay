using HotsOverlay.Core;

namespace HotsOverlay.Core.Tests;

public class ActiveBuildStateTests
{
    private static HeroBuild Build(string name) => new()
    {
        Name = name,
        Tiers = new[] { new TalentTier { Level = 1, OptionCount = 3, PickIndex = 0, PickName = name + " talent" } },
    };

    private static IReadOnlyList<HeroBuild> Builds(params string[] names) => names.Select(Build).ToList();

    [Fact]
    public void SetHero_ExposesTheChosenBuildAndNotifies()
    {
        var state = new ActiveBuildState();
        int changes = 0;
        state.Changed += () => changes++;

        state.SetHero("anduin", "Anduin", Builds("Lightwell", "Leap"), 1, SelectionSource.Auto);

        Assert.Equal("Leap", state.Build?.Name);
        Assert.Equal(SelectionSource.Auto, state.Source);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SetHero_ClampsAnOutOfRangeIndex()
    {
        var state = new ActiveBuildState();

        state.SetHero("anduin", "Anduin", Builds("A", "B"), 99, SelectionSource.Manual);

        Assert.Equal("B", state.Build?.Name);
    }

    [Fact]
    public void NoHeroYet_MeansNoBuild() => Assert.Null(new ActiveBuildState().Build);

    [Fact]
    public void SelectBuild_SwitchesWithinTheHero()
    {
        var state = new ActiveBuildState();
        state.SetHero("anduin", "Anduin", Builds("A", "B", "C"), 0, SelectionSource.Auto);

        Assert.True(state.SelectBuild(2));

        Assert.Equal("C", state.Build?.Name);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(0)] // already selected
    public void SelectBuild_IgnoresInvalidOrUnchangedChoices(int index)
    {
        var state = new ActiveBuildState();
        state.SetHero("anduin", "Anduin", Builds("A", "B", "C"), 0, SelectionSource.Auto);
        int changes = 0;
        state.Changed += () => changes++;

        Assert.False(state.SelectBuild(index));

        Assert.Equal(0, changes);
        Assert.Equal("A", state.Build?.Name);
    }

    [Fact]
    public void SetAutoDetect_OnlyNotifiesOnAChange()
    {
        var state = new ActiveBuildState();
        int changes = 0;
        state.Changed += () => changes++;

        state.SetAutoDetect(true);  // already on
        state.SetAutoDetect(false);

        Assert.False(state.AutoDetect);
        Assert.Equal(1, changes);
    }

    private static HeroBuild Role(string name, string role) => new()
    {
        Name = name,
        Role = role,
        Tiers = new[] { new TalentTier { Level = 1, OptionCount = 3, PickIndex = 0, PickName = name } },
    };

    [Fact]
    public void PreferredIndex_DefaultsToTheRecommendedBuildEvenWhenItIsNotListedFirst()
    {
        // Stukov's real Icy Veins order: Situational, Recommended, ARAM.
        var builds = new[] { Role("Lifesteal", "Situational"), Role("Pandemic", "Recommended"), Role("ARAM", "ARAM") };

        Assert.Equal(1, ActiveBuildState.PreferredIndex(builds, null));
    }

    [Fact]
    public void PreferredIndex_ARememberedChoiceBeatsTheRecommendedBuild()
    {
        var builds = new[] { Role("Lifesteal", "Situational"), Role("Pandemic", "Recommended") };

        Assert.Equal(0, ActiveBuildState.PreferredIndex(builds, "Lifesteal"));
    }

    [Fact]
    public void PreferredIndex_AMissingRememberedBuildFallsBackToTheRecommendedOne()
    {
        var builds = new[] { Role("A", "Situational"), Role("B", "Recommended") };

        Assert.Equal(1, ActiveBuildState.PreferredIndex(builds, "Deleted build"));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("B", 1)]
    [InlineData("c", 2)]            // case-insensitive
    [InlineData("Gone", 0)]         // remembered build no longer exists -> first build
    public void PreferredIndex_FindsTheRememberedBuildOrFallsBackToTheFirst(string? remembered, int expected) =>
        Assert.Equal(expected, ActiveBuildState.PreferredIndex(Builds("A", "B", "C"), remembered));
}
