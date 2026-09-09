# OCG Lab Verification — 2026-09-07

**Verdict: PARTIAL (Instruction 2 acceptance).**

**Resume statement:** Resumed from the Instruction 1 plan (`Docs/OCG_LIVE_HANDOFF_CHIP_PLAN.md`), claimed Instruction 2, closed after static verify. Tray commit `761a35bf` was landed by Dilbot separately — not part of this verify packet (no redo).

## Execution boundary

The ProjectARGON Unity Editor was already running (PID 1582951, project `/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON`) with asset-import workers. Per handoff constraint, no second Unity instance and no batchmode smoke were launched. This is therefore source/static verification only; no live activate-to-resolve interaction was observed in this run.

## Paths traced

1. `Assets/Scripts/WRLDZ/Core/AppSession.cs:318-348`
   - `StartLabTestDuel("ocg_lab_player.json", "ocg_lab_ai.json")` creates a `LabTest` `PendingArMatch`, sets the deck filenames, and loads the duel scene.
   - `Assets/Scripts/WRLDZ/UI/DesktopLabApp.cs:304` calls the same entry point with the two OCG lab decks.
2. `Assets/Scripts/WRLDZ/Core/DuelBootstrap.cs:208-229,265-282`
   - An `ocg_lab` filename selects the OCG lab branch.
   - `OcgPreflightState.TryUseNative` gates native start; success calls `OcgLabDuelHost.StartNative(_db)` and logs native active. Failure or exception falls back to `StartStub(_db)`.
   - Other decks still instantiate `new DuelEngine()`; no AR/live flip was made.
3. `Assets/Scripts/WRLDZ/Duel/DuelCommandService.cs:18-21`
   - When `OcgLabDuelHost.IsActive` and `Current` are set, every intent is routed to `Current.TryExecute` before the C# dispatch path.
4. `Assets/Scripts/WRLDZ/Duel/Ocg/OcgLabDuelHost.cs:101-175,177-304`
   - Native/stub core waits are decoded generically. `Activate` is encoded from idle/battle legal-action lists, responses are sent to the core, and `Pump()` processes the resulting messages.
   - Yes/no effect waits map `Activate` to yes; unsupported waits fail visibly rather than inventing a C# recipe.
5. `Assets/Scripts/WRLDZ/Duel/Rules/OfficialEffectRegistry.cs:57-71,157-168`
   - `WarnIfLuaOwnsResolve` is warn-only for non-lab duels when `c{id}.lua` exists. It is bypassed while the lab host is active. No C# Lua-card recipe was added.
6. `Assets/Scripts/WRLDZ/Duel/Ocg/OcgScriptStore.cs:13-14,52-82,95-164`
   - Scripts are indexed from `Assets/StreamingAssets/OcgCore/scripts`; lookup is basename `c{id}.lua`, including nested `official/`, while excluded trees are ignored.
7. `Assets/Scripts/WRLDZ/Duel/Ocg/NativeOcgDuelCore.cs:37-105,241-255`
   - Native creation requires the plugin plus SQLite/CDB, installs card/script readers, loads `constant.lua`, `utility.lua`, and `procedure.lua`, and loads card scripts through the basename reader during core execution.

## Native gate snapshot

`Library/OcgNativePreflight.last.json` currently reports `ok: true`, API `11.0`, and `idleSeen: true` (fingerprint `5027962905bcf9a0efdc52a04c01d67ddabbfcde8afc5e14d8a26b5cfcd983f9`). Runtime still recomputes the fingerprint and checks plugin load plus SQLite/CDB in `OcgPreflightState.TryUseNative`; those checks were not re-run because the Editor was busy.

## Lab deck / Lua coverage

The two deck files are present at `Assets/StreamingAssets/Decks/ocg_lab_player.json` and `ocg_lab_ai.json`. The table covers every unique card id in their main/extra lists; `YES` means the exact basename exists in the indexed official script tree.

