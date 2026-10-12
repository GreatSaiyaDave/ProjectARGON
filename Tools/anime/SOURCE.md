# Tools/anime — provenance

These scripts were recovered 2026-10-12 from ephemeral Claude scratch space under
`/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/` and landed here so the
measurement pipeline is reproducible instead of living in `/tmp`.

## Original locations

| Landed here | Original path | Used for |
|---|---|---|
| `harvest.py`, `bodyharvest.py`, `hairscan.py`, `eyegrid.py`, `findeyes.py`, `eyecheck.py`, `refine.py`, `gesturescan*.py`, `gestwho.py`, `review.py`, `strip.py`, `grid1.py`, `history/*.sh` | `…/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad/anime/` | 2026-10-11 face + hair + gesture scans |
| `lbpcascade_animeface.xml` | `…/a69659a5-…/scratchpad/lbpcascade_animeface.xml` | anime **face** detector (OpenCV LBP cascade) |
| `grab.py`, `rank.py`, `scan.py`, `pair.py`, `zoomgrid.py`, `montage.py`, `meas.py`, `history/rankall.sh` | `…/138746c4-81e1-4dbe-b842-1cdaa6db0c59/scratchpad/dkb/meas/tools/` | Duelist Kingdom measurement (`MEASUREMENT_NOTES.md`) |
| `measured/hair_summary.json`, `measured/results.json`, `measured/jobs_*.json`, `MEASUREMENT_NOTES.md` | `…/138746c4-…/scratchpad/dkb/meas/` | the measured values + every box citation |

## Changes made on landing

- The cascade path in `harvest.py`, `bodyharvest.py`, `eyegrid.py`, `findeyes.py`,
  `gesturescan*.py` was `os.path.join(os.path.dirname(__file__), '..', 'lbpcascade_animeface.xml')`;
  the cascade now sits **in this folder**, so the `'..'` was dropped. No other edits.
- `history/*.sh` are kept verbatim (they still reference the old scratch paths); they document how
  the 2026-10-11 batches were driven, not a supported entry point.

## Not copied

Generated outputs (frame dumps, contact sheets, `rank/`, `station/`, `strips/`, `keys/`, `ep/`,
`dkframes/`, …) were left in `/tmp`. They are regenerable with the scripts above. The per-character
evidence frames that matter to the textures live outside the repo in
`~/argon-avatar/research/customization/evidence/dk_anime/`.
