# Curriculum batch v212-crv-een-soi

Compiler `CardTextEffectCompiler.Version` = 212 (seed re-exported, 2048 programs, was 1991). Playbook `Tools/CardCompile/README.md`, workflow `.claude/workflows/compile-card-batch.js`.
Sets: CRV, EEN, SOI (plus TLM leftovers). 51 cards Implemented, 1 deferred (Cybernetic Magician), 0 losses. Previous batch: `Docs/CURRICULUM_BATCH_v211-tlm-fet.md`.

## Landed
- CrvEmptyHandAndAuras (`CrvEmptyHandAndAurasRuntime`): Cybernetic Cyclopean (empty-hand battle immunity), Reborn Zombie, Mechanical Hound (activation lock with empty hand), Elemental HERO Wildheart (unaffected by Traps), Gyroid, Tyranno Infinity, Batteryman C (aura multiplier by Batteryman count), Fortress Warrior.
- CrvBattleStepRiders (`CrvBattleStepRidersRuntime`): Goblin Elite Attack Force, Indomitable Fighter Lei Lei, Steamroid, Etoile Cyber, Drillroid, Nanobreaker (Start-of-Damage-Step destroy with Level cap), Chthonian Soldier, Oxygeddon, Black Veloci. Damage Calculation and battle-damage-taken riders.
- CrvDestroyedTriggersAndFloats (`CrvDestroyedTriggersAndFloatsRuntime`): Poison Draw Frog, T.A.D.P.O.L.E., UFOroid, Hydrogeddon, Wroughtweiler, Winged Kuriboh (no battle damage for the rest of the turn), Giant Kozaky. Records face-up-on-leaving, flipped-by-the-battle, sent-to-previous-controller's-GY.
- EenDarkWorldDiscards (`EenDarkWorldDiscardsRuntime`): Beiige, Broww, Goldd, Sillva, Brron, Scarr. Discarded-by-effect triggers queue and activate (new timing DiscardedToGyByCardEffect).
- CrvIgnitionsAndUnions: D.D.M. - Different Dimension Master, Patroid, Doitsu, Protective Soul Ailin.
- CrvHandProceduresAndNamedSummons (`CrvHandProceduresAndNamedSummonsRuntime`): Cyber Dragon, Elemental HERO Bubbleman, King's Knight, Des Frog, Water Dragon, Winged Kuriboh LV10.
- CrvCountersAndPhaseTriggers (`CrvCountersAndPhaseTriggersRuntime`): B.E.S. Crystal Core, B.E.S. Tetran, Dark Catapulter, Ebon Magician Curran, Cyber Archfiend, Rapid-Fire Magician. Non-Spell-Counter cost counters (`CounterCostIsNotSpellCounter`), refunded on cancel.
- TlmLeftoverSpellTraps (`TlmLeftoverSpellTrapsRuntime`): Grave Lure, Pikeru's Second Sight, Level Conversion Lab, Shifting Shadows, Mesmeric Control.

## Deferred
- Cybernetic Magician (59023523): stays Unimplemented, 0 clauses, pinned by a test. Its ATK set is SINGLE EFFECT_SET_ATTACK_FINAL: it wipes earlier ATK modifiers and later ones add on top. The engine sums continuous modifiers into AtkModifier, recomputed by 28 writers in RefreshBoard with no per-source timestamps, so no delta or snapshot can tell an earlier boost ending from a later one arriving.

## Skipped by the planner
- Elemental Mistress Doriado: multiple Attributes on one card (EFFECT_ADD_ATTRIBUTE); CardInstance.Attribute is a single string compared at about 39 sites.
- Jetroid: Trap activation from the hand (EFFECT_TRAP_ACT_IN_HAND) when chosen as an attack target; no hand-Trap legality or UI.
- Familiar-Possessed - Aussa / Eria: Special Summon procedure from hand OR Deck (DuelUI offers procedures on hand cards only) plus a two-monster send cost.
- Infernal Incinerator: custom Tribute Summon/Set procedure (discard the rest of the hand, Tribute 2000+ ATK from either field); DuelUI Tribute marking has no filter or cost hook.
- Over the cap, next batch: Adhesive Explosive, Ancient Gear, Ancient Gear Cannon, Anteatereatingant, B.E.S. Covered Core, Beelze Frog, Chain Thrasher, Chainsaw Insect, Cyber Barrier Dragon, Cyber Laser Dragon, D.D. Guide, Disciple of the Forbidden Spell, Divine Dragon - Excelion, Doom Dozer, Elemental HERO Neo Bubbleman, Gokipon, Grass Phantom, Hamon, Hero Kid, Machine King Prototype, Malice Ascendant, Memory Crusher, Parasitic Ticky, Princess Curran, Princess Pikeru, Proto-Cyber Dragon, Raviel, Ruin, Sand Moth, Silent Insect, Tenkabito Shien, Treeborn Frog, Uria.

