#!/usr/bin/env python3
"""Generate ARGON menu chrome: Shadow Game void + KaibaCorp filament.

Replaces the smoked-glass Imagine plates (rounded glow, 3D gold hardware)
with sharp, nearly square obsidian sheets. Outer cyan/gold hairline, inner
double-filament, flat millennia L-ticks. No bloom halo, no glass highlight.

Writes into Assets/StreamingAssets/WRLDZ/Imagine/ui/ and a kit preview.
"""
from __future__ import annotations

import math
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/StreamingAssets/WRLDZ/Imagine/ui"
PREVIEW = ROOT / "Tools" / "ui_preview_filament_kit.png"

# Battle City night × Shadow Game × millennia
VOID = (14, 16, 32)
SHADOW = (48, 18, 78)
CYAN = (51, 235, 255)
GOLD = (255, 214, 71)
GOLD_DIM = (196, 156, 48)
MAGENTA = (255, 71, 152)
DANGER = (255, 68, 85)
CREAM = (250, 248, 244)


def write_png(path: Path, w: int, h: int, px: bytearray) -> None:
    def chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    raw = bytearray()
    row = w * 4
    for y in range(h):
        raw.append(0)
        raw.extend(px[y * row : (y + 1) * row])
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", ihdr))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        f.write(chunk(b"IEND", b""))


def new_buf(w: int, h: int) -> bytearray:
    return bytearray(w * h * 4)


def blend(px: bytearray, w: int, x: int, y: int, rgba: tuple[int, int, int, int]) -> None:
    if x < 0 or y < 0 or x >= w:
        return
    h = len(px) // (w * 4)
    if y >= h:
        return
    i = (y * w + x) * 4
    sa = rgba[3] / 255.0
    if sa <= 0:
        return
    da = px[i + 3] / 255.0
    out_a = sa + da * (1.0 - sa)
    if out_a <= 0:
        return
    for c in range(3):
        src = rgba[c] / 255.0
        dst = px[i + c] / 255.0
        px[i + c] = int(round((src * sa + dst * da * (1.0 - sa)) / out_a * 255.0))
    px[i + 3] = int(round(out_a * 255.0))


def sd_round_box(px: float, py: float, cx: float, cy: float, hw: float, hh: float, r: float) -> float:
    dx = abs(px - cx) - (hw - r)
    dy = abs(py - cy) - (hh - r)
    ox = max(dx, 0.0)
    oy = max(dy, 0.0)
    return math.hypot(ox, oy) + min(max(dx, dy), 0.0) - r


def coverage(d: float, width: float = 1.35) -> float:
    # 1 inside, 0 outside, AA band around the edge.
    return max(0.0, min(1.0, 0.5 - d / width))


def paint_round_rect(
    px: bytearray,
    w: int,
    h: int,
    x0: float,
    y0: float,
    x1: float,
    y1: float,
    radius: float,
    fill: tuple[int, int, int, int] | None,
    stroke: tuple[int, int, int, int] | None,
    stroke_w: float = 2.0,
) -> None:
    cx = (x0 + x1) * 0.5
    cy = (y0 + y1) * 0.5
    hw = (x1 - x0) * 0.5
    hh = (y1 - y0) * 0.5
    ix0 = max(0, int(x0) - 2)
    iy0 = max(0, int(y0) - 2)
    ix1 = min(w - 1, int(math.ceil(x1)) + 2)
    iy1 = min(h - 1, int(math.ceil(y1)) + 2)
    for y in range(iy0, iy1 + 1):
        for x in range(ix0, ix1 + 1):
            d = sd_round_box(x + 0.5, y + 0.5, cx, cy, hw, hh, radius)
            if fill is not None:
                a = coverage(d)
                if a > 0:
                    blend(px, w, x, y, (fill[0], fill[1], fill[2], int(fill[3] * a)))
            if stroke is not None and stroke_w > 0:
                # Ring: inside the outer edge, outside inner edge.
                inner = d + stroke_w
                ring = coverage(d) - coverage(inner)
                if ring > 0.004:
                    blend(px, w, x, y, (stroke[0], stroke[1], stroke[2], int(stroke[3] * ring)))


