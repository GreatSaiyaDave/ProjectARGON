# Curriculum batch v210-rds-fet

Compiler Version 210 (was 209). Sets RDS and FET. Seed re-exported: 1924 -> 1958 programs (34 added, 0 removed; see seed_diff.txt in the batch cache).

## Landed (46 Implemented)
- RdsRedoBattle: Silent Swordsman LV3 / LV5, Mystic Swordsman LV6, Sasuke Samurai #4, Covering Fire, Cross Counter, Rare Metalmorph (battle redo/negation hooks in RdsRedoBattleRuntime). Sibling Silent Magician LV8 also became Implemented.
- RdsRedoSpellTrap: Chain Burst, Spell Absorption, Forced Ceasefire, Triangle Ecstasy Spark, Necklace of Command, Flint.
- SimpleTriggersAndSpells: Abare Ushioni, Blade Rabbit, Granmarg the Rock Monarch, Good Goblin Housekeeping, The Big March of Animals, Elemental Burst.
- BattleRiders: B.E.S. Big Core, Kangaroo Champ, Poison Fangs, Spiral Spear Strike, Catnipped Kitty, Armed Samurai - Ben Kei (audit fix: extra attacks count only real Equip Cards).
- DestroyWatchersAndSpellShields: Firebird, Maji-Gire Panda, Mokey Mokey Smackdown, Pole Position, Blast Magician.
- RevivalEquipsAndGearfried: Fulfillment of the Contract, Re-Fusion, Release Restraint, Gearfried the Swordmaster, Brain Jacker.
- DeckSwarmsAndFusionMaterials: Hyena, Assault Dog, Rescue Cat, Sacred Phoenix of Nephthys, Centrifugal Field, The Dark / Earth / Light - Hex-Sealed Fusion.
- ChoiceAmongEffects: Fuh-Rin-Ka-Zan, Penalty Game!.

## Deferred / skipped
- Tragedy: needs an EVENT_CHANGE_POS "position changed" response window (none exists). ENGINE_MAP section 4.
- Fusilier Dragon, Behemoth the King of All Animals: need a 0/1-Tribute Normal Summon choice DuelUI does not offer; remove opencode's card-id hack 4d0c17c4b when built.
- Serial Spell: needs "Copy another card's effect" (also blocks Double Spell).

## Audit residuals (unfixed, in ENGINE_MAP Open residuals)
Rescue Cat negation flag lingers until next RefreshBoard; Spell Absorption vs effect-negated Spell unverified; two ATK-set primitives kept separate; double Toons destroyed hook (harmless).

## Status table
```
status        before   after
Implemented     1224    1270  (+46)
Stub             535     523  (-12)
Unimplemented   2888    2854  (-34)
Structural      1563    1563  (+0)
```
Full gained list: 46 cards, no losses (status_diff vs status_before.tsv).

## Verify
`headless.sh . --quiet` (unitPass=7790 unitFail=0, PASS; earlier 250-duel stress PASS), csc_overlay (+ --release) OK, enum_guard 0 errors, handler_guard clean.
