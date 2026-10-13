using System.IO;
using System.Windows;
using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.App;

public partial class App : Application
{
    public ActiveBuildState ActiveBuild { get; } = new();
    public PreferencesStore Prefs { get; } = new();
    public CustomBuildStore Custom { get; } = new();

    /// <summary>Bundled builds only: Icy Veins' Cloudflare rejects the app's own requests with a 403.</summary>
    public BuildCache Cache { get; } = new(seedDir: SeedDirectory(), allowNetwork: false);

    /// <summary>Every hero's builds, loaded in the background at startup (powers hero auto-detection).</summary>
    public Task<HeroCatalog> Catalog { get; }

    /// <summary>When the bundled builds were collected (null if the manifest is missing).</summary>
    public SnapshotInfo? Snapshot { get; } = SnapshotStore.TryRead(SeedDirectory());

    /// <summary>How current the bundled builds look; refined once the latest game patch is known.</summary>
    public DataFreshness? Freshness { get; private set; }

    public event Action? FreshnessChanged;

    private OverlayWindow? _overlayWindow;

    public App()
    {
        // Safety net: the overlay's OCR/screen-capture loop runs against a live game. Without this,
        // an unexpected exception there would take down the whole app, closing the picker too.
        DispatcherUnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("[HotsOverlay] Unhandled exception: " + e.Exception);
            MessageBox.Show(
                "An error occurred (see the console window for details):\n\n" + e.Exception.Message,
                "HOTS Talent Overlay - Error");
            e.Handled = true;
        };

        ActiveBuild.SetAutoDetect(Prefs.AutoDetectHero);
        Catalog = Task.Run(() => HeroCatalog.LoadAsync(Cache));

        // Age is known immediately; the newest game patch arrives a moment later (one small request a day).
        UpdateFreshness(latestPatch: null);
        _ = CheckLatestPatchAsync();
    }

    private async Task CheckLatestPatchAsync()
    {
        string? latest = await new PatchClient().GetLatestPatchAsync();
        if (latest is not null)
            await Dispatcher.InvokeAsync(() => UpdateFreshness(latest));
    }

    private void UpdateFreshness(string? latestPatch)
    {
        Freshness = Snapshot is null ? null : SnapshotFreshness.Evaluate(Snapshot, latestPatch, DateTimeOffset.UtcNow);
        FreshnessChanged?.Invoke();
    }

    /// <summary>Folder of bundled builds, next to the exe. Uses the exe's own folder rather than
    /// AppContext.BaseDirectory, which can point at a temp extraction folder in single-file builds.</summary>
    private static string SeedDirectory()
    {
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        string besideExe = Path.Combine(exeDir ?? "", "SeedBuilds");
        return Directory.Exists(besideExe) ? besideExe : Path.Combine(AppContext.BaseDirectory, "SeedBuilds");
    }

    public OverlayWindow EnsureOverlay()
    {
        _overlayWindow ??= new OverlayWindow();
        return _overlayWindow;
    }

    public void ShowOverlay() => EnsureOverlay().Show();

    /// <summary>The user picked a hero + build (in the picker); remember the build for next time.</summary>
    public void UseHero(string slug, string name, IReadOnlyList<HeroBuild> builds, int buildIndex, SelectionSource source)
    {
        ActiveBuild.SetHero(slug, name, builds, buildIndex, source);
        if (ActiveBuild.Build is { } build)
            Prefs.SetLastBuild(slug, build.Name);
    }

    /// <summary>Switches build for the current hero (panel click or hotkey) and remembers the choice.</summary>
    public void ChooseBuild(int index)
    {
        if (ActiveBuild.SelectBuild(index) && ActiveBuild is { HeroSlug: { } slug, Build: { } build })
            Prefs.SetLastBuild(slug, build.Name);
    }

    public void SetAutoDetect(bool on)
    {
        Prefs.AutoDetectHero = on;
        ActiveBuild.SetAutoDetect(on);
    }

    /// <summary>Hero recognized from the screen: adopt it with its remembered (or first) build.</summary>
    public void SwitchToDetectedHero(HeroBuildSet set)
    {
        var builds = set.Builds.Concat(Custom.Load(set.HeroSlug)).ToList();
        int index = ActiveBuildState.PreferredIndex(builds, Prefs.LastBuild(set.HeroSlug));

        ActiveBuild.SetHero(set.HeroSlug, set.HeroName, builds, index, SelectionSource.Auto);
    }
}
