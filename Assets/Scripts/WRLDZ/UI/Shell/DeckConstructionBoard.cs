using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// EDOPro / Master Duel construction table: Main, Extra, and Side
    /// stay on screen together as overlapping card faces.
    ///
    /// One solver (<see cref="DeckBoardLayout"/>) sizes the whole board, so all
    /// three piles draw the same card at the same column pitch off the same
    /// left edge. Per-tray sizing is what made Extra/Side render at a third of
    /// MAIN's card size on a landscape Game view.
    /// </summary>
    public static partial class DeckCollectionScreen
    {
        // P4 (BUILD_PLAN §6 P4 step 4): the altar of the v6.3 offering hall — 10 columns of the page's 78 × 114 wells
        // (landscape; 94 × 137 portrait), so the aspect is the page's 114 / 78.
        const float BoardCardAspect = 1.4615f;
        const float MaxCardW = 156f;
        // Floor for a legible face. The solver drops under it only when even
        // one row per pile would not fit — a squat card beats a clipped row.
        const float MinCardW = 44f;
        // P4: Main always lays 10 columns (the hall's baked wells); more cards scroll whole rows.
        const int MainColsMin = 10;
        const int MainColsMax = 10;
// Tall enough for WrldzType.Readable titles. 20px clipped a ~36px count.
        const float HeadBarH = 40f;

        static bool IsAr(State st) =>
            st.Presentation == UiPresentation.ArDiskHolo
            || st.Presentation == UiPresentation.ArWorldPanel;

        static bool IsWideLayout(RectTransform phase)
        {
            var rt = phase;
            while (rt.parent is RectTransform p) rt = p;
            var r = rt.rect;
            if (r.width < 16f)
                return Screen.width > Screen.height * 1.15f;
            return r.width > r.height * 1.15f;
        }

        /// <summary>
        /// Pure board geometry. No Unity types beyond Mathf so the numbers can
        /// be replayed off-Editor. Keep in lockstep with
        /// Tools/deck_editor_layout_check.py.
        /// P4: EXTRA and SIDE share one row under MAIN (the page's .xrow), their faces at <see cref="XRowScale"/> of MAIN's
        /// (the page's 54.6 / 78), so the budget is well(rows) + well(1 at the x-row face), not well(rows) + 2 × well(1).
        /// </summary>
        internal static class DeckBoardLayout
        {
            public const float BoardPadX = 6f;
            public const float BoardPadY = 4f;
            public const float BoardSpacing = 4f;
            public const float TrayPadX = 6f;
            public const float TrayPadTop = 2f;
            public const float TrayPadBottom = 3f;
            public const float TraySpacing = 1f;
            public const float WellPad = 2f;
            public const float GapX = 5f;
            public const float GapY = 7f;

            /// <summary>The shared row's faces against MAIN's (the page: 54.6 × 79.8 over 78 × 114).</summary>
            public const float XRowScale = 0.7f;

            /// <summary>EXTRA's share of the shared row (SIDE takes the rest; the page's SIDE label starts at x 421 of 58-911).</summary>
            public const float XRowSplit = 0.42f;

            public const float XRowSpacing = 4f;

            /// <summary>The inline pile labels left of the shared-row wells ("EXTRA  3/15", "SIDE  0/15"): the page's
            /// cards start at x 198.8 and 530.2 (deck_land.json geo.els.xrowItems).</summary>
            public const float XHeadExtraW = 127f;
            public const float XHeadSideW = 103f;

            /// <summary>The shared row's own pitch gap (the page's .xrow: 62.6 px between 54.6 px faces).</summary>
            public const float XGapX = 8f;

            /// <summary>The hall's altar: 10 × 4 wells (BUILD_PLAN §6 P4 step 4).</summary>
            public const int HallCols = 10;
            public const int HallRows = 4;

            /// <summary>The page's slot widths (deck_land / deck_port.json geo.D.altar.sw).</summary>
            public const float HallSlotLand = 78f;
            public const float HallSlotPort = 94f;

            /// <summary>MAIN's tray height above and below its card rows.</summary>
            public const float TrayChrome = TrayPadTop + TrayPadBottom + HeadBarH + TraySpacing;

            /// <summary>A shared-row tray: its label is inline, so only the pads.</summary>
            public const float XTrayChrome = TrayPadTop + TrayPadBottom;

            /// <summary>Board height that is never available to card rows (MAIN's tray + the shared row's).</summary>
            public const float BoardChrome = BoardPadY * 2f + BoardSpacing + TrayChrome + XTrayChrome;

            public struct Plan
            {
                public int Cols;
                public float CardW;
                public float CardH;
                public int MainRows;
                public float MainWell;
                public float RowWell;
                public bool MainScrolls;
            }

            /// <summary>Horizontal run available to card faces inside a tray.</summary>
            public static float RowWidth(float boardW) =>
                Mathf.Max(32f, boardW - BoardPadX * 2f - TrayPadX * 2f - WellPad * 2f);

            public static float CardWidthFor(float boardW, int cols) =>
                Mathf.Min(MaxCardW, (RowWidth(boardW) - GapX * (cols - 1)) / cols);

            /// <summary>Height a well needs to show <paramref name="rows"/> whole rows.</summary>
            public static float WellFor(int rows, float cardH) =>
                WellPad * 2f + rows * cardH + Mathf.Max(0, rows - 1) * GapY;

            /// <summary>The shared row's well for MAIN's face height <paramref name="cardH"/>.</summary>
            public static float XWellFor(float cardH) => WellFor(1, cardH * XRowScale);

            /// <summary>The well run of one shared-row pile (EXTRA when <paramref name="extra"/>, else SIDE) on a board.</summary>
            public static float XRun(float boardW, bool extra)
            {
                var row = Mathf.Max(32f, boardW - BoardPadX * 2f - XRowSpacing);
                var tray = row * (extra ? XRowSplit : 1f - XRowSplit);
                return Mathf.Max(8f, tray - TrayPadX * 2f - (extra ? XHeadExtraW : XHeadSideW) - WellPad * 2f);
            }

            /// <summary>The hall's board (stage px) for slot 0 at (<paramref name="slotX"/>, <paramref name="slotY"/>) and
            /// <paramref name="slotW"/>-wide slots: the cards land in the baked wells (BUILD_PLAN §6 P4 step 4).</summary>
            public static void HallBoard(float slotX, float slotY, float slotW, out float x, out float y, out float w,
                out float h)
            {
                var ch = slotW * BoardCardAspect;
                w = HallCols * slotW + (HallCols - 1) * GapX + (WellPad + TrayPadX + BoardPadX) * 2f;
                h = BoardChrome + WellFor(HallRows, ch) + XWellFor(ch) + 0.5f;
                x = slotX - WellPad - TrayPadX - BoardPadX;
                y = slotY - WellPad - HeadBarH - TraySpacing - TrayPadTop - BoardPadY;
            }

            /// <summary>
            /// Biggest card that still shows every MAIN row plus the shared EXTRA / SIDE row. Falls back to the smallest
            /// legible card (and lets MAIN scroll) when the board is too short for the whole deck.
            /// </summary>
            public static Plan Solve(float boardW, float boardH, int mainSlots)
            {
                mainSlots = Mathf.Max(1, mainSlots);
                var budget = Mathf.Max(24f, boardH - BoardChrome);
                var plan = new Plan();
                var solved = false;

                for (var cols = MainColsMin; cols <= MainColsMax; cols++)
                {
                    var cw = CardWidthFor(boardW, cols);
                    if (cw < MinCardW) break;
                    var ch = cw * BoardCardAspect;
                    var rows = Mathf.Max(1, Mathf.CeilToInt(mainSlots / (float)cols));
                    if (WellFor(rows, ch) + XWellFor(ch) > budget) continue;
                    plan.Cols = cols;
                    plan.CardW = cw;
                    plan.CardH = ch;
                    plan.MainRows = rows;
                    solved = true;
                    break;
                }

                if (!solved)
                {
                    // Nothing fits whole. Take the smallest legible card, and if
                    // even one MAIN row and the shared row overflow, shrink under the
                    // floor — a squat card beats a row sliced off at the tray edge.
                    var cols = MainColsMax;
                    while (cols > MainColsMin && CardWidthFor(boardW, cols) < MinCardW) cols--;
                    var cw = CardWidthFor(boardW, cols);
                    var ch = cw * BoardCardAspect;
                    var room = budget - WellPad * 4f;
                    if (ch * (1f + XRowScale) > room)
                    {
                        ch = Mathf.Max(16f, room / (1f + XRowScale));
                        cw = ch / BoardCardAspect;
                    }

                    plan.Cols = cols;
                    plan.CardW = cw;
                    plan.CardH = ch;
                    plan.MainRows = Mathf.Max(1, Mathf.CeilToInt(mainSlots / (float)cols));
                }

                plan.RowWell = XWellFor(plan.CardH);
                var mainNeed = WellFor(plan.MainRows, plan.CardH);
                var mainRoom = budget - plan.RowWell;
                if (mainNeed <= mainRoom)
                {
                    plan.MainWell = mainNeed;
                    plan.MainScrolls = false;
                }
                else
                {
                    // Show whole rows only — a half card at the tray edge is the
                    // clipped look this board had before.
                    var whole = Mathf.Max(1, Mathf.FloorToInt(
                        (mainRoom - WellPad * 2f + GapY) / (plan.CardH + GapY)));
                    plan.MainWell = WellFor(whole, plan.CardH);
                    plan.MainScrolls = true;
                }

                // Spend the leftover on breathing room, split by row count, so
                // no tray is left with a dead band under its cards.
                var used = plan.MainWell + plan.RowWell;
                var slack = Mathf.Max(0f, budget - used);
                if (slack > 0.5f)
                {
                    if (plan.MainScrolls)
                    {
                        // Growing MAIN here would expose the top of the next row.
                        plan.RowWell += slack;
                    }
                    else
                    {
                        var share = plan.MainRows + 1f;
                        plan.MainWell += slack * (plan.MainRows / share);
                        plan.RowWell += slack * (1f / share);
                    }
                }

                return plan;
            }

            /// <summary>
            /// Column pitch across a run of <paramref name="run"/> units. A pile
            /// too wide for its tray fans its faces the way Extra/Side overlap
            /// in a real client.
            /// </summary>
            public static float StepFor(float run, int n, float cardW, float gap = GapX)
            {
                if (n <= 1) return cardW + gap;
                var packed = n * cardW + (n - 1) * gap;
                return packed <= run ? cardW + gap : (run - cardW) / (n - 1);
            }
        }

        static void BuildConstructionBoard(RectTransform phase, State st,
            float x0, float y0, float x1, float y1)
        {
            var board = new GameObject("ConstructionBoard", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup))
                .GetComponent<RectTransform>();
            board.SetParent(phase, false);
            FloatingPanel.Place(board, x0, y0, x1, y1);
            var bg = board.GetComponent<Image>();
            HubChrome.PaintWell(bg);
            bg.raycastTarget = false;
            var vlg = board.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(
                (int)DeckBoardLayout.BoardPadX, (int)DeckBoardLayout.BoardPadX,
                (int)DeckBoardLayout.BoardPadY, (int)DeckBoardLayout.BoardPadY);
            vlg.spacing = DeckBoardLayout.BoardSpacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            // Every tray height comes from DeckBoardFit. Force-expand would
            // hand Extra/Side the same growth as MAIN.
            vlg.childForceExpandHeight = false;
            st.BoardHost = board;

            var fit = board.gameObject.AddComponent<DeckBoardFit>();
            st.MainTray = MakePileTray(board, st, Section.Main, fit);
            // P4: EXTRA and SIDE side by side on one row (the page's .xrow)
            var xrow = new GameObject("XRow", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(LayoutElement)).GetComponent<RectTransform>();
            xrow.SetParent(board, false);
            var hlg = xrow.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = DeckBoardLayout.XRowSpacing;
            hlg.childAlignment = TextAnchor.UpperLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            var xle = xrow.GetComponent<LayoutElement>();
            xle.flexibleHeight = 0f;
            xle.flexibleWidth = 1f;
            st.ExtraTray = MakePileTray(xrow, st, Section.Extra, fit);
            st.SideTray = MakePileTray(xrow, st, Section.Side, fit);
            fit.BindRow(xle);
            fit.Bind(board);
        }

        static RectTransform MakePileTray(RectTransform board, State st, Section section,
            DeckBoardFit boardFit)
        {
            var name = section.ToString();
            var main = section == Section.Main;
            var tray = new GameObject(name + "Tray", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement))
                .GetComponent<RectTransform>();
            tray.SetParent(board, false);
            var img = tray.GetComponent<Image>();
            // Always 9-sliced. Simple stretched the whole 861px tile across a
            // short wide tray, which flattened the authored corner brackets
            // into 1px bars on Extra/Side. DeckBoardFit shrinks the slice
            // border to match the tray so the corners cannot collapse either.
            HubChrome.PaintPlate(img, section == Section.Extra ? DuelystUi.Gold : DuelystUi.Cyan,
                gold: section == Section.Extra, sliced: true);
            img.raycastTarget = true;
            var le = tray.GetComponent<LayoutElement>();
            le.flexibleHeight = 0f;
            le.flexibleWidth = main ? 1f : section == Section.Extra
                ? DeckBoardLayout.XRowSplit
                : 1f - DeckBoardLayout.XRowSplit;
            if (!main)
            {
                le.minWidth = 0f;
                le.preferredWidth = 0f;
            }

            HorizontalOrVerticalLayoutGroup inner;
            if (main)
            {
                inner = tray.gameObject.AddComponent<VerticalLayoutGroup>();
                inner.padding = new RectOffset(
                    (int)DeckBoardLayout.TrayPadX, (int)DeckBoardLayout.TrayPadX,
                    (int)DeckBoardLayout.TrayPadTop, (int)DeckBoardLayout.TrayPadBottom);
                inner.spacing = DeckBoardLayout.TraySpacing;
                inner.childAlignment = TextAnchor.UpperLeft;
                inner.childControlWidth = true;
                inner.childControlHeight = true;
                inner.childForceExpandWidth = true;
                // Only the well is flexible. Force-expand would grow the header and
                // leave a gap between a pile's title and its card faces.
                inner.childForceExpandHeight = false;
            }
            else
            {
                // the shared row: the label inline, left of the well
                inner = tray.gameObject.AddComponent<HorizontalLayoutGroup>();
                inner.padding = new RectOffset(
                    (int)DeckBoardLayout.TrayPadX, (int)DeckBoardLayout.TrayPadX,
                    (int)DeckBoardLayout.TrayPadTop, (int)DeckBoardLayout.TrayPadBottom);
                inner.spacing = 0f;
                inner.childAlignment = TextAnchor.UpperLeft;
                inner.childControlWidth = true;
                inner.childControlHeight = true;
                inner.childForceExpandWidth = false;
                inner.childForceExpandHeight = true;
            }

            var btn = tray.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                if (IsAr(st)) return;
                st.Section = section;
                FreeUiKit.PlaySelect();
                HighlightPiles(st);
            });

            var headBar = new GameObject("Head", typeof(RectTransform), typeof(LayoutElement));
            headBar.transform.SetParent(tray, false);
            var headLe = headBar.GetComponent<LayoutElement>();
            if (main)
            {
                headLe.minHeight = HeadBarH;
                headLe.preferredHeight = HeadBarH;
                headLe.flexibleHeight = 0f;
            }
            else
            {
                var headW = section == Section.Extra ? DeckBoardLayout.XHeadExtraW : DeckBoardLayout.XHeadSideW;
                headLe.minWidth = headW;
                headLe.preferredWidth = headW;
                headLe.flexibleWidth = 0f;
            }

            var head = Label(headBar.transform, PileTitle(section, 0), 13, DuelystUi.TextCream,
                TextAnchor.MiddleLeft);
            FloatingPanel.Place(head.rectTransform, 0.00f, 0.00f, main ? 0.62f : 1f, 1f);
            head.horizontalOverflow = HorizontalWrapMode.Overflow;
            head.verticalOverflow = VerticalWrapMode.Overflow;
            head.gameObject.name = name + "Count";

            if (main)
            {
                var meter = new GameObject("Meter", typeof(RectTransform), typeof(Image));
                meter.transform.SetParent(headBar.transform, false);
                FloatingPanel.Place(meter.GetComponent<RectTransform>(), 0.64f, 0.28f, 0.98f, 0.72f);
                var mBg = meter.GetComponent<Image>();
                mBg.sprite = UiFoundation.WhiteSprite();
                mBg.color = new Color(0.04f, 0.06f, 0.10f, 0.65f);
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(meter.transform, false);
                var fRt = fill.GetComponent<RectTransform>();
                fRt.anchorMin = Vector2.zero;
                fRt.anchorMax = new Vector2(0.4f, 1f);
                fRt.offsetMin = new Vector2(1f, 1f);
                fRt.offsetMax = new Vector2(-1f, -1f);
                var fImg = fill.GetComponent<Image>();
                fImg.sprite = UiFoundation.WhiteSprite();
                fImg.color = DuelystUi.Cyan;
                st.MainMeter = fImg;
            }

            var well = new GameObject(name + "Well", typeof(RectTransform), typeof(RectMask2D),
                typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            well.transform.SetParent(tray, false);
            var wellLe = well.GetComponent<LayoutElement>();
            wellLe.flexibleHeight = 1f;
            wellLe.flexibleWidth = 1f;
            var wImg = well.GetComponent<Image>();
            wImg.sprite = UiFoundation.WhiteSprite();
            wImg.color = new Color(1f, 1f, 1f, 0.01f);
            wImg.raycastTarget = true;

            var content = new GameObject("C", typeof(RectTransform));
            content.transform.SetParent(well.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            var sc = well.GetComponent<ScrollRect>();
            sc.viewport = well.GetComponent<RectTransform>();
            sc.content = crt;
            sc.horizontal = !main;
            sc.vertical = main;
            sc.movementType = ScrollRect.MovementType.Clamped;
            sc.scrollSensitivity = GridScrollSensitivity;
            sc.inertia = true;

            var fit = content.AddComponent<DeckPileFit>();
            fit.SingleRow = !main;
            fit.FaceScale = main ? 1f : DeckBoardLayout.XRowScale;
            fit.Viewport = well.GetComponent<RectTransform>();
            fit.Board = boardFit;

            boardFit.Register(section, le, wellLe, img, fit);

            switch (section)
            {
                case Section.Extra:
                    st.ExtraSlots = crt;
                    st.ExtraScroll = sc;
                    st.ExtraCountLab = head;
                    break;
                case Section.Side:
                    st.SideSlots = crt;
                    st.SideScroll = sc;
                    st.SideCountLab = head;
                    break;
                default:
                    st.MainSlots = crt;
                    st.MainScroll = sc;
                    st.MainCountLab = head;
                    break;
            }

            return tray;
        }

        static string PileTitle(Section section, int have)
        {
            return section switch
            {
                Section.Extra => "EXTRA  " + have + "/" + TcgRules.ExtraDeckMax,
                Section.Side => "SIDE  " + have + "/" + TcgRules.SideDeckMax,
                // Legal Main is a range (40–60); show it so an underbuilt deck reads clearly.
                _ => "MAIN  " + have + "  (" + TcgRules.MainDeckMin + "–" + TcgRules.MainDeckMax + ")"
            };
        }

        static void FillConstructionBoard(State st)
        {
            if (st?.MainSlots == null) return;
            FillPile(st, st.MainSlots, st.Main, TcgRules.MainDeckMin, Section.Main, st.MainScroll);
            FillPile(st, st.ExtraSlots, st.Extra, TcgRules.ExtraDeckMax, Section.Extra, st.ExtraScroll);
            FillPile(st, st.SideSlots, st.Side, TcgRules.SideDeckMax, Section.Side, st.SideScroll);
            WritePileHeaders(st);
            HighlightPiles(st);
            // Slot counts drive the row count, which drives the card size for
            // every pile — resolve the board before the piles lay themselves out.
            // explicit null checks, not ?.: a destroyed board / a missing component is Unity's fake null (P4 unull scan)
            if (st.BoardHost != null && st.BoardHost.TryGetComponent(out DeckBoardFit boardFit)) boardFit.Solve(force: true);
        }

        static void FillPile(State st, Transform host, List<int> ids, int ghostMin,
            Section section, ScrollRect scroll)
        {
            if (host == null) return;
            FloatingPanel.DestroyChildrenNow(host);
            var count = ids?.Count ?? 0;
            var slots = section == Section.Main
                ? Mathf.Max(ghostMin, count)
                : st.Hall != null ? Mathf.Max(count, HallXRowEmpties) : Mathf.Max(count, TcgRules.ExtraDeckMax);

            for (var i = 0; i < slots; i++)
            {
                if (ids != null && i < count)
                {
                    var id = ids[i];
                    var copies = 0;
                    for (var j = 0; j < count; j++)
                        if (ids[j] == id) copies++;
                    var go = ArtChip(st, host, id, Mathf.Max(1, copies), id == st.SelectedId);
                    BindChipInteract(go, st, id, fromPool: false, section, scroll);
                }
                else
                {
                    GhostFace(host);
                }
            }

            if (host.TryGetComponent(out DeckPileFit fit)) fit.Fit(force: true);
        }

        static void GhostFace(Transform host)
        {
            var go = new GameObject("Ghost", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.sprite = YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite();
            img.preserveAspect = true;
            img.color = new Color(1f, 1f, 1f, 0.16f);
            img.raycastTarget = false;
        }

        static void WritePileHeaders(State st)
        {
            var mainN = st.Main?.Count ?? 0;
            var extraN = st.Extra?.Count ?? 0;
            var sideN = st.Side?.Count ?? 0;
            // P4: in the hall the tabs and the Scale carry MAIN's count (its tray title stays, unpainted); the shared row's
            // titles take the page's .xrow .lab colour
            var cream = st.Hall != null ? HallLabelInk : DuelystUi.TextCream;
            var danger = st.Hall != null ? HallDangerInk : DuelystUi.Danger;
            if (st.MainCountLab != null)
            {
                st.MainCountLab.text = PileTitle(Section.Main, mainN);
                var legal = mainN >= TcgRules.MainDeckMin && mainN <= TcgRules.MainDeckMax;
                st.MainCountLab.color = st.Hall != null ? Color.clear : legal ? DuelystUi.TextCream : DuelystUi.Danger;
            }

            if (st.ExtraCountLab != null)
            {
                st.ExtraCountLab.text = PileTitle(Section.Extra, extraN);
                st.ExtraCountLab.color = extraN > TcgRules.ExtraDeckMax
                    ? danger
                    : cream;
            }

            if (st.SideCountLab != null)
            {
                st.SideCountLab.text = PileTitle(Section.Side, sideN);
                st.SideCountLab.color = sideN > TcgRules.SideDeckMax
                    ? danger
                    : cream;
            }

            if (st.Hall != null)
            {
                // P4: the tabs, the shape strip and the Scale of Ma'at carry the counts
                if (st.MainMeter != null) st.MainMeter.transform.parent.gameObject.SetActive(false);
                HallCountsChanged(st);
                return;
            }

            if (st.MainMeter != null)
            {
                var t = Mathf.Clamp01(mainN / (float)TcgRules.MainDeckMax);
                var rt = st.MainMeter.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(Mathf.Max(0.04f, t), 1f);
                rt.offsetMin = new Vector2(1f, 1f);
                rt.offsetMax = new Vector2(-1f, -1f);
                var legal = mainN >= TcgRules.MainDeckMin && mainN <= TcgRules.MainDeckMax;
                st.MainMeter.color = legal ? DuelystUi.Cyan : DuelystUi.Danger;
            }
        }

        static void HighlightPiles(State st)
        {
            if (st.Hall != null)
            {
                // P4: the hall's tabs show the section (the page draws no tray glow)
                HallSectionChanged(st);
                return;
            }

            GlowTray(st.MainTray, st.Section == Section.Main && !IsAr(st));
            GlowTray(st.ExtraTray, st.Section == Section.Extra && !IsAr(st));
            GlowTray(st.SideTray, st.Section == Section.Side && !IsAr(st));
        }

        static void GlowTray(RectTransform tray, bool on)
        {
            if (tray == null) return;
            var ol = UiFoundation.GetOrAdd<Outline>(tray.gameObject);
            ol.effectColor = on
                ? new Color(0.55f, 0.92f, 1f, 0.70f)
                : new Color(0.35f, 0.80f, 0.95f, 0.12f);
            ol.effectDistance = on ? new Vector2(2.2f, -2.2f) : new Vector2(1.2f, -1.2f);
            ol.useGraphicAlpha = false;
        }

        /// <summary>
        /// Solves the board once and hands the same card metrics to Main, Extra
        /// and Side, so the three piles share a card size, a left edge and a
        /// column pitch.
        /// </summary>
        sealed class DeckBoardFit : MonoBehaviour
        {
            /// <summary>tile_hub 9-slice border, per side, in UI units.</summary>
            const float PlateBorder = 80f;

            sealed class Pile
            {
                public LayoutElement TrayLe;
                public LayoutElement WellLe;
                public Image Plate;
                public DeckPileFit Fit;
            }

            readonly Dictionary<Section, Pile> _piles = new();
            RectTransform _board;
            LayoutElement _row;
            bool _solving;
            float _lastW = -1f;
            float _lastH = -1f;
            int _lastSlots = -1;

            public DeckBoardLayout.Plan Plan { get; private set; }
            public bool Ready { get; private set; }

            public void Bind(RectTransform board)
            {
                _board = board;
                Solve(force: true);
            }

            /// <summary>P4: the shared EXTRA / SIDE row (its height = the x-row well + its tray pads).</summary>
            public void BindRow(LayoutElement row) => _row = row;

            public void Register(Section section, LayoutElement trayLe,
                LayoutElement wellLe, Image plate, DeckPileFit fit)
            {
                _piles[section] = new Pile
                {
                    TrayLe = trayLe,
                    WellLe = wellLe,
                    Plate = plate,
                    Fit = fit
                };
            }

            void OnEnable() => Solve(force: true);
            // Dimension changes arrive mid-layout: mark and let the next pass
            // settle it rather than rebuilding inside the rebuild.
            void OnRectTransformDimensionsChange() => Solve(settle: false);
            void LateUpdate() => Solve();

            int MainSlots()
            {
                _piles.TryGetValue(Section.Main, out var main);
                var n = main?.Fit != null ? main.Fit.transform.childCount : 0;
                return Mathf.Max(TcgRules.MainDeckMin, n);
            }

            public void Solve(bool force = false, bool settle = true)
            {
                if (_solving || _board == null || _piles.Count < 3) return;
                var w = _board.rect.width;
                var h = _board.rect.height;
                if (w < 32f || h < 32f) return;
                var slots = MainSlots();
                if (!force && Ready && slots == _lastSlots
                    && Mathf.Abs(w - _lastW) < 0.5f && Mathf.Abs(h - _lastH) < 0.5f)
                    return;

                _solving = true;
                _lastW = w;
                _lastH = h;
                _lastSlots = slots;
                Plan = DeckBoardLayout.Solve(w, h, slots);
                Ready = true;

                Apply(Section.Main, Plan.MainWell);
                Apply(Section.Extra, Plan.RowWell);
                Apply(Section.Side, Plan.RowWell);
                if (_row != null)
                {
                    var rowH = Plan.RowWell + DeckBoardLayout.XTrayChrome;
                    _row.minHeight = rowH;
                    _row.preferredHeight = rowH;
                }
                // Settle the tray/well rects now — the piles read vp.rect, and a
                // deferred rebuild would lay them out against the old heights.
                if (settle)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_board);
                else
                    LayoutRebuilder.MarkLayoutForRebuild(_board);
                _solving = false;

                if (!settle) return;
                foreach (var pile in _piles.Values)
                    if (pile.Fit != null) pile.Fit.Fit(force: true);
            }

            void Apply(Section section, float wellH)
            {
                if (!_piles.TryGetValue(section, out var pile) || pile == null) return;
                var trayH = wellH + (section == Section.Main ? DeckBoardLayout.TrayChrome : DeckBoardLayout.XTrayChrome);
                if (pile.TrayLe != null)
                {
                    pile.TrayLe.minHeight = trayH;
                    pile.TrayLe.preferredHeight = trayH;
                    pile.TrayLe.flexibleHeight = 0f;
                }

                if (pile.WellLe != null)
                {
                    pile.WellLe.minHeight = wellH;
                    pile.WellLe.preferredHeight = wellH;
                }

                if (pile.Plate != null && pile.Plate.sprite != null
                    && pile.Plate.type == Image.Type.Sliced)
                {
                    // Shrink the slice border until both corner rows fit the
                    // tray, otherwise Unity overlaps them into a flat smear.
                    var fit = PlateBorder * 2f / Mathf.Max(8f, trayH * 0.85f);
                    pile.Plate.pixelsPerUnitMultiplier = Mathf.Max(1f, fit);
                }
            }
        }

        /// <summary>
        /// Lays one pile out from the board's solved card metrics. Never picks
        /// its own card size — that is what let Extra/Side drift to a third of
        /// MAIN's scale.
        /// </summary>
        sealed class DeckPileFit : MonoBehaviour
        {
            public bool SingleRow;
            /// <summary>P4: the shared row draws MAIN's face at DeckBoardLayout.XRowScale.</summary>
            public float FaceScale = 1f;
            public RectTransform Viewport;
            public DeckBoardFit Board;

            bool _fitting;
            int _lastN = -1;
            float _lastW = -1f;
            float _lastH = -1f;
            float _lastCard = -1f;

            void OnEnable() => Fit();
            void Start() => Fit();
            void OnRectTransformDimensionsChange() => Fit();
            void LateUpdate() => Fit();

            public void Fit(bool force = false)
            {
                if (_fitting) return;
                var rt = transform as RectTransform;
                if (rt == null) return;
                var vp = Viewport != null ? Viewport : rt.parent as RectTransform;
                if (vp == null || Board == null || !Board.Ready) return;
                var w = vp.rect.width;
                var h = vp.rect.height;
                if (w < 8f || h < 8f) return;
                var plan = Board.Plan;
                var n = rt.childCount;
                if (!force && n == _lastN && Mathf.Abs(w - _lastW) < 0.5f
                    && Mathf.Abs(h - _lastH) < 0.5f && Mathf.Abs(plan.CardW - _lastCard) < 0.5f)
                    return;

                _fitting = true;
                _lastN = n;
                _lastW = w;
                _lastH = h;
                _lastCard = plan.CardW;

                const float pad = DeckBoardLayout.WellPad;
                var cardW = plan.CardW;
                var cardH = plan.CardH;
                if (FaceScale > 0f && FaceScale < 1f)
                {
                    cardW *= FaceScale;
                    cardH *= FaceScale;
                }
                // Keep the scroll offset, clamped — rebuilding after every add
                // must not throw the pile back to the top.
                var scrollY = rt.anchoredPosition.y;

                if (n == 0)
                {
                    // Never shorter than the well: a Clamped ScrollRect pins
                    // undersized content to the bottom of the viewport.
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
                    rt.anchoredPosition = Vector2.zero;
                    _fitting = false;
                    return;
                }

                if (SingleRow)
                {
                    var run = Mathf.Max(cardW, w - pad * 2f);
                    var step = DeckBoardLayout.StepFor(run, n, cardW,
                        FaceScale < 1f ? DeckBoardLayout.XGapX : DeckBoardLayout.GapX);
                    // Centre the row in whatever height the tray ended up with,
                    // so leftover space reads as margin instead of a dead band.
                    var top = Mathf.Max(pad, (h - cardH) * 0.5f);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
                    rt.anchoredPosition = Vector2.zero;
                    for (var i = 0; i < n; i++)
                    {
                        var child = rt.GetChild(i) as RectTransform;
                        if (child == null) continue;
                        child.anchorMin = new Vector2(0f, 1f);
                        child.anchorMax = new Vector2(0f, 1f);
                        child.pivot = new Vector2(0f, 1f);
                        child.sizeDelta = new Vector2(cardW, cardH);
                        child.anchoredPosition = new Vector2(pad + i * step, -top);
                    }
                }
                else
                {
                    var cols = Mathf.Max(1, plan.Cols);
                    var rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)cols));
                    var stepY = cardH + DeckBoardLayout.GapY;
                    var totalH = pad * 2f + rows * cardH + (rows - 1) * DeckBoardLayout.GapY;
                    var contentH = Mathf.Max(h, totalH);
                    // Centre the block when the whole pile fits; pin it to the
                    // top once it scrolls, so no row is sliced at the tray edge.
                    var top = totalH < h ? (h - totalH) * 0.5f + pad : pad;
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentH);
                    rt.anchoredPosition = new Vector2(0f, Mathf.Clamp(scrollY, 0f, contentH - h));
                    for (var i = 0; i < n; i++)
                    {
                        var child = rt.GetChild(i) as RectTransform;
                        if (child == null) continue;
                        var col = i % cols;
                        var row = i / cols;
                        child.anchorMin = new Vector2(0f, 1f);
                        child.anchorMax = new Vector2(0f, 1f);
                        child.pivot = new Vector2(0f, 1f);
                        child.sizeDelta = new Vector2(cardW, cardH);
                        child.anchoredPosition = new Vector2(
                            pad + col * (cardW + DeckBoardLayout.GapX),
                            -top - row * stepY);
                    }
                }

                _fitting = false;
            }
        }
    }
}
