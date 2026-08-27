using System.Text;
using WRLDZ.Data;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Structural constants from the official Yu-Gi-Oh! TCG Rulebook
    /// (Konami Official Rulebook) + Yugipedia structural pages.
    /// Card-effect resolution lives in <see cref="Rules.OfficialEffectRegistry"/> —
    /// never invent effects here.
    /// See <c>Rules/TCG_RULES_ENGINE.md</c>.
    /// </summary>
    public static class TcgRules
    {
        // —— Deck construction (Rulebook: Deck Construction) ——
        public const int MainDeckMin = 40;
        public const int MainDeckMax = 60;
        public const int ExtraDeckMax = 15;
        public const int SideDeckMax = 15;
        public const int MaxCopiesPerCard = 3; // general case; banlist not enforced here

        // —— Duel setup ——
        public const int StartingLifePoints = 8000;
        public const int StartingHandSize = 5;
        public const int HandSizeLimitEndPhase = 6;
        public const int MonsterZones = 5;
        public const int SpellTrapZones = 5;

        // —— Summon (Rulebook: How to Summon Monsters) ——
        // Normal Summon/Set: once per turn (shared).
        // Level 1–4: no Tribute. Level 5–6: 1 Tribute. Level 7+: 2 Tributes.
        public static int TributesRequired(int level)
        {
            if (level <= 0) return 0;
            if (level <= 4) return 0;
            if (level <= 6) return 1;
            return 2;
        }

        /// <summary>
        /// Rulebook: a monster you control may be Tributed for a Tribute Summon/Set
        /// whether it is face-up or face-down. Face-down tributes are not flipped.
        /// </summary>
        public static bool CanBeTributedForSummon(DuelistState who, CardInstance monster)
        {
            if (who == null || monster == null) return false;
            if (monster.IsToken && monster.CannotBeTributedForSummon) return false;
            return who.TryFindMonster(monster, out _);
        }

        public static bool IsNormalSummonableMonster(CardDef def)
        {
            if (def == null || !def.IsMonster || def.IsExtraDeck) return false;
            // Ritual / Special Summon-only monsters need effects — treat non-ritual main-deck monsters as NS-legal for slice.
            if (def.type != null && def.type.IndexOf("Ritual", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return def.level >= 1;
        }

        public static string RulesSummaryMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# TCG structural rules integrated (Rulebook v10)");
            sb.AppendLine();
            sb.AppendLine("Source: Konami Official Rulebook Version 10.");
            sb.AppendLine();
            sb.AppendLine("## Victory");
            sb.AppendLine("- Reduce opponent LP to 0.");
            sb.AppendLine("- Opponent cannot draw when required (deck out).");
            sb.AppendLine();
            sb.AppendLine("## Turn structure");
            sb.AppendLine("1. Draw Phase — draw 1 (first player skips on first turn).");
            sb.AppendLine("2. Standby Phase — effect timing (empty in slice).");
            sb.AppendLine("3. Main Phase 1 — NS/Set, Flip Summon, change position, Set S/T.");
            sb.AppendLine("4. Battle Phase — optional; **first player cannot conduct Battle Phase on turn 1**.");
            sb.AppendLine("5. Main Phase 2 — same actions as MP1 if NS still available.");
            sb.AppendLine("6. End Phase — hand size to 6.");
            sb.AppendLine();
            sb.AppendLine("## Normal Summon / Set");
            sb.AppendLine("- Once per turn (shared).");
            sb.AppendLine("- Lv 1–4: free. Lv 5–6: 1 Tribute. Lv 7+: 2 Tributes.");
            sb.AppendLine("- NS → face-up Attack. Set → face-down Defense.");
            sb.AppendLine();
            sb.AppendLine("## Battle");
            sb.AppendLine("- ATK vs ATK: higher wins; damage = difference; equal → both destroyed, 0 damage.");
            sb.AppendLine("- ATK vs DEF: ATK>DEF destroy (no pierce); ATK<DEF attacker takes difference; equal → nothing.");
            sb.AppendLine("- Direct attack if opponent controls no monsters.");
            sb.AppendLine();
            sb.AppendLine("## Targeting (slice)");
            sb.AppendLine("- Monster Reborn: choose 1 legal monster in either GY.");
            sb.AppendLine("- Mystical Space Typhoon: choose 1 Spell/Trap on the field.");
            sb.AppendLine("- Enemy Controller: choose 1 face-up opponent monster.");
            sb.AppendLine("- AI auto-picks targets; the player always chooses.");
            sb.AppendLine();
            sb.AppendLine("## Response windows (slice)");
            sb.AppendLine("- Attack declared: defender may activate Mirror Force / Negate Attack / Waboku / set QP, or Pass.");
            sb.AppendLine("- Damage Calculation: defender may activate Kuriboh from hand (discard; no battle damage from that battle), or Pass.");
            sb.AppendLine("- Summon: defender may activate Trap Hole (ATK≥1000) / set QP, or Pass.");
            sb.AppendLine();
            sb.AppendLine("## Problem-Solving Card Text (Konami)");
            sb.AppendLine("- CONDITIONS `:` ACTIVATION `;` RESOLUTION. Colon/semicolon = Chain Link.");
            sb.AppendLine("- Monster text with neither mark does **not** start a chain (continuous / inherent SS).");
            sb.AppendLine("- Spell/Trap **card** activation always starts a chain; leftover Field/Continuous stat lines do not.");
            sb.AppendLine("- Engine: `PsctGrammar` splits official text; fragment templates compile cost + target + resolution.");
            sb.AppendLine();
            sb.AppendLine("## Monster ignition effects");
            sb.AppendLine("- Face-up Effect Monsters may have a Main Phase **Activate** (Spell Speed 1).");
            sb.AppendLine("- Abyss Soldier (18318842): once per turn, discard 1 WATER monster; target 1 card on the field; return it to the hand.");
            sb.AppendLine();
            sb.AppendLine("## Field Spells");
            sb.AppendLine("- Speed 1. Activate from hand in your Main Phase into the **Field Spell Zone** (not a Spell & Trap Zone).");
            sb.AppendLine("- They are Spells, not Traps — they do not Set in S/T Zones and are not Spell Speed 2.");
            sb.AppendLine("- Each player has one Field Zone; activating a new Field Spell sends your previous one to the GY.");
            sb.AppendLine("- A Legendary Ocean (295517): always treated as \"Umi\"; WATER monsters on the field gain 200 ATK/DEF; WATER monsters in both hands and on the field are Level −1 (Yugipedia / Konami).");
            sb.AppendLine();
            sb.AppendLine("## Continuous / registered staples");
            sb.AppendLine("- Swords of Revealing Light: flip FD; opponent cannot declare attacks; EP of opponent's 3rd turn.");
            sb.AppendLine("- Waboku: no battle damage; cannot be destroyed by battle this turn.");
            sb.AppendLine("- Ring of Destruction: opponent's turn only; destroy + original ATK effect damage both ways.");
            sb.AppendLine("- Polymerization: only registered Extra Deck recipes (Gaia Dragon Champion, Black Skull).");
            sb.AppendLine("- Flute of Summoning Dragon: Lord of D. on field; SS up to 2 Dragons from hand.");
            sb.AppendLine();
            sb.AppendLine("## Not yet simulated");
            sb.AppendLine("- Full multi-link chains / SEGOC / Counter Trap sequencing beyond Speed tags;");
            sb.AppendLine("- Damage Step-only effects; most monster effects; banlist construction checks;");
            sb.AppendLine("- Pendulum scales/summon; Link arrows; full Extra Deck recipes beyond registered fusions.");
            return sb.ToString();
        }
    }
}
