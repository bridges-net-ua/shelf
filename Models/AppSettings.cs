using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shelf.Sdk;

namespace Shelf.Models;

public enum BarSide
{
    Left,
    Right
}

public class WidgetEntry
{
    public string InstanceId { get; set; } = "";
    public string TypeId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Pinned { get; set; } = false;
    public string State { get; set; } = "";

    // Which monitor this widget lives on. Null = primary at runtime. A non-null value
    // is the device name (`\\.\DISPLAYN`) of a specific monitor — even if that monitor
    // is currently disconnected, the assignment is preserved here so the widget
    // returns to it when the monitor comes back. Widgets whose target is currently
    // disconnected ("homeless") render at the bottom of the primary panel.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MonitorDeviceName { get; set; }

    // Legacy field for backward compatibility with older settings.json.
    // Migrated to TypeId at load time, then nulled so it is not re-serialized.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }
}

// Per-monitor panel parameters. Same shape across all monitors, but stored
// separately keyed by Screen.DeviceName, so each monitor can have its own
// side / width / auto-hide configuration.
public class MonitorPanelConfig
{
    public BarSide Side { get; set; } = BarSide.Left;
    public int Width { get; set; } = 300;
    public bool AutoHide { get; set; } = false;
}

public class AppSettings
{
    // ===== Legacy single-monitor fields (pre-v1.2).
    // Kept here so older settings.json deserializes; migrated into MonitorPanels[primary]
    // by SettingsService on first run after upgrade, then ignored at runtime.
    public BarSide Side { get; set; } = BarSide.Left;
    public int Width { get; set; } = 300;
    public bool AutoHide { get; set; } = false;

    // ===== Per-monitor panel configs, keyed by Screen.DeviceName (`\\.\DISPLAYN`).
    // Lookup is best-effort: a missing key falls back to a default config built from
    // the legacy fields (or library defaults if they were never set).
    public Dictionary<string, MonitorPanelConfig> MonitorPanels { get; set; } = new();

    public bool AutoStart { get; set; } = false;
    public bool WidgetOrderLocked { get; set; } = false;
    public bool InitializedWithDefaults { get; set; } = false;

    // Daily "minute of silence" at 9:00 - dims every panel, shows a 60s countdown,
    // plays a metronome and silences audio widgets. On by default (national custom).
    public bool MinuteOfSilenceEnabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppLanguage Language { get; set; } = AppLanguage.Uk;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    // ===== Auto-update check (PORTABLE builds only; Store handles its own updates).
    // Persisted so the About-tab badge can render instantly without a network call.
    // Written by the once-a-day background check in App.OnStartup.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastUpdateCheckUtc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LatestKnownVersion { get; set; }

    public List<WidgetEntry> Widgets { get; set; } = new();
}
