# Spatial graph (phone · lenses · EditorSim)

In-repo authority: `AR_DUEL_PRESENTATION.md`, `FULL_AR.md`, `LENSES_XR_SETUP.md`, `AR_ANIME_PRESENTATION.md`.

## One stack

```
DuelEngine  ──►  ArDuelInteractionSystem
                      │
     ┌────────────────┼────────────────┐
     ▼                ▼                ▼
ArFoundationSession  ArLensesSession  ArDuelSpace EditorSim
(ARCore phone)       (OpenXR Quest)   (PC / DIGITAL)
```

`ArDuelSpace` is the stage host. It picks a build method; it must not own rules.

| Piece | Class |
|---|---|
| Stage host | `ArDuelSpace` |
| Interaction | `ArDuelInteractionSystem` |
| Your disk / opp disk | `ArDuelDiskRig` × 2 |
| Physical cards on blades | `ArCardFieldController` |
| Hand (backs to opponent) | `ArHandVolume` |
| Midfield holos (no playmat mesh) | `ArArenaHologramManager` |
| Engine → visuals | `ArDuelSyncBridge` |
| Match params | `ArDuelMatchConfig` |

Disks start **retracted**. `BindEngine` / lenses bind plays **BladeDeploy**.

## Spatial model

| Element | Placement | Notes |
|---|---|---|
| Your disk | Left arm | M + S/T; sets face-down |
| Opponent disk | Their left arm on the shared stage | Same legality |
| Hand | Floating in front of the camera / head | Backs toward opponent |
| Card info | On select only | Local popup; never reveals hidden info |
| Arena | Ground-anchored midfield | Holos from face-up cards only |
| Mr. Referobot | NPC near arena | Referee / announcer — not a second engine |

Separation comes from `ArDuelMatchConfig.SeparationMeters` (create presets or `ArenaSurfaceScanner`). On lenses that value is **real meters**.

## Phone (S23) — Full AR

`FULL_AR.md`: the room is the arena. ARCore detects a horizontal plane; `FloorAnchor` + `ArenaAnchor` lock disks and holos. Passthrough is `ARCameraBackground`, not a `WebCamTexture` quad.

- Editor never starts `ArFoundationSession.ShouldUseOnDevice()` (`Application.isEditor` → false).
- AndroidManifest marks ARCore **optional** so DIGITAL still runs without Play Services for AR.
- Heat: no MSAA on the AR camera; drop holo fill before dropping tracking.
- Setup menu: **WRLDZ → Lab → Full AR — Setup ARCore + URP**. Console should log `WRLDZ_HAS_ARFOUNDATION=ON`.

## Lenses (Quest 3 / OpenXR)

`LENSES_XR_SETUP.md`: XR Origin 1:1 m, left controller = player disk, floor-anchored arena.

```
ArLensesSession (DontDestroyOnLoad)
  XR Origin
    Main Camera (CenterEye)
    Left / Right Controller
    FloorAnchor → ArenaAnchor → ArStage_Lenses
```

Phone camera does **not** validate arm anchoring or life-size scale. Do not ship a “lenses-only” gate.

## DIGITAL / EditorSim

Same `ArDuelInteractionSystem` with a simulated arm tracker. No passthrough. Used for F2P decline, Desktop Lab INSTANT DUEL, and this Cloud VM’s mental model (no Editor here — see `wrldz-cloud-verify`).

## Launch kinds (`ArDuelMatchConfig`)

Create menu, Tear, Hub, Practice, LabTest, NearbyChallenge, NpcStreet, TearBoss, RaidBoss, StoryEra, PvpZone, Tournament.

Street 4000 / 8000 LP, Tear boss 10–15k, Raid 20k+. `TomeLegal` only on Tear boss + Raid. `PossessionCinematic` on street NPCs. Raid engine is still **1v1 vs the boss**; `RaidPartySize` is recorded seats.
