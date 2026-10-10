# Curriculum batch — v174-mfc (MfcSimpleSpellsB)

Date: 2026-10-09. Compiler v173 → v174. Single-agent run (no workflow agents available in
this session); implemented directly in the real checkout, gated by the playbook tools.

## Result

| Set | Before | After |
|-----|-------:|------:|
| MFC — Magician's Force | 36/108 (33.3%) | **38/108 (35.2%)** |
| All 15 pre-Link curriculum sets + rest | 817 Implemented / 397 Unimplemented | **819 / 395** (0 losses) |

Headless `--quiet`: **5896 pass / 0 fail**, 40 AI-vs-AI duels completed, 0 soft-locks,
0 exceptions. `csc_overlay.sh` editor + release: OK. `status_diff.py`: only the 2 intended
cards changed. `enum_guard.py` vs HEAD reports 4 errors + 2 warnings — all pre-existing
mid-enum reorderings from peers' uncommitted work (EffectTiming, EffectResolutionKind,
EffectTargetKind, DuelIntentKind, and the peers' CompiledCardProgram rework); this batch's
own hunks are pure tail appends (verified in the diff).

## Cards compiled (2)

- Emblem of Dragon Destroyer (6390406): add 1 "Buster Blader" from the Deck or the GY to
  the hand (Deck first, then GY — the Lua's LOCATION_DECK + LOCATION_GRAVE order).
- Hidden Spellbook (21840375): during your turn, target 2 Spell Cards in your GY; shuffle
  those targets into the Deck (then shuffle once).

## Shared pieces added

- **EffectActionKind.AddNamedFromDeckOrGyToHand** (+ EffectZoneFilter.ControllerDeckOrGyNamed):
  Deck-or-GY named search. Deck ghosts and GY instances are both offered; resolution takes
  the Deck copy first, else the GY copy (Necrovalley-gated). Quirk: the pick is nominal —
  if both zones hold a copy, the Deck copy is taken regardless of which was picked. Either
  satisfies the printed text.
- **EffectActionKind.ShuffleGySpellsIntoDeck**: each target leaves the GY for the Deck, then
  one shuffle. Mapped to EffectResolutionKind.ReturnToDeck (the switch default of
  UniqueException would mislead AI heuristics).
- **EffectActionKind.ForbidBattlePhaseThisTurn**: RESERVED, unused — see Deferred.
- **EffectVocabulary**: AddNamedFromDeckOrGyToHand → Search.
- **Corpus Provision**: seats the named Deck card for ControllerDeckOrGyNamed zones (the
  shared 8-Celtic board can never satisfy a named search; cf. Graverobber precedent).

## Deferred (Thunder of Ruler stays fail-closed — deliberate, audited)

Thunder of Ruler (91781589, "During your opponent's Standby Phase: You can activate this
card; there is no Battle Phase this turn") was drafted, compiled FullyCompiled in a probe,
then reverted. Its only timing is the opponent's Standby Phase and the engine has no
activation path for a Set Normal Trap on that timing: FirePhaseTriggersFor skips face-down
cards, and CanActivateOfficial refuses manual activation of a program with no Activate
timing. Shipping it Implemented would put a dead, unactivatable card into decks — the exact
harm the fail-closed rule exists to prevent. Queued until a Standby priority window for the
non-turn player exists. Its MfcSimpleSpells pin is kept; the reserved enum member + runtime
arm (Soul Exchange oath stamp reuse) stay for that future work.
Mass Driver and Anti-Spell stay fail-closed per the earlier WholeResolution/SpellCounter gates.

## Tests (Rules/Batches/MfcSimpleSpellsBRegressionTests.cs, 7 checks, all passing)

Compile shapes for both cards (timing/action/zone/TargetCount/NamedCard/ResolutionOf) plus
mini-duels: Emblem Deck search, Emblem GY fallback (same instance returns), Emblem refused
with no copy anywhere, Hidden Spellbook 2-target shuffle (Deck +2), Hidden Spellbook refused
with 1 Spell in GY. Two gate failures found on the way and fixed: (1) the Hidden duel
drove the compiled-pending pick with the cancelling Drive helper — switched to the explicit
TargetCount pick idiom (pass-responses-only settle, Pick/Pick); (2) the corpus stress
activates every FullyCompiled Spell on the shared board, which cannot satisfy a named
search — fixed with the Provision above, not a skip.

## Arena FX

Both families (Deck/GY search, GY-to-Deck shuffle) have no named look — FX_MAP.md family
rows "Draw / mill / search" and "Destroy / banish / send" are Gaps. Acceptable generic for
now (arrival + log line; no new SummonKind path): recorded here instead of a new row, per
the per-group checklist. No files owned by other sessions touched.

## Files

- Duel/TextEffects/Batches/MfcSimpleSpellsBAtoms.cs (+ .meta): 2 whole-sentence atoms.
- Duel/TextEffects/Batches/MfcSimpleSpellsBRuntime.cs (+ .meta): convention stub (all three
  arms resolve inline in TextEffectRuntime).
- Duel/TextEffects/CompiledCardProgram.cs: 3 EffectActionKind + 1 EffectZoneFilter appended.
- Duel/TextEffects/TextEffectRuntime.cs: 3 ApplyClause arms + 1 CollectTargets case.
- Duel/TextEffects/EffectVocabulary.cs: 2 ResolutionOf mappings.
- Duel/TextEffects/AtomRegistration.cs: 1 BatchAtomSets line.
- Duel/Rules/Batches/MfcSimpleSpellsBRegressionTests.cs (+ .meta): 7 checks; 1 Run line in
  CardBatchRegressionTests; MfcSimpleSpells pins updated (Emblem/Hidden moved here).
- Duel/Rules/CorpusTriggerStressTests.cs: named-card Provision.
- Seed re-exported at v174 (912 programs; +Emblem/Hidden; Thunder correctly absent).
