using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shelf.Sdk;

namespace Shelf.Widgets.Nba;

public partial class NbaGameDetailsWindow : Window
{
    private readonly NbaWidget.GameLite _game;
    private string _espnUrl = "";

    public NbaGameDetailsWindow(NbaWidget.GameLite game)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        _game = game;

        // Render whatever we already have from the panel, then fetch full box score.
        AwayName.Text = NbaTeams.DisplayName(game.AwayAbbrev);
        HomeName.Text = NbaTeams.DisplayName(game.HomeAbbrev);
        AwayName.ToolTip = NbaTeams.DisplayName(game.AwayAbbrev);
        HomeName.ToolTip = NbaTeams.DisplayName(game.HomeAbbrev);
        AwayScore.Text = game.AwayScore?.ToString() ?? "--";
        HomeScore.Text = game.HomeScore?.ToString() ?? "--";
        VenueLine.Text = game.StartUtc.ToLocalTime().ToString("dddd, d MMM yyyy HH:mm", Loc.Culture);
        StatusLine.Text = Loc.Get("Nba_Loading");
        ApplyWinnerHighlight(game.AwayScore, game.HomeScore, isFinal: false);
        LoadLogos(game.AwayAbbrev, game.AwayLogoUrl, game.HomeAbbrev, game.HomeLogoUrl);

