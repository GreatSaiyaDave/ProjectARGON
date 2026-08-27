# Lenses XR setup — Quest 3 life-size duels

**Product path:** AR lenses / OpenXR HMD.  
**Phone camera does not validate arm anchoring or life-size scale.**

This project uses **one duel graph** (`ArDuelSpace` → `ArDuelInteractionSystem`).  
On Quest / OpenXR it switches to **1:1 meters**, **floor-anchored arena**, and **left-controller arm disk**.

---

## What you need

| Item | Role |
|------|------|
| **Meta Quest 3** (or 3S) | Primary lenses lab |
| Unity 6 + Android Build Support | Already on this project |
| OpenXR packages | Already in `Packages/manifest.json` |
| USB / wireless ADB | Deploy APK to headset |

Optional later: **Meta XR All-in-One SDK** for full-color **passthrough** underlay.  
Without it you still get **life-size spatial disks + floor arena** in the HMD (dark clear + holos).

---

## Project Settings (once)

1. **Edit → Project Settings → XR Plug-in Management**  
   - **Android** tab: enable **OpenXR**  
   - **PC** tab (optional Link / editor sim): OpenXR  

2. **XR Plug-in Management → OpenXR**  
   - **Meta Quest Support** (Android) — enable  
   - **Oculus Touch Controller Profile** — enable  
   - **Meta Quest Touch Plus** — enable (Quest 3)  

3. **Player → Android**  
   - Scripting backend: **IL2CPP**  
   - Target architectures: **ARM64**  
   - Minimum API: 29+ (Quest)  
   - Orientation: Landscape is fine on HMD (headset owns view)  

4. **Build Settings**  
   - Platform: **Android**  
   - Scenes: Boot → Overworld → MainMenu → DuelSlice  

Menu helper: **WRLDZ → Lab → Open Lenses XR Setup Notes**

---

## Runtime architecture

```
ArLensesSession (DontDestroyOnLoad)
  XR Origin (1:1 m)
    Main Camera   ← CenterEye
    Left Controller  ← player disk
    Right Controller
    FloorAnchor
      ArenaAnchor (midfield at SeparationMeters/2)
      ArStage_Lenses
        PlayerArmDiskRig  ← XrWorldArmTracker
        OppArmDiskRig     ← synthetic or right controller
        Arena holograms
```

| Piece | Class |
|-------|--------|
| Session / origin | `ArLensesSession` |
| 1:1 arm tracker | `XrWorldArmTracker` |
| Bootstrap | `ArLensesBootstrap` |
| Stage branch | `ArDuelSpace.BuildLensesStage` |
| Interaction | `ArDuelInteractionSystem` (`LifeSize = true`) |

**Separation:** same `ArDuelMatchConfig.SeparationMeters` as overworld create — applied as **real meters** (not phone stage units).

**Deploy:** disks start **retracted**; `BindEngine` / lenses bind plays **BladeDeploy**.

---

## How to test on Quest 3

1. Build Android APK (IL2CPP ARM64) with OpenXR enabled.  
2. `adb install -r Builds/Android/...apk`  
3. Put headset on · launch app · log in · Overworld.  
4. **VS AI** or Tear → Zone Mode **ENTER AR**.  
5. You should see:  
   - Floor field in front of you  
   - **Left controller** wears your disk (retract → deploy)  
   - Opponent disk at measured distance along look-forward  
   - Large arena cards at midfield  

6. Logcat: filter `WRLDZ Lenses` / `WRLDZ AR`.

---

## Editor without headset

- Default remains **EditorSim** / phone RT (rules + UI).  
- **WRLDZ → Lab → Run Simulated AR Duel Smoke** still validates dual field without XR.  
- Life-size arm tracking **requires** the HMD (or XR Device Simulator — optional later).

---

## Passthrough (full MR look)

1. Install **Meta XR All-in-One SDK** from Meta’s Unity package.  
2. Enable passthrough feature / composition layer.  
3. Keep our stage parents — only camera background becomes real world.

Until then, spatial scale and arm anchor are still correct for design validation.

---

## Non-goals

- Ray-Ban Meta (no spatial field)  
- Phone camera as proof of life-size dueling  
- Forked “Quest-only engine” — same `DuelEngine`
