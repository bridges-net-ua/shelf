using System;
using System.Collections.Generic;

namespace Shelf.Widgets.Holidays;

public enum HolidayType
{
    State = 0,
    Religious = 1,
    Professional = 2,
    International = 3,
    User = 4
}

public sealed class Holiday
{
    public int Month { get; init; }
    public int Day { get; init; }
    public string Name { get; init; } = "";
    public HolidayType Type { get; init; }

    /// <summary>
    /// Optional anniversary year. Set only for user holidays where the user provided
    /// a year (e.g. someone's birth year).
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Optional mono-icon ID (see <see cref="HolidayIcons"/>). User holidays carry
    /// whatever the user picked in the editor.
    /// </summary>
    public string? Emoji { get; init; }

    /// <summary>
    /// Optional free-form note shown as a small line under the holiday/birthday name
    /// in the widget. Only set for user holidays and birthdays.
    /// </summary>
    public string? Note { get; init; }
}

/// <summary>
/// Holiday lookup. The built-in Ukrainian dataset (and the movable feasts) was removed -
/// the widget now starts empty and shows only the holidays/birthdays the user adds via
/// the settings dialog.
/// </summary>
public static class HolidaysData
{
    /// <summary>
    /// Returns every user holiday matching the given date, sorted by type priority
    /// then name. There are no built-in entries any more - the widget is empty until
    /// the user adds their own.
    /// </summary>
    public static List<Holiday> GetForDate(DateTime date, IEnumerable<Holiday>? userHolidays = null)
    {
        var result = new List<Holiday>();

        if (userHolidays != null)
            foreach (var h in userHolidays)
                if (h.Month == date.Month && h.Day == date.Day)
                    result.Add(h);

        result.Sort((a, b) =>
        {
            int cmp = ((int)a.Type).CompareTo((int)b.Type);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
        return result;
    }
}
