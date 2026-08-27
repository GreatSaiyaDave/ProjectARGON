using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// WRLDZ typography — Master Duel clarity + cyberpunk punch.
    /// Display: Bangers / comic shout · Body: Exo 2 Bold (futuristic UI).
    /// Outlines are heavy so text stays readable on neon AR backgrounds.
    /// </summary>
    public static class WrldzType
    {
        /// <summary>
        /// Global readability boost (S23 outdoor / busy AR stage / Desktop Lab).
        /// Bumped so menu titles, hub rows, and body copy are clearly legible.
        /// </summary>
        public const float Scale = 1.78f;

        /// <summary>Minimum body size on phone (design px before canvas scaler).</summary>
        public const int MinBody = 20;

        /// <summary>Minimum for display / title faces.</summary>
        public const int MinDisplay = 24;

        public static Font Display()
        {
            FreeUiKit.EnsureLoaded();
            return FreeUiKit.DisplayFont() ?? UiFoundation.BuiltinFont();
        }

        public static Font Body()
        {
            FreeUiKit.EnsureLoaded();
            return FreeUiKit.BodyFont() ?? Display();
        }

        public static int Readable(int designSize, bool display = false)
        {
            var s = designSize <= 0 ? 18 : designSize;
            // Lift micro / compact design sizes into readable range first
            if (s <= 10) s = 16;
            else if (s <= 12) s = 18;
            else if (s <= 14) s = 20;
            else if (s <= 16) s = 22;
            else if (s <= 18) s = 24;

            var scaled = Mathf.RoundToInt(s * Scale);
            if (display)
            {
                scaled = Mathf.Max(scaled, Mathf.RoundToInt(s * 1.85f));
                return Mathf.Max(MinDisplay, scaled);
            }

            return Mathf.Max(MinBody, scaled);
        }

        public static void Style(Text t, int designSize, bool display = false, bool heavyOutline = false)
        {
            if (t == null) return;
            t.font = display ? Display() : Body();
            t.fontSize = Readable(designSize, display);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = false;
            t.color = DuelystUi.TextCream;
            ApplyOutline(t, heavyOutline || display);
        }

        /// <summary>
        /// High-contrast label for menu / hub buttons (pure white + thick black outline).
        /// Prefer this over Style() on busy button plates.
        /// </summary>
        public static void StyleButtonLabel(Text t, int designSize = 18, bool display = false)
        {
            if (t == null) return;
            t.font = display ? Display() : Body();
            t.fontSize = Readable(designSize, display);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = false;
            t.color = Color.white;
            ApplyOutline(t, heavy: true, buttonContrast: true);
        }

        public static void ApplyOutline(Text t, bool heavy = false, bool buttonContrast = false)
        {
            if (t == null) return;
            var o = t.GetComponent<Outline>();
            if (o == null) o = t.gameObject.AddComponent<Outline>();
            // Near-black outline for MD-style crisp glyphs on neon / dunes
            o.effectColor = buttonContrast
                ? new Color(0f, 0f, 0f, 1f)
                : new Color(0f, 0f, 0.02f, 0.96f);
            o.effectDistance = buttonContrast
                ? new Vector2(3.4f, -3.4f)
                : heavy
                    ? new Vector2(2.8f, -2.8f)
                    : new Vector2(2.2f, -2.2f);

            Shadow soft = null;
            foreach (var s in t.GetComponents<Shadow>())
            {
                if (s is Outline) continue;
                soft = s;
                break;
            }

            if (soft == null)
                soft = t.gameObject.AddComponent<Shadow>();
            // Button labels: deep drop shadow; body: soft cyan cyber glow
            if (buttonContrast)
            {
                soft.effectColor = new Color(0f, 0f, 0f, 0.85f);
                soft.effectDistance = new Vector2(0f, -3.0f);
            }
            else
            {
                soft.effectColor = new Color(0.15f, 0.75f, 1f, 0.35f);
                soft.effectDistance = new Vector2(0f, -2.0f);
            }
        }

        /// <summary>Gold title treatment (phase / win banners).</summary>
        public static void StyleGoldTitle(Text t, int designSize = 18)
        {
            Style(t, designSize, display: true, heavyOutline: true);
            if (t != null) t.color = DuelystUi.GoldHot;
        }

        /// <summary>Cyan accent label (hints, YOU labels).</summary>
        public static void StyleCyan(Text t, int designSize = 14)
        {
            Style(t, designSize, display: false, heavyOutline: true);
            if (t != null) t.color = DuelystUi.Cyan;
        }
    }
}
