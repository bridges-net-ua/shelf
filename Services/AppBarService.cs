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

    public AppBarService(Window window)
    {
        _window = window;
    }

    public void Register(BarSide side, int width)
    {
        if (_registered) Unregister();

        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) return;

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
        SetPosition(side, width);
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

    public void SetPosition(BarSide side, int width)
    {
        if (!_registered || _hwnd == IntPtr.Zero) return;

        var src = HwndSource.FromHwnd(_hwnd);
        double dpiX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        int screenWidthPx = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpiX);
        int screenHeightPx = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpiY);
        int widthPx = (int)Math.Round(width * dpiX);

        // Compute our desired rect up-front. We do NOT trust ABM_QUERYPOS to come back
        // with the same coordinates — under some race conditions (notably right after a
        // virtual-desktop move + Hide/Show cycle) Windows returns a rect anchored at the
        // left edge regardless of the uEdge we requested, which leaves the panel detached
        // from the intended screen edge. So we issue QUERYPOS for protocol compliance,
        // then overwrite rc with our own values before SETPOS.
        int desiredLeft, desiredTop = 0, desiredRight, desiredBottom = screenHeightPx;
        uint desiredEdge;
        if (side == BarSide.Left)
        {
            desiredEdge = ABE_LEFT;
            desiredLeft = 0;
            desiredRight = widthPx;
        }
        else
        {
            desiredEdge = ABE_RIGHT;
            desiredLeft = screenWidthPx - widthPx;
            desiredRight = screenWidthPx;
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
        Log($"    QUERYPOS returned: L={data.rc.left} T={data.rc.top} R={data.rc.right} B={data.rc.bottom} " +
            $"(desired L={desiredLeft} R={desiredRight}, side={side}, dpiX={dpiX:F3})");

        // Force our own rect — ignore whatever QUERYPOS proposed, keeping only the edge.
        data.uEdge = desiredEdge;
        data.rc.left = desiredLeft;
        data.rc.top = desiredTop;
        data.rc.right = desiredRight;
        data.rc.bottom = desiredBottom;

        SHAppBarMessage(ABM_SETPOS, ref data);
        Log($"    SETPOS final:     L={data.rc.left} T={data.rc.top} R={data.rc.right} B={data.rc.bottom}");

        // Position the WPF window (convert back to DIPs)
        _window.Left = data.rc.left / dpiX;
        _window.Top = data.rc.top / dpiY;
        _window.Width = (data.rc.right - data.rc.left) / dpiX;
        _window.Height = (data.rc.bottom - data.rc.top) / dpiY;
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
            if (notif == ABN_POSCHANGED)
            {
                var s = App.Settings.Current;
                SetPosition(s.Side, s.Width);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }
}
