using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shelf.Sdk;

namespace Shelf.Widgets.Holidays;

public partial class HolidaysSettingsDialog : Window
{
    /// <summary>
    /// Populated when the user confirms the dialog with OK. The widget assigns it to
    /// its own state. On Cancel this stays equal to the original snapshot.
    /// </summary>
    public HolidaysWidget.WidgetState ResultState { get; private set; }

    private readonly HolidaysWidget.WidgetState _initial;
    private readonly ObservableCollection<HolidaysWidget.UserHoliday> _items;
    private readonly ObservableCollection<HolidaysWidget.Birthday> _birthdayItems;

    public HolidaysSettingsDialog(HolidaysWidget.WidgetState initial)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        _initial = initial;
        // Deep copies so Cancel discards every edit.
        _items = new ObservableCollection<HolidaysWidget.UserHoliday>(
            initial.UserHolidays
                   .OrderBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.Name)
                   .Select(CloneUserHoliday));
        _birthdayItems = new ObservableCollection<HolidaysWidget.Birthday>(
            initial.Birthdays
                   .OrderBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.Name)
                   .Select(CloneBirthday));
        ResultState = initial;

        // ===== Settings tab =====
        CbShowHolidays.IsChecked = initial.ShowHolidays;
        CbShowBirthdays.IsChecked = initial.ShowBirthdays;
        CbShowPast.IsChecked = initial.ShowPast;
        CbShowFuture.IsChecked = initial.ShowFuture;
        DaysBeforeSlider.Value = Math.Clamp(initial.DaysBefore, 1, 14);
        DaysAfterSlider.Value = Math.Clamp(initial.DaysAfter, 1, 14);
        MaxPerDaySlider.Value = Math.Clamp(initial.MaxPerDay, 0, 15);
        UpdateSliderEnableStates();
        UpdateDaysBeforeText();
        UpdateDaysAfterText();
        UpdateMaxPerDayText();

        // ===== Holidays tab =====
        HolidayList.ItemsSource = _items;
        UpdateEditHolidayButton();

        // ===== Birthdays tab =====
        BirthdayList.ItemsSource = _birthdayItems;
        UpdateEditBirthdayButton();
    }

    private static HolidaysWidget.UserHoliday CloneUserHoliday(HolidaysWidget.UserHoliday h)
        => new()
        {
            Month = h.Month,
            Day = h.Day,
            Name = h.Name,
            Year = h.Year,
            Emoji = h.Emoji,
            Note = h.Note
        };

    private static HolidaysWidget.Birthday CloneBirthday(HolidaysWidget.Birthday b)
        => new()
        {
            Month = b.Month,
            Day = b.Day,
            Name = b.Name,
            Year = b.Year,
            Emoji = b.Emoji,
            Note = b.Note
        };

    // ===== Settings tab handlers =====

    private void CbShowPast_Click(object sender, RoutedEventArgs e) => UpdateSliderEnableStates();
    private void CbShowFuture_Click(object sender, RoutedEventArgs e) => UpdateSliderEnableStates();

    private void UpdateSliderEnableStates()
    {
        DaysBeforeSlider.IsEnabled = CbShowPast.IsChecked == true;
        DaysAfterSlider.IsEnabled = CbShowFuture.IsChecked == true;
    }

    private void DaysBeforeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateDaysBeforeText();
    private void DaysAfterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateDaysAfterText();
    private void MaxPerDaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateMaxPerDayText();

    private void UpdateDaysBeforeText()
    {
        if (DaysBeforeValueRun != null)
            DaysBeforeValueRun.Text = ((int)DaysBeforeSlider.Value).ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateDaysAfterText()
    {
        if (DaysAfterValueRun != null)
            DaysAfterValueRun.Text = ((int)DaysAfterSlider.Value).ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateMaxPerDayText()
    {
        if (MaxPerDayValueRun == null) return;
        int v = (int)MaxPerDaySlider.Value;
        MaxPerDayValueRun.Text = v == 0
            ? Loc.Get("Holidays_MaxPerDay_Unlimited")
            : v.ToString(CultureInfo.InvariantCulture);
    }

    // ===== Holidays tab handlers =====

    private HolidaysWidget.UserHoliday? SelectedHoliday =>
        HolidayList.SelectedItem as HolidaysWidget.UserHoliday;

    private void HolidayList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateEditHolidayButton();

    private void UpdateEditHolidayButton()
        => EditHolidayButton.IsEnabled = SelectedHoliday != null;

    private void HolidayList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedHoliday != null) EditHolidayInternal(SelectedHoliday);
    }

    private void AddHoliday_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor(HolidayEditorKind.Holiday, existingData: null, data =>
        {
            var h = new HolidaysWidget.UserHoliday
            {
                Month = data.Month,
                Day = data.Day,
                Year = data.Year,
                Name = data.Name,
                Emoji = data.Emoji,
                Note = data.Note
            };
            _items.Add(h);
            HolidayList.SelectedItem = h;
            HolidayList.ScrollIntoView(h);
        });
    }

    private void EditHoliday_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedHoliday is { } h) EditHolidayInternal(h);
    }

    private void EditHolidayInternal(HolidaysWidget.UserHoliday existing)
    {
        var data = new HolidayEditorData
        {
            Month = existing.Month,
            Day = existing.Day,
            Year = existing.Year,
            Name = existing.Name,
            Emoji = existing.Emoji,
            Note = existing.Note
        };

        OpenEditor(HolidayEditorKind.Holiday, data,
            onSaved: result =>
            {
                int idx = _items.IndexOf(existing);
                var updated = new HolidaysWidget.UserHoliday
                {
                    Month = result.Month,
                    Day = result.Day,
                    Year = result.Year,
                    Name = result.Name,
                    Emoji = result.Emoji,
                    Note = result.Note
                };
                if (idx >= 0) _items[idx] = updated;
                HolidayList.SelectedItem = updated;
            },
            onRemoved: () =>
            {
                int idx = _items.IndexOf(existing);
                _items.Remove(existing);
                if (_items.Count > 0)
                    HolidayList.SelectedIndex = idx < _items.Count ? idx : _items.Count - 1;
                else
                    UpdateEditHolidayButton();
            });
    }

    // ===== Birthdays tab handlers =====

    private HolidaysWidget.Birthday? SelectedBirthday =>
        BirthdayList.SelectedItem as HolidaysWidget.Birthday;

    private void BirthdayList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateEditBirthdayButton();

    private void UpdateEditBirthdayButton()
        => EditBirthdayButton.IsEnabled = SelectedBirthday != null;

    private void BirthdayList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedBirthday != null) EditBirthdayInternal(SelectedBirthday);
    }

    private void AddBirthday_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor(HolidayEditorKind.Birthday, existingData: null, data =>
        {
            var b = new HolidaysWidget.Birthday
            {
                Month = data.Month,
                Day = data.Day,
                Year = data.Year,
                Name = data.Name,
                Emoji = data.Emoji,
                Note = data.Note
            };
            _birthdayItems.Add(b);
            BirthdayList.SelectedItem = b;
            BirthdayList.ScrollIntoView(b);
        });
    }

    private void EditBirthday_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBirthday is { } b) EditBirthdayInternal(b);
    }

    private void EditBirthdayInternal(HolidaysWidget.Birthday existing)
    {
        var data = new HolidayEditorData
        {
            Month = existing.Month,
            Day = existing.Day,
            Year = existing.Year,
            Name = existing.Name,
            Emoji = existing.Emoji,
            Note = existing.Note
        };

        OpenEditor(HolidayEditorKind.Birthday, data,
            onSaved: result =>
            {
                int idx = _birthdayItems.IndexOf(existing);
                var updated = new HolidaysWidget.Birthday
                {
                    Month = result.Month,
                    Day = result.Day,
                    Year = result.Year,
                    Name = result.Name,
                    Emoji = result.Emoji,
                    Note = result.Note
                };
                if (idx >= 0) _birthdayItems[idx] = updated;
                BirthdayList.SelectedItem = updated;
            },
            onRemoved: () =>
            {
                int idx = _birthdayItems.IndexOf(existing);
                _birthdayItems.Remove(existing);
                if (_birthdayItems.Count > 0)
                    BirthdayList.SelectedIndex = idx < _birthdayItems.Count ? idx : _birthdayItems.Count - 1;
                else
                    UpdateEditBirthdayButton();
            });
    }

    // ===== Editor dispatch (shared between Holidays and Birthdays) =====

    private void OpenEditor(HolidayEditorKind kind, HolidayEditorData? existingData,
                            Action<HolidayEditorData> onSaved,
                            Action? onRemoved = null)
    {
        var dlg = new HolidayEditorWindow(kind, existingData)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dlg.ShowDialog();

        if (dlg.ResultAction == HolidayEditorAction.Saved && dlg.ResultData != null)
            onSaved(dlg.ResultData);
        else if (dlg.ResultAction == HolidayEditorAction.Removed)
            onRemoved?.Invoke();
    }

    // ===== OK / Cancel =====

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var holidays = _items
            .Select(NormalizeHoliday)
            .OrderBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.Name)
            .ToList();
        var birthdays = _birthdayItems
            .Select(NormalizeBirthday)
            .OrderBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.Name)
            .ToList();

        ResultState = new HolidaysWidget.WidgetState
        {
            Title = _initial.Title,
            UserHolidays = holidays,
            Birthdays = birthdays,
            ShowPast = CbShowPast.IsChecked == true,
            ShowFuture = CbShowFuture.IsChecked == true,
            DaysBefore = (int)DaysBeforeSlider.Value,
            DaysAfter = (int)DaysAfterSlider.Value,
            MaxPerDay = (int)MaxPerDaySlider.Value,
            ShowHolidays = CbShowHolidays.IsChecked == true,
            ShowBirthdays = CbShowBirthdays.IsChecked == true
        };
        try { DialogResult = true; } catch { }
        Close();
    }

    private static HolidaysWidget.UserHoliday NormalizeHoliday(HolidaysWidget.UserHoliday h)
    {
        int m = Math.Clamp(h.Month, 1, 12);
        int d = Math.Clamp(h.Day, 1, 31);
        int refYear = h.Year ?? DateTime.Today.Year;
        int maxDay = DateTime.DaysInMonth(refYear, m);
        if (d > maxDay) d = maxDay;
        var name = (h.Name ?? "").Trim();
        if (string.IsNullOrEmpty(name)) name = Loc.Get("Holidays_NoName");
        return new HolidaysWidget.UserHoliday
        {
            Month = m, Day = d, Name = name, Year = h.Year, Emoji = h.Emoji, Note = h.Note
        };
    }

    private static HolidaysWidget.Birthday NormalizeBirthday(HolidaysWidget.Birthday b)
    {
        int m = Math.Clamp(b.Month, 1, 12);
        int d = Math.Clamp(b.Day, 1, 31);
        int refYear = b.Year ?? DateTime.Today.Year;
        int maxDay = DateTime.DaysInMonth(refYear, m);
        if (d > maxDay) d = maxDay;
        var name = (b.Name ?? "").Trim();
        if (string.IsNullOrEmpty(name)) name = Loc.Get("Holidays_NoName");
        return new HolidaysWidget.Birthday
        {
            Month = m, Day = d, Name = name, Year = b.Year, Emoji = b.Emoji, Note = b.Note
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }

    // ===== Export to file (Holidays / Birthdays) =====

    // Indented JSON, UTF-8 without BOM. UnsafeRelaxedJsonEscaping keeps Ukrainian
    // characters readable (no \u escapes).
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private void ExportHolidays_Click(object sender, RoutedEventArgs e)
    {
        DoExport(
            defaultBasename: Loc.Format("Holidays_Export_HolidaysDefault",
                                        DateTime.Today.ToString("yyyy-MM-dd",
                                                                CultureInfo.InvariantCulture)),
            warnOnTxt: _items.Any(h => !string.IsNullOrEmpty(h.Emoji)),
            serializeJson: () => JsonSerializer.Serialize(_items.ToList(), _jsonOpts),
            serializeTxt: () => SerializeHolidaysTxt(_items));
    }

    private void ExportBirthdays_Click(object sender, RoutedEventArgs e)
    {
        DoExport(
            defaultBasename: Loc.Format("Holidays_Export_BirthdaysDefault",
                                        DateTime.Today.ToString("yyyy-MM-dd",
                                                                CultureInfo.InvariantCulture)),
            warnOnTxt: _birthdayItems.Any(b => !string.IsNullOrEmpty(b.Emoji)),
            serializeJson: () => JsonSerializer.Serialize(_birthdayItems.ToList(), _jsonOpts),
            serializeTxt: () => SerializeBirthdaysTxt(_birthdayItems));
    }

    // Common export driver - SaveFileDialog with JSON/TXT filter, optional warning
    // before lossy TXT export, atomic write with UTF-8 (no BOM).
    private void DoExport(string defaultBasename, bool warnOnTxt,
                          Func<string> serializeJson, Func<string> serializeTxt)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.Get("Holidays_Export_Title"),
            Filter = "JSON (*.json)|*.json|Text (*.txt)|*.txt",
            FilterIndex = 1,
            FileName = defaultBasename,
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dlg.ShowDialog(this) != true) return;

        bool isTxt = dlg.FilterIndex == 2;

        if (isTxt && warnOnTxt)
        {
            var ans = DarkMessageBox.Show(this,
                Loc.Get("Holidays_Export_TxtIconLossWarning"),
                Loc.Get("Title_Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ans != MessageBoxResult.Yes) return;
        }

        try
        {
            string content = isTxt ? serializeTxt() : serializeJson();
            File.WriteAllText(dlg.FileName, content, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(this,
                Loc.Format("Holidays_Export_Error", ex.Message),
                Loc.Get("Holidays_Export_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // TXT format for Holidays - "DD/MM Name" or "DD/MM Name (note)". The current
    // Holiday TXT parser does not understand a year column, so we drop the Year
    // field for TXT export to keep the file round-trippable through TXT import.
    // (JSON export preserves Year, of course.)
    private static string SerializeHolidaysTxt(IEnumerable<HolidaysWidget.UserHoliday> items)
    {
        var sb = new StringBuilder();
        foreach (var h in items.OrderBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.Name))
        {
            sb.Append(h.Day.ToString("00", CultureInfo.InvariantCulture));
            sb.Append('/');
            sb.Append(h.Month.ToString("00", CultureInfo.InvariantCulture));
            sb.Append(' ').Append(h.Name);
            if (!string.IsNullOrWhiteSpace(h.Note))
                sb.Append(" (").Append(h.Note).Append(')');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // TXT format for Birthdays - same as Holidays but with optional year.
    private static string SerializeBirthdaysTxt(IEnumerable<HolidaysWidget.Birthday> items)
    {
        var sb = new StringBuilder();
        foreach (var b in items.OrderBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.Name))
        {
            sb.Append(b.Day.ToString("00", CultureInfo.InvariantCulture));
            sb.Append('/');
            sb.Append(b.Month.ToString("00", CultureInfo.InvariantCulture));
            if (b.Year is int y)
            {
                sb.Append('/');
                sb.Append(y.ToString("0000", CultureInfo.InvariantCulture));
            }
            sb.Append(' ').Append(b.Name);
            if (!string.IsNullOrWhiteSpace(b.Note))
                sb.Append(" (").Append(b.Note).Append(')');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static bool LooksLikeJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        return t.Length > 0 && (t[0] == '[' || t[0] == '{');
    }

    // ===== Import from .txt file (Holidays tab) =====

    private static readonly Regex _lineRegex =
        new(@"^\s*(\d{1,2})\s*[/\.\-]\s*(\d{1,2})\s+(.+?)\s*$", RegexOptions.Compiled);

    // Trailing "(...)" is treated as a separate Note field. Inner brackets like
    // "Olya (mom's) Demchenko" stay in the name because the regex anchors to end of
    // string (re-uses CharClass [^()] so nested brackets fall through to the trailing
    // case only if they are actually the last group).
    private static readonly Regex _trailingNoteRegex =
        new(@"^(.+?)\s*\(([^()]*)\)\s*$", RegexOptions.Compiled);

    private static (string Name, string? Note) SplitNameAndNote(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (raw, null);
        var m = _trailingNoteRegex.Match(raw);
        if (!m.Success) return (raw.Trim(), null);
        var name = m.Groups[1].Value.Trim();
        var note = m.Groups[2].Value.Trim();
        return (name, string.IsNullOrEmpty(note) ? null : note);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.Get("Holidays_Import_Title"),
            Filter = "Holidays (*.json;*.txt)|*.json;*.txt|JSON (*.json)|*.json|Text (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog(this) != true) return;

        string text;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            text = DecodeAuto(bytes);
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(this,
                Loc.Format("Holidays_Import_Error", ex.Message),
                Loc.Get("Holidays_Import_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Auto-detect format: JSON looks like "[...]" / "{...}", else fall back to TXT.
        if (LooksLikeJson(text))
        {
            ImportHolidaysJson(text);
            return;
        }

        var seen = new HashSet<string>(_items.Select(NormalizeKey));
        int imported = 0, dupes = 0, bad = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var match = _lineRegex.Match(line);
            if (!match.Success) { bad++; continue; }

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int day) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int month))
            {
                bad++; continue;
            }

            string rawTail = match.Groups[3].Value.Trim();
            var (name, note) = SplitNameAndNote(rawTail);

            if (day < 1 || day > 31 || month < 1 || month > 12 || name.Length == 0)
            {
                bad++; continue;
            }

            string key = NormalizeKey(month, day, name);
            if (!seen.Add(key)) { dupes++; continue; }

            _items.Add(new HolidaysWidget.UserHoliday
            {
                Month = month,
                Day = day,
                Name = name,
                Note = note
            });
            imported++;
        }

        if (imported > 0)
        {
            HolidayList.SelectedIndex = _items.Count - 1;
            HolidayList.ScrollIntoView(_items[_items.Count - 1]);
        }

        DarkMessageBox.Show(this,
            Loc.Format("Holidays_Import_Summary", imported, dupes, bad),
            Loc.Get("Holidays_Import_SummaryTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string NormalizeKey(HolidaysWidget.UserHoliday h)
        => NormalizeKey(h.Month, h.Day, h.Name ?? "");

    private static string NormalizeKey(int month, int day, string name)
        => $"{month:00}-{day:00}-{name.Trim().ToLowerInvariant()}";

    // ===== Import birthdays from .txt file =====

    // Accepts both "DD/MM Name" (no year) and "DD/MM/YYYY Name" (with birth year).
    // Year separator can be /, . or - and matches the day/month separator independently.
    // Group 3 (year) is optional - if absent, the birthday is imported without a year.
    private static readonly Regex _birthdayLineRegex =
        new(@"^\s*(\d{1,2})\s*[/\.\-]\s*(\d{1,2})(?:\s*[/\.\-]\s*(\d{4}))?\s+(.+?)\s*$",
            RegexOptions.Compiled);

    private void ImportBirthdays_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.Get("Holidays_Import_Title"),
            Filter = "Birthdays (*.json;*.txt)|*.json;*.txt|JSON (*.json)|*.json|Text (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog(this) != true) return;

        string text;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            text = DecodeAuto(bytes);
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(this,
                Loc.Format("Holidays_Import_Error", ex.Message),
                Loc.Get("Holidays_Import_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Auto-detect format: JSON looks like "[...]" / "{...}", else fall back to TXT.
        if (LooksLikeJson(text))
        {
            ImportBirthdaysJson(text);
            return;
        }

        var seen = new HashSet<string>(_birthdayItems.Select(NormalizeBirthdayKey));
        int currentYear = DateTime.Today.Year;
        int imported = 0, dupes = 0, bad = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var match = _birthdayLineRegex.Match(line);
            if (!match.Success) { bad++; continue; }

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int day) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int month))
            {
                bad++; continue;
            }

            // Year is optional. If present it must be in [1900..currentYear] - editor
            // applies the same range, and a future birth year makes no sense.
            int? year = null;
            if (match.Groups[3].Success)
            {
                if (!int.TryParse(match.Groups[3].Value, NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out int y) ||
                    y < 1900 || y > currentYear)
                {
                    bad++; continue;
                }
                year = y;
            }

            string rawTail = match.Groups[4].Value.Trim();
            var (name, note) = SplitNameAndNote(rawTail);
            if (day < 1 || day > 31 || month < 1 || month > 12 || name.Length == 0)
            {
                bad++; continue;
            }

            // Day-of-month sanity (Feb 30 etc.). Use the supplied year if any, else
            // currentYear is a safe reference.
            int refYear = year ?? currentYear;
            if (day > DateTime.DaysInMonth(refYear, month))
            {
                bad++; continue;
            }

            string key = NormalizeBirthdayKey(month, day, name);
            if (!seen.Add(key)) { dupes++; continue; }

            _birthdayItems.Add(new HolidaysWidget.Birthday
            {
                Month = month,
                Day = day,
                Year = year,
                Name = name,
                Emoji = HolidayIcons.Gift,
                Note = note
            });
            imported++;
        }

        if (imported > 0)
        {
            BirthdayList.SelectedIndex = _birthdayItems.Count - 1;
            BirthdayList.ScrollIntoView(_birthdayItems[_birthdayItems.Count - 1]);
        }

        DarkMessageBox.Show(this,
            Loc.Format("Holidays_Import_Summary", imported, dupes, bad),
            Loc.Get("Holidays_Import_SummaryTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string NormalizeBirthdayKey(HolidaysWidget.Birthday b)
        => NormalizeBirthdayKey(b.Month, b.Day, b.Name ?? "");

    private static string NormalizeBirthdayKey(int month, int day, string name)
        => $"{month:00}-{day:00}-{name.Trim().ToLowerInvariant()}";

    private static string DecodeAuto(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                                              throwOnInvalidBytes: true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Cp1251.Decode(bytes);
        }
    }

    // ===== JSON import =====

    // JSON branch of Import_Click. Tolerates extra/missing fields. On parse failure
    // shows the standard import-error dialog (NOT a TXT fallback - if the file looks
    // like JSON but is broken, that's a real error to surface).
    private void ImportHolidaysJson(string text)
    {
        List<HolidaysWidget.UserHoliday>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<HolidaysWidget.UserHoliday>>(text, _jsonOpts);
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(this,
                Loc.Format("Holidays_Import_Error", ex.Message),
                Loc.Get("Holidays_Import_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (items == null) return;

        var seen = new HashSet<string>(_items.Select(NormalizeKey));
        int currentYear = DateTime.Today.Year;
        int imported = 0, dupes = 0, bad = 0;

        foreach (var h in items)
        {
            if (h == null) { bad++; continue; }
            int m = h.Month, d = h.Day;
            string name = (h.Name ?? "").Trim();
            if (m < 1 || m > 12 || d < 1 || d > 31 || name.Length == 0) { bad++; continue; }
            int refYear = h.Year ?? currentYear;
            if (d > DateTime.DaysInMonth(refYear, m)) { bad++; continue; }

            string key = NormalizeKey(m, d, name);
            if (!seen.Add(key)) { dupes++; continue; }

            _items.Add(new HolidaysWidget.UserHoliday
            {
                Month = m,
                Day = d,
                Year = h.Year,
                Name = name,
                Emoji = h.Emoji,
                Note = h.Note
            });
            imported++;
        }

        if (imported > 0)
        {
            HolidayList.SelectedIndex = _items.Count - 1;
            HolidayList.ScrollIntoView(_items[_items.Count - 1]);
        }

        DarkMessageBox.Show(this,
            Loc.Format("Holidays_Import_Summary", imported, dupes, bad),
            Loc.Get("Holidays_Import_SummaryTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportBirthdaysJson(string text)
    {
        List<HolidaysWidget.Birthday>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<HolidaysWidget.Birthday>>(text, _jsonOpts);
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(this,
                Loc.Format("Holidays_Import_Error", ex.Message),
                Loc.Get("Holidays_Import_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (items == null) return;

        var seen = new HashSet<string>(_birthdayItems.Select(NormalizeBirthdayKey));
        int currentYear = DateTime.Today.Year;
        int imported = 0, dupes = 0, bad = 0;

        foreach (var b in items)
        {
            if (b == null) { bad++; continue; }
            int m = b.Month, d = b.Day;
            string name = (b.Name ?? "").Trim();
            if (m < 1 || m > 12 || d < 1 || d > 31 || name.Length == 0) { bad++; continue; }
            // For birthdays, the year is allowed [1900..current] - same rule as editor.
            int? year = null;
            if (b.Year is int by)
            {
                if (by < 1900 || by > currentYear) { bad++; continue; }
                year = by;
            }
            int refYear = year ?? currentYear;
            if (d > DateTime.DaysInMonth(refYear, m)) { bad++; continue; }

            string key = NormalizeBirthdayKey(m, d, name);
            if (!seen.Add(key)) { dupes++; continue; }

            _birthdayItems.Add(new HolidaysWidget.Birthday
            {
                Month = m,
                Day = d,
                Year = year,
                Name = name,
                Emoji = b.Emoji,
                Note = b.Note
            });
            imported++;
        }

        if (imported > 0)
        {
            BirthdayList.SelectedIndex = _birthdayItems.Count - 1;
            BirthdayList.ScrollIntoView(_birthdayItems[_birthdayItems.Count - 1]);
        }

        DarkMessageBox.Show(this,
            Loc.Format("Holidays_Import_Summary", imported, dupes, bad),
            Loc.Get("Holidays_Import_SummaryTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
