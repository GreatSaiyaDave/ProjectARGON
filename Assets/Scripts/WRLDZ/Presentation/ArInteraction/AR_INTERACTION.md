# AR Duel Interaction System

Modular AR interaction for Project ARGON / DM WRLDZ. Extends the existing spatial stage — does **not** rebuild GPS, OCR, lore, or menu chrome.

## Lab hardware

- **Ship target (owner decision 2026-09-23): Android XR glasses** (Unity / OpenXR APK). The S23 is the dev testbed only; all Android APK constraints apply, and new features are not designed phone-first.
- **S23 Ultra + PC (testbed)**: `SimulatedArmTracker` locks the disk to a stable left-forearm offset in the AR stage.
- **HMD / Android XR glasses**: `XrWorldArmTracker` (life-size, meters), driven by `ArLensesSession` — same disk/arena code.

## Components (`Presentation/ArInteraction/`)

| Script | Role |
|--------|------|
| `IArTrackingSource` / `SimulatedArmTracker` / `XrWorldArmTracker` (lenses, via `ArLensesSession`) | Forearm + torso poses |
| `ArDuelDiskRig` | Arm-anchored disk mesh + official zones |
| `ArDiskZone` + `ArZoneLayout` | 5M + 5ST + Field (invisible anchors; no empty pads) |
| `ArCardFieldController` + `ArDiskCardVisual` | **Card Field** — physical cards on both disks |
| `ArArenaHologramManager` + `ArArenaCardVisual` | **Arena Field** — large card assets (disk → midfield); optional 3D models later |
| `ArHandVolume` + `ArFloatingCard` + `ArFloatingHand` | Hand cards held as a fan by the player's translucent right hand (pinch-draw, riffle on SHUFFLE, tap-lift) — `FLOATING_HAND.md` |
| `ArCardDragSystem` | Continuous drag; snap on release |
| `ArSnapRules` | Type/orientation legality + engine commit |
| `ArAnimeSequenceDirector` | Summon/activate/attack presentation |
| `ArDuelSyncBridge` + `IArDuelSync` / `LocalArDuelSync` (file `ArDuelSync.cs`) | Field snapshot publish hook for a future networked transport (not the live sync path) |
| `ArFieldSpellFloor` + `ArFieldMotes` + `ArFieldTerrainPad` + `ArFieldSignature` | **Field Spell environment** — sweep, rising art walls, motes, per-monster terrain + boon/bane aura, holo wash, signature set piece. Rows in `FieldSpellEnvironment.cs`; see `Docs/FIELD_SPELL_SOLID_VISION.md` |
| `ArDuelSync` / `LocalArDuelSync` | Field snapshot + local multiplayer hook |
| `ArDuelInteractionSystem` | Root orchestrator mounted by `ArDuelSpace` |

See **`FIELD_SPLIT.md`** for card-field vs hologram-field lore rules.

## Official zones on disk (Battle City / Yugipedia V2)

Left-arm strap · Plate extends past wrist. Local layout (`ArZoneLayout`):

| Zone | Geometry | Local placement |
|------|----------|-----------------|
| Monster 0–4 | Invisible anchor on sculpted stage | Mid of each blade rectangle |
| Spell/Trap 0–4 | Invisible mouth in the outer-rim indent | Slides in on set; tip stays visible; holos show face-up vs set |
| Field Spell | Invisible drawer anchor | Plate tip |
| Deck / GY | Hub cradle | Central body |

Empty zones draw **nothing**. The mesh stages / slot lips *are* the mechanic.

Cards **snap** via `ArSnapRules` → engine commit → `ArDiskZone.SnapLock` with zone-specific rotation/scale.

On snap, `ArSnapRules` enforces:

- Monsters → Monster Zones only (NS/Set via `TryNormalSummonToZone`)
- Spells/Traps → S/T zones (`TrySetSpellTrapToZone` or activate)
- Field Spells → Field Zone (`TryPlaceFieldSpell`)
- Face-up Attack / Face-down Set orientations applied at lock
- Battle City V2 has no Pendulum slots (S1 disk)

Dropping a monster that can be Normal Summoned **or** Set opens a rules prompt:
**Normal Summon (face-up Attack)** / **Set (face-down Defense)**. Same for Spell Activate vs Set.
A banner during drag lists the legal plays for the hovered zone. Shift still forces Set.

### Input model (no second device required)

