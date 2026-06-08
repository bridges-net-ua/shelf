using System.Windows;
using System.Windows.Input;
using Shelf.Sdk;

namespace Shelf.Widgets.SunMoon;

public partial class SunMoonSettingsDialog : Window
{
    public string ResultCity { get; private set; }
    public bool ResultShowSun { get; private set; }
    public bool ResultShowDayLength { get; private set; }
    public bool ResultShowGoldenBlue { get; private set; }
    public bool ResultShowMoon { get; private set; }
    public bool ResultShowMoonExtra { get; private set; }

    public SunMoonSettingsDialog(SunMoonWidget.WidgetState state)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        ResultCity = state.City ?? "";
        CityBox.Text = ResultCity;
        OptSun.IsChecked = state.ShowSun;
        OptDayLength.IsChecked = state.ShowDayLength;
        OptGoldenBlue.IsChecked = state.ShowGoldenBlue;
        OptMoon.IsChecked = state.ShowMoon;
        OptMoonExtra.IsChecked = state.ShowMoonExtra;

        Loaded += (_, _) =>
        {
            CityBox.Focus();
            CityBox.SelectAll();
        };
    }

    private void CityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Ok_Click(sender, e);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultCity = (CityBox.Text ?? "").Trim();
        ResultShowSun = OptSun.IsChecked == true;
        ResultShowDayLength = OptDayLength.IsChecked == true;
        ResultShowGoldenBlue = OptGoldenBlue.IsChecked == true;
        ResultShowMoon = OptMoon.IsChecked == true;
        ResultShowMoonExtra = OptMoonExtra.IsChecked == true;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
