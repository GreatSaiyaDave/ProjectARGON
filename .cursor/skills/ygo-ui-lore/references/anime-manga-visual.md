# Manga / anime visual language (paraphrased)

Kazuki Takahashi's saga is games-as-combat: Shadow Games, Penalty Games, then Duel Monsters as the public sport. ARGON keeps structural TCG truth and borrows *presence* — declarations, disks, holos — not a second rules engine.

Do not quote scripts. Original ARGON plot (Umbrax, Andrax, Referobot, Kuriboh teams) is in `Docs/GDD_Project_ARGON_v1.1.md` and the blueprint. This file is visual/tone only.

## Two technologies in one world

| Look | Source | Use in ARGON |
|------|--------|----------------|
| **KaibaCorp / Battle City** | Night city, rain, neon, industrial blades | Overworld, hub, live duel, disks |
| **Millennium / Egypt** | Gold, stone, Eye, Puzzle, tomb geometry | Story, tome, artifacts, onboarding millennia |
| **Solid Vision** | Holos standing in real space, not a tabletop | AR arena; 2D art on unlit 3D cards (`AR_ANIME_PRESENTATION.md`) |
| **Shadow Game** | Violet-black, stakes, silence between shouts | Umbrax rifts, cult fields, Seal of Umbrax |

Never melt these into one brown-neon mush. GDD: “Every launch should feel like Battle City at night… Story screens use a different look.”

## Era palettes (seasons)

| Season (ARGON) | Anime mirror | Visual cues |
|----------------|--------------|-------------|
| 1 Duelist Kingdom | Island, castle, starchips, table/arena | Warmer lamps, 2000 LP mode, permanent fields, less disk-blade |
| 2 Battle City | Domino night, Rare Hunters, Duel Disks | **Default living-world look** — cyan holos, rain, rooftops |
| 3 Duel Academy | School, uniforms, Sacred Beasts | Cleaner daylight campus overlays; Winged Kuriboh guidance |
| 4 Virtual World | Cyberspace, Noah, Deck Master preview | Teal glitch, grid floors (`MenuAge.LabNecropolis`) |
| 5 Cult of Bakura | Seal distortion, ritual portals | Violet rifts, parchment + blood-gold (`UmbraxRift`) |

## Duel Disk (Battle City gen)

- Worn on the **left forearm** unless the player mirrors to the right.
- Blade holds five monster slots; S/T sit with the disk grammar ARGON already uses (your disk vs mirrored opponent disk).
- Manga: holos project from the disk. Anime: extra projectors into the street. ARGON: **your disk + mirror disk + midfield holos** (`AR_DUEL_PRESENTATION.md`).
- Hand floats in front, **backs toward the opponent**.

## Solid Vision / later disks

- Holos should read at human scale in AR; phone may shrink fidelity, not presence.
- Unlit cel/anime faces, exposure fit for noon vs night (`ArAnimePresentation`).
- ATK/DEF as a small gauge on the monster, not a spreadsheet.

## Millennium Items (visual only)

Seven gold artifacts: Puzzle, Eye, Key, Scale, Rod, Ring, Necklace. ARGON treats tear-boss drops as limited-charge **artifacts** (GDD §3.4), not as a second TCG. In UI: gold stone, Eye motif on the overworld **MENU/Eye** control — never Kuriboh as the map button.

## Characters as UI tone (not portraits to steal)

- **Pharaoh / Puzzle** — gold, destiny, story briefings.
- **Kaiba** — cyan, steel, ranked, disks, “show me.”
- **Pegasus** — carnival ink, Toon/DK fields, Season 1 possession.
- **Marik / Rare Hunters** — street night, Season 2.
- **Bakura** — millennia + violet, cult endgame.
- **Jaden / Winged Kuriboh** — brighter, Season 3 companion voice.
- **Mr. Referobot** — ARGON original: KaibaCorp automaton + Kuriboh glitch. Referee chrome, not a second protagonist portrait on the HUD.

## Shout vs silence

Anime duels cut from quiet LP ticks to a full-screen summon. ARGON:

- LP digits always on (TCG honesty).
- Summon/attack get a short holo pop (120 ms in / 400 ms out, UI_SPEC).
- Referobot one-liners, not a scrollback covering the field.
- Verbal announce is a legal gateway, not karaoke lyrics burned into the HUD.

## What “manga dark” means here

Early Takahashi is belts, shadows, Penalty Games. ARGON's teen-and-up tone can use that for **Umbrax / Shadow Game** surfaces only. The daily overworld stays playable outdoors: cream type, heavy outline, no gray-on-gray.
