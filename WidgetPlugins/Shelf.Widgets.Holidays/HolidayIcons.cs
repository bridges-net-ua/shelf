using System.Collections.Generic;
using System.Windows.Media;

namespace Shelf.Widgets.Holidays;

/// <summary>
/// Mono-color icons (Material Design Icons subset, MIT-licensed) selectable per
/// user-holiday and per birthday. Stored in state as the string ID; the widget renders
/// the corresponding <see cref="Geometry"/> as a Path filled with the current theme's
/// text brush, so icons follow theme switches automatically.
/// </summary>
public static class HolidayIcons
{
    public const string Gift    = "gift";
    public const string Cake    = "cake";
    public const string Heart   = "heart";
    public const string Star    = "star";
    public const string Candle  = "candle";
    public const string Cross   = "cross";
    public const string Flag    = "flag";
    public const string Tree    = "tree";
    public const string Balloon = "balloon";
    public const string Flower  = "flower";

    /// <summary>Stable order for the editor's icon palette.</summary>
    public static readonly IReadOnlyList<string> Ids = new[]
    {
        Gift, Cake, Heart, Star, Candle, Cross, Flag, Tree, Balloon, Flower
    };

    // 24×24 viewBox. Paths from Material Design Icons (Apache 2.0 / SIL OFL).
    private static readonly Dictionary<string, string> _data = new()
    {
        [Gift]    = "M22,12V20A2,2 0 0,1 20,22H4A2,2 0 0,1 2,20V12A1,1 0 0,1 1,11V8A2,2 0 0,1 3,6H6.17C6.06,5.69 6,5.35 6,5A3,3 0 0,1 9,2C10,2 10.88,2.5 11.43,3.24V3.23L12,4L12.57,3.23V3.24C13.12,2.5 14,2 15,2A3,3 0 0,1 18,5C18,5.35 17.94,5.69 17.83,6H21A2,2 0 0,1 23,8V11A1,1 0 0,1 22,12M4,20H11V12H4V20M20,20V12H13V20H20M9,4A1,1 0 0,0 8,5A1,1 0 0,0 9,6A1,1 0 0,0 10,5A1,1 0 0,0 9,4M15,4A1,1 0 0,0 14,5A1,1 0 0,0 15,6A1,1 0 0,0 16,5A1,1 0 0,0 15,4M3,8V10H11V8H3M13,8V10H21V8H13Z",
        [Cake]    = "M12,6C13.11,6 14,5.11 14,4C14,3.62 13.9,3.27 13.71,2.97L12,0L10.29,2.97C10.1,3.27 10,3.62 10,4A2,2 0 0,0 12,6M16.6,16L15.53,14.92L14.45,16C13.15,17.29 10.87,17.3 9.56,16L8.5,14.92L7.4,16C6.75,16.65 5.88,17 4.96,17C4.23,17 3.56,16.77 3,16.39V21A1,1 0 0,0 4,22H20A1,1 0 0,0 21,21V16.39C20.44,16.77 19.77,17 19.04,17C18.12,17 17.25,16.65 16.6,16M18,9H13V7H11V9H6A3,3 0 0,0 3,12V13.54C3,14.62 3.88,15.5 4.96,15.5C5.5,15.5 6,15.3 6.34,14.93L8.5,12.8L10.61,14.93C11.35,15.67 12.64,15.67 13.38,14.93L15.5,12.8L17.65,14.93C18,15.3 18.5,15.5 19.03,15.5C20.11,15.5 21,14.62 21,13.54V12A3,3 0 0,0 18,9Z",
        [Heart]   = "M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z",
        [Star]    = "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z",
        [Candle]  = "M12,2C12,2 14,4 14,5.5A2,2 0 0,1 12,7.5A2,2 0 0,1 10,5.5C10,4 12,2 12,2M10,9H14V22H10V9Z",
        [Cross]   = "M10,2H14V8H20V12H14V22H10V12H4V8H10V2Z",
        [Flag]    = "M14.4,6L14,4H5V21H7V14H12.6L13,16H20V6H14.4Z",
        [Tree]    = "M10,21V18H3L7,12H4L8,6H6L10,1L14,6H12L16,12H13L17,18H14V21H10Z",
        [Balloon] = "M12,1.5C8.6,1.5 6,4.1 6,7.5C6,11.4 9.3,13.8 11,14.7L10,16C9.5,16 9,16.5 9,17V21.5C9,22.3 9.7,23 10.5,23H13.5C14.3,23 15,22.3 15,21.5V17C15,16.5 14.5,16 14,16L13,14.7C14.7,13.8 18,11.4 18,7.5C18,4.1 15.4,1.5 12,1.5M12,3.5C14.5,3.5 16,5.7 16,7.5C16,9.9 13.9,11.7 12,12.7C10.1,11.7 8,9.9 8,7.5C8,5.7 9.5,3.5 12,3.5Z",
        [Flower]  = "M3,13A9,9 0 0,0 12,22C12,17 7.97,13 3,13M12,5.5A2.5,2.5 0 0,1 14.5,8A2.5,2.5 0 0,1 12,10.5A2.5,2.5 0 0,1 9.5,8A2.5,2.5 0 0,1 12,5.5M5.6,10.25A2.5,2.5 0 0,0 8.1,12.75C8.63,12.75 9.12,12.58 9.5,12.31C9.5,12.37 9.5,12.43 9.5,12.5A2.5,2.5 0 0,0 12,15A2.5,2.5 0 0,0 14.5,12.5C14.5,12.43 14.5,12.37 14.5,12.31C14.88,12.58 15.37,12.75 15.9,12.75C17.28,12.75 18.4,11.63 18.4,10.25C18.4,9.25 17.81,8.4 16.97,8C17.81,7.6 18.4,6.75 18.4,5.75C18.4,4.37 17.28,3.25 15.9,3.25C15.37,3.25 14.88,3.42 14.5,3.69C14.5,3.63 14.5,3.57 14.5,3.5A2.5,2.5 0 0,0 12,1A2.5,2.5 0 0,0 9.5,3.5C9.5,3.57 9.5,3.63 9.5,3.69C9.12,3.42 8.63,3.25 8.1,3.25A2.5,2.5 0 0,0 5.6,5.75C5.6,6.75 6.19,7.6 7.03,8C6.19,8.4 5.6,9.25 5.6,10.25M12,22A9,9 0 0,0 21,13C16,13 12,17 12,22Z",
    };

    /// <summary>
    /// Parses the geometry for the given ID. Returns <c>null</c> for an empty ID,
    /// an unknown ID, or a malformed path (defensive against future data changes).
    /// </summary>
    public static Geometry? GetGeometry(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!_data.TryGetValue(id, out var d)) return null;
        try { return Geometry.Parse(d); }
        catch { return null; }
    }
}
