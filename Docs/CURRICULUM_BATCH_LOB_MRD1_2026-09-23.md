# Curriculum batch — LOB close + MRD tranche 1 (compiler v51)

Date: 2026-09-23. Engine path only (`DuelEngine` + `TextEffects`). No card ids in rules code.

## Result (pre-Link curriculum, `Tools/HeadlessEngine/run.sh --coverage`)

| Set | Before | After |
|-----|-------:|------:|
| LOB — Legend of Blue Eyes White Dragon | 110/126 (87.3%) | **126/126 (100%)** |
| MRD — Metal Raiders | 96/144 (66.7%) | **112/144 (77.8%)** |
| Whole `cards_db` fully compiled | 682 | 718 |

Seed: `compiled_effects_seed_v1.json` re-exported at v51 (446 → 471 programs).
Checks: headless 1128 pass / 0 fail, 250 AI-vs-AI duels completed, 0 soft-locks, Python UI guards pass.

## Cards compiled (32)

**LOB (16):** Umi, Yami, Wasteland, Mountain, Sogen, Forest, Violet Crystal, Follow Wind,
Armed Ninja, Reaper of the Cards, Stop Defense, Fissure, Gravedigger Ghoul, Two-Pronged Attack,
Dragon Capture Jar, Exodia the Forbidden One.

**MRD (16):** Block Attack, Change of Heart, Soul Release, Tremendous Fire, The Immortal of Thunder,
Lava Battleguard, Swamp Battleguard, Shadow Ghoul, Muka Muka, The Little Swordsman of Aile,
The Unhappy Maiden, Dragon Piper, Masked Sorcerer, The Bistro Butcher, White Magical Hat,
Robbin' Goblin.

Shared templates also completed Crimson Ninja and Enraged Muka Muka (other sets).

## Shared mechanics added (reusable by later sets)

- `ClassicEraTemplates.cs` — printed-text shapes for the cards above (current PSCT and older print).
- Multi-Type Field auras ("All Fish, Sea Serpent, … monsters on the field gain 200 ATK/DEF, also …").
- **Exact Type matching** everywhere a Type filter is used. "Beast" no longer matches Winged Beast
  or Beast-Warrior; "Warrior" no longer matches Beast-Warrior (Command Knight, ROTA-style searches).
- Multi-target activation: N targets, "up to N" (Cancel after ≥1 pick finishes), distinct target
  groups (yours + theirs). Duplicate targeted clauses on older cards keep sharing one target.
- Response-window free-chain traps with targets now pick a target instead of resolving with none.
- Kind-checked Set-card destruction (reveal; destroy only if Spell / Trap).
- Explicit face-up Attack / Defense changes (flipping a Set monster runs its FLIP effect).
- Non-targeting lowest-ATK choice (Fissure ignores Lord of D. / target negation).
- Position lock (Dragon Capture Jar), temporary control (Change of Heart, returns in End Phase).
- **Card ownership:** a stolen monster leaving the field goes to its owner's GY / banished / hand.
- LP loss that is not damage; hand win condition (Exodia; both at once = draw, UI shows DRAW).
- Self-scaling ATK/DEF (per named card you control / monsters in GY / cards in hand).
- Battle-damage triggers: "When this card inflicts Battle Damage" and "Each time a monster you control …".
- Random hand discard (`DuelRng.PickIndex`, queueable in tests).
- AI: does not Tribute its own monsters for a one-turn ATK boost.

## Fail-closed fix

Element monsters ("gets/gains the following effect(s) while there is a monster(s) with the following
Attribute(s) on the field: ● FIRE: …") were compiling a bullet as an unconditional ignition
(Element Dragon was falsely "fully compiled" on main). They now refuse until conditional Attribute
effects are modeled.

## Deferred (honest refuse — not compiled)

| Card(s) | Why |
|---------|-----|
| Seven Tools of the Bandit, Magic Jammer, Solemn Judgment, Horn of Heaven, Fake Trap | Need activation / Summon negation on the chain (parked: chain-negate counters). |
| Gate Guardian, Harpie Lady Sisters, Great Moth, Larvae Moth, Cocoon of Evolution | Need "properly Summoned" tracking for nomi monsters and the Petit Moth turn count. |
| Castle of Dark Illusions, Pumpking the King of Ghosts | Growing Standby counters tied to Castle staying face-up. |
| Big Eye, Yado Karu | Need a deck-order / bottom-of-deck ordering UI. |
| Dream Clown, Crass Clown, Tainted Wisdom | Need a "changed battle position" trigger window. |
| Steel Scorpion, Electric Lizard, Mushroom Man #2, Jirai Gumo, Blast Juggler | Delayed / attacker-marking effects and Standby ignitions not yet modeled. |
| Ring of Magnetism, Germ Infection, Paralyzing Potion, Stim-Pack | Equip attack-restriction and Standby decay on Equips. |
| Dark Elf | Attack cost (pay LP to declare an attack). |
| Insect Soldiers of the Sky, Shield & Sword, The Cheerful Coffin, Share the Pain, Elegant Egotist | Damage-Step-only boost, ATK/DEF swap, hand-discard choice, opponent-chosen Tribute, two-name summon. |

## Next batch

Finish MRD (32 left, table above), starting with the position-change trigger window (3 cards)
and Equip restrictions (4 cards), then SRL (44.2%).
