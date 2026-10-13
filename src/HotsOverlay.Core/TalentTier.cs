namespace HotsOverlay.Core;

/// <summary>
/// One talent tier (e.g. Level 4) for a specific build. The overlay only needs
/// <see cref="OptionCount"/> and <see cref="PickIndex"/> to know which slot to highlight
/// in the in-game talent tray — it never needs to OCR talent names.
/// </summary>
public sealed class TalentTier
{
    public required int Level { get; init; }

    /// <summary>How many talent choices are offered at this tier (usually 3-4).</summary>
    public required int OptionCount { get; init; }

    /// <summary>0-based index, within the tray, of the recommended pick.</summary>
    public required int PickIndex { get; init; }

    /// <summary>Display name of the recommended talent, for the picker UI.</summary>
    public required string PickName { get; init; }

    /// <summary>Other talents the guide marks as situationally good at this tier ("or consider...").</summary>
    public IReadOnlyList<string> Alternates { get; init; } = Array.Empty<string>();
}
