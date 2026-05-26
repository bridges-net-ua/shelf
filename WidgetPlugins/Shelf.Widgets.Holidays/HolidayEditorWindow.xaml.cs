using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Shelf.Sdk;

namespace Shelf.Widgets.Holidays;

public enum HolidayEditorAction
{
    Cancelled,
    Saved,
    Removed
}

public enum HolidayEditorKind
{
    Holiday,
    Birthday
}

/// <summary>
/// Flat DTO for the editor's I/O. The dialog reads its initial state from
/// <see cref="HolidayEditorWindow.HolidayEditorWindow(HolidayEditorKind, HolidayEditorData)"/>
/// and writes back to <see cref="HolidayEditorWindow.ResultData"/>. Callers convert to/from
/// <c>UserHoliday</c> or <c>Birthday</c>.
/// </summary>
public class HolidayEditorData
{
    public int Month;
    public int Day;
    public int? Year;
    public string Name = "";
    public string? Emoji;
    public string? Note;
}

public partial class HolidayEditorWindow : Window
{
    public HolidayEditorAction ResultAction { get; private set; } = HolidayEditorAction.Cancelled;
    public HolidayEditorData? ResultData { get; private set; }

    private readonly HolidayEditorKind _kind;
    private readonly bool _isEdit;
    private readonly string _originalNameForDelete;

    /// <summary>
    /// <paramref name="initial"/> non-null = edit mode (pre-fills fields and shows the
    /// Delete button); null = add mode (uses sensible defaults based on <paramref name="kind"/>).
    /// </summary>
    public HolidayEditorWindow(HolidayEditorKind kind, HolidayEditorData? initial)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        _kind = kind;
        _isEdit = initial != null;
        _originalNameForDelete = initial?.Name ?? "";

        // Title / primary button label / delete visibility depend on kind+mode.
        Title = Loc.Get(_isEdit
            ? (kind == HolidayEditorKind.Birthday ? "Holidays_Editor_Title_Birthday_Edit" : "Holidays_Editor_Title_Edit")
            : (kind == HolidayEditorKind.Birthday ? "Holidays_Editor_Title_Birthday_Add" : "Holidays_Editor_Title_Add"));
        OkButton.Content = Loc.Get(_isEdit ? "Holidays_Btn_Save" : "Holidays_Add");
        DeleteButton.Visibility = _isEdit ? Visibility.Visible : Visibility.Collapsed;

        PopulateMonthCombo();
        PopulateDayCombo();
        PopulateYearCombo();
        PopulateIconPalette();

        // Initial values - existing or today's date for a fresh add. For new birthdays
        // the icon defaults to "gift"; for new user holidays the icon defaults to none.
        var today = DateTime.Today;
        int initialMonth = initial?.Month ?? today.Month;
        int initialDay = initial?.Day ?? today.Day;
        NameBox.Text = initial?.Name ?? Loc.Get(kind == HolidayEditorKind.Birthday
            ? "Holidays_Birthday_NewName"
            : "Holidays_NewHoliday");
        string? initialEmoji = initial?.Emoji
            ?? (kind == HolidayEditorKind.Birthday ? HolidayIcons.Gift : null);
        NoteBox.Text = initial?.Note ?? "";

        SelectComboByTag(MonthCombo, initialMonth);
        SelectComboByTag(DayCombo, initialDay);
        SelectYear(initial?.Year);
        SelectEmoji(initialEmoji);

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    // ===== Combo populators =====

    private void PopulateMonthCombo()
    {
        var names = Loc.Culture.DateTimeFormat.MonthGenitiveNames;
        for (int i = 0; i < 12; i++)
        {
            string n = string.IsNullOrWhiteSpace(names[i])
                ? Loc.Culture.DateTimeFormat.MonthNames[i]
                : names[i];
            if (n.Length > 0) n = char.ToUpper(n[0], Loc.Culture) + n.Substring(1);
            MonthCombo.Items.Add(new ComboBoxItem { Content = n, Tag = (i + 1) });
        }
    }

    private void PopulateDayCombo()
    {
        for (int d = 1; d <= 31; d++)
            DayCombo.Items.Add(new ComboBoxItem
            {
                Content = d.ToString(CultureInfo.InvariantCulture),
                Tag = d
            });
    }

    private void PopulateYearCombo()
    {
        // First item = "Без року" (Tag=null). Then years current..1900 descending so
        // the most recent years are reachable without scrolling far.
        YearCombo.Items.Add(new ComboBoxItem
        {
            Content = Loc.Get("Holidays_Year_None"),
            Tag = null
        });
        for (int y = DateTime.Today.Year; y >= 1900; y--)
            YearCombo.Items.Add(new ComboBoxItem
            {
                Content = y.ToString(CultureInfo.InvariantCulture),
                Tag = y
            });
    }

