# Headless engine and Python guards

## HeadlessEngine

`Tools/HeadlessEngine/README.md`. The `.csproj` compiles real sources from `Assets/Scripts/WRLDZ/{Data,Duel}` and **excludes** AGPL `Duel/Ocg`.

| Shim | Role |
|---|---|
| `Shim/UnityEngineShim.cs` | Tiny `UnityEngine` surface (`Debug`, `Mathf`, `JsonUtility` via `System.Text.Json`, …) |
| `Shim/WrldzStubs.cs` | Inert AR / session / GPS types so rules can compile without presentation |

Default run executes `DuelEngineStressTests`: unit regressions (`TcgRegressionTests`, `InteractionRegressionTests`, `CorpusTriggerStressTests`), lab-deck compile check, N complete AI-vs-AI games, battle-math fuzz. Non-zero exit if `report.Ok == false`.

Seed: `Assets/StreamingAssets/WRLDZ/compiled_effects_seed_v1.json`. Load order in Unity: seed → persistent disk (disk wins). Seed is skipped unless `version` matches `CardTextEffectCompiler.Version`.

Offline compile only on the live path. `AllowRuntimeAi` defaults **off**. Yugipedia fetches belong in tools, never in a match (`ygo-gamedev`).

## Python chrome guards

These are string/layout contracts, not a Game view.

| Script | Guards |
|---|---|
| `Tools/hub_overlay_chrome_check.py` | Overlay sheets stay compact (header / well / BACK); dest tiles stay on Eye/hub |
| `Tools/ygo_ui_lore_check.py` | Lore / chrome skill needles (no Konami dump paths) |
| `Tools/deck_editor_layout_check.py` | CARD LIST panel pin — **not** scroll-content pin from `deck-builder-collection-fill` |

## Cloud bootstrap

`.cursor/environment.json` → `bash Tools/cloud-agent-install.sh`. Checks `python3` / cmake / g++ / git and required JSON seeds under `StreamingAssets`. Idempotent.

## Owner folder vs GitHub

Agents write GitHub only. How the owner refreshes Linux Unity Hub is `.cursor/skills/owner-linux-unity/` — in-place script, keep `DMWRDLZUnityProject/ProjectARGON`, never zip-replace. Hub cloud icon is not GitHub.
