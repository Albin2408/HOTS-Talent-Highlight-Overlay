using System.Net;
using System.Text;

namespace HotsOverlay.Scraper.Tests;

/// <summary>One talent tier of an invented guide: what the page would show for it.</summary>
internal sealed record SyntheticTier(
    int Level, string Pick, int OptionCount = 3, int PickPosition = 1, params string[] Alternates);

internal sealed record SyntheticBuild(string Name, string Role, params SyntheticTier[] Tiers);

/// <summary>
/// Renders HTML with the same structure the scraper expects from a build-guide page, using made-up
/// heroes and talents. This is what the public repository's parser tests run on, so none of the real
/// site's content has to be committed.
/// </summary>
internal static class SyntheticGuide
{
    public static readonly int[] StandardLevels = { 1, 4, 7, 10, 13, 16, 20 };

    /// <summary>A build whose talents are "{prefix} L{level}", with the pick in the given 1-based positions.</summary>
    public static SyntheticBuild Build(string name, string role, string prefix, int[]? levels = null, int[]? positions = null)
    {
        levels ??= StandardLevels;
        positions ??= Enumerable.Repeat(1, levels.Length).ToArray();
        return new SyntheticBuild(name, role, levels
            .Select((level, i) => new SyntheticTier(level, $"{prefix} Talent {level}", 3, positions[i]))
            .ToArray());
    }

    public static string HeroPage(IEnumerable<SyntheticBuild> builds, long? lastUpdatedEpoch = null)
    {
        var html = new StringBuilder("<html><body>\n");
        if (lastUpdatedEpoch is { } epoch)
            html.Append($"<div>Last updated <span data-time=\"{epoch}\" class=\"local_date\">on <span>some day</span></span></div>\n");

        html.Append("<div class=\"heroes_builds\">\n");
        foreach (var build in builds)
        {
            html.Append("  <div class=\"heroes_build\">\n");
            html.Append("    <div class=\"heroes_build_header\">\n");
            html.Append($"      <h3 class=\"toc_no_parsing\">{WebUtility.HtmlEncode(build.Name)} </h3>\n");
            html.Append($"      <span class=\"heroes_build_tag heroes_build_tag_x\"><span>{WebUtility.HtmlEncode(build.Role)}</span>" +
                        "<span class=\"heroes_build_tag_image\"></span></span>\n");
            html.Append("    </div>\n");
            html.Append("    <div class=\"heroes_build_talents\">\n");

            foreach (var tier in build.Tiers)
                AppendTier(html, tier);

            string digits = string.Concat(build.Tiers.Select(t => t.PickPosition));
            html.Append("      <div class=\"talent_build_footer\">" +
                        "<div class=\"heroes_build_talent_calculator_url\">" +
                        $"<a href=\"//calculator.example.invalid/sample#55.16!{digits}\">Open in calculator</a></div></div>\n");
            html.Append("    </div>\n  </div>\n");
        }

        return html.Append("</div>\n</body></html>").ToString();
    }

    private static void AppendTier(StringBuilder html, SyntheticTier tier)
    {
        html.Append("      <div class=\"heroes_build_talent_tier\">\n");
        html.Append($"        <span class=\"heroes_build_talent_tier_subtitle\">Level {tier.Level}</span>\n");
        html.Append("        <span class=\"heroes_build_talent_tier_visual\">");
        for (int position = 1; position <= tier.OptionCount; position++)
            html.Append(position == tier.PickPosition
                ? "<span class=\"heroes_build_talent_tier_yes\"></span>"
                : "<span class=\"heroes_build_talent_tier_no\"></span>");
        html.Append("</span>\n");

        html.Append("        <a class=\"heroes_build_talent_tier_recommended\" href=\"//x.invalid/t\" data-heroes-tooltip=\"t\">" +
                    $"<img src=\"//x.invalid/i.jpg\" alt=\"{WebUtility.HtmlEncode(tier.Pick)} Icon\" /></a>\n");

        foreach (var alternate in tier.Alternates)
            html.Append("        <a class=\"heroes_build_talent_tier_situational\" href=\"//x.invalid/t\" data-heroes-tooltip=\"t\">" +
                        $"<img src=\"//x.invalid/i.jpg\" alt=\"{WebUtility.HtmlEncode(alternate)} Icon\" />" +
                        "<span class=\"talent_marker talent_marker_maybe\">?</span></a>\n");

        html.Append("      </div>\n");
    }

    public static string IndexPage(params (string Slug, string Name)[] heroes)
    {
        var html = new StringBuilder("<html><body><div>\n");
        foreach (var (slug, name) in heroes)
            html.Append($"<a href=\"//www.icy-veins.com/heroes/{slug}-build-guide\"><img src=\"//x.invalid/p.jpg\" alt=\"{WebUtility.HtmlEncode(name)} Icon\">" +
                        $"<span>{WebUtility.HtmlEncode(name)}</span><span class=\"free_rotation_marker free_rotation_marker_inactive\"></span></a>\n");
        return html.Append("</div></body></html>").ToString();
    }
}
