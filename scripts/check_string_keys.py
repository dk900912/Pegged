#!/usr/bin/env python3
"""Every string key the code asks for must exist in every resource file.

The resx files are generated from scripts/gen-localization.py, so anything that
was hand-edited into them - instead of into the master table - is one `python
scripts/gen-localization.py` away from being silently deleted, and a missing key
does not fail the build: Loc.Get() falls back to the key name, so the tray or a
window simply shows `Tray.Inbox` instead of a label. This is the tripwire.

    python scripts/check_string_keys.py
"""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "src", "Pegged", "Resources")

CODE_GLOBS = [
    os.path.join(ROOT, "src", "Pegged", "**", "*.cs"),
    os.path.join(ROOT, "src", "Pegged", "**", "*.xaml"),
]

# Loc.Get("Key") / Loc.Format("Key", arg…) / {loc:Str Key}
# The argument tail matters: a plain Loc.Get("Key") has none, and a call with
# one used to be skipped by a regex that demanded the closing parenthesis
# right after the string.
CALL = re.compile(r'Loc\.(?:Get|Format)\(\s*"([^"]+)"(?:\s*,[^)]*)?\)')
MARKUP = re.compile(r'\{loc:Str\s+([A-Za-z0-9_.]+)\s*\}')
DYNAMIC = re.compile(r'Loc\.Get\(\s*(?!")')        # a variable, not a literal

used = set()
for pattern in CODE_GLOBS:
    for path in glob.glob(pattern, recursive=True):
        if os.path.basename(path).endswith(".g.cs"):
            continue
        text = open(path, encoding="utf-8").read()
        used |= set(CALL.findall(text))
        used |= set(MARKUP.findall(text))

# Dynamic lookups (a key built at run time) are listed by hand, otherwise this
# script would demand a key for every runtime-composed name.
DYNAMIC_OK = {
    "Tray.ShowLine", "Tray.HideLine",     # picked by is-open, both in the table
    "Menu.KeepFit",                        # BuildKeepMenu lists these literals
}

files = [os.path.join(RES, "Strings.resx")] + sorted(
    glob.glob(os.path.join(RES, "Strings.*.resx")))

problems = []
for path in files:
    text = open(path, encoding="utf-8").read()
    have = set(re.findall(r'<data name="([^"]+)"', text))
    missing = sorted(k for k in used if k not in have)
    if missing:
        problems.append((os.path.basename(path), missing))

# The reverse direction too: a key in the table that nothing asks for is dead
# weight, and the table has grown by copy-paste before.
all_used = used | DYNAMIC_OK
orphan = []
table_path = os.path.join(ROOT, "scripts", "gen-localization.py")
table = open(table_path, encoding="utf-8").read()
defined = set(re.findall(r'^\s+"([A-Za-z0-9_.]+)":\s*\(', table, re.M))
orphan = sorted(k for k in defined if k not in all_used)

if problems:
    for name, missing in problems:
        print("MISSING in %s: %s" % (name, ", ".join(missing)))
if orphan:
    print("defined but never referenced: %s" % ", ".join(orphan))

if problems:
    print("FAIL: %d file(s) missing keys" % len(problems))
    sys.exit(1)
print("OK: %d keys referenced by code, all present in %d resource files"
      % (len(used), len(files)))
if orphan:
    sys.exit(0)
