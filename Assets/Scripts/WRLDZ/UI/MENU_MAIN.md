# Main menu — Mr. Referobot Hub

**Display title:** Duel Monsters: WRLDZ  
**Unity project:** ProjectARGON  
**Scene:** `MainMenu` (build index 2)

## Role in the graph

- **Not home.** Home is Overworld.
- Entered from Desktop Lab **SYSTEMS HUB** (optional). Live Overworld Eye opens overlays in place.
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

Tome is **not** a Deck tab and **not** a hub tile — Overworld Eye **TOME** and AR SYS **TOME**. Artifact Deck Box is nested under **BAG** (wallet / ON YOU); AR SYS has a glanceable **ARTIFACTS** tile. Profile is the header portrait.

## Aesthetic

- Battle City rain rooftop backdrop (darker lower third so tiles lift)
- Piano-glass navy tiles with cyan / gold rims, gold L-corner ticks, interior wash
- Duel actions first (featured plates), then a 2×3 destination grid
- Dest rows: 3D prop well on the left (Imagine icon fallback) + bold title
- VS AI uses the Battle City disk showcase; VS PLAYER uses twin-disk prop
- MAP is a labeled **BATTLE CITY MAP** plate, not a naked orb
- Related overlays (VS AI, VS PLAYER, DECK, BAG, STORY, BAZAAR, VIEW, SET, formats, scan, tournament, zone prompt) reuse that same capsule grammar via `HubChrome` / `DualMenuPresenter`
- **Bangers** titles · **Exo 2** body · heavy outline (`WrldzType` / `MenuCommandButton.ApplyHubType`)

## Code

`DuelDiskMenuUI.cs` · `MainMenuBootstrap.cs` · `AppSession.GoMainMenu()`

See also: `FLOW.md` (full graph).
