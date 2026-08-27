using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Settings sheet — matches boot / deck glass chrome (backdrop, glass plate, gold edge).
    /// Menu size & transparency prefs apply live to deck + systems menus (AR + phone).
    /// </summary>
    public static class SettingsScreen
    {
        public static RectTransform Build(Transform parent, MenuShell shell, System.Action onClose)
        {
            FreeUiKit.EnsureLoaded();
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_auth.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_menu_void.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/ui/panel_boot_glass.png");

            var root = new GameObject("SettingsRoot", typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            root.SetParent(parent, false);
            FloatingPanel.Stretch(root);
            var dim = root.GetComponent<Image>();
            dim.sprite = UiFoundation.WhiteSprite();
            dim.color = new Color(0.02f, 0.03f, 0.05f, 0.45f);
            dim.raycastTarget = true;

            var panel = FloatingPanel.Create(root, "Settings", goldEdge: false);
            FloatingPanel.Place(panel, 0.06f, 0.08f, 0.94f, 0.92f);
            var pImg = panel.GetComponent<Image>();

            var body = panel;

            var emblem = ImagineAssets.EmblemSpiritEye();
            if (emblem != null)
            {
                var emb = new GameObject("Emblem", typeof(RectTransform), typeof(Image));
                emb.transform.SetParent(body, false);
                FloatingPanel.Place(emb.GetComponent<RectTransform>(), 0.78f, 0.88f, 0.94f, 0.98f);
                var e = emb.GetComponent<Image>();
                e.sprite = emblem;
                e.preserveAspect = true;
                e.raycastTarget = false;
                e.color = new Color(1f, 1f, 1f, 0.88f);
            }

            var title = L(body, "SETTINGS", 20, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
            FloatingPanel.Place(title.rectTransform, 0.05f, 0.90f, 0.52f, 0.98f);

            var close = GlassBtn(body, "DONE", true, () =>
            {
                FreeUiKit.PlayClick();
                if (root != null) UnityEngine.Object.Destroy(root.gameObject);
                onClose?.Invoke();
            });
            FloatingPanel.Place(close.GetComponent<RectTransform>(), 0.54f, 0.90f, 0.76f, 0.98f);

            float y = 0.86f;

            SectionHead(body, ref y, "MENU");
            CycleRow(body, ref y,
                () => "Size · " + MenuChromePrefs.SizeLabel,
                () => MenuChromePrefs.CycleSize());
            CycleRow(body, ref y,
                () => "Transparency · " + MenuChromePrefs.OpacityLabel,
                () =>
                {
                    MenuChromePrefs.CycleOpacity();
                    pImg.color = new Color(0.06f, 0.08f, 0.12f, 0.70f + MenuChromePrefs.PanelAlpha * 0.24f);
                });

            SectionHead(body, ref y, "DEVICE");
            Toggle(body, ref y, "AR Quality");
            Toggle(body, ref y, "Battery Saver", on => shell?.SetBatterySaver(on));
            Toggle(body, ref y, "Eye Tracking");
            Toggle(body, ref y, "Right-Arm Disk");
            Toggle(body, ref y, "High Contrast");

            SectionHead(body, ref y, "AUDIO");
            var vol = L(body, "Master Volume", 13, DuelystUi.TextMuted, TextAnchor.MiddleLeft);
            FloatingPanel.Place(vol.rectTransform, 0.06f, 0.04f, 0.94f, 0.10f);

            return root;
        }

        static void SectionHead(Transform body, ref float y, string text)
        {
            var t = L(body, text, 12, DuelystUi.Cyan, TextAnchor.MiddleLeft);
            FloatingPanel.Place(t.rectTransform, 0.06f, y - 0.028f, 0.40f, y);
            var rule = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            rule.transform.SetParent(body, false);
            FloatingPanel.Place(rule.GetComponent<RectTransform>(), 0.42f, y - 0.016f, 0.94f, y - 0.010f);
            var rImg = rule.GetComponent<Image>();
            rImg.sprite = UiFoundation.WhiteSprite();
            rImg.color = new Color(0.35f, 0.9f, 1f, 0.28f);
            rImg.raycastTarget = false;
            y -= 0.038f;
        }

        static void CycleRow(Transform panel, ref float y, System.Func<string> labelFn, System.Action onCycle)
        {
            var row = GlassRow(panel, y, 0.072f);
            y -= 0.082f;
            var t = L(row.transform, labelFn(), 14, Color.white, TextAnchor.MiddleLeft);
            FloatingPanel.Place(t.rectTransform, 0.05f, 0.12f, 0.96f, 0.88f);
            row.GetComponent<Button>().onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                onCycle?.Invoke();
                t.text = labelFn();
            });
        }

        static void Toggle(Transform panel, ref float y, string label, System.Action<bool> onChange = null)
        {
            var row = GlassRow(panel, y, 0.068f);
            y -= 0.078f;
            var t = L(row.transform, label, 14, Color.white, TextAnchor.MiddleLeft);
            FloatingPanel.Place(t.rectTransform, 0.05f, 0.12f, 0.82f, 0.88f);

            // ON/OFF pip
            var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image));
            pip.transform.SetParent(row.transform, false);
            FloatingPanel.Place(pip.GetComponent<RectTransform>(), 0.86f, 0.28f, 0.96f, 0.72f);
            var pImg = pip.GetComponent<Image>();
            pImg.sprite = UiFoundation.WhiteSprite();
            pImg.color = new Color(0.25f, 0.30f, 0.38f, 0.95f);
            pImg.raycastTarget = false;

            var on = false;
            row.GetComponent<Button>().onClick.AddListener(() =>
            {
                on = !on;
                t.color = on ? DuelystUi.Cyan : Color.white;
                pImg.color = on
                    ? new Color(0.25f, 0.85f, 0.95f, 0.95f)
                    : new Color(0.25f, 0.30f, 0.38f, 0.95f);
                FreeUiKit.PlayClick();
                onChange?.Invoke(on);
            });
        }

        static GameObject GlassRow(Transform panel, float yTop, float h)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(panel, false);
            FloatingPanel.Place(row.GetComponent<RectTransform>(), 0.05f, yTop - h, 0.95f, yTop);
            var img = row.GetComponent<Image>();
            // Prefer holo plate when available for row faces
            var rowPlate = ImagineAssets.HudChip() ?? ImagineAssets.PanelHolo();
            if (rowPlate != null)
            {
                img.sprite = rowPlate;
                img.type = rowPlate.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                img.color = new Color(1f, 1f, 1f, 0.72f);
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.color = new Color(0.06f, 0.10f, 0.16f, 0.84f);
            }

            // Left cyan accent bar
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(row.transform, false);
            FloatingPanel.Place(bar.GetComponent<RectTransform>(), 0f, 0.12f, 0.018f, 0.88f);
            var bImg = bar.GetComponent<Image>();
            bImg.sprite = UiFoundation.WhiteSprite();
            bImg.color = new Color(0.35f, 0.9f, 1f, 0.92f);
            bImg.raycastTarget = false;
            return row;
        }

        static Button GlassBtn(Transform parent, string label, bool gold, System.Action onClick)
        {
            var kind = gold ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary;
            return MenuCommandButton.Create(parent, label, onClick, kind, centerTitle: true);
        }

        static Text L(Transform parent, string text, int size, Color color, TextAnchor align)
        {
            var go = new GameObject("T", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            var font = WrldzType.Body() ?? UiFoundation.BuiltinFont();
            t.font = font;
            t.fontSize = Mathf.Max(16, WrldzType.Readable(size));
            t.fontStyle = FontStyle.Bold;
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 1f);
            o.effectDistance = new Vector2(2f, -2f);
            return t;
        }
    }
}
