#!/usr/bin/env python3
"""Canon lock: story stays 8000 / !DkOverlay. DK PvAI opt-in is badge-gated.

Exit 1 if story call sites flip the overlay, or if Format Select PLAY
is no longer gated on format.dk + TableLawsLive.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STORY_JSON = ROOT / "Assets/StreamingAssets/WRLDZ/Story/season1_dk.json"
MAP = ROOT / "Assets/Scripts/WRLDZ/Core/MapZoneService.cs"
OPP = ROOT / "Assets/Scripts/WRLDZ/Core/OpponentCatalog.cs"
CREATE = ROOT / "Assets/Scripts/WRLDZ/UI/ArDuelCreateScreen.cs"
FMT = ROOT / "Assets/Scripts/WRLDZ/Core/FormatProgress.cs"
CFG = ROOT / "Assets/Scripts/WRLDZ/Core/ArDuelMatchConfig.cs"
SELECT = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/FormatSelectScreen.cs"
BOOT = ROOT / "Assets/Scripts/WRLDZ/Core/DuelBootstrap.cs"
TESTS = ROOT / "Assets/Scripts/WRLDZ/Core/StoryCampaignTests.cs"
CANON = ROOT / "Assets/Scripts/WRLDZ/PROGRESSION_CANON.md"


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def text(path: Path) -> str:
    if not path.is_file():
        fail(f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


def must_contain(path: Path, needle: str, why: str) -> None:
    if needle not in text(path):
        fail(f"{path.relative_to(ROOT)} {why} (need `{needle}`)")


def main() -> int:
    catalog = json.loads(text(STORY_JSON))
    stages = catalog.get("stages") or []
    if len(stages) < 10:
        fail(f"story catalog too short ({len(stages)})")
    for st in stages:
        sid = st.get("id", "?")
        if st.get("startingLp") != 8000:
            fail(f"{sid} startingLp={st.get('startingLp')} (story must stay 8000)")
        if st.get("dkOverlay") is True:
            fail(f"{sid} dkOverlay true (story must stay !DkOverlay)")

    must_contain(MAP, "cfg.DkOverlay = false;", "story factory lost the overlay lock")
    must_contain(MAP, "Launch = ArDuelLaunchKind.StoryEra;", "MakeStoryEra lost StoryEra")
    must_contain(OPP, "c.DkOverlay = false;", "opponent PvAI must stay standard TCG")
    must_contain(OPP, "c.StartingLp = 8000;", "opponent PvAI must stay 8000 LP")
    must_contain(CREATE, "cfg.DkOverlay = false;", "surface-scan PvAI must stay standard TCG")

    fmt = text(FMT)
    if not re.search(
        r"TableLawsLive\(string formatId\)\s*=>\s*"
        r"string\.Equals\(formatId, DuelistKingdomId",
        fmt,
    ):
        fail("TableLawsLive must be live for DuelistKingdomId only")
    if '"raid"' in fmt and "TableLawsLive" in fmt:
        # raid may appear in Ids; it must not appear in the TableLawsLive body
        body = fmt.split("TableLawsLive(string formatId)", 1)[1].split("public static bool HasBadge", 1)[0]
        if "raid" in body or "speed" in body:
            fail("TableLawsLive body must not name other formats")
    must_contain(FMT, "CanOptInDuelistKingdom", "lost badge gate")

    must_contain(CFG, "TryApplyDuelistKingdomOptIn", "lost opt-in helper")
    must_contain(CFG, "DuelistKingdomPvAi", "lost DK PvAI factory")
    must_contain(CFG, "IsStoryLaunch", "lost story-launch predicate")
    must_contain(CFG, "DuelistKingdomStartingLp = 2000", "lost 2000 LP constant")

    must_contain(SELECT, "FormatProgress.TableLawsLive(id)", "Format Select PLAY not gated on TableLawsLive")
    must_contain(SELECT, "CanOptInDuelistKingdom", "Format Select PLAY not re-checked at tap")
    must_contain(SELECT, "ArDuelMatchConfig.DuelistKingdomPvAi()", "Format Select PLAY must launch DK PvAI")
    must_contain(SELECT, "Story stays standard TCG", "Format Select copy lost the story lock")
    must_contain(SELECT, 'cta: live ? "PLAY" : "START"', "live DK card must use PLAY, not START")

    must_contain(BOOT, "!ArDuelMatchConfig.IsStoryLaunch(match.Launch)", "bootstrap must refuse story overlay")
    must_contain(TESTS, "StartingLp == 8000 && !cfg.DkOverlay", "lost opponent-match 8000 lock")
    must_contain(TESTS, "!storyCfg.DkOverlay && storyCfg.StartingLp == 8000", "lost story 8000 lock")
    must_contain(TESTS, "Story launch refuses DK opt-in", "lost opt-in story refusal test")
    must_contain(CANON, "Story duels stay standard TCG", "canon lost the story 8000 lock")
    must_contain(CANON, "non-story PvAI", "canon lost the DK PvAI opt-in")

    def table_laws_live(fid: str) -> bool:
        return fid.lower() == "dk"

    def can_opt_in(badges: set[str]) -> bool:
        return table_laws_live("dk") and "dk" in badges

    def try_apply(launch: str, badges: set[str], starting_lp: int, dk: bool):
        if launch == "StoryEra":
            return False, starting_lp, dk
        if not can_opt_in(badges):
            return False, starting_lp, dk
        return True, 2000, True

    cases = [
        ("Hub + badge", "Hub", {"dk"}, 8000, False, True, 2000, True),
        ("Hub no badge", "Hub", set(), 8000, False, False, 8000, False),
        ("Story + badge", "StoryEra", {"dk"}, 8000, False, False, 8000, False),
        ("Raid badge only", "Hub", {"raid"}, 8000, False, False, 8000, False),
    ]
    for name, launch, badges, lp, dk, exp_ok, exp_lp, exp_dk in cases:
        ok, out_lp, out_dk = try_apply(launch, badges, lp, dk)
        if ok != exp_ok or out_lp != exp_lp or out_dk != exp_dk:
            fail(
                f"gate {name}: got ok={ok} lp={out_lp} dk={out_dk} "
                f"want ok={exp_ok} lp={exp_lp} dk={exp_dk}"
            )

    print("dk overlay opt-in canon")
    print(f"  story stages {len(stages)} · all 8000 / !dkOverlay")
    print("  MapZone / Opponent / Create stay standard TCG")
    print("  Format Select PLAY gated on format.dk + TableLawsLive")
    print("  bootstrap refuses StoryEra overlay")
    print("  decision table 4/4")
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
