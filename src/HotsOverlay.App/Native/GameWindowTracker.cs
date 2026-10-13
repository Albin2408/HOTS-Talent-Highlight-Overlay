using System.Diagnostics;

namespace HotsOverlay.App.Native;

/// <summary>
/// Locates the running Heroes of the Storm process's main window and reports its screen rect
/// (physical pixels) so the overlay can be positioned/sized to match. Blizzard has shipped the
/// client under a couple of different process names over the years, so we try each.
/// </summary>
public static class GameWindowTracker
{
    private static readonly string[] CandidateProcessNames =
    {
        "HeroesOfTheStorm_x64",
        "HeroesOfTheStorm",
        "Heroes of the Storm",
    };

    public static bool TryGetGameWindowRect(out Win32.RECT rect) => TryGetGameWindow(out _, out rect);

    public static bool TryGetGameWindow(out IntPtr hwnd, out Win32.RECT rect)
    {
        hwnd = IntPtr.Zero;
        rect = default;

        foreach (string name in CandidateProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                        continue;
                    if (Win32.IsIconic(handle) || !Win32.IsWindowVisible(handle))
                        continue;
                    if (!Win32.GetWindowRect(handle, out rect))
                        continue;
                    if (rect.Width <= 0 || rect.Height <= 0)
                        continue;

                    hwnd = handle;
                    return true;
                }
            }
        }

        return false;
    }
}
