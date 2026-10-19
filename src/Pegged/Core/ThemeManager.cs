using System;
using System.Windows;
using Microsoft.Win32;

namespace Pegged.Core;

/// <summary>
/// Light and dark, following Windows when asked to. A theme switch swaps one
/// resource dictionary over the other; nothing else in the app knows about it.
/// </summary>
public static class ThemeManager
{
    private const string DarkDictionary = "Themes/FluentTheme.Dark.xaml";
    private static readonly Uri DarkUri = new(DarkDictionary, UriKind.Relative);

    private static ResourceDictionary? _dark;

    public static event EventHandler<bool>? ThemeChanged;

    /// <summary>True when the app is currently dark.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Whether Windows itself is in dark mode (AppsUseLightTheme).</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Apply(ThemeKind kind)
    {
        var dark = kind switch
        {
            ThemeKind.Dark => true,
            ThemeKind.Light => false,
            _ => SystemPrefersDark(),
        };

        ApplyDark(dark);
    }

    private static void ApplyDark(bool dark)
    {
        if (IsDark == dark && _dark is not null) return;

        var app = System.Windows.Application.Current;
        if (app is null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        if (dark)
        {
            if (_dark is null)
            {
                _dark = new ResourceDictionary { Source = DarkUri };
            }

            if (!dictionaries.Contains(_dark)) dictionaries.Add(_dark);
        }
        else if (_dark is not null && dictionaries.Contains(_dark))
        {
            dictionaries.Remove(_dark);
        }

        IsDark = dark;
        ThemeChanged?.Invoke(null, dark);
    }

    /// <summary>Called when Windows changes its own theme while we run.</summary>
    public static void Refresh() => Apply(Settings.Current.Theme);
}
