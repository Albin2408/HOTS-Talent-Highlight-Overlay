using HotsOverlay.App.Native;
using HotsOverlay.Vision;

namespace HotsOverlay.App;

/// <summary>Which part of the game window is scanned for talent cards.</summary>
public static class ScanSettings
{
    /// <summary>
    /// By default only the left side of the window is scanned: that's where HOTS draws the talent
    /// panel, and a smaller image means a much cheaper OCR pass every scan. Cards are found by
    /// name, so no precise calibration is needed — just a region that contains the panel.
    /// </summary>
    public static bool FullWindow { get; set; }

    private const double LeftFraction = 0.45;
    private const double TopMargin = 0.04;
    private const double BottomMargin = 0.04;

    public static ScreenRegion RegionFor(Win32.RECT game)
    {
        if (FullWindow)
            return new ScreenRegion(game.Left, game.Top, game.Width, game.Height);

        int top = (int)(game.Height * TopMargin);
        return new ScreenRegion(
            game.Left,
            game.Top + top,
            (int)(game.Width * LeftFraction),
            game.Height - top - (int)(game.Height * BottomMargin));
    }
}
