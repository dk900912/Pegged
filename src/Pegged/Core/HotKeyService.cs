using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pegged.Core;

/// <summary>
/// A single global hotkey bound to a window's <c>WM_HOTKEY</c> message loop.
/// Re-registration is automatic on settings change.
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private readonly Window _owner;
    private HwndSource? _source;
    private int _id;

    public HotKeyService(Window owner)
    {
        _owner = owner;
    }

    public void Register(uint modifiers, uint virtualKey)
    {
        Unregister();
        if (virtualKey == 0) return;

        var hwnd = new WindowInteropHelper(_owner).Handle;
        if (hwnd == IntPtr.Zero) return;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        _id = 0xC0DE;
        if (!NativeMethods.RegisterHotKey(hwnd, _id, modifiers, virtualKey))
        {
            // Some other app owns this combo: drop silently. The settings
            // window will warn the user if they try to save it.
            _id = 0;
        }
    }

    public void Unregister()
    {
        if (_id != 0 && _source is { } src)
        {
            NativeMethods.UnregisterHotKey(new WindowInteropHelper(_owner).Handle, _id);
            _id = 0;
            src.RemoveHook(WndProc);
            _source = null;
        }
    }

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
