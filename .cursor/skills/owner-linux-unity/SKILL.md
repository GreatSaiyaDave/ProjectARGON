---
name: owner-linux-unity
description: How this owner gets Unity changes onto Linux Unity Hub. Use whenever they say git pull failed, Hub shows no changes, Hub says it is synced but GitHub is not, a command is not working, or they cannot see UI/engine updates. The owner is not a programmer.
---

# Owner gets updates (Linux Unity Hub)

Read this before telling the owner to run git. Cloud Agents push **GitHub** (`GreatSaiyaDave/ProjectARGON`). Unity Hub's synced / connected mark is **not** an automatic pull of those commits.

## Why Hub looks synced

This repo used `ProjectSettings/VersionControlSettings.asset` **Unity Version Control** (Plastic / UVCS). Hub 3.17 can also show a GitHub icon after you *link* a folder. Both mean "this project is tied to a provider." They do **not** mean:

- Hub watched github.com and downloaded the latest `main`
- The folder Hub opens is the Cloud Agent repo
- Play Mode will show plated Eye/DECK chrome

Hover the Hub source-control icon. URL must be `GreatSaiyaDave/ProjectARGON`. UVCS / a different GitHub repo = wrong cloud.

Hub GitHub integration (3.17): create repo, **Add from repository**, show branch. It does not pull on every agent push. Connecting an *existing local folder* to GitHub often links **that stale copy**, it does not replace it with GitHub `main`.

## Hard rules

1. **Never give `git pull origin main` as the only step.** Prefer Hub **Add from repository**, then in-Editor **WRLDZ → Get Latest from GitHub**.
2. **Unity Console is not a terminal.**
3. **This Cloud VM is not their PC.**
4. **Menus are built at runtime.** Edit-mode scenes look the same.
5. **Editor Play starts at Boot splash** unless **WRLDZ → Lab → Open Desktop Lab App**. Eye plates: Desktop Lab → **OVERWORLD (WASD MAP)** → Eye → DECK / BAG.
6. If they say **no changes**, Hub is on an **old folder** until Project search finds `WRLDZ_BUILD` with stamp `PLATES-0907`.

## Reliable path

**One-time (correct GitHub clone in Hub 3.17+):**

1. Close the Editor.
2. Hub → Projects → **Add** → **Add from repository** → GitHub → `GreatSaiyaDave/ProjectARGON` → **main**.
3. Open **that** project with **6000.5.10f1**. Do not keep opening the old row.

**Each later update:** Editor **WRLDZ → Get Latest from GitHub**, then Lab menu. Console `[WRLDZ] BUILD PLATES-0907`.

**Fallback zip:** https://github.com/GreatSaiyaDave/ProjectARGON/archive/refs/heads/main.zip — folder with Assets + ProjectSettings → Hub Add.

`GET_THE_GAME.txt` is owner language. Keep it in sync with `WrldzBuild.Stamp`.

## Do not

- Tell them Hub will auto-sync GitHub because the icon is green.
- Ask them to merge `cursor/*` branches. Canon is `main`.
- Point them at the Hub scene as home. Home is Overworld Eye.
- Treat a Cloud `git push` as something their Editor already has.
