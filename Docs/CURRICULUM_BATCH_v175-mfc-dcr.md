# Curriculum batch — v175-mfc-dcr

Date: 2026-10-11. Compiler v174 -> v175. Workflow run: 9 implementer groups in scratch trees, integrated into
the real checkout, audited, finalized. Per-group implementer notes: `~/.cache/wrldz-card-compile/v175-mfc-dcr/patches/*.md`.

## Result

Status table (`status_diff.py`, before -> after):

```
status        before   after
Implemented      819     876  (+57)
Stub              78      60  (-18)
Unimplemented    395     356  (-39)
Structural       425     425  (+0)
```

57 Implemented gained (47 MFC, 10 DCR), 0 losses, no other card changed. Headless `--quiet`: 6211 pass / 0 fail,
40 AI-vs-AI duels, 0 soft-locks, 0 exceptions (`--duels 250`: 250/250 completed, turnCap 0, softLocks 0, exceptions 0,
wins 122/128/0). `csc_overlay.sh` editor + `--release`: OK. `enum_guard.py`: 0 errors, 0 warnings.

## Cards compiled (57)

**DamageStepRules** (`Batches/DamageStepRules*`): D.D. Crazy Beast (battle-destroyed monster banished), Thousand Needles
(destroys a lower-ATK attacker at End of Damage Step), Continuous Destruction Punch (same, continuous), Ultimate Obedient
Fiend (attack lock + reflect), Meteorain (grants piercing), Kishido Spirit (battle indestructible), Cat's Ear Tribe,
Amazoness Swords Woman (reflects battle damage).
**EquipLocksAndLeaveField**: Demotion (host -2 Level), Big Bang Shot (+400, piercing, banishes host when it leaves),
Autonomous Action Unit (Special Summon from opp GY, destroy host on leave), Raregold Armor (attack-target lock),
Metalsilver Armor (host cannot be targeted).
**DcrEquipSpells**: Shooting Star Bow - Ceal (-1000 ATK, direct attack), Twin Swords of Flashing Light - Tryce (-500,
extra attack, send-cost), Wicked-Breaking Flamberge - Baou (+500, negates monsters it destroys), Rod of the Mind's Eye
(battle damage becomes 1000), Rod of Silence - Kay'est (negates host's battle opponent), Butterfly Dagger - Elma (+300,
returns itself from GY to hand).
**SpellCounterSpenders**: Magical Plant Mandragola, Mega Ton Magical Cannon, Miracle Restoring, Anti-Spell, Apprentice
Magician, Armor Exe, Legendary Flame Lord, Pitch-Black Power Stone (side-wide Spell Counter cost and holder model).
**ChainLinkAnswers**: Jar Robber, Spell Shield Type-8 (two bullets), My Body as a Shield, Chaos Command Magician,
Dramatic Rescue (hand Quick-Play answer).
**FlipMassAndOpponentPicks**: The Spell Absorbing Life, Dark Cat with White Tail, Huge Revolution, Magical Merchant,
Pixie Knight, Pineapple Blast, Dimension Jar.
**PhaseTriggersAndIgnitionCosts**: Amazoness Blowpiper, Helping Robo for Combat, Mass Driver (Tribute burn), Senri Eye,
Magical Scientist, Roulette Barrel, Physical Double.
**UnionRiders**: Kiryu, Second Goblin, Burning Beast, Freezing Beast, Metallizing Parasite - Lunatite, Union Rider,
Combination Attack.
**SummonLocksAndStatRules**: Gyaku-Gire Panda, Amazoness Tiger (unique face-up rule + attack-target lock), Amazoness
Spellcaster, Rivalry of Warlords.

## Audit fixes (integration)

- Amazoness Blowpiper ignored Metalsilver Armor and Chaos Command Magician: `OppFaceUpTargets` filters with
  `ShieldedFromSource`, `ApplyAtkLoss` calls `ChainLinkAnswersRuntime.NegatesRegisteredMonsterTarget` (2 new checks).
- Dark Cat with White Tail `CatCandidates` and Pitch-Black `CollectHolderTargets` now honour Metalsilver.
- Combination Attack `SummonableUnions` checks Rivalry (`BlocksFaceUpSummon`); Apprentice Magician Deck candidates
  pass `faceUp: false`.

## Deferred (stay fail-closed)

