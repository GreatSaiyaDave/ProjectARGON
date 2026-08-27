# External duel logs & data for AI performance

Scraped/catalogued 2026-08-10 for Project ARGON / WRLDZ.
Goal: improve **duel AI decisions** and **rules performance**, not Konami IP theft.

## Reality check

There is **no large free public dump** of Master Duel / DuelingBook full move logs.
- **DuelingBook** actively blocks bot scrapers (CAPTCHA on replays).
- **Master Duel** has no public match API.
- Best path: **self-play review logs** (`DuelReviewLog` JSONL) + open-source AI projects + deck lists.

## Tier A — Use now (legal, structured)

| Source | What you get | How |
|--------|----------------|-----|
| **Our duel_reviews/** | Full move log (PHASE/SUMMN/ACT/ATK/REACT/AI/BOARD) | Auto after every duel |
| **Stress harness** | 80+ AI-vs-AI lab duels | `DuelStressMenu.RunStressBatch` |
| **lab_ai_heuristics_v1.jsonl** | Battle City / lab decision rules | `corpus/` (this folder) |
| **WindBot decks** (sample) | Competitive AI decklists (64 on GitHub) | `decks_windbot_sample/` |
| **ygo-agent decks** (sample) | RL-trained deck pool | `decks_ygoagent_sample/` |

## Tier B — Open source AI (generate your own logs)

| Project | Notes |
|---------|--------|
| [sbl1996/ygo-agent](https://github.com/sbl1996/ygo-agent) | RL env + self-play; `--record` → `.yrp` replays |
| [ProjectIgnis/windbot](https://github.com/ProjectIgnis/windbot) | Rule-based AI executors per deck (decision logic in C#) |
| [IceYGO/windbot](https://github.com/IceYGO/windbot) | Classic WindBot |

Pipeline: run ygo-agent/WindBot self-play → convert transcripts → merge into `duel_reviews/` JSONL.

## Tier C — Public meta (decks / tournaments, not move logs)

| Source | Notes |
|--------|--------|
| [YGOPRODeck tournament decks](https://ygoprodeck.com/category/format/tournament%20meta%20decks) | Decklists, not turn-by-turn |
| [yugiohmeta.com](https://www.yugiohmeta.com/) | Meta reports |
| [TopDeck.gg API](https://topdeck.gg/docs/tournaments-v2) | Free tournament API (**API key required**) — YGO formats listed |
| [formatlibrary.com/replays](https://formatlibrary.com/replays) | DuelingBook replay DB (often subscription) |

## Tier D — Replays (binary; need parsers)

| Format | Client | Notes |
|--------|--------|-------|
| `.yrp` | YGOPro classic | Action re-enactment; fragile across core versions |
| `.yrpX` | EDOPro | Result-based; more stable ([FAQ](https://projectignis.github.io/faq.html)) |

Parsers: study [ygopro replay.cpp](https://github.com/Fluorohydride/ygopro/blob/master/gframe/replay.cpp); community tools exist but no one-shot public JSON corpus.

## Do **not** scrape hard

- DuelingBook private/replay endpoints (ToS + anti-bot).
- Master Duel private client traffic.
- Konami official match APIs (none public for MD).

## Recommended improvement loop (WRLDZ)

1. Play / stress → `persistentDataPath/WRLDZ/duel_reviews/*.jsonl`
2. Merge with `Tools/WRLDZ/external_logs/corpus/lab_ai_heuristics_v1.jsonl`
3. Mine DENY / REACT failures (e.g. missed Trap Hole) → fix engine
4. Mine AI loss patterns → tweak `SimpleAi` / future learned policy
5. Optionally self-play ygo-agent offline and import win-rate metrics

## Files in this folder

```
external_logs/
  EXTERNAL_LOG_SOURCES.md     ← this catalog
  sources/deck_index.json     ← downloaded deck samples index
  decks_windbot_sample/       ← WindBot .ydk samples
  decks_ygoagent_sample/      ← ygo-agent .ydk samples
  corpus/lab_ai_heuristics_v1.jsonl
  ingest_to_review_jsonl.py   ← merge helper
```

Deck samples downloaded: 9
Heuristic events: 29
