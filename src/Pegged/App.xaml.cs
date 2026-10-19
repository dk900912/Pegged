using System.IO;
using System.Linq;
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

    /// <summary>The folder watchers, kept alive while the app is running.</summary>
    internal WatchService? Watch { get; private set; }

    /// <summary>The system-tray icon. Disposing it lets the app exit.</summary>
    internal TrayService? Tray { get; private set; }

    internal static App Instance => (App)System.Windows.Application.Current;

    internal static CaptureStore CaptureStore => Instance.Store;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A tiny development hook: put a picture on the clipboard and exit.
        // Used by the automated smoke test for Module 4.
        if (e.Args.Length >= 2 && e.Args[0] == "--put-clip" && File.Exists(e.Args[1]))
        {
            try
            {
                using var bmp = new System.Drawing.Bitmap(e.Args[1]);
                System.Windows.Forms.Clipboard.SetImage(bmp);
            }
            catch (Exception) { }
            Shutdown(0);
            return;
        }

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

        // Module 4: watch the screenshot folder (and the three extras) for
        // fresh captures, and listen to the clipboard.
        Watch = new WatchService();

        // Module 6: tray + settings + (sync) launch-at-login state.
        Settings.Current.LaunchAtLogin = StartupService.IsEnabled();
        Settings.Current.Save();
        Tray = new TrayService();
        SettingsRequested += OnSettingsRequested;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Exit += OnExit;
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        var existing = System.Windows.Application.Current.Windows
            .OfType<Views.SettingsWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        var w = new Views.SettingsWindow();
        w.Show();
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
        SettingsRequested -= OnSettingsRequested;
        Tray?.Dispose();
        Watch?.Dispose();
        Line?.Dispose();
        Store.Save();
        Settings.Current.Save();
    }
}
