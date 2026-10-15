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

## Not in Season 1

Marik, Ishizu and Odion debut in later arcs (Battle City onward), so there are no S1 frames
for them; their variants arrive with later seasons.

## Scripts

- `scrape_cast.py` – per-episode cast + titles (Yugipedia).
- `scrape_outfits.py` – per-character Season-1 outfit descriptions → `outfit_meta.json`.
- `scrape_anime_refs.py` – extra anime reference images (used while validating the matcher).
- `emblib.py` + `label_candidates.py` – the CLIP matcher / labeller that writes `guess4`,
  `conf`, `margin` into every `candidates.tsv` and rebuilds the two sheet folders.
- `assemble_variant.py` (parent folder) – turns one picked cutout into the 12-file sprite set.
