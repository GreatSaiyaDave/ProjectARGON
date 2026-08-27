# Dual Board Presentation — Card Field + Hologram Field

Anime / Spirit Dueler canon: **two boards**, no painted midfield playmat.

```
  AR CAMERA VIEW (portrait)

     ┌─ OPP left-arm DISK (their physical cards) ─┐
     │  opp S/T · opp monsters on their plate       │
     └──────────────────┬───────────────────────────┘
                        │ project (face-up / sets)
     ┌──────────────────▼───────────────────────────┐
     │  HOLOGRAM FIELD — empty air (Solid Vision)   │
     │  Opp holos · You holos · Ka spawn light      │
     │  NOT a drop target — cinematic projection    │
     └──────────────────▲───────────────────────────┘
                        │ project
     ┌──────────────────┴───────────────────────────┐
     │  YOUR Spirit Dueler DISK (left arm)          │
     │  · Your monster pads · S/T slots · deck/GY   │
     └──────────────────────────────────────────────┘
              [ Hand volume — card backs ]
```

## Boards

| Board | Class | Anchor | Content |
|-------|--------|--------|---------|
| **Card Field** | `ArCardFieldController` + `ArDiskCardVisual` | Your left-arm disk + opp left-arm disk | Physical TCG cards on zone pads/slots |
| **Hologram Field** | `ArArenaHologramManager` + `ArArenaCardVisual` | Mid between arms — **empty air** | Large projections / future 3D models; invisible anchors only |

The **2D UI board** (digital mode) is a separate control surface. AR mode does not paint a Master Duel mat in midfield.

**Composition:** the worn disk + player sit in the near FOV. Hologram arena floats **above and beyond** the wrists (`DiskToHoloMinGap` + `ArmClearanceFromMid`) so Solid Vision never sits on the plate or the player. HUD chrome stays in the top glance / slim bottom tray.

## Information rules (TCG-legal)

| Card state | Your disk | Opp arm disk | Hologram field |
|------------|-----------|--------------|----------------|
| Your face-up mon | Full small card | — | Art / future mesh |
| Your face-down set | Card back | — | Set card-back holo |
| Opp face-up | — | Full (their plate) | Art / future mesh |
| Opp face-down | — | Card back | Set card-back holo |
| Either hand | Never on disk | Never | No |

Selecting a card for full text is **local UI** (`CardInspectPopup`) — never leaks hidden faces to the peer.

## Placement → projection flow

1. Player snaps a card onto a **disk zone** (Card Field). The arena is **never** a drop / tap target.
2. `ArSnapRules` + engine commit the legal play.
3. `SyncNow`: disk visual **animates** onto the pad (magnetic drop) or **into the S/T slot** (present → mouth → pocket, 10% tip). Hologram field spawns a large projection at the invisible midfield anchor (spawn flies from disk zone world position) with a Ka-style light column + ground rings.
4. Flip / activate → disk toaster (eject → flip → reseat or GY); hologram rebuilds (back → art crop, or 3D model when `PreferDynamicModels`).

## Sync path

```
DuelEngine state change
  → DuelUI.RefreshNow / ArDuelSyncBridge
  → ArDuelInteractionSystem.SyncNow
      → CardField.SyncFromEngine
           · PlayerDisk occupants + disk cards
           · OppDisk occupants + disk cards
      → Arena.SyncFromEngine   (empty-air holos; spawn origin = disk zone)
      → HandVolume.SyncHand
```

## Disk retract / deploy

Arm disks mount **retracted**. On duel bind they **deploy** (`BladeDeploy`). Leaving the duel **retracts** them.  
See `SPIRIT_DUELER_DISK.md` and `DiskFxDriver`.

## Assets

| Need | Source |
|------|--------|
| Card back | `StreamingSprite.CardBack` → Imagine → YgoFrames |
| Full card face (disk) | `CardDatabase.GetArt(id)` |
| Hologram illustration | `CardDatabase.GetArtwork` / `CardArtFocus` crop |
| Future monster models | `StreamingAssets/Models/Cards/{id}.obj` via `PreferDynamicModels` |
| Disk mesh | `Models/SpiritDueler/BattleCityDuelDisk.obj` |

## Zone spacing (invisible anchors only)

See **`ArPlaymatLayout`**. Debug pads: set `ArArenaHologramManager.DebugShowFieldGuides = true` (Editor).

## Future: full dynamic 3D models

Set `ArArenaHologramManager.PreferDynamicModels = true` when OBJ coverage is ready.  
`ArArenaCardVisual` swaps billboard art for `CardModelCatalog` meshes; disk cards stay 2D faces until models exist.

## Not yet

- Real multiplayer peer (still local dual-arm stage)
- Per-spell custom VFX meshes beyond projection + aura
- XR hand grab of physical disk cards without viewport ray
