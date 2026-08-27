using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Coordinated onboarding chrome — Open Duelyst CCG kit only.
    /// Dark slate + cyan/gold hex buttons. No mixed Kenney / random gens.
    /// </summary>
    public static class FlowChrome
    {
        public static Color Void => DuelystUi.BgDeep;
        public static Color Gold => DuelystUi.Gold;
        public static Color GoldHot => DuelystUi.Gold;
        public static Color Cyan => DuelystUi.Cyan;
        public static Color Cream => DuelystUi.TextCream;
        public static Color Soft => DuelystUi.TextMuted;
        public static Color Panel => DuelystUi.BgPanel;
        public static Color Danger => DuelystUi.Danger;
        public static Color Ok => DuelystUi.Green;
        public static Color Magenta => new(1f, 0.35f, 0.75f, 1f);

        public static void ApplyPortrait()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
        }

        public static RectTransform CreateCanvas(string name, int sort = 40)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sort;
            UiTheme.ApplyPortraitPhone(go.GetComponent<CanvasScaler>());
            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// Boot / onboarding atmosphere — Egyptian night ages (varies by canvas name).
        /// Falls back to PrimordialNight when host name is unknown.
        /// </summary>
        public static RectTransform RootAtmosphere(Transform canvas)
        {
            var root = MakeImage(canvas, "Root", null, new Color(0f, 0f, 0f, 0.05f));
            Stretch(root);
            root.GetComponent<Image>().raycastTarget = true;

            var age = MenuAge.PrimordialNight;
            if (canvas != null)
            {
                var n = canvas.name ?? "";
                if (n.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Auth", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Login", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    age = MenuAge.OldKingdom;
                else if (n.IndexOf("Kuriboh", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         n.IndexOf("Gift", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         n.IndexOf("Prologue", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    age = MenuAge.ScrollAges;
                else if (n.IndexOf("Terms", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         n.IndexOf("Dob", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    age = MenuAge.Courtyard;
                else
                    age = EgyptianAgesAtmosphere.AgeForHost(n);
            }

            // Attach under canvas so it sits behind root content siblings
            EgyptianAgesAtmosphere.Attach(canvas, age, showAgeCaption: false);
            // Ensure root content is above atmosphere
            root.SetAsLastSibling();
            return root;
        }

        /// <summary>Full-bleed Imagine scene art behind boot UI (splash/title/auth).</summary>
        public static RectTransform SetBootBackdrop(Transform host, Sprite art)
        {
            if (host == null) return null;
            var existing = host.Find("BootBackdrop");
            if (existing != null)
                Object.Destroy(existing.gameObject);

            var go = MakeImage(host, "BootBackdrop", art, Color.white);
            Stretch(go);
            var img = go.GetComponent<Image>();
            img.preserveAspect = false;
            img.raycastTarget = false;
            img.color = art != null ? Color.white : new Color(0.04f, 0.05f, 0.1f, 1f);
            if (art == null)
                img.sprite = UiFoundation.WhiteSprite();
            // Soft dark gradient scrim at bottom so white labels stay readable
            var scrim = MakeImage(go, "BottomScrim", null, new Color(0.02f, 0.03f, 0.06f, 0.55f));
            Place(scrim, 0f, 0f, 1f, 0.42f);
            scrim.GetComponent<Image>().raycastTarget = false;
            go.SetAsFirstSibling();
            return go;
        }

        /// <summary>Quest-frame panel with neon outer rim (cyber × MD).</summary>
        public static RectTransform PanelBox(Transform parent, string name)
        {
            // Outer neon rim (cyan glow edge)
            var shell = MakeImage(parent, name, null, new Color(0.12f, 0.70f, 0.95f, 0.45f));
            shell.GetComponent<Image>().raycastTarget = true;
            var plate = ImagineAssets.PanelBootGlass()
                        ?? ImagineAssets.PanelModal()
                        ?? DuelystUi.Panel();
            var inner = MakeImage(shell, "Inner", plate, Color.white);
            Stretch(inner, 3, 3, 3, 3);
            var img = inner.GetComponent<Image>();
            if (img.sprite != null)
            {
                img.type = img.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                // Keep glass readable without crushing art
                img.color = new Color(1f, 1f, 1f, 0.92f);
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = DuelystUi.BgPanel;
            }

            img.raycastTarget = true;
            // Gold inner highlight line at top (Master Duel chrome)
            var goldTop = MakeImage(inner, "GoldTop", null, new Color(Gold.r, Gold.g, Gold.b, 0.65f));
            Place(goldTop, 0.04f, 0.97f, 0.96f, 0.995f);
            return shell;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, bool display = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            FreeUiKit.EnsureLoaded();
            WrldzType.Style(t, size, display, heavyOutline: true);
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static InputField Field(Transform parent, string name, string placeholder, bool password = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = DuelystUi.InputFieldSpr() ?? DuelystUi.Bar() ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.color = Color.white;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>();
            WrldzType.Style(text, 20, display: false, heavyOutline: false);
            text.color = Cream;
            text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft;
            Stretch(textGo.GetComponent<RectTransform>(), 18, 10, 18, 10);

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            var ph = phGo.GetComponent<Text>();
            WrldzType.Style(ph, 18, display: false);
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(Soft.r, Soft.g, Soft.b, 0.55f);
            ph.text = placeholder;
            ph.alignment = TextAnchor.MiddleLeft;
            Stretch(phGo.GetComponent<RectTransform>(), 18, 10, 18, 10);

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = password
                ? InputField.ContentType.Password
                : InputField.ContentType.Standard;
            input.caretColor = Cyan;
            input.selectionColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            return input;
        }

        /// <summary>
        /// Kind: 0 primary cyan, 1 secondary, 2 confirm green, 3 gold, 4 cancel.
        /// </summary>
        public static Button Btn(Transform parent, string name, string label, Color tint, System.Action onClick)
        {
            var avg = (tint.r + tint.g + tint.b) / 3f;
            Shell.MenuCommandButton.Kind kind;
            if (tint.r > 0.3f && tint.g < 0.25f)
                kind = Shell.MenuCommandButton.Kind.Danger;
            else if (tint.r > 0.4f && tint.g > 0.3f && tint.b < 0.25f)
                kind = Shell.MenuCommandButton.Kind.Gold;
            else if (avg < 0.28f)
                kind = Shell.MenuCommandButton.Kind.Secondary;
            else
                kind = Shell.MenuCommandButton.Kind.Primary;

            var btn = Shell.MenuCommandButton.Create(parent, label, onClick, kind);
            btn.name = name;
            return btn;
        }

        public static void StepIndicator(Transform parent, int current, int total = 6)
        {
            var bar = new GameObject("Steps", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            Place(bar.GetComponent<RectTransform>(), 0.18f, 0.02f, 0.82f, 0.045f);
            for (var i = 0; i < total; i++)
            {
                var pip = MakeImage(bar.transform, "P" + i, null, Color.white);
                var x0 = i / (float)total + 0.03f;
                var x1 = (i + 1) / (float)total - 0.03f;
                Place(pip, x0, 0.2f, x1, 0.8f);
                pip.GetComponent<Image>().color = i <= current
                    ? new Color(Cyan.r, Cyan.g, Cyan.b, 1f)
                    : new Color(1f, 1f, 1f, 0.15f);
            }
        }

        public static Dropdown Dropdown(
            Transform parent,
            string name,
            System.Collections.Generic.IList<string> options,
            int defaultIndex = 0)
        {
            FreeUiKit.EnsureLoaded();
            var font = FreeUiKit.BodyFont() ?? UiFoundation.BuiltinFont();

            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
            root.transform.SetParent(parent, false);
            var rootImg = root.GetComponent<Image>();
            rootImg.sprite = DuelystUi.Bar() ?? UiFoundation.WhiteSprite();
            rootImg.type = rootImg.sprite != null && rootImg.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            rootImg.color = Color.white;
            rootImg.raycastTarget = true;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(root.transform, false);
            var caption = labelGo.GetComponent<Text>();
            caption.font = font;
            caption.fontSize = WrldzType.Readable(20);
            caption.fontStyle = FontStyle.Bold;
            caption.color = Cream;
            caption.alignment = TextAnchor.MiddleLeft;
            caption.raycastTarget = false;
            WrldzType.ApplyOutline(caption);
            Stretch(labelGo.GetComponent<RectTransform>(), 14, 6, 36, 6);

            var template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            template.transform.SetParent(root.transform, false);
            var templateRt = template.GetComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0f, 0f);
            templateRt.anchorMax = new Vector2(1f, 0f);
            templateRt.pivot = new Vector2(0.5f, 1f);
            templateRt.anchoredPosition = new Vector2(0f, 2f);
            templateRt.sizeDelta = new Vector2(0f, 200f);
            var templateImg = template.GetComponent<Image>();
            templateImg.sprite = DuelystUi.Panel() ?? UiFoundation.WhiteSprite();
            templateImg.type = Image.Type.Sliced;
            templateImg.color = Color.white;
            templateImg.raycastTarget = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 40f);

            var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(Image));
            item.transform.SetParent(content.transform, false);
            var itemRt = item.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f);
            itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 40f);
            item.GetComponent<Image>().color = new Color(1, 1, 1, 0.02f);

            var itemLabel = new GameObject("Item Label", typeof(RectTransform), typeof(Text));
            itemLabel.transform.SetParent(item.transform, false);
            Stretch(itemLabel.GetComponent<RectTransform>(), 12, 4, 12, 4);
            var il = itemLabel.GetComponent<Text>();
            il.font = font;
            il.fontSize = WrldzType.Readable(18);
            il.color = Cream;
            il.alignment = TextAnchor.MiddleLeft;

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = item.GetComponent<Image>();
            toggle.isOn = true;

            var scroll = template.GetComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var dd = root.GetComponent<Dropdown>();
            dd.captionText = caption;
            dd.itemText = il;
            dd.template = templateRt;
            dd.ClearOptions();
            var opts = new System.Collections.Generic.List<Dropdown.OptionData>();
            if (options != null)
                foreach (var o in options)
                    opts.Add(new Dropdown.OptionData(o));
            dd.AddOptions(opts);
            dd.value = Mathf.Clamp(defaultIndex, 0, Mathf.Max(0, opts.Count - 1));
            dd.RefreshShownValue();
            template.SetActive(false);
            return dd;
        }

        public static RectTransform MakeImage(Transform parent, string name, Sprite sp, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp ?? UiFoundation.WhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Place(Component c, float x0, float y0, float x1, float y1)
        {
            if (c != null) Place(c.GetComponent<RectTransform>(), x0, y0, x1, y1);
        }
    }
}
