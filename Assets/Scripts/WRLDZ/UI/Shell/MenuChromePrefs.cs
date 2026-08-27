using UnityEngine;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Player-tunable menu chrome shared by AR holo panels and non-AR phone sheets.
    /// Stored in PlayerPrefs; Settings screen cycles values. Deck / systems menus read live.
    /// </summary>
    public static class MenuChromePrefs
    {
        const string KeyOpacity = "WRLDZ_MenuOpacity";
        const string KeySize = "WRLDZ_MenuSize";

        /// <summary>How transparent the glass plate is.</summary>
        public enum OpacityLevel
        {
            /// <summary>~28% fill — strongest see-through (AR-friendly).</summary>
            Clear = 0,
            /// <summary>~42% fill — default balanced glass.</summary>
            Glass = 1,
            /// <summary>~72% fill — more solid for outdoor contrast.</summary>
            Solid = 2
        }

        /// <summary>How much of the screen the menu sheet covers.</summary>
        public enum SizeLevel
        {
            Compact = 0,
            Medium = 1,
            Large = 2
        }

        public static OpacityLevel Opacity
        {
            get => (OpacityLevel)Mathf.Clamp(PlayerPrefs.GetInt(KeyOpacity, (int)OpacityLevel.Glass), 0, 2);
            set
            {
                PlayerPrefs.SetInt(KeyOpacity, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static SizeLevel Size
        {
            get => (SizeLevel)Mathf.Clamp(PlayerPrefs.GetInt(KeySize, (int)SizeLevel.Compact), 0, 2);
            set
            {
                PlayerPrefs.SetInt(KeySize, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static string OpacityLabel => Opacity switch
        {
            OpacityLevel.Clear => "Clear",
            OpacityLevel.Solid => "Solid",
            _ => "Glass"
        };

        public static string SizeLabel => Size switch
        {
            SizeLevel.Medium => "Medium",
            SizeLevel.Large => "Large",
            _ => "Compact"
        };

        public static OpacityLevel CycleOpacity()
        {
            Opacity = (OpacityLevel)(((int)Opacity + 1) % 3);
            return Opacity;
        }

        public static SizeLevel CycleSize()
        {
            Size = (SizeLevel)(((int)Size + 1) % 3);
            return Size;
        }

        /// <summary>Main plate fill alpha (window glass).</summary>
        public static float PanelAlpha => Opacity switch
        {
            OpacityLevel.Clear => 0.28f,
            OpacityLevel.Solid => 0.78f,
            _ => 0.42f
        };

        /// <summary>Full-screen dim behind the sheet (low so world/AR stays visible).</summary>
        public static float DimAlpha => Opacity switch
        {
            OpacityLevel.Clear => 0.18f,
            OpacityLevel.Solid => 0.48f,
            _ => 0.28f
        };

        /// <summary>Inner content plate (slightly denser than outer edge).</summary>
        public static float InsetAlpha => Mathf.Clamp01(PanelAlpha + 0.10f);

        /// <summary>Gold/hairline edge alpha.</summary>
        public static float EdgeAlpha => Opacity switch
        {
            OpacityLevel.Clear => 0.22f,
            OpacityLevel.Solid => 0.45f,
            _ => 0.32f
        };

        /// <summary>List row / button face alpha on deck menus.</summary>
        public static float RowAlpha => Opacity switch
        {
            OpacityLevel.Clear => 0.32f,
            OpacityLevel.Solid => 0.72f,
            _ => 0.48f
        };

        public static Color PanelColor => new(0.05f, 0.07f, 0.11f, PanelAlpha);
        public static Color InsetColor => new(0.04f, 0.06f, 0.10f, InsetAlpha);
        public static Color DimColor => new(0.01f, 0.02f, 0.04f, DimAlpha);
        public static Color EdgeColor => new(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, EdgeAlpha);
        public static Color RowColor => new(0.08f, 0.10f, 0.14f, RowAlpha);
        public static Color RowGoldColor => new(0.28f, 0.22f, 0.08f, RowAlpha + 0.08f);
        public static Color FieldColor => new(0.06f, 0.08f, 0.12f, Mathf.Clamp01(RowAlpha + 0.06f));
        public static Color InspectColor => new(0.03f, 0.05f, 0.09f, Mathf.Clamp01(PanelAlpha + 0.12f));

        /// <summary>
        /// Window anchors for deck / systems sheets. AR is always a step more compact
        /// than phone so holos and the field stay in view.
        /// </summary>
        public static void GetWindowAnchors(UiPresentation presentation,
            out float x0, out float y0, out float x1, out float y1)
        {
            var ar = presentation == UiPresentation.ArDiskHolo
                     || presentation == UiPresentation.ArWorldPanel;
            var size = Size;

            // Base (non-AR) by size
            switch (size)
            {
                case SizeLevel.Large:
                    x0 = 0.05f; y0 = 0.08f; x1 = 0.95f; y1 = 0.92f;
                    break;
                case SizeLevel.Medium:
                    x0 = 0.10f; y0 = 0.14f; x1 = 0.90f; y1 = 0.86f;
                    break;
                default: // Compact — default, least obtrusive
                    x0 = 0.14f; y0 = 0.18f; x1 = 0.86f; y1 = 0.82f;
                    break;
            }

            // AR: shrink further so dual disks + midfield stay readable
            if (ar)
            {
                var insetX = size == SizeLevel.Large ? 0.06f : size == SizeLevel.Medium ? 0.05f : 0.04f;
                var insetY = size == SizeLevel.Large ? 0.06f : size == SizeLevel.Medium ? 0.05f : 0.04f;
                x0 += insetX;
                x1 -= insetX;
                y0 += insetY;
                y1 -= insetY;
            }
        }
    }
}
