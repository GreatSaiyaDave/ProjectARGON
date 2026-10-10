using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Procedural sprites for the Spirit Dueler aura and the floating hand: white RGB with the shape in ALPHA.
    /// (The Imagine smoke PNG they replace has no alpha channel — black background, alpha 255 everywhere — so any
    /// alpha-blended quad that used it drew a dark square: the "black smoke from the deck box".)
    /// Built once per run, cached, and safe for both alpha-blended and additive materials.
    /// </summary>
    public static class SpiritFxTextures
    {
        static Texture2D _glow, _star, _ring, _trail, _runes, _softRect;

        /// <summary>
        /// Zone light-up: a rectangle with a faint fill, a bright border and a soft glow outside it. Stretch the
        /// quad to the card footprint.
        /// </summary>
        public static Texture2D SoftRect => _softRect != null ? _softRect : _softRect = Build("SpiritSoftRect", 64, 64, (u, v) =>
        {
            var d = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
            const float edge = 0.82f;
            if (d <= edge)
            {
                var band = Mathf.Exp(-Mathf.Pow((edge - d) / 0.07f, 2f));
                return Mathf.Clamp01(0.22f + band);
            }

            return Mathf.Clamp01(Mathf.Exp(-Mathf.Pow((d - edge) / 0.08f, 2f)));
        });

        /// <summary>Rune atlas layout: <see cref="RuneTiles"/> × <see cref="RuneTiles"/> glyphs.</summary>
        public const int RuneTiles = 4;

        /// <summary>Soft round glow (gaussian-ish falloff).</summary>
        public static Texture2D Glow => _glow != null ? _glow : _glow = Build("SpiritGlow", 64, 64, (u, v) =>
        {
            var r = Mathf.Sqrt(u * u + v * v);
            var a = Mathf.Exp(-r * r * 5.5f) - Mathf.Exp(-5.5f);
            return Mathf.Clamp01(a / (1f - Mathf.Exp(-5.5f)));
        });

        /// <summary>Anime sparkle: a four-point star with a hot centre.</summary>
        public static Texture2D Star => _star != null ? _star : _star = Build("SpiritStar", 64, 64, (u, v) =>
        {
            var au = Mathf.Abs(u);
            var av = Mathf.Abs(v);
            var r = Mathf.Sqrt(u * u + v * v);
            var ray = Mathf.Max(
                Mathf.Clamp01(1f - av * 14f) * Mathf.Clamp01(1f - au),
                Mathf.Clamp01(1f - au * 14f) * Mathf.Clamp01(1f - av));
            var diag = Mathf.Clamp01(1f - Mathf.Abs(au - av) * 10f) * Mathf.Clamp01(1f - r * 1.9f) * 0.45f;
            var core = Mathf.Exp(-r * r * 40f);
            return Mathf.Clamp01(ray * ray + diag + core);
        });

        /// <summary>
        /// Magic circle for the deploy flare (Skyrim rune circle, drawn anime-crisp): two rings with a band of
        /// twelve glyphs between them, a ring of ticks, an upward triangle inside (Millennium Puzzle nod), a hub ring.
        /// </summary>
        public static Texture2D Ring => _ring != null ? _ring : _ring = BuildCircle();

        /// <summary>
        /// 4×4 atlas of glowing rune glyphs in the spirit of carved dragon script — claw-stroke slashes, bars and
        /// dots on a 3×3 grid — generated from a fixed seed (no copied alphabet). Crisp strokes plus a soft halo.
        /// </summary>
        public static Texture2D Runes => _runes != null ? _runes : _runes = BuildRunes();

        /// <summary>Particle trail strip: bright along the middle, soft to both edges (texture V is the width).</summary>
        public static Texture2D Trail => _trail != null ? _trail : _trail = Build("SpiritTrail", 32, 32, (u, v) =>
        {
            var a = 1f - Mathf.Abs(v);
            return Mathf.Clamp01(a * a * (3f - 2f * a));
        }, TextureWrapMode.Clamp);

        // ------------------------------------------------------------------ glyphs

        struct Glyph
        {
            public Vector2[] A, B;   // stroke segments
            public Vector2[] Dots;
        }

        static List<Glyph> _glyphs;

        static List<Glyph> Glyphs()
        {
            if (_glyphs != null) return _glyphs;
            var rng = new System.Random(7717);
            _glyphs = new List<Glyph>();
            var grid = new[] { -0.62f, 0f, 0.62f };
            for (var g = 0; g < RuneTiles * RuneTiles; g++)
            {
                var a = new List<Vector2>();
                var b = new List<Vector2>();
                var strokes = 2 + rng.Next(3);
                for (var k = 0; k < strokes; k++)
                {
                    Vector2 p0, p1;
                    do
                    {
                        p0 = new Vector2(grid[rng.Next(3)], grid[rng.Next(3)]);
                        p1 = new Vector2(grid[rng.Next(3)], grid[rng.Next(3)]);
                    } while ((p0 - p1).sqrMagnitude < 0.3f || Mathf.Abs(p0.y - p1.y) < 0.01f && rng.Next(3) > 0);
                    a.Add(p0);
                    b.Add(p1);
                }

                var dots = new List<Vector2>();
                if (rng.Next(2) == 0)
                    dots.Add(new Vector2(grid[rng.Next(3)] * 0.8f, grid[rng.Next(3)] * 0.8f));
                _glyphs.Add(new Glyph { A = a.ToArray(), B = b.ToArray(), Dots = dots.ToArray() });
            }

            return _glyphs;
        }

        /// <summary>Distance from (x, y) to the glyph's nearest stroke or dot, in cell units (-1..1).</summary>
        static float GlyphDistance(Glyph g, float x, float y)
        {
            var p = new Vector2(x, y);
            var d = 9f;
            for (var i = 0; i < g.A.Length; i++)
            {
                var ab = g.B[i] - g.A[i];
                var t = Mathf.Clamp01(Vector2.Dot(p - g.A[i], ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                d = Mathf.Min(d, (p - (g.A[i] + ab * t)).magnitude);
            }

            foreach (var c in g.Dots) d = Mathf.Min(d, (p - c).magnitude - 0.06f);
            return d;
        }

        static float GlyphAlpha(Glyph g, float x, float y, float stroke, float px)
        {
            var d = GlyphDistance(g, x, y);
            var core = Mathf.Clamp01((stroke - d) / px + 0.5f);        // crisp, anti-aliased stroke
            var halo = Mathf.Exp(-(d * d) / (0.09f * 0.09f)) * 0.45f;  // soft magic glow around it
            return Mathf.Max(core, halo);
        }

        static Texture2D BuildRunes()
        {
            const int cell = 64;
            var size = cell * RuneTiles;
            var glyphs = Glyphs();
            return Build("SpiritRunes", size, size, (u, v) =>
            {
                // u, v are -1..1 across the whole atlas: find the tile and the cell-local coords
                var fx = (u * 0.5f + 0.5f) * RuneTiles;
                var fy = (v * 0.5f + 0.5f) * RuneTiles;
                var tx = Mathf.Min(RuneTiles - 1, (int)fx);
                var ty = Mathf.Min(RuneTiles - 1, (int)fy);
                var lx = (fx - tx) * 2f - 1f;
                var ly = (fy - ty) * 2f - 1f;
                // Unity's texture sheet counts tiles from the top-left
                var index = (RuneTiles - 1 - ty) * RuneTiles + tx;
                var edge = Mathf.Clamp01((1f - Mathf.Max(Mathf.Abs(lx), Mathf.Abs(ly))) / 0.08f);
                return GlyphAlpha(glyphs[index], lx * 1.15f, ly * 1.15f, 0.085f, 2.5f / cell) * edge;
            });
        }

        static Texture2D BuildCircle()
        {
            const int size = 256;
            var glyphs = Glyphs();
            const float px = 2f / size;
            return Build("SpiritMagicCircle", size, size, (u, v) =>
            {
                var r = Mathf.Sqrt(u * u + v * v);
                if (r > 0.99f) return 0f;
                float Line(float d, float w) => Mathf.Clamp01((w - Mathf.Abs(d)) / px + 0.5f);
                var a = Mathf.Max(Line(r - 0.95f, 0.012f), Line(r - 0.74f, 0.009f));
                a = Mathf.Max(a, Line(r - 0.2f, 0.008f));
                a = Mathf.Max(a, Mathf.Exp(-Mathf.Pow((r - 0.95f) / 0.05f, 2f)) * 0.35f);   // soft outer glow

                // twelve glyphs in the band between the rings
                if (r > 0.76f && r < 0.93f)
                {
                    var ang = Mathf.Atan2(v, u);
                    if (ang < 0f) ang += Mathf.PI * 2f;
                    var seg = Mathf.PI * 2f / 12f;
                    var k = Mathf.FloorToInt(ang / seg);
                    var mid = (k + 0.5f) * seg;
                    var gx = (ang - mid) * 0.845f / 0.085f;
                    var gy = (r - 0.845f) / 0.085f;
                    a = Mathf.Max(a, GlyphAlpha(glyphs[k % glyphs.Count], gx, gy, 0.1f, px / 0.085f));
                }

                // a ring of short ticks just inside the glyph band
                if (r > 0.62f && r < 0.68f)
                {
                    var ang = Mathf.Atan2(v, u);
                    var tick = Mathf.Abs(Mathf.Repeat(ang / (Mathf.PI * 2f) * 48f, 1f) - 0.5f) * 2f;
                    a = Mathf.Max(a, Mathf.Clamp01((tick - 0.82f) / 0.08f) * 0.8f);
                }

                // one upward triangle inscribed in the inner ring
                if (r < 0.74f)
                {
                    var p = new Vector2(u, v);
                    for (var e = 0; e < 3; e++)
                    {
                        var a0 = Mathf.PI * 0.5f + e * Mathf.PI * 2f / 3f;
                        var a1 = a0 + Mathf.PI * 2f / 3f;
                        var p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.72f;
                        var p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.72f;
                        var ab = p1 - p0;
                        var t = Mathf.Clamp01(Vector2.Dot(p - p0, ab) / ab.sqrMagnitude);
                        a = Mathf.Max(a, Line((p - (p0 + ab * t)).magnitude, 0.007f) * 0.8f);
                    }
                }

                return a;
            });
        }

        static Texture2D Build(string name, int w, int h, System.Func<float, float, float> alpha,
            TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                var v = (y + 0.5f) / h * 2f - 1f;
                for (var x = 0; x < w; x++)
                {
                    var u = (x + 0.5f) / w * 2f - 1f;
                    var a = Mathf.Clamp01(alpha(u, v));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.wrapMode = wrap;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(true, true);
            return tex;
        }
    }
}
