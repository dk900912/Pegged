#!/usr/bin/env python3
"""Throwaway: does the settings window itself turn dark when the theme flips?"""
import os
import time

from PIL import ImageGrab

import audit_walkthrough as A

OUT = os.path.join(A.OUT, "shots")


def save(name, box):
    ImageGrab.grab(bbox=box, all_screens=True).save(os.path.join(OUT, name))
    print("wrote", name)


w = A.open_settings()
w.child_window(auto_id="NavAppearance").click_input()
time.sleep(0.4)
r = w.rectangle()
save("settings-light.png", (r.left + 8, r.top + 8, r.right - 8, r.bottom - 8))
w.child_window(auto_id="ThemeCombo").select(2)
time.sleep(1.6)
r = w.rectangle()
save("settings-dark.png", (r.left + 8, r.top + 8, r.right - 8, r.bottom - 8))
w.child_window(auto_id="ThemeCombo").select(0)
time.sleep(1.0)
