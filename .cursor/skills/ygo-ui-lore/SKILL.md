---
name: ygo-ui-lore
description: Yu-Gi-Oh! TCG-game UI, visual language, and manga/anime lore playbook for Project ARGON / Duel Monsters WRLDZ. Use when designing or restyling menus, duel HUD, overworld chrome, story presentation, or lore-facing copy. Not for inventing card resolutions (use ygo-gamedev) and not for generic Unity Editor automation (use unity-skills).
---

# YGO UI, look, and lore (ARGON)

Read this skill before changing player-facing chrome, duel HUD, overworld atmosphere, or story/codex presentation. Prefer ARGON docs (`UI_SPEC.md`, `VISUAL_SYSTEM.md`, `PRODUCT_VISION.md`, `GDD_Project_ARGON_v1.1.md`) over copying a Konami client.

## Hard rules (this repo)

1. **Do not paste Konami chrome.** No Master Duel / Duel Links / LOD screenshots on hub, splash, or map (`ASSET_DIVE.md`). Imagine piano-glass + OpenDuelyst fallback is the kit.
2. **Do not scrape** DuelingBook, Master Duel traffic, or private clients. Public layout notes and rulebook structure are enough.
3. **Paraphrase lore.** Do not dump manga/anime scripts. Original ARGON story (Umbrax, Referobot, Kuriboh teams) lives in the GDD / blueprint.
4. **Two worlds, two looks.** Living world = Battle City night (neon, rain, disks). Story / tome / millennia = parchment, gold, Egyptian ages. Do not wash the overworld in temple brown.
5. **TCG is the core loop.** Digital clients teach HUD grammar (LP, phase, zones, GY). Pokémon GO teaches *map* chrome only. Anime Solid Vision teaches *presence*, not a second rules path.
6. **UI never invents game state.** LP, phase, zone counts, and lock badges read `DuelEngine` / progress only.

## Route

| Task | Read |
|---|---|
| How TCG video games lay out a duel | [references/tcg-clients.md](references/tcg-clients.md) |
| Manga/anime eras, disks, Solid Vision, Millennium look | [references/anime-manga-visual.md](references/anime-manga-visual.md) |
| How ARGON should map those lessons | [references/argon-visual-map.md](references/argon-visual-map.md) |
| Sources (public, paraphrased) | [sources.md](sources.md) |
| In-repo visual tokens | `Assets/Scripts/WRLDZ/UI/VISUAL_SYSTEM.md`, `UI_SPEC.md`, `MENU_DUEL_DISK.md` |

## Visual north star (one sentence)

Battle City rooftop at night: life-size holos, KaibaCorp cyan on the player's disk, magenta on the opponent, gold for phase and LP, cream type with a heavy outline — GO-soft chips on the map, never a Master Duel binder covering the street.

## When you are stuck

- If a submenu looks like dest tiles covering the map, rebuild it with `HubChrome.QuietFill` and a compact `GetWindowAnchors` window. Keep `MountFeatured` / `MountDest` for the Eye only.
- If a screen feels like a tournament client, strip chrome until the stage / map is the hero.
- If a screen feels like a generic sci-fi menu, restore gold phase, cyan/magenta LP, and filament glass.
- If story type appears on the overworld, move it to `MenuAge.ScrollAges` / parchment — keep the map Battle City.
- For rules, stop and open `ygo-gamedev`. This skill does not resolve chains.
- AR spatial graph: `wrldz-ar-lenses`. Map pins / SE / inventory: `wrldz-overworld`. Overlay dest-tile vs quiet sheet: keep `MountFeatured` / `MountDest` on the Eye only.
