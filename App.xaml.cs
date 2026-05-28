using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shelf.Sdk;
using Shelf.Services;

namespace Shelf;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    public static SettingsService Settings { get; private set; } = null!;
    public static TrayIconService Tray { get; private set; } = null!;
    public static WidgetManager Widgets { get; private set; } = null!;
    public static MonitorService Monitors { get; private set; } = null!;

    // One MainWindow per connected monitor (keyed by Screen.DeviceName).
    // Primary is always present even if it currently has zero widgets — it's the
    // landing pad for homeless widgets and the owner of the tray/settings flow.
    // Secondary monitors only get a bar when at least one widget is assigned to
    // them; otherwise no panel is shown and the user keeps the full screen edge.
    private static readonly Dictionary<string, MainWindow> _bars =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, MainWindow> Bars => _bars;

    // Primary bar — convenience accessor used by Tray and Settings flows. Returns
    // null only briefly during topology changes when the primary is being swapped.
    public static MainWindow? PrimaryBar
    {
        get
        {
            var primaryName = Monitors?.Primary.DeviceName;
            if (string.IsNullOrEmpty(primaryName)) return null;
            return _bars.TryGetValue(primaryName, out var bar) ? bar : _bars.Values.FirstOrDefault();
        }
    }

    public static IReadOnlySet<string> GetConnectedDeviceNames() =>
        Monitors.Monitors
            .Select(m => m.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "Shelf.crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogException("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogException("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        try
        {
            base.OnStartup(e);

            _singleInstanceMutex = new Mutex(true, "Shelf_SingleInstance_E94F12C7", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            // Wire SDK services
            Sdk.WindowChrome.DefaultIcon = LoadAppIcon();
            WidgetServices.Host = new HostAdapter();

            Settings = new SettingsService();
#if STORE_BUILD
            // First-launch migration of portable settings into the packaged location.
            // No-op on subsequent launches (marker file blocks repeated copies).
            Settings.MigratePortableToPackaged();
#endif
            Settings.Load();

            // Load the colour palette BEFORE language and any window, so DynamicResource
            // brush lookups inside Theme.xaml have something to resolve against from the
            // very first frame.
            Theme.Initialize(Settings.Current.Theme);

            // Whenever the theme is swapped at runtime via Theme.Apply(...), the WPF
            // DynamicResource consumers re-resolve automatically; the DWM title-bar flag
            // however is a one-shot HWND attribute and must be re-applied for every open
            // window. TrayIconService self-subscribes for its WinForms menu.
            Theme.ThemeChanged += Sdk.WindowChrome.ReapplyTitleBarThemeToAll;

            // Load the UI language before any window or widget is created, so that
            // {DynamicResource ...} string lookups and Loc.Get resolve correctly.
            Loc.Initialize(Settings.Current.Language);

            // One-time registry migration: remove legacy autostart entries ("Polychka",
            // "Помічник") from older installs and promote them to the new "Shelf" value.
            AutoStartService.MigrateLegacyValue();

            WidgetRegistry.Initialize();
            Widgets = new WidgetManager();
            Widgets.Sync();
            Widgets.LoadStates();

            // Monitor topology — must come before any MainWindow is created, since the
            // window constructor takes its target MonitorInfo.
            Monitors = new MonitorService();
            Settings.MigrateLegacyPanel(Monitors.Primary.DeviceName);
            Monitors.TopologyChanged += OnTopologyChanged;

            // Initial fan-out: one bar per monitor that has work to do.
            EnsureBarsForCurrentTopology();

            // Tray must be created AFTER at least the primary bar exists so its
            // Show/Hide command has a window to target. Tray follows the primary
            // bar via App.PrimaryBar — when primary changes (monitor swap), Tray
            // automatically points at the new primary's window.
            Tray = new TrayIconService();
            Tray.Show();

            // Subscribe to widget list changes so EVERY bar rebuilds simultaneously.
            // Without this, moving a widget from monitor 1 to monitor 2 would update
            // monitor 2's panel but leave the old widget visually on monitor 1 until
            // a settings save or window reactivation triggered a refresh.
            Widgets.ActiveWidgetsChanged += RebuildAllBars;
            Settings.Changed += ApplyAllBars;
        }
        catch (Exception ex)
        {
            LogException("OnStartup", ex);
            Shutdown();
        }
    }

    private static void EnsureBarsForCurrentTopology()
    {
        var monitors = Monitors.Monitors;
        var connected = GetConnectedDeviceNames();

        // Remove bars whose monitor disappeared.
        var stale = _bars.Keys.Where(k => !connected.Contains(k)).ToList();
        foreach (var deviceName in stale)
        {
            if (_bars.Remove(deviceName, out var bar))
            {
                try { bar.Close(); } catch { }
            }
        }

        foreach (var mon in monitors)
        {
            bool needsBar = mon.IsPrimary || HasAnyWidgetFor(mon.DeviceName);
            if (!needsBar)
            {
                // Monitor has no assigned widgets — don't show an empty panel. If
                // the user wants Shelf there, they can drop a widget on it via
                // another bar's "Move to monitor ▶" submenu.
                if (_bars.Remove(mon.DeviceName, out var existing))
                {
                    try { existing.Close(); } catch { }
                }
                continue;
            }

            if (!_bars.ContainsKey(mon.DeviceName))
            {
                var bar = new MainWindow(mon);
                _bars[mon.DeviceName] = bar;
                bar.Show();
            }
        }
    }

    private static bool HasAnyWidgetFor(string deviceName) =>
        Settings.Current.Widgets.Any(w => w.Enabled
            && string.Equals(w.MonitorDeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

    private static void OnTopologyChanged()
    {
        // Dispatch back to the UI thread — DisplaySettingsChanged can fire on any
        // thread, and bar lifecycle has to happen on the dispatcher.
        Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            EnsureBarsForCurrentTopology();
            RebuildAllBars();
            ApplyAllBars();
        }));
    }

    private static void RebuildAllBars()
    {
        foreach (var bar in _bars.Values.ToList())
        {
            try { bar.RebuildPanelPublic(); } catch { }
        }
        // Once widgets have moved/disappeared, a previously occupied bar may now
        // be empty — if so, hide it. And a primary bar might have just become a
        // landing pad for a newly assigned widget — wake it up.
        EnsureBarsForCurrentTopology();
    }

    private static void ApplyAllBars()
    {
        foreach (var bar in _bars.Values.ToList())
        {
            try { bar.ApplySettings(); } catch { }
        }
    }

    private static BitmapImage? LoadAppIcon()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Resources/shelf.ico", UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private sealed class HostAdapter : IWidgetHost
    {
        public void RequestSaveStates() => Widgets?.SaveStates();
    }

    private static void LogException(string source, Exception? ex)
    {
        try
        {
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n";
            File.AppendAllText(LogPath, msg);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Widgets?.SaveStates(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { Monitors?.Dispose(); } catch { }
        try { Settings?.Save(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
