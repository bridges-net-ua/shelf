using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Shelf.Sdk;

namespace Shelf.Widgets.Nba;

public partial class NbaSettingsDialog : Window
{
    private sealed class FavRow
    {
        public string Abbreviation { get; init; } = "";
        public string Display => $"{NbaTeams.DisplayName(Abbreviation)} ({Abbreviation})";
    }

    private sealed class AddRow
    {
        public string Abbreviation { get; init; } = "";
        public string Display => $"{NbaTeams.DisplayName(Abbreviation)} ({Abbreviation})";
    }

    private readonly ObservableCollection<FavRow> _favorites = new();
    private readonly ObservableCollection<AddRow> _addable = new();

    public List<string> ResultFavorites { get; private set; } = new();
    public int ResultLeagueGameCount { get; private set; } = 2;
    public int ResultLeaguePastCount { get; private set; } = 1;
    public int ResultFavoriteNextCount { get; private set; } = 1;

    public NbaSettingsDialog(List<string> initialFavorites, int initialLeagueGameCount, int initialLeaguePastCount, int initialFavoriteNextCount)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        foreach (var abbr in initialFavorites.Select(NbaTeams.Normalize).Where(s => !string.IsNullOrEmpty(s)).Distinct())
            _favorites.Add(new FavRow { Abbreviation = abbr });

        FavoritesList.ItemsSource = _favorites;
        FavoritesList.DisplayMemberPath = nameof(FavRow.Display);

        AddCombo.ItemsSource = _addable;
        AddCombo.DisplayMemberPath = nameof(AddRow.Display);

        RebuildAddable();

        LeagueGamesSlider.Value = Math.Clamp(initialLeagueGameCount, 1, 5);
        LeaguePastSlider.Value = Math.Clamp(initialLeaguePastCount, 1, 5);
        FavoriteNextSlider.Value = Math.Clamp(initialFavoriteNextCount, 1, 5);
        UpdateSliderLabels();
    }

    private void RebuildAddable()
    {
        _addable.Clear();
        var current = new HashSet<string>(_favorites.Select(f => f.Abbreviation), StringComparer.OrdinalIgnoreCase);
        foreach (var t in NbaTeams.All.OrderBy(t => t.FullName, StringComparer.CurrentCulture))
        {
            if (!current.Contains(t.Abbreviation))
                _addable.Add(new AddRow { Abbreviation = t.Abbreviation });
        }
        if (_addable.Count > 0) AddCombo.SelectedIndex = 0;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (AddCombo.SelectedItem is AddRow row)
        {
            _favorites.Add(new FavRow { Abbreviation = row.Abbreviation });
            RebuildAddable();
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is FavRow row)
        {
            _favorites.Remove(row);
            RebuildAddable();
        }
    }

    private void LeagueGamesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateSliderLabels();

    private void LeaguePastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateSliderLabels();

    private void FavoriteNextSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateSliderLabels();

    private void UpdateSliderLabels()
    {
        if (LeagueGamesLabel == null || LeaguePastLabel == null || FavoriteNextLabel == null) return;
        int upcoming = (int)Math.Round(LeagueGamesSlider.Value);
        int past = (int)Math.Round(LeaguePastSlider.Value);
        int next = (int)Math.Round(FavoriteNextSlider.Value);
        LeagueGamesLabel.Text = Loc.Format("Nba_Settings_LeagueGameCount", upcoming);
        LeaguePastLabel.Text = Loc.Format("Nba_Settings_LeaguePastCount", past);
        FavoriteNextLabel.Text = Loc.Format("Nba_Settings_FavoriteNextCount", next);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultFavorites = _favorites.Select(f => f.Abbreviation).ToList();
        ResultLeagueGameCount = Math.Clamp((int)Math.Round(LeagueGamesSlider.Value), 1, 5);
        ResultLeaguePastCount = Math.Clamp((int)Math.Round(LeaguePastSlider.Value), 1, 5);
        ResultFavoriteNextCount = Math.Clamp((int)Math.Round(FavoriteNextSlider.Value), 1, 5);
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
