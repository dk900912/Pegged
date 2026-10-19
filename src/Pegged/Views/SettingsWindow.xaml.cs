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
        BuildLanguageCombo();
        BuildKeepCombo();
        BuildSizeCombo();
        BuildThemeCombo();
        ApplyValues();
        _initializing = false;
        NavList.SelectedIndex = 0;
        UpdateNavigation();

        // {loc:Str} is a live binding now, so the labels relabel themselves.
        // The combo boxes hold pre-built strings, so they are rebuilt here.
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
            BuildKeepCombo();
            BuildSizeCombo();
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
        // WPF asks the item for its text when the combo box reports itself to
        // accessibility tools, and a record's generated ToString() would put
        // "LangItem { Name = … }" in the dropdown's automation name.
        public override string ToString() => Name;
    }

    private void BuildLanguageCombo()
    {
        var langs = new List<LangItem>
        {
            new("English",      "en"),
            new("中文（简体）",  "zh-Hans"),
            new("Español",      "es"),
            new("Français",     "fr"),
            new("日本語",        "ja"),
            new("한국어",         "ko"),
            new("Português (BR)","pt-BR"),
            new("Türkçe",       "tr"),
            new("Azərbaycan",   "az"),
        };
        LanguageCombo.DisplayMemberPath = "Name";
        LanguageCombo.ItemsSource = langs;
        var cur = Settings.Current.Language ?? "en";
        LanguageCombo.SelectedItem = langs.FirstOrDefault(l => l.Culture == cur) ?? langs[0];
    }

    private void BuildKeepCombo()
    {
        var items = new List<KeepOption>
        {
            new(Loc.Get("Menu.KeepFit"), null),
            new("8",  8), new("12", 12), new("16", 16), new("20", 20), new("32", 32),
        };
        KeepCombo.DisplayMemberPath = "Label";
        KeepCombo.ItemsSource = items;
        KeepCombo.SelectedItem = items.Find(i => i.Value == Settings.Current.KeepOnLine) ?? items[0];
    }

    private void BuildSizeCombo()
    {
        var items = new List<SizeOption>
        {
            new(Loc.Get("Menu.SizeSmall"),  0),
            new(Loc.Get("Menu.SizeMedium"), 1),
            new(Loc.Get("Menu.SizeLarge"),  2),
        };
        SizeCombo.DisplayMemberPath = "Label";
        SizeCombo.ItemsSource = items;
        SizeCombo.SelectedItem = items.Find(i => i.Value == (int)Settings.Current.Size) ?? items[1];
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
        if (LanguageCombo.SelectedItem is LangItem l)
        {
            Settings.Current.Language = l.Culture;
            Settings.Current.Save();
            Loc.Apply(Settings.Current);
        }
    }

    private void OnSounds(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.Sounds = SoundsSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnKeep(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (KeepCombo.SelectedItem is KeepOption v) Settings.Current.KeepOnLine = v.Value;
        Settings.Current.Save();
    }

    private void OnSize(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (SizeCombo.SelectedItem is SizeOption v) Settings.Current.Size = (CardSize)v.Value;
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