| Card id | Name | Deck occurrence | Exact Lua |
|---:|---|---|---|
| 32452818 | Beaver Warrior | player + AI | NO |
| 53129443 | Dark Hole | player + AI | YES (`official/c53129443.lua`) |
| 295517 | A Legendary Ocean | player | YES (`official/c295517.lua`) |
| 89631139 | Blue-Eyes White Dragon | player + AI | NO |
| 46986414 | Dark Magician | player | NO |
| 15025844 | Mystical Elf | player + AI | NO |
| 91152256 | Celtic Guardian | player + AI | NO |
| 13039848 | Giant Soldier of Stone | player + AI | NO |
| 23205979 | Spirit Reaper | player + AI | YES (`official/c23205979.lua`) |
| 59290628 | Nightmare Horse | player + AI | YES (`official/c59290628.lua`) |
| 24094653 | Polymerization | player + AI | YES (`official/c24094653.lua`) |
| 5053103 | Battle Ox (deck id) | player + AI | NO |
| 26378150 | Rude Kaiser | player + AI | NO |
| 30113682 | Judge Man | player + AI | NO |
| 62397231 | Hyozanryu | player + AI | NO |
| 17985575 | Lord of D. | player + AI | YES (`official/c17985575.lua`) |
| 97590747 | La Jinn the Mystical Genie of the Lamp | AI | NO |
| 41392891 | Feral Imp | AI | NO |
| 85684223 | Reaper on the Nightmare (extra) | player + AI | YES (`official/c85684223.lua`) |

Coverage is 7/19 unique ids with an exact Lua file. The manifest notes that the intended Battle Ox passcode is `5053192` and that deck id `5053103` is forbidden; neither `c5053103.lua` nor `c5053192.lua` is present in the checked tree. This is a deck/data gap, not a reason to invent a C# effect.

## Expected vs actual

| Acceptance item | Expected | Static result / actual run |
|---|---|---|
| Lab launch | `StartLabTestDuel` selects `ocg_lab_*`; native host preferred, stub only on failed/stale preflight | **PASS static.** Both decks exist; bootstrap and Desktop Lab wiring match. Cached preflight is `ok/idleSeen`, but live gate was not re-run. |
| Lua-card activate → resolve | Active lab host routes intent to `OcgLabDuelHost.TryExecute`; native core owns script resolution | **PARTIAL / static only — do not treat as live PASS.** Source routes `Activate` to the core and the reader loads by basename; no live activate→resolve was observed. |
| No-Lua card | No new C# recipe; stable refusal/core-visible failure | **PARTIAL / static only.** Policy + reader report missing scripts; unsupported waits fail visibly in source. Exact no-Lua card behavior was not exercised live. |
| Native smoke | Existing preflight/response-loop lab commands may be used when Editor is free | **NOT RUN.** Editor busy; no second Unity or batchmode launch. Existing menu tests cover preflight and one response from both players, but do not by themselves prove a selected card's resolve. |
| UI | Lab UI/viewer should expose the host state and legal actions | **PARTIAL.** `DuelUI` has host-aware legal snapshot/phase paths, but no live UI interaction was performed. Unsupported native waits remain a documented pause/failure gap. |

## Gaps / blockers

- **Primary blocker:** Unity Editor already open; source/static verification only, so activate-to-resolve is not a live smoke PASS.
- **Native fallback:** Stale/failed preflight or native exception falls back to `StartStub` after **logged** warning/exception (`DuelBootstrap` ~222-228) — easy to miss in a noisy console, but not silent. The stub is a replay tape, not a rules oracle.
- **Coverage:** 12/19 unique lab-deck ids have no exact `c{id}.lua`; Battle Ox has a manifest/deck passcode mismatch (`5053103` vs documented `5053192`).
- **Script presence ≠ resolve:** `c{id}.lua` on disk does not guarantee successful load/resolve (`NativeOcgDuelCore` ~241-255); runtime errors remain a live-smoke risk.
- **Generic message coverage:** documented unsupported OCG waits (for example announce-card, select-sum/counter, sort waits) pause/fail visibly and need later UI/core handling if a card reaches them.
- **UI evidence:** no live interaction was possible during this verification; no claim is made that a specific deck card resolved on screen.

## Perfect-O source-QC (2026-09-07)

**PARTIAL — coherent static; do not treat as live PASS.** Wording below incorporates QC corrections (static bounds on activate/refuse rows; logged stub fallback; lua-presence≠resolve risk; tray `761a35bf` acknowledged as out-of-packet).

## Recommendation

Do **not** enable native OCG for live AR/non-lab duels from this packet. Keep the current lab-only gate and warn-only non-lab compatibility path. After the Editor is free, run the existing `WRLDZ → Lab → Run OCG Lab Tests` / native response-loop path and a targeted Lua-card activation smoke (preferably Dark Hole or another covered card), then separately correct the Battle Ox deck-id data before treating the lab acceptance as full PASS. Do not add C# recipes for missing Lua cards.

No AR live flip, DuelEngine rewrite, CardArt/ignore.conf work, commit, or push was performed in this verify. Tray `761a35bf` is Dilbot’s separate commit and was not modified here.
