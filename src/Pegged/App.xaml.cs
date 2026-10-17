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

    /// <summary>The line itself: raised by the controller once start-up is done.</summary>
    internal LineController? Line { get; private set; }

    internal static App Instance => (App)System.Windows.Application.Current;

    internal static CaptureStore CaptureStore => Instance.Store;

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

        // Module 2: the line and its pills, watching every monitor's top edge.
        Line = new LineController();
        Line.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        Line.Start();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Exit += OnExit;
    }

    /// <summary>Raised when the user asks for the settings window. Module 6
    /// opens it; until then the request is simply dropped.</summary>
    internal static event EventHandler? SettingsRequested;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Settings.Current.Theme == ThemeKind.Auto) Dispatcher.Invoke(ThemeManager.Refresh);
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Line?.Dispose();
        Store.Save();
        Settings.Current.Save();
    }
}
