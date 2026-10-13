using System.Text.RegularExpressions;

namespace HotsOverlay.Core;

/// <summary>
/// Builds a <see cref="HeroBuild"/> from talent names the user typed or pasted (from any guide,
/// or straight off the in-game talent cards). The overlay finds talents on screen by name, so
/// names are all it needs — no option positions.
/// </summary>
public static partial class CustomBuild
{
    public const string RoleLabel = "Custom";

    public static bool TryCreate(string buildName, string talentNamesText, out HeroBuild? build, out string? error)
    {
        build = null;

        if (string.IsNullOrWhiteSpace(buildName))
        {
            error = "Give the build a name.";
            return false;
        }

        var names = talentNamesText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanLine)
            .Where(line => line.Length > 0)
            .ToList();

        int expected = TierLevels.Levels.Count;
        if (names.Count != expected)
        {
            error = $"Enter {expected} talent names, one per line (levels {string.Join(", ", TierLevels.Levels)}) — got {names.Count}.";
            return false;
        }

        build = new HeroBuild
        {
            Name = buildName.Trim(),
            Role = RoleLabel,
            Tiers = TierLevels.Levels
                .Select((level, i) => new TalentTier
                {
                    Level = level,
                    OptionCount = 0, // unknown: custom builds only carry names
                    PickIndex = -1,
                    PickName = names[i],
                })
                .ToList(),
        };
        error = null;
        return true;
    }

    /// <summary>Trims whitespace and a leading "Level 4:" / "L4 -" / "4." style label, if present.</summary>
    internal static string CleanLine(string line) => LeadingLabel().Replace(line.Trim(), "").Trim();

    [GeneratedRegex(@"^(?:level\s*|lv\.?\s*|l)?\d{1,2}\s*[:.)\-–—]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLabel();
}
