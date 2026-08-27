# TCG structural rules integrated (Official Rulebook Version 10)

**Source:** Konami — [Official Rulebook](https://www.yugioh-card.com/en/rulebook/)  
**PDF:** `Downloads/WRLDZ/docs/rules/SD_RuleBook_EN_10.pdf`

## Integrated into DuelEngine

| Topic | Rulebook | Engine behavior |
|-------|----------|-----------------|
| Starting LP | 8000 | Yes |
| Opening hand | 5 | Yes |
| First player first turn | No draw; no Battle Phase | Yes |
| Turn phases | Draw → Standby → MP1 → Battle → MP2 → End | Yes |
| Normal Summon/Set | Once/turn shared | Yes |
| Tribute | Lv5–6: 1; Lv7+: 2 | Yes (manual select + auto-fill) |
| Flip Summon | Face-down DEF → face-up ATK (not same turn as Set) | Yes |
| Change position | Face-up ATK↔DEF; not if summoned/attacked this turn | Yes |
| Battle math | ATK vs ATK / ATK vs DEF / direct | Yes (no piercing default) |
| Hand size End Phase | 6 | Yes |
| Win | LP 0 or cannot draw | Yes |
| Deck size check | Main 40–60 | Logged warning |

## Explicitly NOT integrated (yet)

- Chains / Spell Speeds / activations of Spell·Trap·monster effects
- Quick-Play from hand, Counter Traps, continuous effects
- Special Summon procedures, Extra Deck (Fusion/Synchro/Xyz/Link)
- Field Zone, Pendulum Zones, Link arrows
- Banlist / limited cards
- Optional rules (Master Rule detailed exceptions)

## How to test in ProjectARGON

1. Open `Assets/Scenes/DuelSlice`
2. Play
3. Verify log announces Rulebook v10 structure
4. Turn 1: cannot enter Battle Phase; no draw
5. Summon Lv≤4; End Turn
6. Turn 2+: Battle works; try Tribute (need high-level in hand + field monsters)
7. Win/lose → Restart
