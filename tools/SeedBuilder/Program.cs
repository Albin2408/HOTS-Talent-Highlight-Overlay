// Turns a folder of downloaded Icy Veins build-guide pages (see tools/fetch-pages.sh) into the bundled
// snapshot the app ships with. It parses pages offline; the only network calls it can make are to
// HeroesProfile — the free patch list (to record the newest patch in the snapshot manifest), and,
// only with --win-rates, the token-authenticated builds endpoint.
//
//   dotnet run --project tools/SeedBuilder -- <pages-dir> <seed-out-dir> [--win-rates]
//
// --win-rates needs your own HeroesProfile API token in the HEROESPROFILE_API_TOKEN environment
// variable (a paid HeroesProfile plan). The token is read from the environment only and never saved.
using HotsOverlay.Core;
using HotsOverlay.Scraper;

var positional = args.Where(a => !a.StartsWith("--")).ToList();
bool wantWinRates = args.Contains("--win-rates");
if (positional.Count != 2)
{
    Console.Error.WriteLine("usage: SeedBuilder <pages-dir> <seed-out-dir> [--win-rates]");
    return 2;
}

string pagesDir = positional[0], outDir = positional[1];

string? token = Environment.GetEnvironmentVariable("HEROESPROFILE_API_TOKEN");
if (wantWinRates && string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("--win-rates needs your HeroesProfile API token in the HEROESPROFILE_API_TOKEN environment variable.");
    return 2;
}

var roster = HeroIndexScraper.Parse(File.ReadAllText(Path.Combine(pagesDir, "_index.html")));
Directory.CreateDirectory(outDir);

// Remember what the last snapshot said, so we can report what changed.
var previous = new Dictionary<string, string>();
var oldCache = new BuildCache(outDir);
foreach (var hero in roster)
    if (oldCache.TryLoad(hero.Slug, ignoreTtl: true) is { } old)
        previous[hero.Slug] = Signature(old);
foreach (var file in Directory.GetFiles(outDir, "*.json"))
    File.Delete(file);

var cache = new BuildCache(outDir);
var included = new List<HeroBuildSet>();
var problems = new List<string>();

foreach (var hero in roster)
{
    string page = Path.Combine(pagesDir, hero.Slug + ".html");
    if (!File.Exists(page))
    {
        problems.Add($"{hero.Name}: no downloaded page");
        continue;
    }

    var parsed = HeroBuildScraper.Parse(File.ReadAllText(page), hero);
    var good = new List<HeroBuild>();
    foreach (var build in parsed.Builds)
    {
        string? flaw =
            build.Tiers.Count != TierLevels.Levels.Count ? $"{build.Tiers.Count} tiers" :
            // Most heroes talent at 1/4/7/10/13/16/20, but a few (e.g. Chromie) use other levels.
            !build.Tiers.Zip(build.Tiers.Skip(1), (a, b) => a.Level < b.Level).All(ok => ok) ? "tier levels out of order" :
            build.Tiers.Any(t => t.PickName == "Unknown" || string.IsNullOrWhiteSpace(t.PickName)) ? "unnamed talent" :
            null;

        if (flaw is null) good.Add(build);
        else problems.Add($"{hero.Name} / {build.Name}: dropped ({flaw})");
    }

    if (good.Count == 0)
    {
        problems.Add($"{hero.Name}: no usable builds");
        continue;
    }

    included.Add(new HeroBuildSet
    {
        HeroName = hero.Name,
        HeroSlug = hero.Slug,
        Builds = good,
        ScrapedAtUtc = File.GetLastWriteTimeUtc(page), // when the page was collected, not when we parsed it
        GuideUpdatedUtc = parsed.GuideUpdatedUtc,
    });
}

