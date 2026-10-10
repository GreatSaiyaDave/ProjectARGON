using UnityEngine;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Visual tier of an Eldritch slate / button. Maps to (edge, line, fill) via
    /// <see cref="EldritchPalette"/>. Primary=gold on obsidian · Secondary=cyan on lapis ·
    /// Tertiary=muted cyan on thin obsidian · Eldritch=violet/magenta on abyss · Danger=blood.
    /// </summary>
    // contract note: §2 introduces the enum with the palette and §4 lists it with the components;
    // it lives here so the palette helpers below compile standalone. Single definition project-wide.
    public enum EldritchTier
    {
        Primary,
        Secondary,
        Tertiary,
        Eldritch,
        Danger
    }

    /// <summary>
    /// Eldritch Solid Vision palette + font tokens (ELDRITCH_CONTRACT §2).
    /// Extends <c>DuelystUi</c> / <c>WrldzTheme</c>; never replaces them.
    /// </summary>
    public static class EldritchPalette
    {
        // ── Base tokens ─────────────────────────────────────────────
        public static readonly Color Void = Hex(0x05070F);
        public static readonly Color Abyss = Hex(0x0B0A1E);
        public static readonly Color Obsidian = Hex(0x0E101C);
        public static readonly Color LapisDeep = Hex(0x142A66);
        public static readonly Color Lapis = Hex(0x1E3A8A);
        public static readonly Color SandstoneDark = Hex(0x7A5A34);
        public static readonly Color Sandstone = Hex(0xC9A36B);
        public static readonly Color MillenniumGold = Hex(0xFFD647);
        public static readonly Color GoldHot = Hex(0xFFE86B);
        public static readonly Color SolidVisionCyan = Hex(0x33EBFF);
        public static readonly Color CyanDeep = Hex(0x0C6B86);
        public static readonly Color EldritchViolet = Hex(0x7A2BFF);
        public static readonly Color AbyssalMagenta = Hex(0xC44BFF);
        public static readonly Color IchorGreen = Hex(0x3CFF8C);
        public static readonly Color BloodDanger = Hex(0xFF4455);
        public static readonly Color TextCream = Hex(0xFAF8F4);
        public static readonly Color TextMuted = Hex(0xB8CEE8);

        /// <summary>0xRRGGBB → opaque Color (sRGB values; Unity converts under Linear space).</summary>
        public static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        /// <summary>Slate fill alpha follows the user's chrome opacity preference (0.58 / 0.82 / 0.94).</summary>
        public static float PanelAlpha
        {
            get
            {
                try { return Mathf.Clamp(MenuChromePrefs.PanelAlpha, 0.5f, 0.96f); }
                catch { return 0.88f; }   // PlayerPrefs unavailable (headless import)
            }
        }

        // ── Tier helpers (edge, line, fill) ─────────────────────────

        /// <summary>Holographic slate edge glow color.</summary>
        public static Color EdgeFor(EldritchTier tier)
        {
            switch (tier)
            {
                // Sandstone, not cyan: these tiles are carved lapis with gold inlay, and cyan
                // edges made them read as the same Battle City chrome the overhaul replaces.
                case EldritchTier.Secondary: return Sandstone;
                case EldritchTier.Tertiary: return WithAlpha(SolidVisionCyan, 0.55f);
                case EldritchTier.Eldritch: return EldritchViolet;
                case EldritchTier.Danger: return BloodDanger;
                default: return MillenniumGold;
            }
        }

        /// <summary>Energy line head color (ArcaneEnergyFlow <c>_LineColor</c>).</summary>
        public static Color LineFor(EldritchTier tier)
        {
            switch (tier)
            {
                case EldritchTier.Secondary: return MillenniumGold;
                case EldritchTier.Tertiary: return WithAlpha(SolidVisionCyan, 0.62f);
                case EldritchTier.Eldritch: return AbyssalMagenta;
                case EldritchTier.Danger: return BloodDanger;
                default: return MillenniumGold;
            }
        }

        /// <summary>Energy line tail color (ArcaneEnergyFlow <c>_LineColor2</c>).</summary>
        public static Color Line2For(EldritchTier tier)
        {
            switch (tier)
            {
                case EldritchTier.Secondary: return SandstoneDark;
                case EldritchTier.Tertiary: return WithAlpha(CyanDeep, 0.6f);
                case EldritchTier.Eldritch: return EldritchViolet;
                case EldritchTier.Danger: return WithAlpha(GoldHot, 0.9f);   // blood → ember tail
                default: return SandstoneDark;
            }
        }

        /// <summary>Slate / button fill, alpha from <see cref="PanelAlpha"/>.</summary>
        public static Color FillFor(EldritchTier tier)
        {
            var a = PanelAlpha;
            switch (tier)
            {
                // Lapis proper, not near-black navy — the stone has to be legible as stone.
                case EldritchTier.Secondary: return WithAlpha(Lapis, a);
                case EldritchTier.Tertiary: return WithAlpha(Obsidian, a - 0.22f);
                case EldritchTier.Eldritch: return WithAlpha(Abyss, a);
                case EldritchTier.Danger: return WithAlpha(Obsidian, a);
                default: return WithAlpha(Obsidian, a);
            }
        }

        /// <summary>Scanline tint per tier (cyan holo by default; accent tiers keep their hue).</summary>
        public static Color ScanFor(EldritchTier tier)
        {
            switch (tier)
            {
                // Gold scanlines on the stone tiles read as light raking across carving.
                case EldritchTier.Secondary: return MillenniumGold;
                case EldritchTier.Eldritch: return EldritchViolet;
                case EldritchTier.Danger: return BloodDanger;
                default: return SolidVisionCyan;
            }
        }

        /// <summary>Lovecraft vein tint per tier.</summary>
        public static Color VeinFor(EldritchTier tier)
        {
            switch (tier)
            {
                case EldritchTier.Eldritch: return AbyssalMagenta;
                case EldritchTier.Danger: return BloodDanger;
                default: return EldritchViolet;
            }
        }

        /// <summary>Default vein strength — only the Eldritch tier wears its veins openly.</summary>
        public static float VeinStrengthFor(EldritchTier tier)
        {
            switch (tier)
            {
                // Capture tune: 0.32/0.26 still flooded full-screen sheets violet — the veins
                // are an accent under the stone, not the surface colour. Buttons are small
                // enough that the lower values still read at arm's length.
                case EldritchTier.Eldritch: return 0.15f;
                case EldritchTier.Danger: return 0.16f;
                case EldritchTier.Tertiary: return 0.08f;
                case EldritchTier.Secondary: return 0.13f;  // something breathing under the lapis
                default: return 0.24f;                      // Primary: veins across the hero glass
            }
        }

        /// <summary>Label color that stays readable on the tier fill.</summary>
        public static Color TextFor(EldritchTier tier)
        {
            switch (tier)
            {
                case EldritchTier.Tertiary: return TextMuted;
                case EldritchTier.Danger: return TextCream;
                default: return TextCream;   // heavy black outline does the contrast work
            }
        }

        // ── Fonts ───────────────────────────────────────────────────

        /// <summary>Shout titles: Bangers (Rowdies fallback via FreeUiKit).</summary>
        public static Font Display() =>
            FreeUiKit.DisplayFont()
            ?? Resources.Load<Font>("WRLDZ/Fonts/Rowdies-Bold")
            ?? UiFoundation.BuiltinFont();

        /// <summary>Body copy: Exo 2 Bold.</summary>
        public static Font Body() => FreeUiKit.BodyFont() ?? UiFoundation.BuiltinFont();

        /// <summary>Button labels: Russo One.</summary>
        public static Font Ui() => FreeUiKit.UiFont() ?? UiFoundation.BuiltinFont();

        static Font _digits;

        /// <summary>Holographic digits: Orbitron Bold (Resources), Russo One fallback.</summary>
        public static Font Digits()
        {
            if (_digits != null) return _digits;
            _digits = Resources.Load<Font>("WRLDZ/Fonts/Orbitron-Bold");
            return _digits ?? FreeUiKit.UiFont() ?? UiFoundation.BuiltinFont();
        }
    }
}
