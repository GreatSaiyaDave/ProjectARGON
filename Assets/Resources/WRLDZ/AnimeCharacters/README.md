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
