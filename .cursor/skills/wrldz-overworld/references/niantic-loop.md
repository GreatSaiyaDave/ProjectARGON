# Niantic loop (map · Tears · raids · rooms)

In-repo: `OVERWORLD_ZONES.md`, `FLOW.md`, `MapZoneService`, `MapZoneCatalog`, `OverworldBootstrap`, `OverworldUI`.

## Core loop

```
Overworld GPS map (or WASD walk pad)
        │  approach pin / NPC
        ▼
Zone Mode prompt (in-range only)
   [ENTER AR] [DIGITAL] [Cancel]
        ▼
DuelSlice (same DuelEngine)
        ▼
Rewards → Overworld
```

`OverworldBootstrap` requests lab permissions, then `OverworldUI.Build()`. Indoor / no GPS falls back to the walk pad (`WrldzLab`).

## Pins vs not-pins

| Kind | Map pin? | Job |
|---|---|---|
| Tear | Yes | Harvest SE + set orb. Street NPCs. Boss after 3 straight 8000 LP wins around that Tear. |
| Raid | Yes | 20k+ LP boss. Design lock 3-on-1; now SOLO or 3-ON-1 AI seats. Tome legal. Engine still 1v1 vs boss. |
| Bazaar | Yes | Economy only (buy/sell, altar, tablets). Hub Bazaar UI may still be a stub. |
| Training | Yes | Practice. 0 XP. |
| Story | Yes | Umbrax era missions. LOB / MRD / SRL start unlocked. |
| PvP | Yes | Ranked. No SE. Set orbs through L50. |
| Tournament | **No** | Rooms from Eye TOURNEY. 4- or 8-seat. Lab FILL AI. |
| Street NPC | **No** | Wander around Tear radius. Tap → Possession → duel. |

Anchor pins may toast-only (stub). Do not invent a seventh pin type without updating `MapZoneCatalog` + this table.

## Street NPCs

- Spawn at Tear points. Ruralness = fewer Tears within 1.2 km → larger wander radius.
- Count and 8000 LP mix rise as the player walks closer.
- Must win **one 4000 LP street duel every 24 hours** to keep 8000 LP access.
- `MapZoneService.MakeStreetNpc` / `MakeTearConfig` / `MakeTrainingConfig` fill `ArDuelMatchConfig`.

## Tear harvest

`MapZoneService.TryLootGrant` — small SE, daily soft lock per pin (`PlayerPrefs` date key). Harvest is not a pack opening.

## VS PLAYER

Distance scan (`PlayerDistanceScanner` / GPS marks) → `ArDuelOpponentKind.NearbyPeer` or local pass. Separation meters drive arena layout (`wrldz-ar-lenses`).

## Code entry points

| Concern | Type |
|---|---|
| Scene boot | `OverworldBootstrap` |
| Map + Eye + HUD | `OverworldUI` |
| Pin catalog | `MapZoneCatalog` |
| Loot / match configs | `MapZoneService` |
| GPS / walk | `WrldzLab`, `WrldzInput` |
| Zone prompt | `ZoneModePrompt` |
