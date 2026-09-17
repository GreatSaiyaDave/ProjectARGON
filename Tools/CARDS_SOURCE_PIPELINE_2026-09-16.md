# Cards Source Pipeline — 2026-09-16

Dave / Dilbot / Fanbot agreed local workflow for ProjectARGON (no Cursor until 10/6).

## Purpose

Editable official-card source → fail-closed validate → `cards_db.json` → existing Phase-1 bulk compile.

## Paths (on greatsaiyadave-PC)

| Role | Path |
|------|------|
| Project root | `/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON` |
| Editable source | `Tools/cards_source.json` |
| Compiler | `Tools/compile_cards.py` |
| Export helper | `Tools/export_cards_source.py` |
| Production DB | `Assets/StreamingAssets/Cards/cards_db.json` |
| Phase 1 | `Tools/WRLDZ/phase1_bulk_compile.py` (confirmed present) |
| Docs (this note) | `Tools/CARDS_SOURCE_PIPELINE_2026-09-16.md` **or** `~/backup-bot/Docs/CARDS_SOURCE_PIPELINE_2026-09-16.md` |

## Schema (CardDatabase / CardDef)

Top-level: `{"cards":[...]}` (not a bare list).

Per-card fields (do **not** thin):

`id`, `name`, `type`, `frameType`, `desc`, `atk`, `def`, `level`, `race`, `attribute`, `archetype`

**Official print text = `desc`** (see `OfficialCardAuthority` / `CardDef`).  
Source may use alias `text`; `compile_cards.py` maps `text` → `desc`. Production artifact always uses `desc`.

## Fanbot oracle gate (fail-closed before FC)

1. Required: `id`, `name`, `type`, non-empty official text (`desc` / `text`).
2. Reject obvious stubs: empty, `TODO`/`TBD`/`placeholder`, bracket placeholders, truncated `...` crumbs, absurdly short effect/S-T text.
3. Export from `cards_db.json` preserves **exact** card records (no invented text).
4. Optional Konami/DB cache compare (`Tools/konami_desc_cache.json`):
   - Default: **soft WARN** on wording drift (PSCT updates vs cached Konami dump).
   - `--oracle-fail`: hard FAIL on drift (use when aligning print to Konami before FC).

## Commands

```bash
cd /home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON

# (Re)export editable source from production DB — exact preserve
python3 Tools/export_cards_source.py

# DEFAULT dry-run: validate + write Tools/_out/ only (never production)
python3 Tools/compile_cards.py
# same as:
python3 Tools/compile_cards.py --dry-run

# Apply only after dry-run PASS
python3 Tools/compile_cards.py --apply
```

## Dry-run behavior

- Validates all cards; fail-closed on schema/stub/required-field errors.
- On PASS writes `Tools/_out/cards_db.json` + `Tools/_out/DRY_RUN_SUMMARY.txt`.
- Does **not** overwrite `Assets/StreamingAssets/Cards/cards_db.json`.
- Does **not** run `phase1_bulk_compile.py` (reports whether path was found and what `--apply` would touch).

## Apply behavior (`--apply`)

1. Re-validate (same gates).
2. Write production `Assets/StreamingAssets/Cards/cards_db.json`.
3. Run `Tools/WRLDZ/phase1_bulk_compile.py` (unless `--skip-phase1`), which updates:
   - `Assets/StreamingAssets/WRLDZ/compiled_effects_seed_v1.json`
   - `Tools/WRLDZ/effect_coverage_phase1_latest.txt`

## Perfect-O dry-run summary (scaffold run 2026-09-16 ET)

| Metric | Value |
|--------|-------|
| card_count | 1717 |
| validation | **PASS** |
| stub_rejects | 0 |
| schema_rejects | 0 |
| oracle_soft_diffs (WARN) | 436 (Konami cache wording drift; soft) |
| phase1_found | **True** — `Tools/WRLDZ/phase1_bulk_compile.py` |
| production cards_db touched | **No** (dry-run) |
| apply would touch | `Assets/StreamingAssets/Cards/cards_db.json`; phase1 seed + coverage report |

## Out of scope

No DuelEngine rewrite · no Meshy · no Morphing Jar surgical work · token-lean / local only.

## Install from package

See `bin/INSTALL_ON_PC.sh` in the handoff tarball. Copies Tools scripts + initial `cards_source.json` + this doc into ProjectARGON / `~/backup-bot/Docs/`.
