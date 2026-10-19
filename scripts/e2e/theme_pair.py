#!/usr/bin/env python3
"""Throwaway: the same single card, same rect, light then dark."""
import os
import time

from PIL import ImageGrab

import audit_walkthrough as A

OUT = os.path.join(A.OUT, "shots")


def clear_and_hang(prefix):
    """Every card off the line, then exactly one new one on it."""
    try:
        for b in A.buttons().get("CloseButton", []):
            b.click_input()
    except Exception:
        pass
    time.sleep(0.8)
    A.write_test_images(1, prefix)
    A.wait_cards(1, timeout=8)


def card_box():
    A.reveal()
    A.settle()
    el = [e for e in A.uia().descendants(control_type="Image")
          if e.element_info.automation_id == "ThumbImage"][0]
    r = el.rectangle()
    return (r.left - 24, r.top - 24, r.right + 24, r.bottom + 24)


w = A.open_settings()
w.child_window(auto_id="NavAppearance").click_input()
time.sleep(0.4)

clear_and_hang("pair-light-%d" % int(time.time()))
box = card_box()
ImageGrab.grab(bbox=box, all_screens=True).save(os.path.join(OUT, "pair-light.png"))

w.child_window(auto_id="ThemeCombo").select(2)
time.sleep(1.6)
A.reveal()
A.settle()
ImageGrab.grab(bbox=box, all_screens=True).save(os.path.join(OUT, "pair-dark.png"))

w.child_window(auto_id="ThemeCombo").select(0)
time.sleep(1.0)
print("wrote pair-light.png pair-dark.png at", box)
