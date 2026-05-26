using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Clock;

public enum DayOfWeekFormat
{
    Full,
    Short
}

public enum DateFormat
{
    LongUkrainian,
    Numeric,
    Iso
}

public partial class ClockWidget : UserControl, IWidget
{
    private readonly DispatcherTimer _timer;

    public string Id => "clock";
    public string DisplayName => Loc.Get("Clock_Name");
    public string Description => Loc.Get("Clock_Desc");
    public bool HasSettings => true;

    public class WidgetState
    {
        public bool ShowTime { get; set; } = true;
        public bool Use24Hour { get; set; } = true;
        public bool ShowSeconds { get; set; } = false;

        public bool ShowDayOfWeek { get; set; } = true;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DayOfWeekFormat DayOfWeekFormat { get; set; } = DayOfWeekFormat.Full;

        public bool ShowDate { get; set; } = true;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DateFormat DateFormat { get; set; } = DateFormat.LongUkrainian;
    }

    private WidgetState _state = new();

    public ClockWidget()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Update();

        Loaded += (_, _) => { Update(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new ClockSettingsDialog(_state)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state = dlg.Result;
            Update();
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

    private void Update()
    {
        var now = DateTime.Now;
        var culture = Loc.Culture;
        bool isEn = Loc.Current == AppLanguage.En;

        if (_state.ShowTime)
        {
            string timeFormat = (_state.Use24Hour, _state.ShowSeconds) switch
            {
                (true, true) => "HH:mm:ss",
                (true, false) => "HH:mm",
                (false, true) => "h:mm:ss tt",
                (false, false) => "h:mm tt"
            };
            TimeText.Text = now.ToString(timeFormat, culture);
            TimeText.Visibility = Visibility.Visible;
        }
        else
        {
            TimeText.Visibility = Visibility.Collapsed;
        }

        bool showAnything = _state.ShowDayOfWeek || _state.ShowDate;
        if (!showAnything)
        {
            DateTimeText.Visibility = Visibility.Collapsed;
            return;
        }

        var sb = new StringBuilder();
        if (_state.ShowDayOfWeek)
        {
            string dowFormat = _state.DayOfWeekFormat == DayOfWeekFormat.Short ? "ddd" : "dddd";
            sb.Append(Capitalize(now.ToString(dowFormat, culture), culture));
        }
        if (_state.ShowDate)
        {
            if (sb.Length > 0) sb.Append(" · ");
            // "LongUkrainian" is the historical enum name for the long-date option;
            // the actual format follows the current UI language.
            string longFormat = isEn ? "MMMM d, yyyy" : "d MMMM yyyy 'року'";
            string numericFormat = isEn ? "M/d/yyyy" : "dd.MM.yyyy";
            string dateFormat = _state.DateFormat switch
            {
                DateFormat.LongUkrainian => longFormat,
                DateFormat.Numeric => numericFormat,
                DateFormat.Iso => "yyyy-MM-dd",
                _ => longFormat
            };
            sb.Append(now.ToString(dateFormat, culture));
        }

        DateTimeText.Text = sb.ToString();
        DateTimeText.Visibility = Visibility.Visible;
    }

    private static string Capitalize(string s, CultureInfo culture) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], culture) + s.Substring(1);
}
