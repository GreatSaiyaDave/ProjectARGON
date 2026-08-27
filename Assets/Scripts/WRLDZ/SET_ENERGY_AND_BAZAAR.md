# Set Energy · Stone Tablets · Bazaar Zones (canon deep dive)

**Sources reconciled:** Blueprint v1.1 §3–§6 · Project ARGON Plan · UI_SPEC §3.4 · PROGRESSION_CANON · product conversation locks.  
**Status:** Design lock. Hub Bazaar is a UI stub; spatial zones + SE services not fully implemented.

---

## 1. What Set Energy is

**Set Energy (SE)** is the **collection fuel** of WRLDZ. It is **set-tagged** (LOB, MRD, SDK, story pack ids, etc.) and is **sacrificed / offered** to **stone tablets** to open **packs of 10 cards from that set**.

Lore bridge (Blueprint story zones): defeated shadow spirits become energy players later **sacrifice to altar stone tablets for cards**.

| Is SE | Is not |
|-------|--------|
| Currency to open set tablets → random 10-card packs | Generic soft cash for all shops |
| Story / PvE / walk / altar / raid progression | **PvP win rewards** (Blueprint: **No PvP energy**) |
| Era-aware (which sets you can usefully farm) | Pay-to-win ladder print |

**HUD** may show total SE; **balances** live **per set id**:

```
setEnergyBySet: { "LOB": 40, "MRD": 12, … }
```

Legacy single `progress.setEnergy` = sum until fully migrated.

**Related currencies** (Blueprint / UI_SPEC):

| Currency | Role |
|----------|------|
| **Digizeni (Đ)** | Soft premium · Rare Hunter fees · general soft spend |
| **Duel Coins / Duel-Coin** | Packs cosmetics path · auctions (Witty Phantom) · battle pass premium (Blueprint crypto path) |
| **Set Energy** | Tablet openings · set collection |
| **Starchips / gloves** (S1 DK tourneys) | Seasonal gamble entry (shop / IRL partners) |

---

## 2. Primary sink: stone tablets → 10-card packs

### Loop

```
Earn SE of set X  →  Approach stone tablet (Bazaar / story altar)
                  →  Offer / sacrifice SE of set X
                  →  Pack opens: 10 random cards from set X
                  →  Collect into binder / home box
```

This is **one primary way players earn random cards** (Blueprint §5 Card Acquisition).

| Rule | Detail |
|------|--------|
| Pack size | **10 cards** (Blueprint + ARGON Economy v0.5 pity) |
| Cost | **1,000 SE of that set = one 10-card pack** (GDD v1.1 lock; flat, not rarity-scaled) |
| Pool | Cards from **that set only** (era-legal pool may still filter constructed use) |
| Pity | ARGON Plan: basic pity on pack opening |
| Reveal UX | UI_SPEC: closed tablet → crack/glow → sequential reveal → **Collect** |

### Secondary card acquisition (Blueprint)

| Path | Notes |
|------|--------|
| Tablet packs (SE) | Random set product |
| Consecutive **non-PvP** wins | Every **3 wins → 1 pack (10 cards)**; energy rewards can streak up to **5×** at peak |
| Sacrifices (cards → SE) | Rarity-scaled; **max return ≈ half-pack** value |
| Trades / auctions | Witty Phantom (Duel-Coin); player trades |
| Raid boss energy | Monster-specific energy; **~10 packs equivalent** to unlock a copy (artifact → card) |
| Dailies / raids / boosts / challenges | XP + energy + occasional packs |
| Battle pass free track | “100+ packs/season” scale (Blueprint) |
| Mascot free item boxes | Occasional free packs |

### Explicit non-sources

| Mode | SE / tablet energy |
|------|---------------------|
| **PvP** (ranked, street PvP ladders) | **None** (Blueprint: No PvP energy) |
| Practice / pure lab smoke | No story SE (XP rules per PROGRESSION_CANON) |

---

## 3. How SE is earned (multi-path)

### A. Story mode — low random, position-gated

| Source | Amount | Set tag |
|--------|--------|---------|
| Story chapter / map nodes | **Low · random** | Sets allowed by **story position** (era lock) |
| Story tears / chapter duels | Low–mid | Story-active sets |
| Walk while in story geography | Low random (see walk) | Current season / chapter sets |

