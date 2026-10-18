using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pegged.Core;

/// <summary>
/// The app's single global hotkey. It is owned by <see cref="App" /> and bound to
/// the line window (which lives for the whole session), so the shortcut keeps
/// working after the settings window is closed.
/// </summary>
public sealed class HotKeyService : IDisposable
{
    /// <summary>Application-private id: RegisterHotKey wants one per hwnd,
    /// and this must not collide with the shell's 0x0000..0xBFFF range.</summary>
    private const int HotKeyId = 0x5047;

    private readonly Window _host;
    private HwndSource? _source;
    private int _id;

    public HotKeyService(Window host) => _host = host;

    /// <summary>Applies a binding, or removes the one in place.</summary>
    /// <returns>True when the requested binding (or "no binding") is in effect.</returns>
    public bool Apply(HotkeyBinding? binding)
    {
        Unregister();
        if (binding is null) return true;

        var hwnd = new WindowInteropHelper(_host).Handle;
        if (hwnd == IntPtr.Zero) return false;

        var source = HwndSource.FromHwnd(hwnd);
        if (source is null) return false;

        source.AddHook(WndProc);

        var modifiers = binding.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (NativeMethods.RegisterHotKey(hwnd, HotKeyId, modifiers, binding.VirtualKey))
        {
            _source = source;
            _id = HotKeyId;
            return true;
        }

        // Someone else owns the combo. Take the hook back off so we never
        // leak a listener we are not using, and say so.
        source.RemoveHook(WndProc);
        _source = null;
        _id = 0;
        return false;
    }

    public void Unregister()
    {
        if (_id != 0 && _source is { } src)
        {
            var hwnd = new WindowInteropHelper(_host).Handle;
            if (hwnd != IntPtr.Zero) NativeMethods.UnregisterHotKey(hwnd, _id);
            _id = 0;
            src.RemoveHook(WndProc);
            _source = null;
        }
    }

    /// <summary>Raw form used by the settings window while recording a key.</summary>
    public bool Register(uint modifiers, uint virtualKey)
        => Apply(new HotkeyBinding { Modifiers = modifiers, VirtualKey = virtualKey });

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            App.Instance?.Line?.TogglePin();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
