using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Popover / sheet frame: SolidVisionSlate root, hieroglyph ring, four tentacle corner
    /// decals, optional cartouche title. <see cref="Body"/> is the safe inset content rect.
    /// </summary>
    public sealed class HieroglyphPanel : MonoBehaviour
    {
        const float CartoucheH = 64f;
        const float CartoucheTop = 14f;
        const float CornerPx = 168f;

        public RectTransform Root => transform as RectTransform;
        public RectTransform Body { get; private set; }
        public SolidVisionSlate Slate { get; private set; }
        public HieroglyphBorder Border { get; private set; }
        public Text TitleText { get; private set; }
        public EldritchTier Tier { get; private set; }
        public bool EmbersOnOpen = true;

        bool _closing;

        public static HieroglyphPanel Create(Transform parent, string name, EldritchTier tier,
            string title = null, bool ring = true, bool embersOnOpen = true)
        {
            var slate = SolidVisionSlate.Create(parent, string.IsNullOrEmpty(name) ? "HieroglyphPanel" : name,
                tier, hoverGlow: false, raycast: true);
            var go = slate.gameObject;
            var panel = go.AddComponent<HieroglyphPanel>();
            panel.Slate = slate;
            panel.Tier = tier;
            panel.EmbersOnOpen = embersOnOpen;
            slate.OpenEmbers = false; // panel fires its own dust + ember mix
            MenuMotion.EnsureGroup(go);

            var hasTitle = !string.IsNullOrEmpty(title);
            panel.BuildCorners();
            if (ring)
            {
                panel.Border = HieroglyphBorder.Create(go.transform, HieroglyphBorder.Edge.Ring, 34f,
                    GlyphGlowFor(tier), 3f);
                panel.Border.SetIntensity(0.85f);
            }

            if (hasTitle) panel.BuildCartouche(title, tier);

            // Body: 4% x / 6% y safe margins plus the cartouche band when titled.
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(go.transform, false);
            var body = bodyGo.GetComponent<RectTransform>();
            body.anchorMin = new Vector2(0.04f, 0.06f);
            body.anchorMax = new Vector2(0.96f, 0.94f);
            body.offsetMin = Vector2.zero;
            body.offsetMax = hasTitle ? new Vector2(0f, -(CartoucheH + CartoucheTop + 6f)) : Vector2.zero;
            body.pivot = new Vector2(0.5f, 0.5f);
            panel.Body = body;
            return panel;
        }

        /// <summary>PlayOpen + StaggerChildren(Body) + dust/ember burst from the top edge.</summary>
        public void Open()
        {
            if (Slate != null) Slate.PlayOpen();
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            var g = MenuMotion.EnsureGroup(gameObject);
            g.alpha = 1f;
            g.blocksRaycasts = true;
            g.interactable = true;
            if (Body != null && Body.childCount > 0)
                MenuMotion.PlayOn(gameObject, MenuMotion.StaggerChildren(Body));
            if (EmbersOnOpen)
            {
                var field = UiEmberField.Field(transform);
                if (field != null)
                {
                    field.BurstFromRect(Root, 24, UiEmberField.Kind.Dust, EldritchPalette.Sandstone, 0.86f, 1f);
                    field.BurstFromRect(Root, 10, UiEmberField.Kind.Ember, GlyphGlowFor(Tier), 0.9f, 1f);
                }
            }
        }

        /// <summary>MenuMotion.SheetOut then destroy (immediate in edit mode).</summary>
        public void Close(Action onDone = null)
        {
            if (_closing) return;
            _closing = true;
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                ArObjectUtil.Destroy(gameObject);
                onDone?.Invoke();
                return;
            }

            StartCoroutine(CloseRoutine(onDone));
        }

        // contract note: MenuMotion.SheetOut ends with SetActive(false), which would stop this
        // coroutine before the destroy. Same motion (fade + 28 px drop), then destroy.
        IEnumerator CloseRoutine(Action onDone)
        {
            var rt = Root;
            var g = MenuMotion.EnsureGroup(gameObject);
            g.interactable = false;
            var origin = rt != null ? rt.anchoredPosition : Vector2.zero;
            var fromA = g.alpha;
            var dur = MenuMotion.Snap;
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var k = MenuMotion.OutCubic(t / dur);
                g.alpha = Mathf.Lerp(fromA, 0f, k);
                if (rt != null)
                    rt.anchoredPosition = Vector2.Lerp(origin, origin + new Vector2(0f, -28f), k);
                yield return null;
            }

            g.alpha = 0f;
            g.blocksRaycasts = false;
            ArObjectUtil.Destroy(gameObject);
            onDone?.Invoke();
        }

        // ------------------------------------------------------------------ dressing

        void BuildCorners()
        {
            var spr = EldritchAssets.TentacleCorner();
            if (spr == null) return; // decorative only; nothing to draw without the decal
            var tint = EldritchPalette.EldritchViolet;
            tint.a = 0.35f;
            // Decal is authored in the top-left corner; rotate about the cell center per corner.
            Corner("CornerTL", new Vector2(0f, 1f), new Vector2(1f, -1f), 0f, spr, tint);
            Corner("CornerTR", new Vector2(1f, 1f), new Vector2(-1f, -1f), -90f, spr, tint);
            Corner("CornerBR", new Vector2(1f, 0f), new Vector2(-1f, 1f), 180f, spr, tint);
            Corner("CornerBL", new Vector2(0f, 0f), new Vector2(1f, 1f), 90f, spr, tint);
        }

        void Corner(string name, Vector2 anchor, Vector2 inward, float rotZ, Sprite spr, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(transform, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.sprite = spr;
            img.color = tint;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.maskable = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CornerPx, CornerPx);
            rt.anchoredPosition = inward * (CornerPx * 0.5f - 4f);
            rt.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        }

        void BuildCartouche(string title, EldritchTier tier)
        {
            var cart = new GameObject("Cartouche", typeof(RectTransform), typeof(LayoutElement));
            cart.transform.SetParent(transform, false);
            cart.GetComponent<LayoutElement>().ignoreLayout = true;
            var crt = cart.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.12f, 1f);
            crt.anchorMax = new Vector2(0.88f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0f, -CartoucheTop);
            crt.sizeDelta = new Vector2(0f, CartoucheH);

            var band = Solid(cart.transform, "Band", LapisSprite(),
                tier == EldritchTier.Eldritch ? EldritchPalette.Abyss : EldritchPalette.LapisDeep);
            var bandC = band.color;
            bandC.a = 0.72f;
            band.color = bandC;
            var capW = CartoucheH / 3f; // deco_cartouche_end is 64×192 → 1:3
            var brt = band.rectTransform;
            brt.anchorMin = new Vector2(0f, 0.08f);
            brt.anchorMax = new Vector2(1f, 0.92f);
            brt.offsetMin = new Vector2(capW * 0.8f, 0f);
            brt.offsetMax = new Vector2(-capW * 0.8f, 0f);

            var gold = EldritchPalette.MillenniumGold;
            gold.a = 0.85f;
            Hairline(cart.transform, "LineT", gold, true, capW);
            Hairline(cart.transform, "LineB", gold, false, capW);

            var capSpr = EldritchAssets.CartoucheEnd();
            var sand = EldritchPalette.Sandstone;
            var capL = Solid(cart.transform, "CapL", capSpr, sand);
            capL.preserveAspect = true;
            var lrt = capL.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(capW, 0f);

            var capR = Solid(cart.transform, "CapR", capSpr, sand);
            capR.preserveAspect = true;
            var rrt = capR.rectTransform;
            rrt.anchorMin = new Vector2(1f, 0f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(1f, 0.5f);
            rrt.anchoredPosition = Vector2.zero;
            rrt.sizeDelta = new Vector2(capW, 0f);
            rrt.localScale = new Vector3(-1f, 1f, 1f); // mirror the single cap decal

            var tgo = new GameObject("Title", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(cart.transform, false);
            var t = tgo.GetComponent<Text>();
            WrldzType.StyleGoldTitle(t, 22);
            t.text = title;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(capW, 0f);
            trt.offsetMax = new Vector2(-capW, 0f);
            TitleText = t;
        }

        static Image Solid(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite ?? UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            img.maskable = false;
            return img;
        }

        static void Hairline(Transform parent, string name, Color color, bool top, float inset)
        {
            var img = Solid(parent, name, null, color);
            var rt = img.rectTransform;
            var y = top ? 1f : 0f;
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y);
            rt.pivot = new Vector2(0.5f, y);
            rt.anchoredPosition = new Vector2(0f, top ? -2f : 2f);
            rt.sizeDelta = new Vector2(-inset * 1.6f, 2f);
        }

        static Sprite _lapis;

        /// <summary>EldritchAssets.LapisTile() is a raw texture; wrap it once for the cartouche band.</summary>
        static Sprite LapisSprite()
        {
            if (_lapis != null && _lapis.texture != null) return _lapis;
            var tex = EldritchAssets.LapisTile();
            if (tex == null) return null;
            _lapis = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            _lapis.name = "lapis_tile";
            return _lapis;
        }

        static Color GlyphGlowFor(EldritchTier tier) => tier switch
        {
            EldritchTier.Eldritch => EldritchPalette.AbyssalMagenta,
            EldritchTier.Danger => EldritchPalette.BloodDanger,
            EldritchTier.Secondary => EldritchPalette.SolidVisionCyan,
            _ => EldritchPalette.MillenniumGold
        };
    }
}
