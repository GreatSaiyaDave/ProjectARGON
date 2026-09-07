# Non-AR chrome — Pokémon GO template

**Scope:** Overworld map + Hub menu + soft chips.  
**Not in scope here:** AR duel disks / holograms (separate system).

## Layout reference (Pokémon GO map)

```
[ Profile+level ring ]              [ Weather chip ] [ Compass ]
│                                                   └ nearby popout
│                     FULL-BLEED MAP                           │
│              (parks · roads · pins · avatar)                 │
│                                                              │
[ Toast pill ]                                                 │
 [ DECK ]  [ BAG ]   [ EYE ]   [ STORY ]  [ SET ]
```

## Design rules

1. **Map is the hero** — no heavy navy GBA panels over half the screen.  
2. **Chrome is circular + white frosted chips** — soft shadows, light ink text.  
3. **YGO flavor on content** (Tear pins, disk in main orb, duel row) — not on every panel.  
4. **Millennium Eye** — home menu. Opens the Battle City hub tile grammar (DUEL featured capsules + COMMAND dest grid), not a separate tray of tall holo cards.  
5. **Nearby** = one compass popout (name, kind, meters). No second radar strip.

## Assets

**Chrome first** — Open Duelyst CCG kit (`Vendor/OpenDuelyst/ui/` via `DuelystUi`):

- Center menu → **MENU** hex button (not Kuriboh, not Poké Ball)  
- Circles / bars / panels → same Duelyst family  
- Hub rows → primary / secondary hex buttons  

**Content (not chrome):**

- Map tiles → `Presentation/map_overworld*`  
- Navi / team → onboarding + profile only  
- Pins → tear / arena  

**Legacy pack-ins** (`GoChrome/`) — fallback only:

- `main_orb.png` / `main_orb_glow.png` — fallback if Kuriboh missing  
- `orb_items.png` / `orb_shop.png` / `orb_profile.png`  
- `level_ring.png` · `compass.png` · `avatar_default.png`  
- `pin_tear.png` · `pin_arena.png` · `pin_anchor.png`  
- `nearby_plate.png` · `toast_plate.png` · `weather_chip.png`

## Code

| Class | Role |
|-------|------|
| `GoTheme` | Shared orbs, chips, labels |
| `OverworldUI` | GO map shell + HUD + pins |
| `OverworldMapWorld` | Battle City terrain layers (roads, parks, districts) |
| 3D map presence | `OVERWORLD_3D.md` (token / tilt; home stays GO map) |
| `DuelDiskMenuUI` | GO main-menu sheet |
| `StreamingSprite` | Loads pack-ins |

## Overworld map (Pokémon GO baseline)

- **Uniform cartography**: soft greens, cream blocks, light roads, blue water — one palette
- No art-tile + overlay mashup, no district labels, no neon city wash
- Day/night = light sky/fog overlays only (map tiles stay stable)
- **Pins** are the accent: Tear · Portal · Event · NPC · Treasure · Anchor
- Range ring only when a Tear is in range; distance chips stay short (`42m`)
- Currency strip + compass nearby popout + smooth pan still apply

## Hub (main orb)

Hub scene is optional (full systems). Map chrome is the Eye dock; the Eye menu is the hub template cloned onto the map.
