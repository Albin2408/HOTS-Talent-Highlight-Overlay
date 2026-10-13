using HotsOverlay.Core;

namespace HotsOverlay.Core.Tests;

public class SnapshotFreshnessTests
{
    private static readonly DateTimeOffset Collected = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static SnapshotInfo Snapshot(string? patch = "2.57.0.98348") => new(Collected, patch);

    [Fact]
    public void FreshAndOnTheCurrentPatch_HasNoWarning()
    {
        var result = SnapshotFreshness.Evaluate(Snapshot(), "2.57.0.98348", Collected.AddDays(3));

        Assert.False(result.IsStale);
        Assert.Equal("Builds collected 2026-10-08", result.Summary);
    }

    [Fact]
    public void ANewerGamePatch_Warns()
    {
        var result = SnapshotFreshness.Evaluate(Snapshot("2.57.0.98348"), "2.58.0.99000", Collected.AddDays(3));

        Assert.True(result.IsStale);
        Assert.Contains("2.58.0", result.Warning);
        Assert.Contains("2.57.0", result.Warning);
        Assert.Contains("refresh-builds", result.Warning);
    }

    [Fact]
    public void AHotfixBuildOfTheSamePatch_DoesNotWarn()
    {
        // Same 2.57.0 patch, higher build id: balance didn't change, so don't nag.
        var result = SnapshotFreshness.Evaluate(Snapshot("2.57.0.98304"), "2.57.0.98348", Collected.AddDays(3));

        Assert.False(result.IsStale);
    }

    [Fact]
    public void AnOldSnapshot_WarnsEvenWithoutPatchInfo()
    {
        var result = SnapshotFreshness.Evaluate(Snapshot(null), null, Collected.AddDays(60));

        Assert.True(result.IsStale);
        Assert.Contains("60 days old", result.Warning);
    }

    [Fact]
    public void UnknownLatestPatch_FallsBackToAgeOnly()
    {
        Assert.False(SnapshotFreshness.Evaluate(Snapshot(), null, Collected.AddDays(10)).IsStale);
    }

    [Theory]
    [InlineData("2.57.0.1", "2.55.17.9", 1)]
    [InlineData("2.55.17.1", "2.57.0.9", -1)]
    [InlineData("2.10.0.0", "2.9.0.0", 1)]       // numeric, not alphabetical
    [InlineData("2.57.0.98348", "2.57.0.98304", 0)] // build id ignored
    public void ComparePatches_IsNumericOnMajorMinorPatch(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(SnapshotFreshness.ComparePatches(a, b)));

    [Fact]
    public void Newest_PicksTheHighestVersionIncludingBuildId() =>
        Assert.Equal("2.57.0.98348", SnapshotFreshness.Newest(new[] { "2.55.17.98025", "2.57.0.98304", "2.57.0.98348", "2.57.0.98285" }));
}
