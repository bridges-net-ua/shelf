using System;
using System.IO;
using System.Text.Json;
#if STORE_BUILD
using System.Runtime.InteropServices;
#endif
using Shelf.Models;

namespace Shelf.Services;

public class SettingsService
{
    private static readonly string AppDataRoot =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static readonly string Dir = Path.Combine(AppDataRoot, "Shelf");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    // Legacy locations, in priority order (newest first). On first run, if Shelf/settings.json
    // is missing, the first existing legacy file is copied over.
    private static readonly string[] LegacyDirs =
    {
        Path.Combine(AppDataRoot, "Polychka"), // intermediate rename (pre-v1.0 dev builds)
        Path.Combine(AppDataRoot, "Помічник"), // original brand name
    };

    public AppSettings Current { get; private set; } = new();

    public event Action? Changed;

    public void Load()
    {
        try
        {
            // One-time migration: if the new "Shelf" file is missing, copy the first
            // legacy file we find (Polychka takes priority over Помічник).
            if (!File.Exists(FilePath))
            {
                foreach (var legacyDir in LegacyDirs)
                {
                    var legacyFile = Path.Combine(legacyDir, "settings.json");
                    if (File.Exists(legacyFile))
                    {
                        try
                        {
                            Directory.CreateDirectory(Dir);
                            File.Copy(legacyFile, FilePath, overwrite: false);
                        }
                        catch { /* ignore migration errors — fall back to defaults */ }
                        break;
                    }
                }
            }

            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) Current = loaded;
            }
        }
        catch
        {
            // broken file — keep defaults
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // ignore disk errors
        }
    }

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke();
    }

    // Returns the per-monitor panel config for the given device, falling back to
    // the legacy global Side/Width/AutoHide if the monitor has no entry yet.
    // The legacy fallback is what new monitors inherit on first connection — so
    // users with a single saved configuration get the same panel on every screen
    // until they tweak each individually.
    public MonitorPanelConfig GetMonitorConfig(string deviceName)
    {
        if (!string.IsNullOrEmpty(deviceName)
            && Current.MonitorPanels.TryGetValue(deviceName, out var cfg))
        {
            return cfg;
        }
        return new MonitorPanelConfig
        {
            Side = Current.Side,
            Width = Current.Width,
            AutoHide = Current.AutoHide
        };
    }

    // Persists a per-monitor config and saves to disk. Does NOT fire Changed —
    // callers should follow up with NotifyChanged() if the panel layout needs to
    // react, or just rely on App.ApplyAllBars() for a targeted refresh.
    public void SetMonitorConfig(string deviceName, MonitorPanelConfig cfg)
    {
        if (string.IsNullOrEmpty(deviceName)) return;
        Current.MonitorPanels[deviceName] = cfg;
        Save();
    }

    // One-time migration: if MonitorPanels is empty (which is the case for users
    // upgrading from a single-monitor settings.json), copy the legacy global
    // Side/Width/AutoHide into a fresh entry for the current primary monitor.
    // Idempotent — running it twice has no effect.
    public void MigrateLegacyPanel(string primaryDeviceName)
    {
        if (string.IsNullOrEmpty(primaryDeviceName)) return;
        if (Current.MonitorPanels.Count > 0) return;
        Current.MonitorPanels[primaryDeviceName] = new MonitorPanelConfig
        {
            Side = Current.Side,
            Width = Current.Width,
            AutoHide = Current.AutoHide
        };
        Save();
    }

#if STORE_BUILD
    // ---- MSIX → portable settings bridge ----
    // In a packaged build, %APPDATA% is silently redirected to
    //   %LOCALAPPDATA%\Packages\<PFN>\LocalCache\Roaming\
    // so a user who previously installed the portable zip cannot see their old
    // settings.json. Once per first launch of a packaged build we copy
    // %APPDATA%\Shelf\settings.json (real, non-virtualised) into the redirected
    // location and drop a "migrated.flag" marker so we never overwrite later edits.

    private const uint KF_FLAG_NO_PACKAGE_REDIRECTION = 0x00010000;
    private static readonly Guid FOLDERID_RoamingAppData =
        new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    public void MigratePortableToPackaged()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string markerPath = Path.Combine(Dir, "migrated.flag");
            if (File.Exists(markerPath)) return;

            // If the packaged location already has settings (Store install was used
            // first), don't overwrite — drop the marker so we don't re-check later.
            if (File.Exists(FilePath))
            {
                File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
                return;
            }

            string? realRoaming = GetRealRoamingAppDataPath();
            if (string.IsNullOrEmpty(realRoaming)) return;
            string portableSettings = Path.Combine(realRoaming, "Shelf", "settings.json");
            if (!File.Exists(portableSettings)) return;

            File.Copy(portableSettings, FilePath, overwrite: false);
            File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            // Best-effort — if the copy fails the app just starts with defaults.
        }
    }

    private static string? GetRealRoamingAppDataPath()
    {
        IntPtr pathPtr = IntPtr.Zero;
        try
        {
            int hr = SHGetKnownFolderPath(
                FOLDERID_RoamingAppData,
                KF_FLAG_NO_PACKAGE_REDIRECTION,
                IntPtr.Zero,
                out pathPtr);
            if (hr != 0 || pathPtr == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(pathPtr);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (pathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPtr);
        }
    }
#endif
}
