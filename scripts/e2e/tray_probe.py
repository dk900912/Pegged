#!/usr/bin/env python3
"""Right-clicks Pegged's real tray icon and photographs the menu.

The menu is drawn by the tray service out of "Segoe MDL2 Assets" glyphs, so
"does it look right" is a real question: a missing glyph shows as a tofu box
and the showcase's menu (§6: one toggle, clear, a separator, the capture
sources with their checkmarks, the submenus, sound and login, update, exit)
has a specific order that is easy to break while editing strings.

The icon's rect comes from Shell_NotifyIconGetRect - the supported way to ask
the shell where an icon sits - rather than from the taskbar's toolbar, which
Windows 11 no longer uses for the notification area (there is no
ToolbarWindow32 in it at all, which is how this probe used to fail).

    python scripts/e2e/tray_probe.py
"""
import ctypes
import os
import time

import win32con
from PIL import ImageGrab

try:
    from pywinauto import Desktop
except ImportError:                                     # pragma: no cover
    Desktop = None

import audit_walkthrough as A

user32 = ctypes.windll.user32
SHOTS = A.SHOTS


class GUID(ctypes.Structure):
    _fields_ = [("d1", ctypes.c_ulong), ("d2", ctypes.c_ushort), ("d3", ctypes.c_ushort),
                ("d4", ctypes.c_ubyte * 8)]


class NotifyIconIdentifier(ctypes.Structure):
    _fields_ = [("cbSize", ctypes.c_ulong), ("hWnd", ctypes.c_void_p),
                ("uID", ctypes.c_uint), ("guidItem", GUID)]


def icon_rect():
    """The screen rect of Pegged's tray icon, or None if it has none."""
    pids = A.pegged_pids()
    windows = []

    def cb(hwnd, _):
        try:
            import win32process
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
        except Exception:
            return
        if pid in pids:
            windows.append(hwnd)

    import win32gui
    win32gui.EnumWindows(cb, None)

    rect = ctypes.wintypes.RECT()
    for hwnd in windows:
        for uid in range(1, 33):
            nii = NotifyIconIdentifier(ctypes.sizeof(NotifyIconIdentifier),
                                       hwnd, uid, GUID())
            try:
                ok = ctypes.windll.shell32.Shell_NotifyIconGetRect(
                    ctypes.byref(nii), ctypes.byref(rect))
            except Exception:
                continue
            if ok == 0 and rect.right > rect.left and rect.bottom > rect.top:
                return (rect.left, rect.top, rect.right, rect.bottom)
    return None


def popup_handle():
    """The HWND of the WPF context menu the tray service just opened.

    pywinauto's UIA backend cannot find it by class name - the popup exposes no
    Window pattern to UIA - but Win32 can see it, and once we have the handle we
    can wrap it. A Win32 menu (#32768) also counts, for a native fallback.
    """
    import win32gui
    import win32process
    pids = A.pegged_pids()
    found = []

    def cb(hwnd, _):
        if not win32gui.IsWindowVisible(hwnd):
            return
        cls = win32gui.GetClassName(hwnd)
        if cls not in ("Xaml_WindowedPopupClass", "#32768"):
            return
        try:
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
        except Exception:
            return
        if not pids or pid in pids:
            found.append(hwnd)

    win32gui.EnumWindows(cb, None)
    return found[0] if found else None


def main():
    rect = icon_rect()
    if not rect:
        print("RESULT: no tray icon rect - is Pegged running?")
        return
    x = (rect[0] + rect[2]) // 2
    y = (rect[1] + rect[3]) // 2
    print("Pegged tray icon at %s" % (rect,))

    # Right-click the icon, then look for the popup. WPF's ContextMenu is not
    # a Win32 menu: its window class is Xaml_WindowedPopupClass and #32768
    # never appears. Hunting only for #32768 is why this probe used to report
    # "the menu did not open" while the menu was sitting on screen.
    menu = None
    for attempt in range(4):
        user32.SetCursorPos(x, y)
        time.sleep(0.1)
        user32.mouse_event(win32con.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0)
        time.sleep(0.08)
        user32.mouse_event(win32con.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0)

        # The menu is built on the UI thread of a process that is otherwise
        # idle; give it a beat before asking Win32 for it.
        time.sleep(0.8)
        hwnd = popup_handle()
        if hwnd:
            menu = A.uia(hwnd)
            break
        # The tray swallows the first click after a repaint; retry the icon.
        time.sleep(0.4)

    if menu is None:
        print("no WPF popup menu found - the menu did not open")
        return

    shot = os.path.join(SHOTS, "tray-menu.png")
    ImageGrab.grab(bbox=(rect[0] - 340, rect[1] - 560, rect[2] + 60, rect[3] + 30),
                   all_screens=True).save(shot)
    print("wrote", shot)

    # Close it again without touching anything: Esc.
    win32con_key = 0x1B
    user32.keybd_event(win32con_key, 0, 0, 0)
    user32.keybd_event(win32con_key, 0, win32con.KEYEVENTF_KEYUP, 0)


main()
