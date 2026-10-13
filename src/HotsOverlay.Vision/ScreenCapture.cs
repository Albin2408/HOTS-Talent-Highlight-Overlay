using System.Drawing;
using System.Drawing.Imaging;

namespace HotsOverlay.Vision;

/// <summary>A capture region in physical screen pixels.</summary>
public readonly record struct ScreenRegion(int Left, int Top, int Width, int Height);

/// <summary>
/// Grabs a small region of the screen via GDI BitBlt. Works for windowed and borderless-windowed
/// games; true exclusive-fullscreen bypasses the desktop compositor and generally can't be
/// captured this way, so the app documents Borderless Windowed as the supported mode.
/// </summary>
public static class ScreenCapture
{
    public static Bitmap Capture(ScreenRegion region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), "Capture region must have positive size.");

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(region.Left, region.Top, 0, 0, new Size(region.Width, region.Height));
        return bitmap;
    }
}
