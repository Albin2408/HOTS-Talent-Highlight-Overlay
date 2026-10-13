using System.Text.Json;
using HotsOverlay.Core;

namespace HotsOverlay.Scraper;

/// <summary>Reads/writes the small manifest (<c>_snapshot.json</c>) stored beside the bundled builds.</summary>
public static class SnapshotStore
{
    private const string FileName = "_snapshot.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SnapshotInfo? TryRead(string dir)
    {
        try
        {
            string path = Path.Combine(dir, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<SnapshotInfo>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public static void Write(string dir, SnapshotInfo info)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, FileName), JsonSerializer.Serialize(info, JsonOptions));
    }
}
