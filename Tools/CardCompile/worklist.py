#!/usr/bin/env python3
"""The next cards to compile, in CardEraCurriculum order, with everything an implementer needs.

    python3 Tools/CardCompile/worklist.py status.tsv                     # every Unimplemented/Stub, set order
    python3 Tools/CardCompile/worklist.py status.tsv --sets LON,LOD      # only those curriculum sets
    python3 Tools/CardCompile/worklist.py status.tsv --cards "Jinzo;Royal Decree" --json
    python3 Tools/CardCompile/worklist.py status.tsv --limit 40 --md > worklist.md

status.tsv comes from `Tools/CardCompile/headless.sh . --status-dump`. Set order is
Assets/StreamingAssets/WRLDZ/eras/pre_link_sets.json (LOB → MRD → SRL → PSV → LON …); "-" (no curriculum set)
sorts last. Each row carries the printed text, the reference ygopro script path
(Assets/StreamingAssets/OcgCore/scripts/official~/c<id>.lua) when one exists, and GitHub-branch hints from
Tools/CardCompile/branch_refs.json (cards some unmerged branch already compiled, with the commit to read).
"""
from __future__ import annotations

import argparse
import json
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("status")
    ap.add_argument("--sets", default="", help="comma-separated set codes (default: all)")
    ap.add_argument("--cards", default="", help="';'-separated names or ids (overrides --sets)")
    ap.add_argument("--status-filter", default="Unimplemented,Stub")
    ap.add_argument("--limit", type=int, default=0)
    fmt = ap.add_mutually_exclusive_group()
    fmt.add_argument("--json", action="store_true")
    fmt.add_argument("--md", action="store_true")
    a = ap.parse_args()

    cards = {c["id"]: c for c in json.load(open(os.path.join(REPO, "Assets/StreamingAssets/Cards/cards_db.json"),
                                                 encoding="utf-8"))["cards"]}
    sets = json.load(open(os.path.join(REPO, "Assets/StreamingAssets/WRLDZ/eras/pre_link_sets.json"),
                          encoding="utf-8"))["sets"]
    order = {s["code"]: s["order"] for s in sets}
    refs_path = os.path.join(REPO, "Tools/CardCompile/branch_refs.json")
    refs = json.load(open(refs_path, encoding="utf-8")).get("cards", {}) if os.path.exists(refs_path) else {}
    lua_dir = os.path.join(REPO, "Assets/StreamingAssets/OcgCore/scripts/official~")

    want_status = {s.strip() for s in a.status_filter.split(",") if s.strip()}
    want_sets = {s.strip().upper() for s in a.sets.split(",") if s.strip()}
    want_cards = [c.strip() for c in a.cards.split(";") if c.strip()]

    rows = []
    for line in open(a.status, encoding="utf-8"):
        p = line.rstrip("\n").split("\t")
        if len(p) < 6 or not p[0].isdigit():
            continue
        cid, status, set_code, ctype, race, name = int(p[0]), p[1], p[2], p[3], p[4], p[5]
        if want_cards:
            if str(cid) not in want_cards and name not in want_cards:
                continue
        else:
            if status not in want_status:
                continue
            if want_sets and set_code.upper() not in want_sets:
                continue
        d = cards.get(cid, {})
        lua = os.path.join(lua_dir, f"c{cid}.lua")
        rows.append({
            "id": cid, "name": name, "status": status, "set": set_code, "type": ctype, "race": race,
            "attribute": d.get("attribute"), "level": d.get("level"), "atk": d.get("atk"), "def": d.get("def"),
            "desc": d.get("desc", ""),
            "lua": os.path.relpath(lua, REPO) if os.path.exists(lua) else None,
            "branch": refs.get(str(cid)),
        })
    rows.sort(key=lambda r: (order.get(r["set"], 999), r["name"]))
    if a.limit:
        rows = rows[: a.limit]
    missing = [c for c in want_cards if not any(c in (str(r["id"]), r["name"]) for r in rows)]
    if missing:
        print(f"worklist: not found: {missing}", file=sys.stderr)

    if a.json:
        json.dump(rows, sys.stdout, indent=1, ensure_ascii=False)
        print()
    elif a.md:
        print(f"# Card worklist ({len(rows)} cards)\n")
        for r in rows:
            stats = f" Lv{r['level']} {r['atk']}/{r['def']} {r['attribute']}" if "Monster" in r["type"] else ""
            print(f"## {r['name']} ({r['id']}) — {r['set']} · {r['status']}")
            print(f"*{r['type']} · {r['race']}{stats}*\n")
            print(f"> {r['desc']}\n")
            if r["lua"]:
                print(f"- Reference script: `{r['lua']}`")
            if r["branch"]:
                print(f"- Already compiled on a branch: {r['branch']}")
            print()
    else:
        for r in rows:
            print(f"{r['set']:4} {r['status'][:5]:5} {r['id']:>9} {r['name']} [{r['type']}/{r['race']}]"
                  f"{'  (branch)' if r['branch'] else ''}\n       {r['desc'][:300]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
