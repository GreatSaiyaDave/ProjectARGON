using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI.Eldritch;

namespace WRLDZ.UI
{
    /// <summary>
    /// WRLDZ / Project ARGON visual system — Eldritch Solid Vision.
    /// Obsidian / lapis base, millennium gold + Solid Vision cyan accents, abyssal violet
    /// for the Tear layer. Outdoor high-contrast (gold or cream on obsidian, never grey on grey).
    /// <para>
    /// Every token below is now an alias onto <see cref="EldritchPalette"/>. The FIELD NAMES are
    /// load-bearing — 18 files read <c>WrldzTheme.Cyan</c> and friends — so they never change;
    /// only the values they resolve to moved onto the Eldritch ramp.
    /// </para>
    /// </summary>
    public static class WrldzTheme
    {
        // Base — abyssal void + Solid Vision slate
        public static readonly Color VoidBlack = EldritchPalette.Void;
        public static readonly Color NightPurple = EldritchPalette.Abyss;
        public static readonly Color PanelDeep = EldritchPalette.Obsidian;
        public static readonly Color PanelHolo =
            EldritchPalette.WithAlpha(EldritchPalette.Obsidian, 0.94f);

        // Accents — millennium gold × Solid Vision cyan × eldritch violet
        public static readonly Color Cyan = EldritchPalette.SolidVisionCyan;
        public static readonly Color CyanDim = EldritchPalette.CyanDeep;
        public static readonly Color Magenta = EldritchPalette.AbyssalMagenta;
        public static readonly Color MagentaDim =
            Color.Lerp(EldritchPalette.EldritchViolet, EldritchPalette.Abyss, 0.45f);
        public static readonly Color Gold = EldritchPalette.MillenniumGold;
        public static readonly Color GoldHot = EldritchPalette.GoldHot;
        public static readonly Color Cream = EldritchPalette.TextCream;
        public static readonly Color Soft = EldritchPalette.TextMuted;
        public static readonly Color Danger = EldritchPalette.BloodDanger;
        public static readonly Color Ok = EldritchPalette.IchorGreen;

        // Map / tears — lapis night, violet rifts
        public static readonly Color MapNight = EldritchPalette.Abyss;
        public static readonly Color MapFog =
            EldritchPalette.WithAlpha(Color.Lerp(EldritchPalette.LapisDeep,
                EldritchPalette.EldritchViolet, 0.35f), 0.22f);
        public static readonly Color TearGlow = EldritchPalette.AbyssalMagenta;
        public static readonly Color TearCore =
            Color.Lerp(EldritchPalette.AbyssalMagenta, EldritchPalette.TextCream, 0.34f);
        public static readonly Color AnchorGlow = EldritchPalette.SolidVisionCyan;
        public static readonly Color ShopGlow = EldritchPalette.IchorGreen;

        // Duel — your disk lapis, opponent blood (TCG opposite-sides grammar)
        public static readonly Color OppField =
            Color.Lerp(EldritchPalette.BloodDanger, EldritchPalette.Void, 0.80f);
        public static readonly Color YouField =
            Color.Lerp(EldritchPalette.Lapis, EldritchPalette.Void, 0.70f);
        public static readonly Color LpGold = GoldHot;

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        public static Font DisplayFont()
        {
            FreeUiKit.EnsureLoaded();
            return FreeUiKit.DisplayFont() ?? UiFoundation.BuiltinFont();
        }

        public static Font BodyFont()
        {
            FreeUiKit.EnsureLoaded();
            return FreeUiKit.BodyFont() ?? DisplayFont();
        }

        const string BatterySaverPref = "wrldz.batterySaver";
        static int _batterySaver = -1; // -1 = not read from PlayerPrefs yet

        /// <summary>
        /// Settings → Battery Saver, persisted. 30 fps + dimmed neon while on. Every screen that sets
        /// a frame rate goes through <see cref="ApplyFrameRate"/> so scene builds no longer reset it.
        /// </summary>
        public static bool BatterySaver
        {
            get
            {
                if (_batterySaver < 0)
                {
                    try { _batterySaver = PlayerPrefs.GetInt(BatterySaverPref, 0) != 0 ? 1 : 0; }
                    catch { _batterySaver = 0; }
                }

                return _batterySaver == 1;
            }
            set
            {
                _batterySaver = value ? 1 : 0;
                PlayerPrefs.SetInt(BatterySaverPref, _batterySaver);
                PlayerPrefs.Save();
                ApplyFrameRate();
            }
        }

