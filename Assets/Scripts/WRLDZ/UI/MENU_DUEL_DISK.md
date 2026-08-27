# Duel mode UI + Konami phase flow

## Clean layout (top → bottom)

1. **LP orbs** + **phase banner** (`YOU · MAIN PHASE 1`)  
2. Status / hint  
3. Compact Referobot  
4. AR stage (disks + holograms)  
5. **Phase pills** — MAIN 1 · BATTLE · MAIN 2 · END (highlight current)  
6. Opp field / You field (framed cards, drag targets)  
7. Hand (tap menu / drag play)  
8. Announce + impact timer  
9. **Phase controls** — large **Battle Phase · Main Phase 2 · End Turn**  
10. Utility — Tributes · Cancel · Pass · Restart · Map  

## Official turn structure (Rulebook)

```
Draw → Standby → Main Phase 1 → [Battle Phase] → [Main Phase 2] → End Phase
```

| Control | When legal (your turn, open game state) |
|---------|----------------------------------------|
| **Battle Phase** | From **Main Phase 1** only; **not** first player’s first turn |
| **Main Phase 2** | From Main Phase 1 (skip Battle) or after Battle |
| **End Turn** | From Main 1, Battle, or Main 2 |

Draw / Standby run automatically when a turn begins.

## Play input

- **Tap card** → popup (Summon ATK / **Set Face-Down** / Activate…)  
- **Drag hand → disk** → chooser includes Set Face-Down when legal  
- **Announce** — e.g. `"Battle Phase"`, `"I end my turn"`  

## Structural rules still enforced in engine

See `RULES_ACCURACY.md` / `TcgRules.cs` — NS once/turn, tributes, first-turn no battle, set-turn traps, attack response windows, etc.
