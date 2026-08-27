# ERAZ compile gate — activation without invention

**Date:** 2026-08-27  
**Status:** Draft for review (brainstorming lock; not implemented)  
**Product:** Duel Monsters WRLDZ / Project ARGON  
**Extends:** `Assets/Scripts/WRLDZ/Duel/MASTER_ENGINE_SPEC.md`, `ENGINE_GUARANTEE.md`, `TextEffects/TEXT_EFFECT_PIPELINE.md`  
**Does not replace:** Rulebook structural play in `DuelEngine`. This spec is how **card effects** become legal to activate.

## Problem

The engine already refuses to invent resolutions. Uncompiled text is tagged `[UNIMPLEMENTED]` and activation is rejected. That is honest and also not a duel: most of the 1687-card `cards_db.json` pool cannot use printed effects. Stubs (`CanResolveAny` with leftover unparsed sentences) can activate a fragment — that **is** invention.

Players need:

1. No live “we don’t know this card” refusal on a card that reached the hand/field.
2. No invented or partial effects.
3. Automated learning of new cards onto a **closed** vocabulary.
4. **ERAZ**: nostalgia snapshots (pool, banlist, text, rulings) plus a gate for later TCG.
5. Story badges that unlock ERAZ constructed play. ERAZ is a **subformat of TCG**, not a peer of Dungeon Dice Monsters or GENESYS.

## Goals

- If a card is deck-legal in this duel, its effect has a **complete** program for this ERAZ band. Activate fails only for real YGO (timing, cost, target, OPT, Set this turn).
- Uncompiled / stub programs never enter decks. `[UNIMPLEMENTED]` is a compile/linter line, not a tap-Activate line.
- SpaceXAI (xAI `grok-4.5` via `AiEffectCompiler`) is an **offline compiler only**. Live duels never call a model.
- This coding session (Grok in chat) is **not** in the game.
- Mr. Referobot / opponent ML stays unplugged until Original ERAZ is released and the tutorial badge path is clean.

## Non-goals

- LLM on Activate, damage, or chains.
- Model-invented `EffectActionKind` values.
- Shipping ocgcore or running Lua in Unity (oracle / linter only).
- ERAZ as a format row next to DDM / GENESYS.
- Two different Original **texts** for Duelist Kingdom vs Battle City (same band, same `desc` + rulings).
- First ERAZ badge on account create (it is a **tutorial** reward).
- Duelist Kingdom table rules as a second card database (overlay only).
- Live Firecrawl of Yugipedia during a match.

---

## 1. Authority stack

Live resolution never reads a wiki, a model, or “whatever Konami says this year.” It runs a program compiled for **this ERAZ band**.

```
ERAZ band
  ├── pool          TCG-legal passcodes whose first TCG print ≤ last core date
  ├── banlist       official TCG F/L list frozen at the wall (Modern updates)
  ├── text          desc snapshot for this band (print/PSCT as then)
  └── rulings       closed flags dated to this band
           │
           ▼
  compile onto EffectVocabulary (regex, then optional offline SpaceXAI)
           │
           ▼
  FullyCompiled for (passcode, eraId, textHash)
```

| Input | May | Must not |
|---|---|---|
| Effect text | Produce clauses on known kinds | Invent a missing mechanic |
| Ruling | Set flags already on `EffectClause` (target / `when` vs `if` / Damage Step / OPT / extra cannot-activate) | Add a new resolution; overwrite newer errata in this band |
| Errata | New `TextHash` on **later** bands; recompile those seeds | Rewrite an earlier band’s program |
| Lua / EDOPro | Fail the linter if our program disagrees | Be shipped or used as live rules |

Unexpressable ruling (cannot be a vocabulary flag) ⇒ card is **not** FullyCompiled **for that band** ⇒ stays out of that band’s decks.

Konami/Yugipedia TCG pages are fetched **offline** (Firecrawl), stored with URL + date. The model may propose flags from that fetched text; the validator rejects anything not in the closed set.

**Duelist Kingdom** (2000 LP, no direct, tribute-free, …) is a **story table overlay** on an ERAZ snapshot. It does not fork card text.

---

