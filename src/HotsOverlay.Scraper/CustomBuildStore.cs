using System.Text.Json;
using HotsOverlay.Core;

namespace HotsOverlay.Scraper;

/// <summary>User-entered builds, one JSON file per hero under %AppData%/HotsOverlay/custom.</summary>
public sealed class CustomBuildStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _dir;

    public CustomBuildStore(string? rootDir = null)
    {
        _dir = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HotsOverlay", "custom");
    }

    private string PathFor(string heroSlug) => Path.Combine(_dir, $"{heroSlug}.json");

    public IReadOnlyList<HeroBuild> Load(string heroSlug)
    {
        string path = PathFor(heroSlug);
        if (!File.Exists(path))
            return Array.Empty<HeroBuild>();

        try
        {
            return JsonSerializer.Deserialize<List<HeroBuild>>(File.ReadAllText(path), JsonOptions)
                   ?? new List<HeroBuild>();
        }
        catch (JsonException)
        {
            return Array.Empty<HeroBuild>();
        }
    }

    /// <summary>Adds the build, replacing any existing custom build of the same name for that hero.</summary>
    public void Add(string heroSlug, HeroBuild build)
    {
        var builds = Load(heroSlug)
            .Where(b => !string.Equals(b.Name, build.Name, StringComparison.OrdinalIgnoreCase))
            .Append(build)
            .ToList();
        Write(heroSlug, builds);
    }

    public void Remove(string heroSlug, string buildName)
    {
        var builds = Load(heroSlug)
            .Where(b => !string.Equals(b.Name, buildName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Write(heroSlug, builds);
    }

    private void Write(string heroSlug, List<HeroBuild> builds)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor(heroSlug), JsonSerializer.Serialize(builds, JsonOptions));
    }
}
