---
name: ygo-gamedev
description: Yu-Gi-Oh! TCG game-development playbook for Project ARGON / Duel Monsters WRLDZ. Use when implementing or reviewing duel rules, chains, PSCT, card effects, deck construction, ocgcore/EDOPro/YGOPro scripting, YGOPRODeck/CDB data, or Unity TCG presentation. Not for generic Unity Editor automation (use unity-skills) and not for inventing live resolutions.
---

# YGO game development (ARGON)

Read this skill before changing duel rules, effect compilation, card data, or simulator/lab paths. Prefer the matching reference over improvising Konami mechanics.

## Hard rules (this repo)

1. **Cards are data.** Do not add `if (cardId == …)` except a named unique exception (`MASTER_ENGINE_SPEC.md`).
2. **Cost ≠ effect.** Costs pay at activation, before the chain link exists, and are never refunded if the link is negated.
3. **The chain is a LIFO resolver**, not a pile of Unity events.
4. **Konami TCG is ground truth.** AR/WRLDZ flavor is config on top, not a rewrite of Fast Effect Timing.
5. **Product path** = `DuelEngine` + `OfficialEffectRegistry` + `TextEffects` / ERAZ compile gate. **Lab path** = Unity as a viewer over `edo9300/ygopro-core`. Do not ship Lua as live product rules. Do not mix Konami art into the AGPL ocgcore folders.
6. **Offline compile only.** No LLM on Activate. Yugipedia/YGOPRODeck fetches belong in tools, never in a match.
7. **Do not scrape** DuelingBook private replays or Master Duel client traffic.

## Route

| Task | Read |
|---|---|
| Colon/semicolon, targeting, conjunctions, when vs if | [references/psct-and-timing.md](references/psct-and-timing.md) |
| Who acts next, open game state, SEGOC | same file, Fast Effect Timing section |
| YGOPro / EDOPro / ocgcore split, C API loop | [references/simulator-stack.md](references/simulator-stack.md) |
| Lua `initial_effect`, CCTO, CDB layout | [references/lua-scripting.md](references/lua-scripting.md) |
| YGOPRODeck API, images, deck UX | [references/data-apis-ui.md](references/data-apis-ui.md) |
| How ARGON maps all of that | [references/argon-mapping.md](references/argon-mapping.md) |
| Where this knowledge came from | [sources.md](sources.md) |

## Effect kinds (closed vocabulary)

Map printed text onto timing + cost + resolution. If all three exist, add a PSCT fragment — **no new `EffectActionKind`**. If two or more cards need a missing kind, add one shared kind plus a regression. Unique snowflakes stay named exceptions.

Activated effects (colon or semicolon on the print) are the only effects that start a Chain. Continuous modifiers and summoning procedures do not.

## When you are stuck

- Check printed PSCT, then Fast Effect Timing, then this repo's engine spec — not a wiki anecdote.
- For lab disagreements, treat Ignis Lua as a **linter oracle**, not something to paste into C#.
- For presentation, keep simulation on the authority path and animation on the client (see data-apis-ui).
- AR sessions / disks: `.cursor/skills/wrldz-ar-lenses/`. Overworld / SE: `.cursor/skills/wrldz-overworld/`. Headless checks: `.cursor/skills/wrldz-cloud-verify/`.
