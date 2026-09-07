---
name: owner-linux-unity
description: How this owner gets Unity changes onto Linux Unity Hub. Use whenever they say git pull failed, Hub shows no changes, a command is not working, or they cannot see UI/engine updates. The owner is not a programmer.
---

# Owner gets updates (Linux Unity Hub)

Read this before telling the owner to run git. Cloud Agents push GitHub. Unity Hub never fetches GitHub. The owner's PC only changes when **that** PC gets a new folder and Hub **Adds** it.

## Hard rules

1. **Never give `git pull origin main` as the only step.** The owner has already tried it. It fails when the Hub folder is not a git clone, they are in the wrong directory, or they pasted it into the Unity Console.
2. **Unity Console is not a terminal.** Git commands belong in Linux Terminal (or a ZIP download). Pasting `git pull` into Unity's Console window does nothing.
3. **This Cloud VM is not their PC.** Running git here does not update the folder Hub opens.
4. **Menus are built at runtime.** Edit-mode scenes look the same. They must Play, then open the overlay. "I opened the project in the Editor" is not proof they have new chrome.
5. **Editor Play starts at Boot splash/title**, not the Eye. Desktop Lab does **not** auto-open unless `WRLDZ/Lab/Open Desktop Lab App` or the auth **DESKTOP LAB (NO EQUIPMENT)** button. Eye plates are: Desktop Lab → **OVERWORLD (WASD MAP)** → tap the Eye → DECK / BAG.
6. If they say **no changes**, assume Hub is still opening an **old folder** until they search the Project window for `WRLDZ_BUILD` and see `PLATES-0907` (or the current stamp in `WrldzBuild.cs`).

## Reliable path (give this, in this order)

1. Close Unity Editor and Unity Hub.
2. Prefer a **new folder**, do not fight the old one.
3. Firefox / Chrome: https://github.com/GreatSaiyaDave/ProjectARGON/archive/refs/heads/main.zip  
   Extract it. The folder Hub must open contains **Assets** and **ProjectSettings** (not the parent of that).
4. Optional if they have git in a Terminal (not Unity):  
   `git clone https://github.com/GreatSaiyaDave/ProjectARGON.git ~/ProjectARGON-main`
5. Unity Hub → **Add** → pick that folder → open with **6000.5.10f1**.
6. Project search: `WRLDZ_BUILD`. If the file is missing, Hub is still on the old copy.
7. Menu **WRLDZ → Lab → Open Desktop Lab App** → Play Boot + Lab. Console must show `[WRLDZ] BUILD PLATES-0907`.
8. **OVERWORLD (WASD MAP)** → Eye → **DECK**. Hierarchy: `PhoneMenu_DECK`. Console: `HubChrome overlay`.

Repo file `GET_THE_GAME.txt` is the same steps in owner language. Keep it in sync with `WrldzBuild.Stamp`.

## Do not

- Tell them Hub will "sync" GitHub.
- Ask them to merge `cursor/*` branches. Canon is `main`.
- Point them at the Hub scene as home. Home is Overworld Eye.
- Treat a successful Cloud `git push` as something they can already see.
