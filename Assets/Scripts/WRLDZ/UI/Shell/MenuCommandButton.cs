using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI.Eldritch;

namespace WRLDZ.UI.Shell
{
        /// <summary>
        /// Floating command chip, Eldritch Solid Vision tier: the face is an
        /// <c>ArcaneEnergyFlow</c> material (carved sandstone over lapis / obsidian with a gold
        /// energy line racing the perimeter) instead of a flat fill or a 9-slice Imagine plate.
        /// Signature, <see cref="Kind"/> and the "Title" / "Blurb" child names + anchors are frozen:
        /// ~35 screens call this and many re-place those children by name.
        /// </summary>
        public static class MenuCommandButton
        {
            public enum Kind
            {
                Primary = 0,
                Secondary = 1,
                Gold = 2,
                Danger = 3
            }

        /// <summary>Command chips are floating glass; the flow shader scales fill AND energy by vertex alpha.</summary>
        const float GlassAlpha = 0.70f;

        /// <summary>Hero / dest plates sit closer to stone than glass.</summary>
        const float PlateAlpha = 0.92f;

        // Kept because other files read them (FloatingPanel.FacePrimary aliases FillPrimary, etc.).
        // Values now resolve to EldritchPalette tokens; alphas stay under the 0.72 smoke-test cap.
        public static readonly Color FillPrimary = EldritchPalette.WithAlpha(EldritchPalette.Lapis, GlassAlpha);
        public static readonly Color FillSecondary = EldritchPalette.WithAlpha(EldritchPalette.Obsidian, 0.58f);
        public static readonly Color FillGold = EldritchPalette.WithAlpha(EldritchPalette.Obsidian, GlassAlpha);
        public static readonly Color FillDanger = EldritchPalette.WithAlpha(
            Color.Lerp(EldritchPalette.Obsidian, EldritchPalette.BloodDanger, 0.18f), GlassAlpha);
        public static readonly Color TitleInk = EldritchPalette.TextCream;
        public static readonly Color BlurbInk = EldritchPalette.TextMuted;
        public static readonly Color EdgePrimary = EldritchPalette.Sandstone;
        public static readonly Color EdgeSecondary = EldritchPalette.WithAlpha(EldritchPalette.SolidVisionCyan, 0.55f);
        public static readonly Color EdgeGold = EldritchPalette.MillenniumGold;
        public static readonly Color EdgeDanger = EldritchPalette.BloodDanger;

        /// <summary>
        /// Kind → tier. Secondary (cyan-on-lapis) is the workhorse for sheets, so the default
        /// Primary kind lands there; Gold is the gold-on-obsidian hero tier.
        /// </summary>
        public static EldritchTier TierFor(Kind kind) => kind switch
        {
            Kind.Gold => EldritchTier.Primary,
            Kind.Danger => EldritchTier.Danger,
            Kind.Secondary => EldritchTier.Tertiary,
            _ => EldritchTier.Secondary
        };

        public static Button Create(Transform parent, string title, Action onClick,
            Kind kind = Kind.Primary, string blurb = null, bool centerTitle = false,
            bool plated = false)
        {
            FreeUiKit.EnsureLoaded();
            var hasBlurb = !string.IsNullOrEmpty(blurb);

            var ab = ArcaneButton.Create(parent, title, onClick, TierFor(kind), blurb, null,
                hasBlurb ? 18 : 20, centerTitle, hieroglyphBorder: false);
            if (ab == null) return null;
            var go = ab.gameObject;
            go.name = string.IsNullOrEmpty(title) ? "Cmd" : title;

            CarveStone(ab.Face);
            if (EldritchMaterials.IsEldritch(ab.Mat))
                SetFaceOpacity(ab, plated ? PlateAlpha : GlassAlpha);
            else if (ab.Face != null)
                ab.Face.color = FillFor(kind);   // shader stripped / not imported: flat tier fill

            // Left accent rail survives as a gold inlay — the energy line owns the perimeter,
            // but list rows still need a hard vertical to anchor left-aligned type.
            if (!centerTitle && !plated)
            {
                var rail = Solid(go.transform, "Rail",
                    EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.70f));
                rail.transform.SetAsFirstSibling();
                var rrt = rail.rectTransform;
                rrt.anchorMin = new Vector2(0f, 0.18f);
                rrt.anchorMax = new Vector2(0f, 0.82f);
                rrt.pivot = new Vector2(0f, 0.5f);
                rrt.anchoredPosition = Vector2.zero;
                rrt.sizeDelta = new Vector2(3f, 0f);
                rail.raycastTarget = false;
            }

            // Frozen anchors: screens call transform.Find("Title") and re-place from these boxes.
            if (ab.Title != null)
            {
                ab.Title.color = TitleInk;
                ab.Title.alignment = centerTitle && !hasBlurb
                    ? TextAnchor.MiddleCenter
                    : TextAnchor.MiddleLeft;
                if (hasBlurb) Place(ab.Title.rectTransform, 0.06f, 0.48f, 0.96f, 0.92f);
                else if (centerTitle) Place(ab.Title.rectTransform, 0.04f, 0.10f, 0.96f, 0.90f);
                else Place(ab.Title.rectTransform, 0.06f, 0.14f, 0.96f, 0.86f);
            }

