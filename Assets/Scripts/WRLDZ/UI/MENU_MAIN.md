# Main menu — Mr. Referobot Hub

**Display title:** Duel Monsters: WRLDZ  
**Unity project:** ProjectARGON  
**Scene:** `MainMenu` (build index 2)

## Role in the graph

- **Not home.** Home is Overworld.
- Entered **only** from Overworld Disk **HUB**.
- Exit: **← BATTLE CITY MAP** → Overworld.
- Shadow Duel exit returns to **Overworld**, not this hub.

## Structure

| Control | Action |
|---------|--------|
| VS AI | → Player vs AI create sheet |
| VS PLAYER | → Player vs Player distance scan |
| DECK / BAG / STORY | Collection · inventory · season |
| BAZAAR / VIEW / SET | Shop · free view · settings |
| Profile (avatar) | Avatar customizer |
| MAP orb | → Overworld |

Tome lives inside Deck (Tome tab). Profile is the header portrait.

## Aesthetic

- Battle City rain rooftop backdrop
- Piano-glass navy tiles, thin cyan / gold rims, gold L-corner ticks
- Duel actions first (featured plates), then a 2×3 destination grid
- **Bangers** titles · **Exo 2** body · **Russo One** buttons via FreeUiKit

## Code

`DuelDiskMenuUI.cs` · `MainMenuBootstrap.cs` · `AppSession.GoMainMenu()`

See also: `FLOW.md` (full graph).
