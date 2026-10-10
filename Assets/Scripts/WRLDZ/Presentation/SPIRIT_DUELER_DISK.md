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
| **Strap / cuff** | — | **Left wrist** | Tracker origin = wrist. Deck well is calibrated to that point; the plate hovers just above the arm (`SpiritDuelerSkin`) |
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
| Card models | `CardModelCatalog` | `{id}.glb` + `{id}.shells.json` shell relief (preferred), else `{id}.obj`, else art billboard |
| FX | `DiskFxDriver` | Summon / set / activate / attack charge |
| Skin | `SpiritDuelerSkin` | Ghost-glass color-shift on every kit part; owns the aura |
| Mechanism | `SpiritDuelerMechanics` | The Battle City V2 disk from the kit parts: latch, central piece, blade halves, GY cover, deck flap, field slot, zone light-ups |
| Aura | `SpiritDuelerAura` | Silhouette flame aura (`WRLDZ/SpiritSuperAura`), runes, coils, edge streaks, trailing sparkles, deploy rune circle |
| Showcase disk | `SpiritDuelerDiskView` | Hub / map badge |

## Assets

| Path | Contents |
|------|----------|
| `StreamingAssets/Models/SpiritDueler/BattleCityDuelDisk.obj` | Disk mesh |
| `StreamingAssets/WRLDZ/Imagine/spirit/disk_skin_albedo.png` | Aether glass skin |
| `StreamingAssets/WRLDZ/Imagine/spirit/disk_skin_emission.png` | Energy-vein glow map |
| `StreamingAssets/WRLDZ/Imagine/spirit/wrist_aether_smoke.png` | No longer drawn on the disk (no alpha channel: it drew as black smoke) |
| `StreamingAssets/Models/Cards/{cardId}.glb` + `{cardId}.shells.json` | Per-card shell relief (preferred; 24 cards today) |
| `StreamingAssets/Models/Cards/{cardId}.obj` | Optional single static mesh (none shipped today) |

## Interaction

- **Play**: tap or drag the cards in the floating hand and on the disk → action strip on the card. (The old 2D field rows are dead code — QC M3; digital duels render the same AR stage in a RawImage.)
- **Stage**: decorative + combat pulse (impact window charges the disks).
- **AR later**: re-parent disk mesh to left arm; spirit stage to world ground.

## Look (good-magic hologram + anime/Skyrim aura)

The **DiskMesh body** is translucent ghost glass (`WRLDZ/SpiritGhostUnlit`: fill + fresnel rim) tinted by **Spirit magic** (`AvatarAppearance.accentHex`). Imagine albedo is mid-tone detail only — never a dark emission map, never a second additive copy. Cards stay physical cardboard.

