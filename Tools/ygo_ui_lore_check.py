#!/usr/bin/env python3
"""Guards the ygo-ui-lore skill and the TCG-client duel HUD grammar.

Exit 1 if LP orbs were nulled again or the skill pack is incomplete.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HUD = ROOT / "Assets/Scripts/WRLDZ/UI/DuelFloatingHud.cs"
SKILL = ROOT / ".cursor/skills/ygo-ui-lore"
REQUIRED = (
    SKILL / "SKILL.md",
    SKILL / "sources.md",
    SKILL / "references/tcg-clients.md",
    SKILL / "references/anime-manga-visual.md",
    SKILL / "references/argon-visual-map.md",
)


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def main() -> int:
    for p in REQUIRED:
        if not p.is_file():
            fail(f"missing {p.relative_to(ROOT)}")
    skill = (SKILL / "SKILL.md").read_text(encoding="utf-8")
    if "Do not paste Konami chrome" not in skill:
        fail("skill lost the Konami-chrome hard rule")
    if "ygo-gamedev" not in skill:
        fail("skill should point at ygo-gamedev for rules")

    hud = HUD.read_text(encoding="utf-8")
    if "YouLp = null" in hud or "OppLp = null" in hud:
        fail("DuelFloatingHud still nulls LP orbs (TCG clients always show LP)")
    if 'Island(Root, "OppScore"' not in hud:
        fail("missing OppScore island")
    if "8000" not in hud:
        fail("LP default 8000 missing")

    chrome = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/HubChrome.cs"
    presenter = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/DualMenuPresenter.cs"
    if not chrome.is_file():
        fail("missing HubChrome.cs (hub overlay template)")
    pres = presenter.read_text(encoding="utf-8") if presenter.is_file() else ""
    if "HubChrome.HeaderBar" not in pres:
        fail("DualMenuPresenter phone overlays no longer use HubChrome")

    def island(name: str, max_w: float = 0.32, max_h: float = 0.10) -> tuple[float, float]:
        m = re.search(
            rf'Island\(Root, "{name}", ([0-9.]+)f, ([0-9.]+)f, ([0-9.]+)f, ([0-9.]+)f',
            hud,
        )
        if not m:
            fail(f"could not parse {name} island")
        x0, y0, x1, y1 = map(float, m.groups())
        w, h = x1 - x0, y1 - y0
        if w > max_w + 1e-6 or h > max_h + 1e-6:
            fail(f"{name} too large w={w:.3f} h={h:.3f} (cap {max_w:.2f}×{max_h:.2f})")
        return w, h

    you_w, you_h = island("YouScore")
    phase_w, phase_h = island("Phase", max_w=0.36)
    opp_w, opp_h = island("OppScore")
    print("ygo-ui-lore + duel HUD grammar")
    print("  skill pack ok")
    print(f"  YOU island {you_w:.3f}×{you_h:.3f} · PHASE {phase_w:.3f}×{phase_h:.3f} · OPP {opp_w:.3f}×{opp_h:.3f}")
    print("  YOU/OPP LP orbs present in DuelFloatingHud")
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
