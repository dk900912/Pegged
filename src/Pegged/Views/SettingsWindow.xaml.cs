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
    private HotKeyService? _hotKey;

    public SettingsWindow()
    {
        InitializeComponent();
        BuildLanguageCombo();
        BuildKeepCombo();
        BuildSizeCombo();
        BuildThemeCombo();
        ApplyValues();
        NavList.SelectedIndex = 0;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hotKey = new HotKeyService(this);
        var s = Settings.Current;
        if (s.Shortcut is not null)
        {
            var (mods, vk) = HotkeyParser.Parse(s.Shortcut);
            _hotKey.Register(mods, vk);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotKey?.Dispose();
    }

    // ---- combos ----------------------------------------------------------

    private record LangItem(string Name, string Culture);

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
            new(Loc.Get("Settings.KeepAuto"), null),
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
            new("Small",  0),
            new("Medium", 1),
            new("Large",  2),
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
    }

    // ---- nav -------------------------------------------------------------

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
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
        if (LanguageCombo.SelectedItem is LangItem l)
        {
            Settings.Current.Language = l.Culture;
            Settings.Current.Save();
            Loc.Apply(Settings.Current);
        }
    }

    private void OnSounds(object sender, RoutedEventArgs e)
    {
        Settings.Current.Sounds = SoundsSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnKeep(object sender, SelectionChangedEventArgs e)
    {
        if (KeepCombo.SelectedItem is KeepOption v) Settings.Current.KeepOnLine = v.Value;
        Settings.Current.Save();
    }

    private void OnSize(object sender, SelectionChangedEventArgs e)
    {
        if (SizeCombo.SelectedItem is SizeOption v) Settings.Current.Size = (CardSize)v.Value;
        Settings.Current.Save();
    }

    private void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ThemeOption v)
        {
            Settings.Current.Theme = v.Value;
            Settings.Current.Save();
            ThemeManager.Apply(v.Value);
        }
    }

    private void OnRevealDelay(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Settings.Current.RevealDelay = Math.Round(RevealSlider.Value, 2);
        Settings.Current.Save();
    }

    private void OnTopEdge(object sender, RoutedEventArgs e)
    {
        Settings.Current.RevealOnTopEdge = TopEdgeSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnPeek(object sender, RoutedEventArgs e)
    {
        Settings.Current.PeekAtNewCapture = PeekSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnClipboard(object sender, RoutedEventArgs e)
    {
        Settings.Current.WatchClipboard = ClipboardSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnFullScreen(object sender, RoutedEventArgs e)
    {
        Settings.Current.ShowOverFullScreenApps = FullScreenSwitch.IsOn;
        Settings.Current.Save();
    }

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        Settings.Current.LaunchAtLogin = LoginSwitch.IsOn;
        Settings.Current.Save();
        StartupService.SetEnabled(LoginSwitch.IsOn);
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
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
        // Build a "Ctrl+Alt+P" string from whatever was pressed.
        var mods = ModifierKeys.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= ModifierKeys.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= ModifierKeys.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= ModifierKeys.Shift;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods |= ModifierKeys.Windows;

        if (e.Key == Key.Escape)
        {
            HotkeyBox.Text = Settings.Current.Shortcut?.Display ?? "";
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // Wait for a real key.
            return;
        }

        var s = HotkeyParser.Render(mods, key);
        Settings.Current.Shortcut = s;
        Settings.Current.Save();
        HotkeyBox.Text = s.Display;

        var (m, v) = HotkeyParser.Parse(s);
        _hotKey?.Register(m, v);
        e.Handled = true;
    }

    private void OnWebsite(object sender, RoutedEventArgs e) => UpdateChecker.Run();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---- option rows -----------------------------------------------------

    private sealed class KeepOption
    {
        public string Label { get; }
        public int? Value { get; }
        public KeepOption(string label, int? value) { Label = label; Value = value; }
    }

    private sealed class SizeOption
    {
        public string Label { get; }
        public int Value { get; }
        public SizeOption(string label, int value) { Label = label; Value = value; }
    }

    private sealed class ThemeOption
    {
        public string Label { get; }
        public ThemeKind Value { get; }
        public ThemeOption(string label, ThemeKind value) { Label = label; Value = value; }
    }
}
