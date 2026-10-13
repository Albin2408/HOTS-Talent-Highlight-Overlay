namespace HotsOverlay.Core;

/// <summary>Describes the bundled build snapshot: when it was collected and the newest game patch then.</summary>
public sealed record SnapshotInfo(DateTimeOffset CollectedAtUtc, string? LatestPatchAtCollection);

/// <summary>What to tell the user about how current the builds are.</summary>
public sealed record DataFreshness(string Summary, string? Warning)
{
    public bool IsStale => Warning is not null;
}

/// <summary>
/// Decides whether the bundled builds may be out of date. Two signals: a newer game patch than the
/// one known when the snapshot was taken (balance changes can invalidate builds), and plain age.
/// </summary>
public static class SnapshotFreshness
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(45);

    public static DataFreshness Evaluate(SnapshotInfo snapshot, string? latestPatch, DateTimeOffset now)
    {
        string summary = $"Builds collected {snapshot.CollectedAtUtc:yyyy-MM-dd}";

        if (latestPatch is not null && snapshot.LatestPatchAtCollection is not null
            && ComparePatches(latestPatch, snapshot.LatestPatchAtCollection) > 0)
        {
            return new DataFreshness(summary,
                $"Game patch {Release(latestPatch)} is out; these builds were collected on {Release(snapshot.LatestPatchAtCollection)}. " +
                "Refresh them with tools/refresh-builds.sh.");
        }

        var age = now - snapshot.CollectedAtUtc;
        if (age >= StaleAfter)
        {
            return new DataFreshness(summary,
                $"These builds are {(int)age.TotalDays} days old and may be out of date. Refresh them with tools/refresh-builds.sh.");
        }

        return new DataFreshness(summary, null);
    }

    /// <summary>
    /// Compares game versions like "2.57.0.98348" numerically on major.minor.patch only: the fourth
    /// number is a build id, and hotfix builds of the same patch shouldn't count as a new patch.
    /// </summary>
    public static int ComparePatches(string a, string b)
    {
        var left = Parts(a);
        var right = Parts(b);
        for (int i = 0; i < 3; i++)
        {
            int cmp = left[i].CompareTo(right[i]);
            if (cmp != 0)
                return cmp;
        }

        return 0;
    }

    /// <summary>The newest version in a list, ordered by the same rule plus the build id as tiebreaker.</summary>
    public static string? Newest(IEnumerable<string> versions) => versions
        .Select(v => (Version: v, Parts: Parts(v)))
        .OrderBy(x => x.Parts[0]).ThenBy(x => x.Parts[1]).ThenBy(x => x.Parts[2]).ThenBy(x => x.Parts[3])
        .Select(x => x.Version)
        .LastOrDefault();

    private static string Release(string version) => string.Join('.', version.Split('.').Take(3));

    private static int[] Parts(string version)
    {
        var parts = new int[4];
        var pieces = version.Split('.');
        for (int i = 0; i < 4 && i < pieces.Length; i++)
            int.TryParse(pieces[i], out parts[i]);
        return parts;
    }
}
