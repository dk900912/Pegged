using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
    /// <summary>Per-monitor-v2, the only mode where a window placed at
    /// monitor.Work actually lands there on a 125%/150% screen.</summary>
    private const int DpiAwarenessContextPerMonitorAwareV2 = -4;

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    private static Mutex? _singleInstance;

    internal CaptureStore Store { get; private set; } = null!;

    /// <summary>The line itself: raised by the controller once start-up is done.</summary>
    internal LineController? Line { get; private set; }

    /// <summary>The folder watchers, kept alive while the app is running.</summary>
    internal WatchService? Watch { get; private set; }

    /// <summary>The system-tray icon. Disposing it lets the app exit.</summary>
    internal TrayService? Tray { get; private set; } = null!;

    /// <summary>The app-wide global hotkey. Owned by the app, not by the
    /// settings window, so the shortcut keeps working after it closes.</summary>
    internal HotKeyService? HotKey { get; private set; }

    internal static App Instance => (App)System.Windows.Application.Current;

    internal static CaptureStore CaptureStore => Instance.Store;

    protected override void OnStartup(StartupEventArgs e)
    {
        // The very first thing: without per-monitor-v2 the whole app runs
        // DPI-virtualised and every placement lands in the wrong place (this
        // was measured: the line showed up at (96,96) instead of the top edge).
        SetProcessDpiAwarenessContext((IntPtr)DpiAwarenessContextPerMonitorAwareV2);

        base.OnStartup(e);

        // A crash in an overlay app is invisible: the window is gone and the
        // tray icon with it. Leave a note behind instead of vanishing.
        DispatcherUnhandledException += (_, args) => Record(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Record(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => Record(args.Exception);

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

        // One Pegged per session: a second double-click on the exe used to
        // spawn a whole second set of windows.
        _singleInstance = new Mutex(true, @"Local\Pegged.SingleInstance", out var isNew);
        if (!isNew)
        {
            // A second launch just puts the already-running Pegged back on
            // top and exits; it used to build a whole second set of windows.
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

        // Inbox mode is a system-wide redirect: it only lasts as long as we do,
        // so it is re-applied on every launch and undone on exit.
        if (Settings.Current.InboxEnabled && !InboxMode.Apply())
        {
            Settings.Current.InboxEnabled = false;
            Settings.Current.Save();
        }

        // Module 6: the shortcut lives with the app, not with the settings UI.
        HotKey = new HotKeyService(Line.Line);
        HotKey.Apply(Settings.Current.Shortcut);

        // Module 6: tray + settings + (sync) launch-at-login state.
        Settings.Current.LaunchAtLogin = StartupService.IsEnabled();
        Settings.Current.Save();
        Tray = new TrayService();
        SettingsRequested += OnSettingsRequested;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Exit += OnExit;
    }

    /// <summary>Appends a crash to %APPDATA%\Pegged\crash.log and keeps going
    /// where that is survivable.</summary>
    private static void Record(Exception? exception)
    {
        if (exception is null) return;

        try
        {
            var path = Path.Combine(AppPaths.DataFolder, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n\r\n");
        }
        catch (Exception)
        {
            // Nothing left to do if even the log cannot be written.
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e)    {
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

        // Hand the screenshot folder back to Windows before we go, or the
        // system is left pointing at a folder nobody is watching.
        if (Settings.Current.InboxEnabled) InboxMode.Restore();

        Tray?.Dispose();
        HotKey?.Dispose();
        Watch?.Dispose();
        Line?.Dispose();
        Store.Save();
        Settings.Current.Save();
        _singleInstance?.Dispose();
        _singleInstance = null;
    }
}
