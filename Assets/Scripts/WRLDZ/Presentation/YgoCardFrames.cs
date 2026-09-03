using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Card frames / backs from Custom Yu-Gi-Oh! Database Wiki Assets page:
    /// https://custom-yugioh-database.fandom.com/wiki/Assets
    ///
    /// Stored under StreamingAssets/WRLDZ/YgoFrames/
    /// Templates are blank 421×614 series-style frames with a white art hole.
    /// </summary>
    public static class YgoCardFrames
    {
        public const string Root = "WRLDZ/YgoFrames";

        /// <summary>
        /// Normalized art-window anchors on Card-* templates (bottom-left UI space).
        /// Measured on Card-effect.png (white hole ≈ x 51–370, y 113–432 of 421×614).
        /// </summary>
        public static readonly Vector2 ArtAnchorMin = new(0.121f, 0.297f);
        public static readonly Vector2 ArtAnchorMax = new(0.879f, 0.816f);

        /// <summary>Lower name/stats band for glance labels (under art, above ATK bar).</summary>
        public static readonly Vector2 NameplateMin = new(0.08f, 0.06f);
        public static readonly Vector2 NameplateMax = new(0.92f, 0.28f);

        static readonly Dictionary<string, Sprite> Cache = new();
        static bool _logged;

        public static void EnsureReady()
        {
            if (_logged) return;
            _logged = true;
            var dir = Path.Combine(Application.streamingAssetsPath, Root);
            Debug.Log(Directory.Exists(dir)
                ? $"[WRLDZ] YgoCardFrames ready — {dir}"
                : $"[WRLDZ] YgoCardFrames missing {dir}");
        }

        public static Sprite FrameForArtifact()
        {
            EnsureReady();
            return Frame("Card-artifact.png") ?? Frame("Card-spell.png") ?? Frame("Card-effect.png");
        }

        public static Sprite FrameFor(CardDef def)
        {
            EnsureReady();
            if (def == null) return Frame("Card-effect.png");

            var type = (def.type ?? "") + " " + (def.frameType ?? "");
            var t = type.ToLowerInvariant();

            if (t.Contains("link")) return Frame("Card-link.png") ?? Frame("Card-effect.png");
            if (t.Contains("xyz") || t.Contains("xyz")) return Frame("Card-xyz.png") ?? Frame("Card-effect.png");
            if (t.Contains("synchro")) return Frame("Card-synchro.png") ?? Frame("Card-effect.png");
            if (t.Contains("fusion")) return Frame("Card-fusion.png") ?? Frame("Card-effect.png");
            if (t.Contains("ritual")) return Frame("Card-ritual.png") ?? Frame("Card-effect.png");
            if (t.Contains("token")) return Frame("Card-token.png") ?? Frame("Card-normal.png");
            if (t.Contains("trap")) return Frame("Card-trap.png");
            if (t.Contains("spell")) return Frame("Card-spell.png");
            if (t.Contains("normal") && t.Contains("monster") && !t.Contains("effect"))
                return Frame("Card-normal.png");
            // Default monsters with effects / unknown
            if (t.Contains("monster")) return Frame("Card-effect.png");
            return Frame("Card-effect.png");
        }

        public static Sprite Frame(string fileName)
        {
            EnsureReady();
            return Load($"Templates/{fileName}")
                   ?? Load($"Templates/{fileName.Replace(' ', '_')}");
        }

        /// <summary>
        /// Official face-down art. Primary = HD swirl back (Downloads Back_card_yugioh_hd).
        /// </summary>
        public static Sprite CardBack()
        {
            EnsureReady();
            return Load("Backs/Back_card_yugioh_hd_by_carlos123321_dbwk3sn-pre.png")
                   ?? StreamingSprite.Load("WRLDZ/CardBack/card_back.png")
                   ?? Load("Backs/Back_card_yugioh_hd_by_carlos123321_dbwk3sn-pre.jpg")
                   ?? Load("Backs/Back3.png")
                   ?? Load("Backs/Back4.png")
                   ?? Load("Backs/Back2.jpg")
                   ?? Load("Backs/Gold_back_card_yugioh_by_carlos123321_dc2zeu0-pre.jpg")
                   ?? Load("Backs/Back_card_yugioh_custom_by_carlos123321_dc2zab9-pre.jpg")
                   ?? Load("Backs/Back1.jpg")
                   ?? StreamingSprite.Load("WRLDZ/CardBack/card_back_wrldz.png");
        }

        public static Sprite Load(string relativeUnderYgoFrames)
        {
            EnsureReady();
            var key = relativeUnderYgoFrames;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;

            var full = Path.Combine(Application.streamingAssetsPath, Root, relativeUnderYgoFrames);
            if (!File.Exists(full)) return null;

            var bytes = File.ReadAllBytes(full);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }

            tex.name = Path.GetFileNameWithoutExtension(relativeUnderYgoFrames);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
            spr.name = tex.name;
            Cache[key] = spr;
            return spr;
        }
    }
}
