#!/usr/bin/env python3
"""Guards Battle City hub overlay chrome.

Hub tiles are dest buttons only. Phone overlays are compact quiet sheets
(header / well / BACK) so the map stays visible around them.
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
    must_contain(HUB, "QuietFill", "lost quiet overlay fill")
    must_contain(HUB, "FilamentRim", "lost KaibaCorp filament rim")
    must_contain(HUB, "PaintWell", "lost list/meter well painter")
    must_contain(HUB, "PaintChip", "lost short-row chip painter")

    header = HUB.read_text(encoding="utf-8").split("public static RectTransform HeaderBar", 1)[-1].split("public static RectTransform BodyWell", 1)[0]
    if "CornerTicks" in header:
        fail("overlay HeaderBar still paints L-corner ticks")
    if "TileHub" in header or "PaintPlate" in header:
        fail("overlay HeaderBar still uses dest-tile art")

    well = HUB.read_text(encoding="utf-8").split("public static RectTransform BodyWell", 1)[-1].split("public static RectTransform ListHead", 1)[0]
    if "CornerTicks" in well or "PaintPlate" in well:
        fail("overlay BodyWell still uses dest-tile art / ticks")

    must_contain(PRESENTER, "HubChrome.HeaderBar", "phone overlay is not hub chrome")
    must_contain(PRESENTER, "HubChrome.FooterBack", "phone overlay missing BACK")
    must_contain(PRESENTER, "HubChrome.BodyWell", "phone overlay missing body well")
    must_contain(PRESENTER, "HubChrome.OverlayDim", "phone overlay missing dusk dim")
    presenter = PRESENTER.read_text(encoding="utf-8")
    if "MenuHoloSheet" in presenter or "PanelMenuGlass" in presenter:
        fail("DualMenuPresenter still falls back to smoked MenuHoloSheet / PanelMenuGlass")
    must_contain(PRESENTER, "GetWindowAnchors", "phone overlay is full-screen binder chrome")
    if "HubChrome.PaintPlate" in presenter:
        fail("phone/AR overlay rim still uses dest-tile PaintPlate")

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
    must_contain(board, "HubChrome.PaintWell", "construction board is not a quiet well")

    ow_src = ow.read_text(encoding="utf-8")
    if "PanelMenuGlass()" in ow_src and "Eye" in ow_src:
        # Open Eye must not paint smoked menu glass.
        if "ApplyMenuGlassSprite" in ow_src and "PanelMenuGlass()" in ow_src.split("ApplyMenuGlassSprite", 1)[1][:800]:
            fail("Eye open sheet still uses PanelMenuGlass")
    if "RoundedRectSprite" in ow_src:
        fail("overworld currency / chips still use rounded-rect glass")
    must_contain(ow, "HubChrome.PaintWell", "overworld currency strip is not a quiet well")
    must_contain(ow, "HubChrome.PaintChip", "overworld wallet chips are not quiet chips")

    inv = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/InventoryScreen.cs"
    inv_src = inv.read_text(encoding="utf-8")
    if "plated: false" in inv_src:
        fail("BAG inspect / CREATE DECK still unplated")
    if "HubChrome.WellFill" in inv_src:
        fail("BAG wells still use translucent WellFill")
    must_contain(inv, "HubChrome.PaintWell", "BAG panes are not quiet wells")

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
            fail(f"{path.relative_to(ROOT)} {why} is not a quiet well")

    avatar = ROOT / "Assets/Scripts/WRLDZ/UI/AvatarCustomizerUI.cs"
    av_src = avatar.read_text(encoding="utf-8")
    if "MenuHoloSheet" in av_src or "PanelMenuGlass" in av_src:
        fail("avatar customizer still paints smoked MenuHoloSheet / PanelMenuGlass")
    must_contain(avatar, "HubChrome.PaintWell", "avatar sheet is not a quiet well")

    lab = ROOT / "Assets/Scripts/WRLDZ/UI/DesktopLabApp.cs"
    must_contain(lab, "HubChrome.Sheet", "desktop lab options are not a quiet sheet")
    must_contain(lab, "WrldzBuild.Log", "desktop lab is missing the owner BUILD log")

    build = ROOT / "Assets/Scripts/WRLDZ/Core/WrldzBuild.cs"
    must_contain(build, "CLEAN-0909", "lost owner-visible BUILD stamp")
    must_contain(ROOT / "GET_THE_GAME.txt", "Unity Cloud", "lost Unity Cloud vs GitHub icon note")
    must_contain(ROOT / "GET_THE_GAME.txt", "main.zip", "lost owner zip fallback")
    must_contain(ROOT / "Assets/WRLDZ_BUILD.txt", "CLEAN-0909", "lost Unity Project BUILD file")
    must_contain(
        ROOT / ".cursor/skills/owner-linux-unity/SKILL.md",
        "Do not give `git pull` as the only step",
        "lost owner-linux-unity skill",
    )
    must_contain(
        ROOT / "Assets/Editor/WRLDZ/GitHubPullMenu.cs",
        "Get Latest from GitHub",
        "lost in-Editor GitHub pull menu",
    )
    must_contain(
        ROOT / "Assets/Editor/WRLDZ/GitHubPullMenu.cs",
        "Send this folder to GitHub",
        "lost in-Editor GitHub send menu",
    )
    must_contain(
        ROOT / "GET_THE_GAME.txt",
        "send_this_folder_to_github.sh",
        "lost owner send-to-GitHub one-liner",
    )
    must_contain(
        ROOT / "GET_THE_GAME.txt",
        "unstick_merge_keep_github.sh",
        "lost owner unstick-merge one-liner",
    )
    must_contain(
        ROOT / "GET_THE_GAME.txt",
        "restore_pc_folder_from_before_unstick.sh",
        "lost owner restore-local-folder one-liner",
    )
    restore = ROOT / "Tools/restore_pc_folder_from_before_unstick.sh"
    if not restore.is_file():
        fail("missing Tools/restore_pc_folder_from_before_unstick.sh")
    restore_text = restore.read_text(encoding="utf-8")
    if "git merge --abort" not in restore_text:
        fail("restore script lost merge-abort")
    if "Restore starting" not in restore_text:
        fail("restore script lost startup print (silent-exit guard)")
    if "Removing GitHub leftover" not in restore_text:
        fail("restore script lost GitHub leftover cleanup")
    if "Library_mix_" not in restore_text:
        fail("restore script lost Library mix move")
    if "wrldz-restore-report.txt" not in restore_text:
        fail("restore script lost report path")
    if "Get Latest from GitHub" not in restore_text:
        fail("restore script lost Get Latest warning")
    if "git push" in restore_text:
        fail("restore script must not push to GitHub")
    must_contain(
        ROOT / "GET_THE_GAME.txt",
        "-o /tmp/wrldz-restore.sh",
        "lost owner restore download-then-run one-liner",
    )

    vc = (ROOT / "ProjectSettings/VersionControlSettings.asset").read_text(encoding="utf-8")
    if "Unity Version Control" in vc:
        fail("project Version Control is still Plastic/UVCS (Hub 'synced' != GitHub)")
    if "Visible Meta Files" not in vc:
        fail("project Version Control is not Visible Meta Files (git)")
    menu = ROOT / "Assets/Editor/WRLDZ/DesktopLabMenu.cs"
    must_contain(menu, "PrefSkipBootCascade", "Lab menu no longer skips splash into Desktop Lab")
    title = ROOT / "Assets/Scripts/WRLDZ/UI/BootFlowUI.cs"
    must_contain(title, "WrldzBuild.Log", "title screen is missing BUILD log")

    print("hub overlay chrome")
    print("  HubChrome header / well / BACK present")
    print("  DualMenuPresenter compact Solid Vision slates (map visible)")
    print("  BAG / ARTIFACTS / scan / lab / avatar use quiet wells")
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
