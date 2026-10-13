using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.App;

public partial class MainWindow : Window
{
    private readonly App _app = (App)Application.Current;
    private readonly BuildCache _cache;
    private readonly CustomBuildStore _custom;
    private readonly ActiveBuildState _activeState;
    private IReadOnlyList<HeroSummary> _allHeroes = Array.Empty<HeroSummary>();
    private HeroSummary? _selectedHero;
    private bool _syncingAutoDetect;

    public MainWindow()
    {
        InitializeComponent();
        _cache = _app.Cache;
        _custom = _app.Custom;
        _activeState = _app.ActiveBuild;
        _activeState.Changed += SyncAutoDetectCheck;
        _app.FreshnessChanged += ShowSourceNotice;
        Closed += (_, _) =>
        {
            _activeState.Changed -= SyncAutoDetectCheck;
            _app.FreshnessChanged -= ShowSourceNotice;
        };
        Loaded += MainWindow_Loaded;
    }

    /// <summary>Keeps the checkbox in step with the toggle on the in-game panel.</summary>
    private void SyncAutoDetectCheck()
    {
        _syncingAutoDetect = true;
        AutoDetectCheck.IsChecked = _activeState.AutoDetect;
        _syncingAutoDetect = false;
    }

    private void AutoDetectCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncingAutoDetect)
            _app.SetAutoDetect(AutoDetectCheck.IsChecked == true);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _app.ShowOverlay(); // idle until HOTS is running, then it detects your hero by itself
        SyncAutoDetectCheck();

        HeroHeaderText.Text = "Loading heroes...";
        try
        {
            _allHeroes = await _cache.GetOrFetchHeroIndexAsync();
        }
        catch (Exception ex)
        {
            HeroHeaderText.Text = $"Couldn't load hero list: {ex.Message}";
            return;
        }

        HeroListBox.ItemsSource = _allHeroes.OrderBy(h => h.Name).ToList();
        HeroHeaderText.Text = "Pick a hero to see its builds";
        ShowSourceNotice();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();
        HeroListBox.ItemsSource = string.IsNullOrEmpty(query)
            ? _allHeroes.OrderBy(h => h.Name).ToList()
            : _allHeroes.Where(h => h.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(h => h.Name).ToList();
    }

    private async void HeroListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HeroListBox.SelectedItem is HeroSummary hero)
            await LoadHeroAsync(hero);
    }

    /// <summary>Shows a hero's scraped/sample builds plus any the user added themselves.</summary>
    private async Task LoadHeroAsync(HeroSummary hero)
    {
        _selectedHero = hero;
        AddBuildButton.IsEnabled = true;
        HeroHeaderText.Text = $"{hero.Name} — loading builds...";
        BuildsHost.Children.Clear();

        HeroBuildSet? fetched = null;
        string? problem = null;
        try
        {
            fetched = await _cache.GetOrFetchAsync(hero);
        }
        catch (Exception ex)
        {
            problem = ex.Message;
        }

        // The user may have picked a different hero while this one was loading.
        if (_selectedHero != hero)
            return;

        var builds = (fetched?.Builds ?? Array.Empty<HeroBuild>()).Concat(_custom.Load(hero.Slug)).ToList();
        if (builds.Count == 0)
        {
            HeroHeaderText.Text = $"{hero.Name} — no builds yet";
            BuildsHost.Children.Add(new TextBlock
            {
                Text = (problem ?? "No builds found.") + Environment.NewLine + Environment.NewLine +
                       "You can still use this hero: click \"+ Add my own build\" above and paste the 7 talent names " +
                       "from any build guide — the overlay finds each card in-game by its name.",
                Foreground = (Brush)new BrushConverter().ConvertFromString("#9fb3c8")!,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        HeroHeaderText.Text = $"{hero.Name} — choose a build";
        RenderBuilds(new HeroBuildSet
        {
            HeroName = hero.Name,
            HeroSlug = hero.Slug,
            Builds = builds,
            ScrapedAtUtc = fetched?.ScrapedAtUtc ?? DateTimeOffset.UtcNow,
        });
        ShowSourceNotice();
    }

    private async void AddBuildButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedHero is not { } hero)
            return;

        var dialog = new CustomBuildWindow(hero.Name) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } build)
        {
            _custom.Add(hero.Slug, build);
            await LoadHeroAsync(hero);
        }
    }

    /// <summary>Tells the user when data came from saved/sample builds because the live fetch failed.</summary>
    private void ShowSourceNotice()
    {
        var parts = new List<string>();
        if (_cache.DataSourceNotice is { } notice)
            parts.Add(notice);
        if (_app.Freshness?.Warning is { } warning)
            parts.Add("⚠ " + warning);

        if (parts.Count > 0)
            StatusText.Text = string.Join("  ", parts);
    }

    private void RenderBuilds(HeroBuildSet set)
    {
        BuildsHost.Children.Clear();

        foreach (var build in set.Builds)
        {
            BuildsHost.Children.Add(BuildCard(set, build));
        }
    }

    private Border BuildCard(HeroBuildSet set, HeroBuild build)
    {
        var root = new StackPanel();

        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        var useButton = new Button { Content = "Use this build" };
        useButton.Click += (_, _) =>
        {
            int index = 0;
            for (int i = 0; i < set.Builds.Count; i++)
                if (ReferenceEquals(set.Builds[i], build))
                    index = i;

            _app.UseHero(set.HeroSlug, set.HeroName, set.Builds, index, SelectionSource.Manual);
            StatusText.Text = $"Active: {set.HeroName} — {build.Name}.";
        };
        DockPanel.SetDock(useButton, Dock.Right);
        headerRow.Children.Add(useButton);

        if (build.Role == CustomBuild.RoleLabel)
        {
            var deleteButton = new Button { Content = "Delete", Margin = new Thickness(0, 0, 8, 0), Background = Brushes.DimGray };
            deleteButton.Click += async (_, _) =>
            {
                _custom.Remove(set.HeroSlug, build.Name);
                if (_selectedHero is { } hero)
                    await LoadHeroAsync(hero);
            };
            DockPanel.SetDock(deleteButton, Dock.Right);
            headerRow.Children.Add(deleteButton);
        }

        var titleText = build.Role is null ? build.Name : $"{build.Name}  ({build.Role})";
        if (build.WinRate is { } winRate)
            titleText += $"  ·  {winRate:0.#}% win rate";
        headerRow.Children.Add(new TextBlock
        {
            Text = titleText,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });

        root.Children.Add(headerRow);

        var tierWrap = new WrapPanel();
        foreach (var tier in build.Tiers)
        {
            tierWrap.Children.Add(TierBox(tier));
        }
        root.Children.Add(tierWrap);

        return new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#20242e")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#3a4150")!,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = root,
        };
    }

    private static Border TierBox(TalentTier tier)
    {
        var panel = new StackPanel { Width = 130 };
        panel.Children.Add(new TextBlock
        {
            Text = tier.OptionCount > 0
                ? $"Level {tier.Level}  ·  pick {tier.PickIndex + 1}/{tier.OptionCount}"
                : $"Level {tier.Level}",
            Foreground = (Brush)new BrushConverter().ConvertFromString("#7fb0e0")!,
            FontSize = 11,
        });
        panel.Children.Add(new TextBlock
        {
            Text = tier.PickName,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (tier.Alternates.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "or: " + string.Join(", ", tier.Alternates),
                Foreground = (Brush)new BrushConverter().ConvertFromString("#ff9f43")!,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        return new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#2a303c")!,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(4),
            Child = panel,
        };
    }

    private void FullScreenScanCheck_Changed(object sender, RoutedEventArgs e)
    {
        ScanSettings.FullWindow = FullScreenScanCheck.IsChecked == true;
    }
}
