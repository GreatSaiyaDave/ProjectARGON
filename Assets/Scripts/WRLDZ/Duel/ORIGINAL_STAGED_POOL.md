# Original staged pool (Path B)

**Read `StreamingAssets/WRLDZ/eras/original_status.md` before classifying anything.**
Do not re-audit architecture. Do not re-run 1411-card classify in chat.

## Product

- **Original** is a historical *card pool* (LOB→FET, pre-TLM), not a historical-rules simulation.
- Text is current TCG `cards_db.json` `desc`, except the two snapshots in `eras/text/original.json` (Crush Card linger, Magical Hats anime).
- Supported pool = `CardEffectStatus` Structural + Implemented only.
- Stub and Unimplemented stay in the catalog, cannot enter live constructed decks, never resolve as silent vanilla.
- `ErazFormat.IsReleased("original")` stays false until in-product “supported cards” copy exists. Lab/starter still bypasses the wall.

## Live authority

C# `DuelEngine` + `TextEffectRuntime` + `OfficialEffectRegistry` is the only product rules path.
ocgcore / Lua are lab and lint. Never a live fallback.

## Future Link (VRAINS)

Same model, later band:

1. `eraz_eras.json` `vrains` / `modern` already list `link` in `extraKinds`.
2. `SummonProcedures` refuses unregistered Link recipes.
3. Ship a `supported_<era>.json` of Structural + Implemented passcodes.
4. Do **not** wait for Original 1411/1411, or for GX/5Ds/Zexal/ARC-V 100%.

Path A (every Original card Implemented before any later extra-kind) is rejected.

## How to add cards (low tokens)

```
Tools/original-pool-report.sh          # refresh snapshot if Editor is up
# then implement ONE capability from original_capability_backlog.md
# or: /wrldz-original-capability capability=lob
```

Prefer a compiler template + regression. UniqueException only when no other card shares the mechanic.
Bump `CardTextEffectCompiler.Version` when templates change. Proof: `wrldz_tcg_tests` then the report diff.
