using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// P8 (PERF_PLAN.md step 1, <see cref="MenuV6Perf.DeckArtAtlas"/>): the deck hall's card art at run time on shared atlas
    /// pages. Every chip and MAIN / EXTRA / SIDE slot drew its card's own texture (CardDatabase.GetArt: one Texture2D per
    /// card, ~55-60 distinct textures on screen = one draw each); here each card's texture is COPIED texel for texel
    /// (Graphics.CopyTexture, on the GPU) into a 2048² page of the same graphics format, and the chip's Image gets a
    /// sprite on that page with the same rect size, pivot, pixels per unit and border. The Image keeps its own layout
    /// (preserveAspect, its rect), so it samples the very same texels with the same bilinear weights:
    /// <list type="bullet">
    /// <item>no resampling: the copy is exact, the format is the source's (sRGB kept), no mips (as the source);</item>
    /// <item>a 1-texel gutter around each card holds what the source's wrap mode gives past its edge (Repeat: the opposite
    /// row / column; Clamp / Mirror: the edge itself), the only texels a bilinear tap can reach outside the card;</item>
    /// <item>the page's filter mode and aniso level are the source's (pages are kept per format, filter and aniso).</item>
    /// </list>
    /// What stays on its own texture: anything packed, mipmapped, compressed (a 1-texel copy is not block-aligned), not a
    /// whole-texture sprite, too big for a page, or once the pages are full (2 per format). The inspect sheet, the +1 flight
    /// and the drag ghost load the full texture themselves (HallFace / GetArt): untouched. The pages are freed when the last
    /// hall closes (<see cref="Release"/>).
    /// </summary>
    public static class DeckArtAtlas
    {
        public const int PageSize = 2048;
        public const int MaxPagesPerKind = 2;

        sealed class Page
        {
            public Texture2D Tex;
            public string Kind;
            public int ShelfY, ShelfH, X;
            public int Cells;
        }

        static readonly List<Page> Pages = new();
        static readonly Dictionary<Texture, Sprite> BySource = new();
        static readonly List<Sprite> Made = new();
        static readonly HashSet<string> BadKinds = new();
        static int _users;

        /// <summary>Counters for the budget (MenuV6Capture) and the smoke check.</summary>
        public static int Cells { get; private set; }
        public static int Misses { get; private set; }
        public static int PageCount => Pages.Count;

        /// <summary>GPU texture copies are available (CopyTextureSupport.Basic): otherwise every card keeps its texture.</summary>
        public static bool Supported => (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

        /// <summary>A hall that may use the pages (HallDecorator); <see cref="Release"/> frees them after the last one.</summary>
        public static void Acquire() => _users++;

        public static void Release()
        {
            _users = Math.Max(0, _users - 1);
            if (_users == 0) Clear();
        }

        /// <summary>True when <paramref name="s"/> is a sprite on one of the pages.</summary>
        public static bool IsAtlased(Sprite s)
        {
            if (s == null) return false;
            var t = s.texture;
            if (t == null) return false;
            foreach (var p in Pages)
                if (p.Tex == t)
                    return true;
            return false;
        }

        /// <summary>
        /// The page sprite for <paramref name="art"/> (a card's whole-texture sprite), or <paramref name="art"/> itself when it
        /// stays on its own texture (switch off, unsupported, ineligible, pages full).
        /// </summary>
        public static Sprite For(Sprite art)
        {
            // only while a hall holds the pages (Acquire): nothing outlives the deck builder
            if (art == null || !MenuV6Perf.DeckArtOn || _users <= 0) return art;
            var src = art.texture;
            if (src == null || art.packed) return art;
            if (BySource.TryGetValue(src, out var hit))
            {
                if (hit != null) return hit;
                BySource.Remove(src);
            }

            if (IsAtlased(art) || !Supported) return art;
            var w = src.width;
            var h = src.height;
            var r = art.rect;
            if (r.x != 0f || r.y != 0f || (int)r.width != w || (int)r.height != h) return art;   // a crop / sub-sprite
            if (src.mipmapCount != 1 || src.dimension != TextureDimension.Tex2D) return art;
            var fmt = src.graphicsFormat;
            if (fmt == GraphicsFormat.None || GraphicsFormatUtility.IsCompressedFormat(fmt)) return art;
            if (w + 2 > PageSize || h + 2 > PageSize) return art;
            var kind = src.format + "/" + fmt + "/" + src.filterMode + "/" + src.anisoLevel;
            Page page;
            int cx, cy;
            try
            {
                if (!Place(kind, src, w + 2, h + 2, out page, out cx, out cy))
                {
                    Misses++;
                    return art;
                }

                Copy(src, page.Tex, cx + 1, cy + 1, w, h);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] deck art atlas: " + ex.Message + " (" + art.name + " keeps its texture)");
                Misses++;
                return art;
            }

            var s = Sprite.Create(page.Tex, new Rect(cx + 1, cy + 1, w, h), new Vector2(art.pivot.x / w, art.pivot.y / h),
                art.pixelsPerUnit, 0, SpriteMeshType.FullRect, art.border);
            s.name = art.name;
            Made.Add(s);
            BySource[src] = s;
            page.Cells++;
            Cells++;
            return s;
        }

        /// <summary>A shelf packer: rows of cells (each card + its gutter) left to right, a new row when the cell does not fit,
        /// a new page (≤ 2 per kind) when no row does.</summary>
        static bool Place(string kind, Texture2D src, int cw, int ch, out Page page, out int x, out int y)
        {
            x = y = 0;
            var n = 0;
            foreach (var p in Pages)
            {
                if (p.Kind != kind) continue;
                n++;
                if (Fit(p, cw, ch, out x, out y))
                {
                    page = p;
                    return true;
                }
            }

            page = null;
            if (n >= MaxPagesPerKind || BadKinds.Contains(kind)) return false;
            // the source's own TextureFormat + colour space (RGB24 / RGBA32 sRGB from LoadImage): the same GPU format, however
            // the platform stores it, so CopyTexture is a plain copy
            var tex = new Texture2D(PageSize, PageSize, src.format, false, !src.isDataSRGB)
            {
                name = "MenuV6DeckArtAtlas_" + Pages.Count,
                filterMode = src.filterMode,
                anisoLevel = src.anisoLevel,
                wrapMode = TextureWrapMode.Clamp
                // no HideFlags: a page the hall never released is unloaded with the scene like CardDatabase's own textures
                // (DontSave would keep it in the Editor after Play)
            };
            // p8review: CopyTexture between two members of one format class (e.g. R8G8B8A8_SRGB and _UNorm) copies the raw bytes
            // without an error, and the page would then decode them in the other colour space: the page must be EXACTLY the
            // source's graphics format, else this kind keeps its own textures
            if (tex.graphicsFormat != src.graphicsFormat)
            {
                Debug.LogWarning("[WRLDZ MenuV6] deck art atlas: a page would be " + tex.graphicsFormat + ", the cards are " + src.graphicsFormat +
                                 " (" + kind + " keeps its textures)");
                BadKinds.Add(kind);
                Kill(tex);
                return false;
            }

            tex.Apply(false, true);   // on the GPU only: the cards are copied there, the CPU copy is dropped
            page = new Page { Tex = tex, Kind = kind, ShelfY = 0, ShelfH = 0, X = 0 };
            Pages.Add(page);
            return Fit(page, cw, ch, out x, out y);
        }

        static bool Fit(Page p, int cw, int ch, out int x, out int y)
        {
            x = y = 0;
            // the current shelf takes it when there is width left and it is no taller than the shelf (an empty shelf: when
            // the page has the height left)
            var fits = p.X + cw <= PageSize && (p.ShelfH == 0 ? p.ShelfY + ch <= PageSize : ch <= p.ShelfH);
            if (!fits)
            {
                if (p.ShelfH == 0) return false;   // an empty shelf cannot take it: neither can this page
                var ny = p.ShelfY + p.ShelfH;
                if (ny + ch > PageSize || cw > PageSize) return false;
                p.ShelfY = ny;
                p.ShelfH = 0;
                p.X = 0;
            }

            if (p.ShelfH == 0) p.ShelfH = ch;
            x = p.X;
            y = p.ShelfY;
            p.X += cw;
            return true;
        }

        /// <summary>The card at (x, y) and its 1-texel gutter: what the source's sampler gives one texel past each edge.</summary>
        static void Copy(Texture2D src, Texture2D dst, int x, int y, int w, int h)
        {
            Graphics.CopyTexture(src, 0, 0, 0, 0, w, h, dst, 0, 0, x, y);
            var lc = src.wrapModeU == TextureWrapMode.Repeat ? w - 1 : 0;   // the column a tap left of u = 0 reads
            var rc = src.wrapModeU == TextureWrapMode.Repeat ? 0 : w - 1;   // ... right of u = 1
            var bc = src.wrapModeV == TextureWrapMode.Repeat ? h - 1 : 0;   // ... below v = 0
            var tc = src.wrapModeV == TextureWrapMode.Repeat ? 0 : h - 1;   // ... above v = 1
            Graphics.CopyTexture(src, 0, 0, lc, 0, 1, h, dst, 0, 0, x - 1, y);
            Graphics.CopyTexture(src, 0, 0, rc, 0, 1, h, dst, 0, 0, x + w, y);
            Graphics.CopyTexture(src, 0, 0, 0, bc, w, 1, dst, 0, 0, x, y - 1);
            Graphics.CopyTexture(src, 0, 0, 0, tc, w, 1, dst, 0, 0, x, y + h);
            Graphics.CopyTexture(src, 0, 0, lc, bc, 1, 1, dst, 0, 0, x - 1, y - 1);
            Graphics.CopyTexture(src, 0, 0, rc, bc, 1, 1, dst, 0, 0, x + w, y - 1);
            Graphics.CopyTexture(src, 0, 0, lc, tc, 1, 1, dst, 0, 0, x - 1, y + h);
            Graphics.CopyTexture(src, 0, 0, rc, tc, 1, 1, dst, 0, 0, x + w, y + h);
        }

        /// <summary>Free every page and page sprite (a chip still showing one would draw nothing: only after the last hall).</summary>
        public static void Clear()
        {
            foreach (var s in Made)
                if (s != null)
                    Kill(s);
            foreach (var p in Pages)
                if (p.Tex != null)
                    Kill(p.Tex);
            Made.Clear();
            Pages.Clear();
            BySource.Clear();
            BadKinds.Clear();
            Cells = 0;
            Misses = 0;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        /// <summary>One line for the budget / smoke notes.</summary>
        public static string Line() =>
            "pages " + Pages.Count + ", cells " + Cells + ", misses " + Misses + ", users " + _users + ", supported " + Supported +
            ", on " + MenuV6Perf.DeckArtOn;
    }
}
