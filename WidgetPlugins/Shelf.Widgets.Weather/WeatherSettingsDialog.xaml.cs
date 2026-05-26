using System.Windows;
using System.Windows.Input;
using Shelf.Sdk;

namespace Shelf.Widgets.Weather;

public partial class WeatherSettingsDialog : Window
{
    public string ResultCity { get; private set; }

    public WeatherSettingsDialog(string city)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        ResultCity = city ?? "";
        CityBox.Text = ResultCity;
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
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
