using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Shelf.Services;

public static class AutoStartService
{
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
}
