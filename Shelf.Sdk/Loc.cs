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
/// The language is fixed for the lifetime of the process — changing it in Settings
/// only takes effect after an app restart.
/// </summary>
public static class Loc
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Uk;

    /// <summary>Culture used for date/number formatting that follows the UI language.</summary>
    public static CultureInfo Culture =>
        Current == AppLanguage.En
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("uk-UA");

    private static ResourceDictionary? _strings;

    /// <summary>
    /// Loads the string dictionary for <paramref name="lang"/> into the application
    /// resources. Must be called once at startup, before any window is shown.
    /// </summary>
    public static void Initialize(AppLanguage lang)
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
