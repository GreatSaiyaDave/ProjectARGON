# Curriculum batch v209-ast-finish

Compiler `CardTextEffectCompiler.Version` = 209 (seed re-exported, 1924 programs). Playbook `Tools/CardCompile/README.md`, workflow `.claude/workflows/compile-card-batch.js`.
Sets: AST, MFC, IOC. 32 cards in the batch: 30 Implemented, The Second / Third Sarcophagus Structural, 1 more card (The End of Anubis) deferred. Six groups, mostly ports of the v178 groups that the 10-12 landing left unregistered.

## Landed
- SpellTrapSealsAndImmunity: Talisman of Spell Sealing / Talisman of Trap Sealing (Spell/Trap activation lock + negation, self-destroy without Sealmaster Meisei), Sonic Jammer (FLIP: opponent cannot activate Spell Cards until end of next turn), Dust Barrier (Normals unaffected by opponent Spells, destroyed on 2nd Standby), Armor Break (negate and destroy an Equip Spell activation only).
- NormalMonsterSpells: Delta Attacker (direct attack), Thousand Energy / Triangle Power (original ATK/DEF up, destroyed at End Phase), The Law of the Normal (both hands discarded, field wiped except low Normals), Order to Smash (Tribute a Normal, destroy 1-2 opponent S/T), Sword of the Soul-Eater (Tribute other Normals on activation, +1000 each), Opti-Camouflage Armor (Level 1 equip, direct attack).
- LpLocksDelayedBurnAndGyNegation: King of the Swamp (Fusion substitute sentence atom), Wall of Revealing Light (paid-LP attack lock, new LP-amount pick on placement), Weapon Change (Standby ATK/DEF switch for 700 LP), Dora of Fate (delayed burn on a Normal Summon one Level lower).
- DeckSummonsAndSarcophagi: Enchanting Fitting Room, Monster Gate (Deck excavation Summons), Ninjitsu Art of Transformation (Tribute a Ninja, linked Deck/hand Summon), Human-Wave Tactics (End Phase Summon of battle-destroyed low Normals), The First Sarcophagus (+ Second / Third now Structural through a live placement path), Spirit of the Pharaoh.
- ZoneCapsAndPhaseWindows: Thunder of Ruler (opponent Standby window, forbids their Battle Phase), Neko Mane King (End Phase jump), Kaiser Colosseum (monster cap by your count), Spatial Collapse (zone cap).
- LocksRedirectsAndActivity: Goblin of Greed (no discard as cost), Dark Magician of Chaos (banish instead of leaving, battle banish, End Phase GY Spell), Grave Protector, Burst Stream of Destruction (named attack oath).

## Deferred
- The End of Anubis (65403020): stays 0 clauses, pinned by a test. Needs a negation hook at every resolution site of an effect activated in the GY or targeting a GY card (chain links, ApplyClause, legacy S/T paths, and the many inline GY triggers batch runtimes resolve outside ApplyClause, none of which records the activation location). A partial negation would ship wrong.

## Audit
- Fixed: Dark Magician of Chaos's battle banish ran before Human-Wave Tactics' `NoteBattle`, so a banished low Normal was not counted (`DuelEngine.ApplyBattleDestructions` now calls NoteBattle first; test in `LocksRedirectsAndActivityRegressionTests.CrossGroupAuditDuels`, plus 2 more cross-group probes that passed).
- Left open (fail-soft): Neko Mane King misses a hand/Deck discard by an opponent effect (hand-to-GY paths do not write the GY-entry stamp); Human-Wave Tactics is recorded after a battle-flipped FLIP effect resolves.
- Side effects outside the batch: Cockadoodledoo and Fusion Parasite went Unimplemented -> Stub (shared sentences); both stay out of decks.

## Status table
```
status        before   after
Implemented     1194    1224  (+30)
Stub             537     535  (-2)
Unimplemented   2918    2888  (-30)
Structural      1561    1563  (+2)

gained (32):
  Armor Break (79649195, AST): Unimplemented -> Implemented
  Burst Stream of Destruction (17655904, AST): Stub -> Implemented
  Cockadoodledoo (42338879, -): Unimplemented -> Stub
  Dark Magician of Chaos (40737112, IOC): Unimplemented -> Implemented
  Delta Attacker (39719977, AST): Unimplemented -> Implemented
  Dora of Fate (67464807, AST): Unimplemented -> Implemented
  Dust Barrier (31476755, AST): Unimplemented -> Implemented
  Enchanting Fitting Room (30531525, AST): Unimplemented -> Implemented
  Fusion Parasite (6205579, -): Unimplemented -> Stub
  Goblin of Greed (425934, DCR): Unimplemented -> Implemented
  Grave Protector (11448373, AST): Unimplemented -> Implemented
  Human-Wave Tactics (30353551, AST): Unimplemented -> Implemented
  Kaiser Colosseum (35059553, MFC): Unimplemented -> Implemented
  King of the Swamp (79109599, AST): Stub -> Implemented
  Monster Gate (43040603, AST): Unimplemented -> Implemented
  Neko Mane King (11021521, MFC): Unimplemented -> Implemented
  Ninjitsu Art of Transformation (70861343, AST): Unimplemented -> Implemented
  Opti-Camouflage Armor (44762290, AST): Stub -> Implemented
  Order to Smash (39019325, AST): Unimplemented -> Implemented
  Sonic Jammer (84550200, AST): Unimplemented -> Implemented
  Spatial Collapse (20644748, IOC): Unimplemented -> Implemented
  Spirit of the Pharaoh (25343280, AST): Unimplemented -> Implemented
  Sword of the Soul-Eater (5371656, AST): Stub -> Implemented
  Talisman of Spell Sealing (71983925, AST): Unimplemented -> Implemented
  Talisman of Trap Sealing (19312169, AST): Unimplemented -> Implemented
  The First Sarcophagus (31076103, AST): Unimplemented -> Implemented
  The Law of the Normal (66926224, AST): Unimplemented -> Implemented
  Thousand Energy (5703682, AST): Unimplemented -> Implemented
  Thunder of Ruler (91781589, MFC): Unimplemented -> Implemented
  Triangle Power (32298781, AST): Unimplemented -> Implemented
  Wall of Revealing Light (17078030, AST): Unimplemented -> Implemented
  Weapon Change (10035717, AST): Unimplemented -> Implemented

other changes (2):
  The Second Sarcophagus (4081094, AST): Unimplemented -> Structural
  The Third Sarcophagus (78697395, AST): Unimplemented -> Structural
```

## Verify
`Tools/CardCompile/headless.sh <tree> --quiet` PASS (unitPass 7504, 0 fail, 40/40 duels); `--quiet --duels 250` PASS before the final audit fix and after it (250/250, 0 soft-locks); `csc_overlay.sh` and `--release` OK; `enum_guard.py` 0 errors; `handler_guard.py` 0 unread actions, 0 unregistered batch files; `status_diff.py` no losses. Seed diff: `seed_diff.txt` in the run folder.
