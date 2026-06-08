using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Shelf.Widgets.Currency;

public partial class CurrencyWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Currency_Name");

    // Currencies offered in settings (PrivatBank exchange_rates supports these).
    public static readonly string[] PopularCurrencies =
        { "USD", "EUR", "GBP", "PLN", "CHF", "CZK", "CAD", "JPY", "AUD", "SEK", "NOK", "DKK" };

    // ===== Model =====

    public class CachedRate
    {
        public string Ccy { get; set; } = "";
        public double Buy { get; set; }
        public double Sale { get; set; }
        public double Change { get; set; }   // sale today - sale yesterday
        public bool HasChange { get; set; }
    }

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public List<string> Currencies { get; set; } = new() { "USD", "EUR" };
        public bool ShowChange { get; set; } = true;
        // Last rendered rates, persisted for instant display after restart.
        public List<CachedRate> Cached { get; set; } = new();
        public DateTime? CachedAt { get; set; }
    }

    private WidgetState _state = new();
    private bool _hasData;
    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _refreshTimer;
    private int _fetchGeneration;

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(48);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private const string ApiBase = "https://api.privatbank.ua/p24api/exchange_rates?json&date=";

    // ===== IWidget =====

    public string Id => "currency";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Currency_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public CurrencyWidget()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyState();
        Unloaded += (_, _) => _refreshTimer?.Stop();
        SizeChanged += Widget_SizeChanged;
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new CurrencySettingsDialog(_state.Currencies, _state.ShowChange)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state.Currencies = dlg.ResultCurrencies;
            _state.ShowChange = dlg.ResultShowChange;
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

    // ===== State / refresh =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;

        if (_refreshTimer == null)
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _refreshTimer.Tick += (_, _) => RefreshAsync();
        }
        _refreshTimer.Stop();
        _refreshTimer.Start();

        TryRenderCache();
        RefreshAsync();
    }

    // Paints the persisted snapshot before the network call returns (or if it fails).
    private bool TryRenderCache()
    {
        if (_state.Cached == null || _state.Cached.Count == 0) return false;
        if (_state.CachedAt.HasValue && DateTime.Now - _state.CachedAt.Value > CacheMaxAge) return false;

        BuildRows(_state.Cached);
        _hasData = true;
        if (_state.CachedAt.HasValue) UpdatedText.Text = _state.CachedAt.Value.ToString("HH:mm");
        return true;
    }

    private async void RefreshAsync()
    {
        if (_state.Currencies == null || _state.Currencies.Count == 0)
        {
            BuildRows(new List<CachedRate>());
            SetError(Loc.Get("Currency_Err_NoCurrency"));
            return;
        }

        int gen = ++_fetchGeneration;
        RefreshButton.IsEnabled = false;
        SetError(null);

        try
        {
            var today = await FetchRatesAsync(DateTime.Today);
            if (gen != _fetchGeneration) return;
            if (today == null || today.Count == 0)
            {
                if (!_hasData) SetError(Loc.Get("Currency_Err_Network"));
                return;
            }

            Dictionary<string, (double buy, double sale)>? yesterday = null;
            if (_state.ShowChange)
            {
                try { yesterday = await FetchRatesAsync(DateTime.Today.AddDays(-1)); }
                catch { yesterday = null; } // change is best-effort
                if (gen != _fetchGeneration) return;
            }

            RenderRates(today, yesterday);
        }
        catch
        {
            if (gen == _fetchGeneration && !_hasData) SetError(Loc.Get("Currency_Err_Network"));
        }
        finally
        {
            if (gen == _fetchGeneration) RefreshButton.IsEnabled = true;
        }
    }

    private void RenderRates(Dictionary<string, (double buy, double sale)> today,
                             Dictionary<string, (double buy, double sale)>? yesterday)
    {
        var rows = new List<CachedRate>();
        foreach (var ccy in _state.Currencies)
        {
            if (!today.TryGetValue(ccy, out var t)) continue;

            bool hasChange = false;
            double change = 0;
            if (_state.ShowChange && yesterday != null && yesterday.TryGetValue(ccy, out var y))
            {
                change = t.sale - y.sale;
                hasChange = true;
            }
            rows.Add(new CachedRate { Ccy = ccy, Buy = t.buy, Sale = t.sale, Change = change, HasChange = hasChange });
        }

        BuildRows(rows);
        _hasData = true;
        SetError(null);

        _state.Cached = rows;
        _state.CachedAt = DateTime.Now;
        ScheduleSave();
        UpdatedText.Text = DateTime.Now.ToString("HH:mm");
    }

    // ===== Row building (adaptive grid) =====

    // Below this width the grid collapses to a single column.
    private const double TwoColumnMinWidth = 210;

    private List<CachedRate> _lastRows = new();
    private int _currentColumns;

    private static int ColumnsForWidth(double width) => width >= TwoColumnMinWidth ? 2 : 1;

    private void BuildRows(List<CachedRate> rows)
    {
        _lastRows = rows;
        _currentColumns = ColumnsForWidth(ActualWidth);
        BuildGrid(rows, _currentColumns);
    }

    private void Widget_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        int cols = ColumnsForWidth(ActualWidth);
        if (cols != _currentColumns)
        {
            _currentColumns = cols;
            BuildGrid(_lastRows, cols);
        }
    }

    // Builds an N-column grid of currency cells filling left-to-right, top-to-bottom.
    // A lone trailing cell in 2-column mode spans both columns, centered.
    private void BuildGrid(List<CachedRate> rows, int columns)
    {
        RatesHost.Children.Clear();
        if (rows.Count == 0) return;

        var grid = new Grid();
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int dataRows = (int)Math.Ceiling(rows.Count / (double)columns);
        for (int r = 0; r < dataRows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < rows.Count; i++)
        {
            var cell = BuildCell(rows[i]);
            Grid.SetRow(cell, i / columns);
            Grid.SetColumn(cell, i % columns);
            bool loneLast = columns == 2 && i == rows.Count - 1 && rows.Count % 2 == 1;
            if (loneLast) Grid.SetColumnSpan(cell, 2);
            grid.Children.Add(cell);
        }

        RatesHost.Children.Add(grid);
    }

    // One currency cell: code + buy/sale, each rate captioned with куп./прод. above it.
    // Layout columns: code | buy | spacer | sale | change; rows: captions / values.
    private UIElement BuildCell(CachedRate r)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });        // code
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });        // buy
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });     // spacer
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });        // sale
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });        // change
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });             // captions
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });             // values

        AddCell(grid, Caption(Loc.Get("Currency_Buy")), 0, 1);
        AddCell(grid, Caption(Loc.Get("Currency_Sell")), 0, 3);

        var code = Value(r.Ccy, FontWeights.SemiBold, "PrimaryTextBrush");
        code.Margin = new Thickness(0, 0, 10, 0);
        AddCell(grid, code, 1, 0);
        AddCell(grid, Value(FmtRate(r.Buy), FontWeights.Normal, "PrimaryTextBrush"), 1, 1);
        AddCell(grid, Value(FmtRate(r.Sale), FontWeights.Normal, "PrimaryTextBrush"), 1, 3);

        var changeText = FormatChange(r);
        if (!string.IsNullOrEmpty(changeText))
        {
            var ch = Value(changeText, FontWeights.Normal, "MutedTextBrush");
            ch.FontSize = 12;
            ch.Margin = new Thickness(8, 0, 0, 0);
            AddCell(grid, ch, 1, 4);
        }

        return grid;
    }

    private static void AddCell(Grid grid, UIElement el, int row, int col)
    {
        Grid.SetRow(el, row);
        Grid.SetColumn(el, col);
        grid.Children.Add(el);
    }

    private static TextBlock Caption(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        return tb;
    }

    private static TextBlock Value(string text, FontWeight weight, string brushKey)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = weight,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return tb;
    }

    private static string FmtRate(double r) => r.ToString("0.00", Loc.Culture);

    private static string FormatChange(CachedRate r)
    {
        if (!r.HasChange || Math.Abs(r.Change) < 0.005) return "";
        string arrow = r.Change > 0 ? "⇑" : "⇓";
        return arrow + " " + Math.Abs(r.Change).ToString("0.00", Loc.Culture);
    }

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

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAsync();

    // ===== PrivatBank API =====

    // Returns ccy -> (buy, sale) for the given date, preferring the bank's cash
    // rate (purchaseRate/saleRate) and falling back to the NBU rate when the bank
    // doesn't quote that currency.
    private static async Task<Dictionary<string, (double buy, double sale)>?> FetchRatesAsync(DateTime date)
    {
        string url = ApiBase + date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("exchangeRate", out var arr)
            || arr.ValueKind != JsonValueKind.Array)
            return null;

        var dict = new Dictionary<string, (double, double)>();
        foreach (var el in arr.EnumerateArray())
        {
            if (!el.TryGetProperty("currency", out var c) || c.GetString() is not { } ccy)
                continue;

            double buy = ReadRate(el, "purchaseRate", "purchaseRateNB");
            double sale = ReadRate(el, "saleRate", "saleRateNB");
            if (buy > 0 && sale > 0) dict[ccy] = (buy, sale);
        }
        return dict;
    }

    private static double ReadRate(JsonElement el, string bankKey, string nbKey)
    {
        if (el.TryGetProperty(bankKey, out var b) && b.ValueKind == JsonValueKind.Number && b.GetDouble() > 0)
            return b.GetDouble();
        if (el.TryGetProperty(nbKey, out var n) && n.ValueKind == JsonValueKind.Number)
            return n.GetDouble();
        return 0;
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