// Optional: attach HeroesProfile win rates.
int withWinRates = 0;
if (wantWinRates)
{
    var topByHero = await FetchWinRatesAsync(token!);
    var byKey = topByHero.ToDictionary(kv => WinRateMerger.HeroKey(kv.Key), kv => kv.Value);
    var unmatchedHeroes = new HashSet<string>(topByHero.Keys);

    for (int i = 0; i < included.Count; i++)
    {
        string key = WinRateMerger.HeroKey(included[i].HeroName);
        if (!byKey.TryGetValue(key, out var tops) && !byKey.TryGetValue(WinRateMerger.HeroKey(included[i].HeroSlug), out tops))
        {
            problems.Add($"{included[i].HeroName}: no win-rate data");
            continue;
        }

        unmatchedHeroes.RemoveWhere(name => WinRateMerger.HeroKey(name) == key
                                            || WinRateMerger.HeroKey(name) == WinRateMerger.HeroKey(included[i].HeroSlug));
        included[i] = WinRateMerger.Merge(included[i], tops);
        withWinRates++;
    }

    foreach (var name in unmatchedHeroes)
        problems.Add($"HeroesProfile hero '{name}' matched no Icy Veins hero");
}

foreach (var set in included)
    cache.Save(set);
var collected = File.GetLastWriteTimeUtc(Path.Combine(pagesDir, "_index.html"));
cache.SaveHeroIndex(
    included.Select(s => new HeroSummary(s.HeroSlug, s.HeroName)).ToList(),
    new DateTimeOffset(collected, TimeSpan.Zero));

// Manifest: when this snapshot was collected, and the newest game patch known right now.
string? latestPatch = await new PatchClient(cachePath: Path.Combine(outDir, ".patch-cache.tmp"), ttl: TimeSpan.Zero)
    .GetLatestPatchAsync();
File.Delete(Path.Combine(outDir, ".patch-cache.tmp"));
SnapshotStore.Write(outDir, new SnapshotInfo(new DateTimeOffset(collected, TimeSpan.Zero), latestPatch));

int buildCount = included.Sum(s => s.Builds.Count);
Console.WriteLine($"{included.Count}/{roster.Count} heroes, {buildCount} builds written to {outDir}");
Console.WriteLine($"snapshot: collected {collected:yyyy-MM-dd}, latest game patch {latestPatch ?? "unknown"}");
if (wantWinRates)
    Console.WriteLine($"win rates attached for {withWinRates} heroes");

var changed = included.Where(s => previous.TryGetValue(s.HeroSlug, out var sig) && sig != Signature(s)).Select(s => s.HeroName).ToList();
var added = included.Where(s => !previous.ContainsKey(s.HeroSlug)).Select(s => s.HeroName).ToList();
if (previous.Count > 0)
{
    Console.WriteLine($"since the previous snapshot: {changed.Count} heroes changed, {added.Count} new");
    if (changed.Count > 0)
        Console.WriteLine("  changed: " + string.Join(", ", changed.Take(25)) + (changed.Count > 25 ? ", ..." : ""));
}

foreach (var problem in problems)
    Console.WriteLine("  ! " + problem);
return problems.Count == 0 ? 0 : 1;

// ---- helpers ----

static string Signature(HeroBuildSet set) => string.Join("|", set.Builds.Select(b =>
    b.Name + ":" + b.WinRate + ":" + string.Join(",", b.Tiers.Select(t => t.PickName + "+" + string.Join("/", t.Alternates)))));

static async Task<IReadOnlyDictionary<string, IReadOnlyList<WinRateBuild>>> FetchWinRatesAsync(string token)
{
    var patches = await new PatchClient().GetMajorPatchesAsync();
    var client = new HeroesProfileClient(token);

    // The newest patch may be too fresh to have much data, so fall back to the one before it.
    foreach (string major in patches.Take(2))
    {
        var tops = await client.GetTopBuildsAsync(major);
        Console.WriteLine($"HeroesProfile {major}: {tops.Count} heroes with build data");
        if (tops.Count > 0)
            return tops;
    }

    return new Dictionary<string, IReadOnlyList<WinRateBuild>>();
}
