# Curriculum batch — LOB close + MRD tranches 1–2 (compiler v52)

Date: 2026-09-23. Engine path only (`DuelEngine` + `TextEffects`). No card ids in rules code.

## Result (pre-Link curriculum, `Tools/HeadlessEngine/run.sh --coverage`)

| Set | Before | After |
|-----|-------:|------:|
| LOB — Legend of Blue Eyes White Dragon | 110/126 (87.3%) | **126/126 (100%)** |
| MRD — Metal Raiders | 96/144 (66.7%) | **125/144 (86.8%)** |
| Whole `cards_db` fully compiled | 682 | 729 |

Seed: `compiled_effects_seed_v1.json` re-exported at v52 (446 → 483 programs).
Checks: headless 1148 pass / 0 fail, 250 AI-vs-AI duels completed, 0 soft-locks, Python UI guards pass.

## Cards compiled (32)

**LOB (16):** Umi, Yami, Wasteland, Mountain, Sogen, Forest, Violet Crystal, Follow Wind,
Armed Ninja, Reaper of the Cards, Stop Defense, Fissure, Gravedigger Ghoul, Two-Pronged Attack,
Dragon Capture Jar, Exodia the Forbidden One.

**MRD (16):** Block Attack, Change of Heart, Soul Release, Tremendous Fire, The Immortal of Thunder,
Lava Battleguard, Swamp Battleguard, Shadow Ghoul, Muka Muka, The Little Swordsman of Aile,
The Unhappy Maiden, Dragon Piper, Masked Sorcerer, The Bistro Butcher, White Magical Hat,
Robbin' Goblin.

Shared templates also completed Crimson Ninja and Enraged Muka Muka (other sets).

## MRD tranche 2 (13 more, v52)

Dream Clown, Crass Clown, Tainted Wisdom, Paralyzing Potion, Germ Infection, Stim-Pack,
Ring of Magnetism, Dark Elf, Insect Soldiers of the Sky, Jirai Gumo, Electric Lizard,
Shield & Sword, The Cheerful Coffin.

New shared pieces: position-change triggers (manual change, Flip Summon, card effects),
Equip riders (cannot attack, Standby ATK decay, must-be-attacked), "non X-Type" equip limits,
attack LP cost, Damage Step-only ATK, attack-declared coin toss, attacker lock, ATK/DEF swap
until end of turn, choose-and-discard from hand.

Fixes found on the way: battle math now uses the same ATK/DEF the card shows (lasting changes
such as Adhesion Trap Hole's halving were ignored in battle); the Lua-extracted catalog no
longer gives Insect Soldiers a permanent +1000.

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
| Steel Scorpion, Mushroom Man #2, Blast Juggler | Delayed destruction timing, optional End Phase control swap, Standby-Phase ignition. |
| Share the Pain, Elegant Egotist | Opponent-chosen Tribute; two-name Summon tied to Harpie Lady Sisters' nomi rule. |

## Next batch

Finish MRD (19 left, table above) or move on to SRL (58 left). 859 cards remain across the 15 pre-Link sets.
