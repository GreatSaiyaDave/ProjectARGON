# Artifact cards + soul-card overflow

**Date:** 2026-09-01  
**Status:** Approved for implementation  
**Product:** Duel Monsters WRLDZ / Project ARGON  
**Extends:** `INVENTORY_SPEC.md`, `PROGRESSION_CANON.md`  
**Does not replace:** `DuelEngine` legality. Artifact ids never enter Main/Extra/Side or `cards_db.json`.

## Problem

Key items (currencies, ERAZ badges, Tome, Trade Transport, Millennium artifacts, story keys) live as HUD ints, CSV flags, and backpack pouches. Overflow TCG copies have no timer. The inventory fantasy is cardboard: every item is a card.

## Decision

1. **Artifact cards** are original-IP key items on a grey spell-anatomy YGO face (Artifact orb, not Spell green). They live in an **endless, always-on Artifact Deck Box** (0 backpack cells).
2. **TCG cardboard** stays in boxes / binders / play decks. Overflow becomes a **soul card** (ghost TCG face + Destiny Board ghost, timer where the letter would be). Expiry **removes the copy from the account** (“returned to the aether”).
3. Catalog is `StreamingAssets/WRLDZ/Artifacts/artifacts.json`. Instances are source of truth. `PlayerProgress` currency ints and `erazBadgesCsv` are derived caches.

## Timer (review lock)

Soul-card `expiresUnix` is **wall-clock**. It **keeps running while the player is logged out**, backgrounded, or offline. Login runs `Tick`; anything past `expiresUnix` is already gone.

Pause (push `expiresUnix` by elapsed) **only** during an in-progress duel or the Make Room modal. Home-and-full-boxes freeze (`expiresUnix = 0`) until they leave home.

Durations: Common/Rare 4h, Super/Ultra 8h, Secret/Favorite 12h + 60s modal (one 5-min snooze).

## Membership

**In the box:** Digizeni, Duel-Coin, per-set Set Energy tablets, ERAZ badges, story keys, Tome, Trade Transport, Millennium / tear-boss artifacts.

**Not in the box:** consumables/tools, unopened packs (backpack cargo).

## Out of this slice

Final carved-stone SE tablet art, Millennium 500 LP in-duel activate, story-key episode content, backpack 4×4→8×6 retarget.
