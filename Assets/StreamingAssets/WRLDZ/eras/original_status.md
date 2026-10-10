# Original pool status

Agents: read this file. Do **not** re-audit architecture or re-classify 1411 cards.
Product path: **B-staged** (supported pool = Structural + Implemented).
Link later: same model on the `vrains` band; C# is the only live authority.

| Field | Value |
|---|---|
| generatedUtc | 2026-09-23T09:43:59.0551910Z |
| compilerVersion | 102 |
| IsReleased(original) | False |
| Original total | 1411 |
| Structural | 309 |
| Implemented | 434 |
| Stub | 152 |
| Unimplemented | 516 |
| Ready | 743 (52.7%) |
| Blocking | 668 |
| LOB gaps | 0 |

Invariant: `total = structural + implemented + stub + unimplemented`.

## Sets

| Set | n | struct | impl | stub | unimpl | playable% |
|---|---:|---:|---:|---:|---:|---:|
| LOB | 126 | 82 | 44 | 0 | 0 | 100.0 |
| MRD | 144 | 70 | 68 | 0 | 6 | 95.8 |
| SRL | 104 | 23 | 43 | 7 | 31 | 63.5 |
| PSV | 105 | 26 | 27 | 6 | 46 | 50.5 |
| LON | 105 | 24 | 24 | 16 | 41 | 45.7 |
| LOD | 101 | 15 | 17 | 19 | 50 | 31.7 |
| PGD | 108 | 7 | 37 | 13 | 51 | 40.7 |
| MFC | 108 | 14 | 23 | 22 | 49 | 34.3 |
| DCR | 106 | 8 | 21 | 21 | 56 | 27.4 |
| IOC | 112 | 10 | 39 | 14 | 49 | 43.8 |
| AST | 112 | 11 | 28 | 11 | 62 | 34.8 |
| SOD | 60 | 7 | 26 | 6 | 21 | 55.0 |
| RDS | 60 | 6 | 18 | 11 | 25 | 40.0 |
| FET | 60 | 6 | 19 | 6 | 29 | 41.7 |
| TLM | 60 | 6 | 9 | 7 | 38 | 25.0 |

Refresh: `Tools/original-pool-report.sh`
Capability slice: `/wrldz-original-capability` with `args.capability`.
