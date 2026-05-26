using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using Shelf.Models;
using Shelf.Sdk;
using Shelf.Services;

namespace Shelf.Views;

public partial class SettingsWindow : Window
{
    // Guard so that programmatic IsChecked = ... inside LoadCurrent does not trigger
    // Theme.Apply during initial population.
    private bool _suppressThemeApply = true;

    public SettingsWindow()
    {
        InitializeComponent();
        Sdk.WindowChrome.Apply(this);
        LoadCurrent();
        RebuildWidgetsList();
        SetupAbout();
        _suppressThemeApply = false;
    }

    private void SetupAbout()
    {
        // Версія: тягнемо з Assembly, форматуємо як Major.Minor (наприклад "1.0")
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = version == null ? "1.0" : $"{version.Major}.{version.Minor}";
        AboutVersionText.Text = Loc.Format("About_Version", versionString);

        // Рядок "Програма розроблена: Bridges Community" - назва жирним.
        AboutDeveloperText.Inlines.Clear();
        AboutDeveloperText.Inlines.Add(new Run(Loc.Get("About_DevelopedBy")));
        AboutDeveloperText.Inlines.Add(new Run(" "));
        AboutDeveloperText.Inlines.Add(new Run(Loc.Get("About_Developer"))
        {
            FontWeight = FontWeights.Bold
        });

        // Рядок "Є питання? Пишіть: email" - email клікабельний як mailto-Hyperlink.
        AboutContactText.Inlines.Clear();
        AboutContactText.Inlines.Add(new Run(Loc.Get("About_ContactPrompt")));
        AboutContactText.Inlines.Add(new Run(" "));

        var emailAddress = Loc.Get("About_Email");
        var emailLink = new Hyperlink(new Run(emailAddress))
        {
            NavigateUri = new Uri("mailto:" + emailAddress),
            Foreground = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush")
        };
        emailLink.RequestNavigate += AboutEmailLink_RequestNavigate;
        AboutContactText.Inlines.Add(emailLink);
    }