**Era lock:** Sets release **chronologically** (Blueprint: ~**6 month** chronological unlock cadence, **10-set daily rotations** + double-bonus windows). Constructed / story-legal use respects season until **Season 5** Extra Monster mayhem / wider pool.

### B. CPU / campaign opponents — larger, deck-set-specific

| Source | Amount | Set tag |
|--------|--------|---------|
| Defeat story / campaign AI | **Larger** | Sets **featured in that opponent’s deck** |
| Chapter bosses / Rare Hunter story nodes | Larger band | Their list / era |

Aligns with “farm the Kaiba deck era to open Blue-Eyes-era tablets.”

### C. Exploration walk (Blueprint)

| Source | Amount | Cap |
|--------|--------|-----|
| Miles walked | **1–2 card-energy per mile** (economy-adjusted) | Daily **~20** |
| Weekly distance | Ultra pack + flair (e.g. 50 miles) | Weekly |
| AR landmark scans | XP / lore / sometimes packs | Soft |
| Junkuriboh “Scrap Squad” | **+10% set energy from tears** | Mascot perk |

Walk SE is **set-tagged** to **current story / rotation sets**, not free any-set printing.

### D. Tears · raids · events

| Source | Notes |
|--------|--------|
| Tear clear / tear boss | Set / monster energy; rarer drops |
| Archetype invasion zones | Duels for set energy |
| Raids | Contribution-scaled set/monster energy (cap per duel, e.g. 15k energy units in Blueprint scale) |
| Global invasions | “Massive energy” group clears |

### E. Bazaar altars — cards → SE of that set

| Action | Result |
|--------|--------|
| Offer owned copies at **altar** | Gain **SE of the set that card belongs to** |
| Rate | Rarity-scaled; Blueprint **max ≈ half-pack** equivalent per sacrifice |
| Use | Turn bulk / wrong-set extras into the SE you need for tablets |

Cannot burn quest-bound cards or last copies locked in equipped play decks without un-equip confirm.

### F. Never from PvP

Ranked / PvP awards **XP, flair, Duel-Coin paths** (Blueprint) — **not SE**. Keeps ladders from printing collections.

---

## 4. Bazaar zones — pure economic zones

### Identity

Bazaar is a **zone type** on the overworld, alongside Raid / PvP / Training / Tournament (Blueprint + ARGON Plan: “Bazaar (pack buying)”).

**Purely economic** — not a duel arena of record. You come here to:

- Offer SE to **stone tablets** → **10-card packs**
- Use **altars** (card → SE)
- Trade / auction via **dynamic NPC shopkeepers**
- Buy challenges / cosmetics hooks (Đ / DC)

### Real-world placement (Blueprint)

| Real place | In-game hub flavor |
|------------|-------------------|
| **Participating card shops / game stores** | **Kame Game Shop** (tutorial + bazaar) |
| Malls / partnered small businesses | Bazaar / vendor floor |
| Partner kits | Physical “duel arena” / AR kits at venues |

Blueprint overworld: *card/game shops as Kame Game Shop (tutorial/bazaar)*; *Bazaar trading/buying (tied to shops/malls; global auctions via Witty Phantom)*.  
**Partner businesses** get kits; Mr. Referobot optimizes equitable pin placement (urban / rural).

IRL tournaments and events also use **game stores / malls / conventions** — Bazaar is the **everyday commerce** sibling, not the bracket.

### Zone Mode entry

```
Map pin · Bazaar Zone (geofenced partner shop / mall node)
  → Approach → Zone Mode prompt
  → Enter AR shell or digital Bazaar shell
  → No mandatory duel — economy UI + spatial vendors
```

Hub `MenuId.Bazaar` = same systems **without GPS** (lab / indoor).

### Floor features

| Feature | Role |
|---------|------|
| **Stone tablets** | Spend **set SE** → open **10-card pack** of that set |
| **Altars** | Sacrifice cards → SE of card’s set |
| **Dynamic NPC shopkeepers** | Living vendors (stock, dialogue, limited listings); Grok/living-world NPC layer |
| **Witty Phantom** | Immunity-spirit NPC: **auctions / free cards** (Duel-Coin economy) |
| **Challenges board** | Daily/weekly (may grant small story-set SE, not PvP SE) |
| **Stone tablet pack open FX** | UI_SPEC sequential reveal |

### Dynamic NPC shopkeepers

Blueprint living world: random duelists, civilians, immunity spirits, out-of-time characters. For Bazaar specifically:

