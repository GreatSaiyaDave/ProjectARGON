#!/usr/bin/env python3
"""Append-only guard for the engine's enums.

    python3 Tools/CardCompile/enum_guard.py                      # real checkout vs git HEAD
    python3 Tools/CardCompile/enum_guard.py --repo "$T"          # a scratch_tree.sh copy vs its baseline commit
    python3 Tools/CardCompile/enum_guard.py --base-ref <ref>     # vs any ref

Compiled programs (the StreamingAssets seed, the on-device disk cache) store EffectActionKind / EffectTiming /
EffectZoneFilter / EffectSide … as ints. A member inserted mid-enum, removed, or renumbered silently changes the
meaning of every later value in every stored program (the v120 A Feint Plan insert at index 82 did exactly
that). New members go at the END of the enum.

Exit 1 on any mid-enum insert / removal / reorder / changed explicit value in Duel/TextEffects (error), or in
the rest of Duel/ + Data/ (reported, error only with --strict).
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

ENUM_RX = re.compile(r"\benum\s+(\w+)\s*(?::\s*[\w.]+)?\s*\{(.*?)\}", re.S)


def strip_comments(src: str) -> str:
    src = re.sub(r"/\*.*?\*/", "", src, flags=re.S)
    return re.sub(r"//[^\n]*", "", src)


def members(body: str) -> list[tuple[str, str | None]]:
    out = []
    for part in body.split(","):
        part = re.sub(r"\[[^\]]*\]", "", part).strip()
        if not part:
            continue
        name, _, value = part.partition("=")
        out.append((name.strip(), value.strip() or None))
    return out


def enums(src: str) -> dict[str, list[tuple[str, str | None]]]:
    return {m.group(1): members(m.group(2)) for m in ENUM_RX.finditer(strip_comments(src))}


def git(repo: str, *args: str) -> str:
    return subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True).stdout


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    ap.add_argument("--base-ref", default=None, help="default: HEAD (a scratch tree's HEAD is its baseline)")
    ap.add_argument("--strict", action="store_true", help="errors outside TextEffects fail too")
    a = ap.parse_args()
    repo = os.path.abspath(a.repo)
    ref = a.base_ref or "HEAD"
    roots = ["Assets/Scripts/WRLDZ/Duel", "Assets/Scripts/WRLDZ/Data"]
    errors, warnings, checked = [], [], 0
    for root in roots:
        for dirpath, _, files in os.walk(os.path.join(repo, root)):
            for fn in files:
                if not fn.endswith(".cs"):
                    continue
                path = os.path.join(dirpath, fn)
                rel = os.path.relpath(path, repo)
                new = enums(open(path, encoding="utf-8", errors="replace").read())
                if not new:
                    continue
                old = enums(git(repo, "show", f"{ref}:{rel}"))
                serialized = "/TextEffects/" in rel.replace(os.sep, "/")
                for name, mem in new.items():
                    if name not in old:
                        continue
                    checked += 1
                    before = old[name]
                    prob = []
                    names_now = [m[0] for m in mem]
                    for i, (m, v) in enumerate(before):
                        if m not in names_now:
                            prob.append(f"removed {m} (was index {i})")
                        elif i < len(mem) and mem[i][0] != m:
                            prob.append(f"index {i} is now {mem[i][0]} (was {m})")
                        elif i < len(mem) and mem[i][1] != v:
                            prob.append(f"{m} explicit value {v} -> {mem[i][1]}")
                    if prob:
                        msg = f"{rel}: enum {name}: " + "; ".join(prob[:6])
                        (errors if serialized or a.strict else warnings).append(msg)
    for w in warnings:
        print("WARN ", w)
    for e in errors:
        print("ERROR", e)
    print(f"enum_guard: {checked} enums checked vs {ref}: {len(errors)} errors, {len(warnings)} warnings")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