        Loaded += async (_, _) => await LoadDetailsAsync();
    }

    private void LoadLogos(string awayAbbr, string awayUrl, string homeAbbr, string homeUrl)
    {
        AwayLogo.ToolTip = NbaTeams.DisplayName(awayAbbr);
        HomeLogo.ToolTip = NbaTeams.DisplayName(homeAbbr);
        AwayLogo.Source = NbaLogoCache.TryGet(awayAbbr);
        HomeLogo.Source = NbaLogoCache.TryGet(homeAbbr);
        if (AwayLogo.Source == null && !string.IsNullOrEmpty(awayUrl))
            _ = LoadLogoLater(AwayLogo, awayAbbr, awayUrl);
        if (HomeLogo.Source == null && !string.IsNullOrEmpty(homeUrl))
            _ = LoadLogoLater(HomeLogo, homeAbbr, homeUrl);
    }

    private static async System.Threading.Tasks.Task LoadLogoLater(System.Windows.Controls.Image target, string abbr, string url)
    {
        var bmp = await NbaLogoCache.GetAsync(abbr, url);
        if (bmp != null) target.Source = bmp;
    }

    private void ApplyWinnerHighlight(int? awayScore, int? homeScore, bool isFinal)
    {
        bool awayWon = isFinal && awayScore.HasValue && homeScore.HasValue && awayScore.Value > homeScore.Value;
        bool homeWon = isFinal && awayScore.HasValue && homeScore.HasValue && homeScore.Value > awayScore.Value;
        // Names stay SemiBold for both teams (they're titles, not the winner accent).
        AwayName.FontWeight = FontWeights.SemiBold;
        HomeName.FontWeight = FontWeights.SemiBold;
        // Only the *winner's* score is Bold; loser drops to Normal so the accent is clean.
        AwayScore.FontWeight = awayWon ? FontWeights.Bold : FontWeights.Normal;
        HomeScore.FontWeight = homeWon ? FontWeights.Bold : FontWeights.Normal;
    }

    private async Task LoadDetailsAsync()
    {
        var details = await NbaApi.GetGameDetailsAsync(_game.EventId);
        if (details == null)
        {
            StatusLine.Text = Loc.Get("Nba_Err_Network");
            return;
        }

        _espnUrl = details.EspnGameUrl;

        var s = details.Summary;
        AwayName.Text = NbaTeams.DisplayName(s.AwayAbbrev);
        HomeName.Text = NbaTeams.DisplayName(s.HomeAbbrev);
        AwayName.ToolTip = NbaTeams.DisplayName(s.AwayAbbrev);
        HomeName.ToolTip = NbaTeams.DisplayName(s.HomeAbbrev);
        if (s.AwayScore.HasValue) AwayScore.Text = s.AwayScore.Value.ToString();
        if (s.HomeScore.HasValue) HomeScore.Text = s.HomeScore.Value.ToString();
        ApplyWinnerHighlight(s.AwayScore, s.HomeScore, isFinal: s.IsFinal);
        LoadLogos(s.AwayAbbrev, s.AwayLogoUrl, s.HomeAbbrev, s.HomeLogoUrl);

        string status = s.IsLive ? Loc.Get("Nba_Live")
                      : s.IsFinal ? Loc.Get("Nba_Final")
                      : Loc.Get("Nba_Scheduled");
        // For scheduled games ESPN gives us a US-formatted "5/28 - 8:30 PM EDT"
        // which is hostile to a Ukrainian audience - replace with our own local
        // "dd.MM HH:mm" rendering. For live games we keep ESPN's "Q3 04:21" detail
        // because that's actually useful in-game info. For final we don't append
        // anything.
        if (s.IsLive && !string.IsNullOrEmpty(s.StatusDetail))
            status += " - " + s.StatusDetail;
        else if (!s.IsFinal && !s.IsLive)
            status += " - " + s.StartUtc.ToLocalTime().ToString("dd.MM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        StatusLine.Text = status;

        var localDate = s.StartUtc.ToLocalTime();
        VenueLine.Text = string.IsNullOrEmpty(s.Venue)
            ? localDate.ToString("dddd, d MMM yyyy HH:mm", Loc.Culture)
            : localDate.ToString("dddd, d MMM yyyy HH:mm", Loc.Culture) + " - " + s.Venue;

        BuildLineScoreGrid(details);
        BuildLeaders(details);
    }

    private void BuildLineScoreGrid(NbaApi.GameDetails d)
    {
        LineScoreGrid.Children.Clear();
        LineScoreGrid.ColumnDefinitions.Clear();
        LineScoreGrid.RowDefinitions.Clear();

        int qCount = Math.Max(d.HomeLineScore.Count, d.AwayLineScore.Count);
        if (qCount == 0) return;

        // First column = team label, then qCount columns + total column.
        LineScoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < qCount; i++)
            LineScoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        LineScoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        LineScoreGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        LineScoreGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        LineScoreGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddCell("", 0, 0, secondary: true);
        for (int i = 0; i < qCount; i++)
        {
            string label = i < 4 ? Loc.Format("Nba_Details_Quarter", i + 1) : Loc.Get("Nba_Details_OT");
            AddCell(label, 0, i + 1, secondary: true, center: true);
        }
        AddCell("T", 0, qCount + 1, secondary: true, center: true, bold: true);

        AddTeamRow(d.Summary.AwayAbbrev, d.AwayLineScore, d.Summary.AwayScore, 1, qCount);
        AddTeamRow(d.Summary.HomeAbbrev, d.HomeLineScore, d.Summary.HomeScore, 2, qCount);
    }

    private void AddTeamRow(string abbr, System.Collections.Generic.List<int> line, int? total, int row, int qCount)
    {
        AddCell(abbr, row, 0, bold: true);
        for (int i = 0; i < qCount; i++)
        {
            string v = i < line.Count ? line[i].ToString() : "-";
            AddCell(v, row, i + 1, center: true);
        }
        AddCell(total?.ToString() ?? "-", row, qCount + 1, center: true, bold: true);
    }

    private void AddCell(string text, int row, int col, bool secondary = false, bool center = false, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = (Brush)FindResource(secondary ? "MutedTextBrush" : "PrimaryTextBrush"),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(4, 2, 4, 2),
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        LineScoreGrid.Children.Add(tb);
    }

    private void BuildLeaders(NbaApi.GameDetails d)
    {
        AwayLeadersHeader.Text = NbaTeams.DisplayName(d.Summary.AwayAbbrev);
        HomeLeadersHeader.Text = NbaTeams.DisplayName(d.Summary.HomeAbbrev);

        AwayLeadersList.Children.Clear();
        foreach (var p in d.AwayTopPlayers)
            AwayLeadersList.Children.Add(BuildPlayerRow(p));

        HomeLeadersList.Children.Clear();
        foreach (var p in d.HomeTopPlayers)
            HomeLeadersList.Children.Add(BuildPlayerRow(p));
    }

    private FrameworkElement BuildPlayerRow(NbaApi.PlayerLine p)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };
        sp.Children.Add(new TextBlock
        {
            Text = p.Name,
            FontSize = 12,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        sp.Children.Add(new TextBlock
        {
            Text = string.Format("{0} PTS - {1} REB - {2} AST", p.Points, p.Rebounds, p.Assists),
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedTextBrush")
        });
        return sp;
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_espnUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_espnUrl) { UseShellExecute = true });
        }
        catch { }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = true; } catch { }
        Close();
    }
}
