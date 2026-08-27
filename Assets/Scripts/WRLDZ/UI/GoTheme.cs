using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Overworld + hub chrome — coordinated with Open Duelyst CCG kit (DuelystUi).
    /// Layout is GO-inspired; art language is one cyber-TCG family only.
    /// </summary>
    public static class GoTheme
    {
        public static readonly Color WhiteUi = DuelystUi.TextCream;
        public static readonly Color Ink = DuelystUi.TextCream;
        public static readonly Color InkSoft = DuelystUi.TextMuted;
        public static readonly Color LevelGold = DuelystUi.Gold;
        public static readonly Color MainCyan = DuelystUi.Cyan;
        public static readonly Color MapGrass = new(0.35f, 0.55f, 0.4f, 1f);

        public static void EnsureAssets()
        {
            FreeUiKit.EnsureLoaded();
            VendorKit.EnsureReady();
        }

        /// <summary>Main menu control art — Duelyst circle, never Kuriboh card art.</summary>
        public static Sprite MainOrb() =>
            ImagineAssets.IconMenu()
            ?? DuelystUi.BtnPrimary()
            ?? DuelystUi.BtnCircle()
            ?? DuelystUi.OrbRing();

        public static Sprite MainOrbGlow() =>
            DuelystUi.BtnCircleGlow();

        public static Sprite OrbItems() => DuelystUi.BtnCircle();
        public static Sprite OrbShop() => DuelystUi.BtnCircle();
        public static Sprite OrbProfile() =>
            ImagineAssets.LevelRing() ?? DuelystUi.OrbRing() ?? DuelystUi.BtnCircle();
        public static Sprite LevelRing() =>
            WrldzPresentation.LevelRing() ?? DuelystUi.OrbRing() ?? DuelystUi.BtnCircleGlow();
        public static Sprite NearbyPlate() =>
            ImagineAssets.HudChip() ?? DuelystUi.Bar() ?? DuelystUi.Panel();
        public static Sprite ToastPlate() =>
            ImagineAssets.HudChip() ?? DuelystUi.Bar() ?? DuelystUi.Panel();
        public static Sprite WeatherChip() => DuelystUi.RowChip() ?? DuelystUi.BtnSecondary();
        public static Sprite Compass() =>
            ImagineAssets.IconCompass() ?? DuelystUi.BtnCircle();
        public static Sprite AvatarDefault() =>
            StreamingSprite.Load("WRLDZ/Avatar/Kenney/skaterMaleA.png")
            ?? DuelystUi.OrbRing();

        public static Sprite SheetPanel() =>
            WrldzPresentation.PanelHolo() ?? DuelystUi.Panel();
        public static Sprite RowButtonSprite() =>
            WrldzPresentation.BtnPrimary() ?? DuelystUi.BtnPrimary();
        public static Sprite SecondaryButtonSprite() => DuelystUi.BtnSecondary();

        public static Sprite PinTear() =>
            ImagineAssets.PinTear() ?? WrldzPresentation.PinTear() ?? StreamingSprite.GoPinTear();
        public static Sprite PinArena() =>
            ImagineAssets.PinRaid() ?? WrldzPresentation.PinArena() ?? StreamingSprite.GoPinArena();
        public static Sprite PinAnchor() =>
            ImagineAssets.PinAnchor() ?? WrldzPresentation.PinPortal() ?? PinArena();
        public static Sprite PinTreasure() =>
            ImagineAssets.PinTreasure() ?? WrldzPresentation.PinTreasure() ?? PinArena();
        public static Sprite PinPortal() => ImagineAssets.PinPortal() ?? PinAnchor();
        public static Sprite PinRaid() => ImagineAssets.PinRaid() ?? PinArena();
        public static Sprite PinBazaar() => ImagineAssets.PinBazaar() ?? PinTreasure();
        public static Sprite PinTraining() => ImagineAssets.PinTraining() ?? PinArena();
        public static Sprite PinTournament() => ImagineAssets.PinTournament() ?? PinRaid();
        public static Sprite PinEvent() => ImagineAssets.PinEvent() ?? PinTournament();
        public static Sprite PinNpc() => ImagineAssets.PinNpc() ?? OrbProfile();
        public static Sprite PinStory() => ImagineAssets.PinStory() ?? PinPortal();
        public static Sprite PinPvp() => ImagineAssets.PinPvp() ?? PinEvent();

        public static Button CircleButton(Transform parent, string name, Sprite sprite, Color tint,
            float x0, float y0, float x1, float y1, Action onClick)
        {
            EnsureAssets();
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);

            var img = go.GetComponent<Image>();
            img.sprite = sprite ?? DuelystUi.BtnCircle() ?? UiFoundation.WhiteSprite();
            img.color = tint;
            img.preserveAspect = true;
            img.raycastTarget = true;
            img.type = Image.Type.Simple;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null)
                btn.onClick.AddListener(() =>
                {
                    FreeUiKit.PlayClick();
                    onClick();
                });
            return btn;
        }

        public static RectTransform WhiteChip(Transform parent, string name,
            float x0, float y0, float x1, float y1)
        {
            EnsureAssets();
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.sprite = NearbyPlate() ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = false;
            return rt;
        }

        public static RectTransform Sheet(Transform parent, string name,
            float x0, float y0, float x1, float y1)
        {
            EnsureAssets();
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            var plate = ImagineAssets.PanelHolo() ?? DuelystUi.Panel() ?? UiFoundation.WhiteSprite();
            img.sprite = plate;
            img.type = plate != null && plate.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.color = plate != null && plate != UiFoundation.WhiteSprite()
                ? Color.white
                : new Color(0.05f, 0.07f, 0.11f, 0.96f);
            img.raycastTarget = true;
            return rt;
        }

        public static Button RectAction(Transform parent, string name, string label,
            float x0, float y0, float x1, float y1, bool primary, Action onClick)
        {
            EnsureAssets();
            var kind = primary ? Shell.MenuCommandButton.Kind.Primary : Shell.MenuCommandButton.Kind.Secondary;
            var btn = Shell.MenuCommandButton.Create(parent, label, onClick, kind);
            btn.name = name;
            Place(btn.GetComponent<RectTransform>(), x0, y0, x1, y1);
            return btn;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, bool bold = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: false, heavyOutline: true);
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (bold) t.fontStyle = FontStyle.Bold;
            Stretch(go.GetComponent<RectTransform>());
            return t;
        }

        public static void SetCenterIcon(Transform button, Sprite icon, float scale = 0.4f)
        {
            if (button == null || icon == null) return;
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(button, false);
            var pad = (1f - scale) * 0.5f;
            Place(go.GetComponent<RectTransform>(), pad, pad, 1f - pad, 1f - pad);
            var img = go.GetComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = Color.white;
        }

        public static void IconLabel(Transform parent, string glyph, Color color, float scale = 0.5f)
        {
            var t = Label(parent, "Glyph", glyph, 22, color);
            var pad = (1f - scale) * 0.5f;
            Place(t.rectTransform, pad, pad, 1f - pad, 1f - pad);
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
