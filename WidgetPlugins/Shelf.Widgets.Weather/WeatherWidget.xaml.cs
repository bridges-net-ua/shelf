using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Weather;

public partial class WeatherWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Weather_Name");

    // ===== Model =====

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public string City { get; set; } = "Київ";
        // Cached after a successful geocode so each refresh skips the geocoding call.
        // Cleared (null) whenever the city changes.
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string ResolvedName { get; set; } = "";

        // Last successful forecast snapshot, persisted so the widget can show stale
        // data when the API is down or before the first fetch completes after a restart.
        // Ignored on display when older than <see cref="CacheMaxAge"/>; cleared when
        // the user changes the city (a snapshot for Kyiv is meaningless after switching
        // to Lviv).
        public CachedSnapshot? Cached { get; set; }
    }

    // Persisted weather snapshot - one-to-one mirror of WeatherData plus the time
    // it was captured.
    public class CachedSnapshot
    {
        public DateTime CachedAt { get; set; }
        public double CurrentTemp { get; set; }
        public double FeelsLike { get; set; }
        public int CurrentCode { get; set; }
        public double TodayMax { get; set; }
        public double TodayMin { get; set; }
        public int TomorrowCode { get; set; }
        public double TomorrowMax { get; set; }
        public double TomorrowMin { get; set; }
    }

    // Parsed forecast payload (today = index 0, tomorrow = index 1 of the daily arrays).
    private sealed class WeatherData
    {
        public double CurrentTemp;
        public double FeelsLike;
        public int CurrentCode;
        public double TodayMax, TodayMin;
        public int TomorrowCode;
        public double TomorrowMax, TomorrowMin;
    }

    // Max age after which the cached snapshot is considered too stale to show.
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);

    // ===== Fields =====

    private WidgetState _state = new();
    private bool _hasData;
    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _refreshTimer;
    // Bumped on every refresh so a slow request from a previous call can't
    // overwrite the UI after the user changed the city or refreshed again.
    private int _fetchGeneration;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Weather icon geometries (Material Design path data) - monochrome, theme-coloured.
    private const string IconSunny =
        "M3.55,18.54L4.96,19.95L6.76,18.16L5.34,16.74M11,22.45C11.32,22.45 13,22.45 13," +
        "22.45V19.5H11M12,5.5A6.5,6.5 0 0,0 5.5,12A6.5,6.5 0 0,0 12,18.5A6.5,6.5 0 0,0 18.5," +
        "12A6.5,6.5 0 0,0 12,5.5M20,13H23V11H20M17.24,18.16L19.04,19.95L20.45,18.54L18.66," +
        "16.74M20.45,5.46L19.04,4.05L17.24,5.84L18.66,7.26M13,1.55H11V4.5H13M6.76,5.84L4.96," +
        "4.05L3.55,5.46L5.34,7.26L6.76,5.84M1,13H4V11H1V13Z";
    // Sun behind a cloud. Only the three rays that radiate cleanly into the open
    // sky are kept - the original Material icon also had three rays clashing with
    // the cloud body, which read as visual clutter at small monochrome sizes.
    private const string IconPartlyCloudy =
        "M12.74,5.47C15.1,6.5 16.35,9.03 15.92,11.46C17.19,12.56 18,14.19 18,16V16.17C18.31," +
        "16.06 18.65,16 19,16A3,3 0 0,1 22,19A3,3 0 0,1 19,22H6A4,4 0 0,1 2,18A4,4 0 0,1 6," +
        "14H6.27C5,12.45 4.6,10.24 5.5,8.26C6.72,5.5 9.97,4.24 12.74,5.47M11.93,7.3C10.16," +
        "6.5 8.09,7.31 7.31,9.07C6.85,10.09 6.93,11.22 7.41,12.13C8.5,10.83 10.16,10 12," +
        "10C12.7,10 13.38,10.12 14,10.34C13.94,9 13.18,7.85 11.93,7.3M13.55,3.64C13,3.4 " +
        "12.45,3.23 11.88,3.12L14.37,1.82L15.27,4.71C14.76,4.29 14.19,3.93 13.55,3.64M6.09," +
        "4.44C5.6,4.79 5.17,5.19 4.8,5.63L4.91,2.82L7.87,3.5C7.25,3.71 6.65,4.03 6.09," +
        "4.44M3.04,11.3C3.11,11.9 3.24,12.47 3.43,13L1.06,11.5L3.1,9.28C2.97,9.93 " +
        "2.95,10.61 3.04,11.3Z";
    private const string IconCloudy =
        "M6,19A5,5 0 0,1 1,14A5,5 0 0,1 6,9C7,6.65 9.3,5 12,5C15.43,5 18.24,7.66 18.5," +
        "11.03L19,11A4,4 0 0,1 23,15A4,4 0 0,1 19,19H6M19,13H17V12A5,5 0 0,0 12,7C9.5,7 " +
        "7.45,8.82 7.06,11.19C6.73,11.07 6.37,11 6,11A3,3 0 0,0 3,14A3,3 0 0,0 6,17H19A2," +
        "2 0 0,0 21,15A2,2 0 0,0 19,13Z";
    private const string IconFog =
        "M3,15H13A1,1 0 0,1 14,16A1,1 0 0,1 13,17H3A1,1 0 0,1 2,16A1,1 0 0,1 3,15M16,15H21A1," +
        "1 0 0,1 22,16A1,1 0 0,1 21,17H16A1,1 0 0,1 15,16A1,1 0 0,1 16,15M1,12A5,5 0 0,1 6," +
        "7C7,4.65 9.3,3 12,3C15.43,3 18.24,5.66 18.5,9.03L19,9C21.19,9 22.97,10.76 23,13H21A2," +
        "2 0 0,0 19,11H17V10A5,5 0 0,0 12,5C9.5,5 7.45,6.82 7.06,9.19C6.73,9.07 6.37,9 6,9A3," +
        "3 0 0,0 3,12C3,12.35 3.06,12.69 3.17,13H1.1L1,12M3,19H5A1,1 0 0,1 6,20A1,1 0 0,1 5," +
        "21H3A1,1 0 0,1 2,20A1,1 0 0,1 3,19M8,19H21A1,1 0 0,1 22,20A1,1 0 0,1 21,21H8A1,1 0 " +
        "0,1 7,20A1,1 0 0,1 8,19Z";
    private const string IconRainy =
        "M9,12C9.53,12.14 9.85,12.69 9.71,13.22L8.41,18.05C8.27,18.59 7.72,18.9 7.19," +
        "18.76C6.66,18.62 6.34,18.07 6.5,17.54L7.78,12.71C7.92,12.18 8.47,11.86 9,12M13," +
        "12C13.53,12.14 13.85,12.69 13.71,13.22L11.64,20.95C11.5,21.5 10.95,21.8 10.42," +
        "21.66C9.89,21.5 9.57,20.97 9.71,20.43L11.78,12.71C11.92,12.18 12.47,11.86 13,12M17," +
        "12C17.53,12.14 17.85,12.69 17.71,13.22L16.41,18.05C16.27,18.59 15.72,18.9 15.19," +
        "18.76C14.66,18.62 14.34,18.07 14.5,17.54L15.78,12.71C15.92,12.18 16.47,11.86 17," +
        "12M17,10V9A5,5 0 0,0 12,4C9.5,4 7.45,5.82 7.06,8.19C6.73,8.07 6.37,8 6,8A3,3 0 0,0 " +
        "3,11C3,12.11 3.6,13.08 4.5,13.6V13.59C4.97,13.87 5.14,14.47 4.87,14.94C4.59,15.41 " +
        "4,15.58 3.5,15.31V15.32C2,14.47 1,12.85 1,11A5,5 0 0,1 6,6C7,3.65 9.3,2 12,2C15.43," +
        "2 18.24,4.66 18.5,8.03L19,8A4,4 0 0,1 23,12C23,13.5 22.17,14.84 20.94,15.53C20.47," +
        "15.81 19.87,15.64 19.59,15.17C19.32,14.7 19.5,14.1 19.95,13.82C20.58,13.46 21,12.78 " +
        "21,12A2,2 0 0,0 19,10H17Z";
    private const string IconSnowy =
        "M6,14A1,1 0 0,1 7,15A1,1 0 0,1 6,16A5,5 0 0,1 1,11A5,5 0 0,1 6,6C7,3.65 9.3,2 12," +
        "2C15.43,2 18.24,4.66 18.5,8.03L19,8A4,4 0 0,1 23,12A4,4 0 0,1 19,16H18A1,1 0 0,1 17," +
        "15A1,1 0 0,1 18,14H19A2,2 0 0,0 21,12A2,2 0 0,0 19,10H17V9A5,5 0 0,0 12,4C9.5,4 " +
        "7.45,5.82 7.06,8.19C6.73,8.07 6.37,8 6,8A3,3 0 0,0 3,11A3,3 0 0,0 6,14M7.88," +
        "18.07L10.07,17.5L8.46,15.89C8.07,15.5 8.07,14.86 8.46,14.47C8.85,14.08 9.5,14.08 " +
        "9.88,14.47L11.5,16.09L12.07,13.89C12.21,13.36 12.76,13.04 13.29,13.18C13.83,13.32 " +
        "14.14,13.87 14,14.41L13.41,16.6L15.61,16C16.14,15.89 16.69,16.2 16.83,16.74C16.97," +
        "17.27 16.66,17.82 16.12,17.96L13.93,18.56L15.54,20.17C15.93,20.56 15.93,21.2 15.54," +
        "21.59C15.15,22 14.5,22 14.12,21.59L12.5,20L11.93,22.17C11.79,22.7 11.24,23 10.71," +
        "22.88C10.17,22.74 9.86,22.19 10,21.65L10.59,19.46L8.4,20.05C7.86,20.2 7.31,19.89 " +
        "7.17,19.35C7.03,18.81 7.34,18.27 7.88,18.12Z";
    private const string IconThunder =
        "M6,16A5,5 0 0,1 1,11A5,5 0 0,1 6,6C7,3.65 9.3,2 12,2C15.43,2 18.24,4.66 18.5," +
        "8.03L19,8A4,4 0 0,1 23,12A4,4 0 0,1 19,16H18A1,1 0 0,1 17,15A1,1 0 0,1 18,14H19A2," +
        "2 0 0,0 21,12A2,2 0 0,0 19,10H17V9A5,5 0 0,0 12,4C9.5,4 7.45,5.82 7.06,8.19C6.73," +
        "8.07 6.37,8 6,8A3,3 0 0,0 3,11A3,3 0 0,0 6,14H8.11L7,18H9.59L8,23L13,16H10.41L11.5," +
        "12H6Z";

    // ===== IWidget =====

    public string Id => "weather";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Weather_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public WeatherWidget()
    {
        InitializeComponent();
        UpdatedText.ToolTip = Loc.Get("Weather_LastUpdate");

        Loaded += (_, _) => ApplyState();
        Unloaded += (_, _) => _refreshTimer?.Stop();
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new WeatherSettingsDialog(_state.City)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            string newCity = (dlg.ResultCity ?? "").Trim();
            string oldCity = (_state.City ?? "").Trim();
            if (!string.Equals(newCity, oldCity, StringComparison.OrdinalIgnoreCase))
            {
                _state.City = newCity;
                // Force a re-geocode for the new city on the next refresh.
                _state.Latitude = null;
                _state.Longitude = null;
                _state.ResolvedName = "";
                // Drop the cached snapshot - it belongs to the previous city.
                _state.Cached = null;
                _hasData = false;
                WidgetServices.RequestSaveStates();
                RefreshAsync();
            }
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

    // ===== State / refresh =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;

        if (_refreshTimer == null)
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _refreshTimer.Tick += (_, _) => RefreshAsync();
        }
        _refreshTimer.Stop();
        _refreshTimer.Start();

        // Render the cached snapshot immediately so the user sees data even before
        // the foreground fetch finishes (or if it ends up failing). The fetch then
        // overwrites the cache on success - or silently leaves it alone on failure.
        if (TryRenderCache())
        {
            // Cache is showing; the in-flight fetch should not display the "Loading…"
            // placeholder over it - RefreshAsync respects _hasData for that.
        }

        RefreshAsync();
    }

    // Render the cached snapshot if it exists and isn't stale. Returns true if
    // anything was rendered.
    private bool TryRenderCache()
    {
        var c = _state.Cached;
        if (c == null) return false;
        if (DateTime.Now - c.CachedAt > CacheMaxAge) return false;

        RenderWeather(new WeatherData
        {
            CurrentTemp = c.CurrentTemp,
            FeelsLike = c.FeelsLike,
            CurrentCode = c.CurrentCode,
            TodayMax = c.TodayMax,
            TodayMin = c.TodayMin,
            TomorrowCode = c.TomorrowCode,
            TomorrowMax = c.TomorrowMax,
            TomorrowMin = c.TomorrowMin
        }, snapshotTimestamp: c.CachedAt);
        return true;
    }

    private async void RefreshAsync()
    {
        string city = (_state.City ?? "").Trim();
        if (city.Length == 0)
        {
            SetError(Loc.Get("Weather_Err_NoCity"));
            return;
        }

        int gen = ++_fetchGeneration;
        RefreshButton.IsEnabled = false;
        SetError(null);
        if (!_hasData) CityText.Text = Loc.Get("Weather_Loading");

        try
        {
            double lat, lon;
            if (_state.Latitude.HasValue && _state.Longitude.HasValue)
            {
                lat = _state.Latitude.Value;
                lon = _state.Longitude.Value;
            }
            else
            {
                var geo = await GeocodeAsync(city);
                if (gen != _fetchGeneration) return;
                if (geo == null)
                {
                    SetError(Loc.Get("Weather_Err_City"));
                    return;
                }
                lat = geo.Value.lat;
                lon = geo.Value.lon;
                _state.Latitude = lat;
                _state.Longitude = lon;
                _state.ResolvedName = geo.Value.name;
                ScheduleSave();
            }

            var data = await FetchForecastAsync(lat, lon);
            if (gen != _fetchGeneration) return;
            if (data == null)
            {
                // Forecast endpoint returned a malformed payload. Keep cached UI as
                // is and surface the network error only when we have nothing else.
                if (!_hasData) SetError(Loc.Get("Weather_Err_Network"));
                return;
            }

            RenderWeather(data);
        }
        catch
        {
            // Timeout / 5xx / DNS / etc. - silently keep the cached snapshot on screen
            // if we already showed something, otherwise display the error.
            if (gen == _fetchGeneration && !_hasData)
                SetError(Loc.Get("Weather_Err_Network"));
        }
        finally
        {
            if (gen == _fetchGeneration) RefreshButton.IsEnabled = true;
        }
    }

    // snapshotTimestamp == null  -> fresh fetch: stamp UpdatedText with "now" and
    //                                persist the snapshot to the cache.
    // snapshotTimestamp == DateTime -> rendering an existing cached snapshot: keep
    //                                the original capture time, don't re-save.
    private void RenderWeather(WeatherData d, DateTime? snapshotTimestamp = null)
    {
        _hasData = true;
        CityText.Text = string.IsNullOrWhiteSpace(_state.ResolvedName)
            ? (_state.City ?? "").Trim()
            : _state.ResolvedName;

        var today = Describe(d.CurrentCode);
        SetIcon(TodayIconPath, today.icon);
        TodayTempText.Text = FormatTemp(d.CurrentTemp);
        TodayDescText.Text = Loc.Get(today.descKey);
        TodayMetaText.Text = $"{FormatTemp(d.TodayMax)}/{FormatTemp(d.TodayMin)} · "
                             + Loc.Format("Weather_FeelsShort", FormatTemp(d.FeelsLike));

        var tomorrow = Describe(d.TomorrowCode);
        SetIcon(TomorrowIconPath, tomorrow.icon);
        TomorrowDescRun.Text = " · " + Loc.Get(tomorrow.descKey);
        TomorrowHiLoText.Text = $"{FormatTemp(d.TomorrowMax)} / {FormatTemp(d.TomorrowMin)}";

        DateTime ts;
        if (snapshotTimestamp.HasValue)
        {
            ts = snapshotTimestamp.Value;
        }
        else
        {
            ts = DateTime.Now;
            _state.Cached = new CachedSnapshot
            {
                CachedAt = ts,
                CurrentTemp = d.CurrentTemp,
                FeelsLike = d.FeelsLike,
                CurrentCode = d.CurrentCode,
                TodayMax = d.TodayMax,
                TodayMin = d.TodayMin,
                TomorrowCode = d.TomorrowCode,
                TomorrowMax = d.TomorrowMax,
                TomorrowMin = d.TomorrowMin
            };
            ScheduleSave();
        }
        UpdatedText.Text = ts.ToString("HH:mm");
        SetError(null);
    }

    // Error line is shown only on a failure; otherwise hidden.
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
            // No data yet - drop the "Loading..." placeholder back to the city name.
            if (!_hasData) CityText.Text = (_state.City ?? "").Trim();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAsync();

    // ===== Open-Meteo API =====

    private static async Task<(double lat, double lon, string name)?> GeocodeAsync(string city)
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
        double lat = ReadDouble(r.GetProperty("latitude"));
        double lon = ReadDouble(r.GetProperty("longitude"));
        string name = r.TryGetProperty("name", out var n) && n.GetString() is { } s ? s : city;
        return (lat, lon, name);
    }

    private static async Task<WeatherData?> FetchForecastAsync(double lat, double lon)
    {
        var ci = CultureInfo.InvariantCulture;
        string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(ci)
                     + "&longitude=" + lon.ToString(ci)
                     + "&current=temperature_2m,apparent_temperature,weather_code"
                     + "&daily=weather_code,temperature_2m_max,temperature_2m_min"
                     + "&timezone=auto&forecast_days=2";

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("current", out var cur)
            || !root.TryGetProperty("daily", out var daily))
            return null;

        var wcode = daily.GetProperty("weather_code");
        var wmax = daily.GetProperty("temperature_2m_max");
        var wmin = daily.GetProperty("temperature_2m_min");
        if (wcode.GetArrayLength() < 2 || wmax.GetArrayLength() < 2 || wmin.GetArrayLength() < 2)
            return null;

        return new WeatherData
        {
            CurrentTemp = ReadDouble(cur.GetProperty("temperature_2m")),
            FeelsLike = ReadDouble(cur.GetProperty("apparent_temperature")),
            CurrentCode = ReadInt(cur.GetProperty("weather_code")),
            TodayMax = ReadDouble(wmax[0]),
            TodayMin = ReadDouble(wmin[0]),
            TomorrowCode = ReadInt(wcode[1]),
            TomorrowMax = ReadDouble(wmax[1]),
            TomorrowMin = ReadDouble(wmin[1]),
        };
    }

    private static double ReadDouble(JsonElement el)
        => el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0.0;

    private static int ReadInt(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number) return 0;
        return el.TryGetInt32(out var i) ? i : (int)Math.Round(el.GetDouble());
    }

    // ===== Weather code -> icon + description =====

    // Maps a WMO weather code to a localized description key and a monochrome icon.
    private static (string descKey, string icon) Describe(int code) => code switch
    {
        0 => ("Weather_Cond_Clear", IconSunny),
        1 => ("Weather_Cond_MainlyClear", IconSunny),
        2 => ("Weather_Cond_PartlyCloudy", IconPartlyCloudy),
        3 => ("Weather_Cond_Cloudy", IconCloudy),
        45 or 48 => ("Weather_Cond_Fog", IconFog),
        51 or 53 or 55 => ("Weather_Cond_Drizzle", IconRainy),
        56 or 57 => ("Weather_Cond_FreezingRain", IconSnowy),
        61 or 63 or 65 => ("Weather_Cond_Rain", IconRainy),
        66 or 67 => ("Weather_Cond_FreezingRain", IconSnowy),
        71 or 73 or 75 or 77 => ("Weather_Cond_Snow", IconSnowy),
        80 or 81 or 82 => ("Weather_Cond_Showers", IconRainy),
        85 or 86 => ("Weather_Cond_Snow", IconSnowy),
        95 or 96 or 99 => ("Weather_Cond_Thunderstorm", IconThunder),
        _ => ("Weather_Cond_Cloudy", IconCloudy),
    };

    private static void SetIcon(Path path, string geometry)
    {
        try { path.Data = Geometry.Parse(geometry); }
        catch { path.Data = null; }
    }

    private static string FormatTemp(double t)
        => Math.Round(t).ToString("0", CultureInfo.InvariantCulture) + "°";

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
