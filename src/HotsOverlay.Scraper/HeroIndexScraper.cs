using System.Text.RegularExpressions;
using HotsOverlay.Core;

namespace HotsOverlay.Scraper;

/// <summary>Fetches the full hero roster from the Icy Veins heroes index page.</summary>
public static partial class HeroIndexScraper
{
    public static async Task<IReadOnlyList<HeroSummary>> FetchAsync(CancellationToken ct = default)
    {
        string html = await IcyVeinsClient.Http.GetStringAsync("heroes/", ct);
        return Parse(html);
    }

    public static IReadOnlyList<HeroSummary> Parse(string html)
    {
        var results = new List<HeroSummary>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in HeroLinkRegex().Matches(html))
        {
            string slug = m.Groups["slug"].Value;
            string name = System.Net.WebUtility.HtmlDecode(m.Groups["name"].Value).Trim();
            if (string.IsNullOrEmpty(name) || !seen.Add(slug))
                continue;
            results.Add(new HeroSummary(slug, name));
        }

        return results;
    }

    [GeneratedRegex(
        """<a href="//www\.icy-veins\.com/heroes/(?<slug>[a-z0-9-]+)-build-guide"[^>]*>.*?<span>(?<name>[^<]+)</span>""",
        RegexOptions.Singleline)]
    private static partial Regex HeroLinkRegex();
}
