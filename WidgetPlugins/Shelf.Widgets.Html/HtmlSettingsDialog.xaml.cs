using System.Windows;
using System.Windows.Controls.Primitives;
using Shelf.Sdk;

namespace Shelf.Widgets.Html;

public partial class HtmlSettingsDialog : Window
{
    public string ResultHtml { get; private set; }
    public int ResultHeight { get; private set; }

    public HtmlSettingsDialog(string html, int height)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        ResultHtml = html ?? "";
        ResultHeight = height;
        CodeBox.Text = ResultHtml;
        HeightSlider.Value = height;
        HeightValueRun.Text = height.ToString();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HeightValueRun != null)
            HeightValueRun.Text = ((int)e.NewValue).ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultHtml = CodeBox.Text ?? "";
        ResultHeight = (int)HeightSlider.Value;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
