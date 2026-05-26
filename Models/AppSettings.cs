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

    // Legacy field for backward compatibility with older settings.json.
    // Migrated to TypeId at load time, then nulled so it is not re-serialized.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }
}

public class AppSettings
{
    public BarSide Side { get; set; } = BarSide.Right;
    public int Width { get; set; } = 300;
    public bool AutoHide { get; set; } = false;
    public bool AutoStart { get; set; } = false;
    public bool WidgetOrderLocked { get; set; } = false;
    public bool InitializedWithDefaults { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppLanguage Language { get; set; } = AppLanguage.Uk;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public List<WidgetEntry> Widgets { get; set; } = new();
}
