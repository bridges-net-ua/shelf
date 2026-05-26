using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Shelf.Sdk;

namespace Shelf.Widgets.Radio;

public partial class RadioSettingsDialog : Window
{
    public List<RadioWidget.RadioStation> ResultStations { get; private set; }

    private readonly ObservableCollection<RadioWidget.RadioStation> _stations;
    private bool _loading;

    public RadioSettingsDialog(List<RadioWidget.RadioStation> stations)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        // Work on copies so "Скасувати" discards every edit.
        _stations = new ObservableCollection<RadioWidget.RadioStation>(
            stations.Select(s => new RadioWidget.RadioStation
            {
                Id = s.Id,
                Name = s.Name,
                Url = s.Url
            }));
        ResultStations = stations;

        StationList.ItemsSource = _stations;
        if (_stations.Count > 0) StationList.SelectedIndex = 0;
        UpdateEditPanel();
    }

    private RadioWidget.RadioStation? Selected =>
        StationList.SelectedItem as RadioWidget.RadioStation;

    private void StationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateEditPanel();

    private void UpdateEditPanel()
    {
        _loading = true;
        var st = Selected;
        bool has = st != null;

        NameBox.IsEnabled = has;
        UrlBox.IsEnabled = has;
        RemoveButton.IsEnabled = has;

        NameBox.Text = st?.Name ?? "";
        UrlBox.Text = st?.Url ?? "";
        _loading = false;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not { } st) return;
        st.Name = NameBox.Text;
        StationList.Items.Refresh();
    }

    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not { } st) return;
        st.Url = UrlBox.Text;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var st = new RadioWidget.RadioStation { Name = Loc.Get("Radio_NewStation"), Url = "" };
        _stations.Add(st);
        StationList.SelectedItem = st;
        StationList.ScrollIntoView(st);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } st) return;
        int idx = _stations.IndexOf(st);
        _stations.Remove(st);
        if (_stations.Count > 0)
            StationList.SelectedIndex = idx < _stations.Count ? idx : _stations.Count - 1;
        else
            UpdateEditPanel();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var result = new List<RadioWidget.RadioStation>();
        foreach (var s in _stations)
        {
            var name = (s.Name ?? "").Trim();
            result.Add(new RadioWidget.RadioStation
            {
                Id = s.Id,
                Name = string.IsNullOrEmpty(name) ? Loc.Get("Radio_DefaultStationName") : name,
                Url = (s.Url ?? "").Trim()
            });
        }
        ResultStations = result;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
