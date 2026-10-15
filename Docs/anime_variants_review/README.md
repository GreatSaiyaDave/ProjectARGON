# Season 1 (Duelist Kingdom) outfit variants — review folder

Goal: build 2D anime outfit variants for the 16 canon duelists, starting with
Season 1 (Duelist Kingdom, episodes 1–49), by **extracting from the anime itself**.

This folder is the human-review stage. The assistant cannot see images, so the workflow is:

1. The assistant extracts candidate character cutouts from every S1 episode and lays them
   out as **review sheets** below.
2. **You** look at the sheets and name the frame(s) you want, e.g. `Mako -> s1e007:f29`.
   You can also say "Mako looks wrong here" and it gets re-checked.
3. The assistant rebuilds the 12-file sprite set from your pick and wires it into the game.

## What to open

| File | What it is |
|---|---|
| `by_character/<cid>.jpg` | **Start here.** One sheet per character: every tile the matcher labelled as that character, best confidence first. `slug:fFRAME` + `m<margin>` captions. |
| `heroes/<cid>.jpg` | **Hero focus** (Yami Yugi, Joey, Tea, Tristan, Yami Bakura). Every candidate ranked by similarity to that hero — a *superset* to scan and pick from, not an argmax label. Best first. |
| `fullframes/<cid>.jpg` | **Full-frame references.** Contact sheet of whole 1280×720 source frames per character (`fullframes/<cid>/` holds the individual frames + `index.csv`). |
| `normals/<cid>.jpg` | **2D-in-3D check.** Left→right: original sprite · the generated normal map · the same sprite relit from the left / right / top, using the normal map. This is the form the cel shader now lights in-game. |
| `by_episode/<slug>.jpg` | Per-episode sheet. Header = episode title + cast (from the wiki). Each tile shows the anime frame + the cutout + label. |
| `CHARACTER_INDEX.md` | Character → episodes it appears in + the tiles labelled for it. |
| `OUTFITS.md` | Each character's Season-1 (Duelist Kingdom) outfit, scraped from the wiki. |
| `ALL_00..10.jpg` | Flat overview of all 412 tiles (objective labels only). |
| `dm_s1eNNN/` | Raw per-episode data: `candidates.tsv`, `cand_*.png` (cutouts), `kf/` (source frames), `sheet.jpg`. |

## How to name a frame

`<character> -> s1e<NNN>:f<FRAME>`, for example:

```
Mako  -> s1e007:f29
Kaiba -> s1e024:f218
Tea   -> s1e007:f40
```

`f<FRAME>` is the frame index shown on the **left** of each tile (also the 4th column,
`frame`, of `candidates.tsv`). Give several picks per character if you like; the first
usable full-body standing frame becomes the outfit variant.

## How the labels were produced (and how far to trust them)

- Cutouts come from a two-phase `rembg` (`u2netp`) extractor over all 49 episodes → **412 candidates**.
- Labels come from a **CLIP ViT-B/32** image embedding (ONNX), gallery = each character's
  **Duel Links art + anime portrait**, with flip test-time augmentation.
- Validated on **independent anime images**: **74% top-1 / 80% top-3**. Earlier attempts
  (colour histograms, DINOv2, cast-constrained histograms) were all worse and were dropped.
- Ranking is restricted to the episode's **cast**, and a **confidence margin** gates the label:
  - **green `Name`** = strong (margin ≥ 0.05) — most reliable
  - **amber `Name?`** = weak (margin ≥ 0.03) — a hint, verify visually
  - **grey `?`** = not confident (≈ 2/3 of tiles) — pick from the episode's cast yourself
- So labels are a **navigation aid, not ground truth**. The `by_character` sheets are the
  fastest way to find a character's best frames, especially for characters who appear in
  only a few episodes (Weevil, Rex, Mako, Keith, Mai).

## Hero focus (Yami Yugi, Joey, Tea, Tristan, Yami Bakura)

