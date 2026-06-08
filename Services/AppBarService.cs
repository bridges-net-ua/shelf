using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Shelf.Models;

namespace Shelf.Services;

public class AppBarService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    private const uint ABM_NEW = 0x00;
    private const uint ABM_REMOVE = 0x01;
    private const uint ABM_QUERYPOS = 0x02;
    private const uint ABM_SETPOS = 0x03;

    private const uint ABE_LEFT = 0;
    private const uint ABE_RIGHT = 2;

    private const int ABN_POSCHANGED = 0x01;
    private const int ABN_FULLSCREENAPP = 0x02;

    private readonly Window _window;
    private IntPtr _hwnd;
    private uint _callbackMessage;
    private bool _registered;

    // The monitor + side + width last passed to Register/SetPosition. The Windows
    // ABN_POSCHANGED callback uses these to re-apply our preferred layout when
    // another AppBar's position causes ours to need re-fitting.
    private MonitorInfo? _lastMonitor;
    private BarSide _lastSide;
    private int _lastWidthDip;

    public AppBarService(Window window)
    {
        _window = window;
    }

    public void Register(MonitorInfo monitor, BarSide side, int widthDip)
    {
        if (_registered) Unregister();

        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) return;

        // Unique callback message per HWND would be ideal, but RegisterWindowMessage
        // returns the same value for the same string app-wide. Windows still routes
        // notifications back to the specific HWND that registered, so this is fine
        // even with multiple Shelf MainWindows registering their own AppBars.
        _callbackMessage = RegisterWindowMessage("Shelf_AppBarMessage_E94F");

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd,
            uCallbackMessage = _callbackMessage
        };
        SHAppBarMessage(ABM_NEW, ref data);

        var src = HwndSource.FromHwnd(_hwnd);
        src?.AddHook(WndProc);

        _registered = true;
        SetPosition(monitor, side, widthDip);
    }

    public void Unregister()
    {
        if (!_registered) return;

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd
        };
        SHAppBarMessage(ABM_REMOVE, ref data);

        var src = HwndSource.FromHwnd(_hwnd);
        src?.RemoveHook(WndProc);

        _registered = false;
    }

    public void SetPosition(MonitorInfo monitor, BarSide side, int widthDip)
    {
        if (!_registered || _hwnd == IntPtr.Zero) return;

        _lastMonitor = monitor;
        _lastSide = side;
        _lastWidthDip = widthDip;

        // The monitor's bounds are already in physical pixels. Widths from settings
        // are in DIPs (user-visible scale), so they get multiplied by the monitor's
        // DPI to land in the physical pixel grid the shell API expects.
        double dpiX = monitor.DpiX;
        double dpiY = monitor.DpiY;
        int widthPx = (int)Math.Round(widthDip * dpiX);

        // Pre-compute the desired rect. We don't trust ABM_QUERYPOS to come back
        // with the same coordinates — under some race conditions (notably right
        // after a virtual-desktop move + Hide/Show cycle) Windows returns a rect
        // anchored at the left edge regardless of the uEdge we requested, which
        // leaves the panel detached from the intended screen edge. So we issue
        // QUERYPOS for protocol compliance, then overwrite rc with our own values
        // before SETPOS.
        // Vertical extent is the work area (not the full monitor bounds) so the
        // panel stops at the taskbar's edge instead of overlapping it. Horizontal
        // edges still come from BoundsPx below - the panel hugs the screen edge.
        int desiredLeft, desiredTop = monitor.WorkAreaPx.Top, desiredRight;
        int desiredBottom = monitor.WorkAreaPx.Bottom;
        uint desiredEdge;
        if (side == BarSide.Left)
        {
            desiredEdge = ABE_LEFT;
            desiredLeft = monitor.BoundsPx.Left;
            desiredRight = monitor.BoundsPx.Left + widthPx;
        }
        else
        {
            desiredEdge = ABE_RIGHT;
            desiredLeft = monitor.BoundsPx.Right - widthPx;
            desiredRight = monitor.BoundsPx.Right;
        }

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd,
            uEdge = desiredEdge,
        };
        data.rc.left = desiredLeft;
        data.rc.top = desiredTop;
        data.rc.right = desiredRight;
        data.rc.bottom = desiredBottom;

        SHAppBarMessage(ABM_QUERYPOS, ref data);
        Log($"    [{monitor.DeviceName}] QUERYPOS returned: L={data.rc.left} T={data.rc.top} R={data.rc.right} B={data.rc.bottom} " +
            $"(desired L={desiredLeft} R={desiredRight}, side={side}, dpiX={dpiX:F3})");

        // Force our own rect — ignore whatever QUERYPOS proposed, keeping only the edge.
        data.uEdge = desiredEdge;
        data.rc.left = desiredLeft;
        data.rc.top = desiredTop;
        data.rc.right = desiredRight;
        data.rc.bottom = desiredBottom;

        SHAppBarMessage(ABM_SETPOS, ref data);
        Log($"    [{monitor.DeviceName}] SETPOS final:     L={data.rc.left} T={data.rc.top} R={data.rc.right} B={data.rc.bottom}");

        // Position the WPF window from OUR desired physical rect (full monitor
        // height), converted with the TARGET monitor's DPI. We deliberately ignore
        // the ABM_SETPOS reply here for two reasons learned from real screens:
        //   1. Windows may clip a side AppBar's rect to the work area (e.g. taskbar
        //      shown on this monitor / vertical / auto-hide), leaving a gap above
        //      the taskbar - using desiredBottom forces the panel to the very edge.
        //   2. The current HwndSource's DPI is wrong when the target monitor scales
        //      differently from where the window currently sits, which shrank the
        //      height on mixed-DPI multi-monitor setups.
        // This mirrors the "trust our own rect" stance used for QUERYPOS above and
        // keeps SetPosition consistent with MainWindow.ApplySettings (which also
        // derives the panel height from monitor.DpiY).
        _window.Left = desiredLeft / dpiX;
        _window.Top = desiredTop / dpiY;
        _window.Width = (desiredRight - desiredLeft) / dpiX;
        _window.Height = (desiredBottom - desiredTop) / dpiY;
    }

    private static readonly string LogPath =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Shelf.vd.log");

    private static void Log(string msg)
    {
        try { System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _callbackMessage)
        {
            int notif = wParam.ToInt32();
            if (notif == ABN_POSCHANGED && _lastMonitor != null)
            {
                SetPosition(_lastMonitor, _lastSide, _lastWidthDip);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }
}
