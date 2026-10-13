namespace HotsOverlay.Core;

/// <summary>How the current hero was chosen.</summary>
public enum SelectionSource { None, Manual, Auto }

/// <summary>
/// The hero being played and which of its builds is active. The overlay reads this to know what to
/// highlight; the picker, the in-game build panel, the hotkeys and hero auto-detection all write it.
/// </summary>
public sealed class ActiveBuildState
{
    public string? HeroSlug { get; private set; }
    public string? HeroName { get; private set; }
    public IReadOnlyList<HeroBuild> Builds { get; private set; } = Array.Empty<HeroBuild>();
    public int BuildIndex { get; private set; }
    public SelectionSource Source { get; private set; }

    /// <summary>When true the overlay switches hero by itself on recognizing the talents on screen.</summary>
    public bool AutoDetect { get; private set; } = true;

    public HeroBuild? Build => (uint)BuildIndex < (uint)Builds.Count ? Builds[BuildIndex] : null;

    public event Action? Changed;

    /// <summary>
    /// Index of the build to start on: the one named <paramref name="rememberedName"/> if it still
    /// exists, otherwise Icy Veins' recommended build (not simply the first: for a few heroes, such as
    /// Stukov, the recommended build isn't listed first), otherwise the first.
    /// </summary>
    public static int PreferredIndex(IReadOnlyList<HeroBuild> builds, string? rememberedName)
    {
        if (rememberedName is not null)
        {
            for (int i = 0; i < builds.Count; i++)
                if (string.Equals(builds[i].Name, rememberedName, StringComparison.OrdinalIgnoreCase))
                    return i;
        }

        for (int i = 0; i < builds.Count; i++)
            if (string.Equals(builds[i].Role, HeroBuild.RecommendedRole, StringComparison.OrdinalIgnoreCase))
                return i;

        return 0;
    }

    public void SetHero(string slug, string name, IReadOnlyList<HeroBuild> builds, int buildIndex, SelectionSource source)
    {
        HeroSlug = slug;
        HeroName = name;
        Builds = builds;
        BuildIndex = Math.Clamp(buildIndex, 0, Math.Max(builds.Count - 1, 0));
        Source = source;
        Changed?.Invoke();
    }

    /// <summary>Switches build within the current hero. Returns false if the index is invalid or unchanged.</summary>
    public bool SelectBuild(int index)
    {
        if ((uint)index >= (uint)Builds.Count || index == BuildIndex)
            return false;

        BuildIndex = index;
        Changed?.Invoke();
        return true;
    }

    public void SetAutoDetect(bool on)
    {
        if (AutoDetect == on)
            return;

        AutoDetect = on;
        Changed?.Invoke();
    }
}
