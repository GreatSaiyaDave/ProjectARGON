# Free 3D models — actually used in WRLDZ

Previous work leaned on primitives. **These packs are downloaded and loaded at runtime.**

## Packs on disk

| Path | Source | License |
|------|--------|---------|
| `Characters/*.obj` | [Quaternius Ultimate Animated Character Pack](https://opengameart.org/content/animated-characters-pack) | **CC0** |
| `Characters/*.obj` (Warrior, Wizard…) | [Quaternius RPG Characters](https://opengameart.org/content/lowpoly-rpg-characters) | **CC0** |
| `Referobot/Referobot.obj` | Quaternius Soldier_Male stand-in | **CC0** |
| `Arena/*` | [Kenney Platformer Kit](https://kenney.nl/) | **CC0** |
| `WRLDZ/Avatar/Kenney/*.png` | Kenney Animated Characters Protagonists skins | **CC0** |
| `WRLDZ/YgoFrames/` | [Custom YGO Database Assets](https://custom-yugioh-database.fandom.com/wiki/Assets) | Community templates |
| `WRLDZ/Vendor/` | Kenney UI + GDquest buttons | **CC0** / free |

## Runtime loader

`FreeModelCatalog` → `ObjMeshLoader` → used by `ArDuelSpace`:

- Referobot = real character mesh  
- Player/Opp duelists = Suit / Ninja  
- Arena floor = Kenney platform + stones  

## Support authors

- https://quaternius.com/  
- https://kenney.nl/donate  
