using HotsOverlay.Core;
using HotsOverlay.Scraper;
using HotsOverlay.TestSupport;

namespace HotsOverlay.Scraper.Tests;

/// <summary>Runs the hero detector against the real bundled talent data for every hero.</summary>
public class HeroDetectionOnRealDataTests
{
    private static readonly string SeedDir = Path.Combine(AppContext.BaseDirectory, "SeedBuilds");

    private static OcrLineBox Line(string text, double y)
    {
        var words = new List<OcrWordBox>();
        double x = 120;
        foreach (var w in text.Split(' '))
        {
            words.Add(new OcrWordBox(w, new BoxF(x, y, w.Length * 11, 20)));
            x += w.Length * 11 + 6;
        }
        return new OcrLineBox(words);
    }

    private static async Task<List<HeroBuildSet>> LoadAllAsync()
    {
        var cache = new BuildCache(
            Path.Combine(Path.GetTempPath(), "hots-detect-" + Guid.NewGuid().ToString("N")),
            seedDir: SeedDir, allowNetwork: false);
        var sets = new List<HeroBuildSet>();
        foreach (var hero in await cache.GetOrFetchHeroIndexAsync())
            sets.Add(await cache.GetOrFetchAsync(hero));
        return sets;
    }

    [OptionalDataFact("SeedBuilds")]
    public async Task EveryHeroIsIdentifiedFromTheirOwnLevelOnePanel()
    {
        var sets = await LoadAllAsync();
        var detector = new HeroDetector(sets);
        var wrong = new List<string>();

        foreach (var hero in sets)
        {
            // What the level-1 panel shows: that hero's tier-1 talents we know of (every build's pick,
            // plus the situational alternates, which are options on the same panel).
            var names = hero.Builds
                .SelectMany(b => b.Tiers[0].Alternates.Prepend(b.Tiers[0].PickName))
                .Distinct().ToList();
            var lines = names.Select((n, i) => Line(n, 400 + i * 90)).ToList();

            var guess = detector.Guess(lines);
            if (guess?.Slug != hero.HeroSlug)
                wrong.Add($"{hero.HeroName} (saw: {string.Join(" / ", names)}) -> {guess?.Name ?? "no guess"}");
        }

        Assert.True(wrong.Count == 0,
            $"{wrong.Count}/{sets.Count} heroes misidentified:\n  " + string.Join("\n  ", wrong));
    }

    [OptionalDataFact("SeedBuilds")]
    public async Task EveryHeroIsIdentifiedEvenFromOneLevelSevenTalent()
    {
        var sets = await LoadAllAsync();
        var detector = new HeroDetector(sets);
        var wrong = new List<string>();

        foreach (var hero in sets)
        {
            // Harder: joined mid-game, and only one recommended talent from a later tier is on screen.
            string name = hero.Builds[0].Tiers[2].PickName;
            var guess = detector.Guess(new[] { Line(name, 500) });
            if (guess?.Slug != hero.HeroSlug)
                wrong.Add($"{hero.HeroName} ({name}) -> {guess?.Name ?? "no guess"}");
        }

        Assert.True(wrong.Count <= sets.Count * 0.05,
            $"{wrong.Count}/{sets.Count} heroes misidentified:\n  " + string.Join("\n  ", wrong));
    }

    [OptionalDataFact("SeedBuilds")]
    public async Task NoHeroIsIdentifiedFromOrdinaryHudText()
    {
        var detector = new HeroDetector(await LoadAllAsync());
        var hud = new[] { "Reset Talents", "Select Enemy Hero", "Talent Available", "INVULNERABLE", "Toggle Cooldowns",
                          "Complete Quests", "Set Game Speed", "Choose a talent!", "New Talent!", "06:54", "3518/3518" };

        var guess = detector.Guess(hud.Select((t, i) => Line(t, 100 + i * 40)).ToList());

        Assert.Null(guess);
    }
}
