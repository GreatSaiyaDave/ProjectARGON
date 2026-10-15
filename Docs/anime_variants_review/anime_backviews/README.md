# Anime back-view scan (Meshy input)

The Duelist Kingdom sets came from GravekeepersBrewer's DA gallery, which is **front-facing only**, so
every one of the 12 files in a set holds the same front render and a 3D reconstruction has no rear
information at all. The anime has it: this folder holds the shortlists of back-view candidates pulled
out of the local Duel Monsters episodes, for the owner to approve before they go to Meshy.

## How the scan works

`scripts/scan_anime_backviews.py` walks each episode **once** — it is character-independent, so one pass
over Season 1 serves all sixteen characters:

1. **harvest** — one frame per shot (`ffmpeg select=gt(scene,0.22)`), 1280×720 sources.
2. **cutout** — `rembg` with the **isnet-anime** model (trained on anime), largest connected blob, so
   one character rather than a crowd.
3. **signals** — cached per episode (`signals.jsonl` + `emb.npy`), so re-scoring a different character
   is instant and the model never runs twice for the same episode:

   | signal | what it says |
   |---|---|
   | `sim` | CLIP cosine against the target character's reference art (DA render + wiki stills + anime refs) |
   | `margin` | `sim` minus the best score of any *other* character — identity, not just composition. A title card scores 0.85 against everything, so only the relative comparison means anything. Pools are capped to the same size per character or the side with more references wins on the maximum alone |
   | `skin` | skin-toned fraction of the head band (top 28% of the box). A face is a large skin blob; from behind the head is hair. **This is the back-view signal** |
   | `aspect` `fill` `h` | box height/width, mask area over box area, box height as a fraction of the frame. A standing figure measures ≈2.9 / ≈0.5 (the DA renders); logos and text cards ≈1.3 / ≈0.9 — this is what keeps the title card out |

4. **rank** — `sim × (1 − skin) × min(1, h/0.6)`: recognisable, seen from behind, big enough to cut
   cleanly. The scan cannot see the image, so it shortlists and shows.

## Reading a sheet

`<character>_backview_candidates.jpg` — each cell is the **cutout** (what Meshy would receive) with the
**source frame** inset bottom-right so the pose can be judged, not just the silhouette. The caption is
`episode time sim margin skin%`.

## What to pick

For each character, the frames that read as *them, from behind*: standing, unoccluded, whole figure in
shot, no second character overlapping. Two or three per character is plenty — Meshy takes up to four
views, and front + back is the pair that carries the most information.

## Status

| step | state |
|---|---|
| pilot: `yugi`, episodes 001/003/033 | **15 candidates** — `yugi_pilot/`, awaiting the owner's pick |
| full Season 1 pass (52 episodes, character-independent) | running → `~/argon-avatar/research/anime_variants/backview_scan/` |
| score the other 15 characters from that pass | after the pass: `--select --character <id>` |
| Meshy `multi-image-to-3d` per character (front = DA render + approved back) | not started — 30 credits each, 3009 available |

## Re-running

```bash
# one pass over episodes (character-independent, cached)
python3 scripts/scan_anime_backviews.py --episodes auto --out DIR

# shortlist any character from the cache (instant)
python3 scripts/scan_anime_backviews.py --character kaiba --out DIR --select --min-margin -0.10
```
