using System;
#if !STORE_BUILD
using System.Diagnostics;
using Microsoft.Win32;
#else
using Windows.ApplicationModel;
#endif

namespace Shelf.Services;

// Autostart on Windows login. Two implementations behind STORE_BUILD because
// HKCU\Software\Microsoft\Windows\CurrentVersion\Run is silently virtualised
// inside MSIX packages — writes succeed but Windows ignores them. Store builds
// must use the windows.startupTask extension declared in Package.appxmanifest.
public static class AutoStartService
{
#if !STORE_BUILD
    // ---- Registry-backed autostart (Debug/Release/portable zip) ----

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Shelf";

    // Legacy registry value names from previous renames. Listed in priority order;
    // each is checked and removed by MigrateLegacyValue().
    private static readonly string[] LegacyValueNames = { "Polychka", "Помічник" };

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(ValueName, "\"" + exePath + "\"");
            }
            else
            {
                if (key.GetValue(ValueName) != null)
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // ignore registry errors
        }
    }

    // Remove any legacy autostart entry (Polychka, Помічник) and promote the user's
    // intent to the new "Shelf" value so autostart survives the rename.
    public static void MigrateLegacyValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            bool hadAnyLegacy = false;
            foreach (var legacyName in LegacyValueNames)
            {
                if (key.GetValue(legacyName) != null)
                {
                    hadAnyLegacy = true;
                    key.DeleteValue(legacyName, throwOnMissingValue: false);
                }
            }

            if (hadAnyLegacy && key.GetValue(ValueName) == null)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(ValueName, "\"" + exePath + "\"");
            }
        }
        catch
        {
            // ignore registry errors
        }
    }
#else
    // ---- MSIX startup task (Store) ----
    // TaskId must match <desktop:StartupTask TaskId="..."> in Package.appxmanifest.

    private const string StartupTaskId = "ShelfAutoStart";

    public static bool IsEnabled()
    {
        try
        {
            var task = StartupTask.GetAsync(StartupTaskId).GetAwaiter().GetResult();
            // Enabled and EnabledByPolicy are the "on" states. DisabledByUser /
            // DisabledByPolicy block us from re-enabling — surface that as "off" so
            // the UI checkbox stays unchecked rather than misleading the user.
            return task.State == StartupTaskState.Enabled
                || task.State == StartupTaskState.EnabledByPolicy;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            var task = StartupTask.GetAsync(StartupTaskId).GetAwaiter().GetResult();
            if (enabled)
            {
                // RequestEnableAsync may show a system prompt on first opt-in. Result
                // reflects what Windows actually granted (e.g. DisabledByUser if the
                // user later toggled it off in Settings → Apps → Startup).
                _ = task.RequestEnableAsync().GetAwaiter().GetResult();
            }
            else
            {
                task.Disable();
            }
        }
        catch
        {
            // ignore — autostart toggle should never crash the app
        }
    }

    // No legacy migration in Store builds — registry-based autostart was never
    // reachable from inside the MSIX sandbox in the first place.
    public static void MigrateLegacyValue()
    {
    }
#endif
}
