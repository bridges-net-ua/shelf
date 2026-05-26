using System.Windows;
using Shelf.Sdk;

namespace Shelf.Widgets.Clock;

public partial class ClockSettingsDialog : Window
{
    public ClockWidget.WidgetState Result { get; private set; }

    public ClockSettingsDialog(ClockWidget.WidgetState current)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        Result = new ClockWidget.WidgetState
        {
            ShowTime = current.ShowTime,
            Use24Hour = current.Use24Hour,
            ShowSeconds = current.ShowSeconds,
            ShowDayOfWeek = current.ShowDayOfWeek,
            DayOfWeekFormat = current.DayOfWeekFormat,
            ShowDate = current.ShowDate,
            DateFormat = current.DateFormat
        };

        CbShowTime.IsChecked = Result.ShowTime;
        Cb24Hour.IsChecked = Result.Use24Hour;
        CbSeconds.IsChecked = Result.ShowSeconds;

        CbShowDow.IsChecked = Result.ShowDayOfWeek;
        RbDowFull.IsChecked = Result.DayOfWeekFormat == DayOfWeekFormat.Full;
        RbDowShort.IsChecked = Result.DayOfWeekFormat == DayOfWeekFormat.Short;

        CbShowDate.IsChecked = Result.ShowDate;
        RbDateLong.IsChecked = Result.DateFormat == DateFormat.LongUkrainian;
        RbDateNumeric.IsChecked = Result.DateFormat == DateFormat.Numeric;
        RbDateIso.IsChecked = Result.DateFormat == DateFormat.Iso;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new ClockWidget.WidgetState
        {
            ShowTime = CbShowTime.IsChecked == true,
            Use24Hour = Cb24Hour.IsChecked == true,
            ShowSeconds = CbSeconds.IsChecked == true,
            ShowDayOfWeek = CbShowDow.IsChecked == true,
            DayOfWeekFormat = RbDowShort.IsChecked == true ? DayOfWeekFormat.Short : DayOfWeekFormat.Full,
            ShowDate = CbShowDate.IsChecked == true,
            DateFormat =
                RbDateNumeric.IsChecked == true ? DateFormat.Numeric :
                RbDateIso.IsChecked == true ? DateFormat.Iso :
                DateFormat.LongUkrainian
        };
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