## Audit
- Fixed: the three new CrvCountersAndPhaseTriggers files lacked .meta files; added.
- Left open: three TLM actions map to the wrong EffectVocabulary.ResolutionOf kind (Pikeru's Second Sight, Shifting Shadows -> ChangePosition; Grave Lure -> Discard), harmless since none targets. Exhausting Spell's RemoveAllSpellCounters also zeroes B.E.S. and Dark Catapulter counters (CardInstance.Counters on every card); gate it with SpellCounterSpendersRuntime.IsSpellCounterHolder.
- Side effects outside the batch: Dododo Buster, B.E.S. Covered Core, B.E.S. Big Core MK-2 changed compile output but stay Stub; 11 other cards went Unimplemented -> Stub (shared sentences); Zeradias now self-destroys with no face-up Sanctuary; Horus LV6 ignores Messenger of Peace's attack lock; own-hand effect discards are now noted (Electric Snake triggers on Dark Coffin); cancelling a counter-cost ignition pick refunds counters.

## Status table
```
status        before   after
Implemented     1300    1351  (+51)
Stub             525     531  (+6)
Unimplemented   2822    2765  (-57)
Structural      1563    1563  (+0)

gained (62):
  B.E.S. Crystal Core (22790789, -): Stub -> Implemented
  B.E.S. Tetran (44954628, -): Stub -> Implemented
  Batteryman C (19733961, -): Unimplemented -> Implemented
  Battlin' Boxer Big Bandage (86325573, -): Unimplemented -> Stub
  Beiige, Vanguard of Dark World (33731070, -): Unimplemented -> Implemented
  Black Veloci (52319752, -): Unimplemented -> Implemented
  Blackwing - Gladius the Midnight Sun (28190303, -): Unimplemented -> Stub
  Broww, Huntsman of Dark World (79126789, -): Unimplemented -> Implemented
  Brron, Mad King of Dark World (6214884, -): Unimplemented -> Implemented
  Chthonian Soldier (50916353, -): Unimplemented -> Implemented
  Cyber Archfiend (59907935, -): Unimplemented -> Implemented
  Cyber Dragon (70095154, -): Unimplemented -> Implemented
  Cybernetic Cyclopean (96428622, -): Unimplemented -> Implemented
  D.D.M. - Different Dimension Master (82112775, -): Unimplemented -> Implemented
  Dark Catapulter (33875961, -): Unimplemented -> Implemented
  Des Frog (84451804, -): Unimplemented -> Implemented
  Doitsu (57062206, -): Unimplemented -> Implemented
  Drillroid (71218746, -): Unimplemented -> Implemented
  Ebon Magician Curran (46128076, -): Stub -> Implemented
  Elemental HERO Bubbleman (79979666, -): Stub -> Implemented
  Elemental HERO Wildheart (86188410, -): Unimplemented -> Implemented
  Etoile Cyber (11460577, -): Unimplemented -> Implemented
  Fortress Warrior (66288028, -): Stub -> Implemented
  Giant Kozaky (58185394, -): Unimplemented -> Implemented
  Goblin Elite Attack Force (85306040, -): Unimplemented -> Implemented
  Goldd, Wu-Lord of Dark World (78004197, -): Unimplemented -> Implemented
  Grave Lure (57270476, TLM): Unimplemented -> Implemented
  Gyroid (18325492, -): Unimplemented -> Implemented
  Hieratic Dragon of Tefnuit (77901552, -): Unimplemented -> Stub
  Hydrogeddon (22587018, -): Unimplemented -> Implemented
  Indomitable Fighter Lei Lei (84173492, -): Unimplemented -> Implemented
  Karakuri Barrel mdl 96 "Shinkuro" (92300891, -): Unimplemented -> Stub
  King's Knight (64788463, -): Unimplemented -> Implemented
  Level Conversion Lab (84397023, TLM): Unimplemented -> Implemented
  Lightray Madoor (82579942, -): Unimplemented -> Stub
  Mechanical Hound (22512237, -): Unimplemented -> Implemented
  Mesmeric Control (48642904, -): Unimplemented -> Implemented
  Mine Mole (94079037, -): Unimplemented -> Stub
  Nanobreaker (70948327, -): Unimplemented -> Implemented
  Naturia Beans (44789585, -): Unimplemented -> Stub
  Oracle of the Sun (42280216, -): Unimplemented -> Stub
  Oxygeddon (58071123, -): Unimplemented -> Implemented
  Patroid (71930383, -): Unimplemented -> Implemented
  Pikeru's Second Sight (58015506, TLM): Unimplemented -> Implemented
  Poison Draw Frog (56840658, -): Unimplemented -> Implemented
  Protective Soul Ailin (11678191, -): Unimplemented -> Implemented
  Rapid-Fire Magician (6337436, -): Unimplemented -> Implemented
  Reborn Zombie (23421244, -): Unimplemented -> Implemented
  Scarr, Scout of Dark World (5498296, -): Unimplemented -> Implemented
  Shifting Shadows (59237154, TLM): Unimplemented -> Implemented
  Sillva, Warlord of Dark World (32619583, -): Unimplemented -> Implemented
  Sphere of Chaos (82693042, -): Unimplemented -> Stub
  Spiritual Whisper (50155385, -): Unimplemented -> Stub
  Steamroid (44729197, -): Unimplemented -> Implemented
  T.A.D.P.O.L.E. (10456559, -): Unimplemented -> Implemented
  T.G. Striker (1315120, -): Unimplemented -> Stub
  Tyranno Infinity (83235263, -): Unimplemented -> Implemented
  UFOroid (7602840, -): Unimplemented -> Implemented
  Water Dragon (85066822, -): Unimplemented -> Implemented
  Winged Kuriboh (57116033, -): Unimplemented -> Implemented
  Winged Kuriboh LV10 (98585345, -): Unimplemented -> Implemented
  Wroughtweiler (6480253, -): Unimplemented -> Implemented
```

## Verify
```
Tools/HeadlessEngine/run.sh --quiet          # unitPass=8345 unitFail=0, PASS
bash Tools/CardCompile/csc_overlay.sh . ; bash Tools/CardCompile/csc_overlay.sh . --release
python3 Tools/CardCompile/enum_guard.py --repo . ; python3 Tools/CardCompile/handler_guard.py --repo .
```
Stress run before finalizing: 250 duels, 0 soft locks, 0 exceptions.
