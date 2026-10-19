#!/usr/bin/env python3
"""Clicks the three hover buttons on a card with a real mouse and reports what
each one actually did.

The user's report: "hang one picture, click any of the three little icons, and
all of them say 已复制！". So the question per button is not "did it throw" but
"what did the user see":

  Copy   -> a green "已复制！" badge on the card, clipboard holds the picture
  Crop   -> the crop overlay opens
  Delete -> the card leaves the line

UIA Invoke would not reproduce this: it calls the button's automation peer and
raises Click directly, skipping the press/release pair that the card itself
also listens for. Every click here is a real mouse down/up at the button's
on-screen centre, exactly like a hand.

    python scripts/e2e/repro_card_buttons.py
"""
import ctypes
import json
import os
import subprocess
import sys
import time

import psutil
import win32api
import win32clipboard
import win32con
import win32gui
import win32process
from PIL import Image, ImageDraw, ImageGrab

try:
    from pywinauto import Desktop
except ImportError:                                     # pragma: no cover
    sys.exit("pywinauto is required")

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WATCH = os.path.join(os.environ["APPDATA"], "Pegged", "Screenshots")
SHOTS = os.path.join(ROOT, "outputs", "acceptance", "shots", "audit")
OUT = os.path.join(ROOT, "outputs", "acceptance", "repro-card-buttons.json")

# Brush.Success, light theme (#0F7B0F). The badge is the only thing on the line
# painted with it, so counting these pixels answers "is the badge showing?".
SUCCESS = (15, 123, 15)
SUCCESS_TOL = 26

ctypes.windll.shcore.SetProcessDpiAwareness(2)
user32 = ctypes.windll.user32


# ---------------------------------------------------------------- plumbing

def pids():
    return {p.pid for p in psutil.process_iter(["name"]) if p.info["name"] == "Pegged.exe"}


def ensure_app(timeout=25.0):
    exe = os.path.join(ROOT, "publish", "Pegged.exe")
    if not pids():
        subprocess.Popen([exe], cwd=os.path.dirname(exe),
                         creationflags=0x00000008 | 0x00000200 | 0x01000000)
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            if line():
                return
        except RuntimeError:
            pass
        time.sleep(0.4)
    raise SystemExit("Pegged did not come up")


def windows_of_app():
    out = []
    mine = pids()

    def cb(hwnd, _):
        _, pid = win32process.GetWindowThreadProcessId(hwnd)
        if pid in mine and win32gui.IsWindowVisible(hwnd) and win32gui.GetWindowText(hwnd):
            out.append((hwnd, win32gui.GetWindowText(hwnd), win32gui.GetWindowRect(hwnd)))

    win32gui.EnumWindows(cb, None)
    return out


def line():
    for hwnd, title, _ in windows_of_app():
        if title == "Pegged":
            return hwnd
    raise RuntimeError("line window not found")


def line_rect():
    return win32gui.GetWindowRect(line())


def uia(hwnd=None):
    return Desktop(backend="uia").window(handle=hwnd or line())


def by_id(window=None):
    out = {}
    for b in (window or uia()).descendants(control_type="Button"):
        out.setdefault(b.automation_id(), []).append(b)
    return out


def card_count():
    return len(by_id().get("CloseButton", []))


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


def click(x, y, settle=0.3):
    user32.SetCursorPos(int(x), int(y))
    if settle:
        time.sleep(settle)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.07)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)


def badge_pixels(box):
    """How many pixels of the success green sit inside `box` right now."""
    img = ImageGrab.grab(bbox=box).convert("RGB")
    n = 0
    for px in img.getdata():
        if (abs(px[0] - SUCCESS[0]) <= SUCCESS_TOL
                and abs(px[1] - SUCCESS[1]) <= SUCCESS_TOL
                and abs(px[2] - SUCCESS[2]) <= SUCCESS_TOL):
            n += 1
    return n


def clipboard_has_image():
    try:
        win32clipboard.OpenClipboard()
        try:
            return bool(win32clipboard.IsClipboardFormatAvailable(win32con.CF_DIB)
                        or win32clipboard.IsClipboardFormatAvailable(win32con.CF_BITMAP))
        finally:
            win32clipboard.CloseClipboard()
    except Exception:
        return False


def clear_clipboard():
    try:
        win32clipboard.OpenClipboard()
        win32clipboard.EmptyClipboard()
        win32clipboard.CloseClipboard()
    except Exception:
        pass


def crop_open():
    return any("裁剪" in t for _, t, _ in windows_of_app())


def close_crop():
    """Dismisses the crop overlay if the previous trial left it open.

    The overlay is a full-screen mask: anything left under it never sees a
    click, so a stale overlay turns the next trial into a false failure.
    """
    for hwnd, title, _ in windows_of_app():
        if "裁剪" not in title:
            continue
        w = Desktop(backend="uia").window(handle=hwnd)
        for b in w.descendants(control_type="Button"):
            if b.automation_id() == "CropCancel":
                b.invoke()
                time.sleep(0.7)
                return True
        win32api.keybd_event(win32con.VK_ESCAPE, 0, 0, 0)
        win32api.keybd_event(win32con.VK_ESCAPE, 0, win32con.KEYEVENTF_KEYUP, 0)
        time.sleep(0.7)
        return True
    return False


def write_one(tag):
    os.makedirs(WATCH, exist_ok=True)
    path = os.path.join(WATCH, "btn-%s-%d.png" % (tag, int(time.time() * 1000)))
    im = Image.new("RGB", (760, 480), (55, 120, 165))
    d = ImageDraw.Draw(im)
    d.rectangle([18, 18, 742, 462], outline=(255, 255, 255), width=5)
    d.text((30, 30), tag, fill=(255, 255, 255))
    im.save(path)
    return path