        /// <summary>UI frame-rate target: 30 with Battery Saver, else 60.</summary>
        public static int TargetFrameRate => BatterySaver ? 30 : 60;

        public static void ApplyFrameRate() => Application.targetFrameRate = TargetFrameRate;

        public static void ApplyPortrait()
        {
            ApplyFrameRate();
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
        }

        public static RectTransform Canvas(string name, int sort = 30)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sort;
            UiTheme.ApplyPortraitPhone(go.GetComponent<CanvasScaler>());
            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// BUILD_PLAN D1: the same canvas, opting in to the aspect plan when <paramref name="aspect"/> is true
        /// (landscape surface → the 1920×1080 reference, else today's portrait phone scaler). Every other caller keeps
        /// the portrait default, so the Overworld and the duel are untouched.
        /// </summary>
        public static RectTransform Canvas(string name, int sort, bool aspect)
        {
            if (!aspect) return Canvas(name, sort);
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sort;
            UiTheme.ApplyForAspect(go.GetComponent<CanvasScaler>(), WRLDZ.UI.MenuV6.MenuAspect.Surface);
            return go.GetComponent<RectTransform>();
        }

        public static RectTransform StretchFill(Transform parent, string name, Color color, Sprite sp = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp ?? UiFoundation.WhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt);
            return rt;
        }

        /// <summary>
        /// Solid Vision holo panel: a translucent <see cref="SolidVisionSlate"/> (obsidian fill,
        /// travelling edge light, organic veins) with gold corner ticks. <paramref name="goldEdge"/>
        /// picks the Primary (gold-on-obsidian) tier over Secondary (cyan-on-lapis).
        /// The slate owns its per-instance material and releases it in OnDestroy.
        /// </summary>
        public static RectTransform HoloPanel(Transform parent, string name, bool goldEdge = false)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = root.GetComponent<RectTransform>();

            var tier = goldEdge ? EldritchTier.Primary : EldritchTier.Secondary;
            var slate = SolidVisionSlate.Create(root.transform, "Fill", tier,
                hoverGlow: false, raycast: false);
            if (slate != null)
            {
                Stretch(slate.Rect);
            }
            else
            {
                // Shader missing (asset import in flight) — flat obsidian plate, still readable.
                var fill = StretchFill(root.transform, "Fill", PanelHolo);
                fill.GetComponent<Image>().raycastTarget = false;
            }

            // Thin hieroglyph cornice along the top edge — the panel's "cartouche" cue.
            var band = HieroglyphBorder.Create(root.transform, HieroglyphBorder.Edge.Top, 22f,
                goldEdge ? Gold : Cyan, 2.2f);
            if (band != null) band.SetIntensity(0.7f);

            WRLDZ.UI.Shell.HubChrome.CornerTicks(root.transform, goldEdge ? Gold : Cyan);

