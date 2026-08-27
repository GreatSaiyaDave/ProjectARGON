# Full AR — ARCore 6DOF on the S23

This is the **phone product path**: the room is the arena. You look *through* the S23. Holograms and both Spirit Dueler disks sit on the **real floor** and stay there when you walk. Digital / decline-AR still exists. Combiner HUDs and visor attachments are a later shell, not this.

```
real floor plane (ARCore)
        │
   FloorAnchor ── Arena (midfield, 1:1 m)
        ├── your disk (world-locked, left-near)
        └── opponent disk (world-locked, far)
phone camera = 6DOF window (ARCameraBackground passthrough)
```

Same `DuelEngine` + `ArDuelInteractionSystem` as Editor sim and OpenXR lenses. Presentation only.

## What you get

| | Webcam wallpaper (old) | **Full AR (this)** |
|--|------------------------|---------------------|
| Tracking | None (quad behind holos) | ARCore 6DOF |
| Floor | Fake stage | Detected horizontal plane |
| Disks | Glued to the camera rig | World-locked on the plane |
| Walk around | Holos slide with the phone | Holos stay in the room |
| Passthrough | `WebCamTexture` plane | AR camera background |

Waveguide glasses from e-waste are still not a weekend. This is the spatial graph those glasses will run. OpenXR (`ArLensesSession`) stays for a Quest / Android XR HMD when you have one.

## One-time Unity setup

0. **Exit Play Mode** if the Editor is playing — Unity will not resolve new packages while playing. Wait until `Library/PackageCache` contains `com.unity.xr.arfoundation`. Console should log `WRLDZ_HAS_ARFOUNDATION=ON`.
1. **WRLDZ → Lab → Full AR — Setup ARCore + URP**  
   Assigns the **ARCore** loader on Android (OpenXR remains for Quest) and adds **AR Background Renderer Feature** to `Mobile_Renderer` / `PC_Renderer`.
2. If the menu warns that XR General Settings are missing: **Edit → Project Settings → XR Plug-in Management**, open the **Android** tab once, then rerun the menu.
3. **WRLDZ → Lab → Build APK for S23 Ultra**.

AndroidManifest already marks ARCore **optional** (`com.google.ar.core=optional`) so Digital mode still runs on a device without Play Services for AR.

## Play

1. Overworld → Tear → **ENTER AR** (not Digital).
2. Grant **camera**. Point the S23 at a clear floor / table (~0.25 m² minimum).
3. Status → `ARCORE · 6DOF floor`. Arena and disks snap to that plane at match separation.
4. Walk. Holos stay. Hand cards stay in front of the camera so you can still drag.
5. **DIGITAL** skips ARCore (spatial sim, no tracking) — F2P decline path.

Editor Play stays **EditorSim**. ARCore does not run in the Game view. Iterate layout on PC; prove 6DOF on the S23.

## Code

| Piece | Role |
|-------|------|
| `ArFoundationSession` | AR Session + XR Origin + plane manager + passthrough camera |
| `ArFoundationArmTracker` | Floor-locked disks; camera-relative hand |
| `ArDuelSpace.BuildArFoundationStage` | ENTER AR on device |
| `ArFoundationSetupMenu` | Loader + URP feature |

Fallback: if ARCore is unsupported/failed, we still mounted a spatial stage (black + holos). Webcam wallpaper is no longer the ENTER AR path.

## Heat / FPS (S23)

ARCore + URP + holos. Target 30–60. No MSAA on the AR camera. Close other apps. If the SoC cooks, drop holo fill, not tracking.
