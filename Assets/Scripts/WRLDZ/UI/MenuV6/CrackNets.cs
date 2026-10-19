using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    // =====================================================================================================================
    // The crack engine's pure half (BUILD_PLAN D11, §6 P2 step 2): the networks the page built (exported verbatim to
    // Data/cracks_<page>_<layout>.json: branches, holes and spans, hairline webs, palettes), their timing (grow-in, creep by
    // time only, breathe, the LivingSky spasm, the time stutter), and the C# port of anime_v61_cracks.js shapeOf (:166-189),
    // holeOpen (:148), spallOf (:224-242) and drawNets (:243-302) as vertex-colour strips: chipped lips, the gap onto the
    // Shadow Realm, the dark depth bands, the ink rim, the hairlines and the web. No engine calls beyond UnityEngine's value
    // types, so the offline harness records exactly the triangles Unity draws (CrackNetGraphic maps them into UGUI).
    // Nothing reacts to the player (CRACKS.md §3): every value is a function of the clock and the exported seeds.
    // =====================================================================================================================

    /// <summary>One network's spec (the page's CRK.SPECS row as exported, defaults resolved as the module reads them).</summary>
    public sealed class CrackSpec
    {
        public string Id, Mat;
        public double Rest = .62, Creep = .3, Tau = 80, RootW = .55, Lit = 1, MistN = 2, MistUp = 1, MistRoom = 14, MistS = 1;
        public double Delay, ChipHole = 1, ChipK = 1, InkK = 1, WebLit = .75, DepthK = 1;
        public bool Bolt;
        public double ZoneTop = 1e9;                       // min zone y (the mist's room)
        public double Zx0 = 1e9, Zy0 = 1e9, Zx1 = -1e9, Zy1 = -1e9;   // zones' bounding box (the bolts stay on their wall)
    }

    public sealed class CrackBranch
    {
        public int Id, Depth, N;
        public double W, L, NUx, NUy;
        public double[] X, Y, Ds, Ju, Jl, Pin, MU, ML;
    }

    public sealed class CrackHole
    {
        public int I;
        public double D, Px, Py, Kw, R, Span;
    }

    public sealed class CrackWeb
    {
        public double D0;
        public double[] X, Y;
        public readonly List<(int m, double[] x, double[] y)> Kids = new();
    }

    /// <summary>A network as finishNet left it (anime_v61_cracks.js:104-138).</summary>
    public sealed class CrackNet
    {
        public string Id;
        public CrackSpec Sp;
        public double K = 1, L;
        public int Seed;
        public CrackBranch Main;
        public CrackBranch[] Br;
        public CrackHole[] Holes;
        public CrackWeb[] Web;
        public Color32[] Pal;          // [lit chip wall, chip floor, chip shadow] (palOf at build), null -> MAT[mat]
        public Color32[] Mat;          // resolved colours (Pal ?? MAT[sp.mat] ?? MAT.wall)
    }

    /// <summary>One cracks_&lt;page&gt;_&lt;layout&gt;.json: the nets, Nut's fracture, the bolt nets and the look constants.</summary>
    public sealed class CrackSet
    {
        public string Page, Layout;
        public double K = 1, W = 1920, H = 1080;
        public readonly List<CrackNet> Nets = new();
        public CrackNet Nut;
        public readonly List<CrackNet> BoltNets = new();
        public readonly Dictionary<string, Color32[]> MatTable = new();
        public double RiftRun = .78;

        public static Color32 Hex(string s, Color32 fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6) return fallback;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return fallback;
            return new Color32((byte)((v >> 16) & 255), (byte)((v >> 8) & 255), (byte)(v & 255), 255);
        }

        static readonly Color32 WallLit = new(0x6A, 0x4E, 0x36, 255), WallBase = new(0x3A, 0x2B, 0x21, 255), WallSh = new(0x1A, 0x13, 0x0F, 255);

        /// <summary>Parse a cracks JSON root (MenuV6Json). Throws on a malformed file (the caller keeps the baked plate).</summary>
        public static CrackSet From(Dictionary<string, object> root)
        {
            var set = new CrackSet
            {
                Page = RJ.S(root, "page"),
                Layout = RJ.S(root, "layout"),
                K = RJ.N(root, "k", 1)
            };
            var st = RJ.D(root, "stage");
            set.W = RJ.N(st, "W", 1920);
            set.H = RJ.N(st, "H", 1080);
            var look = RJ.D(root, "look");
            var mat = RJ.D(look, "MAT");
            if (mat != null)
                foreach (var kv in mat)
                    if (kv.Value is List<object> l && l.Count >= 3)
                        set.MatTable[kv.Key] = new[] { Hex(l[0] as string, WallLit), Hex(l[1] as string, WallBase), Hex(l[2] as string, WallSh) };
            if (!set.MatTable.ContainsKey("wall")) set.MatTable["wall"] = new[] { WallLit, WallBase, WallSh };
            var nets = RJ.L(root, "nets");
            if (nets != null)
                foreach (var n in nets)
                    if (n is Dictionary<string, object> nd)
                        set.Nets.Add(NetFrom(nd, set));
            if (RJ.D(root, "nut") is { } nut) set.Nut = NetFrom(nut, set);
            var rift = RJ.D(root, "rift");
            set.RiftRun = RJ.N(rift, "riftRun", .78);
            var bolts = RJ.L(root, "boltNets");
            if (bolts != null)
                foreach (var b in bolts)
                    foreach (var n in set.Nets)
                        if (n.Id == b as string)
                            set.BoltNets.Add(n);
            return set;
        }

        static double[] Arr(List<object> l)
        {
            if (l == null) return Array.Empty<double>();
            var a = new double[l.Count];
            for (var i = 0; i < a.Length; i++) a[i] = l[i] is double d ? d : 0;
            return a;
        }

        static void Pts(List<object> l, out double[] x, out double[] y)
        {
            var n = l?.Count ?? 0;
            x = new double[n];
            y = new double[n];
            for (var i = 0; i < n; i++)
                if (l[i] is List<object> p)
                {
                    x[i] = RJ.At(p, 0);
                    y[i] = RJ.At(p, 1);
                }
        }

        static double Opt(Dictionary<string, object> sp, string k, double def) =>
            sp != null && sp.TryGetValue(k, out var v) && v is double d ? d : def;

        // JS `x || def`: 0 / missing -> def
        static double OrDef(Dictionary<string, object> sp, string k, double def)
        {
            var v = Opt(sp, k, 0);
            return v == 0 ? def : v;
        }

        static CrackNet NetFrom(Dictionary<string, object> nd, CrackSet set)
        {
            var spd = RJ.D(nd, "sp");
            var sp = new CrackSpec
            {
                Id = RJ.S(spd, "id") ?? RJ.S(nd, "id"),
                Mat = RJ.S(spd, "mat") ?? "wall",
                Rest = Opt(spd, "rest", .62),
                Creep = Opt(spd, "creep", .3),
                Tau = OrDef(spd, "tau", 80),
                RootW = Opt(spd, "rootW", .55),
                Lit = Opt(spd, "lit", 1),
                MistN = Opt(spd, "mistN", 2),
                MistUp = Opt(spd, "mistUp", 1),
                MistRoom = Opt(spd, "mistRoom", 14),
                MistS = OrDef(spd, "mistS", 1),
                Delay = OrDef(spd, "delay", 0),
                ChipHole = Opt(spd, "chipHole", 1),
                ChipK = OrDef(spd, "chipK", 1),
                InkK = OrDef(spd, "inkK", 1),
                WebLit = Opt(spd, "webLit", .75),
                DepthK = Opt(spd, "depthK", 1),
                Bolt = Opt(spd, "bolt", 0) != 0
            };
            var zones = RJ.L(spd, "zones");
            if (zones == null && RJ.L(spd, "zone") is { } z1) zones = new List<object> { z1 };
            if (zones != null)
                foreach (var z in zones)
                    if (z is List<object> zz && zz.Count >= 4)
                    {
                        double x0 = RJ.At(zz, 0), y0 = RJ.At(zz, 1), x1 = RJ.At(zz, 2), y1 = RJ.At(zz, 3);
                        sp.ZoneTop = Math.Min(sp.ZoneTop, y0);
                        sp.Zx0 = Math.Min(sp.Zx0, x0);
                        sp.Zy0 = Math.Min(sp.Zy0, y0);
                        sp.Zx1 = Math.Max(sp.Zx1, x1);
                        sp.Zy1 = Math.Max(sp.Zy1, y1);
                    }

            var net = new CrackNet
            {
                Id = RJ.S(nd, "id") ?? sp.Id,
                Sp = sp,
                K = RJ.N(nd, "k", set.K),
                Seed = (int)RJ.N(nd, "seed"),
                L = RJ.N(nd, "L")
            };
            var brs = RJ.L(nd, "br") ?? new List<object>();
            net.Br = new CrackBranch[brs.Count];
            for (var j = 0; j < brs.Count; j++)
            {
                var b = brs[j] as Dictionary<string, object>;
                var br = new CrackBranch
                {
                    Id = (int)RJ.N(b, "id", j), Depth = (int)RJ.N(b, "depth"), W = RJ.N(b, "w"), L = RJ.N(b, "L"),
                    Ds = Arr(RJ.L(b, "ds")), Ju = Arr(RJ.L(b, "ju")), Jl = Arr(RJ.L(b, "jl")), Pin = Arr(RJ.L(b, "pin")),
                    MU = Arr(RJ.L(b, "mU")), ML = Arr(RJ.L(b, "mL"))
                };
                Pts(RJ.L(b, "pts"), out br.X, out br.Y);
                br.N = br.X.Length;
                var nul = RJ.L(b, "nUL");
                br.NUx = RJ.At(nul, 0);
                br.NUy = RJ.At(nul, 1);
                net.Br[j] = br;
            }

            var mi = (int)RJ.N(nd, "main");
            net.Main = mi >= 0 && mi < net.Br.Length ? net.Br[mi] : net.Br.Length > 0 ? net.Br[0] : null;
            var hs = RJ.L(nd, "holes") ?? new List<object>();
            net.Holes = new CrackHole[hs.Count];
            for (var i = 0; i < hs.Count; i++)
            {
                var h = hs[i] as Dictionary<string, object>;
                var p = RJ.L(h, "p");
                net.Holes[i] = new CrackHole
                {
                    I = (int)RJ.N(h, "i"), D = RJ.N(h, "d"), Px = RJ.At(p, 0), Py = RJ.At(p, 1), Kw = RJ.N(h, "kw"),
                    R = RJ.N(h, "r"), Span = RJ.N(h, "span")
                };
            }

            var ws = RJ.L(nd, "web") ?? new List<object>();
            net.Web = new CrackWeb[ws.Count];
            for (var i = 0; i < ws.Count; i++)
            {
                var w = ws[i] as Dictionary<string, object>;
                var web = new CrackWeb { D0 = RJ.N(w, "d0") };
                Pts(RJ.L(w, "P"), out web.X, out web.Y);
                var kids = RJ.L(w, "kids");
                if (kids != null)
                    foreach (var k in kids)
                    {
                        if (k is not Dictionary<string, object> kd) continue;
                        Pts(RJ.L(kd, "P"), out var kx, out var ky);
                        web.Kids.Add(((int)RJ.N(kd, "m"), kx, ky));
                    }

                net.Web[i] = web;
            }

            if (RJ.L(nd, "pal") is { Count: >= 3 } pal)
                net.Pal = new[] { Hex(pal[0] as string, WallLit), Hex(pal[1] as string, WallBase), Hex(pal[2] as string, WallSh) };
            net.Mat = net.Pal ?? (set.MatTable.TryGetValue(sp.Mat, out var m) ? m : set.MatTable["wall"]);
            return net;
        }
    }

    /// <summary>One network to draw this frame: CRK.drawNets' item {N, E (px of path shown), a, open, mist}.</summary>
    public struct CrackItem
    {
        public CrackNet N;
        public double E, A, Open, Mist;
    }

    /// <summary>A scheduled beat (CRK.schedule: {t, r, r2, i}).</summary>
    public struct CrackBeat
    {
        public double T, R, R2;
        public int I;
    }

    /// <summary>The module's timing helpers (anime_v61_cracks.js:368-391), pure functions of the clock and the seeds.</summary>
    public static class CrackTime
    {
        public static double SStep(double a, double b, double x) => MenuV6Math.SStep(a, b, x);
        public static double Seg(double t, double a, double b) => MenuV6Math.Seg(t, a, b);
        public static double Clamp(double x, double a, double b) => x < a ? a : x > b ? b : x;

        /// <summary>CRK.extent: E = L clamp(rest intro + creep (1 - e^(-tl / tau)) intro, 0, 1) (no lurch: v6.2 removed it).</summary>
        public static double Extent(CrackNet n, double intro, double tl)
        {
            var sp = n.Sp;
            var p = sp.Rest * intro + sp.Creep * (1 - Math.Exp(-Math.Max(0, tl) / sp.Tau)) * intro;
            return n.L * Clamp(p, 0, 1);
        }

        /// <summary>The Tear's rare spasm (LivingSky.cs l.110): max(0, sin(.21 tc + 1.7 seed))^26.</summary>
        public static double Spasm(double tc, int seed) => Math.Pow(Math.Max(0, Math.Sin(tc * .21 + seed * 1.7)), 26);

        /// <summary>How far a spalled hole has broken open at extent E (v6.3).</summary>
        public static double HoleOpen(CrackHole h, double e) => SStep(0, 1, (e - (h.D - h.R)) / (h.Span != 0 ? h.Span : 1));

        /// <summary>The breathe: 1 + .03 sin(tc 2π / 5.3 + seed) + .16 spasm.</summary>
        public static double Open(double tc, int seed) => 1 + .03 * Math.Sin(tc * Math.PI * 2 / 5.3 + seed) + .16 * Spasm(tc, seed);

        /// <summary>CRK.schedule(seed, first, gapA, gapB, n): rng(seed) list of {t, r, r2, i}.</summary>
        public static CrackBeat[] Schedule(uint seed, double first, double gapA, double gapB, int n = 200)
        {
            var r = MenuV6Math.Rng(seed);
            var o = new CrackBeat[n];
            var t = first;
            for (var i = 0; i < n; i++)
            {
                var a = r();
                var b = r();
                o[i] = new CrackBeat { T = t, R = a, R2 = b, I = i };
                t += gapA + (gapB - gapA) * r();
            }

            return o;
        }

        /// <summary>The TIME STUTTER (lore l.64): crackTime(list, t) -> tc, waver, dir.</summary>
        public static double StutterTime(CrackBeat[] list, double t, out double waver, out int dir)
        {
            waver = 0;
            dir = 1;
            if (list == null) return t;
            foreach (var s in list)
            {
                if (s.T > t) break;
                var u = t - s.T;
                if (u >= .85) continue;
                const double hold = .26;
                var tc = u < hold ? s.T + u * .08 : MenuV6Math.Lerp(s.T + hold * .08, t, MenuV6Math.InOut((u - hold) / (.85 - hold)));
                waver = Math.Sin(Math.PI * Clamp(u / .85, 0, 1));
                dir = s.R < .5 ? -1 : 1;
                return tc;
            }

            return t;
        }

        /// <summary>One strike's envelope: eased in over ~1.5 frames, out over ~0.2 s.</summary>
        public static double StrikeEnv(double u) =>
            u < 0 || u > .3 ? 0 : u < .05 ? MenuV6Math.Out(u / .05) : u < .11 ? 1 : 1 - MenuV6Math.InOut((u - .11) / .19);
    }

    /// <summary>A vertex-colour (optionally UV-mapped) triangle list in stage px, built from strips (every crack part is a
    /// strip: two lips joined rung by rung), with an edge ramp of <see cref="Feather"/> px for the canvas's antialiasing.</summary>
    public sealed class CrackMesh
    {
        public readonly List<StageVert> V = new();
        public readonly List<int> I = new();
        public float Feather = .9f;

        /// <summary>Realm UV: u = (x + UvDx) / UvTile, v = 1 - (y + UvDy) / UvTile (0 = no UVs).</summary>
        public double UvTile, UvDx, UvDy;

        public void Clear()
        {
            V.Clear();
            I.Clear();
        }

        int Add(double x, double y, Color32 c)
        {
            var sv = new StageVert { X = x, Y = y, C = c };
            if (UvTile > 0)
            {
                sv.U = (float)((x + UvDx) / UvTile);
                sv.V = (float)(1 - (y + UvDy) / UvTile);
            }

            V.Add(sv);
            return V.Count - 1;
        }

        void T(int a, int b, int c)
        {
            I.Add(a);
            I.Add(b);
            I.Add(c);
        }

        static Color32 A(Color32 c, double a)
        {
            c.a = (byte)Math.Round(Math.Max(0, Math.Min(1, a)) * 255);
            return c;
        }

        // scratch for the strip's four edges
        double[] _l1x = new double[64], _l1y = new double[64], _l0x = new double[64], _l0y = new double[64];
        double[] _r1x = new double[64], _r1y = new double[64], _r0x = new double[64], _r0y = new double[64], _cv = new double[64];

        void Grow(int n)
        {
            if (_l1x.Length >= n) return;
            var m = Math.Max(n, _l1x.Length * 2);
            _l1x = new double[m]; _l1y = new double[m]; _l0x = new double[m]; _l0y = new double[m];
            _r1x = new double[m]; _r1y = new double[m]; _r0x = new double[m]; _r0y = new double[m]; _cv = new double[m];
        }

        /// <summary>Fill the strip whose rungs run from (lx, ly)[o + i] to (rx, ry)[o + i], i = 0..n-1: the canvas fill of the
        /// polygon L[0..n) + R(n..0], its edges a ramp of <see cref="Feather"/> px centred on the outline; a rung thinner
        /// than the ramp keeps its coverage as a lower peak (so hairlines fade instead of breaking up).</summary>
        public void Strip(double[] lx, double[] ly, double[] rx, double[] ry, int o, int n, Color32 c, double alpha)
        {
            if (n < 2 || alpha <= .002) return;
            Grow(n);
            var f = Math.Max(1e-3, Feather);
            double ux = 0, uy = 0;
            var have = false;
            for (var i = 0; i < n; i++)
            {
                double dx = lx[o + i] - rx[o + i], dy = ly[o + i] - ry[o + i];
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1e-6)
                {
                    ux = dx / len;
                    uy = dy / len;
                    have = true;
                }
                else if (!have)
                {
                    // a zero rung before any real one: the direction of the first real rung, else across the strip's run
                    for (var j = i + 1; j < n; j++)
                    {
                        double ex = lx[o + j] - rx[o + j], ey = ly[o + j] - ry[o + j];
                        var el = Math.Sqrt(ex * ex + ey * ey);
                        if (el > 1e-6)
                        {
                            ux = ex / el;
                            uy = ey / el;
                            have = true;
                            break;
                        }
                    }

                    if (!have)
                    {
                        double tx = lx[o + n - 1] - lx[o], ty = ly[o + n - 1] - ly[o];
                        var tl = Math.Sqrt(tx * tx + ty * ty);
                        if (tl < 1e-9) return;
                        ux = -ty / tl;
                        uy = tx / tl;
                        have = true;
                    }
                }

                var half = len * .5;
                var io = Math.Min(f * .5, half);
                _l0x[i] = lx[o + i] - ux * io;
                _l0y[i] = ly[o + i] - uy * io;
                _r0x[i] = rx[o + i] + ux * io;
                _r0y[i] = ry[o + i] + uy * io;
                _l1x[i] = lx[o + i] + ux * f * .5;
                _l1y[i] = ly[o + i] + uy * f * .5;
                _r1x[i] = rx[o + i] - ux * f * .5;
                _r1y[i] = ry[o + i] - uy * f * .5;
                _cv[i] = len < f ? len / f : 1;
            }

            var b = V.Count;
            for (var i = 0; i < n; i++)
            {
                var cc = A(c, alpha * _cv[i]);
                var c0 = A(c, 0);
                Add(_l1x[i], _l1y[i], c0);
                Add(_l0x[i], _l0y[i], cc);
                Add(_r0x[i], _r0y[i], cc);
                Add(_r1x[i], _r1y[i], c0);
            }

            for (var i = 0; i + 1 < n; i++)
            {
                int p = b + i * 4, q = b + (i + 1) * 4;
                T(p + 1, q + 1, q + 2);   // core
                T(q + 2, p + 2, p + 1);
                T(p, q, q + 1);           // left ramp
                T(q + 1, p + 1, p);
                T(p + 2, q + 2, q + 3);   // right ramp
                T(q + 3, p + 3, p + 2);
            }
        }

        // scratch for strokes / bands / tapers
        double[] _sx = new double[64], _sy = new double[64], _tx = new double[64], _ty = new double[64];

        void GrowS(int n)
        {
            if (_sx.Length >= n) return;
            var m = Math.Max(n, _sx.Length * 2);
            _sx = new double[m];
            _sy = new double[m];
            _tx = new double[m];
            _ty = new double[m];
        }

        /// <summary>A stroked polyline of width <paramref name="w"/> (canvas lineJoin round ≈ a miter capped at 2×, lineCap
        /// round ≈ a three-rung semicircle) centred on the points; <paramref name="closed"/> joins the last point to the
        /// first.</summary>
        public void Stroke(double[] x, double[] y, int o, int n, double w, Color32 c, double alpha, bool closed = false, bool caps = true)
        {
            if (n < 2 || w <= 0 || alpha <= .002) return;
            var hw = w * .5;
            var cap = !closed && caps ? 3 : 0;
            var m = n + (closed ? 1 : 0) + cap * 2;
            GrowS(m);
            var k = 0;
            double t0x = 0, t0y = 0, t1x = 0, t1y = 0;
            // the tangents at the ends (open lines)
            if (!closed)
            {
                Tan(x, y, o, n, 0, false, out t0x, out t0y);
                Tan(x, y, o, n, n - 1, false, out t1x, out t1y);
            }

            if (cap > 0)
            {
                // the start cap: points behind the first, narrowing to the tip
                for (var j = cap; j >= 1; j--)
                {
                    var phi = j * (Math.PI * .5) / (cap + .35);
                    var back = hw * Math.Sin(phi);
                    var hh = hw * Math.Cos(phi);
                    double px = x[o] - t0x * back, py = y[o] - t0y * back;
                    _sx[k] = px - t0y * hh;
                    _sy[k] = py + t0x * hh;
                    _tx[k] = px + t0y * hh;
                    _ty[k] = py - t0x * hh;
                    k++;
                }
            }

            for (var i = 0; i < n + (closed ? 1 : 0); i++)
            {
                var ii = i % n;
                Miter(x, y, o, n, ii, closed, hw, out var ox, out var oy);
                _sx[k] = x[o + ii] + ox;
                _sy[k] = y[o + ii] + oy;
                _tx[k] = x[o + ii] - ox;
                _ty[k] = y[o + ii] - oy;
                k++;
            }

            if (cap > 0)
            {
                for (var j = 1; j <= cap; j++)
                {
                    var phi = j * (Math.PI * .5) / (cap + .35);
                    var fwd = hw * Math.Sin(phi);
                    var hh = hw * Math.Cos(phi);
                    double px = x[o + n - 1] + t1x * fwd, py = y[o + n - 1] + t1y * fwd;
                    _sx[k] = px - t1y * hh;
                    _sy[k] = py + t1x * hh;
                    _tx[k] = px + t1y * hh;
                    _ty[k] = py - t1x * hh;
                    k++;
                }
            }

            Strip(_sx, _sy, _tx, _ty, 0, k, c, alpha);
        }

        static void Tan(double[] x, double[] y, int o, int n, int i, bool closed, out double tx, out double ty)
        {
            int a, b;
            if (closed)
            {
                a = (i - 1 + n) % n;
                b = (i + 1) % n;
            }
            else
            {
                a = Math.Max(0, i - 1);
                b = Math.Min(n - 1, i + 1);
            }

            tx = x[o + b] - x[o + a];
            ty = y[o + b] - y[o + a];
            var l = Math.Sqrt(tx * tx + ty * ty);
            if (l < 1e-9)
            {
                tx = 1;
                ty = 0;
                return;
            }

            tx /= l;
            ty /= l;
        }

        static void Miter(double[] x, double[] y, int o, int n, int i, bool closed, double hw, out double ox, out double oy)
        {
            // the normals of the two segments meeting at i, averaged; the offset hw / cos(half angle), capped at 2 hw
            double n0x = 0, n0y = 0, n1x = 0, n1y = 0;
            bool h0 = false, h1 = false;
            int p = closed ? (i - 1 + n) % n : i - 1, q = closed ? (i + 1) % n : i + 1;
            if (p >= 0 && p < n)
            {
                double ex = x[o + i] - x[o + p], ey = y[o + i] - y[o + p];
                var l = Math.Sqrt(ex * ex + ey * ey);
                if (l > 1e-9)
                {
                    n0x = -ey / l;
                    n0y = ex / l;
                    h0 = true;
                }
            }

            if (q >= 0 && q < n)
            {
                double ex = x[o + q] - x[o + i], ey = y[o + q] - y[o + i];
                var l = Math.Sqrt(ex * ex + ey * ey);
                if (l > 1e-9)
                {
                    n1x = -ey / l;
                    n1y = ex / l;
                    h1 = true;
                }
            }

            if (!h0 && !h1)
            {
                ox = 0;
                oy = hw;
                return;
            }

            if (!h0)
            {
                ox = n1x * hw;
                oy = n1y * hw;
                return;
            }

            if (!h1)
            {
                ox = n0x * hw;
                oy = n0y * hw;
                return;
            }

            double mx = n0x + n1x, my = n0y + n1y;
            var ml = Math.Sqrt(mx * mx + my * my);
            if (ml < 1e-6)
            {
                ox = n1x * hw;
                oy = n1y * hw;
                return;
            }

            mx /= ml;
            my /= ml;
            var cs = mx * n1x + my * n1y;
            var d = hw / Math.Max(.5, cs);
            ox = mx * d;
            oy = my * d;
        }

        /// <summary>A stroke along edge E clipped to the polygon E..O (the inside half of the page's clipped line): a band
        /// from each E point toward its O point, <paramref name="width"/> deep (never past O).</summary>
        public void Band(double[] ex, double[] ey, double[] qx, double[] qy, int o, int n, double width, Color32 c, double alpha)
        {
            if (n < 2 || width <= .05 || alpha <= .002) return;
            GrowS(n);
            for (var i = 0; i < n; i++)
            {
                double dx = qx[o + i] - ex[o + i], dy = qy[o + i] - ey[o + i];
                var l = Math.Sqrt(dx * dx + dy * dy);
                var f = l > 1e-9 ? Math.Min(1, width / l) : 0;
                _sx[i] = ex[o + i];
                _sy[i] = ey[o + i];
                _tx[i] = ex[o + i] + dx * f;
                _ty[i] = ey[o + i] + dy * f;
            }

            Strip(_sx, _sy, _tx, _ty, 0, n, c, alpha);
        }

        /// <summary>anime_v61_cracks.js taper (:204): a strip tapering from w0 / 2 + .15 to .15 (half widths).</summary>
        public void Taper(double[] x, double[] y, int o, int n, double w0, Color32 c, double alpha)
        {
            if (n < 2 || alpha <= .002) return;
            GrowS(n);
            for (var i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                double dx = x[o + b] - x[o + a], dy = y[o + b] - y[o + a];
                var l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9) l = 1;
                var w = w0 * (1 - i / (double)(n - 1)) * .5 + .15;
                _sx[i] = x[o + i] + dy / l * w;
                _sy[i] = y[o + i] - dx / l * w;
                _tx[i] = x[o + i] - dy / l * w;
                _ty[i] = y[o + i] + dx / l * w;
            }

            Strip(_sx, _sy, _tx, _ty, 0, n, c, alpha);
        }

        /// <summary>anime_v61_cracks.js taperTo (:199-203): the strip <see cref="Taper"/> would draw (widths from the FULL
        /// path), cut at the fractional index f and closed to a point there.</summary>
        public void TaperTo(double[] x, double[] y, int n, double dx0, double dy0, double w0, Color32 c, double alpha, double f)
        {
            if (n < 2 || f <= 0 || alpha <= .002) return;
            f = Math.Min(f, n - 1);
            var ni = (int)Math.Floor(f);
            var fr = f - ni;
            GrowS(ni + 2);
            var k = 0;
            for (var i = 0; i <= ni; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                double dx = x[b] - x[a], dy = y[b] - y[a];
                var l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9) l = 1;
                var w = i == ni && fr < .001 ? 0 : (w0 * (1 - i / (double)(n - 1)) * .5 + .15) * Math.Min(1, (f - i) * 1.5);
                double px = x[i] + dx0, py = y[i] + dy0;
                _sx[k] = px + dy / l * w;
                _sy[k] = py - dx / l * w;
                _tx[k] = px - dy / l * w;
                _ty[k] = py + dx / l * w;
                k++;
            }

            if (fr > .001 && ni + 1 < n)
            {
                var tx = x[ni] + (x[ni + 1] - x[ni]) * fr + dx0;
                var ty = y[ni] + (y[ni + 1] - y[ni]) * fr + dy0;
                _sx[k] = _tx[k] = tx;
                _sy[k] = _ty[k] = ty;
                k++;
            }

            Strip(_sx, _sy, _tx, _ty, 0, k, c, alpha);
        }

        /// <summary>Append a copy of every vertex already in the list, offset by (dx, dy) and its alpha × k (the time
        /// stutter's ghosts, applyWaver).</summary>
        public void Ghost(int vEnd, int iEnd, double dx, double dy, double k)
        {
            var b = V.Count;
            for (var i = 0; i < vEnd; i++)
            {
                var v = V[i];
                v.X += dx;
                v.Y += dy;
                v.C.a = (byte)Math.Round(v.C.a * Math.Max(0, Math.Min(1, k)));
                V.Add(v);
            }

            for (var i = 0; i < iEnd; i++) I.Add(I[i] + b);
        }
    }

    /// <summary>One branch's shape at extent E (shapeOf) and its chipped lips (spallOf), in reusable buffers.</summary>
    public sealed class CrackShape
    {
        public CrackBranch B;
        public int N;
        public bool On;
        public double E, Wmax, Wb;
        public double[] ULx, ULy, LRx, LRy, SPx, SPy, WV, DD, HU, HL;
        public bool Spall;
        public int Rl, Ph, Sd;
        public double[] ULox, ULoy, LRox, LRoy, Gr;

        public CrackShape(CrackBranch b)
        {
            B = b;
            var n = Math.Max(2, b.N);
            ULx = new double[n]; ULy = new double[n]; LRx = new double[n]; LRy = new double[n];
            SPx = new double[n]; SPy = new double[n]; WV = new double[n]; DD = new double[n]; HU = new double[n]; HL = new double[n];
            ULox = new double[n]; ULoy = new double[n]; LRox = new double[n]; LRoy = new double[n]; Gr = new double[n];
        }

        public int Run(int i, int side) => (int)Math.Floor((i + Ph + side) / (double)Rl);
    }

    /// <summary>
    /// CRK.drawNets for one network (anime_v61_cracks.js:243-302), as three meshes in the page's pass order: pass 1 (the
    /// chipped lips, into <c>lips</c>), pass 2 (the gaps onto the Shadow Realm, into <c>gap</c>, UV-mapped onto the realm
    /// tile), passes 3-6 (the dark depth bands, the ink rim, the hairlines and crazing, the web, into <c>top</c>). The page
    /// draws pass 1 for every net, then every gap, then 3-6 per net: a layer keeps that order by drawing all the lips
    /// graphics, then all the gap graphics, then all the top graphics. The page's clipped strokes become bands inside
    /// their polygon (<see cref="CrackMesh.Band"/>).
    /// </summary>
    public sealed class CrackPainter
    {
        static readonly Color32 Ink = new(0, 0, 0, 255), WallInk = new(0x0C, 0x0A, 0x10, 255), Depth = new(0x07, 0x04, 0x0C, 255),
            DepthBand = new(0x1B, 0x0E, 0x2C, 255);

        /// <summary>The realm's flat fill (AN.violet.deep) when the gap mesh carries no texture (#crkprof=novoid).</summary>
        public static readonly Color32 RealmDeep = new(0x2B, 0x17, 0x45, 255);

        /// <summary>Offline harness: the page's own alphas (no Linear-space correction).</summary>
        public static bool LinearOff;

        readonly Dictionary<CrackNet, CrackShape[]> _shapes = new();

        public CrackShape[] ShapesOf(CrackNet n)
        {
            if (_shapes.TryGetValue(n, out var s)) return s;
            s = new CrackShape[n.Br.Length];
            for (var i = 0; i < s.Length; i++) s[i] = new CrackShape(n.Br[i]);
            _shapes[n] = s;
            return s;
        }

        // ---------------------------------------------------------------- the Linear-space correction of the ops' alphas

        static double Dark(double a) => LinearOff ? a : MenuV6Linear.Shade(a);
        static double Light(double a) => LinearOff ? a : MenuV6Linear.Alpha(a, 1.3f);

        // ---------------------------------------------------------------- shapeOf (:166-189)

        /// <summary>Every branch's shape at extent E (open = the breathe), into the reusable buffers; returns how many are on.</summary>
        public int Shape(CrackNet n, double E, double open)
        {
            var shapes = ShapesOf(n);
            var on = 0;
            var rw = n.Sp.RootW;
            foreach (var s in shapes)
            {
                s.On = false;
                var b = s.B;
                if (b.N < 2 || b.Ds[0] >= E - .5) continue;
                var cnt = 0;
                double wmax = 0;
                for (var i = 0; i < b.N; i++)
                {
                    double px = b.X[i], py = b.Y[i], d = b.Ds[i];
                    if (d > E)
                    {
                        var f = (E - b.Ds[i - 1]) / (d - b.Ds[i - 1]);
                        px = b.X[i - 1] + (px - b.X[i - 1]) * f;
                        py = b.Y[i - 1] + (py - b.Y[i - 1]) * f;
                        d = E;
                    }

                    var u = (d - b.Ds[0]) / b.L;
                    var prof = b.Depth == 0 ? rw + (1 - rw) * MenuV6Math.SStep(0, .2, u) : .5 + .5 * MenuV6Math.SStep(0, .3, u);
                    var tip = CrackTime.Clamp((E - d) / (12 * n.K + b.W * 3), 0, 1);
                    var w = b.W * prof * Math.Pow(Math.Max(0, 1 - u), .8) * Math.Sqrt(tip) * open;
                    double bump = 0;
                    if (b == n.Main)
                    {
                        foreach (var h in n.Holes)
                        {
                            var g = (d - h.D) / h.R;
                            bump = Math.Max(bump, Math.Exp(-g * g) * h.Kw * CrackTime.HoleOpen(h, E));
                        }

                        w *= 1 + bump * Math.Sqrt(tip);
                    }

                    int a0 = Math.Max(0, i - 1), a1 = Math.Min(b.N - 1, i + 1);
                    double tx = b.X[a1] - b.X[a0], ty = b.Y[a1] - b.Y[a0];
                    var tl = Math.Sqrt(tx * tx + ty * ty);
                    if (tl < 1e-12) tl = 1;
                    tx /= tl;
                    ty /= tl;
                    double nx = ty, ny = -tx;
                    if (nx * b.NUx + ny * b.NUy < 0)
                    {
                        nx = -nx;
                        ny = -ny;
                    }

                    var hw = w / 2;
                    var pin = MenuV6Math.Lerp(b.Pin[i], 1, MenuV6Math.SStep(.15, .45, bump));
                    var jg = 1 + .5 * Math.Min(1, bump);
                    var mi = Math.Min(i, b.MU.Length - 1);
                    var hu = Math.Min(hw * (.5 + (b.Ju[i] - .5) * jg + .5) * pin, mi >= 0 ? b.MU[mi] : 1e9);
                    var hl = Math.Min(hw * (.5 + (b.Jl[i] - .5) * jg + .5) * pin, mi >= 0 && mi < b.ML.Length ? b.ML[mi] : 1e9);
                    s.ULx[cnt] = px + nx * hu;
                    s.ULy[cnt] = py + ny * hu;
                    s.LRx[cnt] = px - nx * hl;
                    s.LRy[cnt] = py - ny * hl;
                    s.SPx[cnt] = px;
                    s.SPy[cnt] = py;
                    s.WV[cnt] = w;
                    s.DD[cnt] = d;
                    s.HU[cnt] = hu;
                    s.HL[cnt] = hl;
                    cnt++;
                    wmax = Math.Max(wmax, w);
                    if (d >= E) break;
                }

                if (cnt < 2) continue;
                s.N = cnt;
                s.E = E;
                s.Wmax = wmax;
                s.Wb = wmax / Math.Max(.2, open);
                s.On = true;
                Spall(s, n);
                on++;
            }

            return on;
        }

        // ---------------------------------------------------------------- spallOf (:224-242)

        static void Spall(CrackShape s, CrackNet net)
        {
            s.Spall = false;
            var k = net.K;
            var b = s.B;
            var n = s.N;
            if (n < 3) return;
            var grow = MenuV6Math.SStep(1.6 * k, 6 * k, s.Wb);
            if (grow <= .005) return;
            unchecked
            {
                s.Sd = net.Seed * 13 + b.Id * 7;
            }

            s.Rl = 2 + (int)Math.Floor(MenuV6Math.Hash2(b.Id, 3, s.Sd) * 2);
            s.Ph = (int)Math.Floor(MenuV6Math.Hash2(b.Id, 4, s.Sd) * 3);
            var E = s.DD[n - 1];
            var chH = net.Sp.ChipHole;
            var cK = net.Sp.ChipK;
            for (var i = 0; i < n; i++)
            {
                var d = s.DD[i];
                var ends = MenuV6Math.SStep(0, 24 * k, d - b.Ds[0]) * MenuV6Math.SStep(0, 34 * k, E - d);
                double hk = 0;
                if (b == net.Main)
                    foreach (var h in net.Holes)
                    {
                        var g = (d - h.D) / (h.R * 1.25);
                        hk = Math.Max(hk, Math.Exp(-g * g) * h.Kw * CrackTime.HoleOpen(h, s.E));
                    }

                var bas = Math.Min((s.WV[i] * .5 + 3 * k) * (1 + .6 * hk) * cK, 16 * k * cK) * ends * grow * (b.Depth != 0 ? .72 : 1) *
                          (chH + (1 - chH) * Math.Min(1, hk));
                int a0 = Math.Max(0, i - 1), a1 = Math.Min(n - 1, i + 1);
                double tx = s.SPx[a1] - s.SPx[a0], ty = s.SPy[a1] - s.SPy[a0];
                var tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-12) tl = 1;
                double nx = ty / tl, ny = -tx / tl;
                if (nx * b.NUx + ny * b.NUy < 0)
                {
                    nx = -nx;
                    ny = -ny;
                }

                var mi = Math.Min(i, b.MU.Length - 1);
                var su = Math.Min(bas * Math.Max(Amp(s, i, 1), .7 * MenuV6Math.SStep(.45, .75, hk)), Math.Max(0, b.MU[mi] - s.HU[i]));
                var sl = Math.Min(bas * .85 * Math.Max(Amp(s, i, 2), .6 * MenuV6Math.SStep(.45, .75, hk)), Math.Max(0, b.ML[mi] - s.HL[i]));
                s.ULox[i] = s.ULx[i] + nx * su;
                s.ULoy[i] = s.ULy[i] + ny * su;
                s.LRox[i] = s.LRx[i] - nx * sl;
                s.LRoy[i] = s.LRy[i] - ny * sl;
                s.Gr[i] = ends * grow;
            }

            s.Spall = true;
        }

        static double Amp(CrackShape s, int i, int side)
        {
            var r = s.Run(i, side);
            var h = MenuV6Math.Hash2(r, side * 11 + 1, s.Sd);
            return h < .28 ? 0 : h < .78 ? .45 + .35 * MenuV6Math.Hash2(r, side * 11 + 2, s.Sd) : .95 + .35 * MenuV6Math.Hash2(r, side * 11 + 3, s.Sd);
        }

        // ---------------------------------------------------------------- drawNets (:243-302)

        double[] _px = new double[256], _py = new double[256], _qx = new double[256], _qy = new double[256];

        void GrowP(int n)
        {
            if (_px.Length >= n) return;
            var m = Math.Max(n, _px.Length * 2);
            _px = new double[m];
            _py = new double[m];
            _qx = new double[m];
            _qy = new double[m];
        }

        /// <summary>
        /// Pass 1 (the chipped lips) in three parts, so a stencil mask can stand in for the page's clip(sp.poly):
        /// <see cref="LipsFloor"/> (the chip floors = the mask's own graphic), <see cref="LipsInner"/> (the facets and the
        /// chip's two walls: with <paramref name="clipped"/> the page's full strokes, to be clipped by that mask; without it,
        /// bands inside the chip) and <see cref="LipsEdge"/> (the ink round the chipped edge, the ticks, the lit sliver).
        /// The page interleaves them per branch; per net they are drawn part by part (the branches of a net only meet at
        /// their roots).
        /// </summary>
        public void Lips(CrackNet n, CrackShape[] sh, double a, CrackMesh m)
        {
            LipsFloor(n, sh, a, m);
            LipsInner(n, sh, a, m, false);
            LipsEdge(n, sh, a, m);
        }

        public void LipsFloor(CrackNet n, CrackShape[] sh, double a, CrackMesh m)
        {
            var M = n.Mat;
            foreach (var s in sh)
                if (s.On && s.Spall)
                    m.Strip(s.ULox, s.ULoy, s.LRox, s.LRoy, 0, s.N, M[1], a);                // the chip's floor
        }

        public void LipsInner(CrackNet n, CrackShape[] sh, double a, CrackMesh m, bool clipped)
        {
            var kc = Math.Min(1.3, n.K);
            var M = n.Mat;
            var seed = n.Seed;
            foreach (var s in sh)
            {
                if (!s.On || !s.Spall) continue;
                var cnt = s.N;
                for (var side = 1; side <= 2; side++)                                           // facets: one flat plane per chunk
                {
                    var lipX = side == 1 ? s.ULx : s.LRx;
                    var lipY = side == 1 ? s.ULy : s.LRy;
                    var outX = side == 1 ? s.ULox : s.LRox;
                    var outY = side == 1 ? s.ULoy : s.LRoy;
                    var i0 = 0;
                    for (var i = 1; i <= cnt; i++)
                    {
                        if (i < cnt && s.Run(i, side) == s.Run(i0, side)) continue;
                        var i1 = Math.Min(i, cnt - 1);
                        var h = MenuV6Math.Hash2(s.Run(i0, side), side + 40, seed);
                        if (h > .4 && i1 > i0)
                        {
                            var lit = h > .74;
                            var fa = a * (lit ? .6 : .45);
                            m.Strip(lipX, lipY, outX, outY, i0, i1 - i0 + 1, lit ? M[0] : M[2], lit ? Light(fa) : Dark(fa));
                        }

                        i0 = i;
                    }
                }

                // the chip's walls: the upper-left in shadow (5.6 kc), the lower-right lit (4.2 kc), clipped to the chip
                if (clipped)
                {
                    m.Stroke(s.ULox, s.ULoy, 0, cnt, 5.6 * kc, M[2], Dark(a));
                    m.Stroke(s.LRox, s.LRoy, 0, cnt, 4.2 * kc, M[0], a);
                }
                else
                {
                    m.Band(s.ULox, s.ULoy, s.LRox, s.LRoy, 0, cnt, 2.8 * kc, M[2], Dark(a));
                    m.Band(s.LRox, s.LRoy, s.ULox, s.ULoy, 0, cnt, 2.1 * kc, M[0], a);
                }
            }
        }

        public void LipsEdge(CrackNet n, CrackShape[] sh, double a, CrackMesh m)
        {
            var kc = Math.Min(1.3, n.K);
            var iK = n.Sp.InkK;
            var M = n.Mat;
            foreach (var s in sh)
            {
                if (!s.On || !s.Spall) continue;
                var cnt = s.N;
                // ink round the chipped edge (the closed outline ULo + LRo reversed)
                GrowP(cnt * 2);
                for (var i = 0; i < cnt; i++)
                {
                    _px[i] = s.ULox[i];
                    _py[i] = s.ULoy[i];
                    _px[cnt * 2 - 1 - i] = s.LRox[i];
                    _py[cnt * 2 - 1 - i] = s.LRoy[i];
                }

                m.Stroke(_px, _py, 0, cnt * 2, 1.8 * kc * iK, Ink, Dark(a), closed: true);
                // a tick where one chunk meets the next
                for (var i = 1; i < cnt - 1; i++)
                {
                    if (s.Run(i, 1) != s.Run(i - 1, 1)) Seg(m, s.ULox[i], s.ULoy[i], s.ULx[i], s.ULy[i], 1.25 * kc * iK, Ink, Dark(a));
                    if (s.Run(i, 2) != s.Run(i - 1, 2)) Seg(m, s.LRox[i], s.LRoy[i], s.LRx[i], s.LRy[i], 1.25 * kc * iK, Ink, Dark(a));
                }
            }

            // the lit sliver just outside the gap's lower-right lip (style §8.4)
            foreach (var s in sh)
            {
                if (!s.On || s.B.Depth > 1) continue;
                var f = MenuV6Math.SStep(2 * n.K, 7 * n.K, s.Wb);
                if (f < .02) continue;
                GrowP(s.N);
                for (var i = 0; i < s.N; i++)
                {
                    double ox = s.LRx[i] - s.SPx[i], oy = s.LRy[i] - s.SPy[i];
                    var ol = Math.Sqrt(ox * ox + oy * oy);
                    if (ol < 1e-12) ol = 1;
                    _px[i] = s.LRx[i] + ox / ol * 1.6 * kc;
                    _py[i] = s.LRy[i] + oy / ol * 1.6 * kc;
                }

                m.Stroke(_px, _py, 0, s.N, 1.7 * kc, M[0], Light(a * f));
            }
        }

        readonly double[] _sgx = new double[2], _sgy = new double[2];

        void Seg(CrackMesh m, double x0, double y0, double x1, double y1, double w, Color32 c, double a)
        {
            _sgx[0] = x0;
            _sgy[0] = y0;
            _sgx[1] = x1;
            _sgy[1] = y1;
            m.Stroke(_sgx, _sgy, 0, 2, w, c, a);
        }

        /// <summary>Pass 2: the gaps (shapes wider than .35 k at rest) filled with the realm at <paramref name="am"/> (the
        /// layer's largest item alpha, as the page's one drawVoidIn over the union). The mesh's UV mapping is the realm
        /// tile's (set by the caller).</summary>
        public void Gaps(CrackNet n, CrackShape[] sh, double am, Color32 fill, CrackMesh m)
        {
            foreach (var s in sh)
                if (s.On && s.Wb > .35 * n.K)
                    m.Strip(s.ULx, s.ULy, s.LRx, s.LRy, 0, s.N, fill, am);
        }

        readonly double[] _cx = new double[3], _cy = new double[3];

        /// <summary>Passes 3-6 (bands for the depth when no mask clips it).</summary>
        public void Top(CrackNet n, CrackShape[] sh, double a, double E, CrackMesh m)
        {
            DepthBands(n, sh, a, m, false);
            Rest(n, sh, a, E, m);
        }

        /// <summary>Pass 3: the dark depth inside the gap: the page strokes the upper-left lip (3.4 band dark violet, then
        /// 2.4 band near black) clipped to the gaps. With <paramref name="clipped"/> the full strokes (a stencil mask of the
        /// gaps clips them), else bands inside the upper-left lip.</summary>
        public void DepthBands(CrackNet n, CrackShape[] sh, double a, CrackMesh m, bool clipped)
        {
            var k = n.K;
            var dK = n.Sp.DepthK;
            foreach (var s in sh)
            {
                if (!s.On || s.Wb <= .35 * k && !clipped) continue;
                var band = Math.Min(6.4 * k, s.Wmax * .42) * dK;
                if (band < .3) continue;
                if (clipped)
                {
                    m.Stroke(s.ULx, s.ULy, 0, s.N, band * 3.4, DepthBand, Dark(a));   // a dark-violet band under the wall
                    m.Stroke(s.ULx, s.ULy, 0, s.N, band * 2.4, Depth, Dark(a));       // the wall's thickness: near black
                }
                else
                {
                    m.Band(s.ULx, s.ULy, s.LRx, s.LRy, 0, s.N, band * 1.7, DepthBand, Dark(a));
                    m.Band(s.ULx, s.ULy, s.LRx, s.LRy, 0, s.N, band * 1.2, Depth, Dark(a));
                }
            }
        }

        /// <summary>Passes 4-6: the ink rim, the hairlines and crazing, the web.</summary>
        public void Rest(CrackNet n, CrackShape[] sh, double a, double E, CrackMesh m)
        {
            var k = n.K;
            var kc = Math.Min(1.3, k);
            var sp = n.Sp;
            var M = n.Mat;
            var seed = n.Seed;
            // 4 · the ink rim round the gaps
            foreach (var s in sh)
            {
                if (!s.On || s.Wb <= .35 * k) continue;
                var lw = 2 * CrackTime.Clamp(.55 + s.Wmax * .09, .85, 2.1 * kc) * sp.InkK;
                GrowP(s.N * 2);
                for (var i = 0; i < s.N; i++)
                {
                    _px[i] = s.ULx[i];
                    _py[i] = s.ULy[i];
                    _px[s.N * 2 - 1 - i] = s.LRx[i];
                    _py[s.N * 2 - 1 - i] = s.LRy[i];
                }

                m.Stroke(_px, _py, 0, s.N * 2, lw, Ink, Dark(a), closed: true);
            }

            // 5 · hairlines: the narrow branches as tapered ink, fading as the branch opens into a gap
            foreach (var s in sh)
            {
                if (!s.On) continue;
                var hf = 1 - MenuV6Math.SStep(1.4 * k, 3.2 * k, s.Wb);
                if (hf > .01) m.Taper(s.SPx, s.SPy, 0, s.N, Math.Max(1.3, s.Wmax * .9), WallInk, Dark(a * hf));
            }

            // crazing off the chipped edge
            foreach (var s in sh)
            {
                if (!s.On || !s.Spall || s.B.Depth > 1) continue;
                for (var i = 2; i < s.N - 2; i += 2)
                {
                    var h = MenuV6Math.Hash2(i, s.B.Id, seed + 33);
                    if (h < .6) continue;
                    var up = h > .8;
                    double qx = up ? s.ULox[i] : s.LRox[i], qy = up ? s.ULoy[i] : s.LRoy[i];
                    double ox = qx - s.SPx[i], oy = qy - s.SPy[i];
                    var ol = Math.Sqrt(ox * ox + oy * oy);
                    if (ol < .5) continue;
                    ox /= ol;
                    oy /= ol;
                    var an = Math.Atan2(oy, ox) + (MenuV6Math.Hash2(i, 5, seed) - .5) * 1.1;
                    var L2 = (6 + 12 * MenuV6Math.Hash2(i, 4, seed)) * k * s.Gr[i];
                    if (L2 < 1.5) continue;
                    var tw = .35 * (MenuV6Math.Hash2(i, 6, seed) < .5 ? -1 : 1);
                    _cx[0] = qx;
                    _cy[0] = qy;
                    _cx[1] = qx + Math.Cos(an) * L2 * .5;
                    _cy[1] = qy + Math.Sin(an) * L2 * .5;
                    _cx[2] = qx + Math.Cos(an + tw) * L2;
                    _cy[2] = qy + Math.Sin(an + tw) * L2;
                    m.Taper(_cx, _cy, 0, 3, 1.5 * kc, WallInk, Dark(a));
                }
            }

            // 6 · the web of hairline fractures off the break (v6.2): each runs out as the extent passes its root
            if (n.Web != null && n.Web.Length > 0)
            {
                var wl = 2.3 * kc;
                var off = 1.3 * kc;
                var wLit = sp.WebLit;
                foreach (var w in n.Web)
                {
                    var sh0 = E - w.D0;
                    if (sh0 <= 0) continue;
                    var q = MenuV6Math.SStep(0, 70 * k, sh0);
                    if (q < .02) continue;
                    var np = w.X.Length;
                    var f = Math.Max(1, (np - 1) * q);
                    m.TaperTo(w.X, w.Y, np, off, off, wl * .55, M[0], Light(a * wLit), f);   // its lit lower edge
                    m.TaperTo(w.X, w.Y, np, 0, 0, wl, WallInk, Dark(a), f);
                    foreach (var (km, kx, ky) in w.Kids)
                    {
                        if (km >= f - .5) continue;
                        var qk = CrackTime.Clamp((f - km) / Math.Max(1, np - 1 - km), 0, 1);
                        var fk = Math.Max(1, (kx.Length - 1) * qk);
                        m.TaperTo(kx, ky, kx.Length, off, off, wl * .45, M[0], Light(a * wLit), fk);
                        m.TaperTo(kx, ky, kx.Length, 0, 0, wl * .8, WallInk, Dark(a), fk);
                    }
                }
            }
        }
    }
}
