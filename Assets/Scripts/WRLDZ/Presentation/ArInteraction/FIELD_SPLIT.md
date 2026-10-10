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

**Composition:** the worn disk + player sit in the near FOV. Hologram arena floats **above and beyond** the wrists (`DiskToHoloMinGap` + `ArmClearanceFromMid`) so Solid Vision never sits on the plate or the player. The opp glance board carries the opponent's LP, field and HAND/DECK/GY plus the shared turn/phase — not a screen overlay; your own LP, deck and GY stay on and around your disk. Phase keys (BATTLE / MAIN 2 / END TURN) and SHUFFLE / RESTART / MAP are the disk control panel, two key banks in the LP window's plane.

## Information rules (TCG-legal)

| Card state | Your disk | Opp arm disk | Hologram field |
|------------|-----------|--------------|----------------|
| Your face-up mon (ATK) | Full small card | — | Art / future mesh (portrait card under it only during the summon beat) |
| Your face-up mon (DEF) | Full small card, sideways | — | Braced art over its **full card lying sideways** |
| Your face-down set | Card back | — | **Card back** flat on the zone (face hidden until activation) |
| Opp face-up (ATK) | — | Full (their plate) | Art / future mesh (portrait card under it only during the summon beat) |
| Opp face-up (DEF) | — | Full, sideways | Braced art over its **full card lying sideways** |
| Opp face-down | — | Card back | **Card back** flat on the zone (face hidden until activation) |
| Either hand | Never on disk | Never | No |

**Owner rule (2026-09-23, revised 2026-09-27): a face-down card shows in the arena as its card back, never its face, until activation** — either side. Monsters lie sideways (the Defense-plate pose), S/T in portrait; tap the card back (arena, during a target pick) or the disk card to target it. Flip Summon, battle flip and S/T activation reveal it with the flip/rise from that card back. Destroying a set card shows card-back shards at its zone. Code: `ArArenaCardVisual.RestAsSetCard`. (On 2026-09-23 set cards were hidden from the arena entirely; the owner reported on 2026-09-27 that they then did not show.)

**Defense rule (owner, 2026-09-23 — anime Battle City):** a face-up Defense monster's hologram stays
upright but **braced** (crouched × 0.88, leaning back 6°, cool tint, shield glint toward the opponent)
and its **full card** lies flat on the street under it, turned sideways the same way the disk card turns
for Defense, with a soft accent glow (`DefenseCardPlate`, long side 0.92 × `MonsterColumnPitch` so it
never reaches the next column). Attack-position monsters stand with no card, except that on arrival in
Attack the card lies in portrait under the materialize for ≈1.2 s, then fades. A face-down Defense monster
that is attacked shows as its sideways **card back** while it is the target, flips face-up at the damage
step, and that card becomes the Defense plate the monster rises out of (it ends in Defense, never Attack
art). If the attack is cancelled it stays a card back. A face-down monster that leaves without a battle
reveal (bounce, Change of Heart, tribute, face-down banish) is removed quietly — no face leak.
Spell/Trap-caused position changes and summons wait for the activation result gate.
Code: `ArArenaCardVisual.SetDefenseDisplay` / `BeginSummonCardPlate` / `BattleFlipCo` /
`RevealAsAttackTarget`, `ArAnimeSequenceDirector.TrackFaceDownAttackTarget`, `ArArenaHologramManager.RemoveNow`.

Selecting a card for full text is **local UI** (`CardInspectPopup`) — never leaks hidden faces to the peer.

## Placement → projection flow

1. Player snaps a card onto a **disk zone** (Card Field). The arena is **never** a drop / tap target.
2. `ArSnapRules` + engine commit the legal play.
3. `SyncNow`: disk visual **animates** onto the pad (magnetic drop) or **into the S/T slot** (present → mouth → pocket, 10% tip). Hologram field spawns a large projection at the invisible midfield anchor (spawn flies from disk zone world position) with a Ka-style light column + ground rings.
4. Flip / activate → disk toaster (eject → flip → reseat or GY); hologram rebuilds (back → art crop, or 3D model when `PreferDynamicModels`).

## Sync path

```
DuelEngine state change
  → DuelUI.RefreshNow (ArDuelSyncBridge only publishes a snapshot on engine bind)
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
