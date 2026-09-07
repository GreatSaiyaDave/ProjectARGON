---
name: wrldz-ar-lenses
description: AR Foundation, ARCore phone, and OpenXR lenses presentation for Project ARGON / Duel Monsters WRLDZ. Use when changing ArDuelSpace, ArFoundationSession, ArLensesSession, ArDuelInteractionSystem, Zone Mode AR entry, arena GPS scan, or phone vs Quest staging. Not for TCG rules (use ygo-gamedev), Hub chrome (use ygo-ui-lore), or generic XRI REST (use unity-skills/xr).
---

# WRLDZ AR / lenses (ARGON)

Read this skill before changing spatial duel presentation, AR session bootstrap, or the phone vs lenses split. Prefer ARGON docs (`AR_DUEL_PRESENTATION.md`, `FULL_AR.md`, `LENSES_XR_SETUP.md`) over a generic XR tutorial.

## Hard rules (this repo)

1. **One engine, one interaction graph.** `DuelEngine` is authority. `ArDuelInteractionSystem` is the only spatial stack. Do not add a second rules path in AR code.
2. **Never auto-force AR.** Tear / arena approach opens `ZoneModePrompt`: ENTER AR · DIGITAL · Cancel. Hardware never gates core play.
3. **`PreferDigital` is the same stage without camera.** F2P decline still uses disks + holos + `DuelEngine`.
4. **Editor Play is EditorSim.** ARCore does not run in the Game view. Prove 6DOF on an S23; prove 1:1 meters on Quest.
5. **Input goes through `WrldzInput`.** Never call `Input.GetKey` / `GetAxis` (Input System–only project; those throw).
6. **Unlit anime materials** (`ArAnimePresentation`). Do not switch holos to Lit/PBR “for realism.”
7. **Hidden information stays hidden.** Hand backs, face-down sets, and local inspect popups must not leak to the opponent.
8. **Do not follow `pico-design`.** Product path is ARCore phone + OpenXR Quest, not PICO.
9. **`WRLDZ_HAS_ARFOUNDATION`** wraps the real AR Foundation types. Keep the stub class compilable when the define is off.

## Route

| Task | Read |
|---|---|
| Phone vs lenses vs EditorSim | [references/spatial-graph.md](references/spatial-graph.md) |
| AR Foundation 6.6 / ARCore / OpenXR | [references/ar-foundation-6.md](references/ar-foundation-6.md) |
| Dual phone/AR menus | `Assets/Scripts/WRLDZ/UI/MENU_DUAL.md` |
| Sources | [sources.md](sources.md) |

## Presentation targets (closed set)

| Target | Session | Tracking | Scale |
|---|---|---|---|
| Phone ENTER AR | `ArFoundationSession` | ARCore 6DOF + floor plane | World-locked meters on the detected plane |
| Quest / lenses | `ArLensesSession` | OpenXR HMD + controllers | 1:1 m, left-controller arm disk |
| EditorSim / DIGITAL | `ArDuelSpace` sim tracker | None | Stage units; same disks/hand/arena |

All three call `ArDuelInteractionSystem`. Lenses set `LifeSize = true` and `UseExternalTracker` with `XrWorldArmTracker`.

## Dual UI

Every systems menu: `DualMenuPresenter.BuildFrame` → `UiPresentation.NonArPortrait` or `ArDiskHolo`. Do not invent a third full UI. Chrome rules live in `ygo-ui-lore`.

## When you are stuck

- If holos slide with the camera on device, you are on the old webcam wallpaper path — ENTER AR must go through `ArFoundationSession` / `BuildArFoundationStage`.
- If Quest disks are not arm-locked, you used the phone RT stage instead of `ArDuelSpace.BuildLensesStage`.
- If a menu is a full-screen binder during a live duel, it belongs on phone, not `ArDiskHolo`.
- For chains, PSCT, or compiled effects, stop and open `ygo-gamedev`.
