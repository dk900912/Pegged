#!/usr/bin/env python3
"""Push commits through the GitHub Git Data API.

The sandbox has no working git-over-HTTPS (the local proxy answers CONNECT
with 502) but api.github.com is reachable through the same proxy, so commits
are uploaded as objects and the branch ref is moved by hand.

    python scripts/push-via-api.py            # push main to origin
    python scripts/push-via-api.py --dry-run

Afterwards the local tracking ref is updated, so `git status` stays quiet and
the next run only uploads new commits.
"""
import base64
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

API = "https://api.github.com"
DRY = "--dry-run" in sys.argv


def sh(*args, **kw):
    return subprocess.run(args, capture_output=True, text=True, check=True, **kw).stdout


def git(*args):
    return sh("git", *args).strip()


def token():
    return sh("gh", "auth", "token").strip()


def api(path, method="GET", payload=None, attempts=12):
    """One API call, retried.

    The local proxy answers CONNECT with 502 for roughly one request in
    four - not for a specific path or method, just intermittently. Without a
    retry the whole push dies halfway through, and half a commit is pushed.
    Each attempt keeps the same request, so the retry is safe: a POST that
    never reached GitHub has no effect to duplicate.
    """
    data = json.dumps(payload).encode() if payload is not None else None
    last = None
    for attempt in range(attempts):
        req = urllib.request.Request(
            API + path, data=data, method=method,
            headers={"Authorization": f"Bearer {token()}",
                     "Accept": "application/vnd.github+json",
                     "X-GitHub-Api-Version": "2022-11-28",
                     "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read() or b"{}")
        except urllib.error.HTTPError as e:
            # A 4xx is the API answering: retrying cannot change it.
            if e.code < 500 and e.code != 429:
                print("HTTP", e.code, e.read().decode()[:400], file=sys.stderr)
                raise
            last = e
        except urllib.error.URLError as e:
            last = e
        if attempt + 1 < attempts:
            wait = min(0.4 * (2 ** attempt), 6.0)
            print("[retry %d/%d] %s %s: %s" % (attempt + 1, attempts, method, path, last),
                  file=sys.stderr, flush=True)
            time.sleep(wait)
    raise last if isinstance(last, Exception) else RuntimeError(last)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    os.chdir(root)
    remote = git("remote", "get-url", "origin").strip()
    # https://github.com/OWNER/REPO.git
    owner, repo = remote.rstrip("/").removesuffix(".git").split("/")[-2:]
    base = f"/repos/{owner}/{repo}"

    branch = git("rev-parse", "--abbrev-ref", "HEAD")
    head = api(f"{base}/commits/{branch}")["sha"]
    # The commits created through the API are not local objects, so the base
    # for "what is still to upload" is the local `pushed` tag, not the remote.
    tag_ok = subprocess.run(["git", "rev-parse", "--verify", "--quiet", "refs/tags/pushed"],
                            capture_output=True).returncode == 0
    base_ref = "pushed" if tag_ok else head
    pending = git("rev-list", "--reverse", f"{base_ref}..HEAD").split()
    if not pending:
        print("nothing to push")
        return
    print(f"{len(pending)} commit(s) to push onto {head[:8]}")

    # Two bases move together: the remote sha (parent of the commits we create)
    # and the local commit the diff is taken against.
    parent_remote = head
    prev_local = base_ref
    for c in pending:
        message = git("log", "-1", "--format=%B", c)
        files = git("diff-tree", "-r", "--no-commit-id", "--name-status", prev_local, c).splitlines()
        entries = []
        for line in files:
            status, path = line.split("\t", 1)
            if status == "D":
                entries.append({"path": path, "mode": "100644", "type": "blob", "sha": None})
                continue
            if status.startswith("R"):          # rename: reported as R100 old new
                parts = path.split("\t")
                path = parts[-1]
            raw = sh("git", "show", f"{c}:{path}", cwd=root) if False else None
            blob = subprocess.run(["git", "show", f"{c}:{path}"], capture_output=True, cwd=root).stdout
            sha = api(f"{base}/git/blobs", "POST",
                      {"content": base64.b64encode(blob).decode(), "encoding": "base64"})["sha"]
            entries.append({"path": path, "mode": "100644", "type": "blob", "sha": sha})
        tree = api(f"{base}/git/trees", "POST",
                   {"base_tree": api(f"{base}/commits/{parent_remote}")["commit"]["tree"]["sha"],
                    "tree": entries})["sha"]
        new = api(f"{base}/git/commits", "POST",
                  {"message": message, "tree": tree, "parents": [parent_remote]})["sha"]
        print(f"  {c[:8]} -> {new[:8]}  ({len(entries)} file(s))")
        prev_local = c
        parent_remote = new

    if DRY:
        print("dry run: ref not moved")
        return
    api(f"{base}/git/refs/heads/{branch}", "PATCH", {"sha": parent_remote})
    subprocess.run(["git", "tag", "-f", "pushed", pending[-1]], check=True, capture_output=True)
    print("pushed:", parent_remote, "(local base tag ->", pending[-1][:8], ")")


if __name__ == "__main__":
    main()
