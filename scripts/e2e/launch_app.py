#!/usr/bin/env python3
"""Starts the published app so that it outlives the shell that launched it.

A plain `&` from the agent's shell puts the app in the same job object as the
command, so it is reaped when that command's turn ends. Breaking away from the
job and dropping the console keeps it running like a double-click would.
"""
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXE = os.path.join(ROOT, "publish", "Pegged.exe")

DETACHED_PROCESS = 0x00000008
CREATE_NEW_PROCESS_GROUP = 0x00000200
CREATE_BREAKAWAY_FROM_JOB = 0x01000000

flags = DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB
try:
    process = subprocess.Popen([EXE], cwd=os.path.dirname(EXE),
                               creationflags=flags, close_fds=True,
                               stdin=subprocess.DEVNULL,
                               stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL)
except OSError as error:
    print("breakaway refused (%s), starting inside the job" % error)
    process = subprocess.Popen([EXE], cwd=os.path.dirname(EXE),
                               creationflags=DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP,
                               close_fds=True, stdin=subprocess.DEVNULL,
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

time.sleep(3.5)
print("pid=%d alive=%s" % (process.pid, process.poll() is None))
sys.exit(0 if process.poll() is None else 1)
