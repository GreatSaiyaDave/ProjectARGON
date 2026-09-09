using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI
{
    /// <summary>
    /// WRLDZ / Project ARGON visual system — Battle City night + holographic neon.
    /// Dark purple-black base, cyan / magenta / gold accents. Outdoor high-contrast.
    /// Story mode (parchment/Egyptian) is a separate layer and must not bleed here.
    /// </summary>
    public static class WrldzTheme
    {
        // Base — Battle City night void + Solid Vision slate
        public static readonly Color VoidBlack = new(0.02f, 0.025f, 0.07f, 1f);
        public static readonly Color NightPurple = new(0.06f, 0.04f, 0.14f, 1f);
        public static readonly Color PanelDeep = new(0.05f, 0.045f, 0.11f, 1f);
        public static readonly Color PanelHolo = new(0.055f, 0.062f, 0.125f, 0.94f);

        // Accents — KaibaCorp cyan × millennia gold × opponent magenta
        public static readonly Color Cyan = new(0.20f, 0.92f, 1f, 1f);
        public static readonly Color CyanDim = new(0.08f, 0.48f, 0.65f, 1f);
        public static readonly Color Magenta = new(1f, 0.28f, 0.72f, 1f);
        public static readonly Color MagentaDim = new(0.55f, 0.10f, 0.38f, 1f);
        public static readonly Color Gold = new(1f, 0.84f, 0.28f, 1f);
        public static readonly Color GoldHot = new(1f, 0.92f, 0.42f, 1f);
        public static readonly Color Cream = new(0.98f, 0.97f, 0.94f, 1f);
        public static readonly Color Soft = new(0.72f, 0.82f, 0.92f, 1f);
        public static readonly Color Danger = Hex(0xFF4455);
        public static readonly Color Ok = Hex(0x3CFF8C);

        // Map / tears
        public static readonly Color MapNight = Hex(0x0B071C);
        public static readonly Color MapFog = new(0.35f, 0.15f, 0.55f, 0.22f);
        public static readonly Color TearGlow = Hex(0xC44BFF);
        public static readonly Color TearCore = Hex(0xFF66EE);
        public static readonly Color AnchorGlow = Hex(0x4AD4FF);
        public static readonly Color ShopGlow = Hex(0x5CFF9A);

        // Duel — your disk cool, opponent warm (TCG opposite-sides grammar)
        public static readonly Color OppField = Hex(0x3E0C1C);
        public static readonly Color YouField = Hex(0x071C3C);
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

        public static void ApplyPortrait()
        {
            Application.targetFrameRate = 60;
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

        /// <summary>Quest-frame panel from Duelyst kit (coordinated CCG chrome).</summary>
        public static RectTransform HoloPanel(Transform parent, string name, bool goldEdge = false)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = root.GetComponent<RectTransform>();

            var fill = StretchFill(root.transform, "Fill",
                Color.white, WRLDZ.Presentation.DuelystUi.Panel() ?? UiFoundation.WhiteSprite());
            var fillImg = fill.GetComponent<Image>();
            if (fillImg.sprite != null && fillImg.sprite.border.sqrMagnitude > 0)
                fillImg.type = Image.Type.Sliced;
            fillImg.color = fillImg.sprite == UiFoundation.WhiteSprite()
                ? PanelHolo
                : Color.white;
            fillImg.raycastTarget = false;

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

        public static Button BigButton(Transform parent, string name, string label, Color tint, System.Action onClick)
        {
            var kind = tint.r > 0.45f && tint.g < 0.3f
                ? WRLDZ.UI.Shell.MenuCommandButton.Kind.Danger
                : tint.r > 0.4f && tint.g > 0.3f && tint.b < 0.25f
                    ? WRLDZ.UI.Shell.MenuCommandButton.Kind.Gold
                    : WRLDZ.UI.Shell.MenuCommandButton.Kind.Primary;
            var btn = WRLDZ.UI.Shell.MenuCommandButton.Create(parent, label, onClick, kind, centerTitle: true);
            btn.name = name;
            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = new Color(tint.r, tint.g, tint.b, 0.50f);
            return btn;
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

            var daySky = Color.Lerp(new Color(0.35f, 0.55f, 0.85f, 1f), new Color(0.55f, 0.75f, 0.95f, 1f), daylight01);
            var nightSky = VoidBlack;
            var voidC = Color.Lerp(nightSky, daySky, daylight01 * 0.85f);

            var refs = new MapAtmosphereRefs
            {
                Void = StretchFill(parent, "Void", voidC),
                Field = StretchFill(parent, "Field",
                    Color.Lerp(new Color(0.45f, 0.25f, 0.75f, 0.45f), new Color(0.35f, 0.55f, 0.4f, 0.35f), daylight01),
                    FreeUiKit.Field() ?? FreeUiKit.BgGradient()),
            };

            var bc = FreeUiKit.BattleCityBg();
            if (bc != null)
            {
                refs.City = StretchFill(parent, "City",
                    Color.Lerp(new Color(0.7f, 0.55f, 1f, 0.55f), new Color(1f, 0.95f, 0.9f, 0.5f), daylight01), bc);
            }

            refs.Fog = StretchFill(parent, "Fog", FogColor(weather, daylight01));
            refs.Glow = StretchFill(parent, "Glow",
                Color.Lerp(new Color(Cyan.r, Cyan.g, Cyan.b, 0.14f), new Color(1f, 0.9f, 0.5f, 0.1f), daylight01),
                FreeUiKit.GlowSoft());
            Place(refs.Glow, 0.1f, 0.35f, 0.9f, 0.85f);

            refs.Vignette = StretchFill(parent, "Vig", Color.white, FreeUiKit.Vignette());
            if (refs.Vignette.GetComponent<Image>().sprite == null)
                refs.Vignette.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            refs.Wash = StretchFill(parent, "Wash",
                Color.Lerp(new Color(Magenta.r, Magenta.g, Magenta.b, 0.12f), new Color(0.2f, 0.4f, 0.8f, 0.08f), daylight01));
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
            var daySky = new Color(0.4f, 0.6f, 0.9f, 1f);
            SetImg(refs.Void, Color.Lerp(VoidBlack, daySky, daylight01 * 0.8f));
            SetImg(refs.Field,
                Color.Lerp(new Color(0.45f, 0.25f, 0.75f, 0.5f), new Color(0.3f, 0.55f, 0.35f, 0.4f), daylight01));
            if (refs.City != null)
                SetImg(refs.City,
                    Color.Lerp(new Color(0.65f, 0.5f, 1f, 0.55f), new Color(1f, 0.95f, 0.88f, 0.55f), daylight01));
            SetImg(refs.Fog, FogColor(weather, daylight01));
            SetImg(refs.Glow,
                Color.Lerp(new Color(Cyan.r, Cyan.g, Cyan.b, 0.14f), new Color(1f, 0.85f, 0.4f, 0.12f), daylight01));
            if (refs.Wash != null)
                SetImg(refs.Wash,
                    Color.Lerp(new Color(Magenta.r, Magenta.g, Magenta.b, 0.12f), new Color(0.25f, 0.45f, 0.85f, 0.08f), daylight01));
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

        static Color FogColor(WRLDZ.Core.WeatherKind weather, float daylight01)
        {
            return weather switch
            {
                WRLDZ.Core.WeatherKind.Fog => new Color(0.55f, 0.55f, 0.65f, 0.45f),
                WRLDZ.Core.WeatherKind.Rain => new Color(0.15f, 0.2f, 0.35f, 0.28f),
                WRLDZ.Core.WeatherKind.Storm => new Color(0.1f, 0.08f, 0.2f, 0.4f),
                WRLDZ.Core.WeatherKind.Snow => new Color(0.75f, 0.8f, 0.9f, 0.3f),
                WRLDZ.Core.WeatherKind.Cloudy => Color.Lerp(MapFog, new Color(0.4f, 0.45f, 0.55f, 0.28f), daylight01),
                _ => Color.Lerp(MapFog, new Color(0.5f, 0.65f, 0.85f, 0.08f), daylight01)
            };
        }

        static Color WeatherOverlayColor(WRLDZ.Core.WeatherKind weather, float daylight01)
        {
            return weather switch
            {
                WRLDZ.Core.WeatherKind.Rain => new Color(0.2f, 0.35f, 0.55f, 0.12f),
                WRLDZ.Core.WeatherKind.Storm => new Color(0.15f, 0.1f, 0.35f, 0.22f),
                WRLDZ.Core.WeatherKind.Snow => new Color(0.85f, 0.9f, 1f, 0.1f),
                WRLDZ.Core.WeatherKind.Fog => new Color(0.7f, 0.7f, 0.75f, 0.15f),
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