## 2. ERAZ bands (TCG snapshots)

Walls are exclusive: the next era’s first **TCG core booster** is out. Lists are **TCG**, not OCG.

| Id | Years | Pool through | Next era’s first set (illegal here) | Frozen TCG F/L | Extra tools |
|---|---|---|---|---|---|
| `original` | 2002–2005 | *Flaming Eternity* (FET, 2005-03-01) | *The Lost Millennium* (TLM, 2005-06-01) | April 2005 Lists (TCG) | Fusion only |
| `gx` | 2005–2008 | *Light of Destruction* (LODT, 2008-05-13) | *The Duelist Genesis* (TDGS, 2008-09-02) | May 2008 Lists (TCG) | Fusion |
| `5ds` | 2008–2011 | *Extreme Victory* (EXVC, 2011-05-10) | *Generation Force* (GENF, 2011-08-16) | March 2011 Lists (TCG) | + Synchro |
| `zexal` | 2011–2014 | *Primal Origin* (PRIO, 2014-05-16) | *Duelist Alliance* (DUEA, 2014-08-15) | July 2014 Lists (TCG) | + Xyz |
| `arcv` | 2014–2017 | *Maximum Crisis* (MACR, 2017-05-05) | *Code of the Duelist* (COTD, 2017-08-04) | June 2017 Lists (TCG) | + Pendulum |
| `vrains` | 2017–2020 | *Eternity Code* (ETCO, 2020-06-05) | *Rise of the Duelist* (ROTD, 2020-08-07) | July 2020 Lists (TCG) | + Link |
| `modern` | 2020–present | Current TCG | none | **Current Advanced Format** (moves) | Current Master Rule |

Pool is **cumulative**: every TCG-legal passcode with first TCG print on or before that band’s last core date (boosters, tins, structure decks, promos). A 2024 reprint of Blue-Eyes does not add a 2024-only card to `original`.

Master Rule follows the wall: `vrains` uses the TCG Master Rule in force at ETCO / the July 2020 list (MR5 already existed in April 2020). `modern` uses current Master Rule. Do not run Link procedures in `original` / `gx`.

Banlist JSON is scraped from Yugipedia historic TCG lists (URL + `effectiveDate`). If a list name above is off by one Konami cycle, correct from that page — do not invent Forbidden cards.

`pre_link_sets.json` remains the **set-by-set coverage ladder inside Original**. **TLM moves to GX.** Today’s file includes TLM; that is a bug relative to this spec.

`cards_db.json` remains the Modern/current catalog (art, stats, current `desc`). Older bands use **text snapshots**; they do not overwrite `desc`.

---

## 3. Formats vs ERAZ subformat vs story badges

**Formats** (different games or table laws) — format select cards:

- Quick Duel, Shadow Duel / ranked TCG
- Duelist Kingdom (overlay)
- Raid (Tome)
- Dungeon Dice Monsters
- GENESYS
- Speed Duel / Deck Master (later)

**ERAZ is not a format card.** After the player picks a **TCG** format, they pick an **era badge they own**. DDM and GENESYS never show the ERAZ tray.

```
Format select:  Shadow Duel | DDM | GENESYS | Raid | …
                    │
                    ▼  (TCG formats only)
              ERAZ badge tray  (owned + released = PLAY)
                    │
                    ▼
              DuelEngine + ErazFormat(badge → band + overlay)
```

**Two clocks**

| Clock | Who | Rule |
|---|---|---|
| ERAZ **release** | Us | Band is `released` when coverage says every effect card in its pool is FullyCompiled — **computed**, not a hand-toggled JSON flag |
| Player **badge** | Story / tutorial | No badge → band catalog-visible, not constructed-legal |

A player can own a badge before we have released the band (chip: locked, not released). We can release a later band while the player still only has Original (they cannot select it).

**Badge grant**

- Players do **not** start with a badge on account create.
- Completing the **tutorial** grants the **first** badge: `original`.
- Completing each **story season** grants the **next** ERAZ badge in table order (GX, 5D’s, ZEXAL, …), independent of that season’s anime costume.
- Story S1 Duelist Kingdom and S2 Battle City are **not** two Original texts. Both missions may use `original`. S1 missions may apply the DK overlay. Constructed Original is the full FET snapshot from the tutorial badge.

