using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shelf.Sdk;
using IoPath = System.IO.Path;

namespace Shelf.Widgets.Photos;

public partial class PhotosWidget : UserControl, IWidget
{
    private static string DefaultTitle => Loc.Get("Photos_Name");

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif",
        // HEIC/HEIF — supported by WIC on current Windows (HEIF Image Extensions).
        ".heic", ".heif",
        // RAW camera formats — require manufacturer/Microsoft Raw Image Extension codecs
        // installed system-wide; if WIC can't decode the file we silently skip it.
        ".cr2", ".nef", ".arw", ".dng"
    };

    public enum OrderMode { Random, DateNewest, NameAscending }
    public enum TransitionMode { ZoomBlend, CrossFade, FadeBlack, Slide, None }

    public class WidgetState
    {
        public string Title { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public bool IncludeSubfolders { get; set; } = true;
        public OrderMode Order { get; set; } = OrderMode.Random;
        public TransitionMode Transition { get; set; } = TransitionMode.CrossFade;
        public bool KenBurnsEnabled { get; set; } = true;
        public int IntervalSeconds { get; set; } = 30;
        public bool Grayscale { get; set; } = false;
        public int DarkenPercent { get; set; } = 0;
    }

    private WidgetState _state = new();
    private List<string> _files = new();
    private int _currentIndex = -1;
    private DateTime _folderMtime = DateTime.MinValue;
    private bool _paused;
    private bool _isEditingTitle;
    private bool _editTitleCanceled;
    private readonly DispatcherTimer _slideTimer;
    private DispatcherTimer? _copyFeedbackTimer;
    private readonly Random _rng = new();

    // Alternates direction between consecutive photos so the slideshow doesn't feel monotonous.
    // true → zoom-in (1.00 → 1.10), false → zoom-out (1.10 → 1.00).
    private bool _kenBurnsZoomIn = true;

    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(800));
    private static readonly Duration HalfTransitionDuration = new(TimeSpan.FromMilliseconds(400));
    private static readonly IEasingFunction TransitionEasing = new CubicEase { EasingMode = EasingMode.EaseInOut };
    private static readonly IEasingFunction KenBurnsEasing = new SineEase { EasingMode = EasingMode.EaseInOut };

    public string Id => "photos";
    public string DisplayName => DefaultTitle;
    public string Description => Loc.Get("Photos_Desc");
    public bool HasSettings => true;

    public string InstanceLabel =>
        string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;

    public PhotosWidget()
    {
        InitializeComponent();

        _slideTimer = new DispatcherTimer();
        _slideTimer.Tick += (_, _) => ShowNext(advance: true);

        Loaded += (_, _) => ApplyState();
        Unloaded += (_, _) =>
        {
            _slideTimer.Stop();
            StopAllAnimations();
        };
    }

    public UserControl CreateView() => this;

    public void ShowSettings(Window owner)
    {
        var dlg = new PhotosSettingsDialog(_state)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg.ShowDialog() == true)
        {
            _state.FolderPath = dlg.FolderPathResult;
            _state.IncludeSubfolders = dlg.IncludeSubfoldersResult;
            _state.Order = dlg.OrderResult;
            _state.Transition = dlg.TransitionResult;
            _state.KenBurnsEnabled = dlg.KenBurnsEnabledResult;
            _state.IntervalSeconds = dlg.IntervalSecondsResult;
            _state.Grayscale = dlg.GrayscaleResult;
            _state.DarkenPercent = dlg.DarkenPercentResult;

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
            if (loaded != null)
            {
                // Migration: the previous "ZoomBlend" transition introduced a pop-in zoom that
                // clashed with Ken Burns; CrossFade is the new default replacement.
                if (loaded.Transition == TransitionMode.ZoomBlend)
                    loaded.Transition = TransitionMode.CrossFade;
                _state = loaded;
            }
        }
        catch { }
    }

    private void ApplyState()
    {
        ApplyDarkenOverlay();
        ReloadFileList(resetIndex: true);
        UpdateSlideshowTimer();
        UpdatePlayPauseIcon();
    }

    private void ApplyDarkenOverlay()
    {
        if (DarkenOverlay == null) return;
        int pct = Math.Clamp(_state.DarkenPercent, 0, 100);
        bool show = pct > 0 && PlaceholderPanel?.Visibility != Visibility.Visible;
        DarkenOverlay.Opacity = pct / 100.0;
        DarkenOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // The photo area is locked to a 4:3 aspect ratio anchored to the panel's width.
    // We listen for size changes on PhotoContainer itself and:
    //  (a) on width change, set Height = Width × 3/4 (re-triggers SizeChanged with
    //      HeightChanged-only, which we still need to handle for the clip below);
    //  (b) on any size change, refresh Clip to a rounded rectangle so children
    //      (the two Image layers) are visually clipped to the CornerRadius=4 of the
    //      Border. Border doesn't clip its children by CornerRadius on its own.
    private void PhotoContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            double w = e.NewSize.Width;
            if (w > 0) PhotoContainer.Height = w * 3.0 / 4.0;
        }

        double sw = e.NewSize.Width;
        double sh = e.NewSize.Height;
        if (sw > 0 && sh > 0)
        {
            PhotoContainer.Clip = new RectangleGeometry(new Rect(0, 0, sw, sh), 4, 4);
        }
    }

    private void ReloadFileList(bool resetIndex)
    {
        _files.Clear();

        if (string.IsNullOrWhiteSpace(_state.FolderPath) || !Directory.Exists(_state.FolderPath))
        {
            _currentIndex = -1;
            ClearImages();
            ShowPlaceholder(string.IsNullOrWhiteSpace(_state.FolderPath)
                ? Loc.Get("Photos_NoFolder")
                : Loc.Get("Photos_FolderNotFound"));
            return;
        }

        try
        {
            var opt = _state.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var all = Directory.EnumerateFiles(_state.FolderPath, "*.*", opt)
                .Where(p => SupportedExtensions.Contains(IoPath.GetExtension(p)));

            IEnumerable<string> ordered = _state.Order switch
            {
                OrderMode.DateNewest => all.OrderByDescending(p =>
                {
                    try { return File.GetLastWriteTime(p); } catch { return DateTime.MinValue; }
                }),
                OrderMode.NameAscending => all.OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
                _ => all.OrderBy(_ => _rng.Next()),
            };

            _files = ordered.ToList();
            _folderMtime = SafeFolderMtime();
        }
        catch
        {
            _files.Clear();
        }

        if (_files.Count == 0)
        {
            _currentIndex = -1;
            ClearImages();
            ShowPlaceholder(Loc.Get("Photos_NoPhotos"));
            return;
        }

        HidePlaceholder();
        if (resetIndex || _currentIndex < 0 || _currentIndex >= _files.Count)
        {
            _currentIndex = 0;
            LoadCurrent();
        }
    }

    private DateTime SafeFolderMtime()
    {
        try { return Directory.GetLastWriteTime(_state.FolderPath); }
        catch { return DateTime.MinValue; }
    }

    private void UpdateSlideshowTimer()
    {
        _slideTimer.Stop();
        if (_paused) return;
        if (_state.IntervalSeconds <= 0) return;
        if (_files.Count < 2) return;

        _slideTimer.Interval = TimeSpan.FromSeconds(_state.IntervalSeconds);
        _slideTimer.Start();
    }

    private void ShowNext(bool advance)
    {
        if (_files.Count == 0) { ReloadFileList(resetIndex: true); return; }

        // Refresh list if folder changed since last scan.
        var mtime = SafeFolderMtime();
        if (mtime != _folderMtime)
        {
            ReloadFileList(resetIndex: false);
            if (_files.Count == 0) return;
        }

        if (advance)
        {
            if (_state.Order == OrderMode.Random && _files.Count > 1)
            {
                int next;
                do { next = _rng.Next(_files.Count); } while (next == _currentIndex);
                _currentIndex = next;
            }
            else
            {
                _currentIndex = (_currentIndex + 1) % _files.Count;
            }
        }

        LoadCurrent();
    }

    private void ShowPrevious()
    {
        if (_files.Count == 0) return;
        if (_state.Order == OrderMode.Random && _files.Count > 1)
        {
            int prev;
            do { prev = _rng.Next(_files.Count); } while (prev == _currentIndex);
            _currentIndex = prev;
        }
        else
        {
            _currentIndex = (_currentIndex - 1 + _files.Count) % _files.Count;
        }
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        if (_currentIndex < 0 || _currentIndex >= _files.Count) return;

        var path = _files[_currentIndex];
        var img = TryLoadBitmap(path);

        // If the file is broken / removed, drop it and try the next one.
        int attempts = 0;
        while (img == null && _files.Count > 0 && attempts < _files.Count)
        {
            _files.RemoveAt(_currentIndex);
            if (_files.Count == 0) break;
            if (_currentIndex >= _files.Count) _currentIndex = 0;
            path = _files[_currentIndex];
            img = TryLoadBitmap(path);
            attempts++;
        }

        if (img == null)
        {
            ClearImages();
            ShowPlaceholder(Loc.Get("Photos_LoadFailed"));
            return;
        }

        HidePlaceholder();
        TransitionTo(img);
    }

    private BitmapSource? TryLoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            // Decode at roughly 2× the widget's pixel height to keep quality up when Ken Burns
            // zooms in (up to 1.10×) without paying for full-resolution photos in RAM.
            // The widget is 4:3; at first paint ActualHeight can be 0, so fall back to 300px.
            double containerH = PhotoContainer.ActualHeight;
            if (containerH <= 0) containerH = 300;
            int targetPx = Math.Max(200, (int)(containerH * 2.5));

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // release file lock immediately
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelHeight = targetPx;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            if (_state.Grayscale)
            {
                // FormatConvertedBitmap maps RGB → luminance once at load time;
                // cheaper than a per-frame ShaderEffect, and the result is freezable.
                var gray = new FormatConvertedBitmap(bmp, PixelFormats.Gray32Float, null, 0);
                gray.Freeze();
                return gray;
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ===== Transitions =====

    private void StopAllAnimations()
    {
        PhotoImageFront.BeginAnimation(UIElement.OpacityProperty, null);
        PhotoImageBack.BeginAnimation(UIElement.OpacityProperty, null);
        FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        FrontTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        FrontTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        BackScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BackScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BackTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        BackTranslate.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void ClearImages()
    {
        StopAllAnimations();
        PhotoImageFront.Source = null;
        PhotoImageBack.Source = null;
        PhotoImageFront.Opacity = 1;
        PhotoImageBack.Opacity = 0;
        FrontScale.ScaleX = FrontScale.ScaleY = 1;
        FrontTranslate.X = FrontTranslate.Y = 0;
    }

    private void TransitionTo(BitmapSource newImg)
    {
        // First-time load (no prior photo) or transitions disabled — skip the cross-fade.
        bool hasPrevious = PhotoImageFront.Source != null;
        bool skipTransition = !hasPrevious || _state.Transition == TransitionMode.None;

        // IMPORTANT: capture the front layer's CURRENT scale BEFORE stopping animations.
        // BeginAnimation(prop, null) reverts the property to its base value, so reading
        // afterwards would give 1.0 even if the photo was visually at 1.10. That used to
        // cause a "snap" from the zoomed scale back to 1.0 the instant the transition began.
        // Pan is always 0 by design (photos must stay centered), so we don't track it.
        double prevSx = FrontScale.ScaleX;
        double prevSy = FrontScale.ScaleY;

        PhotoImageFront.BeginAnimation(UIElement.OpacityProperty, null);
        FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        FrontTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        FrontTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        if (!skipTransition)
        {
            // Pin back (previous photo) at its true last-seen scale; pan = 0 (always centered).
            PhotoImageBack.Source = PhotoImageFront.Source;
            BackScale.ScaleX = prevSx;
            BackScale.ScaleY = prevSy;
            BackTranslate.X = 0;
            BackTranslate.Y = 0;
            PhotoImageBack.BeginAnimation(UIElement.OpacityProperty, null);
            PhotoImageBack.Opacity = 1.0;

            // Seed the new photo at the SAME scale as the previous one's end.
            // Both layers at identical transform during cross-fade → no scale jump.
            PhotoImageFront.Source = newImg;
            FrontScale.ScaleX = prevSx;
            FrontScale.ScaleY = prevSy;
            FrontTranslate.X = 0;
            FrontTranslate.Y = 0;
            PhotoImageFront.Opacity = 1.0;
        }
        else
        {
            PhotoImageBack.BeginAnimation(UIElement.OpacityProperty, null);
            PhotoImageBack.Opacity = 0;
            PhotoImageBack.Source = null;

            // First photo: reset front to baseline.
            PhotoImageFront.Source = newImg;
            FrontScale.ScaleX = 1.0;
            FrontScale.ScaleY = 1.0;
            FrontTranslate.X = 0;
            FrontTranslate.Y = 0;
            PhotoImageFront.Opacity = 1.0;
        }

        if (skipTransition)
        {
            StartKenBurnsIfEnabled();
            return;
        }

        switch (_state.Transition)
        {
            case TransitionMode.CrossFade: ApplyCrossFadeTransition(); break;
            case TransitionMode.FadeBlack: ApplyFadeBlackTransition(); break;
            case TransitionMode.Slide: ApplySlideTransition(); break;
            // ZoomBlend is legacy; migration in LoadState maps it to CrossFade, but a
            // freshly-set value (or in-memory default before save) falls through here as well.
            default: ApplyCrossFadeTransition(); break;
        }
    }

    private void ApplyCrossFadeTransition()
    {
        PhotoImageFront.Opacity = 0;
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TransitionDuration,
            EasingFunction = TransitionEasing,
            FillBehavior = FillBehavior.HoldEnd
        };
        fadeIn.Completed += (_, _) => OnTransitionCompleted();
        PhotoImageFront.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        var fadeOut = new DoubleAnimation
        {
            From = PhotoImageBack.Opacity,
            To = 0,
            Duration = TransitionDuration,
            EasingFunction = TransitionEasing,
            FillBehavior = FillBehavior.HoldEnd
        };
        PhotoImageBack.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ApplyFadeBlackTransition()
    {
        // Two-phase: back fades out fully (revealing the container's dark background),
        // then front fades in.
        PhotoImageFront.Opacity = 0;

        var backOut = new DoubleAnimation
        {
            From = PhotoImageBack.Opacity,
            To = 0,
            Duration = HalfTransitionDuration,
            EasingFunction = TransitionEasing,
            FillBehavior = FillBehavior.HoldEnd
        };
        backOut.Completed += (_, _) =>
        {
            var frontIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = HalfTransitionDuration,
                EasingFunction = TransitionEasing,
                FillBehavior = FillBehavior.HoldEnd
            };
            frontIn.Completed += (_, _) => OnTransitionCompleted();
            PhotoImageFront.BeginAnimation(UIElement.OpacityProperty, frontIn);
        };
        PhotoImageBack.BeginAnimation(UIElement.OpacityProperty, backOut);
    }

    private void ApplySlideTransition()
    {
        double width = PhotoContainer.ActualWidth > 0 ? PhotoContainer.ActualWidth : 300;
        PhotoImageFront.Opacity = 1;
        FrontTranslate.X = width;

        var slideEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var slideIn = new DoubleAnimation
        {
            From = width,
            To = 0,
            Duration = TransitionDuration,
            EasingFunction = slideEasing,
            FillBehavior = FillBehavior.HoldEnd
        };
        slideIn.Completed += (_, _) =>
        {
            FrontTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            FrontTranslate.X = 0;
            OnTransitionCompleted();
        };
        FrontTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);

        var fadeOut = new DoubleAnimation
        {
            From = PhotoImageBack.Opacity,
            To = 0,
            Duration = TransitionDuration,
            EasingFunction = slideEasing
        };
        PhotoImageBack.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void OnTransitionCompleted()
    {
        PhotoImageBack.BeginAnimation(UIElement.OpacityProperty, null);
        PhotoImageBack.Opacity = 0;
        PhotoImageBack.Source = null;
        StartKenBurnsIfEnabled();
    }

    // ===== Ken Burns =====

    private void StartKenBurnsIfEnabled()
    {
        // Stop any in-flight animations, but PRESERVE FrontScale.ScaleX/Y — TransitionTo
        // seeded them to match the previous photo's end so the cross-fade was seamless.
        // BeginAnimation(null) would snap them back to base; snapshot then re-apply.
        double currentSx = FrontScale.ScaleX;
        double currentSy = FrontScale.ScaleY;
        FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        FrontTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        FrontTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        FrontScale.ScaleX = currentSx;
        FrontScale.ScaleY = currentSy;
        // Pan is always 0 — photos must stay centered in the widget.
        FrontTranslate.X = 0;
        FrontTranslate.Y = 0;

        if (!_state.KenBurnsEnabled) return;
        if (PhotoImageFront.Source is not BitmapSource) return;

        const double minScale = 1.0;
        const double maxScale = 1.10;

        // Animate to the extreme opposite of where we currently are. This gives natural
        // zoom-in/zoom-out alternation AND visual continuity (the new photo inherits the
        // previous photo's end state, then animates onward without any jump).
        double startScale = Math.Clamp(currentSx, minScale, maxScale);
        double mid = (minScale + maxScale) / 2.0;
        double endScale = startScale >= mid ? minScale : maxScale;

        // _kenBurnsZoomIn is kept harmlessly for compatibility; direction is now derived
        // from the current scale rather than from this flag.
        _kenBurnsZoomIn = !_kenBurnsZoomIn;

        double durationSec = _state.IntervalSeconds > 0 ? _state.IntervalSeconds : 60;
        var dur = new Duration(TimeSpan.FromSeconds(durationSec));

        var sx = new DoubleAnimation
        {
            From = startScale, To = endScale,
            Duration = dur, EasingFunction = KenBurnsEasing,
            FillBehavior = FillBehavior.HoldEnd
        };
        var sy = new DoubleAnimation
        {
            From = startScale, To = endScale,
            Duration = dur, EasingFunction = KenBurnsEasing,
            FillBehavior = FillBehavior.HoldEnd
        };

        FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }

    // ===== Placeholder =====

    private void ShowPlaceholder(string text)
    {
        PlaceholderText.Text = text;
        PlaceholderPanel.Visibility = Visibility.Visible;
        PhotoImageFront.Visibility = Visibility.Collapsed;
        PhotoImageBack.Visibility = Visibility.Collapsed;
        ApplyDarkenOverlay();
    }

    private void HidePlaceholder()
    {
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        PhotoImageFront.Visibility = Visibility.Visible;
        PhotoImageBack.Visibility = Visibility.Visible;
        ApplyDarkenOverlay();
    }

    private void UpdatePlayPauseIcon()
    {
        if (PlayPauseIconPath == null || PlayPauseButton == null) return;

        if (_paused || _state.IntervalSeconds <= 0)
        {
            PlayPauseIconPath.Data = Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z");
            PlayPauseButton.ToolTip = Loc.Get("Tip_Play");
        }
        else
        {
            PlayPauseIconPath.Data = Geometry.Parse("M14,19H18V5H14M6,19H10V5H6V19Z");
            PlayPauseButton.ToolTip = Loc.Get("Tip_Pause");
        }
    }

    // ===== Navigation buttons =====

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPrevious();
        UpdateSlideshowTimer();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        ShowNext(advance: true);
        UpdateSlideshowTimer();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.IntervalSeconds <= 0) return;
        _paused = !_paused;
        UpdatePlayPauseIcon();
        UpdateSlideshowTimer();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (PhotoImageFront.Source is not BitmapSource bs) return;

        try
        {
            Clipboard.SetImage(bs);
        }
        catch { return; }

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

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_state.FolderPath) || !Directory.Exists(_state.FolderPath)) return;
        try
        {
            if (_currentIndex >= 0 && _currentIndex < _files.Count && File.Exists(_files[_currentIndex]))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_files[_currentIndex]}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _state.FolderPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void PlaceholderButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner != null) ShowSettings(owner);
    }

    private void PhotoContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && _currentIndex >= 0 && _currentIndex < _files.Count)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _files[_currentIndex],
                    UseShellExecute = true
                });
            }
            catch { }
            e.Handled = true;
        }
    }

    // ===== Title rename =====

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
            _state.Title = (string.IsNullOrEmpty(newTitle) || newTitle == DefaultTitle)
                ? ""
                : newTitle;
            TitleText.Text = string.IsNullOrWhiteSpace(_state.Title) ? DefaultTitle : _state.Title;
            WidgetServices.RequestSaveStates();
        }

        TitleEdit.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        _editTitleCanceled = false;
    }
}
