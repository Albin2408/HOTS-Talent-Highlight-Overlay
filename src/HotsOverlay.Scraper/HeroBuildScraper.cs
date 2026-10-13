using System.Text.RegularExpressions;
using HotsOverlay.Core;
using HtmlAgilityPack;

namespace HotsOverlay.Scraper;

/// <summary>
/// Scrapes a hero's "-build-guide" page on Icy Veins. Each build's talent-calculator link
/// (e.g. "#55.16!3232312") encodes the 1-based pick position per tier, one digit per tier
/// in Level 1/4/7/10/13/16/20 order — this is far more reliable than parsing icon order, so
/// it's the primary source of <see cref="TalentTier.PickIndex"/>. The tier's "visual dots"
/// (yes/no/situational spans) are used only as a cross-check / fallback.
/// </summary>
public static partial class HeroBuildScraper
{
    public static async Task<HeroBuildSet> FetchAsync(HeroSummary hero, CancellationToken ct = default)
    {
        string html = await IcyVeinsClient.Http.GetStringAsync($"heroes/{hero.Slug}-build-guide", ct);
        return Parse(html, hero);
    }

    public static HeroBuildSet Parse(string html, HeroSummary hero)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var builds = new List<HeroBuild>();

        foreach (var heading in doc.DocumentNode.SelectNodes("//h3[@class='toc_no_parsing']")
                                    ?? Enumerable.Empty<HtmlNode>())
        {
            // <h3> sits in a "heroes_build_header" div; the talent tray is that div's next sibling.
            var talentsDiv = heading.SelectSingleNode(
                "parent::div/following-sibling::div[contains(concat(' ', normalize-space(@class), ' '), ' heroes_build_talents ')][1]");
            if (talentsDiv is null)
                continue;

            string? role = heading.SelectSingleNode(
                "following-sibling::span[contains(@class,'heroes_build_tag')]//span[1]")?.InnerText.Trim();

            var build = ParseBuild(heading.InnerText.Trim(), role, talentsDiv);
            if (build is not null)
                builds.Add(build);
        }

        return new HeroBuildSet
        {
            HeroName = hero.Name,
            HeroSlug = hero.Slug,
            Builds = builds,
            ScrapedAtUtc = DateTimeOffset.UtcNow,
            GuideUpdatedUtc = ParseGuideUpdated(html),
        };
    }

    private static HeroBuild? ParseBuild(string name, string? role, HtmlNode talentsDiv)
    {
        var tierNodes = talentsDiv.SelectNodes(
            ".//div[contains(concat(' ', normalize-space(@class), ' '), ' heroes_build_talent_tier ')]");
        if (tierNodes is null || tierNodes.Count == 0)
            return null;

        string? calculatorDigits = ExtractCalculatorDigits(talentsDiv);

        var tiers = new List<TalentTier>();
        for (int i = 0; i < tierNodes.Count; i++)
        {
            var tier = ParseTier(tierNodes[i], calculatorDigits, i);
            if (tier is not null)
                tiers.Add(tier);
        }

        return tiers.Count == 0 ? null : new HeroBuild { Name = name, Role = role, Tiers = tiers };
    }

    private static TalentTier? ParseTier(HtmlNode tierNode, string? calculatorDigits, int tierPosition)
    {
        var subtitle = tierNode.SelectSingleNode(".//span[contains(@class,'heroes_build_talent_tier_subtitle')]");
        var levelMatch = subtitle is null ? null : LevelRegex().Match(subtitle.InnerText);
        if (levelMatch is not { Success: true })
            return null;
        int level = int.Parse(levelMatch.Groups["level"].Value);

        var dotSpans = tierNode.SelectNodes(".//span[contains(@class,'heroes_build_talent_tier_visual')]/span");
        int optionCount = dotSpans?.Count ?? 0;
        int visualYesIndex = -1;
        if (dotSpans is not null)
        {
            for (int i = 0; i < dotSpans.Count; i++)
            {
                if (dotSpans[i].GetAttributeValue("class", "").Contains("heroes_build_talent_tier_yes"))
                {
                    visualYesIndex = i;
                    break;
                }
            }
        }

        var recommended = tierNode.SelectSingleNode(".//a[contains(@class,'heroes_build_talent_tier_recommended')]");
        var img = recommended?.SelectSingleNode(".//img");
        string pickName = CleanTalentName(img?.GetAttributeValue("alt", null));

        int pickIndex = visualYesIndex >= 0 ? visualYesIndex : 0;
        if (calculatorDigits is not null && tierPosition < calculatorDigits.Length)
        {
            int fromCalculator = calculatorDigits[tierPosition] - '1';
            if (fromCalculator >= 0)
                pickIndex = fromCalculator;
        }

        if (optionCount == 0)
            optionCount = pickIndex + 1;

        // Situational alternates sit in the same tier block as extra links, each tagged with a "?".
        var alternates = (tierNode.SelectNodes(".//a[contains(@class,'heroes_build_talent_tier_situational')]//img")
                          ?? Enumerable.Empty<HtmlNode>())
            .Select(img => CleanTalentName(img.GetAttributeValue("alt", null)))
            .Where(name => name != "Unknown" && name != pickName)
            .Distinct()
            .ToList();

        return new TalentTier
        {
            Level = level,
            OptionCount = optionCount,
            PickIndex = pickIndex,
            PickName = pickName,
            Alternates = alternates,
        };
    }

    /// <summary>The guide's "Last updated" stamp: a unix time in a data-time attribute.</summary>
    private static DateTimeOffset? ParseGuideUpdated(string html)
    {
        var match = GuideUpdatedRegex().Match(html);
        return match.Success && long.TryParse(match.Groups["epoch"].Value, out long epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;
    }

    private static string? ExtractCalculatorDigits(HtmlNode talentsDiv)
    {
        var link = talentsDiv.SelectSingleNode(
            ".//div[contains(@class,'heroes_build_talent_calculator_url')]//a[@href]");
        string? href = link?.GetAttributeValue("href", null);
        if (href is null)
            return null;

        var match = CalculatorHashRegex().Match(href);
        return match.Success ? match.Groups["digits"].Value : null;
    }

    private static string CleanTalentName(string? alt) =>
        string.IsNullOrWhiteSpace(alt) ? "Unknown" : IconSuffixRegex().Replace(alt.Trim(), "");

    [GeneratedRegex(@"Level\s+(?<level>\d+)")]
    private static partial Regex LevelRegex();

    [GeneratedRegex(@"#[^!]*!(?<digits>\d+)")]
    private static partial Regex CalculatorHashRegex();

    [GeneratedRegex(@"Last updated\s*<span data-time=""(?<epoch>\d+)""")]
    private static partial Regex GuideUpdatedRegex();

    [GeneratedRegex(@"\s+Icon$")]
    private static partial Regex IconSuffixRegex();
}
