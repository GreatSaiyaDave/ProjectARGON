# Overworld 3D — map presence, not a second game

**Scene:** `Overworld` (home).  
**Canon:** `FLOW.md`, `PRODUCT_VISION.md`, `AR_GPS_SETUP.md`, `GO_CHROME.md`.  
**Avatar mesh:** `AVATAR_3D_INTEGRATION.md`.

Home stays a Pokémon GO map: you walk the real world (or WASD / walk pad indoors), the avatar stays on screen, the map pans underneath. Tears still prompt Zone Mode. 3D belongs on that map as presence, not as a replacement city you explore with a third-person camera.

---

## What exists today

| Piece | Behavior |
|-------|----------|
| `OverworldBootstrap` | Builds the map after location permission |
| `OverworldUI` | HUD, pins, nearby, MENU, GPS / WASD pan |
| `OverworldMapWorld` | Full-bleed 2D city art (`ImagineAssets.BgOverworldMap`) |
| `MapEnvironment` | Meters east/north from origin, weather, day/night |
| Avatar token | 2D portrait in a fixed HUD orb |

Movement math is already 3D-ready: east/north meters. Only the surface is a UI Image.

---

## What 3D overworld is (and is not)

| In scope | Out of scope |
|----------|----------------|
| 3D duelist standing on the map (VRM / GLB) | Open-world Battle City you walk like an RPG |
| Tilted map / simple building extrusions | A second GPS-synced 3D city mesh |
| 3D pins (tear / portal / arena) | Replacing Tear → AR with “enter the 3D street” |
| Same `MapEnvironment` meters | New input besides GPS / WASD / walk pad |

A walkable 3D city as home fights the product: location is the overworld, AR is the place you duel, the map is the index. Building a street-level city also duplicates `ArDuelSpace` and the S23 demo.

Optional later (not Overworld home): story instance or “look around” AR overlay. That is already listed as the AR variant of the map in `PRODUCT_VISION.md`.

---

## Phases

### 1 — 3D token on the existing map

Keep `OverworldUI` pan and pins. Swap the HUD portrait token for a small world-space or UI 3D model (UniVRM when `AvatarAppearance.vrmId` is set; 2D portrait if missing). Idle only. No third-person camera.

Code: `AvatarPortraitView` + a thin `OverworldAvatar3d` spawned under the fixed avatar slot.

### 2 — Tilted map plane

Move `MapContent` from a Screen Space Image to a world-space quad (or orthographic tilt). GPS pan still slides the plane under a fixed screen position. Optional low boxes or billboards for districts. Pins stay children of map content.

`OverworldMapWorld.Build` grows a 3D path; 2D art remains the albedo so we do not author a city mesh.

### 3 — 3D pins

Tear / portal / arena as short meshes or particles on the same UV as today’s pins. Distance, range ring, and Zone Mode prompt stay in `OverworldUI`.

---

## Implementation map

| Work | Touch |
|------|--------|
| 3D token | New presenter; `AvatarAppearance.vrmId` / `use3dPreview` |
| Tilted plane | `OverworldMapWorld` + pan offsets in `OverworldUI.ApplyGpsMapPan` |
| Pins | Existing pin transforms; swap sprite for mesh |
| GPS | Unchanged (`MapEnvironment`) |
| HUD chrome | Unchanged (`GoTheme` / `HudTokens`) |

Do not put duel disks, holos, or `DuelEngine` on the overworld. Those load in `DuelSlice` after Zone Mode.

---

## Acceptance

- Avatar still fixed on screen; map still pans from GPS / WASD / pad.
- Missing VRM falls back to the 2D portrait.
- Tear in range still opens the AR / Digital prompt.
- MENU, nearby, currencies, and profile orb still work.
- S23: no extra full-city mesh; token + plane only.
