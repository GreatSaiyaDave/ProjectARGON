# Spirit Dueler — Battle City disk presentation

**Canon:** `AR_DUEL_PRESENTATION.md` (left-arm disk, opponent arm disk, floating hand backs,
center arena, Mr. Referobot, Pokémon GO non-YGO chrome).

## Visual model (phone AR simulation)

```
┌─────────────────────────────────────┐
│ LP orbs · PHASE · status · referee  │
│ ┌─────────────────────────────────┐ │
│ │ AR SPACE (Solid Vision)         │ │
│ │  HOLOGRAM FIELD — empty air     │ │
│ │    (holos only; no playmat)     │ │
│ │  CARD FIELD — both arm disks    │ │
│ │  HAND volume (torso)            │ │
│ └─────────────────────────────────┘ │
│ YOUR DISK blade (Battle City V2):   │
│   [M1][M2][M3][M4][M5] │ DECK      │  ← top of plate
│   [ST1][ST2][ST3][ST4][ST5]│ GY    │  ← under monsters, wearer side
│   FIELD at tip · EXTRA near hub     │
│ HAND tray                           │
│ Phase dock (Battle / Main2 / End)   │
└─────────────────────────────────────┘
```

### Battle City zone rules (canon)

Source: [Yugipedia — Duel Disk / KC mass production](https://yugipedia.com/wiki/Duel_Disk) (anime Battle City V2) + KaibaCorp lineart + `ArPlaymatLayout`.

| Zone | Label | Where on disk | Geometry |
|------|-------|---------------|----------|
| **Strap / cuff** | — | **Left wrist** | Tracker origin = wrist. Deck well is calibrated to that point; the plate hovers on a faint aether tether (`SpiritDuelerSkin`) |
| 5 **Monster Card slots** | **M1–M5** (hub→tip) | **Top of each card-setting stage** | Full rectangle on the blade — ATK upright · DEF sideways |
| 5 **Spell Card slots** | **ST1–ST5** | **Dashed indent on the outer rim** | Slides into the slot · **tip** stays visible · eject + flip to activate · holos show face-up vs set |
| **Field Card Slot** | **FIELD** | **Front end / tip** of the blade | Drawer that opens at the point |
| **Main Deck Holder** | **DECK** | **H-slot well** on the hub (faces wearer) | Dedicated well cards (`ArDeckWellCards`) fill the channel |
| **Graveyard** | **GY** | Beside Main Deck on hub | Tray / pile; tap to browse (public) |
| **Extra Deck** | **EXTRA** | Inboard of GY on hub | Optional chamber |
| Hand | — | **Not on disk** | `ArHandVolume` at torso |

**Important (anime mechanics):** Spell/Trap slots are **not** on the outer rim opposite the wearer. They sit **directly underneath** each Monster Card Slot on the plate side nearest the user. Activation switches (lore) live on that same wearer-facing side.

Mesh anchors (`BattleCityDuelDisk.obj`, BladePivot local) — measured from the sculpted mesh:
- Monsters: **centered on the five card-setting stages** (the large blade rectangles). The yellow triangles are labels only.
- S/T: **directly under that same stage**, inserted from the **outer-rim dashed indent**; a short end stays in the opening to tap/activate.
- Field Spell: **drawer at the front end / tip** of the blade.
- Deck + GY: round hub.
- Card size fills the stage (world width 0.120). Magnetic drop on pads; present → mouth → pocket slide-in on slots (10% tip stays visible). Activate ejects, flips, then reseats or flies to GY.

**Empty field = sculpted mesh only.** No overlay pads, gold rims, or zone labels
where a card might go. Cards appear when played (magnetic seat on the stage /
slide-in under the monster). Debug: `ArDiskZone.AlwaysShowZoneMarkers` /
`AlwaysShowZoneLabels` (both **off**).

Lore: **cards on the disk**, **larger card projections in the arena** (full 3D models later). See `ArInteraction/FIELD_SPLIT.md`.

## Components

| Piece | Class | Role |
|-------|--------|------|
| AR space | `ArDuelSpace` | Stage host |
| Interaction | `ArDuelInteractionSystem` | Orchestrator |
| Card Field | `ArCardFieldController` | Physical cards on both disks |
| Hologram Field | `ArArenaHologramManager` + `ArArenaCardVisual` | Empty-air midfield holos (no board) |
| Inspect | `CardInspectPopup` | Full legal text on select only |
| Referee | `MrReferobot` | Announcer / rules voice |
| Card models | `CardModelCatalog` | `{id}.obj` or art billboard |
| FX | `DiskFxDriver` | Summon / set / activate / attack charge |
| Skin | `SpiritDuelerSkin` | Ghost-glass color-shift + wrist smoke aura |
| Showcase disk | `SpiritDuelerDiskView` | Hub / map badge |

## Assets

| Path | Contents |
|------|----------|
| `StreamingAssets/Models/SpiritDueler/BattleCityDuelDisk.obj` | Disk mesh |
| `StreamingAssets/WRLDZ/Imagine/spirit/disk_skin_albedo.png` | Aether glass skin |
| `StreamingAssets/WRLDZ/Imagine/spirit/disk_skin_emission.png` | Energy-vein glow map |
| `StreamingAssets/WRLDZ/Imagine/spirit/wrist_aether_smoke.png` | Wrist tether wisps |
| `StreamingAssets/Models/Cards/{cardId}.obj` | Optional per-card 3D |

## Interaction

- **Play**: tap 2D field / hand cards → action strip on the card.
- **Stage**: decorative + combat pulse (impact window charges the disks).
- **AR later**: re-parent disk mesh to left arm; spirit stage to world ground.

## Look (shadow-magic hologram)

The **DiskMesh body** is translucent ghost glass (`WRLDZ/SpiritGhostUnlit`: fill + fresnel rim) tinted by **Spirit magic** (`AvatarAppearance.accentHex`). Imagine albedo is mid-tone detail only — never a dark emission map, never a second additive copy. Wrist wisps are shadow-aether. Cards stay physical cardboard.

**Do not** move OfficialZones / `ArZoneLayout` when changing the body. Markers stay mesh-local so they can still be nudged independently.

## Retract / deploy (anime Battle City)

Two modes. Transition is a visible hologram swing (~0.85s), not a skip (except Instant Duel).

| Mode | When | Visual |
|-------|------|--------|
| **Retracted** | Zone Mode, pre-duel, after Map | Cuff only · blade folded · ghost dim · fade in/out of **body** |
| **Deployed** | Live duel | Blade open · `BladeDeploy` swing · magic flare · zones visible |
| **Combat FX** | Summon / set / activate / attack | Emission punch · shake · `DiskFxDriver` |

```
WristCuff (always)
└─ BladePivot  ← rotates open (retract ↔ deploy)
     ├─ DiskMesh
     ├─ EnergyRing
     └─ OfficialZones (scale in after ~35% open)
```

Code: `DiskFxDriver` · `ArDuelDiskRig.FadeInRetracted` / `DeployForDuel` / `RetractThenFadeOut` · `ArDuelInteractionSystem.DeployDisksForDuel`.

## Pre-duel cinematic

On duel load (`cinematicOpening: true`):

1. **Deploy** — blade opens from retracted wrist form  
2. **Shuffle** — main-deck stack riffle VFX on the disk  
3. **Draw gesture** — move hand / pointer to **DECK zone** (or tap **DRAW HAND**)  
   Cards lift out of the disk H-slot and fly into the hand (back → face). Mid-duel draws use the same path. If the well pose is missing, the top card fades and appears in hand.  
4. **Opening draw** — five cards fly deck → hand; engine starts Main Phase 1  

Classes: `PreDuelCinematic` · `DuelEngine.OpeningSequenceActive` · `MainDeckZone` on `ArDuelDiskRig`.