Players use **the same phone / Editor AR viewport** for everything:

1. **Hand holos** float in front of you (`ArHandVolume`). This is the only live hand — the 2D tray is hidden while holos are up.
2. **Tap** a floating card to inspect and play (Summon / Set / Activate) — same menu as the old 2D tray.
3. **Drag** a floating card (touch / mouse on the AR RawImage) onto your left-arm disk — snaps to the **nearest legal zone** (`ArSnapRules` + engine). The arena is cinematic Solid Vision, never a drop target.
4. Midfield **holograms** update from engine state and spawn with a Ka projection light. They are not a pick device.
5. **Combat responses** (Waboku, Mirror Force, …): big **ACTIVATE** buttons on the phone tray while the attack anim plays — also no extra hardware.

Lenses re-use the same drag/snap graph with XR rays (`XrWorldArmTracker`, driven by `ArLensesSession`).

## Dual boards (Battle City / anime)

1. **Card Field** (`ArCardFieldController`) — physical cards on **your** left-arm disk and the **opponent's** left-arm disk. Face-down = card back; face-up = art.
2. **Hologram Field** (`ArArenaHologramManager` + `ArArenaCardVisual`) — **no visible playmat**. Invisible midfield anchors only; large projections spawn when face-up cards exist (art crop, or the GLB shell relief). Spawn origin = disk pad (fly-in).
   - **Face-down rule (owner, 2026-09-23, revised 2026-09-27):** face-down cards on **both** sides show in the arena as their **card back** lying flat on the zone (monsters sideways at the Defense-plate pose, S/T in portrait); the face stays hidden until they are revealed (flip summon, battle flip, S/T activation), which play from that card back. The disk shows the card back too. A face-down monster that is attacked stays its sideways card back until the damage step flips it. A face-down monster that leaves the field without being revealed leaves quietly (no reveal flip).
   - **Battle-position plates:** monsters show a Defense or Attack card plate under the hologram (kept per owner decision 6).
   - **Spell/Trap beat:** a set S/T that activates plays the full face-down → rise → reveal → resolve → fade sequence even when it activates and resolves in the same engine call (`ActivationBeat`). See `../../AR_DUEL_PRESENTATION.md`.

`PreferDynamicModels` defaults **false** (enlarged card art only). When enabled, `CardModelCatalog` prefers `Models/Cards/{id}.glb` + `{id}.shells.json`, then `{id}.obj`.

AR camera is **player-first**: disk and hand near the lens, hologram arena further and higher. UI phase controls live in a slim strip under the LP glance — not over the disk.

Driven by engine state via `ArDuelInteractionSystem.SyncNow` — the only live sync path (disk sync **before** holograms so spawn origins are valid). `ArDuelSyncBridge` only publishes an `ArHologramSnapshot` to `IArDuelSync` on engine bind (no subscribers yet; reserved for a networked transport).

## Anime simulator

`ArAnimeSequenceDirector` listens to engine log/state:

- Summon → camera push + particle burst + disk FX
- Activate → disk activate burst
- Attack presentation → charge dolly + impact flash

Disk card motion (`ArCardFieldController` + `ArDiskMotion`) follows engine state after `Notify()`:

| Engine event | Disk animation |
|--------------|----------------|
| Set / place S/T | Present above plate → mouth → slide into pocket (10% tip) |
| Activate S/T (Continuous / Field / Equip) | Eject → flip art → reseat face-up |
| Activate S/T (one-shot) | Eject → flip → fly to GY |
| Destroy S/T (MST etc.) | Eject → shatter at the mouth |
| S/T leaves field | Eject → fly to GY |
| Normal Summon / Set monster | Magnetic drop onto the pad |
| Change position | 90° turn on the pad |
| Monster leaves (tribute / destroy) | Lift off pad → GY or shatter |

All timings remain driven by `CombatAnimTimings` / response windows already in the engine.

## Sync

`LocalArDuelSync` publishes `ArHologramSnapshot` locally. Swap `IArDuelSync` for a networked transport later without touching arena/disk code.

## Integration

`ArDuelSpace.BuildStage` mounts `ArDuelInteractionSystem`.  
`DuelUI.Refresh` → `ArDuelSpace.SyncFromEngine` → hand + arena + disk occupants.

Engine additions (minimal):

