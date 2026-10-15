# Anime Characters — Sprite Contract

This directory contains 2D anime character sprites for Project ARGON. All sprites follow a strict naming and art-style contract to ensure consistency across the project.

## Art Style

- **Format:** PNG textures with alpha channel
- **Style:** Cel-shaded anime art
- **Outlines:** No ink outlines — use soft shading and color transitions instead
- **Resolution:** Power-of-two dimensions required
  - **Recommended:** 1024×1024 or 2048×2048
  - All sprites for a given character should use the same resolution

## Directory Structure

Each character lives in its own subdirectory named by the character's unique ID:

```
AnimeCharacters/
├── {id}/
│   ├── {id}_front.png
│   ├── {id}_34.png
│   ├── {id}_side.png
│   ├── {id}_back.png
│   ├── expr_neutral.png
│   ├── expr_serious.png
│   ├── expr_smiling.png
│   ├── gesture_draw.png
│   ├── gesture_summon.png
│   ├── gesture_attack.png
│   ├── gesture_defeat.png
│   └── gesture_idle.png
└── README.md
```

## Angle Sprites

Each character must provide four directional angles:

| File | Description |
|------|-------------|
| `{id}_front.png` | Character facing the viewer (front view) |
| `{id}_34.png` | Character at a three-quarter angle |
| `{id}_side.png` | Character in profile (side view) |
| `{id}_back.png` | Character facing away from the viewer (back view) |

## Expression Overlays

Expression overlays are composited on top of the base angle sprite to convey emotion:

| File | Description |
|------|-------------|
| `expr_neutral.png` | Default / neutral expression |
| `expr_serious.png` | Serious / determined expression |
| `expr_smiling.png` | Smiling / happy expression |

## Gesture Overlays

Gesture overlays are composited on top of the base angle sprite to convey action poses:

| File | Description |
|------|-------------|
| `gesture_draw.png` | Drawing a card from the deck |
| `gesture_summon.png` | Summoning a monster |
| `gesture_attack.png` | Attacking / battle stance |
| `gesture_defeat.png` | Defeated / knocked down pose |
| `gesture_idle.png` | Idle / waiting pose |

## Guidelines

- All sprites for a character must share the same canvas dimensions and pivot point.
- Overlays (expressions and gestures) should be drawn on a transparent background and aligned to the base sprite.
- Keep file sizes optimized — use indexed color or compressed PNG where possible.
- Do not mix resolutions within a single character's sprite set.

## Character Catalog

The following 16 characters are registered in `AnimeCharacterCatalog.cs`, each with a complete 12-file sprite set.
Every sprite is the character's **official Duel Links `T_chara002` standing art** (500×960), scaled and
placed on a 1024×1024 canvas with the feet on the ground line.

The `sn####` source is verified against the DL `charaname` plates (hair colour + body height). Earlier
builds of this folder used a rendered 3D Atem base plus colour transfer and, worse, mapped several
characters to the **wrong** DL source (Tristan showed Yugi, Keith showed Pegasus, Mokuba showed Yami
Bakura, Téa showed Mako, Mako showed Téa, Bakura showed Keith, Ishizu showed Odion, Odion showed Ishizu).
That is fixed here.

| ID | Display Name | Height (m) | Source (Duel Links) |
|----|-------------|------------|---------------------|
| yami_yugi | Yami Yugi | 1.58 | sn0001 `T_chara002` |
| kaiba | Seto Kaiba | 1.86 | sn0002 `T_chara002` |
| joey | Joey Wheeler | 1.78 | sn0003 `T_chara002` |
| mai | Mai Valentine | 1.70 | sn0004 `T_chara002` |
| tea | Téa Gardner | 1.64 | sn0005 `T_chara002` |
| weevil | Weevil Underwood | 1.52 | sn0007 `T_chara002` |
| rex | Rex Raptor | 1.70 | sn0008 `T_chara002` |
| mako | Mako Tsunami | 1.83 | sn0009 `T_chara002` |
| marik | Marik Ishtar | 1.77 | sn0010 `T_chara002` |
| bakura | Yami Bakura | 1.76 | sn0011 `T_chara002` |
| bandit_keith | Bandit Keith | 1.85 | sn0012 `T_chara002` |
| ishizu | Ishizu Ishtar | 1.67 | sn0013 `T_chara002` |
| odion | Odion | 1.75 | sn0014 `T_chara002` |
| pegasus | Maximillion Pegasus | 1.90 | sn0015 `T_chara002` |
| mokuba | Mokuba Kaiba | 1.45 | sn0017 `T_chara002` |
| tristan | Tristan Taylor | 1.83 | sn0029 `T_chara002` |

> **Note on the angle / expression / gesture slots.** `AnimeCharacterActor` keeps its billboard facing the
> camera, so the camera always resolves to the *front* angle — the `_34`/`_side`/`_back` slots are never
> displayed and reuse the front art. `SetExpression` and `PlayGesture` swap the whole base texture, so the
> `expr_*` and `gesture_*` slots also reuse the standing art until per-expression / per-pose art exists
> (the DL character packs only ship one full-body pose each for these characters). This keeps the correct
> character on screen in every state instead of falling back to a blank or a floating face.

Rebuild: `python3 ~/argon-avatar/research/build_dl_sprites.py`.

## Integration

The `AnimeCharacterActor` is integrated into the overworld token system via:

1. **`OverworldAvatar3d.AttachWorld()`** — When a canon character's FBX model is missing, falls back to `AnimeCharacterCatalog.Find(characterId)` and creates a 2D billboard actor.
2. **`CharacterLooks.cs`** — Each canon character look entry sets `characterId` to the matching catalog ID (e.g., `"kaiba"`, `"joey"`, `"yami_yugi"`), enabling the fallback.
3. **`ArOpponentDuelist`** — Detects the anime actor after `AttachWorld` returns null, skips 3D skeleton/IK setup, and routes duel beat gestures (`attack`, `activate`, `summon`, `defeat`) to `AnimeCharacterActor.PlayGesture()`.
4. **`AnimeCharacterActor.cs`** — Billboard quad that:
   - Switches between 4 angle sprites based on camera azimuth
   - Applies expression overlays (neutral/serious/smiling)
   - Plays gesture overlays (draw/summon/attack/defeat/idle) on beat
   - Simulates idle breathing via sine-wave scale modulation
   - Renders a ground shadow blob

## Gesture Mapping

Duel beats from `ArOpponentDuelist.Play()` map to anime gestures:

| Duel Beat | Anime Gesture |
|-----------|--------------|
| `attack` | attack |
| `defeat` | defeat |
| `hit_big` | defeat |
| `activate` | draw |
| `reveal` | summon |
| `summon_big` | summon |
| `start`, `turn`, `hit`, `wait`, `victory` | idle |
