using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The deck hall's rising sheet (BUILD_PLAN §6 P4: the inspect sheet, DRAW 5; deck.js:161-163, :221-247): it rises from the
    /// thumb band over the right column, non-modal. The Puzzle's 8 pieces leave the relic box's mouth, arc up to the sheet's
    /// clasp and lock whole (eye .6 + .1 sin 2t, its shadow); the header band unfurls from the clasp (hw = (w / 2 + 2) h) and the
    /// body drops open (bh = lerp(52, H + 2, b)) through a clip. On the close the body folds, the clasp bursts and the pieces
    /// fly home into the box (the lock flash at its mouth). Callers build their content into <see cref="Content"/> in stage
    /// px; the sheet's own chrome (chrome/insp, Tiled band) sits under it.
    /// </summary>
    public sealed class DeckSheet
    {
        public RectTransform Root { get; private set; }
        public RectTransform Content { get; private set; }
        public bool IsOpen => !double.IsNaN(_openAt) && double.IsNaN(_closeAt);
        public bool Visible => Root != null && Root.gameObject.activeSelf;

        DeckHall _hall;
        Rect _rect, _stage;
        RectTransform _clip;
        CanvasGroup _contentGroup;
        DeckClasp _clasp;
        double _openAt = double.NaN, _closeAt = double.NaN;
        public Action Closed;

        public static DeckSheet Build(DeckHall hall, Transform host, string name, Rect rect)
        {
            if (hall == null || host == null) return null;
            var s = new DeckSheet { _hall = hall, _rect = rect, _stage = hall.StageRect };
            s.Root = DeckHall.Node(host, name, hall.StageRect, hall.StageRect);
            s._clip = DeckHall.Node(s.Root, "Clip", rect, hall.StageRect);
            s._clip.gameObject.AddComponent<RectMask2D>();
            var inner = DeckHall.Node(s._clip, "Sheet", hall.StageRect, rect);
            // the sheet plate takes the taps inside it (non-modal: nothing outside is blocked)
            var plate = hall.Spec.ChromeImg(inner, "Plate", "insp", rect, hall.StageRect);
            if (plate == null)
            {
                plate = SheetDraw.Img(inner, "Plate", UiFoundation.WhiteSprite(), rect, hall.StageRect);
                if (plate != null) plate.color = new Color(.08f, .08f, .12f, .96f);
            }

            if (plate != null) plate.raycastTarget = true;
            s.Content = DeckHall.Node(inner, "Content", hall.StageRect, hall.StageRect);
            s._contentGroup = SheetDraw.Group(s.Content.gameObject);
            s._clasp = DeckClasp.Build(hall, s.Root);
            s.Root.gameObject.SetActive(false);
            s.Place(0, 0);
            hall.OnTick(s.Tick);
            return s;
        }

        public void Open()
        {
            if (Root == null) return;
            var t = _hall.Now;
            // a sheet already up stays up (a new card on the open inspect sheet only swaps the content)
            if (IsOpen) return;
            _openAt = t;
            _closeAt = double.NaN;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            if (!Application.isPlaying) Snap(true);
            else Tick(t);
        }

        public void Close()
        {
            if (Root == null || !Visible || !double.IsNaN(_closeAt)) return;
            _closeAt = _hall.Now;
            if (!Application.isPlaying) Snap(false);
        }

        /// <summary>Hide at once (a rebuild, the screen closing).</summary>
        public void Kill()
        {
            _openAt = _closeAt = double.NaN;
            if (Root != null) Root.gameObject.SetActive(false);
        }

        /// <summary>Edit mode / captures: the sheet at its rest (open) or gone.</summary>
        public void Snap(bool open)
        {
            if (open)
            {
                _openAt = _hall.Now - 10;
                _closeAt = double.NaN;
                Root.gameObject.SetActive(true);
                Tick(_hall.Now);
            }
            else Kill();
        }

        void Tick(double t)
        {
            if (Root == null || !Root.gameObject.activeSelf || double.IsNaN(_openAt)) return;
            var u = t - _openAt;
            var v = double.IsNaN(_closeAt) ? -1 : t - _closeAt;
            DeckHallMath.Lock(u, v, out var h, out var b);
            Place(h, b);
            SheetDraw.SetA(_contentGroup, MenuV6Linear.Fade(DeckHallMath.InspectContent(v)));
            _clasp?.Frame(u, v, t);
            if (v >= 0 && v > 1.45)
            {
                Kill();
                var c = Closed;
                c?.Invoke();
            }
        }

        void Place(double h, double b)
        {
            if (_clip == null) return;
            var w = _rect.width;
            var hw = (w / 2 + 2) * h;
            var bh = Mathf.Lerp(52f, _rect.height + 2f, (float)b);
            var clipR = h <= .001
                ? new Rect(_rect.center.x, _rect.y, 0, 0)
                : new Rect((float)(_rect.center.x - hw), _rect.y - 2f, (float)(2 * hw), bh);
            MenuV6Layout.Place(_clip, clipR, _stage);
            var inner = _clip.childCount > 0 ? _clip.GetChild(0) as RectTransform : null;
            if (inner != null && clipR.width > 0 && clipR.height > 0) MenuV6Layout.Place(inner, _stage, clipR);
            _clip.gameObject.SetActive(clipR.width > .5f && clipR.height > .5f);
        }
    }

    /// <summary>
    /// The sheet's Puzzle clasp (deck_&lt;layout&gt;.json pz: PZR = new PuzzleRelic(84, …), A / emiB / shadow and the 8 pieces
    /// c / s / e): the pieces rise from the relic box's mouth (the lead piece 1 first, then .035 s apart, a quadratic arc 120
    /// px above), lock whole at K (scale sc), hold with the eye's breath and its shadow; on the close they part along their
    /// own directions (.2 s) and fall home into the box (eIn, .03 s apart), the lock flash at the mouth.
    /// </summary>
    public sealed class DeckClasp
    {
        sealed class Piece
        {
            public int I;
            public Vector2 Cb, Home, Dir;
            public Image C, S, E;
            public RectTransform Node;
        }

        readonly List<Piece> _p = new();
        Image _a, _emi, _shadow, _flash;
        RectTransform _whole;
        Rect _aRect, _shadowRect;
        Vector2 _mouth;
        float _scale = .66f, _S = 2f, _W = 225f, _H = 212f;

        public static DeckClasp Build(DeckHall hall, Transform host)
        {
            var pz = hall.Spec.D("pz");
            if (pz == null) return null;
            var c = new DeckClasp();
            c._scale = (float)RJ.N(pz, "sc", .66);
            c._S = (float)RJ.N(pz, "S", 2);
            c._W = (float)RJ.N(pz, "W", 225);
            c._H = (float)RJ.N(pz, "H", 212);
            c._mouth = RJ.V(pz, "boxMouth");
            var origin = RJ.V(pz, "origin");
            var wh = RJ.V(pz, "drawWH");
            c._aRect = new Rect(origin.x, origin.y, wh.x, wh.y);
            c._shadowRect = RJ.R(pz, "shadowRect");
            var parts = RJ.D(pz, "parts");
            var node = DeckHall.Node(host, "Clasp", hall.StageRect, hall.StageRect);
            c._shadow = SheetDraw.Img(node, "Shadow", MenuV6Art.Sprite(RJ.S(RJ.D(parts, "shadow"), "file")), c._shadowRect, hall.StageRect);
            c._whole = DeckHall.Node(node, "Whole", hall.StageRect, hall.StageRect);
            c._a = SheetDraw.Img(c._whole, "A", MenuV6Art.Sprite(RJ.S(RJ.D(parts, "A"), "file")), c._aRect, hall.StageRect);
            c._emi = SheetDraw.Img(c._whole, "EmiB", MenuV6Art.Sprite(RJ.S(RJ.D(parts, "emiB"), "file")), c._aRect, hall.StageRect);
            var size = new Vector2(c._W / c._S * c._scale, c._H / c._S * c._scale);
            foreach (var po in RJ.L(pz, "pieces") ?? new List<object>())
            {
                var p = new Piece
                {
                    I = (int)RJ.N(po, "i"), Cb = RJ.V(po, "cb"), Home = RJ.V(po, "home"), Dir = RJ.V(po, "dir")
                };
                p.Node = DeckHall.Node(node, "Piece" + p.I, new Rect(0, 0, size.x, size.y), hall.StageRect);
                p.Node.anchorMin = p.Node.anchorMax = new Vector2(0f, 1f);
                p.Node.sizeDelta = size;
                // the piece's cb point is its pivot (drawPieceAbs: translate(x, y) then the sprite at −cb / S · s)
                p.Node.pivot = new Vector2(p.Cb.x / c._W, 1f - p.Cb.y / c._H);
                var full = new Rect(0, 0, size.x, size.y);
                p.S = SheetDraw.Img(p.Node, "S", MenuV6Art.Sprite(RJ.S(po, "s")), full, full);
                p.C = SheetDraw.Img(p.Node, "C", MenuV6Art.Sprite(RJ.S(po, "c")), full, full);
                p.E = SheetDraw.Img(p.Node, "E", MenuV6Art.Sprite(RJ.S(po, "e")), full, full);
                c._p.Add(p);
            }

            c._flash = SheetDraw.Img(node, "LockFlash", MenuV6Art.Sprite("WRLDZ/MenuV6/Rigs/Common/spr_goldHot"),
                new Rect(c._mouth.x - 40, c._mouth.y - 40, 80, 80), hall.StageRect);
            c.Frame(-1, -1, 0);
            return c;
        }

        static double Seg(double t, double a, double b) => MenuV6Math.Seg(t, a, b);

        /// <summary>The clasp at u seconds since the open, v since the close (−1 = open), hall time t.</summary>
        public void Frame(double u, double v, double t)
        {
            var anyPiece = false;
            var whole = 0.0;
            for (var n = 0; n < _p.Count; n++)
            {
                var p = _p[n];
                // the lead piece (1) first at .05-.25, the rest .2 + .035 j .. .6 + .035 j
                var j = p.I == 1 ? -1 : (p.I < 1 ? p.I : p.I - 1);
                double a0 = j < 0 ? .05 : .2 + .035 * j, a1 = j < 0 ? .25 : .6 + .035 * j;
                var k = MenuV6Math.Out(Seg(u, a0, a1));
                Vector2 pos;
                double alpha, rot = 0;
                if (u < 0)
                {
                    alpha = 0;
                    pos = _mouth;
                }
                else if (v < 0)
                {
                    var mid = (_mouth + p.Home) * .5f + new Vector2(0, -120f);
                    var kk = (float)k;
                    pos = (1 - kk) * (1 - kk) * _mouth + 2 * (1 - kk) * kk * mid + kk * kk * p.Home;
                    alpha = MenuV6Math.SStep(0, .15, k);
                    rot = (1 - k) * (p.I % 2 == 0 ? .6 : -.6);
                }
                else
                {
                    // the close: part along dir (.76-.96), then fall home into the box (.86 + .03 i .. 1.18 + .03 i, eIn)
                    var sep = MenuV6Math.InOut(Seg(v, .76, .96)) * 14;
                    var f = MenuV6Math.In(Seg(v, .86 + .03 * p.I, 1.18 + .03 * p.I));
                    var parted = p.Home + p.Dir * (float)sep;
                    pos = Vector2.Lerp(parted, _mouth, (float)f);
                    alpha = v < .76 ? 0 : 1 - MenuV6Math.SStep(.85, 1, f);
                    rot = f * (p.I % 2 == 0 ? -.8 : .8);
                }

                var arrived = v < 0 && k >= 1;
                if (arrived) whole += 1.0 / Math.Max(1, _p.Count);
                var show = alpha > .002 && !(v < 0 && WholeShown(u));
                if (p.Node.gameObject.activeSelf != show) p.Node.gameObject.SetActive(show);
                if (!show) continue;
                anyPiece = true;
                p.Node.anchoredPosition = new Vector2(pos.x, -pos.y);
                p.Node.localRotation = Quaternion.Euler(0, 0, (float)(-rot * Mathf.Rad2Deg));
                SheetDraw.SetA(p.C, alpha);
                SheetDraw.SetA(p.S, alpha * .5);
                SheetDraw.SetA(p.E, alpha * .6);
            }

            var wholeOn = v < 0 ? WholeShown(u) : v < .76;
            if (_whole != null && _whole.gameObject.activeSelf != wholeOn) _whole.gameObject.SetActive(wholeOn);
            var eye = .6 + .1 * Math.Sin(2 * t);
            if (_emi != null) _emi.color = new Color(1, 1, 1, MenuV6Linear.Alpha(Math.Min(1, eye)));
            if (_shadow != null) _shadow.color = new Color(1, 1, 1, wholeOn ? MenuV6Linear.Shade(.5) : 0f);
            var fl = v < 0 ? 0 : .7 * MenuV6Math.SStep(1.15, 1.22, v) * (1 - MenuV6Math.SStep(1.22, 1.45, v));
            if (_flash != null) _flash.color = new Color(1, 1, 1, MenuV6Linear.Alpha(fl));
            _ = anyPiece;
            _ = whole;
        }

        static bool WholeShown(double u) => u >= .9;
    }
}
