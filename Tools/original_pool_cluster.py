#!/usr/bin/env python3
"""Cluster Original blocked cards into a capability backlog. No Unity required.

Reads Assets/StreamingAssets/WRLDZ/eras/original_status.json
Writes original_capability_backlog.md and prints a short summary.
"""
from __future__ import annotations

import json
import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ERA = ROOT / "Assets/StreamingAssets/WRLDZ/eras"
STATUS = ERA / "original_status.json"
PREV = ERA / "original_status.prev.json"
BACKLOG = ERA / "original_capability_backlog.md"

PATTERNS = [
    ("multi-type / attribute Field aura", re.compile(
        r"All .+ monsters on the field gain \d+ ATK|All \w+ monsters on the field gain \d+ ATK", re.I)),
    ("Flip reveal-Set-by-card-type", re.compile(
        r"If the (target|selected card) is Set|reveal it", re.I)),
    ("GY banish", re.compile(r"banish|remove from play", re.I)),
    ("both-players draw/discard", re.compile(r"each player|both players", re.I)),
    ("take-control until End Phase", re.compile(r"take control", re.I)),
    ("Spirit End Phase bounce", re.compile(
        r"returns to (its )?owner'?s hand during the End Phase", re.I)),
    ("Union either/or", re.compile(
        r"Once per turn, during your Main Phase, if you control this monster", re.I)),
    ("declare Type/Attribute", re.compile(r"declare 1 (Type|Attribute)", re.I)),
    ("coin/dice", re.compile(r"toss a coin|roll a (six-sided )?die", re.I)),
    ("search / excavate", re.compile(
        r"look at the top|excavate|add .+ from your Deck", re.I)),
    ("negate activation", re.compile(r"negate the activation", re.I)),
    ("win condition", re.compile(r"you win the Duel|win this Duel", re.I)),
    ("token", re.compile(r"Token", re.I)),
    ("Nomi / must first SS", re.compile(
        r"Cannot be Normal Summoned|must first be Special Summoned", re.I)),
    ("leftover parenthetical (stub)", re.compile(r"^\(This card is not treated", re.I)),
]


def load_status(path: Path) -> dict:
    if not path.exists():
        print(f"missing {path}; run Tools/original-pool-report.sh with Editor up", file=sys.stderr)
        raise SystemExit(2)
    return json.loads(path.read_text())


def bucket(card: dict) -> str:
    text = " ".join(filter(None, [card.get("leftover"), card.get("descHead")]))
    status = card.get("status") or ""
    for name, rx in PATTERNS:
        if rx.search(text or ""):
            return name
    t = card.get("type") or "?"
    prefix = "stub " if status == "stub" else ""
    if "Flip" in t:
        return prefix + "flip other"
    if t == "Trap Card":
        return prefix + "trap other"
    if t == "Spell Card":
        return prefix + "spell other"
    return prefix + "monster other"


def main() -> int:
    snap = load_status(STATUS)
    orig = snap.get("original") or {}
    total = int(orig.get("total") or 0)
    st = int(orig.get("structural") or 0)
    im = int(orig.get("implemented") or 0)
    sb = int(orig.get("stub") or 0)
    un = int(orig.get("unimplemented") or 0)
    summed = st + im + sb + un
    if total and summed != total:
        print(f"INVARIANT FAIL {summed} != {total}", file=sys.stderr)
        return 1

    blocked = snap.get("blocked") or []
    buckets = defaultdict(list)
    for c in blocked:
        buckets[bucket(c)].append(c)

    prev_ready = None
    if PREV.exists():
        try:
            p = json.loads(PREV.read_text()).get("original") or {}
            prev_ready = int(p.get("structural") or 0) + int(p.get("implemented") or 0)
        except Exception:
            prev_ready = None
    ready = st + im
    delta = "" if prev_ready is None else f" ({ready - prev_ready:+d} vs previous snapshot)"

    lines = [
        "# Original capability backlog",
        "",
        "Scored from `original_status.json`. Path B: supported pool = Structural + Implemented.",
        f"Ready {ready}/{total}{delta}. Blocking {sb + un} (stub {sb}, unimplemented {un}).",
        "",
        "| n | capability | examples |",
        "|---:|---|---|",
    ]
    ranked = sorted(buckets.items(), key=lambda kv: -len(kv[1]))
    for name, rows in ranked:
        ex = ", ".join(f"{r.get('name')} ({r.get('set')})" for r in rows[:3])
        lines.append(f"| {len(rows)} | {name} | {ex} |")

    lob = snap.get("lobGaps") or []
    lines += ["", "## LOB remaining", ""]
    if not lob:
        lines.append("LOB is complete.")
    else:
        for c in lob:
            lines.append(
                f"- `{c.get('status')}` {c.get('id')} {c.get('name')} ({c.get('type')})"
            )

    BACKLOG.write_text("\n".join(lines) + "\n")

    print(f"Original {total}  ready {ready} ({orig.get('playablePct', 0):.1f}%)  "
          f"stub {sb}  unimpl {un}{delta}")
    print(f"compiler {snap.get('compilerVersion')}  utc {snap.get('generatedUtc')}")
    print(f"LOB gaps {len(lob)}  backlog {BACKLOG.relative_to(ROOT)}")
    print("Top capabilities:")
    for name, rows in ranked[:8]:
        print(f"  {len(rows):4}  {name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
