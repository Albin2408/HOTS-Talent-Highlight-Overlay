using System.Drawing;
using System.Drawing.Text;
using HotsOverlay.Core;
using HotsOverlay.Scraper;
using HotsOverlay.TestSupport;
using HotsOverlay.Vision;

namespace HotsOverlay.Vision.Tests;

/// <summary>
/// The whole recognition chain on every hero in the bundled data: render that hero's level-1 talent
/// names like the in-game panel, read them with the real Windows OCR engine, and ask the detector who
/// it is. Exercises OCR fidelity across ~180 real talent names (apostrophes, colons, hyphens...).
/// </summary>
public class EndToEndDetectionTests
{
    private static Bitmap RenderPanel(IReadOnlyList<string> titles)
    {
        var bmp = new Bitmap(560, 60 + titles.Count * 100, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(28, 30, 70));
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var titleFont = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
        using var descFont = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);

        for (int i = 0; i < titles.Count; i++)
        {
            float y = 30 + i * 100;
            g.FillRectangle(new SolidBrush(Color.FromArgb(40, 44, 100)), 16, y - 12, 528, 84);
            g.FillEllipse(Brushes.Orange, 30, y + 6, 44, 44);
            g.DrawString(titles[i], titleFont, Brushes.White, 90, y);
            g.DrawString("Deals bonus damage to the nearest enemy hero", descFont, Brushes.LightGray, 90, y + 26);
        }

        return bmp;
    }

    [OptionalDataFact("SeedBuilds")]
    public async Task OcrThenDetector_IdentifiesTheHeroFromARenderedLevelOnePanel()
    {
        Assert.True(TextScanner.IsAvailable, "Windows OCR engine isn't available on this machine.");

        var cache = new BuildCache(
            Path.Combine(Path.GetTempPath(), "hots-e2e-" + Guid.NewGuid().ToString("N")),
            seedDir: Path.Combine(AppContext.BaseDirectory, "SeedBuilds"), allowNetwork: false);
        var sets = new List<HeroBuildSet>();
        foreach (var hero in await cache.GetOrFetchHeroIndexAsync())
            sets.Add(await cache.GetOrFetchAsync(hero));
        var detector = new HeroDetector(sets);

        var wrong = new List<string>();
        foreach (var hero in sets)
        {
            var titles = hero.Builds.Select(b => b.Tiers[0].PickName).Distinct().ToList();
            using var panel = RenderPanel(titles);
            var lines = await TextScanner.ScanOrThrowAsync(panel);

            var guess = detector.Guess(lines);
            if (guess?.Slug != hero.HeroSlug)
            {
                string read = string.Join(" | ", lines.Select(l => l.Text).Where(t => t.Length < 40));
                wrong.Add($"{hero.HeroName}: drew [{string.Join(" / ", titles)}] OCR read [{read}] -> {guess?.Name ?? "no guess"}");
            }
        }

        Assert.True(wrong.Count <= sets.Count * 0.08,
            $"{wrong.Count}/{sets.Count} heroes misidentified:\n  " + string.Join("\n  ", wrong));
    }
}
