# S23 Ultra + PC lab guide

**Your rig:** Samsung Galaxy S23 Ultra + current computer. **No headset.**

Product north star is still AR lenses later. Every week of phone work uses the **same duel stage** (`ArDuelSpace`) so lenses plug in without a rewrite.

---

## Daily workflow

| Where | What |
|-------|------|
| **PC Unity Editor** | Rules, UI, map, decks. Game view **1080×2340** portrait. WASD walks map. |
| **S23 Ultra APK** | Truth: camera AR LIVE, GPS, touch, heat/FPS |

Auto presentation:

- S23 → **ARCore 6DOF** (`ArFoundationSession`) on ENTER AR — floor-locked disks, real passthrough
- Editor → `EditorSim` (spatial stage RT)
- Webcam wallpaper is no longer the ENTER AR path. Digital still skips tracking.
- Toggle `ArLensesBootstrap.preferEditorCamera` or `ArPresentationTarget.PreferEditorCamera` to preview webcam on PC (lab only)

---

## One-time device setup

1. **Unity Hub** → install **Android Build Support** (SDK + NDK + OpenJDK) for 6000.4.8f1.  
2. S23: **Settings → About → Software** tap build 7× → **Developer options** → **USB debugging** on.  
3. Cable (or wireless ADB). Accept RSA prompt.  
4. Optional: `adb devices` shows the phone.

---

## Build APK

### From Unity menu
**WRLDZ → Lab → Build APK for S23 Ultra**

Output: `Builds/Android/WRLDZ_S23_Debug.apk`

### Install
```bash
adb install -r Builds/Android/WRLDZ_S23_Debug.apk
adb shell am start -n com.wrldz.duelmonsters/com.unity3d.player.UnityPlayerActivity
```

### Or File → Build Settings → Android → Build And Run

---

## First-run permissions (S23)

App will request:

1. **Camera** — AR duel passthrough (Zone Mode)  
2. **Location** — GO-style map + Tears  

Deny camera → duel still runs as **spatial sim** (dark stage, disks/holos).  
Deny location → use **on-screen walk pad** (or WASD on PC).

---

## Demo path (month-1 DoD)

1. Boot → account / short onboarding  
2. **Overworld** map (GPS outdoors or pad indoors)  
3. Walk within **~55 m** of a Tear (tag: **IN RANGE**) → tap pin → **Zone Mode prompt**  
   - **ENTER AR** (camera passthrough) or **DIGITAL** (same duel, no camera) or **Cancel**  
   - Or **Practice** orb to duel from anywhere  
4. Point at the floor until status reads **ARCORE · 6DOF floor**. Walk — holos stay in the room.  
5. Summon / set / attack / end turn  
6. **Map** back to overworld  

Full AR GPS details: **`AR_GPS_SETUP.md`**.

---

## Code map (lab)

| Piece | Role |
|-------|------|
| `WrldzLab` | Portrait, FPS, permissions |
| `ArPhoneCamera` | Permission + rear cam + orientation |
| `ArDuelSpace` | One spatial stage + passthrough plane |
| `ArPresentationTarget` | EditorSim / PhoneCamera / LensesXr |
| `Plugins/Android/AndroidManifest.xml` | CAMERA + LOCATION, portrait activity |
| `S23AndroidBuild` | Menu APK builder |

OpenXR packages may stay in the project for later — **they are not required to run the S23 demo.**

**Build your own combiner (salvage):** keep this phone. Hunt an AMOLED donor, a Gear VR lens, and 45° beam-splitter glass. Playbook: `SALVAGE_LENSES.md`. Lab toggle: **WRLDZ → Lab → Salvage Lenses (optical see-through)**.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Black AR, badge not LIVE | Grant Camera; reinstall; check Logcat for `[WRLDZ] Phone AR` |
| Landscape UI | Manifest + PlayerSettings force portrait; reboot app |
| No map movement indoors | Use bottom-left **walk pad** |
| Build fails Android | Install Android module in Hub; set SDK path |
| Wrong package name | Must be `com.wrldz.duelmonsters` |
| Heat / low FPS | Already 1080×1920 RT, no MSAA on mobile; close other apps |

Logcat filter:
```bash
adb logcat -s Unity
```
