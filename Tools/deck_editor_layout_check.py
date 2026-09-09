#!/usr/bin/env python3
"""Offline checks for the wide deck-editor CARD LIST / MAIN layout.

The Cloud VM has no Unity Editor. This script:
  1. Guards the PinBelow-on-content footgun that packed chips to the right.
  2. Replays DeckPoolLayout math (pad 6, last column at width-pad).
  3. Checks MAIN leftover vs Extra/Side mins on short Free Aspect Game views.

Exit 1 on failure. Keep formulas in lockstep with
Assets/Scripts/WRLDZ/UI/Shell/DeckCollectionScreen.cs (DeckPoolLayout)
and DeckConstructionBoard.cs.
"""
from __future__ import annotations

import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COLLECTION = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/DeckCollectionScreen.cs"
BOARD = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/DeckConstructionBoard.cs"

CHIP_GAP = 5.0
CHIP_W = 100.0
CHIP_H = 144.0
PAD = 6.0
TARGET_W = 96.0
MIN_COLS = 3
MAX_COLS = 12


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def const_float(src: str, name: str) -> float:
    m = re.search(rf"const float {name} = ([0-9.]+)f;", src)
    if not m:
        fail(f"missing const float {name}")
    return float(m.group(1))


def columns(viewport_w: float, gap: float) -> int:
    raw = math.floor((viewport_w - PAD * 2.0 + gap) / (TARGET_W + gap))
    return max(MIN_COLS, min(MAX_COLS, raw))


def card_width(viewport_w: float, cols: int, gap: float) -> float:
    return (viewport_w - PAD * 2.0 - gap * (cols - 1)) / cols


def chip_x(col: int, card_w: float, gap: float) -> float:
    return PAD + col * (card_w + gap)


def check_source_invariants(collection: str, board: str) -> None:
    # ChipGrid must pin the panel, never rely on callers pinning the return value.
    if "pinFromTop = -1f" not in collection:
        fail("ChipGrid is missing pinFromTop (panel pin must live inside ChipGrid)")
    if "PinBelow(panelRt, x0, x1, pinFromTop, y0)" not in collection:
        fail("ChipGrid does not PinBelow the panel RectTransform")

    # After BuildFilterBar, the wide branch must ChipGrid with pinFromTop and
    # must not PinBelow the scroll or the returned content.
    idx = collection.find("BuildFilterBar(phase, st, wide, searchTop, searchH);")
    if idx < 0:
        fail("BuildFilterBar call missing from BuildEditor")
    snippet = collection[idx : idx + 1600]
    if "PinBelow(st.PoolScroll" in snippet or "PinBelow(list" in snippet:
        fail("wide BuildEditor still PinBelow's the pool scroll/content")
    if "pinFromTop: chromeBottom" not in snippet:
        fail("wide ChipGrid call does not pass pinFromTop for the panel")

    if "var scrollY = rt.anchoredPosition.y" not in collection:
        fail("DeckPoolFit does not preserve vertical scroll")
    if "rt.anchoredPosition = Vector2.zero" in collection:
        fail("DeckPoolFit still zeroes anchoredPosition (wipes CARD LIST scroll)")
    if "new Vector2(0f, scrollY)" not in collection:
        fail("DeckPoolFit does not restore scrollY")

    if "childForceExpandHeight = false" not in board:
        fail("ConstructionBoard / trays must not force-expand height")
    if "childAlignment = TextAnchor.UpperLeft" not in board:
        fail("ConstructionBoard is not left-aligned")


def check_pool_fill() -> None:
    for width in (360.0, 520.0, 700.0, 980.0):
        cols = columns(width, CHIP_GAP)
        cw = card_width(width, cols, CHIP_GAP)
        ch = cw * (CHIP_H / CHIP_W)
        if cw <= 1 or ch <= 1:
            fail(f"degenerate chip size at width={width}")
        first_left = chip_x(0, cw, CHIP_GAP)
        last_right = chip_x(cols - 1, cw, CHIP_GAP) + cw
        if abs(first_left - PAD) > 1e-6:
            fail(f"first chip not at pad for width={width}: {first_left}")
        if abs(last_right - (width - PAD)) > 0.05:
            fail(
                f"last column does not reach width-pad at {width}: "
                f"{last_right} vs {width - PAD}"
            )
        print(
            f"  pool {width:.0f}px → {cols} cols, chip {cw:.1f}x{ch:.1f}, "
            f"x0={first_left:.1f}, x1={last_right:.1f}"
        )


def check_board_budget(board: str) -> None:
    extra_pref = const_float(board, "ExtraRowMin")
    extra_min = const_float(board, "ExtraRowHardMin")
    main_min = const_float(board, "MainTrayMin")
    main_pref = const_float(board, "MainTrayPreferred")
    pad_v = 4.0 + 4.0
    spacing = 6.0 * 2
    chrome = 148.0  # searchTop 88 + searchH 52 + 8

    # Hard floor: both extras at ExtraRowHardMin plus MAIN min.
    hard_floor = pad_v + spacing + main_min + extra_min + extra_min

    cases = (
        ("short Free Aspect", 1100.0, 520.0),
        ("720p Game view", 1280.0, 720.0),
        ("1080p Game view", 1920.0, 1080.0),
    )
    for label, _w, h in cases:
        board_h = h - chrome
        if board_h + 0.5 < hard_floor:
            fail(
                f"{label}: board {board_h:.0f}px < hard floor {hard_floor:.0f}px "
                f"(MAIN would clip under Extra/Side mins)"
            )
        leftover_main = board_h - (pad_v + spacing + extra_pref + extra_pref)
        if leftover_main < main_min:
            leftover_main = board_h - (pad_v + spacing + extra_min + extra_min)
        if leftover_main < main_min - 0.5:
            fail(f"{label}: MAIN leftover {leftover_main:.0f}px < min {main_min:.0f}px")
        print(
            f"  {label}: board {board_h:.0f}px, MAIN leftover {leftover_main:.0f}px "
            f"(min {main_min:.0f}, extra pref {extra_pref:.0f})"
        )

    old_extra = 176.0
    old_main_min = 200.0
    gain = (old_extra - extra_pref) * 2 + (old_main_min - main_pref)
    print(f"  vs original Extra 176 / MAIN 200: ~{gain:.0f}px more MAIN budget")
    if extra_pref >= 128:
        fail(f"Extra/Side preferred still too tall ({extra_pref})")


def main() -> int:
    collection = COLLECTION.read_text(encoding="utf-8")
    board = BOARD.read_text(encoding="utf-8")
    print("deck editor layout check")
    check_source_invariants(collection, board)
    print("  source invariants ok")
    check_pool_fill()
    check_board_budget(board)
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
