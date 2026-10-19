using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The Shadow-Realm mist seeping out of the spalled holes (anime_v61_cracks.js:328-345 drawMist): every puff is one of
    /// the page's own vector cels (MISTL / mistPuff, :312-326, exported verbatim to Common/mist_cels + Data/mist.json: 6
    /// variants × sizes 8..64 in 2 px steps), drawn at its exact size and position (v6.3 fixup: the cel of the nearest even
    /// size). Only the title's Tears and the floor breaks seep mist (CRACKS.md §2 item 6). Pure: the placement is a function
    /// of the clock and the net's seed; <see cref="Build"/> writes textured quads.
    /// </summary>
    public sealed class MistCels
    {
        public const string Sheet = "WRLDZ/MenuV6/Common/mist_cels";
        readonly Dictionary<int, (double x, double y, double d)> _cells = new();
        public int SheetW = 528, SheetH = 1556;

        /// <summary>Offline harness: the page's own alphas.</summary>
        public static bool LinearOff;

        static int Key(int v, int s) => v * 1000 + s;

        public static MistCels From(Dictionary<string, object> root)
        {
            var m = new MistCels();
            var cells = RJ.D(root, "cells");
            if (cells == null) return null;
            foreach (var kv in cells)
            {
                var p = kv.Key.Split('_');
                if (p.Length != 2 || !int.TryParse(p[0], out var v) || !int.TryParse(p[1], out var s)) continue;
                if (kv.Value is List<object> r && r.Count >= 3) m._cells[Key(v, s)] = (RJ.At(r, 0), RJ.At(r, 1), RJ.At(r, 2));
            }

            return m._cells.Count > 0 ? m : null;
        }

        bool Cell(int v, int s, out Rect uv, out double d)
        {
            if (!_cells.TryGetValue(Key(v, s), out var c))
            {
                uv = QuadList.Full;
                d = 0;
                return false;
            }

            uv = QuadList.Cell(SheetW, SheetH, c.x, c.y, c.d, c.d);
            d = c.d;
            return true;
        }

        /// <summary>drawMist(g, items, tc, a0) into <paramref name="q"/> (stage px, the cel sheet's UVs).</summary>
        public void Build(List<CrackItem> items, double tc, double a0, QuadList q)
        {
            foreach (var it in items)
            {
                var N = it.N;
                if (N == null || it.Mist == 0 || it.A <= .01) continue;
                var k = N.K;
                var sp = N.Sp;
                for (var ei = 0; ei < N.Holes.Length; ei++)
                {
                    var e = N.Holes[ei];
                    var ramp = MenuV6Math.SStep(e.D - 6 * k, e.D + 90 * k, it.E);
                    if (ramp <= .002) continue;
                    var n = (int)Math.Round(sp.MistN * (e.Kw >= 1.45 ? 1 : .5), MidpointRounding.AwayFromZero);
                    for (var j = 0; j < n; j++)
                    {
                        var life = 7 + 3 * MenuV6Math.Hash2(j, ei, N.Seed + 2);
                        var off = MenuV6Math.Hash2(j, ei, N.Seed + 3) * life;
                        var tt = tc + off;
                        var cyc = (int)Math.Floor(tt / life);
                        var age = tt - cyc * life;
                        var up = sp.MistUp;
                        var vx = (MenuV6Math.Hash2(j, cyc, N.Seed + 4) - .5) * 7 * k;
                        var vy = -(9 + 7 * MenuV6Math.Hash2(j, cyc, N.Seed + 5)) * k * up;
                        var x = e.Px + vx * age + Math.Sin(tc * .5 + j * 2.1) * 4 * k;
                        var y = e.Py + vy * age - 4 * k;
                        var grow = MenuV6Math.Out(age / life);
                        var sz = (13 + 23 * grow) * k * sp.MistS * (.8 + .4 * MenuV6Math.Hash2(j, cyc, N.Seed + 6)) *
                                 (1 - .25 * MenuV6Math.Seg(age, life - 2.4, life)) * (.4 + .6 * ramp);
                        var zt = sp.ZoneTop - sp.MistRoom * k;
                        var al = MenuV6Math.Win(age, 0, 1.4, life - 2.4, life) * it.Mist * a0 * it.A *
                                 CrackTime.Clamp((y - zt) / (26 * k), 0, 1) * ramp * .85;
                        if (al < .01) continue;
                        var S = Math.Max(8, (int)Math.Round(sz / 2, MidpointRounding.AwayFromZero) * 2);
                        S = Math.Min(64, S);
                        var v = (j * 2 + ei + cyc) % 6;
                        if (!Cell(v, S, out var uv, out var D)) continue;
                        var d2 = D * Math.Max(8, sz) / S;
                        var aa = CrackTime.Clamp(al, 0, 1) * .92;
                        if (!LinearOff) aa = MenuV6Linear.Alpha(aa, 1.3f);
                        q.Sprite(x, y, d2, d2, uv, new Color(1f, 1f, 1f, (float)aa));
                    }
                }
            }
        }
    }
}
