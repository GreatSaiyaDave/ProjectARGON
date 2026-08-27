# YouTube creator insights → WRLDZ efficiency

**Date:** 2026-08-10  
**Sources (watch / learn patterns only — do not copy code or assets):**

| Video | Channel | Focus |
|-------|---------|--------|
| [Coding A Yugioh Simulator #1](https://youtu.be/5bZ9kanZYe4) | [Yugioh Shorts](https://www.youtube.com/@yugiohshorts) | Client–server, Normal Summon, websockets |
| [FREE Yu-Gi-Oh RPG Open Beta Q&A](https://www.youtube.com/live/w3ck0PavoCs) | [dungiYGO](https://www.youtube.com/@dungiYGO) | Open-world YGO RPG launch, product ops |

**Legal:** Inspiration for *architecture and process* only. No scraping their clients, models, UI, or card pipelines into WRLDZ.

---

## 1. Yugioh Shorts — simulator engineering

### What they emphasize

1. **WebSockets** over HTTP long-polling for duel traffic (push field updates without client poll).  
2. **Server authority:** client may send card IDs / zone / intent; server **re-resolves** card objects from room state (never trust client ATK).  
3. **Register response listener *before* emit** so fast server replies are not lost.  
4. **Optimistic 3D UI:** detach hand card → animate; on failure **reattach** with saved transform.  
5. **Server action lock:** reject new actions while processing one (anti-desync / cheat).  
6. **Turn ownership checks** on every intent.  
7. **Room broadcast** (`update field`) so both clients overwrite with server card data + zone + origin for animation.  
8. **Separate “card user data” from mesh** — pure game state objects, presentation layered on top.  
9. **`try/finally` always returns open game state** — continuous re-check + release lock so the duel never soft-locks.  
10. **Server sends list of playable cards** → client yellow glow (legal action affordances).

### Map to WRLDZ (already / next)

| Their pattern | WRLDZ today | Efficiency move |
|---------------|-------------|-----------------|
| Server-only rules | `DuelEngine` + `DuelCommandService` | Keep **all** mutations on engine; UI only intents |
| Action lock | Partial (`IsBusy`, windows) | Explicit `engine.IsProcessingAction` + finally release (like RecoverStuckCombat) |
| Legal play list | Scattered `Can*` | Single `ListLegalIntents(who)` → glow disks/UI once |
| Optimistic UI + rollback | AR drag often commits immediately | Detach visual → `Execute` → rollback mesh if fail |
| Room broadcast | Local-only | When multiplayer: snapshot `GameStateSnapshot` + intent log |
| try/finally open state | Soft-lock recovery exists | Wrap every command path in finally → `RecoverStuckCombat` |
| Card data ≠ mesh | Mostly true | Enforce `CardInstance` never holds Unity mesh refs in engine |

### Channel watchlist

- Follow **@yugiohshorts** for future deep dives (chains, continuous effects — they invited requests).  
- Architecture videos > art/showcase for our efficiency.

---

## 2. dungiYGO — free open-world YGO RPG (open beta)

### Product shape (from launch stream)

- **Open-world YGO RPG** (community / fan game; Discord-gated beta).  
- **No separate “client download” pitch** in stream language — Discord join → play (moderation).  
- **LOB + Metal Raiders + Magic Ruler** packs only until bugs are ironed out — *then* next set.  
- Features called out: **character create** (hair, eyes, save look), **quests**, **achievements**, **currency**, **pack shop**, **trade**, **multiple decks**, **sleeves**, **map arenas**, **NPCs**, **duels**, overworld flavor.  
- **Server concurrent cap ~40** for beta stability.  
- Heavy praise for **3D collaborator** + a **full-time tester** (“doctor”) finding quest bugs.  
- Explicit: **card bugs block set expansion**; smooth dueling first.  
- Cosmetics / Patreon tiers for support, not pay-to-win packs.

### Map to WRLDZ

| Their lesson | WRLDZ action |
|--------------|--------------|
| **Set gate = quality gate** | Matches our LOB → MRD → SRL curriculum: no new set until DENY/Flip/trap soft-locks are gone |
| Discord-only beta for moderation | Lab: account + TEST DUEL; later invite-only build |
| Character create early | Avatar customizer already; 3D VRM path documented |
| Shop packs LOB/MRD/SRL | Align pack UI with `pre_link_sets.json` |
| Trade needs escape hatch | They hit “cannot exit trade” — every modal needs Cancel + timeout |
| Cap concurrency | Stress test before public map multiplayer |
| Dedicated tester + 3D partner | Solo: mastery stress + review logs stand in for “doctor”; outsource 3D parts |

### Channel watchlist

- **@dungiYGO** — product pacing for open-world YGO, beta ops, what players actually click first (avatar → arena → shop → duel).  
- Twitch / Discord links in their descriptions for live bug culture (process, not assets).

---

## 3. Efficiency priorities for *this* project (ordered)

### P0 — Borrow *process*, not code

1. **Command finally-block** — **DONE (2026-08-10)**  
   `DuelCommandService.Execute`: action lock (`IsProcessingAction`), try/catch → Fail, finally → `RecoverStuckCombat` + deferred battle finish + release lock.

2. **LegalIntent snapshot** — **DONE (2026-08-10)**  
   `LegalIntentService.Build` + cyan/gold/green glows on hand/field; AI can reuse the same snapshot.

3. **Set freeze discipline (dungi)**  
   Public “supported set” = LOB staples → MRD → SRL; `CardEraCurriculum` coverage % is the release gate.

4. **Modal escape hatches**  
   Trade / target / response / inspect always Cancel + Pass; never stuck (their trade bug).

### P1 — Multiplayer when ready

5. WebSocket (or Unity NGO / Mirror) with **intent-only** messages:  
   `{ kind, cardId, zoneIndex, seed }` + server `DuelEngine`.  
6. Broadcast `RulesGameStateSnapshot` after each accepted intent.  
7. Optimistic AR card flight + rollback (Shorts pattern).

### P2 — Product loops (dungi-like, WRLDZ canon)

8. Pack open UI fed by `pre_link_sets.json` (LOB/MRD/SRL only).  
9. Avatar save → map token (done 2D; VRM next).  
10. Invite-only build + bug channel before open map multiplayer.

---

## 4. What *not* to do

- Do **not** port their JS/Unity projects or art into ARGON.  
- Do **not** expand to modern meta sets until LOB/MRD/SRL effects + AI pass mastery stress.  
- Do **not** trust any client-side battle math (same cheat surface they called out).

---

## 5. Suggested next coding sprint (from these two videos)

| # | Task | Source lesson | Est. |
|---|------|---------------|------|
| 1 | `DuelCommandService` finally → `RecoverStuckCombat` | Shorts try/finally | 0.5 d |
| 2 | `LegalIntentService.List(who)` + hand/field glow | Shorts playable list | 1–2 d |
| 3 | Mastery freeze: measure LOB/MRD/SRL gaps, script top DENYs | dungi set freeze | ongoing |
| 4 | Pack open stub for three sets only | dungi shop | 1 d |
| 5 | Document multiplayer intent protocol (design only) | Shorts websockets | 0.5 d |

---

## 6. Channel monitoring

```
https://www.youtube.com/@yugiohshorts   # simulator architecture series
https://www.youtube.com/@dungiYGO       # open-world YGO RPG / beta ops
```

Re-run this note when they post “chains” or “continuous effects” deep dives.
