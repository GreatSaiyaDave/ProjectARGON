---
name: owner-linux-unity
description: How this owner gets Unity changes onto Linux Unity Hub. Use when they cannot see updates, thought agents edited local files, Hub looks synced, or git pull failed. The owner is not a programmer.
---

# Owner updates

**Every time you push a fix, say this:** Cloud agents only write GitHub. Their Unity folder on Linux does not change until they update that folder. Hub GitHub cat = link, not download. Hub cloud icon = Unity Cloud ads. Do not imply their local files were edited.

They play `…/DMWRDLZUnityProject/ProjectARGON`. Keep that folder. Do not tell them to zip-replace it or Hub → Add a second copy. They have local work.

Do not give `git pull` as the only step. `git pull` in the **Unity Console** does nothing useful. They need **Linux Terminal**, `cd` into that folder.

If they do **not** see `WRLDZ → Get Latest from GitHub`, they still have the old copy. Do not tell them to use that menu yet.

Tell them (close Unity first):

1. Linux Terminal, not Unity Console.
2. `cd` then drag `ProjectARGON` onto the terminal.
3. One line:

`curl -fsSL https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/Tools/get_latest_into_this_folder.sh | bash`

That script updates **in place**. It stops if Unity is open, if the remote is not `ProjectARGON`, or if they have uncommitted local edits (prints `git status` instead of wiping). If there is no `.git`, Hub only linked GitHub — the script commits their files first, then merges `origin/main`. Conflicts: stop, keep files, ask them to paste the terminal output.

Proof: `WRLDZ_BUILD` = `CLEAN-0907`. Then **WRLDZ → Lab → Open Desktop Lab App**.

After they have that stamp, later updates: **WRLDZ → Get Latest from GitHub** (same in-place pull).

`GET_THE_GAME.txt` is the owner copy. Zip/Add-from-GitHub is last resort on a new machine only, never as the default for this owner.
