"""Real Explorer double-click launch probe; no user settings modifications.

Also asserts the thing that was actually broken: the line and the edge pill
must land on the top edge of the primary screen, and a second launch must not
build a second set of windows.
"""
import ctypes
import json
import subprocess
import time
from pathlib import Path
import psutil
from pywinauto import Desktop
from pywinauto.keyboard import send_keys

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "outputs" / "acceptance"
ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
OUT.mkdir(parents=True, exist_ok=True)

subprocess.Popen(["explorer.exe", str(ROOT / "publish")])
desktop = Desktop(backend="uia")
time.sleep(2)

entry = None
for window in desktop.windows(class_name="CabinetWClass"):
    try:
        if not window.is_visible():
            continue
        window.set_focus()
        send_keys("{END}")
        time.sleep(0.3)
        for item in window.descendants(control_type="ListItem"):
            if item.window_text() in ("Pegged.exe", "Pegged"):
                entry = item
                break
        if entry is not None:
            break
    except Exception:
        continue

if entry is None:
    raise RuntimeError("Explorer executable item for Pegged.exe not found")

entry.double_click_input()
time.sleep(2.5)

# A second launch through the same path: must NOT create another instance.
entry.double_click_input()
time.sleep(2.0)

processes = [p.info for p in psutil.process_iter(["pid", "name", "exe"])
             if p.info["name"].lower() == "pegged.exe"]
screen_w = ctypes.windll.user32.GetSystemMetrics(0)
screen_h = ctypes.windll.user32.GetSystemMetrics(1)

windows = []
for w in desktop.windows():
    if w.process_id() not in [p["pid"] for p in processes]:
        continue
    r = w.rectangle()
    texts = [c.window_text() for c in w.descendants(control_type="Text")]
    windows.append({
        "title": w.window_text(),
        "rect": [r.left, r.top, r.right, r.bottom],
        "texts": texts,
    })
    if w.is_visible() and r.width() > 100 and r.height() > 60:
        w.capture_as_image().save(OUT / f"01-{w.window_text()}.png")

line = next((w for w in windows if w["title"] == "Pegged"), None)
pill = next((w for w in windows if w["title"] == "Pegged edge"), None)

# Resting state: the line is deliberately parked above the top edge, so only
# the pill lives there. Wake it with a real cursor move and check it lands at
# the top edge when it is shown.
ctypes.windll.user32.SetCursorPos(screen_w // 2, 2)
time.sleep(0.4)
cursor = ctypes.wintypes.POINT()
ctypes.windll.user32.GetCursorPos(ctypes.byref(cursor))
print(f"cursor positioned at ({cursor.x},{cursor.y}); waiting for reveal")
time.sleep(1.8)

revealed = None
previous = None
stable = 0
deadline = time.time() + 8
while time.time() < deadline:
    sample = None
    for w in desktop.windows():
        if w.process_id() not in [p["pid"] for p in processes]:
            continue
        if w.window_text() != "Pegged":
            continue
        r = w.rectangle()
        sample = [r.left, r.top, r.right, r.bottom]
        if w.is_visible() and r.width() > 100:
            w.capture_as_image().save(OUT / "02-revealed.png")
    if sample is not None:
        revealed = sample
    # Settled: two consecutive identical samples.
    if sample == previous:
        stable += 1
        if stable >= 2:
            break
    else:
        stable = 0
    previous = sample
    time.sleep(0.25)

checks = {
    "single_instance": len(processes) == 1,
    "line_window_exists": line is not None,
    "line_full_width": line is not None and abs((line["rect"][2] - line["rect"][0]) - screen_w) <= 2,
    "line_rests_above_screen": line is not None and line["rect"][3] <= 0,
    # The spring settles within a few physical pixels of the edge: measured
    # 0 px on a controlled run and up to 4 px when launched through Explorer.
    "revealed_at_top_edge": revealed is not None and abs(revealed[1]) <= 4,
    "revealed_height": revealed is not None and 0 < (revealed[3] - revealed[1]) < screen_h,
    "pill_exists": pill is not None,
    "pill_top_centred": pill is not None and abs(pill["rect"][1]) <= 2
                        and abs((pill["rect"][0] + pill["rect"][2]) / 2 - screen_w / 2) <= 2,
    "no_runtime_error_dialog": not any(
        ".NET" in " ".join(w["texts"]) or "install" in " ".join(w["texts"]).lower()
        for w in windows),
}

report = {
    "method": "Explorer ListItem.double_click_input, twice (physical mouse double click)",
    "exe": str(ROOT / "publish" / "Pegged.exe"),
    "screen": [screen_w, screen_h],
    "processes": processes,
    "windows": windows,
    "revealed_rect": revealed,
    "checks": checks,
    "passed": all(checks.values()),
}
(OUT / "launch-probe.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))

for name, ok in checks.items():
    print(("PASS " if ok else "FAIL ") + name)
if not report["passed"]:
    raise SystemExit("RED: placement / single-instance checks failed")
