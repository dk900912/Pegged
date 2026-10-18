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
    private readonly EventHandler _onLanguageChanged;
    private ContextMenuStrip _menu;

    private System.Drawing.Icon? _plainIcon;
    private System.Drawing.Icon? _clipboardIcon;
    private IntPtr _clipboardHandle;

    public TrayService()
    {
        _onLanguageChanged = (_, _) => RebuildMenu();
        _menu = BuildMenu();

        _icon = new NotifyIcon
        {
            Icon = PlainIcon(),
            Text = "Pegged",
            Visible = true,
            ContextMenuStrip = _menu,
        };

        _icon.DoubleClick += (_, _) => OnOpenSettings();

        // The tooltip says what the line is doing, and the icon changes when
        // the clipboard is being watched (spec 3.7).
        App.CaptureStore.Changed += (_, _) => UpdateStatus();
        Settings.Saved += (_, _) => UpdateStatus();
        UpdateStatus();

        // Localize: rebuild the whole menu when the language changes. Patching
        // by index broke as soon as an item was inserted, and subscribing
        // inside BuildMenu would stack a handler on every rebuild.
        Loc.LanguageChanged += _onLanguageChanged;
    }

    /// <summary>The app icon as it ships.</summary>
    private System.Drawing.Icon PlainIcon()
    {
        _plainIcon ??= System.Drawing.Icon.ExtractAssociatedIcon(typeof(TrayService).Assembly.Location)
                       ?? System.Drawing.SystemIcons.Application;
        return _plainIcon;
    }

    /// <summary>
    /// The same icon with a filled accent dot in the corner: the state marker
    /// for "copied images are being picked up". Drawn rather than shipped so
    /// there is no second asset to keep in step.
    /// </summary>
    private System.Drawing.Icon ClipboardIcon()
    {
        if (_clipboardIcon is not null) return _clipboardIcon;

        try
        {
            using var source = PlainIcon().ToBitmap();
            using var canvas = new System.Drawing.Bitmap(source.Width, source.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            using (var g = System.Drawing.Graphics.FromImage(canvas))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.DrawImage(source, 0, 0, source.Width, source.Height);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                var size = Math.Max(8, canvas.Width / 2);
                var x = canvas.Width - size - 1;
                var y = canvas.Height - size - 1;

                using var ring = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                using var dot = new System.Drawing.SolidBrush(
                    System.Drawing.Color.FromArgb(0x0F, 0x6C, 0xBD));
                g.FillEllipse(ring, x - 1, y - 1, size + 2, size + 2);
                g.FillEllipse(dot, x, y, size, size);
            }

            _clipboardHandle = canvas.GetHicon();
            _clipboardIcon = System.Drawing.Icon.FromHandle(_clipboardHandle);
            return _clipboardIcon;
        }
        catch (Exception)
        {
            return PlainIcon();
        }
    }

    /// <summary>Tooltip and icon state: how much is on the line, and whether
    /// the clipboard is being watched.</summary>
    private void UpdateStatus()
    {
        var count = App.CaptureStore.LiveCount;
        var text = count == 0 ? Loc.Get("Tray.Empty") : Loc.Format("Tray.OnLine", count);
        if (Settings.Current.WatchClipboard) text += " · " + Loc.Get("Settings.Clipboard");

        // NotifyIcon refuses more than 63 characters.
        _icon.Text = text.Length <= 63 ? text : text[..60] + "…";
        _icon.Icon = Settings.Current.WatchClipboard ? ClipboardIcon() : PlainIcon();
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

        // Capture sources: clipboard takeover and the reveal condition. Both
        // are the two switches users touch most often.
        var clipboard = new ToolStripMenuItem(Loc.Get("Settings.Clipboard"))
        {
            CheckOnClick = true,
            Checked = Settings.Current.WatchClipboard,
        };
        clipboard.CheckedChanged += (_, _) =>
        {
            Settings.Current.WatchClipboard = clipboard.Checked;
            Settings.Current.Save();
        };
        m.Items.Add(clipboard);

        var topEdge = new ToolStripMenuItem(Loc.Get("Settings.RevealOnTopEdge"))
        {
            CheckOnClick = true,
            Checked = Settings.Current.RevealOnTopEdge,
        };
        topEdge.CheckedChanged += (_, _) =>
        {
            Settings.Current.RevealOnTopEdge = topEdge.Checked;
            Settings.Current.Save();
        };
        m.Items.Add(topEdge);

        // Keep-on-line and card size, as submenus (spec 3.7).
        var keep = new ToolStripMenuItem(Loc.Get("Settings.KeepOnLine"));
        foreach (var (label, value) in new (string, int?)[]
                 {
                     (Loc.Get("Menu.KeepFit"), null), ("8", 8), ("12", 12),
                     ("16", 16), ("20", 20), ("32", 32),
                 })
        {
            var entry = new ToolStripMenuItem(label) { Checked = Settings.Current.KeepOnLine == value };
            entry.Click += (_, _) =>
            {
                Settings.Current.KeepOnLine = value;
                Settings.Current.Save();
                foreach (ToolStripMenuItem sibling in keep.DropDownItems) sibling.Checked = false;
                entry.Checked = true;
            };
            keep.DropDownItems.Add(entry);
        }
        m.Items.Add(keep);

        var size = new ToolStripMenuItem(Loc.Get("Settings.Size"));
        foreach (var (label, value) in new[]
                 {
                     (Loc.Get("Menu.SizeSmall"), CardSize.Small),
                     (Loc.Get("Menu.SizeMedium"), CardSize.Medium),
                     (Loc.Get("Menu.SizeLarge"), CardSize.Large),
                 })
        {
            var entry = new ToolStripMenuItem(label) { Checked = Settings.Current.Size == value };
            entry.Click += (_, _) =>
            {
                Settings.Current.Size = value;
                Settings.Current.Save();
                foreach (ToolStripMenuItem sibling in size.DropDownItems) sibling.Checked = false;
                entry.Checked = true;
            };
            size.DropDownItems.Add(entry);
        }
        m.Items.Add(size);
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

    /// <summary>Swaps in a freshly built menu (used after a language change).</summary>
    private void RebuildMenu()
    {
        var fresh = BuildMenu();
        var old = _menu;
        _menu = fresh;
        _icon.ContextMenuStrip = fresh;
        old.Dispose();
    }

    public void Dispose()
    {
        Loc.LanguageChanged -= _onLanguageChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();

        _clipboardIcon?.Dispose();
        _clipboardIcon = null;
        if (_clipboardHandle != IntPtr.Zero)
        {
            DestroyIcon(_clipboardHandle);
            _clipboardHandle = IntPtr.Zero;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
