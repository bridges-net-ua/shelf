using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Shelf.Sdk;

// The widget's own namespace is "Shelf.Widgets.Stopwatch", which shadows
// System.Diagnostics.Stopwatch. Aliasing keeps the high-resolution clock usable
// inside the class without sprinkling fully-qualified names through the body.
using SwClock = System.Diagnostics.Stopwatch;

namespace Shelf.Widgets.Stopwatch;

public partial class StopwatchWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Stopwatch_Name");

    // ===== Model =====

    public class WidgetState
    {
        public string Title { get; set; } = "";
    }

    // ===== Fields =====

    private WidgetState _state = new();

    // System.Diagnostics.Stopwatch is the underlying clock (high-resolution).
    // _elapsed accumulates across pauses; _sw measures the current run since
    // the last Start/Resume; both are summed via CurrentElapsed.
    private readonly SwClock _sw = new();
    private bool _running;
    private TimeSpan _elapsed;
    private readonly List<TimeSpan> _laps = new();

    // 50 ms tick - centisecond display refreshes smoothly without flooding the UI.
    private readonly DispatcherTimer _tick;

    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private bool _isLoaded;

    // ===== IWidget =====

    public string Id => "stopwatch";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Stopwatch_Desc");
    public bool HasSettings => false;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public StopwatchWidget()
    {
        InitializeComponent();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _tick.Tick += Tick;

        Loaded += (_, _) =>
        {
            _isLoaded = true;
            ApplyState();
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            // RebuildPanel fires Unloaded then Loaded; a real removal fires only
            // Unloaded. Defer so a re-add cancels the cleanup.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isLoaded) DisposeWidget();
            }), DispatcherPriority.Background);
        };
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner) { /* no settings dialog */ }

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

    private void DisposeWidget()
    {
        _tick.Stop();
        _sw.Stop();
    }

    // ===== State / UI sync =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;
        UpdateUi();
        RefreshDisplay();
        // Survived a panel rebuild while counting - keep the tick alive.
        if (_running) EnsureTick();
    }

    private TimeSpan CurrentElapsed()
        => _elapsed + (_running ? _sw.Elapsed : TimeSpan.Zero);

    // ===== Tick =====

    private void EnsureTick()
    {
        if (!_tick.IsEnabled) _tick.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_running) RefreshDisplay();
    }

    // ===== Buttons =====

    private void RightButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) Pause();
        else Start();
    }

    private void LeftButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) AddLap();
        else Reset();
    }

    private void Start()
    {
        _sw.Restart();
        _running = true;
        EnsureTick();
        UpdateUi();
        RefreshDisplay();
    }

    private void Pause()
    {
        _elapsed += _sw.Elapsed;
        _sw.Stop();
        _running = false;
        _tick.Stop();
        UpdateUi();
        RefreshDisplay();
    }

    private void Reset()
    {
        _sw.Reset();
        _running = false;
        _elapsed = TimeSpan.Zero;
        _laps.Clear();
        _tick.Stop();
        RebuildLapList();
        UpdateUi();
        RefreshDisplay();
    }

    private void AddLap()
    {
        _laps.Add(CurrentElapsed());
        RebuildLapList();
        UpdateUi();
    }

    // ===== UI =====

    private void UpdateUi()
    {
        bool hasElapsed = CurrentElapsed() > TimeSpan.Zero;

        RightButton.Content = _running
            ? Loc.Get("Timer_Pause")
            : (hasElapsed ? Loc.Get("Timer_Resume") : Loc.Get("Timer_Start"));

        LeftButton.Content = _running ? Loc.Get("Timer_Lap") : Loc.Get("Timer_Reset");
        LeftButton.IsEnabled = _running || hasElapsed;

        LapBox.Visibility = _laps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshDisplay()
        => StopwatchDisplay.Text = FormatStopwatch(CurrentElapsed());

    private static string FormatStopwatch(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        int totalCs = (int)(t.TotalMilliseconds / 10);
        int cs = totalCs % 100;
        int totalSec = totalCs / 100;
        int s = totalSec % 60;
        int totalMin = totalSec / 60;
        int m = totalMin % 60;
        int h = totalMin / 60;
        return h > 0
            ? $"{h:0}:{m:00}:{s:00}.{cs:00}"
            : $"{m:00}:{s:00}.{cs:00}";
    }

    private void RebuildLapList()
    {
        LapList.Children.Clear();

        // Newest lap on top.
        for (int i = _laps.Count - 1; i >= 0; i--)
        {
            TimeSpan total = _laps[i];
            TimeSpan split = i == 0 ? _laps[0] : _laps[i] - _laps[i - 1];

            var row = new Grid { Margin = new Thickness(6, 3, 6, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var num = new TextBlock
            {
                Text = Loc.Format("Timer_LapNumber", i + 1),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            num.SetResourceReference(ForegroundProperty, "MutedTextBrush");
            Grid.SetColumn(num, 0);

            var splitText = new TextBlock
            {
                Text = FormatStopwatch(split),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            splitText.SetResourceReference(ForegroundProperty, "SecondaryTextBrush");
            Grid.SetColumn(splitText, 1);

            var totalText = new TextBlock
            {
                Text = FormatStopwatch(total),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            totalText.SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
            Grid.SetColumn(totalText, 2);

            row.Children.Add(num);
            row.Children.Add(splitText);
            row.Children.Add(totalText);
            LapList.Children.Add(row);
        }
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
