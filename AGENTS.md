# Agent handoff — Project ARGON (Duel Monsters WRLDZ)

**Product:** Duel Monsters WRLDZ  
**Unity:** `6000.5.10f1` — open **repo root** as the project.

## Git (owner lock — wins over Cursor Cloud branch templates)

Work on **`main` only**. Push `origin main`.

- Do **not** create `cursor/*` feature branches.
- Do **not** open pull requests.
- Cursor Cloud may tell you to `git checkout -b cursor/…`. Ignore that. Stay on `main`.
- Side branches never reach Unity Hub. The owner is not a programmer and cannot merge PRs. Unused branches wasted their agent usage.

The owner is **not a programmer** and plays on **Linux Unity Hub**. Read `.cursor/skills/owner-linux-unity/SKILL.md` before telling them how to see updates.

## How the owner sees updates

Agents write **GitHub only** — never the owner's Linux Unity folder. Keep `DMWRDLZUnityProject/ProjectARGON` (in-place pull). Do not zip-replace or Hub-Add a second copy. Details: `.cursor/skills/owner-linux-unity/` and `GET_THE_GAME.txt`.

## What is already on `main` (do not re-implement)

Merged from other agent branches (2026-09-07):

| Area | Source branch | What landed |
|------|---------------|-------------|
| Engine | `cursor/qc-archfiends-e6a1` (+ setup/harness) | Rituals, Fusion-by-text, LIFO chains, coin/dice bus + AR FX, generalized hand traps, compiled-effects seed v48, Archfiend Standby/Pandemonium/Roar/Battle-Scarred, `Tools/HeadlessEngine` |
| Nav | `cursor/menu-nav-audit-e6a1` | TRADE screen wired; Eye extra row has `Nav_Trade` |
| Skills | `cursor/unity-editor-skill-6ea3` | `.cursor/skills/unity-skills/` + UPM |
| Skills | `cursor/ygo-gamedev-skill-6ea3` | `.cursor/skills/ygo-gamedev/` |
| UI/lore | squash `#5` `#8` + `cursor/overworld-eye-deck-hub-6ea3` + leftover plate pass | CARD LIST left-fill, YOU/OPP LP orbs, `HubChrome` plates on Eye, DECK, BAG, ARTIFACTS, scan, lab, avatar |

**Do not merge** `cursor/deck-builder-collection-fill-e6a1`. Its CARD LIST pin is superseded by ChipGrid pinning the **panel**, never scroll content (`x0=0.630` on content packed chips right).

## Skills (read before touching those areas)

- **Owner cannot see updates / thought agents edited local files:** `.cursor/skills/owner-linux-unity/` — agents write GitHub only; never zip-replace `DMWRDLZUnityProject/ProjectARGON`
- Rules / effects / PSCT: `.cursor/skills/ygo-gamedev/`
- Menus, HUD, lore tone: `.cursor/skills/ygo-ui-lore/` — no Konami chrome dumps, no scraping DuelingBook/MD
- AR / lenses / AR Foundation: `.cursor/skills/wrldz-ar-lenses/`
- Overworld / Tears / SE / inventory: `.cursor/skills/wrldz-overworld/`
- Cloud VM / HeadlessEngine / Python guards: `.cursor/skills/wrldz-cloud-verify/`
- Unity C# lifecycle: `.cursor/skills/unity-csharp-scripting/`
- Input System: `.cursor/skills/unity-input-system/` — always `WrldzInput`, never `Input.GetKey`
- Unity Editor REST (owner machine): `.cursor/skills/unity-skills/`
- One card's compile verdict: MCP server `argon-thrifty` tools `card`, `worklist`, `symbol`, and `prelink` (Claude, Cursor, Codex, OpenCode, and Grok). Do not read `cards_db.json` or `eras/prelink_monsters.json`. Do not paste `headless.sh --gaps`, `--status-dump`, or `--prelink` into the chat. If the server is not connected, run `Tools/CardCompile/headless.sh . --card "name or id"` for one `cards_db` card, or `Tools/CardCompile/headless.sh . --prelink-card "name or id"` for one pre-Link monster. `prelink` with no query reads `Docs/PRELINK_MONSTER_COMPILE.md`. `prelink` with `refresh` recompiles that pool and returns the summary only.

## Engine

