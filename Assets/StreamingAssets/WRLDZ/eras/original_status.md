# Original pool status

Agents: read this file. Do **not** re-audit architecture or re-classify 1411 cards.
Product path: **B-staged** (supported pool = Structural + Implemented).
Link later: same model on the `vrains` band; C# is the only live authority.

| Field | Value |
|---|---|
| generatedUtc | 2026-09-09T16:54:54.2445730Z |
| compilerVersion | 77 |
| IsReleased(original) | False |
| Original total | 1411 |
| Structural | 305 |
| Implemented | 319 |
| Stub | 165 |
| Unimplemented | 622 |
| Ready | 624 (44.2%) |
| Blocking | 787 |
| LOB gaps | 10 |

Invariant: `total = structural + implemented + stub + unimplemented`.

## Sets

| Set | n | struct | impl | stub | unimpl | playable% |
|---|---:|---:|---:|---:|---:|---:|
| LOB | 126 | 82 | 34 | 3 | 7 | 92.1 |
| MRD | 144 | 66 | 36 | 6 | 36 | 70.8 |
| SRL | 104 | 23 | 31 | 7 | 43 | 51.9 |
| PSV | 105 | 26 | 24 | 6 | 49 | 47.6 |
| LON | 105 | 24 | 22 | 15 | 44 | 43.8 |
| LOD | 101 | 15 | 16 | 19 | 51 | 30.7 |
| PGD | 108 | 7 | 34 | 13 | 54 | 38.0 |
| MFC | 108 | 14 | 19 | 23 | 52 | 30.6 |
| DCR | 106 | 8 | 16 | 21 | 61 | 22.6 |
| IOC | 112 | 10 | 30 | 15 | 57 | 35.7 |
| AST | 112 | 11 | 19 | 11 | 71 | 26.8 |
| SOD | 60 | 7 | 16 | 8 | 29 | 38.3 |
| RDS | 60 | 6 | 10 | 11 | 33 | 26.7 |
| FET | 60 | 6 | 12 | 7 | 35 | 30.0 |
| TLM | 60 | 6 | 9 | 7 | 38 | 25.0 |

## LOB remaining (demo slice)

- `stub` 9076207 Armed Ninja (Flip Effect Monster)
- `stub` 15052462 Violet Crystal (Spell Card)
- `unimplemented` 23424603 Wasteland (Spell Card)
- `stub` 33066139 Reaper of the Cards (Flip Effect Monster)
- `unimplemented` 33396948 Exodia the Forbidden One (Effect Monster)
- `unimplemented` 50045299 Dragon Capture Jar (Trap Card)
- `unimplemented` 50913601 Mountain (Spell Card)
- `unimplemented` 82542267 Gravedigger Ghoul (Spell Card)
- `unimplemented` 83887306 Two-Pronged Attack (Trap Card)
- `unimplemented` 87430998 Forest (Spell Card)

Refresh: `Tools/original-pool-report.sh`
Capability slice: `/wrldz-original-capability` with `args.capability`.
