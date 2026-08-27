using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Coordinated UI kit: Grok Imagine overhaul first, then Open Duelyst vendor,
    /// Master Duel × cyberpunk palette throughout.
    /// Paths: StreamingAssets/WRLDZ/Imagine/ · Vendor/OpenDuelyst/ui/
    /// </summary>
    public static class DuelystUi
    {
        const string Root = "WRLDZ/Vendor/OpenDuelyst/ui/";

        // ── Master Duel × cyberpunk palette ─────────────────────────
        /// <summary>Near-black void (Master Duel stage).</summary>
        public static readonly Color BgDeep = new(0.02f, 0.03f, 0.06f, 1f);
        /// <summary>Raised panel — matte slate, not neon glass.</summary>
        public static readonly Color BgPanel = new(0.06f, 0.08f, 0.12f, 0.94f);
        /// <summary>HUD strip behind text.</summary>
        public static readonly Color BgHud = new(0.03f, 0.05f, 0.10f, 0.88f);
        /// <summary>Primary readable text (warm white like MD).</summary>
        public static readonly Color TextCream = new(0.98f, 0.97f, 0.94f, 1f);
        /// <summary>Secondary labels — still high contrast.</summary>
        public static readonly Color TextMuted = new(0.72f, 0.82f, 0.92f, 1f);
        /// <summary>Neon cyan — player / holos.</summary>
        public static readonly Color Cyan = new(0.20f, 0.92f, 1.00f, 1f);
        public static readonly Color CyanDim = new(0.08f, 0.45f, 0.62f, 1f);
        /// <summary>Master Duel gold — phases, LP, rewards.</summary>
        public static readonly Color Gold = new(1.00f, 0.84f, 0.28f, 1f);
        public static readonly Color GoldHot = new(1.00f, 0.92f, 0.45f, 1f);
        /// <summary>Cyber magenta — opponent / danger accent.</summary>
        public static readonly Color Magenta = new(1.00f, 0.28f, 0.72f, 1f);
        public static readonly Color Green = new(0.30f, 0.98f, 0.55f, 1f);
        public static readonly Color Danger = new(1.00f, 0.32f, 0.38f, 1f);
        public static readonly Color BattleOrange = new(1.00f, 0.48f, 0.18f, 1f);
        /// <summary>Quiet panel edge.</summary>
        public static readonly Color NeonEdge = new(0.50f, 0.68f, 0.78f, 0.42f);
        public static readonly Color GoldEdge = new(0.90f, 0.76f, 0.32f, 0.50f);

        static Sprite P(string file) => StreamingSprite.Load(Root + file);
        static Sprite S(string file, float l, float b, float r, float t) =>
            StreamingSprite.LoadSliced(Root + file, new Vector4(l, b, r, t));

        // ── Core chrome (Imagine → OpenDuelyst) ─────────────────────
        public static Sprite Panel() =>
            ImagineAssets.PanelHolo()
            ?? S("frame_quest@2x.png", 48, 64, 48, 48)
            ?? S("frame_quest.png", 24, 32, 24, 24);

        public static Sprite Bar() =>
            ImagineAssets.BarBottom()
            ?? ImagineAssets.BarTopHud()
            ?? S("bottom_bar_background@2x.png", 8, 8, 8, 8)
            ?? P("bottom_bar_background@2x.png");

        public static Sprite BtnPrimary() =>
            ImagineAssets.BtnPrimary()
            ?? S("button_primary@2x.png", 56, 24, 56, 24)
            ?? P("button_primary@2x.png");

        public static Sprite BtnSecondary() =>
            ImagineAssets.BtnSecondary()
            ?? S("button_secondary@2x.png", 56, 24, 56, 24)
            ?? P("button_secondary@2x.png");

        public static Sprite BtnConfirm() =>
            ImagineAssets.BtnConfirm()
            ?? S("button_confirm@2x.png", 56, 24, 56, 24)
            ?? P("button_confirm@2x.png")
            ?? BtnPrimary();

        public static Sprite BtnCancel() =>
            ImagineAssets.BtnDanger()
            ?? S("button_cancel@2x.png", 56, 24, 56, 24)
            ?? P("button_cancel@2x.png");

        public static Sprite BtnGold() =>
            ImagineAssets.BtnGold()
            ?? S("button_end_turn_mine@2x.png", 64, 28, 64, 28)
            ?? P("button_end_turn_mine@2x.png");

        public static Sprite BtnCircle() =>
            ImagineAssets.BtnCircle()
            ?? P("button_back@2x.png")
            ?? P("button_back.png");

        public static Sprite BtnCircleGlow() =>
            ImagineAssets.LevelRing()
            ?? P("button_back_glow@2x.png")
            ?? BtnCircle();

        public static Sprite BtnClose() =>
            ImagineAssets.BtnClose()
            ?? P("button_close@2x.png")
            ?? P("button_close.png");

        public static Sprite RowChip() =>
            ImagineAssets.HudChip()
            ?? S("gold_main_menu_container@2x.png", 40, 20, 40, 20)
            ?? S("diamond_main_menu_container@2x.png", 40, 20, 40, 20)
            ?? BtnSecondary();

        public static Sprite OrbRing() =>
            ImagineAssets.LevelRing()
            ?? P("dialogue_border@2x.png")
            ?? P("card_background@2x.png");

        public static Sprite ModalDisc() =>
            ImagineAssets.PanelModal()
            ?? P("frame_modal@2x.png")
            ?? Panel();

        public static Sprite CardPlate() =>
            ImagineAssets.ZoneMonsterPlate()
            ?? P("card_background@2x.png");

        public static Sprite IconDeck() =>
            ImagineAssets.IconDeck()
            ?? P("icon_deck@2x.png")
            ?? P("icon_deck.png");

        public static Sprite IconHand() => P("icon_hand@2x.png") ?? P("icon_hand.png");
        public static Sprite IconGold() =>
            ImagineAssets.IconDuelCoin()
            ?? P("icon_gold@2x.png")
            ?? P("icon_gold.png");

        public static Sprite IconAtk() =>
            ImagineAssets.BadgeAtk()
            ?? P("icon_atk@2x.png")
            ?? P("icon_atk.png");

        public static Sprite IconHp() =>
            ImagineAssets.IconSetEnergy()
            ?? P("icon_hp@2x.png")
            ?? P("icon_hp.png");

        public static Sprite IconBag() => ImagineAssets.IconBag();
        public static Sprite IconStory() => ImagineAssets.IconStory();
        public static Sprite IconSettings() => ImagineAssets.IconSettings();
        public static Sprite IconMenu() => ImagineAssets.IconMenu();
        public static Sprite PhaseActive() => ImagineAssets.PhaseActive() ?? BtnGold();
        public static Sprite PhaseIdle() => ImagineAssets.PhaseIdle() ?? BtnSecondary();
        public static Sprite BadgeDef() => ImagineAssets.BadgeDef();
        public static Sprite HandFrame() => ImagineAssets.HandFrame() ?? Bar();
        public static Sprite InputFieldSpr() => ImagineAssets.InputField() ?? Bar();
        public static Sprite DialogueBubble() => ImagineAssets.DialogueBubble() ?? Panel();
    }
}
