using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.UI
{
    /// <summary>
    /// Loads free UI kit from StreamingAssets/UI/kit
    /// (Kenney CC0 + original duel-disk art + YGO-manga display fonts).
    /// </summary>
    public static class FreeUiKit
    {
        static readonly Dictionary<string, Sprite> Sprites = new();
        static readonly Dictionary<string, Texture2D> Textures = new();
        static Font _display;
        static Font _body;
        static Font _ui; // condensed punch for buttons / short labels
        static AudioClip _click;
        static AudioClip _select;
        static AudioClip _confirm;
        static AudioSource _sfx;
        static bool _attempted;

        public static string KitRoot => Path.Combine(Application.streamingAssetsPath, "UI", "kit");
        public static string UiRoot => Path.Combine(Application.streamingAssetsPath, "UI");

        public static void EnsureLoaded()
        {
            if (_attempted) return;
            _attempted = true;

            // Display — manga SFX / shonen duel shout energy
            _display = Resources.Load<Font>("WRLDZ/Fonts/Bangers-Regular")
                       ?? Resources.Load<Font>("WRLDZ/Fonts/Rowdies-Bold")
                       ?? Resources.Load<Font>("WRLDZ/Fonts/RussoOne-Regular")
                       ?? Resources.Load<Font>("WRLDZ/Fonts/Orbitron-Bold")
                       ?? Resources.Load<Font>("WRLDZ/Fonts/Kenney Future");

            // Body — clear modern UI, still futuristic (outdoor readable)
            _body = Resources.Load<Font>("WRLDZ/Fonts/Exo2-Bold")
                    ?? Resources.Load<Font>("WRLDZ/Fonts/Exo2-SemiBold")
                    ?? Resources.Load<Font>("WRLDZ/Fonts/RussoOne-Regular")
                    ?? Resources.Load<Font>("WRLDZ/Fonts/Kenney Future Narrow")
                    ?? _display;

            // UI punch — condensed bold for buttons / short labels
            _ui = Resources.Load<Font>("WRLDZ/Fonts/RussoOne-Regular")
                  ?? Resources.Load<Font>("WRLDZ/Fonts/Rowdies-Bold")
                  ?? _body;

            // OS dynamic fallback if Resources not yet imported (Editor first open)
            _display ??= TryOsFont("Bangers", "Rowdies", "Impact", "Arial Black");
            _body ??= TryOsFont("Exo 2", "Exo2", "Ubuntu", "DejaVu Sans");
            _ui ??= TryOsFont("Russo One", "Arial Black", "DejaVu Sans Bold");

            if (_display == null)
                _display = UiFoundation.BuiltinFont();
            if (_body == null) _body = _display;
            if (_ui == null) _ui = _body;

            Debug.Log($"[WRLDZ] Fonts — display={_display?.name} body={_body?.name} ui={_ui?.name}");

            _click = Resources.Load<AudioClip>("WRLDZ/Audio/click");
            _select = Resources.Load<AudioClip>("WRLDZ/Audio/select");
            _confirm = Resources.Load<AudioClip>("WRLDZ/Audio/confirm");

            // Core YGO-feeling art
            Preload("bg/field.png");
            Preload("bg/disk_arm.png");
            Preload("bg/disk_holo.png");
            Preload("bg/tab_bar_v2.png");
            Preload("bg/glow_soft.png");
            Preload("bg/vignette.png");
            Preload("bg/bg_gradient.png");
            Preload("bg/hex_overlay.png");
            Preload("bg/neon_line.png");
            Preload("bg/panel_neon.png", new Vector4(32, 32, 32, 32));
            Preload("hub/duel_hub.png");
            Preload("hub/duel_face.png");
            Preload("hub/duel_text_scrim.png");
            Preload("panels/glass_tile.png", new Vector4(40, 40, 40, 40));
            Preload("panels/glass_wide.png", new Vector4(40, 40, 40, 40));
            Preload("panels/glass_square.png", new Vector4(36, 36, 36, 36));
            Preload("panels/badge_plate.png");
            Preload("panels/plate_dark.png", new Vector4(36, 36, 36, 36));
            Preload("panels/plate_dark_wide.png", new Vector4(36, 36, 36, 36));
            Preload("panels/plate_dark_square.png", new Vector4(32, 32, 32, 32));
            Preload("panels/plate_tabbar.png", new Vector4(12, 12, 12, 12));
            Preload("panels/plate_badge.png", new Vector4(24, 24, 24, 24));
            Preload("panels/label_scrim.png", new Vector4(20, 20, 20, 20));
            Preload("buttons/button_rectangle_depth_gloss.png", new Vector4(24, 24, 24, 24));
            Preload("buttons/button_rectangle_depth_border.png", new Vector4(24, 24, 24, 24));
            Preload("buttons/button_rectangle_gradient.png", new Vector4(20, 20, 20, 20));
            Preload("buttons/button_round_depth_gloss.png");
            Preload("buttons/star.png");
            Preload("buttons/icon_circle.png");

            foreach (var t in new[] { "tab_news", "tab_duel", "tab_decks", "tab_events", "tab_data" })
                Preload($"tabs/{t}.png");
            foreach (var i in new[]
                     {
                         "ico_decklab", "ico_archives", "ico_settings", "ico_chevron",
                         "ico_battle", "ico_activate", "ico_set", "ico_end", "ico_menu"
                     })
                Preload($"icons/{i}.png");

            // External (not under kit/)
            LoadExternalSprite("battle_city_disk_bg.jpg");

            Debug.Log($"[WRLDZ] FreeUiKit ready — sprites={Sprites.Count} display={_display?.name} body={_body?.name}");
        }

        static Font TryOsFont(params string[] names)
        {
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(n, 48);
                    if (f != null && !string.IsNullOrEmpty(f.name))
                        return f;
                }
                catch
                {
                    // ignore missing OS face
                }
            }

            return null;
        }

        public static Font DisplayFont()
        {
            EnsureLoaded();
            return _display;
        }

        public static Font BodyFont()
        {
            EnsureLoaded();
            return _body ?? _display;
        }

        /// <summary>Condensed punch face for buttons and short chrome labels.</summary>
        public static Font UiFont()
        {
            EnsureLoaded();
            return _ui ?? _body ?? _display;
        }

        public static Sprite Sprite(string relativePath, float ppu = 100f, Vector4? border = null)
        {
            EnsureLoaded();
            relativePath = relativePath.Replace('\\', '/');
            if (Sprites.TryGetValue(relativePath, out var existing) && existing != null)
                return existing;

            var tex = Texture(relativePath);
            if (tex == null) return null;

            var sp = UnityEngine.Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                ppu,
                0,
                SpriteMeshType.FullRect,
                border ?? Vector4.zero);
            Sprites[relativePath] = sp;
            return sp;
        }

        public static Texture2D Texture(string relativePath)
        {
            EnsureLoaded();
            relativePath = relativePath.Replace('\\', '/');
            if (Textures.TryGetValue(relativePath, out var t) && t != null)
                return t;

            var path = Path.Combine(KitRoot, relativePath);
            if (!File.Exists(path))
            {
                // Try UI root (e.g. battle_city_disk_bg.jpg)
                path = Path.Combine(UiRoot, relativePath);
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[WRLDZ] Missing kit texture: " + relativePath);
                    return null;
                }
            }

            var bytes = File.ReadAllBytes(path);
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = relativePath,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            if (!t.LoadImage(bytes, markNonReadable: true))
                return null;

            Textures[relativePath] = t;
            return t;
        }

        static void LoadExternalSprite(string fileName)
        {
            var path = Path.Combine(UiRoot, fileName);
            if (!File.Exists(path)) return;
            var key = fileName;
            if (Textures.ContainsKey(key)) return;
            var bytes = File.ReadAllBytes(path);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            if (!t.LoadImage(bytes, markNonReadable: true)) return;
            Textures[key] = t;
            Sprites[key] = UnityEngine.Sprite.Create(
                t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        }

        static void Preload(string relative, Vector4? border = null)
        {
            if (Texture(relative) == null) return;
            Sprite(relative, 100f, border);
        }

        // ── Convenience ──
        public static Sprite Field() => Sprite("bg/field.png");
        public static Sprite DiskArm() => Sprite("bg/disk_arm.png");
        public static Sprite DiskHolo() => Sprite("bg/disk_holo.png") ?? DuelHub();
        public static Sprite TabBar() => Sprite("bg/tab_bar_v2.png") ?? Sprite("bg/tab_bar.png");
        public static Sprite GlowSoft() => Sprite("bg/glow_soft.png");
        public static Sprite Vignette() => Sprite("bg/vignette.png");
        public static Sprite BgGradient() => Sprite("bg/bg_gradient.png") ?? Field();
        public static Sprite HexOverlay() => Sprite("bg/hex_overlay.png");
        public static Sprite NeonLine() => Sprite("bg/neon_line.png");
        public static Sprite PanelNeon() => Sprite("bg/panel_neon.png", 100f, new Vector4(32, 32, 32, 32));
        public static Sprite DuelHub() => Sprite("hub/duel_hub.png");
        public static Sprite DuelFace() => Sprite("hub/duel_face.png");
        public static Sprite DuelScrim() => Sprite("hub/duel_text_scrim.png");
        public static Sprite BattleCityBg() =>
            Sprites.TryGetValue("battle_city_disk_bg.jpg", out var s) ? s : null;
        public static Sprite GlassTile() => Sprite("panels/glass_tile.png", 100f, new Vector4(40, 40, 40, 40));
        public static Sprite GlassWide() => Sprite("panels/glass_wide.png", 100f, new Vector4(40, 40, 40, 40));
        public static Sprite GlassSquare() => Sprite("panels/glass_square.png", 100f, new Vector4(36, 36, 36, 36));
        public static Sprite BadgePlate() => Sprite("panels/plate_badge.png") ?? Sprite("panels/badge_plate.png");
        public static Sprite PlateDark() => Sprite("panels/plate_dark.png", 100f, new Vector4(36, 36, 36, 36));
        public static Sprite PlateDarkWide() => Sprite("panels/plate_dark_wide.png", 100f, new Vector4(36, 36, 36, 36));
        public static Sprite PlateDarkSquare() => Sprite("panels/plate_dark_square.png", 100f, new Vector4(32, 32, 32, 32));
        public static Sprite PlateTabBar() => Sprite("panels/plate_tabbar.png", 100f, new Vector4(12, 12, 12, 12));
        public static Sprite LabelScrim() => Sprite("panels/label_scrim.png", 100f, new Vector4(20, 20, 20, 20));
        public static Sprite BtnGloss() =>
            Sprite("buttons/button_rectangle_depth_gloss.png", 100f, new Vector4(24, 24, 24, 24));
        public static Sprite BtnBorder() =>
            Sprite("buttons/button_rectangle_depth_border.png", 100f, new Vector4(24, 24, 24, 24));
        public static Sprite BtnGradient() =>
            Sprite("buttons/button_rectangle_gradient.png", 100f, new Vector4(20, 20, 20, 20));
        public static Sprite Star() => Sprite("buttons/star.png");
        public static Sprite TabIcon(string key) => Sprite($"tabs/tab_{key}.png");
        public static Sprite MenuIcon(string key) => Sprite($"icons/ico_{key}.png");

        public static Sprite Panel() => GlassTile() ?? PlateDark();
        public static Sprite PanelFlat() => GlassWide() ?? PlateDarkWide();
        public static Sprite PanelBorder() => BtnBorder() ?? GlassTile();
        public static Sprite RoundButton() => DuelFace() ?? DiskHolo();
        public static Sprite RoundBorder() => DuelHub() ?? DiskHolo();

        public static Sprite Icon(string name)
        {
            return name switch
            {
                "information" or "news" => TabIcon("news"),
                "target" or "duel" => TabIcon("duel"),
                "menuList" or "decks" => TabIcon("decks"),
                "trophy" or "events" => TabIcon("events"),
                "menuGrid" or "data" => TabIcon("data"),
                "gear" or "settings" => MenuIcon("settings"),
                "star" => Star() ?? TabIcon("duel"),
                "phone" => MenuIcon("settings"),
                "arrowDown" => MenuIcon("chevron"),
                _ => Sprite($"icons/{name}.png") ?? MenuIcon(name)
            };
        }

        public static void PlayClick()
        {
            // Prefer soft StreamingAssets pack via WrldzAudio; fall back to Resources clips
            try
            {
                Presentation.WrldzAudio.PlayClick();
                return;
            }
            catch
            {
                // ignore
            }

            Play(_click ?? _select);
        }

        public static void PlaySelect()
        {
            try
            {
                Presentation.WrldzAudio.PlaySelect();
                return;
            }
            catch
            {
                // ignore
            }

            Play(_select ?? _click);
        }

        public static void PlayConfirm()
        {
            try
            {
                Presentation.WrldzAudio.PlayConfirm();
                return;
            }
            catch
            {
                // ignore
            }

            Play(_confirm ?? _click);
        }

        /// <summary>Overworld main menu orb (Millennium Eye) open/close.</summary>
        public static void PlayMillenniumEye()
        {
            try
            {
                Presentation.WrldzAudio.PlayMillenniumEye();
                return;
            }
            catch
            {
                // ignore
            }

            Play(_confirm ?? _select ?? _click);
        }

        static void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (_sfx == null)
            {
                var go = new GameObject("WRLDZ_Sfx");
                Object.DontDestroyOnLoad(go);
                _sfx = go.AddComponent<AudioSource>();
                _sfx.playOnAwake = false;
                _sfx.spatialBlend = 0f;
            }

            _sfx.PlayOneShot(clip, 0.45f);
        }
    }
}