    private void AboutEmailLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // Якщо немає mailto-handler - тихо ігноруємо.
        }
    }

    private void LoadCurrent()
    {
        var s = App.Settings.Current;
        RbLeft.IsChecked = s.Side == BarSide.Left;
        RbRight.IsChecked = s.Side == BarSide.Right;
        WidthSlider.Value = s.Width;
        WidthLabelRun.Text = Loc.Get("Settings_Width");
        WidthValueRun.Text = s.Width.ToString();
        WidthUnitRun.Text = Loc.Get("Settings_Px");
        CbAutoHide.IsChecked = s.AutoHide;
        CbAutoStart.IsChecked = s.AutoStart;
        CbLockOrder.IsChecked = s.WidgetOrderLocked;
        // ComboBox порядок: 0 = Dark, 1 = Light.
        // Якщо в майбутньому додасться нова тема - додай ComboBoxItem у XAML і case тут.
        CbTheme.SelectedIndex = s.Theme == AppTheme.Light ? 1 : 0;
        RbLangUk.IsChecked = s.Language == AppLanguage.Uk;
        RbLangEn.IsChecked = s.Language == AppLanguage.En;
    }

    private void CbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeApply) return;

        var newTheme = CbTheme.SelectedIndex == 1 ? AppTheme.Light : AppTheme.Dark;
        if (newTheme == App.Settings.Current.Theme) return;

        App.Settings.Current.Theme = newTheme;
        App.Settings.Save();
        Theme.Apply(newTheme);
    }

    private void CbLockOrder_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Current.WidgetOrderLocked = CbLockOrder.IsChecked == true;
        App.Settings.Save();
        App.Widgets.NotifyOrderLockChanged();
        RebuildWidgetsList();
    }

    private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WidthValueRun != null)
            WidthValueRun.Text = ((int)e.NewValue).ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings.Current;
        s.Side = RbLeft.IsChecked == true ? BarSide.Left : BarSide.Right;
        s.Width = (int)WidthSlider.Value;
        s.AutoHide = CbAutoHide.IsChecked == true;
        s.AutoStart = CbAutoStart.IsChecked == true;

        var oldLang = s.Language;
        var newLang = RbLangEn.IsChecked == true ? AppLanguage.En : AppLanguage.Uk;
        s.Language = newLang;

        App.Settings.NotifyChanged();

        if (oldLang != newLang)
        {
            var ans = DarkMessageBox.Show(this,
                Loc.Get("Settings_Language_Hint"),
                Loc.Get("Title_RestartNeeded"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (ans == MessageBoxResult.Yes)
            {
                RestartApp();
                return;
            }
        }

        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }

    private static void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true
                });
            }
        }
        catch { }
        Application.Current.Shutdown();
    }

    private void RebuildWidgetsList()
    {
        WidgetsListPanel.Children.Clear();

        foreach (var (entry, widget) in App.Widgets.GetAllWithEntries())
        {
            WidgetsListPanel.Children.Add(BuildRow(entry, widget));
        }

        WidgetsListPanel.Children.Add(BuildAddWidgetButton());
    }

    private UIElement BuildRow(WidgetEntry entry, IWidget widget)
    {
        var row = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox
        {
            IsChecked = entry.Enabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Click += (_, _) =>
        {
            App.Widgets.SetEnabled(entry.InstanceId, cb.IsChecked == true);
        };
        Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        var labelPanel = new StackPanel
        {
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        labelPanel.Children.Add(new TextBlock
        {
            Text = widget.InstanceLabel,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrEmpty(widget.Description))
        {
            labelPanel.Children.Add(new TextBlock
            {
                Text = widget.Description,
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        Grid.SetColumn(labelPanel, 1);
        grid.Children.Add(labelPanel);

        bool reorderEnabled = !App.Settings.Current.WidgetOrderLocked;

        var btnUp = MakeIconButton("↑", Loc.Get("Tip_MoveUp"),
            () => App.Widgets.MoveUp(entry.InstanceId), enabled: reorderEnabled);
        Grid.SetColumn(btnUp, 2);
        grid.Children.Add(btnUp);

        var btnDown = MakeIconButton("↓", Loc.Get("Tip_MoveDown"),
            () => App.Widgets.MoveDown(entry.InstanceId), enabled: reorderEnabled);
        Grid.SetColumn(btnDown, 3);
        grid.Children.Add(btnDown);

        var btnDel = MakeIconButton("✕", Loc.Get("Tip_DeleteWidget"), () =>
            ConfirmAndDelete(entry.InstanceId, widget.InstanceLabel), subtle: true);
        Grid.SetColumn(btnDel, 4);
        grid.Children.Add(btnDel);

        row.Child = grid;
        return row;
    }

    private void ConfirmAndDelete(string instanceId, string displayName)
    {
        var result = DarkMessageBox.Show(this,
            Loc.Format("Confirm_DeleteWidget", displayName),
            Loc.Get("Title_Confirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            App.Widgets.RemoveInstance(instanceId);
            RebuildWidgetsList();
        }
    }

    private Button MakeIconButton(string text, string tooltip, Action onClick, bool subtle = false, bool enabled = true)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 14,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 2, 0),
            Style = (Style)FindResource(subtle ? "IconButtonSubtle" : "IconButton"),
            ToolTip = tooltip,
            IsEnabled = enabled
        };
        btn.Click += (_, _) =>
        {
            onClick();
            RebuildWidgetsList();
        };
        return btn;
    }

    private UIElement BuildAddWidgetButton()
    {
        var btn = new Button
        {
            Content = Loc.Get("Settings_AddWidget"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var menu = new ContextMenu();
        foreach (var type in WidgetRegistry.Types)
        {
            var typeId = type.TypeId;
            var item = new MenuItem { Header = type.DisplayName };
            item.Click += (_, _) =>
            {
                App.Widgets.AddInstance(typeId);
                RebuildWidgetsList();
            };
            menu.Items.Add(item);
        }

        btn.Click += (_, _) =>
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };

        return btn;
    }
}
