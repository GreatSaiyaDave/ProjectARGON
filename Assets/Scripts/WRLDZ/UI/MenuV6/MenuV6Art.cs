using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The v6 art loader (BUILD_PLAN §4, D6, D7). Imported textures under <c>Assets/Resources/WRLDZ/MenuV6/</c> through
    /// <c>Resources.Load</c> only: no direct file reads and no StreamingAssets loader (it decodes to RGBA32 and keys out
    /// dark corners), so the import settings in each .meta (BC7 / ASTC) are what the menu draws.
    /// Local-only art (<c>MenuV6Local/</c>, gitignored: ripped or unlicensed pixels) is reached only through
    /// <see cref="LocalOr"/>; a clone without it gets the tracked fallback. Every miss returns null and every caller
    /// skips what is missing, so the menu builds with no art at all (MenuSmokeTest, edit mode).
    /// </summary>
    public static class MenuV6Art
    {
        public const string Root = "WRLDZ/MenuV6/";
        public const string LocalRoot = "WRLDZ/MenuV6Local/";
        public const string DataRoot = Root + "Data/";
        public const string FontRoot = Root + "Fonts/";

        /// <summary>Tests: ignore MenuV6Local/ (what a clone without the local folder sees).</summary>
        public static bool ForceNoLocal;

        /// <summary>Tests: every load misses (proves the v6 builder is null-safe with no art).</summary>
        public static bool ForceNoArt;

        static readonly Dictionary<string, Object> Cache = new();
        static readonly HashSet<string> Misses = new();

        static T Load<T>(string path) where T : Object
        {
            if (ForceNoArt || string.IsNullOrEmpty(path)) return null;
            var key = typeof(T).Name + ":" + path;
            if (Cache.TryGetValue(key, out var hit))
            {
                if (hit != null) return hit as T;   // Unity null: reimported / unloaded -> reload
                Cache.Remove(key);
            }
            else if (Misses.Contains(key))
            {
                return null;
            }

            T asset = null;
            try { asset = Resources.Load<T>(path); }
            catch { asset = null; }

            if (asset != null) Cache[key] = asset;
            else Misses.Add(key);
            return asset;
        }

        static bool IsLocalPath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith(LocalRoot);

        /// <summary>A sprite by Resources path (e.g. "WRLDZ/MenuV6/Main/Land/room"); tracked art only. P8: with
        /// <see cref="MenuV6Perf.AtlasBypass"/> a sprite packed into a Sprite Atlas comes back unpacked (<see cref="Unpacked"/>).</summary>
        public static Sprite Sprite(string path)
        {
            if (IsLocalPath(path)) return null;
            var s = Load<Sprite>(path);
            return s != null && s.packed && MenuV6Perf.Bypass ? Unpacked(path, s) : s;
        }

        static readonly Dictionary<string, Sprite> UnpackedCache = new();

        /// <summary>
        /// P8 (the capture's "before" for the Sprite Atlas V2 groups): <paramref name="packed"/> as the menu drew it before the
        /// atlases — the same rect, pivot, pixels per unit, border and mesh type (FullRect, as the importer writes them), on its
        /// own texture (the Texture2D at the same Resources path). The packed sprite when that texture is missing.
        /// </summary>
        static Sprite Unpacked(string path, Sprite packed)
        {
            if (UnpackedCache.TryGetValue(path, out var hit))
            {
                if (hit != null) return hit;
                UnpackedCache.Remove(path);
            }

            var tex = Load<Texture2D>(path);
            if (tex == null) return packed;
            var r = packed.rect;
            if (r.width <= 0 || r.height <= 0 || r.xMax > tex.width || r.yMax > tex.height) return packed;
            var s = UnityEngine.Sprite.Create(tex, r, new Vector2(packed.pivot.x / r.width, packed.pivot.y / r.height),
                packed.pixelsPerUnit, 0, SpriteMeshType.FullRect, packed.border);
            s.name = packed.name;
            UnpackedCache[path] = s;
            return s;
        }

        /// <summary>The local-only sprite when present (and not <see cref="ForceNoLocal"/>), else the tracked fallback.</summary>
        public static Sprite LocalOr(string localPath, string fallbackPath)
        {
            if (!ForceNoLocal && IsLocalPath(localPath))
            {
                var local = Load<Sprite>(localPath);
                if (local != null) return local;
            }

            return Sprite(fallbackPath);
        }

        /// <summary>True when the local-only file for <paramref name="localPath"/> is what <see cref="LocalOr"/> returns.</summary>
        public static bool HasLocal(string localPath) =>
            !ForceNoLocal && IsLocalPath(localPath) && Load<Sprite>(localPath) != null;

        public static Texture2D Texture(string path) => IsLocalPath(path) ? null : Load<Texture2D>(path);

        /// <summary>A local-only asset of another type (P2: the Genesis VideoClip, the lock click AudioClip): null when the
        /// local folder is absent or <see cref="ForceNoLocal"/> is set, so every caller keeps its fallback.</summary>
        public static T LocalAsset<T>(string localPath) where T : Object =>
            !ForceNoLocal && IsLocalPath(localPath) ? Load<T>(localPath) : null;

        /// <summary>A Data/*.json TextAsset (layout_land, schedules, manifest).</summary>
        public static TextAsset Data(string name) => Load<TextAsset>(DataRoot + name);

        /// <summary>A v6 font file by its file name without extension (e.g. "Cairo-700").</summary>
        public static Font Font(string fileName) => Load<Font>(FontRoot + fileName);

        /// <summary>Forget cached assets and misses (after art lands in an open Editor, or between test passes).</summary>
        public static void ClearCache()
        {
            Cache.Clear();
            Misses.Clear();
            UnpackedCache.Clear();
            MenuV6Perf.ClearCache();
        }
    }
}
