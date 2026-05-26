using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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

    public string Id => "notes";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Notes_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public class WidgetState
    {
        public string Text { get; set; } = "";
        public int Height { get; set; } = 180;
        public string Title { get; set; } = "";
    }

    private WidgetState _state = new();

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
            ApplyState();
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
    }

    private void ApplyState()
    {
        _suppressSave = true;
        NotesText.Text = _state.Text;
        NotesText.Height = _state.Height;
        _suppressSave = false;
        UpdateTitleDisplay();
    }

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

    private void CaptureFromUi()
    {
        if (NotesText != null) _state.Text = NotesText.Text;
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
