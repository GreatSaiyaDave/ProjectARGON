# Agent handoff — Project ARGON (Duel Monsters WRLDZ)

**Product:** Duel Monsters WRLDZ  
**Unity:** `6000.5.10f1` — open **repo root** as the project.  
**Canon branch:** `main`. Do **not** create `cursor/*` feature branches unless the owner asks. Push and PR against `main`.

The owner plays on **Linux Unity Hub**. Hub only opens a local folder. It does **not** fetch GitHub. After you push `main`, they must `git pull origin main` in **that** folder (or clone fresh and Add that folder in Hub). Console proof DECK is new: `[WRLDZ] DECK HubChrome overlay`. Hierarchy: `PhoneMenu_DECK`.

This Cloud VM has **no Unity Editor**. Verify engine with `Tools/HeadlessEngine/run.sh`. Verify UI with the Python guards below.

## What is already on `main` (do not re-implement)

Merged from other agent branches (2026-09-07):

| Area | Source branch | What landed |
|------|---------------|-------------|
| Engine | `cursor/qc-archfiends-e6a1` (+ setup/harness) | Rituals, Fusion-by-text, LIFO chains, coin/dice bus + AR FX, generalized hand traps, compiled-effects seed v48, Archfiend Standby/Pandemonium/Roar/Battle-Scarred, `Tools/HeadlessEngine` |
| Nav | `cursor/menu-nav-audit-e6a1` | TRADE screen wired; Eye extra row has `Nav_Trade` |
| Skills | `cursor/unity-editor-skill-6ea3` | `.cursor/skills/unity-skills/` + UPM |
| Skills | `cursor/ygo-gamedev-skill-6ea3` | `.cursor/skills/ygo-gamedev/` |
| UI/lore | squash `#5` `#8` + `cursor/overworld-eye-deck-hub-6ea3` | CARD LIST left-fill, YOU/OPP LP orbs, `HubChrome` plates on Eye + DECK (not smoked glass) |

**Do not merge** `cursor/deck-builder-collection-fill-e6a1`. Its CARD LIST pin is superseded by ChipGrid pinning the **panel**, never scroll content (`x0=0.630` on content packed chips right).

## Skills (read before touching those areas)

- Rules / effects / PSCT: `.cursor/skills/ygo-gamedev/`
- Menus, HUD, lore tone: `.cursor/skills/ygo-ui-lore/` — no Konami chrome dumps, no scraping DuelingBook/MD
- Unity Editor automation: `.cursor/skills/unity-skills/`

## Engine

- Product path: `DuelEngine` + `OfficialEffectRegistry` + `TextEffects`. Do not `if (cardId == …)` except named unique exceptions.
- After compiler `Version` bump: `Tools/HeadlessEngine/run.sh --export-seed`
- Default check: `Tools/HeadlessEngine/run.sh --quiet` (needs .NET 8)

## UI chrome

- Hub screenshot is the template: opaque Imagine `tile_hub` / gold plates, cyan/gold rims, L-corner ticks. Use `HubChrome.PaintPlate` / `MountFeatured` / `MountDest`. Not `PanelMenuGlass`, not `UiTheme.RoundedRectSprite` glass chips.
- Home is **Overworld** Eye, not the Hub scene. DECK must use `DualMenuPresenter.BuildFrame`.
- Tiny default `MenuCommandButton.Create` / `FloatingPanel.PrimaryButton` stay **unplated** (~56px 9-slice collapses). Smoke: `Assets/Editor/WRLDZ/MenuSmokeTest.cs` `AssertFloatingChip`.
- Python: `python3 Tools/hub_overlay_chrome_check.py && python3 Tools/ygo_ui_lore_check.py && python3 Tools/deck_editor_layout_check.py`

## Cursor Cloud

Install: `bash Tools/cloud-agent-install.sh` (see `.cursor/environment.json`).
