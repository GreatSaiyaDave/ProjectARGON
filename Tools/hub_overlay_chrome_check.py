#!/usr/bin/env python3
"""Guards Battle City hub overlay chrome.

Hub tiles are dest buttons only. Phone overlays are compact quiet sheets
(header / well / BACK) so the map stays visible around them.
"""
from __future__ import annotations

import re
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
MENU_V6 = ROOT / "Assets/Scripts/WRLDZ/UI/MenuV6"


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
    # P3 (BUILD_PLAN §3, §11 F25): HeaderBar painted its L-ticks through QuietFill(..., millennia: true); a regex, so the
    # spelling millennia:true (no space) cannot slip past a literal needle
    if re.search(r"millennia\s*:\s*true", header):
        fail("overlay HeaderBar still paints L-corner ticks (QuietFill millennia: true)")
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

    # The v6.3 Stone main menu (BUILD_PLAN P1a §3 rows): its labels live in the file that builds them, the legacy
    # menu stays the fallback, local-only art is reached only through MenuV6Art, and that folder is never committed.
    stage = MENU_V6 / "MenuV6Stage.cs"
    for label in ("DUEL", "DECK", "BAG", "STORY", "BAZAAR", "WORLD MAP", "QUIT", "SETTINGS", "VIEW"):
        must_contain(stage, f'"{label}"', f"lost the main-menu label {label}")
    must_contain(stage, "HubChrome.SectionCap", "Stone season caption is not the shared SectionCap")
    must_contain(stage, "HubChrome.LiftPlate", "Stone avatar frame is not the shared LiftPlate")
    must_contain(MENU_UI, "MenuV6Stage.WhyNot", "main menu lost its v6 gate (portrait / kill switch / no art)")
    must_contain(MENU_UI, "BuildVisualsLegacy", "main menu lost its legacy fallback (never a blank home)")
    for cs in sorted(MENU_V6.glob("*.cs")):
        src = cs.read_text(encoding="utf-8")
        if "StreamingSprite" in src:
            fail(f"{cs.relative_to(ROOT)} loads menu art through StreamingSprite (RGBA32, keyed corners)")
        if re.search(r"\bSystem\.IO\b", src):
            fail(f"{cs.relative_to(ROOT)} reads files directly (v6 art goes through Resources.Load)")
        # P1b (D3): the idle rigs draw on UI/Default; P5 adds exactly ONE shader, the cel smoke's, made only by SmokeField
        if re.search(r"\bShader\.Find\b|\bnew\s+Material\s*\(", src) and cs.name != "SmokeField.cs":
            fail(f"{cs.relative_to(ROOT)} makes its own shader / material (D3: UI/Default, the cel smoke's one shader excepted)")
        # AGENTS.md: always WrldzInput, never Input.GetKey (the back key, P5)
        if re.search(r"\bInput\.Get(Key|Button|Mouse)", src):
            fail(f"{cs.relative_to(ROOT)} reads UnityEngine.Input directly (use WrldzInput)")
    # P1b: the portrait plan builds the Stone, and a rig that cannot build keeps its layer's P1a still
    must_contain(stage, "MenuLayoutKind.Portrait", "the Stone lost its portrait plan (P1b)")
    must_contain(stage, "RigsKept", "a rig that cannot build no longer keeps its P1a still")
    for folder in (ROOT / "Assets/Scripts", ROOT / "Assets/Editor"):
        for cs in sorted(folder.rglob("*.cs")):
            if cs == MENU_V6 / "MenuV6Art.cs":
                continue
            if "MenuV6Local/" in cs.read_text(encoding="utf-8", errors="replace"):
                fail(f"{cs.relative_to(ROOT)} names MenuV6Local/ (local-only art goes through MenuV6Art.LocalOr)")
    ignore = (ROOT / ".gitignore").read_text(encoding="utf-8").splitlines() if (ROOT / ".gitignore").is_file() else []
    for line in ("Assets/Resources/WRLDZ/MenuV6Local/", "Assets/Resources/WRLDZ/MenuV6Local.meta"):
        if line not in (l.strip() for l in ignore):
            fail(f".gitignore does not ignore {line} (local-only art, D7 / F12)")
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

    # P2 (BUILD_PLAN §6 P2, D8-D11): the v6.3 title behind its gate with the legacy cascade as the fallback (the credit
    # text kept for SETTINGS -> CREDITS), the D8 handoff and the shared persistence, the menu's entry token, the live
    # cracks on a MaskableGraphic with the baked plates as their fallback, and the edit-mode boot check in the smoke test
    must_contain(title, "TitleStage.WhyNot", "boot lost its v6 title gate (kill switch / no data -> legacy cascade)")
    must_contain(title, "void BuildCredits", "boot lost the credits (the legacy fallback; SETTINGS -> CREDITS, P3)")
    must_contain(title, "Story & design", "boot lost the credit text")
    must_contain(title, "MenuEntry.KeepTitle", "boot lost the D8 handoff (the kept title canvas)")
    must_contain(title, "PersistForWorld", "boot lost the shared world-load persistence")
    must_contain(ROOT / "Assets/Scripts/WRLDZ/Core/MainMenuBootstrap.cs", "MenuEntry.Take", "the menu lost its entry token")
    must_contain(MENU_V6 / "CrackNetGraphic.cs", "MaskableGraphic", "the cracks left UGUI (CrackNetGraphic : MaskableGraphic)")
    must_contain(stage, "LiveCracks", "the Stone lost its live cracks (D11)")
    smoke = ROOT / "Assets/Editor/WRLDZ/MenuSmokeTest.cs"
    must_contain(smoke, '"BootCanvas"', "MenuSmokeTest no longer cleans up the boot canvas (§11 F18)")
    must_contain(smoke, "BuildAndRun", "MenuSmokeTest lost the edit-mode title build check (§11 F18)")

    # P3 (BUILD_PLAN §6 P3, D5 / §11 F5, D12, D14, D19): the Sanctuary painters behind the marker (not a static scope), every
    # overlay sheet in the false door with its synchronous close + FrameGhost, the Stone's own sheets, the QUIT fold copy with
    # the sign-out line, SETTINGS -> CREDITS, the deck builder kept as it is until P4, AvatarCustomizerUI never in the shared frame
    hub_src = HUB.read_text(encoding="utf-8")
    for painter in ("HeaderBar", "BodyWell", "FooterBack", "OverlayDim", "QuietFill", "FilamentRim", "ListRow", "Capsule", "PaintWell"):
        if not re.search(r"public static \w+ " + painter + r"\(", hub_src):
            fail(f"HubChrome lost the painter {painter} (names and signatures are frozen)")
    if hub_src.count("SanctuaryPaint.On(") < 6:
        fail("HubChrome painters no longer check the SanctuaryChrome marker (§11 F5)")
    if "using (HubChrome.Sanctuary" in hub_src or "static IDisposable Sanctuary" in hub_src:
        fail("HubChrome uses a static Sanctuary scope (§11 F5: the marker component, never a static scope)")
    must_contain(MENU_V6 / "SanctuaryChrome.cs", "GetComponentInParent<SanctuaryChrome>", "the Sanctuary marker is not a GetComponentInParent check")
    shell = ROOT / "Assets/Scripts/WRLDZ/UI/Shell/MenuShell.cs"
    must_contain(shell, "SanctuaryChrome.Mark(ModalHost.gameObject", "the overlay ModalHost lost its SanctuaryChrome marker")
    must_contain(shell, "id != MenuId.DeckCollection", "the deck builder no longer keeps today's look in P3 (§11 F3: its switch is P4)")
    must_contain(shell, "SheetCredits.AddRow", "SETTINGS lost its CREDITS row (D9)")
    must_contain(PRESENTER, "FalseDoorFrame.AnyRect", "the overlay sheets lost the v6.3 false door")
    must_contain(PRESENTER, "FrameGhost.Play", "a closing sheet no longer plays its FrameGhost (D19)")
    must_contain(PRESENTER, "FloatingPanel.DestroyDeferred(frame.Root.gameObject)", "DualMenuPresenter.Dismiss no longer closes the frame at once (D19)")
    for name in ("FalseDoorFrame", "FrameGhost", "ModeCards", "RecessSheet", "RingSheet", "SanctuaryChrome"):
        if not (MENU_V6 / f"{name}.cs").is_file():
            fail(f"missing Assets/Scripts/WRLDZ/UI/MenuV6/{name}.cs (P3)")
    for build in ("ModeCards.Build", "RecessSheet.Build", "RingSheet.Build", "QuitSheet.Build"):
        must_contain(MENU_UI, build, f"the Stone lost its {build.split('.')[0]} sheet")
    quit_src = (MENU_V6 / "RecessSheet.cs").read_text(encoding="utf-8")
    for needle, why in (('"Fold your Spirit Dueler?"', "QUIT lost its fold question (D14)"),
                        ('"Your progress is saved. The Duel Disk powers down and you return to the gate."', "QUIT lost the sign-out line (§11 F15)"),
                        ('"STAY"', "QUIT lost STAY"), ('"FOLD"', "QUIT lost FOLD")):
        if needle not in quit_src:
            fail(why)
    must_contain(title, "public const string CreditsText", "the boot's credit text is not shared with SETTINGS -> CREDITS")
    must_contain(MENU_V6 / "SheetCredits.cs", "BootFlowUI.CreditsText", "SETTINGS -> CREDITS does not show the boot's own credit text")
    if "DualMenuPresenter.BuildFrame" in av_src:
        fail("AvatarCustomizerUI takes the shared frame (§12: it belongs to the Player customization session)")
    must_contain(smoke, "MenuV6 sheets", "MenuSmokeTest lost the P3 sheet checks")

    # P5 (BUILD_PLAN §3 "No fifth UI shader" row, §5.6, D3): the one new UI shader is hand-written HLSL in the menu's own
    # folder (no ShaderGraph, no VFX Graph), only SmokeField finds it, and EVERY WRLDZ/UI shader ships: a shader reached
    # only through Shader.Find is stripped from a player build unless its GUID is in m_AlwaysIncludedShaders.
    cel = MENU_V6 / "Shaders/MenuCelSmoke.shader"
    if not cel.is_file():
        fail("missing Assets/Scripts/WRLDZ/UI/MenuV6/Shaders/MenuCelSmoke.shader (P5: the cel smoke, D3)")
    must_contain(cel, 'Shader "WRLDZ/UI/MenuCelSmoke"', "the cel smoke shader lost its name")
    for bad in list(MENU_V6.rglob("*.shadergraph")) + list(MENU_V6.rglob("*.vfx")) + list(MENU_V6.rglob("*.shadersubgraph")):
        fail(f"{bad.relative_to(ROOT)}: no ShaderGraph / VFX Graph in the menu (hand-written HLSL only)")
    must_contain(MENU_V6 / "SmokeField.cs", '"WRLDZ/UI/MenuCelSmoke"', "SmokeField does not find the cel smoke shader by name")
    must_contain(MENU_V6 / "SmokeField.cs", "WrldzTheme.BatterySaver", "the live smoke ignores Battery Saver (SPEC §8.7: a static plate)")
    must_contain(MENU_V6 / "MenuBack.cs", "WrldzInput.KeyDown(KeyCode.Escape)", "the back key does not go through WrldzInput")
    must_contain(MENU_V6 / "SafeArea.cs", "Screen.safeArea", "the v6 screens ignore Screen.safeArea (P5)")
    must_contain(ROOT / "Assets/Editor/WRLDZ/MenuV6ShipSettings.cs", "ShaderUtil.ShaderHasError",
                 "the Always Included step no longer gates on ShaderUtil.ShaderHasError (§5.6)")
    ui_shaders = []
    for sh in sorted((ROOT / "Assets/Scripts/WRLDZ/UI").rglob("*.shader")):
        m = re.search(r'Shader\s+"(WRLDZ/UI/[^"]+)"', sh.read_text(encoding="utf-8", errors="replace"))
        if not m:
            continue
        meta = sh.with_name(sh.name + ".meta")
        g = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", meta.read_text(encoding="utf-8", errors="replace"), re.M) if meta.is_file() else None
        if not g:
            fail(f"{sh.relative_to(ROOT)} has no .meta guid")
        ui_shaders.append((m.group(1), g.group(1)))
    gfx = ROOT / "ProjectSettings/GraphicsSettings.asset"
    if not gfx.is_file():
        fail("missing ProjectSettings/GraphicsSettings.asset (the Always Included Shaders list)")
    gm = re.search(r"m_AlwaysIncludedShaders:\s*\n((?:[ \t]*- \{[^\n]*\}[ \t]*\n)*)", gfx.read_text(encoding="utf-8", errors="replace"))
    block = gm.group(1) if gm else ""
    for name, g in ui_shaders:
        if f"guid: {g}, type: 3" not in block:
            fail(f"{name} (guid {g}) is not in GraphicsSettings m_AlwaysIncludedShaders: a player build strips it "
                 "(BUILD_PLAN §5.6; run WRLDZ.EditorTools.MenuV6ShipSettings.Apply in the Editor)")

    print("hub overlay chrome")
    print("  HubChrome header / well / BACK present")
    print("  DualMenuPresenter compact Solid Vision slates (map visible)")
    print("  BAG / ARTIFACTS / scan / lab / avatar use quiet wells")
    print("  main menu: v6.3 Stone labels + legacy fallback; MenuV6Local only via MenuV6Art, gitignored")
    print("  main menu: portrait plan, rigs on UI/Default (no own shader), P1a stills kept as the rigs' fallback")
    print("  boot: v6.3 title gate + legacy cascade, D8 handoff, entry token, live cracks, edit-mode title smoke")
    print("  sheets: Sanctuary painters behind the marker, false door + FrameGhost, the Stone's sheets, QUIT fold, CREDITS")
    print(f"  P5: one hand-written menu shader (MenuCelSmoke), {len(ui_shaders)} WRLDZ/UI shaders in Always Included, back key via WrldzInput, safe area")
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
