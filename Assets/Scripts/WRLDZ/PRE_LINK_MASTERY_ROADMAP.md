# Pre-Link mastery & release gate

**Goal (your words):** Everything up until **pre-Link monsters** must be **100%** before release.  
**AI goal:** Train on existing cards, master the **next 2 core TCG sets**, then expand set-by-set to the pre-Link wall.

**Canon docs scanned on this machine:**

| Document | Path |
|----------|------|
| Blueprint v1.1 | `~/Downloads/Duel Monsters WRLDZ Blueprint v1.1.docx` |
| Development Plan v1.0 | `~/Downloads/Project_ARGON_Development_Plan_v1.0.docx` |
| Extracted text | `Tools/WRLDZ/extracted_docs/` |
| In-repo | `PRODUCT_VISION.md`, `FLOW.md`, `UI_SPEC.md`, `DMO_AND_AVATAR.md`, `ENGINE_GUARANTEE.md` |

Dev Plan AI phases (confirmed):

1. Rule-based tiers (Easy/Medium/Hard) — **in progress** (`SimpleAi` + `AiHeuristicPolicy`)  
2. ML later  
3. Mr. Referobot commentary  

Blueprint: era-locked content, Grok trains on aggregate play, story AI recreates anime chronology.

---

## Current card DB snapshot

| Metric | Value |
|--------|------:|
| Cards in `cards_db.json` | ~1556 |
| Fusion | 48 |
| Synchro / Xyz / Link | **0** |

DB is already **pre-Link shaped** (early / anime-forward pool). Missing piece is **effect coverage + AI mastery**, not Link monsters.

---

## “Next 2 sets” (TCG core order)

| Order | Set | Code | Role |
|------:|-----|------|------|
| 1 | Legend of Blue Eyes White Dragon | **LOB** | Foundation — normals, early spells/traps |
| 2 | Metal Raiders | **MRD** | **Next set 1** after LOB density |
| 3 | Spell / Magic Ruler | **SRL / MRL** | **Next set 2** — spell density |

Mastery loop per set:

```
Card IDs in set ∩ cards_db
  → regex compile coverage %
  → registry / text runtime coverage %
  → stress AI-vs-AI on set-filtered decks
  → DENY/REACT failures → fix scripts
  → export duel_reviews JSONL → AiHeuristicPolicy / future RL
```

Code: `CardEraCurriculum`, `AiMasteryTrainer`, data `StreamingAssets/WRLDZ/eras/`.

---

## 100% pre-Link definition (release gate)

| Layer | 100% means |
|-------|------------|
| **Structural TCG** | Phases, NS/Set, tribute, battle math, hand size, first turn — always |
| **Effects** | Every non-Normal-Monster card in supported eras either fully compiled **or** legacy-scripted **or** honest refuse (never invent) |
| **AI** | Win rate ≥ target vs random; no soft-locks; Flip/trap response correct on lab + set decks |
| **Formats** | Goat / Edison / pre-MR4 optional later; **no Link** required for v1 |

Wall: stop before first Link-era product (typically **Code of the Duelist** / MR4). Include Fusion + Ritual; Synchro/Xyz when DB expands.

---

## AI training pipeline (what we run)

1. **Live review log** — every duel → JSONL  
2. **Heuristics corpus** — `ai_heuristics_v1.jsonl` → `AiHeuristicPolicy`  
3. **Mastery trainer** — Editor menu / batch: set curriculum self-play  
4. **Optional offline** — ygo-agent / WindBot for volume (see `Tools/WRLDZ/external_logs/`)  

Not “scrape Master Duel.” Self-play + honest coverage metrics.

---

## Avatar (3D path)

See `AVATAR_3D_INTEGRATION.md`.

**Recommendation:** **M3 Character Studio (MIT)** → export **VRM/GLB** → UniVRM in Unity; Takahashi-inspired **original** cosmetics via Imagine.  
Keep existing 2D layered customizer for S23 lab until VRM pack lands.

---

## Menus / navigation (from UI_SPEC + FLOW)

Strange navigation root causes:

1. Overlay stubs for Deck/Bag/Story without deep content  
2. Avatar overlay text instead of real `AvatarCustomizerUI`  
3. Hub vs Overworld “home” confusion  

Fixes in this pass: route **Avatar** → real customizer; **Settings** → real settings screen; clearer MAP home + bottom nav labels.

---

## Visual overhaul (Takahashi-inspired)

Style contract (original WRLDZ art — **not** Konami scans):

- Bold ink outlines, dramatic speed-lines energy  
- Saturated cyan / gold / magenta on deep navy void  
- School / street duel coats, spiked hair silhouettes  
- Holo glass panels with anime flare, not flat cyberpunk-only  

Pack: `StreamingAssets/WRLDZ/Imagine/` + Presentation mirrors.  
Grok Imagine generates **new** chrome; card faces stay `CardArt/`.
