using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// P8 (PERF_PLAN.md steps 1-3): the draw-call work's runtime switches and the Sprite Atlas V2 group table. Every switch
    /// keeps the pixels: an optimisation that is off draws exactly what P7 drew, so MenuV6Capture can shoot the same frozen
    /// frames with each one off and on in ONE Play session and diff them (budget_p8.json, captures/p8).
    /// <list type="bullet">
    /// <item><see cref="DeckArtAtlas"/> (step 1): the deck's chip and slot card art drawn from run-time atlas pages
    /// (<see cref="WRLDZ.UI.MenuV6.DeckArtAtlas"/>: the same texels, copied).</item>
    /// <item>The Sprite Atlas V2 groups (step 2, <see cref="Atlases"/>): Editor assets made in the landing slot
    /// (MenuV6Atlases.Apply). <see cref="AtlasBypass"/> is the capture's "before": the packed sprites are drawn from their
    /// own textures again, as unpacked sprites (MenuV6Art).</item>
    /// <item><see cref="ShapesOnAtlas"/> (step 3): a rig's vector layer (RigShapes) samples the 8×8 white block of the atlas
    /// page its neighbouring quads use instead of UGUI's white texture: white × vertex colour either way, so the shape and
    /// quad layers of one rig share a texture and stop breaking the batch.</item>
    /// </list>
    /// The kill switch PlayerPref <see cref="KillPref"/> = 1 turns all three off (as WRLDZ_MenuV6Off does for the whole v6
    /// menu). Tests and the capture set the statics; nothing here is saved.
    /// </summary>
    public static class MenuV6Perf
    {
        /// <summary>PlayerPrefs int: 1 = every P8 optimisation off (read once; <see cref="ResetKill"/> re-reads it).</summary>
        public const string KillPref = "WRLDZ_MenuV6PerfOff";

        static int _kill = -1;

        public static bool Killed
        {
            get
            {
                if (_kill < 0)
                {
                    try { _kill = PlayerPrefs.GetInt(KillPref, 0) != 0 ? 1 : 0; }
                    catch { _kill = 0; }
                }

                return _kill == 1;
            }
        }

        public static void ResetKill() => _kill = -1;

        /// <summary>Step 1: the deck's card art through the run-time atlas pages.</summary>
        public static bool DeckArtAtlas = true;

        /// <summary>Step 3: RigShapes on its atlas page's white block.</summary>
        public static bool ShapesOnAtlas = true;

        /// <summary>Step 2, the capture's "before": draw every packed MenuV6 sprite from its own texture (unpacked).</summary>
        public static bool AtlasBypass;

        public static bool DeckArtOn => DeckArtAtlas && !Killed;
        public static bool ShapesOn => ShapesOnAtlas && !Killed;
        public static bool Bypass => AtlasBypass || Killed;

        /// <summary>All three on (the shipped state) or off (P7's draws).</summary>
        public static void SetAll(bool on)
        {
            DeckArtAtlas = on;
            ShapesOnAtlas = on;
            AtlasBypass = !on;
        }

        public static string State() =>
            "deckArt " + DeckArtOn + ", shapesOnAtlas " + ShapesOn + ", atlasBypass " + Bypass + (Killed ? " (killed by " + KillPref + ")" : "");

        // ------------------------------------------------------------------ the Sprite Atlas V2 groups (step 2)

        /// <summary>One Sprite Atlas V2 asset: every sprite texture directly in <see cref="Folders"/> (Resources paths under
        /// WRLDZ/MenuV6/) whose longest side is under <see cref="MaxSide"/>, minus <see cref="Exclude"/>, plus its 8×8 white
        /// block <see cref="White"/>.</summary>
        public sealed class AtlasDef
        {
            public string Name;
            public string[] Folders;
            public string[] Exclude;
            public string White;

            /// <summary>Android packs at ASTC 4×4 (a group that mixes 4×4 and 6×6 sources: the 6×6 ones gain, none loses).</summary>
            public string AssetPath => "Assets/Resources/" + AtlasRoot + Name + ".spriteatlasv2";

            public string ResourcesPath => AtlasRoot + Name;
        }

        /// <summary>The atlases and their white blocks live here (Resources path; the folder comes with the P8 art).</summary>
        public const string AtlasRoot = MenuV6Art.Root + "Atlas/";

        /// <summary>Sprites this big or bigger stay out (they would fill a page alone, PERF_PLAN §4 step 2).</summary>
        public const int MaxSide = 1024;

        /// <summary>The packing every atlas keeps (PERF_PLAN §4 step 2; MenuV6Atlases.Apply writes it, the hub guard checks it).</summary>
        public const int Padding = 8;

        public const int PageMax = 2048;

        static string W(string name) => AtlasRoot + name + "_white";

        /// <summary>
        /// PERF_PLAN §4 step 2's table. Out of every group: sprites ≥ 1024 px, textures read as Texture2D (the flipbook sheets:
        /// Rigs/&lt;plan&gt;/keystone/glint and niche/sliver, loaded by IRigGfx.Sheet), tiling, run-time and local-only art,
        /// fonts. Rigs/Common is shared by both plans and a sprite can sit in one atlas only: it goes with the landscape rigs
        /// (the portrait keeps drawing it from its own texture, as today).
        /// </summary>
        public static readonly AtlasDef[] Atlases =
        {
            new() { Name = "MenuV6_Recess_Land", Folders = new[] { "Rigs/Land/recess" }, Exclude = new string[0], White = W("MenuV6_Recess_Land") },
            new() { Name = "MenuV6_Recess_Port", Folders = new[] { "Rigs/Port/recess" }, Exclude = new string[0], White = W("MenuV6_Recess_Port") },
            new()
            {
                Name = "MenuV6_Rigs_Land",
                Folders = new[] { "Rigs/Land/niche", "Rigs/Land/keystone", "Rigs/Land/dom", "Rigs/Land/breach", "Rigs/Common" },
                Exclude = new[] { "Rigs/Land/keystone/glint", "Rigs/Land/niche/sliver" }, White = W("MenuV6_Rigs_Land")
            },
            new()
            {
                Name = "MenuV6_Rigs_Port",
                Folders = new[] { "Rigs/Port/niche", "Rigs/Port/keystone", "Rigs/Port/dom", "Rigs/Port/breach" },
                Exclude = new[] { "Rigs/Port/keystone/glint", "Rigs/Port/niche/sliver" }, White = W("MenuV6_Rigs_Port")
            },
            new()
            {
                Name = "MenuV6_Deck_Land",
                Folders = new[] { "Deck/Land/chrome", "Deck/Land/scale", "Deck/Land/icons", "Deck/Land/box", "Deck/Land/pz" },
                Exclude = new string[0], White = W("MenuV6_Deck_Land")
            },
            new()
            {
                Name = "MenuV6_Deck_Port",
                Folders = new[] { "Deck/Port/chrome", "Deck/Port/scale", "Deck/Port/icons", "Deck/Port/box", "Deck/Port/pz" },
                Exclude = new string[0], White = W("MenuV6_Deck_Port")
            },
            new()
            {
                Name = "MenuV6_Title_Land", Folders = new[] { "Title/Land/galaxy", "Title/Land/ring", "Title/Land/puzzle", "Title/Land/items" },
                Exclude = new string[0], White = W("MenuV6_Title_Land")
            },
            new()
            {
                Name = "MenuV6_Title_Port", Folders = new[] { "Title/Port/galaxy", "Title/Port/ring", "Title/Port/puzzle", "Title/Port/items" },
                Exclude = new string[0], White = W("MenuV6_Title_Port")
            },
        };

        // ------------------------------------------------------------------ step 3: the white blocks

        static readonly Dictionary<Texture, Vector2> WhiteUv = new();
        static readonly Dictionary<Transform, Texture> HostPages = new();
        static int _hostFrame = -1;

        /// <summary>The atlas row that packs the sprite at Resources path <paramref name="key"/> (its folder is one of the
        /// row's), or null.</summary>
        public static AtlasDef AtlasOf(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(MenuV6Art.Root, StringComparison.Ordinal)) return null;
            var slash = key.LastIndexOf('/');
            if (slash <= MenuV6Art.Root.Length) return null;
            var folder = key.Substring(MenuV6Art.Root.Length, slash - MenuV6Art.Root.Length);
            foreach (var a in Atlases)
                foreach (var f in a.Folders)
                    if (f == folder)
                        return a;
            return null;
        }

        /// <summary>
        /// True when <paramref name="page"/> is the packed page of <paramref name="a"/>'s white block; <paramref name="uv"/> is
        /// then the centre of a 4×4-aligned texel block wholly inside that 8×8 white (the bilinear tap reads white texels
        /// only, whatever block grid the BC7 / ASTC page uses). Only that row's white sprite is loaded: a screen never pulls in
        /// the pages of atlases it does not draw.
        /// </summary>
        public static bool WhiteOn(AtlasDef a, Texture page, out Vector2 uv)
        {
            uv = Vector2.zero;
            if (a == null || page == null) return false;
            if (WhiteUv.TryGetValue(page, out uv)) return true;
            var s = MenuV6Art.Sprite(a.White);
            if (s == null || !s.packed || s.texture == null || s.texture != page) return false;
            var t = s.texture;
            var r = s.textureRect;
            if (r.width < 8 || r.height < 8 || t.width <= 0 || t.height <= 0) return false;
            var bx = Mathf.Ceil(r.x / 4f) * 4f;
            var by = Mathf.Ceil(r.y / 4f) * 4f;
            if (bx + 4 > r.xMax || by + 4 > r.yMax) return false;
            uv = new Vector2((bx + 2f) / t.width, (by + 2f) / t.height);
            WhiteUv[t] = uv;
            return true;
        }

        /// <summary>The atlas page (one with its row's white block) that most of the RigQuads beside <paramref name="t"/> draw
        /// from (its rig's host: every shape layer of one rig takes the same page), or null when none does.</summary>
        public static Texture HostPage(Transform t, out Vector2 uv)
        {
            uv = Vector2.zero;
            var p = t != null ? t.parent : null;
            if (p == null) return null;
            if (_hostFrame != Time.frameCount)
            {
                _hostFrame = Time.frameCount;
                HostPages.Clear();
            }

            if (!HostPages.TryGetValue(p, out var best))
            {
                var count = new Dictionary<Texture, int>();
                var n = 0;
                for (var i = 0; i < p.childCount; i++)
                {
                    if (!p.GetChild(i).TryGetComponent(out RigQuads q) || q.Layer == null) continue;
                    var rt = q.Layer.Tex;
                    if (rt == null || rt.Tex == null || rt.Sprite == null || !rt.Sprite.packed) continue;
                    if (!WhiteOn(AtlasOf(rt.Key), rt.Tex, out _)) continue;
                    count[rt.Tex] = (count.TryGetValue(rt.Tex, out var k) ? k : 0) + 1;
                    if (count[rt.Tex] > n)
                    {
                        n = count[rt.Tex];
                        best = rt.Tex;
                    }
                }

                HostPages[p] = best;
            }

            return best != null && WhiteUv.TryGetValue(best, out uv) ? best : null;
        }

        /// <summary>Forget the white blocks (after the atlases change in an open Editor).</summary>
        public static void ClearCache()
        {
            WhiteUv.Clear();
            HostPages.Clear();
        }

        /// <summary>The atlas state as one line (MenuSmokeTest / MenuV6Capture): per atlas, its white block's page or why not.</summary>
        public static string AtlasLine()
        {
            var sb = new StringBuilder();
            foreach (var a in Atlases)
            {
                var s = MenuV6Art.Sprite(a.White);
                sb.Append(a.Name).Append(": ").Append(s == null ? "no white" : !s.packed ? "unpacked" :
                    s.texture != null ? s.texture.name + " " + s.texture.width + "x" + s.texture.height : "no page").Append("; ");
            }

            return sb.ToString();
        }
    }
}