- `TryNormalSummonToZone` / `TrySetSpellTrapToZone`
- `TryPlaceFieldSpell` / `TryPlacePendulum`
- `DuelistState.FieldSpellZone` + `PendulumZones[2]`

## AI opponent duelist (PvE, 2026-09-29) — `ArOpponentDuelist`
The AI no longer duels as a floating disk and a ghost hand. `ArDuelInteractionSystem.EnsureOpponentDuelist` (on the
first `BindEngine`) stands a person behind `OppDisk` — the same customizable body as the player
(`OverworldAvatar3d.AttachWorld`), dressed by `Data/AiDuelistLooks` (Yugi, Kaiba, Joey, Mai, Weevil, Rex, Mako, Pegasus,
Bakura, Marik, Keith, Bonz, Téa by name; everyone else a stable look seeded from the name; no body for Mr. Referobot).
- **Scale / place:** human size at the stage's units per metre (sim 0.46, life-size 1), feet on the street
  (`Arena.ArenaRoot` y), behind the opponent forearm pose, facing you. In EditorSim the opponent's hand cards
  (`OppHandVolume.CardScale = u·1.15/10.57`) and disk (`OppDisk` rig scale `u·0.62/0.64`) drop to that body's scale.
- **Left arm:** analytic two-bone IK onto the forearm pose; `OppDisk.ExternalPose` — the disk rides the left hand bone
  (identical to the tracker pose while the arm is on it; it stays strapped on through the victory pose / the fall).
- **Right hand:** the opponent `ArFloatingHand` still runs the card choreography (draw from the disk deck, fan, carry,
  set on a zone, `GestureLanded`), mesh hidden (`HideModel`); the body's right hand is IK-solved onto its `WristPivot`
  every frame, fingers curl with `GripFan / GripPinch / GripPresent`. During an arm gesture the roles swap
  (`SetTrackedWrist`): the fan follows the body's hand. The fan hangs from `ArHandVolume.AnchorOverride` (the chest).
- **Beats** (polled from the engine): duel start, turn start, spell / trap activation (`SpellActivationPresentation`),
  attack declared (`ActivePresentation`), LP lost (small / big), your LP lost on its turn, a monster arriving without a
  hand carry, idle flavour on your turn, victory / defeat (held). Arm beats wait up to 2 s for the hand to finish;
  turn start / activation / summon extend `DuelPresentationPacer` a little so the AI waits for them. Head looks at the
  working hand, the attack target, or you.
- **Clips:** `Resources/WRLDZ/Avatar3D/ArgonClips_Duel.fbx` — 15 Meshy moves (library + text-to-motion) retargeted onto
  ArgonAvatarRig by `~/argon-avatar/tools/custom/retarget_duel_clips.py` (root motion baked into pose, drift clamped);
  UAL clips are the fallbacks. Zone Mode (`OppDisk` off) hides the body.
- **Check without Play mode:** `WRLDZ ▸ Debug ▸ Opponent Duelist Preview` or
  `unity cmd eval --code 'return WRLDZ.Editor.OpponentDuelistPreview.Render("Seto Kaiba");'` → `Temp/OpponentDuelist/*.png`.

### Disk work (2026-09-29, later): the duelist's hand on its own disk — `ArOpponentDuelist.ReachDisk`
Besides the card hand's choreography, the duelist's right hand works the disk itself. A reach is a timed IK path to a
live disk transform: from the hand's rest → hover over the spot (along the zone's face normal) → contact → back, with
the palm on the disk face, the fingers pointing (tap), pinching (draw) or flat (place / shuffle), the left arm bringing
the disk in toward the chest and tipping it up, the chest leaning in and the head on the spot.
- `tap` (zone): a set spell / trap activated (then the "reveal" beat), a flip or a battle-position change.
- `place` (zone): a card that arrived on the field without a carried-card gesture (e.g. a spell activated from the
  hand — then the "activate" beat); skipped when the zone's `IncomingGesture` shows the card hand is setting it down.
- `draw` (deck): a draw when there is no live card hand; `shuffle` (deck, circular rub): the opening shuffle;
  `grave` (graveyard): available for discard beats.
- One reach at a time, only while the card hand is not mid-draw / place and no arm beat is playing; stale reaches
  (> 4 s) are dropped. Zone index: 0–4 monster, 5–9 spell / trap, 10 field spell.
- Preview: `OpponentDuelistPreview.Render(...)` also renders each reach at contact (`*_reach_*.png`).

