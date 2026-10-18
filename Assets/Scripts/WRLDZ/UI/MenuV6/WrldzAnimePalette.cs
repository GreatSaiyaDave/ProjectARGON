using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// D4: the "Stone Beneath Nut" v6.3 palette, SPEC §5 (tokens of <c>V6/assets/wrldz.css :root</c>), as sRGB values
    /// (Unity converts them under the project's Linear colour space). A new static on purpose: <c>EldritchPalette</c>
    /// still paints the Overworld HUD and the duel (Battle City), and retuning it would break the two-worlds rule
    /// (SPEC §11). Gold is 5–8 % of pixels and only on actions and relics; amethyst is the enemy only (never text).
    /// </summary>
    public static class WrldzAnimePalette
    {
        public static readonly Color Kem = Hex(0x0D0C10);        // base of every screen, outline ink
        public static readonly Color Basalt = Hex(0x2E3136);     // the Stone, walls, slabs
        public static readonly Color Plaster = Hex(0x131115);    // painted pylon faces
        public static readonly Color Obs = Hex(0x151319);        // modal glass, pupils
        public static readonly Color Lapis = Hex(0x1C2B6B);
        public static readonly Color LapisDk = Hex(0x0F1A45);
        public static readonly Color EBlue = Hex(0x1F4FA8);      // glyph infill, energy base
        public static readonly Color EGlow = Hex(0x5FB6FF);      // barrier light
        public static readonly Color EHot = Hex(0xBFE6FF);
        public static readonly Color Faience = Hex(0x1E9AA6);    // XP fill, +1, the ALL chip
        public static readonly Color Turq = Hex(0x45B0A6);
        public static readonly Color Gold = Hex(0xC99A2E);       // action rims, Items, seams
        public static readonly Color GoldLit = Hex(0xEBC66A);
        public static readonly Color GoldDk = Hex(0x6E5118);
        public static readonly Color GoldSpec = Hex(0xFFE86B);   // specular pop only
        public static readonly Color NutStar = Hex(0xE2B24A);
        public static readonly Color Electrum = Hex(0xD1C699);   // cartouche lines, tier-2 rims
        public static readonly Color Gyp = Hex(0xECE5D3);        // primary text
        public static readonly Color Calc = Hex(0xE3D6BA);
        public static readonly Color Muted = Hex(0xA79F8C);      // secondary text
        public static readonly Color Ochre = Hex(0xC39130);
        public static readonly Color Sand = Hex(0xB8966A);
        public static readonly Color Rubric = Hex(0xC0553A);     // QUIT, "ONE SHORT", the exclude chip
        public static readonly Color RubricDk = Hex(0x9B3B25);
        public static readonly Color Carn = Hex(0xB8421F);
        public static readonly Color Ember = Hex(0xFF7A45);
        public static readonly Color Wadj = Hex(0x7FC08E);       // BALANCED · LEGAL, 40/40, LP
        public static readonly Color WadjText = Hex(0x9FE0A8);
        public static readonly Color Ame = Hex(0x5A3A7E);        // smoke, breach void, Umbrax's eyes (enemy only)
        public static readonly Color AmeGlow = Hex(0xA77EDB);
        public static readonly Color AmeDeep = Hex(0x26183A);
        public static readonly Color Cyan = Hex(0x33EBFF);       // Battle City tech, Tears, YOU/OPP
        public static readonly Color Magenta = Hex(0xFF4798);
        public static readonly Color PigmentEBlue = Hex(0x2A5CAA);
        public static readonly Color PigmentRed = Hex(0xA8452B);
        public static readonly Color PigmentGreen = Hex(0x3F8F6B);
        public static readonly Color PigmentOchre = Hex(0xD6A640);
        public static readonly Color PigmentWhite = Hex(0xE9E2CF);
        public static readonly Color CardTypeMonster = Hex(0xE8823A);
        public static readonly Color CardTypeSpell = Hex(0x1FB5A0);
        public static readonly Color CardTypeTrap = Hex(0xB03A5E);

        /// <summary>The room's own base fill (menu.js:319 bakeBackground fills #07060a first): the letterbox colour
        /// around the 16:9 stage on other aspects.</summary>
        public static readonly Color RoomBase = Hex(0x07060A);

        /// <summary>The DUEL word's three hard gold bands (anime_v6.css .duel .t, exported in layout_land.json texts).</summary>
        public static readonly Color DuelBandTop = Hex(0xEDE49E);
        public static readonly Color DuelBandMid = Hex(0xCAA95B);
        public static readonly Color DuelBandLow = Hex(0x917649);

        public static Color Hex(int rgb) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, Mathf.Clamp01(a));
    }
}
