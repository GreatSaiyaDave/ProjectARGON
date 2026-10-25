using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// A sheet over the Stone (BUILD_PLAN §6 P3; menu.js:557-600, :957-986): its host under the Stone's Root, the page's dim
    /// (flat; DUEL radial), the room's DOM dimmed under it, a stage-px space following the Stone's fit, the false door
    /// (<see cref="FalseDoorFrame"/>), the content, and on top the Puzzle-lock (<see cref="PuzzleLock"/>) flying the keystone to
    /// its clasp and home again. One clock: u = menu seconds since the open, v = since the close began. In edit mode it is
    /// built at rest and its close is immediate (MenuSmokeTest); in play mode the close runs the page's curves and the host goes
    /// at the page's sheet end.
    /// </summary>
    public abstract class StoneSheet : MonoBehaviour
    {
        public MenuV6Stage Stone { get; private set; }
        public SheetKind Kind { get; private set; }

        FalseDoorFrame _frame;

        /// <summary>The sheet's false door. Taking it binds the frame's fractures to this sheet's clock, so they grow at sheet
        /// time .9-2.0 of this sheet's u (anime_v61_cracks.js sheetCracks); unbound they read 1e3 and showed whole at once.</summary>
        public FalseDoorFrame Frame
        {
            get => _frame;
            protected set
            {
                _frame = value;
                if (_frame != null) _frame.Clock = () => U;
            }
        }
        public RectTransform Host { get; private set; }
        public RectTransform Space { get; private set; }
        public MenuLayoutKind Lay { get; private set; }

        /// <summary>The sheet's content (its DOM: rows, text, buttons); faded with the content envelope.</summary>
        public RectTransform Content { get; protected set; }

        protected Dictionary<string, object> J;
        protected Rect StageRect;
        protected Image DimImg;
        protected CanvasGroup DimGroup, ContentGroup;
        protected PuzzleLock Lock;
        protected Vector2 Clasp;
        protected double ClaspScale = .5;
        protected readonly List<Selectable> Live = new();
        double _t0, _t1 = double.NaN;
        bool _done;
        Action _after;
        double _afterAt = double.NaN;
        bool _afterFired;
        double _frozenU = double.NaN;

        /// <summary>Menu seconds since the open.</summary>
        public double U => !double.IsNaN(_frozenU) ? _frozenU : MenuV6Clock.Now - _t0;

        public double V => double.IsNaN(_t1) ? -1 : Math.Max(0, MenuV6Clock.Now - _t1);
        public bool Closing => !double.IsNaN(_t1);

        /// <summary>The Stone's flat-dim opacity at rest (#dim.flat .92 × .8 × .9), Linear-corrected.</summary>
        static float FlatDim(double k) => MenuV6Linear.ShadeDark(.92 * .8 * .9 * MenuV6MathF.Clamp01(k));

        protected bool Init(MenuV6Stage stone, RectTransform host, Image dim, SheetKind kind)
        {
            if (stone == null || host == null) return false;
            Stone = stone;
            Host = host;
            Kind = kind;
            Lay = stone.Kind;
            J = SheetData.Root(Lay);
            if (J == null) return false;
            var st = SheetData.Stage(J, Lay);
            StageRect = new Rect(0, 0, st.x, st.y);
            DimImg = dim;
            if (dim != null)
            {
                DimGroup = SheetDraw.Group(dim.gameObject);
                if (kind != SheetKind.Duel)
                {
                    var c = dim.color;
                    c.a = FlatDim(1);
                    dim.color = c;
                }
            }

            Space = SheetDraw.StageSpace(host, stone, "SheetStage");
            _t0 = MenuV6Clock.Now;
            stone.SetKeystoneShown(false);
            return true;
        }

        /// <summary>The content node over the frame (clipped with it), faded by the content envelope.</summary>
        protected RectTransform MakeContent()
        {
            var parent = Frame != null && Frame.Content != null ? Frame.Content : Space;
            Content = SheetDraw.Stretch(parent, "Ledger");
            ContentGroup = SheetDraw.Group(Content.gameObject);
            return Content;
        }

        /// <summary>The Puzzle-lock, last (on top of everything the sheet draws).</summary>
        protected void BuildLock(Vector2 clasp, double sc)
        {
            Clasp = clasp;
            ClaspScale = sc;
            try
            {
                var host = SheetDraw.Stretch(Space, "PuzzleLock");
                var ctx = Stone.Parts.Ctx;
                Lock = PuzzleLock.Build(ctx != null && ctx.Gfx != null ? ctx.Gfx : new UguiGfx(), host, StageRect, Lay, ctx);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] the Puzzle-lock skipped: " + ex.Message);
                Lock = null;
            }
        }

        /// <summary>Start the close. <paramref name="after"/> runs once, <paramref name="afterAt"/> s into the close (NaN = when the
        /// sheet is gone); in edit mode at once.</summary>
        public void Close(Action after = null, double afterAt = double.NaN)
        {
            if (_done) return;
            if (!Closing) _t1 = MenuV6Clock.Now;
            if (after != null)
            {
                _after = after;
                _afterAt = afterAt;
            }

            if (DimImg != null) DimImg.raycastTarget = false;
            foreach (var s in Live)
                if (s != null)
                    s.interactable = false;
            if (!Application.isPlaying)
            {
                Finish();
                return;
            }

            Step();
        }

        /// <summary>Remove at once (a new sheet replaces this one, the menu goes): the Stone is put back at rest.</summary>
        public void Kill()
        {
            if (_done) return;
            _done = true;
            Restore();
            if (Host != null) MenuEntrance.Kill(Host.gameObject);
        }

        void Finish()
        {
            if (_done) return;
            _done = true;
            Restore();
            var after = _after;
            var fire = !_afterFired && after != null;
            _afterFired = true;
            if (Host != null) MenuEntrance.Kill(Host.gameObject);
            if (fire) after();
        }

        void Restore()
        {
            OnRestore();
            if (Stone == null) return;
            Stone.SetKeystoneShown(true);
            Stone.SetSheetDim(0);
            Stone.SetTorchGutter(1);
            if (Lock != null) Lock.Clear();
        }

        /// <summary>A sheet that built parts outside its host (on the Stone) removes them here; it runs on every way out
        /// (the close's end, Kill, the host destroyed).</summary>
        protected virtual void OnRestore()
        {
        }

        void OnDestroy()
        {
            if (!_done) Restore();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || _done) return;
            Step();
        }

        /// <summary>Draw the sheet at its current time (captures call it after freezing the menu clock).</summary>
        public void Step()
        {
            if (_done) return;
            var t = MenuV6Clock.Now;
            double u = U, v = V;
            if (Frame != null) Frame.Apply(SheetMath.RevealAt(Kind, u, v));
            var dim = SheetMath.Dim(Kind, u, v);
            if (DimGroup != null)
            {
                // the group carries the envelope (the dim's crack copy follows it at 30 %): its alpha is the Linear-corrected
                // opacity at this dim over the rest one
                var rest = Kind == SheetKind.Duel ? 1.0 : FlatDim(1);
                var now = Kind == SheetKind.Duel ? SheetMath.DimOpacity(Kind, u, v) / .8 : FlatDim(SheetMath.K(Kind, u, v).Dim);
                DimGroup.alpha = Mathf.Clamp01((float)(rest > 0 ? now / rest : 0));
            }

            Stone.SetSheetDim(dim);
            var ca = SheetMath.ContentA(Kind, u, v);
            if (ContentGroup != null) SheetDraw.SetA(ContentGroup, MenuV6Linear.Fade(ca));
            var live = SheetMath.ContentLive(Kind, u, v);
            if (ContentGroup != null) ContentGroup.blocksRaycasts = live || Kind == SheetKind.Duel;
            if (Lock != null)
                Lock.Frame(Kind, u, v, Clasp, ClaspScale, CardFlare(), MenuV6RigMath.KeystoneEye(t, 0), t);
            Draw(u, v, t);
            if (Frame != null && Frame.Cracks != null) Frame.Cracks.Render(t);
            if (Closing)
            {
                if (!_afterFired && _after != null && !double.IsNaN(_afterAt) && v >= _afterAt)
                {
                    _afterFired = true;
                    var a = _after;
                    _after = null;
                    a();
                }

                if (v >= SheetMath.EndAfterClose(Kind)) Finish();
            }
        }

        /// <summary>Tests / captures: hold the sheet at u (the menu clock may be frozen too) and draw it.</summary>
        public void ShowAt(double u)
        {
            _frozenU = u;
            _t1 = double.NaN;
            Step();
        }

        /// <summary>Captures: hold the sheet <paramref name="v"/> s into its close (u held at <paramref name="u"/>; the menu
        /// clock frozen) and draw it. No route or after-action runs; a v at or past the sheet's end removes it as a close would.
        /// </summary>
        public void ShowCloseAt(double u, double v)
        {
            if (_done) return;
            _frozenU = u;
            _t1 = MenuV6Clock.Now - Math.Max(0, v);
            Step();
        }

        protected virtual double CardFlare() => 0;
        protected abstract void Draw(double u, double v, double t);

        // ------------------------------------------------------------------ shared builders

        protected Text Label(Transform parent, string name, object spec, string shown, Rect parentRect)
        {
            var t = SheetDraw.Label(parent, name, spec, shown, parentRect);
            return t;
        }

        /// <summary>A plate button (rest / hover sprites) at a stage rect with a label spec; its Button is "live" (it goes
        /// non-interactable on the close).</summary>
        protected Button PlateButton(Transform parent, string name, object rest, object hover, Rect plateRect, Rect parentRect,
            object textSpec, string label, Action onClick)
        {
            var sp = SheetData.Sprite(rest);
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp != null ? sp : UiFoundation.WhiteSprite();
            img.type = sp != null && sp.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            if (sp == null) img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;
            MenuV6Layout.Place(img.rectTransform, plateRect, parentRect);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var hs = SheetData.Sprite(hover);
            if (hs != null && sp != null) PlateHover.Attach(go, img, sp, hs);
            if (textSpec != null)
            {
                var t = SheetDraw.Label(go.transform, "Title", textSpec, label, plateRect);
                if (t != null) t.alignment = TextAnchor.MiddleCenter;
            }

            Live.Add(btn);
            return btn;
        }
    }

    // ============================================================================================================== the ledgers

    /// <summary>The ledger rows (BUILD_PLAN §7, D12): real rows only, each from its source; a row with none is left out.</summary>
    public static class LedgerData
    {
        public struct Row
        {
            public string Label, Value;
        }

        static LocalAccountStore.Account Account()
        {
            try
            {
                var s = AppSession.Ensure();   // a MonoBehaviour: no ?. (Unity null)
                return s != null ? s.Account : null;
            }
            catch
            {
                return null;
            }
        }

        public static List<Row> Rows(string id, MenuV6Snapshot snap)
        {
            var rows = new List<Row>();
            switch (id)
            {
                case "deck":
                    Deck(rows);
                    break;
                case "bag":
                    Bag(rows);
                    break;
                case "story":
                    rows.Add(new Row { Label = "Season 1", Value = "Duelist Kingdom" });   // SystemsSheets / MenuV6Data.SeasonCaption
                    if (snap != null && snap.ChapterIndex > 0)
                    {
                        rows.Add(new Row { Label = "Chapter", Value = snap.ChapterIndex.ToString(CultureInfo.InvariantCulture) });
                        if (!string.IsNullOrEmpty(snap.ChapterTitle) && snap.StagesTotal > 0)
                            rows.Add(new Row { Label = snap.ChapterTitle, Value = snap.StagesCleared + " / " + snap.StagesTotal });
                    }

                    break;
                case "bazaar":
                    rows.Add(new Row { Label = "Sets", Value = "LOB · MRD · SRL" });
                    rows.Add(new Row
                    {
                        Label = "Price",
                        Value = StoneTabletService.PackCost.ToString("N0", CultureInfo.InvariantCulture) + " SE of a set · one " +
                                StoneTabletService.PackSize + "-card pack"
                    });
                    break;
                case "view":
                    rows.Add(new Row { Label = "Solid Vision", Value = "AR lens ready" });
                    rows.Add(new Row { Label = "Arena", Value = "scan the table" });
                    rows.Add(new Row { Label = "Mode", Value = "AR or Digital" });
                    break;
            }

            return rows;
        }

        static void Deck(List<Row> rows)
        {
            try
            {
                var acc = Account();
                if (acc == null) return;
                acc.EnsureInventory();
                var inv = acc.inventory;
                if (inv == null || inv.deckBoxes == null) return;
                var a = inv.ActivePlayDeckIndex;
                if (a >= 0 && a < inv.deckBoxes.Length && inv.deckBoxes[a] != null)
                {
                    var box = inv.deckBoxes[a];
                    rows.Add(new Row { Label = (box.name ?? "Deck") + " · equipped", Value = Len(box.main) + " / " + WRLDZ.Duel.TcgRules.MainDeckMin });
                    rows.Add(new Row
                    {
                        Label = "Extra · Side",
                        Value = Len(box.extra) + " / " + WRLDZ.Duel.TcgRules.ExtraDeckMax + " · " + Len(box.side) + " / " + WRLDZ.Duel.TcgRules.SideDeckMax
                    });
                }

                for (var i = 0; i < inv.deckBoxes.Length && rows.Count < 4; i++)
                {
                    var b = inv.deckBoxes[i];
                    if (i == a || b == null || !b.occupied) continue;
                    rows.Add(new Row { Label = b.name ?? "Deck", Value = Len(b.main) + " / " + WRLDZ.Duel.TcgRules.MainDeckMin });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] DECK ledger: " + ex.Message);
            }
        }

        static int Len(int[] a) => a != null ? a.Length : 0;

        /// <summary>The equipped deck's first distinct cards (the DECK strip, through CardArt).</summary>
        public static List<int> DeckCards(int n)
        {
            var list = new List<int>();
            try
            {
                var acc = Account();
                if (acc == null) return list;
                acc.EnsureInventory();
                var inv = acc.inventory;
                var a = inv != null ? inv.ActivePlayDeckIndex : -1;
                if (inv == null || inv.deckBoxes == null || a < 0 || a >= inv.deckBoxes.Length || inv.deckBoxes[a] == null) return list;
                foreach (var id in inv.deckBoxes[a].main ?? Array.Empty<int>())
                {
                    if (id == 0 || list.Contains(id)) continue;
                    list.Add(id);
                    if (list.Count >= n) break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] DECK strip: " + ex.Message);
            }

            return list;
        }

        /// <summary>The BAZAAR strip: two LOB, two MRD and one SRL card of the starter sets (StreamingAssets/WRLDZ/eras/
        /// in_game_set_cards.json, checked 2026-10-19: Blue-Eyes White Dragon, Pot of Greed; Kuriboh, Mirror Force; Mystical
        /// Space Typhoon), the mockup's own five.</summary>
        public static readonly int[] BazaarCards = { 89631139, 55144522, 40640057, 44095762, 5318639 };

        static void Bag(List<Row> rows)
        {
            try
            {
                var acc = Account();
                if (acc == null) return;
                var vm = InventoryService.BuildView(acc);   // the BAG's own wallet chips (InventoryScreen.cs: Đ, ◎, ⚡)
                var inv = acc.inventory;
                var per = new List<string>();
                foreach (var code in new[] { "LOB", "MRD", "SRL" })
                {
                    var n = ArtifactService.SetEnergyOf(inv, code);
                    if (n > 0) per.Add(code + " " + n.ToString("N0", CultureInfo.InvariantCulture));
                }

                rows.Add(new Row
                {
                    Label = "Set Energy",
                    Value = per.Count > 0 ? string.Join(" · ", per) : vm.SetEnergy.ToString("N0", CultureInfo.InvariantCulture)
                });
                rows.Add(new Row { Label = "Digizeni", Value = vm.Digizeni.ToString("N0", CultureInfo.InvariantCulture) });
                rows.Add(new Row { Label = "Duel Coin", Value = vm.DuelCoins.ToString("N0", CultureInfo.InvariantCulture) });
                _setEnergyTotal = vm.SetEnergy;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] BAG ledger: " + ex.Message);
            }
        }

        static int _setEnergyTotal;

        /// <summary>The wallet chip's Set Energy (the BAG strip's one real item).</summary>
        public static int SetEnergyTotal()
        {
            var acc = Account();
            if (acc == null) return 0;
            try { return InventoryService.BuildView(acc).SetEnergy; }
            catch { return _setEnergyTotal; }
        }

        /// <summary>Season 1's gates (StoryCampaignService.Gates) with their state against the current gate (1-based index).</summary>
        public static List<(string title, int state)> Gates(MenuV6Snapshot snap)
        {
            var list = new List<(string, int)>();
            try
            {
                var gates = StoryCampaignService.Gates();
                var cur = snap != null ? snap.ChapterIndex : 0;
                var all = snap != null && snap.StagesTotal > 0 && snap.StagesCleared >= snap.StagesTotal;
                for (var i = 0; i < gates.Count; i++)
                {
                    if (gates[i] == null) continue;
                    var n = i + 1;
                    var state = all || n < cur ? 2 : n == cur ? 1 : 0;
                    list.Add((gates[i].title ?? ("Chapter " + n), state));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] STORY gates: " + ex.Message);
            }

            return list;
        }
    }

    // ============================================================================================================== recess sheets

    /// <summary>
    /// A recess sheet (BUILD_PLAN §6 P3 step 3, D12; menu.js:957-986, wrldz.css #sht): the recess sinks (P1b's press), the room
    /// dims flat, the Puzzle-lock opens the false door; inside, the recess carved into the sheet (its well: the lit
    /// pictogram in the lapis well, its Item on the rim; FM Item art for DECK / BAG / STORY, local with a clean fallback), the
    /// ledger (real rows only), the strip and the CTA into today's screen. "TAP OUTSIDE · THE PUZZLE LOCKS IT".
    /// </summary>
    public sealed class RecessSheet : StoneSheet
    {
        public const string Hint = "TAP OUTSIDE · THE PUZZLE LOCKS IT";
        public string Id { get; private set; }
        Action _cta;
        Vector2 _well;
        double _mr;
        QuadLayer _glow;

        /// <summary>The CTA label per recess (the approved copy; their routes are the old hub's).</summary>
        public static string CtaLabel(string id) => id switch
        {
            "deck" => "OPEN DECK ›",
            "bag" => "OPEN BAG ›",
            "story" => "CONTINUE ›",
            "bazaar" => "OPEN BAZAAR ›",
            "view" => "OPEN VIEW ›",
            _ => null
        };

        /// <summary>Build into <paramref name="host"/> (the Stone's popover host, with its dim). Null when the sheet data or the
        /// frame is missing (the caller then routes straight to the screen, as before).</summary>
        public static RecessSheet Build(MenuV6Stage stone, RectTransform host, Image dim, string id, Action cta)
        {
            if (stone == null || host == null || CtaLabel(id) == null) return null;
            var s = host.gameObject.AddComponent<RecessSheet>();
            if (!s.Init(stone, host, dim, SheetKind.Recess))
            {
                Destroy(s);
                return null;
            }

            s.Id = id;
            s._cta = cta;
            s.Frame = FalseDoorFrame.OverStone(s.Space, SheetKind.Recess, s.Lay, "recess" + id);
            s.BuildWell();
            s.BuildLedger(stone.Data);
            var rec = RJ.D(RJ.D(s.J, "geo"), "recFrame");
            s.BuildLock(RJ.V(rec, "crest"), .5);
            if (!Application.isPlaying) s.ShowAt(10);
            else s.Step();
            return s;
        }

        void BuildWell()
        {
            var w = RJ.D(RJ.D(RJ.D(J, "recess"), "wells"), Id);
            if (w == null) return;
            _well = RJ.V(w, "centre");
            _mr = RJ.N(w, "mr", 92);
            // the breathing gold glow under the well (drawSpr SPR.gold, mr 2.4, (.22 + .08 sin 1.6t) content), live
            var ctx = Stone.Parts.Ctx;
            var gfx = ctx != null && ctx.Gfx != null ? ctx.Gfx : new UguiGfx();
            // the well is drawn with the frame on the sheet canvas (#fxs), under the fractures (#crkF) and the ledger (#sht)
            var host = SheetDraw.Stretch(Frame != null ? Frame.Body : Space, "WellGlow");
            if (Frame != null) host.SetSiblingIndex(1);
            _glow = gfx.Quads(host, "Well_glow", StageRect, gfx.Glow(MenuV6Linear.Glow));
            _glow.Add = true;
            var parent = Frame != null ? Frame.Body : Space;
            var img = SheetDraw.Part(parent, "Well", RJ.D(w, "well"), StageRect);
            if (img != null && Frame != null) img.transform.SetSiblingIndex(2);
        }

        void BuildLedger(MenuV6Snapshot snap)
        {
            var c = MakeContent();
            var spec = RJ.L(RJ.D(RJ.D(RJ.D(J, "texts"), "recess"), Id), "texts");
            var chrome = RJ.D(RJ.D(J, "recess"), "chrome");
            Label(c, "Title", SheetData.Spec(spec, "h"), Id == "set" ? "SETTINGS" : Id.ToUpperInvariant(), StageRect);
            var sub = SheetData.Spec(spec, "sub");
            Label(c, "Sub", sub, RJ.S(sub, "text"), StageRect);   // the approved sub-line (upper-cased by its spec)

            // the rows: label left, value right; the row rule under each (the recorded first row's rects and pitch)
            var labels = new List<Dictionary<string, object>>();
            foreach (var o in spec ?? new List<object>())
                if (o is Dictionary<string, object> d && RJ.S(d, "cls") == "" && RJ.S(d, "tag") == "span") labels.Add(d);
            var values = new List<Dictionary<string, object>>();
            foreach (var o in spec ?? new List<object>())
                if (o is Dictionary<string, object> d && RJ.S(d, "cls") == "" && RJ.S(d, "tag") == "b" &&
                    RJ.R(d, "rect").y < RJ.R(RJ.D(chrome, "strip"), "rect").y) values.Add(d);
            var rows = LedgerData.Rows(Id, snap);
            if (labels.Count > 0 && values.Count > 0)
            {
                var l0 = RJ.R(labels[0], "rect");
                var v0 = RJ.R(values[0], "rect");
                var pitch = labels.Count > 1 ? RJ.R(labels[1], "rect").y - l0.y : l0.height * 1.75f;
                var rule = RJ.D(chrome, "row_rule");
                var rr = RJ.R(rule, "rect");
                for (var i = 0; i < rows.Count && i < 4; i++)
                {
                    var dy = pitch * i;
                    var lslot = SheetData.Slot(labels[0]);
                    var vslot = SheetData.Slot(values[0]);
                    lslot.Rect = new Rect(l0.x, l0.y + dy, v0.xMax - l0.x, l0.height);
                    lslot.NoWrap = true;
                    vslot.Rect = new Rect(l0.x, v0.y + dy, v0.xMax - l0.x, v0.height);
                    vslot.Align = "right";
                    vslot.NoWrap = true;
                    TextFx.Label(c, "Row" + i, lslot, rows[i].Label, StageRect);
                    var vt = TextFx.Label(c, "Val" + i, vslot, rows[i].Value, StageRect);
                    if (vt != null) TextFx.FitWidth(vt, (v0.xMax - l0.x) * .62f, 12);
                    SheetDraw.PartAt(c, "Rule" + i, rule, new Rect(rr.x, rr.y + dy, rr.width, rr.height), StageRect, sliced: true);
                }
            }

            // the strip: its cel, its caption, its content
            var strip = RJ.D(chrome, "strip");
            SheetDraw.Part(c, "Strip", strip, StageRect, sliced: true);
            var cap = SheetData.Spec(spec, "cap");
            var capText = Id switch
            {
                "deck" => "from this deck",
                "bag" => "in the bag",
                "story" => "season 1 · the chapters",
                "bazaar" => "cards in these sets",
                "view" => "the arena",
                _ => null
            };
            if (capText != null) Label(c, "StripCap", cap, capText, StageRect);
            var sr = RJ.R(strip, "rect");
            var cr = RJ.R(cap, "rect");
            var top = cr.yMax + (Lay == MenuLayoutKind.Landscape ? 12 : 18);
            switch (Id)
            {
                case "deck":
                    StripCards(c, LedgerData.DeckCards(5), top, sr);
                    break;
                case "bazaar":
                    StripCards(c, new List<int>(LedgerData.BazaarCards), top, sr);
                    break;
                case "bag":
                    StripSetEnergy(c, spec, chrome);
                    break;
                case "story":
                    StripGates(c, spec, chrome, snap, top);
                    break;
                case "view":
                    Label(c, "StripText", SheetData.Spec(spec, "txt"), "Point the lens at a flat table · monsters take the field at true scale", StageRect);
                    break;
            }

            // the CTA (today's route) and the hint
            var go = SheetData.Spec(spec, "go");
            var gr = RJ.R(go, "rect");
            var plate = new Rect(gr.x - 1, gr.y - 1, gr.width + 4, gr.height + 6);
            var cta = PlateButton(c, "Cta", RJ.D(chrome, "go"), RJ.D(chrome, "go_hover"), plate, StageRect, null, null, OnCta);
            var title = SheetDraw.Label(cta.transform, "Title", go, CtaLabel(Id), plate);
            if (title != null) title.alignment = TextAnchor.MiddleCenter;
            Label(c, "Hint", SheetData.Spec(spec, "hint"), Hint, StageRect);
        }

        void OnCta()
        {
            if (Closing) return;
            var go = _cta;   // today's handler plays its own select
            Close();
            go?.Invoke();
        }

        void StripCards(RectTransform c, List<int> ids, float top, Rect strip)
        {
            if (ids == null || ids.Count == 0) return;
            var db = CardDatabase.Instance != null ? CardDatabase.Instance : CardDatabase.Load();
            if (db == null) return;
            var land = Lay == MenuLayoutKind.Landscape;
            var cw = land ? 80f : 116f;
            var ch = Mathf.Round(cw * 614f / 421f);
            var gap = 16f;
            var sprites = new List<Sprite>();
            foreach (var id in ids)
            {
                Sprite sp = null;
                try { sp = db.GetArt(id); }
                catch { sp = null; }
                if (sp != null) sprites.Add(sp);
            }

            var w = sprites.Count * cw + (sprites.Count - 1) * gap;
            var x = strip.center.x - w / 2;
            for (var i = 0; i < sprites.Count; i++)
            {
                var r = new Rect(x + i * (cw + gap), top, cw, ch);
                var shadow = SheetDraw.Img(c, "CardInk" + i, UiFoundation.WhiteSprite(), new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), StageRect);
                if (shadow != null) shadow.color = new Color(0, 0, 0, .95f);
                var img = SheetDraw.Img(c, "Card" + i, sprites[i], r, StageRect);
                if (img == null) continue;
                img.preserveAspect = false;
                var rot = (i - (sprites.Count - 1) / 2f) * 2.2f;
                img.rectTransform.localRotation = Quaternion.Euler(0, 0, -rot);
                if (shadow != null) shadow.rectTransform.localRotation = img.rectTransform.localRotation;
            }
        }

        void StripSetEnergy(RectTransform c, List<object> spec, Dictionary<string, object> chrome)
        {
            // the one real item: the wallet's Set Energy (Starchips / Locators / the pack: no source, §7)
            var ic = RJ.D(chrome, "icon_se");
            var icr = RJ.R(ic, "rect");
            Dictionary<string, object> b = null, i = null;
            foreach (var o in spec ?? new List<object>())
                if (o is Dictionary<string, object> d && RJ.S(d, "text") == "Set Energy" && RJ.S(d, "tag") == "i") i = d;
            foreach (var o in spec ?? new List<object>())
                if (o is Dictionary<string, object> d && RJ.S(d, "tag") == "b" && RJ.R(d, "rect").y > icr.y) { b = d; break; }
            var cx = SheetData.Frame(RJ.D(RJ.D(J, "recess"), "F")).center.x;
            var dx = cx - icr.center.x;
            SheetDraw.PartAt(c, "SeIcon", ic, new Rect(icr.x + dx, icr.y, icr.width, icr.height), StageRect);
            if (b != null)
            {
                var s = SheetData.Slot(b);
                var r = RJ.R(b, "rect");
                s.Rect = new Rect(cx - 120, r.y, 240, r.height);
                s.Align = "center";
                TextFx.Label(c, "SeValue", s, LedgerData.SetEnergyTotal().ToString("N0", CultureInfo.InvariantCulture), StageRect);
            }

            if (i != null)
            {
                var s = SheetData.Slot(i);
                var r = RJ.R(i, "rect");
                s.Rect = new Rect(cx - 120, r.y, 240, r.height);
                s.Align = "center";
                TextFx.Label(c, "SeLabel", s, "Set Energy", StageRect);
            }
        }

        void StripGates(RectTransform c, List<object> spec, Dictionary<string, object> chrome, MenuV6Snapshot snap, float top)
        {
            var gates = LedgerData.Gates(snap);
            if (gates.Count == 0) return;
            var land = Lay == MenuLayoutKind.Landscape;
            float w = land ? 118 : 150, h = land ? 100 : 130, gap = 12;
            var cx = SheetData.Frame(RJ.D(RJ.D(J, "recess"), "F")).center.x;
            var total = gates.Count * w + (gates.Count - 1) * gap;
            var x0 = cx - total / 2;
            // the chips' own text specs (b: the number, i: the title) by state, from the mockup's chips 1 (done), 3 (now), 4 (ahead)
            var bs = new List<Dictionary<string, object>>();
            var its = new List<Dictionary<string, object>>();
            foreach (var o in spec ?? new List<object>())
            {
                if (o is not Dictionary<string, object> d || RJ.R(d, "rect").y < top - 4) continue;
                if (RJ.S(d, "tag") == "b") bs.Add(d);
                if (RJ.S(d, "tag") == "i") its.Add(d);
            }

            for (var g = 0; g < gates.Count; g++)
            {
                var (title, state) = gates[g];
                var box = new Rect(x0 + g * (w + gap), top, w, h);
                var key = state == 2 ? "ch_done" : state == 1 ? "ch_now" : "ch_plain";
                var part = RJ.D(chrome, key);
                var pr = RJ.R(part, "rect");
                // the chip sprite's margin round its box, from the mockup's own chip (done / plain 7 px, now 22 px)
                var m = state == 1 ? 22f : 7f;
                SheetDraw.PartAt(c, "Chip" + g, part, new Rect(box.x - m, box.y - m, box.width + 2 * m + (pr.width - w - 2 * m), box.height + 2 * m + (pr.height - h - 2 * m)), StageRect, sliced: true);
                var si = state == 2 ? 0 : state == 1 ? 2 : 3;
                if (bs.Count > si)
                {
                    var s = SheetData.Slot(bs[si]);
                    var r = RJ.R(bs[si], "rect");
                    s.Rect = new Rect(box.x, r.y, box.width, r.height);
                    s.Align = "center";
                    TextFx.Label(c, "ChipN" + g, s, (g + 1).ToString(CultureInfo.InvariantCulture), StageRect);
                }

                if (its.Count > si)
                {
                    var s = SheetData.Slot(its[si]);
                    var r = RJ.R(its[si], "rect");
                    s.Rect = new Rect(box.x + 4, r.y, box.width - 8, r.height);
                    s.Align = "center";
                    var t = TextFx.Label(c, "ChipT" + g, s, title, StageRect);
                    if (t != null) TextFx.FitWidth(t, box.width - 8, 10);
                }
            }
        }

        protected override void Draw(double u, double v, double t)
        {
            if (_glow == null) return;
            _glow.Clear();
            var k = SheetMath.K(Kind, u, v).Content;
            LockFx.Spr(_glow, MenuV6Glow.Kind.Gold, _well.x, _well.y, _mr * 2.4, (.22 + .08 * Math.Sin(t * 1.6)) * k);
            _glow.Commit();
        }
    }

    // ============================================================================================================== QUIT

    /// <summary>
    /// QUIT folds the disk (BUILD_PLAN D14, §11 F15; SPEC §8.6): "Fold your Spirit Dueler?" over the sign-out line, STAY / FOLD.
    /// No mockup page exists: the frame, type and plates are the page's own (the recess false door at a decided rect, the
    /// Ring sheet's header type, #sht .txt, the .sig STAY plate, the .quit carnelian plate; sheets_&lt;layout&gt;.json quit), it
    /// runs the recess Puzzle-lock, and the torch gutters as the door closes (the page's omen factor .55). FOLD = Logout +
    /// GoBoot as before; STAY closes.
    /// </summary>
    public sealed class QuitSheet : StoneSheet
    {
        public const string Title = "Fold your Spirit Dueler?";
        public const string Body = "Your progress is saved. The Duel Disk powers down and you return to the gate.";
        Action _fold;

        public static QuitSheet Build(MenuV6Stage stone, RectTransform host, Image dim, Action onFold)
        {
            if (stone == null || host == null) return null;
            var s = host.gameObject.AddComponent<QuitSheet>();
            if (!s.Init(stone, host, dim, SheetKind.Quit))
            {
                Destroy(s);
                return null;
            }

            s._fold = onFold;
            s.Frame = FalseDoorFrame.OverStone(s.Space, SheetKind.Quit, s.Lay, "quit");
            var c = s.MakeContent();
            var q = RJ.D(s.J, "quit");
            var texts = RJ.L(RJ.D(s.J, "texts"), "quit");
            s.Label(c, "Title", SheetData.Spec(texts, "h"), Title, s.StageRect);
            s.Label(c, "Body", SheetData.Spec(texts, "txt"), Body, s.StageRect);
            var parts = RJ.D(q, "parts");
            var stay = RJ.D(parts, "stay");
            var fold = RJ.D(parts, "fold");
            var bstay = s.PlateButton(c, "Stay", stay, RJ.D(parts, "stay_hover"), RJ.R(stay, "rect"), s.StageRect, null, null, s.OnStay);
            var tstay = SheetDraw.Label(bstay.transform, "Title", SheetData.Spec(texts, "stay"), "STAY", RJ.R(stay, "rect"));
            if (tstay != null) tstay.alignment = TextAnchor.MiddleCenter;
            var bfold = s.PlateButton(c, "Fold", fold, RJ.D(parts, "fold_hover"), RJ.R(fold, "rect"), s.StageRect, null, null, s.OnFold);
            var tfold = SheetDraw.Label(bfold.transform, "Title", SheetData.Spec(texts, "t"), "FOLD", RJ.R(fold, "rect"));
            if (tfold != null) tfold.alignment = TextAnchor.MiddleCenter;
            s.BuildLock(RJ.V(RJ.D(q, "geo"), "crest"), RJ.N(RJ.D(q, "geo"), "cs", .5));
            if (!Application.isPlaying) s.ShowAt(10);
            else s.Step();
            return s;
        }

        void OnStay()
        {
            if (Closing) return;
            FreeUiKit.PlaySelect();
            Close();
        }

        void OnFold()
        {
            if (Closing) return;
            FreeUiKit.PlayConfirm();
            var f = _fold;
            Close();
            f?.Invoke();
        }

        protected override void Draw(double u, double v, double t)
        {
            // the torch gutters as the door closes: strength × (1 − .55 win(u, 0, .12, .28, .4)) (decided from menu.js:668)
            Stone.SetTorchGutter(1 - .55 * MenuV6Math.Win(u, 0, .12, .28, .4));
        }
    }
}
