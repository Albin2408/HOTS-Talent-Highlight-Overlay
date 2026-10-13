using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.Scraper.Tests;

public class CustomBuildStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hots-custom-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static HeroBuild Build(string name, string firstTalent)
    {
        CustomBuild.TryCreate(name, $"{firstTalent}\nb\nc\nd\ne\nf\ng", out var build, out var error);
        return build ?? throw new InvalidOperationException(error);
    }

    [Fact]
    public void Load_ReturnsNothingForAHeroWithNoCustomBuilds() =>
        Assert.Empty(new CustomBuildStore(_root).Load("diablo"));

    [Fact]
    public void AddThenLoad_RoundTripsTheBuild()
    {
        var store = new CustomBuildStore(_root);

        store.Add("diablo", Build("Overpower", "Shadow Charge"));
        var loaded = Assert.Single(store.Load("diablo"));

        Assert.Equal("Overpower", loaded.Name);
        Assert.Equal(CustomBuild.RoleLabel, loaded.Role);
        Assert.Equal("Shadow Charge", loaded.Tiers[0].PickName);
        Assert.Equal(7, loaded.Tiers.Count);
    }

    [Fact]
    public void Add_ReplacesAnExistingBuildWithTheSameName()
    {
        var store = new CustomBuildStore(_root);

        store.Add("diablo", Build("Overpower", "Old"));
        store.Add("diablo", Build("overpower", "New"));

        Assert.Equal("New", Assert.Single(store.Load("diablo")).Tiers[0].PickName);
    }

    [Fact]
    public void Remove_DeletesOnlyThatBuild()
    {
        var store = new CustomBuildStore(_root);
        store.Add("diablo", Build("A", "x"));
        store.Add("diablo", Build("B", "y"));

        store.Remove("diablo", "A");

        Assert.Equal("B", Assert.Single(store.Load("diablo")).Name);
    }
}
