#!/usr/bin/env python3
"""Throwaway: sample a horizontal strip of pixels across a hanging card,
in light and in dark, and print it. Answers "what does the frame actually
paint" without guessing.
"""
import ctypes
import os
import time

import win32con
import win32gui
import win32process
from PIL import ImageGrab

import audit_walkthrough as A

user32 = ctypes.windll.user32
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    user32.SetProcessDPIAware()


def strip():
    A.write_test_images(1, "theme-scan-%d" % int(time.time()))
    A.wait_cards(1)
    A.reveal()
    A.settle()
    el = [e for e in A.uia().descendants(control_type="Image")
          if e.element_info.automation_id == "ThumbImage"][0]
    r = el.rectangle()
    y = r.top + (r.bottom - r.top) // 2
    out = []
    for x in range(r.left - 12, r.left + 16):
        out.append("%d:%d" % (x, ImageGrab.grab(bbox=(x - 1, y - 1, x + 2, y + 2),
                                                all_screens=True).convert("L").getpixel((1, 1))))
    return "  ".join(out)


def main():
    w = A.open_settings()
    w.child_window(auto_id="NavAppearance").click_input()
    time.sleep(0.4)
    print("light", A.settings().get("Theme"), strip())
    w.child_window(auto_id="ThemeCombo").select(2)
    time.sleep(1.6)
    print("dark ", A.settings().get("Theme"), strip())
    w.child_window(auto_id="ThemeCombo").select(0)
    time.sleep(1.0)


main()
