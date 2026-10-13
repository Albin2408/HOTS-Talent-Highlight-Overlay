using System.Net.Http.Headers;
using System.Text.Json;
using HotsOverlay.Core;

namespace HotsOverlay.Scraper;

/// <summary>
/// Looks up the newest Heroes of the Storm patch from HeroesProfile's documented free, token-less
/// <c>/openApi/Patches</c> endpoint (intended for exactly this kind of use). Asked at most once a day —
/// the answer is cached — and any failure just means "unknown", never an error the user sees.
/// </summary>
public sealed class PatchClient
{
    public const string Url = "https://api.heroesprofile.com/openApi/Patches";

    private sealed record CacheEntry(DateTimeOffset FetchedAtUtc, string? Latest);

    private static readonly Lazy<HttpClient> DefaultHttp = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HotsOverlay", "0.1"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(personal-use patch check)"));
        return client;
    });

    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly TimeSpan _ttl;

    public PatchClient(HttpClient? http = null, string? cachePath = null, TimeSpan? ttl = null)
    {
        _http = http ?? DefaultHttp.Value;
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotsOverlay", "latest_patch.json");
        _ttl = ttl ?? TimeSpan.FromDays(1);
    }

    /// <summary>The newest patch like "2.57.0.98348", or null if it can't be determined.</summary>
    public async Task<string?> GetLatestPatchAsync(CancellationToken ct = default)
    {
        var cached = ReadCache();
        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAtUtc < _ttl)
            return cached.Latest;

        try
        {
            string json = await _http.GetStringAsync(Url, ct);
            string? latest = LatestFrom(json);
            if (latest is not null)
            {
                WriteCache(new CacheEntry(DateTimeOffset.UtcNow, latest));
                return latest;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // fall through: an old answer beats none
        }

        return cached?.Latest;
    }

    /// <summary>Major patches ("2.57", "2.55", ...), newest first. Not cached; used by the snapshot builder.</summary>
    public async Task<IReadOnlyList<string>> GetMajorPatchesAsync(CancellationToken ct = default)
    {
        string json = await _http.GetStringAsync(Url, ct);
        var byMajor = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
        return byMajor.Keys
            .OrderByDescending(k => k, Comparer<string>.Create(SnapshotFreshness.ComparePatches))
            .ToList();
    }

    /// <summary>Newest version in <c>{"2.57":["2.57.0.98348", ...], "2.55":[...]}</c>; null if unparseable.</summary>
    public static string? LatestFrom(string json)
    {
        try
        {
            var byMajor = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            return byMajor is null ? null : SnapshotFreshness.Newest(byMajor.Values.SelectMany(v => v));
        }
        catch (JsonException)
        {
            return null; // e.g. an HTML error page instead of the JSON
        }
    }

    private CacheEntry? ReadCache()
    {
        try
        {
            return File.Exists(_cachePath) ? JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(_cachePath)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private void WriteCache(CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(entry));
        }
        catch (IOException)
        {
            // caching is best-effort
        }
    }
}
