namespace HotsOverlay.Core;

/// <summary>The fixed HOTS talent tier breakpoints.</summary>
public static class TierLevels
{
    public static readonly IReadOnlyList<int> Levels = new[] { 1, 4, 7, 10, 13, 16, 20 };

    /// <summary>
    /// Index of the highest tier unlocked at <paramref name="level"/>, or -1 if below level 1.
    /// E.g. level 6 -> tier index 1 (Level 4 tier, since Level 7 isn't reached yet).
    /// </summary>
    public static int IndexForLevel(int level)
    {
        int index = -1;
        for (int i = 0; i < Levels.Count; i++)
        {
            if (level >= Levels[i])
                index = i;
        }
        return index;
    }
}