**Mission-scoped play:** a story chapter names its own snapshot + overlay (S1 = `original` + DK rules; an Academy chapter may use `gx`). That is independent of which constructed badges the player owns. Completing the season still awards the next **ERAZ** badge in table order, not a second Original.

Existing `PlayerProgress.unlockedSetsCsv` is superseded as the constructed gate by ERAZ badges + released bands. Set orbs / collection chrome may remain; they must not legalize a card the four gates reject.

---

## 4. Four stacked gates (all required)

A card may be in a constructed deck for this duel only if:

1. **Band released** — FullyCompiled coverage for that ERAZ pool.
2. **Player owns the badge** — tutorial = Original; later seasons = next band.
3. **In pool** — first TCG print ≤ wall; copies ≤ frozen F/L list.
4. **FullyCompiled program** for `(passcode, eraId)` using that band’s text + ruling flags.

Normal Monsters / effectless Extra are `structural`: they pass gate 4 without an activation program. They still need gates 1–3.

**Lab / Desktop Lab** (`labFullCatalogGranted`, lab decks) may run the lab+starter slice without waiting for Original `released` or a tutorial badge. That path is a developer/lab exception, not player constructed. Player Instant Duel still requires the tutorial badge + released Original.

**In the duel:** Activate on a deck-legal card never returns `[UNIMPLEMENTED]`. Stubs cannot activate. First-play xAI is **off**.

---

## 5. Components

Live interpreter stays `DuelEngine` + `TextEffectRuntime`. ERAZ is a snapshot handed to it.

### Data (`StreamingAssets/WRLDZ/eras/`)

| File | Job |
|---|---|
| `eraz_eras.json` | Seven bands: id, years, last core + date, next first set, banlist id, Extra kinds, Master Rule snapshot. `released` is computed from coverage |
| `banlists/tcg_<list>.json` | Same shape as `banlist_advanced.json` plus `effectiveDate` + source URL. Frozen except `tcg_current.json` (Modern) |
| `text/<era>.json` | `passcode → { desc, textHash, sourceDate }` |
| `rulings/<era>.json` | `passcode → { flags, sourceUrl, retrievedDate, stale }` |
| `compiled_effects_seed_<era>.json` | Programs keyed by `(passcode, eraId, textHash)` |

### Code

| Piece | Owns | Must not |
|---|---|---|
| `ErazFormat` (new) | Current band for the duel; pool; copy limits; Extra kinds; badge + released checks | Guess era from anime flavor |
| `OfficialCardAuthority` | `OfficialText(def, era)` / `TextHash(def, era)` — snapshot first; `cards_db` only for Modern | Serve 2024 PSCT into Original |
| `CompiledEffectCache` | Get by `(id, era, hash)`. Live: cache/seed only | Run a program whose hash ≠ this band’s text; call xAI in play |
| `CardEffectStatus` | `Classify(def, era)`. Deck exclusion **on** for the open band | Let stubs into decks |
| `OfficialEffectRegistry.CanActivateOfficial` | Real YGO refusals only | `[UNIMPLEMENTED]` on a deck-legal card |
| `OfficialDataSources` | `MaxCopies(passcode, era)` | Empty Advanced stub as global law |
| `AiEffectCompileSettings.AllowRuntimeAi` | **Default off** for player builds | First-play hitch |
| Deck builder / format select | Badge tray on TCG formats; reject out-of-pool and uncompiled | ERAZ as a DDM peer; Activate on a stub |
| Account / `PlayerProgress` | Tutorial flag → grant `original` badge; season complete → next id | Grant badge on `DefaultNew()` |

`DuelEngine`, chain, battle, AR unchanged except they read era from `ErazFormat`.

### Compile / learn (offline)

```
era text + ruling flags
  → CardTextEffectCompiler (regex)
  → if incomplete: AiEffectCompiler (Editor/CI, xAI grok-4.5, schema)
  → EffectProgramValidator (known enums only; rulings not dropped)
  → FullyCompiled → seed
  → else gap record (missing kind or unexpressable ruling)
```

