using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Spirit Dueler portrait for map token, HUD orb, and customizer.
    /// Face orb: HQ anime portraits. Full-body: paper-doll clothing layers
    /// (body → shirt → bottoms → hands → shoes → look → facial → hat).
    /// </summary>
    public class AvatarPortraitView : MonoBehaviour
    {
        Image _bg, _portrait, _body, _outfit, _eyes, _hair, _acc, _frame;
        Image _shoes, _bottoms, _shirt, _hands, _look, _facial, _hat;
        AvatarAppearance _current;
        bool _fullBody;
        bool _hideBg;
        bool _badgeCrop;

        public static AvatarPortraitView Create(Transform parent, float x0, float y0, float x1, float y1,
            bool showFrame = true)
        {
            var go = new GameObject("AvatarPortrait", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var view = go.AddComponent<AvatarPortraitView>();
            view.BuildLayers(showFrame);
            return view;
        }

        public static AvatarPortraitView CreateFill(Transform parent, bool showFrame = false)
        {
            var go = new GameObject("AvatarPortrait", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            GoTheme.Stretch(go.GetComponent<RectTransform>());
            var view = go.AddComponent<AvatarPortraitView>();
            view.BuildLayers(showFrame);
            return view;
        }

        public static AvatarPortraitView CreateFullBody(Transform parent, float x0, float y0, float x1, float y1,
            bool hideBackground = false, bool badgeCrop = false)
        {
            var go = new GameObject("AvatarFullBody", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var view = go.AddComponent<AvatarPortraitView>();
            view._fullBody = true;
            view._hideBg = hideBackground;
            view._badgeCrop = badgeCrop;
            view.BuildFullBodyLayers();
            return view;
        }

        public static AvatarPortraitView CreateFullBodyFill(Transform parent,
            bool hideBackground = false, bool badgeCrop = false)
        {
            var go = new GameObject("AvatarFullBody", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            GoTheme.Stretch(go.GetComponent<RectTransform>());
            var view = go.AddComponent<AvatarPortraitView>();
            view._fullBody = true;
            view._hideBg = hideBackground;
            view._badgeCrop = badgeCrop;
            view.BuildFullBodyLayers();
            return view;
        }

        void BuildLayers(bool showFrame)
        {
            _bg = MakeLayer("Bg", 0);
            _bg.sprite = GoTheme.OrbProfile() ?? UiFoundation.WhiteSprite();
            _bg.color = new Color(0.12f, 0.14f, 0.20f, 1f);
            _bg.preserveAspect = true;

            // Full HQ face (primary path)
            _portrait = MakeLayer("Portrait", 1);
            _portrait.enabled = false;

            // Legacy layered stack (fallback only)
            _body = MakeLayer("Body", 2);
            _outfit = MakeLayer("Outfit", 3);
            _eyes = MakeLayer("Eyes", 4);
            _hair = MakeLayer("Hair", 5);
            _acc = MakeLayer("Acc", 6);
            if (showFrame)
            {
                _frame = MakeLayer("Frame", 7);
                _frame.sprite = StreamingSprite.Load("WRLDZ/Avatar/frame_portrait.png")
                                ?? UiFoundation.WhiteSprite();
                _frame.color = Color.white;
                _frame.preserveAspect = true;
            }
        }

        void BuildFullBodyLayers()
        {
            _bg = MakeLayer("Bg", 0);
            _bg.sprite = UiFoundation.WhiteSprite();
            _bg.preserveAspect = false;
            if (_hideBg)
            {
                _bg.enabled = false;
                _bg.color = Color.clear;
                _bg.raycastTarget = false;
            }
            else
                _bg.color = new Color(0.07f, 0.09f, 0.14f, 0.35f);

            // Paper-doll back → front. Hat/facial must be accessory-only art
            // (transparent face). A full mannequin head in Hat covers Look
            // and erases the portrait. Bottoms over shirt so pants overlap a
            // long coat's opening. Hands over bottoms, so glove sprites must
            // not include shorts. Look under facial/hat so cap sits on hair.
            _body = MakeLayer("Body", 1);
            _shirt = MakeLayer("Shirt", 2);
            _bottoms = MakeLayer("Bottoms", 3);
            _hands = MakeLayer("Hands", 4);
            _shoes = MakeLayer("Shoes", 5);
            _look = MakeLayer("Look", 6);
            _facial = MakeLayer("Facial", 7);
            _hat = MakeLayer("Hat", 8);
            ApplyBodyCrop();
        }

        void ApplyBodyCrop()
        {
            // Badge: crop calves so hat / shirt fill a circular token.
            // Standing: slight overscan so the figure isn't letterboxed.
            Vector2 min, max;
            if (_badgeCrop)
            {
                min = new Vector2(-0.06f, -0.58f);
                max = new Vector2(1.06f, 1.04f);
            }
            else
            {
                min = new Vector2(0.02f, -0.02f);
                max = new Vector2(0.98f, 1.00f);
            }

            void Crop(Image img)
            {
                if (img == null) return;
                var rt = img.rectTransform;
                rt.anchorMin = min;
                rt.anchorMax = max;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            Crop(_body);
            Crop(_shoes);
            Crop(_bottoms);
            Crop(_shirt);
            Crop(_hands);
            Crop(_look);
            Crop(_facial);
            Crop(_hat);
        }

        Image MakeLayer(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetSiblingIndex(order);
            GoTheme.Stretch(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.type = Image.Type.Simple;
            return img;
        }

        public void Apply(AvatarAppearance appearance)
        {
            if (appearance == null) appearance = AvatarAppearance.Default();
            appearance.ClampToCatalog();
            _current = appearance.Clone();
            if (_fullBody)
            {
                ApplyFullBody(_current);
                return;
            }

            var accent = Parse(appearance.accentHex, GoTheme.MainCyan);
            if (_bg != null)
                _bg.color = Color.Lerp(new Color(0.10f, 0.12f, 0.18f), accent, 0.22f);

            // Prefer Imagine anime portrait
            var face = StreamingSprite.Load(appearance.PortraitStreamingPath);
            if (face != null)
            {
                SetPortraitMode(true);
                if (_portrait != null)
                {
                    _portrait.enabled = true;
                    _portrait.sprite = face;
                    _portrait.color = Color.white;
                    _portrait.preserveAspect = true;
                }

                if (_frame != null)
                    _frame.color = Color.Lerp(Color.white, accent, 0.30f);
                return;
            }

            // Legacy layered fallback
            SetPortraitMode(false);
            var skin = Parse(appearance.skinHex, new Color(0.9f, 0.78f, 0.66f));
            var hairC = Parse(appearance.hairHex, new Color(0.16f, 0.16f, 0.2f));
            var outfitT = Parse(appearance.outfitTintHex, Color.white);

            SetLayer(_body, $"WRLDZ/Avatar/body_{appearance.bodyIndex}.png", skin);
            SetLayer(_outfit, $"WRLDZ/Avatar/outfit_{appearance.outfitIndex}.png", outfitT);
            SetLayer(_eyes, $"WRLDZ/Avatar/eyes_{appearance.eyesIndex}.png", Color.white);
            SetLayer(_hair, $"WRLDZ/Avatar/hair_{appearance.hairIndex}.png", hairC);
            if (appearance.accessoryIndex <= 0)
            {
                if (_acc != null) _acc.enabled = false;
            }
            else
                SetLayer(_acc, $"WRLDZ/Avatar/acc_{appearance.accessoryIndex}.png", Color.white);

            if (_frame != null)
                _frame.color = Color.Lerp(Color.white, accent, 0.35f);
        }

        void SetPortraitMode(bool hq)
        {
            if (_portrait != null) _portrait.enabled = hq;
            // Hide stacked parts when HQ face is active
            void En(Image img, bool on)
            {
                if (img != null) img.enabled = on;
            }

            En(_body, !hq);
            En(_outfit, !hq);
            En(_eyes, !hq);
            En(_hair, !hq);
            En(_acc, !hq);
        }

        public AvatarAppearance Current => _current?.Clone() ?? AvatarAppearance.Default();

        void ApplyFullBody(AvatarAppearance appearance)
        {
            var accent = Parse(appearance.accentHex, GoTheme.MainCyan);
            if (_bg != null && !_hideBg)
                _bg.color = Color.Lerp(new Color(0.07f, 0.09f, 0.14f, 0.40f), accent, 0.16f);

            var skin = Parse(appearance.skinHex, new Color(0.9f, 0.78f, 0.66f));
            SetLayer(_body, ClothingCatalog.BodyArtPath, Color.white);
            if (_body != null && _body.sprite == UiFoundation.WhiteSprite())
                _body.color = new Color(skin.r, skin.g, skin.b, 0.35f);

            ApplyClothing(_bottoms, appearance.bottomsId);
            ApplyClothing(_shirt, appearance.shirtId);
            ApplyClothing(_hands, appearance.handsId);
            ApplyClothing(_shoes, appearance.shoesId);

            SetLayer(_look, appearance.LookStreamingPath, Color.white);

            ApplyClothing(_facial, appearance.facialId);
            ApplyClothing(_hat, appearance.hatId);
        }

        void ApplyClothing(Image img, string id)
        {
            if (img == null) return;
            var item = ClothingCatalog.Get(id);
            if (item == null || item.EmptySlot)
            {
                img.enabled = false;
                return;
            }

            var path = item.ArtPath;
            var spr = string.IsNullOrEmpty(path) ? null : StreamingSprite.Load(path);
            img.enabled = true;
            img.preserveAspect = true;
            if (spr != null)
            {
                img.sprite = spr;
                img.color = Color.white;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                var c = item.Fallback;
                img.color = new Color(c.r, c.g, c.b, 0.42f);
            }
        }

        void SetLayer(Image img, string path, Color tint)
        {
            if (img == null) return;
            var spr = StreamingSprite.Load(path);
            img.enabled = true;
            img.sprite = spr ?? UiFoundation.WhiteSprite();
            img.color = spr != null ? tint : new Color(tint.r, tint.g, tint.b, 0.35f);
        }

        public static Color Parse(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c))
                return c;
            return fallback;
        }
    }
}
