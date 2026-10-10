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
| **Arena** | Between you and opponent | Life-size (or stage-scale) holograms from face-up monsters / activations; set cards as flat card backs |
| **Mr. Referobot** | Physical NPC near arena | Referee + announcer (phase calls, illegal, impact, victory) |

---

## Information rules (TCG-legal visibility)

| Card state | You see | Opponent sees | Arena hologram |
|------------|---------|---------------|----------------|
| Your hand | Face-up fan toward you in the floating right hand (`ArFloatingHand`); full text in **your** popup on select | Backs only | No |
| Your face-down set | Set on your disk | Set on their disk | Card back only (face hidden until activation) |
| Your face-up monster | Full on disk + popup | Full on their disk + popup if they inspect | Yes |
| Opp face-down | Set on their disk | (their own) | Card back only (face hidden until activation) |
| Face-down monster declared as an attack target (either side) | Its sideways **card back** at the Defense-plate pose | Same | Card back only, until the damage-step flip |
| Opp face-up | Full on their disk + midfield holo | Full | Yes |

Selecting a card **never** reveals hidden info to the opponent.  
Popup is **local UI** for the selecting player.

### Face-down cards and battle-position plates (owner rules, 2026-09-23, revised 2026-09-27)

- Face-down cards on **both** sides show in the arena as their **card back** lying flat on the zone
  (monsters sideways at the Defense-plate pose, S/T in portrait), Battle City style. The face stays
  hidden until the card is revealed: flip summon, battle flip, or S/T activation, which play from that
  card back (`ArArenaCardVisual.RestAsSetCard`). The disk shows the card back too. (2026-09-23 hid set
  cards from the arena entirely; the owner reported on 2026-09-27 that set cards then did not show.)
- A face-down monster that is attacked stays its sideways card back until the damage step, then flips:
  card → Defense plate → braced monster. If the attack is cancelled or retargeted, it stays a card back.
- A face-down monster that leaves the field without being revealed (bounce, tribute, Change of
  Heart, face-down banish) is removed quietly as its card back. It flips only when the engine card is
  now face-up and a battle shatter is queued (`CardShatterPresentation.HasQueued`); an effect destroy
  shatters the card back.
- Monsters show a **Defense** or **Attack** card plate under the hologram. Keep these plates
  (owner decision 6).
- Open items from the face-down review (`research/qc/facedown_review_2026-09-23.md`): the disk-side
  `MonsterLeaveThenClear` still flips a leaving face-down monster in place; duplicate signature spell
  FX; seat-hold overlaps; the player-side S/T present pose sits below the default home camera.

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
| Card backs | `StreamingSprite.CardBack()` / `CardBackTexture()` |
| GO chrome sprites | `GoChrome` (`StreamingAssets/WRLDZ/GoChrome`) |
| Duel HUD orchestration | `DuelUI` |
| AR phone companion (profile / systems) | `ArCompanionPhoneHud` — no 2D duel board in AR |
| Dual-board layout notes | `Presentation/ArInteraction/FIELD_SPLIT.md` |

### Phone vs AR field (canon)

| Mode | Phone screen | Duel cards / zones |
|------|----------------|--------------------|
| **AR / Zone Mode ENTER AR** | Profile, decks, backpack, tome, artifacts, settings | Spirit Dueler disk + midfield holos only |
| **Digital / PreferDigital** | The same AR stage rendered full-bleed in a RawImage + phase dock (the old 2D field rows are dead code — QC M3) | Same engine |

During AR there is **no dueling UI on the phone** — only companion systems and a thin Pass/Cancel Target control when a response window opens.

Assets (original / CC0-style pack-ins under `StreamingAssets/WRLDZ/`):

