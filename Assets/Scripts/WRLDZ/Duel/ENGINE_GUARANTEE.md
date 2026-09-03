# Duel logic guarantee

## Two layers

| Layer | Always works | Source |
|-------|----------------|--------|
| **Structural TCG** | NS/Set, tributes, phases, battle math, direct attacks, hand size, first-turn rules | Konami Rulebook via `DuelEngine` |
| **Learned text effects** | PSCT templates compiled **offline** into cache/seed (`AllowRuntimeAi` default **off**; no first-play model hitch) | `TextEffects/*` + `cards_db.json` desc |
| **Legacy scripts** | Hardcoded staples if text path does not cover | `SpellTrapEffects` + `MonsterEffects` |

**Activations:** learn/compile official text → run remembered program → else legacy → else refuse (never invent).  
**Monsters:** still Summon/Set/battle always; Flip/GY text learned the same way.  
**Status:** `CardEffectStatus` (`structural | implemented | stub | unimplemented`). Stub/unimplemented effect cards are refused at **deck** (`MayIncludeInDeck`); a deck-legal card’s live Activate never logs `[UNIMPLEMENTED]` — only real YGO refusals (timing, cost, target, OPT). Founding spec: `MASTER_ENGINE_SPEC.md`.

**AI policy:** SpaceXAI is **offline compile only** (Editor/CI bulk); `AllowRuntimeAi` defaults **off**. Live resolution uses `TextEffectRuntime` + cache — no model calls mid-duel. See **`TextEffects/TEXT_EFFECT_PIPELINE.md`**.

## Open-source reference

Local clone: `~/ygopro-scripts` (YGOPro / EDOPro-style Lua).  
**Lab:** `OcgLabDuelHost` may link edo9300 ocgcore (AGPLv3, isolated folder).  
**Product path:** C# `DuelEngine` still owns legality when the lab host is off.  
See `MASTER_ENGINE_SPEC.md` §0 and `OCGCORE_LAB.md`.

## Lab TEST DUEL

Decks: `StreamingAssets/Decks/lab_rules_player.json` + `lab_rules_ai.json`

- Every Spell/Trap ID is fully scripted  
- Flip monsters included: Man-Eater Bug, Magician of Faith  
- Sangan field→GY search  
- Lord of D. targeting lock  
- Gaia + Curse + Polymerization / Extra Gaia the Dragon Champion  

Map exit returns to Boot for another run.

**Seed bulk:** Editor menu **WRLDZ → Text Effects → AI Bulk Compile Lab Decks + Export Seed** writes  
`StreamingAssets/WRLDZ/compiled_effects_seed_v1.json` so devices learn without an API key.

## Soft-lock recovery

`DuelEngine.RecoverStuckCombat` / `TryEndTurnSafe`:

- Empty response windows auto-pass  
- Stuck attack declarations finish damage calculation  
- Deferred Flip battle finish if target already cleared  
- UI `Update` calls recovery if attack is open with no window  

**Command gateway (priority 1):** every `DuelCommandService.Execute`  
1. rejects re-entrant calls (`IsProcessingAction`)  
2. try/catch → returns Fail instead of freezing  
3. **finally** always runs recovery + clears the action lock  


## Flip by battle

Damage Step Start flips face-down Defense targets face-up.  
**After damage calculation**, Flip effects fire (`MonsterEffects.OnFlipSummoned`) — Man-Eater Bug, Magician of Faith, Cyber Jar — even if the Flip monster would be destroyed by battle.  
Battle GY is **deferred** while the player chooses a Flip target.  

**Lord of D.:** face-up Lord of D. makes **Dragon** monsters illegal targets for card effects (including himself). Man-Eater Bug cannot destroy Lord of D. while that continuous effect applies — target the attacker or a non-Dragon instead.  

## Stress tests

Editor: **WRLDZ → Rules → Run Engine Stress Tests**  
Batch: `-executeMethod WRLDZ.EditorTools.DuelStressMenu.RunStressBatch -stressDuels 80`

Covers: `TcgRegressionTests`, **`InteractionRegressionTests`** (Sangan search, Flip targets, center zones, Pot of Greed, Kuriboh), lab text compile, AI-vs-AI lab duels (both sides via `DuelStressAgent`), battle-math fuzz.  
Report: `wrldz_stress_report.txt` in the project root after batch.

**Interaction rule:** registered Flip / field→GY scripts run **before** text cache so seed/cache never silent-no-ops staples (Sangan, Man-Eater Bug, Magician of Faith, Cyber Jar).

## Duel review log (AI learning)

Live panel **DUEL REVIEW LOG (AI)** during every duel (HIDE / SAVE).  
Auto-export on game over:

- `persistentDataPath/WRLDZ/duel_reviews/duel_*.jsonl` (structured events)  
- matching `.txt` transcript  

Kinds: PHASE · SUMMN · ACT · ATK · REACT · AI · DENY · BOARD · END · …  
 


## Adding a new card effect

1. Read official text in `cards_db.json`  
2. Optionally compare `~/ygopro-scripts/c{passcode}.lua`  
3. Prefer: template in `CardTextEffectCompiler` + arm in `TextEffectRuntime`  
4. Or: offline AI bulk compile onto existing action enums (validated)  
5. Fallback: implement in `SpellTrapEffects` / `MonsterEffects` + `OfficialEffectRegistry`  
6. Add a regression check when practical  