### The duelist's own disk (2026-09-29, later) — `NpcDuelDisk`
The AI duelist wears the real Battle City Duel Disk (Version 2): the same full-size kit the player's Spirit Dueler is
built from (`ArDuelDiskRig` + `SpiritDuelerMechanics`: hub with deck holder, graveyard tray, LP counter and latch;
both blade halves retracted into the hub in standby and shot out into play position on activation; field slot at the
small blade's tip). The owner's rule: an accurate disk, reskinned and anchored to the NPC's forearm — not a play mat.
- `NpcDuelDisk.Wear(parent, rig, layer, units)` takes the opponent rig and, every frame after the duelist's IK, places
  it so the kit's arm-mount cradle sits on the left forearm bone. Measured in the rig root frame: the forearm runs
  along −X toward the hand, +Y is the plate normal, +Z the wearer's side, the cradle axis is 6.6 cm up the forearm
  from the wrist (`CradleCentre`, `RigToCanon`). Zones, cards, deck / GY piles, LP counter and labels are the rig's own.
- Solid skin: the ghost glass, underglow, aura, energy ring and the ghost `DiskMesh` are off; every `DiskPart:*` gets an
  opaque cel-shaded `AvatarRecolor` material in the anime's Battle City colours (white plate, dark hub top, red latch
  button and counter plate, dark strap). The phase buttons are the player's and stay hidden.
- Life size: the rig's scale is exactly the stage's units per metre (the kit itself is full size).
- `Override` (deploy cinematic, defeat): false → `Retract`, true → `DeployForDuel`, null → the rig's own state.
- Previews: `OpponentDuelistPreview` ticks the rig's private `LateUpdate` after every solve with
  `SpiritDuelerMechanics.DebugPose` set (1 = locked open; the deploy shots use 0 / 0.5 / 1), so the mechanism renders.
- The earlier procedural / Blender disk models (`disk_default.py`, `disk_academy.py` in tools/custom) are not used.

### Pre-duel deploy cinematic (2026-09-29) — `PreDuelCinematic` step 1, ~5 s
Replaces the old "both disks swing open together" step. The opponent goes first (a disk hanging in front of your face
would hide its pose), then yours. After it come the shuffle and the draw.
- **Opponent (~4.5–5.2 s, keyed on its own disk — 2026-10-04), `ArOpponentDuelist.PlayDeployCinematic`:**
  - Before the duel the disk is folded and the duelist stands in **standby**: forearm forward of the hip, out from the side,
    plate facing out. The folded halves lie along the forearm past the elbow, so a forearm across the body put one into the
    chest, and a hanging arm stood them up beside the ribs.
  - Some characters get a different standby (`DeployStyle.WithStandby`): the forearm upright in front of the chest,
    plate out (Bonz's frame).
    - Kaiba and Mai: a long coat or waist-length hair fills the space behind the elbow, where the shared standby's
      halves reach.
    - Yugi and Rex: their snap up to the raised pose becomes a straight lift. From the hip, the halves swept the chest.
  - **Snap** (0.5 s): from standby, forward first, then into the activation pose (ease-out-back). The disk opens as the
    pose lands (`OpenDelay`).
  - **Hold through the whole activation.** The body reads the disk's real mechanism (`DiskFxDriver.MechOpen`; previews:
    `SpiritDuelerMechanics.DebugPose`), not its own clock. The pose stays rigid while the halves extend, slide, meet and
    swing, as in the owner's GIF (frames 1–8). There's a 1 cm recoil at the meet clack (mech 0.60) and the lock jolt
    (mech 0.95: 1.8 cm dip + 4°).
  - The plate faces the opponent about 35–40 cm in front of the body during the activation. The halves sweep a disc of
    about 40 cm round the hub in the plate's plane. A plate tipped up at the hips swept it through the legs, the hips,
    long hair and Kaiba's coat. The free hand stays behind that plane (`ActClips`: fist low, Yugi's GIF).
  - **After the lock:** the character's gesture clip (`Clips`) and the referenced stance (`Present`), held `AfterLock`
    seconds, then a 0.8 s settle. `DeployShowSeconds` tells `PreDuelCinematic` how long to keep the cut-in up.
  - Styles (`StyleFor`), each from a frame (scratchpad refs of 2026-10-04; tenor / fandom / Falsebound Kingdom):
    - **Yami Yugi** (owner GIF): fist beside the head, plate out, right fist low; after the lock, the plate level under the chin.
    - **Kaiba** (synchronized activation with Yami Yugi): disk out front; the card is held up beside his head by IK
      (`CardHand`; the `Duel_Reveal` clip threw the arm overhead and across his face). His referenced disk stances
      (diagonal to the hip, level across the waist) put this kit's plate inside his open coat.
    - **Joey** (FBK marshal art): forearm level across the navel after the lock (`LevelPos`: elbow bent about 90° at the
      side, wrist at elbow height; further out the arm ran out of reach and the plate slanted up), fist pump.
    - **Mai** (Doma-era disk only): level across the hips after the lock, card raised.
    - **Yami Marik:** claw thrust at shoulder height, open hand palm down.
    - **Yami Bakura:** disk low at the hip after the lock, finger pointed at you.
    - **Weevil:** level across the belly (`LevelPos`), plate tilted up.
    - **Rex** (Doma-era disk): raised level across the chest, right fist.
    - **Bonz:** upright in front of the chest.
    - No reference exists for Pegasus (never wears a disk in Duel Monsters), Keith, Mako, the Paradox Brothers or Téa,
      so they use the default forward stance.
