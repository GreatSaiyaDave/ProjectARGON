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

That script updates **in place**. It stops if Unity is open, if the remote is not `ProjectARGON`, or if they have uncommitted local edits (prints `git status` and the send one-liner instead of wiping). If there is no `.git`, Hub only linked GitHub — the script commits their files first, then merges `origin/main`. Conflicts: stop, keep files, ask them to paste the terminal output.

**If they ask to commit / sync FROM their Linux folder TO GitHub:** you cannot see that folder. Do not imply you committed their PC. Give the send one-liner (close Unity first):

`curl -fsSL https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/Tools/send_this_folder_to_github.sh | bash`

That commits in place, merges `origin/main` (no force-push), then `git push origin main`. Auth failure: tell them `sudo apt install gh` then `gh auth login` (GitHub.com, HTTPS, browser) and run send again. Do not zip-replace.

If they wanted **this PC's files** and unstick already replaced the folder with GitHub: Close Unity. Same folder. Give this curl:

`curl -fsSL https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/Tools/restore_pc_folder_from_before_unstick.sh | bash`

That resets the folder to the send snapshot in reflog/`ORIG_HEAD`. Does **not** push. Does not force-push. Do not zip-replace.

`unstick_merge_keep_github.sh` is GitHub → PC only. Do not give it when they asked to upload this folder to GitHub.

If send is still mid-merge (they paste `merge conflict in ProjectARGON`, or thousands of `official~` lua renames) **and they want to keep this PC's files**: same restore curl (it aborts the merge). Do not run unstick.

Proof: `WRLDZ_BUILD` = `CLEAN-0909`. Then **WRLDZ → Lab → Open Desktop Lab App**.

After they have that stamp, later updates: **WRLDZ → Get Latest from GitHub** (in-place pull). Send local work: **WRLDZ → Send this folder to GitHub**.

`GET_THE_GAME.txt` is the owner copy. Zip/Add-from-GitHub is last resort on a new machine only, never as the default for this owner.
