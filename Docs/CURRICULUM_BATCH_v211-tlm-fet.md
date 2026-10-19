# Card batch v211-tlm-fet (compiler v211)

Compiler `CardTextEffectCompiler.Version` = **211** (was 210). Seed re-exported (`--export-seed`); diff in the cache dir `seed_diff.txt`.
Previous batch: `Docs/CURRICULUM_BATCH_v209-ast-finish.md` (v210 note: card batch v210-rds-fet, 45 cards, in the Version line).
Playbook `Tools/CardCompile/README.md`, workflow `.claude/workflows/compile-card-batch.js`, map `Tools/CardCompile/ENGINE_MAP.md`.

62 batch cards Implemented in 8 groups (TLM focus plus the soft/hard "once per turn" QC). Tests: `Rules/Batches/<Group>RegressionTests.cs`.

## What landed
### OncePerTurnSoft (20, QC; no status change)
Sasuke Samurai #2 now sets `OncePerTurn` (PerInstance) so its ignition cannot repeat. Labyrinth of Nightmare and Vampire Lord get clause-field fixes (`FiresAtEachPlayersPhase` etc.). The other 17 cards (Bowganian, Lava Golem, Exodia Necross, Pumpking the King of Ghosts, Tsukuyomi, Dice Re-Roll, Ectoplasmer, Messenger of Peace, Imperial Order, Legendary Fiend, Blind Destruction, Darklord Marie, Mask of the Accursed, Mirage of Nightmare, Needle Wall, Nightmare Wheel, Ominous Fortunetelling) have an inherent once per turn (Standby/End Phase trigger, etc.), pinned by `OncePerTurnSoftRegressionTests.InherentOncePerTurn` (passcode -> reason), which the OncePerTurnHard corpus guard exempts.
### TlmBattleShieldsAndBurnZero (5)
Monk Fighter (PreventControllerBattleDamage, only its battles), Hieracosphinx (face-down Defense monsters cannot be attack targets), Grave Ohja (face-down-Defense shield + 300 burn per own Flip Summon, queued trigger), Des Wombat (effect damage to you becomes 0), Kozaky's Self-Destruct Button (burn when a destroyed card goes to the GY).
### TlmSpellTrapAnswersAndAttackLocks (6)
Royal Surrender / Spell-Stopping Statute (negate + destroy a Continuous Trap / Spell activation), Mid Shield Gardna (negate a Spell targeting it, send it to the GY, then flip), Ancient Gear Soldier / Golem / Beast (opponent cannot activate Spell/Trap while this card attacks).
### TlmDerivedStatsAndProcedures (7)
Ultimate Insect LV7 (aura via the LV chain), Millennium Scorpion, Batteryman AA, Lost Guardian, King of the Skull Servants (set-ATK per count + GY revival by banishing), Master Monk (Special Summon procedure by Tributing a named monster), Megarock Dragon. The Master Monk procedure atom also gives Thousand-Eyes Jellyfish (81434470, already Implemented) its "Tribute 1 Ocean's Keeper" procedure; checked, kept.
### TlmCostLinkedSpells (6)
Battery Charger (pay 500 LP, revive a Batteryman), Minefield Eruption, Card of Sanctity (banish all other hand and field cards as cost), Gift of the Martyr, Double Attack, Hero Signal.
### TlmEquipsAndLinkedContinuous (5)
Legendary Black Belt, Nitro Unit, Lone Wolf, Impenetrable Formation, Kaminote Blow.
### TlmFlipsControlAndZoneEvents (5)
White Ninja (new zone `FieldDefensePositionMonsters`), Dummy Golem (opponent picks, control swap), Reshef the Dark Being (discard a Spell, take control until End Phase), Criosphinx (owners of bounced monsters discard), D.D. Survivor.
### OncePerTurnHard (8)
`IsBoilerplate` no longer drops `You can only use this/the/each effect of "X" once per turn.`; `StampNameOpt` stamps `OncePerTurn` + `PerCardName` only on shapes the runtime enforces. Sangan, Witch of the Black Forest, Guerilla Kite, Second Coin Toss, Garbage Ogre, RAM Clouder, Golden Dragon Summoner, Satellarknight Alsahm.

## Status changes outside the 62
- Losses (intended fail-close, Implemented -> Stub; the hard-OPT sentence is no longer boilerplate and exposed dropped costs/riders): Lightning, Dragon Ruler of Drafts (second discard, cannot-attack rider); Rose Lover (GY ignition, Trap-immunity rider); Valerifawn, Mystical Beast of the Forest (target filter, position choice); Xyz-Raypierce (banish cost, per-effect OPT count).
- Reverse Buster and Ocean Dragon Lord - Neo-Daedalus: Unimplemented -> Stub (share the Ancient Gear lock / Master Monk procedure sentences).
- Thousand-Eyes Jellyfish keeps Implemented but gains the Ocean's Keeper procedure (program changed).
- Fairy Cheer Girl, Herald of Pure Light, Battlecruiser Dianthus: Xyz detach cost dropped by the generic ignition parser, stay Structural.

