#!/usr/bin/env python3
"""Bump CardTextEffectCompiler.Version with its history note.

    python3 Tools/CardCompile/version_bump.py 141 "v141: Toon family (…), summon-negation Counter Traps (…)"
    python3 Tools/CardCompile/version_bump.py --show

The compiled-program cache (seed + on-device disk cache) is keyed on TextHash + CompilerVersion, so ANY change
to compiler output needs a bump, or every player keeps being served the old program. The new number must be
above every number any branch or uncommitted line has used (see Tools/CardCompile/README.md "Versions").
After bumping: `Tools/CardCompile/headless.sh . --export-seed`.
"""
from __future__ import annotations

import os
import re
import sys

PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                    "Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs")
RX = re.compile(r"public const int Version = (\d+);")


def main() -> int:
    src = open(PATH, encoding="utf-8").read()
    m = RX.search(src)
    if not m:
        sys.exit("version_bump: Version constant not found")
    if len(sys.argv) >= 2 and sys.argv[1] == "--show":
        print(m.group(1))
        return 0
    if len(sys.argv) != 3 or not sys.argv[1].isdigit():
        print(__doc__)
        return 2
    new, note = int(sys.argv[1]), sys.argv[2].replace("*/", "* /")
    old = int(m.group(1))
    if new <= old:
        sys.exit(f"version_bump: {new} is not above the current {old}")
    src = src[: m.start()] + f"public const int Version = {new}; /* {note} */" + src[m.end():]
    open(PATH, "w", encoding="utf-8").write(src)
    print(f"CardTextEffectCompiler.Version {old} -> {new}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
