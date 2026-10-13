using System.IO;
using System.Text.Json;

namespace HotsOverlay.Scraper;

/// <summary>Small persisted settings: the last build you used per hero, and the auto-detect switch.</summary>
public sealed class PreferencesStore
{
    private sealed class Data
    {
        public Dictionary<string, string> LastBuildByHero { get; set; } = new();
        public bool AutoDetectHero { get; set; } = true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Data _data;

    public PreferencesStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotsOverlay", "preferences.json");
        _data = Load(_path);
    }

    public bool AutoDetectHero
    {
        get => _data.AutoDetectHero;
        set
        {
            _data.AutoDetectHero = value;
            Save();
        }
    }

    public string? LastBuild(string heroSlug) => _data.LastBuildByHero.GetValueOrDefault(heroSlug);

    public void SetLastBuild(string heroSlug, string buildName)
    {
        _data.LastBuildByHero[heroSlug] = buildName;
        Save();
    }

    private static Data Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new Data();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // fall through to defaults: a corrupt prefs file must never stop the app starting
        }

        return new Data();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("[HotsOverlay] Couldn't save preferences: " + ex.Message);
        }
    }
}
