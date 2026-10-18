using System;
using System.Windows;
using System.Windows.Interop;

namespace Pegged.Core;

/// <summary>
/// Hands the keyboard to one of our own windows, and gives it back afterwards.
///
/// The line never activates (that is the whole point of WS_EX_NOACTIVATE), so
/// Windows does not consider Pegged the foreground process. A window shown from
/// it - the crop overlay, the preview, the settings - therefore appears without
/// the keyboard: Esc and Enter go to whatever was in front before, and a
/// fullscreen overlay with a dead keyboard looks like a frozen app.
///
/// SetForegroundWindow is refused for a process that is not foreground, so
/// SwitchToThisWindow does the work when it is.
///
/// Handing it back matters as much as taking it. When one of these windows is
/// closed, Windows looks for the next window to give the foreground to; the
/// only other window of ours is the line, which cannot accept it. The system
/// was left with a foreground that belonged to a window that no longer exists,
/// and for a moment clicks on the line went nowhere - the cards looked frozen
/// right after a preview had been closed.
/// </summary>
public static class WindowFocus
{
    private static IntPtr _previous;

    public static void Take(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var current = NativeMethods.GetForegroundWindow();
        if (current == handle) return;

        if (current != IntPtr.Zero) _previous = current;

        NativeMethods.SetForegroundWindow(handle);

        if (NativeMethods.GetForegroundWindow() != handle)
            NativeMethods.SwitchToThisWindow(handle, true);

        NativeMethods.SetActiveWindow(handle);
        NativeMethods.SetFocus(handle);
    }

    /// <summary>Gives the foreground back to whatever had it before we took it.
    /// Called as the window closes, while we are still the foreground process,
    /// which is the only moment Windows allows the hand-back.</summary>
    public static void Release()
    {
        var previous = _previous;
        _previous = IntPtr.Zero;

        if (previous == IntPtr.Zero || !NativeMethods.IsWindow(previous)) return;
        NativeMethods.SetForegroundWindow(previous);
    }
}
