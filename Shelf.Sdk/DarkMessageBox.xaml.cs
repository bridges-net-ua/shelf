using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shelf.Sdk;

public partial class DarkMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    private DarkMessageBox()
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string title = "",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        var dlg = new DarkMessageBox();
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        dlg.Title = string.IsNullOrEmpty(title) ? Loc.Get("Title_Message") : title;
        dlg.MessageText.Text = text;
        dlg.SetIcon(icon);
        dlg.BuildButtons(buttons);
        dlg.ShowDialog();
        return dlg._result;
    }

    private void SetIcon(MessageBoxImage icon)
    {
        if (icon == MessageBoxImage.None)
        {
            IconBox.Visibility = Visibility.Collapsed;
            return;
        }

        IconBox.Visibility = Visibility.Visible;
        IconEllipse.Visibility = Visibility.Visible;
        IconEllipse.Width = 32;
        IconEllipse.Height = 32;
        IconPath.Fill = Brushes.White;

        switch (icon)
        {
            case MessageBoxImage.Information:
                IconEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
                IconPath.Data = ParseEvenOdd(
                    "M 15,7 L 17,7 L 17,9 L 15,9 Z " +
                    "M 14,12 L 18,12 L 18,24 L 19.5,24 L 19.5,26 L 12.5,26 L 12.5,24 L 14,24 Z");
                break;

            case MessageBoxImage.Question:
                IconEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
                IconPath.Data = ParseEvenOdd(
                    "M 16,6 C 12.5,6 10,8.5 10,12 L 13,12 C 13,10 14.3,8.8 16,8.8 " +
                    "C 17.7,8.8 19,10 19,11.5 C 19,12.5 18.5,13.2 17.5,13.9 " +
                    "C 16,14.9 14.5,16 14.5,18.5 L 14.5,19.5 L 17.5,19.5 L 17.5,18.8 " +
                    "C 17.5,17.5 18.2,16.9 19.2,16.2 C 20.8,15.1 22,13.8 22,11.5 " +
                    "C 22,8.5 19.5,6 16,6 Z " +
                    "M 14.5,22 L 17.5,22 L 17.5,25 L 14.5,25 Z");
                break;

            case MessageBoxImage.Warning:
                // No background ellipse — the icon IS the triangle
                IconEllipse.Visibility = Visibility.Collapsed;
                IconPath.Fill = new SolidColorBrush(Color.FromRgb(0xE6, 0xA9, 0x00));
                IconPath.Data = ParseEvenOdd(
                    "M 16,3 L 30,28 L 2,28 Z " +
                    "M 15,12 L 17,12 L 17,20 L 15,20 Z " +
                    "M 15,22 L 17,22 L 17,24.5 L 15,24.5 Z");
                break;

            case MessageBoxImage.Error:
                IconEllipse.Fill = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                IconPath.Data = ParseEvenOdd(
                    "M 10,11 L 11,10 L 16,15 L 21,10 L 22,11 L 17,16 " +
                    "L 22,21 L 21,22 L 16,17 L 11,22 L 10,21 L 15,16 Z");
                break;
        }
    }

    private static Geometry ParseEvenOdd(string path)
    {
        var g = Geometry.Parse(path);
        if (g is PathGeometry pg) pg.FillRule = FillRule.EvenOdd;
        return g;
    }

    private void BuildButtons(MessageBoxButton buttons)
    {
        ButtonsPanel.Children.Clear();

        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton(Loc.Get("Btn_OK"), MessageBoxResult.OK, isDefault: true, isAccent: true);
                break;

            case MessageBoxButton.OKCancel:
                AddButton(Loc.Get("Btn_Cancel"), MessageBoxResult.Cancel, isCancel: true);
                AddButton(Loc.Get("Btn_OK"), MessageBoxResult.OK, isDefault: true, isAccent: true);
                break;

            case MessageBoxButton.YesNo:
                AddButton(Loc.Get("Btn_No"), MessageBoxResult.No, isCancel: true);
                AddButton(Loc.Get("Btn_Yes"), MessageBoxResult.Yes, isDefault: true, isAccent: true);
                break;

            case MessageBoxButton.YesNoCancel:
                AddButton(Loc.Get("Btn_Cancel"), MessageBoxResult.Cancel, isCancel: true);
                AddButton(Loc.Get("Btn_No"), MessageBoxResult.No);
                AddButton(Loc.Get("Btn_Yes"), MessageBoxResult.Yes, isDefault: true, isAccent: true);
                break;
        }
    }

    private void AddButton(string text, MessageBoxResult result,
        bool isDefault = false, bool isCancel = false, bool isAccent = false)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 96,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel
        };
        if (isAccent)
        {
            btn.Background = (Brush)FindResource("AccentBrush");
            btn.BorderBrush = (Brush)FindResource("AccentBrush");
        }
        btn.Click += (_, _) =>
        {
            _result = result;
            try { DialogResult = (result == MessageBoxResult.OK || result == MessageBoxResult.Yes); } catch { }
            Close();
        };
        ButtonsPanel.Children.Add(btn);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Treat as Cancel/No if such option exists, otherwise keep default behaviour
            foreach (var child in ButtonsPanel.Children)
            {
                if (child is Button b && b.IsCancel)
                {
                    b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    e.Handled = true;
                    return;
                }
            }
        }
    }
}
