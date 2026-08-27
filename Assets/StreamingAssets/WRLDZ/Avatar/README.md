# Spirit Dueler avatars

## HQ anime faces (primary)

`portraits/portrait_0.png` … `portrait_7.png` — Grok Imagine anime human duelists (512² RGBA, chroma-keyed).

| Index | Name |
|------:|------|
| 0 | Midnight Ace |
| 1 | Academy Belle |
| 2 | Street Runner |
| 3 | Neon Spark |
| 4 | Ivory Phantom |
| 5 | Gold Braid |
| 6 | Cap Focus |
| 7 | Teal Spirit |

Selected via `AvatarAppearance.portraitIndex` · customizer **Look** chip.

## Legacy layered (fallback only)

Used only if a portrait file is missing:

| Files | Options |
|-------|---------|
| body_0..2 | Lean, Broad, Balanced |
| hair_0..3 | Short, Spiky, Long, Cap |
| eyes_0..2 | Cool, Warm, Focus |
| outfit_0..3 | Jacket, School, Duel Coat, Casual |
| acc_0..3 | None, Goggles, Earring, Scarf |
| frame_portrait.png | Profile chrome |

Tinted at runtime via Image.color (skin / hair / outfit / accent).
