# AR Duel Presentation — Canon (Spirit Dueler)

**Product template:** Pokémon GO for non-YGO chrome · **Yu-Gi-Oh! anime AR** for the duel itself.  
**This is not a 2D Master Duel board.** The phone/lenses show a **spatial duel**.

**Lab now:** S23 Ultra rear-camera passthrough (`ArPhoneCamera` + `ArDuelSpace`) + PC Editor sim.  
**Later:** same stage on OpenXR lenses. See `S23_LAB.md`.

---

## Spatial model (always)

```
                    [ Mr. Referobot — referee NPC ]
                              │
         ╔════════════════════════════════════╗
         ║     DUEL ARENA (ground-anchored)   ║
         ║   holograms from cards on disks    ║
         ╚════════════════════════════════════╝
                              │
         [ Opponent Spirit Dueler disk — their LEFT ARM ]
                              │
         [ Your Spirit Dueler disk — LEFT ARM ]
                              │
              [ Floating hand — backs to opponent ]
                              │
         [ Select → legal TCG card popup + actions ]
```

| Element | Placement | Shows |
|---------|-----------|--------|
| **Your disk** | Left arm (wearable) | Your M zones + S/T zones; set cards face-down |
| **Opponent disk** | Their left arm (shared stage) | Their M zones + S/T zones; set cards face-down |
| **Hand** | Floating in front of player | **Card backs** toward opponent; fan layout |
| **Card info** | On **select only** | Full legal TCG face: art, name, type, stats, **full text** |
| **Arena** | Between you and opponent | Life-size (or stage-scale) holograms from face-up monsters / activations |
| **Mr. Referobot** | Physical NPC near arena | Referee + announcer (phase calls, illegal, impact, victory) |

---

## Information rules (TCG-legal visibility)

| Card state | You see | Opponent sees | Arena hologram |
|------------|---------|---------------|----------------|
| Your hand | Backs only until select; full text in **your** popup | Backs only | No |
| Your face-down set | Set on your disk | Set on their disk | No |
| Your face-up monster | Full on disk + popup | Full on their disk + popup if they inspect | Yes |
| Opp face-down | Set on their disk | (their own) | Set card-back holo |
| Opp face-up | Full on their disk + midfield holo | Full | Yes |

Selecting a card **never** reveals hidden info to the opponent.  
Popup is **local UI** for the selecting player.

---

## Pokémon GO template (non-YGO chrome)

Use GO as the **map / social / progression shell**:

| GO pattern | WRLDZ |
|------------|--------|
| Full-bleed map + avatar | Overworld GPS map + avatar |
| Soft circular orbs / bottom tray | Menu, bag, profile orbs |
| Encounter prompt | Tear / arena “Enter AR Duel?” |
| Minimal HUD during catch | Minimal HUD during duel (LP orbs, phase, pass) |
| AR camera as world | AR passthrough; duel objects anchored |

YGO energy lives **on** the disks, arena, hand, and card faces — not as a flat GBA board over the camera.

---

## Phone vs lenses

| Mode | How presentation runs |
|------|------------------------|
| **Phone (now)** | Webcam **passthrough** + full-bleed 3D stage + minimal HUD (see `AR_DUELING_DIMENSIONS.md`) |
| **Lenses (target)** | Same scene graph → wrist disk / floor arena (Meta XR / Dueling Dimension MR pattern) |

Rules engine is identical; only transforms change.

---

## Mr. Referobot

- **Role:** Official NPC referee + hype announcer (anime energy, rules authority voice).  
- **Presence:** Always in the duel space (portrait + 3D stand-in until full mesh).  
- **Lines:** Turn start, battle phase, illegal move refusal, trap window, direct attack, LP critical, win/lose.  
- **Future:** Full body mesh, lip-sync TTS, walk into arena.

---

## Implementation map

| System | Script |
|--------|--------|
| Spatial stage | `ArDuelSpace` |
| Card Field (disks) | `ArCardFieldController` + `ArDuelDiskRig` |
| Hologram Field (empty air) | `ArArenaHologramManager` + `ArArenaCardVisual` |
| Card inspect popup | `CardInspectPopup` |
| Referee | `MrReferobot` |
| Card backs | `CardBackArt` |
| GO chrome sprites | `GoChrome` (`StreamingAssets/WRLDZ/GoChrome`) |
| Duel HUD orchestration | `DuelUI` |
| AR phone companion (profile / systems) | `ArCompanionPhoneHud` — no 2D duel board in AR |
| Dual-board layout notes | `Presentation/ArInteraction/FIELD_SPLIT.md` |

### Phone vs AR field (canon)

| Mode | Phone screen | Duel cards / zones |
|------|----------------|--------------------|
| **AR / Zone Mode ENTER AR** | Profile, decks, backpack, tome, artifacts, settings | Spirit Dueler disk + midfield holos only |
| **Digital / PreferDigital** | Full 2D field + hand + phase dock | Same engine; optional small AR strip |

During AR there is **no dueling UI on the phone** — only companion systems and a thin Pass/Cancel Target control when a response window opens.

Assets (original / CC0-style pack-ins under `StreamingAssets/WRLDZ/`):

- `CardBack/card_back.png` — pack-in card back (prefer YgoFrames / CardArt pipeline when present)  
- `YgoRefs/` — real YGO game UI (maps, menus, profiles) — **use these**  
- `Referobot/referobot_portrait.png` — companion AI portrait until mesh art lands
- `GoChrome/*.png` — soft orbs / pins for map UI  

---

## Non-goals (this slice)

- Real multiplayer mirror sync of hand backs  
- Licensed Konami card frame templates (we use clean legal text + our art frames)  
- Full XR interaction SDK (hooks reserved)
