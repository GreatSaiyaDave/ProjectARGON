#!/usr/bin/env python3
"""WRLDZ Spirit Lenses — dimensioned SVG schematics (exact labels)."""
from pathlib import Path

OUT = Path(__file__).resolve().parent
NAVY, GOLD, CYAN, MAG, CREAM, LINE, DIM, BOX = (
    "#0B1220", "#E8C547", "#3BE0F0", "#FF5AA5", "#F4F0E6",
    "#8AA0B8", "#5C6F86", "#152033",
)


def svg(w, h, body):
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">
<rect width="100%" height="100%" fill="{NAVY}"/>
<rect x="24" y="24" width="{w-48}" height="{h-48}" fill="none" stroke="{GOLD}" stroke-width="2"/>
{body}
</svg>
'''


def title(t, sub, w):
    return f'''
<text x="48" y="64" fill="{GOLD}" font-family="DejaVu Sans, sans-serif" font-size="22" font-weight="700">{t}</text>
<text x="48" y="88" fill="{DIM}" font-family="DejaVu Sans, sans-serif" font-size="13">{sub}</text>
<line x1="48" y1="100" x2="{w-48}" y2="100" stroke="{GOLD}" stroke-width="1" opacity="0.4"/>
'''


def box(x, y, w, h, label, sub="", fill=BOX, stroke=CYAN):
    t = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{fill}" stroke="{stroke}" stroke-width="2"/>'
    t += f'<text x="{x+w/2}" y="{y+h/2-4}" fill="{CREAM}" font-family="DejaVu Sans, sans-serif" font-size="14" font-weight="700" text-anchor="middle">{label}</text>'
    if sub:
        t += f'<text x="{x+w/2}" y="{y+h/2+16}" fill="{DIM}" font-family="DejaVu Sans, sans-serif" font-size="11" text-anchor="middle">{sub}</text>'
    return t


def arrow(x1, y1, x2, y2, label="", color=CYAN):
    return f'''
<defs>
  <marker id="ah-{int(x1)}-{int(y1)}" markerWidth="8" markerHeight="8" refX="7" refY="3" orient="auto">
    <path d="M0,0 L0,6 L8,3 z" fill="{color}"/>
  </marker>
