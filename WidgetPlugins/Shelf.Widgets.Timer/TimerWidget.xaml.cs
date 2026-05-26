using System;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Timer;

public partial class TimerWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Timer_Name");

    // ===== Model =====

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public int TimerH { get; set; }
        public int TimerM { get; set; } = 5;     // default countdown: 5 minutes
        public int TimerS { get; set; }
    }

    // ===== Fields =====

    private WidgetState _state = new();

    // System.Diagnostics.Stopwatch is the underlying high-resolution clock for the
    // countdown. _timerRunStart captures the remaining at the last (re)start; the
    // displayed remaining is computed as (runStart - sw.Elapsed) each tick.
    private readonly Stopwatch _timerSw = new();
    private bool _timerRunning;
    private bool _timerStarted;       // false = idle (editable), true = running or paused
    private bool _timerFinished;      // reached zero, alarm active
    private TimeSpan _timerRemaining; // remaining when paused / at finish
    private TimeSpan _timerRunStart;

    // 50 ms tick - the countdown only refreshes seconds, but the smooth interval
    // keeps the boundary tick close to the actual second.
    private readonly DispatcherTimer _tick;
    private DispatcherTimer? _alarmTimer;
    private int _alarmCount;

    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private bool _isLoaded;
    private DispatcherTimer? _saveTimer;

    // ===== IWidget =====

    public string Id => "timer";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Timer_Desc");
    public bool HasSettings => false;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public TimerWidget()
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
            // Unloaded. Defer the check so a re-add (rebuild) cancels the cleanup.
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
        _alarmTimer?.Stop();
        StopFlash();
        _timerSw.Stop();
    }

    // ===== State / UI sync =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;

        HoursBox.Text = _state.TimerH.ToString("00");
        MinutesBox.Text = _state.TimerM.ToString("00");
        SecondsBox.Text = _state.TimerS.ToString("00");

        Preset1.Content = Loc.Format("Timer_PresetMin", 1);
        Preset5.Content = Loc.Format("Timer_PresetMin", 5);
        Preset10.Content = Loc.Format("Timer_PresetMin", 10);
        Preset25.Content = Loc.Format("Timer_PresetMin", 25);

        UpdateTimerUi();
        RefreshTimerDisplay();

        // Survived a panel rebuild while counting - keep the tick alive.
        if (_timerRunning) EnsureTick();
    }

    // ===== Tick =====

    private void EnsureTick()
    {
        if (!_tick.IsEnabled) _tick.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (!_timerRunning) return;

        var rem = _timerRunStart - _timerSw.Elapsed;
        if (rem <= TimeSpan.Zero)
        {
            TimerFinished();
        }
        else
        {
            _timerRemaining = rem;
            RefreshTimerDisplay();
        }
    }

    // ===== Countdown timer =====

    private void TimerRightButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_timerStarted) StartTimer();
        else if (_timerRunning) PauseTimer();
        else ResumeTimer();
    }

    private void TimerLeftButton_Click(object sender, RoutedEventArgs e) => ResetTimer();

    private void StartTimer()
    {
        CommitTimeBoxes();
        var total = new TimeSpan(_state.TimerH, _state.TimerM, _state.TimerS);
        if (total <= TimeSpan.Zero) return;

        _timerRemaining = total;
        _timerRunStart = total;
        _timerStarted = true;
        _timerFinished = false;
        _timerRunning = true;
        _timerSw.Restart();

        EnsureTick();
        UpdateTimerUi();
        RefreshTimerDisplay();
    }

    private void PauseTimer()
    {
        var rem = _timerRunStart - _timerSw.Elapsed;
        _timerRemaining = rem > TimeSpan.Zero ? rem : TimeSpan.Zero;
        _timerSw.Stop();
        _timerRunning = false;
        _tick.Stop();
        UpdateTimerUi();
        RefreshTimerDisplay();
    }

    private void ResumeTimer()
    {
        if (_timerRemaining <= TimeSpan.Zero) return;
        _timerRunStart = _timerRemaining;
        _timerSw.Restart();
        _timerRunning = true;
        EnsureTick();
        UpdateTimerUi();
        RefreshTimerDisplay();
    }

    private void ResetTimer()
    {
        StopAlarm();
        _timerSw.Reset();
        _timerRunning = false;
        _timerStarted = false;
        _timerFinished = false;
        _timerRemaining = TimeSpan.Zero;
        _tick.Stop();
        UpdateTimerUi();
        RefreshTimerDisplay();
    }

    private void TimerFinished()
    {
        _timerRunning = false;
        _timerSw.Stop();
        _timerRemaining = TimeSpan.Zero;
        _timerFinished = true;
        _tick.Stop();

        UpdateTimerUi();
        RefreshTimerDisplay();
        StartAlarm();
    }

    private void UpdateTimerUi()
    {
        bool idle = !_timerStarted && !_timerFinished;

        TimerEditArea.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        TimerPresets.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        TimerDisplay.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        TimerStatusText.Visibility = _timerFinished ? Visibility.Visible : Visibility.Collapsed;

        TimerLeftButton.Content = Loc.Get("Timer_Reset");
        TimerLeftButton.IsEnabled = !idle;

        if (_timerFinished)
        {
            // Reset spans the whole row; the primary button is hidden.
            TimerRightButton.Visibility = Visibility.Collapsed;
            Grid.SetColumnSpan(TimerLeftButton, 3);
            TimerLeftButton.Margin = new Thickness(0);
        }
        else
        {
            TimerRightButton.Visibility = Visibility.Visible;
            Grid.SetColumnSpan(TimerLeftButton, 1);
            TimerLeftButton.Margin = new Thickness(0);

            if (idle)
                TimerRightButton.Content = Loc.Get("Timer_Start");
            else if (_timerRunning)
                TimerRightButton.Content = Loc.Get("Timer_Pause");
            else
                TimerRightButton.Content = Loc.Get("Timer_Resume");
        }
    }

    private void RefreshTimerDisplay()
    {
        TimeSpan show;
        if (_timerRunning)
        {
            show = _timerRunStart - _timerSw.Elapsed;
            if (show < TimeSpan.Zero) show = TimeSpan.Zero;
        }
        else
        {
            show = _timerRemaining;
        }

        // Ceiling so a fresh 5:00 countdown shows "00:05:00", not "00:04:59".
        int totalSeconds = (int)Math.Ceiling(show.TotalSeconds - 1e-6);
        if (totalSeconds < 0) totalSeconds = 0;
        int h = totalSeconds / 3600;
        int m = (totalSeconds % 3600) / 60;
        int s = totalSeconds % 60;
        TimerDisplay.Text = $"{h:00}:{m:00}:{s:00}";
    }

    // ===== Alarm =====

    private void StartAlarm()
    {
        _alarmCount = 0;
        PlayAlarmSound();

        if (_alarmTimer == null)
        {
            _alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _alarmTimer.Tick += (_, _) =>
            {
                _alarmCount++;
                if (_alarmCount >= 9)
                {
                    _alarmTimer!.Stop();
                    return;
                }
                PlayAlarmSound();
            };
        }
        _alarmTimer.Start();
        StartFlash();
    }

    private void StopAlarm()
    {
        _alarmTimer?.Stop();
        StopFlash();
    }

    private static void PlayAlarmSound()
    {
        try { SystemSounds.Exclamation.Play(); }
        catch { }
    }

    private void StartFlash()
    {
        var anim = new DoubleAnimation(1.0, 0.2, TimeSpan.FromMilliseconds(450))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        TimerDisplay.BeginAnimation(OpacityProperty, anim);
    }

    private void StopFlash()
    {
        TimerDisplay.BeginAnimation(OpacityProperty, null);
        TimerDisplay.Opacity = 1.0;
    }

    // ===== hh:mm:ss editing =====

    private void DigitOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsDigit);

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e) => CommitTimeBoxes();

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RepeatButton rb || rb.Tag is not string tag || tag.Length < 2) return;

        ReadTimeBoxes(out int h, out int m, out int s);
        int delta = tag[1] == '+' ? 1 : -1;
        switch (tag[0])
        {
            case 'h': h = Clamp(h + delta, 0, 99); break;
            case 'm': m = Clamp(m + delta, 0, 59); break;
            case 's': s = Clamp(s + delta, 0, 59); break;
        }
        WriteTimeBoxes(h, m, s);
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag && int.TryParse(tag, out int minutes))
            WriteTimeBoxes(0, minutes, 0);
    }

    private void CommitTimeBoxes()
    {
        ReadTimeBoxes(out int h, out int m, out int s);
        WriteTimeBoxes(h, m, s);
    }

    private void ReadTimeBoxes(out int h, out int m, out int s)
    {
        h = ParseClamp(HoursBox.Text, 0, 99);
        m = ParseClamp(MinutesBox.Text, 0, 59);
        s = ParseClamp(SecondsBox.Text, 0, 59);
    }

    private void WriteTimeBoxes(int h, int m, int s)
    {
        HoursBox.Text = h.ToString("00");
        MinutesBox.Text = m.ToString("00");
        SecondsBox.Text = s.ToString("00");
        _state.TimerH = h;
        _state.TimerM = m;
        _state.TimerS = s;
        ScheduleSave();
    }

    private static int ParseClamp(string? text, int min, int max)
        => int.TryParse(text, out int v) ? Clamp(v, min, max) : min;

    private static int Clamp(int v, int min, int max)
        => v < min ? min : (v > max ? max : v);

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
