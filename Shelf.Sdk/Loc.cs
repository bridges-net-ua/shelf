using System;
using System.Globalization;
using System.Windows;

namespace Shelf.Sdk;

/// <summary>
/// Supported UI languages. Persisted in settings.json via <c>AppSettings.Language</c>.
/// </summary>
public enum AppLanguage
{
    Uk,
    En
}

/// <summary>
/// Central localization helper shared by the host and every widget plugin.
///
/// On startup the host calls <see cref="Initialize"/> with the language from settings.
/// That merges the matching <c>Strings.*.xaml</c> ResourceDictionary into
/// <c>Application.Resources</c>, so XAML can resolve strings via
/// <c>{DynamicResource Key}</c> exactly like theme brushes, and code can resolve
/// them via <see cref="Get"/> / <see cref="Format"/>.
///
/// Like <see cref="Theme"/>, the language can be changed live via <see cref="Apply"/> —
/// XAML <c>{DynamicResource ...}</c> consumers re-resolve immediately. After Apply the
/// <see cref="LanguageChanged"/> event fires so imperative consumers (the WinForms tray
/// menu, code-built context menus, widget content rendered in code) can re-read their
/// strings and repaint.
/// </summary>
public static class Loc
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Uk;

    /// <summary>Fired after <see cref="Apply"/> swaps the string dictionary.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Culture used for date/number formatting that follows the UI language.</summary>
    public static CultureInfo Culture =>
        Current == AppLanguage.En
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("uk-UA");

    private static ResourceDictionary? _strings;

    /// <summary>
    /// Loads the string dictionary for <paramref name="lang"/> into the application
    /// resources. Must be called once at startup, before any window is shown.
    /// Does NOT raise <see cref="LanguageChanged"/>.
    /// </summary>
    public static void Initialize(AppLanguage lang)
    {
        Apply(lang, raiseChanged: false);
    }

    /// <summary>
    /// Swaps the active string dictionary to <paramref name="lang"/> at runtime.
    /// XAML DynamicResource references re-resolve immediately. Raises
    /// <see cref="LanguageChanged"/> so imperative consumers can repaint.
    /// </summary>
    public static void Apply(AppLanguage lang)
    {
        Apply(lang, raiseChanged: true);
    }

    private static void Apply(AppLanguage lang, bool raiseChanged)
    {
        Current = lang;

        var app = Application.Current;
        if (app == null) return;

        var uri = new Uri(
            lang == AppLanguage.En
                ? "pack://application:,,,/Shelf.Sdk;component/Strings.en.xaml"
                : "pack://application:,,,/Shelf.Sdk;component/Strings.uk.xaml",
            UriKind.Absolute);

        var dict = new ResourceDictionary { Source = uri };

        if (_strings != null)
            app.Resources.MergedDictionaries.Remove(_strings);
        _strings = dict;
        app.Resources.MergedDictionaries.Add(dict);

        if (raiseChanged)
        {
            try { LanguageChanged?.Invoke(); }
            catch { /* subscribers must not crash the language switch */ }
        }
    }

    /// <summary>Returns the localized string for <paramref name="key"/>, or the key itself if missing.</summary>
    public static string Get(string key)
    {
        var res = Application.Current?.TryFindResource(key);
        return res as string ?? key;
    }

    /// <summary>Returns the localized string for <paramref name="key"/> with <see cref="string.Format(string, object[])"/> applied.</summary>
    public static string Format(string key, params object[] args)
    {
        try { return string.Format(Get(key), args); }
        catch { return Get(key); }
    }
}
