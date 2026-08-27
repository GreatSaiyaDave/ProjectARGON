# Bill of materials — Spirit Lenses v1

Quantities for **one** binocular pair. S23 Ultra is assumed owned.

## A. Harvest (Path A) — steal these

| Qty | Part | Source | Notes |
|-----|------|--------|--------|
| 1 | Matched L+R birdbath engines | Donor glasses | Sealed. Do not split. |
| 1 | DP bridge board (LT7911UXC class) | Same donor | Keep FPC leads |
| 1 | MCU + IMU | Same donor | UV-glued IMU: cut the glue, do not pry the MEMS |
| 1 | USB-C receptacle + temple flex | Same donor | Replace if the port wiggles |
| 1 | Angled USB-C cable | Same donor or buy | Must carry DP + 5 V |

## B. Always buy / print

| Qty | Part | Spec | Why |
|-----|------|------|-----|
| 1 | Full-featured USB-C cable | USB 3.1/3.2, 4K60 video, 3 A | Charge-only cables fake a dead headset |
| 1 | Printed optical jig | PETG, SCH-04 pockets | First-light without temples |
| 1 | Printed frame | PETG/ABS, M2 inserts | After jig proves IPD |
| 8 | M2×6 mm screws + heat-set inserts | brass M2 | Engine retainers |
| 2 | 2 mm EVA foam strips | 8 × 80 mm | Brow / cheek light seal |
| 1 | Ski-goggle strap | 20–25 mm | Better than temples for a prototype |
| 1 | USB-C strain boot | 8 mm ID silicone | Port is the #1 failure |
| 1 | Isopropyl 99% + lint-free wipes | — | Combiners fingerprint forever |

## C. Path B only (no living DP board)

| Qty | Part | Spec | Notes |
|-----|------|------|--------|
| 2 | Sony micro-OLED + driver | ECX334 1024×768 **or** ECX343 1920×1200 | Buy **with** board. 5 V, &lt;2 W each |
| 1 | USB-C DP → dual MIPI/HDMI | LT7911UXC module | Stereo SBS. Do not use two random HDMI dongles |
| 2 | Birdbath optic kits | 0.39–0.7" class, 45° PBS | Must match panel size |

Approximate street: engines from a cracked donor often cheaper than two new ECX kits.

## D. Optional v1.1

| Qty | Part | Spec |
|-----|------|------|
| 1 | BMI270 breakout + ESP32-S3 | If donor IMU is dead |
| 1 | 5 V / 2 A USB-C PD tap | If the S23 browns out (rare at 1 A) |
| 2 | SLAM cameras | Only if you harvested an Air 2 Ultra |

## E. Tools

Calipers, Phillips #00 / #000, plastic spudgers, Kapton tape, hot-air at 80–100 °C for UV glue (not for combiners), USB-C breakout **optional**, S23, a dark room.

## Power budget (typical donor)

| Rail | Draw |
|------|------|
| Two micro-OLEDs + bridge | 0.7–1.5 W |
| MCU + IMU + speakers | 0.2–0.5 W |
| Host | S23 VBUS 5 V / ~1 A spec on XREAL-class glasses |

No extra pack in v1. If you add one later: protected 1S Li-ion + BMS, never a loose cell in a temple.
