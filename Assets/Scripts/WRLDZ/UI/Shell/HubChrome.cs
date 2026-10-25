using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;
using WRLDZ.UI.Eldritch;
using WRLDZ.UI.MenuV6;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Shared chrome for every non-menu screen, re-skinned onto Eldritch Solid Vision.
    /// Plates and wells are <see cref="SolidVisionSlate"/> surfaces (carved lapis / obsidian with a
    /// sandstone rim); capsules are <see cref="ArcaneButton"/> energy tiles; section caps and
    /// headers carry a hieroglyph read-head strip. Every public signature here is frozen — ~35
    /// screens call into it.
    /// <para>P3 (BUILD_PLAN D5 / §11 F5): under a <see cref="SanctuaryChrome"/> marker (the Stone, the overlay shells'
    /// ModalHost) HeaderBar, BodyWell, FooterBack, OverlayDim, QuietFill, FilamentRim, ListRow, Capsule and PaintWell paint the
    /// v6.3 sanctuary look instead (<see cref="SanctuaryPaint"/>: the false door's own chrome and type). The check runs every
    /// time a painter runs, so a row rebuilt later keeps its look; the duel HUD and the Overworld build outside any marker.
    /// </para>
    /// </summary>
    public static class HubChrome
    {
        public static readonly Color Dusk = EldritchPalette.WithAlpha(EldritchPalette.Void, 0.88f);
        public static readonly Color WellFill = EldritchPalette.WithAlpha(EldritchPalette.Obsidian, 0.88f);
        public static readonly Color LowerDusk = EldritchPalette.WithAlpha(EldritchPalette.Void, 0.92f);
        public static readonly Color Slate = EldritchPalette.WithAlpha(EldritchPalette.Obsidian, 0.96f);
        public static readonly Color SlateGold = EldritchPalette.WithAlpha(
            Color.Lerp(EldritchPalette.Obsidian, EldritchPalette.SandstoneDark, 0.40f), 0.96f);
        public static readonly Color ShadowWash = new(0.22f, 0.06f, 0.38f, 0.18f);

        /// <summary>Plate body opacity — stone, not glass (command chips stay at 0.70).</summary>
        const float PlateAlpha = 0.92f;

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ──────────────────────────────────────────────── theme mapping

        /// <summary>
        /// Old Battle City chrome colors become Egyptian: cyan → sandstone, cyan-gold → millennium
        /// gold. Eldritch accents (violet, magenta, blood, ichor) pass through untouched so Zone
        /// Mode and danger rims keep their hue.
        /// </summary>
        public static Color ThemeEdge(Color c)
        {
            if (c.b > c.r + 0.15f && c.g > c.r + 0.10f)
                return EldritchPalette.WithAlpha(EldritchPalette.Sandstone, Mathf.Max(c.a, 0.85f));
            if (c.r > 0.75f && c.g > 0.55f && c.b < 0.40f)   // sandstone (b≈0.42) stays sandstone
                return EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, Mathf.Max(c.a, 0.85f));
            return c;
        }

        /// <summary>
        /// Attach (or re-tier) the component that OWNS and releases this Image's per-instance
        /// material and keeps _RectSize in sync. Returns null for faces an ArcaneButton already owns.
        /// </summary>
        static SolidVisionSlate DressSlate(Image img, EldritchTier tier)
        {
            if (img == null) return null;
            if (img.GetComponent<ArcaneButton>() != null) return null;   // energy face owns its material
            var slate = img.GetComponent<SolidVisionSlate>();
            if (slate == null)
            {
                if (img.sprite == null) img.sprite = UiFoundation.WhiteSprite();
                slate = SolidVisionSlate.Attach(img, tier);
                if (slate != null)
                {
                    slate.HoverGlow = img.GetComponent<Button>() != null;
                    slate.OpenEmbers = false;
                }
            }
            else if (slate.Tier != tier)
            {
                slate.SetTier(tier);
            }

            return slate;
        }

        /// <summary>
        /// Body color rides the Image vertex color and _FillColor stays neutral: the slate shader
        /// scales fill AND emissive by vertex alpha, and the menu smoke test reads Image.color.a on
        /// HUD islands / chips, so the caller's opacity intent has to survive verbatim.
        /// </summary>
        static void Body(SolidVisionSlate slate, Image img, Color body, float alpha, Color edge)
        {
            if (img != null) img.color = new Color(body.r, body.g, body.b, Mathf.Clamp01(alpha));
            if (slate != null) slate.SetTint(Color.white, edge);
        }

        // ──────────────────────────────────────────────── rims & ticks

        public static void LiftPlate(Image face, Color edge)
        {
            if (face == null) return;
            // Outline/Shadow duplicate the quad; on the rect-space shaders that draws the whole
            // effect a second time, offset. Eldritch surfaces glow from the shader instead.
            if (face.GetComponent<ArcaneButton>() != null || face.GetComponent<SolidVisionSlate>() != null)
                return;
            edge = ThemeEdge(edge);
            // Hairline only — Outline blobs were the smoked-glass halo.
            var ol = UiFoundation.GetOrAdd<Outline>(face.gameObject);
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
        /// Sharp cartouche filament on overlay wells. Not dest-tile L-ticks.
        /// Sandstone / gold since the overhaul; eldritch accents keep their hue.
        /// </summary>
        public static void FilamentRim(Transform parent, Color color, float px = 2f)
        {
            if (parent == null) return;
            // v6.3 (under a SanctuaryChrome marker): the false door's own gold line, the same four edges
            color = SanctuaryPaint.On(parent) ? SanctuaryPaint.Rim() : ThemeEdge(color);
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
            img.color = new Color(EldritchPalette.Void.r, EldritchPalette.Void.g, EldritchPalette.Void.b, 0.55f);
            img.raycastTarget = false;
        }

        /// <summary>
        /// Section header: gold epigraphic type over a sandstone stake, with a hieroglyph read-head
        /// strip running the free gutter to its right. The <paramref name="color"/> argument now
        /// only chooses between the two golds — cyan caps were the old chrome.
        /// </summary>
        public static Text SectionCap(Transform parent, string name, string text, Color color,
            float x0, float y0, float x1, float y1)
        {
            var warm = color.r > 0.75f && color.g > 0.55f && color.b < 0.55f;
            var ink = warm ? EldritchPalette.GoldHot : EldritchPalette.MillenniumGold;

            var cap = GoTheme.Label(parent, name, text, 20, ink, TextAnchor.MiddleLeft);
            WrldzType.Style(cap, 20, display: true, heavyOutline: true);
            cap.color = ink;
            WrldzType.ApplyOutline(cap, heavy: true, buttonContrast: true);
            GoTheme.Place(cap.rectTransform, x0, y0, x1, y1);

            var tick = new GameObject(name + "Tick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(parent, false);
            GoTheme.Place(tick.GetComponent<RectTransform>(), x0, y0 - 0.006f, x0 + 0.10f, y0);
            var timg = tick.GetComponent<Image>();
            timg.sprite = UiFoundation.WhiteSprite();
            timg.color = EldritchPalette.WithAlpha(EldritchPalette.Sandstone, 0.90f);
            timg.raycastTarget = false;

            // Every section header in the game reads Egyptian: cells only when the gutter is real.
            if (0.96f - x1 > 0.12f)
            {
                var strip = new GameObject(name + "Glyphs", typeof(RectTransform), typeof(LayoutElement));
                strip.transform.SetParent(parent, false);
                strip.GetComponent<LayoutElement>().ignoreLayout = true;
                GoTheme.Place(strip.GetComponent<RectTransform>(), x1 + 0.02f, y0, 0.96f, y1);
                HieroglyphBorder.Create(strip.transform, HieroglyphBorder.Edge.Top, 30f,
                    EldritchPalette.MillenniumGold, 2.5f);
            }

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
            img.color = new Color(EldritchPalette.Abyss.r, EldritchPalette.Abyss.g, EldritchPalette.Abyss.b,
                MenuChromePrefs.DimAlpha);
            img.raycastTarget = true;
            // v6.3: the page's flat dim (over the Stone at the sheets' own rest opacity, over the map at the menu's budget)
            if (SanctuaryPaint.On(parent)) SanctuaryPaint.Dim(img, parent);
            var btn = dim.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            if (onClose != null)
                btn.onClick.AddListener(() => onClose());
            return img;
        }

        /// <summary>
        /// The workhorse button (43 call sites). Routes to <see cref="ArcaneButton"/> through
        /// <see cref="MenuCommandButton.Create"/> but still returns a plain <see cref="Button"/>
        /// with the same "Title" / "Blurb" children — callers Find() and re-place them.
        /// </summary>
        public static Button Capsule(Transform parent, string title, Action onClick,
            MenuCommandButton.Kind kind = MenuCommandButton.Kind.Primary,
            string blurb = null, bool centerTitle = true, int titleSize = 20,
            bool plated = true)
        {
            if (SanctuaryPaint.On(parent))
            {
                // v6.3: a quiet face (no tier shader) over the page's plate; the Title / Blurb children and the touch target stay
                var sb = SanctuaryPaint.CreateQuiet(parent, title, onClick, kind, blurb, centerTitle, titleSize);
                if (sb != null) SanctuaryPaint.Capsule(sb, kind);
                return sb;
            }

            var btn = MenuCommandButton.Create(parent, title, onClick, kind, blurb,
                centerTitle, plated);
            Dress(btn, kind, titleSize, displayTitle: true);
            return btn;
        }

        /// <summary>Hero / dest plate: carved sandstone over lapis (or obsidian + gold when gold).</summary>
        public static void PaintPlate(Image img, Color edge, bool gold = false, bool sliced = true)
        {
            if (img == null) return;
            HideFilament(img.transform);
            var ol = img.GetComponent<Outline>();
            if (ol != null) ol.enabled = false;

            var tier = gold ? EldritchTier.Primary : EldritchTier.Secondary;
            var ticks = gold ? EldritchPalette.MillenniumGold : EldritchPalette.Sandstone;

            var ab = img.GetComponent<ArcaneButton>();
            if (ab != null)
            {
                // Toggle chips re-call this to flip gold on/off; keep the face's own opacity.
                if (ab.Tier != tier) ab.SetTier(tier);
                MenuCommandButton.CarveStone(img);
                CornerTicks(img.transform, ticks);
                return;
            }

            var slate = DressSlate(img, tier);
            MenuCommandButton.CarveStone(img);
            Body(slate, img, gold ? EldritchPalette.Obsidian : EldritchPalette.Lapis, PlateAlpha,
                gold ? EldritchPalette.MillenniumGold : ThemeEdge(edge));
            CornerTicks(img.transform, ticks);
        }

        /// <summary>Kept for callers that used it to dodge 9-slice collapse; plates are Simple now.</summary>
        public static void FlattenPlate(Image img)
        {
            if (img == null) return;
            img.type = Image.Type.Simple;
            if (img.sprite == null)
                PaintPlate(img, EldritchPalette.Sandstone, sliced: false);
        }

        /// <summary>Quiet obsidian slate — HUD islands / list wells, not dest-tile art.</summary>
        public static void QuietFill(Image img, Color fill, Color? edge = null, bool millennia = false)
        {
            if (img == null) return;
            var ol = img.GetComponent<Outline>();
            if (ol != null) ol.enabled = false;
            if (img.GetComponent<ArcaneButton>() != null) return;   // MenuCommandButton faces
            if (SanctuaryPaint.On(img.transform))
            {
                // v6.3: the ledger strip's cel at the caller's alpha (its readability budget); never L-ticks
                SanctuaryPaint.Fill(img, fill);
                return;
            }

            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;

            var rim = ThemeEdge(edge ?? EldritchPalette.WithAlpha(EldritchPalette.SolidVisionCyan, 0.55f));
            var slate = DressSlate(img, EldritchTier.Tertiary);
            // Hue re-points at obsidian; the caller's alpha is their readability budget, keep it.
            Body(slate, img, EldritchPalette.Obsidian, fill.a, rim);
            if (millennia) CornerTicks(img.transform, rim);
        }

        /// <summary>List / meter / inspect well. Quiet Tertiary slate — no grain, no L-ticks.</summary>
        public static void PaintWell(Image img, bool flatten = false)
        {
            // No HideFilament here: wells are painted onto panels that own a RiftWash, and the
            // old code never struck it. (v6.3: QuietFill paints the ledger strip under a SanctuaryChrome marker.)
            if (img != null && SanctuaryPaint.On(img.transform)) SanctuaryPainted.Tag(img, "PaintWell");
            QuietFill(img, MenuChromePrefs.PanelColor,
                EldritchPalette.WithAlpha(EldritchPalette.Sandstone, 0.55f));
        }

        /// <summary>Map / wallet chip — obsidian slate, sandstone rim + millennia ticks.</summary>
        public static void PaintChip(Image img, Color edge)
        {
            QuietFill(img, EldritchPalette.WithAlpha(EldritchPalette.Obsidian, 0.90f), edge,
                millennia: true);
        }

        /// <summary>Millennia L-corner stakes. Sandstone / gold since the overhaul.</summary>
        public static void CornerTicks(Transform parent, Color color, float arm = 18f, float thick = 2.4f,
            float inset = 6f)
        {
            if (parent == null) return;
            color = ThemeEdge(color);
            var tint = new Color(color.r, color.g, color.b, 0.92f);
            void Arm(string name, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos)
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
                    img.sprite = UiFoundation.WhiteSprite();
                    img.raycastTarget = false;
                    var le = go.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
                }
                else
                {
                    rt = t as RectTransform;
                    img = t.GetComponent<Image>();
                    t.gameObject.SetActive(true);
                }

                rt.anchorMin = anchor;
                rt.anchorMax = anchor;
                rt.pivot = pivot;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
                img.color = tint;
            }

            Arm("TickTL_H", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(arm, thick), new Vector2(inset, -inset));
            Arm("TickTL_V", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(thick, arm), new Vector2(inset, -inset));
            Arm("TickTR_H", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(arm, thick), new Vector2(-inset, -inset));
            Arm("TickTR_V", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(thick, arm), new Vector2(-inset, -inset));
            Arm("TickBL_H", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(arm, thick), new Vector2(inset, inset));
            Arm("TickBL_V", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(thick, arm), new Vector2(inset, inset));
            Arm("TickBR_H", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(arm, thick), new Vector2(-inset, inset));
            Arm("TickBR_V", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(thick, arm), new Vector2(-inset, inset));
        }

        /// <summary>Floating overlay sheet — obsidian slate, sandstone rim, umbrax wash.</summary>
        public static RectTransform Sheet(Transform parent, string name,
            float x0, float y0, float x1, float y1, bool gold = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            QuietFill(img, MenuChromePrefs.PanelColor,
                gold ? EldritchPalette.MillenniumGold : EldritchPalette.Sandstone, millennia: true);
            RiftWash(go.transform);
            return go.GetComponent<RectTransform>();
        }

        public static void Dress(Button btn, MenuCommandButton.Kind kind, int titleSize = 20,
            bool displayTitle = true)
        {
            if (btn == null) return;
            var titleColor = kind switch
            {
                MenuCommandButton.Kind.Gold => EldritchPalette.GoldHot,
                MenuCommandButton.Kind.Danger => new Color(1f, 0.74f, 0.76f, 1f),
                _ => EldritchPalette.TextCream
            };
            MenuCommandButton.ApplyHubType(btn, titleSize, titleColor, displayTitle);
            // LiftPlate is a no-op on Eldritch faces (the shader draws the rim) — kept for the
            // plain Images that still come through Dress.
            LiftPlate(btn.GetComponent<Image>(), MenuCommandButton.EdgeFor(kind));
        }

        /// <summary>Header X — gold cartouche nugget, 56 px touch target.</summary>
        public static Button CloseChip(Transform parent, Action onClose, string label = "X")
        {
            var v6 = SanctuaryPaint.On(parent);
            var btn = v6
                ? SanctuaryPaint.CreateQuiet(parent, label, onClose, MenuCommandButton.Kind.Gold, null, true, 18)
                : MenuCommandButton.Create(parent, label, onClose, MenuCommandButton.Kind.Gold, centerTitle: true);
            btn.name = "Close";
            if (v6) SanctuaryPaint.Capsule(btn, MenuCommandButton.Kind.Gold);   // the gold CTA plate, its ink
            else Dress(btn, MenuCommandButton.Kind.Gold, 18, displayTitle: true);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(56f, 56f);
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
            var v6 = SanctuaryPaint.On(parent);
            if (v6)
            {
                // v6.3: the title sits on the false door's lintel (the frame paints it): no plate, no rule, no glyph strip
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(0f, 0f, 0f, 0f);
                SanctuaryPainted.Tag(img, "HeaderBar");
            }
            // P3: no L-ticks on the header in either look (AGENTS.md "No L-ticks"; the guard pins it)
            else QuietFill(img, new Color(0f, 0f, 0f, 0.22f), EldritchPalette.MillenniumGold);

            if (!v6)
            {
                var rule = new GameObject("GoldRule", typeof(RectTransform), typeof(Image));
                rule.transform.SetParent(go.transform, false);
                Place(rule.GetComponent<RectTransform>(), 0.04f, 0.00f, 0.50f, 0.06f);
                var rimg = rule.GetComponent<Image>();
                rimg.sprite = UiFoundation.WhiteSprite();
                rimg.color = EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.85f);
                rimg.raycastTarget = false;

                // Glyph run along the header's lower right — the close chip owns the upper right.
                var strip = new GameObject("HeaderGlyphs", typeof(RectTransform), typeof(LayoutElement));
                strip.transform.SetParent(go.transform, false);
                strip.GetComponent<LayoutElement>().ignoreLayout = true;
                Place(strip.GetComponent<RectTransform>(), 0.54f, 0.02f, 0.96f, 0.26f);
                HieroglyphBorder.Create(strip.transform, HieroglyphBorder.Edge.Top, 22f,
                    EldritchPalette.WithAlpha(EldritchPalette.Sandstone, 0.85f), 2f);
            }

            var hasSub = !string.IsNullOrEmpty(subtitle);
            titleT = FloatingPanel.Title(go.transform, title ?? "", 24);
            WrldzType.StyleGoldTitle(titleT, 24);
            titleT.fontStyle = FontStyle.Bold | FontStyle.Italic;
            titleT.horizontalOverflow = HorizontalWrapMode.Overflow;
            // Overflow, not Truncate: the header is 9.5 % of the frame, so at the readable type scale the title's
            // 46 % slot can be a pixel shorter than the glyph line — Truncate then drops the whole line and the sheet
            // shows no title at all (seen on DUEL SETUP and OPPONENTS at 4K). The header's RectMask2D still clips.
            titleT.verticalOverflow = VerticalWrapMode.Overflow;
            Place(titleT.rectTransform, 0.05f, hasSub ? 0.46f : 0.12f, 0.78f, 0.92f);

            subT = null;
            if (hasSub)
            {
                subT = FloatingPanel.Body(go.transform, subtitle, 14);
                WrldzType.Style(subT, 14, display: false, heavyOutline: true);
                subT.color = EldritchPalette.TextCream;
                subT.alignment = TextAnchor.MiddleLeft;
                subT.verticalOverflow = VerticalWrapMode.Overflow;   // same: a 40 % slot vs the body line height
                Place(subT.rectTransform, 0.05f, 0.08f, 0.78f, 0.48f);
            }

            if (v6)
            {
                // the sheet header type (texts.quit .h / texts.recess .sub), centred on the lintel
                Place(titleT.rectTransform, 0.10f, hasSub ? 0.46f : 0.10f, 0.90f, 0.95f);
                if (subT != null) Place(subT.rectTransform, 0.10f, 0.04f, 0.90f, 0.46f);
                SanctuaryPaint.HeaderType(titleT, subT);
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
            if (SanctuaryPaint.On(parent))
            {
                // v6.3: the false door's own fill is the body's ground; the well only clips (still the raycast target)
                img.sprite = UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.color = new Color(0f, 0f, 0f, 0f);
                SanctuaryPainted.Tag(img, "BodyWell");
                return go.GetComponent<RectTransform>();
            }

            QuietFill(img, MenuChromePrefs.InsetColor,
                EldritchPalette.WithAlpha(EldritchPalette.Sandstone, 0.45f));
            return go.GetComponent<RectTransform>();
        }

        public static RectTransform ListHead(Transform host, string text)
        {
            // P4 (the P3 landing: SETTINGS' MENU / DEVICE / DUEL showed only their tick): the v6.3 section cap
            if (SanctuaryPaint.On(host))
            {
                var head = SanctuaryPaint.Head(host, text);
                if (head != null) return head;
            }

            var go = new GameObject("Head", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = 32f;
            var t = FloatingPanel.Body(go.transform, text ?? "", 14);
            WrldzType.Style(t, 14, display: true, heavyOutline: true);
            t.color = EldritchPalette.GoldHot;
            t.alignment = TextAnchor.MiddleLeft;
            Place(t.rectTransform, 0.02f, 0.22f, 0.98f, 0.98f);
            var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(go.transform, false);
            Place(tick.GetComponent<RectTransform>(), 0.02f, 0.04f, 0.18f, 0.16f);
            var timg = tick.GetComponent<Image>();
            timg.sprite = UiFoundation.WhiteSprite();
            timg.color = EldritchPalette.WithAlpha(EldritchPalette.Sandstone, 0.90f);
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
            var v6 = SanctuaryPaint.On(host);
            var btn = v6
                ? SanctuaryPaint.CreateQuiet(host, text ?? "", onClick, kind, null, false, 16)
                : Capsule(host, text ?? "", onClick, kind, centerTitle: false, titleSize: 16);
            var le = btn.GetComponent<LayoutElement>();
            var tall = text != null && text.IndexOf('\n') >= 0;
            le.minHeight = tall ? 76f : 64f;
            le.preferredHeight = le.minHeight;
            // Without a width hint the row collapsed to the capsule's own preferred size,
            // truncating labels to chips ("Master Volume" -> "MAST"). Rows span the list.
            le.flexibleWidth = 1f;
            // ...and flexibleWidth is inert unless the list actually drives child widths.
            // These lists are authored as full-width bars, so opt the parent in here rather
            // than in every screen that happens to call ListRow.
            var group = host != null ? host.GetComponent<VerticalLayoutGroup>() : null;
            if (group != null)
                group.childControlWidth = true;
            if (onClick == null)
            {
                btn.interactable = false;
                // Dim the energy line too, or a dead row still races gold. (No ?.: GetComponent hands back Unity's fake null.)
                var ab = btn.GetComponent<ArcaneButton>();
                if (ab != null) ab.SetInteractable(false);
            }

            // v6.3: the page's chapter chip (lit for a gold row, dark for a dead one) and its row type
            if (v6) SanctuaryPaint.Capsule(btn, kind, row: true, gold: gold);

            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.06f, 0.10f, 0.94f, 0.90f);
            var title = titleRt != null ? titleRt.GetComponent<Text>() : null;
            if (title != null)
            {
                title.alignment = TextAnchor.MiddleLeft;
                title.horizontalOverflow = HorizontalWrapMode.Wrap;
                title.verticalOverflow = VerticalWrapMode.Truncate;
                if (v6) title.color = gold ? SanctuaryPaint.Gold : onClick != null ? SanctuaryPaint.Cream : SanctuaryPaint.Muted;
                else title.color = gold ? EldritchPalette.GoldHot : EldritchPalette.TextCream;
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
            PaintPlate(img, gold ? EldritchPalette.MillenniumGold : EldritchPalette.Sandstone, gold: gold);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 152f;
            le.preferredHeight = 160f;
            le.flexibleWidth = 1f;

            var titleT = FloatingPanel.Title(go.transform, title ?? "", 22);
            titleT.fontStyle = FontStyle.Bold | FontStyle.Italic;
            Place(titleT.rectTransform, 0.06f, 0.46f, 0.62f, 0.92f);
            var blurbT = FloatingPanel.Body(go.transform, blurb ?? "", 14);
            WrldzType.Style(blurbT, 14, display: false, heavyOutline: true);
            blurbT.color = EldritchPalette.TextCream;
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
                lockL.color = EldritchPalette.BloodDanger;
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
                gold ? EldritchPalette.GoldHot : EldritchPalette.TextCream, displayTitle: true,
                blurbSize: 16, blurbColor: EldritchPalette.TextCream);
            PaintPlate(btn.GetComponent<Image>(),
                gold ? EldritchPalette.MillenniumGold : EldritchPalette.Sandstone, gold: gold);

            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.06f, 0.46f, 0.62f, 0.92f);
            var blurbRt = btn.transform.Find("Blurb") as RectTransform;
            if (blurbRt != null)
                Place(blurbRt, 0.06f, 0.10f, 0.62f, 0.46f);

            if (btn.GetComponent<RectMask2D>() == null)
                btn.gameObject.AddComponent<RectMask2D>();

            if (icon != null)
            {
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(btn.transform, false);
                Place(ico.GetComponent<RectTransform>(), 0.68f, 0.16f, 0.94f, 0.84f);
                var img = ico.GetComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            return btn;
        }

        /// <summary>
        /// Hub / Eye / Overworld COMMAND dest plate (DECK, BAG, …). Carved treatment: sandstone
        /// grain face plus one struck hieroglyph in the left margin, same as the main menu tiles.
        /// </summary>
        public static Button MountDest(Transform parent, string title, Sprite icon,
            Action onClick, string propStem = null)
        {
            var btn = MenuCommandButton.Create(parent, title, onClick,
                MenuCommandButton.Kind.Primary, centerTitle: true, plated: true);
            var face = btn.GetComponent<Image>();
            PaintPlate(face, EldritchPalette.Sandstone);
            MenuCommandButton.ApplyHubType(btn, 20, EldritchPalette.TextCream, displayTitle: true);

            if (btn.GetComponent<RectMask2D>() == null)
                btn.gameObject.AddComponent<RectMask2D>();

            if (icon != null)
            {
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(btn.transform, false);
                Place(ico.GetComponent<RectTransform>(), 0.28f, 0.38f, 0.72f, 0.90f);
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
            }

            MenuCommandButton.CarveGlyph(btn.transform, 0.80f, 0.04f, 0.50f, 0.19f, 0.92f);

            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                Place(titleRt, 0.06f, 0.06f, 0.94f, 0.34f);
            var titleTxt = titleRt != null ? titleRt.GetComponent<Text>() : null;
            if (titleTxt != null)
                titleTxt.alignment = TextAnchor.MiddleCenter;

            return btn;
        }
    }
}
