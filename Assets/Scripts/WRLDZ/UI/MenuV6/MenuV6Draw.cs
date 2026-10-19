using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The Linear-colour-space correction of the v6 menu's translucent ops (the P1a landing capture: "orb / glyph glow
    /// halos wider and brighter than the approved still"). The project renders in Linear space, so UGUI blends in linear
    /// light; the page blends sRGB values. A soft glow's faint tail (alpha .05–.3) therefore lands 2–3× brighter in Unity,
    /// which widens every halo. Measured, not guessed: <c>unity_build/p1b_code/linmodel.py</c> models the Unity composite
    /// (sRGB decode, premultiplied over in linear light, encode) and reproduces the P1a capture (mean 2.4 / p95 6.7 levels,
    /// against 6.75 / 30.9 for the page model). On that model:
    /// <list type="bullet">
    /// <item>a glow op of alpha <c>a</c> over a sprite alpha profile <c>α</c> draws at <c>(a · α)^g</c> — exact at 0 and
    /// at 1, and over the menu's own backgrounds g = 1.5 halves the error (glow_fit3.py: mean 24–33 → 12–15 levels;
    /// 1.6 over the dark room / sky / niche, 1.4 on the Ring's blue, 1.2 by the torch). The glow sprites are generated
    /// here with the remapped profile (<see cref="MenuV6Glow"/>), so the remap needs no new art;</item>
    /// <item>a dark op (a shadow) of alpha a draws at 1 − (1 − a)^2.2 (<see cref="Shade"/>);</item>
    /// <item>the P1a layers that stay static (their texture profile cannot be remapped without new art) take a runtime
    /// gain per layer (<see cref="LayerGain"/>, fit_static.py).</item>
    /// </list>
    /// No shader (D3): the ops stay UI/Default "over"; P5's one shader may revisit this.
    /// </summary>
    public static class MenuV6Linear
    {
        /// <summary>Offline harness only: the page's own alphas (no correction), to check the rigs' geometry against the
        /// page's frames op for op.</summary>
        public static bool Off;

        /// <summary>The menu's default glow exponent (glow_fit3.py, c = 1).</summary>
        public const float Glow = 1.5f;

        /// <summary>Over the dark room, the sky and the niche.</summary>
        public const float Dark = 1.6f;

        /// <summary>On the Ring echo's blue.</summary>
        public const float RingBlue = 1.4f;

        /// <summary>By the torch (the lit brick wall).</summary>
        public const float Torch = 1.2f;

        /// <summary>The Unity alpha of a translucent op whose page alpha is <paramref name="a"/>: a^g (1 stays 1).</summary>
        public static float Alpha(double a, float g = Glow)
        {
            if (Off) g = 1f;
            if (a <= 0) return 0f;
            if (a >= 1) return 1f;
            return (float)Math.Pow(a, g);
        }

        /// <summary>P3: a layer fading in over the dark room / night (the cold open's E table, the title's DOM twins): the
        /// page's sRGB blend of opacity <paramref name="a"/> over near-black is a linear blend at a^2.2 (exact over black,
        /// within a few % over the dark slates); 1 stays 1, 0 stays 0.</summary>
        public static float Fade(double a)
        {
            if (Off) return (float)(a < 0 ? 0 : a > 1 ? 1 : a);
            if (a <= 0) return 0f;
            if (a >= 1) return 1f;
            return (float)Math.Pow(a, 2.2);
        }

        /// <summary>A dark "over" op (a shadow, the scan lines' ink) of page alpha <paramref name="a"/>: in linear light a
        /// dark layer darkens less, so its alpha rises to 1 − (1 − a)^2.2 (exact for black over any background).</summary>
        public static float Shade(double a)
        {
            if (Off) return (float)(a < 0 ? 0 : a > 1 ? 1 : a);
            if (a <= 0) return 0f;
            if (a >= 1) return 1f;
            return (float)(1 - Math.Pow(1 - a, 2.2));
        }

        // fit_static.py / fit_breach.py (unity_build/p1b_code) on the P1a layers that stay static in P1b (the rigs drawn as
        // above) against the approved still. Stone B keeps 1 (the fit's .6 came from one frame and would only weaken the
        // torch flicker); every other layer is 1 = unchanged.
        static readonly Dictionary<string, float> Gains = new()
        {
            { "torch_pool_b0", .4f }, { "torch_pool_b1", .25f }, { "stone_emiBlur", .4f }, { "stone_emi", .4f },
            { "vignette_top", 1.9f }, { "vignette_bottom", 2.5f }, { "vignette_left", 2.2f }, { "vignette_right", 2.2f },
            { "breach_glow_add", .3f }
        };

        /// <summary>The runtime gain of a static P1a layer (1 for every layer not in the fit).</summary>
        public static float LayerGain(string id) => id != null && Gains.TryGetValue(id, out var g) ? g : 1f;
    }

    /// <summary>
    /// The page's glow sprites (SPR): v6.3 replaces core.js's glowSprite with anGlowSprite (anime_v6.js:141-150, "a flat
    /// core + one soft glow"): a radial gradient of one colour at glowA (0), .32 glowA (.35), 0 (1), under a solid core disc
    /// of radius coreK · size in the colour lifted 78 % toward white. Generated here (no art file) with the gradient's alpha
    /// raised to the Linear correction's exponent (<see cref="MenuV6Linear"/>; the opaque core stays). The pixels are
    /// pure math (<see cref="Pixels"/>, checked against the page's own exported sprites); the Unity texture is
    /// <see cref="MenuV6GlowTex"/>.
    /// </summary>
    public static class MenuV6Glow
    {
        public enum Kind
        {
            Gold, GoldHot, Blue, BlueHot, Ame, Ember, Fire, FireDeep, White, Red, Violet
        }

        // anime_v6.js:149-150: colour, core radius (fire / fireDeep .05, else .085), glow alpha (gold / goldHot .42, else .55)
        static readonly (int r, int g, int b, double core, double glowA)[] Spec =
        {
            (237, 228, 158, .085, .42), (255, 241, 184, .085, .42), (95, 182, 255, .085, .55), (191, 230, 255, .085, .55),
            (201, 166, 255, .085, .55), (255, 150, 70, .085, .55), (255, 170, 70, .05, .55), (255, 110, 40, .05, .55),
            (255, 250, 240, .085, .55), (255, 68, 85, .085, .55), (155, 107, 255, .085, .55)
        };

        public const int Cell = 64, Pad = 2, Cols = 4;
        public static int Stride => Cell + Pad * 2;
        public static int Rows => (Spec.Length + Cols - 1) / Cols;
        public static int Width => Stride * Cols;
        public static int Height => Stride * Rows;

        /// <summary>One sprite's straight-alpha RGBA at pixel (x, y) of a <paramref name="size"/>-px sprite (canvas
        /// pixel centres), the gradient alpha raised to <paramref name="gamma"/>.</summary>
        public static Color32 At(Kind kind, int size, int x, int y, float gamma)
        {
            var (r, g, b, coreK, glowA) = Spec[(int)kind];
            var m = size * .5;
            var dx = x + .5 - m;
            var dy = y + .5 - m;
            var d = Math.Sqrt(dx * dx + dy * dy) / m;
            var ga = d <= .35 ? glowA * (1 + (.32 - 1) * (d / .35)) : d < 1 ? glowA * .32 * (1 - (d - .35) / .65) : 0;
            ga = MenuV6Linear.Alpha(ga, gamma);
            // the core disc (canvas arc fill): its pixel coverage, then source-over the gradient
            var cov = Math.Max(0, Math.Min(1, coreK * size - d * m + .5));
            double cr = r + (255 - r) * .78, cg = g + (255 - g) * .78, cb = b + (255 - b) * .78;
            cr = Math.Round(cr);
            cg = Math.Round(cg);
            cb = Math.Round(cb);
            var a = cov + ga * (1 - cov);
            if (a <= 0) return new Color32((byte)r, (byte)g, (byte)b, 0);
            var R = (cr * cov + r * ga * (1 - cov)) / a;
            var G = (cg * cov + g * ga * (1 - cov)) / a;
            var B = (cb * cov + b * ga * (1 - cov)) / a;
            return new Color32((byte)Math.Round(R), (byte)Math.Round(G), (byte)Math.Round(B), (byte)Math.Round(a * 255));
        }

        /// <summary>The atlas pixels (row 0 = the texture's bottom row, as Texture2D.SetPixels32 takes them). The page's
        /// ember is a 32-px sprite: each of its pixels fills 2 × 2 pixels of its 64-px cell.</summary>
        public static Color32[] Pixels(float gamma)
        {
            int w = Width, h = Height;
            var px = new Color32[w * h];
            for (var k = 0; k < Spec.Length; k++)
            {
                int ox = (k % Cols) * Stride + Pad, oy = (k / Cols) * Stride + Pad;
                var kind = (Kind)k;
                for (var y = 0; y < Cell; y++)
                for (var x = 0; x < Cell; x++)
                {
                    // texture rows go up; the page's sprite rows go down
                    var c = kind == Kind.Ember ? At(kind, 32, x / 2, (Cell - 1 - y) / 2, gamma) : At(kind, Cell, x, Cell - 1 - y, gamma);
                    px[(oy + y) * w + ox + x] = c;
                }
            }

            return px;
        }

        /// <summary>The cell of <paramref name="kind"/> as a UV rect (x, y, w, h) in the atlas.</summary>
        public static Rect Uv(Kind kind)
        {
            var k = (int)kind;
            float w = Width, h = Height;
            var x = (k % Cols) * Stride + Pad;
            var y = (k / Cols) * Stride + Pad;
            return new Rect(x / w, y / h, Cell / w, Cell / h);
        }
    }

    /// <summary>A texture a rig draws with: the Unity texture (null outside Unity), its key (Resources path or "glow:g")
    /// and its pixel size.</summary>
    public sealed class RigTex
    {
        public string Key;
        public Texture Tex;
        public Sprite Sprite;   // when it came from a sprite (masks need the Sprite itself)
        public int W, H;
        public Rect Uv = new(0f, 0f, 1f, 1f);   // the sprite's rect inside its texture

        /// <summary>The UV (0..1 of this sprite, y up) of its <paramref name="w0"/> × <paramref name="h0"/> px content: the
        /// exporter pads every sprite right / bottom to a multiple of 4 px for block compression (rigs_assemble.py write),
        /// so a part drawn by formula (origin + drawWH, half, size / S) must sample its content only. A part with a
        /// 'rect' was grown with its padding and draws whole.</summary>
        public Rect Content(double w0, double h0)
        {
            if (W <= 0 || H <= 0 || w0 <= 0 || h0 <= 0) return new Rect(0f, 0f, 1f, 1f);
            var cw = (float)Math.Min(1, w0 / W);
            var ch = (float)Math.Min(1, h0 / H);
            return new Rect(0f, 1f - ch, cw, ch);
        }

        /// <summary><see cref="Content"/> from a part's recorded "size" [w, h] (the full sprite when absent).</summary>
        public Rect ContentOf(Dictionary<string, object> part)
        {
            var sz = RJ.L(part, "size");
            return sz != null && sz.Count >= 2 ? Content(RJ.At(sz, 0), RJ.At(sz, 1)) : new Rect(0f, 0f, 1f, 1f);
        }
    }

    /// <summary>One vertex of a rig mesh, in stage px (top-left origin, y down).</summary>
    public struct StageVert
    {
        public double X, Y;
        public Color32 C;
        public float U, V;
    }

    /// <summary>
    /// Untextured vector ops of the rigs (vertex colours): triangles, gradient wedges, feathered polygons (the torch
    /// tongues, sparkles), anti-aliased rects (scan lines, windows), circle strokes with the canvas dash, strokes. Stage
    /// px. Pure (no engine calls), so the offline harness records exactly what Unity draws.
    /// </summary>
    public sealed class ShapeList
    {
        public readonly List<StageVert> V = new();
        public readonly List<int> I = new();

        /// <summary>Edge feather (stage px) of polygons and strokes: the canvas's antialiasing, approximately.</summary>
        public float Feather = .9f;

        public void Clear()
        {
            V.Clear();
            I.Clear();
        }

        int Add(double x, double y, Color32 c)
        {
            V.Add(new StageVert { X = x, Y = y, C = c });
            return V.Count - 1;
        }

        static Color32 C(Color c) => c;

        static Color32 C0(Color c) => new((byte)Math.Round(Clamp01(c.r) * 255), (byte)Math.Round(Clamp01(c.g) * 255),
            (byte)Math.Round(Clamp01(c.b) * 255), 0);

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        void T(int a, int b, int c)
        {
            I.Add(a);
            I.Add(b);
            I.Add(c);
        }

        public void Tri(double x0, double y0, Color c0, double x1, double y1, Color c1, double x2, double y2, Color c2)
        {
            var a = Add(x0, y0, C(c0));
            var b = Add(x1, y1, C(c1));
            var c = Add(x2, y2, C(c2));
            T(a, b, c);
        }

        /// <summary>A quad p0 p1 p2 p3 (in order round the outline), one colour per corner.</summary>
        public void Quad(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3,
            Color c0, Color c1, Color c2, Color c3)
        {
            var a = Add(x0, y0, C(c0));
            var b = Add(x1, y1, C(c1));
            var c = Add(x2, y2, C(c2));
            var d = Add(x3, y3, C(c3));
            T(a, b, c);
            T(c, d, a);
        }

        /// <summary>A rect with a box-filter edge (≈ the canvas's coverage of a fractional rect).</summary>
        public void RectAA(double x, double y, double w, double h, Color c)
        {
            if (w <= 0 || h <= 0 || c.a <= 0.001f) return;
            var ix = Math.Min(.5, w * .5);
            var iy = Math.Min(.5, h * .5);
            var peak = c;
            peak.a *= (float)(Math.Min(1, w) * Math.Min(1, h));
            double ox0 = x - .5, oy0 = y - .5, ox1 = x + w + .5, oy1 = y + h + .5;
            double ix0 = x + ix, iy0 = y + iy, ix1 = x + w - ix, iy1 = y + h - iy;
            var c0 = C0(c);
            var cp = C(peak);
            int o0 = Add(ox0, oy0, c0), o1 = Add(ox1, oy0, c0), o2 = Add(ox1, oy1, c0), o3 = Add(ox0, oy1, c0);
            int i0 = Add(ix0, iy0, cp), i1 = Add(ix1, iy0, cp), i2 = Add(ix1, iy1, cp), i3 = Add(ix0, iy1, cp);
            T(i0, i1, i2);
            T(i2, i3, i0);
            T(o0, o1, i1);
            T(i1, i0, o0);
            T(o1, o2, i2);
            T(i2, i1, o1);
            T(o2, o3, i3);
            T(i3, i2, o2);
            T(o3, o0, i0);
            T(i0, i3, o3);
        }

        readonly List<int> _ring = new();
        double[] _ix = new double[64], _iy = new double[64], _ox = new double[64], _oy = new double[64];

        /// <summary>A filled polygon (nonzero; ear clipping, a fan when the outline is not simple). With
        /// <paramref name="feather"/> its edge is a ramp centred on the outline (the core inset by half the feather, the
        /// ramp out to half the feather beyond it): the canvas's 50 % coverage at the true edge, approximately.</summary>
        public void Poly(double[] xs, double[] ys, int n, Color c, bool feather = true)
        {
            if (n < 3 || c.a <= 0.001f) return;
            double area = 0;
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                area += xs[i] * ys[j] - xs[j] * ys[i];
            }

            if (Math.Abs(area) < 1e-6) return;
            var cw = area > 0;   // in stage px (y down) a positive shoelace area is clockwise on screen
            if (!feather || Feather <= 0f)
            {
                Fill(xs, ys, n, cw, C(c));
                return;
            }

            if (_ix.Length < n)
            {
                _ix = new double[n * 2];
                _iy = new double[n * 2];
                _ox = new double[n * 2];
                _oy = new double[n * 2];
            }

            // per-vertex miter of the two edge normals (outward), its length capped so spikes do not invert
            var h = Feather * .5;
            var sign = cw ? 1 : -1;
            for (var i = 0; i < n; i++)
            {
                int p = (i + n - 1) % n, q = (i + 1) % n;
                double e0x = xs[i] - xs[p], e0y = ys[i] - ys[p], e1x = xs[q] - xs[i], e1y = ys[q] - ys[i];
                var l0 = Math.Sqrt(e0x * e0x + e0y * e0y);
                var l1 = Math.Sqrt(e1x * e1x + e1y * e1y);
                double n0x = 0, n0y = 0, n1x = 0, n1y = 0;
                if (l0 > 1e-9) { n0x = sign * e0y / l0; n0y = sign * -e0x / l0; }
                if (l1 > 1e-9) { n1x = sign * e1y / l1; n1y = sign * -e1x / l1; }
                double mx = n0x + n1x, my = n0y + n1y;
                var ml = Math.Sqrt(mx * mx + my * my);
                if (ml < 1e-9) { mx = n1x; my = n1y; ml = 1; }
                mx /= ml;
                my /= ml;
                var cosHalf = Math.Max(.35, mx * n1x + my * n1y);   // miter length 1 / cos(half angle), capped near 2.9
                var d = h / cosHalf;
                _ix[i] = xs[i] - mx * d;
                _iy[i] = ys[i] - my * d;
                _ox[i] = xs[i] + mx * d;
                _oy[i] = ys[i] + my * d;
            }

            var c32 = C(c);
            Fill(_ix, _iy, n, cw, c32);
            var c0 = C0(c);
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                var a0 = Add(_ix[i], _iy[i], c32);
                var a1 = Add(_ix[j], _iy[j], c32);
                var b1 = Add(_ox[j], _oy[j], c0);
                var b0 = Add(_ox[i], _oy[i], c0);
                T(a0, a1, b1);
                T(b1, b0, a0);
            }
        }

        void Fill(double[] xs, double[] ys, int n, bool cw, Color32 c32)
        {
            var b = V.Count;
            for (var i = 0; i < n; i++) Add(xs[i], ys[i], c32);
            _ring.Clear();
            for (var i = 0; i < n; i++) _ring.Add(i);
            var guard = 0;
            while (_ring.Count > 3 && guard++ < n * n)
            {
                var clipped = false;
                for (var k = 0; k < _ring.Count; k++)
                {
                    int ia = _ring[(k + _ring.Count - 1) % _ring.Count], ib = _ring[k], ic = _ring[(k + 1) % _ring.Count];
                    var cross = (xs[ib] - xs[ia]) * (ys[ic] - ys[ia]) - (ys[ib] - ys[ia]) * (xs[ic] - xs[ia]);
                    if (cw ? cross <= 0 : cross >= 0) continue;   // reflex
                    var inside = false;
                    for (var m = 0; m < _ring.Count && !inside; m++)
                    {
                        var ip = _ring[m];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        inside = InTri(xs[ip], ys[ip], xs[ia], ys[ia], xs[ib], ys[ib], xs[ic], ys[ic]);
                    }

                    if (inside) continue;
                    T(b + ia, b + ib, b + ic);
                    _ring.RemoveAt(k);
                    clipped = true;
                    break;
                }

                if (!clipped) break;
            }

            if (_ring.Count == 3)
                T(b + _ring[0], b + _ring[1], b + _ring[2]);
            else if (_ring.Count > 3)
                for (var k = 1; k + 1 < _ring.Count; k++)   // not simple: a fan over what is left
                    T(b + _ring[0], b + _ring[k], b + _ring[k + 1]);
        }

        static bool InTri(double px, double py, double ax, double ay, double bx, double by, double cx, double cy)
        {
            var d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            var d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            var d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            var neg = d1 < 0 || d2 < 0 || d3 < 0;
            var pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>A circle stroke (butt caps) of width <paramref name="w"/> at radius <paramref name="r"/>; with
        /// <paramref name="dashOn"/> &gt; 0 the canvas dash [on, off] at lineDashOffset <paramref name="dashOffset"/>
        /// (the pattern's phase at arclength s is (s + offset) mod (on + off), from angle 0, clockwise on screen).</summary>
        public void Circle(double cx, double cy, double r, double w, Color c, double dashOn = 0, double dashOff = 0,
            double dashOffset = 0)
        {
            if (r <= 0 || w <= 0 || c.a <= 0.001f) return;
            var circ = Math.PI * 2 * r;
            if (dashOn <= 0 || dashOff <= 0)
            {
                Arc(cx, cy, r, w, c, 0, Math.PI * 2, Math.Max(24, (int)(circ / 6)));
                return;
            }

            var per = dashOn + dashOff;
            var o = MenuV6RigMath.Mod(dashOffset, per);
            for (var k = 0; ; k++)
            {
                var s0 = k * per - o;
                if (s0 >= circ) break;
                var s1 = s0 + dashOn;
                if (s1 <= 0) continue;
                s0 = Math.Max(0, s0);
                s1 = Math.Min(circ, s1);
                if (s1 - s0 < 1e-4) continue;
                Arc(cx, cy, r, w, c, s0 / r, s1 / r, Math.Max(1, (int)Math.Ceiling((s1 - s0) / 4)));
            }
        }

        /// <summary>An arc band from angle a0 to a1 (radians, clockwise on screen), width w, its edges a ramp of
        /// <see cref="Feather"/> px centred on them (a band thinner than the ramp keeps its coverage as a lower peak).</summary>
        public void Arc(double cx, double cy, double r, double w, Color c, double a0, double a1, int seg)
        {
            var f = Math.Max(1e-3, Feather);
            var hw = w * .5;
            var hc = Math.Max(0, hw - f * .5);      // the full-alpha core
            var ho = hw + f * .5;                   // where the ramp reaches 0
            var col = c;
            if (w < f) col.a *= (float)(w / f);
            var c32 = C(col);
            var c0 = C0(c);
            double ri = Math.Max(0, r - hc), ro = r + hc, ei = Math.Max(0, r - ho), eo = r + ho;
            int pi0 = -1, pi1 = -1, po0 = -1, po1 = -1;
            for (var s = 0; s <= seg; s++)
            {
                var a = a0 + (a1 - a0) * s / seg;
                var cs = Math.Cos(a);
                var sn = Math.Sin(a);
                var i0 = Add(cx + cs * ri, cy + sn * ri, c32);
                var i1 = Add(cx + cs * ro, cy + sn * ro, c32);
                var e0 = Add(cx + cs * ei, cy + sn * ei, c0);
                var e1 = Add(cx + cs * eo, cy + sn * eo, c0);
                if (s > 0)
                {
                    if (hc > 0)
                    {
                        T(pi0, pi1, i1);
                        T(i1, i0, pi0);
                    }

                    T(po0, pi0, i0);
                    T(i0, e0, po0);
                    T(pi1, po1, e1);
                    T(e1, i1, pi1);
                }

                pi0 = i0;
                pi1 = i1;
                po0 = e0;
                po1 = e1;
            }
        }

        /// <summary>A straight stroke from (x0, y0) to (x1, y1), width w, colour c0 at the start and c1 at the end; its
        /// sides and ends a ramp of <see cref="Feather"/> px centred on the stroke's outline.</summary>
        public void Line(double x0, double y0, double x1, double y1, double w, Color c0, Color c1)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return;
            var f = Math.Max(1e-3, Feather);
            var ux = dx / len;
            var uy = dy / len;
            var hw = w * .5;
            var hc = Math.Max(0, hw - f * .5);
            var ho = hw + f * .5;
            var col0 = c0;
            var col1 = c1;
            if (w < f)
            {
                col0.a *= (float)(w / f);
                col1.a *= (float)(w / f);
            }

            double nx = -uy, ny = ux;
            var a0 = C(col0);
            var a1 = C(col1);
            var z0 = C0(c0);
            var z1 = C0(c1);
            var ex = ux * f * .5;
            var ey = uy * f * .5;
            int p0 = Add(x0 + nx * hc, y0 + ny * hc, a0), p1 = Add(x1 + nx * hc, y1 + ny * hc, a1);
            int p2 = Add(x1 - nx * hc, y1 - ny * hc, a1), p3 = Add(x0 - nx * hc, y0 - ny * hc, a0);
            if (hc > 0)
            {
                T(p0, p1, p2);
                T(p2, p3, p0);
            }

            int q0 = Add(x0 + nx * ho, y0 + ny * ho, z0), q1 = Add(x1 + nx * ho, y1 + ny * ho, z1);
            int q2 = Add(x1 - nx * ho, y1 - ny * ho, z1), q3 = Add(x0 - nx * ho, y0 - ny * ho, z0);
            T(q0, q1, p1);
            T(p1, p0, q0);
            T(p3, p2, q2);
            T(q2, q3, p3);
            // the ends: a ramp half a feather past each end (the canvas's caps read as soft ends at these widths)
            int e1 = Add(x1 + ex, y1 + ey, z1), e0 = Add(x0 - ex, y0 - ey, z0);
            T(p1, q1, e1);
            T(p1, e1, p2);
            T(p2, e1, q2);
            T(p0, e0, q0);
            T(p3, e0, p0);
            T(q3, e0, p3);
        }

        readonly double[] _spX = new double[20], _spY = new double[20];

        /// <summary>anime_v6.js:220 anSparkle: the 4-point cel star of radius s (control points at .22 s), rotated.</summary>
        public void Sparkle(double x, double y, double s, Color c, double rot)
        {
            if (s <= .1 || c.a <= 0.001f) return;
            var k = s * .22;
            double[] P = { 0, -s, k, -k, s, 0, k, k, 0, s, -k, k, -s, 0, -k, -k, 0, -s };
            double cr = Math.Cos(rot), sr = Math.Sin(rot);
            var n = 0;
            _spX[n] = x + P[0] * cr - P[1] * sr;
            _spY[n] = y + P[0] * sr + P[1] * cr;
            n++;
            for (var q = 0; q < 4; q++)
            {
                double sx = P[q * 4], sy = P[q * 4 + 1], qx = P[q * 4 + 2], qy = P[q * 4 + 3], ex = P[q * 4 + 4], ey = P[q * 4 + 5];
                for (var i = 1; i <= 4; i++)
                {
                    if (q == 3 && i == 4) break;   // back at the start
                    var u = i / 4.0;
                    var a = (1 - u) * (1 - u);
                    var b = 2 * (1 - u) * u;
                    var cc = u * u;
                    var px = a * sx + b * qx + cc * ex;
                    var py = a * sy + b * qy + cc * ey;
                    _spX[n] = x + px * cr - py * sr;
                    _spY[n] = y + px * sr + py * cr;
                    n++;
                }
            }

            Poly(_spX, _spY, n, c);
        }
    }

    /// <summary>Textured quads of one texture, stage px: centred / pivoted / rotated sprite draws (core.js drawSpr) and
    /// quads by their corners (the box's cards). Pure, like <see cref="ShapeList"/>.</summary>
    public sealed class QuadList
    {
        public readonly List<StageVert> V = new();
        public readonly List<int> I = new();

        public void Clear()
        {
            V.Clear();
            I.Clear();
        }

        void Add(double x, double y, float u, float v, Color32 c) => V.Add(new StageVert { X = x, Y = y, C = c, U = u, V = v });

        void Close(int b)
        {
            I.Add(b);
            I.Add(b + 1);
            I.Add(b + 2);
            I.Add(b + 2);
            I.Add(b + 3);
            I.Add(b);
        }

        /// <summary>A quad whose top-left corner is (ox, oy) relative to the pivot (px, py), w × h, rotated about the pivot
        /// by <paramref name="rot"/> (radians, clockwise on screen), sampling <paramref name="uv"/> (texture UV, y up).</summary>
        public void Pivoted(double px, double py, double ox, double oy, double w, double h, Rect uv, Color c, double rot)
        {
            if (c.a <= 0.002f || w <= 0 || h <= 0) return;
            double cr = Math.Cos(rot), sr = Math.Sin(rot);
            var b = V.Count;
            Color32 c32 = c;
            void P(double lx, double ly, float u, float v) => Add(px + lx * cr - ly * sr, py + lx * sr + ly * cr, u, v, c32);
            P(ox, oy, uv.xMin, uv.yMax);
            P(ox + w, oy, uv.xMax, uv.yMax);
            P(ox + w, oy + h, uv.xMax, uv.yMin);
            P(ox, oy + h, uv.xMin, uv.yMin);
            Close(b);
        }

        /// <summary>A w × h quad centred on (cx, cy), rotated by <paramref name="rot"/>.</summary>
        public void Sprite(double cx, double cy, double w, double h, Rect uv, Color c, double rot = 0) =>
            Pivoted(cx, cy, -w * .5, -h * .5, w, h, uv, c, rot);

        /// <summary>An axis-aligned stage rect.</summary>
        public void Rect(Rect stage, Rect uv, Color c) => Pivoted(stage.x, stage.y, 0, 0, stage.width, stage.height, uv, c, 0);

        /// <summary>A quad by its corners TL, TR, BR, BL (stage px).</summary>
        public void Corners(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, Rect uv,
            Color c)
        {
            if (c.a <= 0.002f) return;
            var b = V.Count;
            Color32 c32 = c;
            Add(x0, y0, uv.xMin, uv.yMax, c32);
            Add(x1, y1, uv.xMax, uv.yMax, c32);
            Add(x2, y2, uv.xMax, uv.yMin, c32);
            Add(x3, y3, uv.xMin, uv.yMin, c32);
            Close(b);
        }

        public static readonly Rect Full = new(0f, 0f, 1f, 1f);

        /// <summary>The UV of a flipbook cell (x, y, w, h in sheet px, y from the top) of a sheet tw × th px.</summary>
        public static Rect Cell(int tw, int th, double x, double y, double w, double h)
        {
            if (tw <= 0 || th <= 0) return Full;
            return new Rect((float)(x / tw), 1f - (float)((y + h) / th), (float)(w / tw), (float)(h / th));
        }
    }

    /// <summary>A rig's vector layer: the ops go into <see cref="S"/>; <see cref="Commit"/> hands them to the graphic
    /// (Unity: a RigShapes on UI/Default; the harness: a recorder).</summary>
    public sealed class ShapeLayer
    {
        public readonly ShapeList S = new();
        public string Name;

        /// <summary>The page draws this op 'lighter' (additive). Unity draws it "over" with the Linear correction (D3); the
        /// flag only tells the offline harness how the page composites it.</summary>
        public bool Add;
        public Action OnCommit;

        public float Feather
        {
            get => S.Feather;
            set => S.Feather = value;
        }

        public void Clear() => S.Clear();
        public void Commit() => OnCommit?.Invoke();
        public void Tri(double x0, double y0, Color c0, double x1, double y1, Color c1, double x2, double y2, Color c2) => S.Tri(x0, y0, c0, x1, y1, c1, x2, y2, c2);

        public void Quad(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, Color c0, Color c1,
            Color c2, Color c3) => S.Quad(x0, y0, x1, y1, x2, y2, x3, y3, c0, c1, c2, c3);

        public void RectAA(double x, double y, double w, double h, Color c) => S.RectAA(x, y, w, h, c);
        public void Poly(double[] xs, double[] ys, int n, Color c, bool feather = true) => S.Poly(xs, ys, n, c, feather);

        public void Circle(double cx, double cy, double r, double w, Color c, double dashOn = 0, double dashOff = 0, double dashOffset = 0) =>
            S.Circle(cx, cy, r, w, c, dashOn, dashOff, dashOffset);

        public void Line(double x0, double y0, double x1, double y1, double w, Color c0, Color c1) => S.Line(x0, y0, x1, y1, w, c0, c1);
        public void Sparkle(double x, double y, double s, Color c, double rot) => S.Sparkle(x, y, s, c, rot);
    }

    /// <summary>A rig's textured layer of one texture: the quads go into <see cref="Q"/>; <see cref="Commit"/> hands them
    /// to the graphic (Unity: a RigQuads on UI/Default; the harness: a recorder).</summary>
    public sealed class QuadLayer
    {
        public readonly QuadList Q = new();
        public string Name;

        /// <summary>The page draws this op 'lighter' (see <see cref="ShapeLayer.Add"/>).</summary>
        public bool Add;
        public Action OnCommit;
        public Action<RigTex> OnTex;
        RigTex _tex;

        public RigTex Tex
        {
            get => _tex;
            set
            {
                _tex = value;
                OnTex?.Invoke(value);
            }
        }

        public void Clear() => Q.Clear();
        public void Commit() => OnCommit?.Invoke();
        public void Sprite(double cx, double cy, double w, double h, Rect uv, Color c, double rot = 0) => Q.Sprite(cx, cy, w, h, uv, c, rot);
        public void Pivoted(double px, double py, double ox, double oy, double w, double h, Rect uv, Color c, double rot) => Q.Pivoted(px, py, ox, oy, w, h, uv, c, rot);
        public void Rect(Rect stage, Rect uv, Color c) => Q.Rect(stage, uv, c);

        public void Corners(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, Rect uv, Color c) =>
            Q.Corners(x0, y0, x1, y1, x2, y2, x3, y3, uv, c);
    }

    /// <summary>A box-style mask: the stencil shape a sprite (the box body), a child sprite drawn through it (the lid's
    /// shadow) at an offset and alpha.</summary>
    public interface ISpriteMask
    {
        void Set(double dx, double dy, float alpha);
    }

    /// <summary>
    /// How a rig makes its layers. Hosts are opaque handles (a RectTransform in Unity) covering a stage rect; every layer
    /// is created in call order, which is the page's draw order. <see cref="UguiGfx"/> builds UGUI graphics; the offline
    /// harness records the same ops for its raster check against the page frames.
    /// </summary>
    public interface IRigGfx
    {
        ShapeLayer Shapes(object host, string name, Rect frame);
        QuadLayer Quads(object host, string name, Rect frame, RigTex tex);

        /// <summary>A RectMask2D node over <paramref name="clip"/> (stage px) inside a host covering <paramref name="frame"/>.</summary>
        object Clip(object host, string name, Rect clip, Rect frame);

        /// <summary>A polygon stencil mask node (the box's rim): its shape is the returned layer's mesh; children go under
        /// the returned host.</summary>
        object PolyMask(object host, string name, Rect frame, out ShapeLayer shape);

        ISpriteMask SpriteMask(object host, string name, Rect rect, Rect frame, RigTex stencil, RigTex child);

        RigTex Sprite(string path);
        RigTex Local(string path);
        RigTex Sheet(string path);
        RigTex Glow(float gamma);
    }
}
