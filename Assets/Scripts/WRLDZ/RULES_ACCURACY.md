# Card engine accuracy — Anime TCG simulator

**Product identity:** WRLDZ duels are an **anime TCG simulator** (declarations, timed responses, verbal announce).  
**Structural backbone:** [Konami Official Rulebook](https://www.yugioh-card.com/en/rulebook/) (v10)–inspired phases, summons, battle math.  
**Canon doc:** `ANIME_TCG_ENGINE.md`

We do **not** claim 100% of full modern PSCT / Master Rule digital clients. We do claim:

1. Structural legality for starter-slice play.  
2. **Combat-timed** attack/summon reactions (race the animation to IMPACT; no freeze).  
3. **Dual input** — tap and verbal/typed announce share one legal gateway (`DuelCommandService`).  

As more cards are added: response timings → chain/speed (as needed for anime drama) → data-driven card scripts. Do **not** hardcode every card into `if (id == …)` forever.

---

## What was wrong (user report)

| Bug | Cause | Fix |
|-----|--------|-----|
| No way to activate a Trap when opponent attacks | `CanManualActivate` required `TurnPlayer == you` always | Response windows on **opponent’s turn** |
| Traps auto-fired with no choice | `TryTriggerOnAttack` forced Mirror Force / Negate Attack | Defender **chooses** Activate or **Pass** |
| UI locked during AI turn | `_aiRunning` disabled all inputs | Unlock when `IsAwaitingPlayerResponse` |
| AI continued through player’s window | Battle loop did not wait | AI yields until response resolves |

---

## Implemented (structural Rulebook v10)

| Topic | Status |
|-------|--------|
| LP 8000, hand 5, end-phase hand 6 | Yes |
| First player: no draw, no Battle Phase turn 1 | Yes |
| Phases Draw → Standby → MP1 → Battle → MP2 → End | Yes |
| Normal Summon/Set once/turn; tributes Lv5–6 / 7+ | Yes |
| Flip Summon / position change restrictions | Yes |
| Battle math ATK/ATK, ATK/DEF, direct; no default pierce | Yes |
| Win: LP 0 or deck-out | Yes |
| Set Trap/QP cannot activate same turn | Yes |
| **Attack declaration response window** | **Yes** |
| **Summon response window** (Trap Hole) | **Yes** |
| Player Pass / Activate on legal Set cards | Yes |

### Response-legal cards (slice)

| Timing | Cards |
|--------|--------|
| Attack declared | Mirror Force, Negate Attack, Waboku, set Quick-Play (e.g. MST) |
| Monster summoned (face-up NS/Tribute/Flip) | Trap Hole (ATK ≥ 1000), set Quick-Play |

---

## Explicitly incomplete (not 100% of full TCG)

Do not treat these as “done” when adding cards:

1. **Full chain / Spell Speed 1–3** — single activation, no multi-link chain building  
2. **Turn-player priority** before non-turn player (slice: defender responds first on attack)  
3. **Damage Step** windows — Kuriboh (hand QE) plus compiled/catalog ATK/DEF traps (Bark of Dark Ruler, Mask of Weakness). Generic Spell/Trap ignition still illegal in the Damage Step.  
4. **Fast Effect Timing** chart exhaustively  
5. **Continuous / lingering / maintenance costs** beyond simplified Swords / Waboku  
6. **Special Summon procedures**, Extra Deck, Link/Pendulum/Xyz  
7. **Card text engine** — effects are hardcoded IDs in `SpellTrapEffects`  
8. **Banlist**, missing the timing, once-per-turn PSCT fully  
9. **Hand traps** (Ash, etc.) and QP from hand on opponent’s turn (illegal in TCG for QP)  
10. **Mandatory vs optional** triggers with multiple simultaneous effects  

---

## Architecture for more cards

```
DuelEngine          — phases, zones, summons, battle, response open/pass/resolve
ResponseWindow      — timing enum + PendingResponse
SpellTrapEffects    — legality + hardcoded resolutions (replace with data later)
CardDef / effects   — future: effect scripts or data-driven resolvers
```

### When adding a new Trap/Quick-Play

1. Classify **timing** (`AttackDeclared`, `MonsterSummoned`, open game state, later DamageStep…).  
2. Add legality in `IsLegalResponseCard` / `IsSupportedOpenGameState`.  
3. Add resolution in `ResolveResponseCard` or data-driven handler.  
4. Never auto-force activation — always optional unless card text is mandatory.  
5. Extend AI priority in `AiAutoRespond` if the card is defensive.

### Future “chain engine” PR shape

- Stack of chain links with Spell Speeds  
- Build → resolve LIFO  
- SEGOC for simultaneous triggers  
- Then migrate Mirror Force / Trap Hole off special-case paths onto the stack  

---

## How to test the attack response fix

1. Set **Mirror Force** or **Waboku** on your field (previous turn).  
2. End turn; let AI enter Battle and declare an attack.  
3. UI: hint shows **RESPONSE**; legal Set cards glow green; **Activate** on card + **Pass**.  
4. Activate Mirror Force → AI ATK monsters destroyed; attack stops.  
5. Or Pass → damage resolves normally.  
6. Trap Hole: when AI Normal Summons ATK ≥ 1000, same Pass / Activate window.

---

## Claim language

- **Correct:** “Structural TCG rules with official turn structure and attack/summon response windows.”  
- **Incorrect:** “100% accurate full Konami TCG including all card texts and chains.”  

Accuracy target for this slice: **structural rules + starter-deck interactions**. Expand coverage card-family by card-family as content grows.