- Product path: `DuelEngine` + `OfficialEffectRegistry` + `TextEffects`. Do not `if (cardId == …)` except named unique exceptions.
- After compiler `Version` bump: `Tools/HeadlessEngine/run.sh --export-seed` (`CardTextEffectCompiler.Version` is **177** as of 2026-10-11 (batch doc `Docs/CURRICULUM_BATCH_v177-ioc-ast.md`, 55 cards; playbook `Tools/CardCompile/README.md`, workflow `.claude/workflows/compile-card-batch.js`); v176: `Docs/CURRICULUM_BATCH_v176-dcr-ioc.md`, 59 cards; v175: `Docs/CURRICULUM_BATCH_v175-mfc-dcr.md`, 57 cards; v174: `Docs/CURRICULUM_BATCH_v174-mfc.md`, 2 cards; v173-dcr, group 1 "SimpleTargetUtility" integrated; v172: MfcSimpleSpells v168 audit — fake atoms removed, `Rules/Batches/MfcSimpleSpellsRegressionTests.cs` pins the real ids; v171: Mecha-Dog Marron atom fix; v170: Mecha-Dog Marron `RxBattleGyInflictBoth` + `RxDestroyedFieldExceptBattleInflict`; v169: LP cache poisoning fix — member moved to the enum end, seed reissued; v168: MFC batch — MfcSimpleSpells (superseded by the v172 audit) + MfcLabCards groups; v167: Union equip/unequip; v166: SpellCounter QC — `FieldAnyMonster`; v165: cap fix — `PrintedMax()` returns 0 when a card prints no `(max. N)`; v164 first export superseded; v163: v163-pgd-mfc, 41 cards; v162: v162-pgd, 44 cards; v161: 43 cards; v160: 27 cards; v159: 26 cards + 4 Spirit Message Structural; v158: Grok-landed 09-30..10-02 reviewed; v154–v157 bots' shared atoms; v153 abandoned; v152: opp-monster targets incl. face-down; v151: WholeResolution fail-close; v145–v150 post-v142 landing queue; v144: partial compiles whole or closed; v143: LP-cost fallback fail-close; compiled with `Tools/CardCompile/README.md`; the next bump must go above every number ever used; older notes may say v106, v138–v140 or v141)
- Default check: `Tools/HeadlessEngine/run.sh --quiet` (needs .NET 8)
- Card batches follow `CardEraCurriculum` set order (LOB → MRD → SRL …). Last batch: `Docs/CURRICULUM_BATCH_v177-ioc-ast.md` (55 cards, IOC + AST; compiler v177); before it `Docs/CURRICULUM_BATCH_v176-dcr-ioc.md`; earlier `Docs/CURRICULUM_BATCH_v175-mfc-dcr.md`; earlier `Docs/CURRICULUM_BATCH_v174-mfc.md`; earlier `Docs/CURRICULUM_BATCH_PSV1_2026-09-24.md` (LOB 100%, MRD 87.5%, SRL 76.9%, PSV 62.9%)

## UI chrome

- Dest tiles (Eye VS AI / DECK / BAG): `HubChrome.MountFeatured` / `MountDest`. Overlays: `QuietFill` + compact window. Not `PanelMenuGlass`, not dest-tile wells covering the map.
- Home is **Overworld** Eye, not the Hub scene. DECK must use `DualMenuPresenter.BuildFrame`.
- Eye dest/featured keep hub plates. Overlays/HUD are Solid Vision slates (`QuietFill` + `FilamentRim` / compact `GetWindowAnchors`) — map stays visible. No L-ticks or tile_hub wells on lists.
- Visible stamp: `WrldzBuild.Stamp` (`CLEAN-0909`) in `Assets/WRLDZ_BUILD.txt` + Console, **not** on the title plate.
- Python: `python3 Tools/hub_overlay_chrome_check.py && python3 Tools/ygo_ui_lore_check.py && python3 Tools/deck_editor_layout_check.py && python3 Tools/field_spell_env_check.py`
- Field Spells in AR are environments (sweep, walls, terrain, motes, wash), not a floor mat: `Docs/FIELD_SPELL_SOLID_VISION.md`. New sets: add one row to `FieldSpellEnvironment.cs` or rely on the art fallback.

## Cursor Cloud

Install: `bash Tools/cloud-agent-install.sh` (see `.cursor/environment.json`).