## Deferred / skipped (for the next batch)
- Rose Lover: needs activation from the GY (CanActivate accepts only field/hand monsters) plus the Trap-immunity rider.
- Fairy Cheer Girl, Herald of Pure Light, Battlecruiser Dianthus: Xyz detach cost in the PSCT ignition parser; Herald also has "then shuffle 1 card from hand into the Deck"; Dianthus "200 for each card in their hand" (per-count).
- Valerifawn: target filter (Level 2 or lower Beast, not its own name) and Attack / face-down Defense choice.
- Lightning, Dragon Ruler of Drafts: second discard and "cannot attack this turn".
- Xyz-Raypierce: banish 1 Dragon + 1 Wyrm from GY cost; one OPT count per effect.
- Behemoth the King of All Animals: 0/1-Tribute Normal Summon choice (DuelUI lacks it) plus "return Beasts equal to Tributes".
- Elemental Mistress Doriado: EFFECT_ADD_ATTRIBUTE; `CardInstance.Attribute` is one string (about 39 string.Equals sites in 14 files).
- Shifting Shadows, Level Conversion Lab, Grave Lure, Pikeru's Second Sight: over the batch cap.
- Audit leftovers: see ENGINE_MAP section 4 "Open residuals" (Ominous Fortunetelling optional vs mandatory, generic ignition parser looseness, remaining IsBoilerplate drops, `RxStandbyTurnPlayerTakesDmg` missing `FiresAtEachPlayersPhase`).

## Status table
```
status        before   after
Implemented     1270    1300  (+30)
Stub             523     525  (+2)
Unimplemented   2854    2822  (-32)
Structural      1563    1563  (+0)

gained (36):
  Ancient Gear Beast (10509340, TLM): Unimplemented -> Implemented
  Ancient Gear Golem (83104731, TLM): Stub -> Implemented
  Ancient Gear Soldier (56094445, TLM): Unimplemented -> Implemented
  Battery Charger (61181383, TLM): Unimplemented -> Implemented
  Batteryman AA (63142001, TLM): Unimplemented -> Implemented
  Card of Sanctity (42664989, TLM): Unimplemented -> Implemented
  Criosphinx (18654201, TLM): Unimplemented -> Implemented
  D.D. Survivor (48092532, TLM): Unimplemented -> Implemented
  Des Wombat (9637706, TLM): Unimplemented -> Implemented
  Double Attack (34187685, TLM): Unimplemented -> Implemented
  Dummy Golem (13532663, TLM): Unimplemented -> Implemented
  Gift of the Martyr (98792570, TLM): Unimplemented -> Implemented
  Grave Ohja (40937767, TLM): Unimplemented -> Implemented
  Hero Signal (22020907, TLM): Unimplemented -> Implemented
  Hieracosphinx (82260502, TLM): Unimplemented -> Implemented
  Impenetrable Formation (96631852, TLM): Unimplemented -> Implemented
  Kaminote Blow (97570038, TLM): Unimplemented -> Implemented
  King of the Skull Servants (36021814, TLM): Unimplemented -> Implemented
  Kozaky's Self-Destruct Button (21908319, TLM): Unimplemented -> Implemented
  Legendary Black Belt (96458440, TLM): Stub -> Implemented
  Lone Wolf (82452993, TLM): Unimplemented -> Implemented
  Lost Guardian (45871897, TLM): Unimplemented -> Implemented
  Master Monk (49814180, TLM): Unimplemented -> Implemented
  Megarock Dragon (71544954, TLM): Unimplemented -> Implemented
  Mid Shield Gardna (75487237, TLM): Stub -> Implemented
  Millennium Scorpion (82482194, TLM): Unimplemented -> Implemented
  Minefield Eruption (85519211, TLM): Unimplemented -> Implemented
  Monk Fighter (3810071, TLM): Unimplemented -> Implemented
  Nitro Unit (23842445, TLM): Stub -> Implemented
  Ocean Dragon Lord - Neo-Daedalus (10485110, -): Unimplemented -> Stub
  Reshef the Dark Being (62420419, TLM): Unimplemented -> Implemented
  Reverse Buster (90640901, -): Unimplemented -> Stub
  Royal Surrender (56058888, TLM): Unimplemented -> Implemented
  Spell-Stopping Statute (10069180, TLM): Unimplemented -> Implemented
  Ultimate Insect LV7 (19877898, TLM): Unimplemented -> Implemented
  White Ninja (1571945, TLM): Unimplemented -> Implemented

LOSSES (4) — a card that worked before no longer does:
  Lightning, Dragon Ruler of Drafts (89185742, -): Implemented -> Stub
  Rose Lover (89252157, -): Implemented -> Stub
  Valerifawn, Mystical Beast of the Forest (24096499, -): Implemented -> Stub
  Xyz-Raypierce (86559484, -): Implemented -> Stub
```

## Verify
`Tools/CardCompile/headless.sh . --quiet` (PASS, 0 unit failures), `csc_overlay.sh .` and `--release`, `enum_guard.py --repo .`, `handler_guard.py --repo .`, then `status_diff.py status_before.tsv status_after.tsv`. Stress: 250 duels, 0 soft-locks, 0 exceptions.
