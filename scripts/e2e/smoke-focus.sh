#!/usr/bin/env bash
# Focused smoke: double-click preview + empty state visibility
set -u
ROOT="C:/Users/dk900/WorkBuddy AI/win11-tendedero"
E2E="$ROOT/scripts/e2e"
SHOTS="$E2E/shots"
EXE="$ROOT/src/Pegged/bin/Debug/net8.0-windows/Pegged.exe"
PS="/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
APPDATA_WIN="C:/Users/dk900/AppData/Roaming"

export DOTNET_ROOT="C:/Users/dk900/dotnet"

taskkill //F //IM Pegged.exe >/dev/null 2>&1
rm -f "$SHOTS/"*.png 2>/dev/null

# seed one capture
"C:/Users/dk900/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe" -c '
import os
from PIL import Image, ImageDraw
f=os.path.join(os.environ["APPDATA"],"Pegged","Screenshots")
os.makedirs(f,exist_ok=True)
im=Image.new("RGB",(800,500),(58,122,255))
ImageDraw.Draw(im).text((40,60),"Preview Test",fill=(255,255,255))
im.save(os.path.join(f,"preview-test.png"))
'

# settings: one capture on the line
"C:/Users/dk900/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe" -c '
import json,os
p=os.path.join(os.environ["APPDATA"],"Pegged","settings.json")
json.dump({
  "Welcomed":True,"Size":1,"Sounds":False,
  "WatchClipboard":False,
  "ScreenshotFolder":os.path.join(os.environ["APPDATA"],"Pegged","Screenshots"),
  "RevealOnTopEdge":True,"RevealDelay":0.25,"RetractDelay":0.5,
  "PeekAtNewCapture":False,"Theme":0,
  "Hanging":[os.path.join(os.environ["APPDATA"],"Pegged","Screenshots","preview-test.png")]
},open(p,"w",encoding="utf-8"),indent=2)
'

ps_move() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/move.ps1" -X "$1" -Y "$2" >/dev/null 2>&1; }
ps_click() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/click.ps1" >/dev/null 2>&1; }
ps_shot() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/shot.ps1" -Name "$1" -H "${2:-700}" >/dev/null 2>&1; }

echo "== start =="
( "$EXE" > /tmp/peg.log 2>&1 & )
sleep 2

# hover top edge to reveal
ps_move 900 1
sleep 1.0
ps_shot "A-revealed-with-card" 320

echo "== double-click the card =="
# card is near center ~ x=820, y=120
ps_move 820 120
sleep 0.4
ps_click
sleep 1.5
ps_shot "B-after-doubleclick" 700

echo "== click crop button in preview =="
ps_move 900 640
sleep 0.3
ps_click
sleep 1.2
ps_shot "C-crop-open" 700

echo "== done =="
ls "$SHOTS"
cat /tmp/peg.log
taskkill //F //IM Pegged.exe >/dev/null 2>&1
echo "cleaned"
