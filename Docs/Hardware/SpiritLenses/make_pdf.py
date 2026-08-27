#!/usr/bin/env python3
"""Spirit Lenses printable build guide."""
from pathlib import Path
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor, white, black
from reportlab.lib.enums import TA_LEFT, TA_CENTER
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Image, PageBreak, Table, TableStyle, ListFlowable, ListItem, KeepTogether,
)
from reportlab.lib.utils import ImageReader

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "Spirit_Lenses_Build_Guide.pdf"
NAVY = HexColor("#0B1220")
GOLD = HexColor("#C9A227")
INK = HexColor("#1A1A1A")
RULE = HexColor("#C9A227")

styles = getSampleStyleSheet()
styles.add(ParagraphStyle("CoverH", parent=styles["Title"], fontName="Times-Bold", fontSize=22, textColor=NAVY, leading=26, alignment=TA_CENTER, spaceAfter=8))
styles.add(ParagraphStyle("CoverS", parent=styles["Normal"], fontName="Times-Italic", fontSize=12, textColor=HexColor("#444"), alignment=TA_CENTER, spaceAfter=6))
styles.add(ParagraphStyle("H", parent=styles["Heading1"], fontName="Times-Bold", fontSize=16, textColor=NAVY, spaceBefore=12, spaceAfter=8))
styles.add(ParagraphStyle("H2", parent=styles["Heading2"], fontName="Times-Bold", fontSize=13, textColor=NAVY, spaceBefore=10, spaceAfter=6))
styles.add(ParagraphStyle("B", parent=styles["BodyText"], fontName="Times-Roman", fontSize=10, textColor=INK, leading=14, spaceAfter=6))
styles.add(ParagraphStyle("Cell", parent=styles["BodyText"], fontName="Times-Roman", fontSize=8, textColor=INK, leading=11))
styles.add(ParagraphStyle("CellH", parent=styles["BodyText"], fontName="Times-Bold", fontSize=8, textColor=NAVY, leading=11))
styles.add(ParagraphStyle("Cap", parent=styles["Normal"], fontName="Times-Italic", fontSize=9, textColor=HexColor("#555"), alignment=TA_CENTER, spaceBefore=4, spaceAfter=10))
styles.add(ParagraphStyle("Foot", parent=styles["Normal"], fontName="Times-Roman", fontSize=8, textColor=HexColor("#666")))


def P(text, style="B"):
    return Paragraph(text, styles[style])


def cell(text, header=False):
    return Paragraph(text, styles["CellH"] if header else styles["Cell"])


def table(rows, widths):
    data = []
    for i, row in enumerate(rows):
        data.append([cell(c, header=(i == 0)) for c in row])
    t = Table(data, colWidths=widths, repeatRows=1)
    t.setStyle(TableStyle([
        ("GRID", (0, 0), (-1, -1), 0.4, HexColor("#999")),
        ("BACKGROUND", (0, 0), (-1, 0), HexColor("#E8D48B")),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 4),
        ("RIGHTPADDING", (0, 0), (-1, -1), 4),
        ("TOPPADDING", (0, 0), (-1, -1), 3),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
    ]))
    return t


def fig(name, caption, max_w=7.2 * inch, max_h=8.6 * inch):
    path = ROOT / "export" / name
    ir = ImageReader(str(path))
    iw, ih = ir.getSize()
    scale = min(max_w / iw, max_h / ih)
    img = Image(str(path), width=iw * scale, height=ih * scale)
    img.hAlign = "CENTER"
    return KeepTogether([img, P(caption, "Cap")])


def header_footer(canvas, doc):
    canvas.saveState()
    canvas.setFillColor(NAVY)
    canvas.rect(0, letter[1] - 28, letter[0], 28, fill=1, stroke=0)
    canvas.setFillColor(GOLD)
    canvas.setFont("Times-Bold", 9)
    canvas.drawString(0.7 * inch, letter[1] - 18, "WRLDZ SPIRIT LENSES  ·  HARDWARE BUILD GUIDE")
    canvas.setFillColor(NAVY)
    canvas.rect(0, 0, letter[0], 28, fill=1, stroke=0)
    canvas.setFillColor(white)
    canvas.setFont("Times-Roman", 8)
    canvas.drawString(0.7 * inch, 12, "Project ARGON  ·  salvage birdbath engines  ·  S23 Ultra compute")
    canvas.drawRightString(letter[0] - 0.7 * inch, 12, f"p. {doc.page}")
    canvas.restoreState()


