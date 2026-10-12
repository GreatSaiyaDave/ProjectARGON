# Curriculum batch — v177-ioc-ast

Date: 2026-10-11. Compiler v176 -> v177. Workflow run: 8 implementer groups in scratch trees, integrated into the real
checkout, audited, finalized. Per-group implementer notes: `~/.cache/wrldz-card-compile/v177-ioc-ast/patches/*.md`.

## Result

```
status        before   after
Implemented      935     990  (+55)
Stub              49      40  (-9)
Unimplemented    308     262  (-46)
Structural       425     425  (+0)
```

55 Implemented gained (IOC and AST), 0 losses; 6 more group cards (Fissure, Terrorking Archfiend, Vilepawn Archfiend,
Different Dimension Dragon, Metalsilver Armor, Chaos Command Magician) were already Implemented and only had their
targeting truth fixed. Headless `--quiet`: 6845 pass / 0 fail; stress `--duels 250`: 250/250 completed, turnCap 0,
softLocks 0, exceptions 0. `csc_overlay.sh` editor + `--release`: OK. `enum_guard.py`: 0 errors, 0 warnings. No peer landings.
Seed diff: `~/.cache/wrldz-card-compile/v177-ioc-ast/seed_diff.txt`.

## Cards compiled (61 in 8 groups)

**ConditionalSpellsAndTraps**: Blasting the Ruins, Chaos End, Chaos Greed, Self-Destruct Button (both LP to 0, DRAW),
Ojama Delta Hurricane!!, Gryphon's Feather Duster, Destruction Ring (destroy then 1000 to both), D.D. Borderline (Battle Phase lock).
**EndOfDamageStepRiders**: Getsu Fuhma, Ryu Kokki, Prickle Fairy (opponent cannot attack your Insects), Insect Princess,
Enraged Battle Ox (piercing for Beast types), Drillago (conditional direct attack), Two Thousand Needles, Legendary Jujitsu Master.
**BattleDamageEvents**: Begone, Knave!, Robbin' Zombie, Sasuke Samurai #3, Aswan Apparition, Nubian Guard,
Des Counterblow, Piranha Army (double battle damage), Stone Statue of the Aztecs.
**SpellEventsAndHandSendTriggers**: Elephant Statue of Blessing / Disaster and Regenerating Mummy (new hand-send timing),
Curse of Darkness, Tower of Babel (4th Spell Counter -> 3000 damage), Spirit of the Pot of Greed, Fiend's Hand Mirror.
**RecoveryAndPhaseCosts**: Dimension Distortion, Dimension Fusion (each player summons from banished), Salvage, Recycle,
Reload, Coach Goblin, Manticore of Darkness.
**TokensDeclarationsAndPicks**: Stray Lambs, Scapegoat (summon oath), Multiplication of Ants, DNA Transplant, Jade Insect
Whistle, A Hero Emerges, Wild Nature's Release, Balloon Lizard (own counters).
**ChaosBanishSummons**: Chaos Sorcerer, Black Luster Soldier - Envoy of the Beginning (chain attack), Silpheed, Strike Ninja,
Chaosrider Gustaph, Soul Absorption (new `BanishEvents` funnel), Primal Seed.
**TargetingTruth**: Fissure, Terrorking Archfiend, Vilepawn Archfiend, Different Dimension Dragon, Metalsilver Armor,
Chaos Command Magician, Gora Turtle of Illusion, Big Burn (one targeting answer for every site; `TargetingTruthRuntime`).

Audit fix: Big Burn's banish was one event per card, giving Soul Absorption N links; now one `BanishEvents.Group`. Tests added.

## Skipped by the planner (gap recorded in ENGINE_MAP section 4)

- Must-attack obligation: Amazoness Archers, Diffusion Wave-Motion, Staunch Defender, Berserk Gorilla.
- Card-name declaration picker / revealed-hand UI: Archfiend's Oath, Great Phantom Thief, D.D. Designator; hidden pick + reveal: Ante.
- Copy another card's effect: Double Spell. Second target filtered by the first: Formation Union.
- Max-monster-zone cap: Kaiser Colosseum, Spatial Collapse. Phase jump: Neko Mane King. Standby window for a Set Trap: Thunder of Ruler.
- Cannot-discard-as-cost lock: Goblin of Greed. Leave-field redirect: Dark Magician of Chaos.
- Per-player activity record + activation lock: Chaos Emperor Dragon - Envoy of the End.
- Over the cap, next batch (IOC/AST/etc.): 3-Hump Lacooda, 7, Arcane Archer of the Forest, Archlord Zerato, Armor Break, Atomic Firefly,
  Avatar of The Pot, Backfire, Beckoning Light, Blessings of the Nile, Burst Stream of Destruction, Curse of Anubis, Delta Attacker,
  Disc Fighter, Dora of Fate, Dust Barrier, Emissary of the Afterlife, Emissary of the Oasis, Enchanting Fitting Room,
  Ghost Knight of Jackal, Goblin King, Grave Protector, Human-Wave Tactics, Judgment of the Desert, King of the Swamp,
  Lady Ninja Yae, Legacy Hunter, Light of Judgment, Mazera DeVille, Monster Gate, Mystik Wok, Night Assailant,
  Ninjitsu Art of Transformation, Opti-Camouflage Armor, Order to Smash, Sand Gambler, Solar Flare Dragon, Solar Ray,
  Sonic Jammer, Soul Reversal, Soul-Absorbing Bone Tower, Special Hurricane, Spirit of the Pharaoh, Sword of the Soul-Eater,
  Talisman of Spell Sealing, Talisman of Trap Sealing, The Agent of Force - Mars, The Agent of Judgment - Saturn,
  The End of Anubis, The First/Second/Third Sarcophagus, The Kick Man, The Law of the Normal, The Sanctuary in the Sky,
  The Unhappy Girl, Theban Nightmare, Thousand Energy, Triangle Power, Vampire Lady, Wall of Revealing Light, Weapon Change.

No cards were dropped at integration. Open audit residuals (Exhausting Spell vs Balloon Lizard counters; duplicate battle
state records) are in ENGINE_MAP "Open residuals".

## Verify

`Tools/CardCompile/headless.sh . --quiet`, `csc_overlay.sh .` (and `--release`), `enum_guard.py`, `--status-dump` +
`status_diff.py`; one card: `headless.sh . --card "Tower of Babel"`. Compiler `Version` is 177; seed re-exported.
