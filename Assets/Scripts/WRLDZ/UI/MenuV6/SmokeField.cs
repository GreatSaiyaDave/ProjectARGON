using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>One of the page's six cel filters (smoke_&lt;layout&gt;.json kinds; anime_v6.js:163-187).</summary>
    public sealed class SmokeKind
    {
        public string Id;
        public double Slope, Intercept, PreBlur, SliverOpacity = .75, GlowOpacity = .42;
        public int LineR = 2;
        public Vector4 Fill, Mid, Lit, Sliver, Ink, Glow;   // raw sRGB (never converted by a Color path)

        static Vector4 Hex(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Vector4(0, 0, 0, 1);
            s = s.TrimStart('#');
            if (s.Length < 6) return new Vector4(0, 0, 0, 1);
            var v = Convert.ToInt32(s.Substring(0, 6), 16);
            return new Vector4(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public static SmokeKind From(string id, Dictionary<string, object> k)
        {
            if (k == null) return null;
            return new SmokeKind
            {
                Id = id, Slope = RJ.N(k, "slope", 26), Intercept = RJ.N(k, "intercept", -9.9), PreBlur = RJ.N(k, "preBlur"),
                LineR = Math.Max(0, (int)Math.Round(RJ.N(k, "lineRadiusRaster", 2))), SliverOpacity = RJ.N(k, "sliverOpacity", .75),
                GlowOpacity = RJ.N(k, "glowOpacity", .42), Fill = Hex(RJ.S(k, "fill")), Mid = Hex(RJ.S(k, "mid")),
                Lit = Hex(RJ.S(k, "lit")), Sliver = Hex(RJ.S(k, "sliver")), Ink = Hex(RJ.S(k, "ink")), Glow = Hex(RJ.S(k, "glow"))
            };
        }
    }

    /// <summary>A 96 px lobe sprite's alpha (smoke_&lt;layout&gt;.json sprites: the page's anLobeSprite, row 0 = top).</summary>
    public sealed class LobeSprite
    {
        public int W, H;
        public float[] A;   // 0..1

        public static LobeSprite From(Dictionary<string, object> s)
        {
            var b64 = RJ.S(s, "alphaB64");
            if (string.IsNullOrEmpty(b64)) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch { return null; }
            var n = (int)RJ.N(s, "size", 96);
            if (n <= 0 || bytes.Length < n * n) return null;
            var l = new LobeSprite { W = n, H = n, A = new float[n * n] };
            for (var i = 0; i < n * n; i++) l.A[i] = bytes[i] / 255f;
            return l;
        }
    }

    /// <summary>Parsed smoke_&lt;layout&gt;.json (cached per TextAsset).</summary>
    public sealed class SmokeSet
    {
        public double W, H, K = 1, Cx, Cy, FloorY, KR, KCx, KCy, Scale = .5;
        public readonly Dictionary<string, SmokeKind> Kinds = new();
        public readonly LobeSprite[] Dark = new LobeSprite[4];
        public readonly LobeSprite[] Grey = new LobeSprite[2];
        public readonly List<Puff> Puffs = new();
        public readonly List<Vector2> Emit = new(), FisP = new();
        public int PoolN;
        public double PoolFy, PoolSp;
        public readonly List<Fog> TitleFog = new();

        public struct Puff
        {
            public double Life, Off, Vx, Vy, S0, Gs, Rot, Vr, Dark;
            public int K;
            public bool Fis;
        }

        public struct Fog
        {
            public double X, Y, S, Vx, A, Ph;
            public int K;
        }

        static readonly Dictionary<string, (TextAsset ta, SmokeSet s)> Cache = new();

        public static SmokeSet Load(MenuLayoutKind kind)
        {
            var name = kind == MenuLayoutKind.Landscape ? "smoke_land" : "smoke_port";
            try
            {
                var ta = MenuV6Art.Data(name);
                if (ta == null) return null;
                if (Cache.TryGetValue(name, out var hit) && hit.ta == ta) return hit.s;
                var s = From(MenuV6Json.Parse(ta.text) as Dictionary<string, object>);
                Cache[name] = (ta, s);
                return s;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] " + name + ".json: " + ex.Message);
                return null;
            }
        }

        public static SmokeSet From(Dictionary<string, object> root)
        {
            if (root == null) return null;
            var s = new SmokeSet();
            var st = RJ.D(root, "stage");
            s.W = RJ.N(st, "W", 1920);
            s.H = RJ.N(st, "H", 1080);
            s.Scale = RJ.N(RJ.D(root, "shader"), "densityScale", .5);
            var kinds = RJ.D(root, "kinds");
            if (kinds != null)
                foreach (var kv in kinds)
                {
                    var k = SmokeKind.From(kv.Key, kv.Value as Dictionary<string, object>);
                    if (k != null) s.Kinds[kv.Key] = k;
                }

            var spr = RJ.D(root, "sprites");
            for (var i = 0; i < 4; i++) s.Dark[i] = LobeSprite.From(RJ.D(spr, "dark" + i));
            for (var i = 0; i < 2; i++) s.Grey[i] = LobeSprite.From(RJ.D(spr, "grey" + i));
            var m = RJ.D(root, "menu");
            var L = RJ.D(m, "L");
            s.K = RJ.N(L, "k", 1);
            s.Cx = RJ.N(L, "cx", s.W / 2);
            s.Cy = RJ.N(L, "cy", s.H / 2);
            s.FloorY = RJ.N(L, "floorY", s.H);
            s.KR = RJ.N(m, "KR");
            var kc = RJ.L(m, "KC");
            s.KCx = RJ.At(kc, 0, s.Cx);
            s.KCy = RJ.At(kc, 1, s.Cy);
            var puffs = RJ.L(m, "puffs");
            if (puffs != null)
                foreach (var p in puffs)
                    s.Puffs.Add(new Puff
                    {
                        Life = RJ.N(p, "life", 6), Off = RJ.N(p, "off"), Vx = RJ.N(p, "vx"), Vy = RJ.N(p, "vy"), S0 = RJ.N(p, "s0"),
                        Gs = RJ.N(p, "gs"), K = (int)RJ.N(p, "k"), Rot = RJ.N(p, "rot"), Vr = RJ.N(p, "vr"), Dark = RJ.N(p, "dark"),
                        Fis = RJ.B(p, "fis")
                    });
            foreach (var (dst, key) in new[] { (s.Emit, "emit"), (s.FisP, "fisP") })
            {
                var l = RJ.L(m, key);
                if (l == null) continue;
                foreach (var q in l) dst.Add(new Vector2((float)RJ.At(q as List<object>, 0), (float)RJ.At(q as List<object>, 1)));
            }

            var pool = RJ.D(m, "pool");
            s.PoolN = (int)RJ.N(pool, "n");
            s.PoolFy = RJ.N(pool, "fy");
            s.PoolSp = RJ.N(pool, "sp", 46);
            var fog = RJ.L(RJ.D(root, "title"), "fog");
            if (fog != null)
                foreach (var f in fog)
                    s.TitleFog.Add(new Fog
                    {
                        X = RJ.N(f, "x"), Y = RJ.N(f, "y"), S = RJ.N(f, "s"), Vx = RJ.N(f, "vx"), A = RJ.N(f, "a"), Ph = RJ.N(f, "ph"),
                        K = (int)RJ.N(f, "k")
                    });
            return s;
        }

        public bool HasSprites()
        {
            foreach (var d in Dark)
                if (d == null)
                    return false;
            foreach (var g in Grey)
                if (g == null)
                    return false;
            return true;
        }
    }

    /// <summary>
    /// One cel layer's density on the CPU (BUILD_PLAN D3; smoke_&lt;layout&gt;.json shader.route): every puff of one cel kind
    /// splatted into an R8 density over the stage region <see cref="Region"/> at <see cref="Scale"/> of stage res (the alpha
    /// "over" union a = s + d (1 − s), the lobe sprite sampled bilinearly, the sprite rect = the page's drawImage dest rect
    /// under the puff's matrix), then the glow source G0 = blur(M, σ ≈ 8 stage px) at half that res (box passes of
    /// <see cref="GlowChain"/>, sliding sums: the page's feGaussianBlur σ 9 as Chromium runs it, measured against the page's
    /// cel layers; decided over a gaussian at 1/2 res for the phone's CPU). System types only.
    /// </summary>
    public sealed class CelBuffer
    {
        public readonly Rect Region;   // stage px
        public readonly double Scale;
        public readonly int Dw, Dh, Gw, Gh;
        public readonly float[] A;
        public readonly byte[] DBytes, GBytes;
        readonly float[] _m, _g, _tmp;
        int _x0, _y0, _x1, _y1;            // this frame's dirty box (density px, x1 / y1 exclusive)
        int _px0, _py0, _px1 = -1, _py1;   // last frame's
        public bool Any { get; private set; }

        public CelBuffer(Rect region, double scale)
        {
            Region = region;
            Scale = scale;
            Dw = Math.Max(4, (int)Math.Ceiling(region.width * scale));
            Dh = Math.Max(4, (int)Math.Ceiling(region.height * scale));
            Gw = Math.Max(2, (Dw + 1) / 2);
            Gh = Math.Max(2, (Dh + 1) / 2);
            A = new float[Dw * Dh];
            DBytes = new byte[Dw * Dh];
            GBytes = new byte[Gw * Gh];
            _m = new float[Gw * Gh];
            _g = new float[Gw * Gh];
            _tmp = new float[Gw * Gh];
            _x0 = _y0 = int.MaxValue;
            _x1 = _y1 = int.MinValue;
        }

        /// <summary>Start a frame: clear what the last frame drew.</summary>
        public void Begin()
        {
            if (_px1 >= 0)
            {
                for (var y = _py0; y < _py1; y++)
                {
                    var o = y * Dw;
                    Array.Clear(A, o + _px0, _px1 - _px0);
                    Array.Clear(DBytes, o + _px0, _px1 - _px0);
                }

                int gx0 = Math.Max(0, _px0 / 2 - 8), gx1 = Math.Min(Gw, _px1 / 2 + 9), gy0 = Math.Max(0, _py0 / 2 - 8), gy1 = Math.Min(Gh, _py1 / 2 + 9);
                for (var y = gy0; y < gy1; y++) Array.Clear(GBytes, y * Gw + gx0, gx1 - gx0);
            }

            _x0 = _y0 = int.MaxValue;
            _x1 = _y1 = int.MinValue;
            Any = false;
        }

        /// <summary>
        /// drawImage(sprite, ox, oy, w, h) under translate(px, py) rotate(rot) at globalAlpha a (stage px, y down): the
        /// density px whose centres fall in the rotated rect take the sprite's bilinear alpha × a, unioned "over".
        /// </summary>
        public void Splat(LobeSprite s, double px, double py, double ox, double oy, double w, double h, double rot, double a)
        {
            if (s == null || a <= .0005 || w <= .01 || h <= .01) return;
            if (a > 1) a = 1;
            double cr = Math.Cos(rot), sr = Math.Sin(rot);
            // the rect's corners (stage) -> its box in density px
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (var c = 0; c < 4; c++)
            {
                var lx = ox + ((c & 1) != 0 ? w : 0);
                var ly = oy + ((c & 2) != 0 ? h : 0);
                var X = px + lx * cr - ly * sr;
                var Y = py + lx * sr + ly * cr;
                if (X < minX) minX = X;
                if (X > maxX) maxX = X;
                if (Y < minY) minY = Y;
                if (Y > maxY) maxY = Y;
            }

            var sc = Scale;
            double rx = Region.x, ry = Region.y;
            var ix0 = Math.Max(0, (int)Math.Floor((minX - rx) * sc - .5));
            var iy0 = Math.Max(0, (int)Math.Floor((minY - ry) * sc - .5));
            var ix1 = Math.Min(Dw, (int)Math.Ceiling((maxX - rx) * sc + .5));
            var iy1 = Math.Min(Dh, (int)Math.Ceiling((maxY - ry) * sc + .5));
            if (ix0 >= ix1 || iy0 >= iy1) return;
            if (ix0 < _x0) _x0 = ix0;
            if (iy0 < _y0) _y0 = iy0;
            if (ix1 > _x1) _x1 = ix1;
            if (iy1 > _y1) _y1 = iy1;
            Any = true;

            int sw = s.W, sh = s.H;
            var spr = s.A;
            double ku = sw / w, kv = sh / h, inv = 1 / sc;
            var fa = (float)a;
            for (var iy = iy0; iy < iy1; iy++)
            {
                var Y = ry + (iy + .5) * inv - py;
                var X0 = rx + (ix0 + .5) * inv - px;
                // local coords along the row: l = R^-1 (X, Y) - (ox, oy), stepping inv in X
                var lx = X0 * cr + Y * sr - ox;
                var ly = -X0 * sr + Y * cr - oy;
                double dlx = inv * cr, dly = -inv * sr;
                var row = iy * Dw;
                for (var ix = ix0; ix < ix1; ix++, lx += dlx, ly += dly)
                {
                    var u = lx * ku - .5;
                    var v = ly * kv - .5;
                    if (u <= -1 || v <= -1 || u >= sw || v >= sh) continue;
                    var x0 = (int)Math.Floor(u);
                    var y0 = (int)Math.Floor(v);
                    var fx = (float)(u - x0);
                    var fy = (float)(v - y0);
                    float a00 = 0, a10 = 0, a01 = 0, a11 = 0;
                    if (y0 >= 0)
                    {
                        var o = y0 * sw;
                        if (x0 >= 0) a00 = spr[o + x0];
                        if (x0 + 1 < sw) a10 = spr[o + x0 + 1];
                    }

                    if (y0 + 1 < sh)
                    {
                        var o = (y0 + 1) * sw;
                        if (x0 >= 0) a01 = spr[o + x0];
                        if (x0 + 1 < sw) a11 = spr[o + x0 + 1];
                    }

                    var sa = ((a00 * (1 - fx) + a10 * fx) * (1 - fy) + (a01 * (1 - fx) + a11 * fx) * fy) * fa;
                    if (sa <= 0) continue;
                    var d = A[row + ix];
                    A[row + ix] = sa + d * (1 - sa);
                }
            }
        }

        /// <summary>
        /// End a frame: the density bytes, the threshold M, the glow G0 (box blurs at 1/2 the density res) and its bytes.
        /// <paramref name="preBlur"/> (stage px) smooths the density before the threshold (the title's kinds).
        /// Returns the stage box the layer can reach (null-free: Rect.zero when nothing was drawn).
        /// </summary>
        public Rect End(SmokeKind kind)
        {
            _px0 = _py0 = 0;
            _px1 = -1;
            if (!Any || kind == null) return Rect.zero;
            var pad = 4 + kind.LineR + 12;   // lit / mid offsets (10 stage px) + the line, in density px (generous)
            int x0 = Math.Max(0, _x0 - pad), y0 = Math.Max(0, _y0 - pad), x1 = Math.Min(Dw, _x1 + pad), y1 = Math.Min(Dh, _y1 + pad);
            if (kind.PreBlur > 0) Binomial(A, Dw, Dh, x0, y0, x1, y1, kind.PreBlur * Scale);
            for (var y = y0; y < y1; y++)
            {
                var o = y * Dw;
                for (var x = x0; x < x1; x++)
                {
                    var v = A[o + x];
                    DBytes[o + x] = (byte)(v <= 0 ? 0 : v >= 1 ? 255 : (int)(v * 255f + .5f));
                }
            }

            // the glow: M at the density res, averaged 2x2 into the glow res, then 2 x (box3 h + box3 v)
            float sl = (float)kind.Slope, ic = (float)kind.Intercept;
            int gx0 = Math.Max(0, x0 / 2 - 8), gx1 = Math.Min(Gw, x1 / 2 + 9), gy0 = Math.Max(0, y0 / 2 - 8), gy1 = Math.Min(Gh, y1 / 2 + 9);
            for (var gy = gy0; gy < gy1; gy++)
            for (var gx = gx0; gx < gx1; gx++)
            {
                float acc = 0;
                for (var dy = 0; dy < 2; dy++)
                {
                    var yy = gy * 2 + dy;
                    if (yy >= Dh) continue;
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var xx = gx * 2 + dx;
                        if (xx >= Dw) continue;
                        var m = sl * A[yy * Dw + xx] + ic;
                        acc += m <= 0 ? 0 : m >= 1 ? 1 : m;
                    }
                }

                _m[gy * Gw + gx] = acc * .25f;
            }

            // the page's feGaussianBlur σ 9 (Chromium runs it as three box-17 passes: σ ≈ 8.5 stage px) as box passes of
            // GlowChain's widths at 1/4 stage res (4 stage px a cell): measured against the page's cel layers (p5/harness)
            var src = _m;
            foreach (var w in GlowChain)
            {
                BoxH(src, _tmp, gx0, gy0, gx1, gy1, w / 2);
                BoxV(_tmp, _g, gx0, gy0, gx1, gy1, w / 2);
                src = _g;
            }

            for (var gy = gy0; gy < gy1; gy++)
            {
                var o = gy * Gw;
                for (var gx = gx0; gx < gx1; gx++)
                {
                    var v = _g[o + gx];
                    GBytes[o + gx] = (byte)(v <= 0 ? 0 : v >= 1 ? 255 : (int)(v * 255f + .5f));
                }
            }

            for (var gy = gy0; gy < gy1; gy++)
            {
                Array.Clear(_m, gy * Gw + gx0, gx1 - gx0);
                Array.Clear(_g, gy * Gw + gx0, gx1 - gx0);
            }

            _px0 = x0;
            _py0 = y0;
            _px1 = x1;
            _py1 = y1;
            // the glow reaches ~3 sigma (27 stage px) past M; the lit side 10 px
            var inv = 1 / Scale;
            var m0 = 40;
            var bx0 = Math.Max(Region.x, Region.x + x0 * inv - m0);
            var by0 = Math.Max(Region.y, Region.y + y0 * inv - m0);
            var bx1 = Math.Min(Region.xMax, Region.x + x1 * inv + m0);
            var by1 = Math.Min(Region.yMax, Region.y + y1 * inv + m0);
            return bx1 > bx0 && by1 > by0 ? new Rect((float)bx0, (float)by0, (float)(bx1 - bx0), (float)(by1 - by0)) : Rect.zero;
        }

        /// <summary>The glow's box passes (widths, per axis) at 1/4 stage res: σ² = Σ (w² − 1) / 12 cells² ((5, 3, 3, 3):
        /// σ 2.0 cells = 8.0 stage px; against the page's cel layers p99 .86 / mean .12 levels, p5/harness/isolate.py).</summary>
        public static readonly int[] GlowChain = { 5, 3, 3, 3 };

        /// <summary>A box of radius r along x (a sliding sum; outside this frame's region the source is empty).</summary>
        void BoxH(float[] src, float[] dst, int x0, int y0, int x1, int y1, int r)
        {
            var inv = 1f / (2 * r + 1);
            for (var y = y0; y < y1; y++)
            {
                var o = y * Gw;
                float sum = 0;
                for (var x = x0; x < Math.Min(x1, x0 + r); x++) sum += src[o + x];
                for (var x = x0; x < x1; x++)
                {
                    var add = x + r;
                    if (add < x1) sum += src[o + add];
                    var sub = x - r - 1;
                    if (sub >= x0) sum -= src[o + sub];
                    dst[o + x] = sum * inv;
                }
            }
        }

        void BoxV(float[] src, float[] dst, int x0, int y0, int x1, int y1, int r)
        {
            var inv = 1f / (2 * r + 1);
            for (var x = x0; x < x1; x++)
            {
                float sum = 0;
                for (var y = y0; y < Math.Min(y1, y0 + r); y++) sum += src[y * Gw + x];
                for (var y = y0; y < y1; y++)
                {
                    var add = y + r;
                    if (add < y1) sum += src[add * Gw + x];
                    var sub = y - r - 1;
                    if (sub >= y0) sum -= src[sub * Gw + x];
                    dst[y * Gw + x] = sum * inv;
                }
            }
        }

        float[] _blur;

        /// <summary>The title kinds' pre-blur (σ <paramref name="sigma"/> density px): binomial [1 2 1]/4 passes (σ² = .5 each).</summary>
        void Binomial(float[] a, int w, int h, int x0, int y0, int x1, int y1, double sigma)
        {
            var passes = Math.Max(1, (int)Math.Round(sigma * sigma / .5));
            if (_blur == null || _blur.Length != a.Length) _blur = new float[a.Length];
            for (var p = 0; p < passes; p++)
            {
                for (var y = y0; y < y1; y++)
                {
                    var o = y * w;
                    for (var x = x0; x < x1; x++)
                    {
                        var l = x > 0 ? a[o + x - 1] : 0;
                        var r = x + 1 < w ? a[o + x + 1] : 0;
                        _blur[o + x] = (l + 2 * a[o + x] + r) * .25f;
                    }
                }

                for (var y = y0; y < y1; y++)
                {
                    var o = y * w;
                    for (var x = x0; x < x1; x++)
                    {
                        var u = y > 0 ? _blur[o - w + x] : 0;
                        var d = y + 1 < h ? _blur[o + w + x] : 0;
                        a[o + x] = (u + 2 * _blur[o + x] + d) * .25f;
                    }
                }
            }
        }
    }

    /// <summary>WRLDZ/UI/MenuCelSmoke, found once; null when it is missing or does not compile (the caller keeps the
    /// baked smoke plate).</summary>
    public static class CelSmokeShader
    {
        public const string Name = "WRLDZ/UI/MenuCelSmoke";
        static Shader _s;
        static bool _tried;

        /// <summary>Tests: behave as if the shader were missing (the plate fallback).</summary>
        public static bool ForceMissing;

        public static Shader Get()
        {
            if (ForceMissing) return null;
            if (_tried) return _s;
            _tried = true;
            try
            {
                var s = Shader.Find(Name);
                if (s != null && s.isSupported && !HasError(s)) _s = s;
            }
            catch
            {
                _s = null;
            }

            return _s;
        }

        /// <summary>In the Editor a shader with compile errors is still "found": gate on ShaderUtil.ShaderHasError
        /// (§5.6) by reflection (UnityEditor is not in the runtime assembly).</summary>
        static bool HasError(Shader s)
        {
#if UNITY_EDITOR
            try
            {
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("UnityEditor.ShaderUtil", false);
                    if (t != null) break;
                }

                var m = t != null ? t.GetMethod("ShaderHasError", new[] { typeof(Shader) }) : null;
                if (m != null) return (bool)m.Invoke(null, new object[] { s });
            }
            catch
            {
                return false;
            }
#endif
            return false;
        }

        public static void Reset()
        {
            _tried = false;
            _s = null;
        }
    }

    /// <summary>One cel layer on screen: a quad over the layer's reach (stage px) sampling the density in its region,
    /// drawn by WRLDZ/UI/MenuCelSmoke with the kind's filter constants (BUILD_PLAN D3).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [ExecuteAlways]   // OnDestroy frees the density / glow textures and the material in edit mode too (MenuSmokeTest)
    public sealed class CelSmokeGraphic : MaskableGraphic
    {
        public Rect Frame;   // the stage rect this graphic's RectTransform covers
        Rect _box, _region;
        [NonSerialized] Texture2D _dens, _glow;
        [NonSerialized] Material _mat;

        public override Texture mainTexture => _dens != null ? (Texture)_dens : s_WhiteTexture;

        public static CelSmokeGraphic Create(Transform parent, string name, Rect frame, Rect parentRect, CelBuffer buf, SmokeKind kind,
            Shader shader)
        {
            if (parent == null || buf == null || kind == null || shader == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CelSmokeGraphic));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<CelSmokeGraphic>();
            g.Frame = frame;
            g.raycastTarget = false;
            MenuV6Layout.Place(g.rectTransform, frame, parentRect);
            g._region = buf.Region;
            g._dens = new Texture2D(buf.Dw, buf.Dh, TextureFormat.R8, false, true)
            {
                name = name + "_density", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            g._glow = new Texture2D(buf.Gw, buf.Gh, TextureFormat.R8, false, true)
            {
                name = name + "_glow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            g._mat = new Material(shader) { name = name + "_cel", hideFlags = HideFlags.HideAndDontSave };
            var m = g._mat;
            m.SetTexture("_GlowTex", g._glow);
            m.SetVector("_Texel", new Vector4(1f / Mathf.Max(1f, buf.Region.width), 1f / Mathf.Max(1f, buf.Region.height), 0, 0));
            m.SetFloat("_Slope", (float)kind.Slope);
            m.SetFloat("_Intercept", (float)kind.Intercept);
            m.SetFloat("_LineR", kind.LineR);
            m.SetVector("_CFill", kind.Fill);
            m.SetVector("_CMid", kind.Mid);
            m.SetVector("_CLit", kind.Lit);
            m.SetVector("_CSliver", kind.Sliver);
            m.SetVector("_CInk", kind.Ink);
            m.SetVector("_CGlow", kind.Glow);
            m.SetFloat("_SliverA", (float)kind.SliverOpacity);
            m.SetFloat("_GlowA", (float)kind.GlowOpacity);
            m.SetFloat("_RefDark", (float)MenuV6Linear.RoomDark);
            g.material = m;
            g.color = Color.white;
            return g;
        }

        /// <summary>Upload this frame's density and glow and cover <paramref name="box"/> (stage px; zero = draw nothing).</summary>
        public void Show(CelBuffer buf, Rect box)
        {
            if (buf == null || _dens == null || _glow == null) return;
            _dens.LoadRawTextureData(buf.DBytes);
            _dens.Apply(false, false);
            _glow.LoadRawTextureData(buf.GBytes);
            _glow.Apply(false, false);
            if (box != _box)
            {
                _box = box;
                SetVerticesDirty();
            }
        }

        public void Hide()
        {
            if (_box.width <= 0) return;
            _box = Rect.zero;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_box.width <= 0 || _box.height <= 0) return;
            var map = new StageMap(Frame, rectTransform.rect);
            var c = (Color32)color;
            void V(double x, double y)
            {
                var v = UIVertex.simpleVert;
                v.position = map.P(x, y);
                v.color = c;
                v.uv0 = new Vector2((float)((x - _region.x) / _region.width), (float)((y - _region.y) / _region.height));
                vh.AddVert(v);
            }

            V(_box.xMin, _box.yMin);
            V(_box.xMax, _box.yMin);
            V(_box.xMax, _box.yMax);
            V(_box.xMin, _box.yMax);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Kill(_dens);
            Kill(_glow);
            Kill(_mat);
            _dens = _glow = null;
            _mat = null;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }

    /// <summary>A cel layer: its CPU buffer and its graphic.</summary>
    public sealed class CelLayer
    {
        public CelBuffer Buf;
        public CelSmokeGraphic Gfx;
        public SmokeKind Kind;

        public static CelLayer Create(Transform parent, string name, Rect frame, Rect parentRect, Rect region, double scale, SmokeKind kind,
            Shader shader)
        {
            if (kind == null || shader == null) return null;
            var buf = new CelBuffer(region, scale);
            var g = CelSmokeGraphic.Create(parent, name, frame, parentRect, buf, kind, shader);
            return g == null ? null : new CelLayer { Buf = buf, Gfx = g, Kind = kind };
        }

        public void Begin() => Buf.Begin();

        public void End() => Present(Compute());

        /// <summary>The CPU half (any thread: arrays only): threshold, glow, bytes; the stage box the layer reaches.</summary>
        public Rect Compute() => Buf.End(Kind);

        /// <summary>The main-thread half: upload and cover <paramref name="box"/>.</summary>
        public void Present(Rect box)
        {
            if (Gfx == null) return;
            if (box.width > 0) Gfx.Show(Buf, box);
            else Gfx.Hide();
        }

        public void Hide()
        {
            Buf.Begin();
            Buf.End(Kind);
            if (Gfx != null) Gfx.Hide();
        }

        public int Quads => Gfx != null && Buf.Any ? 1 : 0;
    }

    /// <summary>
    /// The menu's live cel smoke (BUILD_PLAN §6 P5 step 4, D3; anime_menu_v6.js:152-165 + menu.js:300-304, :1009-1016): the
    /// seep (170 / 150 dark puffs, cel kind backHi, lobes capped 54 k × .9, density × .82) and the floor pool (46 / 34 grey
    /// lobes, kind back) as two cel layers, the stateless formulas of the page (every puff a pure function of t), drawn in
    /// the P1 smoke plate's place (z 21) and faded by its entrance CanvasGroup (the page fades the layer whole). Updated at
    /// ≤ 30 Hz. The plate stays the fallback: no data, no shader, a build error, and Battery Saver (SPEC §8.7) show it.
    /// v6.3 smoke has no dread term (smoke_&lt;layout&gt;.json menu.dread).
    /// </summary>
    public sealed class SmokeField : MonoBehaviour
    {
        SmokeSet _s;
        CelLayer _pool, _seep;
        Image _plate;
        double _last = double.NaN;
        bool _live;
        Func<double> _clock;

        public const double MaxHz = 30;

        /// <summary>True while the live layers draw (false = the plate shows).</summary>
        public bool Live => _live;

        public int Quads => (_pool != null ? _pool.Quads : 0) + (_seep != null ? _seep.Quads : 0);

        /// <summary>Build the live smoke over the P1 plate <paramref name="plate"/> (its GameObject keeps the entrance's
        /// CanvasGroup; the plate Image is hidden while the live layers draw). Null = keep the plate.</summary>
        public static SmokeField Attach(Image plate, Rect plateRect, Rect stageRect, MenuLayoutKind kind, Func<double> clock)
        {
            if (plate == null) return null;
            var s = SmokeSet.Load(kind);
            var sh = CelSmokeShader.Get();
            if (s == null || sh == null || !s.HasSprites() || s.Puffs.Count == 0) return null;
            if (!s.Kinds.TryGetValue("back", out var back) || !s.Kinds.TryGetValue("backHi", out var hi)) return null;
            var f = plate.gameObject.AddComponent<SmokeField>();
            f._s = s;
            f._plate = plate;
            f._clock = clock;
            f._emit = (pool, spr, px, py, ox, oy, w, h, rot, a) => (pool ? f._pool : f._seep).Buf.Splat(spr, px, py, ox, oy, w, h, rot, a);
            try
            {
                // the pool's band (fy − 22 k .. the floor), the seep the whole stage
                var k = s.K;
                var py0 = Math.Max(0, s.PoolFy - 22 * k - 70 * k);
                var poolRegion = new Rect(0f, (float)py0, (float)s.W, (float)(s.H - py0));
                f._pool = CelLayer.Create(plate.transform, "Smoke_pool", stageRect, plateRect, poolRegion, s.Scale, back, sh);
                f._seep = CelLayer.Create(plate.transform, "Smoke_seep", stageRect, plateRect, stageRect, s.Scale, hi, sh);
                if (f._pool == null || f._seep == null) throw new Exception("cel layer");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] live smoke kept the baked plate: " + ex.Message);
                f.Teardown();
                return null;
            }

            f.Tick(true);
            return f;
        }

        void Teardown()
        {
            foreach (var l in new[] { _pool, _seep })
                if (l != null && l.Gfx != null)
                {
                    if (Application.isPlaying) Destroy(l.Gfx.gameObject);
                    else DestroyImmediate(l.Gfx.gameObject);
                }

            _pool = _seep = null;
            if (_plate != null) _plate.enabled = true;
            if (Application.isPlaying) Destroy(this);
            else DestroyImmediate(this);
        }

        void LateUpdate() => Tick(false);

        /// <summary>Draw the smoke at the current clock (tests / captures: <paramref name="force"/> redraws now).</summary>
        public void Tick(bool force)
        {
            var saver = WrldzTheme.BatterySaver;
            if (saver || _s == null)
            {
                if (_live || force) SetLive(false);
                return;
            }

            if (!_live) SetLive(true);
            var t = _clock != null ? _clock() : MenuV6Clock.Now;
            // a finished worker frame goes up first (its bytes are free again once uploaded)
            if (_job != null)
            {
                if (!_job.IsCompleted && !force) return;
                try { _job.Wait(); }
                catch { _async = false; }   // a worker that threw: the field draws on the main thread from now on
                if (_job.IsCompletedSuccessfully)
                {
                    _pool.Present(_jobBoxPool);
                    _seep.Present(_jobBoxSeep);
                }

                _job = null;
            }

            if (!force && !double.IsNaN(_last) && Math.Abs(t - _last) < 1 / MaxHz && t >= _last) return;
            _last = t;
            // P5 budget: in Play mode the splat, threshold and glow run on a worker (pure arrays) and upload on the next
            // update (one update behind the clock: the smoke moves a few px a second); captures (a frozen clock), tests and
            // edit mode draw on the main thread at the exact t
            if (_async && Application.isPlaying && !MenuV6Clock.Frozen && !force)
            {
                var tt = t;
                _job = System.Threading.Tasks.Task.Run(() =>
                {
                    _pool.Begin();
                    _seep.Begin();
                    Draws(_s, tt, _emit);
                    _jobBoxPool = _pool.Compute();
                    _jobBoxSeep = _seep.Compute();
                });
                return;
            }

            DrawAt(t);
        }

        System.Threading.Tasks.Task _job;
        Rect _jobBoxPool, _jobBoxSeep;
        bool _async = true;

        void SetLive(bool on)
        {
            if (_job != null)
            {
                try { _job.Wait(); }
                catch { _async = false; }
                _job = null;
            }

            _live = on;
            if (_plate != null) _plate.enabled = !on;
            foreach (var l in new[] { _pool, _seep })
            {
                if (l == null || l.Gfx == null) continue;
                l.Gfx.enabled = on;
                if (!on) l.Hide();
            }

            if (on) _last = double.NaN;
        }

        /// <summary>The page's seep + pool at menu time t (per-puff e = 1; the layer's entrance alpha is the CanvasGroup's).</summary>
        public void DrawAt(double t)
        {
            if (_job != null)
            {
                try { _job.Wait(); }
                catch { _async = false; }
                _job = null;
            }

            _pool.Begin();
            _seep.Begin();
            Draws(_s, t, _emit);
            _pool.End();
            _seep.End();
        }

        Puff _emit;   // one delegate for the field's life (no per-frame allocation)

        /// <summary>One puff draw: (into the pool layer?, lobe, translate px py, rotate rot, drawImage ox oy w h, globalAlpha).</summary>
        public delegate void Puff(bool pool, LobeSprite s, double px, double py, double ox, double oy, double w, double h, double rot, double a);

        /// <summary>The page's draws in its order (anime_menu_v6.js:155-164): the seep (kind backHi) then the floor pool (kind
        /// back). Pure: the harness checks it against the page's own drawImage trace.</summary>
        public static void Draws(SmokeSet s, double t, Puff emit)
        {
            var k = s.K;
            // the seep (kind backHi): separate billows, lobes capped at 54 k, density x .82
            for (var i = 0; i < s.Puffs.Count; i++)
            {
                var p = s.Puffs[i];
                Pos(s, p, i, t, out var x, out var y, out var age);
                var sz = Math.Min(p.S0 + p.Gs * age, 54 * k) * .9;
                var a = MenuV6Math.Win(age, 0, .9, p.Life - 1.8, p.Life);
                if (a < .01) continue;
                var ga = Math.Min(1, a * (p.Dark + .15) * .82);
                emit(false, s.Dark[p.K & 3], x, y, -sz / 2, -sz / 2, sz, sz, p.Rot + p.Vr * age, ga);
            }

            // the floor pool (kind back): ONE continuous low bank (overlapping lobes merge)
            var n = s.PoolN;
            var span = s.W + 240;
            for (var i = 0; i < n; i++)
            {
                var ph = MenuV6Math.Hash2(i, 3, 9);
                var x = (i * s.PoolSp + t * (6 + 5 * ph)) % span - 120;
                var r = (30 + 26 * MenuV6Math.Hash2(i, 5, 9) + 8 * Math.Sin(t * .5 + i)) * k;
                var y = s.PoolFy + 18 * k + 8 * k * Math.Sin(t * .35 + i * 1.7);
                emit(true, s.Grey[i % 2], x - r * 1.3, y - r * .5, 0, 0, r * 2.6, r * 1.0, 0, .8);
            }
        }

        /// <summary>menu.js smokePos (v6.3): the emitter of this cycle, the drift, the keep-out ring round the door, the floor.</summary>
        public static void Pos(SmokeSet s, in SmokeSet.Puff p, int i, double t, out double x, out double y, out double age)
        {
            var k = s.K;
            var tt2 = t + p.Off;
            var cyc = Math.Floor(tt2 / p.Life);
            age = tt2 - cyc * p.Life;
            Vector2 E;
            if (p.Fis && s.FisP.Count > 0) E = s.FisP[Math.Min(s.FisP.Count - 1, (int)Math.Floor(MenuV6Math.Hash2(i, (int)cyc, 9) * s.FisP.Count))];
            else if (s.Emit.Count > 0) E = s.Emit[Math.Min(s.Emit.Count - 1, (int)Math.Floor(MenuV6Math.Hash2(i, (int)cyc, 5) * s.Emit.Count))];
            else E = new Vector2((float)s.Cx, (float)s.Cy);
            x = E.x + p.Vx * age * k;
            y = E.y + (p.Fis ? -8 * age + 2.2 * age * age : p.Vy * age + .6 * age * age) * k;
            double cx = s.KCx, cy = s.KCy, KR = s.KR;
            double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < KR)
            {
                var kk = KR / (d > 0 ? d : 1);
                x = cx + dx * MenuV6Math.Lerp(1, kk, .92);
                y = cy + dy * MenuV6Math.Lerp(1, kk, .92);
            }

            var fy = s.FloorY;
            if (y > fy - 6)
            {
                var over = y - (fy - 6);
                y = fy - 6 + over * .1;
                x += MenuV6Math.Clamp((x - s.Cx) / (36 * k), -1, 1) * over * .75;
            }
        }
    }

    /// <summary>
    /// The title's fog banks (BUILD_PLAN §6 P5 step 4; smoke_&lt;layout&gt;.json title, anime_v61_title.js:147-150): 18 dark
    /// banks drifting along the ground (kind blackT: Azathot's smoke is black, the density smoothed before its steep
    /// threshold), on the title's BASE clock (origR: t − 2.6 after the origin freeze, title.js:31), e = seg(t, 17.0, 17.6),
    /// then the title's swell (anime_fix1_title.js:41-45: a puff's alpha also sets its size, × min(1, 1.65 a)) and the
    /// batcher's scale (1.45 before base 9.5, .85 after). Drawn under Nut's part (her clip, the P2 stack). Battery Saver:
    /// drawn once and held (end state). Null without the P5 data or the shader (the title had no fog before P5).
    /// </summary>
    public sealed class TitleFog
    {
        SmokeSet _s;
        CelLayer _l;
        double _last = double.NaN;
        bool _held;
        Bank _emit;

        public int Quads => _l != null ? _l.Quads : 0;

        public static TitleFog Build(RectTransform stage, Rect stageRect, MenuLayoutKind kind)
        {
            if (stage == null) return null;
            var s = SmokeSet.Load(kind);
            var sh = CelSmokeShader.Get();
            if (s == null || sh == null || s.TitleFog.Count == 0 || !s.HasSprites() || !s.Kinds.TryGetValue("blackT", out var kd)) return null;
            double y0 = double.MaxValue, y1 = double.MinValue;
            foreach (var f in s.TitleFog)
            {
                y0 = Math.Min(y0, f.Y - f.S * .3 * 1.45);
                y1 = Math.Max(y1, f.Y + f.S * .3 * 1.45);
            }

            y0 = Math.Max(stageRect.yMin, y0 - 60);
            y1 = Math.Min(stageRect.yMax, y1 + 60);
            if (y1 <= y0) return null;
            var region = new Rect(stageRect.x, (float)y0, stageRect.width, (float)(y1 - y0));
            var go = new GameObject("TitleFog", typeof(RectTransform));
            go.transform.SetParent(stage, false);
            UiTheme.Stretch(go.GetComponent<RectTransform>());
            var l = CelLayer.Create(go.transform, "TitleFog_cel", stageRect, stageRect, region, s.Scale, kd, sh);
            if (l == null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
                return null;
            }

            return new TitleFog { _s = s, _l = l };
        }

        public delegate void Bank(LobeSprite s, double x, double y, double w, double h, double a);

        /// <summary>The fog's draws at base time <paramref name="tb"/> after the swell and the batcher scale (the rect the
        /// batch canvas receives, the alpha it receives). Pure (the harness checks it against the page's trace).</summary>
        public static void Draws(SmokeSet s, double tb, Bank emit)
        {
            var W = s.W;
            var e = MenuV6Math.Seg(tb, 17.0, 17.6);
            var sc = tb < 9.5 ? 1.45 : .85;
            foreach (var f in s.TitleFog)
            {
                var span = W + f.S * 1.5;
                var x = ((f.X + f.Vx * tb) % span + span) % span - f.S * .75;
                var a = f.A * (.75 + .25 * Math.Sin(tb * .4 + f.Ph)) * e;
                if (a < .005) continue;
                var ga = a * .9;
                double rx = x - f.S / 2, ry = f.Y - f.S * .3, rw = f.S, rh = f.S * .6;
                if (ga < .999)
                {
                    // the swell: the rect scales about its centre by .3 + .7 eOut(clamp(a / .6)), alpha -> min(1, 1.65 a)
                    var k = MenuV6Math.Clamp(ga / .6, 0, 1);
                    var sz = .3 + .7 * MenuV6Math.Out(k);
                    rx += rw * (1 - sz) / 2;
                    ry += rh * (1 - sz) / 2;
                    rw *= sz;
                    rh *= sz;
                    ga = Math.Min(1, ga * 1.65);
                }

                // the batcher's scale about the centre
                rx -= rw * (sc - 1) / 2;
                ry -= rh * (sc - 1) / 2;
                rw *= sc;
                rh *= sc;
                emit(s.Dark[f.K & 3], rx, ry, rw, rh, ga);
            }
        }

        /// <summary>Draw at base time <paramref name="tb"/> (≤ 30 Hz).</summary>
        public void Tick(double tb)
        {
            if (_l == null) return;
            if (WrldzTheme.BatterySaver)
            {
                if (_held) return;
                _held = true;
            }
            else _held = false;

            if (!double.IsNaN(_last) && Math.Abs(tb - _last) < 1 / SmokeField.MaxHz && tb >= _last && !_held) return;
            _last = tb;
            _l.Begin();
            _emit ??= (spr, rx, ry, rw, rh, ga) => _l.Buf.Splat(spr, rx, ry, 0, 0, rw, rh, 0, ga);
            Draws(_s, tb, _emit);
            _l.End();
        }
    }
}
