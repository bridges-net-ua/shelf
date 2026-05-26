using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Shelf.Services;

public class VirtualDesktopService : IDisposable
{
    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    private static readonly Guid CLSID_VirtualDesktopManager =
        new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    private static readonly Guid IID_IVirtualDesktopManager =
        new("a5cd92ff-29be-454c-8d04-d82879fb3f1b");

    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const int WS_POPUP = unchecked((int)0x80000000);

    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string? lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private readonly IntPtr _targetHwnd;
    private readonly IVirtualDesktopManager? _manager;
    private readonly DispatcherTimer _timer;
    private Guid _lastSeenCurrentDesktop = Guid.Empty;

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "Shelf.vd.log");

    public event Action? BeforeMove;
    public event Action? AfterMove;

    public VirtualDesktopService(IntPtr targetHwnd, TimeSpan pollInterval)
    {
        _targetHwnd = targetHwnd;
        _manager = TryCreateManager();
        Log($"Service init: hwnd=0x{targetHwnd.ToInt64():X}, managerAvailable={_manager != null}");

        _timer = new DispatcherTimer { Interval = pollInterval };
        _timer.Tick += (_, _) => Tick();
    }

    public bool IsAvailable => _manager != null;

    public void Start()
    {
        if (_manager != null)
        {
            _timer.Start();
            Log("Timer started.");
        }
    }

    public void Stop() => _timer.Stop();

    private static IVirtualDesktopManager? TryCreateManager()
    {
        try
        {
            Guid clsid = CLSID_VirtualDesktopManager;
            Guid iid = IID_IVirtualDesktopManager;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out object obj);
            if (hr != 0)
            {
                Log($"CoCreateInstance failed HR=0x{hr:X8}");
                return null;
            }
            return obj as IVirtualDesktopManager;
        }
        catch (Exception ex)
        {
            Log("TryCreateManager exception: " + ex.Message);
            return null;
        }
    }

    private void Tick()
    {
        if (_manager == null || _targetHwnd == IntPtr.Zero) return;

        try
        {
            Guid current = GetCurrentDesktopId();
            if (current == Guid.Empty) return;
            if (current == _lastSeenCurrentDesktop) return;

            _manager.GetWindowDesktopId(_targetHwnd, out Guid panelDesktop);

            Log($"Switch: prev={FormatGuid(_lastSeenCurrentDesktop)} new={FormatGuid(current)} panelWas={FormatGuid(panelDesktop)}");

            _lastSeenCurrentDesktop = current;

            if (panelDesktop == current) return;

            // Notify host so it can unregister AppBar (so Windows doesn't pin the visual to old desktop).
            BeforeMove?.Invoke();

            // Hide window so Windows doesn't try to keep it on old desktop visually.
            ShowWindow(_targetHwnd, SW_HIDE);

            int hrMove = _manager.MoveWindowToDesktop(_targetHwnd, ref current);

            // Show without activating, then force frame refresh.
            ShowWindow(_targetHwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(_targetHwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // Verify
            _manager.GetWindowDesktopId(_targetHwnd, out Guid panelAfter);
            Log($"  MoveHR=0x{hrMove:X8} panelNow={FormatGuid(panelAfter)}");

            AfterMove?.Invoke();
        }
        catch (Exception ex)
        {
            Log("Tick exception: " + ex.Message);
        }
    }

    private Guid GetCurrentDesktopId()
    {
        if (_manager == null) return Guid.Empty;

        IntPtr fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _targetHwnd)
        {
            int hr = _manager.GetWindowDesktopId(fg, out Guid fgId);
            if (hr == 0 && fgId != Guid.Empty) return fgId;
        }

        IntPtr shell = GetShellWindow();
        if (shell != IntPtr.Zero)
        {
            int hr = _manager.GetWindowDesktopId(shell, out Guid shellId);
            if (hr == 0 && shellId != Guid.Empty) return shellId;
        }

        IntPtr temp = CreateWindowExW(0, "STATIC", null, WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (temp == IntPtr.Zero) return Guid.Empty;
        try
        {
            int hr = _manager.GetWindowDesktopId(temp, out Guid id);
            return hr == 0 ? id : Guid.Empty;
        }
        finally
        {
            DestroyWindow(temp);
        }
    }

    private static string FormatGuid(Guid g) =>
        g == Guid.Empty ? "EMPTY" : g.ToString().Substring(0, 8);

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_manager != null)
        {
            try { Marshal.FinalReleaseComObject(_manager); } catch { }
        }
    }
}
