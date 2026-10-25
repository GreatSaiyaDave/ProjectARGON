using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The Scale of Ma'at in the deck hall's header (BUILD_PLAN §6 P4 step 2; deck.js:255-268 drawScale): the page's own 1x
    /// canvas parts (deck_&lt;layout&gt;.json scale.parts: each a white sprite tinted with the mixed colour and its black ink),
    /// the post fixed, the beam turning about the pivot by the tilt, the pans / jar / feather riding the beam's ends upright,
    /// the blue glow over the feather while legal. At the three rest tilts (−.2 short of 40, 0 legal, +.2 over 60) the baked
    /// rest sprites draw (the page's raster); in between the tilt-0 parts turn / ride. The count is the page's odometer (the
    /// old number rolls up and out, 72 px), and the status line swaps with the old fading before the new
    /// (MAIN NEEDS 40 ↔ MAIN n · LEGAL; MAIN OVER 60 in the needs style, decided). Legality is TcgRules.MainDeckMin / Max.
    /// </summary>
    public sealed class MaatScale
    {
        sealed class Part
        {
            public string Tint;                 // "gold", "feather" or null (drawn as is)
            public int Side;                    // 0 post / beam (pivot), -1 left end, +1 right end
            public bool Beam;
            public Image Rest0, RestM, RestP;   // white (or colour) at tilt 0, -.2, +.2
            public Image Ink0, InkM, InkP;
            public Vector2 Anchor0;             // the part's anchor at tilt 0 (canvas px)
        }

        readonly List<Part> _parts = new();
        Image _glow;
        Vector2 _pivot, _panL, _panR, _glowC;
        float _glowSize = 60f;
        Rect _canvasRect;
        Color _gold0, _gold1, _fea0, _fea1;
        Text _countOld, _countNew, _of, _bad, _ok;
        RectTransform _roll;
        Color _cnt0 = new(240 / 255f, 217 / 255f, 168 / 255f), _cnt1 = new(200 / 255f, 242 / 255f, 207 / 255f);
        Shadow _cntDrop0, _cntDrop1;
        float _lh = 72f;

        int _min, _max;
        int _shown = -1, _from = -1;
        double _changedAt = double.NegativeInfinity, _tiltFrom, _tiltTo, _legalFrom, _legalTo;
        string _statusFrom = "", _statusTo = "";
        bool _badFrom, _badTo;

        public bool Ok => _parts.Count > 0;

        /// <summary>Build into <paramref name="host"/> (a stage-space node covering <paramref name="stageRect"/>).</summary>
        public static MaatScale Build(DeckHallSpec spec, DeckHall hall, Transform host, Rect stageRect, int min, int max, int count)
        {
            var sc = spec.D("scale");
            if (sc == null || host == null) return null;
            var m = new MaatScale { _min = min, _max = max };
            var cv = RJ.V(sc, "canvas");
            m._canvasRect = new Rect(0, 0, cv.x > 0 ? cv.x : 220, cv.y > 0 ? cv.y : 150);
            var at = RJ.R(sc, "rect");
            var node = DeckHall.Node(host, "MaatScale", at, stageRect);
            // the parts in canvas px: a child the canvas's size, scaled to the stage rect (the page's 220 → 176 css px)
            var canvas = new GameObject("Canvas", typeof(RectTransform)).GetComponent<RectTransform>();
            canvas.SetParent(node, false);
            canvas.anchorMin = canvas.anchorMax = canvas.pivot = new Vector2(0f, 1f);
            canvas.sizeDelta = m._canvasRect.size;
            canvas.anchoredPosition = Vector2.zero;
            var k = at.width / Mathf.Max(1f, m._canvasRect.width);
            canvas.localScale = new Vector3(k, k, 1f);
            m._pivot = RJ.V(sc, "pivot");
            m._panL = RJ.V(sc, "panL");
            m._panR = RJ.V(sc, "panR");
            var cols = RJ.D(sc, "colours");
            var gold = RJ.L(cols, "gold");
            var fea = RJ.L(cols, "feather");
            m._gold0 = DeckHallSpec.ColorOf(gold != null && gold.Count > 0 ? gold[0] as string : null, WrldzAnimePalette.Gold);
            m._gold1 = DeckHallSpec.ColorOf(gold != null && gold.Count > 1 ? gold[1] as string : null, WrldzAnimePalette.GoldLit);
            m._fea0 = DeckHallSpec.ColorOf(fea != null && fea.Count > 0 ? fea[0] as string : null, WrldzAnimePalette.Gyp);
            m._fea1 = DeckHallSpec.ColorOf(fea != null && fea.Count > 1 ? fea[1] as string : null, WrldzAnimePalette.WadjText);
            var parts = RJ.D(sc, "parts");
            var inks = new GameObject("Ink", typeof(RectTransform)).GetComponent<RectTransform>();
            inks.SetParent(canvas, false);
            UiTheme.Stretch(inks);
            var faces = new GameObject("Parts", typeof(RectTransform)).GetComponent<RectTransform>();
            faces.SetParent(canvas, false);
            UiTheme.Stretch(faces);
            foreach (var (id, side, beam) in new[]
                     {
                         ("post", 0, false), ("beam", 0, true), ("panL", -1, false), ("jar", -1, false), ("panR", 1, false),
                         ("feather", 1, false)
                     })
            {
                var p0 = RJ.D(parts, id);
                if (p0 == null) continue;
                var part = new Part { Tint = RJ.S(p0, "tint"), Side = side, Beam = beam };
                Image Face(Dictionary<string, object> p) =>
                    p == null ? null : m.Img(faces, id, RJ.D(p, "white") ?? RJ.D(p, "colour"));
                Image Ink(Dictionary<string, object> p) => p == null ? null : m.Img(inks, id + "_ink", RJ.D(p, "ink"));
                part.Rest0 = Face(p0);
                part.Ink0 = Ink(p0);
                if (id != "post")
                {
                    part.RestM = Face(RJ.D(parts, id + "_m"));
                    part.InkM = Ink(RJ.D(parts, id + "_m"));
                    part.RestP = Face(RJ.D(parts, id + "_p"));
                    part.InkP = Ink(RJ.D(parts, id + "_p"));
                }

                part.Anchor0 = side < 0 ? m._pivot + m._panL : side > 0 ? m._pivot + m._panR : m._pivot;
                foreach (var img in new[] { part.Rest0, part.Ink0 })
                {
                    if (img == null) continue;
                    // turn / ride about the part's own anchor at tilt 0
                    var cr = m.CanvasRectOf(img);
                    img.rectTransform.pivot = new Vector2((part.Anchor0.x - cr.x) / Mathf.Max(1f, cr.width),
                        1f - (part.Anchor0.y - cr.y) / Mathf.Max(1f, cr.height));
                }

                m._parts.Add(part);
            }

            var glow = RJ.D(sc, "glow");
            m._glowC = RJ.V(glow, "centre");
            m._glowSize = (float)RJ.N(glow, "size", 60);
            var glowSp = MenuV6Art.Sprite(RJ.S(sc, "glowSprite") ?? "WRLDZ/MenuV6/Rigs/Common/spr_blue");
            if (glowSp != null)
            {
                var g = new GameObject("Glow", typeof(RectTransform), typeof(Image));
                g.transform.SetParent(faces, false);
                m._glow = g.GetComponent<Image>();
                m._glow.sprite = glowSp;
                m._glow.raycastTarget = false;
            }

            m.BuildCount(spec, hall, host, stageRect);
            m.Snap(count);
            return m;
        }

        readonly Dictionary<Image, Rect> _cr = new();

        Rect CanvasRectOf(Image img) => _cr.TryGetValue(img, out var r) ? r : Rect.zero;

        Image Img(Transform parent, string name, Dictionary<string, object> p)
        {
            if (p == null) return null;
            var sp = MenuV6Art.Sprite(RJ.S(p, "file"));
            if (sp == null) return null;
            var a = RJ.L(p, "rect");
            if (a == null || a.Count < 4) return null;
            var r = new Rect((float)RJ.At(a, 0), (float)RJ.At(a, 1), (float)(RJ.At(a, 2) - RJ.At(a, 0)), (float)(RJ.At(a, 3) - RJ.At(a, 1)));
            var img = SheetDraw.Img(parent, name, sp, r, _canvasRect);
            if (img != null) _cr[img] = r;
            return img;
        }

        void BuildCount(DeckHallSpec spec, DeckHall hall, Transform host, Rect stageRect)
        {
            var nSlot = spec.Text("rest", null, "div.cnt", "span.d");
            var ofSlot = spec.Text("rest", "/", "div.cnt", "> i");
            var badSlot = spec.Text("rest", null, "span.bad");
            var okSlot = spec.Text("rest", null, "span.ok");
            if (nSlot != null)
            {
                // the odometer: a clip at the count's rect, the old number above the new one (one line height apart)
                var clipR = nSlot.Rect;
                var clip = DeckHall.Node(host, "Count", clipR, stageRect);
                clip.gameObject.AddComponent<RectMask2D>();
                _roll = DeckHall.Node(clip, "Roll", new Rect(clipR.x, clipR.y, clipR.width, clipR.height), clipR);
                _lh = nSlot.LineHeight > 0 ? nSlot.LineHeight : 72f;
                var oldSlot = DeckHallSpec.At(nSlot, new Rect(clipR.x, clipR.y, clipR.width, clipR.height));
                var newSlot = DeckHallSpec.At(nSlot, new Rect(clipR.x, clipR.y + _lh, clipR.width, clipR.height));
                _countOld = TextFx.Label(_roll, "Old", oldSlot, "", clipR);
                _countNew = TextFx.Label(_roll, "New", newSlot, "", clipR);
                _cnt0 = nSlot.Color;
                _cntDrop0 = _countOld != null ? _countOld.GetComponent<Shadow>() : null;
                _cntDrop1 = _countNew != null ? _countNew.GetComponent<Shadow>() : null;
            }

            _of = hall.DeckLabel(host, "Of", ofSlot, "/ " + _min, stageRect);
            _bad = hall.DeckLabel(host, "StatusBad", badSlot, "", stageRect);
            _ok = hall.DeckLabel(host, "StatusOk", okSlot, "", stageRect);
        }

        string StatusFor(int n, out bool bad)
        {
            bad = n < _min || n > _max;
            if (n < _min) return "MAIN NEEDS " + _min;
            if (n > _max) return "MAIN OVER " + _max;
            return "MAIN " + n + " · LEGAL";
        }

        double LegalOf(int n) => n >= _min && n <= _max ? 1 : 0;

        /// <summary>Show <paramref name="n"/> at rest (the open, a deck switch: no roll).</summary>
        public void Snap(int n)
        {
            _shown = _from = n;
            _tiltFrom = _tiltTo = DeckHallMath.RestTilt(n, _min, _max);
            _legalFrom = _legalTo = LegalOf(n);
            _statusFrom = _statusTo = StatusFor(n, out _badTo);
            _badFrom = _badTo;
            _changedAt = double.NegativeInfinity;
            if (_countOld != null) _countOld.text = n.ToString();
            if (_countNew != null) _countNew.text = n.ToString();
            Apply(1e6, 0);
        }

        /// <summary>The Main count changed to <paramref name="n"/> at hall time <paramref name="t"/> (a +1 lands, a −1, a drop):
        /// the odometer rolls, the beam levels over .75 s, the status swaps.</summary>
        public void Set(int n, double t)
        {
            if (n == _shown) return;
            var u = t - _changedAt;
            // a change mid-roll starts from where the beam is now
            _tiltFrom = u < 1 ? DeckHallMath.Tilt(_tiltFrom, _tiltTo, u) : _tiltTo;
            _legalFrom = u < 1 ? _legalFrom + (_legalTo - _legalFrom) * DeckHallMath.LegalK(u) : _legalTo;
            _from = _shown;
            _shown = n;
            _tiltTo = DeckHallMath.RestTilt(n, _min, _max);
            _legalTo = LegalOf(n);
            _statusFrom = _statusTo;
            _badFrom = _badTo;
            _statusTo = StatusFor(n, out _badTo);
            _changedAt = t;
            if (_countOld != null) _countOld.text = _from.ToString();
            if (_countNew != null) _countNew.text = n.ToString();
        }

        public void Tick(double t) => Apply(t - _changedAt, t);

        void Apply(double u, double t)
        {
            var tilt = DeckHallMath.Tilt(_tiltFrom, _tiltTo, u);
            var legal = _legalFrom + (_legalTo - _legalFrom) * DeckHallMath.LegalK(u);
            DrawScale(tilt, legal, t);
            // the odometer
            var roll = _from == _shown ? 1 : DeckHallMath.Roll(u);
            if (_roll != null) _roll.anchoredPosition = new Vector2(0f, (float)(_lh * roll));
            var cc = DeckHallMath.Mix(_cnt0, _cnt1, legal);
            if (_countOld != null) _countOld.color = cc;
            if (_countNew != null) _countNew.color = cc;
            foreach (var d in new[] { _cntDrop0, _cntDrop1 })
                if (d != null)
                    d.effectColor = new Color(0, 0, 0, (float)(.85 * (1 - legal)));
            // the status: the old line fades out before the new one fades in (§v6.14.10 F12)
            if (_statusFrom == _statusTo)
            {
                Put(_badTo ? _bad : _ok, _statusTo, 1);
                Put(_badTo ? _ok : _bad, "", 0);
            }
            else if (_badFrom == _badTo)
            {
                var first = u < .21;
                Put(_badTo ? _bad : _ok, first ? _statusFrom : _statusTo, first ? DeckHallMath.StatusOld(u) : DeckHallMath.StatusNew(u));
                Put(_badTo ? _ok : _bad, "", 0);
            }
            else
            {
                Put(_badFrom ? _bad : _ok, _statusFrom, DeckHallMath.StatusOld(u));
                Put(_badTo ? _bad : _ok, _statusTo, DeckHallMath.StatusNew(u));
            }
        }

        static void Put(Text t, string s, double a)
        {
            if (t == null) return;
            if (t.text != s) t.text = s;
            SheetDraw.SetA(t, MenuV6Linear.Fade(MenuV6Math.Clamp(a, 0, 1)));
        }

        void DrawScale(double tilt, double legal, double t)
        {
            var rest = Math.Abs(tilt + .2) < 1e-3 ? -1 : Math.Abs(tilt) < 1e-3 ? 0 : Math.Abs(tilt - .2) < 1e-3 ? 1 : 2;
            var gold = DeckHallMath.Mix(_gold0, _gold1, legal);
            var fea = DeckHallMath.Mix(_fea0, _fea1, legal);
            double ct = Math.Cos(tilt), stt = Math.Sin(tilt);
            foreach (var p in _parts)
            {
                var face = p.Tint == "gold" ? gold : p.Tint == "feather" ? fea : Color.white;
                // the baked rest sprites, else the tilt-0 parts moved
                var useM = rest == -1 && p.RestM != null;
                var useP = rest == 1 && p.RestP != null;
                var use0 = !useM && !useP;
                Show(p.RestM, useM, face);
                Show(p.InkM, useM, Color.black);
                Show(p.RestP, useP, face);
                Show(p.InkP, useP, Color.black);
                Show(p.Rest0, use0, face);
                Show(p.Ink0, use0, Color.black);
                if (!use0) continue;
                foreach (var img in new[] { p.Rest0, p.Ink0 })
                {
                    if (img == null) continue;
                    var rt = img.rectTransform;
                    if (p.Beam)
                    {
                        rt.localRotation = Quaternion.Euler(0f, 0f, (float)(-tilt * Mathf.Rad2Deg));
                        rt.anchoredPosition = Vector2.zero;
                    }
                    else if (p.Side != 0)
                    {
                        var arm = p.Side < 0 ? _panL : _panR;
                        var ax = _pivot.x + arm.x * ct - arm.y * stt;
                        var ay = _pivot.y + arm.x * stt + arm.y * ct;
                        rt.localRotation = Quaternion.identity;
                        rt.anchoredPosition = new Vector2((float)(ax - p.Anchor0.x), (float)-(ay - p.Anchor0.y));
                    }
                }
            }

            if (_glow != null)
            {
                var a = legal > .002 ? (.5 + .2 * Math.Sin(3 * t)) * legal : 0;
                var ax = _pivot.x + _panR.x * ct - _panR.y * stt + _glowC.x;
                var ay = _pivot.y + _panR.x * stt + _panR.y * ct + _glowC.y;
                MenuV6Layout.Place(_glow.rectTransform,
                    new Rect((float)ax - _glowSize / 2f, (float)ay - _glowSize / 2f, _glowSize, _glowSize), _canvasRect);
                _glow.color = new Color(1f, 1f, 1f, MenuV6Linear.Alpha(Math.Min(1, a)));
            }
        }

        static void Show(Image img, bool on, Color c)
        {
            if (img == null) return;
            if (img.enabled != on) img.enabled = on;
            if (on && img.color != c) img.color = c;
        }
    }
}
