# Salvage hunt

Goal: **one matched binocular birdbath pair** plus a board that speaks USB-C DisplayPort. The S23 Ultra already does DP Alt Mode (Galaxy S series, DeX).

## Never take apart

| Item | Why |
|------|-----|
| Galaxy S23 Ultra | Compute, GPS, battery, lab phone |
| Swollen lithium packs | Fire |
| A phone OLED ripped off its board | MIPI with no driver is scrap |
| A sealed birdbath you have not proven dead | Alignment is unrecoverable if you split it |

## Search language (copy/paste)

eBay / Craigslist / FB Marketplace / Ali “parts”:

```
Xreal Air for parts
Nreal Light cracked
Xreal Air 2 broken hinge
Rokid Max for parts
RayNeo Air 2 not working
Viture One cracked lens
AR glasses birdbath module
Sony ECX343  micro OLED
```

Prefer **cracked frame / broken hinge / dead USB-C port** with **unscratched combiners**. Optics live, plastic dead = jackpot.

Avoid **sun-damaged** combiners (white spots, rainbow burns). Birdbaths focus sunlight onto the OLED and cook it. Ask the seller for a photo through the lens against a window.

## What to pull from a donor (Path A)

Work over a towel. Phillips #00. Photos before every step.

| Keep as a unit | Leave / recycle |
|----------------|-----------------|
| Left birdbath engine (OLED + PBS + combiner) | Cosmetic shells, once measured |
| Right birdbath engine | Speakers if you do not want audio |
| DP board (LT7911UXC or sibling) + FPC leads | Broken USB-C receptacle (replace later) |
| IMU daughterboard (often UV-glued to a metal frame) | Nose pads (print better ones) |
| MCU board if it talks to the IMU | Dead battery (most of these glasses have none) |
| Angled USB-C cable that came with it | |
| Temples only if hinges still torque | |

**Do not separate OLED from PBS from combiner** unless that engine will never show an image. If one eye is dead, you can still use the surviving engine to learn, but binocular AR needs a matched pair — hunt a second identical model.

## Donor quality ranking

| Rank | Donor | Why |
|------|--------|-----|
| S | XREAL Air 2 / One, Rokid Max 2, RayNeo Air 2 with DP board alive | Engines + LT7911 + IMU, S23 talks to them today |
| A | Same family, DP board dead, engines intact | Path B drivers; keep engines sealed |
| A | XREAL Air 2 Ultra (even dead SoC) | Extra: stereo SLAM cameras for 6DoF later |
| B | Nreal Light | Same birdbath physics; older connectors |
| C | FPV goggles (DJI, Fat Shark) | Displays + aspheres, **not** see-through until you add combiners |
| F | Random sunglasses + phone | Attachment. Not this build. |

## Field test before you buy (ask the seller)

1. Photo of **both combiners** (no pits, no sun spots).
2. Does USB-C **wiggle**? Loose port is repairable; mention it so the price drops.
3. Power it from a laptop USB-C DP port if they still can: both eyes, any image.
4. Model name on the inner temple. **Left and right must be the same model.**

## If the hunt is dry (Path B shopping)

Buy modules, still as **pairs**:

- 2× Sony ECX334C 0.39" 1024×768 **or** ECX343-class 1920×1200, **with** HDMI or MIPI driver board (Tindie “Display Components”, Taobao ECX kits, Manollo Mancelli / ECX Engine community boards).
- 2× birdbath optical engines sold as “AR glasses optical module” (measure before you click; many are monocular HUD toys).
- 1× USB-C DP to dual-MIPI or dual-HDMI board (LT7911UXC class). Two independent HDMI boards give you **cloned 2D**, not stereo, unless you SBS-split in software **and** the boards accept 1080×1920 halves — ugly. Prefer one bridge.

## Sensors (only if the donor IMU is dead)

| Part | Role | Where |
|------|------|--------|
| BMI270 / ICM-42688 breakout | 6-axis at 200–400 Hz | Temple, rigid to the engines |
| ESP32-S3 | USB-HID or BLE quaternion | Same temple |
| Optional: OV5647 pair | 6DoF later | Forward baffles, 6–8 cm baseline |

v1: one IMU, rigid to the optical bench. If the IMU flexes relative to the combiners, the holos swim.

## Shop pass (this weekend)

1. Local listings for the search language above.
2. eBay sold prices so you do not overpay for a working pair (working is nicer but you are here for **parts**).
3. Order a **known-good USB-C DP cable** now (the donor cable may be missing). Marked 4K/60 video, not charge-only.
4. Do not order a 3D-printed frame until engines are in your hands.
