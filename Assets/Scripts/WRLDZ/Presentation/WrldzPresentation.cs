using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Original WRLDZ presentation art under StreamingAssets/WRLDZ/Presentation/.
    /// Prefers Grok Imagine pack (<see cref="ImagineAssets"/>) when present, then
    /// Presentation mirrors (*_imagine.png), then hand-authored pack-ins.
    /// </summary>
    public static class WrldzPresentation
    {
        const string Root = "WRLDZ/Presentation/";

        static Sprite L(string file) => StreamingSprite.Load(Root + file);

        // ── Full-bleed scenes (Imagine overhaul first) ──────────────
        public static Sprite SplashBg() =>
            ImagineAssets.BgSplash() ?? L("bg_splash.png");
        public static Sprite HubBg() =>
            ImagineAssets.BgHub() ?? L("bg_hub.png");
        public static Sprite MenuVoidBg() =>
            ImagineAssets.BgMenuVoid() ?? HubBg();
        public static Sprite DuelStageBg() =>
            ImagineAssets.BgDuelStage()
            ?? ImagineAssets.DuelStagePortrait()
            ?? L("bg_duel_stage_imagine.png")
            ?? L("bg_duel_stage.png");

        // ── Map ─────────────────────────────────────────────────────
        public static Sprite OverworldMap() =>
            ImagineAssets.BgOverworldMap()
            ?? L("map_overworld.png")
            ?? L("map_overworld_alt.png");

        public static Sprite OverworldMapDusk() =>
            ImagineAssets.BgOverworldMap()
            ?? L("map_overworld_dusk.png")
            ?? OverworldMap();

        public static Sprite OverworldMapAlt() =>
            L("map_overworld_alt.png") ?? OverworldMap();

        // ── Emblems / glow ──────────────────────────────────────────
        public static Sprite SpiritEye() =>
            ImagineAssets.EmblemSpiritEye()
            ?? L("emblem_spirit_eye.png")
            ?? L("emblem_spirit_eye_sm.png")
            ?? L("ui_logo_emblem.png");

        public static Sprite LogoEmblem() =>
            ImagineAssets.EmblemSpiritEye()
            ?? L("ui_logo_emblem.png")
            ?? SpiritEye();

        public static Sprite MenuGlow() => L("glow_menu.png");
        public static Sprite SheetPanel() => L("panel_sheet.png");
        public static Sprite Vignette() => L("ui_vignette.png");

        // ── UI chrome (Imagine → Presentation → fallback) ───────────
        public static Sprite PanelHolo() =>
            ImagineAssets.PanelHolo()
            ?? L("ui_panel_holo_imagine.png")
            ?? L("ui_panel_holo.png")
            ?? L("ui_panel_fallback.png")
            ?? SheetPanel();

        public static Sprite BtnPrimary() =>
            ImagineAssets.BtnPrimary()
            ?? L("ui_button_primary_imagine.png")
            ?? L("ui_button_primary.png")
            ?? L("ui_btn_fallback.png");

        public static Sprite HeaderBar() =>
            ImagineAssets.BarTopHud() ?? L("ui_header_bar.png");
        public static Sprite InputField() =>
            ImagineAssets.InputField() ?? L("ui_input.png");
        public static Sprite StepDots() => L("ui_step_dots.png");
        public static Sprite LevelRing() =>
            ImagineAssets.LevelRing() ?? L("level_ring_imagine.png");
        public static Sprite HudChip() => ImagineAssets.HudChip();
        public static Sprite LpBarFrame() => ImagineAssets.LpBarFrame();
        public static Sprite ZoneMonsterPlate() => ImagineAssets.ZoneMonsterPlate();
        public static Sprite HandFrame() => ImagineAssets.HandFrame() ?? L("ui_hand_frame.png");
        public static Sprite PhaseIdleArt() => ImagineAssets.PhaseIdle() ?? L("ui_phase_idle.png");
        public static Sprite PhaseActiveArt() => ImagineAssets.PhaseActive() ?? L("ui_phase_active.png");
        public static Sprite CardBackWrldz() => ImagineAssets.CardBackWrldz();
        public static Sprite DialogueBubble() => ImagineAssets.DialogueBubble();

        // ── Pins ────────────────────────────────────────────────────
        public static Sprite PinTear() =>
            ImagineAssets.PinTear()
            ?? L("pin_tear_imagine.png")
            ?? L("pin_tear.png");

        public static Sprite PinArena() =>
            ImagineAssets.PinArena()
            ?? L("pin_arena_imagine.png")
            ?? L("pin_arena.png");

        public static Sprite PinTreasure() => ImagineAssets.PinTreasure();
        public static Sprite PinPortal() => ImagineAssets.PinPortal();

        // ── Navi portraits ──────────────────────────────────────────
        public static Sprite NaviGalacti() => L("navi_galacti.png") ?? L("navi_kuriboh_ref.png");
        public static Sprite NaviBandit() => L("navi_bandit.png") ?? L("navi_kuriboh_ref.png");
        public static Sprite NaviJunk() => L("navi_junk.png") ?? L("navi_kuriboh_ref.png");
        public static Sprite NaviSpirit() => ImagineAssets.NaviSpirit();

        public static Sprite NaviForTeam(int team) => team switch
        {
            1 => NaviGalacti(),
            2 => NaviBandit(),
            3 => NaviJunk(),
            _ => NaviBandit() ?? NaviSpirit() ?? SpiritEye()
        };

        // ── Duel HUD / FX ───────────────────────────────────────────
        public static Sprite DuelTopBar() =>
            ImagineAssets.BarTopHud() ?? L("ui_duel_topbar.png") ?? PanelHolo();
        public static Sprite DuelBottomBar() =>
            ImagineAssets.BarBottom() ?? L("ui_duel_bottombar.png") ?? PanelHolo();
        public static Sprite PhaseIdle() => PhaseIdleArt() ?? BtnPrimary();
        public static Sprite PhaseActive() => PhaseActiveArt() ?? BtnPrimary();
        public static Sprite FxSummonBurst() =>
            ImagineAssets.FxSummonBurst() ?? L("fx_summon_burst_imagine.png");
        public static Sprite FxImpactSlash() => ImagineAssets.FxImpactSlash();
    }
}
