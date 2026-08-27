using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Yu-Gi-Oh reference pack helpers (maps, classic menus, profiles).
    /// Runtime backgrounds prefer WrldzPresentation originals — never Master Duel screenshots.
    /// </summary>
    public static class YgoRefs
    {
        const string Root = "WRLDZ/YgoRefs/";

        static Sprite L(string rel) => StreamingSprite.Load(Root + rel);

        // ── World maps (reference only; overworld uses WrldzPresentation) ──
        public static Sprite MapDomino() =>
            L("map_domino.png") ?? L("ui-menus/ui_Domino_City-TSC.png");

        public static Sprite MapWc09() =>
            L("map_wc09.png") ?? L("ui-menus/wiki_WorldMap-WC09.png");

        public static Sprite MapWc10() =>
            L("map_wc10.png") ?? L("ui-menus/wiki_WorldMap-WC10.png");

        public static Sprite MapWc11() =>
            L("map_wc11.png") ?? L("ui-menus/wiki_WorldMap-WC11.png");

        /// <summary>Prefer original WRLDZ map art; fall back to classic Domino/WC refs.</summary>
        public static Sprite OverworldMap() =>
            WrldzPresentation.OverworldMap()
            ?? MapDomino()
            ?? MapWc11()
            ?? MapWc10()
            ?? MapWc09();

        // ── Classic menus (reference / emblem source — not full-screen screenshots) ──
        public static Sprite DdsMainMenu() =>
            L("menu_dds_main.png") ?? L("ui-menus/ui_DDS_main_menu.png")
            ?? L("panel_dds_main.png");

        public static Sprite DuelHudRef() =>
            L("bg_duel_hud.png") ?? L("ui-menus/ui_gameplay_DuelistKingdom-LOD.png");

        /// <summary>Hub / splash use original WRLDZ art only.</summary>
        public static Sprite HubBackdrop() =>
            WrldzPresentation.HubBg()
            ?? WrldzPresentation.SplashBg();

        public static Sprite SplashBackdrop() =>
            WrldzPresentation.SplashBg()
            ?? WrldzPresentation.HubBg();

        public static Sprite DuelStageBackdrop() =>
            WrldzPresentation.DuelStageBg();

        // ── Emblems / profiles ───────────────────────────────────────
        public static Sprite MillenniumEye() =>
            WrldzPresentation.SpiritEye()
            ?? L("emblem_millennium_eye.png")
            ?? DdsMainMenu();

        public static Sprite ProfileDefault() =>
            L("profile_default.png")
            ?? L("player-profiles/profile_YamiYugi-DDS.png");

        public static Sprite ProfileYugi() =>
            L("profile_yugi.png") ?? L("player-profiles/profile_Yugi-DDS.png");

        public static Sprite ProfileKaiba() =>
            L("profile_kaiba.png") ?? L("player-profiles/profile_Kaiba-DDS.png");

        public static Sprite Profile(string fileNameWithoutExt)
        {
            if (string.IsNullOrEmpty(fileNameWithoutExt)) return ProfileDefault();
            return L("player-profiles/" + fileNameWithoutExt + ".png")
                   ?? L(fileNameWithoutExt + ".png")
                   ?? ProfileDefault();
        }
    }
}
