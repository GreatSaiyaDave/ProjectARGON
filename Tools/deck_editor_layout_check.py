#!/usr/bin/env python3
"""Offline checks for the deck editor COLLECTION / construction board layout.

The Cloud VM has no Unity Editor. This script:
  1. Guards the PinBelow-on-content footgun that packed chips to the right.
  2. Replays DeckPoolLayout math (pad 6, last column at width-pad).
  3. Replays DeckBoardLayout across real Game-view sizes and asserts the
     three piles share a card size, a left edge and a column pitch, and that
     no row is sliced off the bottom of a tray. P4: the board is the v6.3 hall's
     altar (10 x 4 at the baked wells, EXTRA and SIDE on one row), fitted into
     MenuShellOverlay under the aspect scaler (1920x1080 / match 0.5 landscape).

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
    cannot drift apart silently. P4: EXTRA and SIDE share one row under MAIN
    (faces at XRowScale), so the budget is well(rows) + well(1 x-row face)."""

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
        self.x_scale = const_float(src, "XRowScale")
        self.x_split = const_float(src, "XRowSplit")
        self.x_spacing = const_float(src, "XRowSpacing")
        self.x_head = {True: const_float(src, "XHeadExtraW"), False: const_float(src, "XHeadSideW")}
        self.x_gap = const_float(src, "XGapX")
        self.hall_cols = const_int(src, "HallCols")
        self.hall_rows = const_int(src, "HallRows")
        self.hall_slot = {"land": const_float(src, "HallSlotLand"),
                          "port": const_float(src, "HallSlotPort")}
        self.tray_chrome = (self.tray_pad_top + self.tray_pad_bottom
                            + self.head + self.tray_spacing)
        self.x_tray_chrome = self.tray_pad_top + self.tray_pad_bottom
        self.board_chrome = (self.board_pad_y * 2 + self.board_spacing
                             + self.tray_chrome + self.x_tray_chrome)

    def row_width(self, board_w: float) -> float:
        return max(32.0, board_w - self.board_pad_x * 2
                   - self.tray_pad_x * 2 - self.well_pad * 2)

    def card_width_for(self, board_w: float, cols: int) -> float:
        return min(self.max_card,
                   (self.row_width(board_w) - self.gap_x * (cols - 1)) / cols)

    def well_for(self, rows: int, card_h: float) -> float:
        return self.well_pad * 2 + rows * card_h + max(0, rows - 1) * self.gap_y

    def x_well_for(self, card_h: float) -> float:
        return self.well_for(1, card_h * self.x_scale)

    def x_run(self, board_w: float, extra: bool) -> float:
        row = max(32.0, board_w - self.board_pad_x * 2 - self.x_spacing)
        tray = row * (self.x_split if extra else 1 - self.x_split)
        return max(8.0, tray - self.tray_pad_x * 2 - self.x_head[extra] - self.well_pad * 2)

    def hall_board(self, slot_x: float, slot_y: float, slot_w: float) -> tuple:
        ch = slot_w * self.aspect
        w = (self.hall_cols * slot_w + (self.hall_cols - 1) * self.gap_x
             + (self.well_pad + self.tray_pad_x + self.board_pad_x) * 2)
        h = (self.board_chrome + self.well_for(self.hall_rows, ch)
             + self.x_well_for(ch) + 0.5)
        x = slot_x - self.well_pad - self.tray_pad_x - self.board_pad_x
        y = (slot_y - self.well_pad - self.head - self.tray_spacing
             - self.tray_pad_top - self.board_pad_y)
        return x, y, w, h

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
            if self.well_for(rows, ch) + self.x_well_for(ch) > budget:
                continue
            plan = dict(cols=cols, card_w=cw, card_h=ch, rows=rows)
            break

        if plan is None:
            cols = self.cols_max
            while cols > self.cols_min and self.card_width_for(board_w, cols) < self.min_card:
                cols -= 1
            cw = self.card_width_for(board_w, cols)
            ch = cw * self.aspect
            room = budget - self.well_pad * 4
            if ch * (1 + self.x_scale) > room:
                ch = max(16.0, room / (1 + self.x_scale))
                cw = ch / self.aspect
            plan = dict(cols=cols, card_w=cw, card_h=ch,
                        rows=max(1, math.ceil(main_slots / cols)))

        row_well = self.x_well_for(plan["card_h"])
        main_need = self.well_for(plan["rows"], plan["card_h"])
        main_room = budget - row_well
        if main_need <= main_room:
            main_well, scrolls, visible = main_need, False, plan["rows"]
        else:
            whole = max(1, math.floor((main_room - self.well_pad * 2 + self.gap_y)
                                      / (plan["card_h"] + self.gap_y)))
            main_well, scrolls, visible = self.well_for(whole, plan["card_h"]), True, whole

        slack = max(0.0, budget - (main_well + row_well))
        if slack > 0.5:
            if scrolls:
                row_well += slack
            else:
                share = plan["rows"] + 1.0
                main_well += slack * (plan["rows"] / share)
                row_well += slack * (1.0 / share)

        plan.update(budget=budget, main_well=main_well, row_well=row_well,
                    scrolls=scrolls, visible=visible)
        return plan


