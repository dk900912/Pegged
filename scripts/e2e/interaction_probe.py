#!/usr/bin/env python3
"""Interaction probe: the parts of the line that only a real mouse can prove.

  * WM_NCHITTEST: the dock is click-through everywhere except over a card and
    its own buttons. Asserted by asking the window directly, which is the same
    question Windows asks before it delivers a click.
  * The quick action bar is clickable (it was not: a global WS_EX_TRANSPARENT
    made the whole window see-through, buttons included).
  * Switching the language in Settings relabels the open window on the spot.

Run with the app already started (scripts/e2e/launch_probe.py leaves it up, or
start publish/Pegged.exe yourself).
"""
import ctypes
import json
import os
import sys
import time

import win32api
import win32con
import win32gui
import win32process
from PIL import ImageGrab

try:
    import psutil
except ImportError:
    psutil = None

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "outputs", "acceptance")
SHOTS = os.path.join(OUT, "shots")

WM_NCHITTEST = 0x0084
HTTRANSPARENT = -1
HTCLIENT = 1

user32 = ctypes.windll.user32

# Every coordinate below is physical: the window is Per-Monitor-V2 and the
# pointer API speaks physical pixels, so the probe has to as well. Without this
# GetWindowRect comes back divided by the display scale and every click lands
# somewhere else (this cost one debugging round: the "settings" click went into
# the click-through area and quietly hit Explorer).
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    user32.SetProcessDPIAware()


def make_lparam(x, y):
    return (y << 16) | (x & 0xFFFF)


def nchittest(hwnd, screen_x, screen_y):
    result = user32.SendMessageW(hwnd, WM_NCHITTEST, 0, make_lparam(screen_x, screen_y))
    # SendMessageW is typed as returning a signed 32-bit int through ctypes only
    # if we say so; -1 comes back as 4294967295 otherwise.
    if result > 0x7FFFFFFF:
        result -= 0x100000000
    return result


def pegged_windows():
    found = []
    pids = set()
    if psutil is not None:
        pids = {p.pid for p in psutil.process_iter(["name"]) if p.info["name"] == "Pegged.exe"}

    def cb(hwnd, _):
        try:
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
        except Exception:
            return
        if pids and pid not in pids:
            return
        if win32gui.GetWindowText(hwnd) == "Pegged":
            found.append(hwnd)

    win32gui.EnumWindows(cb, None)
    return found


SAMPLES = []


