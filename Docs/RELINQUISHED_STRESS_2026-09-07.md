# Relinquished stress — 2026-09-07

**Verdict (Relinquished scope): PASS** on all InteractionRegression + ritual harness claims.  
**Suite overall: FAIL (ok=NO)** — `unitFail=64` from other parked/corpus cards (not Relinquished).  
**Damage-reflect clause:** still **UNPROVEN** (compiler `MarkAbsorbed` — no harness assert for equal effect damage).

## Card
- Id: `64631466`
- Text (cards_db): Ritual via Black Illusion Ritual. OPT equip 1 opp monster (max. 1). ATK/DEF = equipped. Battle-destroy → destroy equipped instead. While equipped, battle damage you take from battles involving this card inflicts equal effect damage to opponent.

## Live command
```
~/bin/stress-card Relinquished
# → Unity -batchmode -executeMethod WRLDZ.EditorTools.DuelStressMenu.RunStressBatch -stressDuels 2
```
- Report: `~/backup-bot/context/eval/stress/wrldz_stress_report_20260907_182620.txt`
- Markdown extract: `~/backup-bot/context/eval/stress/stress_Relinquished_20260907_182620.md`
- Elapsed ~6s units + 2 AI duels; `exceptions=0` `softLocks=0`

## Relinquished expected vs actual

| Claim | Expected | Actual |
|-------|----------|--------|
| Ritual monster + summon gate | NS lock + HasSummonProcedure | **PASS** |
| Absorb FullyCompiled / ProgramMayActivate | true | **PASS** |
| Activate absorb (Celtic) | OPT equip resolves | **PASS** |
| Equip leaves opp field | prey equipped / off field | **PASS** |
| ATK = equipped printed ATK | match | **PASS** |
| Second absorb same turn | refused (OPT) | **PASS** |
| Battle substitute | destroy equipped; Relinquished stays | **PASS** |
| Negated absorb | Relinquished dies to battle | **PASS** |
| Black Illusion Ritual SS | Greater 1 works | **PASS** |
| Contract with the Abyss SS | Equal 1 DARK works | **PASS** |
| Earth Chant vs Relinquished | refuses DARK | **PASS** |
| Battle damage reflect to opponent | equal effect damage while equipped | **UNPROVEN** — `RxRelinquishedDamageReflect` is **MarkAbsorbed** in `CardTextEffectCompiler`; no PASS/FAIL line in this run |

## Other FAIL highlights (next stress targets — Dilbot parked / not Relinquished)

From same report (InteractionRegression / corpus):
- Fissure activate/destroy targeting
- Rush Recklessly +700 ATK until EOT
- Call of the Haunted SS + destroy-linked
- Corpus: FullyCompiled Spells Activate on legal board (Book of Life, rituals list, …)

Full suite: **844 passed / 61 failed** (Interaction) + **9/1** corpus → **unitPass=1171 unitFail=64**.

## Local Backup-bot advance path
- GTK now runs **real** work: `/stress Relinquished` → `~/bin/stress-card` (not a paraphrase).
- Reopen Desktop **Backup-bot** to load the updated app.
- Do **not** touch Dilbot Reborn WIP (`DuelEngine` / `SpellTrapEffects` / `TextEffectRuntime` dirty).

## Next concrete action
1. Optional: add a harness assert for Relinquished damage-reflect (or confirm MarkAbsorbed intentional with Dilbot/Fanbot).
2. Stress next FAIL cluster: **Fissure** or **Call of the Haunted** (user pick), via `/stress` after we extend card focus — or I run the same suite filter.
