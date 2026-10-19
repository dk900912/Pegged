#!/usr/bin/env python3
"""Throwaway: screenshot the hanging card in light and dark themes."""
import os
import time

from PIL import ImageGrab

import audit_walkthrough as A

OUT = os.path.join(A.OUT, "shots")


def shot(name):
    A.write_test_images(1, "theme-shot-%d" % int(time.time()))
    A.wait_cards(1)
    A.reveal()
    A.settle()
    el = [e for e in A.uia().descendants(control_type="Image")
          if e.element_info.automation_id == "ThumbImage"][0]
    r = el.rectangle()
    ImageGrab.grab(bbox=(r.left - 20, r.top - 20, r.right + 20, r.bottom + 20),
                   all_screens=True).save(os.path.join(OUT, name))
    print("wrote", name)


w = A.open_settings()
w.child_window(auto_id="NavAppearance").click_input()
time.sleep(0.4)
shot("frame-light.png")
w.child_window(auto_id="ThemeCombo").select(2)
time.sleep(1.6)
shot("frame-dark.png")
w.child_window(auto_id="ThemeCombo").select(0)
time.sleep(1.0)
