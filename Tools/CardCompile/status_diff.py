#!/usr/bin/env python3
"""Compare two `--status-dump` TSVs (Tools/CardCompile/headless.sh <tree> --status-dump).

    python3 Tools/CardCompile/status_diff.py before.tsv after.tsv [--allow 123,456] [--quiet]

Prints every card whose status changed, gains first, and a per-status count table.
Exit 1 if any card got worse (Implemented -> Stub/Unimplemented, Stub -> Unimplemented) unless its id is
listed in --allow (a deliberate fail-closed change, which the batch notes must explain).
Structural <-> anything is reported but never counts as a loss (effectless cards).
"""
from __future__ import annotations

import argparse
import collections
import sys

RANK = {"Implemented": 3, "Stub": 2, "Unimplemented": 1, "Structural": 0}


def load(path: str) -> dict[int, list[str]]:
    rows: dict[int, list[str]] = {}
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 3 and parts[0].isdigit():
                while len(parts) < 6:
                    parts.append("")
                rows[int(parts[0])] = parts
    if not rows:
        sys.exit(f"status_diff: no status rows in {path} (is it a --status-dump TSV?)")
    return rows


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("before")
    ap.add_argument("after")
    ap.add_argument("--allow", default="", help="comma-separated ids allowed to get worse")
    ap.add_argument("--quiet", action="store_true", help="only the summary and losses")
    a = ap.parse_args()
    allow = {int(x) for x in a.allow.split(",") if x.strip().isdigit()}
    b, n = load(a.before), load(a.after)

    gains, losses, other = [], [], []
    for cid, row in n.items():
        old = b.get(cid)
        if old is None:
            other.append(f"  NEW   {row[5]} ({cid}): {row[1]}")
            continue
        if old[1] == row[1]:
            continue
        line = f"  {row[5]} ({cid}, {row[2]}): {old[1]} -> {row[1]}"
        if "Structural" in (old[1], row[1]):
            other.append(line)
        elif RANK[row[1]] > RANK[old[1]]:
            gains.append(line)
        else:
            losses.append((cid, line))
    for cid in b:
        if cid not in n:
            other.append(f"  GONE  {b[cid][5]} ({cid})")

    count = lambda rows: collections.Counter(r[1] for r in rows.values())
    cb, cn = count(b), count(n)
    print("status        before   after")
    for k in ("Implemented", "Stub", "Unimplemented", "Structural"):
        print(f"{k:13} {cb.get(k, 0):6} {cn.get(k, 0):7}  ({cn.get(k, 0) - cb.get(k, 0):+d})")
    if gains and not a.quiet:
        print(f"\ngained ({len(gains)}):")
        print("\n".join(sorted(gains)))
    if other and not a.quiet:
        print(f"\nother changes ({len(other)}):")
        print("\n".join(sorted(other)))
    bad = [l for cid, l in losses if cid not in allow]
    ok = [l for cid, l in losses if cid in allow]
    if ok:
        print(f"\nallowed losses ({len(ok)}):")
        print("\n".join(sorted(ok)))
    if bad:
        print(f"\nLOSSES ({len(bad)}) — a card that worked before no longer does:")
        print("\n".join(sorted(bad)))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
