using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Data;
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

        // Everything bound with {loc:Str} is bound to Strings[key], so this
        // single notification re-reads every visible label in every open
        // window. Nothing has to be reopened.
        Strings.Current.Refresh();

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

/// <summary>
/// The binding source behind every localized label. XAML binds to
/// <c>Strings.Current[Key]</c>; raising PropertyChanged for the indexer
/// ("Item[]") makes WPF re-read all of them at once.
/// </summary>
public sealed class Strings : INotifyPropertyChanged
{
    public static Strings Current { get; } = new();

    public string this[string key] => Loc.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

/// <summary>
/// Reads a localized string from XAML: <c>{loc:Str Tray.Quit}</c>.
///
/// This used to hand back a plain string, which meant a language change left
/// every window that was already open in the old language. It now hands back a
/// one-way binding to <see cref="Strings"/>, so switching language in Settings
/// relabels the settings window, the line and the crop overlay on the spot.
/// </summary>
public sealed class StrExtension : System.Windows.Markup.MarkupExtension
{
    public StrExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(System.IServiceProvider serviceProvider)
    {
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = Strings.Current,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
