using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Shelf.Services;

// Pins our window to every virtual desktop using the undocumented
// IVirtualDesktopManagerInternal.PinView API. Microsoft does not publish a stable
// public API for this — the GUIDs and interface layouts have shifted between Windows
// versions (Win10 → Win11 21H2 → Win11 22H2+). We try each known variant in turn
// and fall back to "not pinned" if none match, in which case the caller keeps the
// older MoveWindowToDesktop machinery as a fallback.
//
// Reference (community-maintained): https://github.com/MScholtes/PSVirtualDesktop and
// numerous reverse-engineering write-ups of explorer.exe.
public static class VirtualDesktopPinService
{
    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    private const uint CLSCTX_LOCAL_SERVER = 0x4;

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "Shelf.vd.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Pin] {msg}\n"); }
        catch { }
    }

    // CLSIDs of the immersive shell services that expose internal interfaces.
    private static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");

    // ─── IApplicationView (GetApplicationViewForHwnd) ──────────────────────────────

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513")]
    private interface IApplicationView { }

    // ─── IApplicationViewCollection ────────────────────────────────────────────────
    // Same shape across Win10/Win11; CLSID is requested via service activation.
    private static readonly Guid CLSID_ApplicationViewCollection_Win10 =
        new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");
    private static readonly Guid CLSID_ApplicationViewCollection_Win11 =
        new("2C08ADF0-A386-4B35-9250-0FE183476FCC");

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
    private interface IApplicationViewCollection
    {
        void GetViews_skip_0();
        void GetViewsByZOrder_skip_1();
        void GetViewsByAppUserModelId_skip_2();

        [PreserveSig]
        int GetViewForHwnd(IntPtr hwnd, out IApplicationView view);
    }

    // ─── IVirtualDesktopPinnedApps — pinning interface ─────────────────────────────
    // This interface stayed surprisingly stable: same GUID across Win10/Win11
    // versions we've tested. PinView is the relevant method.
    private static readonly Guid CLSID_VirtualDesktopPinnedApps =
        new("B5A399E7-1C87-46B8-88E9-FC5747B171BD");

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("4CE81583-1E4C-4632-A621-07A53543148F")]
    private interface IVirtualDesktopPinnedApps
    {
        [PreserveSig] int IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string appId, out bool pinned);
        [PreserveSig] int PinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int IsViewPinned(IApplicationView view, out bool pinned);
        [PreserveSig] int PinView(IApplicationView view);
        [PreserveSig] int UnpinView(IApplicationView view);
    }

    // ─── IServiceProvider (the immersive shell exposes services by GUID) ───────────

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IServiceProvider10
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    /// <summary>
    /// Try to pin the given top-level window to every virtual desktop, retrying for up to
    /// ~1s if Windows hasn't registered the HWND in its Application View Collection yet
    /// (which is normal right after window creation).
    /// Returns true on success, false otherwise.
    /// </summary>
    public static bool TryPinWithRetry(IntPtr hwnd, int maxAttempts = 10, int delayMs = 100)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            if (TryPin(hwnd)) return true;
            System.Threading.Thread.Sleep(delayMs);
        }
        Log($"TryPinWithRetry exhausted after {maxAttempts} attempts.");
        return false;
    }

    /// <summary>
    /// Single-attempt pin. Use <see cref="TryPinWithRetry"/> from startup paths instead.
    /// </summary>
    public static bool TryPin(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            Log("Skip: hwnd is zero.");
            return false;
        }

        IServiceProvider10? shell = null;
        try
        {
            Guid shellClsid = CLSID_ImmersiveShell;
            Guid spIid = typeof(IServiceProvider10).GUID;
            int hr = CoCreateInstance(ref shellClsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref spIid, out object obj);
            if (hr != 0 || obj == null)
            {
                Log($"CoCreateInstance(ImmersiveShell) failed HR=0x{hr:X8}");
                return false;
            }
            shell = obj as IServiceProvider10;
            if (shell == null)
            {
                Log("ImmersiveShell does not implement IServiceProvider.");
                return false;
            }

            // Get IApplicationView for our window. Try Win11 GUID first, fall back to Win10.
            IApplicationView? view = TryGetView(shell, CLSID_ApplicationViewCollection_Win11, hwnd);
            view ??= TryGetView(shell, CLSID_ApplicationViewCollection_Win10, hwnd);
            if (view == null)
            {
                Log("Failed to obtain IApplicationView for hwnd from either CLSID.");
                return false;
            }

            // Pin the view.
            Guid pinClsid = CLSID_VirtualDesktopPinnedApps;
            Guid pinIid = typeof(IVirtualDesktopPinnedApps).GUID;
            int hrPin = shell.QueryService(ref pinClsid, ref pinIid, out object pinObj);
            if (hrPin != 0 || pinObj == null)
            {
                Log($"QueryService(VirtualDesktopPinnedApps) failed HR=0x{hrPin:X8}");
                return false;
            }

            var pin = pinObj as IVirtualDesktopPinnedApps;
            if (pin == null)
            {
                Log("VirtualDesktopPinnedApps does not implement the expected interface.");
                return false;
            }

            int hrCheck = pin.IsViewPinned(view, out bool alreadyPinned);
            if (hrCheck == 0 && alreadyPinned)
            {
                Log("View was already pinned — nothing to do.");
                return true;
            }

            int hrPinView = pin.PinView(view);
            if (hrPinView != 0)
            {
                Log($"PinView failed HR=0x{hrPinView:X8}");
                return false;
            }

            Log("PinView succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            Log("Exception: " + ex.Message);
            return false;
        }
        finally
        {
            if (shell != null)
            {
                try { Marshal.ReleaseComObject(shell); } catch { }
            }
        }
    }

    private static IApplicationView? TryGetView(IServiceProvider10 shell, Guid collectionClsid, IntPtr hwnd)
    {
        try
        {
            Guid clsid = collectionClsid;
            Guid iid = typeof(IApplicationViewCollection).GUID;
            int hr = shell.QueryService(ref clsid, ref iid, out object obj);
            if (hr != 0 || obj is not IApplicationViewCollection coll)
            {
                Log($"QueryService(ViewCollection clsid={collectionClsid}) failed HR=0x{hr:X8}");
                return null;
            }

            int hrView = coll.GetViewForHwnd(hwnd, out IApplicationView view);
            if (hrView != 0)
            {
                Log($"GetViewForHwnd failed HR=0x{hrView:X8} (clsid={collectionClsid})");
                return null;
            }
            return view;
        }
        catch (Exception ex)
        {
            Log($"TryGetView({collectionClsid}) exception: " + ex.Message);
            return null;
        }
    }
}
