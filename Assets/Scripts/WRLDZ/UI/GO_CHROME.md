# Non-AR map chrome (Pokémon GO grammar, WRLDZ skin)

**Scope:** Overworld map chrome, the Millennium Eye menu, and map overlays.
**Not in scope here:** AR duel disks and holograms (separate system).
**Style authority:** [`VISUAL_SYSTEM.md`](VISUAL_SYSTEM.md). If this file and VISUAL_SYSTEM disagree,
VISUAL_SYSTEM wins. **Code authority:** `OverworldUI` (the `Build*` methods named below).

> Rewritten 2026-09-23 (QC docs-drift L6). The older version described a 5-slot
> `[DECK][BAG][EYE][STORY][SET]` dock, "circular + white frosted chips" and an Open Duelyst
> **MENU hex** centre button. None of those exist any more; do not bring them back.

## Layout (current code)

```
[ TopWallet: Đ · ◎ · ⚡ ]                        [ Settings ] [ Compass ]
[ GPS chip ]                                                 └ NEARBY popout
│                                                                        │
│                         FULL-BLEED MAP                                 │
│                 (roads · parks · pins · avatar · NPCs)                 │
│                                                                        │
[ status toast ]                                                         │
[ Profile orb + Lv + XP ]     [ BAG ]  ( MILLENNIUM EYE )  [ STORY ]   [ deck orb ]
```

| Element | Builder | Notes |
|---|---|---|
| Top wallet (Digizeni, Duel-Coin, Set Energy) | `BuildGoTopHud` → `BuildTopWallet` | Each chip opens the Artifact Deck Box on that currency. |
| Settings and Compass orbs | `BuildGoTopHud` | Compass toggles the NEARBY popout (`BuildCompassNearby`). |
| GPS chip | `BuildGpsChip` | Location status. |
| Profile orb (trainer, level, XP, soul badge) | `BuildBottomProfile` | Opens the avatar customizer. |
| BAG and STORY dock orbs | `BuildGoBottomChrome` → `BuildDockOrb` | They flank the Eye. |
| Millennium Eye | `BuildGoBottomChrome` / `BuildEyeMenu` | Home menu (below). |
| Deck orb | `BuildDeckSwitcher` | Opens DECK (deck collection). |

## Millennium Eye menu

Exactly 8 tiles (the `KcTile` list in `BuildEyeMenu`):

```
VS AI            VS PLAYER
DECK     BAG     STORY
BAZAAR   TOME    SET
```

There is no PRACTICE, TRADE or TOURNEY tile. PRACTICE is a card inside VS AI (FormatSelect). SET
lives in the Eye grid, not in the dock. The destinations open as HubChrome overlays; see
[`../FLOW.md`](../FLOW.md).

## Design rules

1. **The map is the hero.** Chrome stays compact at the edges; no heavy panels over half the screen.
2. **One slate language (VISUAL_SYSTEM).** Overlays, sheets and chips use the obsidian
   `SolidVisionSlate` / `QuietFill` + `FilamentRim` + L-ticks. Eye destination and featured tiles
   use the opaque KaibaCorp void plates (`HubChrome.PaintPlate`). No white frosted chips, no
   `PanelMenuGlass`, no Kenney beige, no smoked glass.
3. **Icons:** `ImagineAssets` first, `DuelystUi` (Open Duelyst `ui/`) only as the fallback.
4. **YGO flavour goes on content** (Tear pins, the Eye, duel rows), not on every panel.
5. **Nearby** is one compass popout (name, kind, metres). No second radar strip.
6. **Glasses target (owner decision 2026-09-23 #1):** the ship target is Android XR glasses; the
   phone map is the dev/testbed surface. Do not design new map features phone-first.

## Content (not chrome)

- Map tiles: `Presentation/map_overworld*`, Battle City layers in `OverworldMapWorld`.
- Pins: Tear · Portal · Event · NPC · Treasure · Anchor. The range ring only shows when a Tear is in
  range; distance chips stay short (`42m`).
- Legacy pack-ins under `StreamingAssets/WRLDZ/GoChrome/` are fallbacks only (`GoTheme`).

## Overworld map baseline

- Uniform cartography: soft greens, cream blocks, light roads, blue water, one palette.
- Day/night and weather are light overlays (`ApplyGoAtmosphere`, `BuildWeatherDrops`); map tiles
  stay stable.
- 3D presence on the map: [`OVERWORLD_3D.md`](OVERWORLD_3D.md) (token / tilt; home stays the GO map).

## Code

| Class | Role |
|---|---|
| `GoTheme` | Shared orbs, chips, labels |
| `OverworldUI` | Map shell, HUD, pins, Eye menu |
| `OverworldMapWorld` | Battle City terrain layers |
| `HubChrome` | Shared Battle City capsules and plates |
| `StreamingSprite` / `ImagineAssets` | Sprite loading (through `StreamingFiles`) |