| NPC type | Function |
|----------|----------|
| Generic shopkeepers | Buy/sell flavor, set rotation tables, dialogue |
| **Witty Phantom** | Auctions, free-card moments |
| Seasonal vendors | S1 starchips/gloves; S2 Rare Hunter gamble hooks nearby (not pure Bazaar combat) |
| Partner-shop skins | Real business branding / limited cosmetics (partnership revenue) |

NPCs are **economic actors**, not required duel gates to use tablets.

---

## 5. Seasons & era lock (collection timing)

| Band | Content |
|------|---------|
| **S1–S4** | Story arcs (DK → BC → GX → Virtual); cards **era-locked** for legal use / release cadence |
| **S5+** | Cult wars; **Extra Monster mayhem** / wider modern tools; ongoing content |
| Pack rotations | Post-tutorial all sets *catalog-visible* with **era-locked release**; **10-set daily rotations** + double SE windows |

Competitive gold-frame formats stay separate from story season portals (UI_SPEC).

---

## 6. Full economy map (one diagram)

```
                    WALK / STORY NODES ── low random SE (story-gated sets)
                              │
 TEARS / RAIDS / EVENTS ──────┼── mid–high SE / monster energy
                              │
 CPU STORY DECKS ─────────────┼── larger SE of opponent’s set tags
                              │
 CARD ALTARS (Bazaar) ────────┼── card → SE of that set
                              │
                              ▼
                    SET ENERGY (per set)
                              │
                              ▼
              STONE TABLET ── sacrifice SE ──► 10-CARD PACK (that set)
                              │
                              ▼
                    COLLECTION (binder / box / decks)

 PvP ──────────────────────── no SE
 Duel-Coin / Digizeni ─────── parallel (cosmetics, auctions, soft premium)
```

---

## 7. What exists in code today vs blueprint

| Piece | Blueprint / design | Code today |
|-------|-------------------|------------|
| SE on account / HUD | Yes | `setEnergy`, SE cell, HudTokens |
| Per-set SE | Implied by “set energy” | **Not split yet** |
| Stone tablet → 10-card pack | Core acquisition | **Stub** (Bazaar menu copy) |
| Pack open sequence | UI_SPEC tablet crack/reveal | Partial Imagine assets |
| Bazaar hub | MenuId.Bazaar | Overlay stub text |
| Bazaar map zones at shops | Kame / partners | Overworld pins generic; no partner geofence |
| Dynamic shopkeeper NPCs | Living world + Witty Phantom | Not implemented |
| Altar convert | Sacrifice rarity-scaled | Not implemented |
| No PvP SE | Explicit | Award paths need flag when built |
| Walk SE 1–2 / mile cap 20 | Explicit | Map walk exists; SE grant not wired |
| 3-win pack streak | Explicit | Not implemented |
| Era lock S1–4 / S5 Extra | Story seasons | Season portal pin flavor only |

---

## 8. Implementation map (when building)

| Service | Responsibility |
|---------|----------------|
| `SetEnergyService` | Per-set balances, grant, spend, caps |
| `StoneTabletService` | Cost table, open 10-card pack, pity |
| `BazaarService` | Altar convert, vendor listings |
| `BazaarZone` pin | Partner shop geofence / indoor lab mock |
| `NpcShopkeeper` | Dynamic stock + dialogue hooks |
| `ProgressionService` | Story SE rolls; CPU deck set-tags; **skip SE if PvP** |
| Deck/catalog | `setId` / `era` / `seasonMin` on cards |

---

## 9. Design locks from conversation (reconciled)

1. **SE → stone tablets → packs of 10** from that set (random card earn path).  
2. **Story:** low random SE by story position (era).  
3. **CPU decks:** larger SE of that deck’s sets.  
4. **PvP:** no SE.  
5. **Altars:** cards → SE of card’s set.  
6. **Bazaar:** pure **economic** zone at **participating game shops / businesses**.  
7. **Dynamic NPC shopkeepers** on the bazaar floor.  
8. **S1–4 era lock; S5+ Extra Monster mayhem.**
9. **1,000 SE of set X = one 10-card pack of set X** (GDD v1.1).

Blueprint walk/tear/raid SE and 3-win packs remain valid **additional** faucets under the same “no PvP SE” rule.

---

## 10. One-line pitch

**Set Energy is what the story, the street, and the Bazaar give you; stone tablets are where you spend it for 10-card packs; partner shops are where the economy lives — never the PvP ladder.**
