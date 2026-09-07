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

            sb.AppendLine("WRLDZ menu smoke");
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();

            try { AppSession.Ensure().EnsureLabTestAccount(out _); }
            catch (Exception ex) { sb.AppendLine("  warn lab account: " + ex.Message); }

            var canvas = WrldzTheme.Canvas("MenuSmokeCanvas", 50);
            var host = new GameObject("SmokeHost", typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(canvas, false);
            FloatingPanel.Stretch(host);

            Check("MenuShell.CreateOverlay", () =>
            {
                var shell = MenuShell.CreateOverlay(90);
                if (shell == null || shell.ModalHost == null)
                    throw new Exception("overlay shell missing ModalHost");
            });

            Check("MenuShell.Create (full hub)", () =>
            {
                var shell = MenuShell.Create(91);
                if (shell == null || shell.ContentHost == null)
                    throw new Exception("full shell missing ContentHost");
            });

            Check("DuelDiskMenuUI.Build", () =>
            {
                var go = new GameObject("HubSmoke");
                go.AddComponent<DuelDiskMenuUI>().Build();
            });

            Check("DesktopLabApp.Open", () =>
            {
                DesktopLabApp.Open();
                if (DesktopLabApp.IsActive == false)
                    throw new Exception("lab not marked active");
            });

            MenuShell overlay = null;
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

            Check("DeckCollection phone", () =>
            {
                DeckCollectionScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                RequireLabel(host, "MAIN  ");
                RequireLabel(host, "EXTRA  ");
                RequireLabel(host, "SIDE  ");
                RequireLabel(host, "CARD LIST");
            });
            Check("DeckCollection AR holo", () =>
            {
                DeckCollectionScreen.Build(host, () => { }, UiPresentation.ArDiskHolo);
                RequireLabel(host, "MAIN  ");
                RequireLabel(host, "EXTRA  ");
                RequireLabel(host, "SIDE  ");
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
                var frame = FindNamed(choice, "Frame")?.GetComponent<Image>();
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
                var youFrame = hud.Root.Find("YouScore")?.Find("Frame")?.GetComponent<Image>();
                if (youFrame == null || !IsFilamentSprite(youFrame.sprite))
                    throw new Exception("YouScore is not the filament island sprite");
                var oppFrame = hud.Root.Find("OppScore")?.Find("Frame")?.GetComponent<Image>();
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
                var mat = art.GetComponent<MeshRenderer>()?.sharedMaterial;
                if (mat != null && mat.HasProperty("_Cull") && mat.GetFloat("_Cull") < 1.5f)
                    throw new Exception("S/T art is double-sided — controller would see art, not the back");
                if (back.localPosition.z >= -0.004f)
                    throw new Exception("S/T back quad is not offset toward the controller");
                var yaw = vis.transform.localEulerAngles.y;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, ArArenaCardVisual.SpellControllerYaw(true))) > 8f)
                    throw new Exception(
                        "player S/T yaw must put the card back toward the controller, got " + yaw);
                var backMat = back.GetComponent<MeshRenderer>()?.sharedMaterial;
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
                if (AppSession.Ensure()?.Account == null)
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

            Check("DeckCollection caret opens on-hand dropdown", () =>
            {
                DeckCollectionScreen.Build(host, () => { }, UiPresentation.NonArPortrait);
                var caret = FindNamed(host, "Caret");
                if (caret == null) throw new Exception("Caret missing");
                var es = EventSystem.current ?? UnityEngine.Object.FindAnyObjectByType<EventSystem>();
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
                             "OwSmoke", "OverworldHudCanvas", "OverworldMapCanvas"
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
            var list = OnHandDeckSwitchMenu.Build(hud as RectTransform ?? hud.GetComponent<RectTransform>(),
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
            var list = OnHandDeckSwitchMenu.Build(hud as RectTransform ?? hud.GetComponent<RectTransform>(),
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
            var list = OnHandDeckSwitchMenu.Build(hud as RectTransform ?? hud.GetComponent<RectTransform>(),
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

        static void DestroyNamed(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
