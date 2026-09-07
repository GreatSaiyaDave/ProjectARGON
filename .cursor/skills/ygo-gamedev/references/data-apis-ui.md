# Data APIs and Unity TCG presentation

Summarized from YGOPRODeck API v7, ARGON export tools, and indexed captions from Unity TCG/event-system videos (see [sources.md](../sources.md)).

## YGOPRODeck API v7

- Base: `https://db.ygoprodeck.com/api/v7`
- Main endpoint: `/cardinfo.php` (filters: `name`, `fname`, `id`, `type`, `atk`/`def`/`level` with `lt`/`lte`/`gt`/`gte`, `race`, `attribute`, `archetype`, `banlist`, `format`, dates, …)
- **20 requests / second.** Exceed → blocked for **1 hour**.
- Cache locally. ARGON already snapshots into `cards_db.json` / extra indexes — do not hammer the API from Play Mode or a Cloud Agent loop.
- **Do not hotlink images.** Download once, rehost. Hotlinking gets IPs banned.
- `format=tcg` ≈ cards with a TCG release date (excludes Speed/Rush). `misc=yes` adds extra fields. Genesys points need `format=genesys`.
- `tcgplayer_data` was removed (2026-09-03 changelog).

Other useful lists: `/cardsets.php`, archetypes, check DB version before a full pull.

Passcode (`id`) is the 8-digit printed code. `konami_id` is a different number.

## Deck construction UX (simulators + ARGON)

EDOPro / Master Duel construction tables keep **Main, Extra, and Side on screen together**. Filters search the **collection**, not the deck piles. Collection chips must fill their well from the **left**; do not pin scroll **content** with phase-normalized anchors (that packs chips into the right strip).

Construction rules of thumb (TCG):

- Main 40–60, Extra ≤15, Side ≤15
- Copies: usually 3, with F/L overlay
- Extra pile is horizontal; Main is a wrapping grid

## Unity presentation lessons (from TCG/Unity videos)

These are **view** rules. They do not replace `DuelEngine` / ocgcore.

- **Server/core decides legality; clients animate.** Mirror/PUN-style: command on the authority, RPC to play DoTween. Do not simulate draws twice.
- **Event hub ≠ chain.** A static `Action` bus is fine for UI ("card selected", "lp changed"). Chain links still go through `ChainStack` / core messages.
- Unsubscribe on destroy or you get missing-listener crashes after leaving a duel.
- Draw/summon: spawn at scale 0 or off-camera for a frame, then ease in. Keep raycasts off the hand `CanvasGroup` when it is not that player's window.
- Hand as a reorderable list is a presentation choice; ARGON's duel hand is still a rules zone.

## What not to scrape

Already policy in `Tools/WRLDZ/external_logs/EXTERNAL_LOG_SOURCES.md`:

- DuelingBook replay endpoints (CAPTCHA / ToS)
- Master Duel private traffic
- Live Firecrawl of Yugipedia **during a match**

Legal structured sources: own `duel_reviews` JSONL, WindBot / ygo-agent self-play, YGOPRODeck **decklists** (not move logs), TopDeck.gg with a key.
