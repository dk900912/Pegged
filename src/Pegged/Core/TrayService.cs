using System;
using System.IO;
using System.Windows;
using Pegged.Localization;

namespace Pegged.Core;

/// <summary>
/// The notification-area icon and its right-click menu. Stays alive as long
/// as the app is running, and the app stays alive as long as the tray icon
/// is alive (so the line does not disappear the moment the user closes the
/// settings window).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayService()
    {
        _menu = BuildMenu();

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(typeof(TrayService).Assembly.Location)
                   ?? System.Drawing.SystemIcons.Application,
            Text = "Pegged",
            Visible = true,
            ContextMenuStrip = _menu,
        };

        _icon.DoubleClick += (_, _) => OnOpenSettings();
    }

    private static ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add(Loc.Get("Tray.ShowLine"), null, (_, _) => App.Instance?.Line?.TogglePin());
        m.Items.Add(Loc.Get("Tray.HideLine"), null, (_, _) => App.Instance?.Line?.Retract());
        m.Items.Add("-");
        m.Items.Add(Loc.Get("Tray.ClearAll"), null, (_, _) => App.CaptureStore.Clear());
        m.Items.Add(Loc.Get("Tray.OpenScreenshotsFolder"), null, (_, _) => OpenFolder());
        m.Items.Add("-");

        var sounds = new ToolStripMenuItem(Loc.Get("Tray.Sounds"))
        {
            CheckOnClick = true,
            Checked = Settings.Current.Sounds,
        };
        sounds.CheckedChanged += (_, _) =>
        {
            Settings.Current.Sounds = sounds.Checked;
            Settings.Current.Save();
        };
        m.Items.Add(sounds);

        var login = new ToolStripMenuItem(Loc.Get("Tray.OpenAtLogin"))
        {
            CheckOnClick = true,
            Checked = Settings.Current.LaunchAtLogin,
        };
        login.CheckedChanged += (_, _) =>
        {
            Settings.Current.LaunchAtLogin = login.Checked;
            Settings.Current.Save();
            StartupService.SetEnabled(login.Checked);
        };
        m.Items.Add(login);

        m.Items.Add(Loc.Get("Tray.CheckForUpdates"), null, (_, _) => UpdateChecker.Run());
        m.Items.Add("-");
        m.Items.Add(Loc.Get("Tray.Settings"), null, (_, _) => OnOpenSettings());
        m.Items.Add(Loc.Get("Tray.Quit"), null, (_, _) => App.Instance.Shutdown());

        // Localize: rebuild on language change.
        Loc.LanguageChanged += (_, _) =>
        {
            var items = new (int idx, string key)[]
            {
                (0, "Tray.ShowLine"), (1, "Tray.HideLine"),
                (3, "Tray.ClearAll"), (4, "Tray.OpenScreenshotsFolder"),
                (6, "Tray.Sounds"), (7, "Tray.OpenAtLogin"),
                (8, "Tray.CheckForUpdates"),
                (10, "Tray.Settings"), (11, "Tray.Quit"),
            };
            foreach (var (idx, key) in items)
                if (idx < m.Items.Count && m.Items[idx] is ToolStripMenuItem it) it.Text = Loc.Get(key);
        };

        return m;
    }

    private static void OpenFolder()
    {
        var folder = AppPaths.ScreenshotsFolder;
        if (Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
    }

    private static void OnOpenSettings()
    {
        var existing = Application.Current.Windows
            .OfType<Views.SettingsWindow>()
            .FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            return;
        }
        var w = new Views.SettingsWindow();
        w.Show();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
