# Bring-up with the S23 and Unity

Optics first. Software second. If the white rectangle does not fuse, Unity will not save it.

## Phone

Galaxy S23 Ultra: USB-C **DisplayPort Alt Mode** is native. XREAL-class glasses want **5 V / 1 A** and DP video from the host.

1. Full-featured cable (not charge-only).
2. **Settings → Connected devices → Samsung DeX → Auto start when HDMI connected.**
3. Plug glasses. DeX desktop in both eyes = electrical + optical path is alive.
4. Brightness keys on the temple: 2D vs SBS/3D (model-specific; XREAL Air 2: brightness+ hold ~3 s, one beep).

## Unity image

Birdbaths want **black = off**. Holos on a true black clear.

| Mode | Resolution (typical donor) | Notes |
|------|----------------------------|--------|
| 2D clone | 1920×1080 or 1920×1200 | Same image both eyes. Fine for first UI |
| SBS stereo | 3840×1080 or 3840×1200 | Left | Right. Depth. This is the duel |

Project ARGON already has an OpenXR lenses path and a salvage-OST path. For **these** glasses the S23 is a **DP source**:

- Easiest first light: DeX, then a Unity Android build that targets the **external display** (the glasses enumerate as a monitor).
- Stereo: render SBS to that monitor. If the MCU is in 3D mode it already splits; if it is in 2D clone, you must switch the glasses to SBS.
- Tracking: v1 use the glasses IMU (USB HID) or the S23 gyro only if the phone is strapped to the head (it should not be — it is in the pocket). Pocket gyro is **wrong** for head pose. Wire the donor IMU or a BMI270 on the frame.

Do not use the phone camera as “passthrough.” The combiners **are** the world.

## Pass/fail

| Check | Pass |
|-------|------|
| DeX visible both eyes | Electrical + engines |
| White field fused at 2 m | IPD / toe |
| Black field, world still visible | Combiners not too silvered |
| Holos sit in the room when you turn your head (IMU) | Tracker rigid to engines |
| Walk; holos do not split | Mechanical lock |

## 6DoF later

Harvest SLAM cameras from an Air 2 Ultra, or run ARCore **on the S23** only as a **map/GPS companion**, not as the glasses view. Putting ARCore passthrough into birdbaths double-draws the world and looks like a visor. Stay optical-see-through.