def hline(px: bytearray, w: int, x0: int, x1: int, y: int, rgba: tuple[int, int, int, int], thick: int = 2) -> None:
    if x1 < x0:
        x0, x1 = x1, x0
    for t in range(thick):
        for x in range(x0, x1 + 1):
            blend(px, w, x, y + t, rgba)


def vline(px: bytearray, w: int, x: int, y0: int, y1: int, rgba: tuple[int, int, int, int], thick: int = 2) -> None:
    if y1 < y0:
        y0, y1 = y1, y0
    for t in range(thick):
        for y in range(y0, y1 + 1):
            blend(px, w, x + t, y, rgba)


def l_tick(
    px: bytearray,
    w: int,
    x: int,
    y: int,
    arm: int,
    thick: int,
    color: tuple[int, int, int, int],
    corner: str,
) -> None:
    """Flat millennia L — not 3D hardware."""
    if corner == "tl":
        hline(px, w, x, x + arm, y, color, thick)
        vline(px, w, x, y, y + arm, color, thick)
    elif corner == "tr":
        hline(px, w, x - arm, x, y, color, thick)
        vline(px, w, x - thick + 1, y, y + arm, color, thick)
    elif corner == "bl":
        hline(px, w, x, x + arm, y - thick + 1, color, thick)
        vline(px, w, x, y - arm, y, color, thick)
    else:
        hline(px, w, x - arm, x, y - thick + 1, color, thick)
        vline(px, w, x - thick + 1, y - arm, y, color, thick)


def violet_wash(px: bytearray, w: int, h: int, x0: int, y0: int, x1: int, y1: int, strength: float = 0.22) -> None:
    """Shadow Game top wash — stays in the fill, not a glass streak."""
    hh = max(1, y1 - y0)
    for y in range(y0, y1):
        u = 1.0 - (y - y0) / hh
        a = int(255 * strength * (u * u))
        if a <= 0:
            continue
        for x in range(x0, x1):
            blend(px, w, x, y, (SHADOW[0], SHADOW[1], SHADOW[2], a))


def plate(
    w: int,
    h: int,
    *,
    fill_a: int = 245,
    edge: tuple[int, int, int] = CYAN,
    gold_ticks: bool = True,
    radius: float = 5.0,
    pad: float = 10.0,
    stroke_w: float = 2.2,
    inner: bool = True,
    hollow: bool = False,
    wash: bool = True,
    tick_arm: int = 18,
) -> bytearray:
    px = new_buf(w, h)
    x0, y0, x1, y1 = pad, pad, w - 1 - pad, h - 1 - pad
    fill_alpha = 0 if hollow else fill_a
    fill = (VOID[0], VOID[1], VOID[2], fill_alpha)
    stroke = (edge[0], edge[1], edge[2], 255)
    paint_round_rect(px, w, h, x0, y0, x1, y1, radius, fill if fill_alpha else None, stroke, stroke_w)
    if inner:
        inset = pad + 5.0
        inner_col = (edge[0], edge[1], edge[2], 110)
        paint_round_rect(
            px, w, h, inset, pad + 5.0, w - 1 - inset, h - 1 - pad - 5.0,
            max(1.0, radius - 2.0), None, inner_col, 1.15,
        )
    if wash and not hollow:
        violet_wash(px, w, h, int(x0) + 6, int(y0) + 6, int(x1) - 6, int(y0) + int((y1 - y0) * 0.42), 0.32)
    if gold_ticks:
        inset = int(pad + 11)
        col = (GOLD[0], GOLD[1], GOLD[2], 230)
        l_tick(px, w, inset, inset, tick_arm, 2, col, "tl")
        l_tick(px, w, w - 1 - inset, inset, tick_arm, 2, col, "tr")
        l_tick(px, w, inset, h - 1 - inset, tick_arm, 2, col, "bl")
        l_tick(px, w, w - 1 - inset, h - 1 - inset, tick_arm, 2, col, "br")
    return px


