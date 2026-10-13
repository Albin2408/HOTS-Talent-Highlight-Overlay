namespace HotsOverlay.Core;

/// <summary>A single named build (e.g. "Hunter Build") for one hero.</summary>
public sealed class HeroBuild
{
    public required string Name { get; init; }

    /// <summary>The role label Icy Veins gives the build it recommends for a hero.</summary>
    public const string RecommendedRole = "Recommended";

    /// <summary>Free-text label from the source, e.g. "Recommended", "Situational", "ARAM".</summary>
    public string? Role { get; init; }

    public required IReadOnlyList<TalentTier> Tiers { get; init; }

    /// <summary>Win rate in percent (e.g. 58.4) when a win-rate source covers this build; otherwise null.</summary>
    public double? WinRate { get; init; }

    public TalentTier? TierAt(int level)
    {
        int i = TierLevels.IndexForLevel(level);
        return i >= 0 && i < Tiers.Count ? Tiers[i] : null;
    }
}

/// <summary>All builds scraped for one hero.</summary>
public sealed class HeroBuildSet
{
    public required string HeroName { get; init; }

    public required string HeroSlug { get; init; }

    public required IReadOnlyList<HeroBuild> Builds { get; init; }

    public required DateTimeOffset ScrapedAtUtc { get; init; }

    /// <summary>When the source guide itself was last edited (Icy Veins' "Last updated"), if known.</summary>
    public DateTimeOffset? GuideUpdatedUtc { get; init; }
}
