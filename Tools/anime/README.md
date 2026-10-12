# Tools/anime — anime-frame measuring toolchain

Extract frames from the owner's Yu-Gi-Oh episode library and **measure** the exact colours the
canon duelists are drawn in. These numbers drive the avatar **texture repaints**
(`Assets/Resources/WRLDZ/Avatar3D/{Faces,Hair,Clothes}`) and the hair/skin hexes in
`Assets/Scripts/WRLDZ/Data/CharacterLooks.cs`.

**Rule (owner, 2026-10-11): "Do not make stuff up. Precision and accuracy are vital."**
Every colour that lands in a texture cites a frame + box + kept pixels, produced by `meas.py`.

## Source of truth

- Episodes: `~/Downloads/YouTube Downloader/*.mp4` (202 files, S1–S3 + GX), 1280×720.
- Evidence crops: `~/argon-avatar/research/customization/evidence/dk_anime/` (per-character frames).
- Measured results: `measured/hair_summary.json` (per character, lit/shade hex + pixel counts + spread),
  `measured/results.json` (raw `meas.py` output), `measured/jobs_*.json` (the boxes measured).
- Method + every citation: `MEASUREMENT_NOTES.md`.

## Setup

```bash
Tools/anime/setup.sh          # makes Tools/anime/.venv (numpy + pillow + opencv)
```

Run anything through the wrapper:

```bash
Tools/anime/animepy meas.py measured/jobs_h1.json out/results.json out/ev
```

`meas.py`, `grab.py`, `scan.py`, `pair.py`, `zoomgrid.py`, `montage.py` need only numpy + pillow;
the face/hair scripts add opencv. If `.venv` is absent, `animepy` falls back to `python3`.

## The scripts

| Script | What it does |
|---|---|
| `grab.py <ep> <t> <out.png>` | one full-res 1280×720 frame at time `t` (ffmpeg) |
| `rank.py <ep> <t0> <t1> <outdir> name=h0:h1,s0:s1,v0:v1 …` | 1 fps decode; top 24 frames per named HSV window → contact sheet |
| `scan.py <ep> <t0> <t1> <step> <out.png>` | contact sheet every `step` seconds (find shots) |
| `harvest.py "<ep sub>" start dur fps <outdir> [min_face]` | anime-face crops + `index.json` + `sheet.png` |
| `bodyharvest.py "<ep sub>" start dur fps <outdir> [minf] [maxf]` | face + the body under it (outfit references) |
| `hairscan.py "<ep sub>" t0 t1 r,g,b tol <outdir>` | rank frames by the largest blob of a hair/hat colour |
| `eyegrid.py f.png …` | a frame's eye band at 1:1 with a labelled 20 px grid |
| `findeyes.py f.png …` | auto-find the two pupils (`lx ly rx ry`) + a zoom sheet |
| `eyecheck.py f.png lx ly rx ry` | 3× zooms of guessed pupils with offsets, to correct a misread |
| `refine.py f.png lx ly rx ry [r]` | snap pupil guesses to the sclera/iris/pupil centroid |
| `meas.py <jobs.json> <results.json> <evidence_dir>` | **the sampler**: flat + HSV-class filtered lit/shade fills |
| `pair.py`, `zoomgrid.py`, `montage.py` | evidence contact sheets |
| `gesturescan*.py`, `gestwho.py`, `review.py`, `strip.py`, `grid1.py` | gesture/clip scanning (same family; not texture work) |

`history/` holds the original batch drivers (`batch*.sh`, `hairbatch.sh`, `dkbatch.sh`, `gestbatch*.sh`).

## The measurement loop

1. `scan.py` / `rank.py` to find a clean, well-lit shot of the character.
2. `grab.py` the full-res frame.
3. `eyegrid.py` / `findeyes.py` / `refine.py` to nail a face anchor (for face decals).
4. Write a job `{frame, box, h, s, v, label}` (see `measured/jobs_h1.json`) over the hair/cloth region.
5. `meas.py` → per-tone median hex, pixel counts and a lit/shade evidence image.
6. Fold the median into `measured/hair_summary.json` / `results.json` and cite it in the repaint.

Provenance of every file (original paths, why it exists): `SOURCE.md`.
