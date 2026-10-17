using System.Windows;
using Microsoft.Win32;
using Pegged.Core;
using Pegged.Localization;

namespace Pegged;

/// <summary>
/// Pegged's entry point. It owns the start-up order: paths, settings, words,
/// theme, then whatever the later modules hang off the line.
/// </summary>
// Windows Forms is used only for the tray icon, so both applications are in
// scope and "Application" has to be spelled out here.
public partial class App : System.Windows.Application
{
    internal CaptureStore Store { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureCreated();

        var settings = Settings.Current;
        Loc.Apply(settings);
        ThemeManager.Apply(settings.Theme);

        Store = new CaptureStore();
        Store.Restore();
        Store.Changed += (_, _) => settings.Save();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Exit += OnExit;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Settings.Current.Theme == ThemeKind.Auto) Dispatcher.Invoke(ThemeManager.Refresh);
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Store.Save();
        Settings.Current.Save();
    }
}
