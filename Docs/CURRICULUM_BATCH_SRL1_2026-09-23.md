# Curriculum batch — SRL tranche 1 (compiler v53)

Date: 2026-09-23. Follows `Docs/CURRICULUM_BATCH_LOB_MRD1_2026-09-23.md`.

## Result

| Set | Before | After |
|-----|-------:|------:|
| SRL — Spell Ruler | 46/104 (44.2%) | **74/104 (71.2%)** |
| All 15 pre-Link curriculum sets | 612/1471 | 641/1471 (830 left) |

Seed re-exported at v53 (504 programs). Headless 1169 pass / 0 fail, 250 AI-vs-AI duels
completed, 0 soft-locks, Python UI guards pass.

## Cards compiled (28)

Molten Destruction, Rising Air Current, Gaia Power, Luminous Spark, Chorus of Sanctuary,
Rush Recklessly, The Reliable Guardian, Snake Fang, Maha Vailo, Flash Assailant,
Senju of the Thousand Hands, Sonic Bird, Giant Trunade, Karate Man, Boar Soldier, Toll,
Gravekeeper's Servant, Megamorph, Spellbinding Circle, Horn of Light, Snatch Steal,
Nimble Momonga, Giant Germ, Confiscation, The Forceful Sentry, plus the non-effect Ritual
Monsters Performance of Sword, Hungry Burger and Crab Turtle (now structural, like
non-effect Fusions; Skull Guardian, Black Luster Soldier, Dokurorider and The Masked Beast too).

## Shared pieces added

Attribute field auras with ATK up / DEF down; Defense-Position-only DEF aura; target gains
ATK/DEF until end of turn; per-Equip and per-opponent-monster self stats; Normal/Flip Summon
Ritual searches; return all Spells/Traps to hand; double original ATK then End Phase destroy;
global attack costs (Toll LP, Gravekeeper's Servant mill); Megamorph LP comparison; trap links
(Spellbinding Circle: cannot attack or change position, leaves with the monster);
opponent-Standby LP gain (Snatch Steal); Special Summon every Deck copy (Nimble Momonga
face-down, Giant Germ face-up); choose from the opponent's revealed hand (discard / shuffle into Deck).

Fix: the Lua-extracted catalog no longer overrides a card's own conditional self stat
(Boar Soldier had a permanent −1000 ATK). AI will not pay its last LP to attack.

## Deferred (30 left in SRL)

Toon monsters (Toon World system), Electric Snake / Minar / Penguin Knight (discarded or milled by
the opponent triggers), Ameba / Griggle / Invader of the Throne (control-switch triggers),
Final Destiny / Darkness Approaches (discard several as a cost), Curse of Fiend / Dark Zebra /
Jigen Bakudan (Standby-Phase actions), Painful Choice / Delinquent Duo (opponent chooses),
Chain Energy, Banisher of the Light, Kotodama, Ceremonial Bell, Fairy's Hand Mirror,
Tailor of the Fickle, Spear Cretin, Weather Report, Hiro's Shadow Scout, Wall Shadow /
Magical Labyrinth, and the registry partials Cyber Jar / Relinquished.
