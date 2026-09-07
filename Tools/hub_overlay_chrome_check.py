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

    must_contain(HUB, "PaintPlate", "lost opaque hub plate painter")
    must_contain(HUB, "CornerTicks", "lost hub L-corner ticks")
    must_contain(HUB, "FlattenPlate", "lost short-row plate flatten")
    must_contain(HUB, "PaintWell", "lost list/meter well painter")
    must_contain(HUB, "PaintChip", "lost short-row chip painter")

    must_contain(PRESENTER, "HubChrome.HeaderBar", "phone overlay is not hub chrome")
    must_contain(PRESENTER, "HubChrome.FooterBack", "phone overlay missing BACK")
    must_contain(PRESENTER, "HubChrome.BodyWell", "phone overlay missing body well")
    must_contain(PRESENTER, "HubChrome.OverlayDim", "phone overlay missing dusk dim")
    presenter = PRESENTER.read_text(encoding="utf-8")
    if "MenuHoloSheet" in presenter or "PanelMenuGlass" in presenter:
        fail("DualMenuPresenter still falls back to smoked MenuHoloSheet / PanelMenuGlass")
    must_contain(PRESENTER, "HubChrome.PaintPlate", "AR overlay rim is not a hub plate")

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
    must_contain(deck, "HubChrome.PaintPlate", "DECK chrome is not hub plates")
    deck_src = deck.read_text(encoding="utf-8")
    if "PanelMenuGlass()" in deck_src:
        fail("DECK still uses smoked PanelMenuGlass")
    if "RoundedRectSprite" in deck_src:
        fail("DECK still uses rounded-rect glass chips")

    board = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/DeckConstructionBoard.cs"
    board_src = board.read_text(encoding="utf-8")
    if "PanelMenuGlass()" in board_src:
        fail("construction board still uses smoked PanelMenuGlass")
    must_contain(board, "HubChrome.PaintPlate", "construction board is not hub plates")

    ow_src = ow.read_text(encoding="utf-8")
    if "PanelMenuGlass()" in ow_src and "Eye" in ow_src:
        # Open Eye must not paint smoked menu glass.
        if "ApplyMenuGlassSprite" in ow_src and "PanelMenuGlass()" in ow_src.split("ApplyMenuGlassSprite", 1)[1][:800]:
            fail("Eye open sheet still uses PanelMenuGlass")
    if "RoundedRectSprite" in ow_src:
        fail("overworld currency / chips still use rounded-rect glass")
    must_contain(ow, "HubChrome.PaintWell", "overworld currency strip is not a hub plate")
    must_contain(ow, "HubChrome.PaintChip", "overworld wallet chips are not hub chips")

    inv = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/InventoryScreen.cs"
    inv_src = inv.read_text(encoding="utf-8")
    if "plated: false" in inv_src:
        fail("BAG inspect / CREATE DECK still unplated")
    if "HubChrome.WellFill" in inv_src:
        fail("BAG wells still use translucent WellFill")
    must_contain(inv, "HubChrome.PaintWell", "BAG panes are not hub plates")

    art = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/ArtifactBoxScreen.cs"
    art_src = art.read_text(encoding="utf-8")
    if "plated: false" in art_src:
        fail("ARTIFACTS filters still unplated")
    if "HubChrome.WellFill" in art_src:
        fail("ARTIFACTS scroll still uses translucent WellFill")
    must_contain(art, "HubChrome.Sheet", "ARTIFACTS inspect is not a hub sheet")

    for path, why in (
        (SCAN, "scan meter well"),
        (SYSTEMS, "story/bazaar list well"),
        (ROOT / "Assets/Scripts/WRLDZ/UI/Shell/FreeViewScreen.cs", "FREE VIEW catalog well"),
        (ROOT / "Assets/Scripts/WRLDZ/UI/TournamentRoomScreen.cs", "TOURNEY list well"),
    ):
        src = path.read_text(encoding="utf-8")
        if "HubChrome.WellFill" in src:
            fail(f"{path.relative_to(ROOT)} {why} still uses translucent WellFill")
        if "HubChrome.PaintWell" not in src:
            fail(f"{path.relative_to(ROOT)} {why} is not a hub plate")

    avatar = ROOT / "Assets/Scripts/WRLDZ/UI/AvatarCustomizerUI.cs"
    av_src = avatar.read_text(encoding="utf-8")
    if "MenuHoloSheet" in av_src or "PanelMenuGlass" in av_src:
        fail("avatar customizer still paints smoked MenuHoloSheet / PanelMenuGlass")
    must_contain(avatar, "HubChrome.PaintPlate", "avatar sheet is not a hub plate")

    lab = ROOT / "Assets/Scripts/WRLDZ/UI/DesktopLabApp.cs"
    must_contain(lab, "HubChrome.Sheet", "desktop lab options are not a hub sheet")

    print("hub overlay chrome")
    print("  HubChrome header / well / BACK present")
    print("  DualMenuPresenter phone + AR frames use hub plates")
    print("  BAG / ARTIFACTS / scan / lab / avatar share plates")
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
