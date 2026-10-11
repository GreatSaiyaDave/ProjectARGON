# Curriculum batch — v176-dcr-ioc

Date: 2026-10-11. Compiler v175 -> v176. Workflow run: 9 implementer groups in scratch trees, integrated into the real
checkout, audited, finalized. Per-group implementer notes: `~/.cache/wrldz-card-compile/v176-dcr-ioc/patches/*.md`.

## Result

```
status        before   after
Implemented      876     935  (+59)
Stub              60      49  (-11)
Unimplemented    356     308  (-48)
Structural       425     425  (+0)
```

59 Implemented gained (DCR and IOC), 0 losses, no other card changed (seed diff: 11 programs changed, 48 added, none outside
the batch). Headless `--quiet`: 6536 pass / 0 fail, 40 duels; `--duels 250`: 250/250 completed, turnCap 0, softLocks 0,
exceptions 0. `csc_overlay.sh` editor + `--release`: OK. `enum_guard.py`: 0 errors, 0 warnings. No peer landings.

## Cards compiled (59)

**SimpleTriggersAndSwarm**: Tsukuyomi (Normal/Flip trigger, may target itself), Burning Algae and Outstanding Dog Marron
(new `SentToGyFromAnywhere` timing), Little-Winguard (optional End Phase position change), Don Turtle,
Mustering of the Dark Scorpions, Dark Scorpion Combination.
**DcrSpellTrapUtility**: Ray of Hope, Spell Reproduction (send 2 Spells as cost), Frozen Soul (skip Battle Phase),
Arsenal Robber, Interdimensional Matter Transporter (banish until End Phase), Dragged Down into the Grave,
Final Countdown (win after a turn countdown), Different Dimension Gate.
**BattleDestroyedTriggers**: Kelbek, Keldo, Agido (die roll Special Summon from GY), Kaiser Glider,
Different Dimension Dragon, Shinato King of a Higher Plane, Des Kangaroo.
**GuardiansAndEquipEvents**: Guardian Elma and Chopman the Desperate Outlaw (equip an Equip Spell from the GY),
Guardian Grarl (own hand procedure), Guardian Baou, Fuhma Shuriken, Morale Boost (EVENT_EQUIP / leave-field triggers).
**DerivedAtkMonsters**: Great Maju Garzett, Maju Garzett (ATK from Tributes' printed ATK), Guardian Tryce,
Chaos Necromancer (ATK set from GY count), Gren Maju Da Eiza (ATK from banished count).
**NamedSummonPairs**: A Deal with Dark Ruler, Berserk Dragon, Contract with Exodia, Exodia Necross, Mirage Knight,
Dedication through Light and Darkness.
**ArchfiendRiders**: Shadowknight Archfiend, Terrorking Archfiend, Vilepawn Archfiend, Infernalqueen Archfiend,
Desrook Archfiend, Checkmate, Pandemonium, Pandemonium Watchbear.
**SpellTrapAnswersAndActivationLocks**: Judgment of Anubis, Spell Vanishing, Cursed Seal of the Forbidden Spell,
Trap Jammer, Sasuke Samurai #2, Invader of Darkness.
**MonsterNegationAndControlLocks**: Skill Drain, Final Attack Orders, Blindly Loyal Goblin, Mataza the Zapper,
Stumbling, Non-Spellcasting Area.

## Audit fixes (integration)

- Flaky Archfiend test (Fissure + Pandemonium): a Terrorking die roll negated Fissure about 1 time in 3. The test now queues
  a die of 1 (`ArchfiendRidersRegressionTests.cs`).
- Pandemonium's search ignored Sasuke Samurai #2's Spell/Trap lock: `ArchfiendRidersRuntime.SearchesOffered` now calls
  `LocksAndZonesRuntime.SpellTrapTriggerLocked` (+1 check).
- Morale Boost's two triggers ignored Spell/Trap locks (Sasuke #2, Cold Wave): both queue loops in
  `GuardiansAndEquipEventsRuntime` skip when `SpellTrapTriggerLocked` (+1 check).

## Deferred (stay fail-closed)

- **Goblin of Greed** (425934): needs an engine-wide "cannot discard as a cost" check per player and effect at every
  discard-cost site (TextEffectRuntime CanActivate / chain answer / free chain on chain / ResponseDiscardPayable /
  IgnitionCostLegal / RequiresDiscardSelf, TargetNegationsAndHandQuickEffects, generalized hand traps,
  PayChainDiscard / AskChainAnswerDiscard, the DuelEngine Counter Trap discard, and the batch payers in
  ContinuousTrapsAndSpiritSupport, BattleBoosts, BattleDestroyedGyRecovery, FaceUpQuickEffects). Sending from hand
  (DiscardCostIsSend, Spell Shield) is not a discard and must stay allowed. `ActivationLocked` works per card, not per
  effect. A missed site would make the card wrong; a test pins it Unimplemented.

## Skipped by the planner (engine gaps, see ENGINE_MAP section 4)

Amazoness Archers, Diffusion Wave-Motion, Staunch Defender (must-attack obligation); Ante (simultaneous hidden pick);
Double Spell (copy another card's effect); Formation Union (second target filtered by the first); Great Phantom Thief,
Archfiend's Oath (card-name declaration picker); Kaiser Colosseum (max Monster Zone cap); Neko Mane King (phase jump);
Thunder of Ruler (Standby Phase window); Dark Magician of Chaos (per-card leave-field redirect). 50 more cards were over the
batch cap and go to the next batch (A Hero Emerges, Balloon Lizard, Begone Knave!, Berserk Gorilla, Big Burn, Black Luster
Soldier - Envoy of the Beginning, Blasting the Ruins, Chaos Emperor Dragon - Envoy of the End, Chaos End, Chaos Greed, ...;
the full list is in `~/.cache/wrldz-card-compile/v176-dcr-ioc/worklist.md`).

## Open audit residuals (not fixed)

- Archfiend die-roll protection (`TextEffectRuntime.ApplyClause`, ArchfiendTargetNegation guard) treats Fissure's pick as a
  target (`RequiresTargetChoice && !ChoiceDoesNotTarget`); Fissure does not target. Aligning with `IsPsctTarget` needs its
  own audit of every old-text "choose" clause.
- Three "cannot be destroyed by Spell/Trap" paths: `CannotBeDestroyedBySpellTrapEffects`,
  `...ThatDoNotTargetThis`, and `EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction`. Consolidation candidate.
- Two ways to ask the face-up position of a Special Summon from the GY: Desrook's option-token pick and
  `CardPickRequest.AsksSpecialSummonPlacement`.
- DerivedAtkMonsters' board refresh after a battle flip applies Final Attack Orders before damage calculation; unverified
  against ocgcore.
- Little-Winguard's position change ignores Final Attack Orders' position lock (same end position; misleading log).

## How to verify

```
Tools/CardCompile/headless.sh . --quiet
Tools/CardCompile/headless.sh . --card "Skill Drain"
python3 Tools/CardCompile/enum_guard.py --repo .
bash Tools/CardCompile/csc_overlay.sh . ; bash Tools/CardCompile/csc_overlay.sh . --release
```
