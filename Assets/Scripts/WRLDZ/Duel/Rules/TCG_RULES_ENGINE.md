# TCG Rules Engine — Project ARGON / DM WRLDZ

## Authority (requirement 1)

| What | Source |
|------|--------|
| Card text | `StreamingAssets/Cards/cards_db.json` → `CardDef.desc` (Konami text, Yugipedia-aligned) |
| Structural rules | Konami Official Rulebook + [Yugipedia](https://yugipedia.com/) |
| Effect **resolution** | Only cards in `OfficialEffectRegistry` |

**Policy:** Uncompiled or stub effect cards are **refused at deck** (`CardEffectStatus.MayIncludeInDeck`), not invented at Activate. Live refusals for deck-legal cards are real YGO only; never invent, simplify, or approximate. Structural rules (phases, zones, battle math, summons) are unchanged. Founding architecture: `../MASTER_ENGINE_SPEC.md`.

## Architecture (`Duel/Rules/`)

| Component | Role |
|-----------|------|
| `OfficialCardAuthority` | Official text + hash; no invented wording |
| `OfficialEffectRegistry` | Activatable script allow-list + summon procedures |
| `ChainStack` | CL1…CLn, Spell Speeds 1–3, resolve LIFO |
| `FastEffectTiming` | Priority / Fast Effect windows |
| `BattleMechanics` | Damage calculation, piercing flag, replay detection |
| `SummonProcedures` | Normal/Tribute/Flip/Fusion/Synchro/Xyz/Link/Ritual/Pendulum/Special checks |
| `ContinuousEffectBoard` | Continuous effects (registered only) |
| `RulesValidator` | Pre-snap / pre-action legality for AR + UI |
| `GameStateSnapshot` | Full rules state for multiplayer sync |

`DuelEngine` owns phase, battle steps, chain, continuous board, and commits all state changes. AR only reads state after `Notify()`.

## Turn structure

```
Draw Phase → Standby Phase → Main Phase 1
  → Battle Phase
       Start Step → Battle Step → Damage Step (sub-steps) → End Step
  → Main Phase 2 → End Phase → next turn
```

First player: no draw on turn 1; no Battle Phase on turn 1 (official).

### Damage Step sub-steps

1. Start — flip face-down attack targets  
2. Before damage calculation  
3. Damage calculation (`BattleMechanics.Calculate`)  
4. After damage calculation — apply LP  
5. End — destroy by battle, end-of-DS triggers (registered)  

Replay: if target leaves field or direct attack becomes illegal, return to Battle Step.

## Summoning

Structural checks for Normal / Tribute / Flip.  
Extra Deck / Ritual / Pendulum / Link / Synchro / Xyz require **registered procedures** matching official materials — otherwise illegal (no invented recipes).

## Chains & Fast Effects

- Spell Speed from card type (Counter Trap = 3, Trap/QP = 2, Normal Spell = 1)  
- `ChainStack.CanAddLink` enforces speed rules  
- `FastEffectTiming` tracks priority player  

Full multi-link SEGOC for every trigger is progressive; attack/summon response windows remain for registered traps.

## Public vs private knowledge

`RulesGameStateSnapshot`:

- Opponent hand: **count only**  
- Extra Deck: counts  
- Face-down cards: no public name in snapshot packs for face-down (field state uses FaceUp flag)

## AR integration

`ArSnapRules.Validate` → `RulesValidator.ValidateDiskPlacement` **before** snap.  
`Commit` only mutates via `DuelEngine`. Holograms refresh from `OnStateChanged` / snapshot — never shortcut rules.

## Extending official scripts

1. Confirm current text on Yugipedia for the card ID.  
2. Ensure `cards_db.json` `desc` matches.  
3. Implement resolution in `SpellTrapEffects` (or new script class).  
4. Add card ID to `OfficialEffectRegistry.ActivatableScripts`.  
5. Never resolve from free-text NLP.

## Honest scope note

A complete EDOPro-class engine for **every** printed card is multi-year work. This codebase provides:

- Complete **structural** TCG rules path for the vertical slice  
- Correct battle math, turn structure, zones, chain foundation  
- **Zero invented effects** for unregistered cards  
- A registry path to add official scripts card-by-card  

Starter IDs currently scripted include Pot of Greed, Dark Hole, Raigeki, MST, Monster Reborn, Mirror Force, Trap Hole, Waboku, Negate Attack, and related starter staples (see registry).
