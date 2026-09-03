# OCG lab scripts (AGPL-3.0-or-later)

Pinned ProjectIgnis/CardScripts `e02497b61bd45048230cdfb4cc4a075ecb09b4d3`.

- Root `*.lua`: shared libraries the core/`utility.lua` `LoadScript`s (`constant.lua`, `utility.lua`, `procedure.lua` shim, `chain.lua`, `proc_*.lua`, …).
- `official/**/c*.lua`: official card scripts, directory structure preserved. `OcgScriptStore` indexes by basename.

Not copied: `pre-release/`, `unofficial/` card scripts, `rush/`, `goat/`, `skill/`. `proc_unofficial.lua` is the single unofficial *library* `utility.lua` loads by basename.

Editor extra search root: env `OCG_SCRIPTS_ROOT` or EditorPrefs `WRLDZ.OcgScriptsRoot`. Do not point that at the mixed flat `ygopro-scripts` clone.

See `Assets/Scripts/WRLDZ/Duel/OCGCORE_LAB.md`.
