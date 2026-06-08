using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Notes;

public partial class NotesWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Notes_Name");

    private readonly DispatcherTimer _saveTimer;
    private DispatcherTimer? _copyFeedbackTimer;
    private bool _suppressSave;
    private bool _isEditingTitle;
    private bool _editTitleCanceled;

    // Tab-rename state (parallel to the widget-title rename above).
    private string? _renamingTabId;
    private bool _renameTabCanceled;
    private readonly List<TabVisual> _tabVisuals = new();

    public string Id => "notes";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Notes_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public class NoteTab
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";   // empty = use default "Tab N" by position
        public string Text { get; set; } = "";
    }

    public class WidgetState
    {
        // Legacy single-note field. Kept for back-compat deserialization only -
        // migrated into the first tab by EnsureTabs, then left empty.
        public string Text { get; set; } = "";
        public int Height { get; set; } = 180;
        public string Title { get; set; } = "";
        public List<NoteTab> Tabs { get; set; } = new();
        public string ActiveTabId { get; set; } = "";
    }

    private WidgetState _state = new();

    // Bundles a tab's model with its live UI parts so switch/rename/visuals
    // don't have to walk the visual tree.
    private sealed class TabVisual
    {
        public NoteTab Tab = null!;
        public Button Button = null!;
        public TextBlock Label = null!;
        public TextBox Editor = null!;
    }

    public NotesWidget()
    {
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            CaptureFromUi();
            WidgetServices.RequestSaveStates();
        };

        Loaded += (_, _) => ApplyState();
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        CaptureFromUi();

        var dlg = new NotesSettingsDialog(_state.Height)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state.Height = dlg.HeightResult;
            _suppressSave = true;
            NotesText.Height = _state.Height;
            _suppressSave = false;
            WidgetServices.RequestSaveStates();
        }
    }

    public string SaveState()
    {
        CaptureFromUi();
        return JsonSerializer.Serialize(_state);
    }

    public void LoadState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<WidgetState>(json);
            if (loaded != null) _state = loaded;
        }
        catch { }
        EnsureTabs();
    }

    // Guarantees at least one tab and a valid ActiveTabId. Migrates a legacy
    // single-note state (Text only) into the first tab without losing content.
    private void EnsureTabs()
    {
        _state.Tabs ??= new List<NoteTab>();
        if (_state.Tabs.Count == 0)
        {
            _state.Tabs.Add(new NoteTab
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "",
                Text = _state.Text ?? ""
            });
            _state.Text = ""; // legacy field consumed
        }
        if (string.IsNullOrEmpty(_state.ActiveTabId) || _state.Tabs.All(t => t.Id != _state.ActiveTabId))
            _state.ActiveTabId = _state.Tabs[0].Id;
    }

    private NoteTab? ActiveTab()
    {
        if (_state.Tabs.Count == 0) return null;
        return _state.Tabs.FirstOrDefault(t => t.Id == _state.ActiveTabId) ?? _state.Tabs[0];
    }

    private static string TabDisplayName(NoteTab tab, int index)
        => string.IsNullOrWhiteSpace(tab.Name)
            ? Loc.Format("Notes_Tab_DefaultNum", index + 1)
            : tab.Name;

    private void ApplyState()
    {
        EnsureTabs();
        _suppressSave = true;
        NotesText.Height = _state.Height;
        _suppressSave = false;
        RebuildTabs();
        LoadActiveTabIntoEditor();
        UpdateTitleDisplay();
    }

    private void LoadActiveTabIntoEditor()
    {
        var active = ActiveTab();
        _suppressSave = true;
        NotesText.Text = active?.Text ?? "";
        _suppressSave = false;
    }

    private void CaptureFromUi()
    {
        var active = ActiveTab();
        if (active != null && NotesText != null) active.Text = NotesText.Text;
    }

    // ===== Tabs =====

    private void RebuildTabs()
    {
        _renamingTabId = null;
        TabsBar.Children.Clear();
        _tabVisuals.Clear();
        for (int i = 0; i < _state.Tabs.Count; i++)
        {
            var tv = BuildTabButton(_state.Tabs[i], i);
            _tabVisuals.Add(tv);
            TabsBar.Children.Add(tv.Button);
        }
        RefreshTabVisuals();
    }

    private TabVisual BuildTabButton(NoteTab tab, int index)
    {
        var label = new TextBlock
        {
            Text = TabDisplayName(tab, index),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 120
        };

        var editor = new TextBox
        {
            Visibility = Visibility.Collapsed,
            FontSize = 11,
            MinWidth = 60,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        editor.SetResourceReference(BackgroundProperty, "ElevatedSurfaceBrush");
        editor.SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
        editor.SetResourceReference(TextBoxBase.CaretBrushProperty, "PrimaryTextBrush");
        editor.SetResourceReference(TextBoxBase.SelectionBrushProperty, "AccentBrush");
        editor.SetResourceReference(BorderBrushProperty, "AccentBrush");

        var grid = new Grid();
        grid.Children.Add(label);
        grid.Children.Add(editor);

        var btn = new Button
        {
            Content = grid,
            Tag = tab.Id,
            ToolTip = Loc.Get("Tip_Rename"),
            Style = (Style)FindResource("NoteTabButton")
        };
        btn.Click += TabButton_Click;
        btn.MouseDoubleClick += TabButton_MouseDoubleClick;

        var tv = new TabVisual { Tab = tab, Button = btn, Label = label, Editor = editor };
        btn.ContextMenu = BuildTabContextMenu(tv);
        editor.PreviewKeyDown += (_, e) => TabEditor_PreviewKeyDown(tv, e);
        editor.LostFocus += (_, _) => CommitTabRename(tv);

        return tv;
    }

    private void RefreshTabVisuals()
    {
        foreach (var tv in _tabVisuals)
        {
            bool active = tv.Tab.Id == _state.ActiveTabId;
            tv.Button.SetResourceReference(BackgroundProperty, active ? "AccentBrush" : "ElevatedSurfaceBrush");
            tv.Label.SetResourceReference(TextBlock.ForegroundProperty, active ? "PrimaryTextBrush" : "SecondaryTextBrush");
            tv.Label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private ContextMenu BuildTabContextMenu(TabVisual tv)
    {
        var menu = new ContextMenu();

        var rename = new MenuItem
        {
            Header = Loc.Get("Menu_Rename"),
            Icon = TabMenuIcon("M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z")
        };
        rename.Click += (_, _) => BeginRenameTab(tv);
        menu.Items.Add(rename);

        var delete = new MenuItem
        {
            Header = Loc.Get("Notes_DeleteTab"),
            Icon = TabMenuIcon("M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z")
        };
        delete.Click += (_, _) => ConfirmDeleteTab(tv.Tab);
        menu.Items.Add(delete);

        // Last tab can't be deleted - keep the widget from going empty.
        menu.Opened += (_, _) => delete.IsEnabled = _state.Tabs.Count > 1;

        return menu;
    }

    private static object TabMenuIcon(string geometry)
    {
        var path = new Path { Data = Geometry.Parse(geometry) };
        path.SetResourceReference(Shape.FillProperty, "PrimaryTextBrush");
        return new Viewbox
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 10, 0),
            Child = path
        };
    }

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id) SwitchToTab(id);
    }

    private void TabButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button b && b.Tag is string id)
        {
            var tv = _tabVisuals.FirstOrDefault(x => x.Tab.Id == id);
            if (tv != null)
            {
                BeginRenameTab(tv);
                e.Handled = true;
            }
        }
    }

    private void SwitchToTab(string tabId)
    {
        if (_renamingTabId != null) return;
        if (tabId == _state.ActiveTabId) return;
        var target = _state.Tabs.FirstOrDefault(t => t.Id == tabId);
        if (target == null) return;

        CaptureFromUi();
        _state.ActiveTabId = tabId;
        LoadActiveTabIntoEditor();
        RefreshTabVisuals();
        WidgetServices.RequestSaveStates();
    }

    private void AddTabButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureFromUi();
        var tab = new NoteTab { Id = Guid.NewGuid().ToString("N"), Name = "", Text = "" };
        _state.Tabs.Add(tab);
        _state.ActiveTabId = tab.Id;
        RebuildTabs();
        LoadActiveTabIntoEditor();
        WidgetServices.RequestSaveStates();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            TabsScroll.ScrollToRightEnd();
            NotesText.Focus();
        }), DispatcherPriority.Background);
    }

    private void ConfirmDeleteTab(NoteTab tab)
    {
        if (_state.Tabs.Count <= 1) return;
        int idx = _state.Tabs.IndexOf(tab);
        if (idx < 0) return;

        var ans = DarkMessageBox.Show(
            Window.GetWindow(this),
            Loc.Format("Notes_Confirm_DeleteTab", TabDisplayName(tab, idx)),
            Loc.Get("Title_Confirm"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ans != MessageBoxResult.Yes) return;

        bool wasActive = tab.Id == _state.ActiveTabId;
        _state.Tabs.Remove(tab);
        if (wasActive)
        {
            int newIdx = Math.Min(idx, _state.Tabs.Count - 1);
            _state.ActiveTabId = _state.Tabs[newIdx].Id;
        }
        RebuildTabs();
        LoadActiveTabIntoEditor();
        WidgetServices.RequestSaveStates();
    }

    private void BeginRenameTab(TabVisual tv)
    {
        if (_renamingTabId != null) return;
        if (tv.Tab.Id != _state.ActiveTabId) SwitchToTab(tv.Tab.Id);

        _renamingTabId = tv.Tab.Id;
        _renameTabCanceled = false;
        int idx = _state.Tabs.IndexOf(tv.Tab);
        tv.Editor.Text = TabDisplayName(tv.Tab, idx);
        tv.Label.Visibility = Visibility.Collapsed;
        tv.Editor.Visibility = Visibility.Visible;
        tv.Editor.Focus();
        tv.Editor.SelectAll();
    }

    private void TabEditor_PreviewKeyDown(TabVisual tv, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitTabRename(tv);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _renameTabCanceled = true;
            CommitTabRename(tv);
        }
    }

    private void CommitTabRename(TabVisual tv)
    {
        if (_renamingTabId != tv.Tab.Id) return;
        _renamingTabId = null;

        if (!_renameTabCanceled)
        {
            var newName = tv.Editor.Text.Trim();
            int idx = _state.Tabs.IndexOf(tv.Tab);
            string defaultName = Loc.Format("Notes_Tab_DefaultNum", idx + 1);
            // Empty or matches the positional default -> store empty ("use default")
            tv.Tab.Name = (string.IsNullOrEmpty(newName) || newName == defaultName) ? "" : newName;
            tv.Label.Text = TabDisplayName(tv.Tab, idx);
            WidgetServices.RequestSaveStates();
        }

        tv.Editor.Visibility = Visibility.Collapsed;
        tv.Label.Visibility = Visibility.Visible;
        _renameTabCanceled = false;
    }

    private void TabsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only hijack the wheel when tabs actually overflow; otherwise let the
        // panel's vertical scroll handle it.
        if (TabsScroll.ScrollableWidth <= 0) return;
        TabsScroll.ScrollToHorizontalOffset(TabsScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    // ===== Widget title rename (unchanged) =====

    private void UpdateTitleDisplay()
    {
        if (TitleText == null) return;
        TitleText.Text = string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;
    }

    private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginRenameTitle();
            e.Handled = true;
        }
    }

    private void TitleRenameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        BeginRenameTitle();
    }

    private void BeginRenameTitle()
    {
        if (_isEditingTitle) return;
        _isEditingTitle = true;
        _editTitleCanceled = false;
        TitleEdit.Text = string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEdit.Visibility = Visibility.Visible;
        TitleEdit.Focus();
        TitleEdit.SelectAll();
    }

    private void TitleEdit_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitTitleEdit();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _editTitleCanceled = true;
            CommitTitleEdit();
        }
    }

    private void TitleEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isEditingTitle) CommitTitleEdit();
    }

    private void CommitTitleEdit()
    {
        if (!_isEditingTitle) return;
        _isEditingTitle = false;

        if (!_editTitleCanceled)
        {
            var newTitle = TitleEdit.Text.Trim();
            // Empty or matches the default → store empty (means "use default")
            _state.Title = (string.IsNullOrEmpty(newTitle) || newTitle == DefaultTitle)
                ? ""
                : newTitle;

            UpdateTitleDisplay();
            WidgetServices.RequestSaveStates();
        }

        TitleEdit.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        _editTitleCanceled = false;
    }

    private void NotesText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSave) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(NotesText.Text ?? "");
        }
        catch
        {
            return;
        }

        CopyIconPath.Fill = (Brush)FindResource("AccentBrush");
        _copyFeedbackTimer?.Stop();
        _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _copyFeedbackTimer.Tick += (_, _) =>
        {
            _copyFeedbackTimer?.Stop();
            CopyIconPath.SetValue(Shape.FillProperty, DependencyProperty.UnsetValue);
        };
        _copyFeedbackTimer.Start();
    }
}
