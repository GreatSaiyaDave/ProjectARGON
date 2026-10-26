using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI;
using WRLDZ.UI.MenuV6;
using WRLDZ.UI.Shell;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Instantiates every production menu sheet (phone + AR holo) and reports exceptions.
    /// </summary>
    public static class MenuSmokeTest
    {
        [MenuItem("WRLDZ/Lab/Run Menu Smoke Test")]
        public static void RunInteractive()
        {
            var report = Run();
            EditorUtility.DisplayDialog(
                report.Ok ? "Menu Smoke PASS" : "Menu Smoke FAIL",
                Truncate(report.Summary, 1600),
                "OK");
        }

        public static void RunBatch()
        {
            var report = Run();
            Debug.Log(report.Ok
                ? "[WRLDZ MENU SMOKE] PASS\n" + report.Summary
                : "[WRLDZ MENU SMOKE] FAIL\n" + report.Summary);
            EditorApplication.Exit(report.Ok ? 0 : 1);
        }

        /// <summary>
        /// Edit-mode safe: on-hand switcher list only (no DontDestroyOnLoad session).
        /// Batch: -executeMethod WRLDZ.EditorTools.MenuSmokeTest.RunDeckSwitchBatch
        /// </summary>
        public static void RunDeckSwitchBatch()
        {
            var report = RunDeckSwitch();
            Debug.Log(report.Ok
                ? "[WRLDZ DECK SWITCH SMOKE] PASS\n" + report.Summary
                : "[WRLDZ DECK SWITCH SMOKE] FAIL\n" + report.Summary);
            EditorApplication.Exit(report.Ok ? 0 : 1);
        }

        public struct Report
        {
            public bool Ok;
            public int Checks;
            public int Failures;
            public string Summary;
        }

        public static Report Run()
        {
            var sb = new StringBuilder();
            var checks = 0;
            var fails = 0;

            void Check(string name, Action act)
            {
                checks++;
                try
                {
                    act();
                    sb.AppendLine("  ok  " + name);
                }
                catch (Exception ex)
                {
                    fails++;
                    sb.AppendLine("  FAIL " + name + " — " + ex.GetType().Name + ": " + ex.Message);
                    Debug.LogException(ex);
                }
            }

            // the menu build's own checks (P1a-P5) build menus and press buttons in edit mode: a press that reaches
            // WrldzAudio.Ensure / ScreenRouter.Ensure leaves its root behind (DontDestroyOnLoad throws outside Play), a build
            // can leave a canvas. P5 (the P4 landing: 70 strays in the open scene): each of these checks removes EVERY scene
            // root it created (the roots are listed before it runs; nothing that was there before is touched). The other
            // checks in this file belong to their own sessions and keep their own cleanup.
            void OurCheck(string name, Action act) => Check(name, () =>
            {
                var before = RootSet();
                try { act(); }
                finally { DestroyNewRoots(before); }
            });

            sb.AppendLine("WRLDZ menu smoke");
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();

            try { AppSession.Ensure().EnsureLabTestAccount(out _); }
            catch (Exception ex) { sb.AppendLine("  warn lab account: " + ex.Message); }

            var canvas = WrldzTheme.Canvas("MenuSmokeCanvas", 50);
            var host = new GameObject("SmokeHost", typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(canvas, false);
            FloatingPanel.Stretch(host);

            // P6 (the P5 landing left 29 stray roots in the open scene): the menu shell's own checks (MenuShell, the hub) also
            // remove the roots they make — ScreenRouter.Ensure's DontDestroyOnLoad throws in edit mode and leaves its object
            // behind on every call. Same checks, same verdicts; the other sessions' checks below keep their own cleanup.
            OurCheck("MenuShell.CreateOverlay", () =>
            {
                var shell = MenuShell.CreateOverlay(90);
                if (shell == null || shell.ModalHost == null)
                    throw new Exception("overlay shell missing ModalHost");
            });

            OurCheck("MenuShell.Create (full hub)", () =>
            {
                var shell = MenuShell.Create(91);
                if (shell == null || shell.ContentHost == null)
                    throw new Exception("full shell missing ContentHost");
            });

            OurCheck("DuelDiskMenuUI.Build", () =>
            {
                var go = new GameObject("HubSmoke");
                go.AddComponent<DuelDiskMenuUI>().Build();
            });

            // The v6.3 Stone (BUILD_PLAN P1a §3 rows): the nine labels, the Tiles host, the old control names, a
            // clone without the local-only art, a builder pass with no art at all, and the legacy fallback.
            OurCheck("Main menu v6 Stone: labels, Tiles, control names (1920x1080)", () =>
            {
                RequireMenuV6Art();
                WithHub("HubSmokeV6", 1920, 1080, menu =>
                {
                    if (!menu.IsMenuV6)
                        throw new Exception("v6 path not taken: " + MenuV6Stage.WhyNot(MenuLayoutKind.Landscape));
                    RequireMainMenu(menu.MenuCanvas, MainMenuV6Labels);
                    foreach (var n in MainMenuControls)
                        if (FindNamed(menu.MenuCanvas, n) == null)
                            throw new Exception("control " + n + " missing");
                });
            });
            OurCheck("Main menu v6 Stone with ForceNoLocal (a clone without the local-only art)", () =>
            {
                RequireMenuV6Art();
                try
                {
                    MenuV6Art.ForceNoLocal = true;
                    WithHub("HubSmokeV6Local", 1920, 1080, menu =>
                    {
                        if (!menu.IsMenuV6)
                            throw new Exception("v6 path not taken: " + MenuV6Stage.WhyNot(MenuLayoutKind.Landscape));
                        RequireMainMenu(menu.MenuCanvas, MainMenuV6Labels);
                    });
                    if (MenuV6Stage.HasArt(MenuLayoutKind.Portrait))
                        WithHub("HubSmokeV6LocalPort", 1080, 2340, menu =>
                        {
                            if (!menu.IsMenuV6)
                                throw new Exception("portrait v6 path not taken: " + MenuV6Stage.WhyNot(MenuLayoutKind.Portrait));
                            RequireMainMenu(menu.MenuCanvas, MainMenuV6Labels);
                        });
                }
                finally
                {
                    MenuV6Art.ForceNoLocal = false;
                    MenuV6Art.ClearCache();
                }
            });
            OurCheck("Main menu v6 builder with no art does not throw; the hub falls back", () =>
            {
                RectTransform canvas = null, canvasPort = null;
                try
                {
                    MenuV6Art.ForceNoArt = true;
                    canvas = WrldzTheme.Canvas("MenuV6NoArtCanvas", 50, aspect: true);
                    MenuV6Stage.Build(canvas, new MenuV6Stage.Handlers(), MenuLayoutKind.Landscape);
                    RequireMainMenu(canvas, MainMenuV6Labels);
                    canvasPort = WrldzTheme.Canvas("MenuV6NoArtCanvasPort", 50, aspect: true);
                    MenuV6Stage.Build(canvasPort, new MenuV6Stage.Handlers(), MenuLayoutKind.Portrait);
                    RequireMainMenu(canvasPort, MainMenuV6Labels);
                    WithHub("HubSmokeV6NoArt", 1920, 1080, menu =>
                    {
                        if (menu.IsMenuV6) throw new Exception("v6 path taken with no art");
                        RequireMainMenu(menu.MenuCanvas, MainMenuLegacyLabels);
                    });
                }
                finally
                {
                    MenuV6Art.ForceNoArt = false;
                    MenuV6Art.ClearCache();
                    if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                    if (canvasPort != null) UnityEngine.Object.DestroyImmediate(canvasPort.gameObject);
                }
            });
            // P1b: the portrait plan builds the Stone too (its first cut); the legacy portrait menu stays the fallback.
            OurCheck("Main menu v6 portrait Stone: labels, Tiles, control names (1080x2340)", () =>
            {
                if (!MenuV6Stage.HasArt(MenuLayoutKind.Portrait))
                    throw new Exception("portrait v6 art missing under Resources/" + MenuV6Art.Root + " (the P1b art set is not installed)");
                WithHub("HubSmokeV6Port", 1080, 2340, menu =>
                {
                    if (!menu.IsMenuV6)
                        throw new Exception("portrait v6 path not taken: " + MenuV6Stage.WhyNot(MenuLayoutKind.Portrait));
                    RequireMainMenu(menu.MenuCanvas, MainMenuV6Labels);
                    foreach (var n in MainMenuControls)
                        if (FindNamed(menu.MenuCanvas, n) == null)
                            throw new Exception("control " + n + " missing");
                });
            });
            // P1b: every idle rig builds in both plans (none keeps its P1a still; the box needs the local renders), and every
            // control's hover / press state draws without throwing.
            OurCheck("Main menu v6 rigs build and every hover / press state draws (1920x1080, 1080x2340)", () =>
            {
                foreach (var (w, h, kind) in new[] { (1920, 1080, MenuLayoutKind.Landscape), (1080, 2340, MenuLayoutKind.Portrait) })
                {
                    if (!MenuV6Stage.HasArt(kind)) throw new Exception(kind + " v6 art missing");
                    WithHub("HubSmokeV6Rigs", w, h, menu =>
                    {
                        var stone = menu.Stone;
                        if (stone == null) throw new Exception(kind + ": v6 path not taken");
                        if (stone.Life == null) throw new Exception(kind + ": no MenuV6Life");
                        var kept = new System.Collections.Generic.List<string>(stone.RigsKept);
                        var boxLocal = MenuV6Art.HasLocal(kind == MenuLayoutKind.Landscape
                            ? MenuV6Art.LocalRoot + "Rigs/Land/box/full" : MenuV6Art.LocalRoot + "Rigs/Port/box/full");
                        if (!boxLocal) kept.Remove("box");
                        if (kept.Count > 0) throw new Exception(kind + ": rigs kept the P1a still: " + string.Join(", ", kept));
                        foreach (var n in new[] { "Hero", "Dest_DECK", "Dest_BAG", "Dest_STORY", "Dest_BAZAAR", "Tert_VIEW", "Tert_SET", "MapBack", "Quit", "DeckPlaque", "RingEcho" })
                        {
                            if (!stone.ForceState(n, 0.12, double.NaN)) throw new Exception(kind + ": no hover state for " + n);
                            stone.ForceState(n, 1.0, 0.05);
                            stone.ForceState(n, double.NaN, double.NaN);
                        }

                        if (stone.Life.Dropped.Count > 0)
                            throw new Exception(kind + ": rigs threw while drawing: " + string.Join(", ", stone.Life.Dropped));
                    });
                }
            });
            // The legacy portrait menu (the fallback when the v6 path is off: kill switch, no art, a build error) is today's
            // menu unchanged. The check keeps its P1a name (the landing compares names with a baseline run); since P1b the
            // portrait default is the v6 Stone, so the fallback is reached with the test-only MenuV6Stage.ForceLegacy.
            OurCheck("Main menu portrait (1080x2340) is today's legacy menu", () =>
            {
                try
                {
                    MenuV6Stage.ForceLegacy = true;
                    WithHub("HubSmokeLegacy", 1080, 2340, menu =>
                    {
                        if (menu.IsMenuV6) throw new Exception("portrait took the v6 path with the v6 path off");
                        RequireMainMenu(menu.MenuCanvas, MainMenuLegacyLabels);
                    });
                }
                finally
                {
                    MenuV6Stage.ForceLegacy = false;
                }
            });

            // P2 (BUILD_PLAN §6 P2, §11 F18): the boot builds the v6.3 title's settled idle in edit mode without throwing
            // (no coroutine, no video, no RenderTexture), with and without the local-only art; the legacy cascade still
            // builds; the Stone draws its live cracks and its entrance (cold open, boot handoff) runs and returns to rest.
            OurCheck("Boot title v6: BootFlowUI.BuildAndRun builds the settled idle (1920x1080, 1080x2340)", () =>
            {
                foreach (var (w, h) in new[] { (1920, 1080), (1080, 2340) })
                {
                    var kind = w >= h ? MenuLayoutKind.Landscape : MenuLayoutKind.Portrait;
                    if (TitleStage.WhyNot(kind) != null)
                        throw new Exception(kind + ": " + TitleStage.WhyNot(kind) + " (the P2 art set is not installed)");
                    foreach (var noLocal in new[] { false, true })
                    {
                        try
                        {
                            MenuAspect.Force(w, h);
                            MenuV6Art.ForceNoLocal = noLocal;
                            MenuV6Art.ClearCache();
                            DestroyNamed("BootCanvas");
                            var go = new GameObject("BootFlowHostSmoke");
                            go.AddComponent<BootFlowUI>().BuildAndRun();
                            var bc = GameObject.Find("BootCanvas");
                            if (bc == null) throw new Exception("no BootCanvas");
                            var ts = bc.GetComponentInChildren<TitleStage>(true);
                            if (ts == null) throw new Exception(kind + (noLocal ? " ForceNoLocal" : "") + ": no TitleStage (the legacy cascade ran)");
                            if (!ts.IsLive || Math.Abs(ts.Raw - TitleStage.SettledRaw) > 1e-3)
                                throw new Exception("the title is not the settled idle (raw " + ts.Raw + ")");
                            if (ts.Tears == null || ts.Tears.VertexCount <= 0) throw new Exception("the Tears did not draw");
                            if (FindNamed(ts.transform, "Dom_touch") == null) throw new Exception("TOUCH TO BEGIN missing");
                            if (ts.Missing.Count > 0) throw new Exception("missing: " + string.Join(", ", ts.Missing));
                        }
                        finally
                        {
                            MenuV6Art.ForceNoLocal = false;
                            MenuV6Art.ClearCache();
                            MenuAspect.ClearForce();
                            DestroyNamed("BootCanvas");
                            DestroyNamed("BootFlowHostSmoke");
                        }
                    }
                }
            });

            OurCheck("Boot legacy cascade (v6 title off) still builds", () =>
            {
                try
                {
                    TitleStage.ForceLegacy = true;
                    var go = new GameObject("BootFlowHostSmoke");
                    go.AddComponent<BootFlowUI>().BuildAndRun();
                    var bc = GameObject.Find("BootCanvas");
                    if (bc == null) throw new Exception("no BootCanvas");
                    if (bc.GetComponentInChildren<TitleStage>(true) != null) throw new Exception("v6 title built with the v6 path off");
                }
                finally
                {
                    TitleStage.ForceLegacy = false;
                    DestroyNamed("BootCanvas");
                    DestroyNamed("BootFlowHostSmoke");
                }
            });

            OurCheck("Main menu v6 live cracks + the entrance (cold open, boot handoff) return to rest", () =>
            {
                foreach (var (w, h) in new[] { (1920, 1080), (1080, 2340) })
                    WithHub("HubSmokeV6Entrance", w, h, menu =>
                    {
                        var stone = menu.Stone;
                        if (stone == null) throw new Exception(w + "x" + h + ": v6 path not taken");
                        if (stone.Cracks == null || stone.Cracks.VertexCount <= 0)
                            throw new Exception(w + "x" + h + ": the live cracks did not draw (the baked plate is showing)");
                        stone.ProbeEntrance(false, 0, .3, .9, 1.2, 1.6, 1.95, 2.3, 2.9);
                        stone.ProbeEntrance(true, 0, .2, .5, .9, 1.1, 1.4, 1.7, 1.9, 2.2, 2.5, 2.9, 3.1, 3.3);
                        if (stone.Entrance != null) throw new Exception("the entrance did not finish");
                        RequireMainMenu(menu.MenuCanvas, MainMenuV6Labels);
                    });
            });

            // P3 (BUILD_PLAN §6 P3): the Stone's own sheets build over it in both plans and close (synchronously in edit mode):
            // CHOOSE YOUR DUEL (its cards, STAY), each recess ledger (its CTA label and the hint), the Ring seek, QUIT (the fold
            // question and the sign-out line; FOLD is never pressed: it signs out); SETTINGS opens SettingsScreen in the false
            // door with its CREDITS row (the boot's credit text).
            OurCheck("MenuV6 sheets over the Stone: DUEL, the recess ledgers, the Ring seek, QUIT, SETTINGS -> CREDITS (1920x1080, 1080x2340)", () =>
            {
                foreach (var (w, h) in new[] { (1920, 1080), (1080, 2340) })
                    WithHub("HubSmokeV6Sheets", w, h, menu =>
                    {
                        var stone = menu.Stone;
                        var tag = w + "x" + h + ": ";
                        if (stone == null) throw new Exception(tag + "v6 path not taken");
                        if (!SheetData.Has(stone.Kind)) throw new Exception(tag + "sheet data missing (the P3 art set is not installed)");
                        var canvas = menu.MenuCanvas;

                        Press(canvas, "Hero");
                        var duel = menu.Sheet as ModeCards;
                        if (duel == null) throw new Exception(tag + "DUEL did not open CHOOSE YOUR DUEL");
                        foreach (var l in new[] { ModeCards.TitleText, "YOU", "STAY", "OPP" }) RequireLabel(canvas, l);
                        foreach (var n in new[] { "Card0_VSAI", "Card1_VSPLAYER", "Card2_ZONEMODE" })
                            if (FindNamed(canvas, n) == null) throw new Exception(tag + "mode card " + n + " missing");
                        var states = duel.CardStates();
                        foreach (var st in states)
                            if (st.op < .99) throw new Exception(tag + "a mode card is not dealt at rest");
                        Press(canvas, "Stay");
                        if (menu.Sheet != null || FindNamed(canvas, "Popover_DuelChoice") != null)
                            throw new Exception(tag + "STAY did not close CHOOSE YOUR DUEL");

                        foreach (var (control, id) in new[]
                                 {
                                     ("Dest_DECK", "deck"), ("Dest_BAG", "bag"), ("Dest_STORY", "story"), ("Dest_BAZAAR", "bazaar"),
                                     ("Tert_VIEW", "view")
                                 })
                        {
                            Press(canvas, control);
                            var rs = menu.Sheet as RecessSheet;
                            if (rs == null || rs.Id != id) throw new Exception(tag + control + " did not open its ledger sheet");
                            RequireLabel(canvas, RecessSheet.CtaLabel(id));
                            RequireLabel(canvas, RecessSheet.Hint);
                            if (rs.Frame == null || rs.Frame.Body == null) throw new Exception(tag + id + ": no false door");
                            var dim = FindNamed(canvas, "Popover_Recess_" + id);
                            var dimBtn = CompAt<Button>(Kid(dim, "Dim"));
                            if (dimBtn == null) throw new Exception(tag + id + ": no dim to tap outside");
                            dimBtn.onClick.Invoke();
                            if (menu.Sheet != null || FindNamed(canvas, "Popover_Recess_" + id) != null)
                                throw new Exception(tag + id + ": tapping outside did not close the sheet");
                        }

                        Press(canvas, "MapBack");
                        var ring = menu.Sheet as RingSheet;
                        if (ring == null) throw new Exception(tag + "WORLD MAP did not open the Ring seek");
                        RequireLabel(canvas, "BATTLE CITY · NIGHT");
                        ring.Close();
                        if (FindNamed(canvas, "Popover_WorldMap") != null) throw new Exception(tag + "the Ring sheet did not close");

                        Press(canvas, "Quit");
                        var quit = menu.Sheet as QuitSheet;
                        if (quit == null) throw new Exception(tag + "QUIT did not open the fold sheet");
                        RequireLabel(canvas, QuitSheet.Title);
                        RequireLabel(canvas, QuitSheet.Body);
                        RequireLabel(canvas, "FOLD");
                        Press(canvas, "Stay");   // never FOLD: it signs out
                        if (FindNamed(canvas, "Popover_QuitConfirm") != null) throw new Exception(tag + "STAY did not close QUIT");

                        // SETTINGS over the Stone: MenuShell's ModalHost (marked Stone by DuelDiskMenuUI.EnsureHubShell) mounts
                        // SettingsScreen in the false door at the Stone's contract rect, its CREDITS row opens the boot's credit
                        // text; built on a marked host here (MenuShell.CreateOverlay needs play mode: ScreenRouter's DDOL)
                        var modal = SmokeModalHost("HubSmokeV6SheetsModal", SanctuaryHost.Stone);
                        if (!SanctuaryChrome.OverStone(modal)) throw new Exception(tag + "a Stone-marked host is not over the live Stone");
                        var settings = SettingsScreen.Build(modal, null, () => { });
                        if (settings == null) throw new Exception(tag + "SettingsScreen did not build");
                        var door = settings.GetComponent<FalseDoorFrame>();
                        if (door == null) throw new Exception(tag + "SETTINGS is not in the false door");
                        if (door.Cracks == null) throw new Exception(tag + "the false door over the Stone has no crack net");
                        if (Kid(settings, "BodyWell") == null) throw new Exception(tag + "SETTINGS lost its direct BodyWell");
                        if (!SheetCredits.AddRow(settings, modal)) throw new Exception(tag + "SETTINGS has no list for the CREDITS row");
                        RequireLabel(settings, SheetCredits.RowLabel);
                        var credits = SheetCredits.Open(modal);
                        RequireLabel(credits, "Story & design");
                        var cclose = CompAt<Button>(FindNamed(credits, "Close"));
                        if (cclose == null) throw new Exception(tag + "CREDITS has no Close");
                        cclose.onClick.Invoke();
                        if (credits.gameObject.activeSelf) throw new Exception(tag + "CREDITS did not close at once (D19)");
                        DestroyNamed("HubSmokeV6SheetsModal");
                    });
            });

            Check("DesktopLabApp.Open", () =>
            {
                DesktopLabApp.Open();
                if (DesktopLabApp.IsActive == false)
                    throw new Exception("lab not marked active");
            });

            MenuShell overlay = null;
            var overlayRoots = RootSet();   // P6: the overlay host's checks remove their roots after the last ShowOverlay
            Check("MenuShell overlay host", () =>
            {
                overlay = MenuShell.CreateOverlay(92);
                if (overlay == null) throw new Exception("overlay missing");
            });
            foreach (MenuId id in Enum.GetValues(typeof(MenuId)))
            {
                if (id is MenuId.None or MenuId.OvermapHome or MenuId.SystemsHub or MenuId.DuelLive)
                    continue;
                var captured = id;
                Check("ShowOverlay " + captured, () =>
                {
                    if (overlay == null)
                        throw new Exception("overlay host unavailable (edit-mode DDOL)");
                    overlay.ShowOverlay(captured);
                    if (overlay.ModalHost != null && overlay.ModalHost.childCount == 0
                        && captured != MenuId.AvatarProfile)
                        throw new Exception("overlay opened with empty modal host");
                });
            }

            // (the overlay shell itself, when it built, stays for the Settings checks below; the final cleanup removes it by name)
            DestroyNewRoots(overlayRoots, overlay != null ? overlay.transform.root.gameObject : null);

            // P3 (D5 / §11 F5, D19): an overlay sheet wears the false door, its rows the Sanctuary look — also rows built after
            // ShowOverlay returned (SystemsSheets' Refresh closures) — its close is synchronous, and the same painters outside a
            // marker (the duel HUD, the Overworld) keep today's look; the deck builder (P4: the offering hall) paints itself.
            OurCheck("MenuV6 sheets: the false door over the map, painters behind the marker, synchronous close", () =>
            {
                if (!SheetData.Has(MenuLayoutKind.Landscape)) throw new Exception("sheet data missing (the P3 art set is not installed)");
                try
                {
                    MenuAspect.Force(1920, 1080);
                    // an overlay shell's ModalHost (MenuShell marks it; CreateOverlay itself needs play mode)
                    var modal = SmokeModalHost("HubSmokeV6SheetsModal", SanctuaryHost.Overlay);
                    if (SanctuaryChrome.OverStone(modal)) throw new Exception("an Overlay-marked host claims the Stone");
                    var closed = false;
                    var bazaar = SystemsSheets.BuildBazaar(modal, () => closed = true);
                    if (bazaar == null || bazaar.name != "PhoneMenu_BAZAAR") throw new Exception("BAZAAR missing");
                    var door = bazaar.GetComponent<FalseDoorFrame>();
                    if (door == null) throw new Exception("BAZAAR is not in the false door");
                    if (Kid(bazaar, "BodyWell") == null) throw new Exception("BAZAAR lost its direct BodyWell");
                    if (FindNamed(bazaar, "Close") == null) throw new Exception("BAZAAR lost its Close");
                    if (bazaar.GetComponentsInChildren<SanctuaryPainted>(true).Length == 0)
                        throw new Exception("nothing in the BAZAAR wears the Sanctuary look");
                    // a row built after the sheet returned (SystemsSheets' Refresh closures) is painted too: the marker is read
                    // when the painter runs (§11 F5)
                    var list = FindNamed(bazaar, "List");
                    var later = HubChrome.ListRow(list != null ? list : (Transform)door.Body, "smoke row rebuilt later", null);
                    if (later == null || later.GetComponent<SanctuaryPainted>() == null)
                        throw new Exception("a row built after the sheet returned lost the Sanctuary look (§11 F5)");
                    // outside any marker (the duel HUD, the Overworld's own panels) the same painter keeps today's look
                    var outside = HubChrome.ListRow(host, "smoke row outside a marker", null);
                    if (outside == null || outside.GetComponent<SanctuaryPainted>() != null)
                        throw new Exception("a painter outside any marker painted the Sanctuary look");
                    UnityEngine.Object.DestroyImmediate(outside.gameObject);
                    var close = CompAt<Button>(FindNamed(bazaar, "Close"));
                    if (close == null) throw new Exception("BAZAAR Close missing");
                    close.onClick.Invoke();
                    if (!closed) throw new Exception("BAZAAR Close did not reach its handler");
                    // BAZAAR hands its close to the host (MenuShell.CloseCurrent hides every false door at once, then clears the
                    // host); a sheet that closes itself goes through DualMenuPresenter.Dismiss: ARTIFACTS, in the false door, is
                    // inactive at once and reaches its handler (D19)
                    var artClosed = false;
                    var art = ArtifactBoxScreen.Build(modal, () => artClosed = true, UiPresentation.NonArPortrait);
                    if (art == null || art.GetComponent<FalseDoorFrame>() == null) throw new Exception("ARTIFACTS is not in the false door");
                    var aclose = CompAt<Button>(FindNamed(art, "Close"));
                    if (aclose == null) throw new Exception("ARTIFACTS Close missing");
                    aclose.onClick.Invoke();
                    if (art != null && art.gameObject.activeSelf) throw new Exception("ARTIFACTS still visible after Close (D19)");
                    if (!artClosed) throw new Exception("ARTIFACTS Close did not reach its handler");
                    // the deck builder is the offering hall (P4): MenuShell unmarks the host for it; it never takes the false door
                    // and none of its parts is a Sanctuary painter's
                    SanctuaryChrome.Mark(modal.gameObject, false, SanctuaryHost.Overlay);
                    DeckCollectionScreen.Build(modal, () => { }, UiPresentation.NonArPortrait);
                    var deckRoot = FindNamed(modal, "PhoneMenu_DECK");
                    if (deckRoot == null) throw new Exception("DECK missing");
                    if (deckRoot.GetComponent<FalseDoorFrame>() != null) throw new Exception("the deck builder took the false door");
                    if (deckRoot.GetComponentsInChildren<SanctuaryPainted>(true).Length > 0)
                        throw new Exception("the deck builder took the Sanctuary look");
                    if (DeckHallSpec.WhyNot(MenuLayoutKind.Landscape) == null && deckRoot.GetComponent<DeckHall>() == null)
                        throw new Exception("the deck builder did not build the offering hall");
                }
                finally
                {
                    MenuAspect.ClearForce();
                    DestroyNamed("HubSmokeV6SheetsModal");
                }
            });

            OurCheck("DeckCollection phone", () =>
            {
                DeckCollectionScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                RequireLabel(host, "MAIN  ");
                RequireLabel(host, "EXTRA  ");
                RequireLabel(host, "SIDE  ");
                RequireLabel(host, "COLLECTION");
            });
            OurCheck("DeckCollection AR holo", () =>
            {
                DeckCollectionScreen.Build(host, () => { }, UiPresentation.ArDiskHolo);
                RequireLabel(host, "MAIN  ");
                RequireLabel(host, "EXTRA  ");
                RequireLabel(host, "SIDE  ");
            });
            // P4 (BUILD_PLAN §6 P4 verify): the offering hall in both plans — the frame contract (PhoneMenu_DECK, its direct
            // BodyWell, the back chip named Close), the two-space tray titles and COLLECTION, the tabs (BINDER hidden, D13),
            // the Caret, the shared EXTRA / SIDE row, the Scale and the box; then the hall's checks on the deck in memory (the
            // Close guard with MAIN emptied in memory, "-word", the inspect sheet, DRAW 5 UI-only). Nothing is saved.
            OurCheck("Deck hall: the offering hall, its labels, the shared row, the Close guard, DRAW 5 (1920x1080, 1080x2340)", () =>
            {
                foreach (var (w, hh, kind) in new[] { (1920, 1080, MenuLayoutKind.Landscape), (1080, 2340, MenuLayoutKind.Portrait) })
                {
                    var tag = w + "x" + hh + ": ";
                    var why = DeckHallSpec.WhyNot(kind);
                    if (why != null) throw new Exception(tag + why + " (the P4 art set is not installed)");
                    var dh = new GameObject("DeckHallSmoke", typeof(RectTransform)).GetComponent<RectTransform>();
                    dh.SetParent(canvas, false);
                    FloatingPanel.Stretch(dh);
                    try
                    {
                        MenuAspect.Force(w, hh);
                        DeckCollectionScreen.Build(dh, () => { }, UiPresentation.NonArPortrait);
                        var root = FindNamed(dh, "PhoneMenu_DECK");
                        if (root == null) throw new Exception(tag + "PhoneMenu_DECK missing");
                        var hall = root.GetComponent<DeckHall>();
                        if (hall == null) throw new Exception(tag + "the deck builder did not build the hall");
                        if (hall.Kind != kind) throw new Exception(tag + "the hall took the " + hall.Kind + " plan");
                        if (Kid(root, "BodyWell") == null) throw new Exception(tag + "the hall lost its direct BodyWell");
                        var close = CompAt<Button>(FindNamed(root, "Close"));
                        if (close == null) throw new Exception(tag + "no back chip named Close");
                        foreach (var l in new[] { "MAIN  ", "EXTRA  ", "SIDE  ", "COLLECTION", "DECK", "SAVE", "DRAW 5", "TOME", "TRADE", "ALL" })
                            RequireLabel(root, l);
                        foreach (var t in root.GetComponentsInChildren<Text>(true))
                            if (t != null && t.text == "BINDER") throw new Exception(tag + "BINDER is shown (D13: no source)");
                        foreach (var n in new[] { "Caret", "XRow", "MaatScale", "InspectPopup", "Save" })
                            if (FindNamed(root, n) == null) throw new Exception(tag + n + " missing");
                        if (root.GetComponentsInChildren<SanctuaryPainted>(true).Length > 0)
                            throw new Exception(tag + "a Sanctuary painter painted the hall");
                        var probe = root.GetComponent<DeckCollectionScreen.HallProbe>();
                        if (probe == null) throw new Exception(tag + "no hall probe");
                        var r = probe.Run();
                        if (string.IsNullOrEmpty(r)) throw new Exception(tag + "the hall probe reported nothing");
                        // the hall is the deck builder's own opt-in: another dense sheet (CHOOSE YOUR OPPONENT, DUEL SETUP pass
                        // dense) keeps today's dense frame
                        var other = DualMenuPresenter.BuildFrame(dh, "DENSE SMOKE", "sub", () => { }, UiPresentation.NonArPortrait,
                            dense: true);
                        if (other.Root == null) throw new Exception(tag + "a dense non-deck frame did not build");
                        if (other.Root.GetComponent<DeckHall>() != null)
                            throw new Exception(tag + "a dense non-deck frame took the deck hall");
                    }
                    finally
                    {
                        MenuAspect.ClearForce();
                        UnityEngine.Object.DestroyImmediate(dh.gameObject);
                    }
                }
            });
            OurCheck("Deck builder legacy fallback (no hall data): today's builder with COLLECTION", () =>
            {
                var lh = new GameObject("DeckLegacySmoke", typeof(RectTransform)).GetComponent<RectTransform>();
                lh.SetParent(canvas, false);
                FloatingPanel.Stretch(lh);
                try
                {
                    DeckHallSpec.ForceLegacy = true;
                    DeckCollectionScreen.Build(lh, () => { }, UiPresentation.NonArPortrait);
                    if (lh.GetComponentInChildren<DeckHall>(true) != null) throw new Exception("the forced legacy build took the hall");
                    foreach (var l in new[] { "MAIN  ", "EXTRA  ", "SIDE  ", "COLLECTION" }) RequireLabel(lh, l);
                }
                finally
                {
                    DeckHallSpec.ForceLegacy = false;
                    UnityEngine.Object.DestroyImmediate(lh.gameObject);
                }
            });
            // P5 (BUILD_PLAN §6 P5 verify): the dread ladder (the page's own director vectors; every rung at the omen, the
            // whisper and the story beats, the Stone back at rest), the live cel smoke over its plate (the plate kept when the
            // shader is missing), the title's fog, the back key, the safe-area bands, SETTINGS' pinned CREDITS and the deck's
            // portrait tap pads. Nothing is saved; no beat is left running.
            OurCheck("MenuV6 P5: the dread ladder (the page's director, every rung at the omen / whisper / story beats) (1920x1080, 1080x2340)", () =>
            {
                foreach (var (w, h, kind) in new[] { (1920, 1080, MenuLayoutKind.Landscape), (1080, 2340, MenuLayoutKind.Portrait) })
                {
                    var tag = w + "x" + h + ": ";
                    if (DreadData.Load(kind) == null) throw new Exception(tag + "dread data missing (the P5 art set is not installed)");
                    DreadVectors(kind, tag);
                    WithHub("HubSmokeV6Dread", w, h, menu =>
                    {
                        var stone = menu.Stone;
                        if (stone == null) throw new Exception(tag + "v6 path not taken");
                        if (stone.Dread == null) throw new Exception(tag + "no dread ladder");
                        var o = DreadMath.OmenStart(0);
                        var wh = DreadMath.WhisperStart(0);
                        stone.ProbeDread(false, o - 1, o + .2, o + .5, o + .9, o + 1.2, o + 1.6, o + 2.2, wh + .1, wh + .6, wh + 1.2, wh + 1.9);
                        stone.ProbeDread(true, 3, 6, 8, 9.5, 12.4, 13.6, 14.2, 15, 16.5, 18);
                        if (stone.Life != null && stone.Life.Dropped.Count > 0)
                            throw new Exception(tag + "rigs threw under the ladder: " + string.Join(", ", stone.Life.Dropped));
                        if (stone.Dread.Story) throw new Exception(tag + "the story beat was left running");
                        RequireMainMenu(menu.MenuCanvas, MainMenuV6Labels);
                    });
                }
            });
            OurCheck("MenuV6 P5: the live cel smoke over its plate, the plate kept without the shader; the title's fog (1920x1080, 1080x2340)", () =>
            {
                foreach (var (w, h, kind) in new[] { (1920, 1080, MenuLayoutKind.Landscape), (1080, 2340, MenuLayoutKind.Portrait) })
                {
                    var tag = w + "x" + h + ": ";
                    if (SmokeSet.Load(kind) == null) throw new Exception(tag + "smoke data missing (the P5 art set is not installed)");
                    if (CelSmokeShader.Get() == null) throw new Exception(tag + CelSmokeShader.Name + " is missing or does not compile");
                    WithHub("HubSmokeV6Smoke", w, h, menu =>
                    {
                        var stone = menu.Stone;
                        if (stone == null) throw new Exception(tag + "v6 path not taken");
                        if (stone.Smoke == null) throw new Exception(tag + "the live smoke did not build over the plate");
                        foreach (var t in new[] { .5, 2.8, 9.3, 33.0, 70.0 }) stone.Smoke.DrawAt(t);
                        if (stone.Smoke.Quads < 1) throw new Exception(tag + "the live smoke drew nothing");
                    });
                    try
                    {
                        CelSmokeShader.ForceMissing = true;
                        WithHub("HubSmokeV6SmokePlate", w, h, menu =>
                        {
                            var stone = menu.Stone;
                            if (stone == null) throw new Exception(tag + "v6 path not taken without the smoke shader");
                            if (stone.Smoke != null) throw new Exception(tag + "the live smoke built without its shader");
                            var img = CompAt<Image>(FindNamed(menu.MenuCanvas, "smoke"));
                            if (img == null || !img.enabled) throw new Exception(tag + "the smoke plate is not showing");
                        });
                    }
                    finally
                    {
                        CelSmokeShader.ForceMissing = false;
                    }

                    if (TitleStage.WhyNot(kind) == null)
                    {
                        var tc = WrldzTheme.Canvas("TitleFogSmokeCanvas", 49, aspect: true);
                        var ts = TitleStage.Build(tc, kind, new TitleStage.Options { Video = false, StartRaw = TitleStage.SettledRaw });
                        if (ts == null) throw new Exception(tag + "the title did not build");
                        if (ts.Fog == null) throw new Exception(tag + "the title has no live fog");
                        // P6: the fog is drawn only through Nut's left-limb strip, as the page draws it (the P5 landing's
                        // blobs at the Stone's foot): the clip is the page's part() rect and nothing reaches past it
                        var want = TitleFog.NutLimbClip(RJ.D(RJ.D(MenuV6Json.Parse(MenuV6Art.Data(TitleStage.FileFor(kind)).text)
                            as System.Collections.Generic.Dictionary<string, object>, "rigs"), "nut"));
                        if (want.width <= 0) throw new Exception(tag + "the title data has no Nut rig (rigs.nut)");
                        if (ts.Fog.Clip != want) throw new Exception(tag + "the title's fog clip is " + ts.Fog.Clip + ", not Nut's strip " + want);
                        var fogClip = FindNamed(ts.Stage, "TitleFogClip");
                        if (fogClip == null || fogClip.GetComponent<RectMask2D>() == null)
                            throw new Exception(tag + "the title's fog has no RectMask2D clip");
                        UnityEngine.Object.DestroyImmediate(tc.gameObject);
                    }
                }
            });
            OurCheck("MenuV6 P5: the back key: the popover's STAY, then nothing on the bare menu (1920x1080)", () =>
            {
                WithHub("HubSmokeV6Back", 1920, 1080, menu =>
                {
                    var back = menu.Back;
                    if (back == null) throw new Exception("no back handler on the main menu");
                    if (back.Press() || back.Last != "nothing") throw new Exception("back on the bare menu did something: " + back.Last);
                    Press(menu.MenuCanvas, "Hero");
                    if (menu.Sheet == null) throw new Exception("DUEL did not open its sheet");
                    if (!back.Press() || back.Last != "popover") throw new Exception("back did not close CHOOSE YOUR DUEL (" + back.Last + ")");
                    if (menu.Sheet != null || FindNamed(menu.MenuCanvas, "Popover_DuelChoice") != null)
                        throw new Exception("CHOOSE YOUR DUEL is still open after back");
                    Press(menu.MenuCanvas, "Quit");
                    if (menu.Sheet as QuitSheet == null) throw new Exception("QUIT did not open the fold sheet");
                    if (!back.Press()) throw new Exception("back did not take QUIT's STAY");
                    if (FindNamed(menu.MenuCanvas, "Popover_QuitConfirm") != null) throw new Exception("QUIT is still open after back");
                });
            });
            OurCheck("MenuV6 P5: the safe-area bands (a simulated 120 px top / 80 px bottom inset, 1080x2340)", () =>
            {
                WithHub("HubSmokeV6Safe", 1080, 2340, menu =>
                {
                    var stone = menu.Stone;
                    if (stone == null) throw new Exception("v6 path not taken");
                    var sa = stone.Safe;
                    if (sa == null || sa.Bands == 0) throw new Exception("no safe-area bands");
                    try
                    {
                        var cs = new Vector3[4];
                        stone.Stage.GetWorldCorners(cs);
                        var p0 = RectTransformUtility.WorldToScreenPoint(null, cs[0]);
                        var p2 = RectTransformUtility.WorldToScreenPoint(null, cs[2]);
                        var k = (p2.x - p0.x) / 1080f;   // screen px per stage px
                        if (k <= .001f) return;   // no view to measure in this Editor layout: the bands exist, nothing to move
                        SafeArea.Override = Rect.MinMaxRect(p0.x, p0.y + 80f * k, p2.x, p2.y - 120f * k);
                        sa.Apply(true);
                        if (Mathf.Abs(sa.T - 120f) > 2f || Mathf.Abs(sa.B - 80f) > 2f)
                            throw new Exception("insets measured " + sa.T + " / " + sa.B + " stage px (want 120 / 80)");
                        var top = FindNamed(stone.Stage, "Safe_top") as RectTransform;
                        var foot = FindNamed(stone.Stage, "Safe_footer") as RectTransform;
                        if (top == null || foot == null) throw new Exception("the identity-row / footer bands are missing");
                        if (Mathf.Abs(top.anchoredPosition.y + 100f) > .5f) throw new Exception("the identity row moved " + top.anchoredPosition.y + " (want -100)");
                        if (Mathf.Abs(foot.anchoredPosition.y - 58f) > .5f) throw new Exception("the footer moved " + foot.anchoredPosition.y + " (want +58)");
                    }
                    finally
                    {
                        SafeArea.Override = null;
                        sa.Apply(true);
                    }
                });
            });
            OurCheck("MenuV6 P5: SETTINGS keeps CREDITS on the sheet (the pinned footer) (1920x1080, 1080x2340)", () =>
            {
                foreach (var (w, h) in new[] { (1920, 1080), (1080, 2340) })
                    WithHub("HubSmokeV6Credits", w, h, menu =>
                    {
                        var tag = w + "x" + h + ": ";
                        var modal = SmokeModalHost("HubSmokeV6CreditsModal", SanctuaryHost.Stone);
                        var settings = SettingsScreen.Build(modal, null, () => { });
                        if (settings == null) throw new Exception(tag + "SettingsScreen did not build");
                        if (!SheetCredits.AddRow(settings, modal)) throw new Exception(tag + "SETTINGS has no list for the CREDITS row");
                        var foot = FindNamed(settings, "CreditsFoot");
                        if (foot == null) throw new Exception(tag + "CREDITS is not pinned under the list");
                        RequireLabel(foot, SheetCredits.RowLabel);
                        if (FindNamed(settings, "ListView") == null) throw new Exception(tag + "the list has no viewport above the footer");
                        DestroyNamed("HubSmokeV6CreditsModal");
                    });
            });
            OurCheck("Deck hall P5: every portrait tap is at least 56 px (the tap pads), the safe-area bands (1080x2340)", () =>
            {
                var why = DeckHallSpec.WhyNot(MenuLayoutKind.Portrait);
                if (why != null) throw new Exception(why + " (the P4 art set is not installed)");
                var dh = new GameObject("DeckHallSmokeP5", typeof(RectTransform)).GetComponent<RectTransform>();
                dh.SetParent(canvas, false);
                FloatingPanel.Stretch(dh);
                try
                {
                    MenuAspect.Force(1080, 2340);
                    DeckCollectionScreen.Build(dh, () => { }, UiPresentation.NonArPortrait);
                    var root = FindNamed(dh, "PhoneMenu_DECK");
                    var hall = root != null ? root.GetComponent<DeckHall>() : null;
                    if (hall == null) throw new Exception("the deck builder did not build the hall");
                    if (hall.Safe == null) throw new Exception("the deck hall has no safe-area bands");
                    foreach (var b in root.GetComponentsInChildren<Button>(true))
                    {
                        var img = b.targetGraphic as Image;
                        if (img == null || img.color.a > .001f) continue;   // the hall's hit pattern: an invisible Image + Button
                        var rt = (RectTransform)b.transform;
                        var sz = rt.rect.size;
                        if (sz.x >= 55.5f && sz.y >= 55.5f) continue;
                        var pad = b.transform.Find("TapPad") as RectTransform;
                        if (pad == null || pad.rect.width < 55.5f || pad.rect.height < 55.5f)
                            throw new Exception("the tap " + b.name + " is " + sz.x.ToString("0") + " x " + sz.y.ToString("0") + " px with no 56 px pad");
                    }
                }
                finally
                {
                    MenuAspect.ClearForce();
                    UnityEngine.Object.DestroyImmediate(dh.gameObject);
                }
            });

            // P6 (the P5 landing's defects, BUILD_PLAN §6 P5 polish): the deck select's rows lay out name and count apart, the tab
            // row closes BINDER's slot, the inspect face is never the white sprite, the crack gaps share their stencil masks
            // (CrackLayer.MergeGaps) and the sky-lid apparition sits under no clip (both plans)
            OurCheck("MenuV6 P6: deck select rows, the tab row, the inspect face, shared crack masks, the unclipped sky-lid (1920x1080, 1080x2340)", () =>
            {
                var cs = new Vector3[4];
                float X0(RectTransform r) { r.GetWorldCorners(cs); return cs[0].x; }
                float X1(RectTransform r) { r.GetWorldCorners(cs); return cs[2].x; }
                void Masks(Transform layer, string tag2)
                {
                    if (layer == null) return;
                    int per = 0, merged = 0;
                    for (var i = 0; i < layer.childCount; i++)
                    {
                        var n = layer.GetChild(i).name;
                        if (n.StartsWith("Gaps_", StringComparison.Ordinal)) merged++;
                        else if (n.StartsWith("Gap_", StringComparison.Ordinal)) per++;
                    }

                    if (per > 0 || merged == 0) throw new Exception(tag2 + layer.name + ": " + per + " per-net gap masks, " + merged + " shared");
                }

                foreach (var (w, hh, kind) in new[] { (1920, 1080, MenuLayoutKind.Landscape), (1080, 2340, MenuLayoutKind.Portrait) })
                {
                    var tag = w + "x" + hh + ": ";
                    if (DeckHallSpec.WhyNot(kind) != null) throw new Exception(tag + DeckHallSpec.WhyNot(kind) + " (the P4 art set is not installed)");
                    var dh = new GameObject("DeckHallSmokeP6", typeof(RectTransform)).GetComponent<RectTransform>();
                    dh.SetParent(canvas, false);
                    FloatingPanel.Stretch(dh);
                    try
                    {
                        MenuAspect.Force(w, hh);
                        DeckCollectionScreen.Build(dh, () => { }, UiPresentation.NonArPortrait);
                        var root = FindNamed(dh, "PhoneMenu_DECK");
                        var hall = root != null ? root.GetComponent<DeckHall>() : null;
                        if (hall == null) throw new Exception(tag + "the deck builder did not build the hall");
                        var side = AsRect(FindNamed(root, "Tab_SIDE"));
                        var tome = AsRect(FindNamed(root, "Tab_TOME"));
                        var trade = AsRect(FindNamed(root, "Tab_TRADE"));
                        if (side == null || tome == null || trade == null) throw new Exception(tag + "a tab is missing (SIDE / TOME / TRADE)");
                        var g1 = X0(tome) - X1(side);
                        var g2 = X0(trade) - X1(tome);
                        if (Mathf.Abs(g2 - g1) > Mathf.Max(2f, .15f * Mathf.Abs(g1)))
                            throw new Exception(tag + "the gap TOME -> TRADE is " + g2.ToString("0.0") + " vs SIDE -> TOME " + g1.ToString("0.0") + " (BINDER's slot left open)");
                        var caret = FindNamed(root, "Caret");
                        if (caret == null) throw new Exception(tag + "no Caret");
                        var es = EventSystem.current;
                        if (es == null) es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
                        var ped = new PointerEventData(es);
                        ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerDownHandler);
                        ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerUpHandler);
                        var drop = FindNamed(root, "DeckDropdown");
                        if (drop == null) throw new Exception(tag + "the Caret opened no DeckDropdown");
                        for (var i = 0; i < drop.childCount; i++)
                        {
                            var row = drop.GetChild(i);
                            if (!row.name.StartsWith("DeckBox_", StringComparison.Ordinal)) continue;
                            var nameT = CompAt<Text>(Kid(row, "Name"));
                            var cntT = CompAt<Text>(Kid(row, "Count"));
                            if (nameT == null || cntT == null) throw new Exception(tag + row.name + ": no Name / Count label");
                            if (X1(nameT.rectTransform) > X0(cntT.rectTransform) + .5f)
                                throw new Exception(tag + row.name + ": the name's box runs into the count's");
                            foreach (var t in new[] { nameT, cntT })
                            {
                                var ls = t.GetComponent<LetterSpacingEffect>();
                                var tw = t.preferredWidth + (ls != null ? ls.Spacing * t.text.Length : 0f);
                                if (tw > t.rectTransform.rect.width + 1f)
                                    throw new Exception(tag + row.name + ": '" + t.text + "' is " + tw.ToString("0") + " wide in a " +
                                                        t.rectTransform.rect.width.ToString("0") + " box");
                            }
                        }

                        var probe = root.GetComponent<DeckCollectionScreen.HallProbe>();
                        if (probe != null && probe.ShowFirstCard())
                        {
                            var art = CompAt<Image>(FindNamed(FindNamed(root, "InspectPopup"), "Art"));
                            if (art == null) throw new Exception(tag + "the inspect sheet has no Art");
                            if (art.sprite == UiFoundation.WhiteSprite() && YgoCardFrames.CardBack() != null)
                                throw new Exception(tag + "the inspect art is the white sprite (no art and no card back)");
                            probe.HideCard();
                        }

                        Masks(hall.Cracks != null ? hall.Cracks.transform : null, tag);
                    }
                    finally
                    {
                        MenuAspect.ClearForce();
                        UnityEngine.Object.DestroyImmediate(dh.gameObject);
                    }

                    WithHub("HubSmokeV6P6", w, hh, menu =>
                    {
                        var stone = menu.Stone;
                        if (stone == null) throw new Exception(tag + "v6 path not taken");
                        Masks(stone.Cracks != null ? stone.Cracks.transform : null, tag);
                        var sky = stone.Dread != null ? FindNamed(stone.Root, "SkyLid") : null;
                        for (var p = sky != null ? sky.parent : null; p != null && p != stone.Root; p = p.parent)
                            if (p.GetComponent<RectMask2D>() != null || p.GetComponent<Mask>() != null)
                                throw new Exception(tag + "the sky-lid sits under the clip " + p.name);
                    });
                }
            });

            Check("Inventory phone", () =>
            {
                InventoryScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                RequireLabel(host, "CASE");
                RequireLabel(host, "POCKETS");
                RequireLabel(host, "DECKS");
                RequireLabel(host, "HOME");
            });
            Check("Inventory AR holo", () =>
            {
                InventoryScreen.Build(host, () => { }, UiPresentation.ArDiskHolo);
                RequireLabel(host, "CASE");
                RequireLabel(host, "POCKETS");
                RequireLabel(host, "DECKS");
                RequireLabel(host, "HOME");
            });
            Check("ArtifactBox phone", () =>
            {
                ArtifactBoxScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                RequireLabel(host, "ARTIFACTS");
            });
            Check("ArtifactBox AR holo", () =>
            {
                ArtifactBoxScreen.Build(host, () => { }, UiPresentation.ArDiskHolo);
                RequireLabel(host, "ARTIFACTS");
            });
            Check("ArtifactBox filter chips", () =>
            {
                FloatingPanel.DestroyChildrenNow(host);
                ArtifactBoxScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                RequireLabel(host, "ALL");
                RequireLabel(host, "CURRENCY");
                RequireLabel(host, "TABLET");
                RequireLabel(host, "KEY");
            });
            Check("BAG wallet opens ArtifactBox and X returns", () =>
            {
                FloatingPanel.DestroyChildrenNow(host);
                InventoryScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                RequireLabel(host, "CASE");
                var chip = FindNamed(host, "Chip_Đ");
                if (chip == null) throw new Exception("wallet Đ chip missing");
                var chipBtn = chip.GetComponent<Button>();
                if (chipBtn == null) throw new Exception("wallet Đ chip not clickable");
                chipBtn.onClick.Invoke();
                var art = FindNamed(host, "PhoneMenu_ARTIFACTS");
                if (art == null) throw new Exception("artifact sheet did not open");
                var bag = FindNamed(host, "PhoneMenu_BAG");
                if (bag != null && bag.gameObject.activeSelf)
                    throw new Exception("BAG stayed visible under artifacts");
                RequireLabel(host, "ARTIFACTS");
                var close = FindNamed(art, "Close");
                var closeBtn = close != null ? close.GetComponent<Button>() : null;
                if (closeBtn == null) throw new Exception("artifact Close missing");
                closeBtn.onClick.Invoke();
                art = FindNamed(host, "PhoneMenu_ARTIFACTS");
                if (art != null && art.gameObject.activeSelf)
                    throw new Exception("artifact sheet still open after X");
                bag = FindNamed(host, "PhoneMenu_BAG");
                if (bag == null || !bag.gameObject.activeSelf)
                    throw new Exception("BAG did not return after artifact X");
                RequireLabel(host, "CASE");
            });
            Check("ArtifactBox X hides its own frame", () =>
            {
                FloatingPanel.DestroyChildrenNow(host);
                var root = ArtifactBoxScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                var close = FindNamed(root, "Close");
                var closeBtn = close != null ? close.GetComponent<Button>() : null;
                if (closeBtn == null) throw new Exception("artifact Close missing");
                closeBtn.onClick.Invoke();
                if (root != null && root.gameObject.activeSelf)
                    throw new Exception("artifact frame still visible after X");
            });
            Check("Settings", () => SettingsScreen.Build(host, overlay, () => { }));
            Check("FormatSelect", () => FormatSelectScreen.Build(host, () => { }));
            Check("ArDuelCreate", () => ArDuelCreateScreen.Build(host, () => { }));
            Check("PvPCreate", () => PlayerVsPlayerCreateScreen.Build(host, () => { }));
            Check("Tome sheet", () => SystemsSheets.BuildTome(host, () => { }));
            Check("Story sheet", () => SystemsSheets.BuildStory(host, () => { }));
            Check("Bazaar sheet", () => SystemsSheets.BuildBazaar(host, () => { }));
            Check("Trade sheet", () => SystemsSheets.BuildTrade(host, () => { }));
            Check("Tournament rooms", () => TournamentRoomScreen.Build(host, () => { }));
            Check("CardInspectPopup", () =>
            {
                var pop = CardInspectPopup.Create(canvas);
                var db = CardDatabase.Load();
                var def = db != null ? db.Get(40640057) ?? db.Get(89631139) : null;
                if (def != null)
                {
                    var card = new WRLDZ.Duel.CardInstance { Def = def, FaceUp = true };
                    pop.Show(card, db, true, null);
                    if (!pop.IsOpen) throw new Exception("inspect did not open");
                    pop.Hide();
                    if (pop.IsOpen) throw new Exception("inspect did not close");

                    var picked = false;
                    var cancelled = false;
                    pop.Show(card, db, true,
                        new System.Collections.Generic.List<(string, Color, System.Action)>
                        {
                            ("Choose " + (def.name ?? "target"), Color.green, () => picked = true)
                        },
                        onClose: () => cancelled = true,
                        closeLabel: "CANCEL");
                    if (!pop.IsOpen) throw new Exception("1-target inspect did not open");
                    var labels = CollectButtonLabels(pop.transform);
                    if (!labels.Exists(s => s != null && s.IndexOf("Choose", System.StringComparison.OrdinalIgnoreCase) >= 0))
                        throw new Exception("1-target menu missing Choose action: " + string.Join(",", labels));
                    if (!labels.Exists(s => s != null &&
                                            s.IndexOf("CANCEL", System.StringComparison.OrdinalIgnoreCase) >= 0))
                        throw new Exception("1-target menu missing CANCEL: " + string.Join(",", labels));
                    pop.Hide();
                    if (!cancelled) throw new Exception("CANCEL/close did not fire onClose");
                    if (picked) throw new Exception("Choose should not fire on close");

                    // Same-frame re-Show must replace actions, not stack them
                    var acts = new System.Collections.Generic.List<(string, Color, System.Action)>
                    {
                        ("Attack", Color.red, () => { }),
                        ("Change Pos", Color.cyan, () => { })
                    };
                    pop.Show(card, db, true, acts);
                    pop.Show(card, db, true, acts);
                    var attackN = 0;
                    foreach (var b in pop.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    {
                        var t = b.GetComponentInChildren<UnityEngine.UI.Text>();
                        if (t != null && t.text == "Attack")
                            attackN++;
                    }

                    if (attackN != 1)
                        throw new Exception("inspect action menu repeated itself, Attack buttons=" + attackN);
                }
            });
            Check("GraveyardBrowser opens and pages", () =>
            {
                var gy = GraveyardBrowser.Create(canvas);
                gy.Show(null, null, true, _ => { });
                if (!gy.IsOpen) throw new Exception("empty GY browser did not open");
                gy.Hide();
                if (gy.IsOpen) throw new Exception("GY browser stayed open after Hide");
                var list = new System.Collections.Generic.List<WRLDZ.Duel.CardInstance>
                {
                    new WRLDZ.Duel.CardInstance { InstanceId = 1, CardId = 1 },
                    new WRLDZ.Duel.CardInstance { InstanceId = 2, CardId = 2 }
                };
                gy.Show(list, null, false, _ => { });
                if (!gy.IsOpen) throw new Exception("GY browser did not open with cards");
                if (gy.ShowingPlayerSide) throw new Exception("OPP GY flagged as player");
                gy.Hide();
            });
            Check("ArDragActionHud", () => ArDragActionHud.Create(canvas));
            Check("DuelFloatingHud", () =>
            {
                var hud = DuelFloatingHud.Create(canvas);
                if (hud.DeckCount == null || hud.PhaseLabel == null || hud.StatusLine == null)
                    throw new Exception("floating hud missing score/phase/status");
                if (hud.YouLp == null || hud.OppLp == null)
                    throw new Exception("floating hud missing YOU/OPP LP orbs");
                if (hud.ActionWindow == null || hud.ContextWindow == null)
                    throw new Exception("floating hud missing action/context islands");
                hud.SetLifePoints(8000, 7500);
                if (hud.YouLp.text != "8000" || hud.OppLp.text != "7500")
                    throw new Exception("LP orbs did not take SetLifePoints");
                hud.SetStatus("Your Main Phase 1 — you may Normal Summon");
                hud.SetActionsVisible(true);
                hud.SetContextVisible(false);
                var row = hud.ActionRow != null
                    ? hud.ActionRow.GetComponent<HorizontalLayoutGroup>() : null;
                if (row == null || row.childForceExpandWidth)
                    throw new Exception("action chips must not stretch across the stage");
            });
            Check("live-duel HUD stays glanceable glass", () =>
            {
                var hud = DuelFloatingHud.Create(canvas);
                AssertGlanceIsland(hud.Root.Find("YouScore"), "YouScore", maxW: 0.32f, maxH: 0.10f);
                AssertGlanceIsland(hud.Root.Find("Phase"), "Phase", maxW: 0.36f, maxH: 0.10f);
                AssertGlanceIsland(hud.Root.Find("OppScore"), "OppScore", maxW: 0.32f, maxH: 0.10f);
                AssertGlanceIsland(hud.Root.Find("Status"), "Status", maxW: 0.36f, maxH: 0.06f);
                if (hud.PillMp1 != null && hud.PillMp1.color.a > 0.45f)
                    throw new Exception("phase pip too opaque a=" + hud.PillMp1.color.a.ToString("0.00"));

                try { ArCompanionPhoneHud.Build(canvas, () => { }); }
                catch (InvalidOperationException) { /* AppSession DDOL in edit mode after chips exist */ }
                AssertCompactChip(FindNamed(canvas, "SYS"), "SYS");
                AssertCompactChip(FindNamed(canvas, "MAP"), "MAP");

                var drag = ArDragActionHud.Create(canvas);
                var banner = FindNamed(drag.transform, "DragBanner");
                var bannerImg = banner != null ? banner.GetComponent<Image>() : null;
                if (bannerImg == null)
                    throw new Exception("drag banner missing");
                if (!IsFilamentSprite(bannerImg.sprite) && bannerImg.color.a > 0.40f)
                    throw new Exception("drag banner too opaque a=" + bannerImg.color.a.ToString("0.00"));
                var choice = FindNamed(drag.transform, "DropChoice");
                if (choice == null)
                    throw new Exception("drop choice missing");
                var frame = CompAt<Image>(FindNamed(choice, "Frame"));   // no ?.: Unity null (fake / destroyed)
                if (frame == null || !IsFilamentSprite(frame.sprite))
                    throw new Exception("drop choice is a raw plate — needs inspect glass");
                if (FindNamed(drag.transform, "Exit") == null)
                    throw new Exception("drop choice missing EXIT");
                if (FindNamed(drag.transform, "Close") == null)
                    throw new Exception("drop choice missing Close");
                drag.ShowChoice("Test Card — M1", "Normal Summon (face-up Attack)",
                    "Set (face-down Defense)", () => { }, () => { }, () => { });
                var labels = CollectButtonLabels(drag.transform);
                if (!labels.Exists(s => s != null &&
                                        s.IndexOf("EXIT", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new Exception("drop choice EXIT label missing: " + string.Join(",", labels));
                if (!labels.Exists(s => s != null &&
                                        s.IndexOf("Normal Summon", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new Exception("drop choice summon label missing");
                drag.HideChoice();

                var gaugeSpr = ImagineAssets.StatGaugeTemplate();
                if (gaugeSpr == null || gaugeSpr.name.IndexOf("stat_gauge", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("stat gauge is not the swirl plate");
                var lpSpr = ImagineAssets.LpCounterTemplate();
                if (lpSpr == null || lpSpr.name.IndexOf("lp_counter", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("LP callout is not the swirl plate");
                if (ArEffectCallout.BoxWorldWidth > 0.22f)
                    throw new Exception("effect callout world width " + ArEffectCallout.BoxWorldWidth);
                if (ImagineAssets.HudIslandGlass() == null)
                    throw new Exception("missing Imagine hud_island_glass");
                if (ImagineAssets.HudChipCyan() == null || ImagineAssets.HudFabOrb() == null)
                    throw new Exception("missing Imagine arena chip/orb");
                var youFrame = CompAt<Image>(Kid(hud.Root.Find("YouScore"), "Frame"));
                if (youFrame == null || !IsFilamentSprite(youFrame.sprite))
                    throw new Exception("YouScore is not the filament island sprite");
                var oppFrame = CompAt<Image>(Kid(hud.Root.Find("OppScore"), "Frame"));
                if (oppFrame == null || !IsFilamentSprite(oppFrame.sprite))
                    throw new Exception("OppScore is not the filament island sprite");
            });
            Check("ArFieldSpellFloor is left/right wrap walls, not a street carpet", () =>
            {
                var host = new GameObject("FieldFloorSmoke").transform;
                var f = ArFieldSpellFloor.Create(host, 0);
                if (f == null || f.Root == null)
                    throw new Exception("field spell floor missing root");
                var left = f.Root.Find("WrapL");
                var right = f.Root.Find("WrapR");
                if (left == null || right == null)
                    throw new Exception("field wrap missing left/right walls");
                var lmf = left.GetComponent<MeshFilter>();
                if (lmf == null || lmf.sharedMesh == null || lmf.sharedMesh.vertexCount < 8)
                    throw new Exception("field wrap left is not a wall mesh");
                if (f.Root.Find("Ring") != null || f.Root.Find("ScrimFar") != null)
                    throw new Exception("field wrap still has ring/far scrim");
                f.Sync(null, null, null);
                if (left.gameObject.activeSelf || right.gameObject.activeSelf)
                    throw new Exception("field wrap stayed lit with no Field Spell");
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            });
            Check("arena holograms are not drop targets", () =>
            {
                var host = new GameObject("ArenaNoDropSmoke");
                var arena = ArArenaHologramManager.Create(host.transform, 0);
                var slots = new System.Collections.Generic.List<WRLDZ.Duel.LegalIntentService.LegalSlot>
                {
                    new WRLDZ.Duel.LegalIntentService.LegalSlot
                    {
                        Kind = WRLDZ.Duel.Rules.RulesZoneKind.Monster,
                        Index = 0,
                        CanSummonAtk = true
                    }
                };
                arena.ApplyLegalHighlights(slots);
                var pads = host.GetComponentsInChildren<ArLegalZonePad>(true);
                if (pads != null && pads.Length > 0)
                    throw new Exception("arena spawned legal drop pads (" + pads.Length + ")");
                UnityEngine.Object.DestroyImmediate(host);
            });
            Check("ArOppFieldGlance builds left of disk", () =>
            {
                var host = new GameObject("OppGlanceSmoke").transform;
                var g = ArOppFieldGlance.Create(host, 0);
                if (g == null || g.Root == null)
                    throw new Exception("opp field glance missing root");
                g.Sync(null, null);
                if (g.SlotCount < 11)
                    throw new Exception("opp field glance needs M5+ST5+Field slots");
                if (ArOppFieldGlance.WorldWidth < 0.45f)
                    throw new Exception("opp mini-playmat too small " + ArOppFieldGlance.WorldWidth);
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            });
            Check("opponent disk mirrors player kit (LP, deck, GY; no phase CTAs)", () =>
            {
                var host = new GameObject("OppDiskParity");
                var p = ArDuelDiskRig.Create(host.transform, true, 0, Color.cyan);
                var o = ArDuelDiskRig.Create(host.transform, false, 0, Color.magenta);
                if (o == null || o.LpCounter == null)
                    throw new Exception("opp disk missing LP counter");
                if (o.MainDeckZone == null)
                    throw new Exception("opp disk missing deck well");
                if (o.GraveyardZone == null)
                    throw new Exception("opp disk missing GY");
                if (o.MonsterZones == null || o.MonsterZones.Length != 5)
                    throw new Exception("opp disk missing monster pads");
                if (o.SpellTrapZones == null || o.SpellTrapZones.Length != 5)
                    throw new Exception("opp disk missing S/T slots");
                if (o.PhaseButtons != null)
                    throw new Exception("opp disk should not get the local player's phase chips");
                if (p.PhaseButtons == null)
                    throw new Exception("player disk missing phase chips");
                UnityEngine.Object.DestroyImmediate(host);
            });
            Check("opponent hand is backs-only and not pickable", () =>
            {
                var host = new GameObject("OppHandParity");
                var hv = ArHandVolume.Create(host.transform, 0, Color.red, opponent: true);
                if (!hv.IsOpponent || hv.RevealFaces)
                    throw new Exception("opp hand must hide faces");
                if (hv.PickByRay(new Ray(Vector3.zero, Vector3.forward)) != null)
                    throw new Exception("opp hand must not be tappable");
                UnityEngine.Object.DestroyImmediate(host);
            });
            Check("activated S/T hologram card-back faces the controller", () =>
            {
                var host = new GameObject("StHoloSmoke");
                var dummy = new WRLDZ.Duel.CardInstance { FaceUp = true, CardId = 1 };
                var vis = ArArenaCardVisual.Create(host.transform, dummy, null, 0,
                    playerSide: true, isMonster: false, localPos: Vector3.zero);
                var art = vis.transform.Find("SpellTrapArtwork");
                var back = vis.transform.Find("SpellTrapArtworkBack");
                if (art == null || back == null)
                    throw new Exception("S/T holo missing art/back quads");
                var artMr = art.GetComponent<MeshRenderer>();
                var mat = artMr != null ? artMr.sharedMaterial : null;
                if (mat != null && mat.HasProperty("_Cull") && mat.GetFloat("_Cull") < 1.5f)
                    throw new Exception("S/T art is double-sided — controller would see art, not the back");
                if (back.localPosition.z >= -0.004f)
                    throw new Exception("S/T back quad is not offset toward the controller");
                var yaw = vis.transform.localEulerAngles.y;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, ArArenaCardVisual.SpellControllerYaw(true))) > 8f)
                    throw new Exception(
                        "player S/T yaw must put the card back toward the controller, got " + yaw);
                var backMr = back.GetComponent<MeshRenderer>();
                var backMat = backMr != null ? backMr.sharedMaterial : null;
                Texture backTex = null;
                if (backMat != null)
                {
                    if (backMat.HasProperty("_BaseMap")) backTex = backMat.GetTexture("_BaseMap");
                    if (backTex == null && backMat.HasProperty("_MainTex"))
                        backTex = backMat.GetTexture("_MainTex");
                }

                if (backTex == null || backTex == Texture2D.whiteTexture || backTex == Texture2D.grayTexture)
                    throw new Exception("S/T back is missing the card-back texture");
                UnityEngine.Object.DestroyImmediate(host);
            });
            Check("disk phase chips sit on the blade interior by the deck", () =>
            {
                var host = new GameObject("PhaseRailSmoke").transform;
                var rail = ArDiskPhaseButtons.Create(host, 0);
                if (rail == null || rail.Root == null)
                    throw new Exception("phase rail missing root");
                if (rail.Root.Find("Battle") == null || rail.Root.Find("Main2") == null ||
                    rail.Root.Find("End") == null)
                    throw new Exception("phase rail missing Battle/Main2/End chips");
                var battle = rail.Root.Find("Battle");
                var lp = ArPlaymatLayout.DiskLpWindowLocal;
                if (Mathf.Abs(battle.localPosition.z - lp.z) > 0.01f ||
                    Mathf.Abs(battle.localPosition.y - lp.y) > 0.01f)
                    throw new Exception("phase chips are not on the LP hub band");
                if (battle.localPosition.x <= lp.x)
                    throw new Exception("BATTLE chip is not +X of the LP window");
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            });
            Check("deck pack fits v37 spring holder under the flap", () =>
            {
                // Cults v37 DECK SPRING HOLDER AABB (BattleCityDuelDisk.obj).
                const float holderMinX = -0.1009f, holderMaxX = -0.0643f;
                const float holderMinZ = 0.2090f, holderMaxZ = 0.2350f;
                var o = ArDeckWellCards.WellOrigin;
                var halfW = ArDeckWellCards.Width * 0.5f;
                var halfL = ArDeckWellCards.Length * 0.5f;
                var top = o.y + ArDeckWellCards.PackHeight;
                if (top > ArDeckWellCards.WellCeilingY + 0.0004f)
                    throw new Exception(
                        $"deck pack top {top:0.0000} punches the DECK FLAP / hub roof " +
                        $"(ceiling {ArDeckWellCards.WellCeilingY:0.0000})");
                if (o.x - halfW < holderMinX || o.x + halfW > holderMaxX)
                    throw new Exception("deck pack X does not fit the spring holder");
                if (o.z - halfL < holderMinZ || o.z + halfL > holderMaxZ)
                    throw new Exception("deck pack Z does not fit the spring holder");
                if (o.y < 0.0010f || o.y > 0.0030f)
                    throw new Exception("deck pack is not seated on the spring-holder floor");
            });
            Check("command buttons are floating glass (no 9-slice plate, no LabelScrim)", () =>
            {
                var primary = MenuCommandButton.Create(host, "TEST PRIMARY", () => { },
                    MenuCommandButton.Kind.Primary);
                AssertFloatingChip(primary, "MenuCommandButton.Primary");

                var gold = FloatingPanel.PrimaryButton(host, "TEST GOLD", () => { }, gold: true);
                AssertFloatingChip(gold, "FloatingPanel.PrimaryButton gold");

                var danger = MenuCommandButton.Create(host, "TEST DANGER", () => { },
                    MenuCommandButton.Kind.Danger);
                AssertFloatingChip(danger, "MenuCommandButton.Danger");

                SettingsScreen.Build(host, overlay, () => { });
                AssertNoLabelScrim(host, "SettingsScreen");
            });
            Check("ArCompanionPhoneHud", () => ArCompanionPhoneHud.Build(canvas, () => { }));
            Check("ZoneModePrompt", () => ZoneModePrompt.Open(null, "lab", "Lab Tear"));
            Check("DualMenuPresenter phone+AR frames", () =>
            {
                DualMenuPresenter.BuildFrame(host, "Deck", "sub", () => { }, UiPresentation.NonArPortrait);
                DualMenuPresenter.BuildFrame(host, "Settings", "sub", () => { }, UiPresentation.ArDiskHolo);
            });

            Check("OnHandDeckSwitch empty copy is a detached popout", () =>
            {
                AssertDetachedEmpty(host);
            });

            Check("OnHandDeckSwitch lists pocket deck names", () =>
            {
                AssertPocketNames(host);
            });

            Check("OverworldUI deck switcher pops off MenuGlass", () =>
            {
                var switcherSession = AppSession.Ensure();
                if (switcherSession == null || switcherSession.Account == null)
                    throw new Exception("no session account for overworld switcher");
                DestroyNamed("OverworldHudCanvas");
                DestroyNamed("OverworldMapCanvas");
                var go = new GameObject("OwSmoke");
                go.AddComponent<OverworldUI>().Build();
                var glass = GameObject.Find("MenuGlass");
                if (glass == null) throw new Exception("MenuGlass missing");
                var art = FindNamed(glass.transform, "DeckArt");
                var btn = art != null ? art.GetComponent<Button>() : null;
                if (btn == null) throw new Exception("deck orb button missing");
                btn.onClick.Invoke();
                var list = GameObject.Find(OnHandDeckSwitchMenu.HostName);
                if (list == null)
                    throw new Exception("DeckSwitchList missing");
                if (list.transform.parent == glass.transform)
                    throw new Exception("list still parented to MenuGlass");
                var labels = list.GetComponentsInChildren<Text>(true);
                var any = false;
                foreach (var t in labels)
                {
                    if (t != null && !string.IsNullOrWhiteSpace(t.text))
                    {
                        any = true;
                        break;
                    }
                }

                if (!any) throw new Exception("switcher list has no visible labels");
                UnityEngine.Object.DestroyImmediate(go);
                DestroyNamed("OverworldHudCanvas");
                DestroyNamed("OverworldMapCanvas");
            });

            OurCheck("DeckCollection caret opens on-hand dropdown", () =>
            {
                DeckCollectionScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                var caret = FindNamed(host, "Caret");
                if (caret == null) throw new Exception("Caret missing");
                var es = EventSystem.current;
                if (es == null) es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
                var ped = new PointerEventData(es);
                ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerUpHandler);
                if (FindNamed(host, "DeckDropdown") == null)
                    throw new Exception("DeckDropdown did not open");
            });

            // Cleanup smoke objects so the editor scene is not left polluted
            try
            {
                foreach (var n in new[]
                         {
                             "MenuSmokeCanvas", "MenuShellCanvas", "MenuShellOverlay",
                             "HubSmoke", "DesktopLabApp", "DiskMenuCanvas",
                             "ZoneModePromptCanvas", "AvatarCustomizerUI",
                             "OwSmoke", "OverworldHudCanvas", "OverworldMapCanvas",
                             "HubSmokeV6", "HubSmokeV6Local", "HubSmokeV6NoArt", "HubSmokeLegacy",
                             "MenuV6NoArtCanvas", "HubPropWorld", "HubSmokeV6LocalPort", "MenuV6NoArtCanvasPort",
                             "HubSmokeV6Port", "HubSmokeV6Rigs",
                             "BootCanvas", "BootFlowHostSmoke", "HubSmokeV6Entrance",
                             "HubSmokeV6Sheets", "HubSmokeV6SheetsModal", FrameGhost.GhostName
                         })
                    DestroyNamed(n);
            }
            catch { /* ignore cleanup */ }

            var ok = fails == 0;
            sb.Insert(0, (ok ? "PASS" : "FAIL") + $"  {checks - fails}/{checks} checks\n");
            return new Report
            {
                Ok = ok,
                Checks = checks,
                Failures = fails,
                Summary = sb.ToString()
            };
        }

        public static Report RunDeckSwitch()
        {
            var sb = new StringBuilder();
            var checks = 0;
            var fails = 0;

            void Check(string name, Action act)
            {
                checks++;
                try
                {
                    act();
                    sb.AppendLine("  ok  " + name);
                }
                catch (Exception ex)
                {
                    fails++;
                    sb.AppendLine("  FAIL " + name + " — " + ex.GetType().Name + ": " + ex.Message);
                    Debug.LogException(ex);
                }
            }

            sb.AppendLine("WRLDZ deck-switch smoke");
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var canvas = WrldzTheme.Canvas("DeckSwitchSmokeCanvas", 50);
            var host = new GameObject("SwitchSmokeHost", typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(canvas, false);
            FloatingPanel.Stretch(host);

            Check("OnHandDeckSwitch empty copy is a detached popout", () =>
            {
                AssertDetachedEmpty(host);
            });

            Check("OnHandDeckSwitch lists pocket deck names", () =>
            {
                AssertPocketNames(host);
            });

            Check("OnHandDeckSwitch auto-fits long deck names", () =>
            {
                AssertLongNameWiderThanShort();
            });

            Check("OnHandDeckSwitch button contains its text", () =>
            {
                AssertButtonContainsText(host);
            });

            DestroyNamed("DeckSwitchSmokeCanvas");
            var ok = fails == 0;
            sb.Insert(0, (ok ? "PASS" : "FAIL") + $"  {checks - fails}/{checks} checks\n");
            return new Report
            {
                Ok = ok,
                Checks = checks,
                Failures = fails,
                Summary = sb.ToString()
            };
        }

        // BUILD_PLAN §3: the labels the v6.3 Stone must carry (MenuV6Stage.cs holds the literals) and today's
        // labels for the legacy fallback (DuelDiskMenuUI.BuildVisualsLegacy: SET, not SETTINGS).
        static readonly string[] MainMenuV6Labels =
            { "DUEL", "DECK", "BAG", "STORY", "BAZAAR", "WORLD MAP", "QUIT", "SETTINGS", "VIEW" };

        static readonly string[] MainMenuLegacyLabels =
            { "DUEL", "DECK", "BAG", "STORY", "BAZAAR", "WORLD MAP", "QUIT", "SET", "VIEW" };

        static readonly string[] MainMenuControls =
            { "Hero", "Dest_DECK", "Dest_BAG", "Dest_STORY", "Dest_BAZAAR", "Tert_VIEW", "Tert_SET", "MapBack", "Quit" };

        /// <summary>P5: the page's own director test vectors (dread_&lt;layout&gt;.json director.testVectorsIdle / Story) against
        /// DreadMath.State, every column.</summary>
        static void DreadVectors(MenuLayoutKind kind, string tag)
        {
            var ta = MenuV6Art.Data(kind == MenuLayoutKind.Landscape ? "dread_land" : "dread_port");
            var root = ta != null ? MenuV6Json.Parse(ta.text) as System.Collections.Generic.Dictionary<string, object> : null;
            var dir = RJ.D(root, "director");
            foreach (var (key, story) in new[] { ("testVectorsIdle", false), ("testVectorsStory", true) })
            {
                var rows = RJ.L(dir, key);
                if (rows == null || rows.Count == 0) throw new Exception(tag + "no director " + key);
                foreach (var r in rows)
                {
                    var a = r as System.Collections.Generic.List<object>;
                    var t = RJ.At(a, 0);
                    var S = DreadMath.State(t, story ? DreadMath.StoryD0 : double.NaN);
                    var got = new double[] { t, S.Tier, S.OmenU, S.WhisperU, S.ApparU, S.Omen, S.Whisper, S.Appar, S.Still, S.Lid, S.Surf, S.Push, S.Relief, S.Sg, S.Drop };
                    for (var i = 1; i < got.Length && a != null && i < a.Count; i++)
                        if (Math.Abs(got[i] - RJ.At(a, i)) > 2e-5)
                            throw new Exception(tag + key + " t " + t + " column " + i + ": " + got[i] + " vs the page's " + RJ.At(a, i));
                }
            }
        }

        static void RequireMenuV6Art()
        {
            if (!MenuV6Stage.HasArt(MenuLayoutKind.Landscape))
                throw new Exception("v6 art missing under Resources/" + MenuV6Art.Root +
                                    " (the P1a art set is not installed)");
        }

        static void RequireMainMenu(Transform canvas, string[] labels)
        {
            if (canvas == null) throw new Exception("menu canvas missing");
            foreach (var l in labels) RequireLabel(canvas, l);
            if (FindNamed(canvas, "Tiles") == null) throw new Exception("Tiles host missing");
        }

        /// <summary>Build the main menu for a forced surface, run <paramref name="check"/>, and remove the host and its
        /// canvas again (the owner's open scene keeps nothing, R20).</summary>
        static void WithHub(string hostName, int w, int h, Action<DuelDiskMenuUI> check)
        {
            GameObject go = null;
            DuelDiskMenuUI menu = null;
            try
            {
                MenuAspect.Force(w, h);
                go = new GameObject(hostName);
                menu = go.AddComponent<DuelDiskMenuUI>();
                menu.Build();
                check(menu);
            }
            finally
            {
                MenuAspect.ClearForce();
                if (menu != null && menu.MenuCanvas != null)
                    UnityEngine.Object.DestroyImmediate(menu.MenuCanvas.gameObject);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void RequireLabel(Transform root, string text)
        {
            if (root == null) throw new Exception("missing host for " + text);
            var labels = root.GetComponentsInChildren<Text>(true);
            foreach (var t in labels)
            {
                if (t != null && !string.IsNullOrEmpty(t.text) &&
                    t.text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }

            throw new Exception("missing label " + text);
        }

        static bool IsFilamentSprite(Sprite spr) =>
            spr != null && spr.name != null &&
            (spr.name.IndexOf("hud_", StringComparison.OrdinalIgnoreCase) >= 0
             || spr.name.IndexOf("icon_arena", StringComparison.OrdinalIgnoreCase) >= 0);

        static void AssertGlanceIsland(Transform island, string where, float maxW, float maxH)
        {
            if (island == null) throw new Exception(where + " missing");
            var rt = island.GetComponent<RectTransform>();
            if (rt == null) throw new Exception(where + " missing RectTransform");
            var w = rt.anchorMax.x - rt.anchorMin.x;
            var h = rt.anchorMax.y - rt.anchorMin.y;
            if (w > maxW + 0.001f)
                throw new Exception(where + " too wide " + w.ToString("0.000"));
            if (h > maxH + 0.001f)
                throw new Exception(where + " too tall " + h.ToString("0.000"));
            var img = island.GetComponent<Image>();
            var framed = island.Find("Frame") != null;
            if (img != null && !IsFilamentSprite(img.sprite) && !framed && img.color.a > 0.28f)
                throw new Exception(where + " fill too opaque a=" + img.color.a.ToString("0.00"));
            if (img != null && framed && img.color.a > 0.50f)
                throw new Exception(where + " scrim too opaque a=" + img.color.a.ToString("0.00"));
        }

        static void AssertCompactChip(Transform chip, string where)
        {
            if (chip == null) throw new Exception(where + " missing");
            var rt = chip.GetComponent<RectTransform>();
            if (rt == null) throw new Exception(where + " missing RectTransform");
            var w = rt.anchorMax.x - rt.anchorMin.x;
            var h = rt.anchorMax.y - rt.anchorMin.y;
            if (w > 0.07f)
                throw new Exception(where + " too wide " + w.ToString("0.000"));
            if (h > 0.08f)
                throw new Exception(where + " too tall " + h.ToString("0.000"));
            var img = chip.GetComponent<Image>();
            if (img != null && !IsFilamentSprite(img.sprite) && img.color.a > 0.40f)
                throw new Exception(where + " fill too opaque a=" + img.color.a.ToString("0.00"));
        }

        /// <summary>
        /// Toolbar / command chips must be translucent glass — 9-slice plates
        /// collapse to opaque black at 56px, and LabelScrim paints a black well
        /// over the face.
        /// </summary>
        static void AssertFloatingChip(Button btn, string where)
        {
            if (btn == null) throw new Exception(where + " missing button");
            if (btn.transform.Find("LabelScrim") != null)
                throw new Exception(where + " has LabelScrim overlay");
            var img = btn.GetComponent<Image>();
            if (img == null) throw new Exception(where + " missing Image");
            if (img.type == Image.Type.Sliced)
                throw new Exception(where + " uses 9-slice plate (" +
                                    (img.sprite != null ? img.sprite.name : "null") + ")");
            if (img.color.a > 0.72f)
                throw new Exception(where + " fill too opaque a=" + img.color.a.ToString("0.00"));
            var lum = img.color.r * 0.3f + img.color.g * 0.59f + img.color.b * 0.11f;
            if (lum < 0.05f)
                throw new Exception(where + " fill is near-black lum=" + lum.ToString("0.00"));
        }

        static void AssertNoLabelScrim(Transform root, string where)
        {
            if (root == null) return;
            var trs = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in trs)
            {
                if (t != null && t.name == "LabelScrim")
                    throw new Exception(where + " still has LabelScrim on " + PathOf(t));
            }
        }

        static string PathOf(Transform t)
        {
            var s = t.name;
            var p = t.parent;
            while (p != null)
            {
                s = p.name + "/" + s;
                p = p.parent;
            }
            return s;
        }

        static void AssertDetachedEmpty(Transform hud)
        {
            var bar = MakeSwitchGlass(hud);
            var list = OnHandDeckSwitchMenu.Build(AsRect(hud),
                null, _ => { });
            if (list == null || list.name != OnHandDeckSwitchMenu.HostName)
                throw new Exception("missing DeckSwitchList");
            if (list.transform.parent == bar)
                throw new Exception("list parented to the menu bar");
            RequireLabel(list.transform, OnHandDeckSwitchMenu.EmptyCopy);
        }

        static void AssertPocketNames(Transform hud)
        {
            var inv = PlayerInventory.Empty();
            inv.deckBoxSlotCount = 3;
            inv.EnsureDeckBoxSlots();
            inv.deckBoxes[0].name = "Beatdown Box";
            inv.deckBoxes[1].name = "Control Box";
            inv.carryDeckBoxSlots = 2;
            inv.pockets.pocketCount = 2;
            inv.pockets.pocketDeckBoxIndex = new[] { 0, 1 };
            var list = OnHandDeckSwitchMenu.Build(AsRect(hud),
                inv, _ => { });
            RequireLabel(list.transform, "Beatdown Box");
            RequireLabel(list.transform, "Control Box");
        }

        static void AssertLongNameWiderThanShort()
        {
            const float hudW = 1080f;
            var shortW = OnHandDeckSwitchMenu.PreferredWidth(hudW, new[] { "Deck" });
            var longW = OnHandDeckSwitchMenu.PreferredWidth(hudW,
                new[] { "Legendary Ocean's Revenge" });
            if (longW <= shortW + 8f)
                throw new Exception($"long name did not widen plate ({longW} vs {shortW})");
            if (longW <= 420f)
                throw new Exception($"long name still capped at old 420px width ({longW})");
        }

        static void AssertButtonContainsText(Transform hud)
        {
            var inv = PlayerInventory.Empty();
            inv.deckBoxSlotCount = 1;
            inv.EnsureDeckBoxSlots();
            inv.deckBoxes[0].name = "Legendary Ocean's Revenge";
            inv.carryDeckBoxSlots = 1;
            inv.pockets.pocketCount = 1;
            inv.pockets.pocketDeckBoxIndex = new[] { 0 };
            var list = OnHandDeckSwitchMenu.Build(AsRect(hud),
                inv, _ => { });
            LayoutRebuilder.ForceRebuildLayoutImmediate(list.GetComponent<RectTransform>());
            var row = FindNamed(list.transform, "D0");
            if (row == null) throw new Exception("row D0 missing");
            var rowW = row.GetComponent<RectTransform>().rect.width;
            var label = row.GetComponentInChildren<Text>();
            if (label == null) throw new Exception("row label missing");
            var textW = label.preferredWidth;
            if (rowW + 1f < textW)
                throw new Exception($"button {rowW:0}px is narrower than text {textW:0}px");
        }

        static RectTransform MakeSwitchGlass(Transform parent)
        {
            var go = new GameObject("SwitchGlass", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            FloatingPanel.Place(rt, 0.08f, 0.08f, 0.92f, 0.42f);
            return rt;
        }

        static System.Collections.Generic.List<string> CollectButtonLabels(Transform root)
        {
            var list = new System.Collections.Generic.List<string>();
            if (root == null) return list;
            var texts = root.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && !string.IsNullOrEmpty(texts[i].text))
                    list.Add(texts[i].text);
            }

            return list;
        }

        // Unity-null-safe helpers (no ?? / ?. on UnityEngine.Object: a missing component is a fake null in the Editor and a
        // destroyed object is non-null to C#; both pass ?? and ?. and then throw)
        static T CompAt<T>(Transform t) where T : Component => t != null ? t.GetComponent<T>() : null;
        static Transform Kid(Transform t, string name) => t != null ? t.Find(name) : null;
        /// <summary>A canvas with a "ModalHost" marked as MenuShell marks its own (P3), for the sheet checks.</summary>
        static RectTransform SmokeModalHost(string name, SanctuaryHost hostKind)
        {
            DestroyNamed(name);
            var canvas = WrldzTheme.Canvas(name, 92, true);
            var modal = new GameObject("ModalHost", typeof(RectTransform)).GetComponent<RectTransform>();
            modal.SetParent(canvas, false);
            FloatingPanel.Stretch(modal);
            SanctuaryChrome.Mark(modal.gameObject, true, hostKind);
            return modal;
        }

        /// <summary>Invoke the Button of the control named <paramref name="name"/> under <paramref name="root"/> (a tap).</summary>
        static void Press(Transform root, string name)
        {
            var b = CompAt<Button>(FindNamed(root, name));
            if (b == null) throw new Exception("no control " + name);
            b.onClick.Invoke();
        }

        static RectTransform AsRect(Transform t)
        {
            if (t == null) return null;
            var rt = t as RectTransform;
            return rt != null ? rt : t.GetComponent<RectTransform>();
        }

        static Transform FindNamed(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = FindNamed(root.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }

        /// <summary>Every root GameObject of every loaded scene, by instance (P5: OurCheck's before-list).</summary>
        static System.Collections.Generic.HashSet<GameObject> RootSet()
        {
            var ids = new System.Collections.Generic.HashSet<GameObject>();
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                    if (go != null)
                        ids.Add(go);
            }

            return ids;
        }

        /// <summary>Remove every root made since <paramref name="before"/> (never one that was there already, nor
        /// <paramref name="keep"/>; edit mode only).</summary>
        static int DestroyNewRoots(System.Collections.Generic.HashSet<GameObject> before, GameObject keep = null)
        {
            if (Application.isPlaying || before == null) return 0;
            var made = new System.Collections.Generic.List<GameObject>();
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                    if (go != null && !before.Contains(go) && go != keep)
                        made.Add(go);
            }

            foreach (var go in made)
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            return made.Count;
        }

        static void DestroyNamed(string name)
        {
            // every copy (each build makes its own DiskMenuCanvas)
            for (var guard = 0; guard < 32; guard++)
            {
                var go = GameObject.Find(name);
                if (go == null) return;
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