- **In-duel clipping fixes (same pass):**
  - **Gestures:**
    - A clip's lean or stagger carries the disk arm, measured against the chest's idle pose: its full travel, its full
      twist about the vertical, and 35 % of its lean / tilt. Swung rigidly, a forward lean dropped the blade into the
      thigh. With only 35 % of the twist (until 2026-10-05), the attack command, which turns the duelist side-on, left
      the hub in Kaiba's coat (49 crossings, now 0).
    - During any gesture the disk also eases 6 cm forward, because the hub's underside met the belly in a lean.
    - Start, victory and defeat no longer hand the disk arm to the clip.
    - **Defeat (2026-10-05):** `Duel_Defeat` throws the duelist onto their back, flat by about 1 s. The disk arm is
      posed in the torso's own frame, built from the bones (hips → neck, shoulder to shoulder):
      - Standing, it hangs limp out at the side, plate facing out, while the powered-down disk folds.
      - Going down, it is flung out from the torso's axis (`DeployStyle.DefeatArmDeg`, 70°; Mai 55°).
      - Lying, it rests on the street, palm down, the folded disk flat on top of the forearm, elbow low. It comes down
        only once the spine is nearly flat, and never more than 35° below the shoulder.
      - The earlier slump was aimed in the standing frame, so on the ground it pushed the disk 25 cm through the
        street. The clip-count probe never saw that.
      - Rejected: plate out stood the folded halves in the street; plate down put the big blade into the ribs; a raised
        elbow stood the disk up like a fin; aiming at the street while the body was still tipping turned the plate up
        beside the ribs.
      - Defeat crossings: Yugi 4 (his loose sleeve at the elbow), Mai 44 (her hair as the blade folds while she is
        still standing), everyone else 0.
    - **Street guard (`KeepDiskAboveStreet`):** after the left-arm IK and strap, if any shown disk part (its mesh box)
      is below the street, the disk-arm target lifts and the arm re-solves (up to 3 passes). Every beat is covered.
    - Per-character room (`DeployStyle.GestureRoom`): Mai's disk sits a further 12 cm out and 16 cm forward during
      beats; her waist-length hair fills the space a lean swings into. Attack went from 159 to 51 and summon from 46 to 2.
    - Kaiba's card hand (`CardHand`) keeps the elbow forward and down. Lifted out to the side, the upper arm pulled
      his coat sleeve's armpit, which is weighted to the chest, into black strands.
  - **Disk reaches:**
    - The disk arm brings the disk across toward the working hand when a zone is out of reach. The pull goes sideways
      and up only.
    - The hand arcs over the plate.
    - The elbow is hinted above the plate.
    - Fingertip touches come in pitched down.
    - The wrist lifts until the lowest fingertip rests about 6 mm above the spot (fingertip = 1.3 distal-bone lengths
      past the last joint).
    - The worn disk is strapped before the right hand aims, and the reach's chest lean is applied before both arms are
      solved. Both used to run after the IK, which moved the hand into the plate.
    - **Right-arm keep-out (`KeepRightHandOutOfDisk`, every frame):** each disk part is a slab in its own frame (Y = the
      plate normal).
      - The hand's joints, fingertips (gloves 1 cm more) and the forearm (4.5 cm sleeve) push the hand out to their
        side of the plate.
      - The elbow and the lower upper arm (9 cm sleeve: Yugi's jacket bunches out that far round a bent elbow) lift the
        elbow, as the IK pole, with the hand pinned.
      - Yugi's tap went from 82 to 0 and place from 47 to about 20.
  - **Leg-wear weights:** `argon_bottoms_*` lose their arm and hand bone weights at import
    (`AvatarImportPostprocessor.OnPostprocessModel`). Yugi's trousers had 346 vertices pulled toward the raised wrist.
- **Measuring it:** `OpponentDeploySweep.Run(name[, outDir, render, beats, only, markAt])` (Editor) plays the deploy on
  the real mechanism timeline and every beat and reach. Each probe (`DiskClipProbe`, explicit skinning;
  `BakeMesh(useScale)` is off by 2–40× on these rigs) counts body, outfit and hair edges through the disk parts, per
  phase. Pass `only: "beat|reach:kind"` to keep one eval under the time limit.
  - It also logs `floor!` lines: the lowest disk point and disk hand over each beat (below 2 cm, and always for defeat),
    plus the resting heights and arm angle at the end of the defeat. `only: "foldgeom"` prints the folded disk's extent
    along the forearm.
  - Renders follow the body (centred between the hips and the head), over a grey street plane, and add `eye` (from the
    player's head) and `top` views. `OpponentDeploySweep.DebugSkip = 1` shows the clip's own disk arm for comparison.
  - **Cut-in:** the street puts the opponent metres away, and a phone or headset view cannot be pushed in. So `DeployCutIn` slides a slanted anime banner across the top with a live close-up camera (head to waist, slow push-in), the name, and "DUEL!".
  - PreDuel waits on the duelist's own clock (`DeployElapsed`), so a frame hitch cannot cut the pose short.
- **Player (~2.9 s), `PreDuelCinematic.PlayerDiskShowcase`:**
  - The Spirit Dueler leaves the arm (`ExternalPose`) and spins up in front of the camera. It sits at about half the view width, sized from the open blade length.
  - The blade swings open, the rune ring flares, and the LP window boots (lamp test, then the starting LP rolls up).
  - It hovers, then flies back onto the live arm pose.
  - While it flies, `SpiritDuelerAura.Travelling` stops the world-space ribbons, sparks and flames (they would streak across the view), and `Emphasis` drops to 35 %.
  - Smootherstep flight curves.
- **Previews:**
  - `OpponentDuelistPreview.Render(name, null, false, deployOnly: true)` renders wind-up, snap, pose, settle and after.
  - Live: read `ArCam.targetTexture` plus the cut-in camera's texture. The Desktop Lab AR Sim / Instant Duel now seats
    Mr. Referobot (`ai_kaiba.json`, 2026-10-05), who has no duelist body, so pick a roster opponent to see one.

### Anime shading (2026-09-29) — `Presentation/Avatar/AnimeToon.hlsl`
`WRLDZ/AvatarRecolor` (bodies, outfits, companions, NPC disk models) and `WRLDZ/AvatarHair` are cel-shaded:
- The painted colour stays flat. The key light (the main light, or a fixed upper-front-left key when there is none) only picks the band: one crisp cool shadow band, never multiplied by the light colour.
- A thin rim light on the lit side.
- Hair gets an angel-ring sheen band.
- Every model draws an inverted-hull ink outline pass (`SRPDefaultUnlit`, constant width in pixels, ink = painted colour × 0.28). It skips body texels hidden under an outfit.
- Tunables per material: `_ShadowAmt`, `_RimAmt`, `_OutlineWidth`, `_OutlineDark` (and `_Sheen` on hair).

## One-shot Spells from the hand: shown, not placed (2026-10-04)

Owner: a Normal / Quick-Play / Ritual Spell goes to the GY after resolving, so the hand does not set it on a zone.
The rule is `ArSnapRules.RevealsFromHand`; Field, Continuous and Equip Spells are still placed by the hand.

- **The reveal** (`ArFloatingHand.PlayReveal`, ~1.2 s + 0.24 s dissolve):
  - The hand lifts the card out of the fan, raises it and turns its face square to the opponent.
    - Your hand turns it away from your eye.
    - The opponent's hand turns it to you, at collar height (`RevealRiseOpponentM`).
  - It holds the card up with a short thrust, then the activation commits (`atReveal`).
  - The card shrinks out of the hand, and the floating card is `HeldByGesture`, so the hand sync doesn't delete it
    mid-reveal.
- **Zone choice:** the engine already seats an activation in the first free S/T Zone (centre first,
  `SpellTrapEffects.PlaceFaceUpForActivation`). The player never picks one.
  - Card-menu / zone-pad Activate: `DuelUI.DoActivate` → `ArDuelInteractionSystem.TryRevealFromHandWithGesture`.
  - Drag-drop with Activate: `ArCardDragSystem.FinishCommit` → `PlayReveal` + `CommitRevealed`. The card is not
    snap-locked on the dropped zone, and the chooser title no longer names that zone for Activate.
  - Real tracked hands: commit directly; your own hand made the gesture.
- **The disk** (`ArCardFieldController.AppearActivatedFromHand`):
  - The card appears face-up in its slot in the ejected, activated pose, with the slot flash and a small overshoot
    (`ArDiskMotion.HandActivateAppear`). It does not slide in, eject and flip like a Set.
  - It holds while the chain / beat plays, then reseats (staying cards) or flies to the GY (`ActivatedFinish`,
    shared with the Set-card toaster).
  - An already-resolved card gets the same appearance as a ghost.
- **The opponent:**
  - The AI commits before any presentation, so `ArHandVolume.TryQueueGeneratedPlace` queues a generated reveal for
    each one-shot that left its hand with a fresh activation event.
  - While it plays, the slot (`IncomingGesture`) and the arena (`GestureInFlight`) wait.
  - At the end, `ActivationBeat.RestartClock` restarts the beat (art readable, results and fade count from the
    reveal), and the disk / arena sync.
  - `ArOpponentDuelist` plays its cast ("activate") only after its hand has finished revealing; started earlier, the
    clip took the arm and the whole fan away.

- **Spells that stay on the field (Continuous / Equip) take the chosen zone** (2026-10-05).
  - The engine used to seat every activation in the first free zone, so the zone a player picked for one was
    cosmetic. A dragged card could also stay locked on the dropped zone while the engine seated it elsewhere.
  - `DuelEngine.SetPreferredSpellTrapZone` / `ClearPreferredSpellTrapZone` (commit 648dc24b) give a per-side
    preference that `FirstEmptySpellTrap` honours while that zone is usable. It stays set until the zone is taken,
    so a target or cost pick before placement keeps it.
  - `ArSnapRules.Commit` (a drag onto a zone) and the zone-pad Activate (`DuelUI.DoActivate` via `_activateInZone`)
    set it. The zone pad also has the hand carry the card to that zone, like a Set.
  - One-shots ignore it (`RevealsFromHand`).
  - Verified in Play mode: Messenger of Peace via the S/T 5 pad seats in S/T 5, and Banner of Courage dragged to
    S/T 1 seats in S/T 1 (both used to land in the centre zone).

## Arena S/T row: tight, art to the opponent (2026-10-04)

- **Spacing:** S/T holograms stand as one group centred on the M3 column.
  - The pitch is one card face plus a 0.10 gap (`ArPlaymatLayout.SpellTrapColumnPitch` = 0.68), times the live
    hologram scale.
  - It replaces the 1.20 monster pitch with the half-column stagger: 4.8 wide, and its side cards had to turn to
    the camera to read.
- **Facing:** an activated S/T stands square to the arena axis (`ArArenaCardVisual.ViewFlat` = arena −Z) for every
  column.
  - Your card shows you its back and the opponent its art; the opponent's card shows you its art.
  - It used to turn each zone to the stage camera, which swung your side cards' art out toward the sidelines.
  - The 12° lean is unchanged: the owner's side tilts up toward its eye.
- **Trade-off:** a tight row sits behind the monster wall, so a tall monster can hide part of the S/T behind it
  from the other side.
- **Checks:** `ArPlaymatLayout.RunSanityChecks` now asserts that the row is centred on M3 and grouped tight (29/29
  pass).
- **Verifying in Play mode:** `WRLDZ.Editor.ArPlayFrameRecorder.Start(dir, seconds, interval)` grabs per-frame
  views while a duel plays:
  - the stage camera, pitched down, your disk, the arena, the opponent close-up, and the view from the opponent's eye;
  - a `log.txt` of the opponent's gesture and hand state per frame.
