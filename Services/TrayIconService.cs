using System;
using System.Drawing;
using System.Windows.Forms;
using Shelf.Sdk;
using Shelf.Views;

namespace Shelf.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainWindow _bar;
    private SettingsWindow? _openSettings;
    private TrayPalette _palette;
    private readonly Action _themeChangedHandler;

    public TrayIconService(MainWindow bar)
    {
        _bar = bar;
        _palette = TrayPalette.For(Theme.Current);

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(_palette),
            Visible = false,
            Text = Loc.Get("App_Name")
        };

        _icon.ContextMenuStrip = BuildContextMenu(_palette);
        _icon.DoubleClick += (_, _) => ToggleBar();

        // Live theme switch: rebuild the menu and the fallback icon so the WinForms
        // surface follows the WPF theme. Theme.ThemeChanged fires after the colour
        // dictionary swap, so reads of Theme.Current here see the new value.
        _themeChangedHandler = OnThemeChanged;
        Theme.ThemeChanged += _themeChangedHandler;
    }

    private void OnThemeChanged()
    {
        _palette = TrayPalette.For(Theme.Current);

        var oldMenu = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = BuildContextMenu(_palette);
        try { oldMenu?.Dispose(); } catch { }

        var oldIcon = _icon.Icon;
        _icon.Icon = CreateIcon(_palette);
        try { oldIcon?.Dispose(); } catch { }
    }

    private ContextMenuStrip BuildContextMenu(TrayPalette palette)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = palette.SurfaceColor,
            ForeColor = palette.TextColor,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new ThemedColorTable(palette)) { RoundedEdges = false },
            Padding = new Padding(3),
            DropShadowEnabled = false,
            Font = new Font("Segoe UI", 9F)
        };

        AddItem(menu, palette, Loc.Get("Tray_ShowHide"), () => ToggleBar());
        AddItem(menu, palette, Loc.Get("Tray_Settings"), () => OpenSettings());
        menu.Items.Add(BuildSeparator(palette));
        AddItem(menu, palette, Loc.Get("Tray_Exit"), () => System.Windows.Application.Current.Shutdown());

        return menu;
    }

    private static void AddItem(ContextMenuStrip menu, TrayPalette palette, string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            BackColor = palette.SurfaceColor,
            ForeColor = palette.TextColor,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0)
        };
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
    }

    private static ToolStripSeparator BuildSeparator(TrayPalette palette)
    {
        return new ToolStripSeparator
        {
            BackColor = palette.SurfaceColor,
            ForeColor = palette.SeparatorColor,
            Margin = new Padding(0, 2, 0, 2)
        };
    }

    public void Show() => _icon.Visible = true;

    private void ToggleBar()
    {
        if (_bar.IsVisible) _bar.Hide();
        else _bar.Show();
    }

    private void OpenSettings()
    {
        if (_openSettings != null)
        {
            _openSettings.Activate();
            return;
        }

        _openSettings = new SettingsWindow
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
        };
        _openSettings.Closed += (_, _) => _openSettings = null;
        _openSettings.Show();
    }

    private static Icon CreateIcon(TrayPalette palette)
    {
        try
        {
            var streamInfo = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/shelf.ico", UriKind.Absolute));
            if (streamInfo != null)
            {
                using var s = streamInfo.Stream;
                return new Icon(s);
            }
        }
        catch { }

        // Fallback - тільки якщо .ico не знайдено. Малюємо літеру в кольорах теми,
        // щоб у світлій темі вона теж читалася.
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(palette.FallbackIconBackColor);
            g.FillRectangle(bg, 0, 0, 32, 32);
            using var font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(palette.FallbackIconForeColor);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Loc.Current == AppLanguage.En ? "S" : "П", font, fg, new RectangleF(0, 0, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        try { Theme.ThemeChanged -= _themeChangedHandler; } catch { }
        try { _icon.Visible = false; } catch { }
        try { _icon.Dispose(); } catch { }
    }

    /// <summary>
    /// WinForms-side mirror of the WPF brush palette. Two factories produce the Dark and
    /// Light variants; values mirror what <c>Themes/Theme.Dark.xaml</c> and
    /// <c>Themes/Theme.Light.xaml</c> define for the corresponding brush keys. Keep them
    /// in sync if the XAML palette is tweaked.
    /// </summary>
    private sealed class TrayPalette
    {
        public Color SurfaceColor { get; }
        public Color HoverColor { get; }
        public Color BorderColor { get; }
        public Color TextColor { get; }
        public Color SeparatorColor { get; }
        public Color FallbackIconBackColor { get; }
        public Color FallbackIconForeColor { get; }

        private TrayPalette(Color surface, Color hover, Color border, Color text,
                            Color separator, Color iconBack, Color iconFore)
        {
            SurfaceColor = surface;
            HoverColor = hover;
            BorderColor = border;
            TextColor = text;
            SeparatorColor = separator;
            FallbackIconBackColor = iconBack;
            FallbackIconForeColor = iconFore;
        }

        public static TrayPalette For(AppTheme theme) =>
            theme == AppTheme.Light ? Light() : Dark();

        private static TrayPalette Dark() => new(
            surface:   Color.FromArgb(0x25, 0x25, 0x26),  // SurfaceBrush
            hover:     Color.FromArgb(0x2D, 0x2D, 0x30),  // HoverBrush
            border:    Color.FromArgb(0x3F, 0x3F, 0x46),  // BorderBrush
            text:      Color.FromArgb(0xEA, 0xEA, 0xEA),  // PrimaryTextBrush
            separator: Color.FromArgb(0x5C, 0x5C, 0x62),  // AccentBrush
            iconBack:  Color.FromArgb(0x1E, 0x1E, 0x1E),  // BackgroundBrush
            iconFore:  Color.FromArgb(0xEA, 0xEA, 0xEA)); // PrimaryTextBrush

        private static TrayPalette Light() => new(
            surface:   Color.FromArgb(0xFF, 0xFF, 0xFF),  // SurfaceBrush (white card)
            hover:     Color.FromArgb(0xEC, 0xEC, 0xEC),  // HoverBrush
            border:    Color.FromArgb(0xD0, 0xD0, 0xD5),  // BorderBrush
            text:      Color.FromArgb(0x1E, 0x1E, 0x1E),  // PrimaryTextBrush
            separator: Color.FromArgb(0x70, 0x70, 0x75),  // AccentBrush
            iconBack:  Color.FromArgb(0xF5, 0xF5, 0xF5),  // BackgroundBrush (off-white)
            iconFore:  Color.FromArgb(0x1E, 0x1E, 0x1E)); // PrimaryTextBrush
    }

    private sealed class ThemedColorTable : ProfessionalColorTable
    {
        private readonly TrayPalette _p;
        public ThemedColorTable(TrayPalette palette) { _p = palette; }

        public override Color ToolStripDropDownBackground => _p.SurfaceColor;
        public override Color ToolStripBorder => _p.BorderColor;
        public override Color MenuBorder => _p.BorderColor;
        public override Color MenuStripGradientBegin => _p.SurfaceColor;
        public override Color MenuStripGradientEnd => _p.SurfaceColor;
        public override Color ImageMarginGradientBegin => _p.SurfaceColor;
        public override Color ImageMarginGradientMiddle => _p.SurfaceColor;
        public override Color ImageMarginGradientEnd => _p.SurfaceColor;
        public override Color MenuItemSelected => _p.HoverColor;
        public override Color MenuItemSelectedGradientBegin => _p.HoverColor;
        public override Color MenuItemSelectedGradientEnd => _p.HoverColor;
        public override Color MenuItemBorder => _p.HoverColor;
        public override Color MenuItemPressedGradientBegin => _p.HoverColor;
        public override Color MenuItemPressedGradientEnd => _p.HoverColor;
        public override Color MenuItemPressedGradientMiddle => _p.HoverColor;
        public override Color SeparatorDark => _p.SeparatorColor;
        public override Color SeparatorLight => _p.SeparatorColor;
        public override Color CheckBackground => _p.HoverColor;
        public override Color CheckSelectedBackground => _p.HoverColor;
        public override Color CheckPressedBackground => _p.HoverColor;
        public override Color ButtonSelectedHighlight => _p.HoverColor;
        public override Color ButtonSelectedHighlightBorder => _p.HoverColor;
    }
}
