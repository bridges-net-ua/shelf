using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shelf.Models;
using Shelf.Sdk;
using Shelf.Services;
using Shelf.Views;

namespace Shelf;

public partial class MainWindow : Window
{
    private AppBarService? _appBar;
    private VirtualDesktopService? _virtualDesktopService;
    private DispatcherTimer? _hideTimer;
    private bool _hidden;
    private const int HiddenTriggerWidthPx = 3;

    // The monitor this window is bound to. Each MainWindow renders the widgets
    // assigned to its monitor (plus homeless widgets on primary). DeviceName is
    // the key for looking up the per-monitor panel config in AppSettings.
    private readonly MonitorInfo _monitor;
    public string MonitorDeviceName => _monitor.DeviceName;
    public bool IsPrimary => _monitor.IsPrimary;


    // Drag state for widget reordering
    private bool _dragArmed;
    private bool _dragCaptured;
    private Point _dragStartPoint;
    private Grid? _dragContainer;
    private WidgetEntry? _dragEntry;
    private Border? _insertionLine;
    private Image? _ghostImage;
    private double _ghostOffsetY;
    private DispatcherTimer? _autoScrollTimer;
    private int _autoScrollDirection;
    private const double DragThreshold = 4.0;
    // A widget is grabbed for reordering by its top strip (the header where the
    // title sits); presses below this many px from the container top don't drag.
    private const double HeaderDragZonePx = 34.0;
    private const double AutoScrollEdgePx = 36.0;
    private const double AutoScrollSpeedPxPerTick = 8.0;

