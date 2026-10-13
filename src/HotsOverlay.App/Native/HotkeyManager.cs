using System.Windows;
using System.Windows.Interop;

namespace HotsOverlay.App.Native;

/// <summary>
/// Global hotkeys (delivered as WM_HOTKEY to the owning window even while another app has focus).
/// Registrations can be dropped and re-made at will, so callers can hold the keys only while they
/// make sense — e.g. while HOTS is the foreground window — instead of swallowing them system-wide.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyManager(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window has no HwndSource.");
        _source.AddHook(WndProc);
    }

    public bool IsRegistered => _handlers.Count > 0;

    public bool Register(uint modifiers, uint virtualKey, Action onPressed)
    {
        int id = _nextId++;
        if (!Win32.RegisterHotKey(_source.Handle, id, modifiers | Win32.MOD_NOREPEAT, virtualKey))
            return false; // another app already owns this key; that's fine, just skip it

        _handlers[id] = onPressed;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _handlers.Keys)
            Win32.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
