# Agent handoff — Project ARGON (Duel Monsters WRLDZ)

**Product:** Duel Monsters WRLDZ  
**Unity:** `6000.5.10f1` — open **repo root** as the project.  
**Canon branch:** `main`. Do **not** create `cursor/*` feature branches unless the owner asks. Push against `main`.

The owner is **not a programmer** and plays on **Linux Unity Hub**. Read `.cursor/skills/owner-linux-unity/SKILL.md` before telling them how to see updates.

## How the owner sees updates (Hub "synced" is not GitHub pull)

Unity Hub's green / connected mark is **Unity Version Control (Plastic)** or a GitHub *link*. Cloud Agents push `github.com/GreatSaiyaDave/ProjectARGON`. Hub does not pull those commits by itself.

**One-time:** Hub → Add → **Add from repository** → GitHub → `GreatSaiyaDave/ProjectARGON` → main → open **that** project (`6000.5.10f1`).

**Each update:** Editor **WRLDZ → Get Latest from GitHub**, then **WRLDZ → Lab → Open Desktop Lab App**. Proof: Project search `WRLDZ_BUILD`, title **PLATES-0907**.

Do not tell them to `git pull` as the only step. Do not tell them Hub will auto-download agent pushes. Details: `.cursor/skills/owner-linux-unity/SKILL.md` and `GET_THE_GAME.txt`.

Editor Play on Boot = splash unless the Lab menu. Overlay chrome is **runtime**. This Cloud VM has **no Unity Editor**. Python: `python3 Tools/hub_overlay_chrome_check.py && python3 Tools/ygo_ui_lore_check.py && python3 Tools/deck_editor_layout_check.py`.

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

- **Owner cannot see updates / git pull failed:** `.cursor/skills/owner-linux-unity/`
- Rules / effects / PSCT: `.cursor/skills/ygo-gamedev/`
- Menus, HUD, lore tone: `.cursor/skills/ygo-ui-lore/` — no Konami chrome dumps, no scraping DuelingBook/MD
- Unity Editor automation: `.cursor/skills/unity-skills/`

## Engine

- Product path: `DuelEngine` + `OfficialEffectRegistry` + `TextEffects`. Do not `if (cardId == …)` except named unique exceptions.
- After compiler `Version` bump: `Tools/HeadlessEngine/run.sh --export-seed`
- Default check: `Tools/HeadlessEngine/run.sh --quiet` (needs .NET 8)

## UI chrome

- Hub screenshot is the template: opaque Imagine `tile_hub` / gold plates, cyan/gold rims, L-corner ticks. Use `HubChrome.PaintPlate` / `PaintWell` / `PaintChip` / `MountFeatured` / `MountDest`. Not `PanelMenuGlass`, not `UiTheme.RoundedRectSprite` glass chips, not `HubChrome.WellFill` as a well face.
- Home is **Overworld** Eye, not the Hub scene. DECK must use `DualMenuPresenter.BuildFrame`.
- Player overlays already plated: Eye, DECK, BAG, ARTIFACTS, scan, FREE VIEW, TOURNEY, avatar, Desktop Lab options, currency strip. Keep them plated.
- Tiny default `MenuCommandButton.Create` / `FloatingPanel.PrimaryButton` stay **unplated** (~56px 9-slice collapses). AR editor X on DECK stays `plated: false`. Smoke: `Assets/Editor/WRLDZ/MenuSmokeTest.cs` `AssertFloatingChip`.
- Visible stamp: `WrldzBuild.Stamp` (`PLATES-0907`) on title + Desktop Lab. Bump it when they must re-Add the folder.
- Python: `python3 Tools/hub_overlay_chrome_check.py && python3 Tools/ygo_ui_lore_check.py && python3 Tools/deck_editor_layout_check.py`

## Cursor Cloud

Install: `bash Tools/cloud-agent-install.sh` (see `.cursor/environment.json`).
