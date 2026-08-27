# WRLDZ Imagine — Full Visual Overhaul

Original art generated with **Grok Imagine** for Project ARGON / Duel Monsters:WRLDZ.

**Style contract:** piano-glass navy × Master Duel gold/cyan. Thin luminous rims, gold L-corner ticks, no candy gloss, no circuitry clutter. Icons are line glyphs (cyan stroke, gold accent) with no circular frames. No Konami screenshots; card faces stay in `CardArt/`.

## How it loads

| Class | Role |
|-------|------|
| `ImagineAssets` | Direct pack paths |
| `DuelystUi` | **Global chrome** — Imagine first, OpenDuelyst fallback |
| `WrldzPresentation` | Full-bleed scenes + emblems |
| `FlowChrome` | Boot/onboarding atmosphere + panels |
| `GoTheme` / `HudTokens` / `MenuShell` | Overworld + hub |
| `DuelUI` | Stage wash, hand frame, phase pills, zone rims, ATK/DEF badges |

Almost every screen that already used `DuelystUi.Panel()` / `BtnPrimary()` picks up the overhaul automatically.

## Layout

```
Imagine/
  bg/       Full-bleed scenes
  icons/    HUD + nav icons + emblem
  pins/     Map markers
  ui/       Panels, buttons, bars, duel chrome
  fx/       Summon / impact overlays
  manifest.json
```

## Inventory

### bg/
| File | Screen |
|------|--------|
| `bg_splash.png` | Boot / title atmosphere |
| `bg_hub.png` | Onboarding + hub lounge |
| `bg_menu_void.png` | Systems hub void |
| `bg_overworld_map.png` | GPS map texture |
| `bg_duel_stage.png` | Duel stage wash |

### icons/
| File | Use |
|------|-----|
| `icon_digizeni.png` | Digizeni |
| `icon_duel_coin.png` | Duel coins |
| `icon_set_energy.png` | Set energy |
| `navi_spirit.png` | Navi token |
| `icon_compass.png` | Map compass |
| `icon_deck.png` | Deck nav |
| `icon_bag.png` | Inventory |
| `icon_story.png` | Story |
| `icon_settings.png` | Settings |
| `icon_menu.png` | Hub menu |
| `icon_duel.png` | VS AI / VS Player / Free View |
| `icon_bazaar.png` | Bazaar |
| `emblem_spirit_eye.png` | Brand / spirit eye |

### pins/
| File | Use |
|------|-----|
| `pin_tear.png` | Dimensional tear |
| `pin_portal.png` | Portal / anchor |
| `pin_arena.png` | Arena site |
| `pin_treasure.png` | Loot node |

### ui/
| File | Use |
|------|-----|
| `panel_holo_glass.png` | 9-slice glass panel |
| `panel_modal.png` | Modal / floating sheets |
| `button_primary_plate.png` | Primary CTA |
| `button_primary_hover.png` | Primary hover |
| `button_primary_pressed.png` | Primary pressed |
| `button_secondary_plate.png` | Secondary |
| `button_danger_plate.png` | Cancel / danger |
| `button_gold_plate.png` | Gold CTA / end turn |
| `button_circle.png` | Circle orb |
| `button_close.png` | Close |
| `tile_hub.png` | Hub destination tile |
| `tile_hub_gold.png` | Featured / gold tile |
| `bar_bottom.png` | Bottom nav / HUD strip |
| `bar_top_hud.png` | Top HUD strip |
| `hud_chip_plate.png` | Currency chips |
| `level_ring.png` | Profile ring |
| `lp_bar_frame.png` | LP chrome |
| `hand_frame.png` | Hand tray |
| `input_field.png` | Text fields |
| `dialogue_bubble.png` | Referobot bubble |
| `zone_monster_plate.png` | Field zone rim |
| `phase_active.png` / `phase_idle.png` | Phase pills |
| `badge_atk.png` / `badge_def.png` | Field position |
| `card_back_wrldz.png` | Original card back |
| `bg_duel_stage_portrait.png` | Portrait stage alt |

### fx/
| File | Use |
|------|-----|
| `fx_summon_burst.png` | Summon overlay |
| `fx_impact_slash.png` | Battle impact |

## Notes

- Files may be JPEG-encoded data under `.png` names; Unity `LoadImage` accepts both.
- **Chroma green is isolation only** — icons/pins/ui/fx are baked with real alpha (green keyed out).
  Raw green-screen backups live under `StreamingAssets/WRLDZ/_chroma_backup/`.
  Runtime safety: `StreamingSprite` auto-keys remaining green corners on WRLDZ chrome (never on `/bg/` scenes).
- Presentation mirrors (`Presentation/bg_*.png`, `*_imagine.png`) keep older loaders working.
