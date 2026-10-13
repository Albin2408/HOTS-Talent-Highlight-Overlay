using System.Globalization;
using System.Text;
using HotsOverlay.Core;

namespace HotsOverlay.Scraper;

/// <summary>
/// Folds win-rate builds into a hero's Icy Veins builds: an Icy Veins build that is identical to a
/// top win-rate build simply gets that win rate attached, and the remaining top builds are added
/// beside it so you can pick by what actually wins.
/// </summary>
public static class WinRateMerger
{
    public const string SourceLabel = "Heroes Profile";

    /// <summary>How many not-already-covered win-rate builds to add per hero (keeps the panel compact).</summary>
    public const int MaxExtraBuilds = 3;

    public static HeroBuildSet Merge(HeroBuildSet hero, IReadOnlyList<WinRateBuild> topBuilds)
    {
        var existing = hero.Builds.ToList();
        var covered = new HashSet<int>();

        for (int i = 0; i < existing.Count; i++)
        {
            for (int j = 0; j < topBuilds.Count; j++)
            {
                if (covered.Contains(j) || !SameTalents(existing[i], topBuilds[j]))
                    continue;

                existing[i] = new HeroBuild
                {
                    Name = existing[i].Name,
                    Role = existing[i].Role,
                    Tiers = existing[i].Tiers,
                    WinRate = topBuilds[j].WinRate,
                };
                covered.Add(j);
                break;
            }
        }

        var levels = existing.FirstOrDefault()?.Tiers.Select(t => t.Level).ToList() is { Count: 7 } own
            ? own
            : TierLevels.Levels.ToList();

        var added = topBuilds
            .Select((build, rank) => (build, rank))
            .Where(x => !covered.Contains(x.rank) && x.build.Talents.Count == levels.Count)
            .Take(MaxExtraBuilds)
            .Select(x => new HeroBuild
            {
                Name = $"Top win rate #{x.rank + 1}",
                Role = SourceLabel,
                WinRate = x.build.WinRate,
                Tiers = x.build.Talents.Select((name, i) => new TalentTier
                {
                    Level = levels[i],
                    OptionCount = 0, // positions unknown: the overlay finds cards by name
                    PickIndex = -1,
                    PickName = name,
                }).ToList(),
            });

        return new HeroBuildSet
        {
            HeroName = hero.HeroName,
            HeroSlug = hero.HeroSlug,
            ScrapedAtUtc = hero.ScrapedAtUtc,
            GuideUpdatedUtc = hero.GuideUpdatedUtc,
            Builds = existing.Concat(added).ToList(),
        };
    }

    /// <summary>Lowercase letters/digits only, accents dropped: "Lúcio" and "lucio", "E.T.C." and "etc" agree.</summary>
    public static string HeroKey(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static bool SameTalents(HeroBuild build, WinRateBuild other) =>
        build.Tiers.Count == other.Talents.Count
        && build.Tiers.Zip(other.Talents, (tier, name) =>
            TalentNameMatcher.Normalize(tier.PickName) == TalentNameMatcher.Normalize(name)).All(same => same);
}
