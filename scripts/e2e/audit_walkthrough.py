#!/usr/bin/env python3
"""Full interaction walkthrough of the running app.

Every check drives a real surface (real clicks, real keys, real files) and
asserts a user-visible outcome, not "it did not throw". Failures are recorded
and the run continues, so one broken surface does not hide the rest.

    python scripts/e2e/audit_walkthrough.py            # whole sweep
    python scripts/e2e/audit_walkthrough.py --only card

Writes outputs/acceptance/audit.json and screenshots into
outputs/acceptance/shots/audit/.
"""
import argparse
import atexit
import ctypes
import json
import os
import subprocess
import sys
import time

import win32api
import win32clipboard
import win32con
import win32gui
import win32process
from PIL import Image, ImageDraw, ImageGrab

try:
    from pywinauto import Desktop
except ImportError:                                     # pragma: no cover
    Desktop = None

try:
    import psutil
except ImportError:                                     # pragma: no cover
    psutil = None

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "outputs", "acceptance")
SHOTS = os.path.join(OUT, "shots", "audit")
WATCH = os.path.join(os.environ["APPDATA"], "Pegged", "Screenshots")
SETTINGS = os.path.join(os.environ["APPDATA"], "Pegged", "settings.json")
CRASH = os.path.join(os.environ["APPDATA"], "Pegged", "crash.log")

user32 = ctypes.windll.user32

# Physical pixels everywhere: the app is Per-Monitor-V2.
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:                                       # pragma: no cover
    user32.SetProcessDPIAware()

RESULTS = []

# The handle the preview window got, so the checks after it are about that
# window and not about "whatever else happens to be open".
_preview = {"hwnd": None}

# Same for the crop overlay.
_crop = {"hwnd": None}


# ---------------------------------------------------------------- plumbing

def pegged_pids():
    if psutil is None:
        return set()
    return {p.pid for p in psutil.process_iter(["name"]) if p.info["name"] == "Pegged.exe"}


def windows_of_app(min_width=0):
    pids = pegged_pids()
    found = []

    def cb(hwnd, _):
        try:
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
        except Exception:
            return
        if pids and pid not in pids:
            return
        if not win32gui.IsWindowVisible(hwnd):
            return
        r = win32gui.GetWindowRect(hwnd)
        if (r[2] - r[0]) >= min_width:
            found.append((hwnd, win32gui.GetWindowText(hwnd), r))

    win32gui.EnumWindows(cb, None)
    return found


def all_windows():
    """Every visible top-level window on the desktop, ours or not."""
    found = []

    def cb(hwnd, _):
        if not win32gui.IsWindowVisible(hwnd):
            return
        if not win32gui.GetWindowText(hwnd):
            return
        try:
            found.append((hwnd, win32gui.GetWindowText(hwnd), win32gui.GetWindowRect(hwnd)))
        except Exception:
            return

    win32gui.EnumWindows(cb, None)
    return found


def line():
    for hwnd, title, _ in windows_of_app():
        if title == "Pegged":
            return hwnd
    raise RuntimeError("line window not found - is Pegged running?")


def line_rect():
    return win32gui.GetWindowRect(line())


def ensure_app(timeout=25.0):
    """Starts the published build if nothing is running.

    Every check here drives a live window, so an app that is not up turns the
    whole sweep into identical "line window not found" failures. Bringing it up
    ourselves keeps the sweep self-contained and keeps that message meaning
    "the app really died mid-run" rather than "nobody started it".
    """
    exe = os.path.join(ROOT, "publish", "Pegged.exe")
    if not pegged_pids():
        if not os.path.exists(exe):
            raise SystemExit("publish/Pegged.exe missing - run the publish step first")
        subprocess.Popen([exe], cwd=os.path.dirname(exe))
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            if line():
                return
        except RuntimeError:
            pass
        time.sleep(0.4)
    raise SystemExit("Pegged did not present its line window within %.0fs" % timeout)


def uia(hwnd=None):
    return Desktop(backend="uia").window(handle=hwnd or line())


def buttons(window=None):
    window = window or uia()
    out = {}
    for b in window.descendants(control_type="Button"):
        out.setdefault(b.automation_id(), []).append(b)
    return out


def card_count(window=None):
    return len(buttons(window).get("CloseButton", []))


def settings_window(timeout=8.0):
    """The settings window, found by its own content.

    Not by title - the title is localized - and not by "the first window that
    is not the line", because the crop overlay and the preview are our windows
    too and one of them may still be open.
    """
    deadline = time.time() + timeout
    while True:
        for hwnd, title, _ in windows_of_app(400):
            if title == "Pegged":
                continue
            try:
                w = uia(hwnd)
                if w.child_window(auto_id="NavCapture").exists(timeout=0.3):
                    return raise_window(w)
            except Exception:
                continue
        if time.time() >= deadline:
            raise RuntimeError("settings window not found")
        time.sleep(0.4)


