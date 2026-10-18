#!/usr/bin/env bash
# Hover chrome test
set -u
ROOT="C:/Users/dk900/WorkBuddy AI/win11-tendedero"
E2E="$ROOT/scripts/e2e"
SHOTS="$E2E/shots"
EXE="$ROOT/src/Pegged/bin/Debug/net8.0-windows/Pegged.exe"
PS="/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"

export DOTNET_ROOT="C:/Users/dk900/dotnet"

taskkill //F //IM Pegged.exe >/dev/null 2>&1
rm -f "$SHOTS/"*.png 2>/dev/null

ps_move() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/move.ps1" -X "$1" -Y "$2" >/dev/null 2>&1; }
ps_shot() { "$PS" -NoProfile -ExecutionPolicy Bypass -File "$E2E/shot.ps1" -Name "$1" -H "${2:-320}" >/dev/null 2>&1; }

( "$EXE" > /tmp/peg.log 2>&1 & )
sleep 2

# reveal line
ps_move 900 1
sleep 1.0
ps_shot "hover-01-revealed" 320

# move onto card
ps_move 820 120
sleep 0.6
ps_shot "hover-02-on-card" 320

# move away
ps_move 900 500
sleep 0.8
ps_shot "hover-03-away" 60

ls "$SHOTS"
taskkill //F //IM Pegged.exe >/dev/null 2>&1
echo done
