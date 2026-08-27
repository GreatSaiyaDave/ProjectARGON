using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Horizontal command chip whose width is the label (plus optional icon + padding).
    /// The background always contains the glyphs — no overflow past the plate.
    /// </summary>
    public static class TextFitButton
    {
        public const float IconPx = 36f;
        public const float Gap = 10f;
        public const float PadL = 14f;
        public const float PadR = 18f;
        public const float PadY = 8f;
        public const float MinH = 48f;
        public const int TitleDesignSize = 16;

        public struct Built
        {
            public GameObject Go;
            public RectTransform Rt;
            public Text Label;
            public float Width;
            public float Height;
        }

        public static float MeasureTitle(string title)
        {
            var s = title ?? "";
            var font = WrldzType.Body() ?? UiFoundation.BuiltinFont();
            var size = WrldzType.Readable(TitleDesignSize);
            var approx = size * 0.62f * Mathf.Max(1, s.Length);
            if (font == null) return approx;
            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = size,
                fontStyle = FontStyle.Bold,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generateOutOfBounds = true,
                textAnchor = TextAnchor.MiddleLeft,
                scaleFactor = 1f,
                richText = false,
                lineSpacing = 1f,
                pivot = Vector2.zero,
                resizeTextForBestFit = false
            };
            var genW = new TextGenerator().GetPreferredWidth(s, settings);
            return genW > 1f ? genW : approx;
        }

        public static float WidthFor(string title, bool withIcon)
        {
            var icon = withIcon ? IconPx + Gap : 0f;
            return PadL + icon + MeasureTitle(title) + PadR;
        }

        public static Built Create(Transform parent, string name, string title, Sprite icon,
            Color fill, Color edge, Action onClick)
        {
            FreeUiKit.EnsureLoaded();
            var label = title ?? "";
            var withIcon = icon != null;
            var width = WidthFor(label, withIcon);
            var height = MinH;

            var go = new GameObject(string.IsNullOrEmpty(name) ? "TextFit" : name,
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = fill;
            img.raycastTarget = onClick != null;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = edge;
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            ol.useGraphicAlpha = false;

            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset((int)PadL, (int)PadR, (int)PadY, (int)PadY);
            hlg.spacing = Gap;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            if (parent == null || parent.GetComponent<LayoutGroup>() == null)
            {
                var fit = go.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            var rootLe = go.GetComponent<LayoutElement>();
            rootLe.minHeight = MinH;
            rootLe.preferredHeight = MinH;
            rootLe.preferredWidth = width;
            rootLe.flexibleWidth = 0f;

            if (withIcon)
            {
                var emblem = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                emblem.transform.SetParent(go.transform, false);
                var eImg = emblem.GetComponent<Image>();
                eImg.sprite = icon;
                eImg.preserveAspect = true;
                eImg.color = Color.white;
                eImg.raycastTarget = false;
                var eLe = emblem.GetComponent<LayoutElement>();
                eLe.preferredWidth = IconPx;
                eLe.preferredHeight = IconPx;
                eLe.minWidth = IconPx;
                eLe.minHeight = IconPx;
                eLe.flexibleWidth = 0f;
            }

            var labelGo = new GameObject("L", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            labelGo.transform.SetParent(go.transform, false);
            var t = labelGo.GetComponent<Text>();
            WrldzType.Style(t, TitleDesignSize, display: false, heavyOutline: true);
            t.text = label;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;
            var textW = MeasureTitle(label);
            var tLe = labelGo.GetComponent<LayoutElement>();
            tLe.preferredWidth = textW;
            tLe.minWidth = textW;
            tLe.preferredHeight = MinH - PadY * 2f;
            tLe.flexibleWidth = 0f;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = new Color(0.75f, 0.92f, 1f, 1f);
            cols.pressedColor = new Color(0.55f, 0.78f, 0.95f, 1f);
            cols.selectedColor = Color.white;
            cols.fadeDuration = 0.04f;
            btn.colors = cols;
            btn.interactable = onClick != null;
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());

            return new Built
            {
                Go = go,
                Rt = rt,
                Label = t,
                Width = width,
                Height = height
            };
        }
    }
}
