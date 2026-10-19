#!/usr/bin/env python3
"""Throwaway: full-screen diff of light vs dark, listing what repainted."""
import os
import time

from PIL import Image, ImageChops, ImageGrab

import audit_walkthrough as A

OUT = os.path.join(A.OUT, "shots")

w = A.open_settings()
w.child_window(auto_id="NavAppearance").click_input()
time.sleep(0.4)

A.clear_all = None
A.write_test_images(1, "diff-light-%d" % int(time.time()))
A.wait_cards(1)
A.reveal()
A.settle()
time.sleep(0.3)
light = ImageGrab.grab(all_screens=True).convert("RGB")

w.child_window(auto_id="ThemeCombo").select(2)
time.sleep(1.6)
A.reveal()
A.settle()
time.sleep(0.3)
dark = ImageGrab.grab(all_screens=True).convert("RGB")
light.save(os.path.join(OUT, "full-light.png"))
dark.save(os.path.join(OUT, "full-dark.png"))

box = ImageChops.difference(light, dark).getbbox()
print("difference bbox:", box)
if box:
    d = ImageChops.difference(light, dark).convert("L")
    print("max diff:", max(d.getdata()))

w.child_window(auto_id="ThemeCombo").select(0)
time.sleep(1.0)
