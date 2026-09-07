# ARGON mapping (do this in the Unity tree)

## Tokens

| Token | Hex / role | Lives in |
|-------|------------|----------|
| Void | `#05070F` / `DuelystUi.BgDeep` | Stage, sheets |
| Panel | navy glass 92–96% | `FloatingPanel`, Imagine holo |
| Cream | `#FAF8F4` | Titles, LP, names |
| Gold | `#FFD647` / `GoldHot` | Phase, currency, millennia |
| Cyan | `#33EBFF` | You, holos, primary CTA |
| Magenta | `#FF4798` | Opponent, Tear, alerts |
| Ok / Danger | green / `#FF4455` | Confirm / End Turn |

Chrome kit: `StreamingAssets/WRLDZ/Imagine/` via `ImagineAssets` → `DuelystUi`. Fallback OpenDuelyst. No Kenney beige.

## Surfaces

| Surface | Grammar from TCG games | ARGON implementation |
|---------|------------------------|----------------------|
| Live duel HUD | Opposite LP + center phase | `DuelFloatingHud` — YOU cyan left, OPP magenta right, gold phase + M1/BP/M2/EP pips |
| Phase CTAs | GBA/PSP phase menu | Thumb chips Battle / Main 2 / End, legal only |
| Field | Disk blade, not a 2D mat on the map | `ArDuelSpace` disks + holos; digital uses the same stage |
| GY / Deck / Extra | Edge piles | Counts on the YOU island; GY tap opens `GraveyardBrowser` |
| Inspect | On select | `CardInspectPopup` |
| Overworld map | GO chips + YGO pins | `GoTheme` / `OverworldUI` HUD |
| Overworld Eye (home menu) | Same as Hub | `OverworldUI.BuildEyeMenu` → `HubChrome.MountFeatured` / `MountDest` |
| Hub scene | Destination grid (template, not daily home) | `DuelDiskMenuUI`, Battle City rain, piano-glass tiles |
| Hub overlays | Same capsules as hub | `HubChrome` + `DualMenuPresenter` phone frame (header / well / BACK) |
| Story / Tome | Parchment | `MenuAge.ScrollAges`, not NightPurple wash |
| Deck editor | Hub overlay + left-fill collection | `DeckCollectionScreen` via `DualMenuPresenter.BuildFrame`; pin the **panel**, never scroll content |

## Dual presentation

Every menu: `UiPresentation.NonArPortrait` and `ArDiskHolo` (plus rare `ArWorldPanel`). Same intents (`ScreenRouter`). AR parents to the left-arm disk.

## Atmosphere ages

`EgyptianAgesAtmosphere` / `MenuAge`:

| Age | Screens |
|-----|---------|
| PrimordialNight | Boot splash |
| OldKingdom | Title / auth |
| IntermediateKingdom | Systems hub (glass over night, not a tomb wallpaper) |
| NewKingdom | Disk command |
| LabNecropolis | Desktop lab |
| ScrollAges | Story / tome |
| UmbraxRift | AR create / cult |
| Courtyard | Settings / profile |

Overworld uses `WrldzTheme.BuildMapAtmosphere` (Battle City night/day + weather), not the desert ages driver.

## Motion

- Panel open 180–220 ms, fade + 8 px rise (`UI_SPEC.md`).
- LP pulse on change (white flash back to cyan/magenta; danger under 2000).
- No particle spam over names during tablet/pack reveal.

## Copy / lore in UI

- Referobot rulings: short, spoken, legal. No invented effects.
- Story nodes: Pharaoh-aware, player is the outsider (GDD). UI can say “Tear · time distortion” — do not paste anime dialogue.
- Kuriboh is Navi / team, **never** the center map MENU button.
- Format cards show LP + two-line rules (Quick, Shadow, DK, Raid). ERAZ badge is a tray on TCG sheets, not its own format row.

## Anti-patterns

- Master Duel binder on the GPS map.
- Pinning `ChipGrid` content with phase-normalized `x0=0.630`.
- Hiding LP because “AR is cinematic.”
- Egyptian parchment on the Battle City overworld.
- A rectangular phone overlay that does not share hub capsules (`HubChrome`).
- Gray icons without an accent (outdoor fail).
- Shipping Konami screenshots as `bg_hub` / splash.
