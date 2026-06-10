using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Shelf.Sdk;

namespace Shelf.Widgets.Html;

public partial class HtmlWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Html_Name");

    // Starter content for a freshly added instance: a styled greeting plus a live
    // JS clock, so the user immediately sees HTML/CSS/JS all working. Ukrainian
    // text in seeded state follows the Radio precedent (built-in station list).
    private const string DemoHtml = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
  body  { margin:0; font-family:'Segoe UI',sans-serif; color:#e8e8ec;
          background:transparent; text-align:center; padding:14px 8px; }
  .hi   { font-size:13px; color:#9a9aa2; }
  .clock{ font-size:30px; font-weight:600; margin-top:6px; letter-spacing:1px; }
  .hint { font-size:11px; color:#6e6e76; margin-top:10px; }
</style>
</head>
<body>
  <div class="hi">Це HTML-віджет</div>
  <div class="clock" id="c">--:--:--</div>
  <div class="hint">Змініть код у налаштуваннях віджета</div>
  <script>
    function tick() {
      document.getElementById('c').textContent =
        new Date().toLocaleTimeString('uk-UA');
    }
    tick(); setInterval(tick, 1000);
  </script>
</body>
</html>
""";

    // ===== Model =====

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public string Html { get; set; } = DemoHtml;
        public int Height { get; set; } = 200;
    }

    private WidgetState _state = new();
    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private bool _initStarted;

    // One browser environment for every HtmlWidget instance. The user-data folder
    // must NOT be next to the .exe (read-only under Program Files / MSIX), so it
    // goes to %LOCALAPPDATA%\Shelf\WebView2.
    private static Task<CoreWebView2Environment>? _envTask;

    private static Task<CoreWebView2Environment> GetEnvironmentAsync() =>
        _envTask ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Shelf", "WebView2"));

    // ===== IWidget =====

    public string Id => "html";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Html_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public HtmlWidget()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyState();
        // Browser process is killed only on REAL removal - a transient RebuildPanel
        // unload re-attaches the same instance (same deferred check as Radio).
        Unloaded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded)
            {
                try { Web.Dispose(); } catch { }
            }
        }), DispatcherPriority.Background);
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new HtmlSettingsDialog(_state.Html, _state.Height)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state.Html = dlg.ResultHtml;
            _state.Height = dlg.ResultHeight;
            Web.Height = _state.Height;
            RenderHtml();
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

    // ===== Lifecycle =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;
        Web.Height = _state.Height;
        InitWebViewAsync();
    }

    private async void InitWebViewAsync()
    {
        if (_initStarted) return;
        _initStarted = true;

        try
        {
            var env = await GetEnvironmentAsync();
            await Web.EnsureCoreWebView2Async(env);

            var core = Web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;

            // The pasted code IS the widget. Any http(s) navigation (link click,
            // location.href, window.open) goes to the default browser instead of
            // navigating the widget away from its content. NavigateToString uses
            // data:/about: URIs, which pass through untouched.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                OpenExternal(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                var uri = e.Uri ?? "";
                if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    OpenExternal(uri);
                }
            };

            // Transparent page background lets the panel theme show through until
            // the user's CSS sets its own.
            Web.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            RenderHtml();
        }
        catch
        {
            // Most likely the WebView2 Evergreen runtime is missing (old Win10).
            Web.Visibility = Visibility.Collapsed;
            ErrorText.Text = Loc.Get("Html_Err_Runtime");
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void RenderHtml()
    {
        try { Web.CoreWebView2?.NavigateToString(_state.Html ?? ""); }
        catch { }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => RenderHtml();

    private static void OpenExternal(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { }
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