    /// <summary>
    /// Populates the icon palette: 1 "no icon" item (Tag = "") followed by the 10
    /// glyph IDs from <see cref="HolidayIcons.Ids"/>. Each glyph is rendered as a 22×22
    /// Path filled with PrimaryTextBrush so it follows the theme.
    /// </summary>
    private void PopulateIconPalette()
    {
        IconPalette.Items.Add(BuildNoneItem());
        foreach (var id in HolidayIcons.Ids)
            IconPalette.Items.Add(BuildIconItem(id));
    }

    private ListBoxItem BuildNoneItem()
    {
        var glyph = new TextBlock
        {
            Text = "×",
            FontSize = 22,
            FontWeight = FontWeights.Light,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.Get("Holidays_Icon_None")
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        return new ListBoxItem { Tag = "", Content = glyph };
    }

    private ListBoxItem BuildIconItem(string id)
    {
        var geom = HolidayIcons.GetGeometry(id);
        var path = new Path
        {
            Data = geom,
            Width = 22, Height = 22,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        path.SetResourceReference(Path.FillProperty, "PrimaryTextBrush");
        return new ListBoxItem { Tag = id, Content = path };
    }

    // ===== Selection helpers =====

    private static void SelectComboByTag(ComboBox combo, int tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem cbi && cbi.Tag is int t && t == tag)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = -1;
    }

    private void SelectYear(int? year)
    {
        if (year is null)
        {
            YearCombo.SelectedIndex = 0;
            return;
        }
        for (int i = 0; i < YearCombo.Items.Count; i++)
        {
            if (YearCombo.Items[i] is ComboBoxItem cbi && cbi.Tag is int y && y == year.Value)
            {
                YearCombo.SelectedIndex = i;
                return;
            }
        }
        YearCombo.SelectedIndex = 0;
    }

    private void SelectEmoji(string? emoji)
    {
        string target = emoji ?? "";
        for (int i = 0; i < IconPalette.Items.Count; i++)
        {
            if (IconPalette.Items[i] is ListBoxItem lbi && (lbi.Tag as string) == target)
            {
                IconPalette.SelectedIndex = i;
                return;
            }
        }
        IconPalette.SelectedIndex = 0; // fall back to "none"
    }

    // When the month changes, clamp the selected day to that month's max day count.
    private void MonthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonthCombo.SelectedItem is not ComboBoxItem cbi || cbi.Tag is not int month) return;
        if (DayCombo.SelectedItem is not ComboBoxItem dcbi || dcbi.Tag is not int day) return;

        int year = DateTime.Today.Year;
        if (YearCombo.SelectedItem is ComboBoxItem ycbi && ycbi.Tag is int y) year = y;

        int maxDay = DateTime.DaysInMonth(year, month);
        if (day > maxDay)
            SelectComboByTag(DayCombo, maxDay);
    }

    // No-op: just keeps the selection sticky. Real value is read in Ok_Click.
    private void IconPalette_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    // ===== Buttons =====

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (MonthCombo.SelectedItem is not ComboBoxItem mcbi || mcbi.Tag is not int month) return;
        if (DayCombo.SelectedItem is not ComboBoxItem dcbi || dcbi.Tag is not int day) return;

        int? year = null;
        if (YearCombo.SelectedItem is ComboBoxItem ycbi && ycbi.Tag is int y) year = y;

        // Day clamping - day combo lists 1..31 but Feb/Apr/Jun/Sep/Nov max out lower.
        int refYear = year ?? DateTime.Today.Year;
        int maxDay = DateTime.DaysInMonth(refYear, month);
        if (day > maxDay) day = maxDay;

        var name = (NameBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name)) name = Loc.Get("Holidays_NoName");

        string? emoji = null;
        if (IconPalette.SelectedItem is ListBoxItem lbi && lbi.Tag is string s && s.Length > 0)
            emoji = s;

        var noteTrimmed = (NoteBox.Text ?? "").Trim();
        string? note = string.IsNullOrEmpty(noteTrimmed) ? null : noteTrimmed;

        ResultAction = HolidayEditorAction.Saved;
        ResultData = new HolidayEditorData
        {
            Month = month,
            Day = day,
            Year = year,
            Name = name,
            Emoji = emoji,
            Note = note
        };
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEdit) return;

        string label = string.IsNullOrWhiteSpace(_originalNameForDelete)
            ? Loc.Get("Holidays_NoName")
            : _originalNameForDelete;

        // Different confirm message per category - the wording maps to the noun.
        string confirmKey = _kind == HolidayEditorKind.Birthday
            ? "Confirm_DeleteBirthday"
            : "Confirm_DeleteHoliday";

        var ans = DarkMessageBox.Show(this,
            Loc.Format(confirmKey, label),
            Loc.Get("Title_Confirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (ans != MessageBoxResult.Yes) return;

        ResultAction = HolidayEditorAction.Removed;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = HolidayEditorAction.Cancelled;
        try { DialogResult = false; } catch { }
        Close();
    }
}
