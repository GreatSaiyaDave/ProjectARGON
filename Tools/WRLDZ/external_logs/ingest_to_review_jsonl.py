#!/usr/bin/env python3
"""
Merge external / stress / review logs into one JSONL stream matching DuelReviewLog events.

Usage:
  python3 ingest_to_review_jsonl.py \\
    --inputs corpus/lab_ai_heuristics_v1.jsonl \\
             ~/.config/unity3d/*/WRLDZ/duel_reviews/*.jsonl \\
    --out corpus/merged_for_ai.jsonl

Each line is a DuelLogEvent-compatible object:
  seq, utc, duelTime, turn, phase, actor, kind, message,
  lpYou, lpOpp, handYou, handOpp, monYou, monOpp, cardId, cardName, extra
"""
from __future__ import annotations

import argparse
import glob
import json
import sys
from datetime import datetime, timezone
from pathlib import Path


def load_jsonl(path: Path):
    if not path.is_file():
        return
    with path.open(encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                # plain text transcript line → wrap
                yield {
                    "seq": 0,
                    "utc": datetime.now(timezone.utc).isoformat(),
                    "duelTime": 0.0,
                    "turn": 0,
                    "phase": "?",
                    "actor": "Import",
                    "kind": "Info",
                    "message": line,
                    "lpYou": 0,
                    "lpOpp": 0,
                    "handYou": 0,
                    "handOpp": 0,
                    "monYou": 0,
                    "monOpp": 0,
                    "cardId": "",
                    "cardName": "",
                    "extra": f"source_file={path.name}",
                }


def expand(patterns):
    for p in patterns:
        hits = glob.glob(p, recursive=True)
        if hits:
            for h in hits:
                yield Path(h)
        else:
            path = Path(p)
            if path.exists():
                yield path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--inputs", nargs="+", required=True, help="JSONL files or globs")
    ap.add_argument("--out", required=True, help="Output merged JSONL")
    ap.add_argument("--tag", default="merged", help="extra tag on each event")
    args = ap.parse_args()

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    seq = 0
    n_files = 0
    n_events = 0
    with out.open("w", encoding="utf-8") as w:
        for path in expand(args.inputs):
            n_files += 1
            for ev in load_jsonl(path):
                seq += 1
                n_events += 1
                ev["seq"] = seq
                extra = ev.get("extra") or ""
                tag = f"ingest={args.tag};file={path.name}"
                ev["extra"] = (extra + ";" + tag).strip(";")
                # ensure required keys
                for k, d in (
                    ("utc", datetime.now(timezone.utc).isoformat()),
                    ("duelTime", 0.0),
                    ("turn", 0),
                    ("phase", "?"),
                    ("actor", "Import"),
                    ("kind", "Info"),
                    ("message", ""),
                    ("lpYou", 0),
                    ("lpOpp", 0),
                    ("handYou", 0),
                    ("handOpp", 0),
                    ("monYou", 0),
                    ("monOpp", 0),
                    ("cardId", ""),
                    ("cardName", ""),
                ):
                    ev.setdefault(k, d)
                w.write(json.dumps(ev, ensure_ascii=False) + "\n")

    print(f"files={n_files} events={n_events} → {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
