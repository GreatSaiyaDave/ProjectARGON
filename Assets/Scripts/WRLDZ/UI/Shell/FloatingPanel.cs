using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI.Eldritch;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Shared sheet chrome, Eldritch Solid Vision: one holographic slate
    /// (<see cref="SolidVisionSlate"/>) ringed by a <see cref="HieroglyphBorder"/>.
    /// Button helpers route to the Eldritch tiers and stay high-contrast for outdoor / AR.
    /// Layout maths (<see cref="Place"/> / <see cref="Stretch"/> / <see cref="Grid"/>) is
    /// untouched — 44 screens depend on it.
    /// </summary>
    public static class FloatingPanel
    {
        public const float MinButtonHeight = 56f;
        public const float MinTouch = 48f;

        /// <summary>
        /// Vertex alpha on an Eldritch face. The shaders multiply by vertex color, so this is the
        /// single knob that keeps a chip "floating glass" (MenuSmokeTest rejects a face above 0.72).
        /// </summary>
        const float GlassAlpha = 0.70f;

        /// <summary>Ring thickness on a generic floating sheet — thinner than a HieroglyphPanel
        /// so callers placing content at a 6 % inset still clear the glyph cells.</summary>
        const float RingPx = 26f;

        /// <summary>
        /// Translucent glass tints — cream labels stay readable without a black well.
        /// </summary>
        public static readonly Color FacePrimary = MenuCommandButton.FillPrimary;
        public static readonly Color FaceSecondary = MenuCommandButton.FillSecondary;
        public static readonly Color FaceGold = MenuCommandButton.FillGold;
        public static readonly Color FaceDanger = MenuCommandButton.FillDanger;
        public static readonly Color LabelOnButton = new(1f, 1f, 1f, 1f);
        public static readonly Color BlurbOnButton = new(0.90f, 0.94f, 1f, 1f);

        /// <summary>
        /// Tear down UI children this frame. Deferred Destroy leaves the old sheet
        /// visible under the new one for a frame (tab flicker / stacked menus).
        /// </summary>
        public static void DestroyChildrenNow(Transform t)
        {
            if (t == null) return;
            for (var i = t.childCount - 1; i >= 0; i--)
            {
                var ch = t.GetChild(i);
                if (ch != null)
                    Object.DestroyImmediate(ch.gameObject);
            }
        }

        public static void DestroyNow(GameObject go)
        {
            if (go == null) return;
            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Hide now, destroy after the current UI callback. DestroyImmediate from a
        /// Button onClick (the close chip sits on the panel) fatals the Editor / player.
        /// </summary>
        public static void DestroyDeferred(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            if (Application.isPlaying)
            {
                Object.Destroy(go);
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (go != null) Object.DestroyImmediate(go);
            };
#else
            Object.Destroy(go);
#endif
        }

        /// <summary>
        /// Eldritch tier for the legacy (goldEdge / danger / primary) flags every caller still
        /// speaks. Gold → Primary (gold on obsidian), plain → Secondary (lapis + sandstone).
        /// </summary>
        public static EldritchTier TierFor(bool gold = false, bool danger = false,
            bool primary = true)
        {
            if (danger) return EldritchTier.Danger;
            if (gold) return EldritchTier.Primary;
            return primary ? EldritchTier.Secondary : EldritchTier.Tertiary;
        }

        /// <summary>
        /// Floating sheet: a Solid Vision slate with a hieroglyph ring. Signature is frozen —
        /// <paramref name="goldEdge"/> now selects the gold (Primary) tier instead of a gold rim.
        /// </summary>
        public static RectTransform Create(Transform parent, string name, bool goldEdge = false)
        {
            FreeUiKit.EnsureLoaded();
            var tier = goldEdge ? EldritchTier.Primary : EldritchTier.Secondary;

            var slate = SolidVisionSlate.Create(parent,
                string.IsNullOrEmpty(name) ? "FloatingPanel" : name, tier,
                hoverGlow: false, raycast: true);
            if (slate == null) return null;

            var shell = slate.gameObject;
            var ol = shell.GetComponent<Outline>();
            if (ol != null) ol.enabled = false;

            // Shader missing (asset import / headless): keep it a readable dark plate, not white.
            if (slate.Mat == null && slate.Face != null)
                slate.Face.color = EldritchPalette.FillFor(tier);

            // Ring first so caller content (added afterwards) draws on top of the glyph cells.
            var ring = HieroglyphBorder.Create(shell.transform, HieroglyphBorder.Edge.Ring, RingPx,
                goldEdge ? EldritchPalette.MillenniumGold : EldritchPalette.SolidVisionCyan, 3f);
            if (ring != null) ring.SetIntensity(0.80f);

            return slate.Rect;
        }

        public static Button PrimaryButton(Transform parent, string label, System.Action onClick,
            bool danger = false, bool gold = false, bool centerLabel = false)
        {
            var ab = ArcaneButton.Create(parent, label, onClick, TierFor(gold, danger),
                titleSize: 20, centerTitle: true);
            if (ab == null)
            {
                var kind = danger ? MenuCommandButton.Kind.Danger
                    : gold ? MenuCommandButton.Kind.Gold
                    : MenuCommandButton.Kind.Primary;
                return MenuCommandButton.Create(parent, label, onClick, kind, centerTitle: true);
            }

            // Floating glass, not a solid chip: the flow shader multiplies by vertex color.
            if (ab.Face != null) ab.Face.color = new Color(1f, 1f, 1f, GlassAlpha);
            if (ab.Title != null)
            {
                ab.Title.font = EldritchPalette.Ui() ?? ab.Title.font;
                WrldzType.ApplyOutline(ab.Title, heavy: true, buttonContrast: true);
            }

            return ab.Button;
        }

        /// <summary>
        /// Tint a floating chip onto the Eldritch tiers. Always Simple + WhiteSprite — never a
        /// 9-slice plate. The slate component owns (and releases) the per-instance material.
        /// </summary>
        public static void ApplyButtonFace(Image img, bool primary = true, bool gold = false,
            bool danger = false)
        {
            if (img == null) return;
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;

            var slate = SolidVisionSlate.Attach(img, TierFor(gold, danger, primary));
            if (slate != null && slate.Mat != null)
            {
                slate.HoverGlow = false;
                slate.OpenEmbers = false;
                img.color = new Color(1f, 1f, 1f, GlassAlpha);
                return;
            }

            // Shader missing (asset import / headless): flat tier tints, still translucent.
            if (danger) img.color = FaceDanger;
            else if (gold) img.color = FaceGold;
            else if (primary) img.color = FacePrimary;
            else img.color = FaceSecondary;
        }

        /// <summary>
        /// Retired. Black label wells were covering the floating chips.
        /// Kept as a no-op so leftover callers cannot reintroduce the overlay.
        /// </summary>
        public static Image AddLabelScrim(Transform button, float x0 = 0.04f, float y0 = 0.08f,
            float x1 = 0.96f, float y1 = 0.92f)
        {
            return null;
        }

        /// <summary>
        /// Sheet title: Eldritch display face, millennium gold on obsidian, heavy black outline.
        /// Size is still the caller's design size (WrldzType.Readable does the DPI scaling).
        /// </summary>
        public static Text Title(Transform parent, string text, int size = 28)
        {
            var go = new GameObject("Title", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.StyleGoldTitle(t, size);
            t.font = EldritchPalette.Display() ?? t.font;
            t.color = EldritchPalette.MillenniumGold;
            WrldzType.ApplyOutline(t, heavy: true, buttonContrast: true);
            t.text = text;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// Sheet body copy: Exo 2 Bold, cream, heavy outline. Never shrunk — this carries the
        /// small print on 40-odd screens, so <paramref name="size"/> passes straight through.
        /// </summary>
        public static Text Body(Transform parent, string text, int size = 18)
        {
            var go = new GameObject("Body", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: false, heavyOutline: true);
            t.font = EldritchPalette.Body() ?? t.font;
            t.color = EldritchPalette.TextCream;
            WrldzType.ApplyOutline(t, heavy: true, buttonContrast: true);
            t.text = text;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Muted variant of <see cref="Body"/> for subtitles / captions under a title.</summary>
        public static Text Caption(Transform parent, string text, int size = 15)
        {
            var t = Body(parent, text, size);
            t.color = EldritchPalette.TextMuted;
            return t;
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        /// <summary>
        /// Shared action-sheet geometry: equal columns, equal gutters.
        /// Coordinates are normalized inside the DualMenuPresenter body well.
        /// </summary>
        public static class Grid
        {
            public const float X0 = 0.02f;
            public const float X1 = 0.98f;
            public const float Gap = 0.04f;
            public const float ColL1 = 0.48f;
            public const float ColR0 = 0.52f;
            public const float Row = 0.12f;

            public static void Full(RectTransform rt, float y0, float y1) =>
                Place(rt, X0, y0, X1, y1);

            public static void Pair(RectTransform left, RectTransform right, float y0, float y1)
            {
                Place(left, X0, y0, ColL1, y1);
                Place(right, ColR0, y0, X1, y1);
            }

            public static void Triple(RectTransform a, RectTransform b, RectTransform c,
                float y0, float y1)
            {
                Place(a, X0, y0, 0.32f, y1);
                Place(b, 0.34f, y0, 0.66f, y1);
                Place(c, 0.68f, y0, X1, y1);
            }

            public static void Quad(RectTransform a, RectTransform b, RectTransform c, RectTransform d,
                float y0, float y1)
            {
                Place(a, X0, y0, 0.24f, y1);
                Place(b, 0.26f, y0, 0.49f, y1);
                Place(c, 0.51f, y0, 0.74f, y1);
                Place(d, 0.76f, y0, X1, y1);
            }
        }
    }
}
