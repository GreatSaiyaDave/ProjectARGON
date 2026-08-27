# Kuriboh companion (center menu button)

**Role:** Pokémon GO uses a Poké Ball as the center menu button.  
WRLDZ uses **real Kuriboh** art (card **40640057**) as the player’s spirit partner.

## Art source

| File | Use |
|------|-----|
| `40640057.png` / `40640057_menu.png` | Real Kuriboh crop (YGOPRODeck / CardArt) |
| `kuriboh_main.png` / `kuriboh_menu.png` | Same real Kuriboh art (menu path aliases) |
| `kuriboh_main_glow.png` | Soft glow under the button (from real art) |
| `CardArt/40640057.jpg` | Canonical card art for `CardDatabase.GetArt(40640057)` |

**No placeholder mascot.** Menu loads card ID `40640057` first via `CardDatabase`, then these Companion files.

## Code

`OverworldUI.LoadKuribohMenuSprite` + `BuildGoBottomChrome` — center button is Kuriboh → Hub.
