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
    private const int DWMWCP_DONOTROUND = 1;

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
        HookMaximizeToWorkArea(window);
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

        // Maximize / restore for resizable windows (the chrome's MaxButton fires these).
        // Like the close command they aren't auto-wired, because UseAeroCaptionButtons=False.
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.MaximizeWindowCommand,
            (_, args) => { SystemCommands.MaximizeWindow(window); args.Handled = true; }));
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.RestoreWindowCommand,
            (_, args) => { SystemCommands.RestoreWindow(window); args.Handled = true; }));
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
    /// Forces square (non-rounded) outer corners on the window. Windows 11 rounds every
    /// top-level window's corners by default; this opts out via DWMWCP_DONOTROUND. Used by
    /// the borderless dock panel (MainWindow) so it sits flush as a straight-edged bar.
    /// No-op on Windows 10 (corners are already square). Safe to call before the HWND
    /// exists - defers to <see cref="Window.SourceInitialized"/>.
    /// </summary>
    public static void ApplySquareCorners(Window window)
    {
        void Set()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int pref = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set();
        else
            window.SourceInitialized += (_, _) => Set();
    }

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

    // ===== Maximize-to-work-area =====
    // A borderless window (WindowStyle=None + shell:WindowChrome) maximizes to the FULL
    // monitor by default, covering the taskbar and our own Shelf AppBar strip. Handling
    // WM_GETMINMAXINFO clamps the maximized bounds to the monitor work area (rcWork),
    // which already excludes both the taskbar and the reserved AppBar strip - so a
    // maximized window (e.g. Settings) behaves like a normal window.

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private static void HookMaximizeToWorkArea(Window window)
    {
        void Hook()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            HwndSource.FromHwnd(hwnd)?.AddHook(
                (IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                    WndProc(window, h, msg, wp, lp, ref handled));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Hook();
        else
            window.SourceInitialized += (_, _) => Hook();
    }

    private static IntPtr WndProc(Window window, IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        // Clamp the MAXIMIZED bounds to the monitor work area (excludes taskbar + AppBar).
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                RECT work = mi.rcWork;
                RECT mon = mi.rcMonitor;
                // Maximized position/size are expressed relative to the monitor's top-left.
                mmi.ptMaxPosition.X = work.Left - mon.Left;
                mmi.ptMaxPosition.Y = work.Top - mon.Top;
                mmi.ptMaxSize.X = work.Right - work.Left;
                mmi.ptMaxSize.Y = work.Bottom - work.Top;
            }
        }

        // Re-apply the window's MinWidth/MinHeight as the minimum track size. Because we
        // mark the message handled, WPF no longer enforces the window minimum for us -
        // without this the window could be shrunk until the buttons, tabs and scrollbar
        // disappear. MinWidth/MinHeight are in DIPs, so scale to physical pixels.
        double scaleX = 1.0, scaleY = 1.0;
        var src = HwndSource.FromHwnd(hwnd);
        if (src?.CompositionTarget != null)
        {
            var m = src.CompositionTarget.TransformToDevice;
            if (m.M11 > 0) scaleX = m.M11;
            if (m.M22 > 0) scaleY = m.M22;
        }
        if (window.MinWidth > 0 && !double.IsInfinity(window.MinWidth))
            mmi.ptMinTrackSize.X = (int)Math.Ceiling(window.MinWidth * scaleX);
        if (window.MinHeight > 0 && !double.IsInfinity(window.MinHeight))
            mmi.ptMinTrackSize.Y = (int)Math.Ceiling(window.MinHeight * scaleY);

        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }
}
