#!/usr/bin/env python3
"""What actually changed between two compiled-effects seeds.

    python3 Tools/CardCompile/seed_diff.py old_seed.json new_seed.json

Every program's CompilerVersion / CompiledUtc changes on each export, and a new EffectClause field shows up
(at its default) in every clause, so a plain diff says "everything changed". This compares programs on the
fields both seeds share, and lists programs added, removed and genuinely changed (clause-level detail), plus
the new clause fields and the values they take.
"""
from __future__ import annotations

import json
import sys


def load(path: str):
    d = json.load(open(path, encoding="utf-8"))
    return d.get("version"), {(p["CardId"], p.get("EraId") or ""): p for p in d.get("programs", [])}


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    vo, old = load(sys.argv[1])
    vn, new = load(sys.argv[2])
    print(f"seed {vo} ({len(old)} programs) -> {vn} ({len(new)} programs)")
    added = sorted(new[k]["CardName"] for k in new if k not in old)
    removed = sorted(old[k]["CardName"] for k in old if k not in new)
    new_fields: dict[str, set[str]] = {}
    changed = []
    for k in sorted(new, key=lambda k: new[k]["CardName"]):
        if k not in old:
            continue
        a, b = old[k], new[k]
        diffs = []
        for f in set(a) | set(b):
            if f in ("CompiledUtc", "CompilerVersion", "Clauses"):
                continue
            if f in a and f in b and a[f] != b[f]:
                diffs.append(f"{f}: {a[f]!r} -> {b[f]!r}")
        ca, cb = a.get("Clauses") or [], b.get("Clauses") or []
        if len(ca) != len(cb):
            diffs.append(f"clauses {len(ca)} -> {len(cb)}")
        for i in range(max(len(ca), len(cb))):
            x = ca[i] if i < len(ca) else {}
            y = cb[i] if i < len(cb) else {}
            for f in y:
                if x and f not in x:
                    new_fields.setdefault(f, set()).add(json.dumps(y[f]))
            d = {f: (x.get(f), y.get(f)) for f in set(x) & set(y) if x.get(f) != y.get(f)}
            if x and y and d:
                diffs.append(f"clause {i}: " + ", ".join(f"{f} {v[0]!r}->{v[1]!r}" for f, v in sorted(d.items())))
        if diffs:
            changed.append((b["CardName"], diffs))
    print(f"\nadded ({len(added)}): {', '.join(added)}")
    print(f"removed ({len(removed)}): {', '.join(removed)}")
    if new_fields:
        print("\nnew clause fields (values seen in existing programs):")
        for f, vals in sorted(new_fields.items()):
            print(f"  {f}: {', '.join(sorted(vals)[:6])}")
    print(f"\nchanged ({len(changed)}):")
    for name, diffs in changed:
        print(f"  {name}")
        for d in diffs[:8]:
            print(f"      {d}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