def story():
    s = []
    s += [Spacer(1, 1.6 * inch), P("WRLDZ / PROJECT ARGON", "CoverS"),
          P("Spirit Lenses", "CoverH"),
          P("Custom binocular AR glasses from salvaged birdbath engines", "CoverS"),
          P("Schematics · salvage hunt · bill of materials · assembly", "CoverS"),
          Spacer(1, 0.4 * inch),
          P("The Galaxy S23 Ultra is the computer. Do not gut it. The glasses are a stereo optical-see-through display plus IMU, built around sealed engines harvested from donor AR glasses (XREAL / Rokid / RayNeo / VITURE class).", "B"),
          P("Waveguides cannot be made in a garage. A phone taped to sunglasses is not this build. Alignment inside a birdbath engine <b>is</b> the product — keep engines sealed.", "B"),
          PageBreak()]

    s += [P("1. What you are building", "H"),
          P("Binocular optical-see-through AR glasses. Two birdbath engines (micro-OLED + 45° polarizing beam splitter + curved combiner), one USB-C DisplayPort pipe from the S23 Ultra. Black OLED pixels are off, so holograms float in the real room. Field of view is about 45–52° diagonal. The combiner throws away roughly 70–75% of world light (Karl Guttag / Nreal Light teardown) — they look like sunglasses because of physics, not fashion.", "B"),
          P("Typical donor internals (XREAL Air 2): Sony ECX343E 1920×1200 per eye, Lontium LT7911UXC DP-to-dual-MIPI, MCU, IMU. Host supplies 5 V / ~1 A and video. Most of these glasses have no battery.", "B"),
          P("2. Order of work (do not skip)", "H2")]
    s.append(ListFlowable([
        ListItem(P("Hunt one matched donor pair. Do not print a frame first.", "B")),
        ListItem(P("First light in the donor frame with the S23. Prove both eyes and DisplayPort.", "B")),
        ListItem(P("Caliper the engines. Schematic envelopes are typical, not gospel.", "B")),
        ListItem(P("Print an open jig (no temples). Fuse a white rectangle in both eyes.", "B")),
        ListItem(P("Print the frame, temples, strain relief. Wire. On-head first light.", "B")),
        ListItem(P("IMU + Unity side-by-side. Cosmetics last.", "B")),
    ], bulletType="1", leftIndent=18))
    s += [P("3. Paths", "H2")]
    s.append(table([
        ["Path", "When", "What you steal / buy"],
        ["A  Harvest", "You found cracked XREAL / Rokid / RayNeo / VITURE", "Sealed L+R engines + DP board + IMU + cable"],
        ["B  Modules", "No living DP board", "2× Sony ECX334/343 + drivers + LT7911-class bridge + birdbath kits"],
        ["C  Not this", "Dead Quest 3", "Video-passthrough helmet. Different product."],
    ], [1.3*inch, 2.4*inch, 3.5*inch]))
    s += [PageBreak(), P("Schematic SCH-01 — system architecture", "H"),
          fig("SCH-01_system.png", "SCH-01  ·  S23 is compute. Glasses are display + IMU + optics.")]
    s += [PageBreak(), P("Schematic SCH-02 — optical path", "H"),
          fig("SCH-02_optical.png", "SCH-02  ·  One-eye birdbath (side) and binocular IPD plan. Stack depth ≈ 25 mm.")]
    s += [PageBreak(), P("Schematic SCH-03 — electrical (functional)", "H"),
          fig("SCH-03_electrical.png", "SCH-03  ·  Not a PCB to etch. Reuse a living donor board if you have one.")]
    s += [PageBreak(), P("Schematic SCH-04 — mechanical", "H"),
          fig("SCH-04_mechanical.png", "SCH-04  ·  Print after the engines are on the bench. Caliper your pair.")]

    s += [PageBreak(), P("4. Salvage hunt", "H"),
          P("Search copy-paste: <i>Xreal Air for parts · Nreal Light cracked · Rokid Max for parts · RayNeo Air 2 not working · Viture One cracked lens · AR glasses birdbath module</i>. Prefer cracked frame / dead USB-C with unscratched combiners. Sun-spotted combiners are trash — birdbaths focus sunlight onto the OLED.", "B"),
          P("Never: S23 Ultra, swollen cells, a phone panel ripped off its board, a sealed engine you have not proven dead.", "B")]
    s.append(table([
        ["Keep as a unit", "Notes"],
        ["Left + right birdbath engines", "OLED+PBS+combiner sealed. Matched pair from one model."],
        ["DP board (LT7911UXC class) + FPC leads", "Label L / R. Do not swap."],
        ["MCU + IMU", "IMU often UV-glued to metal. Cut glue; do not pry MEMS."],
        ["Angled USB-C cable", "Must carry SuperSpeed DP + 5 V. Charge-only cables fake a dead headset."],
        ["USB-C temple flex", "Replace the receptacle if it wiggles."],
    ], [2.6*inch, 4.6*inch]))
    s += [P("Field test before you buy: photo of both combiners; USB-C wiggle; any image on a laptop DP port; same model left and right.", "B"),
          P("5. Bill of materials (one pair)", "H")]
    s.append(table([
        ["Qty", "Part", "Source"],
        ["1", "Matched L+R birdbath engines", "Donor glasses"],
        ["1", "DP bridge + MCU + IMU", "Same donor"],
        ["1", "Full-featured USB-C cable (4K60 video)", "Donor or buy — not charge-only"],
        ["1", "Printed open jig, then frame (PETG/ABS, M2 inserts)", "After calipers"],
        ["8", "M2×6 mm screws + brass heat-set inserts", "Buy"],
        ["2", "EVA foam strips 8×80 mm", "Brow / cheek light seal"],
        ["1", "Ski-goggle strap 20–25 mm + USB-C strain boot", "Prototype retention"],
        ["2*", "Sony ECX334/343 + driver (Path B only)", "Tindie / Taobao ECX kits"],
        ["1*", "LT7911UXC-class USB-C DP → dual MIPI/HDMI (Path B)", "If donor board is dead"],
    ], [0.6*inch, 4.2*inch, 2.4*inch]))
    s += [P("* Path B only. Two random HDMI boards clone 2D; they do not give stereo unless a single bridge SBS-splits.", "B"),
          P("Power: two OLEDs + bridge typically 0.7–1.5 W; MCU/IMU/speakers 0.2–0.5 W. Host VBUS 5 V / ~1 A. No extra pack in v1.", "B")]

    s += [P("6. Assembly", "H"),
          P("Dark room. No sunlight on combiners. Tape glass edges. Do not stare at max-brightness OLED at 20 mm.", "B"),
          P("First light in the donor frame", "H2"),
          P("S23: Settings → Connected devices → Samsung DeX → Auto start when HDMI connected = ON. Full-featured cable, angled plug into the temple. Both eyes should show DeX. Brightness+ hold ~3 s on many XREAL/Rokid units toggles 3D/SBS — leave 2D for this step. If black: another cable, a laptop DP port, the donor cable. Photograph through each combiner.", "B"),
          P("Teardown", "H2"),
          P("Photo every screw. Temples off; support the USB-C flex. Rokid Max glues the outer lens — heat + alcohol, do not pry the combiner. Each engine: typically 3× Phillips plus a glue bead. Stop when it is a sealed brick with an FPC pigtail. Label L/R. Caliper W, D, H, FPC exit. Those six numbers override SCH-04.", "B"),
          P("Open jig, then frame", "H2"),
          P("Pockets at 63 mm IPD, 0.4 mm slop, M2 inserts. White full-screen. You want one floating rectangle, not two. Slide IPD ±2 mm, toe-in 1° if double. Do not live with a split image. USB-C on the same temple the donor used, 15 mm service loop, strain boot. IMU rigid to the engines, not a floppy temple tip. PETG, not PLA (creeps on skin heat). Strap for the prototype. Cosmetics last.", "B"),
          P("7. Bring-up (S23 + Unity)", "H"),
          P("Optics first. If the white rectangle does not fuse, software will not save it. DeX in both eyes = electrical path alive. Unity: black clear (holos only). 2D clone 1920×1080/1200 for first UI; SBS 3840×1080/1200 for depth. Pocket gyro is the wrong pose — the S23 is in a pocket. Use the glasses IMU (USB HID) or a BMI270 on the frame. Do not pipe phone-camera passthrough into birdbaths; the combiners are the world.", "B"),
          P("Pass: DeX both eyes; white field fused at 2 m; black field still shows the room; holos stay fused when you turn your head; walk without a split image.", "B"),
          P("6DoF later: SLAM cameras off an XREAL Air 2 Ultra, or keep ARCore on the S23 as a map companion only.", "B"),
          Spacer(1, 12),
          P("Rebuild drawings: python3 schematics/make_schematics.py then inkscape export. Sources: Guttag Nreal Light birdbath (~25 mm stack, ~70–75% world-light loss); XREAL Air 2 teardown (ECX343E, LT7911UXC, 5 V host); S23 Ultra USB-C DisplayPort Alt Mode / DeX.", "B")]
    return s


def main():
    # regenerate pngs if script asked
    doc = SimpleDocTemplate(
        str(OUT), pagesize=letter,
        leftMargin=0.7*inch, rightMargin=0.7*inch,
        topMargin=0.6*inch, bottomMargin=0.5*inch,
        title="WRLDZ Spirit Lenses — Hardware Build Guide",
        author="Project ARGON",
    )
    doc.build(story(), onFirstPage=header_footer, onLaterPages=header_footer)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
