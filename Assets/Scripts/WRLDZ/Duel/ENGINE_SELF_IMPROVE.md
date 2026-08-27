# Duel Engine — Training & Self-Improvement Plan

## What broke (Kuriboh case study)

**Symptom:** Player “used” Kuriboh’s effect; card never left the hand.

**Root causes found:**

1. **UI gate bug (`DuelUI.DoActivate`)** — Hand Activate was only allowed when  
   `!_engine.IsAwaitingResponse`. Damage Calculation *is* a response window, so  
   the UI offered **Activate** on hand Kuriboh but `DoActivate` never submitted  
   the command. Fixed: hand activate works in open game state *and* response windows.
2. **Reference mismatch risk** — Discard used `Hand.Remove(card)` by reference.  
   UI/intent clones can fail silently. Fixed: resolve LegalCards → live Hand instance  
   by `InstanceId`, plus `DiscardFromHandByInstance`.
3. **Coverage model** — Only a handful of IDs are hard-coded in  
   `MonsterEffects` / `SpellTrapEffects`. The pool in `cards_db.json` is large;  
   most Effect Monsters are Normal Summonable but have **no scripted resolution**.

**PSCT for Kuriboh (authority):**  
*During damage calculation, if your opponent's monster attacks (Quick Effect):  
You can discard this card; you take no battle damage from that battle.*  
Cost before semicolon = discard. Operation = no battle damage this battle.

---

## How the engine works today

```
cards_db.json (Konami text)
        │
        ▼
 OfficialCardAuthority ──► CardTextEffectCompiler (regex templates)
        │                         │
        │                         ▼
        │                  CompiledEffectCache (disk + seed)
        │                         │ optional SpaceXAI offline compile
        ▼                         ▼
 MonsterEffects / SpellTrapEffects / TextEffectRuntime
        │
        ▼
 DuelEngine (timing windows, chain, battle math)
        │
        ▼
 DuelCommandService ← UI / voice / AI
```

| Layer | Role | Live LLM? |
|-------|------|-----------|
| Structural rules | Turn, summon, damage calc | No |
| Hard-coded scripts | Staples + Kuriboh DC | No |
| Text program cache | Regex/AI-compiled clauses | Compile only (not in duel) |
| `AiMasteryTrainer` | Self-play duels + JSONL | No |
| `AiHeuristicPolicy` | Heuristic lines from play | No |

Live duels **must not** call an LLM for resolution. Only cached programs execute.

---

## Why “it doesn’t understand all cards”

1. **Sparse scripts** — Registry lists ~15 Spells/Traps + ~6 monster effects.  
2. **Regex coverage** — Templates match common PSCT shapes (Draw, Raigeki, Flip destroy…).  
   Rare wording fails → `FullyCompiled=false` → unparsed fragments.  
3. **No universal interpreter** — Unparsed text is correctly **not invented**  
   (`OfficialEffectRegistry` rejects activation).  
4. **AI compile is optional** — `AiEffectCompiler` can fill gaps offline;  
   seed file is currently nearly empty.  
5. **UI timing bugs** — Even correct scripts fail if the command path is gated wrong  
   (Kuriboh).

---

## Improvement pillars

### A. Correctness first (immediate)

| Action | Benefit |
|--------|---------|
| Fix response hand-activate UI | Hand QEs actually fire |
| InstanceId discard / cost helpers | Costs always paid |
| Regression: discard + 0 damage | Never regress Kuriboh |
| PSCT template for Kuriboh-style | Text compiler learns the shape |
| Expand DC hand QEs (Honest later) | Same timing window |

### B. Effect coverage pipeline (self-improving knowledge)

```
cards_db.desc
  → bulk compile (Editor menu)
  → FullyCompiled?  ──no──► AI offline compile (schema-validated)
  → EffectProgramValidator
  → seed + persistent cache
  → coverage report (% of pool)
```

- **Never** invent mid-duel.  
- **Do** grow the cache offline from official text.  
- Measure: `% of Effect Monsters with FullyCompiled` per set (`CardEraCurriculum`).

### C. Self-play mastery (self-improving *play*)

Already: `AiMasteryTrainer` + `DuelReviewLog` JSONL.

Extend:

1. **Outcome tags** — illegal attempt, soft lock, effect fail, unexpected LP.  
2. **Heuristic updates** — from review: “prefer Kuriboh when LP would hit 0”.  
3. **Curriculum** — only promote AI difficulty when set coverage ≥ threshold.  
4. **Counterfactual tests** — replay recorded attack with/without Kuriboh;  
   assert discard + LP.

### D. Authority loop (official data)

```
YGOPro Lua / Yugipedia / Konami text
  → OfficialDataSources.OfficialCardRecord
  → passcode-keyed scripts
  → banlist_advanced.json
```

Port Lua patterns for high-traffic IDs; keep C# as runtime.

### E. Observability

- Duel log lines must always include **Hand/GY counts** after costs (done for Kuriboh).  
- Desktop Lab: “Effect coverage” panel — unscripted IDs in current decks.  
- Assert: activating an effect with a cost never leaves the card in the same zone.

---

## Recommended roadmap

| Phase | Goal |
|-------|------|
| **0 (done)** | Kuriboh UI + discard fix + discard unit check |
| **1 (done)** | Bulk compile seed for starter + lab decks; coverage % report — see `EffectCoverageService` |
| **2 (in progress)** | Effectless Fusions = structural cover; mastery **coverage gate** (100% playable on starter+lab); expand staples |
| **3** | AI offline compile for remaining LOB–LOD wording / curriculum sets |
| **4** | Self-play mastery: review → heuristic promotion (only when gate PASS) |
| **5** | Damage Step complete (before/after DC windows beyond Kuriboh) |

**Effectless Extra Deck** (Black Skull Dragon, Gaia the Dragon Champion, …) use  
`OfficialCardAuthority.HasNoActivatableEffect` — materials text only, no activation script needed.

### Phase 1 how-to

```
WRLDZ → Text Effects → Phase 1 — Compile Starter+Lab + Report + Seed
# or batch:
Unity -batchmode -nographics -projectPath … \
  -executeMethod WRLDZ.EditorTools.EffectCoverageMenu.RunPhase1Batch -quit
```

Outputs:
- `StreamingAssets/WRLDZ/compiled_effects_seed_v1.json` — shipped programs  
- `persistentDataPath/WRLDZ/reports/effect_coverage_starter_and_lab_latest.txt`  
- `Tools/WRLDZ/effect_coverage_phase1_latest.txt` (batch copy)

---

## How to train / verify locally

```
WRLDZ → Rules → Run TCG Regression Tests
WRLDZ → AI → Run Pre-Link Mastery Pass
WRLDZ → Text Effects → Bulk Compile (Editor)
```

Manual: Instant Duel → get attacked with Kuriboh in hand → Damage Calculation →  
**Activate** on Kuriboh → log should show discard → GY; hand count −1; 0 battle damage.

---

## Design constraints (do not violate)

1. **Konami text is law** — `CardDef.desc` / OfficialCardAuthority.  
2. **No live LLM rulings** — only cached programs.  
3. **Fail closed** — unscripted effects do not invent resolution.  
4. **Single engine** — AR/UI command via `DuelCommandService` / `IDuelEngine`.  
5. **Costs before semicolon** — always mutate zones before applying the operation.