def wide_button(w: int, h: int, edge: tuple[int, int, int], fill_a: int = 250) -> bytearray:
    px = plate(w, h, fill_a=fill_a, edge=edge, gold_ticks=False, radius=4.0, pad=8.0, tick_arm=12)
    # Blade slots — Duel Disk grammar, left and right.
    mid = h // 2
    col = (GOLD[0], GOLD[1], GOLD[2], 220)
    x_l = 18
    x_r = w - 20
    vline(px, w, x_l, mid - 14, mid + 14, col, 2)
    vline(px, w, x_r, mid - 14, mid + 14, col, 2)
    hline(px, w, x_l - 3, x_l + 5, mid - 1, col, 2)
    hline(px, w, x_r - 5, x_r + 3, mid - 1, col, 2)
    return px


def hollow_chip(w: int, h: int, edge: tuple[int, int, int], radius: float = 6.0) -> bytearray:
    return plate(
        w, h, fill_a=0, edge=edge, gold_ticks=True, radius=radius,
        pad=8.0, stroke_w=2.0, hollow=True, wash=False, tick_arm=12,
    )


def bar(w: int, h: int) -> bytearray:
    # Thin dock: 9-slice top/bottom is only 12px — tiny ticks.
    px = plate(
        w, h, fill_a=230, edge=CYAN, gold_ticks=False, radius=3.0,
        pad=6.0, stroke_w=1.8, inner=True, wash=True, tick_arm=8,
    )
    col = (GOLD[0], GOLD[1], GOLD[2], 210)
    l_tick(px, w, 14, 10, 8, 2, col, "tl")
    l_tick(px, w, w - 15, 10, 8, 2, col, "tr")
    l_tick(px, w, 14, h - 11, 8, 2, col, "bl")
    l_tick(px, w, w - 15, h - 11, 8, 2, col, "br")
    return px


def blit(dst: bytearray, dw: int, dh: int, src: bytearray, sw: int, sh: int, ox: int, oy: int) -> None:
    for y in range(sh):
        ty = oy + y
        if ty < 0 or ty >= dh:
            continue
        for x in range(sw):
            tx = ox + x
            if tx < 0 or tx >= dw:
                continue
            i = (y * sw + x) * 4
            blend(dst, dw, tx, ty, (src[i], src[i + 1], src[i + 2], src[i + 3]))


