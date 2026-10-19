#!/usr/bin/env python3
"""Repro: with two or more cards, the first one hung is hard to click.

The user's symptom is about *clicking the picture to copy it*, so the loop
clicks real points inside each picture and asserts three things:

  1. the point belongs to the picture it is aimed at (no invisible chrome
     owns it), and
  2. the clipboard carries an image afterwards, and
  3. no window opened instead (a click that lands on a button of the hover
     row opens the crop overlay or a preview).

Points come from the app's own idea of the picture: the `ThumbImage` element
per card.

    python scripts/e2e/repro_first_card_click.py [--cards 2] [--grid]
"""
import argparse
import os
import sys
import time

import win32api
import win32clipboard
import win32con
import win32gui
from PIL import ImageGrab
from pywinauto.uia_element_info import UIAElementInfo

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audit_walkthrough as A           # noqa: E402  (the harness lives there)


def thumbs():
    """One rect per card, from the ThumbImage element (physical pixels)."""
    found = [e for e in A.uia().descendants(control_type="Image")
             if e.element_info.automation_id == "ThumbImage"]
    return [e.rectangle() for e in found]


def inset(rect, fx=0.10, fy=0.10):
    """Trims the UIA bounds, which include the tilt and the shadow."""
    dx = int((rect.right - rect.left) * fx)
    dy = int((rect.bottom - rect.top) * fy)
    return (rect.left + dx, rect.top + dy, rect.right - dx, rect.bottom - dy)


def button_boxes():
    """Every hover control on every card: copy, crop, delete, take down.

    A click that lands on one of them is not a click on the picture, so the
    repro must not aim there - and clicking the delete one would take a card
    off the line and invalidate the whole run.
    """
    out = []
    for key in ("CloseButton", "CopyButton", "CropButton", "DeleteButton"):
        try:
            out += [b.rectangle() for b in A.buttons().get(key, [])]
        except Exception:
            pass
    return out


def on_chrome(x, y, chrome):
    return any(r.left - 2 <= x <= r.right + 2 and r.top - 2 <= y <= r.bottom + 2
               for r in chrome)


def owner_card(x, y, boxes):
    """Which card's picture owns the point, by containment (or None)."""
    for i, (l, t, r, b) in enumerate(boxes):
        if l <= x <= r and t <= y <= b:
            return i
    return None


def click(x, y):
    win32api.SetCursorPos((int(x), int(y)))
    time.sleep(0.35)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.06)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(1.3)


def clear_clipboard():
    try:
        win32clipboard.OpenClipboard()
        win32clipboard.EmptyClipboard()
        win32clipboard.SetClipboardText("repro")
        win32clipboard.CloseClipboard()
    except Exception:
        pass


def close_strays():
    for hwnd, title, _ in A.other_windows(300):
        if title in ("Pegged", "设置"):
            continue
        try:
            win32gui.PostMessage(hwnd, win32con.WM_CLOSE, 0, 0)
        except Exception:
            pass
    time.sleep(0.4)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cards", type=int, default=2)
    ap.add_argument("--grid", action="store_true")
    args = ap.parse_args()

    A.park_host()
    A.clear_cards()
    # A fresh prefix every run: overwriting an existing name is a change, not
    # a creation, and the watcher (rightly) ignores it.
    A.write_test_images(args.cards, "repro-%d" % int(time.time()))
    if not A.wait_cards(args.cards):
        print("SETUP FAIL: wanted %d cards, saw %d" % (args.cards, A.card_count()))
        return 2
    A.reveal()
    A.settle()
    line_hwnd = A.line()
    print("cards=%d  line=%s" % (A.card_count(), A.line_rect()))

    rects = [inset(r) for r in thumbs()]
    chrome = button_boxes()
    for i, r in enumerate(rects):
        print("card[%d] picture box=%s" % (i, r))
    print("hover controls: %d rects" % len(chrome))

    failures = 0

    def probe(i, x, y):
        nonlocal failures
        if on_chrome(x, y, chrome):
            print("card[%d] (%d,%d) skipped: a hover control sits there" % (i, x, y))
            return
        under = win32gui.GetAncestor(win32gui.WindowFromPoint((x, y)),
                                     win32con.GA_ROOT)
        before = {h for h, _, _ in A.windows_of_app(300)}
        clear_clipboard()
        click(x, y)
        has = A.clipboard_has_image()
        opened = [ti for h, ti, _ in A.other_windows(300) if h not in before]
        ok = (has and not opened and under == line_hwnd
              and owner_card(x, y, rects) == i)
        failures += 0 if ok else 1
        print("card[%d] (%d,%d) copied=%s opened=%s  %s"
              % (i, x, y, has, opened, "OK" if ok else "BAD"))
        close_strays()

    print("\n--- click three heights inside each picture ---")
    for i, (l, t, r, b) in enumerate(rects):
        cx = (l + r) // 2
        for fy in (0.2, 0.4, 0.6):
            probe(i, cx, int(t + (b - t) * fy))

    if args.grid:
        print("\n--- 3x3 grid inside each picture ---")
        for i, (l, t, r, b) in enumerate(rects):
            if i:                       # the copies just made can re-flow the
                rects = [inset(r) for r in thumbs()]   # line: re-read boxes
                print("  now %d cards, boxes=%s" % (A.card_count(), rects))
            l, t, r, b = rects[i]
            print("card[%d]" % i)
            for ry in (0.15, 0.35, 0.55):
                for rx in (0.2, 0.5, 0.8):
                    probe(i, int(l + (r - l) * rx), int(t + (b - t) * ry))

    A.pointer_away()
    print("\n%s" % ("GREEN: every click on a picture copied that picture"
                    if failures == 0 else "RED: %d click(s) failed" % failures))
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
