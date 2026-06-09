using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Radio;

public partial class RadioWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Radio_Name");

    // ===== Models =====

    public class RadioStation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public List<RadioStation> Stations { get; set; } = new();
        public string SelectedStationId { get; set; } = "";
        public int Volume { get; set; } = 50;
        public bool Muted { get; set; } = false;
        // False only for a brand-new widget — used to seed the built-in station list
        // exactly once, so clearing the list later is not undone on the next load.
        public bool Initialized { get; set; } = false;
    }

    // ===== Fields =====

    private WidgetState _state = new();
    private readonly MediaPlayer _player = new();
    private bool _isPlaying;
    private bool _quietWasPlaying;
    private bool _isLoaded;
    private bool _suppressSelectionChanged;
    private bool _suppressVolumeEvents;
    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private DispatcherTimer? _saveTimer;
    // Bumped on every playback start so a slow .pls/.m3u resolve from a previous
    // station can't open its stream after the user already switched away.
    private int _playGeneration;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // Icon geometries (Material Design path data).
    private const string PlayIcon = "M8,5.14V19.14L19,12.14L8,5.14Z";
    private const string StopIcon = "M18,18H6V6H18V18Z";
    private const string VolumeIcon =
        "M14,3.23V5.29C16.89,6.15 19,8.83 19,12C19,15.17 16.89,17.84 14,18.7V20.77C18,19.86 " +
        "21,16.28 21,12C21,7.72 18,4.14 14,3.23M16.5,12C16.5,10.23 15.5,8.71 14,7.97V16C15.5," +
        "15.29 16.5,13.76 16.5,12M3,9V15H7L12,20V4L7,9H3Z";
    private const string MuteIcon =
        "M12,4L9.91,6.09L12,8.18M4.27,3L3,4.27L7.73,9H3V15H7L12,20V13.27L16.25,17.53C15.58," +
        "18.04 14.83,18.46 14,18.7V20.77C15.38,20.45 16.63,19.82 17.68,18.96L19.73,21L21," +
        "19.73L12,10.73M19,12C19,12.94 18.8,13.82 18.46,14.64L19.97,16.15C20.62,14.91 21," +
        "13.5 21,12C21,7.72 18,4.14 14,3.23V5.29C16.89,6.15 19,8.83 19,12M16.5,12C16.5,10.23 " +
        "15.5,8.71 14,7.97V10.18L16.45,12.63C16.5,12.43 16.5,12.21 16.5,12Z";

    // ===== IWidget =====

    public string Id => "radio";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Radio_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public RadioWidget()
    {
        InitializeComponent();

        _player.MediaOpened += (_, _) => SetError(null);
        _player.MediaFailed += (_, _) =>
        {
            _isPlaying = false;
            UpdatePlayState();
            SetError(Loc.Get("Radio_Err_Stream"));
        };
        _player.MediaEnded += (_, _) =>
        {
            _isPlaying = false;
            UpdatePlayState();
            SetError(null);
        };

        Loaded += (_, _) => { _isLoaded = true; ApplyState(); };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            // RebuildPanel fires Unloaded then Loaded; a real removal fires only
            // Unloaded. Defer the check so a re-add (rebuild) cancels the stop.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isLoaded) StopPlayback();
            }), DispatcherPriority.Background);
        };
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new RadioSettingsDialog(_state.Stations)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state.Stations = dlg.ResultStations;
            if (!_state.Stations.Any(s => s.Id == _state.SelectedStationId))
                _state.SelectedStationId = _state.Stations.FirstOrDefault()?.Id ?? "";

            RebuildStationCombo();

            // The station being played may have been deleted in the dialog.
            if (_isPlaying && CurrentStation() == null) StopPlayback();

            WidgetServices.RequestSaveStates();
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
        }
        catch { }
    }

    // ===== State / UI sync =====

    // Live language switch: re-read the title, station combo and play-state labels.
    public void OnLanguageChanged() => ApplyState();

    private void ApplyState()
    {
        // No built-in stations - the list starts empty; the user adds their own via the
        // settings dialog. (The Initialized flag is kept only for older saved state.)

        TitleText.Text = InstanceLabel;
        RebuildStationCombo();

        _suppressVolumeEvents = true;
        VolumeSlider.Value = Math.Clamp(_state.Volume, 0, 100);
        _suppressVolumeEvents = false;
        UpdateVolumeUi();
        ApplyVolume();

        UpdatePlayState();
    }

    private RadioStation? CurrentStation() =>
        _state.Stations.FirstOrDefault(s => s.Id == _state.SelectedStationId);

    private void RebuildStationCombo()
    {
        _suppressSelectionChanged = true;
        StationCombo.ItemsSource = null;
        StationCombo.ItemsSource = _state.Stations;
        StationCombo.SelectedItem = CurrentStation();
        _suppressSelectionChanged = false;

        bool hasStations = _state.Stations.Count > 0;
        PrevButton.IsEnabled = hasStations;
        NextButton.IsEnabled = hasStations;
        StationCombo.IsEnabled = hasStations;
        PlayStopButton.IsEnabled = hasStations;
        UpdateNowPlayingUi();
    }

    // Error line is hidden in every normal state — shown only on a playback failure.
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
        }

        UpdateNowPlayingUi();
    }

    private void UpdatePlayState()
    {
        PlayStopIconPath.Data = Geometry.Parse(_isPlaying ? StopIcon : PlayIcon);
        UpdateNowPlayingUi();
    }

    private void UpdateNowPlayingUi()
    {
        var station = CurrentStation();
        StationDisplayText.Text = station?.Name ?? Loc.Get("Radio_NoStations");

        if (ErrorText.Visibility == Visibility.Visible)
            StatusText.Text = Loc.Get("Radio_Status_Error");
        else if (_isPlaying)
            StatusText.Text = Loc.Get("Radio_Status_Playing");
        else
            StatusText.Text = station == null ? Loc.Get("Radio_Status_Empty") : Loc.Get("Radio_Status_Ready");
    }

    private void UpdateVolumeUi()
    {
        bool silent = _state.Muted || _state.Volume <= 0;
        VolumeIconPath.Data = Geometry.Parse(silent ? MuteIcon : VolumeIcon);
        VolumeText.Text = $"{_state.Volume}%";
    }

    // The slider position (0-100) is mapped to amplitude with a square taper so the
    // lower part of the slider is genuinely quiet — a linear map made 5% feel like 50%.
    private void ApplyVolume()
    {
        double t = Math.Clamp(_state.Volume, 0, 100) / 100.0;
        _player.Volume = _state.Muted ? 0.0 : t * t;
    }

    // ===== Playback =====

    private void PlayStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) StopPlayback();
        else StartPlayback(CurrentStation());
    }

    private async void StartPlayback(RadioStation? station)
    {
        if (station == null || string.IsNullOrWhiteSpace(station.Url))
        {
            SetError(Loc.Get("Radio_Err_SelectStation"));
            return;
        }

        int gen = ++_playGeneration;
        _isPlaying = true;
        UpdatePlayState();
        SetError(null);

        string url = station.Url.Trim();
        if (url.EndsWith(".pls", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = await ResolvePlaylistAsync(url);
            if (!string.IsNullOrWhiteSpace(resolved)) url = resolved!;
        }

        // The user may have pressed Stop or switched stations while resolving.
        if (gen != _playGeneration || !_isPlaying) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _isPlaying = false;
            UpdatePlayState();
            SetError(Loc.Get("Radio_Err_BadUrl"));
            return;
        }

        try
        {
            _player.Stop();
            _player.Open(uri);
            ApplyVolume();
            _player.Play();
        }
        catch
        {
            _isPlaying = false;
            UpdatePlayState();
            SetError(Loc.Get("Radio_Err_Stream"));
        }
    }

    private void StopPlayback()
    {
        _playGeneration++;
        _isPlaying = false;
        try
        {
            _player.Stop();
            _player.Close();
        }
        catch { }
        UpdatePlayState();
        SetError(null);
    }

    // Minute of silence: stop on enter, restart the same station on exit.
    public void SetQuietMode(bool quiet)
    {
        if (quiet)
        {
            _quietWasPlaying = _isPlaying;
            if (_isPlaying) StopPlayback();
        }
        else
        {
            if (_quietWasPlaying) StartPlayback(CurrentStation());
            _quietWasPlaying = false;
        }
    }

    // Resolves a .pls / .m3u playlist to the first direct stream URL inside it.
    private static async Task<string?> ResolvePlaylistAsync(string url)
    {
        try
        {
            var content = await Http.GetStringAsync(url);
            foreach (var raw in content.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                // .pls — "File1=http://..."
                if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = line.IndexOf('=');
                    if (eq >= 0)
                    {
                        var v = line.Substring(eq + 1).Trim();
                        if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return v;
                    }
                    continue;
                }

                // .m3u — a bare URL on its own line.
                if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return line;
            }
        }
        catch { }
        return null;
    }

    // ===== Station selection =====

    private void StationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (StationCombo.SelectedItem is not RadioStation st) return;

        _state.SelectedStationId = st.Id;
        UpdateNowPlayingUi();
        ScheduleSave();

        // Switching station while playing immediately retunes.
        if (_isPlaying) StartPlayback(st);
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => StepStation(-1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => StepStation(+1);

    private void StepStation(int delta)
    {
        int n = _state.Stations.Count;
        if (n == 0) return;
        int idx = StationCombo.SelectedIndex;
        if (idx < 0) idx = 0;
        idx = (idx + delta % n + n) % n;
        StationCombo.SelectedIndex = idx; // raises SelectionChanged
    }

    // ===== Volume =====

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _state.Volume = (int)Math.Round(e.NewValue);

        if (_suppressVolumeEvents)
        {
            UpdateVolumeUi();
            ApplyVolume();
            return;
        }

        // Dragging the volume slider lifts the mute.
        if (_state.Muted) _state.Muted = false;

        UpdateVolumeUi();
        ApplyVolume();
        ScheduleSave();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        _state.Muted = !_state.Muted;
        UpdateVolumeUi();
        ApplyVolume();
        ScheduleSave();
    }

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
