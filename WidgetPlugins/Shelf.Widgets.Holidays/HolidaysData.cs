using System;
using System.Collections.Generic;
using System.Linq;

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
    /// a year (e.g. someone's birth year). Built-in holidays leave this null - they
    /// repeat every year and have no anniversary count.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Optional mono-icon ID (see <see cref="HolidayIcons"/>). Built-in holidays leave
    /// this null - they have no associated icon. User holidays carry whatever the user
    /// picked in the editor.
    /// </summary>
    public string? Emoji { get; init; }

    /// <summary>
    /// Optional free-form note shown as a small line under the holiday/birthday name
    /// in the widget. Only set for user holidays and birthdays.
    /// </summary>
    public string? Note { get; init; }
}

/// <summary>
/// Built-in Ukrainian holiday dataset (~120 fixed dates) plus the three main
/// movable feasts (Palm Sunday / Easter / Trinity) computed per year from the
/// Gregorian Easter date used by the OCU since September 2023.
/// </summary>
public static class HolidaysData
{
    private static readonly Holiday[] _builtIn = new Holiday[]
    {
        // ===== Січень =====
        new() { Month=1,  Day=1,  Type=HolidayType.State,         Name="Новий рік" },
        new() { Month=1,  Day=1,  Type=HolidayType.Religious,     Name="Обрізання Господнє та Святого Василія" },
        new() { Month=1,  Day=4,  Type=HolidayType.International, Name="Всесвітній день шрифту Брайля" },
        new() { Month=1,  Day=6,  Type=HolidayType.Religious,     Name="Богоявлення (Водохреща)" },
        new() { Month=1,  Day=22, Type=HolidayType.State,         Name="День Соборності України" },
        new() { Month=1,  Day=25, Type=HolidayType.Professional,  Name="Тетянин день (День студента)" },
        new() { Month=1,  Day=27, Type=HolidayType.International, Name="Міжнародний день памʼяті жертв Голокосту" },
        new() { Month=1,  Day=29, Type=HolidayType.State,         Name="День памʼяті героїв Крут" },

        // ===== Лютий =====
        new() { Month=2,  Day=2,  Type=HolidayType.Religious,     Name="Стрітення Господнє" },
        new() { Month=2,  Day=4,  Type=HolidayType.International, Name="Всесвітній день боротьби з раком" },
        new() { Month=2,  Day=11, Type=HolidayType.International, Name="Міжнародний день жінок і дівчат у науці" },
        new() { Month=2,  Day=14, Type=HolidayType.International, Name="День Святого Валентина" },
        new() { Month=2,  Day=15, Type=HolidayType.State,         Name="День памʼяті воїнів-інтернаціоналістів" },
        new() { Month=2,  Day=20, Type=HolidayType.State,         Name="День Героїв Небесної Сотні" },
        new() { Month=2,  Day=21, Type=HolidayType.International, Name="Міжнародний день рідної мови" },
        new() { Month=2,  Day=24, Type=HolidayType.State,         Name="День спротиву окупації України" },

        // ===== Березень =====
        new() { Month=3,  Day=1,  Type=HolidayType.International, Name="Всесвітній день цивільної оборони" },
        new() { Month=3,  Day=3,  Type=HolidayType.International, Name="Всесвітній день дикої природи" },
        new() { Month=3,  Day=8,  Type=HolidayType.State,         Name="Міжнародний жіночий день" },
        new() { Month=3,  Day=9,  Type=HolidayType.State,         Name="День народження Тараса Шевченка" },
        new() { Month=3,  Day=15, Type=HolidayType.International, Name="Всесвітній день прав споживачів" },
        new() { Month=3,  Day=20, Type=HolidayType.International, Name="Міжнародний день щастя" },
        new() { Month=3,  Day=21, Type=HolidayType.International, Name="Всесвітній день поезії" },
        new() { Month=3,  Day=22, Type=HolidayType.International, Name="Всесвітній день води" },
        new() { Month=3,  Day=23, Type=HolidayType.International, Name="Всесвітній день метеорології" },
        new() { Month=3,  Day=25, Type=HolidayType.Religious,     Name="Благовіщення Пресвятої Богородиці" },
        new() { Month=3,  Day=26, Type=HolidayType.Professional,  Name="День Національної гвардії України" },

        // ===== Квітень =====
        new() { Month=4,  Day=1,  Type=HolidayType.International, Name="День сміху" },
        new() { Month=4,  Day=2,  Type=HolidayType.International, Name="Міжнародний день дитячої книги" },
        new() { Month=4,  Day=7,  Type=HolidayType.International, Name="Всесвітній день здоровʼя" },
        new() { Month=4,  Day=12, Type=HolidayType.International, Name="Всесвітній день авіації і космонавтики" },
        new() { Month=4,  Day=18, Type=HolidayType.International, Name="Міжнародний день памʼяток і визначних місць" },
        new() { Month=4,  Day=22, Type=HolidayType.International, Name="Всесвітній день Землі" },
        new() { Month=4,  Day=23, Type=HolidayType.International, Name="Всесвітній день книги і авторського права" },
        new() { Month=4,  Day=26, Type=HolidayType.State,         Name="День Чорнобильської трагедії" },
        new() { Month=4,  Day=29, Type=HolidayType.International, Name="Міжнародний день танцю" },

        // ===== Травень =====
        new() { Month=5,  Day=1,  Type=HolidayType.State,         Name="День праці" },
        new() { Month=5,  Day=3,  Type=HolidayType.International, Name="Всесвітній день свободи преси" },
        new() { Month=5,  Day=8,  Type=HolidayType.State,         Name="День памʼяті та перемоги над нацизмом у Другій світовій війні" },
        new() { Month=5,  Day=12, Type=HolidayType.International, Name="Міжнародний день медичної сестри" },
        new() { Month=5,  Day=15, Type=HolidayType.International, Name="Міжнародний день сімʼї" },
        new() { Month=5,  Day=17, Type=HolidayType.Professional,  Name="День науки України" },
        new() { Month=5,  Day=18, Type=HolidayType.State,         Name="День памʼяті жертв депортації кримськотатарського народу" },
        new() { Month=5,  Day=18, Type=HolidayType.International, Name="Міжнародний день музеїв" },
        new() { Month=5,  Day=21, Type=HolidayType.International, Name="Всесвітній день культурного розмаїття" },
        new() { Month=5,  Day=29, Type=HolidayType.International, Name="Міжнародний день миротворців ООН" },
        new() { Month=5,  Day=31, Type=HolidayType.International, Name="Всесвітній день без тютюну" },

        // ===== Червень =====
        new() { Month=6,  Day=1,  Type=HolidayType.International, Name="Міжнародний день захисту дітей" },
        new() { Month=6,  Day=5,  Type=HolidayType.International, Name="Всесвітній день довкілля" },
        new() { Month=6,  Day=8,  Type=HolidayType.International, Name="Всесвітній день океанів" },
        new() { Month=6,  Day=12, Type=HolidayType.International, Name="Всесвітній день боротьби з дитячою працею" },
        new() { Month=6,  Day=14, Type=HolidayType.International, Name="Всесвітній день донора крові" },
        new() { Month=6,  Day=20, Type=HolidayType.International, Name="Всесвітній день біженців" },
        new() { Month=6,  Day=22, Type=HolidayType.State,         Name="День скорботи і вшанування памʼяті жертв війни в Україні" },
        new() { Month=6,  Day=23, Type=HolidayType.Professional,  Name="День державної служби" },
        new() { Month=6,  Day=26, Type=HolidayType.International, Name="Міжнародний день боротьби з наркоманією" },
        new() { Month=6,  Day=27, Type=HolidayType.State,         Name="День молоді" },
        new() { Month=6,  Day=28, Type=HolidayType.State,         Name="День Конституції України" },

        // ===== Липень =====
        new() { Month=7,  Day=2,  Type=HolidayType.International, Name="Всесвітній день НЛО" },
        new() { Month=7,  Day=11, Type=HolidayType.International, Name="Всесвітній день народонаселення" },
        new() { Month=7,  Day=20, Type=HolidayType.International, Name="Міжнародний день шахів" },
        new() { Month=7,  Day=23, Type=HolidayType.International, Name="Всесвітній день китів і дельфінів" },
        new() { Month=7,  Day=28, Type=HolidayType.State,         Name="День Української Державності" },
        new() { Month=7,  Day=30, Type=HolidayType.International, Name="Всесвітній день боротьби з торгівлею людьми" },

        // ===== Серпень =====
        new() { Month=8,  Day=6,  Type=HolidayType.Religious,     Name="Преображення Господнє (Яблучний Спас)" },
        new() { Month=8,  Day=8,  Type=HolidayType.International, Name="Всесвітній день котів" },
        new() { Month=8,  Day=12, Type=HolidayType.International, Name="Міжнародний день молоді" },
        new() { Month=8,  Day=13, Type=HolidayType.International, Name="Міжнародний день лівшів" },
        new() { Month=8,  Day=15, Type=HolidayType.Religious,     Name="Успіння Пресвятої Богородиці" },
        new() { Month=8,  Day=19, Type=HolidayType.International, Name="Всесвітній день гуманітарної допомоги" },
        new() { Month=8,  Day=23, Type=HolidayType.State,         Name="День Державного Прапора України" },
        new() { Month=8,  Day=24, Type=HolidayType.State,         Name="День Незалежності України" },
        new() { Month=8,  Day=29, Type=HolidayType.State,         Name="День памʼяті захисників України" },

        // ===== Вересень =====
        new() { Month=9,  Day=1,  Type=HolidayType.Professional,  Name="День знань" },
        new() { Month=9,  Day=8,  Type=HolidayType.Religious,     Name="Різдво Пресвятої Богородиці" },
        new() { Month=9,  Day=8,  Type=HolidayType.International, Name="Міжнародний день грамотності" },
        new() { Month=9,  Day=9,  Type=HolidayType.International, Name="Всесвітній день краси" },
        new() { Month=9,  Day=10, Type=HolidayType.International, Name="Всесвітній день запобігання самогубствам" },
        new() { Month=9,  Day=14, Type=HolidayType.Religious,     Name="Воздвиження Чесного Хреста Господнього" },
        new() { Month=9,  Day=15, Type=HolidayType.International, Name="Міжнародний день демократії" },
        new() { Month=9,  Day=16, Type=HolidayType.International, Name="Міжнародний день охорони озонового шару" },
        new() { Month=9,  Day=21, Type=HolidayType.International, Name="Міжнародний день миру" },
        new() { Month=9,  Day=27, Type=HolidayType.International, Name="Всесвітній день туризму" },
        new() { Month=9,  Day=29, Type=HolidayType.International, Name="Міжнародний день кави" },
        new() { Month=9,  Day=30, Type=HolidayType.Professional,  Name="День усиновлення" },

        // ===== Жовтень =====
        new() { Month=10, Day=1,  Type=HolidayType.State,         Name="День захисників і захисниць України" },
        new() { Month=10, Day=1,  Type=HolidayType.Religious,     Name="Покрова Пресвятої Богородиці" },
        new() { Month=10, Day=1,  Type=HolidayType.International, Name="Міжнародний день людей похилого віку" },
        new() { Month=10, Day=2,  Type=HolidayType.International, Name="Міжнародний день ненасильства" },
        new() { Month=10, Day=4,  Type=HolidayType.International, Name="Всесвітній день тварин" },
        new() { Month=10, Day=5,  Type=HolidayType.International, Name="Всесвітній день вчителів" },
        new() { Month=10, Day=9,  Type=HolidayType.International, Name="Всесвітній день пошти" },
        new() { Month=10, Day=10, Type=HolidayType.International, Name="Всесвітній день психічного здоровʼя" },
        new() { Month=10, Day=16, Type=HolidayType.International, Name="Всесвітній день продовольства" },
        new() { Month=10, Day=24, Type=HolidayType.International, Name="День Організації Обʼєднаних Націй" },
        new() { Month=10, Day=31, Type=HolidayType.International, Name="Гелловін" },

        // ===== Листопад =====
        new() { Month=11, Day=1,  Type=HolidayType.International, Name="Міжнародний день вегана" },
        new() { Month=11, Day=9,  Type=HolidayType.State,         Name="День української писемності та мови" },
        new() { Month=11, Day=13, Type=HolidayType.International, Name="Всесвітній день доброти" },
        new() { Month=11, Day=16, Type=HolidayType.International, Name="Міжнародний день толерантності" },
        new() { Month=11, Day=17, Type=HolidayType.Professional,  Name="Міжнародний день студента" },
        new() { Month=11, Day=19, Type=HolidayType.International, Name="Всесвітній день туалету" },
        new() { Month=11, Day=20, Type=HolidayType.International, Name="Всесвітній день дитини" },
        new() { Month=11, Day=21, Type=HolidayType.State,         Name="День Гідності та Свободи" },
        new() { Month=11, Day=21, Type=HolidayType.Religious,     Name="Введення в храм Пресвятої Богородиці" },
        new() { Month=11, Day=25, Type=HolidayType.International, Name="Міжнародний день боротьби з насильством щодо жінок" },

        // ===== Грудень =====
        new() { Month=12, Day=1,  Type=HolidayType.International, Name="Всесвітній день боротьби зі СНІДом" },
        new() { Month=12, Day=3,  Type=HolidayType.International, Name="Міжнародний день людей з інвалідністю" },
        new() { Month=12, Day=5,  Type=HolidayType.International, Name="Міжнародний день волонтерів" },
        new() { Month=12, Day=6,  Type=HolidayType.State,         Name="День Збройних Сил України" },
        new() { Month=12, Day=6,  Type=HolidayType.Religious,     Name="Святого Миколая Чудотворця" },
        new() { Month=12, Day=9,  Type=HolidayType.International, Name="Міжнародний день боротьби з корупцією" },
        new() { Month=12, Day=10, Type=HolidayType.International, Name="День прав людини" },
        new() { Month=12, Day=14, Type=HolidayType.State,         Name="День вшанування учасників ліквідації аварії на ЧАЕС" },
        new() { Month=12, Day=22, Type=HolidayType.Professional,  Name="День енергетика" },
        new() { Month=12, Day=24, Type=HolidayType.Religious,     Name="Святвечір" },
        new() { Month=12, Day=25, Type=HolidayType.State,         Name="Різдво Христове" },
    };

