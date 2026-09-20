#!/usr/bin/env python3
"""Offline checks for the deck editor CARD LIST / construction board layout.

The Cloud VM has no Unity Editor. This script:
  1. Guards the PinBelow-on-content footgun that packed chips to the right.
  2. Replays DeckPoolLayout math (pad 6, last column at width-pad).
  3. Replays DeckBoardLayout across real Game-view sizes and asserts the
     three piles share a card size, a left edge and a column pitch, and that
     no row is sliced off the bottom of a tray.

Exit 1 on failure. Keep formulas in lockstep with
Assets/Scripts/WRLDZ/UI/Shell/DeckCollectionScreen.cs (DeckPoolLayout)
and DeckConstructionBoard.cs (DeckBoardLayout).
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


def const_int(src: str, name: str) -> int:
    m = re.search(rf"const int {name} = ([0-9]+);", src)
    if not m:
        fail(f"missing const int {name}")
    return int(m.group(1))


# ── CARD LIST (right-hand pool) ──────────────────────────────────────────────

def columns(viewport_w: float, gap: float) -> int:
    raw = math.floor((viewport_w - PAD * 2.0 + gap) / (TARGET_W + gap))
    return max(MIN_COLS, min(MAX_COLS, raw))


def card_width(viewport_w: float, cols: int, gap: float) -> float:
    return (viewport_w - PAD * 2.0 - gap * (cols - 1)) / cols


def chip_x(col: int, card_w: float, gap: float) -> float:
    return PAD + col * (card_w + gap)


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


# ── Construction board (Main / Extra / Side) ────────────────────────────────

class Board:
    """Mirror of DeckBoardLayout. Values are read out of the C# so the two
    cannot drift apart silently."""

    def __init__(self, src: str) -> None:
        self.aspect = const_float(src, "BoardCardAspect")
        self.max_card = const_float(src, "MaxCardW")
        self.min_card = const_float(src, "MinCardW")
        self.head = const_float(src, "HeadBarH")
        self.cols_min = const_int(src, "MainColsMin")
        self.cols_max = const_int(src, "MainColsMax")
        self.board_pad_x = const_float(src, "BoardPadX")
        self.board_pad_y = const_float(src, "BoardPadY")
        self.board_spacing = const_float(src, "BoardSpacing")
        self.tray_pad_x = const_float(src, "TrayPadX")
        self.tray_pad_top = const_float(src, "TrayPadTop")
        self.tray_pad_bottom = const_float(src, "TrayPadBottom")
        self.tray_spacing = const_float(src, "TraySpacing")
        self.well_pad = const_float(src, "WellPad")
        self.gap_x = const_float(src, "GapX")
        self.gap_y = const_float(src, "GapY")
        self.tray_chrome = (self.tray_pad_top + self.tray_pad_bottom
                            + self.head + self.tray_spacing)
        self.board_chrome = (self.board_pad_y * 2 + self.board_spacing * 2
                             + self.tray_chrome * 3)

    def row_width(self, board_w: float) -> float:
        return max(32.0, board_w - self.board_pad_x * 2
                   - self.tray_pad_x * 2 - self.well_pad * 2)

    def card_width_for(self, board_w: float, cols: int) -> float:
        return min(self.max_card,
                   (self.row_width(board_w) - self.gap_x * (cols - 1)) / cols)

    def well_for(self, rows: int, card_h: float) -> float:
        return self.well_pad * 2 + rows * card_h + max(0, rows - 1) * self.gap_y

    def solve(self, board_w: float, board_h: float, main_slots: int) -> dict:
        main_slots = max(1, main_slots)
        budget = max(24.0, board_h - self.board_chrome)
        plan = None
        for cols in range(self.cols_min, self.cols_max + 1):
            cw = self.card_width_for(board_w, cols)
            if cw < self.min_card:
                break
            ch = cw * self.aspect
            rows = max(1, math.ceil(main_slots / cols))
            if self.well_for(rows, ch) + self.well_for(1, ch) * 2 > budget:
                continue
            plan = dict(cols=cols, card_w=cw, card_h=ch, rows=rows)
            break

        if plan is None:
            cols = self.cols_max
            while cols > self.cols_min and self.card_width_for(board_w, cols) < self.min_card:
                cols -= 1
            cw = self.card_width_for(board_w, cols)
            ch = cw * self.aspect
            room = budget - self.well_pad * 6
            if ch * 3 > room:
                ch = max(16.0, room / 3)
                cw = ch / self.aspect
                cols = max(self.cols_min,
                           min(self.cols_max,
                               math.floor((self.row_width(board_w) + self.gap_x)
                                          / (cw + self.gap_x))))
                cw = self.card_width_for(board_w, cols)
                ch = cw * self.aspect
            plan = dict(cols=cols, card_w=cw, card_h=ch,
                        rows=max(1, math.ceil(main_slots / cols)))

        row_well = self.well_for(1, plan["card_h"])
        main_need = self.well_for(plan["rows"], plan["card_h"])
        main_room = budget - row_well * 2
        if main_need <= main_room:
            main_well, scrolls, visible = main_need, False, plan["rows"]
        else:
            whole = max(1, math.floor((main_room - self.well_pad * 2 + self.gap_y)
                                      / (plan["card_h"] + self.gap_y)))
            main_well, scrolls, visible = self.well_for(whole, plan["card_h"]), True, whole

        slack = max(0.0, budget - (main_well + row_well * 2))
        if slack > 0.5:
            if scrolls:
                row_well += slack * 0.5
            else:
                share = plan["rows"] + 2.0
                main_well += slack * (plan["rows"] / share)
                row_well += slack * (1.0 / share)

        plan.update(budget=budget, main_well=main_well, row_well=row_well,
                    scrolls=scrolls, visible=visible)
        return plan


def canvas_scale(screen_w: float, screen_h: float) -> float:
    """CanvasScaler ScaleWithScreenSize, 1080x2340 reference, match 0.65."""
    return (screen_w / 1080.0) ** 0.35 * (screen_h / 2340.0) ** 0.65


# phase / board fractions measured off the built hierarchy:
# panel → BodyWell(0.92 x, 0.76 y) → BodyHost → PhaseBody(0.968 x, 0.938 y)
PHASE_W_FRAC = 0.660
PHASE_H_FRAC = 0.4976
WIDE_BOARD_X = 0.615
WIDE_CHROME = 176.0      # searchTop 112 + searchH 56 + 8
NARROW_CHROME = 256.0    # searchTop 192 + searchH 56 + 8
NARROW_BOARD_Y0 = 0.360

GAME_VIEWS = (
    ("short landscape 1378x656", 1378, 656, True),
    ("720p", 1280, 720, True),
    ("1080p", 1920, 1080, True),
    ("1440p", 2560, 1440, True),
    ("portrait phone 1080x2340", 1080, 2340, False),
)


def board_rect(screen_w: float, screen_h: float, wide: bool) -> tuple[float, float]:
    scale = canvas_scale(screen_w, screen_h)
    phase_w = PHASE_W_FRAC * screen_w / scale
    phase_h = PHASE_H_FRAC * screen_h / scale
    if wide:
        return WIDE_BOARD_X * phase_w, phase_h - WIDE_CHROME
    return phase_w, (phase_h - NARROW_CHROME) - NARROW_BOARD_Y0 * phase_h


def check_board(board_src: str) -> None:
    b = Board(board_src)
    print(f"  board chrome {b.board_chrome:.0f}px "
          f"(tray chrome {b.tray_chrome:.0f} x3)")
    for label, sw, sh, wide in GAME_VIEWS:
        bw, bh = board_rect(sw, sh, wide)
        scale = canvas_scale(sw, sh)
        for main_n in (40, 60):
            p = b.solve(bw, bh, main_n)
            cw, ch = p["card_w"], p["card_h"]

            # 1. Every pile draws the same face.
            if cw < b.min_card and not p["scrolls"] and cw * 3 > p["budget"]:
                fail(f"{label}/{main_n}: card {cw:.0f} under floor {b.min_card:.0f}")

            # 2. Trays fit the board exactly — no pile pushed past the edge.
            used = (b.board_chrome + p["main_well"] + p["row_well"] * 2)
            if used > bh + 0.6:
                fail(f"{label}/{main_n}: trays need {used:.0f}px of a {bh:.0f}px board")

            # 3. Whole rows only — no card sliced at a tray edge.
            vis = (p["main_well"] - b.well_pad * 2 + b.gap_y) / (ch + b.gap_y)
            if p["scrolls"] and abs(vis - round(vis)) > 0.02:
                fail(f"{label}/{main_n}: MAIN shows {vis:.2f} rows while scrolling")
            if p["row_well"] + 0.01 < b.well_for(1, ch):
                fail(f"{label}/{main_n}: Extra/Side well {p['row_well']:.0f} "
                     f"clips a {ch:.0f}px card")

            # 4. A 15-card Extra row shares MAIN's pitch or fans inside the tray.
            run = b.row_width(bw)
            packed = 15 * cw + 14 * b.gap_x
            step = cw + b.gap_x if packed <= run else (run - cw) / 14
            if step * 14 + cw > run + 0.01:
                fail(f"{label}/{main_n}: Extra row overruns the tray")

            if main_n == 40:
                print(f"  {label:26s} board {bw:.0f}x{bh:.0f} → "
                      f"{p['cols']:2d} cols, card {cw:.0f}x{ch:.0f} "
                      f"({cw*scale:.0f}x{ch*scale:.0f}px), "
                      f"{p['rows']} rows, {p['visible']} visible"
                      f"{' (scrolls)' if p['scrolls'] else ''}")
            else:
                print(f"  {'':26s} 60-card main → {p['cols']:2d} cols, "
                      f"card {cw:.0f}, {p['rows']} rows, {p['visible']} visible"
                      f"{' (scrolls)' if p['scrolls'] else ''}")


# ── Source invariants ───────────────────────────────────────────────────────

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
    snippet = collection[idx: idx + 1600]
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

    # One solver feeds all three piles. Per-tray card sizing is the bug that
    # drew Extra/Side at a third of MAIN's scale.
    if "class DeckBoardLayout" not in board:
        fail("DeckBoardLayout solver missing from DeckConstructionBoard")
    if "public DeckBoardLayout.Plan Plan" not in board:
        fail("DeckBoardFit does not expose a solved Plan")
    if "var cardW = plan.CardW;" not in board or "var cardH = plan.CardH;" not in board:
        fail("DeckPileFit no longer takes its card size from the board plan")
    if re.search(r"cardH = Mathf\.Clamp\(h", board):
        fail("DeckPileFit still derives Extra/Side card height from tray height")

    # Trays must 9-slice. Simple stretched tile_hub flat on Extra/Side.
    if "sliced: true" not in board:
        fail("pile trays no longer paint a sliced plate")
    if "pixelsPerUnitMultiplier" not in board:
        fail("tray plate does not rescale its 9-slice border (corners collapse)")

    # Clamped ScrollRect bottom-pins content smaller than the viewport.
    if "RectTransform.Axis.Vertical, h);" not in board:
        fail("empty pile content is not padded to the well height")


def main() -> int:
    collection = COLLECTION.read_text(encoding="utf-8")
    board = BOARD.read_text(encoding="utf-8")
    print("deck editor layout check")
    check_source_invariants(collection, board)
    print("  source invariants ok")
    check_pool_fill()
    check_board(board)
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
