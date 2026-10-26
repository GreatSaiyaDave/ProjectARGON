using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The v6.3 offering hall (BUILD_PLAN §6 P4): the deck builder's frame and its fixed art, in the page's stage px
    /// (deck_&lt;layout&gt;.json) fitted into the overlay canvas (uniform contain; the hall's own wall fills a non-16:9 margin).
    /// <list type="bullet">
    /// <item>the frame contract of every sheet: the root "PhoneMenu_" + title, a direct child "BodyWell" holding "BodyHost",
    /// a Button named "Close" (the back chip: the deck screen routes it through RequestLeaveEditor), the title and subtitle
    /// Texts;</item>
    /// <item>the room bake, Nut, the deck's live cracks (P2's CrackLayer on cracks_deck_&lt;layout&gt;.json, the deck's bolt /
    /// stutter seeds), the hall's 24 gold motes and the placeholder strip;</item>
    /// <item>the stage-space layers the deck screen builds into: Body (its widgets), Fx (flights, bursts, ripples) and Over
    /// (the DRAW 5 sheet).</item>
    /// </list>
    /// The clock is the menu clock (<see cref="MenuV6Clock"/>), read relative to the open (never restarted: the Stone behind the
    /// hall keeps its own timeline). Nothing here changes deck data; nothing here is a HubChrome painter (the deck keeps
    /// SanctuaryChrome.Paint off).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckHall : MonoBehaviour
    {
        public DeckHallSpec Spec { get; private set; }
        public MenuLayoutKind Kind => Spec != null ? Spec.Kind : MenuLayoutKind.Landscape;
        public Rect StageRect => Spec != null ? Spec.StageRect : new Rect(0, 0, 1920, 1080);

        public RectTransform Root { get; private set; }
        public RectTransform Stage { get; private set; }
        public RectTransform BodyWell { get; private set; }
        public RectTransform BodyHost { get; private set; }
        public RectTransform Fx { get; private set; }
        public RectTransform Over { get; private set; }
        public Button Close { get; private set; }
        public Text Title { get; private set; }
        public Text Subtitle { get; private set; }
        public CrackLayer Cracks { get; private set; }

        /// <summary>P5: the hall's safe-area bands (portrait: the header, the thumb band).</summary>
        public SafeArea Safe { get; internal set; }

        /// <summary>Menu seconds at the open; <see cref="Now"/> is the hall's own time since then.</summary>
        public double T0 { get; private set; }

        public double Now => Math.Max(0, MenuV6Clock.Now - T0);

        readonly List<Action<double>> _ticks = new();
        readonly List<Image> _motes = new();
        Vector2 _lastSize = new(-1, -1);
        double _lastT = double.NaN;
        List<object> _moteList;

        /// <summary>Null when the hall can be built for the current layout plan; else why not (the legacy builder is kept).</summary>
        public static string WhyNot() => DeckHallSpec.WhyNot(MenuAspect.Current);

        /// <summary>Run every frame with the hall time (rigs, flights, the entrance); a callback that throws is dropped once.</summary>
        public void OnTick(Action<double> f)
        {
            if (f != null) _ticks.Add(f);
        }

        /// <summary>Captures and tests: hold the hall at <paramref name="t"/> seconds since the open.</summary>
        public void FreezeAt(double t)
        {
            MenuV6Clock.Freeze((float)(T0 + Math.Max(0, t)));
            Tick(true);
        }

        // ------------------------------------------------------------------ build

        /// <summary>The deck builder's frame (DualMenuPresenter.BuildFrame, dense). Null when <see cref="WhyNot"/> says so.</summary>
        public static DeckHall Build(Transform modalHost, string title, string subtitle, Action onClose)
        {
            if (modalHost == null || WhyNot() != null) return null;
            var spec = DeckHallSpec.Load(MenuAspect.Current);
            if (spec == null) return null;
            var stageRect = spec.StageRect;

            var rootGo = new GameObject("PhoneMenu_" + title, typeof(RectTransform), typeof(Image));
            rootGo.transform.SetParent(modalHost, false);
            var root = rootGo.GetComponent<RectTransform>();
            UiTheme.Stretch(root);
            var bg = rootGo.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = WrldzAnimePalette.RoomBase;
            bg.raycastTarget = true;   // the hall is the screen: nothing behind it takes a tap
            var hall = rootGo.AddComponent<DeckHall>();
            hall.Spec = spec;
            hall.Root = root;
            hall.T0 = MenuV6Clock.Now;
            var hallD = spec.D("hall");

            // the wall behind a non-16:9 hall (cover, so its bricks are never squashed)
            var wallSp = spec.Sprite(RJ.D(hallD, "wall"));
            if (wallSp != null)
            {
                var wall = new GameObject("Wall", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
                wall.transform.SetParent(root, false);
                var wi = wall.GetComponent<Image>();
                wi.sprite = wallSp;
                wi.raycastTarget = false;
                var fit = wall.GetComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = stageRect.width / Mathf.Max(1f, stageRect.height);
            }

            hall.Stage = StageNode(root, "Stage", spec.Stage);
            hall.Fit();
            SheetDraw.Img(hall.Stage, "Room", spec.Sprite(RJ.D(hallD, "room")), stageRect, stageRect);
            SheetDraw.Img(hall.Stage, "Nut", spec.Sprite(RJ.D(hallD, "nut")), stageRect, stageRect);
            hall.BuildCracks();
            hall.BuildMotes(RJ.L(RJ.D(hallD, "motes"), "list"));

            hall.BodyWell = StageNode(root, "BodyWell", spec.Stage);
            hall.BodyWell.gameObject.AddComponent<StageFollow>().Source = hall.Stage;
            hall.BodyWell.localScale = hall.Stage.localScale;
            hall.BodyHost = SheetDraw.Stretch(hall.BodyWell, "BodyHost");
            hall.Fx = StageNode(root, "Fx", spec.Stage);
            hall.Fx.gameObject.AddComponent<StageFollow>().Source = hall.Stage;
            hall.Fx.localScale = hall.Stage.localScale;
            hall.Over = StageNode(root, "Over", spec.Stage);
            hall.Over.gameObject.AddComponent<StageFollow>().Source = hall.Stage;
            hall.Over.localScale = hall.Stage.localScale;

            hall.BuildHeader(title, subtitle, onClose);
            hall.BuildPlaceholder();
            hall.Tick(true);
            Debug.Log("[WRLDZ MenuV6] deck hall · " + spec.Lay + " · PhoneMenu_" + title);
            return hall;
        }

        static RectTransform StageNode(Transform parent, string name, Vector2 size)
        {
            var n = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            n.SetParent(parent, false);
            n.anchorMin = n.anchorMax = new Vector2(.5f, .5f);
            n.pivot = new Vector2(.5f, .5f);
            n.sizeDelta = size;
            n.anchoredPosition = Vector2.zero;
            return n;
        }

        void BuildCracks()
        {
            try
            {
                var set = CrackData.Load("deck", Kind);
                if (set == null || set.Nets.Count == 0) return;
                // the deck's bolts (921) and stutter (922); at rest in edit mode (the stills' grown breaks)
                var drv = new MenuCrackDriver(set.Nets, false, deck: true) { Rest = !Application.isPlaying };
                var host = SheetDraw.Stretch(Stage, "CracksDeck");
                Cracks = CrackLayer.Create(host, "CracksDeck_live", StageRect, set, set.Nets, drv, CrackClock);
                if (Cracks != null) Cracks.Render(CrackClock());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] deck cracks skipped: " + ex.Message);
                Cracks = null;
            }
        }

        /// <summary>The deck's crack clock: the hall time; edit mode and frozen captures draw the grown rest (≥ 2 s).</summary>
        double CrackClock() => Application.isPlaying || MenuV6Clock.Frozen ? Now : Math.Max(Now, 2.0);

        void BuildMotes(List<object> list)
        {
            _moteList = list;
            var spr = MenuV6Art.Sprite("WRLDZ/MenuV6/Rigs/Common/spr_gold");
            if (list == null || spr == null) return;
            var host = SheetDraw.Stretch(Stage, "Motes");
            foreach (var _ in list)
            {
                var img = SheetDraw.Img(host, "Mote", spr, new Rect(0, 0, 8, 8), StageRect);
                if (img != null) _motes.Add(img);
            }
        }

        /// <summary>The hall's motes (deck.js:192): drawSpr(gold, x0 + 8 sin(.5 t + i), y0 − 9 age, size,
        /// .4 win(age, 0, .8, life − 1.2, life)), age = (t + off) mod life.</summary>
        void TickMotes(double t)
        {
            if (_moteList == null) return;
            for (var i = 0; i < _motes.Count && i < _moteList.Count; i++)
            {
                var m = _moteList[i];
                double life = RJ.N(m, "life", 6), off = RJ.N(m, "off"), x0 = RJ.N(m, "x0"), y0 = RJ.N(m, "y0"), sz = RJ.N(m, "size", 8);
                var age = ((t + off) % life + life) % life;
                var x = x0 + 8 * Math.Sin(.5 * t + i);
                var y = y0 - 9 * age;
                var a = .4 * MenuV6Math.Win(age, 0, .8, life - 1.2, life);
                var img = _motes[i];
                MenuV6Layout.Place(img.rectTransform, new Rect((float)(x - sz / 2), (float)(y - sz / 2), (float)sz, (float)sz), StageRect);
                SheetDraw.SetA(img, MenuV6Linear.Alpha(a));
            }
        }

        // ------------------------------------------------------------------ the header that stays (Close, the title)

        void BuildHeader(string title, string subtitle, Action onClose)
        {
            var s = Spec;
            var backEl = s.El("back");
            if (backEl.width <= 0) backEl = s.ChromeEl("back");
            var closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(BodyWell, false);
            var hit = closeGo.GetComponent<Image>();
            hit.sprite = UiFoundation.WhiteSprite();
            hit.color = new Color(1, 1, 1, 0);
            hit.raycastTarget = true;
            // the page's round back chip, hit Ø + 16 px
            MenuV6Layout.Place(hit.rectTransform, new Rect(backEl.x - 8, backEl.y - 8, backEl.width + 16, backEl.height + 16), StageRect);
            var hitRect = new Rect(backEl.x - 8, backEl.y - 8, backEl.width + 16, backEl.height + 16);
            s.ChromeImg(closeGo.transform, "Chip", "back", backEl, hitRect);
            var glyph = s.Chrome("back_glyph");
            if (glyph != null)
                SheetDraw.Img(closeGo.transform, "Glyph", s.Sprite(glyph), RJ.R(glyph, "shotRect"), hitRect);
            var btn = closeGo.GetComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                onClose?.Invoke();
            });
            Close = btn;

            Title = DeckLabel(BodyWell, "Title", s.Text("rest", "DECK", "div.dt", "div.t"), title ?? "DECK");
            Subtitle = DeckLabel(BodyWell, "Subtitle", s.Text("rest", null, "div.dt", "div.s"), "play deck box · build");
            _ = subtitle;   // the frame's how-to-leave line: the hall's own sub-line is the page's (the back chip is the way out)
        }

        void BuildPlaceholder()
        {
            var ph = Spec.D("ph5");
            if (ph == null) return;
            var r = RJ.R(ph, "rect");
            var plate = new GameObject("Placeholder", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(BodyWell, false);
            var img = plate.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = DeckHallSpec.ColorOf(RJ.S(ph, "bgc"), new Color(12 / 255f, 10 / 255f, 16 / 255f, .82f));
            img.raycastTarget = false;
            MenuV6Layout.Place(img.rectTransform, r, StageRect);
            var slot = DeckHallSpec.Slot(ph);
            if (slot != null)
            {
                var lines = RJ.L(ph, "lines");
                slot.Rect = lines != null && lines.Count > 0 ? DeckHallSpec.RectOf(lines[0]) : r;
                slot.Upper = RJ.S(ph, "textTransform") == "uppercase";
                slot.NoWrap = true;
                var lab = TextFx.Label(BodyWell, "PlaceholderText", slot, RJ.S(ph, "own") ?? RJ.S(ph, "text"), StageRect);
                if (lab != null) lab.raycastTarget = false;
            }
        }

        // ------------------------------------------------------------------ helpers for the deck screen

        /// <summary>A Text in stage px from a recorded spec (null spec = nothing).</summary>
        public Text DeckLabel(Transform parent, string name, MenuV6Layout.TextSlot slot, string shown, Rect? parentRect = null)
        {
            if (slot == null || parent == null) return null;
            return TextFx.Label(parent, name, slot, shown ?? "", parentRect ?? StageRect);
        }

        /// <summary>A stage-px node under <paramref name="parent"/> (which covers <paramref name="parentRect"/>).</summary>
        public static RectTransform Node(Transform parent, string name, Rect r, Rect parentRect)
        {
            var n = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            n.SetParent(parent, false);
            MenuV6Layout.Place(n, r, parentRect);
            return n;
        }

        /// <summary>A tappable stage-px element: a transparent hit Image (raycast) + Button, chrome added by the caller.</summary>
        public static Button Hit(Transform parent, string name, Rect r, Rect parentRect, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(1, 1, 1, 0);
            img.raycastTarget = true;
            MenuV6Layout.Place(img.rectTransform, r, parentRect);
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            // P5 (SPEC §9.4, portrait_p5.json taps): a portrait target under 56 px gets an invisible 56 px pad (visuals unchanged)
            if (MenuAspect.Current == MenuLayoutKind.Portrait) TapPad.Ensure(img.rectTransform, r);
            return b;
        }

        // ------------------------------------------------------------------ the frame

        public void Fit()
        {
            if (Stage == null || Root == null || Spec == null) return;
            _lastSize = Root.rect.size;
            var k = MenuAspect.StageScale(Root, Spec.Stage);
            Stage.localScale = new Vector3(k, k, 1f);
        }

        void OnRectTransformDimensionsChange()
        {
            if (Stage != null) Fit();
        }

        void LateUpdate()
        {
            if (Root != null && Root.rect.size != _lastSize) Fit();
            Tick(false);
        }

        void Tick(bool force)
        {
            var t = Now;
            if (!force && t == _lastT) return;
            _lastT = t;
            TickMotes(t);
            for (var i = _ticks.Count - 1; i >= 0; i--)
            {
                try { _ticks[i](t); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ MenuV6] deck hall rig dropped: " + ex.Message);
                    _ticks.RemoveAt(i);
                }
            }
        }
    }
}
