using System;
using System.Windows.Input;

namespace Pegged.Core;

/// <summary>
/// Translates between a <c>HotkeyBinding</c> stored in settings, the Win32
/// modifier + virtual-key pair that <c>RegisterHotKey</c> wants, and the
/// "Ctrl+Alt+P" string the user sees in the UI.
/// </summary>
public static class HotkeyParser
{
    public static (uint Modifiers, uint VirtualKey) Parse(HotkeyBinding b)
    {
        var mods = b.Modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT
                                  | NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN);
        return (mods, b.VirtualKey);
    }

    public static HotkeyBinding Render(ModifierKeys mods, Key key)
    {
        uint m = 0;
        if ((mods & ModifierKeys.Control) != 0) m |= NativeMethods.MOD_CONTROL;
        if ((mods & ModifierKeys.Alt)     != 0) m |= NativeMethods.MOD_ALT;
        if ((mods & ModifierKeys.Shift)   != 0) m |= NativeMethods.MOD_SHIFT;
        if ((mods & ModifierKeys.Windows) != 0) m |= NativeMethods.MOD_WIN;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var name = RenderName(m, key);
        return new HotkeyBinding { Modifiers = m, VirtualKey = vk, Display = name };
    }

    private static string RenderName(uint mods, Key key)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & NativeMethods.MOD_ALT)     != 0) parts.Add("Alt");
        if ((mods & NativeMethods.MOD_SHIFT)   != 0) parts.Add("Shift");
        if ((mods & NativeMethods.MOD_WIN)     != 0) parts.Add("Win");
        parts.Add(NiceKeyName(key));
        return string.Join("+", parts);
    }

    private static string NiceKeyName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return k.ToString().Substring(1);
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return k.ToString().Replace("NumPad", "Num ");
        return k.ToString();
    }
}
