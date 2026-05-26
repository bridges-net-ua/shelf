using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Holidays;

public partial class HolidaysWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Holidays_Name");

    // ===== Model =====

    // Persisted shape of a user-added date. Year is optional - when set, the widget
    // appends " (N р.)" with the anniversary count where N = today.Year - Year.
    // Emoji is an optional ID from HolidayIcons - drawn as a mono Path on each row.
    public class UserHoliday
    {
        public int Month { get; set; }
        public int Day { get; set; }
        public string Name { get; set; } = "";
        public int? Year { get; set; }
        public string? Emoji { get; set; }
        public string? Note { get; set; }

        // Used by the settings dialog's ListBox (DisplayMemberPath="Display").
        [System.Text.Json.Serialization.JsonIgnore]
        public string Display
        {
            get
            {
                string label = string.IsNullOrWhiteSpace(Name) ? Loc.Get("Holidays_NoName") : Name;
                string suffix = "";
                if (Year is int y)
                {
                    int years = DateTime.Today.Year - y;
                    if (years >= 1) suffix = " " + Loc.Format("Holidays_AnniversaryYears", years);
                }
                return $"{Day:00}.{Month:00} · {label}{suffix}";
            }
        }
    }

    // Parallel structure to UserHoliday for the "birthdays" category. Same shape, but
    // semantically different: Year is the person's birth year, and the default Emoji
    // for new birthdays is "gift".
    public class Birthday
    {
        public int Month { get; set; }
        public int Day { get; set; }
        public string Name { get; set; } = "";
        public int? Year { get; set; }
        public string? Emoji { get; set; } = HolidayIcons.Gift;
        public string? Note { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string Display
        {
            get
            {
                string label = string.IsNullOrWhiteSpace(Name) ? Loc.Get("Holidays_NoName") : Name;
                string suffix = "";
                if (Year is int y)
                {
                    int years = DateTime.Today.Year - y;
                    if (years >= 1) suffix = " " + Loc.Format("Holidays_AnniversaryYears", years);
                }
                return $"{Day:00}.{Month:00} · {label}{suffix}";
            }
        }
    }

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public List<UserHoliday> UserHolidays { get; set; } = new();
        public List<Birthday> Birthdays { get; set; } = new();

        // Display behavior (added later - older states without these fields get defaults
        // that reproduce the legacy 3-day yesterday/today/tomorrow render).
        public bool ShowPast { get; set; } = true;
        public bool ShowFuture { get; set; } = true;
        public int DaysBefore { get; set; } = 1;
        public int DaysAfter { get; set; } = 1;
        // 0 = no limit. Otherwise show first N items per sub-block, then a "+X" badge
        // whose ToolTip lists the hidden ones. Limit is applied independently to the
        // holidays sub-block and to the birthdays sub-block.
        public int MaxPerDay { get; set; } = 0;

        // Category toggles. Both true by default - new installs see holidays as before,
        // birthdays start invisible only because the list is empty until the user adds.
        public bool ShowHolidays { get; set; } = true;
        public bool ShowBirthdays { get; set; } = true;
    }

    // ===== Fields =====

    private WidgetState _state = new();
    private DispatcherTimer? _refreshTimer;
    private DateTime _currentDay = DateTime.MinValue;

    private bool _isEditingTitle;
    private bool _editTitleCanceled;

    // ===== IWidget =====

    public string Id => "holidays";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Holidays_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public HolidaysWidget()
    {
        InitializeComponent();

        Loaded += (_, _) => ApplyState();
        Unloaded += (_, _) => _refreshTimer?.Stop();
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new HolidaysSettingsDialog(_state)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state = dlg.ResultState;
            WidgetServices.RequestSaveStates();
            RefreshDisplay(force: true);
        }
    }

    public string SaveState() => JsonSerializer.Serialize(_state);

    public void LoadState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<WidgetState>(json);
            if (loaded != null) _state = loaded;
        }
        catch { }
    }

    // ===== UI =====

    private void ApplyState()
    {
        TitleText.Text = InstanceLabel;

        if (_refreshTimer == null)
        {
            // Poll every 15 minutes - enough to roll over a few minutes after midnight.
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _refreshTimer.Tick += (_, _) => RefreshDisplay(force: false);
        }
        _refreshTimer.Stop();
        _refreshTimer.Start();

        RefreshDisplay(force: true);
    }

    private void RefreshDisplay(bool force)
    {
        var today = DateTime.Today;
        if (!force && today == _currentDay) return;
        _currentDay = today;

        // User holidays mapped to Holiday struct (with optional Emoji carried through).
        // Empty list when the holidays category is toggled off - GetForDate then returns
        // only built-in dates, which we also gate via ShowHolidays just below.
        var userHolidays = _state.UserHolidays
            .Select(u => new Holiday
            {
                Month = u.Month,
                Day = u.Day,
                Name = u.Name,
                Year = u.Year,
                Emoji = u.Emoji,
                Note = u.Note,
                Type = HolidayType.User
            })
            .ToList();

        var ci = Loc.Culture;
        ContentHost.Children.Clear();

        // Range: [-DaysBefore .. +DaysAfter], gated by ShowPast/ShowFuture.
        int start = _state.ShowPast ? -Math.Max(0, _state.DaysBefore) : 0;
        int end = _state.ShowFuture ? Math.Max(0, _state.DaysAfter) : 0;

        bool needSep = false;
        for (int offset = start; offset <= end; offset++)
        {
            var date = today.AddDays(offset);

            var holidaysOfDay = _state.ShowHolidays
                ? HolidaysData.GetForDate(date, userHolidays)
                : new List<Holiday>(0);

            var birthdaysOfDay = _state.ShowBirthdays
                ? _state.Birthdays
                    .Where(b => b.Month == date.Month && b.Day == date.Day)
                    .ToList()
                : new List<Birthday>(0);

            if (holidaysOfDay.Count == 0 && birthdaysOfDay.Count == 0) continue;

            if (needSep) ContentHost.Children.Add(BuildSeparator());
            ContentHost.Children.Add(BuildDayBlock(offset, date, holidaysOfDay, birthdaysOfDay, ci, today));
            needSep = true;
        }
    }

    private static Border BuildSeparator()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 9, 0, 8) };
        b.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        return b;
    }

    // Thinner separator between the two sub-blocks of the same day (holidays / birthdays).
    private static Border BuildSubBlockSeparator()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 4), Opacity = 0.6 };
        b.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        return b;
    }

    // One day block: a label line followed by 0..2 sub-blocks (holidays, birthdays).
    // The "today" block (offset == 0) uses larger, bolder type to stand out. The
    // per-day limit is applied to each sub-block independently.
    private UIElement BuildDayBlock(int offset, DateTime date,
        List<Holiday> holidays, List<Birthday> birthdays,
        CultureInfo ci, DateTime today)
    {
        bool emphasised = offset == 0;
        var sp = new StackPanel();

        string label = offset switch
        {
            0 => Loc.Get("Holidays_Today"),
            -1 => Loc.Format("Holidays_DayLabelDate",
                             Loc.Get("Holidays_Yesterday"),
                             date.ToString("d MMMM", ci)),
            1 => Loc.Format("Holidays_DayLabelDate",
                            Loc.Get("Holidays_Tomorrow"),
                            date.ToString("d MMMM", ci)),
            _ => $"{date.ToString("dddd", ci).ToUpper(ci)} · {date.ToString("d MMMM", ci)}"
        };

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = emphasised ? 11 : 10,
            FontWeight = FontWeights.SemiBold
        };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        sp.Children.Add(labelText);

        // Holiday sub-block (built-in + user holidays). Emoji and Note are optional.
        bool renderedAnything = false;
        if (holidays.Count > 0)
        {
            AppendSubBlock(sp,
                holidays.Select(h => (FormatHolidayName(h, today), h.Emoji, h.Note)).ToList(),
                emphasised);
            renderedAnything = true;
        }

        if (birthdays.Count > 0)
        {
            if (renderedAnything) sp.Children.Add(BuildSubBlockSeparator());
            AppendSubBlock(sp,
                birthdays.Select(b => (FormatBirthdayName(b, today), b.Emoji, b.Note)).ToList(),
                emphasised);
        }

        return sp;
    }

    // Renders a list of (text, emoji?, note?) entries with the per-day limit applied.
    // First row gets a small top margin so it sits below the day label / separator.
    private void AppendSubBlock(StackPanel host,
                                List<(string Text, string? Emoji, string? Note)> entries,
                                bool emphasised)
    {
        int max = _state.MaxPerDay;
        bool needBadge = max > 0 && entries.Count > max;
        var visible = needBadge ? entries.Take(max).ToList() : entries;
        var hidden = needBadge ? entries.Skip(max).ToList() : new List<(string, string?, string?)>(0);

        for (int i = 0; i < visible.Count; i++)
        {
            host.Children.Add(BuildEntryRow(
                visible[i].Text, visible[i].Emoji, visible[i].Note,
                emphasised, topMargin: i == 0 ? 4 : 2));
        }

        if (needBadge)
        {
            var more = new TextBlock
            {
                Text = Loc.Format("Holidays_More_Format", hidden.Count),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0),
                Cursor = System.Windows.Input.Cursors.Help,
                ToolTip = string.Join("\n", hidden.Select(h =>
                    "• " + h.Item1 + (string.IsNullOrWhiteSpace(h.Item3) ? "" : $" — {h.Item3}")))
            };
            more.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            host.Children.Add(more);
        }
    }

    // One entry row: optional Path icon + text panel (main name + optional second
    // small line with the note). Wrapping works because the icon column is Auto and
    // the text column is *.
    private static UIElement BuildEntryRow(string text, string? emoji, string? note,
                                           bool emphasised, double topMargin)
    {
        string foregroundKey = emphasised ? "PrimaryTextBrush" : "SecondaryTextBrush";
        double fontSize = emphasised ? 14 : 12;
        var fontWeight = emphasised ? FontWeights.SemiBold : FontWeights.Normal;
        var geometry = HolidayIcons.GetGeometry(emoji);

        // Text section: main name + optional note line.
        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var mainText = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap
        };
        mainText.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        textPanel.Children.Add(mainText);

        if (!string.IsNullOrWhiteSpace(note))
        {
            var noteText = new TextBlock
            {
                Text = note,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            };
            noteText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            textPanel.Children.Add(noteText);
        }

        if (geometry == null)
        {
            textPanel.Margin = new Thickness(0, topMargin, 0, 0);
            return textPanel;
        }

        // Icon + text in a 2-column grid so the text wraps cleanly.
        var grid = new Grid { Margin = new Thickness(0, topMargin, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        double iconSize = emphasised ? 16 : 14;
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Width = iconSize,
            Height = iconSize,
            Stretch = System.Windows.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 8, 0)
        };
        path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, foregroundKey);
        Grid.SetColumn(path, 0);
        grid.Children.Add(path);

        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);
        return grid;
    }

    // Appends the "(N р.)" anniversary suffix when a Year was provided and is at least
    // 1 year in the past.
    private static string FormatHolidayName(Holiday h, DateTime today)
    {
        if (h.Year is int y)
        {
            int years = today.Year - y;
            if (years >= 1) return h.Name + " " + Loc.Format("Holidays_AnniversaryYears", years);
        }
        return h.Name;
    }

    private static string FormatBirthdayName(Birthday b, DateTime today)
    {
        if (b.Year is int y)
        {
            int years = today.Year - y;
            if (years >= 1) return b.Name + " " + Loc.Format("Holidays_AnniversaryYears", years);
        }
        return b.Name;
    }

    // ===== Title rename =====

    private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginRenameTitle();
            e.Handled = true;
        }
    }

    private void TitleRenameMenuItem_Click(object sender, RoutedEventArgs e) => BeginRenameTitle();

    private void BeginRenameTitle()
    {
        if (_isEditingTitle) return;
        _isEditingTitle = true;
        _editTitleCanceled = false;
        TitleEdit.Text = string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEdit.Visibility = Visibility.Visible;
        TitleEdit.Focus();
        TitleEdit.SelectAll();
    }

    private void TitleEdit_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitTitleEdit();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _editTitleCanceled = true;
            CommitTitleEdit();
        }
    }

    private void TitleEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isEditingTitle) CommitTitleEdit();
    }

    private void CommitTitleEdit()
    {
        if (!_isEditingTitle) return;
        _isEditingTitle = false;

        if (!_editTitleCanceled)
        {
            var newTitle = TitleEdit.Text.Trim();
            _state.Title = (string.IsNullOrEmpty(newTitle) || newTitle == DefaultTitle)
                ? ""
                : newTitle;
            TitleText.Text = InstanceLabel;
            WidgetServices.RequestSaveStates();
        }

        TitleEdit.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        _editTitleCanceled = false;
    }
}
