# Open Duelyst assets (CC0)

**Source:** https://github.com/open-duelyst/duelyst  
**License:** Creative Commons Zero v1.0 Universal (CC0) — free for any use, commercial or not.  
**Copied from:** `app/resources/` (sparse checkout 2026-07-19)

## Inventory (≈200 MB)

| Folder | Files | Size | WRLDZ use |
|--------|------:|-----:|-----------|
| `units/` | ~1392 | 57M | Spirit / Tear monsters, companion sprites, emote-style units (600+ animated sheets + `.plist`) |
| `maps/` | 79 | 67M | Overworld / hub scenic backdrops, board scenes |
| `fx/` | 423 | 13M | Summon / attack / spell VFX |
| `emotes/` | 520 | 15M | Reaction bubbles, social |
| `profile_icons/` | 256 | 18M | Avatar frames / rank badges |
| `ui/` | 307 | 7.4M | Card backs, buttons, bottom bars, brackets |
| `card_backgrounds/` | 138 | 9.3M | Faction card art plates |
| `icons/` | 746 | 7.5M | UI glyphs |
| `crests/` | 32 | 6.6M | Faction crests |
| `tiles/` | 72 | 1M | Board tiles |

## Best fits for Duel Monsters: WRLDZ

1. **`ui/card_back*.png`** — alternate free card backs (not Konami; fine as WRLDZ spirit-binder backs).  
2. **`ui/bottom_bar_*`, `button_*`** — polished CCG chrome.  
3. **`units/*.png` + `.plist`** — animated pixel creatures for AR field presence / companion skins (parse plist → Unity spritesheets).  
4. **`fx/`** — battle impact FX when Master Duel-style 3D isn’t ready.  
5. **`maps/`** — atmospheric duel stages.  
6. **`profile_icons/` + `crests/`** — hub rank / faction identity.

## Unity notes

- Sheets are Cocos2d-style `.plist` atlases. Use a plist→Sprite importer or crop by frame rects.  
- Prefer `@2x` assets where present.  
- **Do not** rebrand as official Yu-Gi-Oh; these are Duelyst fantasy units used as free spirit art.
