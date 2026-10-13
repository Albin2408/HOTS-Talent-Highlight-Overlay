using HotsOverlay.Core;

namespace HotsOverlay.Core.Tests;

public class CustomBuildTests
{
    private const string SevenNames = """
        Pressurized Glands
        Adrenal Overload
        Networked Carapace
        Ultimate Evolution
        Soma Transference
        Adrenaline Boost
        Hivemind
        """;

    [Fact]
    public void TryCreate_MapsSevenNamesOntoTheTierLevels()
    {
        bool ok = CustomBuild.TryCreate("My Abathur", SevenNames, out var build, out var error);

        Assert.True(ok, error);
        Assert.Equal("My Abathur", build!.Name);
        Assert.Equal(CustomBuild.RoleLabel, build.Role);
        Assert.Equal(TierLevels.Levels, build.Tiers.Select(t => t.Level));
        Assert.Equal("Pressurized Glands", build.Tiers[0].PickName);
        Assert.Equal("Hivemind", build.Tiers[6].PickName);
    }

    [Fact]
    public void TryCreate_StripsLevelLabelsAndBlankLines()
    {
        const string labelled = "Level 1: Pressurized Glands\n\n4. Adrenal Overload\nL7 - Networked Carapace\n" +
                                "10) Ultimate Evolution\nlv13: Soma Transference\n16 - Adrenaline Boost\n20: Hivemind\n";

        bool ok = CustomBuild.TryCreate("x", labelled, out var build, out var error);

        Assert.True(ok, error);
        Assert.Equal(
            new[] { "Pressurized Glands", "Adrenal Overload", "Networked Carapace", "Ultimate Evolution",
                    "Soma Transference", "Adrenaline Boost", "Hivemind" },
            build!.Tiers.Select(t => t.PickName));
    }

    [Fact]
    public void TryCreate_RejectsTheWrongNumberOfTalents()
    {
        bool ok = CustomBuild.TryCreate("x", "One\nTwo\nThree", out var build, out var error);

        Assert.False(ok);
        Assert.Null(build);
        Assert.Contains("7", error);
        Assert.Contains("got 3", error);
    }

    [Fact]
    public void TryCreate_RejectsABlankName()
    {
        Assert.False(CustomBuild.TryCreate("  ", SevenNames, out _, out var error));
        Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CleanLine_LeavesNormalTalentNamesAlone()
    {
        Assert.Equal("Power Word: Shield", CustomBuild.CleanLine("Power Word: Shield"));
        Assert.Equal("Huntress' Fury", CustomBuild.CleanLine("  Huntress' Fury "));
    }
}
