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
    private readonly EventHandler<bool> _onThemeChanged;
    private ContextMenuStrip _menu;

    // The rows that have to say what is true now, not what was true when the
    // menu was built. The submenu parents are kept so their ticks can be
    // re-synced, and the folder list rebuilt, each time the menu opens.
    private ToolStripMenuItem _toggleItem = new();
    private ToolStripMenuItem _clearItem = new();
    private ToolStripMenuItem _inboxItem = new();
    private ToolStripMenuItem _clipboardItem = new();
    private ToolStripMenuItem _folderItem = new();
    private ToolStripMenuItem _keepItem = new();
    private ToolStripMenuItem _sizeItem = new();
    private ToolStripMenuItem _shortcutItem = new();
    private ToolStripMenuItem? _shortcutCurrent;

    private System.Drawing.Icon? _plainIcon;
    private System.Drawing.Icon? _clipboardIcon;
    private IntPtr _clipboardHandle;

    public TrayService()
    {
        _onLanguageChanged = (_, _) => RebuildMenu();
        _onThemeChanged = (_, _) => RebuildMenu();
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

        // The icons are drawn in the menu's own text colour, so a theme switch
        // needs them drawn again or they turn invisible.
        ThemeManager.ThemeChanged += _onThemeChanged;
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


    // ---- the menu --------------------------------------------------------

    // Segoe Fluent Icons glyphs, one per menu row (showcase 6). Written as
    // escapes: they are private-use codepoints, and a mangled glyph is the one
    // thing that makes a native menu look like a mock-up.
    private const string GlyphToggle = "\uE700";     // the line, folded or not
    private const string GlyphClear = "\uE74D";      // trash can
    private const string GlyphCamera = "\uE722";     // screenshots taken
    private const string GlyphCopy = "\uE8C8";       // copied images
    private const string GlyphFolder = "\uE8B7";     // watched folders
    private const string GlyphCount = "\uE8EF";      // how many are kept
    private const string GlyphSize = "\uE790";       // small / medium / large
    private const string GlyphKey = "\uE765";        // the shortcut
    private const string GlyphSound = "\uE767";      // sounds
    private const string GlyphPower = "\uE7E8";      // open at login
    private const string GlyphUpdate = "\uE72C";     // check for updates
    private const string GlyphQuit = "\uE711";       // quit

    /// <summary>The menu, in the order the showcase draws it: the line itself,
    /// the two capture sources, the four submenus, then the three switches,
    /// updates and quit.</summary>
    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Opening += (_, _) => RefreshMenu();

        // "Show / hide line": one row that says what it will do, with the
        // global shortcut spelled out beside it. The shortcut is display only
        // here - the app's own hotkey owns the keystroke.
        _toggleItem = new ToolStripMenuItem(Loc.Get("Tray.ShowLine"), TrayIcons.Get(GlyphToggle),
            (_, _) => App.Instance?.Line?.TogglePin());
        _toggleItem.ShowShortcutKeys = false;
        m.Items.Add(_toggleItem);

        // Greyed out while the line is empty: a clear button that does nothing
        // reads as broken.
        _clearItem = new ToolStripMenuItem(Loc.Get("Tray.ClearAll"), TrayIcons.Get(GlyphClear),
            (_, _) => App.CaptureStore.Clear());
        m.Items.Add(_clearItem);

        m.Items.Add("-");

        _inboxItem = CheckItem(Loc.Get("Tray.Inbox"), GlyphCamera, Settings.Current.InboxEnabled,
            on => InboxMode.SetEnabled(on));
        m.Items.Add(_inboxItem);

        _clipboardItem = CheckItem(Loc.Get("Menu.WatchClipboard"), GlyphCopy, Settings.Current.WatchClipboard,
            on =>
            {
                Settings.Current.WatchClipboard = on;
                Settings.Current.Save();
            });
        m.Items.Add(_clipboardItem);

        _folderItem = BuildFolderMenu();
        m.Items.Add(_folderItem);

        _keepItem = new ToolStripMenuItem(Loc.Get("Menu.KeepOnLine"), TrayIcons.Get(GlyphCount));
        BuildKeepMenu(_keepItem);
        m.Items.Add(_keepItem);

        _sizeItem = new ToolStripMenuItem(Loc.Get("Menu.Size"), TrayIcons.Get(GlyphSize));
        BuildSizeMenu(_sizeItem);
        m.Items.Add(_sizeItem);

        _shortcutItem = new ToolStripMenuItem(Loc.Get("Menu.Shortcut"), TrayIcons.Get(GlyphKey));
        BuildShortcutMenu(_shortcutItem);
        m.Items.Add(_shortcutItem);

        m.Items.Add("-");

        var sounds = CheckItem(Loc.Get("Tray.Sounds"), GlyphSound, Settings.Current.Sounds,
            on => App.CaptureStore.Sounds = on);
        m.Items.Add(sounds);

        var login = CheckItem(Loc.Get("Tray.OpenAtLogin"), GlyphPower, Settings.Current.LaunchAtLogin,
            on =>
            {
                Settings.Current.LaunchAtLogin = on;
                Settings.Current.Save();
                StartupService.SetEnabled(on);
            });
        m.Items.Add(login);

        m.Items.Add(new ToolStripMenuItem(Loc.Get("Tray.CheckForUpdates"), TrayIcons.Get(GlyphUpdate),
            (_, _) => UpdateChecker.Run()));

        m.Items.Add("-");

        m.Items.Add(new ToolStripMenuItem(Loc.Get("Tray.Quit"), TrayIcons.Get(GlyphQuit),
            (_, _) => App.Instance.Shutdown()));

        return m;
    }

    /// <summary>A row with a check box in front of it, the way the four switches
    /// in the showcase's menu read.</summary>
    private static ToolStripMenuItem CheckItem(string text, string glyph, bool on, Action<bool> changed)
    {
        var item = new ToolStripMenuItem(text, TrayIcons.Get(glyph))
        {
            CheckOnClick = true,
            Checked = on,
        };
        item.CheckedChanged += (_, _) => changed(item.Checked);
        return item;
    }

    /// <summary>
    /// The watched folders: each opens the folder it watches, then a separator,
    /// "add folder" and "stop watching" for the ones already on the list. The
    /// main screenshots folder is on the list too, so there is still a way to
    /// open where the captures land.
    /// </summary>
    private ToolStripMenuItem BuildFolderMenu()
    {
        var parent = new ToolStripMenuItem(Loc.Get("Menu.ExtraFolders"), TrayIcons.Get(GlyphFolder));
        RebuildFolderMenu(parent);
        return parent;
    }

    private void RebuildFolderMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var folders = new List<string> { AppPaths.ScreenshotsFolder };
        folders.AddRange(Settings.Current.ExtraFolders.Where(f => !string.IsNullOrWhiteSpace(f)));

        foreach (var folder in folders)
        {
            var name = string.IsNullOrWhiteSpace(folder) ? folder : Path.GetFileName(folder.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name)) name = folder;

            var open = new ToolStripMenuItem(name) { ToolTipText = folder };
            open.Click += (_, _) => OpenFolder(folder);
            parent.DropDownItems.Add(open);
        }

        parent.DropDownItems.Add("-");

        var add = new ToolStripMenuItem(Loc.Get("Menu.AddFolder"), null,
            (_, _) => OnOpenSettings("capture"));
        add.Enabled = Settings.Current.ExtraFolders.Count < Settings.MaxExtraFolders;
        parent.DropDownItems.Add(add);

        if (Settings.Current.ExtraFolders.Count > 0)
        {
            var stop = new ToolStripMenuItem(Loc.Get("Menu.StopWatching"));
            foreach (var folder in Settings.Current.ExtraFolders)
            {
                var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
                var entry = new ToolStripMenuItem(name);
                entry.Click += (_, _) =>
                {
                    Settings.Current.ExtraFolders.RemoveAll(f =>
                        string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                    Settings.Current.Save();
                    App.Instance?.Watch?.Restart();
                };
                stop.DropDownItems.Add(entry);
            }
            parent.DropDownItems.Add(stop);
        }
    }

    /// <summary>
    /// Keep on line: as many as fit, or 25 / 50 / 100. The numbers are the
    /// original's <c>Line.keepChoices</c>; the line keeps that many captures
    /// before the oldest one falls off.
    /// </summary>
    private void BuildKeepMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var choices = new List<(string Label, int? Value)>
        {
            (Loc.Get("Menu.KeepFit"), null), ("25", 25), ("50", 50), ("100", 100),
        };

        foreach (var (label, value) in choices)
        {
            var entry = new ToolStripMenuItem(label)
            {
                Checked = Settings.Current.KeepOnLine == value,
                Tag = value,
            };
            entry.Click += (_, _) =>
            {
                Settings.Current.KeepOnLine = value;
                Settings.Current.Save();
                SelectOnly(parent, entry);
            };
            parent.DropDownItems.Add(entry);
        }
    }

    private void BuildSizeMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        foreach (var (label, value) in new (string, CardSize)[]
                 {
                     (Loc.Get("Menu.SizeSmall"), CardSize.Small),
                     (Loc.Get("Menu.SizeMedium"), CardSize.Medium),
                     (Loc.Get("Menu.SizeLarge"), CardSize.Large),
                 })
        {
            var entry = new ToolStripMenuItem(label)
            {
                Checked = Settings.Current.Size == value,
                Tag = value,
            };
            entry.Click += (_, _) =>
            {
                Settings.Current.Size = value;
                Settings.Current.Save();
                SelectOnly(parent, entry);
            };
            parent.DropDownItems.Add(entry);
        }
    }

    /// <summary>Shortcut: what it is now, and how to change it. Changing is the
    /// settings window's shortcut page, which records a key combination.</summary>
    private void BuildShortcutMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        _shortcutCurrent = new ToolStripMenuItem(Settings.Current.Shortcut?.Display ?? Loc.Get("Menu.ShortcutNone"))
        {
            Enabled = false,
        };
        parent.DropDownItems.Add(_shortcutCurrent);

        parent.DropDownItems.Add(new ToolStripMenuItem(Loc.Get("Menu.ShortcutChange"), null,
            (_, _) => OnOpenSettings("shortcuts")));
    }

    /// <summary>One tick, where the cursor is: the row a click just picked.</summary>
    private static void SelectOnly(ToolStripMenuItem parent, ToolStripMenuItem chosen)
    {
        foreach (ToolStripMenuItem child in parent.DropDownItems)
            child.Checked = ReferenceEquals(child, chosen);
    }

    /// <summary>Puts the tick back on the row for <paramref name="value"/>,
    /// which is how the submenus are re-synced when the menu opens.</summary>
    private static void SelectValue(ToolStripMenuItem parent, object? value)
    {
        foreach (ToolStripMenuItem child in parent.DropDownItems)
        {
            if (child.Tag is not null) child.Checked = Equals(child.Tag, value);
        }
    }

    /// <summary>
    /// The menu says what is true right now, not what was true when it was
    /// built: the line may have been revealed or emptied since. Windows asks
    /// for this every time the menu opens, so this runs on Opening only.
    /// </summary>
    private void RefreshMenu()
    {
        var open = App.Instance?.Line?.IsOpen ?? false;
        _toggleItem.Text = Loc.Get(open ? "Tray.HideLine" : "Tray.ShowLine");

        var shortcut = Settings.Current.Shortcut?.Display;
        _toggleItem.ShortcutKeyDisplayString = string.IsNullOrEmpty(shortcut)
            ? Loc.Get("Menu.ShortcutNone")
            : shortcut;

        _clearItem.Enabled = App.CaptureStore.LiveCount > 0;

        _inboxItem.Checked = Settings.Current.InboxEnabled;
        _inboxItem.Enabled = InboxMode.Unavailable is null;
        _inboxItem.ToolTipText = Loc.Get("Settings.InboxDesc");

        _clipboardItem.Checked = Settings.Current.WatchClipboard;

        SelectValue(_keepItem, Settings.Current.KeepOnLine);
        SelectValue(_sizeItem, Settings.Current.Size);
        RebuildFolderMenu(_folderItem);
        if (_shortcutCurrent is not null)
        {
            _shortcutCurrent.Text = Settings.Current.Shortcut?.Display ?? Loc.Get("Menu.ShortcutNone");
        }
    }

    private static void OpenFolder(string folder)
    {
        if (Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
    }

    /// <summary>The settings window, on the page named, opened once.</summary>
    private static void OnOpenSettings(string? page = null)
    {
        var existing = Application.Current.Windows
            .OfType<Views.SettingsWindow>()
            .FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            if (page is not null) existing.SelectPage(page);
            return;
        }
        var w = new Views.SettingsWindow();
        w.Show();
        if (page is not null) w.SelectPage(page);
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
        ThemeManager.ThemeChanged -= _onThemeChanged;
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
