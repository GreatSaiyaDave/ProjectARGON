using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Floating command chip: translucent fill, thin accent edge, cream type.
    /// Never 9-slice Imagine plates here — those collapse to opaque black at
    /// toolbar height (~56px) and paint a slab over the map / stage.
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

        // Glass fills — dark enough for cream type, open enough to read the world.
        public static readonly Color FillPrimary = new(0.10f, 0.32f, 0.44f, 0.48f);
        public static readonly Color FillSecondary = new(0.10f, 0.14f, 0.20f, 0.42f);
        public static readonly Color FillGold = new(0.32f, 0.24f, 0.06f, 0.50f);
        public static readonly Color FillDanger = new(0.42f, 0.10f, 0.14f, 0.50f);
        public static readonly Color TitleInk = new(0.96f, 0.96f, 0.94f, 1f);
        public static readonly Color BlurbInk = new(0.78f, 0.86f, 0.94f, 1f);
        public static readonly Color EdgePrimary = new(0.35f, 0.78f, 0.90f, 1f);
        public static readonly Color EdgeSecondary = new(0.38f, 0.44f, 0.52f, 1f);
        public static readonly Color EdgeGold = new(0.92f, 0.78f, 0.32f, 1f);
        public static readonly Color EdgeDanger = new(0.92f, 0.36f, 0.40f, 1f);

        public static Button Create(Transform parent, string title, Action onClick,
            Kind kind = Kind.Primary, string blurb = null, bool centerTitle = false)
        {
            FreeUiKit.EnsureLoaded();
            var edge = EdgeFor(kind);
            var fill = FillFor(kind);

            var go = new GameObject(string.IsNullOrEmpty(title) ? "Cmd" : title,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var face = go.GetComponent<Image>();
            face.sprite = UiFoundation.WhiteSprite();
            face.type = Image.Type.Simple;
            face.color = fill;
            face.raycastTarget = true;

            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(edge.r, edge.g, edge.b, 0.70f);
            ol.effectDistance = new Vector2(1.4f, -1.4f);
            ol.useGraphicAlpha = false;

            var le = go.GetComponent<LayoutElement>();
            le.minHeight = string.IsNullOrEmpty(blurb) ? 50f : 64f;
            le.preferredHeight = le.minHeight;
            le.flexibleWidth = 1f;

            if (!centerTitle)
            {
                var rail = Solid(go.transform, "Rail", edge);
                var rrt = rail.rectTransform;
                rrt.anchorMin = new Vector2(0f, 0.18f);
                rrt.anchorMax = new Vector2(0f, 0.82f);
                rrt.pivot = new Vector2(0f, 0.5f);
                rrt.anchoredPosition = Vector2.zero;
                rrt.sizeDelta = new Vector2(3f, 0f);
                rail.raycastTarget = false;
            }

            var hasBlurb = !string.IsNullOrEmpty(blurb);
            var titleAlign = centerTitle && !hasBlurb ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            var titleGo = MakeText(go.transform, "Title", title, TitleInk,
                titleAlign, maxSize: hasBlurb ? 18 : 20);
            if (hasBlurb)
            {
                Place(titleGo.rectTransform, 0.06f, 0.48f, 0.96f, 0.92f);
                var blurbGo = MakeText(go.transform, "Blurb", blurb, BlurbInk,
                    TextAnchor.MiddleLeft, maxSize: 13);
                blurbGo.fontStyle = FontStyle.Normal;
                Place(blurbGo.rectTransform, 0.06f, 0.08f, 0.96f, 0.46f);
            }
            else if (centerTitle)
            {
                Place(titleGo.rectTransform, 0.04f, 0.10f, 0.96f, 0.90f);
            }
            else
            {
                Place(titleGo.rectTransform, 0.06f, 0.14f, 0.96f, 0.86f);
            }

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = face;
            var block = ColorBlock.defaultColorBlock;
            block.normalColor = Color.white;
            block.highlightedColor = new Color(1.15f, 1.15f, 1.18f, 1f);
            block.pressedColor = new Color(0.72f, 0.78f, 0.82f, 1f);
            block.selectedColor = Color.white;
            block.fadeDuration = 0.08f;
            btn.colors = block;
            if (onClick != null)
            {
                btn.onClick.AddListener(() =>
                {
                    FreeUiKit.PlayClick();
                    onClick();
                });
            }

            return btn;
        }

        static Color EdgeFor(Kind kind) => kind switch
        {
            Kind.Gold => EdgeGold,
            Kind.Danger => EdgeDanger,
            Kind.Secondary => EdgeSecondary,
            _ => EdgePrimary
        };

        static Color FillFor(Kind kind) => kind switch
        {
            Kind.Gold => FillGold,
            Kind.Danger => FillDanger,
            Kind.Secondary => FillSecondary,
            _ => FillPrimary
        };

        static Image Solid(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Text MakeText(Transform parent, string name, string value,
            Color color, TextAnchor align, int maxSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = UiFoundation.BuiltinFont() ?? WrldzType.Body();
            t.fontSize = maxSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = align;
            t.color = color;
            t.text = value ?? "";
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 10;
            t.resizeTextMaxSize = Mathf.Clamp(maxSize, 12, 22);
            t.raycastTarget = false;
            t.supportRichText = false;
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.65f);
            o.effectDistance = new Vector2(0.8f, -0.8f);
            return t;
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
