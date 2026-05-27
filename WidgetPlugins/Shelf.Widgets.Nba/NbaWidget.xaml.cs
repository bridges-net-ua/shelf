using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Nba;

public partial class NbaWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Nba_Name");

    // ===== Persisted state =====

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public List<string> FavoriteTeams { get; set; } = new();
        public int LeagueGameCount { get; set; } = 2;     // 1..5 upcoming non-final games to show in LEAGUE section
        public int LeaguePastCount { get; set; } = 1;     // 1..5 finished league games to show above upcoming
        public int FavoriteNextCount { get; set; } = 1;   // 1..5 upcoming games per favourite team block
        public CachedSnapshot? Cached { get; set; }
    }

    public class CachedSnapshot
    {
        public DateTime CachedAt { get; set; }
        public string StatusLabel { get; set; } = "";
        public List<FavoriteSnapshot> Favorites { get; set; } = new();
        public List<GameLite> TodayGames { get; set; } = new();
        public List<SeriesLite> PlayoffSeries { get; set; } = new();
    }

    public class FavoriteSnapshot
    {
        public string Abbreviation { get; set; } = "";
        public GameLite? LastGame { get; set; }
        public List<GameLite> NextGames { get; set; } = new();
        public string SeriesSummary { get; set; } = "";
    }

    public class GameLite
    {
        public string EventId { get; set; } = "";
        public DateTime StartUtc { get; set; }
        public string Status { get; set; } = "";
        public string StatusDetail { get; set; } = "";
        public string HomeAbbrev { get; set; } = "";
        public string AwayAbbrev { get; set; } = "";
        public string HomeLogoUrl { get; set; } = "";
        public string AwayLogoUrl { get; set; } = "";
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public string TopScorerName { get; set; } = "";
        public int TopScorerPoints { get; set; }

        public bool IsFinal => Status?.Contains("FINAL", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsLive => Status?.Contains("IN_PROGRESS", StringComparison.OrdinalIgnoreCase) == true
                              || Status?.Contains("HALFTIME", StringComparison.OrdinalIgnoreCase) == true;
    }

    public class SeriesLite
    {
        public string TeamA { get; set; } = "";
        public string TeamB { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(48);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromMinutes(1);
    private const int LogoSizePanel = 20;

    // ===== Fields =====

    private WidgetState _state = new();
    private bool _hasData;
    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _refreshTimer;
    private int _fetchGeneration;
    private bool _liveMode;

    // ===== IWidget =====

    public string Id => "nba";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Nba_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public NbaWidget()
    {
        InitializeComponent();
        UpdatedText.ToolTip = Loc.Get("Nba_LastUpdate");

        Loaded += (_, _) => ApplyState();
        Unloaded += (_, _) => _refreshTimer?.Stop();
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new NbaSettingsDialog(
            _state.FavoriteTeams.ToList(),
            _state.LeagueGameCount,
            _state.LeaguePastCount,
            _state.FavoriteNextCount)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state.FavoriteTeams = dlg.ResultFavorites
                .Select(NbaTeams.Normalize)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();
            _state.LeagueGameCount = Math.Clamp(dlg.ResultLeagueGameCount, 1, 5);
            _state.LeaguePastCount = Math.Clamp(dlg.ResultLeaguePastCount, 1, 5);
            _state.FavoriteNextCount = Math.Clamp(dlg.ResultFavoriteNextCount, 1, 5);
            WidgetServices.RequestSaveStates();
            RefreshAsync();
        }
    }

    public string SaveState() => JsonSerializer.Serialize(_state);

    public void LoadState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<WidgetState>(json);
            if (loaded != null) _state = loaded;
            if (_state.LeagueGameCount < 1) _state.LeagueGameCount = 2;
            if (_state.LeagueGameCount > 5) _state.LeagueGameCount = 5;
            if (_state.LeaguePastCount < 1) _state.LeaguePastCount = 1;
            if (_state.LeaguePastCount > 5) _state.LeaguePastCount = 5;
            if (_state.FavoriteNextCount < 1) _state.FavoriteNextCount = 1;
            if (_state.FavoriteNextCount > 5) _state.FavoriteNextCount = 5;
        }
        catch { }
    }

    // ===== Lifecycle =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;
        TryRenderCache();
        StartTimers();
        RefreshAsync();
    }

    private void StartTimers()
    {
        if (_refreshTimer == null)
        {
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Tick += (_, _) => RefreshAsync();
        }
        _refreshTimer.Interval = _liveMode ? LiveRefreshInterval : RefreshInterval;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void SwitchToLiveMode(bool live)
    {
        if (_liveMode == live || _refreshTimer == null) return;
        _liveMode = live;
        _refreshTimer.Interval = _liveMode ? LiveRefreshInterval : RefreshInterval;
    }

    private bool TryRenderCache()
    {
        var c = _state.Cached;
        if (c == null) return false;
        if (DateTime.Now - c.CachedAt > CacheMaxAge) return false;
        RenderSnapshot(c, c.CachedAt);
        return true;
    }

    private async void RefreshAsync()
    {
        int gen = ++_fetchGeneration;
        RefreshButton.IsEnabled = false;
        SetError(null);
        if (!_hasData) StatusText.Text = Loc.Get("Nba_Loading");

        try
        {
            var now = DateTime.Now;
            var snapshot = await BuildSnapshotAsync(now);
            if (gen != _fetchGeneration) return;
            if (snapshot == null)
            {
                if (!_hasData) SetError(Loc.Get("Nba_Err_Network"));
                return;
            }

            _state.Cached = snapshot;
            ScheduleSave();
            RenderSnapshot(snapshot, snapshot.CachedAt);
        }
        catch
        {
            if (gen == _fetchGeneration && !_hasData)
                SetError(Loc.Get("Nba_Err_Network"));
        }
        finally
        {
            if (gen == _fetchGeneration) RefreshButton.IsEnabled = true;
        }
    }

    // ===== Snapshot builder =====

    private async Task<CachedSnapshot?> BuildSnapshotAsync(DateTime now)
    {
        var snapshot = new CachedSnapshot { CachedAt = now };

        // Forward scan: gather LeagueGameCount upcoming (non-final) games.
        // Today's scoreboard is fetched in full so we can use the finished half
        // for playoff/series detection later. 14-day safety cap protects against
        // off-season scans that never find any upcoming games.
        int targetUpcoming = Math.Max(1, _state.LeagueGameCount);
        const int maxScanDays = 14;
        var allTodayGames = new List<NbaApi.GameSummary>();          // used for playoff/series detection
        var upcomingGames = new List<NbaApi.GameSummary>();          // upcoming portion of LEAGUE list
        for (int d = 0; d < maxScanDays && upcomingGames.Count < targetUpcoming; d++)
        {
            var games = await NbaApi.GetScoreboardAsync(now.Date.AddDays(d));
            if (d == 0) allTodayGames.AddRange(games);
            foreach (var g in games)
            {
                bool isUpcoming = !g.IsFinal && g.StartUtc >= now.AddHours(-3);
                if (!isUpcoming) continue;
                if (upcomingGames.Count >= targetUpcoming) break;
                upcomingGames.Add(g);
            }
        }

        // Backward scan: gather LeaguePastCount finished games. Walk yesterday → past
        // until we have enough (14-day cap). Today's already-fetched scoreboard is
        // mined first for any finished games before going back.
        int targetPast = Math.Max(1, _state.LeaguePastCount);
        var pastGames = new List<NbaApi.GameSummary>();
        foreach (var g in allTodayGames)
        {
            if (g.IsFinal && pastGames.Count < targetPast) pastGames.Add(g);
        }
        for (int d = -1; d > -maxScanDays && pastGames.Count < targetPast; d--)
        {
            var games = await NbaApi.GetScoreboardAsync(now.Date.AddDays(d));
            foreach (var g in games)
            {
                if (!g.IsFinal) continue;
                if (pastGames.Count >= targetPast) break;
                pastGames.Add(g);
            }
        }
        // Past games might come out unsorted because today's batch is appended
        // before the backward sweep; bring them back into chronological order so
        // older sits above newer.
        pastGames.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        // Keep only the most recent N (in case today contributed extras).
        if (pastGames.Count > targetPast)
            pastGames = pastGames.GetRange(pastGames.Count - targetPast, targetPast);

        // Combined list: past (chronological, oldest → newest) then upcoming.
        var merged = new List<NbaApi.GameSummary>();
        merged.AddRange(pastGames);
        merged.AddRange(upcomingGames);
        snapshot.TodayGames = merged.Select(ToLite).ToList();

        // Playoff detection: full today scoreboard + past + upcoming all considered.
        var detectionPool = allTodayGames.Concat(pastGames).Concat(upcomingGames).ToList();
        bool playoffMode = detectionPool.Any(g => g.SeasonType == "3");
        if (playoffMode)
        {
            var seenSeries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in detectionPool.Where(g => g.SeasonType == "3" && !string.IsNullOrWhiteSpace(g.SeriesSummary)))
            {
                string key = string.Join("-", new[] { g.HomeAbbrev, g.AwayAbbrev }.OrderBy(s => s));
                if (seenSeries.Add(key))
                {
                    snapshot.PlayoffSeries.Add(new SeriesLite
                    {
                        TeamA = g.HomeAbbrev,
                        TeamB = g.AwayAbbrev,
                        Summary = g.SeriesSummary
                    });
                }
            }
        }

        if (playoffMode)
            snapshot.StatusLabel = Loc.Get("Nba_Status_Playoffs");
        else if (detectionPool.Count > 0)
            snapshot.StatusLabel = Loc.Get("Nba_Status_Regular");
        else
            snapshot.StatusLabel = Loc.Get("Nba_Status_OffSeason");

        foreach (var abbr in _state.FavoriteTeams)
        {
            var (last, nexts) = await NbaApi.GetTeamLastAndNextsAsync(abbr, now, _state.FavoriteNextCount);
            var favSnap = new FavoriteSnapshot
            {
                Abbreviation = abbr,
                LastGame = last != null ? ToLite(last) : null,
                NextGames = nexts.Select(ToLite).ToList(),
                SeriesSummary = last?.SeriesSummary ?? nexts.FirstOrDefault()?.SeriesSummary ?? ""
            };
            snapshot.Favorites.Add(favSnap);
        }

        return snapshot;
    }

    private static GameLite ToLite(NbaApi.GameSummary g) => new()
    {
        EventId = g.EventId,
        StartUtc = g.StartUtc,
        Status = g.Status,
        StatusDetail = g.StatusDetail,
        HomeAbbrev = g.HomeAbbrev,
        AwayAbbrev = g.AwayAbbrev,
        HomeLogoUrl = g.HomeLogoUrl,
        AwayLogoUrl = g.AwayLogoUrl,
        HomeScore = g.HomeScore,
        AwayScore = g.AwayScore,
        TopScorerName = g.TopScorerName,
        TopScorerPoints = g.TopScorerPoints
    };

    // ===== Render =====

    private void RenderSnapshot(CachedSnapshot snap, DateTime timestamp)
    {
        _hasData = true;
        StatusText.Text = snap.StatusLabel;
        UpdatedText.Text = timestamp.ToString("HH:mm");
        SetError(null);

        // ===== Favourites =====
        FavouritesHost.Children.Clear();
        bool anyFav = snap.Favorites.Count > 0;
        FavouritesSection.Visibility = anyFav ? Visibility.Visible : Visibility.Collapsed;

        bool liveNeeded = false;
        for (int i = 0; i < snap.Favorites.Count; i++)
        {
            if (i > 0)
                FavouritesHost.Children.Add(BuildDashedSeparator());
            FavouritesHost.Children.Add(BuildFavoriteBlock(snap.Favorites[i], ref liveNeeded));
        }

        // ===== League: chronological list. A dashed separator splits past games
        //       (finished) from upcoming ones; each row carries its own date prefix. =====
        TodayGamesHost.Children.Clear();
        var leagueGames = snap.TodayGames
            .OrderBy(g => g.StartUtc)
            .ToList();

        bool sawPast = false;
        bool separatorInserted = false;
        foreach (var g in leagueGames)
        {
            if (g.IsFinal)
            {
                sawPast = true;
            }
            else if (sawPast && !separatorInserted)
            {
                TodayGamesHost.Children.Add(BuildDashedSeparator());
                separatorInserted = true;
            }
            TodayGamesHost.Children.Add(BuildLeagueGameRow(g));
            if (g.IsLive) liveNeeded = true;
        }

        // Series info now lives in the per-game tooltip (see BuildSeriesTooltip),
        // so the standalone "playoff series" summary block has been removed.

        // ===== Empty state =====
        bool anyContent = anyFav || leagueGames.Count > 0;
        EmptyText.Visibility = anyContent ? Visibility.Collapsed : Visibility.Visible;
        if (!anyContent)
            EmptyText.Text = _state.FavoriteTeams.Count == 0
                ? Loc.Get("Nba_Empty_AddFavorites")
                : Loc.Get("Nba_Empty_NoData");

        SwitchToLiveMode(liveNeeded);
    }

    private FrameworkElement BuildDashedSeparator()
    {
        var path = new Path
        {
            Stroke = (Brush)FindResource("BorderBrush"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 3 },
            Stretch = Stretch.Fill,
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            Data = new LineGeometry(new Point(0, 0), new Point(1, 0))
        };
        return path;
    }

    private FrameworkElement BuildFavoriteBlock(FavoriteSnapshot fav, ref bool liveNeeded)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };

        // Centered team name with full-name tooltip
        var nameText = new TextBlock
        {
            Text = NbaTeams.DisplayName(fav.Abbreviation),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            ToolTip = NbaTeams.DisplayName(fav.Abbreviation),
            Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(nameText);

        if (fav.LastGame != null)
        {
            panel.Children.Add(BuildGameRow(fav.LastGame, isPast: true, favoriteAbbr: fav.Abbreviation, labelKey: "Nba_LastGame"));
            if (fav.LastGame.IsLive) liveNeeded = true;
        }
        foreach (var ng in fav.NextGames)
        {
            panel.Children.Add(BuildGameRow(ng, isPast: false, favoriteAbbr: fav.Abbreviation, labelKey: "Nba_NextGame"));
            if (ng.IsLive) liveNeeded = true;
        }
        if (fav.LastGame == null && fav.NextGames.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.Get("Nba_NoGames"),
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        if (!string.IsNullOrEmpty(fav.SeriesSummary))
        {
            panel.Children.Add(new TextBlock
            {
                Text = fav.SeriesSummary,
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedTextBrush"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        return panel;
    }

    private FrameworkElement BuildGameRow(GameLite g, bool isPast, string favoriteAbbr, string labelKey)
    {
        var border = new Border
        {
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent
        };
        border.MouseLeftButtonUp += (_, _) => OpenGameDetails(g);

        var sp = new StackPanel();

        // Header label: "Минула:" / "Наступна:"
        sp.Children.Add(new TextBlock
        {
            Text = Loc.Get(labelKey) + ":",
            FontSize = 10,
            Foreground = (Brush)FindResource("SecondaryTextBrush")
        });

        // Score / vs row with logos
        sp.Children.Add(BuildScoreRow(g, isPast, favoriteAbbr));

        if (g.IsLive)
        {
            sp.Children.Add(new TextBlock
            {
                Text = Loc.Get("Nba_Live") + (string.IsNullOrEmpty(g.StatusDetail) ? "" : " - " + g.StatusDetail),
                FontSize = 10,
                Foreground = (Brush)FindResource("AccentBrush"),
                FontWeight = FontWeights.SemiBold
            });
        }
        if (isPast && !string.IsNullOrEmpty(g.TopScorerName) && g.TopScorerPoints > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = Loc.Get("Nba_TopScorer") + ": " + g.TopScorerName + " - " + g.TopScorerPoints + " PTS",
                FontSize = 10,
                Foreground = (Brush)FindResource("MutedTextBrush")
            });
        }

        border.Child = sp;
        return border;
    }

    // Builds the "[away-logo] AWAY [scoreA-scoreB | vs] HOME [home-logo] - 27.05 [HH:mm]" row.
    // Past games: winner's abbrev and *only* the winner's score rendered bold; loser stays Normal.
    private FrameworkElement BuildScoreRow(GameLite g, bool isPast, string favoriteAbbr)
    {
        bool hasScores = g.HomeScore.HasValue && g.AwayScore.HasValue;
        bool awayWon = hasScores && g.AwayScore!.Value > g.HomeScore!.Value;
        bool homeWon = hasScores && g.HomeScore!.Value > g.AwayScore!.Value;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // away logo
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // away abbr
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // score block (3 runs) or vs
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // home abbr
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // home logo
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // spacer
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // date/time

        string tooltip = BuildSeriesTooltip(g);

        AddLogo(grid, 0, g.AwayAbbrev, g.AwayLogoUrl);
        AddAbbrLabel(grid, 1, g.AwayAbbrev, bold: isPast && awayWon, leftPad: 4);

        if (hasScores && (isPast || g.IsLive))
        {
            // Split " 110 - 105 " into three Runs so only the winner's number is bold.
            grid.Children.Add(BuildScoreNumbersBlock(
                g.AwayScore!.Value, g.HomeScore!.Value,
                awayBold: isPast && awayWon,
                homeBold: isPast && homeWon,
                fontSize: 12,
                column: 2,
                tooltip: tooltip));
        }
        else
        {
            var vsTb = new TextBlock
            {
                Text = " vs ",
                FontSize = 12,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrEmpty(tooltip)) vsTb.ToolTip = tooltip;
            Grid.SetColumn(vsTb, 2);
            grid.Children.Add(vsTb);
        }

        AddAbbrLabel(grid, 3, g.HomeAbbrev, bold: isPast && homeWon, leftPad: 0, rightPad: 4);
        AddLogo(grid, 4, g.HomeAbbrev, g.HomeLogoUrl);

        // Date / time on the right
        var local = g.StartUtc.ToLocalTime();
        string rightText = isPast
            ? local.ToString("dd.MM", CultureInfo.InvariantCulture)
            : local.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
        var dateTb = new TextBlock
        {
            Text = rightText,
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(dateTb, 6);
        grid.Children.Add(dateTb);

        return grid;
    }

    // Renders " <away> - <home> " as a single TextBlock where only the winning
    // side's digits are bold. Used by both BuildScoreRow and BuildLeagueGameRow.
    // If <paramref name="tooltip"/> is non-empty it's attached as the ToolTip so
    // the user can hover the score to see series info.
    private TextBlock BuildScoreNumbersBlock(int awayScore, int homeScore, bool awayBold, bool homeBold, double fontSize, int column, string tooltip = "")
    {
        var tb = new TextBlock
        {
            FontSize = fontSize,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.Inlines.Add(new System.Windows.Documents.Run(" " + awayScore)
        {
            FontWeight = awayBold ? FontWeights.Bold : FontWeights.Normal
        });
        tb.Inlines.Add(new System.Windows.Documents.Run("-")
        {
            FontWeight = FontWeights.Normal
        });
        tb.Inlines.Add(new System.Windows.Documents.Run(homeScore + " ")
        {
            FontWeight = homeBold ? FontWeights.Bold : FontWeights.Normal
        });
        if (!string.IsNullOrEmpty(tooltip)) tb.ToolTip = tooltip;
        Grid.SetColumn(tb, column);
        return tb;
    }

    private void AddLogo(Grid grid, int col, string abbr, string url)
    {
        var img = new Image
        {
            Width = LogoSizePanel,
            Height = LogoSizePanel,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = NbaTeams.DisplayName(abbr),
            Source = NbaLogoCache.TryGetGray(abbr)
        };
        Grid.SetColumn(img, col);
        grid.Children.Add(img);
        if (img.Source == null && !string.IsNullOrEmpty(url))
            _ = LoadGrayLogoLater(img, abbr, url);
    }

    private static async Task LoadGrayLogoLater(Image target, string abbr, string url)
    {
        var bmp = await NbaLogoCache.GetGrayAsync(abbr, url);
        if (bmp != null) target.Source = bmp;
    }

    private void AddAbbrLabel(Grid grid, int col, string abbr, bool bold, double leftPad, double rightPad = 0)
    {
        var tb = new TextBlock
        {
            Text = abbr,
            FontSize = 12,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = NbaTeams.DisplayName(abbr),
            Margin = new Thickness(leftPad, 0, rightPad, 0)
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    // Parses a raw ESPN series.summary like "OKC leads series 2-1" / "OKC wins series 4-0"
    // into a localized tooltip line that names BOTH teams. Returns "" when this game
    // isn't part of a tracked playoff series or the summary doesn't match the expected
    // pattern (regular season, series not started yet, ESPN format drift, etc.).
    private string BuildSeriesTooltip(GameLite g)
    {
        var cached = _state.Cached;
        if (cached == null || cached.PlayoffSeries.Count == 0) return "";

        SeriesLite? match = null;
        foreach (var s in cached.PlayoffSeries)
        {
            bool sameTeams =
                (string.Equals(s.TeamA, g.HomeAbbrev, StringComparison.OrdinalIgnoreCase)
                 && string.Equals(s.TeamB, g.AwayAbbrev, StringComparison.OrdinalIgnoreCase))
                ||
                (string.Equals(s.TeamA, g.AwayAbbrev, StringComparison.OrdinalIgnoreCase)
                 && string.Equals(s.TeamB, g.HomeAbbrev, StringComparison.OrdinalIgnoreCase));
            if (sameTeams) { match = s; break; }
        }
        if (match == null || string.IsNullOrWhiteSpace(match.Summary)) return "";

        // Patterns we expect from ESPN:
        //   "OKC leads series 2-1"
        //   "OKC wins series 4-0"  (or sometimes "won series 4-0" / "win series 4-0")
        //   "Series starts 6/3"    (no leader/score - skip)
        var m = System.Text.RegularExpressions.Regex.Match(
            match.Summary,
            @"^\s*([A-Z]{2,4})\s+(leads|wins?|won)\s+series\s+(\d+-\d+)\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return "";

        string leader = NbaTeams.Normalize(m.Groups[1].Value);
        string action = m.Groups[2].Value.ToLowerInvariant();
        string score = m.Groups[3].Value;

        // Resolve opponent abbreviation from the current game's two teams.
        string opp;
        if (string.Equals(leader, g.HomeAbbrev, StringComparison.OrdinalIgnoreCase))
            opp = g.AwayAbbrev;
        else if (string.Equals(leader, g.AwayAbbrev, StringComparison.OrdinalIgnoreCase))
            opp = g.HomeAbbrev;
        else
            opp = string.Equals(leader, match.TeamA, StringComparison.OrdinalIgnoreCase) ? match.TeamB : match.TeamA;

        bool won = action == "wins" || action == "win" || action == "won";
        string key = won ? "Nba_Series_Won" : "Nba_Series_Leads";
        return Loc.Format(key, leader, opp, score);
    }

    // League row: a single horizontal line with date+time prefix, logos+abbrevs
    // around the "vs" / score, and the optional series summary as a small sub-line
    // directly below ("(OKC leads series 3-2)").
    private FrameworkElement BuildLeagueGameRow(GameLite g)
    {
        var outer = new Border
        {
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent
        };
        outer.MouseLeftButtonUp += (_, _) => OpenGameDetails(g);

        var stack = new StackPanel();

        bool hasScores = g.HomeScore.HasValue && g.AwayScore.HasValue && (g.IsFinal || g.IsLive);
        bool awayWon = hasScores && g.AwayScore!.Value > g.HomeScore!.Value;
        bool homeWon = hasScores && g.HomeScore!.Value > g.AwayScore!.Value;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // dd.MM (HH:mm) prefix
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // away logo
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // away abbr
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // vs / score
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // home abbr
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // home logo
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // spacer
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Live indicator (optional)

        string tooltip = BuildSeriesTooltip(g);
        bool isPast = g.IsFinal;

        var local = g.StartUtc.ToLocalTime();
        // Past dates stay muted/normal; upcoming dates get the brighter "this is
        // what to look forward to" treatment - bold white date, time stays muted.
        var prefixDate = new System.Windows.Documents.Run(
            local.ToString("dd.MM", CultureInfo.InvariantCulture))
        {
            Foreground = (Brush)FindResource(isPast ? "MutedTextBrush" : "PrimaryTextBrush"),
            FontWeight = isPast ? FontWeights.Normal : FontWeights.Bold
        };
        var prefixTime = new System.Windows.Documents.Run(
            " (" + local.ToString("HH:mm", CultureInfo.InvariantCulture) + ") ")
        {
            Foreground = (Brush)FindResource("MutedTextBrush"),
            FontWeight = FontWeights.Normal
        };
        var prefix = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        prefix.Inlines.Add(prefixDate);
        prefix.Inlines.Add(prefixTime);
        Grid.SetColumn(prefix, 0);
        grid.Children.Add(prefix);

        AddLogo(grid, 1, g.AwayAbbrev, g.AwayLogoUrl);
        AddAbbrLabel(grid, 2, g.AwayAbbrev, bold: g.IsFinal && awayWon, leftPad: 4);

        if (hasScores)
        {
            grid.Children.Add(BuildScoreNumbersBlock(
                g.AwayScore!.Value, g.HomeScore!.Value,
                awayBold: g.IsFinal && awayWon,
                homeBold: g.IsFinal && homeWon,
                fontSize: 11,
                column: 3,
                tooltip: tooltip));
        }
        else
        {
            var vsTb = new TextBlock
            {
                Text = " vs ",
                FontSize = 11,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrEmpty(tooltip)) vsTb.ToolTip = tooltip;
            Grid.SetColumn(vsTb, 3);
            grid.Children.Add(vsTb);
        }

        AddAbbrLabel(grid, 4, g.HomeAbbrev, bold: g.IsFinal && homeWon, leftPad: 0, rightPad: 4);
        AddLogo(grid, 5, g.HomeAbbrev, g.HomeLogoUrl);

        // Right side: only show the Live marker; for upcoming games the time is
        // already in the prefix, so the right column stays empty.
        if (g.IsLive)
        {
            var liveTb = new TextBlock
            {
                Text = Loc.Get("Nba_Live"),
                FontSize = 11,
                Foreground = (Brush)FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(liveTb, 7);
            grid.Children.Add(liveTb);
        }

        stack.Children.Add(grid);

        outer.Child = stack;
        return outer;
    }

    private void OpenGameDetails(GameLite g)
    {
        var owner = Window.GetWindow(this);
        var win = new NbaGameDetailsWindow(g)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        win.ShowDialog();
    }

    // ===== Errors / busy =====

    private void SetError(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            ErrorText.Text = "";
            ErrorText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            if (!_hasData)
            {
                StatusText.Text = "";
                EmptyText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAsync();

    // ===== Debounced save =====

    private void ScheduleSave()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer!.Stop();
                WidgetServices.RequestSaveStates();
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ===== Title rename =====

    private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginRenameTitle();
            e.Handled = true;
        }
    }

    private void TitleRenameMenuItem_Click(object sender, RoutedEventArgs e) => BeginRenameTitle();

    private void BeginRenameTitle()
    {
        if (_isEditingTitle) return;
        _isEditingTitle = true;
        _editTitleCanceled = false;
        TitleEdit.Text = string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEdit.Visibility = Visibility.Visible;
        TitleEdit.Focus();
        TitleEdit.SelectAll();
    }

    private void TitleEdit_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitTitleEdit();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _editTitleCanceled = true;
            CommitTitleEdit();
        }
    }

    private void TitleEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isEditingTitle) CommitTitleEdit();
    }

    private void CommitTitleEdit()
    {
        if (!_isEditingTitle) return;
        _isEditingTitle = false;

        if (!_editTitleCanceled)
        {
            var newTitle = TitleEdit.Text.Trim();
            _state.Title = (string.IsNullOrEmpty(newTitle) || newTitle == DefaultTitle)
                ? ""
                : newTitle;
            TitleText.Text = InstanceLabel;
            WidgetServices.RequestSaveStates();
        }

        TitleEdit.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        _editTitleCanceled = false;
    }
}
