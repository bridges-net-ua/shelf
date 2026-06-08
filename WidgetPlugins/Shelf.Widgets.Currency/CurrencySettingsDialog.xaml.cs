using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Shelf.Sdk;

namespace Shelf.Widgets.Currency;

public partial class CurrencySettingsDialog : Window
{
    public List<string> ResultCurrencies { get; private set; }
    public bool ResultShowChange { get; private set; }

    public CurrencySettingsDialog(List<string> current, bool showChange)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        ResultCurrencies = new List<string>(current ?? new List<string>());

        foreach (var ccy in CurrencyWidget.PopularCurrencies)
        {
            var cb = new CheckBox
            {
                Content = ccy + " (" + Loc.Get("Currency_Ccy_" + ccy) + ")",
                Tag = ccy,
                IsChecked = ResultCurrencies.Contains(ccy),
                Margin = new Thickness(0, 3, 0, 3)
            };
            CurrencyHost.Children.Add(cb);
        }

        ShowChangeBox.IsChecked = showChange;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Keep the canonical PopularCurrencies order for the checked items.
        ResultCurrencies = CurrencyHost.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag)
            .ToList();
        ResultShowChange = ShowChangeBox.IsChecked == true;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
