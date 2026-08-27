# Visual system — one kit, one language

## Rule

**Primary chrome:** Grok Imagine piano-glass pack at `StreamingAssets/WRLDZ/Imagine/`  
(`ImagineAssets` → preferred by `DuelystUi`, `WrldzPresentation`, `FlowChrome`, `GoTheme`).  
Navy glass, thin cyan/gold rims, gold L-ticks. No candy capsules, no Kenney beige.

**Menu atmosphere:** animated Egyptian / night / ages backdrop via  
`EgyptianAgesAtmosphere` — each host picks a `MenuAge` (Primordial, Old Kingdom,  
New Kingdom, Lab Necropolis, Scroll Ages, Umbrax Rift, Courtyard…).  
Stars, dunes, pyramids/pylons, hieroglyphs, gold dust, and torch flicker animate per age.

**Fallback kit:** `StreamingAssets/WRLDZ/Vendor/OpenDuelyst/ui/`

Do **not** mix Kenney beige panels, random AI panels, or photo backgrounds into menus.

## Code entry

| Class | Role |
|-------|------|
| `DuelystUi` | Sprites + locked palette |
| `FlowChrome` | Boot / onboarding |
| `GoTheme` | Overworld + hub layout |
| `WrldzTheme` | Colors + canvas |

## Assets (family)

| Role | File |
|------|------|
| Menu panel | `frame_quest@2x.png` |
| Bar / toast | `bottom_bar_background@2x.png` |
| Primary CTA | `button_primary@2x.png` (cyan hex) |
| Secondary | `button_secondary@2x.png` |
| Confirm | `button_confirm@2x.png` (green) |
| Gold / end | `button_end_turn_mine@2x.png` |
| Cancel | `button_cancel@2x.png` |
| Circle / back | `button_back@2x.png` |
| Avatar ring | `dialogue_border@2x.png` |

## Palette

- Background `#0A0F14`
- Panel dark slate
- Text cream / muted blue-grey
- Accent cyan (primary), gold, green (ok), red (danger)

## Content art (separate from chrome)

- Map tiles: `Presentation/map_overworld*.png`
- Navis: `Presentation/navi_*` (onboarding / profile — **not** the map menu button)
- Card faces: `CardArt/{id}.jpg`

Chrome = Duelyst.  
**Center overworld control = MENU button (Duelyst primary), never Kuriboh card art.**  
Kuriboh = team / Navi identity only.
