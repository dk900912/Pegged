#!/usr/bin/env bash
# Pegged end-to-end smoke: drives the real app with a real cursor.
# Every step saves a screenshot so the result can be eyeballed.
set -u
ROOT="C:/Users/dk900/WorkBuddy AI/win11-tendedero"
E2E="$ROOT/scripts/e2e"
SHOTS="$E2E/shots"
EXE="$ROOT/src/Pegged/bin/Debug/net8.0-windows/Pegged.exe"
PS="/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
APPDATA_WIN="C:/Users/dk900/AppData/Roaming"

export DOTNET_ROOT="C:/Users/dk900/dotnet"

ps_move() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/move.ps1" -X "$1" -Y "$2" >/dev/null 2>&1; }
ps_click() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/click.ps1" >/dev/null 2>&1; }
ps_shot() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/shot.ps1" -Name "$1" -H "${2:-320}" >/dev/null 2>&1; }

# --- clean slate ---------------------------------------------------------
taskkill //F //IM Pegged.exe >/dev/null 2>&1
rm -f "$APPDATA_WIN/Pegged/Screenshots/"* 2>/dev/null
rm -f "$SHOTS/"*.png 2>/dev/null

# --- seed: three sample captures ---------------------------------------
"C:/Users/dk900/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe" - <<'PY'
import os
from PIL import Image, ImageDraw
folder = os.path.join(os.environ["APPDATA"], "Pegged", "Screenshots")
os.makedirs(folder, exist_ok=True)
def shot(name, c1, c2, label):
    w, h = 800, 500
    im = Image.new("RGB", (w, h), c1)
    d = ImageDraw.Draw(im)
    for i in range(h):
        t = i/h
        d.line([(0,i),(w,i)], fill=tuple(int(c1[j]+(c2[j]-c1[j])*t) for j in range(3)))
    d.rectangle([20,20,w-20,80], fill=(255,255,255))
    d.rectangle([20,110,w-20,h-20], fill=(250,250,252))
    for k in range(7):
        y = 140 + k*45
        d.rectangle([40,y,40+140+(k*61)%380, y+20], fill=(222,230,244))
    d.text((36,46), label, fill=(28,30,38))
    p = os.path.join(folder, name); im.save(p); return p
shot("shot-1.png", (58,122,255), (140,96,255), "Pegged E2E #1")
shot("shot-2.png", (250,248,244), (226,236,250), "Pegged E2E #2")
shot("shot-3.png", (24,26,32), (58,72,92), "Pegged E2E #3")
print("seeded 3 captures")
PY

# --- fresh settings: nothing on the line, watch the folder + clipboard ---
"C:/Users/dk900/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe" - <<'PY'
import json, os
p = os.path.join(os.environ["APPDATA"], "Pegged", "settings.json")
json.dump({
  "Welcomed": True, "Size": 1, "Sounds": False,
  "WatchClipboard": True,
  "ScreenshotFolder": os.path.join(os.environ["APPDATA"], "Pegged", "Screenshots"),
  "RevealOnTopEdge": True, "RevealDelay": 0.25, "RetractDelay": 0.5,
  "PeekAtNewCapture": True,
  "Theme": 0,
  "Hanging": []
}, open(p, "w", encoding="utf-8"), indent=2)
print("settings reset")
PY

echo "== T1 collapsed: pill only =="
( "$EXE" > /tmp/peg.log 2>&1 & )
sleep 2
ps_move 900 600
sleep 1
ps_shot "01-collapsed" 60

echo "== T2 hover top edge -> line drops =="
ps_move 900 1
sleep 0.35
ps_shot "02-reveal-mid" 320
sleep 0.9
ps_shot "03-reveal-done" 320

echo "== T3 folder watcher: drop a new screenshot =="
cp "$APPDATA_WIN/Pegged/Screenshots/shot-1.png" "$APPDATA_WIN/Pegged/Screenshots/live-1.png"
sleep 1.2
ps_shot "04-watcher-hangs" 320

echo "== T4 clipboard: put an image on the clipboard =="
"$EXE" --put-clip "$APPDATA_WIN/Pegged/Screenshots/shot-2.png" >/dev/null 2>&1 &
sleep 1.4
ps_shot "05-clipboard-hangs" 320

echo "== T5 leave the edge -> line retracts =="
ps_move 900 700
sleep 1.4
ps_shot "06-retracted" 60

echo "== T6 hover a card -> hover chrome =="
ps_move 900 1
sleep 1.0
# first card sits near the horizontal centre of the line
ps_move 820 120
sleep 0.6
ps_shot "07-card-hover" 320

echo "== T7 single click -> Copied! =="
ps_click
sleep 0.35
ps_shot "08-copied" 320

echo "== T8 double click -> preview window =="
ps_click
sleep 1.2
ps_shot "09-preview" 700

echo "== T9 crop from preview =="
ps_move 900 640
sleep 0.3
ps_click
sleep 1.2
ps_shot "10-crop" 700

echo "== T10 apply the crop =="
ps_move 1580 640
sleep 0.3
ps_click
sleep 1.4
ps_shot "11-crop-applied" 320

echo "== T11 settings via the toolbar gear =="
ps_move 900 1
sleep 1.0
ps_move 1010 26
sleep 0.5
ps_click
sleep 1.3
ps_shot "12-settings" 700

echo "== T12 dark theme =="
# click the Appearance nav item, then the theme combo
ps_move 165 200
sleep 0.3
ps_click
sleep 0.6
ps_shot "13-settings-appearance" 700

echo "== done =="
ls "$SHOTS"
echo "--- app log ---"
cat /tmp/peg.log