**Aura (owner, 2026-09-27: "Super Saiyan like", "tighten it to the shape of the duel disk", "Skyrim-like magic
quality but keep the anime style").** `SpiritDuelerAura` under `WristAetherAura`:

| Layer | What | Notes |
|---|---|---|
| Flame aura | `WRLDZ/SpiritSuperAura` on one camera-facing quad | Hugs the disk's own outline. Each frame the body is drawn (shader pass `SpiritAuraMask`, command buffer) into a 256² mipmapped mask from the viewer's eye through the exact window the quad covers, so mask and quad line up. Anime: crisp cel bands around the silhouette (Spirit colour outward, white-hot next to the body) pulled up into sharp tongues, plus a thin inner rim light. Skyrim: a wide soft haze with wisps drifting up. The body stays clear. Breathes; flares on `DiskFx` energy and on deploy. Opponent at 0.75 strength. |
| Runes | world-space particles, 4×4 procedural glyph atlas | Glowing claw-stroke glyphs rise off the edges and fade (word-wall feel). Generated from a fixed seed, no copied alphabet. |
| Coils | 2 `TrailRenderer`s | Spiral along the long axis, hub to tip, half a cycle apart; mesh-local, so they follow the blade swing. |
| Edge streaks | world-space particles | Shed from the mesh outline (rasterised once per mesh), rising; few from the bottom edge so they never cross the plate. |
| Sparkles | world-space particles + ribbon trails | Four-point stars off the edges: white / Spirit colour / gold. They trail behind a moving arm. |
| Deploy flare | quad, procedural magic circle | Rune circle (glyph band, tick ring, one upward triangle) that spins a quarter turn as it opens. |

Everything is additive light (`WRLDZ/HoloUnlit` or the aura shader) on procedural white-plus-alpha sprites (`SpiritFxTextures`). **No smoke:** the Imagine wrist-smoke PNG has no alpha channel, so it drew as black smoke when alpha-blended, and as green curls when tinted. `MenuSmokeTest.AssertSpiritDuelerAura` fails if any aura material samples it.

**Palettes (owner, 2026-09-27: "palette swapping for the aura color and duel disk color").** The disk body colour and
the aura colour are picked separately in the avatar customizer (**Duel Disk** and **Aura** chips; saved as
`AvatarAppearance.diskHex` / `auraHex`, catalogs `AvatarCatalog.DiskPalettes` / `AuraPalettes`). The first swatch,
"Match Spirit magic", is empty and follows `accentHex`, which is how every disk looked before. `SpiritDuelerPalette`
resolves the player's pair; `ArDuelDiskRig.SetPalette(disk, aura, fade)` crossfades a live disk (0.6 s): the ghost
body and the kit parts take the disk colour; the flame aura, runes, coils, sparkles, deploy circle, `DiskFx`, the
mechanism's lights, the disk light and the floating hand's glow take the aura colour. Zone markers and the DECK label
keep the colour they were built with (the Spirit magic colour). Saving in the customizer re-applies it to a
running duel (`SpiritDuelerPalette.Changed`). Editor / development builds: **F7** cycles the aura, **F8** the disk
(preview only, not saved).

**Do not** move OfficialZones / `ArZoneLayout` when changing the body. Markers stay mesh-local so they can still be nudged independently.

## Retract / deploy (anime Battle City V2, built from the kit parts)

The disk is **assembled from the print-kit parts**, not one rigid mesh: `BattleCityDuelDisk.obj` keeps the 23 `g`
groups of the Cults3D kit (benjohnson789 "Working Full Size Battle City Duel Disk", Fusion v37; the owner's copies
of the v37 OBJ / `.f3d` / `.3mf` are in `~/Downloads`). `ObjMeshLoader.LoadWithGroupsFromStreaming` reads the file
once into the merged body (bounds + the aura's outline; not rendered) and one mesh per part, and
`SpiritDuelerMechanics` builds and drives the mechanism under `BladePivot`:

```
BladePivot  (stays in the play pose; impacts jolt it)
├─ DiskMesh            merged body, forceRenderingOff (bounds, aura outline)
├─ DiskParts           SpiritDuelerMechanics (jolts on lock)
│  ├─ Hub              MAIN BODY, covers, COUNTER TOP, DECK SPRING HOLDER, Body37–41, arm mount (strapped, static)
│  ├─ CentralPiece     BIG + SMALL BLADE CONNECTOR, rotates about the hub (CarrierPivot)
│  │  ├─ BigBlade      BIG BLADE, Card retainter: extends along +X from the seam
│  │  └─ SmallBlade    SMALL BLADE: extends toward the field tip
│  │     └─ FieldSpellSlot   rides the tip drawer (FieldSpellZone offset)
│  ├─ Latch            swings on its pin
│  ├─ LatchButton      the hub button (presses in)
│  ├─ GyCover          rides the graveyard tray (locked over the deck in standby), pops when a card lands
│  └─ DeckFlap         lifts on every draw
├─ OfficialZones …     unchanged (zone anchors are the kit's assembled pose)
```

Canon (owner reference, 2026-10-04): the anime's "Duel Disk, activate!" cut
(`~/Pictures/yugioh-yugi-ready-for-duel-*.gif`, 12 frames at 100 ms), read the way it is drawn — looking down on the
hub's top face with the hand up, which is mesh −X up and −Z (the Graveyard / plate side) to the image's left:

- **frames 1–2, standby and extend** — both halves lie alongside the round body on opposite sides, parallel to the
  forearm and pointing toward the elbow, their hub ends tucked into the body: the three-stage BIG BLADE (M3–M5 /
  ST3–ST5 in this mesh's numbering; the owner's "section 1") on the Graveyard side, the two-stage SMALL BLADE
  (M1–M2 / ST1–ST2 + the field slot; "section 2") on the counter-top side. The latch releases and both slide
  straight out of the body along their connectors, still parallel (frame 2: wide hub end up by the wrist, tip down);
- **frames 2–5, slide together and meet** — the two halves slide toward each other: each connector sweeps along the
  ring toward the deck, the three-stage half leaning out progressively as it goes, the two-stage half hanging
  near-vertical beside the wrist until late (frame 4) and swinging its last ~70° between frames 4 and 5; they
  arrive end to end beside the deck holder as one plate across the hand side of the hub (frame 5) — a shallow
  inverted V, which is what the kit's offset seam gives — the three-stage half on the Graveyard side of the seam;
- **frames 5–8, swing** — the central piece carries the joined plate on round the ring, back down the Graveyard
  side, a clear gap off the hub with the connectors bridging it (frame 7: the plate vertical beside the hub), and
  the plate slides in to the rim as it reaches the authored pose along the forearm (frame 8);
- **frames 9–12** — the locked disk, the three-stage half toward the elbow.

Mechanics: each section hangs off its own connector on the central piece; the connector yaws about the central axis
(sliding along the hub ring), the section is hinged at its root, and its root slides along the connector (`RootAt`:
`BigStandbyRootM` 15.0 cm / `SmallStandbyRootM` 16.7 cm from the axis in standby, out to the authored 18.9 / 18.6 cm,
the connectors' full length, where it stays). Standby: `BigStandbyDeg` = `PlaySeamDeg` (≈248°, the very radial the
seam locks to — the big half ends where it started) and `SmallStandbyDeg` = its mirror across the forearm (≈112°);
both `*StandbyDirDeg` 0° (parallel to the forearm, toward the elbow). At those radii the big half's inner rail is
2.5 cm inside the Graveyard-side rim, under the GY cover; the small half's rail touches the counter-top rim, where the
kit's five rim studs (Body37–41, OBJ groups) stop it. Sweep: the big connector 76° and the small 64° toward the deck;
each half turns from parallel to its joined direction on its own ramp over the eased sweep (`HingeAt`: `BigTurn0`
0.10 — the three-stage half leans out almost from the start, trailing its circle; `SmallTurn0` 0.45 — the two-stage
half hangs by the wrist until mid-sweep, then swings); parallel-to-the-forearm is exactly the big half's locked
attitude on its connector (`BigStandbyHinge` 0), the small half's 19° kink (`SmallStandbyHinge`) closes with its
turn. From the end of the extension the roots ride `SwingOutsetM` 3.5 cm beyond the authored radius (the kit's
connector ends still touch the trays' inner rails) and the joined plate slides that back in over the last part of
the swing (`SlideIn0` 0.55 of it). Meet: `MeetSeamDeg` = the deck well's own radial
(≈172°), the central piece held at `CarrierStandbyYaw` (≈76°) so the seam sits beside the deck holder; the joined
plate stands across the hand side of the hub, its inner rail on the rim. Lock: the central piece rounds the ring to
yaw 0 (a small `Snap` overshoot) — the kit's authored pose, so every zone anchor lines up. All of it is local to
`BladePivot`, so the opponent's worn disk (mirrored rig frame) runs the identical mechanism. Rejected readings: the
135° / 270° standby (both trays toward the hand) put the three-stage tray on the wrong side of the hub, and a radial
"clock hand" sweep with a fold at the end was not in the frames (owner, 2026-10-04: "frames 2–4 are all wrong").
`DiskFxDriver.MechOpen` (0 standby → 1 play, moved only by `BladeDeploy`, `BladeRetract` and the snaps, never by
event pulses) runs this timeline over `DiskFxDriver.DeploySeconds` = 2.4 s, in the GIF's proportions (extend 1 :
sweep 3 : swing 3) with its pauses; retract plays it backwards in 1.1 s. Every pose is a pure function of `MechOpen`,
so a restarted cinematic, a scene reload, a cancelled duel or an interrupted deploy resumes or resets without a jump
(the rig's safety snap waits for the whole deploy; it used to fire at 1.25 s):

| MechOpen | Beat |
|---|---|
| 0.00–0.10 | hub button clicks in and out (tap SFX); latch swings open (0.02–0.08) |
| 0.06–0.22 | **extend**: both sections slide straight out of the round body along their connectors (slide SFX), light streaks at the tips |
| 0.22–0.27 | pause |
| 0.27–0.60 | **slide together and meet**: the connectors carry both halves along the ring toward the deck (slide SFX), the three-stage half leaning out as it goes, the two-stage half hanging by the wrist until mid-sweep; they meet end to end beside the deck holder (clack SFX) |
| 0.60–0.66 | pause |
| 0.66–0.93 | **swing to the lock**: the central piece carries the joined plate round the ring, 3.5 cm off the hub, and it slides in to the rim over the last part, settling with a small overshoot (slide SFX) |
| 0.92–0.98 | latch re-locks |
| 0.95 | **lock**: jolt through the disk, clack SFX (card-set), tip bursts, zones light up in a sweep M1→M5, S/T 1→5, Field |

Editor knob: `SpiritDuelerMechanics.DebugPose` (0..1 freezes every disk at that point of the deploy; −1 = live).

During play (hooks on `ArDuelDiskRig`, fired by the disk itself, so every actor gets them):
`PulseDispenseTopCard` → deck flap; `SyncGraveyardPile` count up → GY cover pop; `NoteSeated(zone)` (end of the pad
snap / toaster insert) → that slot lights; `NoteSetActivated(zone)` (toaster eject) → hub button press + slot light.
Angles, distances and timings are named constants in `SpiritDuelerMechanics`. Editor knob:
`SpiritDuelerMechanics.DebugPose` (0..1 freezes every disk at that point of the deploy; −1 = live).
With an OBJ without groups the rig falls back to the old single mesh and whole-disk tilt.

| Mode | When | Visual |
|-------|------|--------|
| **Retracted** | Zone Mode, pre-duel, after Map | Hub on the arm · blades inside the hub · central piece swung aside · GY over the deck |
| **Deployed** | Live duel | Kit assembled pose · zones lit once at lock |
| **Combat FX** | Summon / set / activate / attack | Emission punch · jolt · slot light-ups · `DiskFxDriver` |

Code: `SpiritDuelerMechanics` · `DiskFxDriver.MechOpen` · `ArDuelDiskRig.FadeInRetracted` / `DeployForDuel` /
`RetractThenFadeOut` · `ArDuelInteractionSystem.DeployDisksForDuel`.

## Pre-duel setup and cinematic

Before the cinematic, the duel scene shows the **DUEL SETUP** sheet (`UI/Shell/PreDuelSetupScreen`, opened by
`DuelBootstrap`; the same `DualMenuPresenter` chrome as every sheet). The player picks:

- **First turn** — coin toss (heads / tails are dealt to Player 1 / Player 2 at random here and shown before the toss)
  or dice roll (both roll, higher goes first, a tie is rolled again).
- **Deck** — a deck box, or the equipped deck (skip selection). Unchanged for the NPC (`OpponentCatalog`).
- **ERAZ era** — the bands of `eraz_eras.json`; a band is selectable when it is released and the account may select it
  (`ErazFormat.IsReleased`, `ErazProgress.CanSelectBand`), else listed LOCKED.
- **Format** — `DuelFormatCatalog`, a view over the rules the project already defines: Standard TCG (8000 LP, the
  era's end-of-format list) and Advanced TCG (8000 LP, the current Advanced Format list,
  `Cards/banlist_advanced.json`) are open; Quick Duel (4000 LP), Duelist Kingdom (`DuelRulesOverlay.DuelistKingdomTable`)
  and the badge formats (`FormatProgress`) are listed LOCKED. A launch that dictates its format (boss, story, PvP
  zone, street NPC, the DK overlay) shows it fixed.
- **Deck check** — every card of the chosen deck against the chosen format and era (`PlayDeckValidator.Inspect`:
  shape, ownership, the deck editor's wall `ErazDeckRules.CanAddToDeck` with the format's list); illegal cards are
  greyed with the reason, nothing is removed, START stays disabled until the deck is legal, and the check is run again
  at every duel start (restart included), so the setup cannot be bypassed.

The choices live on `ArDuelMatchConfig` (`RulesFormatId`, `ErazBandId`, `PlayerDeckBoxIndex`, `FirstPlayerMethod`,
`CoinHeadsIsPlayerOne`) and are the values the duel starts with. The lab's Instant Duel, test duels and launches that
fix the player's deck file skip the sheet; the first turn is then decided silently by the same method.

On duel load (`cinematicOpening: true`):

0. **First turn** — the setup's toss / roll on the engine's own stream (`DuelEngine.Rng`): the arena raises the coin
   or die hologram (`CoinDicePresentation` → `ArCoinDiceFx`), the hint names the result, `DuelEngine.SetFirstPlayer`
   gives turn 1 to the winner (`DuelEngine.FirstPlayer`; the AI opens when it wins the toss)  
1. **Deploy** — the opponent's deploy cut-in, then your Spirit Dueler rises and activates (the mechanism above)  
2. **Shuffle** — main-deck stack riffle VFX on the disk (the already-open plate only gets a `HoloProject` energy pulse; it does not re-fold)  
3. **Draw gesture** — move hand / pointer to **DECK zone** (or tap **DRAW HAND**)  
   The translucent floating right hand reaches to the disk magazine and pinch-draws each card (`ArFloatingHand.QueueDraw`; see `ArInteraction/FLOATING_HAND.md`): the card slides from the magazine into the pinch, flips face-up and slides into its fan slot. Mid-duel draws use the same path. Cards no longer fly from the H-slot to a hand by themselves.  
4. **Opening draw** — the floating hand pinch-draws five cards from the magazine (same path as above); engine starts Main Phase 1  

Classes: `PreDuelCinematic` · `DuelEngine.OpeningSequenceActive` · `MainDeckZone` on `ArDuelDiskRig`.
