# Free asset sources search (2026-07-19)

## 1. Open Duelyst — **best free CCG pack**

- **Repo:** https://github.com/open-duelyst/duelyst  
- **License:** CC0 1.0 (entire game + art)  
- **Art root:** `app/resources/`  
- **Installed:** `StreamingAssets/WRLDZ/Vendor/OpenDuelyst/` (~200 MB)

| Subfolder | What you get | WRLDZ fit |
|-----------|--------------|-----------|
| `units/` | 600+ unit spritesheets + Cocos `.plist` animations | Spirit monsters, companions, Tear creatures |
| `ui/` | Card backs, buttons, bars, brackets | CCG chrome polish |
| `card_backgrounds/` | Faction art plates | Spirit Binder / deck builder |
| `fx/` | Spell / combat VFX | Duel impact until 3D FX ready |
| `maps/` | Board / scenic maps | Duel stages, hub wash |
| `profile_icons/`, `crests/`, `emotes/` | Rank + social | Hub identity |
| `icons/`, `tiles/` | Glyphs + board tiles | Misc UI |

**Also useful:**  
- Godot re-export: https://github.com/Jordyfel/duelyst-animated-sprites-godot  
- Unity re-export: https://screensmith.itch.io/duelyst-unit-animations-for-unity  

## 2. OpenGameArt Public Domain Pack — **installed**

- **Page:** https://opengameart.org/content/public-domain-pack  
- **License:** CC0  
- **Installed:** `StreamingAssets/WRLDZ/Vendor/OpenGameArt_PublicDomain/` (~26 MB, 95 files)  
- **Contents:** cut-out animals, architecture (cathedral/temple/columns), forest/house BGs, portraits, decorations, skulls, trees/rocks  
- **Fit:** map props, NPC faces, Umbrax flourishes — **not** a YGO UI substitute

## 3. DeviantArt — **search carefully; do not bulk-rip**

DA is mostly **all rights reserved**. Only use when the author explicitly grants:

- Stock / free stock  
- Public domain / CC0  
- “Free to use commercially” / free download with clear terms  

### Search queries (on deviantart.com)

```
game assets free to use commercial
RPG UI stock free
pixel sprites free commercial
card game UI free download
fantasy map stock free
```

Filter: **Downloadable** + read the **description license** every time.

### Patterns that work

| Type | Notes |
|------|--------|
| **Stock** accounts | Explicit stock licenses; check commercial clause |
| **OGA mirrors** | Many OGA packs also posted on DA — prefer OGA download + license stamp |
| **Adoptable “free base”** | Often non-commercial or credit-required only |
| **Fan art (YGO, Pokémon, etc.)** | **Not free** for shipping — ignore for production |

### Safer alternatives to DA scrapes

- OpenGameArt advanced search (CC0 filter)  
- Kenney.nl (CC0)  
- itch.io “CC0” / “public domain” tags  
- Wikimedia Commons (verify PD)  

## Priority order for WRLDZ

1. **YgoRefs + CardArt** — real YGO look (your reference pack + YGOPRODeck)  
2. **OpenDuelyst** — free animated CCG creatures + polished UI chrome  
3. **OGA Public Domain Pack** — scenic props  
4. **Kenney / GDquest** — GO-style orbs / generic buttons  
5. **DeviantArt** — only hand-picked, license-clear stock  

## Next wiring (optional)

- `VendorKit` loaders for `OpenDuelyst/ui/card_back@2x.png`  
- Plist atlas importer for `OpenDuelyst/units/*`  
- Map decals from OGA `cathedral.png` / `temple.png` on overworld
