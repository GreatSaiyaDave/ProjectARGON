---
name: wrldz-overworld
description: Overworld Niantic-style loop for Project ARGON / Duel Monsters WRLDZ. Use when changing the GPS map, Tears, raids, bazaar, Set Energy, inventory, progression, tournament rooms, or duel launch from Overworld. Not for TCG resolutions (use ygo-gamedev), Hub plate styling (use ygo-ui-lore), or AR session graphs (use wrldz-ar-lenses).
---

# WRLDZ overworld (ARGON)

Read this skill before changing the living-world loop. Home is **Overworld**, not the Hub scene. Prefer `FLOW.md`, `OVERWORLD_ZONES.md`, `PROGRESSION_CANON.md`, `SET_ENERGY_AND_BAZAAR.md`.

## Hard rules (this repo)

1. **Overworld is HOME.** Boot → Overworld. MainMenu is an optional systems hub (Desktop Lab SYSTEMS HUB). Daily play is the Eye on the map.
2. **Tears are this game’s PokéStop.** Walk up, harvest (cooldown), street NPCs, then Tear boss. Do not make Tears a second duel engine.
3. **Never auto-force AR.** In-range Tear → `ZoneModePrompt` ENTER AR / DIGITAL / Cancel.
4. **PvP grants no Set Energy.** Orbs through L50 yes; SE no. Practice = **0 XP**.
5. **Tournaments are rooms, not map pins.** Host from Eye **TOURNEY**.
6. **Deck boxes are not bulk storage.** Pockets / play deck box only. Backpack is tetris cargo. Currencies live in the Artifact Deck Box.
7. **Do not merge** `cursor/deck-builder-collection-fill-e6a1`. CARD LIST: ChipGrid pins the **panel**, never scroll content.
8. **Hardware never gates core play.** No GPS → walk pad. No ARCore → DIGITAL.

## Route

| Task | Read |
|---|---|
| Map pins, street NPCs, raids, tourney | [references/niantic-loop.md](references/niantic-loop.md) |
| SE, tablets, inventory, XP | [references/economy-inventory.md](references/economy-inventory.md) |
| Scene / menu graph | `Assets/Scripts/WRLDZ/FLOW.md` |
| Sources | [sources.md](sources.md) |

## Scene roles

| Scene | Role |
|---|---|
| `Boot` | Onboarding only. Mid-game destination only after logout. |
| `Overworld` | Home. GPS map + avatar + Eye. |
| `MainMenu` | Systems hub, not home. |
| `DuelSlice` | Always the spatial duel stage. Exit → Overworld (lab TEST DUEL → Boot). |
| `Possession` | Street NPC beat (human → Tear → spirit). Then DuelSlice. |

## Eye (home menu)

`OverworldUI.BuildEyeMenu` → `HubChrome.MountFeatured` / `MountDest` (dest tiles belong **here**, not on overlay sheets).

DUEL: VS AI / VS PLAYER. COMMAND: DECK / BAG / STORY / BAZAAR / TOME / SET. Extra: PRACTICE / TOURNEY / TRADE.

Overlays use `DualMenuPresenter.BuildFrame` (quiet compact window so the map stays visible). Chrome details: `ygo-ui-lore`.

## When you are stuck

- If a change only exists on the Hub scene, the owner will not see it on daily play — put it on Overworld Eye / overlays.
- If SE appears after a ranked win, revert it. Blueprint: no PvP energy.
- If a street NPC starts a raid-rules duel, check `ArDuelMatchConfig` launch kind + LP + `TomeLegal`.
- For AR session / disks, open `wrldz-ar-lenses`. For chains, open `ygo-gamedev`.
