using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Grok Imagine visual overhaul pack under StreamingAssets/WRLDZ/Imagine/.
    /// Full-bleed scenes + chrome + icons + pins + FX. Prefer over vendor kits.
    /// </summary>
    public static class ImagineAssets
    {
        const string Root = "WRLDZ/Imagine/";

        static Sprite L(string relative) => StreamingSprite.Load(Root + relative);

        static Sprite Sliced(string relative, Vector4 border) =>
            StreamingSprite.LoadSliced(Root + relative, border);

        // ── Full-bleed backgrounds (boot cascade + hub) ─────────────
        public static Sprite BgSplash() => L("bg/bg_splash.png");
        /// <summary>Title screen — golden Old Kingdom dawn variant of splash world.</summary>
        public static Sprite BgTitle() => L("bg/bg_title.png") ?? BgSplash();
        /// <summary>Credits — quieter starfield dunes.</summary>
        public static Sprite BgCredits() => L("bg/bg_credits.png") ?? BgSplash();
        /// <summary>Auth / DOB / courtyard account screens.</summary>
        public static Sprite BgAuth() => L("bg/bg_auth.png") ?? BgTitle();
        public static Sprite BgHub() => L("bg/bg_hub.png") ?? BgTitle();
        public static Sprite BgMenuVoid() => L("bg/bg_menu_void.png");
        public static Sprite BgDeckBuilder() =>
            L("bg/bg_deck_builder.png") ?? BgMenuVoid();
        public static Sprite BgOverworldMap() => L("bg/bg_overworld_map.png");
        /// <summary>Secondary district tile for map corners (Battle City / Ingress).</summary>
        public static Sprite BgOverworldDistrict() => L("bg/bg_overworld_district.png");
        public static Sprite BgDuelStage() =>
            L("bg/bg_duel_stage.png") ?? L("ui/bg_duel_stage_portrait.png");
        public static Sprite DuelStagePortrait() =>
            L("ui/bg_duel_stage_portrait.png") ?? BgDuelStage();

        /// <summary>Translucent boot plate (obsidian filament, not smoked glass).</summary>
        public static Sprite PanelBootGlass() =>
            Sliced("ui/panel_boot_glass.png", new Vector4(64, 64, 64, 64))
            ?? L("ui/panel_boot_glass.png")
            ?? PanelModal();

        // ── Set Energy / XM-style map particles ─────────────────────
        public static Texture2D SpiritDiskAlbedo() =>
            StreamingSprite.LoadTexture(Root + "spirit/disk_skin_albedo.png");
        public static Texture2D SpiritDiskEmission() =>
            StreamingSprite.LoadTexture(Root + "spirit/disk_skin_emission.png");
        public static Texture2D SpiritWristSmoke() =>
            StreamingSprite.LoadTexture(Root + "spirit/wrist_aether_smoke.png", TextureWrapMode.Clamp);

        public static Sprite FxSeMoteCyan() => L("fx/fx_se_mote_cyan.png");
        public static Sprite FxSeMoteGold() => L("fx/fx_se_mote_gold.png");
        public static Sprite FxSeGatherBurst() => L("fx/fx_se_gather_burst.png");
        public static Sprite FxEyeWhiteGlow() => L("fx/fx_eye_white_glow.png");
        public static Sprite FxEyePupilRed() => L("fx/fx_eye_pupil_red.png");
        public static Sprite FxEyeRedHalo() => L("fx/fx_eye_red_halo.png") ?? FxEyePupilRed();
        public static Sprite FxEyePupilCore() => L("fx/fx_eye_pupil_core.png") ?? FxEyeWhiteGlow();
        public static Sprite FxHoloScan() => L("fx/fx_holo_scan.png") ?? FxEyeWhiteGlow();
        public static Sprite FxEyeBurstRing() =>
            L("fx/fx_eye_burst_ring.png") ?? LevelRing() ?? FxEyeWhiteGlow();

        // ── Icons (HUD / nav / inventory) ───────────────────────────
        public static Sprite IconSoul() => L("icons/icon_soul.png") ?? NaviSpirit();
        public static Sprite IconSoulFractured() =>
            L("icons/icon_soul_fractured.png") ?? IconSoul();
        public static Sprite IconDigizeni() => L("icons/icon_digizeni.png");
        public static Sprite IconDuelCoin() => L("icons/icon_duel_coin.png");
        public static Sprite IconSetEnergy() => L("icons/icon_set_energy.png");
        public static Sprite IconArtifactOrb() => L("icons/icon_artifact_orb.png");
        public static Sprite FxSoulDestinyGhost() => L("fx/soul_destiny_ghost.png");
        public static Sprite NaviSpirit() => L("icons/navi_spirit.png");
        public static Sprite IconCompass() => L("icons/icon_compass.png");
        public static Sprite IconDeck() => L("icons/icon_deck.png");

        /// <summary>
        /// Custom deck emblem: strategy (beatdown/burn/…) or Yugipedia attr/type.
        /// Empty / unknown → default card-fan.
        /// </summary>
        public static Sprite DeckStyleIcon(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "deck")
                return IconDeck();
            var p = id.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
            if (p.StartsWith("attr_"))
                return YgoAttribute(p.Substring(5)) ?? IconDeck();
            if (p.StartsWith("type_"))
                return L("icons/ygo/" + p + ".png") ?? IconDeck();
            return L("icons/deckstyle/icon_" + p + ".png") ?? IconDeck();
        }
        public static Sprite IconDeckMain() => L("icons/icon_deck_main.png") ?? IconDeck();
        public static Sprite IconDeckExtra() => L("icons/icon_deck_extra.png") ?? IconFilterExtra() ?? IconDeck();
        public static Sprite IconDeckSide() => L("icons/icon_deck_side.png") ?? IconDeck();
        public static Sprite IconBag() => L("icons/icon_bag.png");
        public static Sprite IconStory() => L("icons/icon_story.png");
        public static Sprite IconSettings() => L("icons/icon_settings.png");
        public static Sprite IconMenu() => L("icons/icon_menu.png");
        public static Sprite IconDuel() => L("icons/icon_duel.png") ?? IconMenu();
        public static Sprite IconVsPvp() => L("icons/icon_vs_pvp.png") ?? IconDuel();
        public static Sprite IconPractice() => L("icons/icon_practice.png") ?? IconDuel();
        /// <summary>Free View — spirit-eye projector, never the VS AI disk.</summary>
        public static Sprite IconView() =>
            L("icons/icon_view.png")
            ?? IconMillenniumEyeOpen()
            ?? EmblemSpiritEye()
            ?? IconPractice();
        public static Sprite IconTome() => L("icons/icon_tome.png") ?? IconStory();
        public static Sprite IconBazaar() => L("icons/icon_bazaar.png") ?? IconGoldSafe();
        public static Sprite IconFilterAll() => L("icons/icon_filter_all.png") ?? IconDeck();
        public static Sprite IconFilterMonster() => L("icons/icon_filter_monster.png") ?? IconDuel();
        public static Sprite IconFilterSpell() => L("icons/icon_filter_spell.png") ?? IconTome();
        public static Sprite IconFilterTrap() => L("icons/icon_filter_trap.png") ?? IconStory();
        public static Sprite IconFilterExtra() => L("icons/icon_filter_extra.png") ?? IconDeck();
        public static Sprite IconFilterFacets() => L("icons/icon_filter_facets.png") ?? IconSettings();
        public static Sprite IconFilterSort() => L("icons/icon_filter_sort.png") ?? IconMenu();
        public static Sprite IconLimitForbidden() => L("icons/icon_limit_forbidden.png");
        public static Sprite IconLimitLimited() => L("icons/icon_limit_limited.png");
        public static Sprite IconLimitSemi() => L("icons/icon_limit_semi.png");

        /// <summary>Yugipedia CC-BY attribute medallion (DARK, LIGHT, …).</summary>
        public static Sprite YgoAttribute(string attr)
        {
            if (string.IsNullOrEmpty(attr)) return null;
            return L("icons/ygo/attr_" + attr.Trim().ToLowerInvariant() + ".png");
        }

        /// <summary>Yugipedia Master Duel Type icon (Warrior, Winged Beast, …).</summary>
        public static Sprite YgoMonsterType(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            var slug = type.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
            return L("icons/ygo/type_" + slug + ".png");
        }

        /// <summary>Yugipedia Spell/Trap property icon (Equip, Quick-Play, Counter…).</summary>
        public static Sprite YgoSpellTrapKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            var slug = kind.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
            return L("icons/ygo/st_" + slug + ".png");
        }
        public static Sprite IconSlotLook() => L("icons/icon_slot_look.png") ?? IconFilterAll();
        public static Sprite IconSlotHat() => L("icons/icon_slot_hat.png") ?? IconSlotLook();
        public static Sprite IconSlotFacial() => L("icons/icon_slot_facial.png") ?? IconSlotLook();
        public static Sprite IconSlotShirt() => L("icons/icon_slot_shirt.png") ?? IconSlotLook();
        public static Sprite IconSlotHands() => L("icons/icon_slot_hands.png") ?? IconSlotLook();
        public static Sprite IconSlotBottoms() => L("icons/icon_slot_bottoms.png") ?? IconSlotLook();
        public static Sprite IconSlotShoes() => L("icons/icon_slot_shoes.png") ?? IconSlotLook();
        public static Sprite IconSlotAccent() => L("icons/icon_slot_accent.png") ?? IconSlotLook();
        public static Sprite IconSlotTitle() => L("icons/icon_slot_title.png") ?? IconSlotLook();
        public static Sprite IconTeamGalactikuriboh() =>
            L("icons/icon_team_galactikuriboh.png") ?? IconSlotLook();
        public static Sprite IconTeamKuribandit() =>
            L("icons/icon_team_kuribandit.png") ?? IconSlotLook();
        public static Sprite IconTeamJunkuriboh() =>
            L("icons/icon_team_junkuriboh.png") ?? IconSlotLook();
        public static Sprite IconTeam(WRLDZ.Data.KuribohTeam team) => team switch
        {
            WRLDZ.Data.KuribohTeam.Galactikuriboh => IconTeamGalactikuriboh(),
            WRLDZ.Data.KuribohTeam.Kuribandit => IconTeamKuribandit(),
            WRLDZ.Data.KuribohTeam.Junkuriboh => IconTeamJunkuriboh(),
            _ => null
        };

        public static Sprite TileBlockApt() => L("map/tile_block_apt.png");
        public static Sprite TileBlockShop() => L("map/tile_block_shop.png");
        public static Sprite TileBlockTower() => L("map/tile_block_tower.png");
        public static Sprite TilePark() => L("map/tile_park.png");
        public static Sprite TileMall() => L("map/tile_mall.png");
        public static Sprite TileGameStore() => L("map/tile_gamestore.png");
        public static Sprite TileStripMall() => L("map/tile_stripmall.png");
        public static Sprite IconTournament() =>
            L("icons/icon_tournament.png") ?? PinTournament() ?? IconDuel();

        public static Sprite CinematicPossessHuman() => L("cinematic/possess_human.png");
        public static Sprite CinematicPossessTear() => L("cinematic/possess_tear.png") ?? CinematicPossessHuman();
        public static Sprite CinematicPossessSpirit() => L("cinematic/possess_spirit.png") ?? CinematicPossessTear();
        public static Sprite CinematicPossessDisk() => L("cinematic/possess_disk.png") ?? CinematicPossessSpirit();
        static Sprite IconGoldSafe() => L("icons/icon_duel_coin.png") ?? IconDigizeni();
        /// <summary>Pokéball-style overworld main menu orb — Millennium Eye.</summary>
        public static Sprite IconMillenniumEyeMenu() =>
            L("icons/icon_millennium_eye_menu.png")
            ?? L("icons/icon_millennium_eye_menu_256.png")
            ?? EmblemSpiritEye()
            ?? IconMenu();
        public static Sprite IconMillenniumEyeOpen() =>
            L("icons/icon_millennium_eye_open.png")
            ?? IconMillenniumEyeMenu();
        public static Sprite EmblemSpiritEye() => L("icons/emblem_spirit_eye.png");
        /// <summary>Takahashi-inspired original avatar creator preview (Imagine pack).</summary>
        public static Sprite AvatarCreatorPreview() => L("icons/avatar_creator_preview.png");

        // ── Map pins (unique irregular silhouettes per zone kind) ──
        public static Sprite PinTear() => L("pins/pin_tear.png");
        public static Sprite PinPortal() => L("pins/pin_portal.png");
        public static Sprite PinRaid() => L("pins/pin_raid.png") ?? L("pins/pin_arena.png");
        public static Sprite PinArena() => PinRaid() ?? PinPortal();
        public static Sprite PinBazaar() => L("pins/pin_bazaar.png") ?? PinTreasure();
        public static Sprite PinTraining() => L("pins/pin_training.png") ?? PinArena();
        public static Sprite PinTournament() => L("pins/pin_tournament.png") ?? PinRaid();
        public static Sprite PinEvent() => L("pins/pin_event.png") ?? PinTournament();
        public static Sprite PinNpc() => L("pins/pin_npc.png");
        public static Sprite PinTreasure() => L("pins/pin_treasure.png");
        public static Sprite PinAnchor() => L("pins/pin_anchor.png") ?? PinPortal();
        public static Sprite PinStory() => L("pins/pin_story.png") ?? PinPortal();
        public static Sprite PinPvp() => L("pins/pin_pvp.png") ?? PinEvent() ?? PinTournament();

        // ── Panels / bars ───────────────────────────────────────────
        public static Sprite PanelHolo() =>
            Sliced("ui/panel_holo_glass.png", new Vector4(72, 64, 72, 64))
            ?? L("ui/panel_holo_glass.png");

        /// <summary>Legacy name. Art is now an obsidian filament sheet, not smoked glass.</summary>
        public static Sprite PanelMenuGlass() =>
            Sliced("ui/panel_menu_glass.png", new Vector4(48, 32, 48, 32))
            ?? L("ui/panel_menu_glass.png")
            ?? PanelHolo();

        public static Sprite PanelModal() =>
            Sliced("ui/panel_modal.png", new Vector4(72, 64, 72, 64))
            ?? PanelHolo();

        public static Sprite PanelDeckMain() =>
            Sliced("ui/panel_deck_main.png", new Vector4(160, 110, 160, 110))
            ?? L("ui/panel_deck_main.png")
            ?? PanelHolo();
        public static Sprite PanelDeckExtra() =>
            Sliced("ui/panel_deck_extra.png", new Vector4(160, 110, 160, 110))
            ?? PanelDeckMain();
        public static Sprite PanelDeckSide() =>
            Sliced("ui/panel_deck_side.png", new Vector4(160, 110, 160, 110))
            ?? PanelDeckMain();
        public static Sprite PanelInspectSheet() =>
            Sliced("ui/panel_inspect_sheet.png", new Vector4(80, 80, 80, 80))
            ?? L("ui/panel_inspect_sheet.png")
            ?? PanelModal();

        public static Sprite BarBottom() =>
            Sliced("ui/bar_bottom.png", new Vector4(24, 12, 24, 12))
            ?? L("ui/bar_bottom.png");

        public static Sprite BarTopHud() =>
            Sliced("ui/bar_top_hud.png", new Vector4(24, 12, 24, 12))
            ?? BarBottom();

        public static Sprite HudChip() =>
            HudChipCyan()
            ?? Sliced("ui/hud_chip_plate.png", new Vector4(40, 16, 40, 16))
            ?? L("ui/hud_chip_plate.png");

        /// <summary>Hollow cyan filament island — arena HUD, not a filled plate.</summary>
        public static Sprite HudIslandGlass() => L("ui/hud_island_glass.png") ?? HudChip();

        public static Sprite HudChipCyan() => L("ui/hud_chip_cyan.png");
        public static Sprite HudChipGold() => L("ui/hud_chip_gold.png") ?? HudChipCyan();
        public static Sprite HudChipDanger() => L("ui/hud_chip_danger.png") ?? HudChipCyan();
        public static Sprite HudFabOrb() => L("ui/hud_fab_orb.png") ?? BtnCircle();
        public static Sprite HudPipIdle() => L("ui/hud_pip_idle.png") ?? PhaseIdle();
        public static Sprite HudPipActive() => L("ui/hud_pip_active.png") ?? PhaseActive();
        public static Sprite HudCalloutGlass() => L("ui/hud_callout_glass.png") ?? HudIslandGlass();
        public static Sprite IconArenaSys() => L("icons/icon_arena_sys.png") ?? IconSettings();
        public static Sprite IconArenaShuffle() => L("icons/icon_arena_shuffle.png") ?? IconDeck();

        public static Sprite LpBarFrame() =>
            Sliced("ui/lp_bar_frame.png", new Vector4(48, 12, 48, 12))
            ?? L("ui/lp_bar_frame.png");

        /// <summary>4Kids spiral LP plate (numbers drawn live, not baked in).</summary>
        public static Sprite LpCounterTemplate() =>
            L("ui/lp_counter_swirl.jpg")
            ?? L("ui/lp_counter_template.jpg")
            ?? LpBarFrame();

        /// <summary>ATK over DEF point-gauge plate at the bottom-right of monster holos.</summary>
        public static Sprite StatGaugeTemplate() =>
            L("ui/stat_gauge_swirl.jpg")
            ?? L("ui/stat_gauge_template.jpg")
            ?? LpCounterTemplate();

        public static Sprite HandFrame() =>
            Sliced("ui/hand_frame.png", new Vector4(40, 24, 40, 24))
            ?? L("ui/hand_frame.png");

        public static Sprite InputField() =>
            Sliced("ui/input_field.png", new Vector4(32, 16, 32, 16))
            ?? L("ui/input_field.png");

        public static Sprite DialogueBubble() =>
            Sliced("ui/dialogue_bubble.png", new Vector4(40, 28, 40, 40))
            ?? L("ui/dialogue_bubble.png");

        public static Sprite ZoneMonsterPlate() =>
            Sliced("ui/zone_monster_plate.png", new Vector4(40, 40, 40, 40))
            ?? L("ui/zone_monster_plate.png");

        // ── Buttons ─────────────────────────────────────────────────
        // Rounded-rect plates. Borders sit on solid corners so 9-slice
        // stays visible at menu row height.
        static readonly Vector4 BtnSlice = new(72f, 56f, 72f, 56f);

        public static Sprite BtnPrimary() =>
            Sliced("ui/button_primary_plate.png", BtnSlice)
            ?? L("ui/button_primary_plate.png");

        public static Sprite BtnPrimaryHover() =>
            Sliced("ui/button_primary_hover.png", BtnSlice)
            ?? L("ui/button_primary_hover.png")
            ?? BtnPrimary();

        public static Sprite BtnPrimaryPressed() =>
            Sliced("ui/button_primary_pressed.png", BtnSlice)
            ?? L("ui/button_primary_pressed.png")
            ?? BtnPrimary();

        public static Sprite BtnSecondary() =>
            Sliced("ui/button_secondary_plate.png", BtnSlice)
            ?? L("ui/button_secondary_plate.png")
            ?? BtnPrimary();

        public static Sprite BtnDanger() =>
            Sliced("ui/button_danger_plate.png", BtnSlice)
            ?? L("ui/button_danger_plate.png")
            ?? BtnSecondary();

        public static Sprite BtnGold() =>
            Sliced("ui/button_gold_plate.png", BtnSlice)
            ?? L("ui/button_gold_plate.png")
            ?? BtnPrimary();

        /// <summary>Holo command card for the overworld Eye fan menu.</summary>
        public static Sprite MenuHoloCard() =>
            Sliced("ui/menu_holo_card.png", new Vector4(72, 88, 72, 88))
            ?? L("ui/menu_holo_card.png")
            ?? TileHub();

        /// <summary>Gold featured command card (VS AI).</summary>
        public static Sprite MenuHoloCardGold() =>
            Sliced("ui/menu_holo_card_gold.png", new Vector4(72, 88, 72, 88))
            ?? L("ui/menu_holo_card_gold.png")
            ?? BtnGold()
            ?? MenuHoloCard();

        /// <summary>Wide holographic overlay / profile sheet.</summary>
        public static Sprite MenuHoloSheet() =>
            // Art has ~110×95 px transparent glow around the glass. Slicing inside
            // that pad stretched empty margin and left title/close floating off-panel.
            Sliced("ui/menu_holo_sheet.png", new Vector4(110, 95, 110, 95))
            ?? L("ui/menu_holo_sheet.png")
            ?? PanelMenuGlass();

        /// <summary>Square destination tile (hub grid).</summary>
        public static Sprite TileHub() =>
            Sliced("ui/tile_hub.png", new Vector4(80, 80, 80, 80))
            ?? L("ui/tile_hub.png")
            ?? PanelModal();

        /// <summary>Gold featured destination tile (VS AI / primary duel).</summary>
        public static Sprite TileHubGold() =>
            Sliced("ui/tile_hub_gold.png", new Vector4(80, 80, 80, 80))
            ?? L("ui/tile_hub_gold.png")
            ?? BtnGold()
            ?? TileHub();

        public static Sprite BtnConfirm() => BtnPrimary(); // green tint applied by caller if needed
        public static Sprite BtnCircle() => L("ui/button_circle.png");
        public static Sprite BtnClose() => L("ui/button_close.png");
        public static Sprite LevelRing() => L("ui/level_ring.png");

        // ── Duel badges / phase ─────────────────────────────────────
        public static Sprite PhaseActive() =>
            Sliced("ui/phase_active.png", new Vector4(32, 12, 32, 12))
            ?? L("ui/phase_active.png");

        public static Sprite PhaseIdle() =>
            Sliced("ui/phase_idle.png", new Vector4(32, 12, 32, 12))
            ?? L("ui/phase_idle.png");

        public static Sprite BadgeAtk() => L("ui/badge_atk.png");
        public static Sprite BadgeDef() => L("ui/badge_def.png");
        public static Sprite CardBackWrldz() => L("ui/card_back_wrldz.png");

        // ── FX ──────────────────────────────────────────────────────
        public static Sprite FxSummonBurst() => L("fx/fx_summon_burst.png");
        public static Sprite FxImpactSlash() => L("fx/fx_impact_slash.png");

        /// <summary>True if the overhaul pack is installed on disk.</summary>
        public static bool PackPresent() =>
            PanelHolo() != null || BgSplash() != null || IconDigizeni() != null;
    }
}
