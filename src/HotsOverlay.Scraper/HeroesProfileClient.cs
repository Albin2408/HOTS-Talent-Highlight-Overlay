using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotsOverlay.Scraper;

/// <summary>One of a hero's most successful talent builds: its win rate (percent) and the 7 talent names.</summary>
public sealed record WinRateBuild(double WinRate, IReadOnlyList<string> Talents);

/// <summary>The token was rejected, or the plan doesn't include the endpoint, or the reply wasn't JSON.</summary>
public sealed class HeroesProfileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Client for HeroesProfile's "Hero Talent Builds" endpoint, which returns each hero's top builds by
/// win rate. It needs a personal, paid <c>api_token</c> (supplied by the user — never stored by this
/// app), and per HeroesProfile's terms on the lower plans the data must be pulled and stored rather
/// than called live from an app, which is why it is only used by the snapshot-building tool.
/// </summary>
public sealed class HeroesProfileClient
{
    private const string BuildsUrl = "https://api.heroesprofile.com/api/Heroes/Talents/Builds";

    private static readonly Lazy<HttpClient> DefaultHttp = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HotsOverlay", "0.1"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(personal-use snapshot builder)"));
        return client;
    });

    private sealed class RawBuild
    {
        [JsonPropertyName("win_rate")] public double WinRate { get; set; }
        [JsonPropertyName("build_talents")] public List<string>? BuildTalents { get; set; }
    }

    private readonly string _token;
    private readonly HttpClient _http;

    public HeroesProfileClient(string apiToken, HttpClient? http = null)
    {
        _token = apiToken;
        _http = http ?? DefaultHttp.Value;
    }

    /// <summary>Top builds per hero (keyed by the hero name HeroesProfile uses), best win rate first.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<WinRateBuild>>> GetTopBuildsAsync(
        string majorPatch, string gameType = "Storm League", CancellationToken ct = default)
    {
        string url = $"{BuildsUrl}?mode=json&timeframe_type=major&timeframe={Uri.EscapeDataString(majorPatch)}" +
                     $"&game_type={Uri.EscapeDataString(gameType)}&api_token={Uri.EscapeDataString(_token)}";

        string body;
        try
        {
            body = await _http.GetStringAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            // Deliberately not ex.ToString(): keep the token-bearing URL out of any log.
            throw new HeroesProfileException($"HeroesProfile request failed: {ex.Message}");
        }

        return ParseBuilds(body);
    }

    /// <summary>Parses <c>{"Abathur":[{"win_rate":59.8,"build_talents":[...7 names...]}], ...}</c>.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<WinRateBuild>> ParseBuilds(string json)
    {
        Dictionary<string, List<RawBuild>>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, List<RawBuild>>>(json);
        }
        catch (JsonException ex)
        {
            throw new HeroesProfileException(
                "HeroesProfile didn't return build data (an HTML page usually means the api_token is missing, " +
                "invalid, or its plan doesn't include this endpoint).", ex);
        }

        var result = new Dictionary<string, IReadOnlyList<WinRateBuild>>();
        foreach (var (hero, builds) in raw ?? new())
        {
            var usable = builds
                .Where(b => b.BuildTalents is { Count: > 0 } && b.BuildTalents.All(t => !string.IsNullOrWhiteSpace(t)))
                .OrderByDescending(b => b.WinRate)
                .Select(b => new WinRateBuild(b.WinRate, b.BuildTalents!))
                .ToList();

            if (usable.Count > 0)
                result[hero] = usable;
        }

        return result;
    }
}