            if (ab.Blurb != null)
            {
                ab.Blurb.color = BlurbInk;
                ab.Blurb.fontStyle = FontStyle.Normal;
                Place(ab.Blurb.rectTransform, 0.06f, 0.08f, 0.96f, 0.46f);
            }

            return ab.Button;
        }

        public static Color EdgeFor(Kind kind) => kind switch
        {
            Kind.Gold => EdgeGold,
            Kind.Danger => EdgeDanger,
            Kind.Secondary => EdgeSecondary,
            _ => EdgePrimary
        };

        public static Color FillFor(Kind kind) => kind switch
        {
            Kind.Gold => FillGold,
            Kind.Danger => FillDanger,
            Kind.Secondary => FillSecondary,
            _ => FillPrimary
        };

        /// <summary>
        /// Opacity rides the Image vertex color, not the material: the flow shader multiplies both
        /// fill and emissive by it, and the menu smoke test reads <c>Image.color.a</c> on chips.
        /// </summary>
        public static void SetFaceOpacity(ArcaneButton ab, float alpha)
        {
            if (ab == null) return;
            var m = ab.Mat;
            if (m != null)
            {
                // Tier fills carry MenuChromePrefs alpha; fold it into the vertex color instead so
                // the two do not multiply into a ghost.
                Opaque(m, EldritchMaterials.FillColorAId);
                Opaque(m, EldritchMaterials.FillColorBId);
            }

            if (ab.Face != null) ab.Face.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        static void Opaque(Material m, int id)
        {
            if (m == null || !m.HasProperty(id)) return;
            var c = m.GetColor(id);
            c.a = 1f;
            m.SetColor(id, c);
        }

        /// <summary>
        /// Carved read: sandstone grain as the shader's _MainTex (it multiplies the tier fill, so
        /// lapis becomes carved lapis). Same idiom as <c>DuelDiskMenuUI.CarveFace</c>. No-op when
        /// the StreamingAssets tile is missing.
        /// </summary>
        public static void CarveStone(Image face)
        {
            if (face == null) return;
            var grain = StoneSprite();
            if (grain != null) face.sprite = grain;
            else if (face.sprite == null) face.sprite = UiFoundation.WhiteSprite();
            face.type = Image.Type.Simple;   // rect-space shaders assume a full-rect quad
        }

        static Sprite _stone;

        /// <summary>Sandstone grain wrapped once as a full-rect sprite (the loader returns a texture).</summary>
        public static Sprite StoneSprite()
        {
            if (_stone != null && _stone.texture != null) return _stone;
            var tex = EldritchAssets.SandstoneTile();
            if (tex == null) return null;
            _stone = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _stone.name = "sandstone_face";
            return _stone;
        }

        /// <summary>One gold hieroglyph struck into a tile's margin. Null-safe, decorative only.</summary>
        public static Image CarveGlyph(Transform host, float alpha,
            float x0 = 0.05f, float y0 = 0.52f, float x1 = 0.20f, float y1 = 0.93f)
        {
            if (host == null) return null;
            var pool = EldritchGlyphs.Atlas();
            if (pool == null || pool.Length == 0) return null;
            if (host.Find("Glyph") != null) return null;

            var go = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            go.transform.SetAsFirstSibling();   // under the label, over the face
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.sprite = pool[Mathf.Abs((host.name ?? "g").GetHashCode()) % pool.Length];
            img.preserveAspect = true;
            img.raycastTarget = false;
            var gold = EldritchPalette.MillenniumGold;
            img.color = new Color(gold.r, gold.g, gold.b, Mathf.Clamp01(alpha));
            return img;
        }

        static Image Solid(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Hub tiles: lock type so best-fit cannot shrink labels into the plate.
        /// Compact toolbar chips keep the builder's own best-fit.
        /// </summary>
        public static void ApplyHubType(Button btn, int titleSize, Color titleColor, bool displayTitle,
            int blurbSize = 16, Color? blurbColor = null)
        {
            if (btn == null) return;
            var title = btn.transform.Find("Title")?.GetComponent<Text>();
            if (title != null)
            {
                title.resizeTextForBestFit = false;
                WrldzType.Style(title, titleSize, display: displayTitle, heavyOutline: true);
                title.color = titleColor;
                WrldzType.ApplyOutline(title, heavy: true, buttonContrast: true);
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                title.verticalOverflow = VerticalWrapMode.Overflow;
            }

            var blurb = btn.transform.Find("Blurb")?.GetComponent<Text>();
            if (blurb != null)
            {
                blurb.resizeTextForBestFit = false;
                WrldzType.Style(blurb, blurbSize, display: false, heavyOutline: true);
                blurb.color = blurbColor ?? BlurbInk;
                WrldzType.ApplyOutline(blurb, heavy: true, buttonContrast: true);
                blurb.fontStyle = FontStyle.Bold;
            }
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