def fill_rect(px: bytearray, w: int, h: int, rgba: tuple[int, int, int, int]) -> None:
    for y in range(h):
        for x in range(w):
            i = (y * w + x) * 4
            px[i : i + 4] = bytes(rgba)


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    jobs: list[tuple[str, bytearray, int, int]] = []

    def add(name: str, buf: bytearray, w: int, h: int) -> None:
        jobs.append((name, buf, w, h))
        write_png(OUT / name, w, h, buf)
        print(f"  {name} {w}x{h}")

    print("shadow-filament chrome →", OUT.relative_to(ROOT))

    tw, th = 512, 512
    add("tile_hub.png", plate(tw, th, edge=CYAN, fill_a=248), tw, th)
    add("tile_hub_gold.png", plate(tw, th, edge=GOLD, fill_a=248), tw, th)

    bw, bh = 768, 256
    add("button_primary_plate.png", wide_button(bw, bh, CYAN), bw, bh)
    add("button_primary_hover.png", wide_button(bw, bh, CYAN, fill_a=252), bw, bh)
    add("button_primary_pressed.png", wide_button(bw, bh, CYAN, fill_a=240), bw, bh)
    add("button_secondary_plate.png", wide_button(bw, bh, CYAN, fill_a=236), bw, bh)
    add("button_gold_plate.png", wide_button(bw, bh, GOLD), bw, bh)
    add("button_danger_plate.png", wide_button(bw, bh, DANGER, fill_a=248), bw, bh)

    pw, ph = 768, 448
    add("panel_holo_glass.png", plate(pw, ph, edge=CYAN, fill_a=242, radius=6.0, pad=14.0), pw, ph)
    add("panel_menu_glass.png", plate(pw, ph, edge=CYAN, fill_a=240, radius=5.0, pad=12.0), pw, ph)
    add("panel_modal.png", plate(pw, ph, edge=GOLD, fill_a=246, radius=6.0, pad=14.0), pw, ph)
    add("panel_boot_glass.png", plate(pw, ph, edge=GOLD, fill_a=248, radius=6.0, pad=14.0), pw, ph)
    add("menu_holo_sheet.png", plate(pw, ph, edge=CYAN, fill_a=244, radius=6.0, pad=16.0, tick_arm=22), pw, ph)
    add("panel_inspect_sheet.png", plate(pw, ph, edge=CYAN, fill_a=246, radius=5.0, pad=14.0), pw, ph)

    cw, ch = 512, 640
    add("menu_holo_card.png", plate(cw, ch, edge=CYAN, fill_a=248, radius=6.0, pad=14.0, tick_arm=20), cw, ch)
    add("menu_holo_card_gold.png", plate(cw, ch, edge=GOLD, fill_a=248, radius=6.0, pad=14.0, tick_arm=20), cw, ch)

    dw, dh = 768, 420
    add("panel_deck_main.png", plate(dw, dh, edge=CYAN, fill_a=244, radius=5.0, pad=16.0), dw, dh)
    add("panel_deck_extra.png", plate(dw, dh, edge=GOLD, fill_a=244, radius=5.0, pad=16.0), dw, dh)
    add("panel_deck_side.png", plate(dw, dh, edge=CYAN, fill_a=236, radius=5.0, pad=16.0), dw, dh)

    hw, hh = 768, 280
    add("hud_island_glass.png", hollow_chip(hw, hh, CYAN, radius=5.0), hw, hh)
    add("hud_callout_glass.png", hollow_chip(hw, 220, GOLD, radius=5.0), hw, 220)

    chw, chh = 640, 192
    add("hud_chip_cyan.png", hollow_chip(chw, chh, CYAN, radius=5.0), chw, chh)
    add("hud_chip_gold.png", hollow_chip(chw, chh, GOLD, radius=5.0), chw, chh)
    add("hud_chip_danger.png", hollow_chip(chw, chh, DANGER, radius=5.0), chw, chh)
    add("hud_chip_plate.png", plate(chw, chh, edge=CYAN, fill_a=220, gold_ticks=False, radius=4.0, pad=8.0), chw, chh)

    bar_w, bar_h = 768, 128
    add("bar_bottom.png", bar(bar_w, bar_h), bar_w, bar_h)
    add("bar_top_hud.png", bar(bar_w, bar_h), bar_w, bar_h)

    # Kit preview on Battle City void
    prev_w, prev_h = 1280, 720
    prev = new_buf(prev_w, prev_h)
    fill_rect(prev, prev_w, prev_h, (18, 28, 22, 255))
    # map-green so plates read as slates, not wireframes
    violet_wash(prev, prev_w, prev_h, 0, 0, prev_w, 280, 0.28)
    # dest tiles
    tile = plate(280, 280, edge=CYAN, fill_a=248)
    tile_g = plate(280, 280, edge=GOLD, fill_a=248)
    blit(prev, prev_w, prev_h, tile, 280, 280, 48, 80)
    blit(prev, prev_w, prev_h, tile_g, 280, 280, 360, 80)
    # overlay sheet
    sheet = plate(520, 300, edge=CYAN, fill_a=244, pad=14.0, tick_arm=20)
    blit(prev, prev_w, prev_h, sheet, 520, 300, 700, 70)
    # command row
    row = wide_button(600, 96, CYAN)
    blit(prev, prev_w, prev_h, row, 600, 96, 48, 420)
    row_g = wide_button(360, 96, GOLD)
    blit(prev, prev_w, prev_h, row_g, 360, 96, 670, 420)
    # HUD island
    island = hollow_chip(420, 120, CYAN)
    blit(prev, prev_w, prev_h, island, 420, 120, 48, 545)
    island_m = hollow_chip(360, 120, MAGENTA)
    blit(prev, prev_w, prev_h, island_m, 360, 120, 500, 545)
    write_png(PREVIEW, prev_w, prev_h, prev)
    print("preview", PREVIEW)
    print("wrote", len(jobs), "plates")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
