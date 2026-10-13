namespace HotsOverlay.TestSupport;

/// <summary>
/// Some tests exercise real third-party data (a snapshot of Icy Veins' builds, saved guide pages). That
/// data is deliberately not in the public repository, so those tests skip themselves unless the data has
/// been generated locally (tools/refresh-builds.sh). Everything else runs everywhere.
/// </summary>
public static class OptionalData
{
    public static bool Exists(string relativeDirectory)
    {
        string path = Path.Combine(AppContext.BaseDirectory, relativeDirectory);
        return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
    }

    public static string SkipReason(string relativeDirectory) =>
        $"Needs local data in '{relativeDirectory}', which isn't in the public repo. " +
        "Generate it with tools/refresh-builds.sh (see the README).";
}

/// <summary>A [Fact] that skips when the named data folder (relative to the test output) is missing or empty.</summary>
public sealed class OptionalDataFactAttribute : FactAttribute
{
    public OptionalDataFactAttribute(string relativeDirectory)
    {
        if (!OptionalData.Exists(relativeDirectory))
            Skip = OptionalData.SkipReason(relativeDirectory);
    }
}

/// <summary>A [Theory] that skips when the named data folder (relative to the test output) is missing or empty.</summary>
public sealed class OptionalDataTheoryAttribute : TheoryAttribute
{
    public OptionalDataTheoryAttribute(string relativeDirectory)
    {
        if (!OptionalData.Exists(relativeDirectory))
            Skip = OptionalData.SkipReason(relativeDirectory);
    }
}
