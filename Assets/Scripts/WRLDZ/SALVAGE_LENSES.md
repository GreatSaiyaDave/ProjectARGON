# Salvage AR lenses — optical see-through

**This is the hardware path.** Not a phone visor. Not Quest. You strip **donor devices**, keep the **S23 Ultra intact**, and build a **Millennium Eye**: one combiner in front of one eye, Unity holos shining into it.

Optical see-through means the real world is **glass**, not a camera. Black pixels on an OLED fall into the combiner and vanish. That is why the Unity salvage mode renders **black + holograms only**.

```
                    donor AMOLED (screen down)
                            │
                      convex lens  f ≈ 50 mm
                            │
        world  ────────  45° combiner  ────────  your eye
                     (50/50 glass or film)
```

Do **not** gut the Galaxy S23 Ultra. It is the computer, the IMU (if it sits on the head), and the daily phone-AR lab.

---

## What you are actually building

A **monocular HUD + spatial duel window**, Google-Glass class, Yu-Gi-Oh Millennium Eye shell later.

| What salvage can do | What it cannot do |
|---------------------|-------------------|
| See-through holos in one eye | Waveguide glasses from e-waste |
| ~20–30° FOV duel window | Quest-wide FOV without a donor birdbath |
| 1:1 floor arena via gyro | Inside-out SLAM (that stays on the S23 later) |
| Life-size disks in that window | Two-eye stereo until you salvage a *second* identical optic |

Stereo is a v2 when you have two combiners. Start with **one eye**.

---

## Hunt list (steal parts, ranked)

### Never take apart

- Galaxy S23 Ultra
- Swollen lithium packs
- A phone **panel** ripped off its board (MIPI without a driver is scrap)

Keep donor phones **assembled**. The whole phone is the display module.

### Tier S — steal a finished optical engine

These *are* AR lenses. Dead is fine. You want the **birdbath + micro-OLED**, not the firmware.

| Donor | What you pull |
|-------|----------------|
| Xreal Air / Light, Rokid, RayNeo, Nreal | Birdbath combiners + 0.39–0.7" OLED + USB-C DP board |
| DJI / Fat Shark / Walksnail FPV goggles | Microdisplays + aspheres (HDMI). Combiner is extra. |
| Camera EVF (Sony/Canon/Panasonic) | Tiny OLED + magnifier |

If you find **any** dead USB-C AR glasses, stop hunting. That is the product. S23 DeX / DisplayPort alt-mode can drive many of them.

### Tier A — common e-waste (this is the default build)

| Donor | What you pull | Why |
|-------|----------------|-----|
| **Old Samsung AMOLED phone that still boots** | Whole phone | Black pixels = actually off = see-through. IMU + Android + this APK. |
| Gear VR, Daydream, Cardboard, PSVR | Aspheric / fresnel lenses | Eyepiece. ~40–50 mm FL is the sweet spot. |
| Broken binoculars / jeweler loupe / toy microscope | Plano-convex lens | Same job if VR lenses are missing. |
| Car HUD, teleprompter glass, two-way mirror | Combiner | Real 50/50 glass beats film. |
| Cheap sunglasses / safety glasses / ski goggles | Frame + strap + foam | Wearable shell. |
| Old Bluetooth gamepad | Input | You cannot tap a phone that is face-down in the optic. |

**AMOLED > LCD.** An LCD donor stays milky gray in the combiner. Samsung Galaxy S7–S10 / Note / A-series AMOLED that still power on are the prize.

### Tier B — $10–30 if the drawer is empty

- 40×30 mm **plate beam splitter** 70T/30R or 50/50 (Ali / surplus)
- Ø25–40 mm **plano-convex** lens, **f = 50 mm**
- Window **mirror film** (one-way) + 2 mm clear acrylic
- Elastic ski strap, packing foam

### Tier C — sensors only if the display is a *dumb* panel

If the glasses are USB-C DP (no IMU of their own) and the S23 lives in a pocket, head tracking dies. Then salvage:

- A second dead phone **taped to the brow** running gyro (or this APK)
- MPU-6050 / BMI160 out of a drone, hoverboard, or Nintendo wreck + Bluetooth serial

Until that exists, **the phone that is rendering must sit on your head.**

---

## Default recipe (cardboard tonight, print later)

You need **three** optical parts and a tray.

