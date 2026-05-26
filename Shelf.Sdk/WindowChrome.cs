using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Shelf.Sdk;

public static class WindowChrome
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    // Win11 (build 22000+): rounds the window's outer corners. Has no effect on older
    // Windows or on windows with AllowsTransparency=True.
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public static ImageSource? DefaultIcon { get; set; }

    /// <summary>
    /// One-call setup for every Window in the app: assigns the default icon, swaps in
    /// the custom <c>ChromeWindow</c> style (custom caption bar in the app's theme),
    /// rounds the window corners on Win11, and sets the DWM dark/light flag (kept for
    /// safety even though our custom caption hides the system-drawn title bar).
    /// </summary>
    /// <remarks>
    /// Windows that explicitly use <c>WindowStyle="None"</c> in XAML (e.g. <c>MainWindow</c>,
    /// the AppBar panel) are <b>not</b> re-styled - the assumption is they already have
    /// their own chrome and we must not overwrite it.
    /// </remarks>
    public static void Apply(Window window)
    {
        if (DefaultIcon != null) window.Icon ??= DefaultIcon;
        ApplyChromeStyle(window);
        ApplyTitleBarTheme(window);
    }

    private static void ApplyChromeStyle(Window window)
    {
        // Respect windows that already opted out of system chrome - they have their own.
        if (window.WindowStyle == WindowStyle.None) return;

        var app = Application.Current;
        if (app == null) return;

        if (app.TryFindResource("ChromeWindow") is Style style)
            window.Style = style;

        // The close button in the chrome template fires SystemCommands.CloseWindowCommand.
        // shell:WindowChrome auto-registers handlers for caption commands only when
        // UseAeroCaptionButtons=True; we use False (drawing our own X), so the binding
        // must be registered manually per-window.
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand,
            (_, args) => { window.Close(); args.Handled = true; }));
    }

    /// <summary>
    /// Sets the DWM "immersive dark mode" flag and the Win11 corner preference on the
    /// window's HWND according to the current <see cref="Theme.Current"/>. Safe to call
    /// before the HWND exists - will defer to <see cref="Window.SourceInitialized"/>
    /// automatically.
    /// </summary>
    public static void ApplyTitleBarTheme(Window window)
    {
        void Set()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int useDark = Theme.Current == AppTheme.Dark ? 1 : 0;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            if (hr != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
            }

            // Round the outer corners on Win11. On older Windows DwmSetWindowAttribute
            // returns a non-zero hresult for an unknown attribute - safe to ignore.
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set();
        else
            window.SourceInitialized += (_, _) => Set();
    }

    /// <summary>Backwards-compatible alias.</summary>
    public static void ApplyDarkTitleBar(Window window) => ApplyTitleBarTheme(window);

    /// <summary>
    /// Re-applies the title-bar theme to every currently open <see cref="Window"/>.
    /// Called from the theme-change subscription in App.xaml.cs.
    /// </summary>
    public static void ReapplyTitleBarThemeToAll()
    {
        var app = Application.Current;
        if (app == null) return;

        foreach (Window w in app.Windows)
        {
            try { ApplyTitleBarTheme(w); }
            catch { /* one failed window must not block the rest */ }
        }
    }
}
