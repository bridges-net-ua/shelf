using System.Windows;
using Shelf.Sdk;

namespace Shelf.Widgets.Todo;

public partial class TodoSettingsDialog : Window
{
    public int HeightResult { get; private set; }
    public bool HideCompletedResult { get; private set; }

    public TodoSettingsDialog(int height, bool hideCompleted)
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        HeightSlider.Value = height;
        HeightValueRun.Text = height.ToString();
        CbHideCompleted.IsChecked = hideCompleted;
        HeightResult = height;
        HideCompletedResult = hideCompleted;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HeightValueRun != null)
            HeightValueRun.Text = ((int)e.NewValue).ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        HeightResult = (int)HeightSlider.Value;
        HideCompletedResult = CbHideCompleted.IsChecked == true;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