- **Neko Mane King** (11021521): trigger side expressible, but "It becomes the End Phase of this turn" needs a
  phase-jump primitive. EndTurn is safe only from an idle Main/Battle Phase; the generic idle hook is also reached
  mid-loop (FirePhaseTriggers, BeginTurn/ContinueDrawPhase/ContinueStandbyAfterTriggers, SimpleAi), so a jump would run
  the old turn's remaining triggers in the next turn. Needs gated transition points plus Battle Phase termination.
- **Formation Union** (26931058): bullet 1 needs a second target (the host) filtered by the first pick; bullet 2 needs a
  "Union in Union state in your S/T Zone" zone in both target mirrors. Spell/Trap paths support one target or
  independent groups only.
- **Kaiser Colosseum** (35059553): Lua EFFECT_MAX_MZONE caps only TOFIELD placements by the capped player; control
  changes and the controller's own placements on the opponent's field are exempt. ~58 FirstEmpty sites carry neither
  the placing player nor the reason; Tribute freeing a zone and EFFECT_UNRELEASABLE_SUM need a forced-Tribute hook.

## Skipped by the planner

Missing subsystems: Amazoness Archers, Diffusion Wave-Motion, Staunch Defender (must-attack obligation); Great Phantom
Thief, Archfiend's Oath (card-name declaration picker); Ante, Dragged Down into the Grave (revealed-hand state);
Double Spell (copy another card's effect); Thunder of Ruler (no Standby window for a Set Normal Trap).
Over the cap, next batch: A Deal with Dark Ruler, Agido, Arsenal Robber, Berserk Dragon, Blindly Loyal Goblin,
Checkmate, Contract with Exodia, Dark Scorpion Combination, Desrook Archfiend, Different Dimension Dragon, Different
Dimension Gate, Exodia Necross, Final Attack Orders, Final Countdown, Frozen Soul, Goblin of Greed, Great Maju
Garzett, Guardian Baou, Guardian Elma, Guardian Grarl, Guardian Tryce, Infernalqueen Archfiend, Interdimensional Matter
Transporter, Judgment of Anubis, Kaiser Glider, Kelbek, Keldo, Little-Winguard, Maju Garzett, Mirage Knight, Morale
Boost, Mustering of the Dark Scorpions, Non-Spellcasting Area, Outstanding Dog Marron, Pandemonium, Pandemonium
Watchbear, Ray of Hope, Sasuke Samurai #2, Shadowknight Archfiend, Shinato King of a Higher Plane, Skill Drain, Spell
Reproduction, Spell Vanishing, Terrorking Archfiend, Tsukuyomi, Vilepawn Archfiend.

## Open residuals (fail-soft, not fixed)

- Physical Double ignores Rivalry of Warlords in its activation/target check (the Token Special Summon is then refused;
  no soft-lock). Needs a `CollectTargets` hook for the action.
- `EffectClause.AiEquipsOwnMonster`: `DcrEquipSpellsRuntime.TryAutoPick` runs first, so the EquipLocks
  `|| c.AiEquipsOwnMonster` arm in the FieldAnyMonster `AutoPick` case is dead code (AI quality only).
- Two "send a card from hand" cost primitives: `DiscardCostIsSend` (any card, via `RequiresDiscardCost`) and
  `CostSendSpellsFromHandCount` (Spell only, ChainLinkAnswers). `EngineResidualsRuntime.PayChainDiscard` ignores
  `DiscardCostIsSend` (no card reaches it today).
- Order of `DamageStepRulesRuntime.ReflectBattleDamage` before `DcrEquipSpellsRuntime.ChangeBattleDamage` (Swords Woman
  vs Rod of the Mind's Eye) not checked against ocgcore.
- My Body as a Shield's would-destroy check ignores `ExceptThisCard` (harmless for Legendary Flame Lord today).
- Union-state ignitions (Kiryu, Second Goblin) resolve as activated, with no opponent chain window.

## Verify

```
bash Tools/CardCompile/headless.sh . --quiet
bash Tools/CardCompile/headless.sh . --card "Rivalry of Warlords"
bash Tools/CardCompile/csc_overlay.sh . ; bash Tools/CardCompile/csc_overlay.sh . --release
python3 Tools/CardCompile/enum_guard.py
```
New tests: `Rules/Batches/*RegressionTests.cs` for the nine groups (one call line each in `CardBatchRegressionTests.Run`).