`heroes/<cid>.jpg` ranks **all 412 candidates** by similarity to that hero (CLIP gallery =
Duel Links art + anime portrait + solo anime refs). Because the hero set is small and the
five looks are distinct, this is the fastest way to find each hero's frames — but the
ranking cross-contaminates (a hero's list can contain another hero), so treat it as a
**superset to scan**, not a verdict.

Validated accuracy for these five (leave-one-out over independent anime images):
~73% hero-to-hero. Hair colour is very discriminative for this group (Yami black+magenta,
Joey blonde, Tea warm brown, Tristan neutral brown, Bakura white) — useful when you are
eyeballing the sheets, but it did not improve the automatic matcher.

## Full-frame references (`fullframes/`)

Whole 1280×720 source frames for game development, ranked per character by CLIP similarity,
top-ranked first. One folder per character with the individual `.jpg` frames + `index.csv`
(`file, episode, frame, similarity`) and a contact sheet `fullframes/<cid>.jpg`.

| Character | Source | Frames |
|---|---|---|
| Yami Yugi, Joey, Tea, Tristan, Ryo Bakura | extractor candidates | top 60 each |
| Yami Bakura | **all** e013/e014 keyframes (the ring spirit's Shadow Duel), figure-filtered | top 80 |

`fFRAME` in a filename maps to the extractor's `frame` column; the source file is
`dm_s1eNNN/kf/k_<FRAME+1:05d>.jpg`. Yami Bakura is separated from Ryo Bakura by episode:
e013 "Evil Spirit of the Ring" / e014 are the possessed (spirit) appearance, everything else
Bakura is treated as Ryo. As everywhere in this folder, ranking is a **navigation aid** — the
lists cross-contaminate, so confirm the character by eye.

## 2D art that exists in 3D (form map)

Every character sprite now has a matching **form map** at
`Assets/Resources/WRLDZ/AnimeCharacters/<id>/<id>_normal.png`: `RGB` = a tangent-space
normal, `A` = depth. It is derived from the flat art, so it is deterministic and offline.

`AnimeCelShader` samples it so the cel terminator follows the **body** instead of the whole
billboard, and the depth channel parallaxes the sprite with the camera. `AnimeCharacterActor`
loads it automatically per set; `SetFormLighting(strength, parallax)` tunes it. With no form
map the sprite stays flat (the old behaviour) instead of breaking.

### Form map v2 (the Duelist Kingdom sets)

The original `build_normal_depth.py` takes the distance transform of the silhouette. Measured
against the shader's maths, that field is almost **flat across the body** — its gradient only
spikes within a few pixels of the edge — so the sprite was *always* uniform: **0% of the figure
in shadow under a frontal or sunset key, 78% under a backlight** (uniformly flat either way,
never a terminator crossing the body).

`build_formmap_v2.py` (default: every `*_da` set) rebuilds it from the body's **local run
lengths**, so each blob — torso, arm, leg — is its own cylinder and the curvature keeps growing
across the whole body instead of dying at a ridge. `PHI = 1.40` caps the silhouette tilt at
~49°, deliberately short of 90° so the edge never darkens into an ink outline.

The form map alone was still not enough: a camera-facing plane under a frontal key is lit
uniformly no matter what the normals say. `AnimeToon.hlsl` therefore gained
`AnimeSpriteKeyDir()` — a fixed lateral portrait key from camera-left — and `AnimeCelShader` a
`_KeyBias` property (`0` = portrait key, `1` = the plain scene key, the default, so **every
pre-existing set is unchanged**). The DK sets set it to `0.15` in
`AnimeCharacterActor.ApplyFormMap()` along with `SetFormLighting(1f, 0.03f)`, which keeps the
world light influencing the shot without letting it collapse back to frontal.

**Billboard fix (required before any of this could show).** The sprite quad is Unity's
built-in `Quad`, which faces **-Z**, and `AnimeCharacterActor.Billboard()` turns it toward the
camera — so the game was rendering the quad's **back**: the art was mirror-flipped in world
*and* the mesh normal pointed away from the viewer, inverting the sign of every form-map
pixel. Replaying the shader maths against the shipped maps (`scripts/qc_keyframe.py`) that put
**53% of a DK figure in the shadow band (83% of its right third)**, and every one of the
original 16 characters at **100% shadow under a frontal sun and 0% under a backlight** — lit
backwards. The quad is now turned 180° (`localRotation = Euler(0, 180, 0)`), which un-mirrors
the art, points `normalWS` at the camera and leaves the parallax (and every number below) as
measured. `AnimeSpriteKeyDir` also now forms its side vector as `cross(up, v)`, which is
genuinely camera-left — the earlier `cross(v, up)` was camera-right and would have dropped the
terminator on the mirrored side of the sheets.

Measured across five scene lights, share of the figure in the shadow band:

| configuration | frontal | overhead | light from right | backlight | sunset |
|---|---|---|---|---|---|
| **shipped** (v2 map + `_KeyBias` 0.15) | 14–19% | 21–24% | 16–20% | 34–38% | 24–27% |
| portrait key only | 29–33% | | | | |
| old scene key only | **0%** | 18% | 18% | 78% | **0%** |

Full per-character numbers: `normals/formmap_v2_report.json`.

Also fixed for the DK sets: their texture importer settings had fallen back to Unity defaults
(mipmaps **on**, `alphaIsTransparency` **off**, wrap **repeat**). Mipmapped alpha makes a
`clip(a-0.5)` cutout dissolve and shimmer at distance, and without de-fringing the edge keyed
from black leaves a dark fringe that reads as an ink outline. All 180 DK colour sprite `.meta`
files now match the base sets exactly (mip off, clamp, `alphaIsTransparency` on, max 1024).

`normals/<cid>_da.jpg` shows it: **original · normal map · OLD scene key · shipped key** — the
last two tiles are the same art under the old and the new key, so the difference is visible in
one row.

## Not in Season 1

Marik, Ishizu and Odion debut in later arcs (Battle City onward), so there are no S1 frames
for them; their variants arrive with later seasons.

## Scripts

- `scrape_cast.py` – per-episode cast + titles (Yugipedia).
- `scrape_outfits.py` – per-character Season-1 outfit descriptions → `outfit_meta.json`.
- `scrape_anime_refs.py` – extra anime reference images (used while validating the matcher).
- `emblib.py` + `label_candidates.py` – the CLIP matcher / labeller that writes `guess4`,
  `conf`, `margin` into every `candidates.tsv` and rebuilds the two sheet folders.
- `hero_sheets.py` – per-hero ranked contact sheets (`heroes/`).
- `export_fullframes.py` – full-frame reference sets (`fullframes/`) from the candidates.
- `yami_extra.py` – expands Yami Bakura by scanning all e013/e014 keyframes.
- `build_normal_depth.py` (parent folder) – generates the per-character depth+normal form maps
  for the original 16 sets. Superseded for the DK sets by the next line.
- `build_formmap_v2.py` (parent folder) – form map v2 for the `*_da` sets (local-cylinder normals),
  regenerates `<id>_normal.png`, the `normals/<cid>_da.jpg` sheets and `formmap_v2_report.json`,
  and re-checks the shading maths offline. Pass ids to rebuild only those.
- `qc_keyframe.py` (parent folder) – replays `AnimeCelShader`'s exact key geometry against the
  shipped 512px maps: validates `span_u` against an independent implementation, checks the stored
  `nx` sign at both silhouette edges, and scores the three candidate key geometries (post-fix,
  pre-fix, quad-flip-without-the-key-flip). Writes `normals/qc_keyframe.json`. This is the check
  that found the billboard bug.
- `qc_base.py` (parent folder) – the same replay for the original 16 base sets (v1 maps, strength
  0.8, pure scene key), before vs after the billboard fix. Writes `normals/qc_base.json`.
- `assemble_variant.py` (parent folder) – turns one picked cutout into the 12-file sprite set.
