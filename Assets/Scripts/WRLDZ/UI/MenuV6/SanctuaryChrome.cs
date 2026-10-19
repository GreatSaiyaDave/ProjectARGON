using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.UI.Eldritch;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>Where a v6.3 surface sits: over the Stone (the main menu's own sheets, the hub's overlay shell) or over the map.</summary>
    public enum SanctuaryHost
    {
        Stone,
        Overlay
    }

    /// <summary>
    /// D5 as amended by §11 F5: a marker on the root of a v6.3 surface (the Stone, the overlay shells' ModalHost). The shared
    /// HubChrome painters paint the v6.3 look only under it, by checking <see cref="Under"/> (GetComponentInParent) when they
    /// build or rebuild a row, so rows rebuilt later (SystemsSheets' Refresh closures) keep the right look and the duel HUD /
    /// Overworld painters, built outside any marker, never see it. A marker with <see cref="Paint"/> false keeps today's look
    /// for its subtree (the deck builder until P4, §11 F3; the AR holo frame).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SanctuaryChrome : MonoBehaviour
    {
        /// <summary>False: the subtree under this marker keeps today's chrome.</summary>
        public bool Paint = true;

        public SanctuaryHost Host = SanctuaryHost.Overlay;

        /// <summary>The nearest marker above <paramref name="t"/> (inactive parents included), or null.</summary>
        public static SanctuaryChrome Of(Transform t) => t != null ? t.GetComponentInParent<SanctuaryChrome>(true) : null;

        /// <summary>True when <paramref name="t"/> paints the v6.3 look.</summary>
        public static bool Under(Transform t)
        {
            var m = Of(t);
            return m != null && m.Paint;
        }

        /// <summary>True when the sheets under <paramref name="t"/> sit over a live Stone: its marker says so and a Stone is up. A
        /// shared overlay shell the menu marked (DuelDiskMenuUI.EnsureHubShell) keeps its mark after the player walks to the map,
        /// where OverworldUI reuses it; there its sheets take the map window again.</summary>
        public static bool OverStone(Transform t)
        {
            var m = Of(t);
            if (m == null || m.Host != SanctuaryHost.Stone) return false;
            if (m.TryGetComponent(out MenuV6Stage _)) return true;
            var live = FindAnyObjectByType<MenuV6Stage>();
            return live != null && live.isActiveAndEnabled;
        }

        public static SanctuaryChrome Mark(GameObject go, bool paint, SanctuaryHost host = SanctuaryHost.Overlay)
        {
            if (go == null) return null;
            if (!go.TryGetComponent(out SanctuaryChrome m)) m = go.AddComponent<SanctuaryChrome>();
            m.Paint = paint;
            m.Host = host;
            return m;
        }
    }

    /// <summary>Tags what a Sanctuary painter painted (tests read it: a row rebuilt later still wears the look).</summary>
    [DisallowMultipleComponent]
    public sealed class SanctuaryPainted : MonoBehaviour
    {
        public string Painter;

        public static void Tag(Component c, string painter)
        {
            if (c == null) return;
            if (!c.TryGetComponent(out SanctuaryPainted p)) p = c.gameObject.AddComponent<SanctuaryPainted>();
            p.Painter = painter;
        }
    }

    /// <summary>
    /// The v6.3 bodies of the shared HubChrome painters (BUILD_PLAN §6 P3, D5): the page's own sheet chrome (the ledger strip
    /// cel, the chapter chips, the gold CTA plate, the carnelian FOLD plate; sheets_&lt;layout&gt;.json recess / quit parts,
    /// 9-sliced at the canvas's stage scale) and its type (texts.recess / texts.quit specs). HubChrome keeps every name,
    /// signature and call order; it routes here only under a <see cref="SanctuaryChrome"/> marker. Callers' text sizes are their
    /// layout budget and are kept; the face, colour and tracking are the page's.
    /// </summary>
    public static class SanctuaryPaint
    {
        public static readonly Color Gold = new(235 / 255f, 198 / 255f, 106 / 255f, 1f);       // #EBC66A
        public static readonly Color LineGold = new(202 / 255f, 169 / 255f, 91 / 255f, 1f);    // #CAA95B (the frame's l1)
        public static readonly Color Cream = new(233 / 255f, 223 / 255f, 198 / 255f, 1f);      // #E9DFC6 (.tg.on)
        public static readonly Color Gyp = new(236 / 255f, 229 / 255f, 211 / 255f, 1f);        // #ECE5D3 (row values)
        public static readonly Color Label = new(201 / 255f, 191 / 255f, 166 / 255f, 1f);      // #C9BFA6 (row labels)
        public static readonly Color Muted = new(143 / 255f, 134 / 255f, 114 / 255f, 1f);      // #8F8672
        public static readonly Color Ink = new(26 / 255f, 18 / 255f, 5 / 255f, 1f);            // #1A1205 (on gold)
        public static readonly Color Carnelian = new(231 / 255f, 154 / 255f, 125 / 255f, 1f);  // #E79A7D (.quit .t)
        public static readonly Color DimInk = new(3 / 255f, 2 / 255f, 6 / 255f, 1f);           // #dim.flat rgb(3, 2, 6)

        public static bool On(Transform t) => SanctuaryChrome.Under(t);

        public static MenuLayoutKind Lay(Transform t)
        {
            var cv = t != null ? t.GetComponentInParent<Canvas>() : null;
            var root = cv != null ? cv.rootCanvas : null;
            var rt = root != null ? root.transform as RectTransform : null;
            if (rt != null && rt.rect.width > 1f && rt.rect.height > 1f)
                return rt.rect.width >= rt.rect.height ? MenuLayoutKind.Landscape : MenuLayoutKind.Portrait;
            return MenuAspect.Current;
        }

        static Dictionary<string, object> Json(Transform t) => SheetData.Root(Lay(t));

        /// <summary>A sheet part sprite by recess / quit chrome key (go, go_hover, ch_plain, ch_now, ch_done, strip, row_rule;
        /// quit: stay, stay_hover, fold, fold_hover) for this canvas's layout.</summary>
        public static Sprite Part(Transform t, string group, string key)
        {
            var j = Json(t);
            object part = group == "quit"
                ? RJ.D(RJ.D(RJ.D(j, "quit"), "parts"), key)
                : RJ.D(RJ.D(RJ.D(j, "recess"), "chrome"), key);
            return SheetData.Sprite(part);
        }

        /// <summary>A recorded text spec (texts.recess.bag / texts.quit) as a slot scaled by k (stage px → canvas units).</summary>
        public static MenuV6Layout.TextSlot Type(Transform t, string group, string cls, int nth = 0)
        {
            var j = Json(t);
            var list = group == "quit"
                ? RJ.L(RJ.D(j, "texts"), "quit")
                : RJ.L(RJ.D(RJ.D(RJ.D(j, "texts"), "recess"), "bag"), "texts");
            var s = SheetData.Slot(SheetData.Spec(list, cls, null, nth));
            return s == null ? null : Scale(s, SheetDraw.CanvasK(t));
        }

        public static MenuV6Layout.TextSlot Scale(MenuV6Layout.TextSlot s, float k)
        {
            if (s == null) return null;
            return new MenuV6Layout.TextSlot
            {
                Sel = s.Sel, Text = s.Text, Rect = s.Rect, Opacity = s.Opacity, Family = s.Family, Weight = s.Weight,
                Size = s.Size * k, Italic = s.Italic, Color = s.Color, LetterSpacing = s.LetterSpacing * k, Upper = s.Upper,
                Align = s.Align, LineHeight = s.LineHeight * k, Shadow = s.Shadow, ShadowColor = s.ShadowColor,
                ShadowOffset = s.ShadowOffset * k, ShadowBlur = s.ShadowBlur * k, NoWrap = s.NoWrap
            };
        }

        /// <summary>Restyle an existing Text with a slot's face, colour, tracking and drop, keeping its own rect (and its size
        /// when <paramref name="keepSize"/>: the caller's layout budget).</summary>
        public static void Style(Text t, MenuV6Layout.TextSlot slot, bool keepSize, Color? color = null, TextAnchor? align = null)
        {
            if (t == null || slot == null) return;
            var size = t.fontSize;
            var text = t.text;
            var rt = t.rectTransform;
            Vector2 aMin = rt.anchorMin, aMax = rt.anchorMax, oMin = rt.offsetMin, oMax = rt.offsetMax, piv = rt.pivot;
            var hOver = t.horizontalOverflow;
            var vOver = t.verticalOverflow;
            var keepAlign = t.alignment;
            var s = keepSize ? Scale(slot, size / Mathf.Max(1f, slot.Size)) : slot;
            TextFx.Apply(t, s, text);
            if (keepSize) t.fontSize = size;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = piv;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;
            t.horizontalOverflow = hOver;
            t.verticalOverflow = vOver;
            t.alignment = align ?? keepAlign;
            if (color.HasValue) t.color = color.Value;
        }

        // ------------------------------------------------------------------ the painters' bodies

        /// <summary>OverlayDim: the page's flat dim (#dim.flat rgb(3, 2, 6)); over the Stone at the sheets' rest opacity
        /// (.8 × .9 × .92, Linear-corrected), over the map at the menu's own budget (MenuChromePrefs.DimAlpha).</summary>
        public static void Dim(Image img, Transform parent)
        {
            if (img == null) return;
            var stone = SanctuaryChrome.OverStone(parent);
            var a = stone ? MenuV6Linear.Shade(.8 * .9 * .92) : MenuChromePrefs.DimAlpha;
            img.color = new Color(DimInk.r, DimInk.g, DimInk.b, a);
            SanctuaryPainted.Tag(img, "OverlayDim");
        }

        /// <summary>QuietFill: the ledger strip's cel (s-base, ink border, inner light; 9-sliced) at the caller's alpha; no slate
        /// shader, never L-ticks.</summary>
        public static void Fill(Image img, Color fill)
        {
            if (img == null) return;
            var slate = img.GetComponent<SolidVisionSlate>();
            if (slate != null) slate.enabled = false;
            img.material = null;
            var sp = Part(img.transform, "recess", "strip");
            if (sp != null)
            {
                img.sprite = sp;
                img.type = Image.Type.Sliced;
                SheetDraw.SliceAt(img, SheetDraw.CanvasK(img.transform));
                img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(fill.a));
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.color = new Color(30 / 255f, 26 / 255f, 35 / 255f, Mathf.Clamp01(fill.a));   // #1E1A23, the frame's fill
            }

            SanctuaryPainted.Tag(img, "QuietFill");
        }

        /// <summary>FilamentRim: the frame's own gold line (#CAA95B), same four edges.</summary>
        public static Color Rim() => new(LineGold.r, LineGold.g, LineGold.b, .92f);

        /// <summary>A plate under a quiet ArcaneButton face (the face stays the raycast target): rest and hover sprites.</summary>
        public static Image Plate(Button btn, Sprite rest, Sprite hover, float alpha = 1f)
        {
            if (btn == null || rest == null) return null;
            var go = new GameObject("Plate", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(btn.transform, false);
            go.transform.SetAsFirstSibling();
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.sprite = rest;
            img.type = rest.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, alpha);
            SheetDraw.SliceAt(img, SheetDraw.CanvasK(btn.transform));
            UiTheme.Stretch(img.rectTransform);
            if (hover != null) PlateHover.Attach(btn.gameObject, img, rest, hover);
            return img;
        }

        /// <summary>A capsule's v6.3 face: Gold = the CTA plate (go / go_hover) and its ink, Danger = the FOLD plate and
        /// carnelian, the rest = the slate chip (ch_plain, lit ch_now on hover; a gold row = ch_now) and cream.</summary>
        public static void Capsule(Button btn, MenuCommandButton.Kind kind, bool row = false, bool gold = false)
        {
            if (btn == null) return;
            var t = btn.transform;
            Sprite rest, hover;
            Color ink;
            var cta = kind == MenuCommandButton.Kind.Gold && !row;
            if (cta)
            {
                rest = Part(t, "recess", "go");
                hover = Part(t, "recess", "go_hover");
                ink = Ink;
            }
            else if (kind == MenuCommandButton.Kind.Danger)
            {
                rest = Part(t, "quit", "fold");
                hover = Part(t, "quit", "fold_hover");
                ink = Carnelian;
            }
            else
            {
                rest = Part(t, "recess", gold ? "ch_now" : btn.interactable ? "ch_plain" : "ch_done");
                hover = gold || !btn.interactable ? null : Part(t, "recess", "ch_now");
                ink = gold ? Gold : btn.interactable ? Cream : Muted;
            }

            Plate(btn, rest, hover);
            var title = t.Find("Title");
            var tt = title != null ? title.GetComponent<Text>() : null;
            var slot = cta ? Type(t, "recess", "go") : Type(t, "recess", "", 0);
            if (tt != null && slot != null)
            {
                Style(tt, slot, keepSize: true, color: ink);
                if (!cta && slot.Weight < 700) tt.fontStyle = FontStyle.Bold;
            }

            var blurb = t.Find("Blurb");
            var bt = blurb != null ? blurb.GetComponent<Text>() : null;
            var bslot = Type(t, "recess", "hint");
            if (bt != null && bslot != null) Style(bt, bslot, keepSize: true, color: cta ? Ink : Label);
            SanctuaryPainted.Tag(btn, row ? "ListRow" : "Capsule");
        }

        /// <summary>
        /// A command button with a quiet face (no tier material: the page's plate is drawn under it), built like
        /// MenuCommandButton.Create: the same GameObject (Image + Button + LayoutElement + ArcaneButton), the "Title" / "Blurb"
        /// children at the same frozen anchors, so every caller that re-places them by name still works.
        /// </summary>
        public static Button CreateQuiet(Transform parent, string title, System.Action onClick, MenuCommandButton.Kind kind,
            string blurb, bool centerTitle, int titleSize)
        {
            var hasBlurb = !string.IsNullOrEmpty(blurb);
            var ab = ArcaneButton.Create(parent, title, onClick, MenuCommandButton.TierFor(kind), blurb, null,
                hasBlurb ? 18 : Mathf.Max(12, titleSize), centerTitle, hieroglyphBorder: false, quiet: true);
            if (ab == null) return null;
            ab.gameObject.name = string.IsNullOrEmpty(title) ? "Cmd" : title;
            if (ab.Title != null)
            {
                ab.Title.alignment = centerTitle && !hasBlurb ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                var rt = ab.Title.rectTransform;
                if (hasBlurb) Anchor(rt, 0.06f, 0.48f, 0.96f, 0.92f);
                else if (centerTitle) Anchor(rt, 0.04f, 0.10f, 0.96f, 0.90f);
                else Anchor(rt, 0.06f, 0.14f, 0.96f, 0.86f);
                ab.Title.fontSize = Mathf.Max(12, titleSize);
            }

            if (ab.Blurb != null) Anchor(ab.Blurb.rectTransform, 0.06f, 0.08f, 0.96f, 0.46f);
            return ab.Button;
        }

        static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>HeaderBar's title / subtitle type (the sheets' own: texts.quit .h, texts.recess .sub).</summary>
        public static void HeaderType(Text title, Text sub)
        {
            if (title != null)
            {
                var s = Type(title.transform, "quit", "h");
                if (s != null)
                {
                    s.NoWrap = true;
                    Style(title, s, keepSize: false, align: TextAnchor.MiddleCenter);
                    title.horizontalOverflow = HorizontalWrapMode.Overflow;
                    TextFx.FitWidth(title, Mathf.Max(10f, title.rectTransform.rect.width), 12);
                }
            }

            if (sub != null)
            {
                var s = Type(sub.transform, "recess", "sub");
                if (s != null)
                {
                    Style(sub, s, keepSize: false, align: TextAnchor.MiddleCenter);
                    sub.horizontalOverflow = HorizontalWrapMode.Wrap;
                }
            }
        }
    }

    /// <summary>Swaps a sanctuary plate to its hover sprite while the pointer is over the control (the page's :hover).</summary>
    public sealed class PlateHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image _img;
        Sprite _rest, _hover;

        public static void Attach(GameObject go, Image img, Sprite rest, Sprite hover)
        {
            if (go == null) return;
            if (!go.TryGetComponent(out PlateHover h)) h = go.AddComponent<PlateHover>();
            h._img = img;
            h._rest = rest;
            h._hover = hover;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (_img != null && _hover != null) _img.sprite = _hover;
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (_img != null && _rest != null) _img.sprite = _rest;
        }

        void OnDisable()
        {
            if (_img != null && _rest != null) _img.sprite = _rest;
        }
    }
}