            return rt;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, bool display = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display, heavyOutline: display);
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// Arcane energy-flow button. The legacy <paramref name="tint"/> argument is kept for the
        /// callers that pass a mood color; it now only picks the Eldritch tier (the face itself is
        /// painted by the flow shader, so tinting the Image would mud the travelling line).
        /// </summary>
        public static Button BigButton(Transform parent, string name, string label, Color tint, System.Action onClick)
        {
            var ab = ArcaneButton.Create(parent, label, onClick, TierForTint(tint),
                blurb: null, icon: null, titleSize: 22, centerTitle: true,
                hieroglyphBorder: true);
            if (ab == null) return null;
            ab.gameObject.name = name;
            return ab.Button;
        }

        /// <summary>Legacy mood-color → Eldritch tier. Red=Danger · warm=Primary(gold) · violet=Eldritch.</summary>
        public static EldritchTier TierForTint(Color tint)
        {
            if (tint.r > 0.45f && tint.g < 0.30f && tint.b < 0.45f) return EldritchTier.Danger;
            if (tint.r > 0.40f && tint.g > 0.30f && tint.b < 0.25f) return EldritchTier.Primary;
            if (tint.b > 0.45f && tint.r > 0.35f && tint.g < 0.40f) return EldritchTier.Eldritch;
            return EldritchTier.Secondary;
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        /// <summary>Battle City atmosphere layers under a parent rect (default night look).</summary>
        public static void BuildNightAtmosphere(Transform parent) =>
            BuildMapAtmosphere(parent, daylight01: 0f, weather: WRLDZ.Core.WeatherKind.Clear);

        /// <summary>
        /// Map atmosphere driven by day/night + weather.
        /// Named children (Void/Field/City/Fog/Glow/Wash/WeatherOverlay) can be retinted live.
        /// </summary>
        public static MapAtmosphereRefs BuildMapAtmosphere(
            Transform parent,
            float daylight01,
            WRLDZ.Core.WeatherKind weather)
        {
            FreeUiKit.EnsureLoaded();
            daylight01 = Mathf.Clamp01(daylight01);

            var daySky = Color.Lerp(DuskSky, NoonSky, daylight01);
            var nightSky = VoidBlack;
            var voidC = Color.Lerp(nightSky, daySky, daylight01 * 0.85f);

            var refs = new MapAtmosphereRefs
            {
                Void = StretchFill(parent, "Void", voidC),
                Field = StretchFill(parent, "Field", FieldColor(daylight01),
                    FreeUiKit.Field() ?? FreeUiKit.BgGradient()),
            };

            var bc = FreeUiKit.BattleCityBg();
            if (bc != null)
                refs.City = StretchFill(parent, "City", CityColor(daylight01), bc);

            refs.Fog = StretchFill(parent, "Fog", FogColor(weather, daylight01));
            refs.Glow = StretchFill(parent, "Glow", GlowColor(daylight01), FreeUiKit.GlowSoft());
            Place(refs.Glow, 0.1f, 0.35f, 0.9f, 0.85f);

            refs.Vignette = StretchFill(parent, "Vig", Color.white, FreeUiKit.Vignette());
            if (refs.Vignette.GetComponent<Image>().sprite == null)
                refs.Vignette.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            refs.Wash = StretchFill(parent, "Wash", WashColor(daylight01));
            Place(refs.Wash, 0f, 0.2f, 0.2f, 1f);

            // Weather overlay (rain sheet / storm flash base)
            refs.WeatherOverlay = StretchFill(parent, "WeatherOverlay", WeatherOverlayColor(weather, daylight01));
            refs.WeatherOverlay.GetComponent<Image>().raycastTarget = false;

            ApplyAtmosphere(refs, daylight01, weather);
            return refs;
        }

        public static void ApplyAtmosphere(MapAtmosphereRefs refs, float daylight01, WRLDZ.Core.WeatherKind weather)
        {
            if (refs == null) return;
            daylight01 = Mathf.Clamp01(daylight01);
            var daySky = Color.Lerp(DuskSky, NoonSky, daylight01);
            SetImg(refs.Void, Color.Lerp(VoidBlack, daySky, daylight01 * 0.8f));
            SetImg(refs.Field, FieldColor(daylight01));
            if (refs.City != null)
                SetImg(refs.City, CityColor(daylight01));
            SetImg(refs.Fog, FogColor(weather, daylight01));
            SetImg(refs.Glow, GlowColor(daylight01));
            if (refs.Wash != null)
                SetImg(refs.Wash, WashColor(daylight01));
            if (refs.Vignette != null)
            {
                var a = Mathf.Lerp(0.55f, 0.25f, daylight01);
                if (weather is WRLDZ.Core.WeatherKind.Storm or WRLDZ.Core.WeatherKind.Fog) a += 0.1f;
                SetImg(refs.Vignette,
                    refs.Vignette.GetComponent<Image>().sprite != null
                        ? new Color(1f, 1f, 1f, Mathf.Clamp01(a + 0.3f))
                        : new Color(0f, 0f, 0f, a));
            }

            if (refs.WeatherOverlay != null)
                SetImg(refs.WeatherOverlay, WeatherOverlayColor(weather, daylight01));
        }

        // ── Eldritch atmosphere ramps ────────────────────────────────
        // Night = abyssal lapis + violet rift light. Day = sandstone desert glare.
        // The day/night + weather LOGIC is untouched; only the endpoints moved onto the palette.

        static readonly Color DuskSky =
            Color.Lerp(EldritchPalette.Lapis, EldritchPalette.LapisDeep, 0.35f);

        static readonly Color NoonSky =
            Color.Lerp(EldritchPalette.Sandstone, EldritchPalette.SolidVisionCyan, 0.30f);

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Ground plate: violet rift haze at night → warm sandstone at noon.</summary>
        static Color FieldColor(float daylight01) =>
            Color.Lerp(A(EldritchPalette.EldritchViolet, 0.45f),
                A(EldritchPalette.SandstoneDark, 0.38f), daylight01);

        /// <summary>Battle City skyline wash: lapis/violet neon at night → bleached stone by day.</summary>
        static Color CityColor(float daylight01) =>
            Color.Lerp(
                A(Color.Lerp(EldritchPalette.EldritchViolet, EldritchPalette.SolidVisionCyan, 0.25f), 0.55f),
                A(Color.Lerp(EldritchPalette.Sandstone, EldritchPalette.TextCream, 0.45f), 0.52f),
                daylight01);

        /// <summary>Horizon bloom: Solid Vision cyan at night → millennium gold at noon.</summary>
        static Color GlowColor(float daylight01) =>
            Color.Lerp(A(EldritchPalette.SolidVisionCyan, 0.14f),
                A(EldritchPalette.MillenniumGold, 0.11f), daylight01);

        /// <summary>Left-edge wash: abyssal magenta at night → cool lapis by day.</summary>
        static Color WashColor(float daylight01) =>
            Color.Lerp(A(EldritchPalette.AbyssalMagenta, 0.12f),
                A(EldritchPalette.Lapis, 0.08f), daylight01);

        static Color FogColor(WRLDZ.Core.WeatherKind weather, float daylight01)
        {
            return weather switch
            {
                WRLDZ.Core.WeatherKind.Fog => A(Color.Lerp(EldritchPalette.Sandstone,
                    EldritchPalette.LapisDeep, 0.55f), 0.45f),
                WRLDZ.Core.WeatherKind.Rain => A(EldritchPalette.LapisDeep, 0.30f),
                WRLDZ.Core.WeatherKind.Storm => A(Color.Lerp(EldritchPalette.EldritchViolet,
                    EldritchPalette.Void, 0.55f), 0.42f),
                WRLDZ.Core.WeatherKind.Snow => A(Color.Lerp(EldritchPalette.TextMuted,
                    EldritchPalette.TextCream, 0.4f), 0.30f),
                WRLDZ.Core.WeatherKind.Cloudy => Color.Lerp(MapFog,
                    A(Color.Lerp(EldritchPalette.LapisDeep, EldritchPalette.Sandstone, 0.35f), 0.28f),
                    daylight01),
                _ => Color.Lerp(MapFog, A(EldritchPalette.Sandstone, 0.09f), daylight01)
            };
        }

        static Color WeatherOverlayColor(WRLDZ.Core.WeatherKind weather, float daylight01)
        {
            return weather switch
            {
                WRLDZ.Core.WeatherKind.Rain => A(EldritchPalette.CyanDeep, 0.13f),
                WRLDZ.Core.WeatherKind.Storm => A(EldritchPalette.EldritchViolet, 0.22f),
                WRLDZ.Core.WeatherKind.Snow => A(EldritchPalette.TextCream, 0.10f),
                WRLDZ.Core.WeatherKind.Fog => A(EldritchPalette.Sandstone, 0.14f),
                _ => new Color(0f, 0f, 0f, 0f)
            };
        }

        static void SetImg(RectTransform rt, Color c)
        {
            if (rt == null) return;
            var img = rt.GetComponent<Image>();
            if (img != null) img.color = c;
        }

        public class MapAtmosphereRefs
        {
            public RectTransform Void, Field, City, Fog, Glow, Wash, Vignette, WeatherOverlay;
        }
    }
}
