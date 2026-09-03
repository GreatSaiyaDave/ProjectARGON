using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Free third-party UI packs vendored under StreamingAssets/WRLDZ/Vendor/.
    /// Kenney (CC0), Free Game GUI, GDquest mobile buttons, LPC samples (credit file).
    /// Loads as 9-slice-friendly sprites for clean GO-style chrome.
    /// </summary>
    public static class VendorKit
    {
        const string Root = "WRLDZ/Vendor";
        static readonly Dictionary<string, Sprite> Cache = new();
        static bool _logged;

        public static void EnsureReady()
        {
            if (_logged) return;
            _logged = true;
            var dir = Path.Combine(Application.streamingAssetsPath, Root);
            var exists = Directory.Exists(dir);
            PinEditorCwd();
            Debug.Log(exists
                ? $"[WRLDZ] VendorKit ready at {dir}"
                : $"[WRLDZ] VendorKit missing folder {dir}");
        }

        // ── Kenney UI Pack (preferred chrome) ─────────────────────────────

        public static Sprite RoundButton(string color = "Blue") =>
            LoadSliced($"KenneyUI/{color}/button_round_depth_gradient.png", 20)
            ?? LoadSliced($"KenneyUI/{color}/button_round_gradient.png", 20)
            ?? Load($"KenneyUI/{color}/button_round_flat.png");

        public static Sprite RoundButtonBorder(string color = "Blue") =>
            LoadSliced($"KenneyUI/{color}/button_round_depth_border.png", 20)
            ?? RoundButton(color);

        public static Sprite RectButton(string color = "Blue") =>
            LoadSliced($"KenneyUI/{color}/button_rectangle_depth_gradient.png", 16, 16, 16, 16)
            ?? LoadSliced($"KenneyUI/{color}/button_rectangle_gradient.png", 16);

        public static Sprite SquareButton(string color = "Grey") =>
            LoadSliced($"KenneyUI/{color}/button_square_depth_flat.png", 12)
            ?? Load($"KenneyUI/{color}/button_square_flat.png");

        public static Sprite CheckCircle(string color = "Blue") =>
            Load($"KenneyUI/{color}/check_round_color.png")
            ?? Load($"KenneyUI/{color}/icon_circle.png");

        public static Sprite IconCircle(string color = "Blue") =>
            Load($"KenneyUI/{color}/icon_circle.png");

        public static Sprite Star(string color = "Yellow") =>
            Load($"KenneyUI/{color}/star.png")
            ?? Load("KenneyUI/Extra/star.png");

        public static Sprite Arrow(string dir /*n s e w*/, string color = "Blue") =>
            Load($"KenneyUI/{color}/arrow_basic_{dir}.png");

        // ── Kenney RPG panels ─────────────────────────────────────────────

        public static Sprite PanelBlue() =>
            LoadSliced("KenneyRPG/panel_blue.png", 20)
            ?? LoadSliced("KenneyRPG/panel_beige.png", 20);

        public static Sprite PanelBeige() =>
            LoadSliced("KenneyRPG/panel_beige.png", 20)
            ?? LoadSliced("KenneyRPG/panel_beigeLight.png", 20);

        public static Sprite PanelInset() =>
            LoadSliced("KenneyRPG/panelInset_beige.png", 16)
            ?? LoadSliced("KenneyRPG/panelInset_blue.png", 16);

        // ── Free Game GUI (huge source — use carefully) ───────────────────

        public static Sprite Window() =>
            LoadSliced("FreeGameGUI/Window.png", 80, 100, 80, 100);

        public static Sprite CasualButton() =>
            LoadSliced("FreeGameGUI/Button.png", 60, 80, 60, 80);

        // ── GDquest cartoon mobile buttons ────────────────────────────────

        public static Sprite MobilePlay() => Load("MobileButtons/b_Play1.png");
        public static Sprite MobileMore() => Load("MobileButtons/b_More1.png");
        public static Sprite MobileParams() => Load("MobileButtons/b_Parameters.png");
        public static Sprite MobileYes() => Load("MobileButtons/b_Yes.png");
        public static Sprite MobileNo() => Load("MobileButtons/b_No.png");
        public static Sprite MobileLeaderboard() => Load("MobileButtons/b_Leaderboard.png");
        public static Sprite MobileForward() => Load("MobileButtons/b_Forward1.png");
        public static Sprite MobileRestart() => Load("MobileButtons/b_Restart.png");

        // ── Kenney game icons (subset) ────────────────────────────────────

        public static Sprite Icon(string name) => Load($"Icons/{name}.png");
        public static Sprite IconHome() => Icon("home") ?? Icon("button1");
        public static Sprite IconGear() => Icon("gear") ?? Icon("wrench") ?? MobileParams();
        public static Sprite IconBag() => Icon("shoppingCart") ?? Icon("shoppingBasket") ?? MobileMore();
        public static Sprite IconTrophy() => Icon("trophy") ?? MobileLeaderboard();
        public static Sprite IconSingle() => Icon("singleplayer") ?? Icon("person") ?? RoundButton();

        // ── LPC map avatar walk (credit Liberated Pixel Cup) ──────────────

        public static Sprite LpcMaleWalk() => Load("LPC/body_male_walk.png");
        public static Sprite LpcFemaleWalk() => Load("LPC/body_female_walk.png");

        // ── Loaders ───────────────────────────────────────────────────────

        public static Sprite Load(string relativeUnderVendor)
        {
            EnsureReady();
            var key = relativeUnderVendor;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var full = Path.Combine(Application.streamingAssetsPath, Root, relativeUnderVendor);
            if (!File.Exists(full))
            {
                PinEditorCwd();
                return null;
            }

            var bytes = File.ReadAllBytes(full);
            PinEditorCwd();
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Object.Destroy(tex);
                return null;
            }

            tex.name = Path.GetFileNameWithoutExtension(relativeUnderVendor);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
            spr.name = tex.name;
            Cache[key] = spr;
            return spr;
        }

        /// <summary>9-slice borders in pixels (L,B,R,T).</summary>
        public static Sprite LoadSliced(string relativeUnderVendor, float border)
            => LoadSliced(relativeUnderVendor, border, border, border, border);

        public static Sprite LoadSliced(string relativeUnderVendor, float l, float b, float r, float t)
        {
            EnsureReady();
            var key = relativeUnderVendor + $"#slice{l}_{b}_{r}_{t}";
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var full = Path.Combine(Application.streamingAssetsPath, Root, relativeUnderVendor);
            if (!File.Exists(full))
            {
                PinEditorCwd();
                return null;
            }

            var bytes = File.ReadAllBytes(full);
            PinEditorCwd();
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Object.Destroy(tex);
                return null;
            }

            tex.name = Path.GetFileNameWithoutExtension(relativeUnderVendor);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            // Clamp borders to half size
            var bl = Mathf.Min(l, tex.width * 0.45f);
            var bb = Mathf.Min(b, tex.height * 0.45f);
            var br = Mathf.Min(r, tex.width * 0.45f);
            var bt = Mathf.Min(t, tex.height * 0.45f);
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(bl, bb, br, bt));
            spr.name = tex.name + "_sliced";
            Cache[key] = spr;
            return spr;
        }

        static void PinEditorCwd()
        {
#if UNITY_EDITOR
            try
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                Directory.SetCurrentDirectory(root);
            }
            catch
            {
                // Editor guard also pins on compile / update.
            }
#endif
        }
    }
}