def canvas_scale(screen_w: float, screen_h: float) -> float:
    """P4 (BUILD_PLAN §11 F3): MenuShellOverlay opts into UiTheme.ApplyForAspect —
    a landscape surface (wider than 1.15 x tall) takes the 1920x1080 reference at
    match 0.5, a portrait one today's 1080x2340 at match 0.65."""
    if screen_w > screen_h * 1.15:
        return (screen_w / 1920.0) ** 0.5 * (screen_h / 1080.0) ** 0.5
    return (screen_w / 1080.0) ** 0.35 * (screen_h / 2340.0) ** 0.65


# The hall (deck_<lay>.json, the approved deck page): the stage, slot 0 of the
# 10 x 4 altar, the altar slab and, in portrait, the search band under the
# shared row (decision 12: the row ends above the band). The hall is built in
# stage px and fitted into the overlay canvas (uniform contain), so the board
# below is the same at every view of a layout; the canvas model gives its
# on-screen size.
HALL = {
    "land": dict(stage=(1920.0, 1080.0), slot0=(72.0, 318.0),
                 altar=(46.0, 248.0, 923.0, 958.0), below=None),
    "port": dict(stage=(1080.0, 2340.0), slot0=(44.0, 604.0),
                 altar=(18.0, 534.0, 1055.0, 1386.0), below=1300.0),
}
# EXTRA / SIDE on one row under MAIN: the board's right edge over the stage width
# (the plan's "split 0.615 -> about 0.49")
WIDE_BOARD_X = 0.48
# The page's pitch (deck_land.json geo.D.altar gx 5 / gy 7; the .xrow faces 54.6 wide at a 62.6 pitch): the board
# above is derived from the C# constants, so without this pin a changed gap would still "fit" while the cards leave
# the baked wells. (Portrait's page gy is 8: one board spacing serves both plans, a 1 px / row P5 parity item.)
PAGE_PITCH_LAND = dict(gap_x=5.0, gap_y=7.0, x_gap=8.0)

GAME_VIEWS = (
    ("short landscape 1378x656", 1378, 656, True),
    ("720p", 1280, 720, True),
    ("1080p", 1920, 1080, True),
    ("1440p", 2560, 1440, True),
    ("portrait phone 1080x2340", 1080, 2340, False),
)


def board_rect(b: "Board", screen_w: float, screen_h: float, wide: bool):
    lay = "land" if wide else "port"
    hall = HALL[lay]
    scale = canvas_scale(screen_w, screen_h)
    cw, ch = screen_w / scale, screen_h / scale
    sw, sh = hall["stage"]
    k = min(cw / sw, ch / sh)
    x, y, w, h = b.hall_board(hall["slot0"][0], hall["slot0"][1], b.hall_slot[lay])
    return lay, hall, (x, y, w, h), k, scale


