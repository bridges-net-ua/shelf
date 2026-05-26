using System;
using System.IO;
using System.Text.Json;
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
}
