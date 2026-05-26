using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shelf.Sdk;

namespace Shelf.Widgets.Todo;

public partial class TodoWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Todo_Name");

    private readonly DispatcherTimer _saveTimer;
    private DispatcherTimer? _copyFeedbackTimer;
    private bool _isEditingTitle;
    private bool _editTitleCanceled;

    public string Id => "todo";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Todo_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public class TodoItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get; set; } = "";
        public bool Done { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class WidgetState
    {
        public List<TodoItem> Items { get; set; } = new();
        public int Height { get; set; } = 220;
        public bool HideCompleted { get; set; } = false;
        public string Title { get; set; } = "";
    }

    private WidgetState _state = new();

    public TodoWidget()
    {
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            WidgetServices.RequestSaveStates();
        };

        Loaded += (_, _) =>
        {
            ApplyState();
            DataObject.AddPastingHandler(NewTaskInput, OnNewTaskPasting);
        };
        Unloaded += (_, _) => DataObject.RemovePastingHandler(NewTaskInput, OnNewTaskPasting);
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new TodoSettingsDialog(_state.Height, _state.HideCompleted)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state.Height = dlg.HeightResult;
            _state.HideCompleted = dlg.HideCompletedResult;
            ApplyState();
            WidgetServices.RequestSaveStates();
        }
    }

    public string SaveState() => JsonSerializer.Serialize(_state);

    public void LoadState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<WidgetState>(json);
            if (loaded != null) _state = loaded;
        }
        catch { }
    }

    private void ApplyState()
    {
        ListScroll.Height = _state.Height;
        BuildList();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void BuildList()
    {
        ItemsList.Children.Clear();
        foreach (var item in _state.Items)
        {
            if (_state.HideCompleted && item.Done) continue;
            ItemsList.Children.Add(BuildRow(item));
        }
        UpdateHeader();
        UpdateClearButton();
    }

    private UIElement BuildRow(TodoItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox
        {
            IsChecked = item.Done,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        var tb = new TextBox
        {
            Text = item.Text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2, 0, 2),
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 13,
            CaretBrush = (Brush)FindResource("PrimaryTextBrush"),
            SelectionBrush = (Brush)FindResource("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            AcceptsReturn = false
        };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        var del = new Button
        {
            Content = "×",
            FontSize = 14,
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Style = (Style)FindResource("IconButtonSubtle"),
            ToolTip = Loc.Get("Tip_Delete"),
            Margin = new Thickness(4, 0, 0, 0)
        };
        Grid.SetColumn(del, 2);
        grid.Children.Add(del);

        Action applyDoneStyle = () =>
        {
            if (item.Done)
            {
                tb.TextDecorations = TextDecorations.Strikethrough;
                tb.Foreground = (Brush)FindResource("MutedTextBrush");
            }
            else
            {
                tb.TextDecorations = null;
                tb.Foreground = (Brush)FindResource("PrimaryTextBrush");
            }
        };
        applyDoneStyle();

        cb.Click += (_, _) =>
        {
            item.Done = cb.IsChecked == true;
            applyDoneStyle();

            _state.Items.Remove(item);
            if (item.Done)
            {
                _state.Items.Add(item);
            }
            else
            {
                int insertAt = _state.Items.FindIndex(i => i.Done);
                if (insertAt < 0) _state.Items.Add(item);
                else _state.Items.Insert(insertAt, item);
            }

            if (_state.HideCompleted && item.Done)
                AnimateFadeOutAndRebuild(grid);
            else
                AnimateReorder(grid, item);

            UpdateHeader();
            UpdateClearButton();
            ScheduleSave();
        };

        tb.TextChanged += (_, _) =>
        {
            item.Text = tb.Text;
            ScheduleSave();
        };

        del.Click += (_, _) =>
        {
            _state.Items.Remove(item);
            BuildList();
            ScheduleSave();
        };

        return grid;
    }

    private void UpdateHeader()
    {
        int active = _state.Items.Count(i => !i.Done);
        var title = string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;
        if (TitleText != null) TitleText.Text = title;
        if (CountText != null) CountText.Text = $" ({active})";
        if (CopyButton != null) CopyButton.IsEnabled = active > 0;
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
        TitleDisplayPanel.Visibility = Visibility.Collapsed;
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
            _state.Title = (string.IsNullOrEmpty(newTitle) || newTitle == DefaultTitle)
                ? ""
                : newTitle;
            UpdateHeader();
            WidgetServices.RequestSaveStates();
        }

        TitleEdit.Visibility = Visibility.Collapsed;
        TitleDisplayPanel.Visibility = Visibility.Visible;
        _editTitleCanceled = false;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var active = _state.Items
            .Where(i => !i.Done && !string.IsNullOrWhiteSpace(i.Text))
            .ToList();
        if (active.Count == 0) return;

        var title = string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;
        var sb = new StringBuilder();
        sb.Append(title).AppendLine(":");
        foreach (var item in active)
            sb.Append("- ").AppendLine(item.Text.Trim());

        var text = sb.ToString().TrimEnd();
        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch { return; }

        // Brief accent-color flash on the copy icon
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

    private void UpdateClearButton()
    {
        int done = _state.Items.Count(i => i.Done);
        if (done > 0)
        {
            ClearCompletedButton.Visibility = Visibility.Visible;
            ClearCompletedButton.Content = Loc.Format("Todo_ClearCompletedN", done);
        }
        else
        {
            ClearCompletedButton.Visibility = Visibility.Collapsed;
        }
    }

    private void NewTaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = NewTaskInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) { e.Handled = true; return; }

            _state.Items.Insert(0, new TodoItem { Text = text });
            NewTaskInput.Text = "";
            BuildList();
            ScheduleSave();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            NewTaskInput.Text = "";
            e.Handled = true;
        }
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        _state.Items.RemoveAll(i => i.Done);
        BuildList();
        ScheduleSave();
    }

    private static readonly Duration ReorderDuration = new(TimeSpan.FromMilliseconds(280));
    private static readonly Duration FadeOutDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly IEasingFunction ReorderEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

    private void AnimateReorder(FrameworkElement movedRow, TodoItem movedItem)
    {
        // Map: target Children index for the moved row. In the non-HideCompleted path
        // (the only one that reaches here) item index in _state.Items maps 1:1 to Children index.
        int newIndex = _state.Items.IndexOf(movedItem);
        if (newIndex < 0) return;

        int currentIndex = ItemsList.Children.IndexOf(movedRow);
        if (currentIndex < 0 || currentIndex == newIndex) return;

        // Snapshot current visual Y for every row (layout slot + any in-flight transform offset)
        var children = ItemsList.Children.OfType<FrameworkElement>().ToList();
        var oldVisualY = new Dictionary<FrameworkElement, double>();
        foreach (var child in children)
        {
            double layoutY = LayoutInformation.GetLayoutSlot(child).Y;
            double transformY = (child.RenderTransform as TranslateTransform)?.Y ?? 0;
            oldVisualY[child] = layoutY + transformY;
        }

        // Reorder visuals (does not touch the model)
        ItemsList.Children.Remove(movedRow);
        ItemsList.Children.Insert(newIndex, movedRow);
        ItemsList.UpdateLayout();

        // For each row: animate render-transform from (old - new) back to 0
        foreach (var child in children)
        {
            double newLayoutY = LayoutInformation.GetLayoutSlot(child).Y;
            double delta = oldVisualY[child] - newLayoutY;

            var transform = child.RenderTransform as TranslateTransform;

            if (Math.Abs(delta) < 0.5)
            {
                if (transform != null && transform.Y != 0)
                {
                    transform.BeginAnimation(TranslateTransform.YProperty, null);
                    transform.Y = 0;
                }
                continue;
            }

            if (transform == null)
            {
                transform = new TranslateTransform();
                child.RenderTransform = transform;
            }

            // Reset any prior animation so we can seed a fresh starting offset.
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = delta;

            var anim = new DoubleAnimation
            {
                From = delta,
                To = 0,
                Duration = ReorderDuration,
                EasingFunction = ReorderEasing,
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

    private void AnimateFadeOutAndRebuild(FrameworkElement row)
    {
        var anim = new DoubleAnimation
        {
            From = row.Opacity,
            To = 0,
            Duration = FadeOutDuration,
            EasingFunction = ReorderEasing,
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            row.BeginAnimation(UIElement.OpacityProperty, null);
            row.Opacity = 1;
            BuildList();
        };
        row.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private const int MaxPastedTasks = 500;

    private static readonly Regex ListMarkerRegex =
        new(@"^\s*(?:\d+[.\)]\s+|[-*•–]\s+)", RegexOptions.Compiled);

    private void OnNewTaskPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)
            && !e.SourceDataObject.GetDataPresent(DataFormats.Text))
            return;

        var raw = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string
                  ?? e.SourceDataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrEmpty(raw)) return;
        if (raw.IndexOfAny(new[] { '\r', '\n' }) < 0) return; // single line — let WPF handle it

        var lines = Regex.Split(raw, "\r\n|\r|\n")
            .Select(StripListMarker)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(MaxPastedTasks)
            .ToList();

        if (lines.Count == 0) return;

        // Insert in reverse so the first pasted line ends up on top.
        for (int i = lines.Count - 1; i >= 0; i--)
            _state.Items.Insert(0, new TodoItem { Text = lines[i] });

        NewTaskInput.Text = "";
        BuildList();
        ScheduleSave();

        e.CancelCommand();
    }

    private static string StripListMarker(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";
        return ListMarkerRegex.Replace(line, "").Trim();
    }
}
