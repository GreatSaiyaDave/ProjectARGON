# Asset dive

## Policy

1. **Presentation pack first** — original WRLDZ art in `StreamingAssets/WRLDZ/Presentation/`  
2. **CardArt + Companion** — real card faces / Kuriboh 40640057  
3. **YgoRefs** — Yu-Gi-Oh! screenshots and portraits, parked off-build since 2026-09-23 (owner decision 4; `tools/OFFBUILD_MANIFEST.md`). Restore them before wiring them in.  
4. **OpenDuelyst `ui/` / Kenney UI & RPG / GDquest buttons** — generic chrome fallbacks. The unused OpenDuelyst trees (units, maps, fx…), OGA Public Domain, Free Game GUI and the Kenney icons / Sci-Fi packs are off-build since 2026-09-23 (owner decision 4; `tools/OFFBUILD_MANIFEST.md`).  


## Wired

| Surface | Asset |
|---------|--------|
| Splash | `Presentation/bg_splash.png` |
| Hub | `Presentation/bg_hub.png` |
| Overworld map | `Presentation/map_overworld*.png` |
| Emblem / MAP | `Presentation/emblem_spirit_eye.png` |
| Kuriboh menu | Card 40640057 + circular crop |
| Pins | `Presentation/pin_tear.png` / `pin_arena.png` |
| Profiles | WRLDZ profile art (YgoRefs portraits are parked off-build) |

## Free packs

See `Vendor/` (OpenDuelyst `ui/`, Kenney UI / RPG, GDquest mobile buttons, LPC). Unused packs live in `../ProjectARGON_offbuild/` — see `tools/OFFBUILD_MANIFEST.md` to restore one before wiring it.
