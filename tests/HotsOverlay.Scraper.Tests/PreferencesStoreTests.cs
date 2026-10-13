using HotsOverlay.Scraper;

namespace HotsOverlay.Scraper.Tests;

public class PreferencesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hots-prefs-tests-" + Guid.NewGuid().ToString("N"));

    private string PrefsPath => Path.Combine(_dir, "preferences.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Defaults_AutoDetectOnAndNothingRemembered()
    {
        var prefs = new PreferencesStore(PrefsPath);

        Assert.True(prefs.AutoDetectHero);
        Assert.Null(prefs.LastBuild("anduin"));
    }

    [Fact]
    public void ChoicesSurviveARestart()
    {
        var first = new PreferencesStore(PrefsPath);
        first.SetLastBuild("anduin", "Lightwell Build");
        first.SetLastBuild("tyrande", "Hunter Build");
        first.AutoDetectHero = false;

        var second = new PreferencesStore(PrefsPath);

        Assert.Equal("Lightwell Build", second.LastBuild("anduin"));
        Assert.Equal("Hunter Build", second.LastBuild("tyrande"));
        Assert.False(second.AutoDetectHero);
    }

    [Fact]
    public void ACorruptFileFallsBackToDefaultsInsteadOfCrashing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PrefsPath, "{ this is not json");

        var prefs = new PreferencesStore(PrefsPath);

        Assert.True(prefs.AutoDetectHero);
        Assert.Null(prefs.LastBuild("anduin"));
    }
}
