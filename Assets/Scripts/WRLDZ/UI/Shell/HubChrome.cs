using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Battle City dest tiles for the Eye / hub. Overlays are Solid Vision slates:
    /// obsidian fill, 1 px KaibaCorp filament, millennia gold stakes. Map stays
    /// visible around compact windows — never smoked glass covering the street.
    /// </summary>
    public static class HubChrome
    {
        public static readonly Color Dusk = new(0.03f, 0.035f, 0.08f, 0.38f);
        public static readonly Color WellFill = new(0.04f, 0.045f, 0.10f, 0.88f);
        public static readonly Color LowerDusk = new(0.03f, 0.035f, 0.08f, 0.42f);
        public static readonly Color Slate = new(0.035f, 0.042f, 0.09f, 0.96f);
        public static readonly Color SlateGold = new(0.12f, 0.09f, 0.03f, 0.96f);
        public static readonly Color ShadowWash = new(0.22f, 0.06f, 0.38f, 0.18f);

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
            // Hairline only — Outline blobs were the smoked-glass halo.
            var ol = face.GetComponent<Outline>() ?? face.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(edge.r, edge.g, edge.b, 0.38f);
            ol.effectDistance = new Vector2(1.0f, -1.0f);
            ol.useGraphicAlpha = false;
            ol.enabled = true;
            Shadow sh = null;
            foreach (var s in face.GetComponents<Shadow>())
            {
                if (s is Outline) continue;
                sh = s;
                break;
            }

            if (sh == null) sh = face.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.40f);
            sh.effectDistance = new Vector2(0f, -2f);
        }

        /// <summary>
        /// Sharp KaibaCorp filament on overlay wells. Not dest-tile L-ticks.
        /// </summary>
        public static void FilamentRim(Transform parent, Color color, float px = 1.5f)
        {
            if (parent == null) return;
            void Edge(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 pos)
            {
                var t = parent.Find(name);
                RectTransform rt;
                Image img;
                if (t == null)
                {
                    var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(parent, false);
                    rt = go.GetComponent<RectTransform>();
                    img = go.GetComponent<Image>();
                    var le = go.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
                    img.sprite = UiFoundation.WhiteSprite();
                    img.raycastTarget = false;
                }
                else
                {
                    rt = t as RectTransform;
                    img = t.GetComponent<Image>();
                    t.gameObject.SetActive(true);
                }

                rt.anchorMin = aMin;
                rt.anchorMax = aMax;
                rt.pivot = pivot;
                rt.sizeDelta = size;
                rt.anchoredPosition = pos;
                img.color = new Color(color.r, color.g, color.b, 0.92f);
                img.enabled = true;
            }

            Edge("FilamentT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, px), Vector2.zero);
            Edge("FilamentB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, px), Vector2.zero);
            Edge("FilamentL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(px, 0f), Vector2.zero);
            Edge("FilamentR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(px, 0f), Vector2.zero);
        }

        /// <summary>Hide overlay filament when a host returns to a HUD bar sprite.</summary>
        public static void HideFilament(Transform parent)
        {
            if (parent == null) return;
            foreach (var n in new[] { "FilamentT", "FilamentB", "FilamentL", "FilamentR", "RiftWash" })
            {
                var t = parent.Find(n);
                if (t != null)
                    t.gameObject.SetActive(false);
            }
        }

        /// <summary>Umbrax top wash — Shadow Game, not a glass streak.</summary>
        public static void RiftWash(Transform parent)
        {
            if (parent == null) return;
            var t = parent.Find("RiftWash");
            Image img;
            if (t == null)
            {
                var go = new GameObject("RiftWash", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                go.transform.SetAsFirstSibling();
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0.58f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                img = go.GetComponent<Image>();
                img.sprite = UiFoundation.WhiteSprite();
                img.raycastTarget = false;
                var le = go.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
            }
            else
            {
                t.gameObject.SetActive(true);
                img = t.GetComponent<Image>();
            }

            img.color = ShadowWash;
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
            img.color = new Color(0.04f, 0.03f, 0.09f, MenuChromePrefs.DimAlpha);
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
            if (plated)
                PaintPlate(btn.GetComponent<Image>(),
                    kind == MenuCommandButton.Kind.Gold ? DuelystUi.GoldHot : DuelystUi.Cyan,
                    gold: kind == MenuCommandButton.Kind.Gold);
            return btn;
        }

        /// <summary>Authored KaibaCorp Duel Disk plate. Never a code rectangle.</summary>
        public static void PaintPlate(Image img, Color edge, bool gold = false, bool sliced = true)
        {
            if (img == null) return;
            var spr = gold
                ? ImagineAssets.TileHubGold() ?? ImagineAssets.BtnGold()
                : ImagineAssets.TileHub() ?? ImagineAssets.BtnPrimary();
            HideFilament(img.transform);
            var ol = img.GetComponent<Outline>();
            if (ol != null) ol.enabled = false;
            if (spr != null)
            {
                img.sprite = spr;
                img.type = sliced && spr.border.sqrMagnitude > 0.1f
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                img.color = Color.white;
                return;
            }

            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = gold ? SlateGold : Slate;
            FilamentRim(img.transform, edge, px: 2f);
            CornerTicks(img.transform, gold ? DuelystUi.GoldHot : DuelystUi.Cyan);
        }

        /// <summary>Avoid 9-slice collapse on short toolbar rows.</summary>
        public static void FlattenPlate(Image img)
        {
            if (img == null) return;
            img.type = Image.Type.Simple;
            if (img.sprite == null)
                PaintPlate(img, DuelystUi.Cyan, sliced: false);
        }

        /// <summary>Quiet obsidian fill — HUD / list wells, not dest-tile art.</summary>
        public static void QuietFill(Image img, Color fill, Color? edge = null)
        {
            if (img == null) return;
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = fill;
            var ol = img.GetComponent<Outline>();
            if (ol != null) ol.enabled = false;
            if (edge.HasValue)
                FilamentRim(img.transform, edge.Value);
        }

        /// <summary>List / meter / inspect well. No tile_hub, no L-ticks.</summary>
        public static void PaintWell(Image img, bool flatten = false)
        {
            QuietFill(img, MenuChromePrefs.PanelColor, DuelystUi.Cyan);
        }

        /// <summary>Map / wallet chip — GO-soft navy, thin accent rim.</summary>
        public static void PaintChip(Image img, Color edge)
        {
            QuietFill(img, new Color(0.07f, 0.08f, 0.14f, 0.90f), edge);
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

        /// <summary>Floating overlay sheet — quiet navy, thin rim.</summary>
        public static RectTransform Sheet(Transform parent, string name,
            float x0, float y0, float x1, float y1, bool gold = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            QuietFill(img, MenuChromePrefs.PanelColor, gold ? DuelystUi.Gold : DuelystUi.Cyan);
            RiftWash(go.transform);
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
        }

        /// <summary>Header X — gold hub nugget (Simple stretch, never 9-slice).</summary>
        public static Button CloseChip(Transform parent, Action onClose, string label = "X")
        {
            var btn = MenuCommandButton.Create(parent, label, onClose,
                MenuCommandButton.Kind.Secondary, centerTitle: true);
            btn.name = "Close";
            Dress(btn, MenuCommandButton.Kind.Secondary, 18, displayTitle: true);
            PaintPlate(btn.GetComponent<Image>(), DuelystUi.GoldHot, gold: true);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(48f, 48f);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            return btn;
        }

        public static Button FooterBack(Transform parent, Action onClose, string label = "BACK")
        {
            var btn = Capsule(parent, label, onClose, MenuCommandButton.Kind.Gold,
                centerTitle: true, titleSize: 20);
            btn.name = "Back";
            Place(btn.GetComponent<RectTransform>(), 0.22f, 0.018f, 0.78f, 0.105f);
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
            Place(go.GetComponent<RectTransform>(), 0.03f, 0.90f, 0.97f, 0.995f);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            QuietFill(img, new Color(0.05f, 0.04f, 0.10f, 0.22f));

            var rule = new GameObject("GoldRule", typeof(RectTransform), typeof(Image));
            rule.transform.SetParent(go.transform, false);
            Place(rule.GetComponent<RectTransform>(), 0.04f, 0.00f, 0.50f, 0.06f);
            var rimg = rule.GetComponent<Image>();
            rimg.sprite = UiFoundation.WhiteSprite();
            rimg.color = new Color(DuelystUi.GoldHot.r, DuelystUi.GoldHot.g, DuelystUi.GoldHot.b, 0.85f);
            rimg.raycastTarget = false;

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

        public static RectTransform BodyWell(Transform parent, float x0 = 0.04f, float y0 = 0.12f,
            float x1 = 0.96f, float y1 = 0.88f)
        {
            var go = new GameObject("BodyWell", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            QuietFill(img, MenuChromePrefs.InsetColor);
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
            PaintPlate(img, gold ? DuelystUi.GoldHot : DuelystUi.Cyan, gold: gold);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 152f;
            le.preferredHeight = 160f;
            le.flexibleWidth = 1f;

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
            PaintPlate(btn.GetComponent<Image>(), gold ? DuelystUi.GoldHot : DuelystUi.Cyan, gold: gold);

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

            return btn;
        }

        /// <summary>Hub / Eye COMMAND dest plate (DECK, BAG, …).</summary>
        public static Button MountDest(Transform parent, string title, Sprite icon,
            Action onClick, string propStem = null)
        {
            var btn = MenuCommandButton.Create(parent, title, onClick,
                MenuCommandButton.Kind.Primary, centerTitle: true, plated: true);
            var face = btn.GetComponent<Image>();
            PaintPlate(face, DuelystUi.Cyan);
            MenuCommandButton.ApplyHubType(btn, 20, DuelystUi.TextCream, displayTitle: true);

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

            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.38f, 0.14f, 0.96f, 0.86f);
            var titleTxt = titleRt != null ? titleRt.GetComponent<Text>() : null;
            if (titleTxt != null)
                titleTxt.alignment = TextAnchor.MiddleLeft;

            return btn;
        }
    }
}
