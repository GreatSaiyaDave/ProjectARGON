using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// Black lightning crackling along a crack (lore l.60 "A rift tore open, black lightning crackling around it";
    /// anime_v61_cracks.js:348-368): rare (menu seed 911: first at 6.5 s, then every 7-14 s), 2-3 strikes per event, each
    /// eased in over ~1.5 frames and out over 0.2 s, ≥ 0.16 s apart (≤ 3 per second, flash safe). A black #040108 line with
    /// a thin flat edge of the realm's own dark violet; no halo, no pale core (v6.3). The path is the page's boltPath: one
    /// arc leaps off the crack into the wall, a fork, one arc runs along it past the tip; it stays on its wall. Pure.
    /// </summary>
    public static class BoltGraphic
    {
        static readonly Color32 Edge0 = new(43, 23, 69, 255), Edge1 = new(58, 31, 94, 255), Core = new(4, 1, 8, 255);

        /// <summary>Offline harness: the page's own alphas.</summary>
        public static bool LinearOff;

        sealed class Pt
        {
            public double X, Y;
        }

        static List<Pt> Zig(Func<double> R, double x, double y, double an, int segs, double len, double spread)
        {
            var P = new List<Pt> { new() { X = x, Y = y } };
            for (var i = 0; i < segs; i++)
            {
                var b = an + (i % 2 != 0 ? 1 : -1) * spread * (.5 + R());
                x += Math.Cos(b) * len * (.7 + .6 * R());
                y += Math.Sin(b) * len * (.7 + .6 * R());
                P.Add(new Pt { X = x, Y = y });
            }

            return P;
        }

        /// <summary>CRK.boltPath(N, E, seed, k): the strike's 2-3 polylines (null when the crack is too short yet).</summary>
        public static List<(double[] x, double[] y)> Path(CrackNet N, double E, uint seed, double k)
        {
            var M = N.Main;
            if (M == null || M.N < 2) return null;
            var R = MenuV6Math.Rng(seed);
            var emax = Math.Min(E, M.Ds[M.N - 1]);
            if (emax < 30 * k) return null;
            var n = -1;
            for (var i = 0; i < M.N; i++)
                if (M.Ds[i] > emax)
                {
                    n = i;
                    break;
                }

            if (n < 0) n = M.N;
            if (n < 6) return null;
            var i0 = (int)Math.Floor(n * (.2 + .6 * R()));
            double qx = M.X[i0], qy = M.Y[i0];
            int ia = Math.Max(0, i0 - 2), ib = Math.Min(n - 1, i0 + 2);
            var ta = Math.Atan2(M.Y[ib] - M.Y[ia], M.X[ib] - M.X[ia]);
            var side = R() < .5 ? -1 : 1;
            var flip = R() < .5 ? 0 : Math.PI;
            var aA = ta + flip + side * (.5 + .35 * R());
            var segsA = 5 + (int)Math.Floor(R() * 3);
            var A = Zig(R, qx, qy, aA, segsA, 11 * k, .75);
            var f0 = A[2 + (int)Math.Floor(R() * 2)];
            var aF = ta + side * 1.1 + (R() < .5 ? -.5 : .5);
            var F = Zig(R, f0.X, f0.Y, aF, 3, 9 * k, .7);
            var P = new List<Pt>();
            for (var i = (int)Math.Floor(n * .55); i < n; i += 2) P.Add(new Pt { X = M.X[i], Y = M.Y[i] });
            if (P.Count > 1)
            {
                Pt z = P[P.Count - 1], yv = P[P.Count - 2];
                var tail = Zig(R, z.X, z.Y, Math.Atan2(z.Y - yv.Y, z.X - yv.X), 4, 9 * k, .6);
                for (var i = 1; i < tail.Count; i++) P.Add(tail[i]);
            }

            var paths = new List<List<Pt>> { A, F };
            if (P.Count > 2)
            {
                var Q = new List<Pt>(P.Count);
                for (var j = 0; j < P.Count; j++)
                {
                    if (j != 0 && j < P.Count - 4)
                    {
                        var dx = (R() - .5) * 8 * k;
                        var dy = (R() - .5) * 8 * k;
                        Q.Add(new Pt { X = P[j].X + dx, Y = P[j].Y + dy });
                    }
                    else Q.Add(P[j]);
                }

                paths.Add(Q);
            }

            var sp = N.Sp;
            var pad = 14 * k;
            var outp = new List<(double[] x, double[] y)>();
            foreach (var path in paths)
            {
                var xs = new double[path.Count];
                var ys = new double[path.Count];
                for (var i = 0; i < path.Count; i++)
                {
                    xs[i] = CrackTime.Clamp(path[i].X, sp.Zx0 - pad, sp.Zx1 + pad);
                    ys[i] = CrackTime.Clamp(path[i].Y, sp.Zy0 - pad, sp.Zy1 + pad);
                }

                outp.Add((xs, ys));
            }

            return outp;
        }

        /// <summary>CRK.drawBolt(g, paths, a, k): the dark-violet edge (6 k at .7 a, 4.4 k at .55 a), then the black line
        /// (3.2 k at .97 a); round caps, miter joins.</summary>
        public static void Paint(CrackMesh m, List<(double[] x, double[] y)> paths, double a, double k)
        {
            if (paths == null || a <= .01) return;
            double D(double x) => LinearOff ? x : MenuV6Linear.Shade(x);
            foreach (var (x, y) in paths) m.Stroke(x, y, 0, x.Length, 6 * k, Edge0, D(.7 * a));
            foreach (var (x, y) in paths) m.Stroke(x, y, 0, x.Length, 4.4 * k, Edge1, D(.55 * a));
            foreach (var (x, y) in paths) m.Stroke(x, y, 0, x.Length, 3.2 * k, Core, D(.97 * a));
        }

        /// <summary>The menu's strikes at crack time tc (crackDraw :677-680): events within .8 s, 2 or 3 strikes each.</summary>
        public static void Strikes(CrackBeat[] bolts, List<CrackNet> boltNets, Func<CrackNet, double> extentOf, double tc, double a,
            uint seedBase, CrackMesh m)
        {
            if (bolts == null || boltNets == null || boltNets.Count == 0) return;
            foreach (var s in bolts)
            {
                if (s.T > tc + .01) break;
                if (tc - s.T >= .8) continue;
                var N = boltNets[(int)Math.Floor(s.R * boltNets.Count) % boltNets.Count];
                if (N == null) continue;
                var E = extentOf(N);
                var ns = s.R2 < .5 ? 2 : 3;
                for (var j = 0; j < ns; j++)
                {
                    var u = tc - s.T - j * (.16 + .05 * j);
                    var e = CrackTime.StrikeEnv(u);
                    if (e <= 0) continue;
                    Paint(m, Path(N, E, (uint)(seedBase + s.I * 7 + j), N.K), e * a, N.K);
                }
            }
        }
    }
}
