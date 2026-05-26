using System;
using System.Windows;

namespace Shelf.Sdk;

/// <summary>
/// Supported UI themes. Persisted in settings.json via <c>AppSettings.Theme</c>.
/// </summary>
public enum AppTheme
{
    Dark,
    Light
}

/// <summary>
/// Central theme helper shared by the host and every widget plugin.
///
/// On startup the host calls <see cref="Initialize"/> with the theme from settings.
/// That merges the matching <c>Themes/Theme.*.xaml</c> colour dictionary into
/// <c>Application.Resources</c> as the FIRST merged dictionary, so the brush keys
/// (BackgroundBrush, SurfaceBrush, etc.) are resolved by <c>{DynamicResource ...}</c>
/// inside Theme.xaml's styles and every consumer XAML.
///
/// Unlike <see cref="Loc"/>, the theme can be changed live via <see cref="Apply"/> -
/// DynamicResource consumers re-resolve immediately. After Apply the
/// <see cref="ThemeChanged"/> event fires so non-WPF consumers (e.g. the WinForms
/// tray menu and the DWM title-bar flag) can react.
/// </summary>
public static class Theme
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Fired after <see cref="Apply"/> swaps the colour dictionary.</summary>
    public static event Action? ThemeChanged;

    private static ResourceDictionary? _colours;

    /// <summary>
    /// Loads the colour dictionary for <paramref name="theme"/>. Must be called once
    /// at startup, before any window is shown. Does NOT raise <see cref="ThemeChanged"/>.
    /// </summary>
    public static void Initialize(AppTheme theme)
    {
        Apply(theme, raiseChanged: false);
    }

    /// <summary>
    /// Swaps the active colour dictionary to <paramref name="theme"/> at runtime.
    /// DynamicResource references re-resolve immediately. Raises <see cref="ThemeChanged"/>.
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        Apply(theme, raiseChanged: true);
    }

    private static void Apply(AppTheme theme, bool raiseChanged)
    {
        Current = theme;

        var app = Application.Current;
        if (app == null) return;

        var uri = new Uri(
            theme == AppTheme.Light
                ? "pack://application:,,,/Shelf.Sdk;component/Themes/Theme.Light.xaml"
                : "pack://application:,,,/Shelf.Sdk;component/Themes/Theme.Dark.xaml",
            UriKind.Absolute);

        var dict = new ResourceDictionary { Source = uri };

        if (_colours != null)
            app.Resources.MergedDictionaries.Remove(_colours);
        _colours = dict;
        // Insert FIRST so colour keys are resolved before the styles dictionary that
        // sits later in the merged list and references them via DynamicResource.
        app.Resources.MergedDictionaries.Insert(0, dict);

        if (raiseChanged)
        {
            try { ThemeChanged?.Invoke(); }
            catch { /* subscribers must not crash the theme switch */ }
        }
    }
}
