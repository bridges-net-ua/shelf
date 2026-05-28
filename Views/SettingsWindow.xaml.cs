using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    // Per-monitor draft state — we don't write directly to Settings.MonitorPanels
    // until OK is pressed. CbMonitor selection-changed commits the current UI
    // values into the draft for the previously-selected monitor, then loads the
    // draft (or a fresh default) for the newly-selected one.
    private bool _suppressMonitorChange = true;
    private readonly Dictionary<string, MonitorPanelConfig> _draftPanels =
        new(StringComparer.OrdinalIgnoreCase);
    private string _selectedMonitorDeviceName = "";

    private sealed class MonitorListItem
    {
        public required string DeviceName { get; init; }
        public required string DisplayName { get; init; }
        public override string ToString() => DisplayName;
    }

    public SettingsWindow()
    {
        InitializeComponent();
        Sdk.WindowChrome.Apply(this);
        LoadCurrent();
        RebuildWidgetsList();
        SetupAbout();
        _suppressThemeApply = false;
        _suppressMonitorChange = false;
    }

    private void SetupAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = version == null ? "1.0" : $"{version.Major}.{version.Minor}";
        AboutVersionText.Text = Loc.Format("About_Version", versionString);

        AboutDeveloperText.Inlines.Clear();
        AboutDeveloperText.Inlines.Add(new Run(Loc.Get("About_DevelopedBy")));
        AboutDeveloperText.Inlines.Add(new Run(" "));
        AboutDeveloperText.Inlines.Add(new Run(Loc.Get("About_Developer"))
        {
            FontWeight = FontWeights.Bold
        });

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
            // No mailto handler installed — silently ignore.
        }
    }

    private void LoadCurrent()
    {
        var s = App.Settings.Current;

        // Populate the monitor selector with all currently connected monitors,
        // and seed the draft dictionary with their saved configs (or defaults
        // built from the legacy global fields if there's no saved entry yet).
        var monitors = App.Monitors.Monitors;
        var items = new List<MonitorListItem>();
        foreach (var m in monitors)
        {
            _draftPanels[m.DeviceName] = CloneConfig(App.Settings.GetMonitorConfig(m.DeviceName));
            items.Add(new MonitorListItem
            {
                DeviceName = m.DeviceName,
                DisplayName = m.DisplayName
            });
        }
        CbMonitor.ItemsSource = items;

        // Select primary by default — that's almost always what the user wants to
        // configure first.
        var primary = items.FirstOrDefault(
            i => string.Equals(i.DeviceName, App.Monitors.Primary.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (primary != null)
        {
            CbMonitor.SelectedItem = primary;
            _selectedMonitorDeviceName = primary.DeviceName;
        }
        else if (items.Count > 0)
        {
            CbMonitor.SelectedIndex = 0;
            _selectedMonitorDeviceName = items[0].DeviceName;
        }

        // Load per-monitor controls from the just-seeded draft of the selected monitor.
        LoadPerMonitorControls(_selectedMonitorDeviceName);

        WidthLabelRun.Text = Loc.Get("Settings_Width");
        WidthUnitRun.Text = Loc.Get("Settings_Px");

        // Global (non-per-monitor) controls.
        CbAutoStart.IsChecked = s.AutoStart;
        CbLockOrder.IsChecked = s.WidgetOrderLocked;
        CbTheme.SelectedIndex = s.Theme == AppTheme.Light ? 1 : 0;
        RbLangUk.IsChecked = s.Language == AppLanguage.Uk;
        RbLangEn.IsChecked = s.Language == AppLanguage.En;
    }

    private void LoadPerMonitorControls(string deviceName)
    {
        if (!_draftPanels.TryGetValue(deviceName, out var cfg))
            cfg = new MonitorPanelConfig();

        RbLeft.IsChecked = cfg.Side == BarSide.Left;
        RbRight.IsChecked = cfg.Side == BarSide.Right;
        WidthSlider.Value = cfg.Width;
        WidthValueRun.Text = cfg.Width.ToString();
        CbAutoHide.IsChecked = cfg.AutoHide;
    }

    // Pulls the currently visible per-monitor field values into a MonitorPanelConfig.
    // Called before switching monitors and on Ok_Click.
    private MonitorPanelConfig CaptureCurrentPerMonitorConfig() => new()
    {
        Side = RbLeft.IsChecked == true ? BarSide.Left : BarSide.Right,
        Width = (int)WidthSlider.Value,
        AutoHide = CbAutoHide.IsChecked == true
    };

    private static MonitorPanelConfig CloneConfig(MonitorPanelConfig src) => new()
    {
        Side = src.Side,
        Width = src.Width,
        AutoHide = src.AutoHide
    };

    private void CbMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonitorChange) return;
        if (CbMonitor.SelectedItem is not MonitorListItem newItem) return;
        if (string.Equals(newItem.DeviceName, _selectedMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
            return;

        // Commit current UI into the draft of the monitor we're leaving, then
        // load the draft of the new selection.
        if (!string.IsNullOrEmpty(_selectedMonitorDeviceName))
            _draftPanels[_selectedMonitorDeviceName] = CaptureCurrentPerMonitorConfig();

        _selectedMonitorDeviceName = newItem.DeviceName;
        LoadPerMonitorControls(_selectedMonitorDeviceName);
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
        // Commit the currently visible per-monitor edits into the draft.
        if (!string.IsNullOrEmpty(_selectedMonitorDeviceName))
            _draftPanels[_selectedMonitorDeviceName] = CaptureCurrentPerMonitorConfig();

        // Persist every draft monitor config into Settings. Untouched monitors
        // get their original config written back (the draft was cloned from
        // settings at LoadCurrent), so this is non-destructive.
        var s = App.Settings.Current;
        foreach (var kv in _draftPanels)
            s.MonitorPanels[kv.Key] = kv.Value;

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

        var connected = App.GetConnectedDeviceNames();
        foreach (var (entry, widget) in App.Widgets.GetAllWithEntries())
        {
            WidgetsListPanel.Children.Add(BuildRow(entry, widget, connected));
        }

        WidgetsListPanel.Children.Add(BuildAddWidgetButton());
    }

    private UIElement BuildRow(WidgetEntry entry, IWidget widget, IReadOnlySet<string> connected)
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

        // Monitor badge — small line under the description showing which monitor
        // this widget lives on. Skipped only when there's a single connected
        // monitor (no useful information to convey).
        if (App.Monitors.Monitors.Count > 1)
        {
            string monitorLine;
            if (string.IsNullOrEmpty(entry.MonitorDeviceName))
            {
                monitorLine = Loc.Format("Settings_Widget_OnMonitor", Loc.Get("Monitor_Primary"));
            }
            else if (connected.Contains(entry.MonitorDeviceName))
            {
                var mon = App.Monitors.FindByDeviceName(entry.MonitorDeviceName);
                monitorLine = Loc.Format("Settings_Widget_OnMonitor", mon?.DisplayName ?? entry.MonitorDeviceName);
            }
            else
            {
                monitorLine = Loc.Format("Settings_Widget_OnMonitor_Homeless", entry.MonitorDeviceName);
            }
            labelPanel.Children.Add(new TextBlock
            {
                Text = monitorLine,
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
                // Settings "Add widget" is monitor-agnostic — new widgets default
                // to the primary monitor (MonitorDeviceName=null). Users use the
                // panel's right-click menu to add directly on a non-primary
                // monitor, then "Move to monitor ▶" if they want to relocate.
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
