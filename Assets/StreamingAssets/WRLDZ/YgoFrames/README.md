# YGO card frames — Custom Yu-Gi-Oh! Database Wiki

**Source:** https://custom-yugioh-database.fandom.com/wiki/Assets  

These files were downloaded via the Fandom MediaWiki API for use as **blank card templates, backs, textures, and symbols** in Project ARGON / Duel Monsters:WRLDZ.

## Layout

| Folder | Contents |
|--------|----------|
| `Templates/` | `Card-normal/effect/spell/trap/fusion/synchro/xyz/link/ritual/token.png` + gods + `Card-artifact.png` (grey spell anatomy, Artifact orb) |
| `Backs/` | Card back artworks |
| `Textures/` | Foil / paper textures |
| `Rarities/` | Foil overlays |
| `Symbols/` | Stars / attribute extras |

Core templates are **421×614** with a white art hole (≈ x 51–370, y 113–432).

## Code

- `YgoCardFrames.cs` — picks frame by `CardDef` type, exposes art-window anchors  
- `DuelUI.CreateCardButton` — composites frame + `CardArt/{id}`  
- `CardInspectPopup` — large framed preview + text + actions  
- Card backs via `YgoCardFrames.CardBack()`

## Credit / license

Assets on the wiki are community-uploaded for custom-card creation.  
**Not official Konami product.** Respect Fandom / original uploaders’ terms; credit the [Assets page](https://custom-yugioh-database.fandom.com/wiki/Assets) and named artists where listed (e.g. Reaxter templates on the wiki).

If redistributing this game publicly, re-verify each file’s license on the wiki File: page.