def check_board(board_src: str) -> None:
    b = Board(board_src)
    print(f"  board chrome {b.board_chrome:.0f}px "
          f"(MAIN tray {b.tray_chrome:.0f} + shared row {b.x_tray_chrome:.0f})")
    for label, sw, sh, wide in GAME_VIEWS:
        lay, hall, (bx, by, bw, bh), k, scale = board_rect(b, sw, sh, wide)
        slot = b.hall_slot[lay]
        ax0, ay0, ax1, ay1 = hall["altar"]
        if bx < ax0 - 0.5 or by < ay0 - 0.5 or bx + bw > ax1 + 0.5 or by + bh > ay1 + 0.5:
            fail(f"{label}: board {bx:.0f},{by:.0f} {bw:.0f}x{bh:.0f} leaves the altar slab {hall['altar']}")
        if hall["below"] is not None and by + bh > hall["below"] + 0.01:
            fail(f"{label}: the shared row ends at {by + bh:.1f}, under the search band at {hall['below']:.0f}")
        if wide and abs((bx + bw) / hall["stage"][0] - WIDE_BOARD_X) > 0.01:
            fail(f"{label}: the altar split moved to {(bx + bw) / hall['stage'][0]:.3f} (WIDE_BOARD_X {WIDE_BOARD_X})")
        if wide:
            for key, want in PAGE_PITCH_LAND.items():
                got = getattr(b, key)
                if abs(got - want) > 0.01:
                    fail(f"{label}: {key} {got:g} is not the page's {want:g} (the cards would leave the baked wells)")
        for main_n in (40, 60):
            p = b.solve(bw, bh, main_n)
            cw, ch = p["card_w"], p["card_h"]

            # 1. MAIN lays the hall's 10 columns at the baked wells' size.
            if p["cols"] != b.hall_cols or abs(cw - slot) > 0.5:
                fail(f"{label}/{main_n}: {p['cols']} cols of {cw:.1f} — the hall's wells are "
                     f"{b.hall_cols} x {slot:.0f}")
            if cw < b.min_card and not p["scrolls"]:
                fail(f"{label}/{main_n}: card {cw:.0f} under floor {b.min_card:.0f}")

            # 2. Trays fit the board exactly — no pile pushed past the edge.
            used = b.board_chrome + p["main_well"] + p["row_well"]
            if used > bh + 0.6:
                fail(f"{label}/{main_n}: trays need {used:.0f}px of a {bh:.0f}px board")

            # 3. Whole rows only — no card sliced at a tray edge; the altar shows its 4 rows.
            vis = (p["main_well"] - b.well_pad * 2 + b.gap_y) / (ch + b.gap_y)
            if p["scrolls"] and abs(vis - round(vis)) > 0.02:
                fail(f"{label}/{main_n}: MAIN shows {vis:.2f} rows while scrolling")
            if p["visible"] != b.hall_rows:
                fail(f"{label}/{main_n}: MAIN shows {p['visible']} rows, the altar has {b.hall_rows}")
            if p["row_well"] + 0.01 < b.x_well_for(ch):
                fail(f"{label}/{main_n}: shared-row well {p['row_well']:.0f} "
                     f"clips a {ch * b.x_scale:.0f}px card")

            # 4. A 15-card EXTRA (or SIDE) row keeps its pitch or fans inside its half of the row.
            xcw = cw * b.x_scale
            for extra in (True, False):
                run = b.x_run(bw, extra)
                packed = 15 * xcw + 14 * b.x_gap
                step = xcw + b.x_gap if packed <= run else (run - xcw) / 14
                if step * 14 + xcw > run + 0.01 or step <= 0:
                    fail(f"{label}/{main_n}: {'EXTRA' if extra else 'SIDE'} row overruns its tray")

            px = cw * k * scale
            if main_n == 40:
                print(f"  {label:26s} {lay} board {bw:.0f}x{bh:.0f} (fit k {k:.3f}) -> "
                      f"{p['cols']:2d} cols, card {cw:.0f}x{ch:.0f} "
                      f"({px:.0f}x{px * b.aspect:.0f}px), "
                      f"{p['rows']} rows, {p['visible']} visible"
                      f"{' (scrolls)' if p['scrolls'] else ''}; shared row {xcw:.1f}x{ch * b.x_scale:.1f}")
            else:
                print(f"  {'':26s} 60-card main -> {p['cols']:2d} cols, "
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

    # P4: EXTRA and SIDE share one row, solved by the same plan (their face = MAIN's x XRowScale).
    if '"XRow"' not in board or "fit.BindRow(" not in board:
        fail("the shared EXTRA / SIDE row is missing from the construction board")
    if "fit.FaceScale = main ? 1f : DeckBoardLayout.XRowScale;" not in board:
        fail("the shared row no longer takes its face from MAIN's plan")
    if "XWellFor(ch) > budget" not in board:
        fail("Solve no longer budgets one shared row (well(rows) + well(1 x-row face))")


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
