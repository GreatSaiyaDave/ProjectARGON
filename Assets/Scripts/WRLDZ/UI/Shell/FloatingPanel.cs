using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Shared sheet: one matte plate + 1 px edge. No nested glass, no gold hairline.
    /// Button helpers stay high-contrast for outdoor / AR.
    /// </summary>
    public static class FloatingPanel
    {
        public const float MinButtonHeight = 56f;
        public const float MinTouch = 48f;

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

        public static RectTransform Create(Transform parent, string name, bool goldEdge = false)
        {
            FreeUiKit.EnsureLoaded();
            var edge = goldEdge
                ? new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.55f)
                : new Color(0.55f, 0.72f, 0.82f, 0.40f);

            var shell = new GameObject(name, typeof(RectTransform), typeof(Image));
            shell.transform.SetParent(parent, false);
            var img = shell.GetComponent<Image>();
            var plate = DuelystUi.Panel() ?? ImagineAssets.PanelHolo();
            img.sprite = plate ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.color = plate != null ? Color.white : new Color(0.06f, 0.08f, 0.12f, 0.94f);
            img.raycastTarget = true;

            var ol = shell.AddComponent<Outline>();
            ol.effectColor = edge;
            ol.effectDistance = new Vector2(1.2f, -1.2f);
            ol.useGraphicAlpha = false;

            return shell.GetComponent<RectTransform>();
        }

        public static Button PrimaryButton(Transform parent, string label, System.Action onClick,
            bool danger = false, bool gold = false, bool centerLabel = false)
        {
            var kind = danger ? MenuCommandButton.Kind.Danger
                : gold ? MenuCommandButton.Kind.Gold
                : MenuCommandButton.Kind.Primary;
            return MenuCommandButton.Create(parent, label, onClick, kind, centerTitle: centerLabel);
        }

        /// <summary>Tint a floating chip. Always Simple + WhiteSprite — never a 9-slice plate.</summary>
        public static void ApplyButtonFace(Image img, bool primary = true, bool gold = false,
            bool danger = false)
        {
            if (img == null) return;
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
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

        public static Text Title(Transform parent, string text, int size = 28)
        {
            var go = new GameObject("Title", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.StyleGoldTitle(t, size);
            t.text = text;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            return t;
        }

        public static Text Body(Transform parent, string text, int size = 18)
        {
            var go = new GameObject("Body", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: false, heavyOutline: true);
            t.text = text;
            t.color = DuelystUi.TextCream;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
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
    }
}
