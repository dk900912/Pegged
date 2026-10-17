using System.Globalization;
using System.Resources;
using System.Windows;
using Pegged.Core;

namespace Pegged.Localization;

/// <summary>
/// The words Pegged uses. Every string lives in Resources/Strings.resx and its
/// satellite resources; this is the one door in.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Manager =
        new("Pegged.Resources.Strings", typeof(Loc).Assembly);

    public static event EventHandler? LanguageChanged;

    /// <summary>The culture resources are read from.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Applies the language from the settings, or Windows' own.</summary>
    public static void Apply(Settings settings)
    {
        Culture = settings.EffectiveCulture();
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key) => Manager.GetString(key, Culture) ?? key;

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(Culture, template, args); }
        catch (FormatException) { return template; }
    }

}

/// <summary>Reads a localized string from XAML: <c>{loc:Str Tray.Quit}</c>.</summary>
public sealed class StrExtension : System.Windows.Markup.MarkupExtension
{
    public StrExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(System.IServiceProvider serviceProvider) => Loc.Get(Key);
}