**New card rule** (existing `EffectVocabulary.NewCardRule`): if two or more cards need a missing kind, add one shared kind + a regression. Unique one-offs: named `UniqueException`. Never `if (cardId == …)` unless no other card shares the mechanic.

**SpaceXAI:** `AiEffectCompiler` already posts official text to `https://api.x.ai/v1/chat/completions` with a closed JSON schema. Validator rejects unknown actions and empty invents. **Offline bulk / CI only** under this spec. Player devices resolve from seed. This chat agent is not that API.

---

## 6. Tests

Extend, do not skip: `InteractionRegressionTests`, `TcgRegressionTests`, `scan_engine_gaps.py`.

| Check | Must fail before the gate exists |
|---|---|
| Lab/starter activations | Still resolve; no stub “one sentence only” |
| Deck gate | Uncompiled effect card cannot enter an ERAZ deck; Normal Monsters can |
| Activate gate | Deck-legal: fail only for real YGO; never `[UNIMPLEMENTED]` |
| Era snapshot | Same passcode, Original vs later: different hash/program on a functional-errata fixture |
| Badge | No tutorial badge → cannot select Original constructed. Tutorial grants `original`. Season complete → next band only if released |
| Format vs subformat | DDM / GENESYS: no ERAZ tray. Shadow Duel: tray |
| Gap scan | Missing kind / unexpressable ruling listed; Lua disagreement = linter fail, not a live effect |
| Release | Band `released` iff 100% FullyCompiled on its effect pool |

Coverage: lab + starter is already 40/40 playable (dev slice **inside** Original). Original constructed does not open for players until the FET-and-earlier effect pool is FullyCompiled.

---

## 7. Ship order

1. Close compile / deck / activate gates on lab + starter. Turn **off** `AllowRuntimeAi` in player builds. Ban stubs.
2. Tutorial grants `original` badge. No badge ⇒ no constructed ERAZ.
3. Freeze Original snapshot (FET wall, April 2005 TCG list, era text/rulings). Move TLM out of Original set lists.
4. Mark Original `released` only at 100% FullyCompiled for that pool.
5. GX band (and later) as compile completes; story season completion awards the next badge.

Opponent / Referobot integration waits until step 4.

---

## Key decisions

1. **Fail at compile/deck, not at Activate** — live refusals are real YGO only.
2. **Stubs are illegal** — partial compile cannot activate or enter decks.
3. **Closed vocabulary** — new cards map onto existing kinds or wait; AI cannot invent kinds.
4. **SpaceXAI is offline compile** — not a duel brain; this chat is not in the client.
5. **ERAZ freezes pool + banlist + text + rulings** per band; compile key is `(passcode, eraId, textHash)`.
6. **ERAZ is a TCG subformat** selected by badge tray; DDM/GENESYS are formats.
7. **Tutorial grants the first badge (Original).** Later badges come from finishing story seasons, in ERAZ table order. Story costume ≠ band text.
8. **DK vs Battle City do not split Original card text.** DK is an overlay.
9. **Two clocks:** we release a band when it compiles; the player selects it when they own the badge.
10. **Lua is a linter**, not law. Historic F/L lists come from Yugipedia TCG pages with URL + date.

## Open questions (none blocking)

- Exact TCG F/L **list id** if Yugipedia names a neighboring cycle; correct from the page when scraping, do not guess Forbidden cards.
- Optional DK-era **set-wall** inside Original for *story missions only* (constructed Original is full FET after tutorial). Not required to start.

## Implementation slices (for the later plan)

1. Gates on current cache: `FullyCompiled` required; `ExcludeUnimplementedFromDecks` on for the active pool; `AllowRuntimeAi` off; activate path never `[UNIMPLEMENTED]` for deck-legal cards. Tests.
2. `eraz_eras.json` + `ErazFormat` + tutorial badge on `PlayerProgress`. Format select tray vs DDM/GENESYS. Tests.
3. Original text/rulings/banlist snapshots; TLM moved to GX; cache key includes era. Tests.
4. Offline bulk compile + gap scan per era; Original `released` when coverage is 100%. Tests.
5. Later bands + season → next badge, as data exists.