def reveal(timeout=8.0):
    """Puts the pointer on the top edge and waits for the dock to settle.

    The spring is deliberately underdamped, so "wait 1.5s" is not the same as
    "it has arrived": poll until two samples in a row agree.
    """
    user32.SetCursorPos(user32.GetSystemMetrics(0) // 2, 2)

    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        time.sleep(0.25)
        handles = pegged_windows()
        if not handles:
            continue
        rect = win32gui.GetWindowRect(handles[0])
        SAMPLES.append(list(rect))
        if last is not None and rect == last and rect[1] <= 1:
            return handles[0]
        last = rect

    handles = pegged_windows()
    return handles[0] if handles else None


def main():
    os.makedirs(SHOTS, exist_ok=True)
    report = {"checks": {}, "detail": {}}

    # A settings window left open by an earlier run would make the click check
    # pass for the wrong reason.
    for stale in [find_other_window(None)]:
        if stale:
            win32gui.PostMessage(stale, win32con.WM_CLOSE, 0, 0)
            time.sleep(0.8)

    hwnd = reveal()
    report["detail"]["samples"] = SAMPLES
    handles = [hwnd] if hwnd else []
    if not handles:
        print("FAIL line window not found")
        report["checks"]["line_window"] = False
        return write(report)

    hwnd = handles[0]
    rect = win32gui.GetWindowRect(hwnd)
    report["detail"]["window_rect"] = list(rect)
    report["checks"]["line_window"] = True

    # --- click through -----------------------------------------------------
    # An empty corner of the dock: 40px in from the left, 20px down from the
    # top. No card, no toolbar there.
    empty = nchittest(hwnd, rect[0] + 40, rect[1] + 20)
    report["detail"]["hittest_empty"] = empty
    report["checks"]["click_through_empty"] = empty == HTTRANSPARENT

    # The toolbar sits centred at the top of the dock. Find the span that
    # answers HTCLIENT, which is exactly the row of buttons.
    centre_x = (rect[0] + rect[2]) // 2
    toolbar_y = rect[1] + 24
    solid = [x for x in range(centre_x - 140, centre_x + 140)
             if nchittest(hwnd, x, toolbar_y) == HTCLIENT]
    report["detail"]["toolbar_span"] = [min(solid), max(solid)] if solid else None
    report["checks"]["toolbar_clickable"] = len(solid) > 20

    # --- the settings button actually opens the window ---------------------
    if solid:
        # The gear is the right-most of the three buttons. Stay clear of the
        # toolbar's own 6px padding: clicking there hits the Border, which has
        # no handler and silently does nothing (this cost a debugging round).
        click_x = max(solid) - 22
        user32.SetCursorPos(click_x, toolbar_y)
        time.sleep(0.35)
        user32.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        user32.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        time.sleep(1.4)
    else:
        click_x = None
    report["detail"]["clicked_at"] = click_x

    settings = find_other_window(hwnd)

    report["checks"]["settings_opened"] = settings is not None
    report["detail"]["settings_rect"] = list(win32gui.GetWindowRect(settings)) if settings else None

    shot = os.path.join(SHOTS, "dock-interaction.png")
    ImageGrab.grab(bbox=(rect[0], rect[1], rect[2], rect[1] + 300)).save(shot)

    if settings:
        try:
            report["checks"].update(language_switch(settings, report["detail"]))
        except Exception as error:
            report["detail"]["language_error"] = f"{type(error).__name__}: {error}"
            report["checks"]["language_refresh"] = False

        # Leave the machine as it was found: this probe opened the settings
        # centre, and a probe that leaves it up makes the next one report a
        # stray window it did not create.
        try:
            win32gui.PostMessage(settings, win32con.WM_CLOSE, 0, 0)
            time.sleep(0.8)
        except Exception as error:
            report["detail"]["settings_close_error"] = f"{type(error).__name__}: {error}"

    return write(report)


def find_other_window(line_hwnd):
    """Any other real window of the process: the settings centre."""
    pids = set()
    if psutil is not None:
        pids = {p.pid for p in psutil.process_iter(["name"]) if p.info["name"] == "Pegged.exe"}

    found = []

    def cb(hwnd, _):
        if hwnd == line_hwnd or not win32gui.IsWindowVisible(hwnd):
            return
        try:
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
        except Exception:
            return
        if pids and pid not in pids:
            return
        r = win32gui.GetWindowRect(hwnd)
        if (r[2] - r[0]) > 500 and (r[3] - r[1]) > 350:
            found.append(hwnd)

    win32gui.EnumWindows(cb, None)
    return found[0] if found else None


def language_switch(settings_hwnd, detail):
    """Switching the language in Settings must relabel the open window.

    {loc:Str} is a live binding to Strings[key] now, so this is the check that
    proves a language change no longer needs the window to be reopened.
    """
    checks = {}
    try:
        from pywinauto import Desktop
    except ImportError:
        detail["language"] = "pywinauto missing"
        return checks

    title = win32gui.GetWindowText(settings_hwnd)
    detail["settings_title"] = title
    detail["settings_class"] = win32gui.GetClassName(settings_hwnd)

    # By handle, not by title: the title is itself a localized string, so it
    # changes the moment the test switches the language and a title lookup
    # starts failing halfway through.
    #
    # Raised first: click_input() goes to whatever is on top, and the settings
    # window is easy to leave behind a folder window. When that happens the
    # click lands on the folder, the language never changes and this check
    # reports a bug that is not there.
    import audit_walkthrough as A
    A.raise_window(settings_hwnd)
    dlg = Desktop(backend="uia").window(handle=settings_hwnd)

    nav = dlg.child_window(auto_id="NavCapture")
    before = " ".join(t.window_text() for t in nav.descendants(control_type="Text"))
    detail["nav_before"] = before

    if "捕获源" in before:
        target, other, expected = "English", "中文（简体）", "Capture sources"
    else:
        target, other, expected = "中文（简体）", "English", "捕获源"

    # The language row is a segmented control: three pills in front (follow
    # the system, 简体中文, English) and the rest under the "…" pill, so the
    # pill is clicked rather than selected from a list.
    host = dlg.child_window(auto_id="LangSegmented")
    pill = [p for p in host.descendants(control_type="RadioButton")
            if p.element_info.name == target]
    if pill:
        pill[0].click_input()
        time.sleep(1.2)

    after = " ".join(t.window_text() for t in nav.descendants(control_type="Text"))
    detail["nav_after"] = after
    checks["language_refresh"] = after != before and expected in after

    # Put the language back so the machine is left as it was found.
    if after != before:
        host = dlg.child_window(auto_id="LangSegmented")
        back = [p for p in host.descendants(control_type="RadioButton")
                if p.element_info.name == other]
        if back:
            back[0].click_input()
            time.sleep(1.2)

    return checks


def write(report):
    report["passed"] = all(report["checks"].values()) and bool(report["checks"])
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "interaction-probe.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    for key, value in report["checks"].items():
        print(("PASS " if value else "FAIL ") + key)
    print("passed:", report["passed"])
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
