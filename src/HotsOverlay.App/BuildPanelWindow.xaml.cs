using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using HotsOverlay.App.Native;
using HotsOverlay.Core;

namespace HotsOverlay.App;

/// <summary>
/// Small always-on-top card in the game: who you're playing, one button per build (click to switch,
/// no alt-tab), and the active build's pick for every tier with its situational alternatives — the
/// tier currently on screen lit up. Draggable; never takes focus from the game.
/// </summary>
public partial class BuildPanelWindow : Window
{
    private static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4A));
    private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27));
    private static readonly Brush IdleText = new SolidColorBrush(Color.FromRgb(0xC7, 0xD3, 0xE0));
    private static readonly Brush DimText = new SolidColorBrush(Color.FromRgb(0x8E, 0xA3, 0xB8));
    private static readonly Brush ButtonIdle = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x3C));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47));
    private static readonly Brush Faint = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x7A));

    private sealed record Row(Border Border, TextBlock Name, TextBlock? Alternates);

    private readonly App _app = (App)Application.Current;
    private readonly Dictionary<int, Row> _rows = new();
    private int? _activeLevel;
    private bool _positioned;

    public BuildPanelWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Win32.MakeNoActivateToolWindow(new WindowInteropHelper(this).Handle);

        _app.ActiveBuild.Changed += Refresh;
        _app.FreshnessChanged += Refresh;
        Closed += (_, _) =>
        {
            _app.ActiveBuild.Changed -= Refresh;
            _app.FreshnessChanged -= Refresh;
        };
        Refresh();
    }

    /// <summary>The tier whose recommended talent is currently on screen (null = none).</summary>
    public void SetActiveLevel(int? level)
    {
        _activeLevel = level;
        ApplyActiveRow();
    }

    /// <summary>A problem the user can fix (e.g. wrong display mode); null clears it.</summary>
    public void SetStatus(string? message)
    {
        StatusText.Text = message ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Refresh()
    {
        var state = _app.ActiveBuild;
        RowsHost.Children.Clear();
        BuildButtons.Children.Clear();
        _rows.Clear();

        if (state.Build is not { } build)
        {
            HeaderText.Text = "HOTS Talent Overlay";
            SubHeaderText.Text = state.AutoDetect
                ? "Waiting for your talent panel. Open it (it appears at level 1) and your hero is detected automatically."
                : "Auto-detect is off. Pick a hero in the HOTS Talent Overlay window.";
            HintText.Text = "";
        }
        else
        {
            HeaderText.Text = state.HeroName ?? "";
            SubHeaderText.Text = DescribeBuild(state, build);

            for (int i = 0; i < state.Builds.Count; i++)
                BuildButtons.Children.Add(BuildButton(i, state.Builds[i], i == state.BuildIndex));

            foreach (var tier in build.Tiers)
            {
                var name = new TextBlock
                {
                    Text = $"{tier.Level,2}   {tier.PickName}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    Foreground = IdleText,
                };
                var stack = new StackPanel();
                stack.Children.Add(name);

                TextBlock? alternates = null;
                if (tier.Alternates.Count > 0)
                {
                    alternates = new TextBlock
                    {
                        Text = "or: " + string.Join(", ", tier.Alternates),
                        FontSize = 10,
                        FontStyle = FontStyles.Italic,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(26, 0, 0, 0),
                        Foreground = DimText,
                    };
                    stack.Children.Add(alternates);
                }

                var row = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4), Child = stack };
                RowsHost.Children.Add(row);
                _rows[tier.Level] = new Row(row, name, alternates);
            }

            HintText.Text = state.Builds.Count > 1
                ? $"drag to move · numpad 1-{Math.Min(state.Builds.Count, 6)} switches build"
                : "drag to move";
        }

        AutoDetectText.Text = state.AutoDetect
            ? "auto-detect hero: ON (click to turn off)"
            : "auto-detect hero: OFF (click to turn on)";

        ShowFreshness();
        ApplyActiveRow();
    }

    private static string DescribeBuild(ActiveBuildState state, HeroBuild build)
    {
        string how = state.Source == SelectionSource.Auto ? "detected automatically" : "chosen manually";
        if (build.WinRate is not { } winRate)
            return how;

        string source = build.Role is { } role && role.Contains("Heroes Profile") ? " (Heroes Profile)" : "";
        return $"{how} · {winRate:0.#}% win rate{source}";
    }

    private void ShowFreshness()
    {
        if (_app.Freshness is not { } freshness)
        {
            FreshnessText.Text = "";
            return;
        }

        FreshnessText.Text = freshness.Warning is { } warning ? $"{freshness.Summary}. {warning}" : freshness.Summary;
        FreshnessText.Foreground = freshness.IsStale ? Amber : Faint;
    }

    private Button BuildButton(int index, HeroBuild build, bool selected)
    {
        string label = build.Name.EndsWith(" Build", StringComparison.OrdinalIgnoreCase) ? build.Name[..^6] : build.Name;
        if (build.WinRate is { } winRate)
            label += $" · {winRate:0.#}%";

        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Background = selected ? Gold : ButtonIdle,
            Foreground = selected ? Dark : IdleText,
            FontWeight = selected ? FontWeights.Bold : FontWeights.Normal,
        };
        button.Click += (_, _) => _app.ChooseBuild(index);
        return button;
    }

    private void ApplyActiveRow()
    {
        foreach (var (level, row) in _rows)
        {
            bool active = level == _activeLevel;
            row.Border.Background = active ? Gold : Brushes.Transparent;
            row.Name.Foreground = active ? Dark : IdleText;
            row.Name.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            if (row.Alternates is not null)
                row.Alternates.Foreground = active ? Dark : DimText;
        }
    }

    /// <summary>Parks the panel in the game window's top-right corner the first time only, so a
    /// position the user dragged it to is never overridden.</summary>
    public void PlaceNear(Win32.RECT game, double dpiScaleX, double dpiScaleY)
    {
        if (_positioned)
            return;

        UpdateLayout();
        Left = game.Right / dpiScaleX - ActualWidth - 16;
        Top = game.Top / dpiScaleY + 16;
        _positioned = true;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void AutoDetectText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _app.SetAutoDetect(!_app.ActiveBuild.AutoDetect);
        e.Handled = true; // don't also start a window drag
    }
}
