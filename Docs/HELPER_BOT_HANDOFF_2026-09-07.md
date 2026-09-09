# HELPER BOT HANDOFF — Dilbot → The Helper Bot (local)
Date: 2026-09-07 ~19:15 ET
From: Dilbot (cloud)
To: **The Helper Bot** (local Desktop GTK / `~/bin/argon-app` / Ollama `argon-local`)
Also visible to: Backup-bot (cloud cover) via same handoff queue
Domain: engine
QC: Mr. Perfect-O (source + headless/compile after any code change)

## Goal
Continue Project ARGON from Dilbot’s last checkpoint: **finish Monster Reborn soft-lock** (smoke → optional commit → headless seal), then optional **OCG lab live smoke** (Instruction 2 residual). Do not expand scope.

## Repo / checkpoint
- Path: `/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON`
- Branch: `main` **ahead of origin by 3** (`9267e7b0`, `0cc4cb33`, `761a35bf`) — **do not push** unless Great SaiyaDave asks
- Last commit: `761a35bf` — response tray ACTIVATE/PASS + layout harden + `ignore.conf` nul/pid + `Docs/OCG_LIVE_HANDOFF_CHIP_PLAN.md`

## Working tree (dirty — intentional)
**Reborn soft-lock WIP (KEEP — this is the active item):**
- `Assets/Scripts/WRLDZ/Duel/DuelEngine.cs` — `ResolveChainStack` passes `link.Target` / `Targets[0]` as `forcedTarget`
- `Assets/Scripts/WRLDZ/Duel/SpellTrapEffects.cs` — AI `PassResponse` on failed BeginOrResolve; ChainResponse only payable negate monsters
- `Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs` — `TryResolveActivation(..., forcedTarget=null)` honors forced pick

**Leave alone unless Dave asks:**
- `Packages/manifest.json` + `packages-lock.json` (Auth package — unused)
- All untracked `Assets/StreamingAssets/CardArt/*`
- `wrldz_stress_report.txt` / Relinquished stress docs (Backup-bot already shipped; Perfect-O PASS harness)
- Tools/`_argon_*` scratch scripts

## QC already done
- Perfect-O **source PASS** on Reborn soft-lock (compile skipped while Editor open). Claims 1–4 match. Non-blocking residual: empty both target fields → auto-pick fallback.
- Response tray smoke + commit `761a35bf` CLEAR.
- OCG lab verify: `Docs/OCG_LAB_VERIFY_2026-09-07.md` = **PARTIAL** (static only). Fanbot: do not treat as live OCG PASS.
- Relinquished stress: harness **PASS**; damage-reflect **UNPROVEN** residual. Fanbot oracle when asserting: opponent takes the battle damage that would have hit the controller (no double-hit / no controller absorb-then-reflect). MarkAbsorbed alone ≠ proven. **Not** the active queue item unless Dave reprioritizes.

## Locked policies
- Modern PSCT; eras = card availability only — no rules forks
- Surgical Duel edits only — no rewrite
- `Docs/RESOLVE_OWNERSHIP.md`: OCG owns resolve when lua exists; never invent C# for Lua cards
- No all-AR OCG flip
- No second Unity if Editor open — use Pipeline `unity run … --command wrldz_tcg_tests` or wait for free Editor / batchmode
- Perfect-O QC before ship; no commit/push unless Dave asks

## Presentation (FYI — art leads)
Thin Instant Duel anime ship rides existing hooks: `PlaySummonFx`, `PlaySummonSequence`, `DiskFxEvent.SummonFlash`, attack director + DiskFx charge/impact. Later engine gaps (not blockers): calm LP float tick, ATK/DEF world plate. Art room / Fantasia owns Ox / Flame Swordsman visuals.

## Instruction 1 — Reborn smoke (IN_PROGRESS / do this first)
1. Confirm Editor state. If Play Mode available: activate Monster Reborn → pick GY target → opponent chain window → resolve. Expect: no soft-lock; chosen monster Special Summons; AI either passes or successfully activates a payable negate.
2. If Editor busy / Pipeline reachable: prefer targeted suite filter if available; else note BLOCKED for live smoke and skip to Instruction 2 only if Dave says so.
3. Log expected vs actual in a short note under `~/backup-bot/context/` or append to this brief.

Acceptance: no hang after GY pick; duel continues; Reborn target honored on resolve.

## Instruction 2 — Headless seal (only after smoke PASS or Dave waives smoke)
When Editor **closed** / no lock: headless or Pipeline `wrldz_tcg_tests`. Expect no new `error CS`. Pre-existing suite FAILs (unitFail≈61–64) are parked harness — do not “fix the suite.” Ping Perfect-O for compile seal note.

## Instruction 3 — Commit Reborn (only if Dave asks OR smoke PASS + Dave OK)
Commit **only** the three Reborn files above. Message sketch: soft-lock fix — AI PassResponse on failed chain negate + honor chain-link target on FullyCompiled resolve. Skip CardArt / Auth packages / stress report. **No push** unless asked.

## Instruction 4 — OCG lab live smoke (optional; only if Dave picks)
Residual of Instruction 2 from OCG plan: live `ocg_lab_*` activate→resolve when Editor free. Update `Docs/OCG_LAB_VERIFY_2026-09-07.md` from PARTIAL → PASS/FAIL with evidence. No AR flip. Coverage gap 7/19 Lua is data, not invent-C#.

## Instruction 5 — Parked (do not start unless Dave reprioritizes)
- Relinquished damage-reflect assert (Fanbot oracle above)
- Chain-negate counters (Jammer / Seven Tools) — Chip3 parked
- Spirit Reaper / Rite / NV / Medusa / Decayed harness
- LP float tick + ATK/DEF world plate

## Next exact action for The Helper Bot
1. Open The Helper Bot Desktop app.
2. Read this brief (`~/backup-bot/context/HELPER_BOT_HANDOFF_2026-09-07.md`).
3. Run `/handoff next` or `handoff-tool next` → execute **Instruction 1 only**.
4. Report smoke PASS/FAIL in room @Dilbot @Mr. Perfect-O (or bridge to-grok).
5. Stop after Instruction 1 unless Dave unlocks 2–4.

## Do not
- Push to origin
- Commit CardArt / Auth package / stress noise
- Steal Fantasia Meshy / art ownership
- Treat OCG PARTIAL as live PASS
- Claim Relinquished reflect proven
- Rewrite DuelEngine
