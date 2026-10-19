using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pegged.Core;
using Pegged.Localization;

namespace Pegged.Views;

/// <summary>
/// The settings window: a side nav and a content card. The nav stays in
/// one place; the content swaps between General / Capture / Appearance /
/// Shortcuts / About.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _initializing = true;

    public SettingsWindow()
    {
        InitializeComponent();

        // Wired here rather than in the markup: the markup compiler silently
        // ignores SelectionChanged="..." on a UserControl subclass - no error,
        // no warning, the control just never tells anyone. The segmented rows
        // work only because these three lines exist.
        LangSegmented.SelectionChanged += OnLanguage;
        KeepSegmented.SelectionChanged += OnKeep;
        SizeSegmented.SelectionChanged += OnSize;

        BuildLanguageSegmented();
        BuildKeepSegmented();
        BuildSizeSegmented();
        BuildThemeCombo();
        ApplyValues();
        _initializing = false;
        NavList.SelectedIndex = 0;
        UpdateNavigation();

        // {loc:Str} is a live binding now, so the labels relabel themselves.
        // The segmented rows hold pre-built strings, so they are rebuilt here.
        Loc.LanguageChanged += OnLanguageChanged;

        // Opened from the line, which never activates: take the keyboard or
        // the window opens behind the focus and the first click only wakes it.
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => WindowFocus.Take(this)));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _initializing = true;
        try
        {
            BuildKeepSegmented();
            BuildSizeSegmented();
            BuildThemeCombo();
            UpdateExtraFoldersText();
            UpdateInboxText();
        }
        finally
        {
            _initializing = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        WindowFocus.Release();
        base.OnClosed(e);
    }

    // ---- combos ----------------------------------------------------------

    private record LangItem(string Name, string Culture)
    {
        // A row of pills shows the pill's text, which is the record's name.
        public override string ToString() => Name;
    }

    /// <summary>
    /// The language row (showcase 5): three pills for the languages Windows
    /// itself offers first — follow-the-system, Simplified Chinese and English —
    /// and a "…" pill that opens the rest. The control does the splitting, so
    /// all ten languages stay reachable without a drop-down.
    /// </summary>
    private void BuildLanguageSegmented()
    {
        var langs = new List<LangItem>
        {
            new(Loc.Get("Settings.LanguageSystem"), ""),
            new("中文（简体）",  "zh-Hans"),
            new("English",      "en"),
            new("Español",      "es"),
            new("Français",     "fr"),
            new("日本語",        "ja"),
            new("한국어",         "ko"),
            new("Português (BR)","pt-BR"),
            new("Türkçe",       "tr"),
            new("Azərbaycan",   "az"),
        };

        LangSegmented.VisibleCount = 4;
        LangSegmented.Items = langs;

        var cur = Settings.Current.Language ?? "";
        LangSegmented.SelectedValue =
            langs.FirstOrDefault(l => l.Culture == cur) ?? langs[0];
    }

    /// <summary>
    /// Keep-on-line: as many as fit, or 25/50/100. The choices are the
    /// original's (<c>Line.keepChoices</c>); 8/12/16/20/32 was never a real
    /// option and made the line hold eight screenshots and nothing more.
    /// </summary>
    private void BuildKeepSegmented()
    {
        var items = new List<KeepOption>
        {
            new(Loc.Get("Menu.KeepFit"), null),
            new("25", 25), new("50", 50), new("100", 100),
        };

        KeepSegmented.Items = items;
        KeepSegmented.SelectedValue =
            items.Find(i => i.Value == Settings.Current.KeepOnLine) ?? items[0];
    }

    private void BuildSizeSegmented()
    {
        var items = new List<SizeOption>
        {
            new(Loc.Get("Menu.SizeSmall"),  0),
            new(Loc.Get("Menu.SizeMedium"), 1),
            new(Loc.Get("Menu.SizeLarge"),  2),
        };
        SizeSegmented.Items = items;
        SizeSegmented.SelectedValue = items.Find(i => i.Value == (int)Settings.Current.Size) ?? items[1];
    }

    private void BuildThemeCombo()
    {
        var items = new List<ThemeOption>
        {
            new(Loc.Get("Settings.ThemeAuto"),  ThemeKind.Auto),
            new(Loc.Get("Settings.ThemeLight"), ThemeKind.Light),
            new(Loc.Get("Settings.ThemeDark"),  ThemeKind.Dark),
        };
        ThemeCombo.DisplayMemberPath = "Label";
        ThemeCombo.ItemsSource = items;
        ThemeCombo.SelectedItem = items.Find(i => i.Value == Settings.Current.Theme) ?? items[0];
    }

    private void ApplyValues()
    {
        var s = Settings.Current;
        SoundsSwitch.IsOn = s.Sounds;
        ClipboardSwitch.IsOn = s.WatchClipboard;
        FullScreenSwitch.IsOn = s.ShowOverFullScreenApps;
        TopEdgeSwitch.IsOn = s.RevealOnTopEdge;
        PeekSwitch.IsOn = s.PeekAtNewCapture;
        LoginSwitch.IsOn = s.LaunchAtLogin;
        RevealSlider.Value = s.RevealDelay;
        HotkeyBox.Text = s.Shortcut?.Display ?? "";
        FolderPath.Text = s.ScreenshotFolder;
        UpdateExtraFoldersText();
        InboxSwitch.IsOn = s.InboxEnabled;
        UpdateInboxText();
    }

    // ---- nav -------------------------------------------------------------

    /// <summary>Brings the page with the given tag ("general", "capture",
    /// "appearance", "shortcuts", "about") to the front. Used when something
    /// else sends the user here - the tray's "add folder" opens the capture
    /// page, its "change shortcut" opens this window's shortcut page.</summary>
    public void SelectPage(string tag)
    {
        var target = NavList.Items.OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;

        if (NavList.SelectedItem != target)
        {
            NavList.SelectedItem = target;
        }
        else
        {
            UpdateNavigation();
        }
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        var sel = (NavList.SelectedItem as ListBoxItem)?.Tag as string;
        GeneralPane.Visibility    = sel == "general"    ? Visibility.Visible : Visibility.Collapsed;
        CapturePane.Visibility    = sel == "capture"    ? Visibility.Visible : Visibility.Collapsed;
        AppearancePane.Visibility = sel == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPane.Visibility  = sel == "shortcuts"  ? Visibility.Visible : Visibility.Collapsed;
        AboutPane.Visibility      = sel == "about"      ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- handlers --------------------------------------------------------

    private void OnLanguage(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (LangSegmented.SelectedValue is LangItem l)
        {
            // An empty culture means "follow Windows": recorded as null so the
            // next launch follows whatever the shell is set to then.
            Settings.Current.Language = string.IsNullOrEmpty(l.Culture) ? null : l.Culture;
            Settings.Current.Save();
            Loc.Apply(Settings.Current);
        }
    }

    private void OnKeep(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (KeepSegmented.SelectedValue is KeepOption v) Settings.Current.KeepOnLine = v.Value;
        Settings.Current.Save();
    }

    private void OnSize(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (SizeSegmented.SelectedValue is SizeOption v) Settings.Current.Size = (CardSize)v.Value;
        Settings.Current.Save();
    }

    private void OnSounds(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.Sounds = SoundsSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (ThemeCombo.SelectedItem is ThemeOption v)
        {
            Settings.Current.Theme = v.Value;
            Settings.Current.Save();
            ThemeManager.Apply(v.Value);
        }
    }

    private void OnRevealDelay(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        Settings.Current.RevealDelay = Math.Round(RevealSlider.Value, 2);
        Settings.Current.Save();
    }

    private void OnTopEdge(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.RevealOnTopEdge = TopEdgeSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnPeek(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.PeekAtNewCapture = PeekSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnClipboard(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.WatchClipboard = ClipboardSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnFullScreen(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.ShowOverFullScreenApps = FullScreenSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.LaunchAtLogin = LoginSwitch.IsOn;
        Settings.Current.Save();
        StartupService.SetEnabled(LoginSwitch.IsOn);
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.Get("Settings.FolderChange"),
            InitialDirectory = Settings.Current.ScreenshotFolder,
        };
        if (dlg.ShowDialog(this) == true)
        {
            Settings.Current.ScreenshotFolder = dlg.FolderName;
            Settings.Current.Save();
            FolderPath.Text = dlg.FolderName;
            App.Instance?.Watch?.Restart();
        }
    }

    private void OnHotkeyDown(object sender, KeyEventArgs e)
    {
        if (_initializing) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;
        e.Handled = true;
        if (e.IsRepeat) return;

        if (key == Key.Escape)
        {
            HotkeyBox.Text = Settings.Current.Shortcut?.Display ?? "";
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var hotKey = App.Instance.HotKey;
        if (hotKey is null)
        {
            MessageBox.Show(this, "全局快捷键服务尚未就绪，未更改原快捷键。请稍后重试。",
                Loc.Get("Settings.Shortcuts"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (key == Key.Delete)
        {
            hotKey.Unregister();
            Settings.Current.Shortcut = null;
            Settings.Current.Save();
            HotkeyBox.Clear();
            return;
        }

        var shortcut = HotkeyParser.Render(Keyboard.Modifiers, key);
        var (modifiers, virtualKey) = HotkeyParser.Parse(shortcut);
        if (virtualKey == 0 || !hotKey.Register(modifiers, virtualKey))
        {
            HotkeyBox.Text = Settings.Current.Shortcut?.Display ?? "";
            MessageBox.Show(this,
                $"无法注册快捷键“{shortcut.Display}”：该组合可能已被其他程序占用或被系统保留。请换一组按键。原快捷键未更改。",
                Loc.Get("Settings.Shortcuts"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Settings.Current.Shortcut = shortcut;
        Settings.Current.Save();
        HotkeyBox.Text = shortcut.Display;
    }

    private void UpdateExtraFoldersText()
    {
        var folders = Settings.Current.ExtraFolders;
        ExtraFoldersText.Text = folders.Count == 0
            ? Loc.Get("Settings.ExtraFoldersNone")
            : string.Join("\n", folders);
    }

    private void OnAddExtraFolder(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (Settings.Current.ExtraFolders.Count >= Settings.MaxExtraFolders)
        {
            MessageBox.Show(this, Loc.Get("Settings.ExtraFoldersFull"),
                Loc.Get("Settings.Capture"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.Get("Settings.FolderAdd"),
            InitialDirectory = Settings.Current.ScreenshotFolder,
        };
        if (dlg.ShowDialog(this) != true) return;

        if (Settings.Current.ExtraFolders.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase))
            return;

        Settings.Current.ExtraFolders.Add(dlg.FolderName);
        Settings.Current.Save();
        App.Instance?.Watch?.Restart();
        UpdateExtraFoldersText();
    }

    private void OnClearExtraFolders(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.ExtraFolders.Clear();
        Settings.Current.Save();
        App.Instance?.Watch?.Restart();
        UpdateExtraFoldersText();
    }

    // ---- inbox mode ------------------------------------------------------

    /// <summary>
    /// The row says what the mode can actually do here. Where the shell has no
    /// Screenshots folder to redirect the switch is greyed out with the reason
    /// instead of being offered and then bouncing back - a switch that refuses
    /// to stay on reads as a broken app, not as a system that said no.
    /// </summary>
    private void UpdateInboxText()
    {
        if (InboxMode.Unavailable is { } why)
        {
            InboxSwitch.IsEnabled = false;
            InboxSwitch.IsOn = false;
            InboxText.Text = Loc.Format("Settings.InboxUnavailable", why);
            return;
        }

        InboxSwitch.IsEnabled = true;
        var current = InboxMode.CurrentSystemFolder() ?? Settings.Current.ScreenshotFolder ?? "";
        InboxText.Text = Loc.Format("Settings.InboxPath", current);
    }

    private void OnInbox(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        if (!InboxMode.SetEnabled(InboxSwitch.IsOn))
        {
            _initializing = true;
            InboxSwitch.IsOn = false;
            _initializing = false;

            // Inline, never a modal: Pegged never owns the foreground, so a
            // MessageBox opened from here appears behind the settings window
            // and the next click on the switch dismisses it instead of
            // reaching the switch. That is how this check used to "fail".
            InboxText.Text = Loc.Format("Settings.InboxUnavailable",
                InboxMode.LastError ?? InboxMode.Unavailable ?? "-");
            return;
        }

        UpdateInboxText();
    }

    private void OnRestoreInbox(object sender, RoutedEventArgs e)
    {
        InboxMode.Restore();
        Settings.Current.InboxEnabled = false;
        Settings.Current.Save();
        App.Instance?.Watch?.Restart();

        _initializing = true;
        InboxSwitch.IsOn = false;
        _initializing = false;
        UpdateInboxText();
    }

    private void OnWebsite(object sender, RoutedEventArgs e) => UpdateChecker.Run();    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---- option rows -----------------------------------------------------

    private sealed class KeepOption
    {
        public string Label { get; }
        public int? Value { get; }
        public KeepOption(string label, int? value) { Label = label; Value = value; }
        public override string ToString() => Label;
    }

    private sealed class SizeOption
    {
        public string Label { get; }
        public int Value { get; }
        public SizeOption(string label, int value) { Label = label; Value = value; }
        public override string ToString() => Label;
    }

    private sealed class ThemeOption
    {
        public string Label { get; }
        public ThemeKind Value { get; }
        public ThemeOption(string label, ThemeKind value) { Label = label; Value = value; }
        public override string ToString() => Label;
    }
}
