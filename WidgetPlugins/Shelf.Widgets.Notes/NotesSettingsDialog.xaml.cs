using System.Windows;
using System.Windows.Controls.Primitives;
using Shelf.Sdk;

namespace Shelf.Widgets.Notes;

public partial class NotesSettingsDialog : Window
{
    public int HeightResult { get; private set; }

    public NotesSettingsDialog(int currentHeight)
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        HeightSlider.Value = currentHeight;
        HeightValueRun.Text = currentHeight.ToString();
        HeightResult = currentHeight;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HeightValueRun != null)
            HeightValueRun.Text = ((int)e.NewValue).ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        HeightResult = (int)HeightSlider.Value;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
