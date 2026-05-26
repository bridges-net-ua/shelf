using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Shelf.Sdk;

namespace Shelf.Widgets.Photos;

public partial class PhotosSettingsDialog : Window
{
    public string FolderPathResult { get; private set; } = "";
    public bool IncludeSubfoldersResult { get; private set; }
    public PhotosWidget.OrderMode OrderResult { get; private set; }
    public PhotosWidget.TransitionMode TransitionResult { get; private set; }
    public bool KenBurnsEnabledResult { get; private set; }
    public int IntervalSecondsResult { get; private set; }
    public bool GrayscaleResult { get; private set; }
    public int DarkenPercentResult { get; private set; }

    public PhotosSettingsDialog(PhotosWidget.WidgetState state)
    {
        InitializeComponent();
        WindowChrome.Apply(this);

        FolderPathBox.Text = state.FolderPath;
        CbIncludeSubfolders.IsChecked = state.IncludeSubfolders;
        OrderCombo.SelectedIndex = (int)state.Order;
        IntervalCombo.SelectedIndex = FindIntervalIndex(state.IntervalSeconds);
        TransitionCombo.SelectedIndex = FindTransitionIndex(state.Transition);
        CbKenBurns.IsChecked = state.KenBurnsEnabled;
        CbGrayscale.IsChecked = state.Grayscale;
        DarkenSlider.Value = state.DarkenPercent;

        // Seed results so OK without changes preserves current values.
        FolderPathResult = state.FolderPath;
        IncludeSubfoldersResult = state.IncludeSubfolders;
        OrderResult = state.Order;
        TransitionResult = state.Transition;
        KenBurnsEnabledResult = state.KenBurnsEnabled;
        IntervalSecondsResult = state.IntervalSeconds;
        GrayscaleResult = state.Grayscale;
        DarkenPercentResult = state.DarkenPercent;
    }

    private int FindIntervalIndex(int seconds)
    {
        for (int i = 0; i < IntervalCombo.Items.Count; i++)
        {
            if (IntervalCombo.Items[i] is ComboBoxItem cbi
                && cbi.Tag is string tag
                && int.TryParse(tag, out var s)
                && s == seconds)
                return i;
        }
        return 2; // 30s
    }

    private int FindTransitionIndex(PhotosWidget.TransitionMode mode)
    {
        var name = mode.ToString();
        for (int i = 0; i < TransitionCombo.Items.Count; i++)
        {
            if (TransitionCombo.Items[i] is ComboBoxItem cbi
                && cbi.Tag is string tag
                && tag == name)
                return i;
        }
        return 0; // CrossFade (default)
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.Get("Photos_ChooseFolder") };
        if (!string.IsNullOrWhiteSpace(FolderPathBox.Text))
        {
            try { dlg.InitialDirectory = FolderPathBox.Text; } catch { }
        }
        if (dlg.ShowDialog(this) == true)
        {
            FolderPathBox.Text = dlg.FolderName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        FolderPathResult = FolderPathBox.Text?.Trim() ?? "";
        IncludeSubfoldersResult = CbIncludeSubfolders.IsChecked == true;
        OrderResult = (PhotosWidget.OrderMode)OrderCombo.SelectedIndex;
        KenBurnsEnabledResult = CbKenBurns.IsChecked == true;
        GrayscaleResult = CbGrayscale.IsChecked == true;
        DarkenPercentResult = (int)Math.Round(DarkenSlider.Value);

        IntervalSecondsResult = 30;
        if (IntervalCombo.SelectedItem is ComboBoxItem ic
            && ic.Tag is string itag
            && int.TryParse(itag, out var s))
        {
            IntervalSecondsResult = s;
        }

        TransitionResult = PhotosWidget.TransitionMode.CrossFade;
        if (TransitionCombo.SelectedItem is ComboBoxItem tc
            && tc.Tag is string ttag
            && Enum.TryParse<PhotosWidget.TransitionMode>(ttag, out var tm))
        {
            TransitionResult = tm;
        }

        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }
}