1. **Display** — S23 (lab) or donor AMOLED, screen facing the lens.
2. **Lens** — plano-convex, f ≈ 50 mm, ~35 mm diameter. From Gear VR / loupe.
3. **Combiner** — 50×70 mm acrylic + mirror film, or real beam-splitter glass, at **45–50°**.

### Why 50 mm

Virtual image at a 2.5 m duel stand:

\[
\frac{1}{u} = \frac{1}{f} + \frac{1}{2.5\,\mathrm{m}} \approx \frac{1}{50\,\mathrm{mm}}
\]

Display-to-lens distance **u ≈ 49 mm**. Put the lens almost at the focal length. Slide ±5 mm until the holos sit in the room, not on the glass.

### Layout (right eye, Millennium Eye)

```
 TOP
  ┌──────────────────────┐
  │  phone, screen DOWN  │  S23 163.4 × 78.1 × 8.9 mm
  └──────────┬───────────┘
             │  ~49 mm
          [ lens ]
             │  ~10 mm
      ╱  combiner 45°  ╲     ← eye looks through this
     ╱                  ╲
  WORLD                EYE (20–25 mm relief)
```

**Camera window is irrelevant** on this path. You are not doing video passthrough.

### Cardboard bench (no printer)

Corrugated or foamcore:

| Piece | Size | Notes |
|-------|------|--------|
| Base | 180 × 120 mm | |
| Phone rails | 168 × 82 mm inner | S23 landscape, screen down, 2 mm slop |
| Lens baffle | hole Ø lens + 1 mm | Centered under the screen, 49 mm below glass |
| Combiner slot | 2–3 mm kerf at 45° | 50 × 70 mm pane |
| Eye hood | optional | Blocks stray room light so blacks stay black |

Build order: **lens focus first** (look at a white rectangle on the phone). Then add the combiner. Then turn on salvage mode (black + holos).

---

## Unity: what to shine into the glass

Menu: **WRLDZ → Lab → Salvage Lenses (optical see-through)**

| Behavior | Why |
|----------|-----|
| Black clear, no webcam | Combiner = real world. Camera would double the scene. |
| Letterboxed **optical window** | Only the pixels the combiner actually sees. Rest of the OLED stays black. |
| Gyro head tracking | Phone is on your head in this recipe. |
| 1:1 floor + arm disks | Same `ArDuelSpace` graph as OpenXR. |
| Volume down = recenter | Forward = arena. Volume up reserved for confirm later. |
| Landscape while active | Bench mount. Overworld returns to portrait on exit. |

Editor: mouse-look stereo-free preview (RMB). `[` `]` shrink/grow the optical window, arrows nudge it, **P** saves.

On device the same APK: enable salvage, start a practice duel, drop the phone in the tray.

---

## Wiring / power

- **S23 in the tray:** one device. Cable out the crown (USB-C toward temple). Power bank on a belt if you duel long.
- **Donor phone in the tray:** install the same APK on the donor. S23 stays in the pocket for map / companion later.
- **USB-C AR glasses (Tier S):** S23 DeX / DP alt-mode into the glasses. IMU must still be on the head (glasses IMU or a brow-phone).

Do not run two Unity copies fighting one match. One renderer.

---

## What “done” looks like on the bench

1. White full-screen → you see a floating rectangle in the room through the combiner.
2. Focus the lens until the rectangle is sharp at arm’s length.
3. Salvage mode on → rectangle goes black, holos hang in the room.
4. Turn your head → arena stays world-locked after a recenter.
5. Opponent disk sits about 2.5 m in front of you (match separation).

If holos are pasted on the glass, the lens is too close or too far. If the world is too dark, the combiner is too mirrored — swap 50/50 for 70T/30R or peel a layer of film. If blacks glow gray, the donor is LCD — find AMOLED.

---

## After the bench

1. 3D-print the tray (`Tools/WRLDZ/salvage/optical_bench.py` → STL).
2. Wrap a sunglasses frame / ski strap around the same optic. Millennium Eye shell is cosmetics on a proven path.
3. Second eye only when the first eye is sharp.
4. OpenXR / Quest path in this repo stays for a real HMD. Salvage does not replace it.

---

## Safety

- No cracked combiners at eye relief. Tape edges.
- No swollen cells.
- Brightness: OLED in a combiner can still be punchy. Start dim, outdoors later.
- You have **one eye** on holos and one on the world. That is intended. Do not walk in traffic debugging FOV.
