# Floating hand — the player's right hand holds the cards

Owner rule (2026-09-23): the avatar body is not shown in duels or Zone Mode (the player's physical body is there).
The **floating cards** mechanic is replaced by a **translucent floating right hand** that holds the hand cards as a
fan, draws them from the Spirit Dueler disk, riffles them on SHUFFLE, and lifts a tapped card.

| piece | role |
|---|---|
| `ArFloatingHand.cs` | the hand: model, material, fan slots, draw / riffle / select, fade |
| `ArHandVolume.cs` | creates it (player volume only), keeps the engine hand order, calls `Tick` every LateUpdate, picks |
| `ArFloatingCard.cs` | card visuals unchanged; `HeldByHand` (the hand writes the transform, no idle spring / bob), `SetHandRestImmediate`, `BeginHandDraw` / `EndHandDraw`, a return that tracks the live slot |
| `ArCardDragSystem.cs` | hover / press → `SetHover` (peek, re-picked every frame); tap → `ToggleSelected` (lift) + the same `OnHandCardTapped` menu; drag → `BeginCarry` (the hand holds the card; the card follows the pointer every frame); a legal drop → `PlayPlace` (the hand sets it down, commit at contact) |
| `ArDuelInteractionSystem.TryPlaceFromHandWithGesture` | the card-menu zone pick (`DuelUI.PlaceInSlot`) goes through the same hand gesture |
| `UI/DuelUI.cs` `DoShuffleHand` | existing SHUFFLE button → `ArFloatingHand.NotifyHandShuffled()` |
| `Resources/WRLDZ/Avatar3D/argon_hand_right.fbx` | mesh, built by `~/argon-avatar/tools/make_hand.py` (see AVATAR_3D_INTEGRATION.md) |

The opponent's hand volume keeps the old free-floating fan (backs only).

## A/B switch

`ArFloatingHand.Enabled` (static, default `true`). Set it `false` from the Editor (Debug inspector, a quick script, or
the Immediate window) and every idle card moves back to the old free-floating fan (`ArHandVolume.ComputeFanSlot`) on the
next frame; flip it back and they return to the hand. If the FBX is missing or has no `Anchor_*` empties the hand logs
a warning and the old fan is used automatically.

## Anchors and placement

Everything is framed from the FBX's `Anchor_*` empties at load, so FBX axis conventions or a 100x import scale cannot
tilt or resize the hand. Canonical hand space: wrist at the origin, +Y fingers, palm facing −Z (the eye), +X thumb.

- **Parent:** `HandVolume/FloatingHand` (the volume is placed by `ArHandVolume` at torso + 0.28 fwd − 0.12 up and faces
  the camera's forward).
- **Scale (`MetreScale`):** 1:1 metres on lenses / ARCore / salvage (`Tracker` is not `SimulatedArmTracker`);
  in EditorSim / phone RT the hand is `SimHandCardHeights` = 1.35 card heights (was 0.9; owner asked for 50% bigger on
  2026-09-27), ≈ 2.5 units per metre. It grows around the grip, so the fan does not move.
- **Rest pose:** grip (thumb web) at `(+lateral, −0.52 card heights, 0)` in volume space — lateral 0.20 m life-size,
  0.95 card widths in EditorSim, i.e. to the player's **right**, away from the disk on the left arm and the Kuriboh.
  Rotation `Euler(6, 10, 12)`: palm to the eye, top tipped back, palm yawed toward the midline, fingers leaning left.
  Idle bob 5 mm + ±1.2° roll.
- **Fan (`SlotFor(i, count)`):** arc around `Anchor_Grip`, radius 0.46 card heights, 17° per card (max 100° total),
  centred on a lean of half the grip→fingertip angle (`Anchor_FanLean`, the rest-pose tips: ~20° left). Index 0 is
  leftmost; the **rightmost card is nearest the eye**, so every card shows its top-left (name) corner. Cards stack in
  parallel planes (never interpenetrating): the front card sits just behind the thumb pad (`Anchor_ThumbTip`), the
  rest step back toward the finger pads (`Anchor_FanPads`); the step is compressed so the whole fan fits in the
  thumb → pad gap (≈ 4.3 cm), so no card is ever inside the fingers. Fan cards are 60% of the disk card's body
  thickness (`HeldCardThickness`, local z only): ten oversized sim cards need ≈ 3 cm.
  **The hand writes every idle card's transform itself each LateUpdate** (`DriveFan`; `ArFloatingCard.HeldByHand`):
  the fan is rigid with the hand (no spring lag, no per-card bob — the cards used to slosh against the hand that
  held them) and only slot changes are eased per card: lifts / tilts at 16/s, the re-spread, depth and size at 11/s.
  The fan is laid out for the cards actually drawn (`FanCount`: the engine hand minus the draws still queued), so
  the gap opens as each card arrives. A card returning from a drag or a failed drop snaps toward the **live** slot
  (`ReturnToHandRest` with `HeldByHand`) and lands exactly on it; the hand lets go of it one card height out.
- **Blend shapes:** `FanHold` with cards, `Relaxed` when empty, `Pinch` while drawing, `Present` while carrying a
  card (index + middle straight and parted with the card clamped between them, ring + pinky curled, thumb folded:
  the anime "I play this card" grip); weights always sum to 1. `Anchor_PresentGrip` / `Anchor_PresentTip` frame the
  carried card (grip → tip is the card's up axis, the thumb side is its face). Without them (older FBX) the hand
  carries with the pinch instead. Pinch / Present follow their eased gesture drivers exactly; the fan grip opens and
  the empty hand relaxes with an ease of their own (`_wHasCards`), so the first draw into an empty hand never snaps
  the fingers. The importer recomputes blend-shape normals (`AvatarImportPostprocessor`: Calculate), so a curled
  finger is lit as curled.
- **Material:** `WRLDZ/SpiritHandHolo` (Resources/WRLDZ/Shaders; falls back to `WRLDZ/SpiritGhostUnlit`), drawn as HD
  anime (AGENTS.md art bar): the glass colour (avatar `skinHex` lerped 22% toward a light tint of the Spirit magic
  colour, alpha 0.5, fill 0.85) gets **one crisp cel shadow band** from the key light (`AnimeToon.hlsl`, the same
  shading as the avatars) and a thin lit-edge rim, and an **ink outline pass** (inverted hull, 1.6 px, ink = a deep
  shade of the Spirit colour) that dissolves with the seam. The hull's depth is pulled a little toward the eye
  (`_InkDepthPull`), so the thumb's line survives where it crosses a card face (the back-face depth used to fail
  there). The Spirit fresnel rim, faint energy bands wrist → fingertips (`_BandAmt` 0.3), hotter fingertips and
  hand-local hologram bands (`_ScanAmt` 0.04) stay, subtler. The forearm dissolves into light on a noisy seam
  **below** the wrist (`_DissolveAt` −0.18, width 0.12 hand lengths), so the wrist narrowing and the heel of the
  palm are drawn. A depth-only pass first, so only the nearest surface draws (no palm through the fingers) and the
  ink hull only survives outside it. `ArFloatingHand` writes `_WristPos` / `_HandAxis` / `_OutlineColor` every
  frame and fades the whole projection with `_Fade`. Queue 3160 (after the aura, the deck well and the field holos,
  so the depth pass never punches a hand-shaped hole in an effect drawn after it; the hand's own FX at 3170), no
  shadows, no collider. Cards are opaque and write depth, so fingers behind the fan are hidden and the thumb over
  the faces shows. The hand never blocks a pick.
- **Spirit FX (world space):** star sparkles with short trails drift off the fingers; light motes gather up into the
  wrist seam; each draw bursts sparkles at the deck and pulls a tapered comet streak behind the card.
- **Card faces read correctly:** `ArPhysicalCardBuilder` turns the Face quad 180° so the art reads from +Z (Unity's Quad
  reads from −Z). Before 2026-09-27 every face-up card in the hand and on the disks showed mirrored text.
- **Empty hand:** fades out 4 s after the last card leaves; the next draw fades it back in.

## Behaviour

- **Draw** (`QueueDraw`, from `ArHandVolume.SyncHand` for each new card): the card waits invisible in the deck well;
  the hand blends to `Pinch` and reaches palm-down onto the disk magazine (`ArDuelDiskRig.TryGetDrawOriginWorldPose`,
  resampled every frame so a moving arm still works), the pinch lands on the top card's near edge,
  `PulseDispenseTopCard` + draw SFX as it closes, the thumb **peels** the card back off the stack (it slides along the
  deck, then lifts into the pinch), the hand returns to rest blending back to `FanHold` while the card grows, flips
  face-up and drops into its slot **from above** (an arc over the fan, then down into the gap). 0.9 s for a single draw, 0.75 s + 0.62 s per card when chained (opening hand
  of 5 ≈ 3.2 s, inside the pre-duel wait and `DuelPresentationPacer.HoldDraw`). Ease-out throughout.
- **Shuffle** (`PlayRiffle`): the existing hand SHUFFLE button (`DuelUI.DoShuffleHand`, bottom-left orb) sets
  `NotifyHandShuffled`; the next sync riffles even if the random order came out unchanged. A pure reorder of the same
  cards is also treated as a shuffle (in AR the 2D reorder tray is hidden). Riffle = 1.15 s: fan closes to a stack
  (0–22%), three flick passes — halves swing out left/right with a small roll and interleave their depth order at the
  apex (a perfect-riffle permutation, so no two cards share a depth) — then the fan reopens in the new engine order.
  Picks pause while riffling. A shuffle during a draw waits until the draws finish.
- **Pick** (`ArFloatingHand.PickCard`, through `ArHandVolume.PickForPointer`): the card boxes are tested on this
  frame's transforms (physics auto-sync is off in this project, so collider raycasts saw last step's poses). The
  selected card's lifted box wins first; the hovered card keeps the pick while the ray is on its lifted box **or** its
  unlifted rest box unless a card in front of it is hit, so a peeking card cannot move out from under the pointer
  and flicker; otherwise the nearest box along the ray. `ArCardDragSystem` re-picks the hover every frame from the
  last pointer position, so the peek follows a fan moving under a resting cursor.
- **Peek** (`SetHover`): the card under the mouse (Editor / desktop, `IPointerMoveHandler` on the viewport) or under
  the finger the moment it is pressed rises 1.4 cm out of the fan, so you can see which card you are about to take.
- **Select** (`ToggleSelected`): tapping a card lifts it 4 cm (× `MetreScale`) along its own up and tilts its top 14°
  toward the eye, pivoting at its bottom edge (so the bottom never swings back through the thumb); it keeps its own
  depth in the stack. Tap it again to drop it back; closing the card menu drops it too (`DuelUI.ClearCardSelection`
  → `ArCardDragSystem.ClearHandLift`). The play / inspect menu opens exactly as before (`OnHandCardTapped`).
  Dragging, a field tap, or the card leaving the hand clears the lift.
- **Drag** (`BeginCarry`): as the drag starts the hand lets go of the fan, takes the card between its index and
  middle fingers (`Present`) and follows it under the pointer; the card straightens up to face the eye and grows 8%
  over 0.12 s, and it keeps closing on the pointer every frame (`StepDragFollow`), not only on pointer-move events,
  so it never stops short of the finger; the drop lands where the pointer is. A card taken while the hand was still
  letting go of the last one cross-fades the grip (`TakeCarry`) instead of jumping. The Summon / Set chooser opens
  with the hand still holding the card over the zone. A drop with no legal zone, an EXIT on the chooser, or an
  engine refusal: the card snaps back to its live slot and the hand rides it until it is a card height out, then
  lets go (`EndCarry`). A draw that arrives while the hand holds a card waits until the grip has opened
  (`CarryHoldsPose`); the two gestures used to fight over the one hand pose.
- **Place** (`PlayPlace`, also used by the card menu's zone pick through
  `ArDuelInteractionSystem.TryPlaceFromHandWithGesture`): the play is validated first (an illegal one never leaves
  the hand); then the hand carries the card on a small arc to exactly where the zone's own seat motion starts
  (monster pad: 6 cm above the pad in its final orientation, face-down sets turn over on the way; S/T and Field:
  the toaster's present pose), and the engine commit + `SnapLock` fire **at contact**. The hand then rides the pad
  snap / toaster present for its duration and eases back to the fan. While a gesture runs the drag system holds the
  pointer (`GestureBusy`) and DuelUI ignores AR taps.
- **Back to the fan**: after every place, return or cancel the hand lets go (`EndCarry`) and blends back to its rest
  pose holding the fan (about 1.5 s after the card lands). `EndCarry` never depends on the carried card still
  existing (the disk's own card visual replaces the floating one at contact), and `TickCarry` drops a carry whose card
  is gone or was seated by a direct commit, so the hand cannot stay parked at the disk. Hiding the hand mid-place
  still reports the place back (`FinishPlace(false)`), so the drag system never waits for it forever.
- **Shuffle** closes the fan from the **old** angles (`PlayRiffle(fromIndex)`, the pre-shuffle engine indices) and the
  last flick lands the depth order on the engine order, so the reopen is already the final fan; a riffle asked for
  while one runs is queued, not restarted.
- **Opponent** (`ArHandVolume.TryQueueGeneratedPlace`): the AI's plays run the same draw and place gestures on its
  own hand and disk. `ArDuelInteractionSystem` holds the arena summon until the card touches the zone
  (`GestureInFlight` / `GestureLanded`), with a 3 s timeout so the engine is never held up.
- **Real hands (mixed reality)**: on OpenXR hand tracking (`XrWorldArmTracker` implements `IArHandInput` through the
  Hand Interaction profile) the Spirit Dueler rides the left hand's grip (`leftForearmOffset` / `Euler`, forearm
  side) and the fan rides the real right palm (`SetTrackedGrip`, `GripToModel`); the ghost hand mesh hides so the
  cards sit in the player's own hand (`ShowGhostOverRealHand` puts it back). Either real hand selects
  (`ArCardDragSystem.PollHands`, ahead of the controller aim ray):
  - fingertip within 0.3 card heights of a card → peek; pinch (closes at 0.85, opens at 0.5) within 0.35 → take it.
    The right hand, which wears the fan, only takes a card by its top half, above its own fingers;
  - pinch let go without moving → tap (lift + play menu), exactly like a screen tap;
  - pinch moved 0.18 card heights → carry: the card stays under the fingers where they closed on it and turns with
    the hand (smoothed at 38/s against tracking jitter), legal zones light as it passes over them;
  - let go within 1.05 card heights of a legal zone → played there, decided by how the card faces the eye: face-up =
    Summon / activate, turned face-down = Set (the chooser only appears for a play the card cannot make). The
    player's own hand did the placing, so the ghost place gesture is skipped and the zone seats the card;
  - no legal zone, or the hand lost for more than 0.35 s → the card flies back to the fan;
  - a pinch away from every card points with the hand's aim ray (disk cards, zones, arena targets).
  Enable **Hand Interaction Profile** under Project Settings → XR Plug-in Management → OpenXR (Android and
  Standalone; on as of 2026-09-27). On-device calibration still needed: `GripToModel` and the forearm offsets.

## How to verify (Editor, Desktop Lab)

1. Focus the Editor so `argon_hand_right.fbx` imports (Inspector: Rig = None, blend shapes `Relaxed/FanHold/Pinch`,
   nine `Anchor_*` children). Console on play: `[WRLDZ AR] Floating hand ready · lean NN° · anchors ok`.
2. Desktop Lab → **QUICK AR** duel. After deploy + deck shuffle, the opening draw shows the translucent right hand
   reaching to the left-arm disk five times, pinching a card out each time, and building a fan on the right.
3. Draw phase of later turns: one pinch-draw (~0.9 s).
4. Press the hand **SHUFFLE** orb (bottom-left): the fan closes, flicks three times, reopens in a new order.
5. Tap a card: it lifts and tilts toward you and the play menu opens; tap again (or close) to drop it back. Drag a card
   onto the disk: the usual legal-zone snap / chooser works from the fan position.
6. Play out your hand: 4 s after the last card leaves, the hand fades away; the next draw brings it back.
7. A/B: set `ArFloatingHand.Enabled = false` while playing — cards return to the old free-floating fan.
8. Real hands without a headset: in play, set `ArCardDragSystem.Hands = new SimulatedHandInput()` (via `unity cmd
   eval`) and write `Left` / `Right` poses each frame (`Pinch`, `Poke`, `Grip`, `PinchValue`). A pinch at a card,
   moved to a zone and released face-up Summons it; rotate the grip 180° about the card's up on the way and it Sets.
   Console: `Drag start: … (real hand)`, `Hand release: … face-down`, `Snap OK → …`.

Regenerate the mesh: `~/Applications/blender-5.2.0-linux-x64/blender -b --python ~/argon-avatar/tools/make_hand.py`
(add `-- <in.glb> <out.fbx> <render_dir>` to preview the poses as PNGs). 2026-09-30 build: one Catmull-Clark level
(10.4k verts, a smooth silhouette under the ink line), FanHold = fingers flat and **together** behind the stack with
the thumb across the front (a natural curl puts the pads 5 cm in front of the palm, where no thumb can clear a
stack this thick; the fingers are behind the cards from the player's eye anyway), `Anchor_FanPads` /
`Anchor_FanLean` exported, the thumb solver multi-started.