def raise_window(w):
    """Puts a window in front of everything and waits until it really is.

    A click_input() goes to whatever is on top, so a settings window sitting
    under a folder window silently clicks the folder instead of the app.
    SetForegroundWindow alone is not enough: Windows refuses it unless the
    caller already owns the foreground, which a probe never does. The
    topmost bounce does not need that right, and the hit test proves it
    worked instead of assuming it.
    """
    handle = w.handle if hasattr(w, "handle") else w
    for _ in range(4):
        user32.SetForegroundWindow(handle)
        user32.BringWindowToTop(handle)
        win32gui.SetWindowPos(handle, win32con.HWND_TOPMOST, 0, 0, 0, 0,
                              win32con.SWP_NOMOVE | win32con.SWP_NOSIZE | win32con.SWP_SHOWWINDOW)
        win32gui.SetWindowPos(handle, win32con.HWND_NOTOPMOST, 0, 0, 0, 0,
                              win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
        time.sleep(0.2)

        rect = win32gui.GetWindowRect(handle)
        if rect[2] > rect[0] and rect[3] > rect[1]:
            at = win32gui.WindowFromPoint(((rect[0] + rect[2]) // 2, (rect[1] + rect[3]) // 2))
            if at:
                root = win32gui.GetAncestor(at, win32con.GA_ROOT)
                if root == handle:
                    break
        time.sleep(0.2)
    time.sleep(0.25)
    return w


def open_settings():
    """The settings window, opening it from the gear if it is not up yet."""
    try:
        return raise_window(settings_window(timeout=1.5))
    except RuntimeError:
        pass

    for attempt in (1, 2):
        reveal()
        pointer(960, 30)
        try:
            buttons()["SettingsButton"][0].invoke()
        except KeyError:
            pass
        try:
            return raise_window(settings_window(timeout=4.0))
        except RuntimeError:
            if attempt == 2:
                raise


def frame_luma():
    """The card's own border, which is the themed surface token.

    The dock used to be a filled panel and the theme switch was sampled on
    that fill. The dock paints nothing now, so the fill sample reads whatever
    desktop is behind it. The polaroid frame is the app's own painted surface
    that is always there when a card hangs: #FEFEFE on the light theme,
    #2A2E36 on the dark one - a difference of two hundred, so a theme switch
    cannot be mistaken for anything else.
    """
    write_test_images(1, "audit-theme-%d" % int(time.time()))
    wait_cards(1)
    # The line tucks itself away once the pointer has been off it for a
    # moment, and a tucked card's rect sits above the screen - where a sample
    # of "three pixels left of it" is a sample of the bottom of the desktop.
    # So it is brought back down immediately before the reading.
    reveal()
    settle()

    found = [e for e in uia().descendants(control_type="Image")
             if e.element_info.automation_id == "ThumbImage"]
    if not found:
        raise RuntimeError("no ThumbImage on the line")

    rect = found[0].rectangle()
    # Two pixels outside the picture, half way up it: the 4px frame. Not the
    # top-left corner - the frame's corner is rounded, and a point just
    # outside the picture there is a point outside the card entirely.
    x, y = rect.left - 2, rect.top + (rect.bottom - rect.top) // 2
    return ImageGrab.grab(bbox=(x - 2, y - 2, x + 2, y + 2), all_screens=True) \
                    .convert("L").getpixel((2, 2))


def seg(dialog, control_id, label=None, index=None):
    """A pill of a segmented control (showcase 5).

    Pick it by its text when that text is language independent - the keep-
    on-line pills read 25 / 50 / 100 in every language - and by its position
    when it is not: the size row is 小/中/大 here, Small/Medium/Large
    elsewhere, but the first pill is always the small one.

    The settings centre shows keep-on-line and card size as rows of pills, not
    as drop-downs, so "select 3" no longer means anything: the row is clicked
    on the pill, the way a person would.
    """
    host = dialog.child_window(auto_id=control_id)
    pills = [p for p in host.descendants(control_type="RadioButton")
             if p.element_info.name]
    if label is not None:
        for pill in pills:
            if pill.element_info.name == label:
                return pill
        raise LookupError("no pill %r in %s (have %s)" % (
            label, control_id, [p.element_info.name for p in pills]))
    if index is None:
        return pills[0]
    return pills[index]


def other_windows(min_width=0):
    return [(h, t, r) for h, t, r in windows_of_app(min_width) if t != "Pegged"]


def settle(timeout=4.0):
    """Waits for the dock to stop moving: two identical rects, fully down."""
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        time.sleep(0.15)
        rect = line_rect()
        if last == rect and rect[1] == 0:
            return rect
        last = rect
    return line_rect()


def foreground_handle():
    return user32.GetForegroundWindow()


def focused_is_pegged():
    """True when the keyboard would go to Pegged and nowhere else.

    Esc and Enter are sent as real key presses, and they go to whichever
    window owns the focus. If that is the app that started this script, the
    key lands in its UI - Esc there can cancel the very run being measured.
    """
    fg = user32.GetForegroundWindow()
    _, pid = win32process.GetWindowThreadProcessId(fg)
    return (pid in pegged_pids()), (win32gui.GetWindowText(fg) or hex(fg))


def reveal(timeout=8.0):
    user32.SetCursorPos(user32.GetSystemMetrics(0) // 2, 2)
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        time.sleep(0.2)
        rect = line_rect()
        if last == rect and rect[1] == 0:
            return rect
        last = rect
    return line_rect()


def pointer(x, y, settle=0.45):
    user32.SetCursorPos(int(x), int(y))
    time.sleep(settle)


def pointer_away(settle=1.6):
    user32.SetCursorPos(user32.GetSystemMetrics(0) // 2, user32.GetSystemMetrics(1) - 40)
    time.sleep(settle)


def click(x, y, button="left", double=False):
    pointer(x, y, 0.25)
    down, up = (win32con.MOUSEEVENTF_LEFTDOWN, win32con.MOUSEEVENTF_LEFTUP) if button == "left" \
        else (win32con.MOUSEEVENTF_RIGHTDOWN, win32con.MOUSEEVENTF_RIGHTUP)
    if double:
        for _ in range(2):
            win32api.mouse_event(down, 0, 0, 0, 0)
            time.sleep(0.03)
            win32api.mouse_event(up, 0, 0, 0, 0)
            time.sleep(0.06)
    else:
        win32api.mouse_event(down, 0, 0, 0, 0)
        time.sleep(0.06)
        win32api.mouse_event(up, 0, 0, 0, 0)


def card_point(index=0):
    """A point inside the photo, above the hover action row.

    UIA reports physical pixels while the app's own hit rects are in DIP, so
    this stays a comfortable distance inside the picture rather than near its
    edge, where a swaying card and the 125% scale would put the point outside.
    """
    w = uia()
    copies = buttons(w).get("CopyButton", [])
    deletes = buttons(w).get("DeleteButton", [])
    if index >= len(copies):
        raise RuntimeError("no card at index %d" % index)
    c = copies[index].rectangle()
    d = deletes[index].rectangle()
    return (c.left + d.right) // 2, c.top - 30


def shot(name):
    os.makedirs(SHOTS, exist_ok=True)
    path = os.path.join(SHOTS, name + ".png")
    ImageGrab.grab(bbox=(0, 0, user32.GetSystemMetrics(0), user32.GetSystemMetrics(1))).save(path)
    return path


def settings():
    with open(SETTINGS, encoding="utf-8") as f:
        return json.load(f)


def write_test_images(count, prefix="audit"):
    os.makedirs(WATCH, exist_ok=True)
    made = []
    for i in range(count):
        path = os.path.join(WATCH, "%s-%d.png" % (prefix, i))
        # The app hangs a capture on the watcher's Created event, and saving over
        # a file left by an earlier sweep only raises Changed. Drop the stale
        # fixture first so every call really is "a new screenshot arrived".
        try:
            os.remove(path)
        except FileNotFoundError:
            pass
        im = Image.new("RGB", (760, 480), (40 + 40 * i, 110, 150))
        d = ImageDraw.Draw(im)
        d.rectangle([18, 18, 742, 462], outline=(255, 255, 255), width=5)
        d.text((30, 30), "audit %d" % i, fill=(255, 255, 255))
        im.save(path)
        made.append(path)
        time.sleep(0.45)
    return made


def clear_cards():
    """Takes everything down through the real toolbar button."""
    reveal()
    b = buttons().get("ClearButton")
    if b:
        b[0].invoke()
        time.sleep(2.2)


def wait_cards(expected, timeout=8.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if card_count() == expected:
            return True
        time.sleep(0.25)
    return card_count() == expected


def clear_clipboard():
    try:
        win32clipboard.OpenClipboard()
        win32clipboard.EmptyClipboard()
        win32clipboard.CloseClipboard()
    except Exception:
        pass


def put_clipboard_image(size=(180, 130)):
    """Puts a picture on the clipboard the way a screenshot tool would: as a
    device independent bitmap, with no text and no file in tow."""
    from io import BytesIO

    image = Image.new("RGB", size, (30, 140, 190))
    draw = ImageDraw.Draw(image)
    draw.rectangle([10, 10, size[0] - 10, size[1] - 10], outline=(255, 255, 255), width=4)

    buffer = BytesIO()
    image.save(buffer, "BMP")
    dib = buffer.getvalue()[14:]        # CF_DIB is a BMP without its file header

    for _ in range(10):
        try:
            win32clipboard.OpenClipboard()
            try:
                win32clipboard.EmptyClipboard()
                win32clipboard.SetClipboardData(win32con.CF_DIB, dib)
            finally:
                win32clipboard.CloseClipboard()
            return True
        except Exception:
            time.sleep(0.2)
    return False


def drag(start, end, steps=14):
    """A real mouse drag: press, move in steps, release."""
    pointer(*start, settle=0.2)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    for i in range(1, steps + 1):
        x = start[0] + (end[0] - start[0]) * i // steps
        y = start[1] + (end[1] - start[1]) * i // steps
        user32.SetCursorPos(int(x), int(y))
        time.sleep(0.02)
    time.sleep(0.1)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(0.3)


def clipboard_has_image():
    """True when the clipboard carries a picture.

    A copy can put it there as a DIB, a DDB or a PNG depending on what the
    receiver asked for, and the app flushes the clipboard right after setting
    it, which keeps it locked for a moment - hence the retries.
    """
    fmt_png = win32clipboard.RegisterClipboardFormat("PNG")
    for _ in range(12):
        try:
            win32clipboard.OpenClipboard()
            try:
                return any(win32clipboard.IsClipboardFormatAvailable(fmt) for fmt in
                           (win32con.CF_DIB, win32con.CF_BITMAP, fmt_png))
            finally:
                win32clipboard.CloseClipboard()
        except Exception:
            time.sleep(0.2)
    return False


def key_combo(*vks):
    for vk in vks:
        user32.keybd_event(vk, 0, 0, 0)
    time.sleep(0.05)
    for vk in reversed(vks):
        user32.keybd_event(vk, 0, win32con.KEYEVENTF_KEYUP, 0)


# ------------------------------------------------------------- host window
#
# This walkthrough drives the real mouse and the real keyboard: the dock lives
# on the top edge of the screen and only real input reaches it. The window that
# started it - the chat app - is normally maximised and sits under that same
# top edge, so a click meant for the dock lands on the chat window instead. If
# it lands on "stop", the run is cancelled from the outside and looks like the
# user cancelled it. The host is moved below the dock for the duration and put
# back afterwards.

HOST_NAMES = {"WorkBuddyAI.exe", "WorkBuddy.exe", "CodeBuddy.exe", "electron.exe",
              "Cursor.exe", "Code.exe", "WindowsTerminal.exe", "powershell.exe",
              "cmd.exe", "conhost.exe"}
DOCK_BAND = 320          # the dock plus its hover toolbar, in pixels
_saved_placements = []


def host_windows():
    if psutil is None:
        return []
    out = []

    def cb(hwnd, _):
        _, pid = win32process.GetWindowThreadProcessId(hwnd)
        try:
            name = psutil.Process(pid).name()
        except Exception:
            return
        if name not in HOST_NAMES or not win32gui.IsWindowVisible(hwnd):
            return
        if not win32gui.GetWindowText(hwnd):
            return
        out.append((hwnd, win32gui.GetWindowRect(hwnd)))

    win32gui.EnumWindows(cb, None)
    return out


def crash_mark():
    """Size and mtime of the crash log, or None when there is no log."""
    try:
        st = os.stat(CRASH)
        return (st.st_size, st.st_mtime)
    except OSError:
        return None


def park_host():
    """Moves anything of ours out of the top band, and remembers where it was."""
    del _saved_placements[:]
    for hwnd, rect in host_windows():
        if rect[1] >= DOCK_BAND:
            continue
        placement = win32gui.GetWindowPlacement(hwnd)
        _saved_placements.append((hwnd, rect, placement[1]))
        if placement[1] == win32con.SW_SHOWMAXIMIZED:
            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
            time.sleep(0.3)
            rect = win32gui.GetWindowRect(hwnd)
        width = min(rect[2] - rect[0], 1280)
        height = min(rect[3] - rect[1], 900)
        win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, 40, DOCK_BAND + 40,
                              width, height, win32con.SWP_SHOWWINDOW)
        time.sleep(0.3)


def unpark_host():
    for hwnd, rect, show in _saved_placements:
        try:
            win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, rect[0], rect[1],
                                  rect[2] - rect[0], rect[3] - rect[1], 0)
            if show == win32con.SW_SHOWMAXIMIZED:
                win32gui.ShowWindow(hwnd, win32con.SW_MAXIMIZE)
        except Exception:
            pass
    del _saved_placements[:]


def close_launch_windows():
    """Closes the Explorer folders the launch probe leaves behind.

    Every launch opens the publish folder to double-click the exe, and those
    folder windows pile up on top of the settings window: a click_input() then
    lands on folder chrome instead of the app, and a check fails for a reason
    that has nothing to do with the app. One run measured it - the point over
    the settings window hit 'publish - 文件资源管理器', so the theme switch never
    happened and the dark-theme reading was of the desktop.

    Only windows whose title begins with 'publish' are closed, and only by
    sending them the same WM_CLOSE the X button sends, so no file is touched.
    """
    closed = 0
    for hwnd, title, _ in all_windows():
        if not title.startswith("publish"):
            continue
        try:
            win32gui.PostMessageW(hwnd, win32con.WM_CLOSE, 0, 0)
            closed += 1
        except Exception:
            pass
    if closed:
        time.sleep(0.6)
    return closed


def wheel(notches, x=960, y=60):
    """A real wheel turn, the way a hand does it.

    Not SendMessage: WPF does not raise MouseWheel for a synthetic message that
    did not come through the input queue, so a posted WM_MOUSEWHEEL tests
    nothing. A real notch goes to the window under the pointer, which is what
    the dock relies on.
    """
    pointer(x, y, 0.4)
    for _ in range(abs(notches)):
        win32api.mouse_event(win32con.MOUSEEVENTF_WHEEL, 0, 0,
                             120 if notches > 0 else -120, 0)
        time.sleep(0.12)
    time.sleep(0.6)


# ---------------------------------------------------------------- checks

def check(cid, area, expected, fn):
    try:
        ok, actual = fn()
    except Exception as error:
        ok, actual = False, "%s: %s" % (type(error).__name__, error)
    RESULTS.append({"id": cid, "area": area, "expected": expected,
                    "actual": str(actual), "ok": bool(ok)})
    print(("PASS " if ok else "FAIL ") + cid.ljust(34) + str(actual), flush=True)
    save()      # an interrupted run still leaves everything it had reached
    return ok


def save():
    os.makedirs(OUT, exist_ok=True)
    report = {"passed": sum(1 for r in RESULTS if r["ok"]),
              "total": len(RESULTS), "results": RESULTS}
    with open(os.path.join(OUT, "audit.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    # One file per area too: the sweep is run area by area when it has to be
    # kept short, and each run must leave its own record.
    for area in {r["area"] for r in RESULTS}:
        with open(os.path.join(OUT, "audit-%s.json" % area), "w", encoding="utf-8") as f:
            json.dump({"results": [r for r in RESULTS if r["area"] == area]},
                      f, indent=2, ensure_ascii=False)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", default=None)
    args = parser.parse_args()

    os.makedirs(SHOTS, exist_ok=True)
    original_settings = settings()

    # A sweep without a running app reports the same failure 34 times. Bring the
    # published build up first so the checks are about behaviour, not about us.
    ensure_app()

    # What the crash log looked like before the run. "No unhandled exception"
    # means nothing was appended *during* this run: a log left behind by an
    # earlier session (or by a harness that loaded Pegged's assembly) says
    # nothing about this one.
    crash_before = crash_mark()

    # The launch probe's Explorer windows cover the settings window and eat the
    # clicks aimed at it.
    close_launch_windows()

    # Nothing of ours may sit under the top edge while the dock is being driven.
    park_host()
    atexit.register(unpark_host)

    def wanted(area):
        return args.only is None or args.only == area

    # ---------------- dock ----------------
    if wanted("dock"):
        def empty_hint_present():
            texts = [t.window_text() for t in uia().descendants(control_type="Text")]
            return any("挂到这里" in t or "hang here" in t for t in texts)

        def dock_empty_hint():
            clear_cards()
            reveal()
            hint = empty_hint_present()
            texts = [t.window_text() for t in uia().descendants(control_type="Text")]
            return hint, "hint text present" if hint else "no hint text: %s" % texts[:6]

        check("dock.empty_hint", "dock", "空挂绳显示引导文案", dock_empty_hint)

        def dock_clear_all():
            write_test_images(2, "audit-clear")
            wait_cards(2)
            reveal()
            pointer(960, 30)
            time.sleep(0.4)
            buttons()["ClearButton"][0].invoke()
            time.sleep(2.2)
            left = card_count()
            back = empty_hint_present()
            return left == 0 and back, "cards after clear=%d, hint back=%s" % (left, back)

        check("dock.clear_all", "dock", "一键清空取下全部卡片并恢复引导", dock_clear_all)

        def dock_toolbar_auto_hide():
            reveal()
            pointer_away()
            rect = line_rect()
            hit = user32.SendMessageW(line(), 0x0084, 0, ((rect[1] + 36) << 16) | 960)
            return hit != 1, "toolbar hittest=%s after leaving (expect not HTCLIENT)" % hit

        check("dock.toolbar_auto_hide", "dock", "指针离开后工具栏收起并让出点击", dock_toolbar_auto_hide)

        def dock_pin_holds():
            # The toolbar fades in after the pointer arrives; an Invoke that
            # lands during the fade is swallowed by a button that is not
            # hit-testable yet, and the check then fails on a line that was
            # never pinned. Wait for the fade, then prove the pin took.
            reveal()
            pointer(960, 30)
            time.sleep(0.5)
            b = buttons()
            if "PinButton" not in b:
                return False, "no pin button"
            b["PinButton"][0].invoke()
            time.sleep(0.3)
            pointer_away(1.8)
            top = line_rect()[1]
            return top == 0, "top=%d after pointer left (pinned should stay 0)" % top

        check("dock.pin_holds_open", "dock", "固定后指针离开仍展开", dock_pin_holds)

        def dock_unpin_retracts():
            # Only meaningful if the line really is pinned first: an invoke
            # that never took would leave the line tucked, and "still tucked
            # after unpin" would read as a pass.
            reveal()
            pointer(960, 30)
            time.sleep(0.5)
            if line_rect()[1] != 0:
                return False, "line never came down (top=%d), cannot unpin" % line_rect()[1]
            b = buttons()
            b["PinButton"][0].invoke()
            pointer_away(1.8)
            top = line_rect()[1]
            return top < -100, "top=%d after unpin (expect tucked above screen)" % top

        check("dock.unpin_retracts", "dock", "取消固定后自动收起", dock_unpin_retracts)

        def dock_hotkey():
            pointer_away(1.0)
            before = line_rect()[1]
            key_combo(win32con.VK_CONTROL, win32con.VK_MENU, ord("P"))
            after = settle(timeout=3.0)[1]
            key_combo(win32con.VK_CONTROL, win32con.VK_MENU, ord("P"))
            time.sleep(1.4)
            back = line_rect()[1]
            return (before < -100 and after == 0 and back < -100,
                    "top before=%d after hotkey=%d after again=%d" % (before, after, back))

        check("dock.hotkey_toggle", "dock", "Ctrl+Alt+P 展开/收起", dock_hotkey)

        def dock_settles_quickly():
            """The fly-in is a 0.42s spring, not a wobble: a second after the
            reveal it must be down and still."""
            pointer_away(1.0)
            key_combo(win32con.VK_CONTROL, win32con.VK_MENU, ord("P"))
            time.sleep(1.0)
            a = line_rect()[1]
            time.sleep(0.3)
            b = line_rect()[1]
            key_combo(win32con.VK_CONTROL, win32con.VK_MENU, ord("P"))
            time.sleep(1.2)
            return a == 0 and b == 0, "top at 1.0s=%d, at 1.3s=%d (both must be 0)" % (a, b)

        check("dock.reveal_settles", "dock", "展开动画 1 秒内落定不抖动", dock_settles_quickly)

        def dock_peek_keeps_pointer():
            """A new capture starts a 1.6s peek. If the pointer is resting on
            the line when it expires, the line must not tuck itself away."""
            clear_cards()
            reveal()
            settle()
            pointer(960, 60)
            write_test_images(1, "audit-peek")
            time.sleep(2.6)
            top = line_rect()[1]
            return top == 0, "top=%d 2.6s after a peek started under the pointer" % top

        check("dock.peek_keeps_pointer", "dock", "peek 到期时指针在舱体上则不收起", dock_peek_keeps_pointer)

    # ---------------- cards ----------------
    if wanted("card"):
        def clipboard_hangs_capture():
            clear_cards()
            clear_clipboard()
            time.sleep(0.6)
            if not put_clipboard_image():
                return False, "could not put a bitmap on the clipboard"
            hung = wait_cards(1, timeout=9.0)
            return hung, "cards=%d after a bitmap on the clipboard (WatchClipboard=%s)" % (
                card_count(), settings().get("WatchClipboard"))

        check("clipboard.hangs_capture", "card", "复制图片自动上线一张", clipboard_hangs_capture)
        clear_cards()
        clear_clipboard()

        def card_hang_from_file():
            clear_cards()
            write_test_images(1)
            return wait_cards(1), "cards=%d after one file (expect 1)" % card_count()

        check("card.hang_from_file", "card", "新截图自动挂上", card_hang_from_file)

        def card_hover_chrome():
            reveal()
            pointer(*card_point())
            b = buttons()
            need = ["CopyButton", "CropButton", "DeleteButton", "CloseButton"]
            missing = [n for n in need if n not in b]
            return not missing, "missing=%s" % missing if missing else "copy/crop/delete/close all present"

        check("card.hover_chrome", "card", "悬停出现复制/裁剪/删除/关闭", card_hover_chrome)

        def card_click_copies():
            win32clipboard.OpenClipboard()
            win32clipboard.EmptyClipboard()
            win32clipboard.CloseClipboard()
            reveal()
            before = card_count()
            x, y = card_point()
            click(x, y)
            time.sleep(1.6)
            has = clipboard_has_image()
            after = card_count()
            return (has and after == before,
                    "clipboard image=%s, cards %d -> %d (a copy must not hang a duplicate)"
                    % (has, before, after))

        check("card.click_copies_image", "card", "单击卡片复制，且不把自己的图片再挂一张", card_click_copies)

        def card_first_card_clickable():
            """With two cards up, the first one hung must still copy on a click.

            Regression: the empty-state hint was hidden with Opacity, and in
            WPF an element at Opacity 0 still takes clicks. The hint is
            centred, so it sat invisible over the middle of the dock - exactly
            where the first card hangs - and every click meant for the first
            card's picture was swallowed by the hint instead of copying it.
            """
            clear_cards()
            write_test_images(2, "audit-first-%d" % int(time.time()))
            if not wait_cards(2):
                return False, "two cards did not hang"
            reveal()
            settle()
            win32clipboard.OpenClipboard()
            win32clipboard.EmptyClipboard()
            win32clipboard.CloseClipboard()

            pictures = [e.rectangle() for e in uia().descendants(control_type="Image")
                        if e.element_info.automation_id == "ThumbImage"]
            if len(pictures) < 2:
                return False, "only %d pictures found" % len(pictures)

            # The first one hung is the leftmost card on the line.
            first = min(pictures, key=lambda r: r.left)
            x = (first.left + first.right) // 2
            y = first.top + int((first.bottom - first.top) * 0.4)
            click(x, y)
            time.sleep(1.6)
            ok = clipboard_has_image()

            # The checks that follow expect one card hanging.
            clear_cards()
            write_test_images(1, "audit-one-%d" % int(time.time()))
            wait_cards(1)
            return ok, "clicked the first card's picture at (%d,%d): clipboard image=%s" % (x, y, ok)

        check("card.first_card_clickable", "card", "挂 2 张以上时第一张仍可单击复制", card_first_card_clickable)

        def card_double_click_preview():
            before = {h for h, _, _ in windows_of_app(300)}
            x, y = card_point()
            click(x, y, double=True)
            time.sleep(1.6)
            after = {h for h, _, _ in windows_of_app(300)} - before
            titles = [win32gui.GetWindowText(h) for h in after]
            # Remember which handle the preview got: the next two checks are
            # about that window, and "the first window that is not the line"
            # is the settings centre whenever the user (or a previous probe)
            # left it open.
            _preview["hwnd"] = next(iter(after), None)
            return bool(after), "new windows=%s" % titles

        check("card.double_click_preview", "card", "双击打开预览窗口", card_double_click_preview)

        def card_preview_takes_keyboard():
            hwnd = _preview["hwnd"]
            if hwnd is None:
                return False, "no preview window"
            fg = foreground_handle()
            return fg == hwnd, "foreground=%s preview=%s" % (
                win32gui.GetWindowText(fg) or hex(fg), hex(hwnd))

        check("card.preview_takes_keyboard", "card", "预览窗口打开即获得焦点（Esc 可关）",
              card_preview_takes_keyboard)

        def card_preview_close():
            hwnd = _preview["hwnd"]
            ours, where = focused_is_pegged()
            if not ours:
                return False, "Esc not sent: focus is %r, not Pegged" % where
            user32.keybd_event(win32con.VK_ESCAPE, 0, 0, 0)
            user32.keybd_event(win32con.VK_ESCAPE, 0, win32con.KEYEVENTF_KEYUP, 0)
            time.sleep(1.2)
            # Only the preview has to be gone. Another window of ours being
            # open (the settings centre, say) is not a failure of Esc.
            left = [t for h, t, _ in other_windows(0) if h == hwnd]
            return not left, "preview %s closed=%s" % (hex(hwnd) if hwnd else "-", not left)

        check("card.preview_close", "card", "Esc 关闭预览窗口", card_preview_close)

        def card_right_click_drops():
            reveal()
            before = card_count()
            x, y = card_point()
            click(x, y, button="right")
            time.sleep(1.6)
            after = card_count()
            return after == before - 1, "cards %d -> %d" % (before, after)

        check("card.right_click_drops", "card", "右键取下卡片", card_right_click_drops)

        def card_close_button():
            reveal()
            write_test_images(1, "audit-close")
            wait_cards(1)
            before = card_count()
            buttons()["CloseButton"][0].invoke()
            time.sleep(1.6)
            after = card_count()
            return after == before - 1, "cards %d -> %d" % (before, after)

        check("card.close_button_drops", "card", "卡片右上角 × 取下", card_close_button)

        def card_prune_external():
            clear_cards()
            made = write_test_images(1, "audit-prune")
            wait_cards(1)
            os.remove(made[0])
            time.sleep(2.5)
            return card_count() == 0, "cards=%d after external delete" % card_count()

        check("card.prune_external", "card", "外部删除文件后卡片自动脱落", card_prune_external)

        def card_wheel_scroll():
            clear_cards()
            # The line only scrolls in "keep on line" mode. Without it the
            # cards close ranks so that all of them fit, and there is nothing
            # to scroll - which is what the original does too.
            w = open_settings()
            w.child_window(auto_id="NavGeneral").click_input()
            time.sleep(0.4)
            seg(w, "KeepSegmented", "25").click_input()
            time.sleep(0.9)
            write_test_images(16, "audit-scroll")
            wait_cards(16, timeout=14.0)
            reveal()
            settle()
            first = buttons()["CopyButton"][0].rectangle().left
            wheel(6)
            moved = buttons()["CopyButton"][0].rectangle().left
            seg(w, "KeepSegmented", "25").click_input()      # 25 again: still on line
            win32gui.PostMessage(w.handle, win32con.WM_CLOSE, 0, 0)
            time.sleep(0.6)
            return moved > first + 20, "first card x %d -> %d after six notches up" % (first, moved)

        check("card.wheel_scroll", "card", "卡片超出一屏时滚轮向旧照片滚动", card_wheel_scroll)

    # ---------------- crop ----------------
    if wanted("crop"):
        def crop_opens():
            clear_cards()
            write_test_images(1, "audit-crop")
            wait_cards(1)
            reveal()
            pointer(*card_point())
            before = {h for h, _, _ in windows_of_app(800)}
            buttons()["CropButton"][0].invoke()
            time.sleep(1.8)
            new = [h for h, t, _ in other_windows(800) if h not in before]
            titles = [win32gui.GetWindowText(h) for h in new]
            _crop["hwnd"] = new[0] if new else None
            return bool(new), "windows=%s" % titles

        check("crop.opens", "crop", "裁剪浮层打开", crop_opens)

        def crop_takes_keyboard():
            hwnd = _crop["hwnd"]
            if hwnd is None:
                return False, "no overlay"
            fg = foreground_handle()
            return fg == hwnd, "foreground=%s overlay=%s (Esc needs the overlay focused)" % (
                win32gui.GetWindowText(fg) or hex(fg), hex(hwnd))

        check("crop.takes_keyboard", "crop", "裁剪浮层一打开就拿到键盘", crop_takes_keyboard)

        def crop_cancel():
            hwnd = _crop["hwnd"]
            ours, where = focused_is_pegged()
            if not ours:
                return False, "Esc not sent: focus is %r, not Pegged" % where
            user32.keybd_event(win32con.VK_ESCAPE, 0, 0, 0)
            user32.keybd_event(win32con.VK_ESCAPE, 0, win32con.KEYEVENTF_KEYUP, 0)
            time.sleep(1.2)
            left = [t for h, t, _ in other_windows(0) if h == hwnd]
            return not left, "overlay %s closed=%s" % (hex(hwnd) if hwnd else "-", not left)

        check("crop.esc_cancels", "crop", "Esc 取消裁剪", crop_cancel)

        def open_crop(prefix):
            clear_cards()
            write_test_images(1, prefix)
            wait_cards(1)
            reveal()
            pointer(*card_point())
            before = {h for h, _, _ in windows_of_app(800)}
            buttons()["CropButton"][0].invoke()
            time.sleep(1.8)
            new = [h for h, t, _ in other_windows(800) if h not in before]
            _crop["hwnd"] = new[0] if new else None
            return _crop["hwnd"]

        def crop_resize_and_apply():
            """Drag the bottom-right handle in, then apply.

            The handle is the only way to change the crop without typing
            numbers, and apply is the only thing that ever writes a file and
            hangs it, so this is the check that proves the overlay does
            something. The overlay starts with the documented 80%-centred
            selection, which is what puts the handle 10% in from the picture's
            own corner - aiming at the picture corner instead lands on the
            mask, and the drag does nothing at all.
            """
            hwnd = open_crop("audit-crop-resize")
            if hwnd is None:
                return False, "overlay did not open"

            window = uia(hwnd)
            images = window.descendants(control_type="Image")
            if not images:
                return False, "no image inside the overlay"

            picture = images[0].rectangle()
            before = window.child_window(auto_id="SizeHint").window_text()

            x = picture.right - int((picture.right - picture.left) * 0.1)
            y = picture.bottom - int((picture.bottom - picture.top) * 0.1)
            drag((x, y), (x - 150, y - 120))
            time.sleep(0.4)
            after = window.child_window(auto_id="SizeHint").window_text()

            if after == before:
                return False, "drag on the handle did not resize: %r -> %r" % (before, after)

            cards_before = card_count()
            window.child_window(auto_id="CropApply").invoke()
            time.sleep(2.4)

            cards_after = card_count()
            closed = not [t for h, t, _ in other_windows(0) if h == hwnd]
            ok = closed and cards_after == cards_before + 1
            return ok, "size %r -> %r, cards %d -> %d, overlay closed=%s" % (
                before, after, cards_before, cards_after, closed)

        check("crop.resize_and_apply", "crop",
              "拖手柄改选框 + 应用裁剪生成新卡片", crop_resize_and_apply)

    # ---------------- settings ----------------
    if wanted("settings"):
        # A stray overlay from an earlier section would be in front of the
        # settings window and take the clicks meant for it.
        for hwnd, title, _ in other_windows(400):
            win32gui.PostMessage(hwnd, win32con.WM_CLOSE, 0, 0)
        time.sleep(0.8)

        def settings_open():
            reveal()
            pointer(960, 30)
            buttons()["SettingsButton"][0].invoke()
            time.sleep(2.0)
            try:
                w = settings_window()
            except RuntimeError as error:
                return False, str(error)
            return True, "settings window hwnd=%s" % hex(w.handle)

        check("settings.open", "settings", "齿轮打开设置中心", settings_open)

        def settings_takes_keyboard():
            w = settings_window()
            fg = foreground_handle()
            return fg == w.handle, "foreground=%s settings=%s" % (
                win32gui.GetWindowText(fg) or hex(fg), hex(w.handle))

        check("settings.takes_keyboard", "settings", "设置窗口打开即获得焦点", settings_takes_keyboard)

        def settings_nav():
            w = settings_window()
            seen = []
            for nav, pane in (("NavCapture", "CapturePane"), ("NavAppearance", "AppearancePane"),
                              ("NavShortcuts", "ShortcutsPane"), ("NavAbout", "AboutPane"),
                              ("NavGeneral", "GeneralPane")):
                try:
                    w.child_window(auto_id=nav).click_input()
                    time.sleep(0.35)
                    seen.append(nav)
                except Exception as error:
                    return False, "%s: %s" % (nav, error)
            return len(seen) == 5, "visited=%s" % seen

        check("settings.nav_pages", "settings", "左侧五个导航页都能切换", settings_nav)

        def settings_toggle_persists():
            w = settings_window()
            w.child_window(auto_id="NavCapture").click_input()
            time.sleep(0.4)
            before = settings()["WatchClipboard"]
            w.child_window(auto_id="ClipboardSwitch").click_input()
            time.sleep(0.8)
            after = settings()["WatchClipboard"]
            w.child_window(auto_id="ClipboardSwitch").click_input()
            time.sleep(0.8)
            back = settings()["WatchClipboard"]
            return (after != before and back == before,
                    "WatchClipboard %s -> %s -> %s" % (before, after, back))

        check("settings.toggle_persists", "settings", "开关写回 settings.json", settings_toggle_persists)

        def settings_theme_live():
            w = settings_window()
            w.child_window(auto_id="NavAppearance").click_input()
            time.sleep(0.4)
            light = frame_luma()
            w.child_window(auto_id="ThemeCombo").select(2)
            time.sleep(1.4)
            dark = frame_luma()
            w.child_window(auto_id="ThemeCombo").select(0)
            time.sleep(1.0)
            return dark < light - 60, "card frame light=%d dark=%d" % (light, dark)

        check("settings.theme_live", "settings", "切换深色主题卡片边框立刻换色", settings_theme_live)

        def settings_size_live():
            w = settings_window()
            w.child_window(auto_id="NavGeneral").click_input()
            time.sleep(0.4)
            seg(w, "SizeSegmented", index=0).click_input()
            time.sleep(1.2)
            reveal()
            settle()
            small_h = line_rect()[3] - line_rect()[1]
            seg(w, "SizeSegmented", index=2).click_input()
            time.sleep(1.2)
            reveal()
            settle()
            large_h = line_rect()[3] - line_rect()[1]
            seg(w, "SizeSegmented", index=1).click_input()
            time.sleep(1.0)
            return large_h > small_h, "dock height small=%d large=%d" % (small_h, large_h)

        check("settings.size_live", "settings", "尺寸切换立刻改变舱体高度", settings_size_live)

        def settings_hotkey_esc():
            w = settings_window()
            w.child_window(auto_id="NavShortcuts").click_input()
            time.sleep(0.4)
            before = settings().get("Shortcut")
            box = w.child_window(auto_id="HotkeyBox")
            box.click_input()
            time.sleep(0.3)
            ours, where = focused_is_pegged()
            if not ours:
                return False, "Esc not sent: focus is %r, not Pegged" % where
            user32.keybd_event(win32con.VK_ESCAPE, 0, 0, 0)
            user32.keybd_event(win32con.VK_ESCAPE, 0, win32con.KEYEVENTF_KEYUP, 0)
            time.sleep(0.8)
            after = settings().get("Shortcut")
            return after == before, "shortcut %s -> %s" % (before, after)

        check("settings.hotkey_esc_keeps", "settings", "录制时 Esc 保留原快捷键", settings_hotkey_esc)

        def screenshots_folder_resolves():
            """Does this Windows expose FOLDERID_Screenshots at all?

            Asked of the OS, not of the app: on a profile that has never
            saved a capture there the shell answers ERROR_FILE_NOT_FOUND, and
            no amount of registry writing, KF_FLAG_DONT_VERIFY or
            SHChangeNotify changes that (all three were measured).
            """
            from ctypes import wintypes

            class Guid(ctypes.Structure):
                _fields_ = [("a", wintypes.DWORD), ("b", wintypes.WORD),
                            ("c", wintypes.WORD), ("d", ctypes.c_ubyte * 8)]

            fid = Guid()
            ctypes.windll.ole32.CLSIDFromString(
                ctypes.c_wchar_p("{B7BEDE81-DF94-4682-A7D8-57A71709C8E0}"),
                ctypes.byref(fid))
            out = ctypes.c_void_p()
            hr = ctypes.windll.shell32.SHGetKnownFolderPath(
                ctypes.byref(fid), 0, None, ctypes.byref(out))
            if hr != 0:
                return None, "0x%08X" % (hr & 0xFFFFFFFF)
            try:
                return ctypes.wstring_at(out), "ok"
            finally:
                ctypes.windll.ole32.CoTaskMemFree(out)

        def settings_inbox_roundtrip():
            """The switch honours what the system can actually do.

            Two branches, both user-visible:
              resolvable   -> click on, settings true, click off, settings false
              unresolvable -> the switch is greyed out and says why, and
                              nothing is written. It must not look enabled and
                              then bounce back, and it must not open a modal
                              (which used to appear behind the settings window
                              and swallow the next click).
            """
            w = settings_window()
            w.child_window(auto_id="NavCapture").click_input()
            time.sleep(0.4)

            folder, why = screenshots_folder_resolves()
            sw = w.child_window(auto_id="InboxSwitch")

            if folder is None:
                enabled = sw.is_enabled()
                sw.click_input()
                time.sleep(1.2)
                after = settings().get("InboxEnabled")
                popups = [t for _, t, _ in other_windows(0) if "Inbox" in t]
                ok = (not enabled) and after is False and not popups
                return ok, "system has no Screenshots folder (%s); switch enabled=%s settings=%s popups=%s" % (
                    why, enabled, after, popups)

            sw.click_input()
            time.sleep(1.6)
            on = settings().get("InboxEnabled")
            sw.click_input()
            time.sleep(1.6)
            off = settings().get("InboxEnabled")
            return (on is True and off is False,
                    "InboxEnabled %s -> %s (folder %s)" % (on, off, folder))

        check("settings.inbox_roundtrip", "settings",
              "Inbox 开关按系统能力行事（可开可关 / 不支持时置灰不弹窗）",
              settings_inbox_roundtrip)

        def settings_close():
            w = settings_window()
            win32gui.PostMessage(w.handle, win32con.WM_CLOSE, 0, 0)
            time.sleep(1.2)
            left = [t for _, t, _ in other_windows(400)]
            return not left, "left open=%s" % left

        check("settings.close", "settings", "设置窗口可关闭", settings_close)

    # ---------------- cleanup ----------------
    clear_cards()
    for name in os.listdir(WATCH):
        if name.startswith("audit"):
            try:
                os.remove(os.path.join(WATCH, name))
            except Exception:
                pass
    with open(SETTINGS, "w", encoding="utf-8") as f:
        json.dump(original_settings, f, indent=2, ensure_ascii=False)

    grew = crash_mark() != crash_before
    RESULTS.append({"id": "run.no_crash_log", "area": "run",
                    "expected": "全程无未处理异常", "actual":
                        "crash.log untouched" if not grew else "crash.log written during the run",
                    "ok": not grew})
    print(("PASS " if not grew else "FAIL ") + "run.no_crash_log".ljust(34)
          + ("crash.log untouched" if not grew else "crash.log written during the run"), flush=True)

    unpark_host()

    passed = sum(1 for r in RESULTS if r["ok"])
    save()

    print("\n%d/%d passed" % (passed, len(RESULTS)), flush=True)
    return 0 if passed == len(RESULTS) else 1


if __name__ == "__main__":
    sys.exit(main())
