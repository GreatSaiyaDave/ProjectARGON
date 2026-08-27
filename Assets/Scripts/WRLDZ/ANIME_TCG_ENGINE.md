# Anime TCG Engine — canon

**WRLDZ is an anime TCG simulator**, not a tournament-rules clone of Konami’s digital clients.

The **card engine is the most vital system** in the product. Everything else (map, AR, hub) funnels into duels that feel like the show: declarations, drama, and legal resolution.

---

## North star

| Axis | Konami tournament TCG | WRLDZ anime simulator |
|------|----------------------|------------------------|
| Authority | Full PSCT / Master Rule text | **Structural TCG truth** + anime presentation |
| Input | Tap / click only | **Tap + verbal announce** (same legality gate) |
| Responses | Open game state / pause priority | **Combat reactions** — race the attack **animation** to IMPACT |
| Tone | Competitive precision | **“It’s time to duel”** — declarations, stakes, presence |
| Future AR | Optional | Life-size arenas; voice fits lenses-first; anim hit events |

Structural rules (phases, summons, battle math, set-turn restrictions, win cons) stay honest.  
Presentation and **how you issue moves** lean anime: say the move; the engine only allows what is legal **right now**.

---

## Dual input, single rules path

```
  [ Tap card / button ]     [ Voice / typed announce ]
            \                       /
             \                     /
              v                   v
                 DuelIntent
                      │
                      v
              DuelCommandService   ← only legal actions execute
                      │
                      v
                  DuelEngine
```

- **Illegal announce** → spoken/log reject (“You can’t Normal Summon twice this turn.”)  
- **Legal announce** → same resolution as tap  
- No second rules path for voice — voice is just another front-end  

---

## Combat reactions (anime — not a pause menu)

**Canon:** the game does **not** freeze for responses. Reactions are **reaction-time combat** tied to the **acting card’s animation**.

### Attack example
1. Opponent declares **Blue-Eyes White Dragon, direct attack**.  
2. Attack animation **starts immediately** (charge → flight → impact).  
3. `CombatAnimTimings.ForAttack` sets **IMPACT ≈ 5s** (heavier for high ATK / level).  
4. While the model animates, defender may:
   - **Tap** a glowing Set trap → Activate mid-charge  
   - **Announce** “Activate Mirror Force!”  
   - **Pass** / let it through  
5. **If no legal reaction before IMPACT** → hit lands, damage resolves.  
6. **If trap fires before IMPACT** → animation interrupted / negated (Mirror Force, Negate Attack, etc.).

### Summon example
Same model with a shorter **appear** anim (~1.5–3s) for Trap Hole timing.

### Implementation hooks
| Piece | Role |
|-------|------|
| `CombatAnimTimings` | Impact / total duration from card weight (later: real clip + hit event) |
| `ActiveCombatPresentation` | Live anim clock on `DuelEngine` |
| `PendingResponse.ReactionSeconds` | = impact time of that action |
| `TimedResponseClock` | UI + auto-pass **at impact**, not an arbitrary menu timer |

When AR/3D clips ship, **drive `ImpactAtSeconds` from animation events** — the rules path stays the same.

---

## Verbal grammar (examples)

| Announce | Intent |
|----------|--------|
| “I summon Dark Magician in Attack Position” | Normal Summon ATK |
| “Set Celtic Guardian” / “Set in Defense” | Set DEF |
| “Set Mystical Space Typhoon” | Set S/T |
| “Activate Pot of Greed” | Activate from hand/field |
| “Activate Mirror Force” | Response activate |
| “Celtic Guardian, attack!” | Attack (prompt target if needed) |
| “Attack directly” | Direct attack with selected/only attacker |
| “Enter the Battle Phase” | Battle |
| “I end my turn” | End turn |
| “Pass” / “No traps” | Pass response |

Name matching is fuzzy against cards in **legal zones for that action**.

---

## Layers (grow toward “perfect”)

```
1. DuelEngine          structural state machine (phases, zones, LP)
2. Response + Clock    timed anime windows
3. DuelCommandService  legal command gateway (tap + voice)
4. VerbalMoveParser    natural language → DuelIntent
5. SpellTrapEffects    starter resolutions → later data-driven card scripts
6. Chain / Speed       optional later; anime may simplify multi-link drama
7. Voice I/O           device STT → parser; TTS for announcements / AI lines
```

**Perfect** means: every legal anime-style declaration resolves correctly; every illegal one is refused with clear feedback; timed responses never soft-lock; voice and tap stay in sync.

---

## Product implications

- AR / lenses: microphone announce while looking at life-size field.  
- Non-AR phone: announce field + tap still fully playable.  
- AI opponent: can log verbal-style lines (“I summon…”) for flavor.  
- Accessibility: typed announce = voice without mic.  

---

## Related docs

- `RULES_ACCURACY.md` — structural vs full-PSCT honesty  
- `TCG_ENGINE_INTEGRATION.md` — integrated checklist  
- `PRODUCT_VISION.md` — world loop; duels use this engine  
