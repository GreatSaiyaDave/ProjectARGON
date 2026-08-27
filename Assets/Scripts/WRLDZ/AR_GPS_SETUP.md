# AR GPS Overworld — Setup & Lab Loop

**Product:** GO-style real-world map → Tear → AR spatial duel → Map.  
**Rig:** Galaxy S23 Ultra (truth) + PC Editor (WASD / walk pad).  
**DuelEngine stays pure** — no GPS imports; overworld owns location.

---

## Architecture

```
OverworldBootstrap
  → WrldzLab.RequestLabPermissions (Camera + Fine/Coarse Location)
  → OverworldUI.Build
       ├─ MapEnvironment (DontDestroyOnLoad)
       │     GPS poll · meters N/E from origin · weather · day/night
       ├─ Map canvas (pans under fixed avatar)
       ├─ Pins (Tears / anchors) on map content
       └─ HUD (NEARBY, walk pad, MENU, Practice, Recenter)
Tear in range → GoDuel → ArDuelSpace (PhoneCamera / EditorSim)

3D on this map (avatar token, optional tilt) is `UI/OVERWORLD_3D.md`. Home does not become a walkable city.
```

| Piece | Role |
|-------|------|
| `WrldzLab` | Portrait, FPS, **runtime permissions** |
| `WrldzInput` | Safe keyboard + `Input.location` GPS |
| `MapEnvironment` | GPS → meters, origin, weather (Open-Meteo), day/night |
| `OverworldUI` | GO shell: pan, pins, proximity, zone enter |
| `ArPhoneCamera` / `ArDuelSpace` | AR duel stage when you leave the map |
| `Plugins/Android/AndroidManifest.xml` | CAMERA + LOCATION |

---

## How movement works

1. **Session origin** = first good GPS fix (or default Tokyo region in Editor).  
2. Position → **meters east / north** of origin.  
3. **Avatar stays fixed** on HUD; **map content pans** opposite walk (GO style).  
4. **Editor / indoor:** WASD or on-screen walk pad calls `SimulateWalkMeters`.  
5. **Tears** are pins on the local map. Distance updates live.  
6. **Interact radius** = `MapEnvironment.TearInteractRadiusM` (**55 m**).  
   - Inside radius → tap pin → **AR Zone Mode duel**.  
   - Outside → toast tells you to walk closer (or use **Practice** orb).

---

## PC Editor (daily)

1. Open project, scene flow **Boot → Overworld** (or Play from Boot after login).  
2. Game view **1080×2340** portrait (WrldzLab).  
3. **WASD** (or arrows) walks the map.  
4. Walk toward a glowing Tear until tag shows **IN RANGE**, then **tap the pin**.  
5. Or tap **Practice** (right orb) to duel without proximity.  
6. **MENU** → hub; **Recenter** resets origin under your current meters.

No real GPS in Editor — status shows `Editor · WASD walks map`.

---

## S23 Ultra (real GPS + camera)

### Permissions
App requests on Overworld enter:

- **Location (fine + coarse)** — map  
- **Camera** — AR duel passthrough (when you enter a Tear)

Also enable **Settings → Location** on the phone (system toggle).

### Build & run
```
WRLDZ → Lab → Build APK for S23 Ultra
adb install -r Builds/Android/WRLDZ_S23_Debug.apk
```

See `S23_LAB.md` for USB debugging and logcat.

### Outdoor checklist
1. Login → Overworld.  
2. Status should move to **GPS locked ±Nm**.  
3. Walk; coords / pin meters update.  
4. Approach Tear → **TEAR IN RANGE** → tap pin → AR duel.  
5. Grant camera when prompted for passthrough.  
6. **Map** returns to Overworld.

### Indoor desk demo
- Use **walk pad** (bottom-left) or Practice orb.  
- GPS may stay weak indoors — pad still moves the map.

---

## Status strings (HUD)

| Status | Meaning |
|--------|---------|
| `GPS locked ±Nm` | Real fix, walking updates map |
| `GPS weak` | Fix but poor accuracy — stay outdoors |
| `GPS acquiring…` | Waiting for first fix |
| `Editor · WASD…` | PC sim |
| `Walk pad · enable GPS…` | Mobile without fix |
| `Enable Location in phone Settings` | System location off |
| `Location permission denied` | User denied app permission |

---

## Tuning

| Constant | Where | Default |
|----------|--------|---------|
| `TearInteractRadiusM` | `MapEnvironment` | 55 m |
| `MetersPerMapWidth` | `OverworldUI` | 280 m (map pan scale) |
| GPS accuracy / update | `WrldzInput.LocationStart` | 8 m / 3 m |

---

## Create AR Duel (from Overworld)

All live matches run in **AR**. The midfield is **anchored between you and your opponent** using separation meters.

| Control | Action |
|---------|--------|
| **VS AI** | Player vs AI: Standing / Table / Street / Practice |
| **VS PVP** | Player vs Player: mark P1 + P2 → auto-scan distance → AR arena |
| **MENU** | Hub (PvAI, PvP, formats, deck, settings) |
| **Practice orb** | Instant AR practice at default separation |
| **Tear (in range)** | Zone Mode prompt → ENTER AR · DIGITAL · Cancel |

### Player vs AI presets

| Preset | Separation | Feel |
|--------|------------|------|
| Table | 1.6 m | Close |
| Standing (default) | 2.5 m | Face-to-face |
| Street | 4.0 m | Open space |

### Player vs Player (auto distance scan)

1. **MARK PLAYER 1** — host stands still (GPS lock or map origin in Editor).
2. **Walk / stand** Player 2 at their real-world spot (live meters update on screen).
3. **MARK PLAYER 2** — haversine (GPS) or walk meters (Editor) → `SeparationMeters` clamped **1.2–8 m**.
4. **START AR DUEL** → `AppSession.StartArDuel` with `NearbyPeer` · hotseat (no SimpleAi).

Classes: `PlayerDistanceScanner`, `PlayerVsPlayerCreateScreen`, `ArDuelMatchConfig.PlayerVsPlayer`.

`AppSession.StartArDuel(config)` → `SimulatedArmTracker.ApplyPlayerSeparationMeters`.

---

## Not yet / later

- Server-side Tear placements tied to real POIs  
- Compass-facing map rotate (toggle is stub)  
- Multi-device nearby join (shared match over network) — hotseat works now  
- Background location (not needed for foreground GO loop)

---

## Quick test (Editor)

1. Play → Overworld.  
2. **VS AI** → Standing → START (AI opponent).  
3. **VS PVP** → Mark P1 → WASD a few meters → Mark P2 → START (hotseat, arena spans measured distance).  
4. Or **WASD** until a Tear is **IN RANGE**, tap pin.  
5. **Map** home; compare Table vs Street disk spacing on PvAI.
