using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Shelf.Services;

// Snapshot of a single connected monitor at the time the service polled the OS.
// All four bounds values are in PHYSICAL pixels (BoundsPx, WorkAreaPx). DpiX/DpiY
// are the effective DPI of this monitor (per-monitor-v2 aware). DisplayName is a
// short human-readable label used in UI ("Display 2: 2560×1440").
public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }   // `\\.\DISPLAY2`
    public required bool IsPrimary { get; init; }
    public required System.Drawing.Rectangle BoundsPx { get; init; }
    public required System.Drawing.Rectangle WorkAreaPx { get; init; }
    public required double DpiX { get; init; }
    public required double DpiY { get; init; }
    public required string DisplayName { get; init; }  // "Головний (1920×1080)" / "Display 2: 2560×1440"
}

// Tracks the set of connected monitors, exposes per-monitor DPI and a `TopologyChanged`
// event that fires whenever Windows reports a display configuration change
// (connect / disconnect / DPI change / primary swap / resolution change).
//
// The Win32 GetDpiForMonitor API (shcore.dll) gives us effective DPI per HMONITOR;
// this matches what the WPF HwndSource.CompositionTarget.TransformToDevice reports
// for a window placed on that monitor under our PerMonitorV2 manifest.
public sealed class MonitorService : IDisposable
{
    public event Action? TopologyChanged;

    private List<MonitorInfo> _monitors = new();
    private readonly object _lock = new();

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get { lock (_lock) return _monitors.ToList(); }
    }

    public MonitorInfo Primary
    {
        get
        {
            lock (_lock)
            {
                return _monitors.FirstOrDefault(m => m.IsPrimary)
                    ?? _monitors.FirstOrDefault()
                    ?? FallbackPrimary();
            }
        }
    }

    public MonitorService()
    {
        Refresh();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public MonitorInfo? FindByDeviceName(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return null;
        lock (_lock)
        {
            return _monitors.FirstOrDefault(
                m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Resolves a widget's MonitorDeviceName to an actual MonitorInfo.
    // Null/empty/unknown DeviceName ⇒ primary (widget is "homeless" if the original
    // DeviceName was set but the monitor isn't connected right now, but we still
    // render it on primary as a fallback display).
    public MonitorInfo ResolveOrPrimary(string? deviceName) =>
        FindByDeviceName(deviceName) ?? Primary;

    public bool IsConnected(string? deviceName) =>
        !string.IsNullOrEmpty(deviceName) && FindByDeviceName(deviceName) != null;

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Refresh();
        Log("DisplaySettingsChanged → topology refreshed.");
        TopologyChanged?.Invoke();
    }

    private void Refresh()
    {
        var fresh = new List<MonitorInfo>();
        try
        {
            // Group all screens by index so we can build a "Display N" label that
            // matches what Windows' Settings app shows (best-effort — there's no
            // public API to get the "1/2/3" number directly).
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                double dpiX = 96.0, dpiY = 96.0;
                try
                {
                    var monitor = MonitorFromPoint(
                        new POINT { X = s.Bounds.Left + 1, Y = s.Bounds.Top + 1 },
                        MONITOR_DEFAULTTONEAREST);
                    if (monitor != IntPtr.Zero)
                    {
                        int hr = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiXi, out uint dpiYi);
                        if (hr == 0)
                        {
                            dpiX = dpiXi / 96.0;
                            dpiY = dpiYi / 96.0;
                        }
                    }
                }
                catch
                {
                    // Older Windows or missing shcore.dll — fall back to 96 DPI assumption.
                }

                int wPx = s.Bounds.Width;
                int hPx = s.Bounds.Height;

                fresh.Add(new MonitorInfo
                {
                    DeviceName = s.DeviceName,
                    IsPrimary = s.Primary,
                    BoundsPx = s.Bounds,
                    WorkAreaPx = s.WorkingArea,
                    DpiX = dpiX,
                    DpiY = dpiY,
                    DisplayName = s.Primary
                        ? $"Display {i + 1}: {wPx}×{hPx} ★"
                        : $"Display {i + 1}: {wPx}×{hPx}"
                });
            }
        }
        catch (Exception ex)
        {
            Log("Refresh failed: " + ex.Message);
        }

        if (fresh.Count == 0) fresh.Add(FallbackPrimary());

        lock (_lock) _monitors = fresh;
    }

    private static MonitorInfo FallbackPrimary() => new()
    {
        DeviceName = "\\\\.\\DISPLAY1",
        IsPrimary = true,
        BoundsPx = new System.Drawing.Rectangle(0, 0, 1920, 1080),
        WorkAreaPx = new System.Drawing.Rectangle(0, 0, 1920, 1080),
        DpiX = 1.0,
        DpiY = 1.0,
        DisplayName = "Display 1: 1920×1080 ★"
    };

    public void Dispose()
    {
        try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
    }

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "Shelf.vd.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Monitors] {msg}\n"); }
        catch { }
    }

    // ===== P/Invoke =====

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
