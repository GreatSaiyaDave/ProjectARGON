# OCG lab scripts (AGPL-3.0-or-later)

Pinned ProjectIgnis/CardScripts `e02497b61bd45048230cdfb4cc4a075ecb09b4d3`.

- Root `*.lua`: shared libraries the core/`utility.lua` `LoadScript`s (`constant.lua`, `utility.lua`, `procedure.lua` shim, `chain.lua`, `proc_*.lua`, …). Keep these imported — there are ~30 files.
- `official~/c*.lua`: official card scripts. Folder name **must** end with `~`. Unity's Asset Pipeline ignores `~` folders (see Hidden assets in the reserved-folder docs), so FileHasher does not hash these ~13k lua files on Editor open. `OcgScriptStore` still indexes them by basename via a recursive disk scan. Do not rename back to `official/` and do not add `.meta` files here — that OOM-crashed the Editor (`FileHasher::ComputeFileHashJob`, ~122 TB garbage alloc).

Not copied: `pre-release/`, `unofficial/` card scripts, `rush/`, `goat/`, `skill/`. `proc_unofficial.lua` is the single unofficial *library* `utility.lua` loads by basename.

Editor extra search root: env `OCG_SCRIPTS_ROOT` or EditorPrefs `WRLDZ.OcgScriptsRoot`. Do not point that at the mixed flat `ygopro-scripts` clone.

Player builds do not copy `~` folders through the Asset Pipeline. Lab play in the Editor reads this tree from disk. Do not store-ship this path.

See `Assets/Scripts/WRLDZ/Duel/OCGCORE_LAB.md`.
