# WRLDZ Duel Engine — Master architecture

Founding spec distilled from `wrldz-duel-engine-master-prompt.pdf`.
**Cards are data. The engine is the interpreter.** Do not add `if (cardId == …)`
except for a mechanic no other card has.

This file is authority for *how* the engine must grow. Live legality still lives in
`DuelEngine` + `OfficialEffectRegistry` + `TextEffects`.

## 0. ocgcore / YGOPro — oracle, never shipped

ocgcore + Project Ignis scripts are AGPLv3 (network copyleft). They must **not**
be linked into WRLDZ or run as a production backend.

**Chosen model: reference oracle only.**

- Local Lua at `~/ygopro-scripts` is mined into facts
  (`extract_ygopro_continuous.py`, `extract_ygopro_triggers.py`).
- We port **behavior**, we do not run Lua in Unity.
- Divergence vs those facts in `scan_engine_gaps.py` is a bug, not a guess.

## 1. Principles (must keep)

1. **Cards are data** — `cards_db.json` + compiled `CompiledCardProgram` + catalogs.
   New cards = data entry that passes the linter, not a new C# branch.
2. **Cost ≠ effect.** Cost is paid at activation, before the chain link exists, and is
   never refunded if the link is negated.
3. **The chain is the resolver** — explicit LIFO stack (`ChainStack`), not callbacks.
4. **State checks at defined checkpoints** (LP 0, deck-out). YGO has no MTG-style
   "0 ATK destroys the monster" state-based action.
5. **Public vs hidden** is a permissions layer (`RulesGameStateSnapshot`).
6. **Konami rulebook is ground truth.** AR/WRLDZ deviations are config flags on
   top of the TCG engine, never a rewrite of core rules.

## 2–5. Already owned by `DuelEngine`

Zones, phases (including Damage Step sub-steps), Normal/Tribute/Flip, chain
speeds, attack/summon/Damage Step response windows. Extra Deck / Ritual /
Pendulum / Link / Synchro / Xyz: **registered procedures only** for v1
(`SummonProcedures`). Half-implemented Extra Deck is forbidden — unregistered
recipes stay illegal.

## 6. Effect DSL (`CompiledCardProgram` / `EffectClause`)

Every effect must eventually declare:

| Field | WRLDZ today |
|---|---|
| Category (Ignition / Trigger / Quick / Continuous) | `EffectTiming` + `IsQuickEffect` |
| Spell Speed | `OfficialEffectRegistry.SpeedOf` |
| Cost | `RequiresDiscardCost`, `RequiresLpCostMultiple` |
| Condition checked at | `ConditionCheckedAt` (activation / resolution / both) |
| Targets locked at activation | `RequiresTargetChoice` + `PendingActivation` |
| Resolution primitives | `EffectActionKind` |
| Once-per-turn scope | `OncePerTurn` + `OncePerTurnScope` |
| Negatability | `ActivationNegatable` / `EffectNegatable` (defaults true) |

A bug in "how Damage Step traps are collected" is fixed **once** in
`CollectLegalResponseCards`, not per card.

### 6.1 Finite kinds, then exceptions

Yu-Gi-Oh has many cards and few *kinds* of effects. The interpreter is
`EffectVocabulary`:

| Axis | Closed set |
|---|---|
| Timing | `EffectTiming` (Activate, Flip, triggers, Continuous, Standby, End, Damage) |
| Cost | Discard, Tribute, SendToGy, PayLp, BanishFromGy, RemoveCounters, or none |
| Resolution | Destroy, Banish, bounce, Draw, Search, SS, Damage, LP, stats, position, direct/extra attack, Token, Equip, Control, Counters, Negate, PreventDamage, TreatAsName, Randomize, Protection |

**New card workflow** (`EffectVocabulary.NewCardRule`):

1. Name the timing + cost + resolution from that table.
2. If all three exist, add a PSCT fragment only — **no new `EffectActionKind`**.
3. If a kind is missing and **two or more** cards need it, add one shared kind and a regression.
4. If the mechanic is unique (Cyber Jar, Time Wizard wrong-call, Ring of Destruction), mark `UniqueException` and fail loud. `if (cardId == …)` only when no other card shares it.

The rest of the corpus is exceptions on purpose. That list should stay short and named.

## 7. Once-per-turn

- Default: **per instance** (`CardInstance.EffectUsedThisTurn`), reset on that
  controller's next turn.
- Per-name OPT is not implemented yet — do not fake it with the instance flag.

## 8. Timing language

- **"When"** vs **"if"** is stored on the clause (`ConditionCheckedAt`). Missed
  timing still needs an explicit pending-trigger window (progressive).
- **Damage Step** is its own restricted legality, not a Battle Phase boolean.
  ATK/DEF-modifying traps (Bark of Dark Ruler, Mask of Weakness) are legal there;
  generic Normal Spells are not.

## 9. Validation instead of live-play firefighting

1. `InteractionRegressionTests` / `TcgRegressionTests` — known interactions.
2. `Tools/scan_engine_gaps.py` — cards_db vs compiler vs Lua seeds (linter).
3. `CardEffectStatus` — `structural | implemented | stub | unimplemented`.
4. Simulated play only after (1)–(3). A playtest miss should almost always be
   **data**, not a new engine branch.

## 10. Authoritative sources

`OfficialDataSources` + `cards_db.json` desc + Yugipedia / Konami DB via Firecrawl.
Do not infer a ruling from flavor.

## 11. Fail loud — never silently vanilla

| Status | Meaning | Play |
|---|---|---|
| `structural` | Normal Monster / effectless Extra | Summon / battle |
| `implemented` | Registry, full compile, or Lua catalog | Activate + resolve |
| `stub` | Partial compile | Only compiled clauses; rest refused |
| `unimplemented` | Effect text, no program | **Activation refused.** Log `[UNIMPLEMENTED]`. |

**Deviation (lab):** unimplemented **monsters may still be Normal Summoned/Set
and battle** so the 1556-card pool is not empty. That is *not* "the effect
worked." Deck-builder exclusion (`CardEffectStatus.ExcludeUnimplementedFromDecks`)
stays **off** until lab/starter decks are 100% implemented. Spells/Traps with no
program cannot activate.

An Effect Monster with zero registered clauses is a **schema violation** for the
linter, same class as a missing cost.

## 12. Done

Adding a card is data that passes the gap scan. Playtest "fixes" correct that
card's program. If a card bug requires touching chain/timing/cost core again,
Sections 3–8 were incomplete — not "this card is special."
