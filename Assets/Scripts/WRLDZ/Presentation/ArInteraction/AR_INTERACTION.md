# AR Duel Interaction System

Modular AR interaction for Project ARGON / DM WRLDZ. Extends the existing spatial stage — does **not** rebuild GPS, OCR, lore, or menu chrome.

## Lab hardware

- **S23 Ultra + PC**: `SimulatedArmTracker` locks the disk to a stable left-forearm offset in the AR stage.
- **Later HMD**: add `XrControllerArmTracker` (or AR body joints) and assign transforms — same disk/arena code.

## Components (`Presentation/ArInteraction/`)

| Script | Role |
|--------|------|
| `IArTrackingSource` / `SimulatedArmTracker` / `XrControllerArmTracker` | Forearm + torso poses |
| `ArDuelDiskRig` | Arm-anchored disk mesh + official zones |
| `ArDiskZone` + `ArZoneLayout` | 5M + 5ST + Field (invisible anchors; no empty pads) |
| `ArCardFieldController` + `ArDiskCardVisual` | **Card Field** — physical cards on both disks |
| `ArArenaHologramManager` + `ArArenaCardVisual` | **Arena Field** — large card assets (disk → midfield); optional 3D models later |
| `ArHandVolume` + `ArFloatingCard` | Floating hand holos in front of torso |
| `ArCardDragSystem` | Continuous drag; snap on release |
| `ArSnapRules` | Type/orientation legality + engine commit |
| `ArAnimeSequenceDirector` | Summon/activate/attack presentation |
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

Lenses later re-use the same drag/snap graph with XR rays (`XrControllerArmTracker`).

## Dual boards (Battle City / anime)

1. **Card Field** (`ArCardFieldController`) — physical cards on **your** left-arm disk and the **opponent's** left-arm disk. Face-down = card back; face-up = art.
2. **Hologram Field** (`ArArenaHologramManager` + `ArArenaCardVisual`) — **no visible playmat**. Invisible midfield anchors only; large projections spawn when cards exist (art crop now, 3D models later). Spawn origin = disk pad (fly-in).

`PreferDynamicModels` defaults **false** (enlarged card art only). Enable later for `Models/Cards/{id}.obj`.

AR camera is **player-first**: disk and hand near the lens, hologram arena further and higher. UI phase controls live in a slim strip under the LP glance — not over the disk.

Driven by engine state via `SyncNow` / `ArDuelSyncBridge` (disk sync **before** holograms so spawn origins are valid).

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