    // Movable feasts cached per year (Easter and its dependents).
    private static readonly Dictionary<int, List<Holiday>> _movableCache = new();
    private static readonly object _movableLock = new();

    /// <summary>
    /// Returns every holiday matching the given date, sorted by type priority
    /// (State first, then Religious, Professional, International, User).
    /// </summary>
    public static List<Holiday> GetForDate(DateTime date, IEnumerable<Holiday>? userHolidays = null)
    {
        var result = new List<Holiday>();

        foreach (var h in _builtIn)
            if (h.Month == date.Month && h.Day == date.Day)
                result.Add(h);

        foreach (var h in GetMovableForYear(date.Year))
            if (h.Month == date.Month && h.Day == date.Day)
                result.Add(h);

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

    private static List<Holiday> GetMovableForYear(int year)
    {
        lock (_movableLock)
        {
            if (_movableCache.TryGetValue(year, out var cached)) return cached;

            var easter = ComputeGregorianEaster(year);
            var palmSunday = easter.AddDays(-7);
            var trinity = easter.AddDays(49);

            var list = new List<Holiday>
            {
                new() { Month = palmSunday.Month, Day = palmSunday.Day,
                        Type = HolidayType.Religious, Name = "Вербна неділя" },
                new() { Month = easter.Month,     Day = easter.Day,
                        Type = HolidayType.Religious, Name = "Великдень (Пасха)" },
                new() { Month = trinity.Month,    Day = trinity.Day,
                        Type = HolidayType.Religious, Name = "Трійця (Пʼятидесятниця)" },
            };
            _movableCache[year] = list;
            return list;
        }
    }

    /// <summary>
    /// Gauss algorithm — Gregorian (Western) Easter date for the given year.
    /// Used by the Orthodox Church of Ukraine since September 2023.
    /// </summary>
    public static DateTime ComputeGregorianEaster(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateTime(year, month, day);
    }
}