    static MainWindow()
    {
        // Class-level handler ensures Ctrl+X/C/V/A reach every TextBox in this app, including
        // those hosted inside this borderless, non-activating panel window where WPF's default
        // input routing for these gestures can silently no-op.
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(OnTextBoxPreviewKeyDown));
    }

    private static void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0) return;
        if (sender is not TextBox tb) return;

        switch (e.Key)
        {
            case Key.X:
                if (!tb.IsReadOnly && tb.SelectionLength > 0)
                {
                    var text = tb.SelectedText;
                    int start = tb.SelectionStart;
                    int len = tb.SelectionLength;
                    // Delete synchronously first — user gets instant feedback regardless of clipboard state
                    tb.Text = tb.Text.Remove(start, len);
                    tb.CaretIndex = start;
                    SetClipboardAsync(text);
                }
                e.Handled = true;
                break;

            case Key.C:
                if (tb.SelectionLength > 0)
                    SetClipboardAsync(tb.SelectedText);
                e.Handled = true;
                break;

            case Key.V:
                if (!tb.IsReadOnly)
                {
                    // Route through TextBoxBase.Paste() rather than direct Text manipulation
                    // so DataObject.Pasting handlers (e.g. multi-line splitter in Todo widget)
                    // run. Direct invocation bypasses the KeyBinding/CommandBinding chain that
                    // was unreliable in this borderless Topmost window.
                    tb.Paste();
                }
                e.Handled = true;
                break;

            case Key.A:
                tb.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private static void SetClipboardAsync(string text)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        // Use WinForms Clipboard — it exposes explicit retry count so we can keep UI snappy
        // even when a misbehaving clipboard hook is installed. Runs on the UI thread but
        // via Background dispatcher priority, so it doesn't block immediate input handling.
        dispatcher.BeginInvoke(new Action(() =>
        {
            try { System.Windows.Forms.Clipboard.SetDataObject(text ?? "", copy: true, retryTimes: 3, retryDelay: 50); }
            catch { }
        }), DispatcherPriority.Background);
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            if (Clipboard.ContainsText())
                return Clipboard.GetText();
        }
        catch { }
        return null;
    }

    public MainWindow(MonitorInfo monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        Sdk.WindowChrome.Apply(this);
        BuildPanelContextMenu();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        SizeChanged += (_, _) => UpdatePinnedScrollMaxHeight();
    }

    private static readonly Duration PinReorderDuration = new(TimeSpan.FromMilliseconds(280));
    private static readonly IEasingFunction PinReorderEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

    private void UpdatePinnedScrollMaxHeight()
    {
        // Cap pinned zone at ~60% of the window so it never starves the scrollable zone.
        double cap = Math.Max(120, ActualHeight * 0.6);
        if (Math.Abs(PinnedScroll.MaxHeight - cap) > 0.5)
            PinnedScroll.MaxHeight = cap;
    }

    // ===== Menu icons (Material Design path data, 24×24 viewBox) =====

    private const string IconPin =
        "M16,12V4H17V2H7V4H8V12L6,14V16H11.2V22H12.8V16H18V14L16,12Z";
    private const string IconCog =
        "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5," +
        "3.5 0 0,1 12,15.5M19.43,12.98C19.47,12.66 19.5,12.34 19.5,12C19.5,11.66 19.47," +
        "11.34 19.43,11.02L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54," +
        "5.05 19.27,4.97 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46," +
        "2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44," +
        "6.05L4.95,5.05C4.73,4.97 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46," +
        "9.37L4.57,11.02C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.66 4.57,12.98L2.46," +
        "14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95," +
        "18.95L7.44,17.95C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10," +
        "22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56," +
        "17.95L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73," +
        "14.78 21.54,14.63L19.43,12.98Z";
    private const string IconTrash =
        "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";
    private const string IconAdd =
        "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";
    private const string IconTune =
        "M3,17V19H9V17H3M3,5V7H13V5H3M13,21V19H21V17H13V15H11V21H13M7,9V11H3V13H7V15H9V9H7M21," +
        "13V11H11V13H21M15,9H17V7H21V5H17V3H15V9Z";
    private const string IconLock =
        "M12,17A2,2 0 0,0 14,15C14,13.89 13.1,13 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M18," +
        "8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V10C4,8.89 4.9,8 6,8H7V6A5,5 " +
        "0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,3A3,3 0 0,0 9,6V8H15V6A3,3 0 0,0 12,3Z";
    private const string IconExit =
        "M16,17V14H9V10H16V7L21,12L16,17M14,2A2,2 0 0,1 16,4V6H14V4H5V20H14V18H16V20A2,2 0 " +
        "0,1 14,22H5A2,2 0 0,1 3,20V4A2,2 0 0,1 5,2H14Z";
    private const string IconClock =
        "M12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20M12,2A10,10 " +
        "0 0,1 22,12A10,10 0 0,1 12,22C6.47,22 2,17.5 2,12A10,10 0 0,1 12,2M12.5,7V12.25L17," +
        "14.92L16.25,16.15L11,13V7H12.5Z";
    private const string IconNotes =
        "M14,17H7V15H14M17,13H7V11H17M17,9H7V7H17M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5," +
        "21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3Z";
    private const string IconTodo =
        "M3,5H9V11H3V5M5,7V9H7V7H5M11,7H21V9H11V7M11,15H21V17H11V15M5,20L1.5,16.5L2.91," +
        "15.09L5,17.17L9.59,12.59L11,14L5,20Z";
    private const string IconPhotos =
        "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2," +
        "2 0 0,0 5,21H19A2,2 0 0,0 21,19Z";
    private const string IconRadio =
        "M20,6A2,2 0 0,1 22,8V20A2,2 0 0,1 20,22H4A2,2 0 0,1 2,20V8A2,2 0 0,1 4,6H15.86L10.5," +
        "3.79L11.26,2L18.83,5.13C19.5,5.41 19.97,6 20,6M20,20V8H4V20H20M12,9A4,4 0 0,1 16," +
        "13A4,4 0 0,1 12,17A4,4 0 0,1 8,13A4,4 0 0,1 12,9M12,11A2,2 0 0,0 10,13A2,2 0 0,0 12," +
        "15A2,2 0 0,0 14,13A2,2 0 0,0 12,11M6,11A1,1 0 0,1 7,12A1,1 0 0,1 6,13A1,1 0 0,1 5," +
        "12A1,1 0 0,1 6,11Z";
    private const string IconWeather =
        "M12.74,5.47C15.1,6.5 16.35,9.03 15.92,11.46C17.19,12.56 18,14.19 18,16V16.17C18.31," +
        "16.06 18.65,16 19,16A3,3 0 0,1 22,19A3,3 0 0,1 19,22H6A4,4 0 0,1 2,18A4,4 0 0,1 6," +
        "14H6.27C5,12.45 4.6,10.24 5.5,8.26C6.72,5.5 9.97,4.24 12.74,5.47M11.93,7.3C10.16," +
        "6.5 8.09,7.31 7.31,9.07C6.85,10.09 6.93,11.22 7.41,12.13C8.5,10.83 10.16,10 12," +
        "10C12.7,10 13.38,10.12 14,10.34C13.94,9 13.18,7.85 11.93,7.3M13.55,3.64C13,3.4 " +
        "12.45,3.23 11.88,3.12L14.37,1.82L15.27,4.71C14.76,4.29 14.19,3.93 13.55,3.64M6.09," +
        "4.44C5.6,4.79 5.17,5.19 4.8,5.63L4.91,2.82L7.87,3.5C7.25,3.71 6.65,4.03 6.09," +
        "4.44M18,9.71C17.91,9.12 17.78,8.55 17.59,8L19.97,9.5L17.92,11.73C18.05,11.08 18.08," +
        "10.4 18,9.71M3.04,11.3C3.11,11.9 3.24,12.47 3.43,13L1.06,11.5L3.1,9.28C2.97,9.93 " +
        "2.95,10.61 3.04,11.3Z";
    private const string IconTimer =
        "M12,20A7,7 0 0,1 5,13A7,7 0 0,1 12,6A7,7 0 0,1 19,13A7,7 0 0,1 12,20M19.03," +
        "7.39L20.45,5.97C20,5.46 19.55,5 19.04,4.56L17.62,6C16.07,4.74 14.12,4 12,4A9,9 0 " +
        "0,0 3,13A9,9 0 0,0 12,22C17,22 21,17.97 21,13C21,10.88 20.26,8.93 19.03,7.39M11," +
        "14H13V8H11M15,1H9V3H15V1Z";
    private const string IconHolidays =
        "M22,12V20A2,2 0 0,1 20,22H4A2,2 0 0,1 2,20V12A1,1 0 0,1 1,11V8A2,2 0 0,1 3," +
        "6H6.17C6.06,5.69 6,5.35 6,5A3,3 0 0,1 9,2C10,2 10.88,2.5 11.43,3.24V3.23L12," +
        "4L12.57,3.23V3.24C13.12,2.5 14,2 15,2A3,3 0 0,1 18,5C18,5.35 17.94,5.69 17.83," +
        "6H21A2,2 0 0,1 23,8V11A1,1 0 0,1 22,12M4,20H11V13H4V20M20,20V13H13V20H20M9,4A1," +
        "1 0 0,0 8,5A1,1 0 0,0 9,6A1,1 0 0,0 10,5A1,1 0 0,0 9,4M15,4A1,1 0 0,0 14,5A1," +
        "1 0 0,0 15,6A1,1 0 0,0 16,5A1,1 0 0,0 15,4M3,8V11H11V8H3M13,8V11H21V8H13Z";
    private const string IconWidgetGeneric =
        "M3,11H11V3H3V11M3,21H11V13H3V21M13,21H21V13H13V21M13,3V11H21V3H13Z";
    private const string IconNba =
        "M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12C22,6.5 17.5,2 12,2M10," +
        "4.13C10.65,4.19 11.32,4.31 12,4.5V10.97L7.79,4.86C8.5,4.5 9.24,4.25 10,4.13M5.61," +
        "6.36L11.4,14.79L9.65,16L4.42,8.5C4.74,7.73 5.13,7 5.61,6.36M3.65,11.32L7.13,16.35L5," +
        "17.77C4.36,16.66 3.94,15.41 3.77,14.06C3.69,13.16 3.61,12.24 3.65,11.32M9.94,19.71C8.66," +
        "19.42 7.47,18.86 6.45,18.1L11.13,15L13.65,18.61C12.4,19.3 11.04,19.66 9.94,19.71M11.92," +
        "14.04L13.77,11.06L19.71,12.39C19.5,13.78 19,15.07 18.21,16.18L11.92,14.04M19.93," +
        "10.45L14.04,9.12L17.85,2.97C18.65,3.97 19.27,5.1 19.65,6.32C19.97,7.59 20.06,8.95 19.93," +
        "10.45M16.93,2.5L13.04,8.74L12.86,2.65C13.93,2.85 14.94,3.21 15.88,3.7C16.27,3.94 16.61," +
        "4.2 16.93,4.5V2.5Z";

    private static string WidgetTypeIconGeometry(string typeId) => typeId switch
    {
        "clock" => IconClock,
        "notes" => IconNotes,
        "todo" => IconTodo,
        "photos" => IconPhotos,
        "radio" => IconRadio,
        "weather" => IconWeather,
        "timer" => IconTimer,
        "holidays" => IconHolidays,
        "nba" => IconNba,
        _ => IconWidgetGeneric
    };

    // Builds a fresh 16px monochrome icon for a MenuItem.Icon slot. Fill follows the
    // owning MenuItem's Foreground, so the icon dims together with a disabled item.
    private static FrameworkElement MenuIcon(string geometry)
    {
        // No Stretch on the Path — the Viewbox scales it. A Path with Stretch inside a
        // Viewbox collapses to zero size (Viewbox measures with an infinite constraint).
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry)
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "PrimaryTextBrush");
        return new Viewbox
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 10, 0),
            Child = path
        };
    }

    private void BuildPanelContextMenu()
    {
        var menu = new ContextMenu();
        AppendPanelMenuItems(menu);
        ContextMenu = menu;
    }

    // Adds the panel-level actions (add widget, settings, lock, exit) to any ContextMenu,
    // preceded by a section header. Both the widget menu and the window-level (empty-area)
    // menu use this so there is exactly one set of panel actions in the app.
    private void AppendPanelMenuItems(ContextMenu menu)
    {
        menu.Items.Add(BuildMenuSectionHeader(Loc.Get("Menu_Section_App")));

        var addItem = new MenuItem { Header = Loc.Get("Menu_AddWidget"), Icon = MenuIcon(IconAdd) };
        foreach (var type in WidgetRegistry.Types)
        {
            var typeId = type.TypeId;
            var subItem = new MenuItem
            {
                Header = type.DisplayName,
                Icon = MenuIcon(WidgetTypeIconGeometry(typeId))
            };
            // Primary panel adds widgets with null assignment (so they follow the
            // primary monitor if it changes). Non-primary panels stamp the current
            // monitor's DeviceName, so the new widget lands here and stays here
            // even if this monitor is later disconnected/reconnected.
            string? targetMonitor = _monitor.IsPrimary ? null : _monitor.DeviceName;
            subItem.Click += (_, _) => App.Widgets.AddInstance(typeId, targetMonitor);
            addItem.Items.Add(subItem);
        }
        menu.Items.Add(addItem);

        var settingsItem = new MenuItem { Header = Loc.Get("Menu_AppSettings"), Icon = MenuIcon(IconTune) };
        settingsItem.Click += OnOpenSettings;
        menu.Items.Add(settingsItem);

        var lockItem = new MenuItem { Icon = MenuIcon(IconLock) };
        UpdateLockHeader(lockItem);
        lockItem.Click += OnToggleLock;
        menu.Items.Add(lockItem);

        var exitItem = new MenuItem { Header = Loc.Get("Menu_Exit"), Icon = MenuIcon(IconExit) };
        exitItem.Click += OnExit;
        menu.Items.Add(exitItem);

        // Refresh dynamic headers on every open so external toggles (e.g. from Settings window)
        // show up immediately.
        menu.Opened += (_, _) => UpdateLockHeader(lockItem);
    }

    private FrameworkElement BuildMenuSectionHeader(string text)
    {
        // Use a Separator as the container: in a Menu/ContextMenu, Separator is intrinsically
        // non-interactive (no hover/focus), unlike MenuItem which always reacts. The template
        // replaces the default dashed line with a centered title TextBlock.
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(TextBlock.TextProperty, text);
        factory.SetValue(TextBlock.FontFamilyProperty, FindResource("UiFont"));
        factory.SetValue(TextBlock.FontSizeProperty, 11.0);
        factory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        factory.SetValue(TextBlock.ForegroundProperty, FindResource("MutedTextBrush"));
        factory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 3));

        var template = new ControlTemplate(typeof(Separator)) { VisualTree = factory };

        return new Separator
        {
            Template = template,
            // Cancel the Separator base style's fixed 1px height / negative margins.
            Margin = new Thickness(0),
            Height = double.NaN,
            Background = Brushes.Transparent
        };
    }

    private static void UpdateLockHeader(MenuItem item)
    {
        item.Header = App.Settings.Current.WidgetOrderLocked
            ? Loc.Get("Menu_UnlockOrder")
            : Loc.Get("Menu_LockOrder");
    }

    private void OnToggleLock(object sender, RoutedEventArgs e)
    {
        App.Settings.Current.WidgetOrderLocked = !App.Settings.Current.WidgetOrderLocked;
        App.Settings.Save();
        App.Widgets.NotifyOrderLockChanged();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // ApplySettings / RebuildPanel for all bars are driven from App-level event
        // wiring (Settings.Changed / Widgets.ActiveWidgetsChanged → ApplyAllBars /
        // RebuildAllBars). Here we only do the first paint for this window.
        _appBar = new AppBarService(this);
        ApplySettings();
        RebuildPanel();

        var hwnd = new WindowInteropHelper(this).Handle;

        // Start the legacy mover first so we always have a working virtual-desktop
        // strategy. Then try to pin the window via the immersive shell API — on success
        // we tear down the mover, since the pin is strictly better (no Hide/Show flicker,
        // no AppBar re-registration, no edge-detach bug).
        _virtualDesktopService = new VirtualDesktopService(hwnd, TimeSpan.FromMilliseconds(150));
        _virtualDesktopService.BeforeMove += OnBeforeDesktopMove;
        _virtualDesktopService.AfterMove += OnAfterDesktopMove;
        _virtualDesktopService.Start();

        // Pin attempt is deferred to ApplicationIdle priority: at OnSourceInitialized the
        // HWND exists but Windows hasn't yet registered it in the Application View
        // Collection (GetViewForHwnd returns TYPE_E_ELEMENTNOTFOUND otherwise).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!VirtualDesktopPinService.TryPinWithRetry(hwnd)) return;

            // Pin succeeded — disable the mover and unsubscribe from its events.
            if (_virtualDesktopService != null)
            {
                _virtualDesktopService.BeforeMove -= OnBeforeDesktopMove;
                _virtualDesktopService.AfterMove -= OnAfterDesktopMove;
                _virtualDesktopService.Dispose();
                _virtualDesktopService = null;
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    public void RebuildPanelPublic() => RebuildPanel();

    private void RebuildPanel()
    {
        // Abort any in-flight drag — the visual tree is about to be replaced.
        if (_ghostImage != null) CleanupDrag();

        // Snapshot current visual Y for every wrapper, keyed by InstanceId.
        // Coordinates are taken relative to RootGrid so they remain comparable
        // after wrappers move between PinnedHost and WidgetsHost.
        var oldVisualY = SnapshotWrapperYs();

        PinnedHost.Children.Clear();
        WidgetsHost.Children.Clear();

        // Filter widgets to those that belong to this monitor. Primary picks up
        // widgets with no assignment plus homeless ones (whose stored DeviceName
        // is currently disconnected). The connected-set is supplied by App so all
        // bars share a consistent snapshot of topology.
        var connected = App.GetConnectedDeviceNames();
        foreach (var (entry, widget) in App.Widgets.GetActiveForMonitor(
                     _monitor.DeviceName, _monitor.IsPrimary, connected))
        {
            var view = widget.CreateView();
            // Detach view from any prior wrapper (Grid is a Panel too).
            if (view.Parent is Panel p && p != PinnedHost && p != WidgetsHost)
                p.Children.Remove(view);
            else if (view.Parent is ContentControl cc)
                cc.Content = null;

            view.ContextMenu = BuildWidgetContextMenu(widget, entry);

            var container = BuildWidgetContainer(view, entry);
            if (entry.Pinned) PinnedHost.Children.Add(container);
            else WidgetsHost.Children.Add(container);
        }

        PinnedBorder.Visibility = PinnedHost.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdatePinnedScrollMaxHeight();

        // Now the new layout exists in the visual tree — animate from old positions.
        AnimatePanelRelayout(oldVisualY);
    }

    private System.Collections.Generic.Dictionary<string, double> SnapshotWrapperYs()
    {
        var dict = new System.Collections.Generic.Dictionary<string, double>();
        foreach (var host in new Panel[] { PinnedHost, WidgetsHost })
        {
            foreach (var child in host.Children.OfType<FrameworkElement>())
            {
                if (child.Tag is not string id) continue;
                try
                {
                    double y = child.TransformToAncestor(RootGrid).Transform(new Point(0, 0)).Y;
                    dict[id] = y;
                }
                catch { }
            }
        }
        return dict;
    }

    private void AnimatePanelRelayout(System.Collections.Generic.Dictionary<string, double> oldVisualY)
    {
        if (oldVisualY.Count == 0) return;

        // Force the new layout to settle so we can read final positions.
        RootGrid.UpdateLayout();

        foreach (var host in new Panel[] { PinnedHost, WidgetsHost })
        {
            foreach (var child in host.Children.OfType<FrameworkElement>())
            {
                if (child.Tag is not string id) continue;
                if (!oldVisualY.TryGetValue(id, out var oldY)) continue;

                double newY;
                try { newY = child.TransformToAncestor(RootGrid).Transform(new Point(0, 0)).Y; }
                catch { continue; }

                double delta = oldY - newY;
                if (Math.Abs(delta) < 0.5) continue;

                var transform = child.RenderTransform as TranslateTransform;
                if (transform == null)
                {
                    transform = new TranslateTransform();
                    child.RenderTransform = transform;
                }

                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = delta;

                var anim = new DoubleAnimation
                {
                    From = delta,
                    To = 0,
                    Duration = PinReorderDuration,
                    EasingFunction = PinReorderEasing,
                    FillBehavior = FillBehavior.Stop
                };
                anim.Completed += (_, _) =>
                {
                    transform.BeginAnimation(TranslateTransform.YProperty, null);
                    transform.Y = 0;
                };
                transform.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }
    }

    private Grid BuildWidgetContainer(UserControl view, WidgetEntry entry)
    {
        var grid = new Grid { Tag = entry.InstanceId };
        grid.Children.Add(view);

        // Non-pinned widgets are reordered by dragging their header strip — the host
        // listens on the container itself (pinned widgets never participate in reorder).
        if (!entry.Pinned)
        {
            grid.PreviewMouseLeftButtonDown += WidgetContainer_MouseDown;
            grid.PreviewMouseLeftButtonUp += WidgetContainer_MouseUp;
            grid.PreviewMouseMove += WidgetContainer_MouseMove;
            grid.LostMouseCapture += WidgetContainer_LostCapture;
        }

        return grid;
    }

    private void WidgetContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (App.Settings.Current.WidgetOrderLocked) return;
        if (sender is not Grid grid || grid.Tag is not string instanceId) return;

        // Only the top header strip starts a drag — presses lower down belong to the body.
        if (e.GetPosition(grid).Y > HeaderDragZonePx) return;
        // Presses on interactive controls (buttons, fields, lists, sliders) are left alone.
        if (IsInteractiveOriginalSource(e.OriginalSource as DependencyObject, grid)) return;

        var entry = App.Settings.Current.Widgets.FirstOrDefault(w => w.InstanceId == instanceId);
        if (entry == null) return;

        _dragContainer = grid;
        _dragEntry = entry;
        _dragStartPoint = e.GetPosition(WidgetsHost);
        _dragArmed = true;
        // No capture and no Handled here — a plain click, double-click rename, or the
        // context menu in the header must still work. Capture happens in StartDrag,
        // once the pointer actually moves past the threshold.
    }

    private void WidgetContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragArmed && _dragEntry != null && _dragContainer != null)
        {
            var posInHost = e.GetPosition(WidgetsHost);

            if (_ghostImage == null)
            {
                if (Math.Abs(posInHost.Y - _dragStartPoint.Y) < DragThreshold &&
                    Math.Abs(posInHost.X - _dragStartPoint.X) < DragThreshold)
                    return;
                StartDrag(e);
            }

            UpdateDrag(posInHost, e);
            return;
        }

        // Not dragging — hint with a move-cursor that the header strip is grabbable.
        if (sender is Grid grid)
        {
            bool overHeader = !App.Settings.Current.WidgetOrderLocked
                && e.GetPosition(grid).Y <= HeaderDragZonePx
                && !IsInteractiveOriginalSource(e.OriginalSource as DependencyObject, grid);
            grid.Cursor = overHeader ? Cursors.Hand : null;
        }
    }

    // Walks up from the pressed element; true if an interactive control is hit before
    // reaching the widget container, so a header drag must not hijack that interaction.
    private static bool IsInteractiveOriginalSource(DependencyObject? source, DependencyObject container)
    {
        while (source != null && source != container)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Primitives.ScrollBar
                or ComboBox or ComboBoxItem or Slider or MenuItem)
                return true;

            source = source is System.Windows.Media.Visual v
                ? System.Windows.Media.VisualTreeHelper.GetParent(v)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void StartDrag(MouseEventArgs e)
    {
        if (_dragContainer == null) return;

        // Snapshot the container into a bitmap for the ghost preview
        var rect = _dragContainer.RenderSize;
        if (rect.Width < 1 || rect.Height < 1) return;

        // An actual drag gesture is confirmed — capture the mouse now (deferred from
        // mouse-down so plain header clicks and title rename are not hijacked).
        _dragContainer.CaptureMouse();
        _dragCaptured = true;
        _dragContainer.Cursor = Cursors.Hand;

        var rtb = new RenderTargetBitmap(
            (int)Math.Round(rect.Width),
            (int)Math.Round(rect.Height),
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(_dragContainer);
        rtb.Freeze();

        _ghostImage = new Image
        {
            Source = rtb,
            Width = rect.Width,
            Height = rect.Height,
            Opacity = 0.75,
            IsHitTestVisible = false
        };
        DragOverlay.Children.Add(_ghostImage);

        // Pin the ghost so cursor stays at the same relative Y where the user grabbed
        var posInContainer = e.GetPosition(_dragContainer);
        _ghostOffsetY = posInContainer.Y;

        // Dim the source so user sees what's being moved
        _dragContainer.Opacity = 0.3;

        // Insertion line — full width minus small horizontal padding
        _insertionLine = new Border
        {
            Height = 3,
            Background = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(1.5)
        };
        // Initial placement: at source's current position (no visible move yet)
        int sourceIdx = WidgetsHost.Children.IndexOf(_dragContainer);
        if (sourceIdx < 0) sourceIdx = 0;
        WidgetsHost.Children.Insert(sourceIdx, _insertionLine);
    }

    private void UpdateDrag(Point posInHost, MouseEventArgs e)
    {
        if (_ghostImage == null) return;

        // Ghost follows the cursor in the overlay canvas
        var posInOverlay = e.GetPosition(DragOverlay);
        Canvas.SetLeft(_ghostImage, 0);
        Canvas.SetTop(_ghostImage, posInOverlay.Y - _ghostOffsetY);

        // Move insertion line to the gap closest to mouse Y
        int targetIndex = ComputeInsertionIndex(posInHost.Y);
        MoveInsertionLineTo(targetIndex);

        // Auto-scroll when near top/bottom edge of viewport
        var posInViewer = e.GetPosition(PanelScroll);
        if (posInViewer.Y < AutoScrollEdgePx) SetAutoScroll(-1);
        else if (posInViewer.Y > PanelScroll.ActualHeight - AutoScrollEdgePx) SetAutoScroll(1);
        else SetAutoScroll(0);
    }

    private int ComputeInsertionIndex(double mouseY)
    {
        // Walk children (excluding insertion line) and find first whose vertical
        // midpoint is below mouseY — insertion goes immediately before it.
        int gapIndex = 0;
        for (int i = 0; i < WidgetsHost.Children.Count; i++)
        {
            var child = WidgetsHost.Children[i];
            if (child == _insertionLine) continue;
            if (child is not FrameworkElement fe) { gapIndex++; continue; }

            var midY = fe.TranslatePoint(new Point(0, fe.RenderSize.Height / 2), WidgetsHost).Y;
            if (mouseY < midY) return gapIndex;
            gapIndex++;
        }
        return gapIndex;
    }

    private void MoveInsertionLineTo(int gapIndex)
    {
        if (_insertionLine == null) return;

        // Translate "gap index ignoring insertion line" → actual position in WidgetsHost.Children
        int currentLineIdx = WidgetsHost.Children.IndexOf(_insertionLine);
        int actualIndex = gapIndex;
        if (currentLineIdx >= 0 && currentLineIdx < actualIndex) actualIndex++;

        if (currentLineIdx == actualIndex) return;
        if (currentLineIdx >= 0) WidgetsHost.Children.Remove(_insertionLine);

        // Re-clamp after removal shifts indices
        int target = gapIndex;
        if (target < 0) target = 0;
        if (target > WidgetsHost.Children.Count) target = WidgetsHost.Children.Count;
        WidgetsHost.Children.Insert(target, _insertionLine);
    }

    private void WidgetContainer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var container = sender as Grid;

        // Capture all state BEFORE releasing mouse capture — calling ReleaseMouseCapture
        // triggers LostMouseCapture synchronously, which would otherwise clear our state.
        bool hadDrag = _ghostImage != null;
        var dragEntry = _dragEntry;
        string? anchorInstanceId = null;

        if (hadDrag && _insertionLine != null)
        {
            int lineIdx = WidgetsHost.Children.IndexOf(_insertionLine);
            for (int i = lineIdx + 1; i < WidgetsHost.Children.Count; i++)
            {
                var child = WidgetsHost.Children[i];
                if (child is Grid g && g.Tag is string id && id != dragEntry?.InstanceId)
                {
                    anchorInstanceId = id;
                    break;
                }
            }
        }

        // Cleanup visuals + state, THEN release capture (so LostCapture handler sees clean state)
        bool wasCaptured = _dragCaptured;
        CleanupDrag();
        if (wasCaptured) container?.ReleaseMouseCapture();

        if (hadDrag && dragEntry != null)
        {
            App.Widgets.MoveBeforeEnabled(dragEntry.InstanceId, anchorInstanceId);
        }
    }

    private void WidgetContainer_LostCapture(object sender, MouseEventArgs e)
    {
        // Capture can be lost if the user alt-tabs or another modal grabs focus
        if (_ghostImage != null) CleanupDrag();
        else if (_dragArmed) CleanupDrag();
    }

    private void CleanupDrag()
    {
        if (_ghostImage != null)
        {
            DragOverlay.Children.Remove(_ghostImage);
            _ghostImage = null;
        }
        if (_insertionLine != null)
        {
            WidgetsHost.Children.Remove(_insertionLine);
            _insertionLine = null;
        }
        if (_dragContainer != null)
        {
            _dragContainer.Opacity = 1.0;
            _dragContainer.Cursor = null;
            _dragContainer = null;
        }
        _dragEntry = null;
        _dragArmed = false;
        _dragCaptured = false;
        SetAutoScroll(0);
    }

    private void SetAutoScroll(int direction)
    {
        if (_autoScrollDirection == direction)
        {
            if (direction == 0 && _autoScrollTimer != null) _autoScrollTimer.Stop();
            return;
        }
        _autoScrollDirection = direction;

        if (direction == 0)
        {
            _autoScrollTimer?.Stop();
            return;
        }

        if (_autoScrollTimer == null)
        {
            _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _autoScrollTimer.Tick += (_, _) =>
            {
                var offset = PanelScroll.VerticalOffset + _autoScrollDirection * AutoScrollSpeedPxPerTick;
                offset = Math.Max(0, Math.Min(offset, PanelScroll.ScrollableHeight));
                PanelScroll.ScrollToVerticalOffset(offset);
            };
        }
        _autoScrollTimer.Start();
    }

    // Material Design "monitor" icon — 24×24 viewBox.
    private const string IconMonitor =
        "M21,16H3V4H21M21,2H3C1.89,2 1,2.89 1,4V16A2,2 0 0,0 3,18H10V20H8V22H16V20H14V18H21A2,2 0 0,0 23,16V4C23,2.89 22.1,2 21,2Z";

    private ContextMenu BuildWidgetContextMenu(
        Shelf.Sdk.IWidget widget,
        Shelf.Models.WidgetEntry entry)
    {
        var menu = new ContextMenu();
        var instanceId = entry.InstanceId;

        menu.Items.Add(BuildMenuSectionHeader(Loc.Get("Menu_Section_Widget")));

        var pinItem = new MenuItem { Icon = MenuIcon(IconPin) };
        UpdatePinHeader(pinItem, instanceId);
        pinItem.Click += (_, _) => App.Widgets.TogglePin(instanceId);
        menu.Items.Add(pinItem);

        // "Move to monitor ▶" submenu. Shown only when more than one monitor is
        // connected — single-monitor users would just see a pointless one-item list.
        var monitors = App.Monitors.Monitors;
        if (monitors.Count > 1)
        {
            var moveItem = new MenuItem
            {
                Header = Loc.Get("Menu_MoveToMonitor"),
                Icon = MenuIcon(IconMonitor)
            };
            foreach (var mon in monitors)
            {
                bool isCurrentTarget = string.Equals(
                    entry.MonitorDeviceName ?? App.Monitors.Primary.DeviceName,
                    mon.DeviceName, StringComparison.OrdinalIgnoreCase);
                // Primary entry encodes assignment as null (so widget follows the
                // primary if it changes). Non-primary targets store the explicit
                // DeviceName.
                string? assignTo = mon.IsPrimary ? null : mon.DeviceName;
                var subItem = new MenuItem
                {
                    Header = isCurrentTarget ? $"✓ {mon.DisplayName}" : mon.DisplayName
                };
                subItem.Click += (_, _) => App.Widgets.MoveToMonitor(instanceId, assignTo);
                moveItem.Items.Add(subItem);
            }
            menu.Items.Add(moveItem);
        }

        if (widget.HasSettings)
        {
            var settingsItem = new MenuItem { Header = Loc.Get("Menu_WidgetSettings"), Icon = MenuIcon(IconCog) };
            settingsItem.Click += (_, _) => widget.ShowSettings(this);
            menu.Items.Add(settingsItem);
        }

        var deleteItem = new MenuItem { Header = Loc.Get("Menu_DeleteWidget"), Icon = MenuIcon(IconTrash) };
        deleteItem.Click += (_, _) => ConfirmAndDeleteWidget(instanceId);
        menu.Items.Add(deleteItem);

        // Single divider between widget actions and panel actions.
        menu.Items.Add(new Separator());

        AppendPanelMenuItems(menu);

        // Pin label can change from elsewhere (programmatic toggle, etc.) — refresh on every open.
        menu.Opened += (_, _) => UpdatePinHeader(pinItem, instanceId);

        return menu;
    }

    private static void UpdatePinHeader(MenuItem item, string instanceId)
    {
        item.Header = App.Widgets.IsPinned(instanceId) ? Loc.Get("Menu_Unpin") : Loc.Get("Menu_Pin");
    }

    private void ConfirmAndDeleteWidget(string instanceId)
    {
        // Look up label fresh — user might have renamed the widget after the menu was built.
        var widget = App.Widgets.GetInstance(instanceId);
        var label = widget?.InstanceLabel ?? Loc.Get("Widget_Fallback");

        var result = DarkMessageBox.Show(
            this,
            Loc.Format("Confirm_DeleteWidget", label),
            Loc.Get("Title_Confirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            App.Widgets.RemoveInstance(instanceId);
        }
    }

    private bool _appBarWasRegistered;

    private void OnBeforeDesktopMove()
    {
        var cfg = App.Settings.GetMonitorConfig(_monitor.DeviceName);
        _appBarWasRegistered = !cfg.AutoHide;
        if (_appBarWasRegistered) _appBar?.Unregister();
    }

    private void OnAfterDesktopMove()
    {
        if (!_appBarWasRegistered) return;
        var cfg = App.Settings.GetMonitorConfig(_monitor.DeviceName);

        // Defer AppBar.Register until WPF finishes restoring composition state after the
        // SW_HIDE/SW_SHOW cycle the virtual-desktop mover does. If we Register synchronously
        // here, HwndSource.CompositionTarget can transiently be null, AppBarService then
        // falls back to dpiX=1.0, and on high-DPI monitors that produces wrong coordinates —
        // the panel ends up offset from the screen edge instead of flush against it.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            VdLog($"  [{_monitor.DeviceName}] Before Register: Left={Left:F1} Top={Top:F1} W={Width:F1} H={Height:F1} " +
                  $"monitorW={_monitor.BoundsPx.Width} dpiX={_monitor.DpiX:F3}");
            _appBar?.Register(_monitor, cfg.Side, cfg.Width);
            VdLog($"  [{_monitor.DeviceName}] After Register:  Left={Left:F1} Top={Top:F1} W={Width:F1} H={Height:F1}");
        }), DispatcherPriority.Background);
    }

    private static readonly string VdLogPath =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Shelf.vd.log");

    private static void VdLog(string msg)
    {
        try { System.IO.File.AppendAllText(VdLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _virtualDesktopService?.Dispose();
        _appBar?.Unregister();
    }

    public void ApplySettings()
    {
        BeginAnimation(LeftProperty, null);

        var s = App.Settings.Current;
        var cfg = App.Settings.GetMonitorConfig(_monitor.DeviceName);

        // All coordinates are computed from this monitor's physical bounds, divided
        // by its per-monitor DPI to land in WPF DIPs. AutoStart toggle is global so
        // only the primary window applies it (idempotent on subsequent monitors).
        double dpiY = _monitor.DpiY <= 0 ? 1.0 : _monitor.DpiY;
        double monitorTop = _monitor.BoundsPx.Top / dpiY;
        double monitorHeight = _monitor.BoundsPx.Height / dpiY;

        if (cfg.AutoHide)
        {
            _appBar?.Unregister();

            Height = monitorHeight;
            Top = monitorTop;
            Width = cfg.Width;
            Left = VisibleLeft(cfg, _monitor);
            _hidden = false;

            if (!IsMouseOver) ScheduleHide();
        }
        else
        {
            // Set all four coords before re-registering the AppBar — assigning Width alone
            // would leave the window briefly at the old Left, visually shifting it.
            // AppBar.SetPosition will then re-confirm (or adjust) these values.
            Top = monitorTop;
            Height = monitorHeight;
            Width = cfg.Width;
            Left = VisibleLeft(cfg, _monitor);
            _appBar?.Register(_monitor, cfg.Side, cfg.Width);
            _hidden = false;
            CancelHideTimer();
        }

        if (_monitor.IsPrimary) AutoStartService.Set(s.AutoStart);
    }

    private static double VisibleLeft(MonitorPanelConfig cfg, MonitorInfo monitor)
    {
        double dpiX = monitor.DpiX <= 0 ? 1.0 : monitor.DpiX;
        return cfg.Side == BarSide.Right
            ? (monitor.BoundsPx.Right / dpiX) - cfg.Width
            : monitor.BoundsPx.Left / dpiX;
    }

    private static double HiddenLeft(MonitorPanelConfig cfg, MonitorInfo monitor)
    {
        double dpiX = monitor.DpiX <= 0 ? 1.0 : monitor.DpiX;
        return cfg.Side == BarSide.Right
            ? (monitor.BoundsPx.Right / dpiX) - HiddenTriggerWidthPx
            : (monitor.BoundsPx.Left / dpiX) - (cfg.Width - HiddenTriggerWidthPx);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        CancelHideTimer();
        if (App.Settings.GetMonitorConfig(_monitor.DeviceName).AutoHide && _hidden) SlideIn();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (App.Settings.GetMonitorConfig(_monitor.DeviceName).AutoHide) ScheduleHide();
    }

    private void ScheduleHide()
    {
        CancelHideTimer();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _hideTimer.Tick += (_, _) =>
        {
            CancelHideTimer();
            if (!_hidden) SlideOut();
        };
        _hideTimer.Start();
    }

    private void CancelHideTimer()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
    }

    private void SlideIn()
    {
        _hidden = false;
        AnimateLeft(VisibleLeft(App.Settings.GetMonitorConfig(_monitor.DeviceName), _monitor));
    }

    private void SlideOut()
    {
        _hidden = true;
        AnimateLeft(HiddenLeft(App.Settings.GetMonitorConfig(_monitor.DeviceName), _monitor));
    }

    private void AnimateLeft(double to)
    {
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        BeginAnimation(LeftProperty, anim);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        w.ShowDialog();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
