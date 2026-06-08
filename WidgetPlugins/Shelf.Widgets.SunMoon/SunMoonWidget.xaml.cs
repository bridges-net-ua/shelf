using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.SunMoon;

public partial class SunMoonWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("SunMoon_Name");

    // ===== Model =====

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public string City { get; set; } = "Київ";
        // Cached after a successful geocode so each refresh skips the network call.
        // Cleared when the city changes.
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string ResolvedName { get; set; } = "";
        // IANA timezone of the city (e.g. "Europe/Kyiv"), from the geocode response,
        // so times are shown in the city's local time, not the PC's.
        public string TimeZoneId { get; set; } = "";

        // Per-block visibility - every block can be toggled independently.
        public bool ShowSun { get; set; } = true;
        public bool ShowDayLength { get; set; } = true;
        public bool ShowGoldenBlue { get; set; } = true;
        public bool ShowMoon { get; set; } = true;
        public bool ShowMoonExtra { get; set; } = true;
    }

    private WidgetState _state = new();
    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private DispatcherTimer? _timer;
    private int _fetchGeneration;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // ===== IWidget =====

    public string Id => "sunmoon";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("SunMoon_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public SunMoonWidget()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyState();
        Unloaded += (_, _) => _timer?.Stop();
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new SunMoonSettingsDialog(_state)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            string newCity = (dlg.ResultCity ?? "").Trim();
            string oldCity = (_state.City ?? "").Trim();
            bool cityChanged = !string.Equals(newCity, oldCity, StringComparison.OrdinalIgnoreCase);

            _state.City = newCity;
            _state.ShowSun = dlg.ResultShowSun;
            _state.ShowDayLength = dlg.ResultShowDayLength;
            _state.ShowGoldenBlue = dlg.ResultShowGoldenBlue;
            _state.ShowMoon = dlg.ResultShowMoon;
            _state.ShowMoonExtra = dlg.ResultShowMoonExtra;

            if (cityChanged)
            {
                // Force a re-geocode for the new city.
                _state.Latitude = null;
                _state.Longitude = null;
                _state.ResolvedName = "";
                _state.TimeZoneId = "";
            }

            ApplyVisibility();
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
        }
        catch { }
    }

    // ===== Lifecycle / refresh =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;
        ApplyVisibility();

        if (_timer == null)
        {
            // Times are static for the day; a 1-minute tick is enough to roll over
            // at midnight and keep "next new/full" fresh. Recompute is local & cheap.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += (_, _) => { if (HasCoords) Recompute(); };
        }
        _timer.Stop();
        _timer.Start();

        RefreshAsync();
    }

    private bool HasCoords => _state.Latitude.HasValue && _state.Longitude.HasValue;

    // Shows/hides each block and its top separator. The separator of the first
    // visible block is hidden so no divider ever dangles at the top.
    private void ApplyVisibility()
    {
        (UIElement sep, UIElement panel, bool show)[] blocks =
        {
            (SunSep, SunBlock, _state.ShowSun),
            (DaySep, DayLengthPanel, _state.ShowDayLength),
            (GoldenSep, GoldenBluePanel, _state.ShowGoldenBlue),
            (MoonSep, MoonCore, _state.ShowMoon),
            (MoonExtraSep, MoonExtraPanel, _state.ShowMoonExtra),
        };
        bool firstShown = true;
        foreach (var (sep, panel, show) in blocks)
        {
            panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            sep.Visibility = (show && !firstShown) ? Visibility.Visible : Visibility.Collapsed;
            if (show) firstShown = false;
        }
    }

    private async void RefreshAsync()
    {
        string city = (_state.City ?? "").Trim();
        if (city.Length == 0)
        {
            SetError(Loc.Get("SunMoon_Err_NoCity"));
            return;
        }

        if (HasCoords)
        {
            // All data is computed locally - no network needed.
            Recompute();
            return;
        }

        int gen = ++_fetchGeneration;
        RefreshButton.IsEnabled = false;
        SetError(null);
        CityText.Text = Loc.Get("SunMoon_Loading");

        try
        {
            var geo = await GeocodeAsync(city);
            if (gen != _fetchGeneration) return;
            if (geo == null)
            {
                SetError(Loc.Get("SunMoon_Err_City"));
                return;
            }
            _state.Latitude = geo.Value.lat;
            _state.Longitude = geo.Value.lon;
            _state.ResolvedName = geo.Value.name;
            _state.TimeZoneId = geo.Value.tz;
            WidgetServices.RequestSaveStates();
            Recompute();
        }
        catch
        {
            if (gen == _fetchGeneration) SetError(Loc.Get("SunMoon_Err_Network"));
        }
        finally
        {
            if (gen == _fetchGeneration) RefreshButton.IsEnabled = true;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAsync();

    // ===== Local computation & render =====

    private void Recompute()
    {
        if (!HasCoords) return;
        double lat = _state.Latitude!.Value;
        double lon = _state.Longitude!.Value;
        var tz = ResolveTz(_state.TimeZoneId);

        SetError(null);
        CityText.Text = string.IsNullOrWhiteSpace(_state.ResolvedName)
            ? (_state.City ?? "").Trim()
            : _state.ResolvedName;

        DateTime utcNow = DateTime.UtcNow;
        DateTime cityNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        var today = DateOnly.FromDateTime(cityNow);
        var yesterday = today.AddDays(-1);

        // --- Sun core ---
        var sunrise = Astronomy.SunEventUtc(today, lat, lon, 90.833, rising: true);
        var sunset = Astronomy.SunEventUtc(today, lat, lon, 90.833, rising: false);

        if (sunrise == null && sunset == null)
        {
            var polar = Astronomy.GetPolarState(today, lat, lon);
            PolarText.Text = polar == Astronomy.PolarState.PolarDay
                ? Loc.Get("SunMoon_PolarDay")
                : Loc.Get("SunMoon_PolarNight");
            PolarText.Visibility = Visibility.Visible;
            SunCore.Visibility = Visibility.Collapsed;
        }
        else
        {
            PolarText.Visibility = Visibility.Collapsed;
            SunCore.Visibility = Visibility.Visible;
            SunriseText.Text = FmtTime(sunrise, tz);
            SunsetText.Text = FmtTime(sunset, tz);
        }

        // --- Day length + solar noon (single line) ---
        if (_state.ShowDayLength)
        {
            if (sunrise.HasValue && sunset.HasValue)
            {
                var len = sunset.Value - sunrise.Value;
                DayLengthRun.Text = FmtDuration(len);

                var rY = Astronomy.SunEventUtc(yesterday, lat, lon, 90.833, true);
                var sY = Astronomy.SunEventUtc(yesterday, lat, lon, 90.833, false);
                if (rY.HasValue && sY.HasValue)
                {
                    int diffMin = (int)Math.Round((len - (sY.Value - rY.Value)).TotalMinutes);
                    string min = Loc.Get("SunMoon_MinShort");
                    string diffStr = diffMin > 0 ? "⇧ +" + diffMin + " " + min
                                   : diffMin < 0 ? "⇩ -" + Math.Abs(diffMin) + " " + min
                                   : "0 " + min;
                    DayDiffRun.Text = " " + diffStr;
                }
                else DayDiffRun.Text = "";
            }
            else
            {
                DayLengthRun.Text = "-";
                DayDiffRun.Text = "";
            }
            SolarNoonRun.Text = FmtTime(Astronomy.SolarNoonUtc(today, lon), tz);
        }

        // --- Golden + blue hour (title lives in XAML; here only the times) ---
        if (_state.ShowGoldenBlue)
        {
            // Golden hour: Sun altitude -4 deg .. +6 deg  (zenith 94 .. 84)
            string gm = Range(Astronomy.SunEventUtc(today, lat, lon, 94, true),
                              Astronomy.SunEventUtc(today, lat, lon, 84, true), tz);
            string ge = Range(Astronomy.SunEventUtc(today, lat, lon, 84, false),
                              Astronomy.SunEventUtc(today, lat, lon, 94, false), tz);
            // NBSP binds each "morning/evening + range" group; the only breakable space
            // is before "evening", so the evening time never splits from its label.
            string nb = " ";
            GoldenHourText.Text = Loc.Get("SunMoon_Morning") + nb + gm + nb + "· "
                                + Loc.Get("SunMoon_Evening") + nb + ge;

            // Blue hour: Sun altitude -6 deg .. -4 deg  (zenith 96 .. 94)
            string bm = Range(Astronomy.SunEventUtc(today, lat, lon, 96, true),
                              Astronomy.SunEventUtc(today, lat, lon, 94, true), tz);
            string be = Range(Astronomy.SunEventUtc(today, lat, lon, 94, false),
                              Astronomy.SunEventUtc(today, lat, lon, 96, false), tz);
            string nbb = " ";
            BlueHourText.Text = Loc.Get("SunMoon_Morning") + nbb + bm + nbb + "· "
                              + Loc.Get("SunMoon_Evening") + nbb + be;
        }

        // --- Moon core ---
        var (illum, age, phaseIdx, waxing) = Astronomy.MoonPhase(utcNow);
        RenderMoon(illum, waxing);
        MoonPhaseText.Text = Loc.Get("SunMoon_Phase_" + phaseIdx);
        MoonIllumText.Text = Loc.Format("SunMoon_IllumLine",
            (int)Math.Round(illum * 100),
            age.ToString("0.0", Loc.Culture));

        // --- Moon extra: rise/set + next new/full ---
        if (_state.ShowMoonExtra)
        {
            DateTime cityMidnightLocal = DateTime.SpecifyKind(cityNow.Date, DateTimeKind.Unspecified);
            DateTime utcMidnight = TimeZoneInfo.ConvertTimeToUtc(cityMidnightLocal, tz);
            var (mRise, mSet) = Astronomy.MoonRiseSet(utcMidnight, lat, lon);
            MoonRiseSetText.Text = Loc.Format("SunMoon_MoonRiseSet", FmtTime(mRise, tz), FmtTime(mSet, tz));

            var nextNew = Astronomy.NextMoonPhase(utcNow, 0);
            var nextFull = Astronomy.NextMoonPhase(utcNow, 180);
            MoonNextText.Text = Loc.Format("SunMoon_MoonNext", FmtDate(nextNew, tz), FmtDate(nextFull, tz));
        }

        UpdatedText.Text = cityNow.ToString("HH:mm");
    }

    // Non-breaking spaces keep "HH:mm - HH:mm" from wrapping in the middle of a range.
    private string Range(DateTime? a, DateTime? b, TimeZoneInfo tz)
        => FmtTime(a, tz) + " - " + FmtTime(b, tz);

    // ===== Moon disc =====

    private void RenderMoon(double illum, bool waxing)
    {
        const double r = 16, cx = 17, cy = 17;
        if (illum >= 0.985)
            MoonLitPath.Data = new EllipseGeometry(new Point(cx, cy), r, r);
        else if (illum <= 0.015)
            MoonLitPath.Data = null;
        else
            MoonLitPath.Data = BuildMoonGeometry(illum, waxing, cx, cy, r);
    }

    // Path tracing the lit portion of the Moon disc: a semicircular outer limb on
    // the lit side plus an elliptical terminator whose minor axis shrinks toward the
    // quarter phases. waxing -> lit on the right; waning -> left.
    private static Geometry BuildMoonGeometry(double f, bool waxing, double cx, double cy, double r)
    {
        var top = new Point(cx, cy - r);
        var bottom = new Point(cx, cy + r);
        double rx = r * Math.Abs(1 - 2 * f);
        bool gibbous = f > 0.5;

        var fig = new PathFigure { StartPoint = top, IsClosed = true };

        var limbSweep = waxing ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        fig.Segments.Add(new ArcSegment(bottom, new Size(r, r), 0, false, limbSweep, true));

        SweepDirection termSweep = waxing
            ? (gibbous ? SweepDirection.Clockwise : SweepDirection.Counterclockwise)
            : (gibbous ? SweepDirection.Counterclockwise : SweepDirection.Clockwise);
        fig.Segments.Add(new ArcSegment(top, new Size(rx, r), 0, false, termSweep, true));

        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    // ===== Helpers =====

    private static TimeZoneInfo ResolveTz(string id)
    {
        if (!string.IsNullOrEmpty(id))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { }
        }
        return TimeZoneInfo.Local;
    }

    private static string FmtTime(DateTime? utc, TimeZoneInfo tz)
        => utc.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(utc.Value, tz).ToString("HH:mm")
            : "-";

    private static string FmtDate(DateTime utc, TimeZoneInfo tz)
        => TimeZoneInfo.ConvertTimeFromUtc(utc, tz).ToString("dd.MM");

    private static string FmtDuration(TimeSpan t)
        => (int)t.TotalHours + ":" + t.Minutes.ToString("00");

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
    }

    // ===== Open-Meteo geocoding (the only network call) =====

    private static async Task<(double lat, double lon, string name, string tz)?> GeocodeAsync(string city)
    {
        string lang = Loc.Culture.TwoLetterISOLanguageName == "uk" ? "uk" : "en";
        string url = "https://geocoding-api.open-meteo.com/v1/search?name="
                     + Uri.EscapeDataString(city)
                     + "&count=1&language=" + lang + "&format=json";

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
            return null;

        var r = results[0];
        double lat = r.GetProperty("latitude").GetDouble();
        double lon = r.GetProperty("longitude").GetDouble();
        string name = r.TryGetProperty("name", out var n) && n.GetString() is { } s ? s : city;
        string tz = r.TryGetProperty("timezone", out var tzEl) && tzEl.GetString() is { } t ? t : "";
        return (lat, lon, name, tz);
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
