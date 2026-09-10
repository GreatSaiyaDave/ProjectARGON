---
name: wrldz-cloud-verify
description: Cloud Agent and headless verification for Project ARGON / Duel Monsters WRLDZ. Use when this VM has no Unity Editor, when running HeadlessEngine or Python UI guards, when bumping CardTextEffectCompiler.Version, or when checking whether a change is visible in the owner's Unity Hub folder. Not for inventing card resolutions (use ygo-gamedev) or driving a live Editor REST server (use unity-skills only if /health is up).
---

# WRLDZ Cloud / headless verify (ARGON)

This Cloud VM has **no Unity Editor**. Do not wait for `GET /health` on ports 8090–8100. Verify engine and chrome with the commands below.

## Hard rules

1. **Engine:** `Tools/HeadlessEngine/run.sh --quiet` (needs .NET 8). Same `DuelEngine` sources as Unity; shims live under `Tools/HeadlessEngine/Shim/` (never under `Assets/`).
2. **UI chrome (no Game view):** `python3 Tools/hub_overlay_chrome_check.py && python3 Tools/ygo_ui_lore_check.py && python3 Tools/deck_editor_layout_check.py`
3. **Compiler version bump:** if you change `CardTextEffectCompiler.Version`, run `Tools/HeadlessEngine/run.sh --export-seed` so `compiled_effects_seed_v1.json` matches. Current Version is **51** (older AGENTS.md tables may still say v48/v50).
4. **Owner Unity Hub does not fetch GitHub.** After merge to `main`, tell them to update **in place** via `.cursor/skills/owner-linux-unity/` (`GET_THE_GAME.txt`). Never zip-replace `DMWRDLZUnityProject/ProjectARGON`. Hub cloud icon is not GitHub.
5. **Do not scrape** DuelingBook / Master Duel. Do not ship Ignis Lua as product rules.
6. **Product path** stays `DuelEngine` + registry + text compiler. Lab ocgcore is AGPL and separable.
7. **Push `origin main`.** Do not create `cursor/*` branches or PRs. Hub only ever pulls `main`.

## Route

| Task | Command / file |
|---|---|
| Full engine stress | `Tools/HeadlessEngine/run.sh --quiet` |
| More AI-vs-AI games | `Tools/HeadlessEngine/run.sh --quiet --duels 250` |
| Inspect one compile | `Tools/HeadlessEngine/run.sh --card "Call of the Haunted"` |
| Coverage / gaps | `--coverage` / `--gaps` |
| Re-export seed | `--export-seed` |
| Bootstrap this VM | `bash Tools/cloud-agent-install.sh` |
| How the harness works | [references/headless-and-guards.md](references/headless-and-guards.md) |

## UnitySkills REST

`.cursor/skills/unity-skills/` + UPM `com.besty.unity-skills` only work when the owner starts **Window → UnitySkills** on their Linux Editor. On Cloud: treat REST as unavailable. Advisory module docs (architecture, XR names, URP) are still readable.

## Console proof (owner Play Mode)

DECK overlay: log `[WRLDZ] DECK HubChrome overlay`. Hierarchy: `PhoneMenu_DECK`.

## When you are stuck

- Headless fail on missing `dotnet`: install .NET 8 SDK (see HeadlessEngine README), do not skip the suite.
- Python guard FAIL: fix the C# chrome, do not weaken the needle unless the owner changed the grammar.
- Need Game view: say so; do not fake a screenshot of the Editor.