</defs>
<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="2" marker-end="url(#ah-{int(x1)}-{int(y1)})"/>
''' + (f'<text x="{(x1+x2)/2}" y="{(y1+y2)/2-8}" fill="{DIM}" font-size="11" font-family="DejaVu Sans, sans-serif" text-anchor="middle">{label}</text>' if label else "")


def note(x, y, lines):
    s = ""
    for i, ln in enumerate(lines):
        s += f'<text x="{x}" y="{y+i*16}" fill="{CREAM}" font-family="DejaVu Sans, sans-serif" font-size="12">{ln}</text>'
    return s


def sheet_system():
    w, h = 1400, 900
    b = title("SCH-01  SYSTEM ARCHITECTURE", "WRLDZ Spirit Lenses  ·  binocular optical see-through  ·  S23 Ultra is the computer", w)
    b += box(60, 160, 220, 90, "Galaxy S23 Ultra", "USB-C DP Alt Mode · 5V", stroke=GOLD)
    b += box(360, 160, 240, 90, "USB-C cable", "full-featured · DP + USB2 + 5V/1A", stroke=LINE)
    b += box(680, 140, 280, 130, "DP bridge  LT7911UXC", "salvage from donor glasses board", stroke=CYAN)
    b += box(1040, 80, 280, 100, "LEFT Sony micro-OLED", "ECX343E / ECX334  MIPI", stroke=MAG)
    b += box(1040, 230, 280, 100, "RIGHT Sony micro-OLED", "matched pair  MIPI", stroke=MAG)
    b += box(680, 320, 280, 100, "MCU + IMU", "3DoF gyro/accel  USB2 HID", stroke=GOLD)
    b += box(360, 500, 280, 110, "LEFT birdbath engine", "PBS + curved combiner  ~25 mm", stroke=CYAN)
    b += box(760, 500, 280, 110, "RIGHT birdbath engine", "matched  IPD 58–68 mm", stroke=CYAN)
    b += box(560, 700, 300, 100, "Your eyes + real world", "optical see-through  ~45° FOV", stroke=GOLD)
    b += arrow(280, 205, 360, 205, "DP video")
    b += arrow(600, 205, 680, 205)
    b += arrow(960, 175, 1040, 130, "MIPI L")
    b += arrow(960, 235, 1040, 280, "MIPI R")
    b += arrow(820, 270, 820, 320)
    b += arrow(1180, 180, 1180, 500, "")
    b += arrow(1180, 330, 900, 500, "")
    b += f'<text x="1220" y="360" fill="{DIM}" font-size="11" font-family="DejaVu Sans, sans-serif">light into engines</text>'
    b += arrow(500, 610, 620, 700)
    b += arrow(900, 610, 780, 700)
    b += note(60, 300, [
        "DO NOT gut the S23. It is compute, GPS, and battery.",
        "Glasses are a dumb stereo display + IMU.",
        "Harvest birdbath engines as sealed units.",
        "Waveguides cannot be made in a garage.",
    ])
    b += note(60, 800, ["WRLDZ / Project ARGON  ·  SCH-01  ·  not to scale  ·  keep engines sealed"])
    return svg(w, h, b)


def sheet_optical():
    w, h = 1400, 980
    b = title("SCH-02  OPTICAL PATH  (one eye, side) + BINOCULAR PLAN", "Birdbath: OLED down into 45° polarizing beam splitter, folded onto a curved combiner. Depth ≈ 25 mm (Guttag / Nreal Light).", w)

    # Side view one eye
    b += f'<text x="80" y="140" fill="{GOLD}" font-size="16" font-family="DejaVu Sans, sans-serif">A. SIDE VIEW — RIGHT EYE</text>'
    # OLED
    b += f'<rect x="180" y="170" width="140" height="18" fill="{MAG}" stroke="{CREAM}" stroke-width="1"/>'
    b += f'<text x="250" y="164" fill="{CREAM}" font-size="12" text-anchor="middle" font-family="DejaVu Sans, sans-serif">micro-OLED  (screen down)</text>'
    # PBS diamond
    b += f'<polygon points="250,210 310,270 250,330 190,270" fill="#1c3348" stroke="{CYAN}" stroke-width="2"/>'
    b += f'<line x1="190" y1="270" x2="310" y2="270" stroke="{GOLD}" stroke-width="1" stroke-dasharray="4 3"/>'
    b += f'<text x="250" y="276" fill="{GOLD}" font-size="11" text-anchor="middle" font-family="DejaVu Sans, sans-serif">PBS 45°</text>'
    # combiner bowl
    b += f'<path d="M 250,330 Q 250,430 160,450" fill="none" stroke="{CYAN}" stroke-width="4"/>'
    b += f'<text x="300" y="410" fill="{CYAN}" font-size="12" font-family="DejaVu Sans, sans-serif">curved combiner</text>'
    b += f'<text x="300" y="426" fill="{DIM}" font-size="11" font-family="DejaVu Sans, sans-serif">(semi-mirror ~25–30% R)</text>'
    # world
    b += f'<text x="80" y="456" fill="{CREAM}" font-size="12" font-family="DejaVu Sans, sans-serif">WORLD →</text>'
    b += arrow(140, 450, 200, 450, "", CREAM)
    # eye
    b += f'<ellipse cx="430" cy="270" rx="28" ry="18" fill="none" stroke="{GOLD}" stroke-width="2"/>'
    b += f'<circle cx="418" cy="270" r="8" fill="{GOLD}"/>'
    b += f'<text x="470" y="274" fill="{GOLD}" font-size="13" font-family="DejaVu Sans, sans-serif">EYE</text>'
    b += f'<text x="470" y="292" fill="{DIM}" font-size="11" font-family="DejaVu Sans, sans-serif">relief 12–18 mm</text>'
    # rays
    b += f'<line x1="250" y1="188" x2="250" y2="210" stroke="{MAG}" stroke-width="2"/>'
    b += f'<line x1="250" y1="270" x2="402" y2="270" stroke="{MAG}" stroke-width="2"/>'
    b += f'<text x="80" y="520" fill="{CREAM}" font-size="12" font-family="DejaVu Sans, sans-serif">Black OLED pixels = off = see-through. LCD donors stay milky. AMOLED / micro-OLED only.</text>'
    b += f'<text x="80" y="540" fill="{CREAM}" font-size="12" font-family="DejaVu Sans, sans-serif">Combiner blocks ~70–75% of world light (sunglasses). Do not walk in traffic while debugging FOV.</text>'

    # Dimensions
    b += f'<line x1="160" y1="188" x2="160" y2="450" stroke="{DIM}" stroke-width="1"/>'
    b += f'<text x="70" y="330" fill="{DIM}" font-size="11" font-family="DejaVu Sans, sans-serif">~25 mm</text>'
    b += f'<text x="70" y="346" fill="{DIM}" font-size="11" font-family="DejaVu Sans, sans-serif">stack depth</text>'

    # Binocular plan
    b += f'<text x="80" y="600" fill="{GOLD}" font-size="16" font-family="DejaVu Sans, sans-serif">B. PLAN — BOTH EYES</text>'
    b += f'<rect x="180" y="640" width="160" height="90" rx="6" fill="{BOX}" stroke="{CYAN}" stroke-width="2"/>'
    b += f'<text x="260" y="690" fill="{CREAM}" font-size="13" text-anchor="middle" font-family="DejaVu Sans, sans-serif">L engine</text>'
    b += f'<rect x="520" y="640" width="160" height="90" rx="6" fill="{BOX}" stroke="{CYAN}" stroke-width="2"/>'
    b += f'<text x="600" y="690" fill="{CREAM}" font-size="13" text-anchor="middle" font-family="DejaVu Sans, sans-serif">R engine</text>'
    b += f'<line x1="340" y1="685" x2="520" y2="685" stroke="{GOLD}" stroke-width="2"/>'
    b += f'<text x="430" y="670" fill="{GOLD}" font-size="12" text-anchor="middle" font-family="DejaVu Sans, sans-serif">IPD 63 mm nom.</text>'
    b += f'<text x="430" y="720" fill="{DIM}" font-size="11" text-anchor="middle" font-family="DejaVu Sans, sans-serif">slide 58–68 mm</text>'
    b += f'<circle cx="260" cy="800" r="16" fill="none" stroke="{GOLD}" stroke-width="2"/>'
    b += f'<circle cx="600" cy="800" r="16" fill="none" stroke="{GOLD}" stroke-width="2"/>'
    b += f'<text x="260" y="840" fill="{DIM}" font-size="11" text-anchor="middle" font-family="DejaVu Sans, sans-serif">left pupil</text>'
    b += f'<text x="600" y="840" fill="{DIM}" font-size="11" text-anchor="middle" font-family="DejaVu Sans, sans-serif">right pupil</text>'
    b += f'<line x1="260" y1="730" x2="260" y2="784" stroke="{LINE}" stroke-width="1" stroke-dasharray="3 3"/>'
    b += f'<line x1="600" y1="730" x2="600" y2="784" stroke="{LINE}" stroke-width="1" stroke-dasharray="3 3"/>'
    b += f'<text x="900" y="660" fill="{CREAM}" font-size="13" font-family="DejaVu Sans, sans-serif">FOV ≈ 45–52° diagonal (birdbath class)</text>'
    b += f'<text x="900" y="682" fill="{CREAM}" font-size="13" font-family="DejaVu Sans, sans-serif">Do not split a sealed engine. Alignment is the product.</text>'
    b += f'<text x="900" y="704" fill="{CREAM}" font-size="13" font-family="DejaVu Sans, sans-serif">Harvest LEFT and RIGHT as a matched pair from one donor.</text>'
    b += note(80, 920, ["WRLDZ / Project ARGON  ·  SCH-02  ·  dimensions typical; measure YOUR donor engines before printing the frame"])
    return svg(w, h, b)


def sheet_electrical():
    w, h = 1400, 980
    b = title("SCH-03  ELECTRICAL  (functional)   USB-C DisplayPort glasses", "S23 Ultra supplies 5 V / ~1 A + DP 1.2/1.4 video + USB 2.0 data. Glasses have no battery.", w)
    b += note(60, 130, [
        "This is a FUNCTIONAL schematic of a harvested Xreal/Rokid/RayNeo-class board, not a PCB to etch.",
        "If the donor DP board is alive: reuse it whole. If dead: buy a replacement LT7911UXC module or HDMI driver pair.",
    ])
    # USB-C
    b += box(60, 200, 200, 80, "USB-C plug", "from S23  full-featured cable", GOLD)
    b += box(320, 180, 220, 50, "VBUS  5 V", "≥ 1 A  glasses rail")
    b += box(320, 245, 220, 50, "DP lanes", "SSTX/RX  SuperSpeed")
    b += box(320, 310, 220, 50, "USB 2.0 D+/D−", "IMU / buttons / audio")
    b += box(320, 375, 220, 50, "CC1 / CC2", "orientation + DP alt")
    b += box(620, 220, 280, 120, "LT7911UXC", "DP → 2× MIPI-DSI  + EDID", CYAN)
    b += box(980, 160, 340, 80, "LEFT OLED  MIPI", "Sony ECX343E 1920×1200 or ECX334 1024×768", MAG)
    b += box(980, 280, 340, 80, "RIGHT OLED  MIPI", "matched panel  same timing", MAG)
    b += box(620, 400, 280, 90, "MCU", "panel init, brightness, 2D/SBS", GOLD)
    b += box(980, 400, 340, 90, "IMU  6-axis", "BMI / ICM class  USB HID stream", GOLD)
    b += box(620, 540, 280, 70, "open-ear speakers", "optional  DP audio or USB")
    b += box(980, 540, 340, 70, "proximity / wear sensor", "optional  blanks image off-face")

    b += arrow(260, 240, 320, 205)
    b += arrow(540, 270, 620, 270)
    b += arrow(900, 250, 980, 200)
    b += arrow(900, 310, 980, 320)
    b += arrow(760, 340, 760, 400)
    b += arrow(900, 445, 980, 445)

    b += f'<text x="60" y="660" fill="{GOLD}" font-size="15" font-family="DejaVu Sans, sans-serif">USB-C CABLE RULE</text>'
    b += note(60, 684, [
        "A charge-only cable will make the glasses look dead. Must carry SuperSpeed (DP) + 5 V.",
        "Use the donor cable first. Then a marked “4K / 60 / video” USB-C cable. Angle plug toward the temple.",
        "S23: Settings → Connected devices → Samsung DeX → Auto start when HDMI/DP connected = ON for first-light.",
        "Unity later: render side-by-side (SBS) 3840×1080 (or 3840×1200). Long-press donor brightness+ 3 s for 3D/SBS if the MCU supports it.",
    ])
    b += f'<text x="60" y="800" fill="{GOLD}" font-size="15" font-family="DejaVu Sans, sans-serif">IF THE DONOR BOARD IS DEAD</text>'
    b += note(60, 824, [
        "Path B: two HDMI micro-OLED driver boards (ECX334/335/343, 5 V, &lt;2 W each) + USB-C DP to dual-HDMI splitter.",
        "Stereo requires a splitter that can SBS-split or two cloned 2D images (no depth). Prefer a living LT7911 board.",
        "Never power swollen cells. Donor glasses usually have NO pack — host 5 V only. Do not add a pack unless you know BMS.",
    ])
    b += note(60, 930, ["WRLDZ / Project ARGON  ·  SCH-03  ·  functional, not a PCB fab drawing"])
    return svg(w, h, b)


def sheet_mechanical():
    w, h = 1400, 980
    b = title("SCH-04  MECHANICAL  FRAME + ENGINE POCKETS", "Print the frame around MEASURED donor engines. Numbers below are envelopes — caliper your pair.", w)

    b += f'<text x="80" y="140" fill="{GOLD}" font-size="16" font-family="DejaVu Sans, sans-serif">A. FRONT  (looking at wearer)</text>'
    # frame
    b += f'<rect x="120" y="170" width="520" height="160" rx="80" fill="none" stroke="{GOLD}" stroke-width="3"/>'
    b += f'<rect x="160" y="200" width="180" height="100" rx="8" fill="{BOX}" stroke="{CYAN}" stroke-width="2"/>'
    b += f'<text x="250" y="255" fill="{CREAM}" font-size="13" text-anchor="middle" font-family="DejaVu Sans, sans-serif">L pocket</text>'
    b += f'<rect x="420" y="200" width="180" height="100" rx="8" fill="{BOX}" stroke="{CYAN}" stroke-width="2"/>'
    b += f'<text x="510" y="255" fill="{CREAM}" font-size="13" text-anchor="middle" font-family="DejaVu Sans, sans-serif">R pocket</text>'
    b += f'<line x1="250" y1="350" x2="510" y2="350" stroke="{GOLD}" stroke-width="1"/>'
    b += f'<text x="380" y="370" fill="{GOLD}" font-size="12" text-anchor="middle" font-family="DejaVu Sans, sans-serif">IPD 63 ±5 mm (pupil centers)</text>'
    # temples
    b += f'<rect x="40" y="210" width="70" height="24" fill="{BOX}" stroke="{LINE}"/>'
    b += f'<rect x="650" y="210" width="70" height="24" fill="{BOX}" stroke="{LINE}"/>'
    b += f'<text x="75" y="200" fill="{DIM}" font-size="11" text-anchor="middle" font-family="DejaVu Sans, sans-serif">L temple</text>'
    b += f'<text x="685" y="200" fill="{DIM}" font-size="11" text-anchor="middle" font-family="DejaVu Sans, sans-serif">R temple / USB-C</text>'

    b += f'<text x="80" y="430" fill="{GOLD}" font-size="16" font-family="DejaVu Sans, sans-serif">B. ENGINE ENVELOPE  (measure, then pad 0.4 mm)</text>'
    rows = [
        ("Typical sealed birdbath engine", "48 × 28 × 22 mm  W×D×H  (VERIFY)"),
        ("Optical stack depth", "~25 mm  OLED → combiner"),
        ("Eye relief", "12–18 mm  combiner to cornea"),
        ("Temple USB-C", "right or left  angled cable  strain-relief 15 mm"),
        ("Nose bridge", "print 3 heights  8 / 12 / 16 mm"),
        ("Strap points", "ski-goggle pins at temple rear  4 mm holes"),
        ("Clearance to brows", "≥ 6 mm  foam strip"),
    ]
    y = 460
    for name, val in rows:
        b += f'<text x="100" y="{y}" fill="{CREAM}" font-size="13" font-family="DejaVu Sans, sans-serif">{name}</text>'
        b += f'<text x="520" y="{y}" fill="{CYAN}" font-size="13" font-family="DejaVu Sans, sans-serif">{val}</text>'
        y += 28

    b += f'<text x="80" y="700" fill="{GOLD}" font-size="16" font-family="DejaVu Sans, sans-serif">C. PRINT + FIT</text>'
    b += note(100, 728, [
        "1. Caliper both donor engines. They are often NOT identical left/right (cable exits differ).",
        "2. Model pockets in CAD: engine + 0.4 mm slop, 1.5 mm walls, M2 heat-set inserts at 4 corners.",
        "3. Print PETG or ABS (PLA creeps on skin heat). 0.16 mm layers on pockets.",
        "4. First print: open jig (no temples) — confirm IPD and both eyes fuse a white rectangle.",
        "5. Only then add temples, USB-C strain, foam, Millennium-Eye shell cosmetics.",
        "6. Do not glue combiners. Engines drop in and screw down so you can service FPC cables.",
    ])
    b += note(80, 930, ["WRLDZ / Project ARGON  ·  SCH-04  ·  print AFTER donor engines are on the bench"])
    return svg(w, h, b)


def main():
    sheets = {
        "SCH-01_system.svg": sheet_system(),
        "SCH-02_optical.svg": sheet_optical(),
        "SCH-03_electrical.svg": sheet_electrical(),
        "SCH-04_mechanical.svg": sheet_mechanical(),
    }
    for name, data in sheets.items():
        p = OUT / name
        p.write_text(data)
        print("wrote", p)


if __name__ == "__main__":
    main()
