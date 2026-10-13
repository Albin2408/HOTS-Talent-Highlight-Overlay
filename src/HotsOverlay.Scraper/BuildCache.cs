using System.Text.Json;
using System.Text.Json.Serialization;
using HotsOverlay.Core;

namespace HotsOverlay.Scraper;

/// <summary>
/// Local disk cache for scraped hero builds under %AppData%/HotsOverlay/builds, so the app
/// doesn't re-fetch Icy Veins on every launch (single low-rate request per hero, ~7 day TTL).
/// If the live fetch fails (offline, or the site refuses non-browser clients), it falls back to
/// previously cached data, then to a small built-in sample set, and says so via
/// <see cref="DataSourceNotice"/> rather than leaving the app empty.
/// </summary>
public sealed class BuildCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dir;
    private readonly string? _seedDir;
    private readonly bool _allowNetwork;
    private readonly TimeSpan _ttl;
    private readonly Func<HeroSummary, CancellationToken, Task<HeroBuildSet>> _fetchBuilds;
    private readonly Func<CancellationToken, Task<IReadOnlyList<HeroSummary>>> _fetchIndex;

    /// <summary>Says where the last result came from when it wasn't a fresh live fetch (bundled
    /// snapshot, or saved data because fetching failed). Null after a successful live fetch.</summary>
    public string? DataSourceNotice { get; private set; }

    public BuildCache(
        string? rootDir = null,
        TimeSpan? ttl = null,
        string? seedDir = null,
        bool allowNetwork = true,
        Func<HeroSummary, CancellationToken, Task<HeroBuildSet>>? fetchBuilds = null,
        Func<CancellationToken, Task<IReadOnlyList<HeroSummary>>>? fetchIndex = null)
    {
        _dir = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HotsOverlay", "builds");
        _seedDir = seedDir;
        _allowNetwork = allowNetwork;
        _ttl = ttl ?? TimeSpan.FromDays(7);
        _fetchBuilds = fetchBuilds ?? HeroBuildScraper.FetchAsync;
        _fetchIndex = fetchIndex ?? HeroIndexScraper.FetchAsync;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Shown when there is no build data at all (the data isn't part of the public repository).</summary>
    public const string NoDataMessage =
        "No build data found. Generate it with tools/refresh-builds.sh (see the README), then restart the app.";

    private static string PathFor(string dir, string heroSlug) => Path.Combine(dir, $"{heroSlug}.json");

    private static string IndexPathFor(string dir) => Path.Combine(dir, "_hero_index.json");

    public HeroBuildSet? TryLoad(string heroSlug, bool ignoreTtl = false)
    {
        var set = ReadBuildSet(_dir, heroSlug);
        if (set is null)
            return null;
        if (!ignoreTtl && DateTimeOffset.UtcNow - set.ScrapedAtUtc > _ttl)
            return null;
        return set;
    }

    public void Save(HeroBuildSet set) =>
        File.WriteAllText(PathFor(_dir, set.HeroSlug), JsonSerializer.Serialize(set, JsonOptions));

    /// <summary>Saves the roster. <paramref name="collectedAt"/> is when the data was gathered, if that
    /// differs from now (the snapshot tool passes the date the pages were downloaded).</summary>
    public void SaveHeroIndex(IReadOnlyList<HeroSummary> heroes, DateTimeOffset? collectedAt = null) =>
        File.WriteAllText(IndexPathFor(_dir),
            JsonSerializer.Serialize(new HeroIndexCacheEntry(collectedAt ?? DateTimeOffset.UtcNow, heroes), JsonOptions));

    /// <summary>Returns the build set from the bundled snapshot (when network is disabled), a fresh
    /// cache, or a live fetch — falling back to saved/bundled data if the fetch fails.</summary>
    public async Task<HeroBuildSet> GetOrFetchAsync(HeroSummary hero, CancellationToken ct = default)
    {
        string? failure = null;

        if (_allowNetwork)
        {
            var cached = TryLoad(hero.Slug);
            if (cached is not null)
                return cached;

            try
            {
                var fresh = await _fetchBuilds(hero, ct);
                Save(fresh);
                DataSourceNotice = null;
                return fresh;
            }
            catch (Exception ex) when (IsFetchFailure(ex, ct))
            {
                failure = ex.Message;
            }
        }

        // Bundled snapshot first when network is off (it's the freshest thing we have); otherwise
        // saved data first, since a failed fetch usually means it's the better of the two.
        HeroBuildSet? set;
        string source;
        if (_allowNetwork)
        {
            (set, source) = TryLoad(hero.Slug, ignoreTtl: true) is { } saved
                ? (saved, "previously saved builds")
                : BundledBuildSet(hero.Slug);
        }
        else
        {
            (set, source) = BundledBuildSet(hero.Slug) is { Set: not null } bundled
                ? bundled
                : (TryLoad(hero.Slug, ignoreTtl: true), "previously saved builds");
        }

        if (set is null)
            throw new BuildDataUnavailableException(
                failure is null
                    ? $"No builds are bundled or saved for {hero.Name}."
                    : $"Couldn't load builds for {hero.Name} ({failure}) and there's no saved or bundled data for them.");

        DataSourceNotice = failure is null
            ? $"Using {source}."
            : $"Couldn't reach Icy Veins ({failure}) — showing {source}.";
        return set;
    }

    private (HeroBuildSet? Set, string Source) BundledBuildSet(string heroSlug)
    {
        var set = _seedDir is null ? null : ReadBuildSet(_seedDir, heroSlug);
        return (set, set is null ? "" : $"bundled builds collected {set.ScrapedAtUtc:yyyy-MM-dd}");
    }

    private sealed record HeroIndexCacheEntry(DateTimeOffset ScrapedAtUtc, IReadOnlyList<HeroSummary> Heroes);

    /// <summary>Returns the hero roster from the bundled snapshot (when network is disabled), a
    /// fresh cache, or a live fetch — falling back to saved/bundled data if the fetch fails.</summary>
    public async Task<IReadOnlyList<HeroSummary>> GetOrFetchHeroIndexAsync(CancellationToken ct = default)
    {
        string? failure = null;
        var saved = ReadIndex(_dir);
        var bundled = _seedDir is null ? null : ReadIndex(_seedDir);

        if (_allowNetwork)
        {
            if (saved is not null && DateTimeOffset.UtcNow - saved.ScrapedAtUtc <= _ttl)
                return saved.Heroes;

            try
            {
                var heroes = await _fetchIndex(ct);
                SaveHeroIndex(heroes);
                DataSourceNotice = null;
                return heroes;
            }
            catch (Exception ex) when (IsFetchFailure(ex, ct))
            {
                failure = ex.Message;
            }
        }

        var entry = _allowNetwork ? saved ?? bundled : bundled ?? saved;
        if (entry is null)
            throw new BuildDataUnavailableException(
                failure is null ? NoDataMessage : $"Couldn't load the hero list ({failure}). " + NoDataMessage);

        string source = ReferenceEquals(entry, bundled)
            ? $"bundled builds for {entry.Heroes.Count} heroes (collected {entry.ScrapedAtUtc:yyyy-MM-dd})"
            : "the previously saved hero list";
        DataSourceNotice = failure is null
            ? $"Using {source}."
            : $"Couldn't reach Icy Veins ({failure}) — showing {source}.";
        return entry.Heroes;
    }

    private static bool IsFetchFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException;

    private static HeroBuildSet? ReadBuildSet(string dir, string heroSlug)
    {
        string path = PathFor(dir, heroSlug);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<HeroBuildSet>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HeroIndexCacheEntry? ReadIndex(string dir)
    {
        string path = IndexPathFor(dir);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<HeroIndexCacheEntry>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Builds couldn't be fetched and there was nothing cached or bundled to fall back on.</summary>
public sealed class BuildDataUnavailableException(string message) : Exception(message);
