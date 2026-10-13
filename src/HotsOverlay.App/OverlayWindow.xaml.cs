using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HotsOverlay.App.Native;
using HotsOverlay.Core;
using HotsOverlay.Scraper;
using HotsOverlay.Vision;

namespace HotsOverlay.App;

/// <summary>
/// Transparent, click-through, always-on-top window that tracks the live HOTS window. Every scan it
/// OCRs the left side of the game, works out which hero you're playing from the talent names on
/// screen, finds that build's recommended talent by name, and draws a highlight around its card.
/// Set HOTS_DEBUG=1 to print what the OCR sees to the console.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>OCR can miss a frame; keep the highlight for a few misses before hiding it.</summary>
    private const int MissesBeforeHide = 3;

    /// <summary>Scans (~0.7s each) of an all-black capture before telling the user the game isn't capturable.</summary>
    private const int BlankScansBeforeWarning = 6;

    private static readonly bool DebugLogging = Environment.GetEnvironmentVariable("HOTS_DEBUG") == "1";

    private readonly App _app = (App)Application.Current;
    private readonly ActiveBuildState _state;
    private readonly BuildPanelWindow _panel = new();
    private readonly DispatcherTimer _timer;
    private readonly List<Border> _alternateBoxes = new();
    private HotkeyManager? _hotkeys;
    private HeroCatalog? _catalog;
    private bool _isScanning;
    private int _misses;
    private int _blankScans;

    public OverlayWindow()
    {
        InitializeComponent();

        _state = _app.ActiveBuild;
        _state.Changed += OnStateChanged;

        SourceInitialized += (_, _) =>
        {
            Win32.MakeClickThroughOverlay(new WindowInteropHelper(this).Handle);
            _hotkeys = new HotkeyManager(this);
        };
        Closed += (_, _) =>
        {
            _timer?.Stop();
            _state.Changed -= OnStateChanged;
            _hotkeys?.Dispose();
            _panel.Close();
        };

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += async (_, _) => await ScanTickAsync();
        _timer.Start();
    }

    private void OnStateChanged()
    {
        _misses = 0;
        HideHighlights();
        _panel.SetActiveLevel(null);
        _hotkeys?.UnregisterAll(); // re-registered on the next scan to match the new number of builds
    }

    private async Task ScanTickAsync()
    {
        if (_isScanning)
            return;

        _isScanning = true;
        try
        {
            await ScanCoreAsync();
        }
        catch (Exception ex)
        {
            // Never let a bad frame take the whole app down (that would close the picker too).
            Console.Error.WriteLine("[HotsOverlay] Scan tick failed: " + ex);
            HideHighlights();
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task ScanCoreAsync()
    {
        if (!GameWindowTracker.TryGetGameWindow(out var gameHwnd, out var game))
        {
            HideEverything();
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        Left = game.Left / dpi.DpiScaleX;
        Top = game.Top / dpi.DpiScaleY;
        Width = game.Width / dpi.DpiScaleX;
        Height = game.Height / dpi.DpiScaleY;
        if (!IsVisible)
            Show();

        if (!_panel.IsVisible)
            _panel.Show();
        _panel.PlaceNear(game, dpi.DpiScaleX, dpi.DpiScaleY);
        UpdateHotkeys(Win32.GetForegroundWindow() == gameHwnd);

        if (!TextScanner.IsAvailable)
        {
            _panel.SetStatus("Windows' English text recognition isn't available, so the overlay can't read the game. " +
                             "Add the English (US) language pack in Windows Settings > Time & language.");
            return;
        }

        var region = ScanSettings.RegionFor(game);
        if (region.Width <= 0 || region.Height <= 0)
            return;

        IReadOnlyList<OcrLineBox> lines;
        using (var capture = ScreenCapture.Capture(region))
        {
            _blankScans = LooksBlank(capture) ? _blankScans + 1 : 0;
            MaskOwnWindows(capture, region);
            lines = await TextScanner.ScanAsync(capture);
        }

        _panel.SetStatus(_blankScans >= BlankScansBeforeWarning
            ? "Can't see the game. In HOTS open Options > Video and set Display Mode to Windowed (Fullscreen)."
            : _app.Catalog.IsFaulted
                ? BuildCache.NoDataMessage
                : null);

        DetectHero(lines);

        var build = _state.Build;
        if (build is null)
        {
            HideHighlights();
            if (DebugLogging)
                LogScan(lines, null);
            return;
        }

        var matches = TalentNameMatcher.FindAll(lines, build);
        var best = matches.FirstOrDefault(m => !m.IsAlternate);
        if (DebugLogging)
            LogScan(lines, best);

        if (best is null)
        {
            if (++_misses >= MissesBeforeHide)
            {
                HideHighlights();
                _panel.SetActiveLevel(null);
            }
            return;
        }

        _misses = 0;
        PositionHighlight(best.CardBox, region, game, dpi);
        _panel.SetActiveLevel(best.Tier.Level);

        // Situational alternates from the same tier, except one whose text is the pick's own line.
        ShowAlternates(
            matches.Where(m => m.IsAlternate && ReferenceEquals(m.Tier, best.Tier) && !Overlaps(m.NameBox, best.NameBox)).ToList(),
            region, game, dpi);
    }

    /// <summary>Switches to whichever hero's talents are on screen, once two scans in a row agree.</summary>
    private void DetectHero(IReadOnlyList<OcrLineBox> lines)
    {
        if (!_state.AutoDetect)
            return;

        if (_catalog is null && _app.Catalog.IsCompletedSuccessfully)
            _catalog = _app.Catalog.Result;
        if (_catalog is null)
            return;

        if (_catalog.Detector.Observe(lines) is { } guess
            && guess.Slug != _state.HeroSlug
            && _catalog.Find(guess.Slug) is { } set)
        {
            if (DebugLogging)
                Console.Error.WriteLine($"[detect] hero -> {guess.Name} (score {guess.Score:0.00}; saw {string.Join(", ", guess.MatchedTalents)})");
            _app.SwitchToDetectedHero(set);
        }
    }

    private void UpdateHotkeys(bool gameIsForeground)
    {
        if (_hotkeys is null)
            return;

        // Numpad keys are only claimed while HOTS has focus, so they keep working everywhere else.
        bool want = gameIsForeground && _state.Builds.Count > 1;
        if (want && !_hotkeys.IsRegistered)
        {
            for (int i = 0; i < Math.Min(_state.Builds.Count, 6); i++)
            {
                int index = i;
                _hotkeys.Register(0, Win32.VK_NUMPAD1 + (uint)i, () => _app.ChooseBuild(index));
            }
        }
        else if (!want && _hotkeys.IsRegistered)
        {
            _hotkeys.UnregisterAll();
        }
    }

    /// <summary>True when the capture is (nearly) pure black — what exclusive fullscreen looks like to a screen grab.</summary>
    private static bool LooksBlank(System.Drawing.Bitmap capture)
    {
        const int columns = 24, rows = 12, threshold = 6;
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < columns; cx++)
            {
                var pixel = capture.GetPixel(
                    Math.Min(capture.Width - 1, (cx * capture.Width) / columns),
                    Math.Min(capture.Height - 1, (cy * capture.Height) / rows));
                if (pixel.R > threshold || pixel.G > threshold || pixel.B > threshold)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Blacks out this app's own normal windows (the picker, dialogs) in the capture. They sit on
    /// top of the game and print talent names themselves, so left in, the OCR would "find" the
    /// picker's build cards and highlight those instead of the real talent panel. (The overlay and
    /// build panel are layered windows, which the screen capture already leaves out.)
    /// </summary>
    private void MaskOwnWindows(System.Drawing.Bitmap capture, ScreenRegion region)
    {
        using var g = System.Drawing.Graphics.FromImage(capture);

        foreach (Window window in Application.Current.Windows)
        {
            if (window is OverlayWindow or BuildPanelWindow || !window.IsVisible)
                continue;

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var r))
                continue;

            g.FillRectangle(System.Drawing.Brushes.Black, r.Left - region.Left, r.Top - region.Top, r.Width, r.Height);
        }
    }

    /// <summary>Converts a box in the scanned image's pixels into this window's local DIP space.</summary>
    private Rect ToLocalRect(BoxF card, ScreenRegion region, Win32.RECT game, DpiScale dpi)
    {
        double xPx = region.Left - game.Left + card.X;
        double yPx = region.Top - game.Top + card.Y;

        double x = Math.Max(0, xPx / dpi.DpiScaleX);
        double y = Math.Max(0, yPx / dpi.DpiScaleY);
        double w = Math.Max(0, Math.Min(card.Width / dpi.DpiScaleX, Width - x));
        double h = Math.Max(0, Math.Min(card.Height / dpi.DpiScaleY, Height - y));
        return new Rect(x, y, w, h);
    }

    private void PositionHighlight(BoxF card, ScreenRegion region, Win32.RECT game, DpiScale dpi) =>
        Place(HighlightBox, ToLocalRect(card, region, game, dpi));

    private static void Place(Border box, Rect rect)
    {
        box.Width = rect.Width;
        box.Height = rect.Height;
        Canvas.SetLeft(box, rect.X);
        Canvas.SetTop(box, rect.Y);
        box.Visibility = Visibility.Visible;
    }

    private void ShowAlternates(IReadOnlyList<TalentMatch> alternates, ScreenRegion region, Win32.RECT game, DpiScale dpi)
    {
        for (int i = 0; i < alternates.Count; i++)
        {
            if (i >= _alternateBoxes.Count)
            {
                var box = CreateAlternateBox();
                _alternateBoxes.Add(box);
                OverlayCanvas.Children.Add(box);
            }

            Place(_alternateBoxes[i], ToLocalRect(alternates[i].CardBox, region, game, dpi));
        }

        for (int i = alternates.Count; i < _alternateBoxes.Count; i++)
            _alternateBoxes[i].Visibility = Visibility.Collapsed;
    }

    /// <summary>Orange frame with an "OR CONSIDER" tag: a second-choice talent, visually quieter than the gold pick.</summary>
    private static Border CreateAlternateBox()
    {
        var orange = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x43));
        return new Border
        {
            BorderBrush = orange,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x9F, 0x43)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0x9F, 0x43), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8,
            },
            Child = new Border
            {
                Background = orange,
                CornerRadius = new CornerRadius(0, 0, 0, 6),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "OR CONSIDER",
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27)),
                },
            },
        };
    }

    private static bool Overlaps(BoxF a, BoxF b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private void HideHighlights()
    {
        HighlightBox.Visibility = Visibility.Collapsed;
        foreach (var box in _alternateBoxes)
            box.Visibility = Visibility.Collapsed;
    }

    private void HideEverything()
    {
        _blankScans = 0;
        _hotkeys?.UnregisterAll();
        if (IsVisible)
            Hide();
        if (_panel.IsVisible)
            _panel.Hide();
    }

    private static void LogScan(IReadOnlyList<OcrLineBox> lines, TalentMatch? best)
    {
        Console.Error.WriteLine($"[scan] {lines.Count} lines; match: " +
            (best is null ? "none" : $"{best.Tier.PickName} (L{best.Tier.Level}, score {best.Score:0.00})"));
        foreach (var line in lines)
            Console.Error.WriteLine($"    '{line.Text}'  @ {line.Box.X:0},{line.Box.Y:0} h={line.Box.Height:0}");
    }
}
