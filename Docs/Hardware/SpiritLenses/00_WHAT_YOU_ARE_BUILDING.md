# WRLDZ Spirit Lenses — what you are actually building

Custom **binocular optical-see-through AR glasses**. Two sealed birdbath engines, one USB-C DisplayPort pipe from the **Galaxy S23 Ultra**. The S23 is the computer. The glasses are display + IMU + optics.

This is a **hardware** job: salvage, measure, print, assemble, first light. Unity is only the image you shine into the engines after the optics work.

## What this is

| | |
|--|--|
| Form | Wearable glasses, two eyes |
| Optics | Birdbath (micro-OLED + 45° polarizing beam splitter + curved combiner) |
| World | Real glass. Black pixels on OLED are off, so holos float in the room |
| FOV | About 45–52° diagonal — cinema-in-sunglasses, not Hololens-wide |
| Tracking v1 | 3DoF from the glasses IMU (or a brow IMU) |
| Compute | S23 Ultra USB-C **DisplayPort Alt Mode** + 5 V / ~1 A |
| Look | Your printed frame (Millennium Eye / Spirit Dueler shell is cosmetics on a proven optical pair) |

## What this is not

- A phone taped to sunglasses
- A cardboard monocular HUD
- A waveguide you grind in a garage (you cannot)
- Gutting the S23
- Ripping a phone MIPI panel off its board

Karl Guttag’s Nreal Light teardown: the whole birdbath stack is about **25 mm** deep and the combiner throws away **~70–75%** of world light. That is why every birdbath pair looks like sunglasses. Physics, not a style choice.

## The only garage-viable “full AR” optic

**Harvest complete left + right birdbath engines from one donor pair of AR glasses.** Alignment inside an engine is the product. You do not rebuild the PBS/combiner sandwich unless the engine is already trash.

Donor class (all the same optical family):

- XREAL Air / Air 2 / Air 2 Pro / One
- Rokid Max / Max 2
- RayNeo Air 2
- VITURE One / Pro / Luma
- Nreal Light (older, excellent teardown literature)

XREAL Air 2: Sony **ECX343E** 1920×1200 per eye, **LT7911UXC** DP-to-dual-MIPI, MCU, IMU. Host (your S23) supplies video + 5 V. No pack in the glasses.

XREAL Air 2 Ultra additionally has **dual SLAM cameras + a small SoC** — steal that pair if you want 6DoF later. v1 does not need it.

## Two salvage paths

**Path A — harvest (do this):** dead or cracked donor glasses. Engines + DP board + IMU + USB-C temple. Print a new frame around the engines.

**Path B — modules (only if Path A fails):** buy 2× Sony ECX334/343 micro-OLED + HDMI/MIPI driver boards + a birdbath optic kit (or engines pulled from a second broken pair). Harder. Stereo SBS needs a DP splitter (LT7911 class), not two random HDMI boards.

**Path C — not glasses:** a dead Quest 3 is video-passthrough full AR, not a lens. Different product. Ignore unless you want a helmet.

## Order of work (do not skip)

1. **Hunt the donor pair.** Do not print a frame first.
2. **First light in the donor frame** with the S23. Prove both eyes and DP.
3. **Caliper the engines.** SCH-04 envelopes are typical, not gospel.
4. **Open jig** (no temples): both eyes fuse a white rectangle.
5. **Printed frame + temples + strain relief.**
6. **IMU + Unity SBS.** Cosmetics last.

Schematics: `schematics/SCH-01` … `SCH-04`.  
Hunt: `02_SALVAGE_HUNT.md`.  
Parts: `03_BOM.md`.  
Hands: `04_ASSEMBLY.md`.  
Image into Unity: `05_BRINGUP.md`.
