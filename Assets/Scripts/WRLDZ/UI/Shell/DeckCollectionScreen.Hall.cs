using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.UI.MenuV6;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// P4 (BUILD_PLAN §6 P4, D13): the deck builder as the v6.3 offering hall. Only the painting changes: the same State, the
    /// same construction board (now with the shared EXTRA / SIDE row), the same owned-only BuildPool, ChipGrid (it still pins
    /// the panel), TryAdd / TryRem / RouteAdd, the TcgRules legality, PersistDeck autosave, the deck switcher's Caret →
    /// DeckDropdown and RequestLeaveEditor on every way out. The hall adds: the Scale of Ma'at over the Main count, the relic
    /// box (open while editing, SAVE shuts it), the tabs (MAIN / EXTRA / SIDE drive st.Section; TOME / TRADE open their
    /// MenuIds; BINDER hidden: no source), the shape strip from st.Main, the search band ("-word" excludes in rubric, NAME /
    /// TEXT scope, FILTER with its count, SORT), the quick chips, the active chips with CLEAR ALL, the House of Life grid
    /// (niches, own counts, in-deck pips, MAX / LIMIT badges, the home-only veil), the rising inspect sheet (TEXT only; −1,
    /// +1 labelled by RouteAdd, OBTAIN · BAZAAR → MenuId.Bazaar, IN THIS DECK n), the +1 flight, DRAW 5 (UI-only) and the
    /// thumb band. Everything is placed in the hall's stage px (deck_&lt;layout&gt;.json).
    /// </summary>
    public static partial class DeckCollectionScreen
    {
        /// <summary>The shared row's dashed slots when it holds fewer cards (the page: SIDE 0/15 shows three).</summary>
        const int HallXRowEmpties = 3;

        /// <summary>The page's .xrow .lab colour, and the rubric for a pile over its cap.</summary>
        static readonly Color HallLabelInk = new(179 / 255f, 168 / 255f, 142 / 255f, 1f);
        static readonly Color HallDangerInk = new(224 / 255f, 122 / 255f, 92 / 255f, 1f);
        static readonly Color HallRubric = new(224 / 255f, 122 / 255f, 92 / 255f, 1f);

        sealed class HallState
        {
            public DeckHall Hall;
            public DeckHallSpec Spec;
            public Transform ModalHost;
            public MaatScale Scale;
            public RelicBox Box;
            public DeckSheet InspectSheet;
            public DrawFiveSheet DrawFive;
            public Text Inscription;
            public float StatusUntil = -1f;
            public string StatusMsg;
            public bool StatusOk = true;

            /// <summary>The search scope: 1 = NAME (the page's default), 2 = TEXT.</summary>
            public int Scope = 1;

            /// <summary>The ERA chip (ErazFormat.InPool for the deck's era, Original); off until picked in FILTER.</summary>
            public bool Era;

            public int OwnedTotal;
            public int LastDeckIndex = -1;
            public int LastMain = -1;
            public string SavedSig;
            public bool FlightPending;
            public bool Entered;
            public double EnterAt;
            public double RefillAt = double.NaN;
            public double ExclusionAt = double.NaN;
            public string ExclusionWord;

            // the inspect sheet's live parts
            public Image InspArt;
            public RectTransform InspArtRt;
            public Text InspName, InspType, InspOwned, InspDesc, InspPlusLabel, InspInDeck;
            public Button InspPlus, InspMinus, InspObtain;
            public Image InspPlusFace, InspPlusP50, InspPlusP100, InspPlusPress;
            public double PlusAt = double.NaN;

            // the thumb band and the header
            public Image SaveFace, SavePress;
            public double SaveAt = double.NaN;
            public Text BarFilterBadge;
            public GameObject BarFilterBadgeGo;

            // the rebuilt editor (PhaseBody)
            public Text MatchCount, MatchRest, CollectionCount;
            public InputField Field;
            public Text Mirror;
            public Image SearchPlate, SearchFocus;
            public readonly List<(Text t, int section)> Tabs = new();
            public readonly List<Text> TabCounts = new();
            public Image TabOn;
            public readonly List<Rect> TabRects = new();
            public readonly List<Text> ShapeTexts = new();
            public readonly List<Image> CurveBars = new();
            public readonly List<Rect> CurveRects = new();
            public Text SearchFilterBadge;
            public GameObject SearchFilterBadgeGo;
            public RectTransform ChipsRow, ActiveRow, Phase;
        }

        // ------------------------------------------------------------------ the persistent parts (built once per open)

        static void HallBuildPersistent(State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var body = hall.BodyWell;
            var stage = hall.StageRect;

            // the header's status line (the box's line; Status messages show in it for a moment)
            st.Status = hall.DeckLabel(body, "Status", spec.Text("rest", "editing", "span.st"), "editing · the box is open");
            if (st.Status == null)
            {
                st.Status = L(body, "", 11, DuelystUi.TextMuted, TextAnchor.MiddleLeft);
                MenuV6Layout.Place(st.Status.rectTransform, spec.El("autoSt"), stage);
            }

            // Sugoroku's reading of the box (the approved lore line, §7: content, not data)
            var insSpec = spec.TextSpec("rest", null, "span.ins");
            h.Inscription = hall.DeckLabel(body, "Inscription", DeckHallSpec.Slot(insSpec),
                "“The one who solves me shall gain\nthe powers and knowledge of darkness.”");
            hall.DeckLabel(body, "InscriptionBy", spec.Text("rest", "· Sugoroku", "span.ins", "> i"),
                "· Sugoroku’s reading of the box (manga, Duel 1)");

            h.Box = RelicBox.Build(spec, body, stage, FirstCards(st));
            h.Scale = MaatScale.Build(spec, hall, body, stage, TcgRules.MainDeckMin, TcgRules.MainDeckMax, st.Main?.Count ?? 0);
            HallThumbBand(st);
            h.DrawFive = DrawFiveSheet.Build(hall, hall.Over, id => st.Db != null ? st.Db.GetArt(id) : null,
                id => st.Db?.Get(id)?.name ?? ("#" + id));

            h.EnterAt = hall.Now;
            hall.OnTick(t => HallTick(st, t));
            UiFoundation.GetOrAdd<HallProbe>(hall.gameObject).Bound = st;
            // the editor's layer last: its sheets (FILTER, the leave guard, the deck menu) cover the header and the band
            hall.BodyHost.SetAsLastSibling();
        }

        /// <summary>Repaint the chips now (FillCardGrids rebuilt them).</summary>
        static void HallDecorateNow(State st)
        {
            var ph = PhaseOf(st);
            if (st.Hall != null && st.Hall.Entered && Application.isPlaying) st.Hall.RefillAt = st.Hall.Hall.Now;
            if (ph != null && ph.TryGetComponent(out HallDecorator d)) d.Now();
        }

        /// <summary>The cc_sel ring on the collection card the inspect sheet shows.</summary>
        static void HallSelectRing(State st)
        {
            var h = st.Hall;
            if (h == null || st.PoolGrid == null) return;
            var open = h.InspectSheet != null && h.InspectSheet.IsOpen;
            for (var i = 0; i < st.PoolGrid.childCount; i++)
            {
                var c = st.PoolGrid.GetChild(i);
                var ring = c.Find("cc_sel");
                var on = open && c.name == "C" + st.SelectedId;
                if (ring != null) ring.gameObject.SetActive(on);
                else if (on && c.TryGetComponent(out HallDecor _))
                {
                    var deco = PhaseOf(st);
                    if (deco != null && deco.TryGetComponent(out HallDecorator dd)) dd.Ring(c);
                }
            }
        }

        static List<int> FirstCards(State st)
        {
            var list = new List<int>();
            if (st.Main == null) return list;
            for (var i = 0; i < st.Main.Count && list.Count < 3; i++) list.Add(st.Main[i]);
            return list;
        }

        /// <summary>The thumb band (the page's .tbar): FILTER (its count) / SORT / DRAW 5 / ⋯ / SAVE.</summary>
        static void HallThumbBand(State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var body = hall.BodyWell;
            var stage = hall.StageRect;
            var bar = spec.El("tbar");
            var items = new (string own, string icon, Action act)[]
            {
                ("FILTER", "lotus", () => { var ph = PhaseOf(st); if (ph != null) OpenFilterTray(ph, st); }),
                ("SORT", "sort", () => CycleSort(st)),
                ("DRAW 5", "torc", () => HallDrawFive(st)),
                ("⋯", null, () => { var ph = PhaseOf(st); if (ph != null) OpenDeckContextMenu(ph, st, ClampDeck(st)); })
            };
            var n = 0;
            foreach (var (own, icon, act) in items)
            {
                var spec0 = spec.TextSpec("rest", own, "div.tbar", "span.tb");
                var el = spec0 != null ? RJ.R(spec0, "rect") : Rect.zero;
                if (el.width <= 0) continue;
                var b = DeckHall.Hit(body, "Tb_" + own, el, stage, () =>
                {
                    FreeUiKit.PlaySelect();
                    act();
                });
                spec.ChromeImg(b.transform, "Plate", own == "⋯" ? "tbar_more" : "tbar_tb", el, el);
                var slot = DeckHallSpec.Slot(spec0);
                var lines = RJ.L(spec0, "lines");
                if (icon != null && lines != null && lines.Count > 0)
                {
                    var ir = DeckHallSpec.RectOf(lines[0]);   // the icon's own box (the line before the label)
                    var ic = RJ.D(spec.D("icons"), icon);
                    var sp = ic != null ? spec.Sprite(ic) : null;
                    if (sp != null) SheetDraw.Img(b.transform, "Icon", sp, ir, el);
                    if (slot != null)
                    {
                        var lr = new Rect(ir.xMax + 10f, el.y, el.xMax - ir.xMax - 10f, el.height);
                        hall.DeckLabel(b.transform, "Label", DeckHallSpec.At(slot, lr), own, el);
                    }
                }
                else if (slot != null)
                {
                    var lab = hall.DeckLabel(b.transform, "Label", DeckHallSpec.At(slot, el), own, el);
                    if (lab != null) lab.alignment = TextAnchor.MiddleCenter;
                }

                if (own == "FILTER")
                {
                    var bs = spec.TextSpec("rest", null, "div.tbar", "span.tb:nth-of-type(1) > em");
                    var br = bs != null ? RJ.R(bs, "rect") : Rect.zero;
                    if (br.width > 0)
                    {
                        var badge = DeckHall.Node(b.transform, "Badge", br, el);
                        spec.ChromeImg(badge, "Plate", "tbar_badge", br, br);
                        h.BarFilterBadge = hall.DeckLabel(badge, "N", DeckHallSpec.At(DeckHallSpec.Slot(bs), br), "", br);
                        if (h.BarFilterBadge != null) h.BarFilterBadge.alignment = TextAnchor.MiddleCenter;
                        h.BarFilterBadgeGo = badge.gameObject;
                    }
                }

                n++;
            }

            // SAVE: the gold plate, pressed (deck.js:208), then the box shuts
            var saveSpec = spec.TextSpec("rest", "SAVE", "span.save");
            var se = saveSpec != null ? RJ.R(saveSpec, "rect") : spec.El("save");
            if (se.width > 0)
            {
                var b = DeckHall.Hit(body, "Save", se, stage, () => HallSave(st));
                h.SaveFace = spec.ChromeImg(b.transform, "Plate", "save", se, se);
                h.SavePress = spec.ChromeImg(b.transform, "Pressed", "save_press", se, se);
                if (h.SavePress != null) SheetDraw.SetA(h.SavePress, 0);
                var lines = RJ.L(saveSpec, "lines");
                var slot = DeckHallSpec.Slot(saveSpec);
                if (slot != null)
                {
                    var lab = hall.DeckLabel(b.transform, "Label", DeckHallSpec.At(slot, se), "SAVE", se);
                    if (lab != null) lab.alignment = TextAnchor.MiddleCenter;
                }

                _ = lines;
            }

            _ = bar;
            _ = n;
        }

        static RectTransform PhaseOf(State st) => st?.Body != null ? st.Body.Find("PhaseBody") as RectTransform : null;

        static void CycleSort(State st)
        {
            st.Sort = st.Sort switch
            {
                SortMode.Name => SortMode.Level,
                SortMode.Level => SortMode.Atk,
                SortMode.Atk => SortMode.Def,
                SortMode.Def => SortMode.Type,
                _ => SortMode.Name
            };
            st.Rebuild();
            HallStatus(st, "Sort · " + SortLabel(st.Sort), true);
        }

        static void HallSave(State st)
        {
            var h = st.Hall;
            h.SaveAt = h.Hall.Now;
            if (!DeckIsComplete(st))
            {
                FreeUiKit.PlayClick();
                Status(st, IncompleteDeckLabel(st), false);
                return;
            }

            PersistDeck(st);
            FreeUiKit.PlayConfirm();
            h.SavedSig = DeckSig(st);
            if (h.Box != null) h.Box.Shut(h.Hall.Now);
            h.StatusMsg = null;
            h.StatusUntil = -1f;
        }

        static void HallDrawFive(State st)
        {
            var h = st.Hall;
            if (h.DrawFive == null) return;
            if (st.Main == null || st.Main.Count == 0)
            {
                Status(st, "Main is empty — nothing to draw.", false);
                return;
            }

            HideInspect(st);
            h.DrawFive.Open(st.Main);
        }

        static string DeckSig(State st)
        {
            var sb = new StringBuilder();
            foreach (var l in new[] { st.Main, st.Extra, st.Side })
            {
                sb.Append('|');
                if (l == null) continue;
                foreach (var id in l) sb.Append(id).Append(',');
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the editor (PhaseBody, rebuilt)

        static void BuildHallEditor(RectTransform phase, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            h.Tabs.Clear();
            h.TabCounts.Clear();
            h.TabRects.Clear();
            h.ShapeTexts.Clear();
            h.CurveBars.Clear();
            h.CurveRects.Clear();

            HallCartouche(phase, st);
            HallTabs(phase, st);
            HallShapeStrip(phase, st);

            // the altar: the construction board at the baked wells (its solver lands on 10 × 4, DeckBoardLayout.HallBoard)
            var slots = spec.Slots;
            var slot0 = slots.Count > 0 ? slots[0] : new Rect(72, 318, 78, 114);
            var slotW = spec.Land ? DeckBoardLayout.HallSlotLand : DeckBoardLayout.HallSlotPort;
            if (Mathf.Abs(slot0.width - slotW) > .5f)
                Debug.LogWarning("[WRLDZ MenuV6] deck hall slot " + slot0.width + " px, the board expects " + slotW);
            DeckBoardLayout.HallBoard(slot0.x, slot0.y, slotW, out var bx, out var by, out var bw, out var bh);
            BuildConstructionBoard(phase, st, 0f, 0f, 1f, 1f);
            var board = phase.Find("ConstructionBoard") as RectTransform;
            if (board != null)
            {
                MenuV6Layout.Place(board, new Rect(bx, by, bw, bh), stage);
                var bimg = board.GetComponent<Image>();
                if (bimg != null) bimg.color = Color.clear;   // the slab and its wells are baked in the room
                foreach (var tray in new[] { st.MainTray, st.ExtraTray, st.SideTray })
                {
                    if (tray == null) continue;
                    var ti = tray.GetComponent<Image>();
                    if (ti != null) ti.color = Color.clear;
                }

                var labSpec = spec.Text("rest", "EXTRA", "div.xrow", "span.lab");
                if (labSpec != null)
                {
                    foreach (var lab in new[] { st.ExtraCountLab, st.SideCountLab })
                        if (lab != null)
                        {
                            TextFx.Apply(lab, labSpec, lab.text);
                            lab.alignment = TextAnchor.MiddleLeft;
                        }
                }
            }

            HallSearchBand(phase, st);
            h.ChipsRow = DeckHall.Node(phase, "QuickChips", stage, stage);
            h.ActiveRow = DeckHall.Node(phase, "ActiveChips", stage, stage);
            h.Phase = phase;
            HallQuickChips(h.ChipsRow, st);
            HallActiveChips(h.ActiveRow, st);

            // COLLECTION · n cards owned (the real owned total, §7)
            var holSpec = spec.TextSpec("rest", "COLLECTION", "div.hol");
            if (holSpec != null)
            {
                var lines = RJ.L(holSpec, "lines");
                var r = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : RJ.R(holSpec, "rect");
                hall.DeckLabel(phase, "CollectionHead", DeckHallSpec.At(DeckHallSpec.Slot(holSpec), new Rect(r.x, r.y, r.width + 40, r.height)), "COLLECTION");
            }
            else
            {
                var lab = Label(phase, "COLLECTION", 12, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
                MenuV6Layout.Place(lab.rectTransform, spec.El("hol"), stage);
            }

            h.CollectionCount = hall.DeckLabel(phase, "CollectionCount", spec.Text("rest", "·", "div.hol", "span.s"), "");

            // the House of Life grid: ChipGrid (it pins the panel) over the grid cells, scrolling under the inspect sheet
            var grid = spec.Grid;
            var g0 = grid.Count > 0 ? grid[0] : new Rect(944, 476, 132, 192);
            var pad = 16f;
            var gridTop = g0.y - pad;
            var gridBottom = spec.D("geo") != null ? RJ.At(RJ.L(spec.Geo, "bar"), 1, spec.Stage.y - 106) : spec.Stage.y - 106;
            var gridRect = new Rect(g0.x - pad, gridTop, spec.Stage.x - (g0.x - pad) - (spec.Land ? 40f : 28f),
                (float)gridBottom - 14f - gridTop);
            st.PoolGrid = ChipGrid(phase, st, 0f, 0f, 1f, 1f, "PoolGrid", out st.PoolScroll);
            var panel = st.PoolScroll != null ? st.PoolScroll.GetComponent<RectTransform>() : null;
            if (panel != null)
            {
                MenuV6Layout.Place(panel, gridRect, stage);
                var pimg = panel.GetComponent<Image>();
                if (pimg != null) pimg.color = new Color(1, 1, 1, 0.001f);   // the niches are the grid; the panel only takes taps
                var vp = st.PoolScroll.viewport;
                if (vp != null)
                {
                    vp.offsetMin = Vector2.zero;
                    vp.offsetMax = Vector2.zero;
                }
            }

            if (st.PoolGrid != null && st.PoolGrid.TryGetComponent(out DeckPoolFit fit))
            {
                fit.HallCols = spec.GridCols;
                fit.HallCell = new Vector2(spec.GridCw, spec.GridCh);
                fit.HallGap = new Vector2(spec.GridGx, spec.GridGy);
                fit.HallPad = pad;
            }

            // the match line ("n of N") under the search band
            var cnt = spec.TextSpec("rest", null, "div.act", "span.cnt");
            var c1 = spec.Text("rest", null, "div.act", "b.c1");
            var cr = cnt != null ? RJ.R(cnt, "rect") : Rect.zero;
            h.MatchCount = hall.DeckLabel(phase, "MatchCount", c1, "");
            h.MatchRest = hall.DeckLabel(phase, "MatchRest", DeckHallSpec.Slot(cnt), "");
            if (h.MatchCount != null && cr.width > 0)
            {
                // right-aligned as one line: the count, then " of N"
                MenuV6Layout.Place(h.MatchCount.rectTransform, new Rect(cr.x - 160, cr.y, 220, cr.height), stage);
                h.MatchCount.alignment = TextAnchor.MiddleRight;
            }

            if (h.MatchRest != null && cr.width > 0)
                MenuV6Layout.Place(h.MatchRest.rectTransform, new Rect(cr.x + 64, cr.y, cr.width - 64, cr.height), stage);
            st.MatchLine = h.MatchRest;

            var deco = UiFoundation.GetOrAdd<HallDecorator>(phase.gameObject);
            deco.St = st;

            st.RefreshCards = () => FillCardGrids(st, resetScroll: false);
            FillCardGrids(st);
        }

        /// <summary>The cartouche (deck name ▾) = the switcher: tap the name to rename, the Caret opens DeckDropdown.</summary>
        static void HallCartouche(RectTransform phase, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            st.Inv?.EnsureDeckBoxSlots();
            var idx = ClampDeck(st);
            var cart = spec.El("cart");
            var host = DeckHall.Node(phase, "DeckSwitcher", cart, stage);
            spec.ChromeImg(host, "Plate", "cart", cart, cart);
            var nameSpec = spec.TextSpec("rest", null, "span.cart");
            var lines = RJ.L(nameSpec, "lines");
            var nr = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : cart;
            var caretEl = spec.El("caret");
            var nameHit = DeckHall.Hit(host, "NameHit", new Rect(cart.x + 12, cart.y, Mathf.Max(20, caretEl.x - cart.x - 16), cart.height), cart, null);
            var nameT = hall.DeckLabel(nameHit.transform, "Name", DeckHallSpec.At(DeckHallSpec.Slot(nameSpec),
                new Rect(nr.x, cart.y, Mathf.Max(20, caretEl.x - nr.x - 8), cart.height)), DeckName(st),
                new Rect(cart.x + 12, cart.y, Mathf.Max(20, caretEl.x - cart.x - 16), cart.height));
            if (nameT != null)
            {
                nameT.horizontalOverflow = HorizontalWrapMode.Overflow;
                TextFx.FitWidth(nameT, Mathf.Max(20, caretEl.x - nr.x - 8), 16);
            }

            BindHold(nameHit.gameObject, () => OpenRenameSheet(phase, st, idx), () => OpenDeckContextMenu(phase, st, idx));
            var caretR = new Rect(caretEl.x - 14, cart.y, caretEl.width + 28, cart.height);
            var caretHit = DeckHall.Hit(host, "Caret", caretR, cart, null);
            var cc = spec.Chrome("cart_caret");
            if (cc != null) SheetDraw.Img(caretHit.transform, "Glyph", spec.Sprite(cc), RJ.R(cc, "shotRect"), caretR);
            BindHold(caretHit.gameObject, () =>
            {
                CloseDeckMenu(st);
                CloseRenameSheet(st);
                if (st.DeckDropOpen) CloseDeckDropdown(st);
                else
                {
                    OpenDeckDropdownUp(phase, st, host);
                    HallPlaceDropdown(st, phase, cart);
                }
            }, () => OpenDeckContextMenu(phase, st, idx));
        }

        /// <summary>The on-hand deck list drops under the cartouche (its own rows and taps unchanged).</summary>
        static void HallPlaceDropdown(State st, RectTransform phase, Rect cart)
        {
            if (st.DeckDropHost == null) return;
            var rt = st.DeckDropHost.GetComponent<RectTransform>();
            var rows = Mathf.Max(1, st.DeckDropHost.transform.childCount);
            var rowH = 58f;
            MenuV6Layout.Place(rt, new Rect(cart.x, cart.yMax + 6, cart.width, rows * rowH + (rows - 1) * 6), st.Hall.Hall.StageRect);
        }

        /// <summary>A legacy sheet (the deck context menu) set next to the hall's own controls.</summary>
        static void HallPlaceSheet(State st, RectTransform sheet)
        {
            if (sheet == null) return;
            var spec = st.Hall.Spec;
            var bar = spec.El("tbar");
            var w = spec.Land ? 420f : 560f;
            var hgt = spec.Land ? 330f : 440f;
            MenuV6Layout.Place(sheet, new Rect(bar.x, bar.y - hgt - 16, w, hgt), st.Hall.Hall.StageRect);
        }

        static void HallTabs(RectTransform phase, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            var items = spec.ElList("tabItems");
            if (items == null) return;
            var onSpec = spec.TextSpec("rest", null, "div.tabs", "span.on");
            var offSpec = spec.TextSpec("rest", "EXTRA", "div.tabs");
            var numSpec = spec.TextSpec("rest", null, "div.tabs", "b.mn");
            var tabOn = spec.Chrome("tab_on");
            if (tabOn != null)
            {
                h.TabOn = SheetDraw.Img(phase, "TabOn", spec.Sprite(tabOn), RJ.R(tabOn, "shotRect"), stage);
            }

            foreach (var it in items)
            {
                var text = RJ.S(it, "text") ?? "";
                var r = DeckHallSpec.RectOf(RJ.L(it, "r") ?? new List<object>());
                if (r.width <= 0) continue;
                var word = text.Split(' ')[0];
                if (word == "BINDER") continue;   // D13: no binder source
                var section = word == "MAIN" ? 0 : word == "EXTRA" ? 1 : word == "SIDE" ? 2 : -1;
                var b = DeckHall.Hit(phase, "Tab_" + word, r, stage, () =>
                {
                    FreeUiKit.PlaySelect();
                    if (section >= 0)
                    {
                        st.Section = (Section)section;
                        HighlightPiles(st);
                    }
                    else if (word == "TOME") HallRoute(st, MenuId.TomeRaid);
                    else if (word == "TRADE") HallRoute(st, MenuId.Trade);
                });
                var slot = DeckHallSpec.Slot(section == 0 ? onSpec : offSpec);
                if (slot == null) continue;
                var lines = RJ.L(section == 0 ? onSpec : offSpec, "lines");
                var lr = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : r;
                var wordRect = new Rect(r.x, lr.y, r.width, lr.height);
                if (word == "TOME")
                {
                    // the key glyph before the word (the page's .lock: Forbidden Memories art local, its own drawing tracked)
                    var key = RJ.D(spec.D("icons"), "key");
                    var kr = RJ.R(key, "rect");
                    var ks = key != null ? spec.Sprite(key) : null;
                    if (ks != null && kr.width > 0) SheetDraw.Img(b.transform, "Key", ks, kr, r);
                    if (kr.width > 0) wordRect = new Rect(kr.xMax + 2, lr.y, r.xMax - kr.xMax - 2, lr.height);
                }

                var lab = hall.DeckLabel(b.transform, "Word", DeckHallSpec.At(slot, wordRect), word, r);
                h.Tabs.Add((lab, section));
                h.TabRects.Add(r);
                if (section >= 0 && numSpec != null)
                {
                    var ns = DeckHallSpec.Slot(numSpec);
                    var wordW = lab != null ? lab.preferredWidth + slot.LetterSpacing * word.Length : 60f;
                    var nRect = new Rect(r.x + wordW + 4, RJ.R(numSpec, "rect").y, 40, RJ.R(numSpec, "rect").height);
                    var num = hall.DeckLabel(b.transform, "Count", DeckHallSpec.At(ns, nRect), "", r);
                    if (num != null) num.alignment = TextAnchor.MiddleLeft;
                    h.TabCounts.Add(num);
                }
                else h.TabCounts.Add(null);
            }

            HallTabState(st);
        }

        static void HallTabState(State st)
        {
            var h = st.Hall;
            if (h == null) return;
            var on = h.Spec.TextSpec("rest", null, "div.tabs", "span.on");
            var off = h.Spec.TextSpec("rest", "EXTRA", "div.tabs");
            var onC = DeckHallSpec.ColorOf(RJ.S(on, "color"), WrldzAnimePalette.Gyp);
            var offC = DeckHallSpec.ColorOf(RJ.S(off, "color"), WrldzAnimePalette.Muted);
            var counts = new[] { st.Main?.Count ?? 0, st.Extra?.Count ?? 0, st.Side?.Count ?? 0 };
            for (var i = 0; i < h.Tabs.Count; i++)
            {
                var (t, section) = h.Tabs[i];
                var active = section >= 0 && (int)st.Section == section;
                if (t != null) t.color = active ? onC : offC;
                var num = i < h.TabCounts.Count ? h.TabCounts[i] : null;
                if (num != null && section >= 0) num.text = counts[section].ToString();
                if (active && h.TabOn != null && i < h.TabRects.Count)
                {
                    var tab = h.Spec.Chrome("tab_on");
                    var el = RJ.R(tab, "elRect");
                    var shot = RJ.R(tab, "shotRect");
                    var r = h.TabRects[i];
                    MenuV6Layout.Place(h.TabOn.rectTransform,
                        new Rect(r.x + (shot.x - el.x), shot.y, r.width + (shot.width - el.width), shot.height), h.Hall.StageRect);
                }
            }
        }

        static void HallSectionChanged(State st) => HallTabState(st);

        /// <summary>MONSTER n · SPELL n · TRAP n and the level curve, from st.Main + CardDef.</summary>
        static void HallShapeStrip(RectTransform phase, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            var items = spec.ElList("shapeItems");
            var slot = spec.Text("rest", "MONSTER", "div.shape");
            var keys = new[] { "shape_mo", "shape_sp", "shape_tr" };
            for (var i = 0; i < 3 && items != null && i < items.Count; i++)
            {
                var r = DeckHallSpec.RectOf(RJ.L(items[i], "r"));
                var sq = spec.ChromeEl(keys[i]);
                if (sq.width > 0)
                {
                    var at = new Rect(r.x, sq.y, sq.width, sq.height);
                    spec.ChromeImg(phase, "Shape" + i, keys[i], at, stage);
                }

                var tr = new Rect(r.x + (sq.width > 0 ? sq.width + 8 : 0), r.y, r.width + 40, r.height);
                h.ShapeTexts.Add(hall.DeckLabel(phase, "ShapeText" + i, DeckHallSpec.At(slot, tr), ""));
            }

            var bars = spec.ElList("curveBars");
            var barSp = spec.ChromeSprite("curve_bar");
            if (bars != null && barSp != null)
                foreach (var b in bars)
                {
                    var r = DeckHallSpec.RectOf(b);
                    var img = SheetDraw.Img(phase, "CurveBar", barSp, r, stage, sliced: true);
                    if (img == null) continue;
                    h.CurveBars.Add(img);
                    h.CurveRects.Add(r);
                }
        }

        static void HallShapeCounts(State st)
        {
            var h = st.Hall;
            if (h == null) return;
            int mo = 0, sp = 0, tr = 0;
            var bands = new int[6];
            if (st.Main != null && st.Db != null)
                foreach (var id in st.Main)
                {
                    var d = st.Db.Get(id);
                    if (d == null) continue;
                    if (d.IsMonster)
                    {
                        mo++;
                        bands[DeckHallMath.CurveBand(Mathf.Max(1, d.level))]++;
                    }
                    else if (d.IsSpell) sp++;
                    else if (d.IsTrap) tr++;
                }

            var words = new[] { "MONSTER " + mo, "SPELL " + sp, "TRAP " + tr };
            for (var i = 0; i < h.ShapeTexts.Count && i < 3; i++)
                if (h.ShapeTexts[i] != null)
                    h.ShapeTexts[i].text = words[i];
            for (var i = 0; i < h.CurveBars.Count && i < 6; i++)
            {
                // bottom-aligned in the curve's box (deck.html .shape .curve)
                var r = h.CurveRects[i];
                var bottom = r.yMax;
                var hh = (float)DeckHallMath.CurveBar(bands[i]);
                var shotPad = h.Spec.ShotFor("curve_bar", new Rect(0, 0, 9, hh));
                var el = new Rect(r.x, bottom - hh, r.width, hh);
                var shot = new Rect(el.x + shotPad.x, el.y + shotPad.y, shotPad.width, shotPad.height);
                MenuV6Layout.Place(h.CurveBars[i].rectTransform, shot, h.Hall.StageRect);
            }
        }

        // ------------------------------------------------------------------ the search band

        static void HallSearchBand(RectTransform phase, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            var srch = spec.El("srch");
            h.SearchPlate = spec.ChromeImg(phase, "SearchPlate", "srch", srch, stage);
            h.SearchFocus = spec.ChromeImg(phase, "SearchFocus", "srch_focus", srch, stage);
            if (h.SearchFocus != null) SheetDraw.SetA(h.SearchFocus, 0);
            var ibis = RJ.D(spec.D("icons"), "ibis");
            if (ibis != null) SheetDraw.Img(phase, "Ibis", spec.Sprite(ibis), RJ.R(ibis, "rect"), stage);

            var q = spec.El("q");
            var qph = spec.TextSpec("rest", null, "span.qph");
            var qt = spec.TextSpec("inspect", null, "span.qt");
            var qx = spec.TextSpec("inspect", null, "span.qx");
            var fieldR = new Rect(q.x, (float)RJ.R(qph, "rect").y, q.width, (float)Math.Max(30, RJ.R(qph, "rect").height));
            if (fieldR.height <= 0) fieldR = q;
            var go = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(phase, false);
            var gImg = go.GetComponent<Image>();
            gImg.sprite = UiFoundation.WhiteSprite();
            gImg.color = new Color(1, 1, 1, 0);
            gImg.raycastTarget = true;
            MenuV6Layout.Place(gImg.rectTransform, fieldR, stage);
            var textSlot = DeckHallSpec.Slot(qt) ?? DeckHallSpec.Slot(qph);
            var txt = hall.DeckLabel(go.transform, "Text", DeckHallSpec.At(textSlot, fieldR), st.Search ?? "", fieldR);
            var ph = hall.DeckLabel(go.transform, "Placeholder", DeckHallSpec.Slot(qph) != null ? DeckHallSpec.At(DeckHallSpec.Slot(qph), fieldR) : null,
                "Search cards…", fieldR);
            // the shown query: rich text over the field's own (invisible) glyphs, so the caret still lands on them
            h.Mirror = hall.DeckLabel(go.transform, "Mirror", DeckHallSpec.At(textSlot, fieldR), "", fieldR);
            if (txt == null)
            {
                txt = L(go.transform, st.Search ?? "", 14, Color.white, TextAnchor.MiddleLeft);
                FloatingPanel.Stretch(txt.rectTransform);
            }

            txt.raycastTarget = true;
            txt.supportRichText = false;
            txt.color = new Color(txt.color.r, txt.color.g, txt.color.b, 0f);
            if (h.Mirror != null)
            {
                h.Mirror.supportRichText = true;
                h.Mirror.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            var field = go.GetComponent<InputField>();
            field.textComponent = txt;
            if (ph != null) field.placeholder = ph;
            field.caretColor = new Color(237 / 255f, 228 / 255f, 158 / 255f, 1f);
            field.customCaretColor = true;
            field.caretWidth = 2;
            field.text = st.Search ?? "";
            h.Field = field;
            var qxColor = DeckHallSpec.ColorOf(RJ.S(qx, "color"), HallRubric);

            void Mirror(string v)
            {
                if (h.Mirror != null) h.Mirror.text = QueryRich(v, qxColor);
            }

            void Apply(string v)
            {
                var next = v ?? "";
                if (next == (st.Search ?? "")) return;
                var hadEx = ExclusionWord(st.Search);
                st.Search = next;
                var ex = ExclusionWord(next);
                if (ex != null && ex != hadEx)
                {
                    h.ExclusionAt = hall.Now;
                    h.ExclusionWord = ex;
                }

                if (st.PoolGrid != null) FillCardGrids(st, resetScroll: true);
                HallRefreshChips(st);
            }

            Mirror(field.text);
            field.onValueChanged.AddListener(Mirror);
            field.onEndEdit.AddListener(Apply);
            var live = go.AddComponent<SearchLive>();
            live.Bind(field, Apply);

            // NAME | TEXT scope
            var scope = spec.El("scope");
            var scopeShot = spec.Chrome("scope");
            if (scopeShot != null && scope.width > 0)
            {
                spec.ChromeImg(phase, "ScopePlate", "scope", scope, stage);
                var nameSpec = spec.TextSpec("rest", "NAME", "span.scope");
                var textSpec = spec.TextSpec("rest", "TEXT", "span.scope");
                var nr = RJ.R(nameSpec, "rect");
                var tr = RJ.R(textSpec, "rect");
                var onImg = spec.ChromeImg(phase, "ScopeOn", "scope_on", h.Scope == 2 ? tr : nr, stage);
                var nameT = hall.DeckLabel(phase, "ScopeName", DeckHallSpec.Slot(nameSpec), "NAME");
                var textT = hall.DeckLabel(phase, "ScopeText", DeckHallSpec.Slot(textSpec), "TEXT");
                var onC = DeckHallSpec.ColorOf(RJ.S(nameSpec, "color"), Color.black);
                var offC = DeckHallSpec.ColorOf(RJ.S(textSpec, "color"), WrldzAnimePalette.Muted);
                foreach (var t in new[] { nameT, textT })
                    if (t != null)
                    {
                        t.alignment = TextAnchor.MiddleCenter;
                    }

                if (nameT != null) nameT.color = h.Scope == 2 ? offC : onC;
                if (textT != null) textT.color = h.Scope == 2 ? onC : offC;
                _ = onImg;
                DeckHall.Hit(phase, "Scope_NAME", nr, stage, () =>
                {
                    FreeUiKit.PlaySelect();
                    h.Scope = 1;
                    st.Rebuild();
                });
                DeckHall.Hit(phase, "Scope_TEXT", tr, stage, () =>
                {
                    FreeUiKit.PlaySelect();
                    h.Scope = 2;
                    st.Rebuild();
                });
            }

            // FILTER (its count) and SORT ride the band in landscape; the portrait page keeps them in the thumb band only
            if (spec.Land)
            {
                var fSpec = spec.TextSpec("rest", "FILTER", "div.srch", "span.fbtn");
                var sSpec = spec.TextSpec("rest", "SORT", "div.srch", "span.fbtn");
                foreach (var (sp, own) in new[] { (fSpec, "FILTER"), (sSpec, "SORT") })
                {
                    if (sp == null) continue;
                    var r = RJ.R(sp, "rect");
                    var b = DeckHall.Hit(phase, "Band_" + own, r, stage, () =>
                    {
                        FreeUiKit.PlaySelect();
                        if (own == "FILTER") OpenFilterTray(phase, st);
                        else CycleSort(st);
                    });
                    spec.ChromeImg(b.transform, "Plate", "fbtn", r, r);
                    var lab = hall.DeckLabel(b.transform, "Label", DeckHallSpec.At(DeckHallSpec.Slot(sp), r), own == "SORT" ? "SORT" : "FILTER", r);
                    if (lab != null) lab.alignment = TextAnchor.MiddleCenter;
                    if (own == "FILTER")
                    {
                        var bs = spec.TextSpec("rest", null, "div.srch", "span.fbtn:nth-of-type(4) > em");
                        var br = bs != null ? RJ.R(bs, "rect") : Rect.zero;
                        if (br.width > 0)
                        {
                            var badge = DeckHall.Node(b.transform, "Badge", br, r);
                            spec.ChromeImg(badge, "Plate", "fbtn_badge", br, br);
                            h.SearchFilterBadge = hall.DeckLabel(badge, "N", DeckHallSpec.At(DeckHallSpec.Slot(bs), br), "", br);
                            if (h.SearchFilterBadge != null) h.SearchFilterBadge.alignment = TextAnchor.MiddleCenter;
                            h.SearchFilterBadgeGo = badge.gameObject;
                        }
                    }
                }
            }
        }

        /// <summary>The query as shown: include words in the field colour, each "-word" in rubric with the page's U+2212.</summary>
        static string QueryRich(string q, Color rubric)
        {
            if (string.IsNullOrEmpty(q)) return "";
            var hex = ColorUtility.ToHtmlStringRGB(rubric);
            var sb = new StringBuilder();
            var parts = q.Split(' ');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var w = Esc(parts[i]);
                if (parts[i].Length > 1 && parts[i][0] == '-')
                    sb.Append("<color=#").Append(hex).Append(">−").Append(Esc(parts[i].Substring(1))).Append("</color>");
                else sb.Append(w);
            }

            return sb.ToString();
        }

        static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        static string ExclusionWord(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            foreach (var t in q.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (t.Length > 1 && t[0] == '-')
                    return t.Substring(1);
            return null;
        }

        // ------------------------------------------------------------------ the quick chips and the active chips

        static readonly (string lab, Filter f, string attr, bool lv)[] HallChipMap =
        {
            ("ALL", Filter.All, null, false), ("MONSTER", Filter.Mon, null, false), ("SPELL", Filter.Spell, null, false),
            ("TRAP", Filter.Trap, null, false), ("LIGHT", Filter.All, "LIGHT", false), ("DARK", Filter.All, "DARK", false),
            ("EARTH", Filter.All, "EARTH", false), ("LV 4+", Filter.All, null, true)
        };

        static bool HasLv4(State st) => (" " + (st.Search ?? "") + " ").IndexOf(" lv4+ ", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool ChipOn(State st, int i)
        {
            var (lab, f, attr, lv) = HallChipMap[i];
            if (lab == "ALL") return st.Filter == Filter.All && string.IsNullOrEmpty(st.Attribute) && !HasLv4(st);
            if (lv) return HasLv4(st);
            if (attr != null) return string.Equals(st.Attribute, attr, StringComparison.OrdinalIgnoreCase);
            return st.Filter == f;
        }

        static void HallQuickChips(RectTransform phase, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            var items = spec.ElList("chipItems");
            if (items == null) return;
            var onSpec = spec.TextSpec("rest", "ALL", "div.chips");
            var offSpec = spec.TextSpec("rest", "MONSTER", "div.chips");
            for (var i = 0; i < items.Count && i < HallChipMap.Length; i++)
            {
                var r = DeckHallSpec.RectOf(RJ.L(items[i], "r"));
                if (r.width <= 0) continue;
                var idx = i;
                var on = ChipOn(st, i);
                var b = DeckHall.Hit(phase, "Chip_" + HallChipMap[i].lab, r, stage, () =>
                {
                    FreeUiKit.PlaySelect();
                    HallChipTap(st, idx);
                });
                spec.ChromeImg(b.transform, "Plate", on ? "chip_on" : "chip_off", r, r);
                var lab = hall.DeckLabel(b.transform, "Label", DeckHallSpec.At(DeckHallSpec.Slot(on ? onSpec : offSpec), r),
                    HallChipMap[i].lab, r);
                if (lab != null) lab.alignment = TextAnchor.MiddleCenter;
            }
        }

        static void HallChipTap(State st, int i)
        {
            var (lab, f, attr, lv) = HallChipMap[i];
            if (lab == "ALL")
            {
                st.Filter = Filter.All;
                st.Attribute = "";
                if (HasLv4(st)) st.Search = RemoveToken(st.Search, "lv4+");
            }
            else if (lv)
                st.Search = HasLv4(st) ? RemoveToken(st.Search, "lv4+") : ((st.Search ?? "").Trim() + " lv4+").Trim();
            else if (attr != null)
                st.Attribute = string.Equals(st.Attribute, attr, StringComparison.OrdinalIgnoreCase) ? "" : attr;
            else
                st.Filter = st.Filter == f ? Filter.All : f;

            if (st.Hall.Field != null && st.Hall.Field.text != (st.Search ?? "")) st.Hall.Field.SetTextWithoutNotify(st.Search ?? "");
            if (st.Hall.Mirror != null) st.Hall.Mirror.text = QueryRich(st.Search, HallRubric);
            FillCardGrids(st, resetScroll: true);
            HallRefreshChips(st);
        }

        static string RemoveToken(string q, string token)
        {
            if (string.IsNullOrEmpty(q)) return "";
            var parts = new List<string>();
            foreach (var t in q.Split(' '))
                if (!string.Equals(t, token, StringComparison.OrdinalIgnoreCase) && t.Length > 0)
                    parts.Add(t);
            return string.Join(" ", parts);
        }

        /// <summary>The active filters (the page's .act): ERA, each EXCLUDE word, the FILTER facets; CLEAR ALL clears them.</summary>
        static void HallActiveChips(RectTransform phase, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            var act = spec.El("act");
            var acSpec = spec.TextSpec("rest", "ERA", "div.act", "span.ac");
            var exSpec = spec.TextSpec("rest", "EXCLUDE", "div.act", "span.ac.ex");
            var xSpec = spec.Chrome("ac_x");
            var chips = new List<(string text, bool ex, Action clear)>();
            if (h.Era) chips.Add(("ERA · ORIGINAL", false, () => h.Era = false));
            var exWord = ExclusionWord(st.Search);
            if (exWord != null)
            {
                var w = exWord;
                chips.Add(("EXCLUDE " + w, true, () => st.Search = RemoveToken(st.Search, "-" + w)));
            }

            if (st.Kind != KindFilter.Any) chips.Add((KindLabel(st.Kind), false, () => st.Kind = KindFilter.Any));
            if (st.Level > 0) chips.Add((st.Level >= 9 ? "LV 9+" : "LV " + st.Level, false, () => st.Level = 0));
            if (st.AtkBand != StatBand.Any) chips.Add((StatBandLabel(st.AtkBand, "ATK "), false, () => st.AtkBand = StatBand.Any));
            if (st.DefBand != StatBand.Any) chips.Add((StatBandLabel(st.DefBand, "DEF "), false, () => st.DefBand = StatBand.Any));
            if (!string.IsNullOrEmpty(st.MonsterType))
            {
                var r = st.MonsterType;
                chips.Add((r.ToUpperInvariant(), false, () => st.MonsterType = ""));
            }

            if (!string.IsNullOrEmpty(st.SpellKind)) chips.Add((st.SpellKind.ToUpperInvariant() + " SPELL", false, () => st.SpellKind = ""));
            if (!string.IsNullOrEmpty(st.TrapKind)) chips.Add((st.TrapKind.ToUpperInvariant() + " TRAP", false, () => st.TrapKind = ""));
            if (st.InDeck != InDeckFilter.Any) chips.Add((st.InDeck == InDeckFilter.InDeck ? "IN DECK" : "NOT IN DECK", false, () => st.InDeck = InDeckFilter.Any));
            if (!string.IsNullOrEmpty(st.Archetype))
            {
                var a = st.Archetype;
                chips.Add((a.Length > 14 ? a.Substring(0, 13) + "…" : a, false, () => st.Archetype = ""));
            }

            var acEl = spec.ChromeEl("ac");
            var x = act.x;
            var limit = act.x + act.width * (spec.Land ? .7f : .62f);
            var n = 0;
            foreach (var (text, ex, clear) in chips)
            {
                var slot = DeckHallSpec.Slot(ex && exSpec != null ? exSpec : acSpec);
                if (slot == null) break;
                var probe = TextFx.Label(phase, "Probe", DeckHallSpec.At(slot, new Rect(0, 0, 400, 30)), text, stage);
                var tw = probe != null ? probe.preferredWidth + slot.LetterSpacing * text.Length : text.Length * 10f;
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe.gameObject);
                var w = tw + 28 + 26;
                if (x + w > limit && n > 0) break;
                var r = new Rect(x, acEl.y, w, acEl.height > 0 ? acEl.height : 41);
                var cap = clear;
                var b = DeckHall.Hit(phase, "Active_" + n, r, stage, () =>
                {
                    FreeUiKit.PlayClick();
                    cap();
                    HallQueryChanged(st);
                });
                var plate = spec.ChromeImg(b.transform, "Plate", ex ? "ac_ex" : "ac", r, r);
                if (plate != null && ex && Application.isPlaying && !double.IsNaN(h.ExclusionAt))
                {
                    // the exclusion chip pops in from its left edge (eBack .35 s)
                    var pg = b.gameObject.AddComponent<HallPop>();
                    pg.Hall = hall;
                    pg.At = h.ExclusionAt;
                }

                var lab = hall.DeckLabel(b.transform, "Label", DeckHallSpec.At(slot, new Rect(r.x + 14, r.y, tw + 4, r.height)), text, r);
                if (lab != null) lab.alignment = TextAnchor.MiddleLeft;
                if (xSpec != null)
                {
                    var xs = RJ.R(xSpec, "shotRect");
                    var xe = RJ.R(xSpec, "elRect");
                    var xr = new Rect(r.xMax - 14 - xs.width, r.y + (xs.y - xe.y), xs.width, xs.height);
                    SheetDraw.Img(b.transform, "X", spec.Sprite(xSpec), xr, r);
                }

                x += w + 12;
                n++;
            }

            if (n > 0)
            {
                var clr = spec.Chrome("clr");
                var cs = clr != null ? RJ.R(clr, "shotRect") : Rect.zero;
                var ce = spec.ChromeEl("clr");
                var cr = new Rect(x, ce.y, ce.width > 0 ? ce.width : 92, ce.height > 0 ? ce.height : 26);
                var b = DeckHall.Hit(phase, "ClearAll", cr, stage, () =>
                {
                    FreeUiKit.PlayConfirm();
                    st.Search = "";
                    st.Filter = Filter.All;
                    st.Scope = FilterScope.Both;
                    ClearAdvancedFilters(st);
                    h.Era = false;
                    HallQueryChanged(st);
                });
                if (clr != null) SheetDraw.Img(b.transform, "Glyph", spec.Sprite(clr), new Rect(cr.x + (cs.x - ce.x), cr.y + (cs.y - ce.y), cs.width, cs.height), cr);
            }
        }

        static void HallRefreshChips(State st)
        {
            // a typed query rebuilds the two chip rows only (the field keeps its focus)
            var h = st.Hall;
            if (h == null) return;
            foreach (var row in new[] { h.ChipsRow, h.ActiveRow })
                if (row != null)
                    FloatingPanel.DestroyChildrenNow(row);
            if (h.ChipsRow != null) HallQuickChips(h.ChipsRow, st);
            if (h.ActiveRow != null) HallActiveChips(h.ActiveRow, st);
            HallFilterBadges(st);
        }

        /// <summary>A chip changed the query or a facet: the field shows it, the grids refill, the chip rows rebuild.</summary>
        static void HallQueryChanged(State st)
        {
            var h = st.Hall;
            if (h != null && h.Field != null && h.Field.text != (st.Search ?? "")) h.Field.SetTextWithoutNotify(st.Search ?? "");
            if (h != null && h.Mirror != null) h.Mirror.text = QueryRich(st.Search, HallRubric);
            FillCardGrids(st, resetScroll: true);
            HallRefreshChips(st);
        }

        static int ActiveFilterCount(State st)
        {
            var n = 0;
            if (st.Hall != null && st.Hall.Era) n++;
            if (st.Kind != KindFilter.Any) n++;
            if (st.Level > 0) n++;
            if (st.AtkBand != StatBand.Any) n++;
            if (st.DefBand != StatBand.Any) n++;
            if (!string.IsNullOrEmpty(st.MonsterType)) n++;
            if (!string.IsNullOrEmpty(st.SpellKind)) n++;
            if (!string.IsNullOrEmpty(st.TrapKind)) n++;
            if (st.InDeck != InDeckFilter.Any) n++;
            if (!string.IsNullOrEmpty(st.Archetype)) n++;
            if (ExclusionWord(st.Search) != null) n++;
            return n;
        }

        static void HallFilterBadges(State st)
        {
            var h = st.Hall;
            if (h == null) return;
            var n = ActiveFilterCount(st);
            foreach (var (t, go) in new[] { (h.SearchFilterBadge, h.SearchFilterBadgeGo), (h.BarFilterBadge, h.BarFilterBadgeGo) })
            {
                if (go != null) go.SetActive(n > 0);
                if (t != null) t.text = n.ToString();
            }
        }

        // ------------------------------------------------------------------ the pool query (BuildPool's hook)

        /// <summary>
        /// The query BuildPool matches with TokenMatches, with "-word" tokens taken out: a card whose name / text (or, in the
        /// hall, its scope) contains the word leaves <paramref name="ids"/>. In the hall the NAME / TEXT scope narrows plain
        /// words to the name (+ archetype) or the card text, and the ERA chip keeps ErazFormat.InPool(id, Original) cards.
        /// Structured tokens (lv7, atk&gt;2000, dark, spell …) stay TokenMatches' own. The owned total (before any filter) is
        /// kept for the hall's "n of N".
        /// </summary>
        static string HallQuery(State st, HashSet<int> ids)
        {
            var raw = (st.Search ?? "").Trim();
            if (st.Hall != null) st.Hall.OwnedTotal = ids.Count;
            var keep = new List<string>();
            var excl = new List<string>();
            var plain = new List<string>();
            foreach (var t in raw.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (t.Length > 1 && t[0] == '-') excl.Add(t.Substring(1));
                else
                {
                    keep.Add(t);
                    if (IsPlainWord(t)) plain.Add(t);
                }
            }

            var scope = st.Hall != null ? st.Hall.Scope : 0;
            var era = st.Hall != null && st.Hall.Era;
            if (excl.Count == 0 && (scope == 0 || plain.Count == 0) && !era) return string.Join(" ", keep);
            var drop = new List<int>();
            foreach (var id in ids)
            {
                var def = st.Db?.Get(id);
                if (era && !ErazFormat.InPool(id, ErazFormat.Original))
                {
                    drop.Add(id);
                    continue;
                }

                if (def == null) continue;
                var gone = false;
                foreach (var w in excl)
                    if (ScopeHit(def, w, scope == 0 ? 0 : scope))
                    {
                        gone = true;
                        break;
                    }

                if (!gone && scope != 0)
                    foreach (var w in plain)
                        if (!ScopeHit(def, w, scope))
                        {
                            gone = true;
                            break;
                        }

                if (gone) drop.Add(id);
            }

            foreach (var id in drop) ids.Remove(id);
            return string.Join(" ", keep);
        }

        /// <summary>A word TokenMatches would match by substring (not a level / stat / kind / attribute shortcut).</summary>
        static bool IsPlainWord(string t)
        {
            var low = t.ToLowerInvariant();
            if (low.Length >= 3 && low.StartsWith("lv") && char.IsDigit(low[2])) return false;
            if (low.StartsWith("atk") || low.StartsWith("def")) return low.Length > 3 && char.IsLetter(low[3]);
            if (int.TryParse(low, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;
            switch (low)
            {
                case "monster": case "spell": case "trap": case "extra": case "normal": case "effect": case "fusion":
                case "ritual": case "synchro": case "xyz": case "link": case "tuner": case "flip": case "toon":
                case "spirit": case "union": case "pendulum": case "field": case "equip": case "continuous":
                case "quick-play": case "quickplay": case "qp": case "counter": case "ctr":
                    return false;
            }

            foreach (var a in AttributeOptions)
                if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase))
                    return false;
            foreach (var a in AttributeAbbr)
                if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase))
                    return false;
            foreach (var a in MonsterTypeOptions)
                if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase))
                    return false;
            foreach (var a in MonsterTypeAbbr)
                if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        /// <summary>0 = name, archetype or text; 1 = NAME (name + archetype); 2 = TEXT (the card text).</summary>
        static bool ScopeHit(CardDef def, string w, int scope)
        {
            if (def == null || string.IsNullOrEmpty(w)) return false;
            bool Has(string s) => !string.IsNullOrEmpty(s) && s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
            return scope switch
            {
                1 => Has(def.name) || Has(def.archetype),
                2 => Has(def.desc),
                _ => Has(def.name) || Has(def.archetype) || Has(def.desc)
            };
        }

        static void HallMatchLine(State st)
        {
            var h = st.Hall;
            var shown = st.PoolRows != null ? st.PoolRows.Count : 0;
            if (h.MatchCount != null) h.MatchCount.text = shown.ToString("N0", CultureInfo.InvariantCulture);
            if (h.MatchRest != null)
                h.MatchRest.text = "of " + h.OwnedTotal.ToString("N0", CultureInfo.InvariantCulture) + " · " + SortLabel(st.Sort);
            if (h.CollectionCount != null)
                h.CollectionCount.text = "· " + h.OwnedTotal.ToString("N0", CultureInfo.InvariantCulture) + " cards in your collection";
            HallFilterBadges(st);
        }

        // ------------------------------------------------------------------ counts, the Scale, the box

        static void HallCountsChanged(State st)
        {
            var h = st.Hall;
            if (h == null) return;
            HallTabState(st);
            HallShapeCounts(st);
            var n = st.Main?.Count ?? 0;
            if (h.Scale != null && !h.FlightPending)
            {
                if (h.LastMain < 0 || h.LastDeckIndex != ClampDeck(st)) h.Scale.Snap(n);
                else h.Scale.Set(n, h.Hall.Now);
            }

            h.LastMain = n;
            if (h.Box != null && h.Box.Sealed && h.SavedSig != null && h.SavedSig != DeckSig(st))
            {
                h.Box.Reopen(h.Hall.Now);
                h.SavedSig = null;
            }

            h.Box?.SetCards(FirstCards(st));
        }

        static void HallAfterRebuild(State st)
        {
            var h = st.Hall;
            if (h == null) return;
            var idx = ClampDeck(st);
            if (h.LastDeckIndex >= 0 && h.LastDeckIndex != idx)
            {
                // a deck select: the lid lifts on the new deck
                h.Box?.Reopen(h.Hall.Now);
                h.Scale?.Snap(st.Main?.Count ?? 0);
                h.SavedSig = null;
            }

            h.LastDeckIndex = idx;
            h.Box?.SetCards(FirstCards(st));
            HallFilterBadges(st);
            HallTabState(st);
        }

        static void HallStatus(State st, string msg, bool ok)
        {
            var h = st.Hall;
            h.StatusMsg = msg;
            h.StatusOk = ok;
            h.StatusUntil = Time.unscaledTime + 2.6f;
            HallStatusApply(st);
        }

        static void HallStatusApply(State st)
        {
            var h = st.Hall;
            if (st.Status == null) return;
            var showMsg = !string.IsNullOrEmpty(h.StatusMsg) && (Time.unscaledTime < h.StatusUntil || !Application.isPlaying);
            var line = showMsg ? h.StatusMsg : h.Box != null ? h.Box.StatusLine : "editing · the box is open";
            if (st.Status.text != line) st.Status.text = line;
            var sealedNow = h.Box != null && h.Box.Sealed;
            st.Status.color = showMsg
                ? (h.StatusOk ? new Color(159 / 255f, 224 / 255f, 168 / 255f) : HallRubric)
                : sealedNow ? new Color(237 / 255f, 228 / 255f, 158 / 255f) : new Color(143 / 255f, 134 / 255f, 114 / 255f);
            if (h.Inscription != null)
                h.Inscription.color = sealedNow ? new Color(232 / 255f, 200 / 255f, 114 / 255f) : new Color(201 / 255f, 168 / 255f, 106 / 255f);
        }

        static void HallTick(State st, double t)
        {
            var h = st.Hall;
            if (h == null) return;
            h.Scale?.Tick(t);
            h.Box?.Tick(t);
            HallStatusApply(st);
            if (h.SaveFace != null && h.SavePress != null)
            {
                var pk = double.IsNaN(h.SaveAt) ? 0 : DeckHallMath.SavePress(t - h.SaveAt);
                SheetDraw.SetA(h.SavePress, pk);
                var s = (float)(1 - .06 * pk);
                h.SaveFace.transform.parent.localScale = new Vector3(s, s, 1f);
            }

            if (h.InspPlusFace != null)
            {
                // the +1 plate's glow pulse (.5 + .5 sin 7t: insp_pl / _p50 / _p100 crossfade) and its press
                var pulse = .5 + .5 * Math.Sin(7 * t);
                SheetDraw.SetA(h.InspPlusP50, pulse < .5 ? pulse * 2 : 2 - pulse * 2);
                SheetDraw.SetA(h.InspPlusP100, pulse > .5 ? pulse * 2 - 1 : 0);
                var pressed = !double.IsNaN(h.PlusAt) && t - h.PlusAt < .18;
                SheetDraw.SetA(h.InspPlusPress, pressed ? 1 : 0);
            }

            var f = h.SearchFocus;
            if (f != null && h.Field != null) SheetDraw.SetA(f, h.Field.isFocused ? 1 : 0);
        }

        // ------------------------------------------------------------------ routes from the hall (the guard kept)

        static void HallRoute(State st, MenuId id)
        {
            var mh = st.Hall != null ? st.Hall.ModalHost : null;
            var shell = mh != null ? mh.GetComponentInParent<MenuShell>() : null;
            if (shell == null)
            {
                Status(st, id == MenuId.Bazaar ? "The Bazaar opens from the menu." : "Opens from the menu.", false);
                return;
            }

            if (DeckIsComplete(st))
            {
                PersistDeck(st);
                shell.ShowOverlay(id);
                return;
            }

            OpenLeaveGuard(st, "REVERT & LEAVE", () =>
            {
                ApplyRevert(st);
                shell.ShowOverlay(id);
            });
        }

        // ------------------------------------------------------------------ the inspect sheet

        static void BuildHallInspect(Transform body, State st)
        {
            var h = st.Hall;
            var hall = h.Hall;
            var spec = h.Spec;
            var stage = hall.StageRect;
            var rect = spec.Inspect;
            h.InspectSheet = DeckSheet.Build(hall, body, "InspectPopup", rect);
            if (h.InspectSheet == null) return;
            st.Inspect = h.InspectSheet.Root.gameObject;
            var c = h.InspectSheet.Content;

            var big = spec.El("big");
            spec.ChromeImg(c, "BigFrame", "insp_big", big, stage);
            h.InspArt = SheetDraw.Img(c, "Art", UiFoundation.WhiteSprite(), big, stage);
            if (h.InspArt != null)
            {
                h.InspArt.preserveAspect = true;
                h.InspArtRt = h.InspArt.rectTransform;
            }

            st.InspectArt = h.InspArt;
            var tabSpec = spec.TextSpec("rest", "TEXT", "div.tabs2", "> b");
            hall.DeckLabel(c, "TabText", DeckHallSpec.Slot(tabSpec), "TEXT");
            var under = spec.Chrome("insp_tab_on");
            if (under != null) SheetDraw.Img(c, "TabOn", spec.Sprite(under), RJ.R(under, "shotRect"), stage);
            h.InspName = hall.DeckLabel(c, "Name", spec.Text("rest", null, "div.info", "div.nm"), "");
            st.InspectName = h.InspName;
            var tySpec = spec.TextSpec("rest", null, "div.info", "div.ty");
            h.InspType = hall.DeckLabel(c, "Type", DeckHallSpec.Slot(tySpec), "");
            h.InspOwned = hall.DeckLabel(c, "Owned", spec.Text("rest", "· owned", "div.ty", "span.imd"), "");
            st.InspectStats = h.InspType;

            // the card text in a scroll (long effects)
            var txSpec = spec.TextSpec("rest", null, "div.info", "div.tx");
            var tx = RJ.R(txSpec, "rect");
            var acts = spec.El("acts");
            var txRect = new Rect(tx.x, tx.y, tx.width, Mathf.Max(40, acts.y - tx.y - 6));
            var scroll = DeckHall.Node(c, "DescScroll", txRect, stage);
            scroll.gameObject.AddComponent<RectMask2D>();
            var sImg = scroll.gameObject.AddComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(1, 1, 1, 0);
            var sc = scroll.gameObject.AddComponent<ScrollRect>();
            var content = new GameObject("Desc", typeof(RectTransform), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            content.SetParent(scroll, false);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var txSlot = DeckHallSpec.Slot(txSpec);
            h.InspDesc = txSlot != null ? TextFx.Label(content, "Text", DeckHallSpec.At(txSlot, new Rect(0, 0, tx.width, 40)), "", new Rect(0, 0, tx.width, 40)) : null;
            if (h.InspDesc != null)
            {
                var drt = h.InspDesc.rectTransform;
                drt.anchorMin = new Vector2(0, 1);
                drt.anchorMax = new Vector2(1, 1);
                drt.pivot = new Vector2(.5f, 1);
                drt.offsetMin = drt.offsetMax = Vector2.zero;
                h.InspDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
                h.InspDesc.verticalOverflow = VerticalWrapMode.Overflow;
                h.InspDesc.alignment = TextAnchor.UpperLeft;
                var csf = h.InspDesc.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.gameObject.AddComponent<VerticalLayoutGroup>().childControlHeight = true;
            sc.viewport = scroll;
            sc.content = content;
            sc.horizontal = false;
            sc.vertical = true;
            sc.movementType = ScrollRect.MovementType.Clamped;
            sc.scrollSensitivity = 40f;
            st.InspectDesc = h.InspDesc;

            // − · +1 MAIN · OBTAIN · BAZAAR · IN THIS DECK n
            var mnSpec = spec.TextSpec("rest", "−", "div.acts", "span.mn");
            var plSpec = spec.TextSpec("rest", "+1", "div.acts", "span.pl");
            var plSmall = spec.TextSpec("rest", "MAIN", "span.pl", "> small");
            var obSpec = spec.TextSpec("rest", "OBTAIN", "div.acts", "span.ob");
            var inSpec = spec.TextSpec("rest", "IN THIS DECK", "div.acts", "span.in");
            var idSpec = spec.TextSpec("rest", null, "span.in", "b.id");

            var mr = RJ.R(mnSpec, "rect");
            if (mr.width > 0)
            {
                h.InspMinus = DeckHall.Hit(c, "Minus", mr, stage, () => HallMinus(st));
                spec.ChromeImg(h.InspMinus.transform, "Plate", "insp_mn", mr, mr);
                var lab = hall.DeckLabel(h.InspMinus.transform, "Label", DeckHallSpec.At(DeckHallSpec.Slot(mnSpec), mr), "−", mr);
                if (lab != null) lab.alignment = TextAnchor.MiddleCenter;
                st.InspectRem = h.InspMinus;
            }

            var pr = RJ.R(plSpec, "rect");
            if (pr.width > 0)
            {
                h.InspPlus = DeckHall.Hit(c, "Plus1", pr, stage, () => HallPlus(st));
                h.InspPlusFace = spec.ChromeImg(h.InspPlus.transform, "Plate", "insp_pl", pr, pr);
                h.InspPlusP50 = spec.ChromeImg(h.InspPlus.transform, "Pulse50", "insp_pl_p50", pr, pr);
                h.InspPlusP100 = spec.ChromeImg(h.InspPlus.transform, "Pulse100", "insp_pl_p100", pr, pr);
                h.InspPlusPress = spec.ChromeImg(h.InspPlus.transform, "Pressed", "insp_pl_press", pr, pr);
                SheetDraw.SetA(h.InspPlusPress, 0);
                var lines = RJ.L(plSpec, "lines");
                var nr = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : pr;
                hall.DeckLabel(h.InspPlus.transform, "One", DeckHallSpec.At(DeckHallSpec.Slot(plSpec), nr), "+1", pr);
                h.InspPlusLabel = hall.DeckLabel(h.InspPlus.transform, "Route", DeckHallSpec.Slot(plSmall), "MAIN", pr);
                st.InspectAdd = h.InspPlus;
            }

            var obr = RJ.R(obSpec, "rect");
            if (obr.width > 0)
            {
                h.InspObtain = DeckHall.Hit(c, "Obtain", obr, stage, () => HallRoute(st, MenuId.Bazaar));
                spec.ChromeImg(h.InspObtain.transform, "Plate", "insp_ob", obr, obr);
                var tab = RJ.D(spec.D("icons"), "bazaar");
                var tabSp = tab != null ? spec.Sprite(tab) : null;
                var lines = RJ.L(obSpec, "lines");
                var ir = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : new Rect(obr.x + 14, obr.y + 11, 15, 24);
                if (tabSp != null) SheetDraw.Img(h.InspObtain.transform, "Tablet", tabSp, ir, obr);
                hall.DeckLabel(h.InspObtain.transform, "Label", DeckHallSpec.At(DeckHallSpec.Slot(obSpec),
                    new Rect(ir.xMax + 8, obr.y, obr.xMax - ir.xMax - 12, obr.height)), "OBTAIN · BAZAAR", obr);
            }

            var inr = RJ.R(inSpec, "rect");
            if (inr.width > 0)
            {
                var node = DeckHall.Node(c, "InThisDeck", inr, stage);
                spec.ChromeImg(node, "Plate", "insp_in", inr, inr);
                var lines = RJ.L(inSpec, "lines");
                var lr = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : inr;
                hall.DeckLabel(node, "Label", DeckHallSpec.At(DeckHallSpec.Slot(inSpec), new Rect(lr.x, inr.y, lr.width + 8, inr.height)), "IN THIS DECK", inr);
                h.InspInDeck = hall.DeckLabel(node, "N", DeckHallSpec.Slot(idSpec), "0", inr);
            }
        }

        static void ShowHallInspect(State st, int id)
        {
            var h = st.Hall;
            if (h.InspectSheet == null) return;
            h.DrawFive?.Kill();
            st.SelectedId = id;
            var def = st.Db?.Get(id);
            if (h.InspArt != null)
            {
                var a = st.Db != null ? st.Db.GetArt(id) : null;
                h.InspArt.sprite = a != null ? a : UiFoundation.WhiteSprite();
                h.InspArt.color = a != null ? Color.white : new Color(1, 1, 1, .08f);
            }

            var limit = OfficialDataSources.StatusOf(id);
            var limitTag = limit switch
            {
                BanlistStatus.Forbidden => "  ·  Forbidden",
                BanlistStatus.Limited => "  ·  Limited",
                BanlistStatus.SemiLimited => "  ·  Semi-Limited",
                _ => ""
            };
            if (h.InspName != null)
            {
                h.InspName.text = (def?.name ?? "#" + id) + limitTag;
                TextFx.FitWidth(h.InspName, h.InspName.rectTransform.rect.width > 1 ? h.InspName.rectTransform.rect.width : 680f, 22);
            }

            var type = TypeLine(def);
            if (h.InspType != null) h.InspType.text = type;
            if (h.InspOwned != null)
            {
                var owned = st.Inv != null ? st.Inv.CountOnHand(id) + st.Inv.CountAtHome(id) : 0;
                h.InspOwned.text = "· owned " + owned;
                if (h.InspType != null)
                {
                    var w = h.InspType.preferredWidth + type.Length * 2.52f;
                    var tr = h.InspType.rectTransform;
                    var r = RJ.R(h.Spec.TextSpec("rest", null, "div.info", "div.ty"), "rect");
                    MenuV6Layout.Place(h.InspOwned.rectTransform, new Rect(r.x + w + 8, r.y, 300, r.height), h.Hall.StageRect);
                    _ = tr;
                }
            }

            if (h.InspDesc != null)
            {
                h.InspDesc.text = def?.desc ?? "";
                LayoutRebuilder.ForceRebuildLayoutImmediate(h.InspDesc.rectTransform.parent as RectTransform);
            }

            HallInspectButtons(st, id);
            h.InspectSheet.Open();
            st.Inspect.transform.SetAsLastSibling();
            HallSelectRing(st);
            var cell = st.PoolGrid != null ? st.PoolGrid.Find("C" + id) as RectTransform : null;
            if (cell != null) DeckFlight.Ripple(h.Hall, DeckFlight.StageRectOf(h.Hall, cell).center);
        }

        static string TypeLine(CardDef d)
        {
            if (d == null) return "";
            if (d.IsMonster)
            {
                var parts = new List<string>();
                if (d.level > 0) parts.Add((d.IsXyzMonster ? "RANK " : d.IsLinkMonster ? "LINK " : "LV ") + d.level);
                var ar = ((d.attribute ?? "") + " " + (d.race ?? "")).Trim();
                if (ar.Length > 0) parts.Add(ar.ToUpperInvariant());
                parts.Add("ATK " + Mathf.Max(0, d.atk) + (d.IsLinkMonster ? "" : " / DEF " + Mathf.Max(0, d.def)));
                return string.Join(" · ", parts);
            }

            var kind = string.IsNullOrEmpty(d.race) || string.Equals(d.race, "Normal", StringComparison.OrdinalIgnoreCase) ? "" : d.race + " ";
            return (kind + (d.IsSpell ? "Spell" : d.IsTrap ? "Trap" : d.type ?? "")).ToUpperInvariant();
        }

        static void HallInspectButtons(State st, int id)
        {
            var h = st.Hall;
            var def = st.Db?.Get(id);
            var inDeck = Cnt(st.Main, id) + Cnt(st.Extra, id) + Cnt(st.Side, id);
            if (h.InspInDeck != null) h.InspInDeck.text = inDeck.ToString();
            if (h.InspPlusLabel != null)
                h.InspPlusLabel.text = RouteAdd(st, def) switch
                {
                    Section.Extra => "EXTRA",
                    Section.Side => "SIDE",
                    _ => "MAIN"
                };
            var canAdd = st.Inv != null && st.Inv.CountOnHand(id) > 0;
            if (h.InspPlus != null) h.InspPlus.gameObject.SetActive(canAdd);
            if (h.InspMinus != null) h.InspMinus.gameObject.SetActive(inDeck > 0);
        }

        static void HideHallInspect(State st)
        {
            var h = st.Hall;
            h?.InspectSheet?.Close();
            HallSelectRing(st);
        }

        static void HallPlus(State st)
        {
            var h = st.Hall;
            var id = st.SelectedId;
            h.PlusAt = h.Hall.Now;
            if (h.InspPlus != null) DeckFlight.Ripple(h.Hall, DeckFlight.StageRectOf(h.Hall, h.InspPlus.GetComponent<RectTransform>()).center);
            var from = h.InspArtRt != null ? DeckFlight.StageRectOf(h.Hall, h.InspArtRt) : Rect.zero;
            h.FlightPending = Application.isPlaying;
            if (!TryAdd(st, id, out var msg))
            {
                h.FlightPending = false;
                Status(st, msg, false);
                return;
            }

            FreeUiKit.PlayConfirm();
            Status(st, msg, true);
            st.RefreshCards?.Invoke();
            HallInspectButtons(st, id);
            // the new card flies to its slot (the last of the pile it was routed to) and lands; then the Scale levels
            var pile = st.Section switch { Section.Extra => st.ExtraSlots, Section.Side => st.SideSlots, _ => st.MainSlots };
            var list = ListOf(st, st.Section);
            var slotIdx = list.Count - 1;
            var target = pile != null && slotIdx >= 0 && slotIdx < pile.childCount ? pile.GetChild(slotIdx) as RectTransform : null;
            var to = target != null ? DeckFlight.StageRectOf(h.Hall, target) : Rect.zero;
            var art = st.Db != null ? st.Db.GetArt(id) : null;
            DeckFlight.Plus1(h.Hall, art, from, to, target != null ? target.gameObject : null, () =>
            {
                h.FlightPending = false;
                h.Scale?.Set(st.Main?.Count ?? 0, h.Hall.Now);
            });
        }

        static void HallMinus(State st)
        {
            var id = st.SelectedId;
            if (TryRem(st, id, out var msg))
            {
                FreeUiKit.PlayClick();
                Status(st, msg, true);
                st.RefreshCards?.Invoke();
                HallInspectButtons(st, id);
            }
            else Status(st, msg, false);
        }

        // ------------------------------------------------------------------ the House of Life chrome on the card chips

        /// <summary>
        /// Paints the chips the builder makes (PoolChip / ArtChip / GhostFace) in the hall's chrome as they appear: the
        /// collection cards in their niches with the own badge, MAX / LIMIT / forbidden, the in-deck pips and the name; the
        /// altar slots in the slot ring, the first empty slot the dashed "+", the shared row's in its own frame; the entrance
        /// lay-in on the open. Data, taps and drags stay the chips' own.
        /// </summary>
        sealed class HallDecorator : MonoBehaviour
        {
            [NonSerialized] public State St;
            readonly Dictionary<Transform, int> _counts = new();

            void LateUpdate()
            {
                if (St?.Hall == null) return;
                Scan(St.PoolGrid, true, false);
                Scan(St.MainSlots, false, false);
                Scan(St.ExtraSlots, false, true);
                Scan(St.SideSlots, false, true);
                Entrance();
            }

            public void Now()
            {
                foreach (var t in new[] { St.PoolGrid, St.MainSlots, St.ExtraSlots, St.SideSlots })
                    if (t != null)
                        _counts.Remove(t);
                LateUpdate();
            }

            void Scan(Transform host, bool pool, bool xrow)
            {
                if (host == null) return;
                var n = host.childCount;
                if (_counts.TryGetValue(host, out var seen) && seen == n && !Dirty(host)) return;
                _counts[host] = n;
                var firstGhost = true;
                for (var i = 0; i < n; i++)
                {
                    var c = host.GetChild(i);
                    if (c == null) continue;
                    if (c.TryGetComponent(out HallDecor d) && d.Index == i) continue;
                    if (d == null) d = c.gameObject.AddComponent<HallDecor>();
                    d.Index = i;
                    if (pool) Pool(c, i, d);
                    else if (c.name == "Ghost") Ghost(c, firstGhost && !xrow, xrow);
                    else Slot(c, xrow);
                    if (c.name == "Ghost") firstGhost = false;
                }
            }

            static bool Dirty(Transform host) => false;

            void Entrance()
            {
                var h = St.Hall;
                if (h.Entered)
                {
                    Refill(h);
                    return;
                }

                var t = h.Hall.Now - h.EnterAt;
                if (!Application.isPlaying || t > 2.5)
                {
                    h.Entered = true;
                    t = 100;
                }

                if (St.MainSlots != null)
                    for (var i = 0; i < St.MainSlots.childCount; i++)
                    {
                        var k = (float)DeckHallMath.SlotIn(t, i);
                        var g = SheetDraw.Group(St.MainSlots.GetChild(i).gameObject);
                        g.alpha = St.MainSlots.GetChild(i).name == "Ghost" ? .9f * k + (k >= 1 ? .1f : 0f) : k;
                        St.MainSlots.GetChild(i).localScale = Vector3.one * (.94f + .06f * k);
                    }

                if (St.PoolGrid != null)
                    for (var i = 0; i < St.PoolGrid.childCount; i++)
                    {
                        var e = (float)DeckHallMath.CardIn(t, i);
                        SheetDraw.Group(St.PoolGrid.GetChild(i).gameObject).alpha = e;
                    }
            }

            /// <summary>A refilled collection (a search, a chip) pops in card by card (eOut .28 s, 12 ms apart; the page's
            /// results pop with back .35 s and survivors glide: the grid here is rebuilt, so every card is a newcomer).</summary>
            void Refill(HallState h)
            {
                if (double.IsNaN(h.RefillAt) || St.PoolGrid == null) return;
                var u = h.Hall.Now - h.RefillAt;
                var done = u > 1.2;
                for (var i = 0; i < St.PoolGrid.childCount; i++)
                {
                    var k = done ? 1f : (float)MenuV6Math.Back(MenuV6Math.Seg(u - .012 * i, 0, .35));
                    var c = St.PoolGrid.GetChild(i);
                    SheetDraw.Group(c.gameObject).alpha = Mathf.Clamp01(k * 1.5f);
                    c.localScale = Vector3.one * Mathf.Max(0f, .88f + .12f * k);
                }

                if (done) h.RefillAt = double.NaN;
            }

            void Pool(Transform c, int i, HallDecor d)
            {
                var h = St.Hall;
                var spec = h.Spec;
                var cols = Mathf.Max(1, spec.GridCols);
                var face = c.GetComponent<Image>();
                if (face != null) face.color = new Color(1, 1, 1, 0.001f);   // keeps the tap / drag target
                var id = IdOf(c);
                var row = i / cols;
                var col = i % cols;
                var cell = new Rect(0, 0, spec.GridCw, spec.GridCh);
                var niche = spec.Niche(row, col, out var nOff);
                if (niche != null && c.Find("Niche") == null)
                {
                    var ni = SheetDraw.Img(c, "Niche", niche, new Rect(nOff.x, nOff.y, nOff.width, nOff.height), cell);
                    if (ni != null) ni.transform.SetAsFirstSibling();
                }

                var art = c.Find("A") as RectTransform;
                if (art != null)
                {
                    art.offsetMin = Vector2.zero;
                    art.offsetMax = Vector2.zero;
                }

                Hide(c, "Qty");
                Hide(c, "Ban");
                var frame = AddChrome(c, "cc_frame", cell, art != null ? art.GetSiblingIndex() : 1);
                _ = frame;
                var onHand = St.Inv != null ? St.Inv.CountOnHand(id) : 0;
                var unowned = onHand <= 0;
                var veil = c.Find("V");
                if (veil != null)
                {
                    var vi = veil.GetComponent<Image>();
                    if (vi != null) vi.color = new Color(0f, 0f, 0f, .45f);   // the page's brightness(.55); the existing home-only veil
                }

                // the own badge (×n, top-left), the limit badge (top-right), the pips and the name under the card
                var ownSpec = spec.TextSpec("rest", "×", "div.cc", "span.own");
                var ownEl = spec.ChromeEl("cc_own");
                var gridEl = spec.Grid.Count > 0 ? spec.Grid[0] : new Rect(944, 476, 132, 192);
                if (ownEl.width > 0)
                {
                    var r = new Rect(ownEl.x - gridEl.x, ownEl.y - gridEl.y, ownEl.width + (onHand >= 2 ? 2 : 0), ownEl.height);
                    var node = Badge(c, "Own", unowned ? "cc_unowned_own" : "cc_own", r, cell);
                    var lab = h.Hall.DeckLabel(node, "N", DeckHallSpec.At(DeckHallSpec.Slot(ownSpec), r), "×" + onHand, cell);
                    if (lab != null)
                    {
                        lab.alignment = TextAnchor.MiddleCenter;
                        if (unowned) lab.color = new Color(187 / 255f, 187 / 255f, 187 / 255f);
                    }
                }

                var status = OfficialDataSources.StatusOf(id);
                var inDeck = Cnt(St.Main, id) + Cnt(St.Extra, id) + Cnt(St.Side, id);
                var free = St.Inv != null ? St.Inv.FreeCopiesForDeck(id, St.DeckIndex) : 0;
                var cap = OfficialDataSources.MaxCopies(id);
                string badge = null, key = null;
                if (status == BanlistStatus.Forbidden) AddChrome(c, "cc_forb", cell, c.childCount);
                else if (status == BanlistStatus.Limited) { badge = "LIMIT 1"; key = "cc_lm"; }
                else if (status == BanlistStatus.SemiLimited) { badge = "LIMIT 2"; key = "cc_lm"; }
                else if (!unowned && inDeck > 0 && inDeck >= Mathf.Min(free, cap > 0 ? cap : 3)) { badge = "MAX"; key = "cc_mx"; }
                if (badge != null)
                {
                    var bs = spec.TextSpec("rest", key == "cc_lm" ? "LIMIT" : "MAX", "div.cc", key == "cc_lm" ? "span.bd.lm" : "span.bd.mx");
                    var be = spec.ChromeEl(key);
                    var bw = be.width > 0 ? be.width : 44f;
                    var r = new Rect(gridEl.width + 6 - bw, -8, bw, be.height > 0 ? be.height : 22);
                    var node = Badge(c, "Badge", key, r, cell);
                    var lab = h.Hall.DeckLabel(node, "T", DeckHallSpec.At(DeckHallSpec.Slot(bs), r), badge, cell);
                    if (lab != null) lab.alignment = TextAnchor.MiddleCenter;
                }

                // the pips: three diamonds, lit per copy in this deck
                var pipOn = spec.Chrome("cc_pip_on");
                var pipOff = spec.Chrome("cc_pip_off");
                if (pipOn != null && pipOff != null)
                {
                    var pe = RJ.R(pipOn, "elRect");
                    var ps = RJ.R(pipOn, "shotRect");
                    var py = pe.y - gridEl.y;
                    var step = 16f;
                    var x0 = gridEl.width / 2f - step - pe.width / 2f;
                    for (var k = 0; k < 3; k++)
                    {
                        var el = new Rect(x0 + k * step, py, pe.width, pe.height);
                        var shot = new Rect(el.x + (ps.x - pe.x), el.y + (ps.y - pe.y), ps.width, ps.height);
                        SheetDraw.Img(c, "Pip" + k, spec.Sprite(k < inDeck ? pipOn : pipOff), shot, cell);
                    }
                }

                var def = St.Db?.Get(id);
                var nm = def?.name ?? "";
                var nmSpec = spec.TextSpec("rest", null, "div.cc", "span.nm");
                var lg = spec.TextSpec("rest", null, "div.cc", "span.nm.lg");
                var nr0 = RJ.R(nmSpec, "rect");
                var nr = new Rect(0, nr0.y - gridEl.y, gridEl.width, nr0.height);
                var useLg = nm.Length > 23 && lg != null;
                var nl = h.Hall.DeckLabel(c, "Name", DeckHallSpec.At(DeckHallSpec.Slot(useLg ? lg : nmSpec), nr), nm, cell);
                if (nl != null)
                {
                    nl.alignment = TextAnchor.MiddleCenter;
                    nl.horizontalOverflow = HorizontalWrapMode.Overflow;
                    // long names shrink to fit (floor = the .lg size), then ellipsis (decision 4)
                    var floor = useLg ? Mathf.FloorToInt(DeckHallSpec.Slot(lg).Size) : 12;
                    TextFx.FitWidth(nl, gridEl.width + 8, floor);
                    while (nl.preferredWidth > gridEl.width + 10 && nl.text.Length > 4)
                        nl.text = nl.text.Substring(0, nl.text.Length - 2).TrimEnd() + "…";
                }

                if (id == St.SelectedId && St.Hall.InspectSheet != null && St.Hall.InspectSheet.IsOpen) Ring(c);
                d.Painted = true;
            }

            /// <summary>The selection ring (chrome/cc_sel) around a collection card.</summary>
            public void Ring(Transform c)
            {
                if (c == null || c.Find("cc_sel") != null) return;
                var spec = St.Hall.Spec;
                AddChrome(c, "cc_sel", new Rect(0, 0, spec.GridCw, spec.GridCh), 0);
            }

            void Slot(Transform c, bool xrow)
            {
                var spec = St.Hall.Spec;
                var face = c.GetComponent<Image>();
                if (face != null) face.color = new Color(1, 1, 1, 0.001f);
                var art = c.Find("A") as RectTransform;
                if (art != null)
                {
                    art.offsetMin = Vector2.zero;
                    art.offsetMax = Vector2.zero;
                }

                Hide(c, "Qty");
                Hide(c, "Ban");
                var key = xrow ? "xrow_img" : "slot";
                var el = spec.ChromeEl(key);
                AddChrome(c, key, new Rect(0, 0, el.width > 0 ? el.width : 78, el.height > 0 ? el.height : 114), art != null ? art.GetSiblingIndex() : 1);
            }

            void Ghost(Transform c, bool plus, bool xrow)
            {
                var spec = St.Hall.Spec;
                var img = c.GetComponent<Image>();
                if (img != null) img.color = Color.clear;
                var key = xrow ? "xrow_side" : "slot_empty";
                var el = spec.ChromeEl(key);
                var cell = new Rect(0, 0, el.width > 0 ? el.width : 78, el.height > 0 ? el.height : 114);
                AddChrome(c, key, cell, 0);
                if (plus)
                {
                    var p = spec.Chrome("slot_empty_plus");
                    if (p != null)
                    {
                        var pe = RJ.R(p, "elRect");
                        var ps = RJ.R(p, "shotRect");
                        SheetDraw.Img(c, "Plus", spec.Sprite(p), new Rect(ps.x - pe.x, ps.y - pe.y, ps.width, ps.height), cell);
                    }
                }
            }

            /// <summary>A chrome shot on a chip, its margins kept in the chip's own units (the chip is resized by the fits).</summary>
            Image AddChrome(Transform c, string key, Rect cellAtPage, int sibling)
            {
                var spec = St.Hall.Spec;
                var sp = spec.ChromeSprite(key);
                if (sp == null) return null;
                var go = new GameObject(key, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(c, false);
                var img = go.GetComponent<Image>();
                img.sprite = sp;
                img.raycastTarget = false;
                img.type = spec.Sliced(key) && sp.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                var el = spec.ChromeEl(key);
                var shot = spec.ShotFor(key, el);
                var rt = img.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                // margins from the page's own element (shadows / rings reach past the card); a chip in the shared row (.7)
                // scales them with it
                var k = el.width > 0 && cellAtPage.width > 0 ? cellAtPage.width / el.width : 1f;
                rt.offsetMin = new Vector2(-(el.x - shot.x) * k, -(shot.yMax - el.yMax) * k);
                rt.offsetMax = new Vector2((shot.xMax - el.xMax) * k, (el.y - shot.y) * k);
                if (sibling >= 0 && sibling < c.childCount) go.transform.SetSiblingIndex(sibling);
                return img;
            }

            RectTransform Badge(Transform c, string name, string key, Rect r, Rect cell)
            {
                var node = DeckHall.Node(c, name, r, cell);
                var spec = St.Hall.Spec;
                spec.ChromeImg(node, "Plate", key, r, r);
                return node;
            }

            static void Hide(Transform c, string child)
            {
                var t = c.Find(child);
                if (t != null) t.gameObject.SetActive(false);
            }

            static int IdOf(Transform c)
            {
                var n = c.name;
                return n.Length > 1 && n[0] == 'C' && int.TryParse(n.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
            }
        }

        sealed class HallDecor : MonoBehaviour
        {
            public int Index = -1;
            public bool Painted;
        }

        /// <summary>The exclusion chip's pop (eBack scale from its left edge, deck.js:142).</summary>
        sealed class HallPop : MonoBehaviour
        {
            public DeckHall Hall;
            public double At;

            void LateUpdate()
            {
                if (Hall == null) return;
                var k = (float)Math.Max(0, DeckHallMath.ExclusionChip(Hall.Now - At));
                var rt = (RectTransform)transform;
                rt.pivot = new Vector2(0f, .5f);
                rt.localScale = new Vector3(k, k, 1f);
                if (k >= 1f && Hall.Now - At > .4) enabled = false;
            }
        }

        /// <summary>
        /// Tests and captures (MenuSmokeTest, MenuV6Capture): the hall's checks on the deck in memory. Nothing here persists:
        /// RequestLeaveEditor persists only a complete deck, and the probe empties MAIN in memory first (then puts it back),
        /// so the leave guard opens and no save is written; DRAW 5 copies MAIN; the box is shut visually only.
        /// </summary>
        public sealed class HallProbe : MonoBehaviour
        {
            internal object Bound;
            State St => Bound as State;

            public DeckHall Hall => St?.Hall?.Hall;

            public int MainCount => St?.Main?.Count ?? 0;

            /// <summary>The functional checks; throws on the first that fails, else a one-line summary.</summary>
            public string Run()
            {
                var st = St;
                if (st?.Hall == null) throw new Exception("not a hall build");
                var h = st.Hall;
                var sb = new StringBuilder();
                // the altar: 10 columns at the baked wells, EXTRA and SIDE on one row
                var fit = st.BoardHost != null ? st.BoardHost.GetComponent<DeckBoardFit>() : null;
                if (fit == null || !fit.Ready) throw new Exception("board not solved");
                if (fit.Plan.Cols != DeckBoardLayout.HallCols) throw new Exception("MAIN lays " + fit.Plan.Cols + " columns");
                if (st.BoardHost.Find("XRow") == null) throw new Exception("no shared EXTRA / SIDE row");
                if (st.ExtraTray == null || st.SideTray == null || st.ExtraTray.parent != st.SideTray.parent)
                    throw new Exception("EXTRA and SIDE are not on one row");
                sb.Append("10 cols, card ").Append(fit.Plan.CardW.ToString("0.#", CultureInfo.InvariantCulture));
                if (h.Scale == null || !h.Scale.Ok) throw new Exception("the Scale of Ma'at did not build");
                if (h.Box == null) throw new Exception("the relic box did not build");
                // the back chip keeps the incomplete-deck guard (MAIN emptied in memory: nothing is persisted)
                var close = h.Hall.Close;
                if (close == null || close.name != "Close") throw new Exception("no back chip named Close");
                var saved = new List<int>(st.Main);
                try
                {
                    st.Main.Clear();
                    close.onClick.Invoke();
                    var host = st.Window != null ? st.Window : st.Body;
                    var guard = host != null ? host.Find("LeaveGuard") : null;
                    if (guard == null) throw new Exception("the incomplete-deck guard did not open from the back chip");
                    UnityEngine.Object.DestroyImmediate(guard.gameObject);
                }
                finally
                {
                    st.Main.Clear();
                    st.Main.AddRange(saved);
                }

                sb.Append(", guard ok");
                // "-word" excludes (in memory: the pool is rebuilt from the same owned set)
                var pool = BuildPool(st);
                if (pool.Count > 0)
                {
                    var keepSearch = st.Search;
                    try
                    {
                        var name = pool[0].Def?.name ?? "";
                        var word = name.Split(' ')[0];
                        if (word.Length >= 3)
                        {
                            st.Search = "-" + word;
                            var without = BuildPool(st);
                            foreach (var r in without)
                                if (r.Def != null && r.Def.name != null && r.Def.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                                    throw new Exception("-" + word + " kept " + r.Def.name);
                            sb.Append(", -").Append(word).Append(" ").Append(pool.Count).Append("→").Append(without.Count);
                        }
                    }
                    finally
                    {
                        st.Search = keepSearch;
                    }

                    // the inspect sheet rises and folds
                    ShowInspect(st, pool[0].Id);
                    if (h.InspectSheet == null || !h.InspectSheet.Visible) throw new Exception("the inspect sheet did not open");
                    HideInspect(st);
                    sb.Append(", inspect ok");
                }

                // DRAW 5 is UI-only: five of MAIN, MAIN unchanged
                if (st.Main.Count > 0 && h.DrawFive != null)
                {
                    var before = string.Join(",", st.Main);
                    h.DrawFive.Open(st.Main);
                    if (h.DrawFive.Drawn.Count != Mathf.Min(5, st.Main.Count)) throw new Exception("DRAW 5 drew " + h.DrawFive.Drawn.Count);
                    foreach (var id in h.DrawFive.Drawn)
                        if (!st.Main.Contains(id)) throw new Exception("DRAW 5 drew a card not in MAIN");
                    if (string.Join(",", st.Main) != before) throw new Exception("DRAW 5 changed MAIN");
                    h.DrawFive.Kill();
                    sb.Append(", draw 5 ok");
                }

                return sb.ToString();
            }

            /// <summary>Captures: the inspect sheet on the first collection card, at rest.</summary>
            public bool ShowFirstCard()
            {
                var st = St;
                if (st?.Hall == null || st.PoolRows == null || st.PoolRows.Count == 0) return false;
                ShowInspect(st, st.PoolRows[0].Id);
                st.Hall.InspectSheet?.Snap(true);
                return true;
            }

            public void HideCard()
            {
                if (St?.Hall != null) HideInspect(St);
            }

            /// <summary>
            /// Captures: the +1 flight drawn on its own (DeckFlight.Plus1 from the open inspect card to MAIN's last card slot,
            /// which hides until the card lands) — no TryAdd, the deck is not touched and nothing persists. False when no
            /// inspect card or MAIN slot is up.
            /// </summary>
            public bool FlyPreview()
            {
                var st = St;
                var h = st?.Hall;
                if (h?.Hall == null || h.InspArtRt == null || st.MainSlots == null || st.MainSlots.childCount == 0) return false;
                var from = DeckFlight.StageRectOf(h.Hall, h.InspArtRt);
                var last = Mathf.Clamp((st.Main?.Count ?? 1) - 1, 0, st.MainSlots.childCount - 1);
                var target = st.MainSlots.GetChild(last) as RectTransform;
                var to = target != null ? DeckFlight.StageRectOf(h.Hall, target) : Rect.zero;
                if (from.width <= 0 || to.width <= 0) return false;
                DeckFlight.Plus1(h.Hall, st.Db != null ? st.Db.GetArt(st.SelectedId) : null, from, to, target.gameObject, null);
                return true;
            }

            string _keepSearch;
            Filter _keepFilter;

            /// <summary>
            /// Captures: a search with chips, in memory — an exclusion word of the first collection card's name typed through
            /// the field's own path (the EXCLUDE chip pops, the rubric span) and the MONSTER quick chip on. Nothing persists;
            /// <see cref="ClearSearch"/> puts the query and the filter back. Null when there is nothing to search.
            /// </summary>
            public string SearchPreview()
            {
                var st = St;
                var h = st?.Hall;
                if (h?.Field == null) return null;
                string word = null;
                foreach (var r in BuildPool(st))
                {
                    var w0 = (r.Def?.name ?? "").Split(' ')[0];
                    if (w0.Length >= 4 && char.IsLetter(w0[0]) && char.IsLetter(w0[w0.Length - 1]))
                    {
                        word = w0.ToLowerInvariant();
                        break;
                    }
                }

                if (word == null) return null;
                _keepSearch = st.Search ?? "";
                _keepFilter = st.Filter;
                var q = "-" + word;
                h.Field.text = q;
                h.Field.onEndEdit.Invoke(q);
                var mon = Array.FindIndex(HallChipMap, c => c.lab == "MONSTER");
                if (mon >= 0 && st.Filter != Filter.Mon) HallChipTap(st, mon);
                return q;
            }

            public void ClearSearch()
            {
                var st = St;
                var h = st?.Hall;
                if (h?.Field == null || _keepSearch == null) return;
                st.Filter = _keepFilter;
                h.Field.text = _keepSearch;
                h.Field.onEndEdit.Invoke(_keepSearch);
                HallQueryChanged(st);
                _keepSearch = null;
            }

            /// <summary>Captures: the box shut as SAVE shuts it, without saving (the status line says sealed).</summary>
            public void Seal()
            {
                var h = St?.Hall;
                if (h?.Box == null) return;
                h.SaveAt = h.Hall.Now;
                h.Box.Shut(h.Hall.Now);
            }

            public void Unseal()
            {
                var h = St?.Hall;
                h?.Box?.Reopen(h.Hall.Now);
            }

            public bool DrawFive(bool open)
            {
                var h = St?.Hall;
                if (h?.DrawFive == null) return false;
                if (open)
                {
                    if (St.Main == null || St.Main.Count == 0) return false;
                    h.DrawFive.Open(St.Main);
                }
                else h.DrawFive.Kill();
                return true;
            }
        }
    }
}
