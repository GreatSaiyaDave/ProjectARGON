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

The following 16 characters are registered in `AnimeCharacterCatalog.cs`, each with a complete 12-file sprite set:

| ID | Display Name | Height (m) | Front Source | Angle Sprites | Expressions | Gestures |
|----|-------------|------------|--------------|--------------|-------------|----------|
| yami_yugi | Yami Yugi | 1.58 | Blender (Atem GLB) | Atem GLB render | DL cutin 001-003 | DL chara007/chara050 |
| kaiba | Seto Kaiba | 1.86 | Blender (Atem color-shift) | Atem GLB rendered, color-shifted blonde/white | DL cutin 001-003 | DL chara007/chara050 |
| joey | Joey Wheeler | 1.78 | Blender (Duelist_Ready GLB) | Duelist_Ready GLB render | DL cutin 001-003 | DL chara007/chara050 |
| mai | Mai Valentine | 1.70 | DL `T_chara002` (sn0004) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| marik | Marik Ishtar | 1.77 | DL `T_chara002` (sn0010) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| bakura | Yami Bakura | 1.76 | DL `T_chara002` (sn0012) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| pegasus | Maximillion Pegasus | 1.90 | Face texture composite (sn0016 missing from DL) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| tea | Téa Gardner | 1.64 | DL `T_chara002` (sn0009) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| weevil | Weevil Underwood | 1.52 | DL `T_chara002` (sn0007) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| rex | Rex Raptor | 1.70 | DL `T_chara002` (sn0008) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| mako | Mako Tsunami | 1.83 | DL `T_chara002` (sn0005) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| tristan | Tristan Taylor | 1.83 | DL `T_chara002` (sn0006) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| mokuba | Mokuba Kaiba | 1.45 | DL `T_chara002` (sn0011) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| ishizu | Ishizu Ishtar | 1.67 | DL `T_chara002` (sn0014) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| odion | Odion | 1.75 | DL `T_chara002` (sn0013) | Color-shifted Atem | Face texture crop | Color-shifted Atem |
| bandit_keith | Bandit Keith | 1.85 | DL `T_chara002` (sn0015) | Color-shifted Atem | Face texture crop | Color-shifted Atem |

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
