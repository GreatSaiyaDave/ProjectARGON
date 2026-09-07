#!/usr/bin/env python3
"""Guards Battle City hub overlay chrome.

Hub tiles are the template. Phone overlays must share HubChrome
(header capsule, dusk dim, body well, gold BACK) instead of a
one-off rectangular holo sheet.
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HUB = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/HubChrome.cs"
PRESENTER = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/DualMenuPresenter.cs"
SETTINGS = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/SettingsScreen.cs"
FORMATS = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/FormatSelectScreen.cs"
SCAN = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/SurfaceScanSheet.cs"
SYSTEMS = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/SystemsSheets.cs"
MENU_UI = ROOT / "Assets/Scripts/WRLDZ/UI/DuelDiskMenuUI.cs"


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def must_contain(path: Path, needle: str, why: str) -> None:
    if not path.is_file():
        fail(f"missing {path.relative_to(ROOT)}")
    text = path.read_text(encoding="utf-8")
    if needle not in text:
        fail(f"{path.relative_to(ROOT)} {why} (need `{needle}`)")


def main() -> int:
    must_contain(HUB, "public static class HubChrome", "lost HubChrome")
    must_contain(HUB, "HeaderBar", "lost overlay header capsule")
    must_contain(HUB, "FooterBack", "lost gold BACK footer")
    must_contain(HUB, "FeaturedCard", "lost featured format cards")
    must_contain(HUB, "ListRow", "lost dest-style list rows")
    must_contain(HUB, "OverlayDim", "lost dusk dim")

    must_contain(PRESENTER, "HubChrome.HeaderBar", "phone overlay is not hub chrome")
    must_contain(PRESENTER, "HubChrome.FooterBack", "phone overlay missing BACK")
    must_contain(PRESENTER, "HubChrome.BodyWell", "phone overlay missing body well")
    must_contain(PRESENTER, "HubChrome.OverlayDim", "phone overlay missing dusk dim")
    presenter = PRESENTER.read_text(encoding="utf-8")
    if "ImagineAssets.MenuHoloSheet()" in presenter and "BuildPhoneFrame" in presenter:
        # AR may still fall back to MenuHoloSheet; phone must not.
        phone = presenter.split("BuildPhoneFrame", 1)[1].split("BuildArFrame", 1)[0]
        if "MenuHoloSheet" in phone:
            fail("phone overlay still paints a rectangular MenuHoloSheet")

    must_contain(MENU_UI, "HubChrome.SectionCap", "hub no longer shares SectionCap")
    must_contain(MENU_UI, "HubChrome.LiftPlate", "hub no longer shares LiftPlate")
    must_contain(SETTINGS, "DualMenuPresenter.BuildFrame", "settings left the dual overlay chrome")
    must_contain(FORMATS, "HubChrome.FeaturedCard", "format cards are not hub featured plates")
    must_contain(SCAN, "HubChrome.Capsule", "scan CTAs are not hub capsules")
    must_contain(SYSTEMS, "HubChrome.ListRow", "story/bazaar/tome rows are not hub dest rows")

    ow = ROOT / "Assets/Scripts/WRLDZ/UI/OverworldUI.cs"
    must_contain(ow, "HubChrome.MountFeatured", "overworld Eye menu is not hub featured tiles")
    must_contain(ow, "HubChrome.MountDest", "overworld Eye menu is not hub dest tiles")
    must_contain(ow, "HubChrome.SectionCap", "overworld Eye menu missing DUEL/COMMAND caps")

    deck = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/DeckCollectionScreen.cs"
    must_contain(deck, "DualMenuPresenter.BuildFrame", "DECK still bypasses hub overlay chrome")

    print("hub overlay chrome")
    print("  HubChrome header / well / BACK present")
    print("  DualMenuPresenter phone frame uses hub grammar")
    print("  Settings, formats, scan, systems sheets share capsules")
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