- `CardBack/card_back.png` — pack-in card back (prefer YgoFrames / CardArt pipeline when present)  
- ~~`YgoRefs/`~~ — Yu-Gi-Oh! screenshots, parked off-build (`tools/OFFBUILD_MANIFEST.md`, owner decision
  2026-09-23 #4). Restore them before wiring them in.
- `Referobot/referobot_portrait.png` — companion AI portrait until mesh art lands
- `GoChrome/*.png` — soft orbs / pins for map UI  

---

## Spell / Trap activation beat (timing canon, 2026-09-23)

Owner report: *"The timing in the duel arena is off. Spells/traps do not seem to stay revealed long
enough for players to realize what happened."* Battle City order: the card springs up and its art is
readable, the name is called out, **then** its effect happens, **then** it lingers or goes to the GY.

- **Engine stays synchronous** — `DuelEngine` legality is never delayed. `ActivationBeat`
  (`Presentation/ArInteraction/SpellActivationPresentation.cs`) is a presentation-only result gate:
  it opens on `RegisterActivation`, is told when the engine resolves (`BeginResolve` per chain link,
  `MarkResolved` / `QueueFadeToGy`), and tags removals queued in that frame (`CardShatterPresentation`)
  to the resolving card.
- **Who waits on it:** `ArArenaHologramManager` (removals, revived monsters, flips, opponent glance),
  `ArArenaCardVisual` (hover → fade on the beat clock; concealed Sets now reveal before fading),
  `ArCardFieldController` (disk toaster holds the card up until the fade; destroys wait),
  `ArDuelInteractionSystem` (LP counters, deck / GY piles; re-sync when the gate opens),
  `ArHandVolume` (drawn cards), `ArAnimeSequenceDirector` (impact FX), `DuelUI` (HUD LP, 2D field,
  name banner, queued BATTLE / MAIN 2 / END, attack declarations), `DuelPresentationPacer`
  (`IsHolding` = max(own hold, beat end); result holds count from the gate) → `SimpleAi`.
- All constants live in `SpellActivationPresentation` (see `SOLID_VISION_BEHAVIOR.md` §4.4).

| t (s from engine event) | Own card | Opponent card |
|---|---|---|
| Disk eject → flip (art on disk) | 0 → 0.90 | 0 → 0.90 |
| Arena rise starts / art readable | 0.90 / **1.39** | 0.90 / **1.39** |
| Results shown (`ResultAt`) | **2.79** | **3.19** |
| Fade starts (`FadeAt`), disk → GY | **5.89** | **7.89** |
| Beat end (AI / player advance) | 6.64 | 8.64 |

Per scenario (hand-cast cards: the disk ghost inserts first, so disk art is at ≈ 1.93 s):

| Scenario | Timeline |
|---|---|
| S1 opp Trap Hole (Set) on your Normal Summon | 0 banner; summoned monster staged on its zone 0.48–1.03; Trap Hole art 0.90 disk / 1.39 arena; **3.19** monster shatters; 7.89 Trap Hole fades, disk → GY; 8.64 your queued BATTLE / END runs |
| S2 opp Mirror Force (Set) on your attack | attacker keeps its charge glow 0–3.19; art 1.39; **3.19** attackers shatter; 7.89 fade; 8.64 end |
| S3 opp Raigeki / Dark Hole from hand | ghost hidden until 0.90; art 1.39; **3.19** face-up monsters shatter (face-down: flip, shatter ≈ 4.55); 7.89 fade; AI next ≈ 12.2 (8.64 + 3.6 step) |
| S4 your Pot of Greed | art 1.39; **2.79** two cards arrive in hand (deck count drops); 5.89 fade; 6.64 end |
| S5 your Set Magic Cylinder / Negate Attack vs AI attack (tap at tp) | tp+1.39 art (attacker still charged); **tp+2.79** opponent LP rolls / battle ends; tp+5.89 fade; AI next tp+6.64+3.6 |
| S6 Swords of Revealing Light (opp) | art 1.39; **3.19** your Set monsters flip; hover to 7.89; settle 7.89–8.34, stays readable over its zone; later MST shatters it on MST's result beat |
| S7 chain: opp Raigeki CL1, you chain Seven Tools CL2 at t1 | 0 CL1 revealed, chain countdown starts; art 1.39 (CL1 holds while unresolved); t1 CL2 disk flip; t1+1.39 CL2 art; **t1+2.79** CL2 result: Raigeki shatters, LP counter shows the 1000 cost (same engine call, so it rides the gate); a non-negated CL1 result would follow at t1+4.29 (`ChainLinkResultGapSeconds`); t1+5.89 CL2 fades |
| S8 your Quick-Play in battle (targeted, tap tp, pick tq) | tp+1.39 art (card holds while you pick, ≤ 12 s); results (ATK change + battle) at max(tp+2.79, tq); fade at max(result+2.0, tp+5.89) |

---

## Non-goals (this slice)

- Real multiplayer mirror sync of hand backs  
- Full XR interaction SDK (hooks reserved)

---

## Kuriboh AR companion (AR duels + Zone Mode)

`Presentation/KuribohArBehaviour.cs` — the chosen Kuriboh as a small, silent spatial presence. The overworld HUD
navi (`KuribohCompanion.AttachMap`) is unchanged.

- **Mount:** `ArDuelInteractionSystem` build → `KuribohCompanion.EnsureDuel` → `KuribohArBehaviour.Ensure`, parented to
  the **stage root** (beside the hand volume, not the arena — Zone Mode hides the arena). Replaces the old fixed
  opaque quad in the arena (kept only as a fallback when no interaction system is found). `ArDuelSpace.BindEngine` /
  `SyncFromEngine` call `KuribohArBehaviour.BindUnder(_stage, engine)`.
- **Look:** camera-facing hologram card, ~0.28 m tall — the team's navi portrait (backdrop keyed out) at 78 % alpha over a
  `WRLDZ/SpiritGhostUnlit` plate tinted with the disk accent. Unlit, no shadows, no colliders, no physics.
- **Home anchor (player's LEFT):** 0.55–0.85 m left, 0.15–0.35 m above eye level, 0.4–0.7 m forward, in a *leveled*
  head basis (yaw-only, lightly smoothed; world up), re-rolled each visible cycle. A candidate must be ≥ 0.25 m outside
  the **hand volume** AABB (floating cards + any other renderer under `ArHandVolume`, e.g. the floating right hand) and
  the **Spirit Dueler disk** AABB (`ArPlaymatLayout.DiskMeshBoundsMin` / `DiskMeshBoundsMax` in blade-pivot space), and ≥ 0.35 m off the player→opponent-field
  segment. Re-validated every 0.5 s; if a volume moved into it, a new home is picked and the current behaviour drops to
  Hover. When the hand volume is inactive (Zone Mode, pre-draw) a conservative fixed chest box is used (logged once).
  Every frame the position is also pushed out of both keep-outs.
- **Timer:** visible 12–25 s, hidden 8–20 s (uniform), 0.4 s fade with a soft ease-out-back scale pop. Starts hidden
  3–6 s so the disk deploy / opening draw own the first beat.
- **Behaviours (while visible):** Hover-bob 40 % (sine bob, slow yaw wobble) · Orbit 25 % (0.5 m loop round home,
  clamped ≥ 0.30 m left of the eyes) · Dart 20 % (opponent's newest monster, the player's newest summon, the deck
  well, or the Referobot; stops 0.3 m short, wiggles 1 s, returns) · Peek 15 % (dips toward the hand but stays
  ≥ 0.3 m off it, aborts if the hand moves). `KuribohArBehaviour.ReduceMotion = true` → Hover only (no setting sets this flag yet — QC ar-presentation F11).
- **Duel events:** no event bus exists, so it diffs `DuelEngine.OnStateChanged`: a player summon wakes it (if hidden
  ≥ 3 s) and Darts to the new monster; an opponent summon Darts there 50 % of the time; a draw gives a small bounce.
- **Known limit:** the spec'd home (≈ 50–65° left of forward) sits at or past the left edge of a phone / Editor-sim
  view — by design it lives in the periphery; Orbit and Dart bring it into view.
