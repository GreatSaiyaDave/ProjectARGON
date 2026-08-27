# WRLDZ presentation assets

## Policy

| Source | Path | Use |
|--------|------|-----|
| **WRLDZ Presentation** | `Presentation/` | Splash, hub, overworld map, emblems, pins — **no game screenshots** |
| **Grok Imagine** | `Imagine/` | HUD icons, map pins, holo panels, FX — original cyber chrome |
| **Card art** | `StreamingAssets/CardArt/{id}.jpg` | Every card face (YGOPRODeck pipeline) |
| **Kuriboh menu** | `Companion/` + card **40640057** | Center overworld menu button |
| **YgoRefs** | `YgoRefs/` | Classic maps/profiles as fallback & design reference |
| **Card frames** | `YgoFrames/` | CYDB-style frames |
| Free packs | `Vendor/` | OpenDuelyst, OGA PD, Kenney |

Master Duel / LOD Steam screenshots are **archived** under `YgoRefs/_archive_screenshots/` and are not loaded at runtime.

## YgoRefs (official game UI)

Synced from `~/Yu-Gi-Oh References`:

- `ui-menus/` — DDS, Sacred Cards, WC maps, LOD, Master Duel screenshots  
- `player-profiles/` — Duelist portraits  
- Aliases: `map_domino.png`, `bg_master_duel.jpg`, `profile_default.png`, `emblem_millennium_eye.png`, …

Code: `WRLDZ.Presentation.YgoRefs`

## Free packs (non-YGO chrome only)

Kenney / GDquest / Free Game GUI under `Vendor/` — still fine for GO-style orbs when a YGO image isn’t the right tool.  
They are **not** a replacement for card art, Kuriboh, Domino maps, or duelist portraits.
