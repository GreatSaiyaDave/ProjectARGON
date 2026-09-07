using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Battle City hub grammar shared by the home tiles and every related overlay.
    /// Capsule plates, cyan/gold neon rims, gold italic titles, cream blurbs,
    /// dusk dim so the hub / map still peeks through.
    /// </summary>
    public static class HubChrome
    {
        public static readonly Color Dusk = new(0.02f, 0.05f, 0.10f, 0.42f);
        public static readonly Color WellFill = new(0.03f, 0.05f, 0.09f, 0.42f);
        public static readonly Color LowerDusk = new(0.02f, 0.03f, 0.08f, 0.62f);

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void LiftPlate(Image face, Color edge)
        {
            if (face == null) return;
            var ol = face.GetComponent<Outline>() ?? face.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(edge.r, edge.g, edge.b, 0.70f);
            ol.effectDistance = new Vector2(2.4f, -2.4f);
            ol.useGraphicAlpha = false;
            Shadow sh = null;
            foreach (var s in face.GetComponents<Shadow>())
            {
                if (s is Outline) continue;
                sh = s;
                break;
            }

            if (sh == null) sh = face.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(0f, -3f);
        }

        public static void TextScrim(Transform parent, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject("Scrim", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.02f, 0.04f, 0.08f, 0.55f);
            img.raycastTarget = false;
        }

        public static Text SectionCap(Transform parent, string name, string text, Color color,
            float x0, float y0, float x1, float y1)
        {
            var cap = GoTheme.Label(parent, name, text, 20, color, TextAnchor.MiddleLeft);
            WrldzType.Style(cap, 20, display: true, heavyOutline: true);
            cap.color = color;
            WrldzType.ApplyOutline(cap, heavy: true, buttonContrast: true);
            GoTheme.Place(cap.rectTransform, x0, y0, x1, y1);

            var tick = new GameObject(name + "Tick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(parent, false);
            GoTheme.Place(tick.GetComponent<RectTransform>(), x0, y0 - 0.006f, x0 + 0.10f, y0);
            var timg = tick.GetComponent<Image>();
            timg.sprite = UiFoundation.WhiteSprite();
            timg.color = new Color(color.r, color.g, color.b, 0.85f);
            timg.raycastTarget = false;
            return cap;
        }

        /// <summary>Dusk veil so the hub / map stays readable behind a sheet.</summary>
        public static Image OverlayDim(Transform parent, Action onClose)
        {
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(parent, false);
            FloatingPanel.Stretch(dim.GetComponent<RectTransform>());
            dim.transform.SetAsFirstSibling();
            var img = dim.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.02f, 0.05f, 0.10f, MenuChromePrefs.DimAlpha);
            img.raycastTarget = true;
            var btn = dim.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            if (onClose != null)
                btn.onClick.AddListener(() => onClose());
            return img;
        }

        public static Button Capsule(Transform parent, string title, Action onClick,
            MenuCommandButton.Kind kind = MenuCommandButton.Kind.Primary,
            string blurb = null, bool centerTitle = true, int titleSize = 20,
            bool plated = true)
        {
            var btn = MenuCommandButton.Create(parent, title, onClick, kind, blurb,
                centerTitle, plated);
            Dress(btn, kind, titleSize, displayTitle: true);
            // Compact CTAs are often ~72–96px. TileHub 9-slice borders are 80px
            // and collapse to a smoked slab — stretch the plate art instead.
            if (plated && centerTitle && string.IsNullOrEmpty(blurb))
                FlattenPlate(btn.GetComponent<Image>());
            return btn;
        }

        /// <summary>Imagine Battle City plate — opaque painted tile, not smoked glass.</summary>
        public static void PaintPlate(Image img, Color edge, bool gold = false, bool sliced = true)
        {
            if (img == null) return;
            var plate = gold
                ? ImagineAssets.TileHubGold() ?? ImagineAssets.BtnGold() ?? ImagineAssets.PanelModal()
                : ImagineAssets.TileHub() ?? ImagineAssets.BtnPrimary() ?? ImagineAssets.PanelModal();
            if (plate != null)
            {
                img.sprite = plate;
                img.type = sliced && plate.border.sqrMagnitude > 0.1f
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                img.color = Color.white;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.color = gold
                    ? new Color(0.28f, 0.20f, 0.06f, 0.96f)
                    : new Color(0.06f, 0.10f, 0.16f, 0.96f);
            }

            LiftPlate(img, edge);
        }

        /// <summary>Avoid 9-slice collapse on short toolbar rows.</summary>
        public static void FlattenPlate(Image img)
        {
            if (img == null) return;
            img.type = Image.Type.Simple;
            if (img.sprite == null)
                PaintPlate(img, DuelystUi.Cyan, sliced: false);
        }

        /// <summary>Gold / cyan L-corner ticks from the hub featured/dest tiles.</summary>
        public static void CornerTicks(Transform parent, Color color)
        {
            if (parent == null) return;
            void Arm(string name, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos)
            {
                if (parent.Find(name) != null) return;
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = anchor;
                rt.anchorMax = anchor;
                rt.pivot = pivot;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
                var img = go.GetComponent<Image>();
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(color.r, color.g, color.b, 0.92f);
                img.raycastTarget = false;
                var le = go.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
            }

            Arm("TickTL_H", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, 2.4f), new Vector2(6f, -6f));
            Arm("TickTL_V", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(2.4f, 18f), new Vector2(6f, -6f));
            Arm("TickTR_H", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(18f, 2.4f), new Vector2(-6f, -6f));
            Arm("TickTR_V", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(2.4f, 18f), new Vector2(-6f, -6f));
            Arm("TickBL_H", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 2.4f), new Vector2(6f, 6f));
            Arm("TickBL_V", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(2.4f, 18f), new Vector2(6f, 6f));
            Arm("TickBR_H", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(18f, 2.4f), new Vector2(-6f, 6f));
            Arm("TickBR_V", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(2.4f, 18f), new Vector2(-6f, 6f));
        }

        /// <summary>Large overlay sheet — hub tile face + L-ticks.</summary>
        public static RectTransform Sheet(Transform parent, string name,
            float x0, float y0, float x1, float y1, bool gold = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            PaintPlate(img, gold ? DuelystUi.Gold : DuelystUi.Cyan, gold);
            CornerTicks(go.transform, gold ? DuelystUi.GoldHot : DuelystUi.Cyan);
            MenuHoloPulse.Attach(go, scan: true, breathe: false);
            return go.GetComponent<RectTransform>();
        }

        public static void Dress(Button btn, MenuCommandButton.Kind kind, int titleSize = 20,
            bool displayTitle = true)
        {
            if (btn == null) return;
            var titleColor = kind switch
            {
                MenuCommandButton.Kind.Gold => DuelystUi.GoldHot,
                MenuCommandButton.Kind.Danger => new Color(1f, 0.72f, 0.74f, 1f),
                MenuCommandButton.Kind.Secondary => DuelystUi.TextCream,
                _ => Color.white
            };
            MenuCommandButton.ApplyHubType(btn, titleSize, titleColor, displayTitle);
            var edge = kind switch
            {
                MenuCommandButton.Kind.Gold => DuelystUi.Gold,
                MenuCommandButton.Kind.Danger => MenuCommandButton.EdgeDanger,
                MenuCommandButton.Kind.Secondary => DuelystUi.Cyan,
                _ => DuelystUi.Cyan
            };
            LiftPlate(btn.GetComponent<Image>(), edge);
            MenuHoloPulse.Attach(btn.gameObject, scan: true, breathe: false);
        }

        /// <summary>Header X — gold hub nugget (Simple stretch, never 9-slice).</summary>
        public static Button CloseChip(Transform parent, Action onClose, string label = "X")
        {
            var btn = MenuCommandButton.Create(parent, label, onClose,
                MenuCommandButton.Kind.Secondary, centerTitle: true);
            btn.name = "Close";
            Dress(btn, MenuCommandButton.Kind.Secondary, 18, displayTitle: true);
            var face = btn.GetComponent<Image>();
            var nugget = ImagineAssets.BtnGold() ?? ImagineAssets.TileHubGold();
            if (face != null && nugget != null)
            {
                face.sprite = nugget;
                face.type = Image.Type.Simple;
                face.color = Color.white;
                LiftPlate(face, DuelystUi.Gold);
            }
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(64f, 64f);
            rt.anchoredPosition = new Vector2(-8f, -8f);
            return btn;
        }

        public static Button FooterBack(Transform parent, Action onClose, string label = "BACK")
        {
            var btn = Capsule(parent, label, onClose, MenuCommandButton.Kind.Gold,
                centerTitle: true, titleSize: 20);
            btn.name = "Back";
            Place(btn.GetComponent<RectTransform>(), 0.18f, 0.012f, 0.82f, 0.125f);
            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.08f, 0.12f, 0.92f, 0.88f);
            return btn;
        }

        public static RectTransform HeaderBar(Transform parent, string title, string subtitle,
            Action onClose, out Text titleT, out Text subT)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), 0.04f, 0.855f, 0.96f, 0.978f);
            var img = go.GetComponent<Image>();
            var plate = ImagineAssets.BtnPrimary() ?? ImagineAssets.TileHub() ?? ImagineAssets.PanelHolo();
            if (plate != null)
            {
                img.sprite = plate;
                img.type = plate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                img.color = Color.white;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = MenuCommandButton.FillPrimary;
            }

            LiftPlate(img, DuelystUi.Cyan);
            CornerTicks(go.transform, DuelystUi.Cyan);
            MenuHoloPulse.Attach(go, scan: true, breathe: false, phase: 0.15f);

            var hasSub = !string.IsNullOrEmpty(subtitle);
            titleT = FloatingPanel.Title(go.transform, title ?? "", 24);
            WrldzType.StyleGoldTitle(titleT, 24);
            titleT.fontStyle = FontStyle.Bold | FontStyle.Italic;
            titleT.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleT.verticalOverflow = VerticalWrapMode.Truncate;
            Place(titleT.rectTransform, 0.05f, hasSub ? 0.46f : 0.12f, 0.78f, 0.92f);

            subT = null;
            if (hasSub)
            {
                subT = FloatingPanel.Body(go.transform, subtitle, 14);
                WrldzType.Style(subT, 14, display: false, heavyOutline: true);
                subT.color = DuelystUi.TextCream;
                subT.alignment = TextAnchor.MiddleLeft;
                Place(subT.rectTransform, 0.05f, 0.08f, 0.78f, 0.48f);
            }

            if (onClose != null)
                CloseChip(go.transform, onClose);
            return go.GetComponent<RectTransform>();
        }

        public static RectTransform BodyWell(Transform parent, float x0 = 0.04f, float y0 = 0.145f,
            float x1 = 0.96f, float y1 = 0.840f)
        {
            var go = new GameObject("BodyWell", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            PaintPlate(img, DuelystUi.Cyan);
            CornerTicks(go.transform, DuelystUi.Cyan);
            return go.GetComponent<RectTransform>();
        }

        public static RectTransform ListHead(Transform host, string text)
        {
            var go = new GameObject("Head", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = 32f;
            var t = FloatingPanel.Body(go.transform, text ?? "", 14);
            WrldzType.Style(t, 14, display: true, heavyOutline: true);
            t.color = DuelystUi.GoldHot;
            t.alignment = TextAnchor.MiddleLeft;
            Place(t.rectTransform, 0.02f, 0.22f, 0.98f, 0.98f);
            var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(go.transform, false);
            Place(tick.GetComponent<RectTransform>(), 0.02f, 0.04f, 0.18f, 0.16f);
            var timg = tick.GetComponent<Image>();
            timg.sprite = UiFoundation.WhiteSprite();
            timg.color = new Color(DuelystUi.GoldHot.r, DuelystUi.GoldHot.g, DuelystUi.GoldHot.b, 0.85f);
            timg.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        public static Button ListRow(Transform host, string text, Action onClick, bool gold = false)
        {
            var kind = gold
                ? MenuCommandButton.Kind.Gold
                : onClick != null
                    ? MenuCommandButton.Kind.Primary
                    : MenuCommandButton.Kind.Secondary;
            var btn = Capsule(host, text ?? "", onClick, kind, centerTitle: false, titleSize: 16);
            var le = btn.GetComponent<LayoutElement>();
            var tall = text != null && text.IndexOf('\n') >= 0;
            le.minHeight = tall ? 76f : 64f;
            le.preferredHeight = le.minHeight;
            if (onClick == null)
                btn.interactable = false;
            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.06f, 0.10f, 0.94f, 0.90f);
            var title = titleRt != null ? titleRt.GetComponent<Text>() : null;
            if (title != null)
            {
                title.alignment = TextAnchor.MiddleLeft;
                title.horizontalOverflow = HorizontalWrapMode.Wrap;
                title.verticalOverflow = VerticalWrapMode.Truncate;
                title.color = gold ? DuelystUi.GoldHot : DuelystUi.TextCream;
            }

            return btn;
        }

        public static RectTransform FeaturedCard(Transform parent, string title, string blurb,
            Action onClick, bool gold, string cta = "START")
        {
            var go = new GameObject(string.IsNullOrEmpty(title) ? "Card" : title,
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            var plate = gold
                ? ImagineAssets.TileHubGold() ?? ImagineAssets.BtnGold()
                : ImagineAssets.BtnPrimary() ?? ImagineAssets.TileHub();
            if (plate != null)
            {
                img.sprite = plate;
                img.type = plate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                img.color = Color.white;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = gold ? MenuCommandButton.FillGold : MenuCommandButton.FillPrimary;
            }

            LiftPlate(img, gold ? DuelystUi.Gold : DuelystUi.Cyan);
            CornerTicks(go.transform, gold ? DuelystUi.GoldHot : DuelystUi.Cyan);
            MenuHoloPulse.Attach(go, scan: true, breathe: true, phase: gold ? 0f : 0.5f);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 152f;
            le.preferredHeight = 160f;
            le.flexibleWidth = 1f;

            TextScrim(go.transform, 0.04f, 0.10f, 0.62f, 0.90f);
            var titleT = FloatingPanel.Title(go.transform, title ?? "", 22);
            titleT.fontStyle = FontStyle.Bold | FontStyle.Italic;
            Place(titleT.rectTransform, 0.06f, 0.46f, 0.62f, 0.92f);
            var blurbT = FloatingPanel.Body(go.transform, blurb ?? "", 14);
            WrldzType.Style(blurbT, 14, display: false, heavyOutline: true);
            blurbT.color = DuelystUi.TextCream;
            Place(blurbT.rectTransform, 0.06f, 0.10f, 0.62f, 0.46f);

            if (onClick != null)
            {
                var play = Capsule(go.transform, cta, onClick, MenuCommandButton.Kind.Gold,
                    centerTitle: true, titleSize: 18);
                Place(play.GetComponent<RectTransform>(), 0.66f, 0.22f, 0.96f, 0.78f);
            }
            else
            {
                var lockL = FloatingPanel.Body(go.transform, "LOCKED", 16);
                lockL.color = DuelystUi.Danger;
                lockL.alignment = TextAnchor.MiddleCenter;
                Place(lockL.rectTransform, 0.66f, 0.22f, 0.96f, 0.78f);
            }

            return go.GetComponent<RectTransform>();
        }

        /// <summary>Hub / Eye featured plate (VS AI, VS PLAYER).</summary>
        public static Button MountFeatured(Transform parent, string title, string blurb,
            Sprite icon, Action onClick, bool gold, string propStem = null)
        {
            var kind = gold ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary;
            var btn = MenuCommandButton.Create(parent, title, onClick, kind, blurb, plated: true);
            MenuCommandButton.ApplyHubType(btn, 24,
                gold ? DuelystUi.GoldHot : DuelystUi.Cyan, displayTitle: true,
                blurbSize: 16, blurbColor: DuelystUi.TextCream);
            LiftPlate(btn.GetComponent<Image>(), gold ? DuelystUi.Gold : DuelystUi.Cyan);
            CornerTicks(btn.transform, gold ? DuelystUi.GoldHot : DuelystUi.Cyan);
            TextScrim(btn.transform, 0.04f, 0.10f, 0.62f, 0.90f);

            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.06f, 0.46f, 0.62f, 0.92f);
            var blurbRt = btn.transform.Find("Blurb") as RectTransform;
            if (blurbRt != null)
                Place(blurbRt, 0.06f, 0.10f, 0.62f, 0.46f);

            var well = false;
            if (!string.IsNullOrEmpty(propStem))
            {
                well = HubPropView.CreateInUi(btn.transform, propStem,
                    0.62f, 0.08f, 0.97f, 0.92f,
                    gold ? DuelystUi.GoldHot : DuelystUi.Cyan, HubPropView.FeaturedRt) != null;
            }

            if (!well && icon != null)
            {
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(btn.transform, false);
                Place(ico.GetComponent<RectTransform>(), 0.64f, 0.12f, 0.96f, 0.88f);
                var img = ico.GetComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            MenuHoloPulse.Attach(btn.gameObject, scan: true, breathe: true, phase: gold ? 0f : 0.5f);
            return btn;
        }

        /// <summary>Hub / Eye COMMAND dest plate (DECK, BAG, …).</summary>
        public static Button MountDest(Transform parent, string title, Sprite icon,
            Action onClick, string propStem = null)
        {
            var btn = MenuCommandButton.Create(parent, title, onClick,
                MenuCommandButton.Kind.Primary, centerTitle: true, plated: true);
            var face = btn.GetComponent<Image>();
            var plate = ImagineAssets.BtnPrimary() ?? ImagineAssets.TileHub() ?? ImagineAssets.PanelHolo();
            if (face != null && plate != null)
            {
                face.sprite = plate;
                face.type = plate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                face.color = Color.white;
            }

            LiftPlate(face, DuelystUi.Cyan);
            CornerTicks(btn.transform, DuelystUi.Cyan);
            MenuCommandButton.ApplyHubType(btn, 20, Color.white, displayTitle: true);

            var well = HubPropView.CreateInUi(btn.transform, propStem,
                0.03f, 0.10f, 0.36f, 0.90f, DuelystUi.Cyan, HubPropView.DestRt) != null;
            if (!well && icon != null)
            {
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(btn.transform, false);
                Place(ico.GetComponent<RectTransform>(), 0.03f, 0.12f, 0.36f, 0.88f);
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
            }

            TextScrim(btn.transform, 0.36f, 0.18f, 0.96f, 0.82f);
            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.38f, 0.14f, 0.96f, 0.86f);
            var titleTxt = titleRt != null ? titleRt.GetComponent<Text>() : null;
            if (titleTxt != null)
                titleTxt.alignment = TextAnchor.MiddleLeft;

            MenuHoloPulse.Attach(btn.gameObject, scan: true, breathe: true);
            return btn;
        }
    }
}
