# Duel Monsters Online (DMO) research + avatar system

## What is Duel Monsters Online?

**Duel Monsters Online (DMO)** is an **independent / fan-made** YGO-style MMO in early access (community: r/DMOGame, social clips). It is **not** an official Konami product and **not** affiliated with Project ARGON / WRLDZ.

Reported player-facing features (from public clips / community talk — may change):

| Area | DMO vibe | What we take as **inspiration only** |
|------|----------|--------------------------------------|
| Character create | Name + clothing / look design | Full **avatar customizer** (body, hair, outfit, colors, title) |
| Overworld MMO | Walk a world, meet duelists | GO-style map + avatar token (already WRLDZ canon) |
| Duel drama | Anime-forward social duels | Our anime TCG engine + timed reactions (separate system) |
| Progression | Rank / path fantasy | Spirit Rank, titles on profile |

## What we can **not** use

- DMO art, models, UI packs, audio, logos, or code  
- Konami card frames / official assets beyond our own licensed/data pipeline  
- Copying DMO screenshots into the game as textures  

**Legal / product rule:** inspiration for *features and flow* only. All pack-ins under `StreamingAssets/WRLDZ/` are original.

## WRLDZ avatar system (implemented)

### Data (`AvatarAppearance`)

Stored on each account (`avatar` + legacy `avatarColor`):

- Body / hair / eyes / outfit / accessory indices  
- Skin, hair, outfit tint, accent hex colors  
- Title (e.g. Spirit Dueler, Tear Hunter)

### Menus

1. **Profile** — portrait, name, rank, title → **Customize Avatar**  
2. **Customizer** — live preview, part chips, ‹ › cycle, color swatches, display name, **Save look**

Entry points:

- Overworld **profile orb** (top-left)  
- Hub **Profile & Avatar** row + face button  

### Presentation

- Layered sprites: `StreamingAssets/WRLDZ/Avatar/`  
- `AvatarPortraitView` on map token + HUD orb + customizer  

### Related menus (roadmap)

| Menu | Status |
|------|--------|
| Profile sheet | Done |
| Avatar customizer | Done |
| Title unlocks via rank | Stub titles list |
| Wardrobe shop / cosmetics currency | Not yet (Energy row stub) |
| First-time create wizard after Terms | Optional next (auto-open customizer once) |

## Pokémon GO vs DMO vs WRLDZ

| Shell | Source |
|-------|--------|
| Map HUD orbs, nearby, main menu button | **Pokémon GO** non-AR template |
| Character look + social MMO fantasy | **DMO-like** create / walk / duel loop (inspired) |
| Cards, disks, anime combat | **YGO / anime TCG** (our engine) |