def wait_cards(n, timeout=9.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if card_count() == n:
            return True
        time.sleep(0.25)
    return card_count() == n


def clear_cards():
    reveal()
    b = by_id().get("ClearButton")
    if b:
        b[0].invoke()
        time.sleep(2.2)


# ------------------------------------------------------------------- trial

def trial(button_id, tag, instant=False):
    """Hangs one card, clicks `button_id` with a real mouse, reports what happened.

    `instant` skips the pause between moving the pointer and pressing, which is
    the case the action row must survive: a click that arrives before the row
    has faded in must not land on an invisible button.
    """
    clear_cards()
    close_crop()
    clear_clipboard()
    write_one(tag)
    if not wait_cards(1):
        return {"button": button_id, "error": "the card never hung"}
    reveal()
    time.sleep(0.4)

    # Read the button rect now: a swaying card moves, and a 28px target does not
    # forgive a stale rectangle.
    btns = by_id()
    if button_id not in btns:
        return {"button": button_id, "error": "button not in the automation tree"}
    rect = btns[button_id][0].rectangle()
    cx, cy = (rect.left + rect.right) // 2, (rect.top + rect.bottom) // 2
    card = btns["CloseButton"][0].rectangle()
    box = (max(card.left - 60, 0), max(card.top - 60, 0),
           min(card.right + 60, user32.GetSystemMetrics(0)),
           min(card.bottom + 90, user32.GetSystemMetrics(1)))

    click(cx, cy, settle=0 if instant else 0.3)
    # The badge is on screen within a frame; a take-down falls for 550ms, so the
    # two outcomes need different patience. Sample the badge early, then give
    # the card time to actually leave.
    time.sleep(0.45)
    badge = badge_pixels(box)
    deadline = time.time() + 2.4
    while time.time() < deadline and card_count() == 1:
        time.sleep(0.2)

    result = {
        "button": button_id,
        "clicked_at": [cx, cy],
        "badge_pixels": badge,
        "badge_showing": badge > 120,
        "clipboard_image": clipboard_has_image(),
        "crop_open": crop_open(),
        "cards": card_count(),
    }

    path = os.path.join(SHOTS, "repro-%s.png" % tag)
    os.makedirs(SHOTS, exist_ok=True)
    ImageGrab.grab(bbox=box).save(path)
    result["shot"] = path
    return result


def hover_repeats():
    """The row must come back every time the pointer does.

    WPF cannot supply this on a click-through window: it never sees the pointer
    leave, so it raises MouseEnter once and the card stays "entered" forever.
    Driven that way the row appears on the first hover only, and every later
    click lands on the picture instead - the reported "all three say 已复制".
    """
    clear_cards()
    close_crop()
    write_one("repeat")
    if not wait_cards(1):
        return False, "the card never hung"
    reveal()
    btns = by_id()
    rect = btns["CropButton"][0].rectangle()
    cx, cy = (rect.left + rect.right) // 2, (rect.top + rect.bottom) // 2

    for _ in range(2):
        user32.SetCursorPos(960, 2)
        time.sleep(0.7)
        user32.SetCursorPos(cx, cy)
        time.sleep(0.7)

    click(cx, cy, settle=0.15)
    time.sleep(0.6)
    opened = crop_open()
    close_crop()
    return opened, "crop opened on the second hover=%s" % opened


def main():
    ensure_app()
    results = []
    for button_id, tag in (("CopyButton", "copy"),
                           ("CropButton", "crop"),
                           ("DeleteButton", "delete")):
        r = trial(button_id, tag)
        results.append(r)
        print(json.dumps(r, ensure_ascii=False))

    print()
    ok = True

    def check(label, passed, detail):
        nonlocal ok
        ok = ok and passed
        print("%s %-28s %s" % ("PASS" if passed else "FAIL", label, detail))

    r = results[0]
    check("copy.badge", r.get("badge_showing") and r.get("clipboard_image"),
          "badge=%s clipboard=%s" % (r.get("badge_pixels"), r.get("clipboard_image")))
    r = results[1]
    check("crop.opens", r.get("crop_open"), "crop overlay open=%s" % r.get("crop_open"))
    check("crop.no_badge", not r.get("badge_showing"), "badge pixels=%s" % r.get("badge_pixels"))
    r = results[2]
    check("delete.takes_down", r.get("cards") == 0, "cards after click=%s" % r.get("cards"))
    check("delete.no_badge", not r.get("badge_showing"), "badge pixels=%s" % r.get("badge_pixels"))

    # The mirror bug: while the row is faded out it must not own the pixels over
    # the picture. A click that lands before the fade-in has to copy, not delete.
    fast = trial("DeleteButton", "instant", instant=True)
    results.append(fast)
    print(json.dumps(fast, ensure_ascii=False))
    check("row.hidden_not_clickable", fast.get("cards") == 1,
          "cards after an instant click on the hidden row=%s (expect 1, not taken down)"
          % fast.get("cards"))

    repeats, detail = hover_repeats()
    check("row.hover_repeats", repeats, detail)

    # The cross in the corner is invisible at rest too, and it deletes: a click
    # that lands before the chrome fades in must copy, not take the card down.
    corner = trial("CloseButton", "corner", instant=True)
    results.append(corner)
    print(json.dumps(corner, ensure_ascii=False))
    check("corner.hidden_not_clickable", corner.get("cards") == 1,
          "cards after an instant click on the hidden cross=%s (expect 1)"
          % corner.get("cards"))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({"ok": ok, "results": results}, f, ensure_ascii=False, indent=2)
    print("\n%s" % ("GREEN: each button did its own job" if ok
                    else "RED: at least one button did the wrong thing"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
