using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

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

    // The physical-pixel rect the panel must occupy while registered. The guard
    // timer re-asserts it when the shell moves the window behind our back (see
    // the position-guard section below).
    private RECT _desiredRect;
    private bool _hasDesiredRect;
    private DispatcherTimer? _guardTimer;

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
        _hasDesiredRect = false;
        StopGuard();
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

        // Place the HWND directly in physical pixels via SetWindowPos - NOT through
        // WPF Window.Left/Top. Lessons from real screens:
        //   1. We deliberately ignore the ABM_SETPOS reply (Windows may clip a side
        //      AppBar's rect to the work area) and any HwndSource DPI conversion
        //      (wrong on mixed-DPI setups) - our desired rect needs no DIP math.
        //   2. WPF dependency properties no-op when the value looks unchanged. After
        //      the shell moves the HWND, Window.Left can still read the old value,
        //      so re-assigning it does nothing. SetWindowPos always takes effect.
        // WPF picks the move up via WM_WINDOWPOSCHANGED and syncs its own state.
        _desiredRect = new RECT
        {
            left = desiredLeft, top = desiredTop,
            right = desiredRight, bottom = desiredBottom
        };
        _hasDesiredRect = true;
        SetWindowPos(_hwnd, IntPtr.Zero, desiredLeft, desiredTop,
            desiredRight - desiredLeft, desiredBottom - desiredTop,
            SWP_NOZORDER | SWP_NOACTIVATE);

        StartGuard();
    }

    // ===== Position guard =====
    //
    // The shell can move an appbar window AFTER we placed it - observed live when
    // panels re-register while a widget is being deleted: a transient double strip
    // reservation makes Windows shove the bar one slot sideways a few seconds after
    // our final SETPOS (vd.log showed perfect coords, yet the window sat shifted).
    // While registered, a cheap once-a-second GetWindowRect compares the actual
    // window rect with the desired one and snaps the window back on any drift.

    private void StartGuard()
    {
        if (_guardTimer == null)
        {
            _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _guardTimer.Tick += (_, _) => GuardTick();
        }
        _guardTimer.Start();
    }

    private void StopGuard() => _guardTimer?.Stop();

    private void GuardTick()
    {
        if (!_registered || !_hasDesiredRect || _hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(_hwnd, out var rc)) return;

        if (rc.left != _desiredRect.left || rc.top != _desiredRect.top
            || rc.right != _desiredRect.right || rc.bottom != _desiredRect.bottom)
        {
            Log($"    [{_lastMonitor?.DeviceName}] Guard: drift L={rc.left} T={rc.top} " +
                $"R={rc.right} B={rc.bottom} -> snap back to L={_desiredRect.left} " +
                $"T={_desiredRect.top} R={_desiredRect.right} B={_desiredRect.bottom}");
            SetWindowPos(_hwnd, IntPtr.Zero, _desiredRect.left, _desiredRect.top,
                _desiredRect.right - _desiredRect.left, _desiredRect.bottom - _desiredRect.top,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }
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
