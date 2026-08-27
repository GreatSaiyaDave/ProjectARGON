# AR · Anime presentation (phone + lenses)

## Product

WRLDZ is an **anime Solid Vision simulator** with **real TCG rules**, played:

1. **On phones** (camera passthrough / Editor sim RT)  
2. **With AR lenses** (OpenXR / Quest-class — same interaction graph, life-size disks)

Users walk real space with **variable lighting** (noon sun → night indoor). Holos must stay readable.

## Style lock

| Rule | Choice |
|------|--------|
| Look | **2D anime art on 3D objects** (cel / unlit), not photoreal PBR |
| Cards now | **Physical thickness** + art face + card back + accent rim |
| Monsters now | **Artwork holograms** (billboard + glow rim) |
| Monsters later | Per-id **GLB/OBJ** + dynamic summon/attack behavior |
| Lighting | **Unlit + exposure fit** (`ArAnimePresentation`) — Lit is a last resort |

## Phone + lenses (how they work together)

```
Rules engine (one) ──► ArDuelInteractionSystem
                            │
            ┌───────────────┼───────────────┐
            ▼               ▼               ▼
     Phone RT stage    Lenses XR cam    Editor sim
     (ArDuelSpace)     (ArLensesSession)  (no cam)
            │               │               │
            └──────── same disks / hand / arena ──┘
```

- **One rules + interaction stack.** Presentation targets differ; gameplay does not.  
- Phone: passthrough plane + stage camera → RawImage.  
- Lenses: world-locked stage at arm / floor scale.  
- Materials stay **unlit** so both paths read the same art.

## Variable lighting

`ArAnimePresentation.TickExposure(stageCam)` each few frames:

- Estimates ambient + light luminance  
- Sets `ExposureMul` so holos **pop outdoors** and **don’t blow out in the dark**  
- Applied through `MakeFaceMaterial` / `MakeSolid` / `Expose`

## Code map

| Piece | Role |
|-------|------|
| `ArAnimePresentation` | Exposure, unlit shaders, holo renderer flags |
| `ArPhysicalCardBuilder` | Thick TCG card (edge + face + back + rim) |
| `ArDiskCardVisual` | Disk zone cards |
| `ArFloatingCard` | Hand volume cards |
| `ArArenaCardVisual` | Midfield holos (art until mesh) |
| `CardModelCatalog` | Optional per-card OBJ; art materials |

## Drop-in when you research models

```
StreamingAssets/Models/Cards/{cardId}.obj   → monster mesh
StreamingAssets/WRLDZ/Avatar3D/{id}.vrm     → trainer body (later)
```

Set `ArArenaCardVisual.PreferMeshWhenAvailable = true` when coverage is ready.

## Out of scope (intentionally 2D)

Overworld map pins, menus, HUD chrome, currency chips.
