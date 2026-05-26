using System;
using System.IO;
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
    public static MainWindow Bar { get; private set; } = null!;
    public static WidgetManager Widgets { get; private set; } = null!;

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
            WindowChrome.DefaultIcon = LoadAppIcon();
            WidgetServices.Host = new HostAdapter();

            Settings = new SettingsService();
            Settings.Load();

            // Load the colour palette BEFORE language and any window, so DynamicResource
            // brush lookups inside Theme.xaml have something to resolve against from the
            // very first frame.
            Theme.Initialize(Settings.Current.Theme);

            // Whenever the theme is swapped at runtime via Theme.Apply(...), the WPF
            // DynamicResource consumers re-resolve automatically; the DWM title-bar flag
            // however is a one-shot HWND attribute and must be re-applied for every open
            // window. TrayIconService self-subscribes for its WinForms menu.
            Theme.ThemeChanged += WindowChrome.ReapplyTitleBarThemeToAll;

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

            Bar = new MainWindow();
            Bar.Show();

            Tray = new TrayIconService(Bar);
            Tray.Show();
        }
        catch (Exception ex)
        {
            LogException("OnStartup", ex);
            Shutdown();
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
        try { Settings?.Save(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
