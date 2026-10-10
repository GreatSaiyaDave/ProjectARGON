using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Screen-level dressing shared by every Eldritch surface: carved stone faces, struck
    /// hieroglyphs, screen-corner tentacles, and the atmosphere prune.
    ///
    /// This exists because the main menu and the Desktop Lab both needed the identical treatment
    /// and a second copy had already started to drift. One implementation, two callers.
    /// </summary>
    public static class EldritchDressing
    {
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

        /// <summary>
        /// Give a button the Egyptian read a flat fill lacks: sandstone grain sampled as the
        /// shader's _MainTex (it multiplies the tier fill, so lapis becomes carved lapis) plus one
        /// struck hieroglyph in the left margin.
        /// </summary>
        /// <param name="btn">Target button; safe to pass null.</param>
        /// <param name="glyphAlpha">0 skips the glyph entirely.</param>
        /// <param name="glyphX0">Left edge of the glyph cell, normalized.</param>
        /// <param name="glyphX1">Right edge of the glyph cell, normalized.</param>
        /// <param name="textX0">
        /// When &gt; 0, Title and Blurb are re-placed to start here so long labels never run under
        /// the glyph. Rows with wide copy (the Desktop Lab) need this; short tiles do not.
        /// </param>
        public static void CarveFace(ArcaneButton btn, float glyphAlpha = 0.85f,
            float glyphX0 = 0.05f, float glyphX1 = 0.20f, float textX0 = 0f)
        {
            if (btn == null) return;

            var grain = StoneSprite();
            if (grain != null && btn.Face != null)
                btn.Face.sprite = grain;   // Bind() already ran, so this survives

            if (textX0 > 0f)
            {
                ShiftLeftEdge(btn.Title, textX0);
                ShiftLeftEdge(btn.Blurb, textX0);
            }

            if (glyphAlpha <= 0f) return;
            var pool = EldritchGlyphs.Atlas();
            if (pool == null || pool.Length == 0) return;

            var go = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(btn.transform, false);
            go.transform.SetAsFirstSibling();   // under the label, over the face
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(glyphX0, 0.18f);
            rt.anchorMax = new Vector2(glyphX1, 0.82f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.sprite = pool[Mathf.Abs((btn.name ?? "").GetHashCode()) % pool.Length];
            img.preserveAspect = true;
            img.raycastTarget = false;
            var gold = EldritchPalette.MillenniumGold;
            img.color = new Color(gold.r, gold.g, gold.b, glyphAlpha);
        }

        static void ShiftLeftEdge(Text t, float x0)
        {
            if (t == null) return;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(x0, rt.anchorMin.y);
            rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
        }

        /// <summary>
        /// Re-face a button as a fragment of the failing barrier-stone: carved stone, a breach
        /// into the Shadow Realm, Millennium gold inlay and Andrax's barrier light. The seal
        /// animates itself and strains under the pointer.
        /// </summary>
        public static BarrierStone SealFace(ArcaneButton btn, SealState state)
        {
            if (btn == null || btn.Face == null) return null;
            var seal = BarrierStone.Attach(btn.Face, state);
            if (seal == null) return null;
            // The stone owns the surface now, so drop the energy-flow ring's own dressing.
            if (btn.Border != null) btn.Border.SetIntensity(0.55f);
            return seal;
        }

        /// <summary>
        /// The same fragment for everything that is not a button: panels, input slots, dropdown
        /// bars, decorative plinths. <see cref="SealFace"/> only takes an <see cref="ArcaneButton"/>,
        /// which left most of a screen with no approved way into the language at all.
        /// </summary>
        /// <param name="reactive">Pointer strain. Off for plates nobody can press.</param>
        public static BarrierStone SealPlate(Image face, SealState state, bool reactive = false)
        {
            if (face == null) return null;

            // Two components must never both claim Image.material. SolidVisionSlate.OnDestroy
            // releases its own Mat field rather than Face.material, so the order is safe either
            // way, but strip first so nothing re-binds the slate between here and Attach.
            var slate = face.GetComponent<SolidVisionSlate>();
            if (slate != null) ArObjectUtil.Destroy(slate);

            // The rect shaders draw their own edge / inlay and read _MainTex.a as the plate's
            // shape, so the face has to stay a full-rect Simple white sprite.
            face.sprite = UiFoundation.WhiteSprite();
            face.type = Image.Type.Simple;
            face.color = Color.white;

            var seal = BarrierStone.Attach(face, state);
            if (seal != null) seal.Reactive = reactive;
            return seal;
        }

        /// <summary>
        /// Something vast is holding the frame. The decal is authored rooted bottom-left, so each
        /// corner gets it mirrored inward, dim violet, behind every interactive plate.
        /// </summary>
        public static void ScreenTentacles(RectTransform root, float alpha = 0.30f)
        {
            if (root == null) return;
            var spr = EldritchAssets.TentacleCorner();
            if (spr == null) return;
            var tint = EldritchPalette.EldritchViolet;
            tint.a = alpha;

            void Corner(string name, float x0, float y0, float x1, float y1, float sx, float sy)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(x0, y0);
                rt.anchorMax = new Vector2(x1, y1);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = new Vector3(sx, sy, 1f);
                var img = go.GetComponent<Image>();
                img.sprite = spr;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = tint;
            }

            Corner("TentacleBL", 0.00f, 0.00f, 0.42f, 0.30f, 1f, 1f);
            Corner("TentacleBR", 0.58f, 0.00f, 1.00f, 0.30f, -1f, 1f);
            Corner("TentacleTL", 0.00f, 0.74f, 0.38f, 1.00f, 1f, -1f);
            Corner("TentacleTR", 0.62f, 0.74f, 1.00f, 1.00f, -1f, -1f);
        }

        /// <summary>
        /// Keep only the atmosphere's twinkling stars. Its sky / sand / veil plates are opaque and
        /// bury the painted backdrop, its glyph stream is legacy Text over the U+13000 block (which
        /// renders as tofu boxes), and its dust motes are bare rectangles. The hieroglyph borders
        /// and <see cref="UiEmberField"/> carry those motifs properly.
        /// </summary>
        public static void KeepOnlyStars(Transform atmosphere)
        {
            if (atmosphere == null) return;
            for (var i = 0; i < atmosphere.childCount; i++)
            {
                var ch = atmosphere.GetChild(i);
                if (!ch.name.StartsWith("Star", StringComparison.Ordinal))
                    ch.gameObject.SetActive(false);
            }
        }
    }
}
