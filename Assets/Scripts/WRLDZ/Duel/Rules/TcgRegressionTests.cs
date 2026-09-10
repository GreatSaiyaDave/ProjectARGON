using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Regression checks for official battle math, structural summon rules, and registered effects.
    /// Run: WRLDZ → Rules → Run TCG Regression Tests
    /// </summary>
    public static class TcgRegressionTests
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok)
                {
                    pass++;
                    sb.AppendLine("PASS  " + name);
                }
                else
                {
                    fail++;
                    sb.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
                }
            }

            // ── Battle: ATK vs higher DEF ──
            {
                var atk = MockMonster(1800, 1000, BattlePosition.Attack, true);
                var def = MockMonster(800, 2000, BattlePosition.Defense, true);
                var r = BattleMechanics.Calculate(atk, def, false, false, false, false, false);
                BattleMechanics.Sanitize(ref r, atk, def);
                Check("ATK 1800 vs DEF 2000: defender lives", !r.DestroyDefender && !r.DestroyAttacker);
                Check("ATK 1800 vs DEF 2000: attacker takes 200", r.DamageToAttackingPlayer == 200);
                Check("ATK 1800 vs DEF 2000: no damage to defender", r.DamageToDefendingPlayer == 0);
            }

            // ── Battle: ATK vs lower DEF ──
            {
                var atk = MockMonster(1800, 1000, BattlePosition.Attack, true);
                var def = MockMonster(450, 600, BattlePosition.Defense, true);
                var r = BattleMechanics.Calculate(atk, def, false, false, false, false, false);
                Check("ATK 1800 vs DEF 600: destroy defender", r.DestroyDefender);
                Check("ATK 1800 vs DEF 600: 0 battle damage", r.DamageToDefendingPlayer == 0);
                Check("ATK 1800 vs DEF 600: attacker lives", !r.DestroyAttacker);
            }

            // ── Battle: face-down always DEF ──
            {
                var atk = MockMonster(1200, 800, BattlePosition.Attack, true);
                var fd = MockMonster(2000, 1500, BattlePosition.Attack, false); // wrong Position flag
                Check("Face-down uses defense stat", BattleMechanics.UsesDefenseStat(fd));
                var r = BattleMechanics.Calculate(atk, fd, false, false, false, false, false);
                BattleMechanics.Sanitize(ref r, atk, fd);
                Check("Face-down DEF 1500 vs ATK 1200: lives", !r.DestroyDefender);
                Check("Face-down DEF 1500 vs ATK 1200: dmg 300 to attacker", r.DamageToAttackingPlayer == 300);
            }

            // ── Battle: equal ATK ──
            {
                var a = MockMonster(1400, 1200, BattlePosition.Attack, true);
                var b = MockMonster(1400, 1000, BattlePosition.Attack, true);
                var r = BattleMechanics.Calculate(a, b, false, false, false, false, false);
                Check("Equal ATK: both destroyed", r.DestroyAttacker && r.DestroyDefender);
                Check("Equal ATK: 0 damage", r.DamageToAttackingPlayer == 0 && r.DamageToDefendingPlayer == 0);
            }

            // ── Battle: ATK vs higher ATK ──
            {
                var a = MockMonster(1500, 1000, BattlePosition.Attack, true);
                var b = MockMonster(2000, 1000, BattlePosition.Attack, true);
                var r = BattleMechanics.Calculate(a, b, false, false, false, false, false);
                Check("ATK 1500 vs ATK 2000: destroy attacker", r.DestroyAttacker && !r.DestroyDefender);
                Check("ATK 1500 vs ATK 2000: 500 to attacker controller", r.DamageToAttackingPlayer == 500);
            }

            // ── Battle: ATK vs lower ATK ──
            {
                var a = MockMonster(2500, 1000, BattlePosition.Attack, true);
                var b = MockMonster(2000, 1000, BattlePosition.Attack, true);
                var r = BattleMechanics.Calculate(a, b, false, false, false, false, false);
                Check("ATK 2500 vs ATK 2000: destroy defender", r.DestroyDefender && !r.DestroyAttacker);
                Check("ATK 2500 vs ATK 2000: 500 to defender controller", r.DamageToDefendingPlayer == 500);
            }

            // ── Battle: ATK = DEF ──
            {
                var a = MockMonster(1500, 0, BattlePosition.Attack, true);
                var d = MockMonster(1000, 1500, BattlePosition.Defense, true);
                var r = BattleMechanics.Calculate(a, d, false, false, false, false, false);
                Check("ATK = DEF: nothing destroyed", !r.DestroyAttacker && !r.DestroyDefender);
                Check("ATK = DEF: 0 damage", r.DamageToAttackingPlayer == 0 && r.DamageToDefendingPlayer == 0);
            }

            // ── Battle: direct ──
            {
                var a = MockMonster(1600, 0, BattlePosition.Attack, true);
                var r = BattleMechanics.Calculate(a, null, false, false, false, false, false);
                Check("Direct: 1600 damage", r.DamageToDefendingPlayer == 1600 && !r.DestroyDefender);
            }

            // ── Battle: piercing ──
            {
                var a = MockMonster(2000, 0, BattlePosition.Attack, true);
                var d = MockMonster(500, 800, BattlePosition.Defense, true);
                var r = BattleMechanics.Calculate(a, d, attackerHasPiercing: true, false, false, false, false);
                Check("Piercing: destroy DEF", r.DestroyDefender);
                Check("Piercing: 1200 damage", r.DamageToDefendingPlayer == 1200 && r.PiercingApplied);
            }

            // ── Battle: Waboku-style no damage / no destroy ──
            {
                var a = MockMonster(3000, 0, BattlePosition.Attack, true);
                var d = MockMonster(1000, 0, BattlePosition.Attack, true);
                var r = BattleMechanics.Calculate(a, d, false,
                    attackerCannotBeDestroyedByBattle: false,
                    defenderCannotBeDestroyedByBattle: true,
                    noBattleDamageToAttacker: false,
                    noBattleDamageToDefender: true);
                Check("Waboku defender: no destroy", !r.DestroyDefender);
                Check("Waboku defender: no damage", r.DamageToDefendingPlayer == 0);
            }

            // ── Sanitize blocks illegal destroy ──
            {
                var atk = MockMonster(1000, 1000, BattlePosition.Attack, true);
                var def = MockMonster(500, 2000, BattlePosition.Defense, true);
                var r = new BattleMechanics.BattleResult { DestroyDefender = true };
                BattleMechanics.Sanitize(ref r, atk, def);
                Check("Sanitize clears illegal DEF destroy", !r.DestroyDefender);
            }

            // ── Structural: tributes required ──
            {
                Check("Lv4 needs 0 tributes", TcgRules.TributesRequired(4) == 0);
                Check("Lv5 needs 1 tribute", TcgRules.TributesRequired(5) == 1);
                Check("Lv6 needs 1 tribute", TcgRules.TributesRequired(6) == 1);
                Check("Lv7 needs 2 tributes", TcgRules.TributesRequired(7) == 2);
                Check("Lv8 needs 2 tributes", TcgRules.TributesRequired(8) == 2);
            }

            // ── Structural: hand size / LP constants ──
            {
                Check("Starting LP 8000", TcgRules.StartingLifePoints == 8000);
                Check("Starting hand 5", TcgRules.StartingHandSize == 5);
                Check("Hand limit End Phase 6", TcgRules.HandSizeLimitEndPhase == 6);
                Check("Monster zones 5", TcgRules.MonsterZones == 5);
                Check("Spell/Trap zones 5", TcgRules.SpellTrapZones == 5);
            }

            // ── Registry: staples present ──
            {
                Check("Registry: Raigeki", OfficialEffectRegistry.HasActivatableScript(12580477));
                Check("Registry: Pot of Greed", OfficialEffectRegistry.HasActivatableScript(55144522));
                Check("Registry: Mirror Force", OfficialEffectRegistry.HasActivatableScript(44095762));
                Check("Registry: Swords", OfficialEffectRegistry.HasActivatableScript(72302403));
                Check("Registry: Trap Hole", OfficialEffectRegistry.HasActivatableScript(4206964));
                Check("Registry: Ring of Destruction", OfficialEffectRegistry.HasActivatableScript(83555666));
                Check("Registry: Waboku", OfficialEffectRegistry.HasActivatableScript(12607053));
                Check("Unregistered ID refused", !OfficialEffectRegistry.HasActivatableScript(99999999));
            }

            // ── Fusion procedure IDs registered ──
            {
                Check("Fusion: Gaia the Dragon Champion registered",
                    OfficialEffectRegistry.HasSummonProcedure(66889139, SummonKind.FusionSummon));
                Check("Fusion: Black Skull Dragon registered",
                    OfficialEffectRegistry.HasSummonProcedure(11901678, SummonKind.FusionSummon));
            }

            // ── Official PSCT grammar (Konami Part 3) ──
            {
                var abyss = new CardDef
                {
                    id = 18318842,
                    name = "Abyss Soldier",
                    type = "Effect Monster",
                    desc =
                        "Once per turn: You can discard 1 WATER monster to the Graveyard to target 1 card on the field; return it to the hand."
                };
                var abyssSent = PsctGrammar.Parse(abyss.desc, abyss);
                Check("PSCT Abyss Soldier: one chain sentence", abyssSent.Count == 1);
                Check("PSCT Abyss Soldier: colon + semicolon",
                    abyssSent.Count == 1 && abyssSent[0].HasColon && abyssSent[0].HasSemicolon);
                Check("PSCT Abyss Soldier: makes a Chain Link",
                    abyssSent.Count == 1 && abyssSent[0].MakesChainLink);
                Check("PSCT Abyss Soldier: Once per turn ignition",
                    abyssSent.Count == 1 && abyssSent[0].OncePerTurn &&
                    abyssSent[0].SuggestedTiming == EffectTiming.Activate);
                Check("PSCT Abyss Soldier: condition is Once per turn",
                    abyssSent.Count == 1 &&
                    abyssSent[0].Condition.IndexOf("Once per turn", System.StringComparison.OrdinalIgnoreCase) >= 0);
                Check("PSCT Abyss Soldier: resolution is return to hand",
                    abyssSent.Count == 1 &&
                    abyssSent[0].Resolution.IndexOf("return", System.StringComparison.OrdinalIgnoreCase) >= 0);

                var sangan = new CardDef
                {
                    id = 26202165,
                    name = "Sangan",
                    type = "Effect Monster",
                    desc =
                        "If this card is sent from the field to the GY: Add 1 monster with 1500 or less ATK from your Deck to your hand."
                };
                var sg = PsctGrammar.Parse(sangan.desc, sangan);
                Check("PSCT Sangan: colon, no semicolon",
                    sg.Count == 1 && sg[0].HasColon && !sg[0].HasSemicolon && sg[0].MakesChainLink);
                Check("PSCT Sangan: field→GY timing",
                    sg.Count == 1 && sg[0].SuggestedTiming == EffectTiming.SentFromFieldToGy);

                var raigeki = new CardDef
                {
                    id = 12580477,
                    name = "Raigeki",
                    type = "Spell Card",
                    desc = "Destroy all monsters your opponent controls."
                };
                var rg = PsctGrammar.Parse(raigeki.desc, raigeki);
                Check("PSCT Raigeki: Spell with no colon still chains (card activation)",
                    rg.Count == 1 && !rg[0].HasColon && !rg[0].HasSemicolon && rg[0].MakesChainLink);

                var cyber = new CardDef
                {
                    id = 1,
                    name = "Cyber Dragon",
                    type = "Effect Monster",
                    desc =
                        "If only your opponent controls a monster, you can Special Summon this card (from your hand)."
                };
                var cd = PsctGrammar.Parse(cyber.desc, cyber);
                Check("PSCT inherent SS: monster with no colon does NOT start a chain",
                    cd.Count == 1 && !cd[0].HasColon && !cd[0].MakesChainLink && cd[0].IsUnchainedMonsterText);
                Check("PSCT inherent SS: Cyber Dragon text does not block Normal Summon",
                    !PsctGrammar.BlocksNormalSummonOrSet(cyber.desc));

                var alo = new CardDef
                {
                    id = 295517,
                    name = "A Legendary Ocean",
                    type = "Spell Card",
                    race = "Field",
                    desc =
                        "(This card's name is always treated as \"Umi\".) All WATER monsters on the field gain 200 ATK/DEF. Reduce the Level of all WATER monsters in both players' hands and on the field by 1."
                };
                var alos = PsctGrammar.Parse(alo.desc, alo);
                Check("PSCT ALO: parenthetical name is not a Chain Link",
                    alos.Count >= 1 && alos[0].IsParenthetical && !alos[0].MakesChainLink);
                Check("PSCT ALO: Field Spell ATK/Level sentences are continuous, not extra chains",
                    alos.Exists(x =>
                        x.Raw.IndexOf("WATER", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !x.MakesChainLink));

                var mk3 = new CardDef
                {
                    id = 64342551,
                    name = "Amphibious Bugroth MK-3",
                    type = "Effect Monster",
                    race = "Machine",
                    attribute = "WATER",
                    desc =
                        "While \"Umi\" is face-up on the field, this card can attack your opponent's Life Points directly."
                };
                var mk3s = PsctGrammar.Parse(mk3.desc, mk3);
                Check("PSCT MK-3: continuous, no Chain Link",
                    mk3s.Count == 1 && !mk3s[0].MakesChainLink);
                var mk3p = CardTextEffectCompiler.Compile(mk3);
                Check("PSCT MK-3: compiles CanAttackDirectly while Umi",
                    mk3p != null && mk3p.FullyCompiled &&
                    mk3p.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.CanAttackDirectly &&
                        string.Equals(c.RequiresFaceUpName, "Umi", StringComparison.OrdinalIgnoreCase)),
                    mk3p == null
                        ? "null"
                        : $"full={mk3p.FullyCompiled} n={mk3p.ClauseList.Count} unparsed={string.Join("|", mk3p.UnparsedFragments ?? Array.Empty<string>())}");

                var starBoy = new CardDef
                {
                    id = 8201910,
                    name = "Star Boy",
                    type = "Effect Monster",
                    race = "Aqua",
                    attribute = "WATER",
                    desc =
                        "As long as this card remains face-up on the field, increase the ATK of all WATER monsters by 500 points and decrease the ATK of all FIRE monsters by 400 points."
                };
                var sbp = CardTextEffectCompiler.Compile(starBoy);
                Check("PSCT Star Boy: two continuous ATK auras, no Chain Link",
                    sbp != null && sbp.FullyCompiled &&
                    sbp.ClauseList.Count == 2 &&
                    sbp.ClauseList.TrueForAll(c =>
                        c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                        !c.MakesChainLink),
                    sbp == null
                        ? "null"
                        : $"full={sbp.FullyCompiled} n={sbp.ClauseList.Count} unparsed={string.Join("|", sbp.UnparsedFragments ?? Array.Empty<string>())}");
                Check("PSCT Star Boy: WATER +500 ATK-only",
                    sbp != null && sbp.ClauseList.Exists(c =>
                        c != null &&
                        string.Equals(c.AttributeFilter, "WATER", StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == 500 && c.DefAmount == 0));
                Check("PSCT Star Boy: FIRE −400 ATK-only",
                    sbp != null && sbp.ClauseList.Exists(c =>
                        c != null &&
                        string.Equals(c.AttributeFilter, "FIRE", StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == -400 && c.DefAmount == 0));

                YgoProContinuousCatalog.EnsureLoaded();
                Check("YGOPro bulk catalog loaded (Lua auras)",
                    YgoProContinuousCatalog.AuraCardCount > 100,
                    $"auraCards={YgoProContinuousCatalog.AuraCardCount}");
                var starAuras = YgoProContinuousCatalog.AurasFor(8201910);
                Check("YGOPro catalog: Star Boy WATER +500 and FIRE −400",
                    starAuras != null &&
                    starAuras.Count >= 2 &&
                    starAuras.Any(a =>
                        a != null && a.atk == 500 &&
                        string.Equals(a.attribute, "WATER", StringComparison.OrdinalIgnoreCase)) &&
                    starAuras.Any(a =>
                        a != null && a.atk == -400 &&
                        string.Equals(a.attribute, "FIRE", StringComparison.OrdinalIgnoreCase)));
                Check("YGOPro catalog: MK-3 direct while Umi",
                    YgoProContinuousCatalog.DirectsFor(64342551).Any(d =>
                        d != null &&
                        string.Equals(d.requiresName, "Umi", StringComparison.OrdinalIgnoreCase)));
                Check("YGOPro catalog loaded extra-attack grants",
                    YgoProContinuousCatalog.ExtraAttackCardCount > 0,
                    $"extraCards={YgoProContinuousCatalog.ExtraAttackCardCount}");
                Check("YGOPro catalog: Mermaid Knight extra attack while Umi",
                    YgoProContinuousCatalog.ExtraAttacksFor(24435369).Any(e =>
                        e != null && e.extra >= 1 &&
                        string.Equals(e.requiresName, "Umi", StringComparison.OrdinalIgnoreCase)));
                Check("YGOPro catalog: Twinheaded Beast unconditional extra attack",
                    YgoProContinuousCatalog.ExtraAttacksFor(82035781).Any(e =>
                        e != null && e.extra >= 1 && string.IsNullOrEmpty(e.requiresName)));

                var mermaid = new CardDef
                {
                    id = 24435369,
                    name = "Mermaid Knight",
                    type = "Effect Monster",
                    race = "Aqua",
                    attribute = "WATER",
                    desc =
                        "While \"Umi\" is face-up on the field, this card can attack twice during the same Battle Phase."
                };
                var mermaidProg = CardTextEffectCompiler.Compile(mermaid);
                Check("PSCT Mermaid Knight: ExtraAttacks while Umi, no Chain Link",
                    mermaidProg != null && mermaidProg.FullyCompiled &&
                    mermaidProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.ExtraAttacks &&
                        c.Amount == 1 &&
                        !c.MakesChainLink &&
                        string.Equals(c.RequiresFaceUpName, "Umi", StringComparison.OrdinalIgnoreCase)),
                    mermaidProg == null
                        ? "null"
                        : $"full={mermaidProg.FullyCompiled} n={mermaidProg.ClauseList.Count} unparsed={string.Join("|", mermaidProg.UnparsedFragments ?? Array.Empty<string>())}");

                var twinheaded = new CardDef
                {
                    id = 82035781,
                    name = "Twinheaded Beast",
                    type = "Effect Monster",
                    desc = "This card can attack twice during the same Battle Phase."
                };
                var twinProg = CardTextEffectCompiler.Compile(twinheaded);
                Check("PSCT Twinheaded Beast: ExtraAttacks unconditional",
                    twinProg != null && twinProg.FullyCompiled &&
                    twinProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.ExtraAttacks &&
                        c.Amount == 1 &&
                        string.IsNullOrEmpty(c.RequiresFaceUpName)),
                    twinProg == null
                        ? "null"
                        : $"full={twinProg.FullyCompiled} n={twinProg.ClauseList.Count} unparsed={string.Join("|", twinProg.UnparsedFragments ?? Array.Empty<string>())}");

                var grayWing = new CardDef
                {
                    id = 29618570,
                    name = "Gray Wing",
                    type = "Effect Monster",
                    desc =
                        "Discard 1 card from your hand during your Main Phase 1. This monster can attack twice during the Battle Phase of this turn."
                };
                var grayProg = CardTextEffectCompiler.Compile(grayWing);
                Check("PSCT Gray Wing: ignition this-turn is NOT continuous ExtraAttacks",
                    grayProg == null ||
                    !grayProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.ExtraAttacks));

                var bark = new CardDef
                {
                    id = 41925941,
                    name = "Bark of Dark Ruler",
                    type = "Trap Card",
                    race = "Normal",
                    desc =
                        "If a Fiend-Type monster you control battles, during the Damage Step: Pay LP (in multiples of 100 points), then target the opponent's battling monster; that opponent's monster loses that much ATK and DEF, until the end of this turn."
                };
                var barkp = CardTextEffectCompiler.Compile(bark);
                Check("PSCT Bark of Dark Ruler: Damage Step pay-LP ATK/DEF loss",
                    barkp != null && barkp.FullyCompiled &&
                    barkp.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.LoseAtkDefUntilEndOfTurn &&
                        c.Timing == EffectTiming.DamageCalculation &&
                        c.RequiresLpCostMultiple == 100 &&
                        string.Equals(c.RaceFilter, "Fiend", StringComparison.OrdinalIgnoreCase)),
                    barkp == null
                        ? "null"
                        : $"full={barkp.FullyCompiled} n={barkp.ClauseList.Count} unparsed={string.Join("|", barkp.UnparsedFragments ?? Array.Empty<string>())}");

                var sak = new CardDef
                {
                    id = 56120475,
                    name = "Sakuretsu Armor",
                    type = "Trap Card",
                    desc =
                        "When an opponent's monster declares an attack: Target the attacking monster; destroy that target."
                };
                var sakp = CardTextEffectCompiler.Compile(sak);
                Check("PSCT Sakuretsu Armor: AttackDeclared destroy attacker",
                    sakp != null && sakp.FullyCompiled &&
                    sakp.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.Destroy &&
                        c.Timing == EffectTiming.AttackDeclared &&
                        c.Zone == EffectZoneFilter.AttackingMonster),
                    sakp == null
                        ? "null"
                        : $"full={sakp.FullyCompiled} n={sakp.ClauseList.Count} unparsed={string.Join("|", sakp.UnparsedFragments ?? Array.Empty<string>())}");

                var cyl = new CardDef
                {
                    id = 62279055,
                    name = "Magic Cylinder",
                    type = "Trap Card",
                    desc =
                        "When an opponent's monster declares an attack: Target the attacking monster; negate the attack, and if you do, inflict damage to your opponent equal to its ATK."
                };
                var cylp = CardTextEffectCompiler.Compile(cyl);
                Check("PSCT Magic Cylinder: negate attack + damage equal to ATK",
                    cylp != null &&
                    cylp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.NegateThisAttack) &&
                    cylp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.InflictDamageEqualToAtk),
                    cylp == null
                        ? "null"
                        : $"full={cylp.FullyCompiled} n={cylp.ClauseList.Count} unparsed={string.Join("|", cylp.UnparsedFragments ?? Array.Empty<string>())}");

                var drain = new CardDef
                {
                    id = 43250041,
                    name = "Draining Shield",
                    type = "Trap Card",
                    desc =
                        "When an opponent's monster declares an attack: Target the attacking monster; negate that attack, and if you do, gain LP equal to that target's ATK."
                };
                var drainp = CardTextEffectCompiler.Compile(drain);
                Check("PSCT Draining Shield: negate attack + gain LP equal to ATK",
                    drainp != null &&
                    drainp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.NegateThisAttack) &&
                    drainp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.GainLpEqualToAtk),
                    drainp == null
                        ? "null"
                        : $"full={drainp.FullyCompiled} n={drainp.ClauseList.Count} unparsed={string.Join("|", drainp.UnparsedFragments ?? Array.Empty<string>())}");

                YgoProTriggerCatalog.EnsureLoaded();
                Check("YGOPro trigger catalog has Bark + Sakuretsu + Cylinder",
                    YgoProTriggerCatalog.For(41925941) != null &&
                    YgoProTriggerCatalog.For(56120475) != null &&
                    YgoProTriggerCatalog.For(62279055) != null,
                    $"n={YgoProTriggerCatalog.Count}");

                var celtic = new CardDef
                {
                    id = 91152256, name = "Celtic Guardian", type = "Normal Monster",
                    desc = "An elf who learned to wield a sword, he baffles enemies with lightning-swift attacks."
                };
                Check("Master spec: Normal Monster is structural",
                    CardEffectStatus.Classify(celtic) == CardEffectStatusKind.Structural);
                var exiledVocab = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 74131780, name = "Exiled Force", type = "Effect Monster",
                    desc =
                        "You can Tribute this card to target 1 monster on the field; destroy that target."
                });
                var exiledClause = exiledVocab?.ClauseList.Find(c =>
                    c != null && c.Action == EffectActionKind.Destroy);
                Check("Vocabulary: Exiled Force is Tribute → Destroy (shared kind, not a unique card)",
                    exiledClause != null &&
                    EffectVocabulary.CostOf(exiledClause) == EffectCostKind.Tribute &&
                    EffectVocabulary.ResolutionOf(exiledClause) == EffectResolutionKind.Destroy);
                Check("Vocabulary: Time Wizard is UniqueException (call-wrong is not a shared kind)",
                    EffectVocabulary.IsUniqueException(EffectActionKind.CoinCallDestroyOppOrSelf) &&
                    EffectVocabulary.ResolutionOf(new EffectClause
                    {
                        Action = EffectActionKind.CoinCallDestroyOppOrSelf
                    }) == EffectResolutionKind.UniqueException);
                Check("Vocabulary: Thunder Dragon is Discard → Search",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 31786629, name = "Thunder Dragon", type = "Effect Monster",
                        desc =
                            "You can discard this card; add up to 2 \"Thunder Dragon\" from your Deck to your hand."
                    }) is { } tdv &&
                    tdv.ClauseList.Exists(c =>
                        c != null &&
                        EffectVocabulary.CostOf(c) == EffectCostKind.Discard &&
                        EffectVocabulary.ResolutionOf(c) == EffectResolutionKind.Search));
                Check("Vocabulary: shared kinds do not include UniqueException",
                    System.Array.IndexOf(EffectVocabulary.SharedResolutions,
                        EffectResolutionKind.UniqueException) < 0);
                Check("Master spec: Bark of Dark Ruler is implemented",
                    CardEffectStatus.Classify(bark) == CardEffectStatusKind.Implemented);
                Check("Master spec: Sakuretsu Armor is implemented",
                    CardEffectStatus.Classify(sak) == CardEffectStatusKind.Implemented);
                Check("PSCT Sakuretsu 'When' is checked at activation",
                    sakp != null && sakp.ClauseList.Exists(c =>
                        c != null && c.CheckedAt == ConditionCheckedAt.Activation));

                var haDes = new CardDef
                {
                    id = 53982768,
                    name = "Dark Ruler Ha Des",
                    type = "Effect Monster",
                    desc =
                        "Cannot be Special Summoned from the GY. Negate the effects of monsters destroyed by battle with Fiend monsters you control."
                };
                var haStatus = CardEffectStatus.Classify(haDes);
                Check("Master spec: uncompiled Effect Monster is stub or unimplemented (never silent vanilla)",
                    haStatus == CardEffectStatusKind.Unimplemented ||
                    haStatus == CardEffectStatusKind.Stub,
                    haStatus.ToString());

                {
                    var prev = CardEffectStatus.ExcludeUnimplementedFromDecks;
                    CardEffectStatus.ExcludeUnimplementedFromDecks = true;
                    try
                    {
                        var db = CardDatabase.Instance ?? CardDatabase.Load();
                        if (db == null)
                            Check("CardDatabase load for deck gate", false);
                        Check("Deck gate: Normal Monster may enter when exclusion on",
                            CardEffectStatus.MayIncludeInDeck(celtic));
                        Check("Deck gate: Ha Des (uncompiled effect) may NOT enter when exclusion on",
                            !CardEffectStatus.MayIncludeInDeck(haDes));
                        Check("Classify: Ha Des is not Implemented",
                            CardEffectStatus.Classify(haDes) != CardEffectStatusKind.Implemented);

                        if (db != null)
                        {
                            var pot = db.Get(55144522); // Pot of Greed
                            if (pot != null)
                                Check("Deck gate: Pot of Greed (compiled/registry) may enter",
                                    CardEffectStatus.MayIncludeInDeck(pot));
                        }

                        PlayerPrefs.DeleteKey("wrldz_ai_fx_runtime");
                        PlayerPrefs.Save();
                        Check("Runtime AI default is off",
                            !AiEffectCompileSettings.AllowRuntimeAi);

                        Check("ProgramMayActivate: Ha Des false",
                            !OfficialEffectRegistry.ProgramMayActivate(haDes));
                        Check("ProgramMayActivate: Celtic Guardian false (structural)",
                            !OfficialEffectRegistry.ProgramMayActivate(celtic));
                    }
                    finally
                    {
                        CardEffectStatus.ExcludeUnimplementedFromDecks = prev;
                    }
                }

                var gran = new CardDef
                {
                    id = 13944422,
                    name = "Granadora",
                    type = "Effect Monster",
                    desc =
                        "When this monster is Normal Summoned, Flip Summoned or Special Summoned, increase your Life Points by 1000 points. When this card is destroyed and sent to the Graveyard, you take 2000 points of damage."
                };
                var granp = CardTextEffectCompiler.Compile(gran);
                Check("PSCT Granadora: summon +1000 LP and destroyed-to-GY 2000 damage",
                    granp != null && granp.FullyCompiled &&
                    granp.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.ThisCardSummoned &&
                        c.Action == EffectActionKind.GainLifePoints && c.Amount == 1000) &&
                    granp.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.SentFromFieldToGy &&
                        c.Action == EffectActionKind.TakeEffectDamage && c.RequiresDestroyed &&
                        c.Amount == 2000),
                    granp == null
                        ? "null"
                        : $"full={granp.FullyCompiled} n={granp.ClauseList.Count} unparsed={string.Join("|", granp.UnparsedFragments ?? Array.Empty<string>())}");

                var maiden = new CardDef
                {
                    id = 17214465,
                    name = "Maiden of the Aqua",
                    type = "Effect Monster",
                    desc =
                        "As long as this card remains face-up on the field, the field is treated as \"Umi\" (however there is no increasing or decreasing of ATK/DEF due to \"Umi\"'s effect). If there is an active Field Spell Card on the field, this effect is not applied."
                };
                var maidp = CardTextEffectCompiler.Compile(maiden);
                Check("PSCT Maiden of the Aqua: field treated as Umi",
                    maidp != null &&
                    maidp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.FieldTreatedAsName &&
                        string.Equals(c.TreatedAsName, "Umi", StringComparison.OrdinalIgnoreCase)),
                    maidp == null
                        ? "null"
                        : $"full={maidp.FullyCompiled} n={maidp.ClauseList.Count} unparsed={string.Join("|", maidp.UnparsedFragments ?? Array.Empty<string>())}");

                var twall = new CardDef
                {
                    id = 18605135,
                    name = "Tornado Wall",
                    type = "Trap Card",
                    race = "Continuous",
                    desc =
                        "Activate only while \"Umi\" is on the field. While \"Umi\" is face-up on the field, you take no Battle Damage from attacking monsters. Destroy this card when \"Umi\" leaves the field."
                };
                var twp = CardTextEffectCompiler.Compile(twall);
                Check("PSCT Tornado Wall: Umi activate + no battle damage + self-destroy",
                    twp != null && twp.FullyCompiled &&
                    twp.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Activate &&
                        string.Equals(c.RequiresFaceUpName, "Umi", StringComparison.OrdinalIgnoreCase) &&
                        c.StaysOnField) &&
                    twp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.PreventControllerBattleDamage) &&
                    twp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp),
                    twp == null
                        ? "null"
                        : $"full={twp.FullyCompiled} n={twp.ClauseList.Count} unparsed={string.Join("|", twp.UnparsedFragments ?? Array.Empty<string>())}");

                var daedalus = new CardDef
                {
                    id = 37721209,
                    name = "Levia-Dragon - Daedalus",
                    type = "Effect Monster",
                    race = "Sea Serpent",
                    attribute = "WATER",
                    desc =
                        "You can send 1 face-up \"Umi\" you control to the GY; destroy all other cards on the field."
                };
                var daep = CardTextEffectCompiler.Compile(daedalus);
                Check("PSCT ignition: Exiled Force tributes itself and destroys a target",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 74131780, name = "Exiled Force", type = "Effect Monster",
                        desc =
                            "You can Tribute this card to target 1 monster on the field; destroy that target."
                    }) is { } exf && exf.ClauseList.Exists(c =>
                        c != null && c.RequiresTributeThis &&
                        c.Action == EffectActionKind.Destroy && c.RequiresTargetChoice),
                    "compile miss");
                Check("PSCT ignition: Cannon Soldier tributes for 500 damage",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 11384280, name = "Cannon Soldier", type = "Effect Monster",
                        desc = "You can Tribute 1 monster; inflict 500 damage to your opponent."
                    }) is { } csol && csol.ClauseList.Exists(c =>
                        c != null && c.RequiresTributeCount == 1 &&
                        c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 500),
                    "compile miss");
                Check("PSCT ignition: Chaos Sorcerer banishes a face-up monster",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 9596126, name = "Chaos Sorcerer", type = "Effect Monster",
                        desc =
                            "Once per turn: You can target 1 face-up monster on the field; banish that target."
                    }) is { } chaos && chaos.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Banish && c.RequiresTargetChoice),
                    "compile miss");
                Check("PSCT ignition: Des Lacooda sets itself face-down",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 2326738, name = "Des Lacooda", type = "Effect Monster",
                        desc = "Once per turn: You can change this card to face-down Defense Position."
                    }) is { } laco && laco.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense),
                    "compile miss");
                Check("PSCT ignition: Thunder Dragon discards itself from hand and searches",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 31786629, name = "Thunder Dragon", type = "Effect Monster",
                        desc =
                            "You can discard this card; add up to 2 \"Thunder Dragon\" from your Deck to your hand."
                    }) is { } td && td.ClauseList.Exists(c =>
                        c != null && c.RequiresDiscardSelf && c.ActivatesFromHand &&
                        c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                        c.Amount == 2),
                    "compile miss");
                Check("PSCT Time Wizard: call coin, destroy opp or self",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 71625222, name = "Time Wizard", type = "Effect Monster",
                        desc =
                            "Once per turn: You can toss a coin and call it. If you call it right, destroy all monsters your opponent controls. If you call it wrong, destroy as many monsters you control as possible, and if you do, take damage equal to half the total ATK those destroyed monsters had while face-up on the field."
                    }) is { } tw && tw.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.CoinCallDestroyOppOrSelf),
                    "compile miss");
                Check("PSCT Breaker: NS counter + ATK per counter + remove to destroy ST",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 71413901, name = "Breaker the Magical Warrior", type = "Effect Monster",
                        desc =
                            "If this card is Normal Summoned: Place 1 Spell Counter on it (max. 1). Gains 300 ATK for each Spell Counter on it. You can remove 1 Spell Counter from this card, then target 1 Spell/Trap on the field; destroy that target."
                    }) is { } brk &&
                    brk.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.PlaceSpellCounters) &&
                    brk.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.GainAtkPerSpellCounter &&
                        c.Amount == 300) &&
                    brk.ClauseList.Exists(c =>
                        c != null && c.RequiresRemoveSpellCounters == 1 &&
                        c.Action == EffectActionKind.Destroy),
                    "compile miss");
                Check("PSCT Cyber-Stein: pay 5000, SS Fusion from Extra",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 69015963, name = "Cyber-Stein", type = "Effect Monster",
                        desc =
                            "You can pay 5000 LP; Special Summon 1 Fusion Monster from your Extra Deck in Attack Position."
                    }) is { } stein && stein.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.SpecialSummonFusionFromExtra &&
                        c.PayLpAmount == 5000),
                    "compile miss");
                Check("PSCT Lekunga: banish 2 WATER, SS token",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 62543393, name = "Lekunga", type = "Effect Monster",
                        desc =
                            "You can banish 2 WATER monsters from your Graveyard; Special Summon 1 \"Lekunga Token\" (Plant-Type/WATER/Level 2/ATK 700/DEF 700) in Attack Position."
                    }) is { } lek && lek.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonToken &&
                        c.BanishFromGyCount == 2 && c.TokenAtk == 700),
                    "compile miss");
                Check("PSCT Possessed Dark Soul: tribute this, take control LV≤3",
                    CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 52860176, name = "Possessed Dark Soul", type = "Effect Monster",
                        desc =
                            "You can Tribute this face-up card; take control of all face-up Level 3 or lower monsters your opponent currently controls."
                    }) is { } pds && pds.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.TakeControlLevelLeq &&
                        c.RequiresTributeThis && c.Amount == 3),
                    "compile miss");
                var lavaGolemDef = new CardDef
                {
                    id = 102380, name = "Lava Golem", type = "Effect Monster",
                    desc =
                        "Cannot be Normal Summoned/Set. Must first be Special Summoned (from your hand) to your opponent's field by Tributing 2 monsters they control. You cannot Normal Summon/Set the turn you Special Summon this card. Once per turn, during your Standby Phase: Take 1000 damage."
                };
                Check("PSCT Lava Golem: Standby 1000 damage",
                    CardTextEffectCompiler.Compile(lavaGolemDef) is { } lg && lg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.StandbyPhase &&
                        c.Action == EffectActionKind.TakeEffectDamage && c.Amount == 1000),
                    "compile miss");
                Check("PSCT Lava Golem: printed text blocks Normal Summon/Set",
                    PsctGrammar.BlocksNormalSummonOrSet(lavaGolemDef.desc));
                Check("PSCT Daedalus: send face-up named Umi to GY, destroy all other cards",
                    daep != null && daep.FullyCompiled &&
                    daep.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Activate &&
                        c.Action == EffectActionKind.Destroy &&
                        c.RequiresSendNamedToGy &&
                        string.Equals(c.RequiresFaceUpName, "Umi", StringComparison.OrdinalIgnoreCase) &&
                        c.Zone == EffectZoneFilter.AllOtherCardsOnField &&
                        !c.RequiresTargetChoice),
                    daep == null
                        ? "null"
                        : $"full={daep.FullyCompiled} n={daep.ClauseList.Count} unparsed={string.Join("|", daep.UnparsedFragments ?? Array.Empty<string>())}");

                var chimera = new CardDef
                {
                    id = 68658728,
                    name = "Little Chimera",
                    type = "Effect Monster",
                    desc =
                        "All FIRE monsters on the field gain 500 ATK. All WATER monsters on the field lose 400 ATK."
                };
                var chp = CardTextEffectCompiler.Compile(chimera);
                Check("PSCT Little Chimera: FIRE +500 and WATER −400",
                    chp != null && chp.FullyCompiled &&
                    chp.ClauseList.Exists(c =>
                        c != null &&
                        string.Equals(c.AttributeFilter, "FIRE", StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == 500) &&
                    chp.ClauseList.Exists(c =>
                        c != null &&
                        string.Equals(c.AttributeFilter, "WATER", StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == -400),
                    chp == null
                        ? "null"
                        : $"full={chp.FullyCompiled} n={chp.ClauseList.Count} unparsed={string.Join("|", chp.UnparsedFragments ?? Array.Empty<string>())}");

                var ck = new CardDef
                {
                    id = 10375182,
                    name = "Command Knight",
                    type = "Effect Monster",
                    race = "Warrior",
                    desc = "All Warrior monsters you control gain 400 ATK. If you control another monster, monsters your opponent controls cannot target this card for attacks."
                };
                var ckp = CardTextEffectCompiler.Compile(ck);
                Check("PSCT Command Knight: Warrior you-control +400 ATK compiles",
                    ckp != null &&
                    ckp.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.ContinuousGainAtkDef &&
                        string.Equals(c.RaceFilter, "Warrior", StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == 400 &&
                        c.Side == EffectSide.Controller),
                    ckp == null
                        ? "null"
                        : $"full={ckp.FullyCompiled} n={ckp.ClauseList.Count} unparsed={string.Join("|", ckp.UnparsedFragments ?? Array.Empty<string>())}");

                var combo = new CardDef
                {
                    id = 99,
                    name = "Test Ignition Destroy",
                    type = "Effect Monster",
                    desc = "Once per turn: You can target 1 card on the field; destroy it."
                };
                var prog = CardTextEffectCompiler.Compile(combo);
                Check("PSCT fragment: Once per turn target+destroy compiles",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Destroy &&
                        c.OncePerTurn && c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.AnyCardOnField),
                    prog == null
                        ? "null"
                        : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} unparsed={string.Join("|", prog.UnparsedFragments ?? System.Array.Empty<string>())}");
            }

            // ── Spell Speed ──
            {
                var trap = new CardDef { id = 1, type = "Trap Card", race = "Normal" };
                var counter = new CardDef { id = 2, type = "Trap Card", race = "Counter" };
                var qp = new CardDef { id = 3, type = "Spell Card", race = "Quick-Play" };
                var normal = new CardDef { id = 4, type = "Spell Card", race = "Normal" };
                Check("Normal Trap Speed 2", OfficialEffectRegistry.SpeedOf(trap) == SpellSpeed.Speed2);
                Check("Counter Trap Speed 3", OfficialEffectRegistry.SpeedOf(counter) == SpellSpeed.Speed3);
                Check("Quick-Play Speed 2", OfficialEffectRegistry.SpeedOf(qp) == SpellSpeed.Speed2);
                Check("Normal Spell Speed 1", OfficialEffectRegistry.SpeedOf(normal) == SpellSpeed.Speed1);
                var field = new CardDef { id = 295517, type = "Spell Card", race = "Field" };
                Check("Field Spell Speed 1", OfficialEffectRegistry.SpeedOf(field) == SpellSpeed.Speed1);
                Check("Field Spell is not a Trap", field.IsFieldSpell && field.IsSpell && !field.IsTrap);
                Check("Field Spell detection uses race=Field not type",
                    field.IsFieldSpell && field.type.IndexOf("Field", System.StringComparison.OrdinalIgnoreCase) < 0);
            }

            // ── Normal Monster authority ──
            {
                var nm = new CardDef
                {
                    id = 10, type = "Normal Monster", name = "Test", desc = "Flavor only."
                };
                Check("Normal Monster has no effect", OfficialCardAuthority.IsNormalMonsterNoEffect(nm));
                Check("Normal Monster HasNoActivatableEffect", OfficialCardAuthority.HasNoActivatableEffect(nm));
                var bsd = new CardDef
                {
                    id = 11901678,
                    name = "Black Skull Dragon",
                    type = "Fusion Monster",
                    frameType = "fusion",
                    desc = "\"Summoned Skull\" + \"Red-Eyes Black Dragon\"\n\n(This card is always treated as an \"Archfiend\" card.)"
                };
                Check("Black Skull Dragon is effectless Extra", OfficialCardAuthority.HasNoActivatableEffect(bsd));
                Check("Black Skull Dragon is not Normal Monster type", !OfficialCardAuthority.IsNormalMonsterNoEffect(bsd));
                var gaiaF = new CardDef
                {
                    id = 66889139,
                    name = "Gaia the Dragon Champion",
                    type = "Fusion Monster",
                    frameType = "fusion",
                    desc = "\"Gaia The Fierce Knight\" + \"Curse of Dragon\""
                };
                Check("Gaia the Dragon Champion is effectless Extra",
                    OfficialCardAuthority.HasNoActivatableEffect(gaiaF));
                var em = new CardDef
                {
                    id = 11, type = "Effect Monster", name = "Effect", desc = "Once per turn: …"
                };
                Check("Effect Monster not structural-only flavor",
                    !OfficialCardAuthority.IsNormalMonsterNoEffect(em));
            }

            // ── Replay helper ──
            {
                var a = MockMonster(1000, 1000, BattlePosition.Attack, true);
                var defPlayer = new DuelistState("Def", false);
                // empty field after direct when no monsters at declaration → no replay
                Check("Replay: empty after empty direct = no new monsters",
                    !BattleMechanics.NeedsReplay(a, null, defPlayer, hadMonstersAtDeclaration: false));
                // had monsters at declaration false, but now has one → replay for direct
                defPlayer.MonsterZones[0].Occupant = MockMonster(500, 500, BattlePosition.Defense, false);
                Check("Replay: monster appeared after direct declaration",
                    BattleMechanics.NeedsReplay(a, null, defPlayer, hadMonstersAtDeclaration: false));
            }

            // ── Flip-by-battle registration ──
            {
                Check("Man-Eater Bug is Flip-effect monster",
                    MonsterEffects.IsFlipEffectMonster(MonsterEffects.ManEaterBug));
                Check("Magician of Faith is Flip-effect monster",
                    MonsterEffects.IsFlipEffectMonster(MonsterEffects.MagicianOfFaith));
                Check("Lord of D. is Dragon-target protector ID",
                    MonsterEffects.LordOfD == 17985575);
            }

            // ── Kuriboh PSCT (damage calculation hand QE) ──
            {
                var atk = MockMonster(3000, 2500, BattlePosition.Attack, true);
                var r = BattleMechanics.Calculate(atk, null, false, false, false, false, true);
                Check("Kuriboh-style noDmgDef: 0 damage on direct", r.DamageToDefendingPlayer == 0);
                var r2 = BattleMechanics.Calculate(atk, null, false, false, false, false, false);
                Check("Without Kuriboh: direct deals ATK", r2.DamageToDefendingPlayer == 3000);

                var defender = new DuelistState("Def", true);
                var attackerCtrl = new DuelistState("Atk", false);
                var kuri = new CardInstance
                {
                    InstanceId = 1,
                    CardId = MonsterEffects.Kuriboh,
                    Def = new CardDef
                    {
                        id = MonsterEffects.Kuriboh,
                        name = "Kuriboh",
                        type = "Effect Monster",
                        frameType = "effect",
                        desc =
                            "During damage calculation, if your opponent's monster attacks (Quick Effect): " +
                            "You can discard this card; you take no battle damage from that battle."
                    }
                };
                defender.Hand.Add(kuri);
                var attacker = MockMonster(2500, 2100, BattlePosition.Attack, true);
                Check("Kuriboh legal in DamageCalculation vs opponent attack",
                    MonsterEffects.IsLegalHandDamageCalculationEffect(
                        defender, kuri, ResponseTiming.DamageCalculation, attackerCtrl, attacker));
                Check("Kuriboh illegal at AttackDeclared",
                    !MonsterEffects.IsLegalHandDamageCalculationEffect(
                        defender, kuri, ResponseTiming.AttackDeclared, attackerCtrl, attacker));
                Check("Kuriboh illegal if you are the attacker",
                    !MonsterEffects.IsLegalHandDamageCalculationEffect(
                        defender, kuri, ResponseTiming.DamageCalculation, defender, attacker));
                Check("Kuriboh is registered monster effect",
                    MonsterEffects.IsRegisteredMonsterEffect(MonsterEffects.Kuriboh));

                // Full cost: discard from hand → GY by InstanceId
                var handCountBefore = defender.Hand.Count;
                var gyBefore = defender.Graveyard.Count;
                var discarded = MonsterEffects.DiscardFromHandByInstance(defender, kuri);
                Check("Kuriboh discard removes from hand",
                    discarded != null && defender.Hand.Count == handCountBefore - 1);
                Check("Kuriboh discard places in GY",
                    defender.Graveyard.Count == gyBefore + 1 &&
                    defender.Graveyard.Exists(c => c != null && c.InstanceId == kuri.InstanceId));
            }

            // ── cards_db corpus: shared kinds compile for any matching text ──
            {
                var db = CardDatabase.Load();
                Check("Corpus: cards_db loaded", db != null && db.Count > 1000,
                    db == null ? "null" : $"count={db.Count}");
                if (db != null)
                {
                    var all = EffectCoverageService.MeasureAllCardsDb(db, writeReportFile: false);
                    Check("Corpus: entire cards_db is classified (not just lab decks)",
                        all.InDatabase >= 1400, $"inDB={all.InDatabase}");

                    var gaps = EffectCoverageService.SharedKindCompileGaps(db);
                    Check("Corpus: shared-kind texts in cards_db compile (new cards with these shapes work)",
                        gaps.Count == 0,
                        gaps.Count == 0 ? "" : string.Join("; ", gaps.Take(8)));

                    var fish = db.Get(3643300);
                    var fp = fish != null ? CardTextEffectCompiler.Compile(fish) : null;
                    Check("Corpus: Legendary Fisherman compiles Protection (Umi lock)",
                        fp != null &&
                        fp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.CannotBeAttackTarget &&
                            c.AllowsDirectAttackWhileProtected &&
                            string.Equals(c.RequiresFaceUpName, "Umi",
                                StringComparison.OrdinalIgnoreCase)) &&
                        fp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.UnaffectedByCardEffects &&
                            string.Equals(c.UnaffectedByFilter, "Spell",
                                StringComparison.OrdinalIgnoreCase)),
                        fp == null
                            ? "null"
                            : $"n={fp.ClauseList.Count} unparsed={string.Join("|", fp.UnparsedFragments ?? Array.Empty<string>())}");

                    var synthetic = new CardDef
                    {
                        id = 90000001,
                        name = "Umi Guardian (new-card shape)",
                        type = "Effect Monster",
                        desc =
                            "While \"Umi\" is on the field, this card is unaffected by Spell effects and cannot be targeted for attacks, but does not prevent your opponent from attacking you directly."
                    };
                    var syn = CardTextEffectCompiler.Compile(synthetic);
                    Check("New-card rule: Fisherman-shaped text compiles without a cardId branch",
                        syn != null &&
                        syn.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.CannotBeAttackTarget) &&
                        syn.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.UnaffectedByCardEffects),
                        syn == null ? "null" : $"n={syn.ClauseList.Count}");

                    var hay = db.Get(21015833);
                    var hp = hay != null ? CardTextEffectCompiler.Compile(hay) : null;
                    Check("Corpus: Hayabusa Knight compiles ExtraAttacks (second attack wording)",
                        hp != null && hp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.ExtraAttacks && c.Amount == 1));

                    var moon = db.Get(14087893);
                    var mp = moon != null ? CardTextEffectCompiler.Compile(moon) : null;
                    Check("Corpus: Book of Moon compiles SetTargetFaceDownDefense",
                        mp != null && mp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SetTargetFaceDownDefense));

                    var treasure = db.Get(1435851);
                    var tp = treasure != null ? CardTextEffectCompiler.Compile(treasure) : null;
                    Check("Corpus: Dragon Treasure compiles Equip +300/+300",
                        tp != null && tp.FullyCompiled && tp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Dragon", StringComparison.OrdinalIgnoreCase)));

                    var sala = db.Get(32268901);
                    var sp = sala != null ? CardTextEffectCompiler.Compile(sala) : null;
                    Check("Corpus: Salamandra FullyCompiled Equip only FIRE +700",
                        sp != null && sp.FullyCompiled &&
                        sp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 700 &&
                            string.Equals(c.AttributeFilter, "FIRE", StringComparison.OrdinalIgnoreCase)),
                        sp == null
                            ? "null"
                            : $"full={sp.FullyCompiled} unparsed={string.Join("|", sp.UnparsedFragments ?? Array.Empty<string>())}");

                    var sword = db.Get(61854111);
                    var swp = sword != null ? CardTextEffectCompiler.Compile(sword) : null;
                    Check("Corpus: Legendary Sword FullyCompiled Equip only Warrior +300/+300",
                        swp != null && swp.FullyCompiled &&
                        swp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Warrior", StringComparison.OrdinalIgnoreCase)),
                        swp == null
                            ? "null"
                            : $"full={swp.FullyCompiled} unparsed={string.Join("|", swp.UnparsedFragments ?? Array.Empty<string>())}");

                    var axeDef = db.Get(40619825);
                    var axeProg = axeDef != null ? CardTextEffectCompiler.Compile(axeDef) : null;
                    Check("Corpus: Axe of Despair FullyCompiled Equip +1000 and GY to top of Deck",
                        axeProg != null && axeProg.FullyCompiled &&
                        axeProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 1000) &&
                        axeProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.SentFromFieldToGy &&
                            c.Action == EffectActionKind.PlaceThisOnTopOfDeck &&
                            c.RequiresTributeCount == 1),
                        axeProg == null
                            ? "null"
                            : $"full={axeProg.FullyCompiled} unparsed={string.Join("|", axeProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var stand = db.Get(56747793);
                    var standProg = stand != null ? CardTextEffectCompiler.Compile(stand) : null;
                    Check("Corpus: United We Stand FullyCompiled +800 ATK/DEF per face-up monster",
                        standProg != null && standProg.FullyCompiled &&
                        standProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 800 && c.EquipDefBonus == 800 &&
                            c.ScaleAmountByControllerMonsters),
                        standProg == null
                            ? "null"
                            : $"full={standProg.FullyCompiled} unparsed={string.Join("|", standProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var mage = db.Get(83746708);
                    var mageProg = mage != null ? CardTextEffectCompiler.Compile(mage) : null;
                    Check("Corpus: Mage Power FullyCompiled +500 ATK/DEF per Spell/Trap",
                        mageProg != null && mageProg.FullyCompiled &&
                        mageProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 500 && c.ScaleAmountByControllerSpellTraps));

                    var synStand = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000009,
                        name = "New Equip (United We Stand shape)",
                        type = "Spell Card",
                        race = "Equip",
                        frameType = "equip",
                        desc = "The equipped monster gains 800 ATK/DEF for each face-up monster you control."
                    });
                    Check("New-card rule: per-monster Equip ATK/DEF compiles without a cardId branch",
                        synStand != null && synStand.FullyCompiled &&
                        synStand.ClauseList.Exists(c =>
                            c != null && c.ScaleAmountByControllerMonsters &&
                            c.EquipAtkBonus == 800));

                    var synEq = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000006,
                        name = "New Equip (Salamandra shape)",
                        type = "Spell Card",
                        race = "Equip",
                        frameType = "equip",
                        desc = "Equip only to a FIRE monster. It gains 700 ATK."
                    });
                    Check("New-card rule: Equip-only FIRE +ATK compiles without a cardId branch",
                        synEq != null && synEq.FullyCompiled &&
                        synEq.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 700 &&
                            string.Equals(c.AttributeFilter, "FIRE", StringComparison.OrdinalIgnoreCase)));

                    var fall = db.Get(32919136);
                    var fallProg = fall != null ? CardTextEffectCompiler.Compile(fall) : null;
                    Check("Corpus: Falling Down FullyCompiled opponent-equip take-control",
                        fallProg != null && fallProg.FullyCompiled &&
                        fallProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.TakeControlOfTarget &&
                            c.Zone == EffectZoneFilter.OppFaceUpMonsters) &&
                        fallProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp &&
                            c.RequiresControllerNamedCard &&
                            string.Equals(c.RequiresFaceUpName, "Archfiend",
                                StringComparison.OrdinalIgnoreCase)) &&
                        fallProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.StandbyPhase &&
                            c.OpponentTurnOnly &&
                            c.Action == EffectActionKind.TakeEffectDamage &&
                            c.Amount == 800),
                        fallProg == null
                            ? "null"
                            : $"full={fallProg.FullyCompiled} n={fallProg.ClauseList.Count} unparsed={string.Join("|", fallProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synFall = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000007,
                        name = "New Equip (Falling Down shape)",
                        type = "Spell Card",
                        race = "Equip",
                        frameType = "equip",
                        desc =
                            "Activate this card by targeting an opponent's monster; equip this card to it. Take control of it. Destroy this card unless you control an \"Archfiend\" card. During each of your opponent's Standby Phases: You take 800 damage."
                    });
                    Check("New-card rule: Falling Down-shaped text compiles without a cardId branch",
                        synFall != null && synFall.FullyCompiled &&
                        synFall.ClauseList.Exists(c =>
                            c != null && c.TakeControlOfTarget &&
                            c.Zone == EffectZoneFilter.OppFaceUpMonsters));

                    var coh = db.Get(4031928);
                    var cohProg = coh != null ? CardTextEffectCompiler.Compile(coh) : null;
                    Check("Corpus: Change of Heart FullyCompiled take-control until End Phase",
                        cohProg != null && cohProg.FullyCompiled &&
                        cohProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.TakeControlTarget &&
                            c.RequiresTargetChoice &&
                            c.Zone == EffectZoneFilter.OppFaceUpMonsters),
                        cohProg == null
                            ? "null"
                            : $"full={cohProg.FullyCompiled} n={cohProg.ClauseList.Count} unparsed={string.Join("|", cohProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var tamer = db.Get(37620434);
                    var tamerProg = tamer != null ? CardTextEffectCompiler.Compile(tamer) : null;
                    Check("Corpus: Shadow Tamer FullyCompiled Flip Fiend take-control until End Phase",
                        tamerProg != null && tamerProg.FullyCompiled &&
                        tamerProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.TakeControlTarget &&
                            c.RequiresTargetChoice &&
                            string.Equals(c.RaceFilter, "Fiend", StringComparison.OrdinalIgnoreCase)) &&
                        tamerProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.AlwaysTreatedAsName &&
                            string.Equals(c.TreatedAsName, "Archfiend", StringComparison.OrdinalIgnoreCase)),
                        tamerProg == null
                            ? "null"
                            : $"full={tamerProg.FullyCompiled} n={tamerProg.ClauseList.Count} unparsed={string.Join("|", tamerProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var manip = db.Get(63018132);
                    var manipProg = manip != null ? CardTextEffectCompiler.Compile(manip) : null;
                    Check("Corpus: Dragon Manipulator FullyCompiled Flip Dragon take-control until End Phase",
                        manipProg != null && manipProg.FullyCompiled &&
                        manipProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.TakeControlTarget &&
                            c.RequiresTargetChoice &&
                            string.Equals(c.RaceFilter, "Dragon", StringComparison.OrdinalIgnoreCase)),
                        manipProg == null
                            ? "null"
                            : $"full={manipProg.FullyCompiled} n={manipProg.ClauseList.Count} unparsed={string.Join("|", manipProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synCoh = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000056,
                        name = "New Spell (Change of Heart shape)",
                        type = "Spell Card",
                        race = "Normal",
                        desc = "Target 1 monster your opponent controls; take control of it until the End Phase."
                    });
                    Check("New-card rule: take-control until End Phase compiles without a cardId branch",
                        synCoh != null && synCoh.FullyCompiled &&
                        synCoh.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.TakeControlTarget &&
                            c.RequiresTargetChoice));

                    var synTamer = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000057,
                        name = "New Flip (Shadow Tamer shape)",
                        type = "Flip Effect Monster",
                        desc =
                            "FLIP: Target 1 Aqua-Type monster your opponent controls; take control of that target until the End Phase."
                    });
                    Check("New-card rule: Flip Type take-control until End Phase compiles without a cardId branch",
                        synTamer != null && synTamer.FullyCompiled &&
                        synTamer.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.TakeControlTarget &&
                            string.Equals(c.RaceFilter, "Aqua", StringComparison.OrdinalIgnoreCase)));

                    var synManip = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000058,
                        name = "New Flip (Dragon Manipulator shape)",
                        type = "Flip Effect Monster",
                        desc =
                            "FLIP: Take control of 1 face-up Insect-Type monster on your opponent's side of the field until the end of the End Phase."
                    });
                    Check("New-card rule: old Flip take-control wording compiles without a cardId branch",
                        synManip != null && synManip.FullyCompiled &&
                        synManip.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.TakeControlTarget &&
                            string.Equals(c.RaceFilter, "Insect", StringComparison.OrdinalIgnoreCase)));

                    var eria = db.Get(74364659);
                    var eriaProg = eria != null ? CardTextEffectCompiler.Compile(eria) : null;
                    Check("Corpus: Eria the Water Charmer while-face-up leftover is not FullyCompiled",
                        eriaProg == null || !eriaProg.FullyCompiled);

                    var jowls = db.Get(5257687);
                    var jowlsProg = jowls != null ? CardTextEffectCompiler.Compile(jowls) : null;
                    Check("Corpus: Jowls of Dark Demise extra rider is not FullyCompiled",
                        jowlsProg == null || !jowlsProg.FullyCompiled);

                    var ec = db.Get(98045062);
                    var ecProg = ec != null ? CardTextEffectCompiler.Compile(ec) : null;
                    Check("Corpus: Enemy Controller choice leftover is not FullyCompiled",
                        ecProg == null || !ecProg.FullyCompiled);

                    var brain = db.Get(87910978);
                    var brainProg = brain != null ? CardTextEffectCompiler.Compile(brain) : null;
                    Check("Corpus: Brain Control Normal Summoned/Set leftover is not FullyCompiled",
                        brainProg == null || !brainProg.FullyCompiled);

                    var rec = db.Get(74848038);
                    var recProg = rec != null ? CardTextEffectCompiler.Compile(rec) : null;
                    Check("Corpus: Monster Reincarnation FullyCompiled discard + GY monster add",
                        recProg != null && recProg.FullyCompiled &&
                        recProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.AddFromGyToHand &&
                            c.RequiresDiscardCost &&
                            c.Zone == EffectZoneFilter.ControllerGyMonsters),
                        recProg == null
                            ? "null"
                            : $"full={recProg.FullyCompiled} n={recProg.ClauseList.Count} unparsed={string.Join("|", recProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var ecto = db.Get(97342942);
                    var ectoProg = ecto != null ? CardTextEffectCompiler.Compile(ecto) : null;
                    Check("Corpus: Ectoplasmer FullyCompiled End Phase turn-player tribute + half original ATK",
                        ectoProg != null && ectoProg.FullyCompiled &&
                        ectoProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.EndPhase &&
                            c.Action == EffectActionKind.InflictDamageHalfTributedAtk &&
                            c.RequiresTributeCount == 1 &&
                            c.TributeFaceUpOnly &&
                            c.TurnPlayerTributes &&
                            c.StaysOnField),
                        ectoProg == null
                            ? "null"
                            : $"full={ectoProg.FullyCompiled} n={ectoProg.ClauseList.Count} unparsed={string.Join("|", ectoProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synEcto = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000010,
                        name = "New Continuous (Ectoplasmer shape)",
                        type = "Spell Card",
                        race = "Continuous",
                        frameType = "spell",
                        desc =
                            "Once per turn, during each player's End Phase: The turn player must Tribute 1 face-up monster, and if they do, inflict damage to their opponent equal to half the original ATK of the Tributed monster."
                    });
                    Check("New-card rule: End Phase turn-player tribute + half original ATK compiles without a cardId branch",
                        synEcto != null && synEcto.FullyCompiled &&
                        synEcto.ClauseList.Exists(c =>
                            c != null &&
                            c.TurnPlayerTributes &&
                            c.TributeFaceUpOnly &&
                            c.Action == EffectActionKind.InflictDamageHalfTributedAtk));

                    var levelLimit = db.Get(3136426);
                    var llProg = levelLimit != null ? CardTextEffectCompiler.Compile(levelLimit) : null;
                    Check("Corpus: Level Limit - Area B leftover unique is not FullyCompiled",
                        llProg == null || !llProg.FullyCompiled);

                    var jam = db.Get(21770260);
                    var jamProg = jam != null ? CardTextEffectCompiler.Compile(jam) : null;
                    Check("Corpus: Jam Breeding Machine leftover unique is not FullyCompiled",
                        jamProg == null || !jamProg.FullyCompiled);

                    var toon = db.Get(15259703);
                    var toonProg = toon != null ? CardTextEffectCompiler.Compile(toon) : null;
                    Check("Corpus: Toon World FullyCompiled pay-1000 Activate",
                        toonProg != null && toonProg.FullyCompiled &&
                        toonProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate && c.PayLpAmount == 1000),
                        toonProg == null
                            ? "null"
                            : $"full={toonProg.FullyCompiled} unparsed={string.Join("|", toonProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synToon = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000011,
                        name = "New Continuous (Toon World shape)",
                        type = "Spell Card",
                        race = "Continuous",
                        desc = "Activate this card by paying 1000 LP."
                    });
                    Check("New-card rule: pay-LP Continuous Activate compiles without a cardId branch",
                        synToon != null && synToon.FullyCompiled &&
                        synToon.ClauseList.Exists(c => c != null && c.PayLpAmount == 1000));

                    var laby = db.Get(66526672);
                    var labyProg = laby != null ? CardTextEffectCompiler.Compile(laby) : null;
                    Check("Corpus: Labyrinth of Nightmare FullyCompiled End Phase turn-player positions",
                        labyProg != null && labyProg.FullyCompiled &&
                        labyProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.EndPhase &&
                            c.TurnPlayerIsSubject &&
                            c.Action == EffectActionKind.ChangeBattlePosition));

                    var burn = db.Get(24294108);
                    var burnProg = burn != null ? CardTextEffectCompiler.Compile(burn) : null;
                    Check("Corpus: Burning Land FullyCompiled destroy Field Spells + turn-player Standby damage",
                        burnProg != null && burnProg.FullyCompiled &&
                        burnProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.Destroy &&
                            c.Zone == EffectZoneFilter.FieldSpellsOnField) &&
                        burnProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.StandbyPhase &&
                            c.TurnPlayerIsSubject &&
                            c.Action == EffectActionKind.TakeEffectDamage &&
                            c.Amount == 500),
                        burnProg == null
                            ? "null"
                            : $"full={burnProg.FullyCompiled} unparsed={string.Join("|", burnProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var grav = db.Get(85742772);
                    var gravProg = grav != null ? CardTextEffectCompiler.Compile(grav) : null;
                    Check("Corpus: Gravity Bind FullyCompiled Level 4+ cannot attack",
                        gravProg != null && gravProg.FullyCompiled &&
                        gravProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.ContinuousCannotAttack &&
                            c.AmountIsLevel && c.Amount == 4));

                    var ibar = db.Get(23615409);
                    var ibProg = ibar != null ? CardTextEffectCompiler.Compile(ibar) : null;
                    Check("Corpus: Insect Barrier FullyCompiled opponent Insect cannot attack",
                        ibProg != null && ibProg.FullyCompiled &&
                        ibProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.ContinuousCannotAttack &&
                            string.Equals(c.RaceFilter, "Insect", StringComparison.OrdinalIgnoreCase) &&
                            c.Side == EffectSide.Opponent));

                    var mop = db.Get(44656491);
                    var mopProg = mop != null ? CardTextEffectCompiler.Compile(mop) : null;
                    Check("Corpus: Messenger of Peace FullyCompiled ATK≥1500 lock + pay 100 or destroy",
                        mopProg != null && mopProg.FullyCompiled &&
                        mopProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.ContinuousCannotAttack &&
                            c.Amount == 1500) &&
                        mopProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.PayLpOrDestroyThis &&
                            c.PayLpAmount == 100),
                        mopProg == null
                            ? "null"
                            : $"full={mopProg.FullyCompiled} unparsed={string.Join("|", mopProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var call = db.Get(97077563);
                    var callProg = call != null ? CardTextEffectCompiler.Compile(call) : null;
                    Check("Corpus: Call of the Haunted FullyCompiled GY SS + leave-field destroy",
                        callProg != null && callProg.FullyCompiled &&
                        callProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.DestroyHostWhenThisLeaves &&
                            !c.SummonInDefense));

                    var soul = db.Get(92924317);
                    var soulProg = soul != null ? CardTextEffectCompiler.Compile(soul) : null;
                    Check("Corpus: Soul Resurrection FullyCompiled Normal Monster GY SS in Defense",
                        soulProg != null && soulProg.FullyCompiled &&
                        soulProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.SummonInDefense &&
                            c.RequiresNormalMonster));

                    var skill = db.Get(82732705);
                    var skillProg = skill != null ? CardTextEffectCompiler.Compile(skill) : null;
                    Check("Corpus: Skill Drain leftover unique is not FullyCompiled",
                        skillProg == null || !skillProg.FullyCompiled);

                    var warrior = db.Get(95281259);
                    var wProg = warrior != null ? CardTextEffectCompiler.Compile(warrior) : null;
                    Check("Corpus: The Warrior Returning Alive FullyCompiled Warrior GY add",
                        wProg != null && wProg.FullyCompiled &&
                        wProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.AddFromGyToHand &&
                            c.Zone == EffectZoneFilter.ControllerGyMonsters));

                    var mask = db.Get(28933734);
                    var maskProg = mask != null ? CardTextEffectCompiler.Compile(mask) : null;
                    Check("Corpus: Mask of Darkness FullyCompiled Flip Trap GY add",
                        maskProg != null && maskProg.FullyCompiled &&
                        maskProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.AddFromGyToHand &&
                            c.Zone == EffectZoneFilter.ControllerGyTraps));

                    var spellRepro = db.Get(29228529);
                    var srProg = spellRepro != null ? CardTextEffectCompiler.Compile(spellRepro) : null;
                    Check("Corpus: Spell Reproduction leftover send-2 is not FullyCompiled",
                        srProg != null && !srProg.FullyCompiled);

                    var giantRat = db.Get(97017120);
                    var ratProg = giantRat != null ? CardTextEffectCompiler.Compile(giantRat) : null;
                    Check("Corpus: Giant Rat battle-destroyed EARTH 1500 Deck SS is not a free ignition",
                        ratProg != null && ratProg.FullyCompiled &&
                        !ratProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        ratProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.SentFromFieldToGy &&
                            c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromDeck &&
                            c.AmountIsAtkMax && c.Amount == 1500 &&
                            string.Equals(c.AttributeFilter, "EARTH", StringComparison.OrdinalIgnoreCase)),
                        ratProg == null
                            ? "null"
                            : $"full={ratProg.FullyCompiled} n={ratProg.ClauseList.Count} " +
                              $"unparsed={string.Join("|", ratProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var fox = db.Get(88753985);
                    var foxProg = fox != null ? CardTextEffectCompiler.Compile(fox) : null;
                    Check("Corpus: Fox Fire End Phase battle-GY self-SS is not a free ignition",
                        foxProg != null && foxProg.FullyCompiled &&
                        !foxProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        foxProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.EndPhase &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.ResolvesFromGy &&
                            c.RequiresThisDestroyedByBattle) &&
                        foxProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.CannotBeTributedForSummon),
                        foxProg == null
                            ? "null"
                            : $"full={foxProg.FullyCompiled} n={foxProg.ClauseList.Count} " +
                              $"unparsed={string.Join("|", foxProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var witch = db.Get(78010363);
                    var witchProg = witch != null ? CardTextEffectCompiler.Compile(witch) : null;
                    Check("Corpus: Witch of the Black Forest DEF 1500 search is not a free ignition",
                        witchProg != null && witchProg.FullyCompiled &&
                        !witchProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        witchProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.SentFromFieldToGy &&
                            c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.AmountIsDefMax && c.Amount == 1500),
                        witchProg == null
                            ? "null"
                            : $"full={witchProg.FullyCompiled} n={witchProg.ClauseList.Count} " +
                              $"unparsed={string.Join("|", witchProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var germ = db.Get(95178994);
                    var germProg = germ != null ? CardTextEffectCompiler.Compile(germ) : null;
                    Check("Corpus: Giant Germ battle-GY 500 burn is not a free ignition",
                        germProg != null &&
                        !germProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        germProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.InflictDamageToOpponent &&
                            c.Amount == 500),
                        germProg == null
                            ? "null"
                            : $"full={germProg.FullyCompiled} n={germProg.ClauseList.Count} " +
                              $"unparsed={string.Join("|", germProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var tomato = db.Get(83011277);
                    var tomatoProg = tomato != null ? CardTextEffectCompiler.Compile(tomato) : null;
                    Check("Corpus: Mystic Tomato battle-GY DARK 1500 Deck SS is not a free ignition",
                        tomatoProg != null && tomatoProg.FullyCompiled &&
                        tomatoProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromDeck && c.AmountIsAtkMax && c.Amount == 1500 &&
                            string.Equals(c.AttributeFilter, "DARK", StringComparison.OrdinalIgnoreCase)),
                        tomatoProg == null
                            ? "null"
                            : $"full={tomatoProg.FullyCompiled} unparsed={string.Join("|", tomatoProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var birdface = db.Get(45547649);
                    var bfProg = birdface != null ? CardTextEffectCompiler.Compile(birdface) : null;
                    Check("Corpus: Birdface battle-GY named Harpie Lady search is not a free ignition",
                        bfProg != null && bfProg.FullyCompiled &&
                        bfProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            string.Equals(c.NamedCard, "Harpie Lady", StringComparison.OrdinalIgnoreCase)),
                        bfProg == null
                            ? "null"
                            : $"full={bfProg.FullyCompiled} unparsed={string.Join("|", bfProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var orchis = db.Get(46571052);
                    var orchisProg = orchis != null ? CardTextEffectCompiler.Compile(orchis) : null;
                    Check("Corpus: Vampiric Orchis NS named hand SS is not a free ignition",
                        orchisProg != null && orchisProg.FullyCompiled &&
                        !orchisProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        orchisProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.ThisCardSummoned &&
                            c.RequiresThisNormalSummoned &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromHand &&
                            string.Equals(c.NamedCard, "Des Dendle", StringComparison.OrdinalIgnoreCase)),
                        orchisProg == null
                            ? "null"
                            : $"full={orchisProg.FullyCompiled} unparsed={string.Join("|", orchisProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var scarabs = db.Get(15383415);
                    var scarabsProg = scarabs != null ? CardTextEffectCompiler.Compile(scarabs) : null;
                    Check("Corpus: Swarm of Scarabs Flip Summon destroy is not a free extra ignition",
                        scarabsProg != null && scarabsProg.FullyCompiled &&
                        scarabsProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SetThisFaceDownDefense) &&
                        scarabsProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisFlipSummoned &&
                            c.Action == EffectActionKind.Destroy &&
                            c.RequiresTargetChoice),
                        scarabsProg == null
                            ? "null"
                            : $"full={scarabsProg.FullyCompiled} unparsed={string.Join("|", scarabsProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var locusts = db.Get(41872150);
                    var locustsProg = locusts != null ? CardTextEffectCompiler.Compile(locusts) : null;
                    Check("Corpus: Swarm of Locusts Flip Summon ST destroy is not a free extra ignition",
                        locustsProg != null && locustsProg.FullyCompiled &&
                        locustsProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisFlipSummoned &&
                            c.Action == EffectActionKind.Destroy &&
                            c.Zone == EffectZoneFilter.FieldSpellTraps),
                        locustsProg == null
                            ? "null"
                            : $"full={locustsProg.FullyCompiled} unparsed={string.Join("|", locustsProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var crater = db.Get(78243409);
                    var craterProg = crater != null ? CardTextEffectCompiler.Compile(crater) : null;
                    Check("Corpus: The Thing in the Crater destroyed-field Pyro hand SS is not battle-only",
                        craterProg != null && craterProg.FullyCompiled &&
                        !craterProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        craterProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresDestroyed &&
                            !c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.SpecialSummonFromHand &&
                            string.Equals(c.RaceFilter, "Pyro", StringComparison.OrdinalIgnoreCase)),
                        craterProg == null
                            ? "null"
                            : $"full={craterProg.FullyCompiled} unparsed={string.Join("|", craterProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var witchDoc = db.Get(75946257);
                    var wdProg = witchDoc != null ? CardTextEffectCompiler.Compile(witchDoc) : null;
                    Check("Corpus: Witch Doctor of Chaos FullyCompiled Flip either-GY banish",
                        wdProg != null && wdProg.FullyCompiled &&
                        !wdProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        wdProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.Banish &&
                            c.Zone == EffectZoneFilter.EitherGyMonsters &&
                            c.RequiresTargetChoice),
                        wdProg == null
                            ? "null"
                            : $"full={wdProg.FullyCompiled} n={wdProg.ClauseList.Count} " +
                              $"unparsed={string.Join("|", wdProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var statue = db.Get(75209824);
                    var statueProg = statue != null ? CardTextEffectCompiler.Compile(statue) : null;
                    Check("Corpus: Guardian Statue FullyCompiled Flip Summon bounce + set-FD",
                        statueProg != null && statueProg.FullyCompiled &&
                        statueProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SetThisFaceDownDefense &&
                            c.OncePerTurn) &&
                        statueProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisFlipSummoned &&
                            c.Action == EffectActionKind.ReturnToHand &&
                            c.RequiresTargetChoice),
                        statueProg == null
                            ? "null"
                            : $"full={statueProg.FullyCompiled} unparsed={string.Join("|", statueProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var medusa = db.Get(2694423);
                    var medusaProg = medusa != null ? CardTextEffectCompiler.Compile(medusa) : null;
                    Check("Corpus: Medusa Worm FullyCompiled Flip Summon destroy + set-FD",
                        medusaProg != null && medusaProg.FullyCompiled &&
                        medusaProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SetThisFaceDownDefense &&
                            c.OncePerTurn) &&
                        medusaProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisFlipSummoned &&
                            c.Action == EffectActionKind.Destroy &&
                            c.RequiresTargetChoice),
                        medusaProg == null
                            ? "null"
                            : $"full={medusaProg.FullyCompiled} unparsed={string.Join("|", medusaProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var moai = db.Get(45159319);
                    var moaiProg = moai != null ? CardTextEffectCompiler.Compile(moai) : null;
                    Check("Corpus: Moai Interceptor Cannons FullyCompiled OPT set-FD",
                        moaiProg != null && moaiProg.FullyCompiled &&
                        moaiProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SetThisFaceDownDefense &&
                            c.OncePerTurn),
                        moaiProg == null
                            ? "null"
                            : $"full={moaiProg.FullyCompiled} unparsed={string.Join("|", moaiProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var immortal = db.Get(84926738);
                    var immortalProg = immortal != null ? CardTextEffectCompiler.Compile(immortal) : null;
                    Check("Corpus: Immortal of Thunder Flip gain 3000 LP compiles; GY lose leftover",
                        immortalProg != null && !immortalProg.FullyCompiled &&
                        immortalProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.GainLifePoints &&
                            c.Amount == 3000),
                        immortalProg == null
                            ? "null"
                            : $"full={immortalProg.FullyCompiled} unparsed={string.Join("|", immortalProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var spirit = db.Get(48659020);
                    var spiritProg = spirit != null ? CardTextEffectCompiler.Compile(spirit) : null;
                    Check("Corpus: Spirit Caller FullyCompiled Flip Level-3-or-lower Normal GY SS",
                        spiritProg != null && spiritProg.FullyCompiled &&
                        spiritProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                            c.RequiresNormalMonster &&
                            c.AmountIsLevel && c.Amount == 3 &&
                            c.RequiresTargetChoice && c.IsOptional),
                        spiritProg == null
                            ? "null"
                            : $"full={spiritProg.FullyCompiled} unparsed={string.Join("|", spiritProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synSpirit = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000041,
                        name = "Flip GY Normal SS (new-card shape)",
                        type = "Flip Effect Monster",
                        desc =
                            "FLIP: You can Special Summon 1 Level 3 or lower Normal Monster from your Graveyard to your side of the field."
                    });
                    Check("New-card rule: Spirit Caller-shaped text compiles without a cardId branch",
                        synSpirit != null && synSpirit.FullyCompiled &&
                        synSpirit.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.RequiresNormalMonster && c.AmountIsLevel && c.Amount == 3));

                    var gkSpy = db.Get(24317029);
                    var gkSpyProg = gkSpy != null ? CardTextEffectCompiler.Compile(gkSpy) : null;
                    Check("Corpus: Gravekeeper's Spy FullyCompiled Flip series ATK<=1500 Deck SS",
                        gkSpyProg != null && gkSpyProg.FullyCompiled &&
                        gkSpyProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromDeck && c.NamedCardIsSeries && c.AmountIsAtkMax &&
                            c.Amount == 1500 &&
                            string.Equals(c.NamedCard, "Gravekeeper's", StringComparison.OrdinalIgnoreCase)),
                        gkSpyProg == null
                            ? "null"
                            : $"full={gkSpyProg.FullyCompiled} unparsed={string.Join("|", gkSpyProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var decayed = db.Get(10209545);
                    var decayedProg = decayed != null ? CardTextEffectCompiler.Compile(decayed) : null;
                    Check("Corpus: Decayed Commander NS named hand SS compiles; battle-damage leftover",
                        decayedProg != null && !decayedProg.FullyCompiled &&
                        decayedProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisNormalSummoned &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromHand &&
                            string.Equals(c.NamedCard, "Zombie Tiger", StringComparison.OrdinalIgnoreCase)),
                        decayedProg == null
                            ? "null"
                            : $"full={decayedProg.FullyCompiled} unparsed={string.Join("|", decayedProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var cobra = db.Get(86801871);
                    var cobraProg = cobra != null ? CardTextEffectCompiler.Compile(cobra) : null;
                    Check("Corpus: Cobra Jar Flip token SS compiles; battle-destroyed inflict leftover",
                        cobraProg != null && !cobraProg.FullyCompiled &&
                        cobraProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SpecialSummonToken &&
                            c.TokenLevel == 3 && c.TokenAtk == 1200 && c.TokenDef == 1200 &&
                            string.Equals(c.TokenName, "Poisonous Snake Token",
                                StringComparison.OrdinalIgnoreCase)) &&
                        !cobraProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.InflictDamageToOpponent),
                        cobraProg == null
                            ? "null"
                            : $"full={cobraProg.FullyCompiled} n={cobraProg.ClauseList.Count} " +
                              $"unparsed={string.Join("|", cobraProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synCobra = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000042,
                        name = "Flip token Stars (new-card shape)",
                        type = "Flip Effect Monster",
                        desc =
                            "FLIP: Special Summon 1 \"Poisonous Snake Token\" (Reptile-Type/EARTH/3 Stars/ATK 1200/DEF 1200)."
                    });
                    Check("New-card rule: Flip token with Stars compiles without a cardId branch",
                        synCobra != null && synCobra.FullyCompiled &&
                        synCobra.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SpecialSummonToken &&
                            c.TokenLevel == 3 && c.TokenAtk == 1200));

                    var incarnate = db.Get(97093037);
                    var incProg = incarnate != null ? CardTextEffectCompiler.Compile(incarnate) : null;
                    Check("Corpus: The Creator Incarnate FullyCompiled tribute-this named hand SS",
                        incProg != null && incProg.FullyCompiled &&
                        incProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.RequiresTributeThis &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromHand &&
                            string.Equals(c.NamedCard, "The Creator", StringComparison.OrdinalIgnoreCase)),
                        incProg == null
                            ? "null"
                            : $"full={incProg.FullyCompiled} unparsed={string.Join("|", incProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var chick = db.Get(36262024);
                    var chickProg = chick != null ? CardTextEffectCompiler.Compile(chick) : null;
                    Check("Corpus: Black Dragon's Chick FullyCompiled send-this named hand SS",
                        chickProg != null && chickProg.FullyCompiled &&
                        chickProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.RequiresSendThisToGy &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromHand &&
                            string.Equals(c.NamedCard, "Red-Eyes B. Dragon",
                                StringComparison.OrdinalIgnoreCase)),
                        chickProg == null
                            ? "null"
                            : $"full={chickProg.FullyCompiled} unparsed={string.Join("|", chickProg.UnparsedFragments ?? Array.Empty<string>())}");


                    var gym = db.Get(7512044);
                    var gymProg = gym != null ? CardTextEffectCompiler.Compile(gym) : null;
                    Check("Corpus: Gather Your Mind FullyCompiled named Deck add (shuffle absorbed; Oath OPT boilerplate)",
                        gymProg != null && gymProg.FullyCompiled &&
                        gymProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            c.FromDeck &&
                            string.Equals(c.NamedCard, "Gather Your Mind", StringComparison.OrdinalIgnoreCase)),
                        gymProg == null
                            ? "null"
                            : $"full={gymProg.FullyCompiled} unparsed={string.Join("|", gymProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synGym = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000043,
                        name = "Named Deck add (new-card shape)",
                        type = "Spell Card",
                        race = "Normal",
                        desc = "Add 1 \"Named Deck add (new-card shape)\" card from your Deck to your hand."
                    });
                    Check("New-card rule: named Deck add compiles without a cardId branch",
                        synGym != null && synGym.FullyCompiled &&
                        synGym.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            string.Equals(c.NamedCard, "Named Deck add (new-card shape)",
                                StringComparison.OrdinalIgnoreCase)));

                    var venus = db.Get(64734921);
                    var venusProg = venus != null ? CardTextEffectCompiler.Compile(venus) : null;
                    Check("Corpus: Venus FullyCompiled pay-500 named hand-or-Deck SS",
                        venusProg != null && venusProg.FullyCompiled &&
                        venusProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.PayLpAmount == 500 &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromHand && c.FromDeck &&
                            string.Equals(c.NamedCard, "Mystical Shine Ball",
                                StringComparison.OrdinalIgnoreCase)),
                        venusProg == null
                            ? "null"
                            : $"full={venusProg.FullyCompiled} unparsed={string.Join("|", venusProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var ladybug = db.Get(83994646);
                    var ladybugProg = ladybug != null ? CardTextEffectCompiler.Compile(ladybug) : null;
                    Check("Corpus: 4-Starred Ladybug FullyCompiled Flip destroy opp Level 4",
                        ladybugProg != null && ladybugProg.FullyCompiled &&
                        ladybugProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.Destroy &&
                            c.AmountIsLevel && c.Amount == 4 &&
                            c.Side == EffectSide.Opponent &&
                            !c.RequiresTargetChoice),
                        ladybugProg == null
                            ? "null"
                            : $"full={ladybugProg.FullyCompiled} unparsed={string.Join("|", ladybugProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var grizz = db.Get(57839750);
                    var grizzProg = grizz != null ? CardTextEffectCompiler.Compile(grizz) : null;
                    Check("Corpus: Mother Grizzly battle-GY WATER 1500 Deck SS is not a free ignition",
                        grizzProg != null && grizzProg.FullyCompiled &&
                        !grizzProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        grizzProg.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromDeck && c.AmountIsAtkMax && c.Amount == 1500 &&
                            string.Equals(c.AttributeFilter, "WATER", StringComparison.OrdinalIgnoreCase)),
                        grizzProg == null
                            ? "null"
                            : $"full={grizzProg.FullyCompiled} unparsed={string.Join("|", grizzProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var blRitual = db.Get(55761792);
                    var blrProg = blRitual != null ? CardTextEffectCompiler.Compile(blRitual) : null;
                    // Generalized Ritual Summon: the Ritual Spell compiles to a rules-driven
                    // RitualSummon clause (named monster + required Tribute Level) — not an
                    // invented per-card hack.
                    Check("Corpus: Black Luster Ritual compiles to a generalized Ritual Summon",
                        blrProg != null && blrProg.FullyCompiled &&
                        blrProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.RitualSummon &&
                            c.NamedCard == "Black Luster Soldier" && c.Amount == 8));

                    var poison = db.Get(40320754);
                    var poisonProg = poison != null ? CardTextEffectCompiler.Compile(poison) : null;
                    Check("Corpus: Lord Poison battle-destroyed GY SS is not a free ignition",
                        poisonProg != null && poisonProg.FullyCompiled &&
                        !poisonProg.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate) &&
                        poisonProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.SentFromFieldToGy &&
                            c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                            string.Equals(c.RaceFilter, "Plant", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.ExceptNamedCard, "Lord Poison",
                                StringComparison.OrdinalIgnoreCase)),
                        poisonProg == null
                            ? "null"
                            : $"full={poisonProg.FullyCompiled} n={poisonProg.ClauseList.Count} " +
                              $"unparsed={string.Join("|", poisonProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var desert = db.Get(13409151);
                    var desertProg = desert != null ? CardTextEffectCompiler.Compile(desert) : null;
                    Check("Corpus: Desertapir FullyCompiled Flip set-FD except itself",
                        desertProg != null && desertProg.FullyCompiled &&
                        desertProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SetTargetFaceDownDefense &&
                            string.Equals(c.ExceptNamedCard, "Desertapir",
                                StringComparison.OrdinalIgnoreCase)),
                        desertProg == null
                            ? "null"
                            : $"full={desertProg.FullyCompiled} unparsed={string.Join("|", desertProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var clown = db.Get(42647539);
                    var clownProg = clown != null ? CardTextEffectCompiler.Compile(clown) : null;
                    Check("Corpus: Ryu-Kishin Clown FullyCompiled Summoned change position",
                        clownProg != null && clownProg.FullyCompiled &&
                        clownProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.ThisCardSummoned &&
                            c.Action == EffectActionKind.ChangeBattlePosition &&
                            c.RequiresTargetChoice),
                        clownProg == null
                            ? "null"
                            : $"full={clownProg.FullyCompiled} unparsed={string.Join("|", clownProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var bow = db.Get(52090844);
                    var bowProg = bow != null ? CardTextEffectCompiler.Compile(bow) : null;
                    Check("Corpus: Bowganian FullyCompiled Standby inflict 600",
                        bowProg != null && bowProg.FullyCompiled &&
                        bowProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.StandbyPhase &&
                            c.Action == EffectActionKind.InflictDamageToOpponent &&
                            c.Amount == 600),
                        bowProg == null
                            ? "null"
                            : $"full={bowProg.FullyCompiled} unparsed={string.Join("|", bowProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var sage = db.Get(13604200);
                    var sageProg = sage != null ? CardTextEffectCompiler.Compile(sage) : null;
                    Check("Corpus: Sage's Stone leftover (no invented If-you-control spell lock)",
                        sageProg != null && !sageProg.FullyCompiled &&
                        !sageProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SpecialSummonNamed));

                    var release = db.Get(75417459);
                    var relProg = release != null ? CardTextEffectCompiler.Compile(release) : null;
                    Check("Corpus: Release Restraint leftover (no invented tribute-named spell)",
                        relProg != null && !relProg.FullyCompiled);

                    var ktitle = db.Get(87210505);
                    var ktProg = ktitle != null ? CardTextEffectCompiler.Compile(ktitle) : null;
                    Check("Corpus: Knight's Title leftover (no invented tribute-named spell)",
                        ktProg != null && !ktProg.FullyCompiled);

                    var dekoichi = db.Get(87621407);
                    var dekProg = dekoichi != null ? CardTextEffectCompiler.Compile(dekoichi) : null;
                    Check("Corpus: Dekoichi scaled extra-draw leftover (not invented)",
                        dekProg != null && !dekProg.FullyCompiled);

                    var lady = db.Get(90147755);
                    var ladyProg = lady != null ? CardTextEffectCompiler.Compile(lady) : null;
                    Check("Corpus: Lady Assailant banish-top-3 leftover (not invented)",
                        ladyProg != null && !ladyProg.FullyCompiled);

                    var remedy = db.Get(11868825);
                    var rp = remedy != null ? CardTextEffectCompiler.Compile(remedy) : null;
                    Check("Corpus: Goblin's Secret Remedy compiles GainLifePoints 600",
                        rp != null && rp.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.GainLifePoints && c.Amount == 600));

                    var absEnd = db.Get(27744077);
                    var aep = absEnd != null ? CardTextEffectCompiler.Compile(absEnd) : null;
                    Check("Corpus: Absolute End compiles opponent-turn ForceOpponentDirectAttacksThisTurn",
                        aep != null && aep.FullyCompiled &&
                        aep.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.ForceOpponentDirectAttacksThisTurn &&
                            c.OpponentTurnOnly),
                        aep == null
                            ? "null"
                            : $"full={aep.FullyCompiled} n={aep.ClauseList.Count} unparsed={string.Join("|", aep.UnparsedFragments ?? Array.Empty<string>())}");

                    var synEnd = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000002,
                        name = "Forced Direct (new-card shape)",
                        type = "Trap Card",
                        race = "Normal",
                        desc =
                            "Activate only during your opponent's turn. This turn, the attacks from your opponent's monsters become direct attacks."
                    });
                    Check("New-card rule: Absolute End-shaped text compiles without a cardId branch",
                        synEnd != null &&
                        synEnd.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.ForceOpponentDirectAttacksThisTurn &&
                            c.OpponentTurnOnly));

                    var healer = db.Get(2130625);
                    var hpHealer = healer != null ? CardTextEffectCompiler.Compile(healer) : null;
                    Check("Corpus: Numinous Healer compiles when-you-take-damage GainLifePoints",
                        hpHealer != null && hpHealer.FullyCompiled &&
                        hpHealer.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.YouTakeLifePointDamage &&
                            c.Action == EffectActionKind.GainLifePoints &&
                            c.Amount == 1000 &&
                            c.ExtraAmountPerCopyInGy == 500),
                        hpHealer == null
                            ? "null"
                            : $"full={hpHealer.FullyCompiled} n={hpHealer.ClauseList.Count} unparsed={string.Join("|", hpHealer.UnparsedFragments ?? Array.Empty<string>())}");

                    var synHeal = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000003,
                        name = "Damage Heal (new-card shape)",
                        type = "Trap Card",
                        race = "Normal",
                        desc =
                            "You can only activate this card when you take damage to your Life Points. Increase your Life Points by 1000 points. Also, increase your Life Points by 500 points for each \"Damage Heal\" card in your Graveyard."
                    });
                    Check("New-card rule: Numinous Healer-shaped text compiles without a cardId branch",
                        synHeal != null &&
                        synHeal.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.YouTakeLifePointDamage &&
                            c.Action == EffectActionKind.GainLifePoints));

                    var mermaid = db.Get(85802526);
                    var merProg = mermaid != null ? CardTextEffectCompiler.Compile(mermaid) : null;
                    Check("Corpus: Cure Mermaid compiles Standby GainLifePoints 800",
                        merProg != null && merProg.FullyCompiled &&
                        merProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.StandbyPhase &&
                            c.Action == EffectActionKind.GainLifePoints &&
                            c.Amount == 800 &&
                            !c.RequiresThisAttackPosition &&
                            !c.RequiresThisDefensePosition),
                        merProg == null
                            ? "null"
                            : $"full={merProg.FullyCompiled} n={merProg.ClauseList.Count} unparsed={string.Join("|", merProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synMer = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000005,
                        name = "Standby Heal (new-card shape)",
                        type = "Effect Monster",
                        desc =
                            "As long as this card remains face-up on your side of the field, increase your Life Points by 800 points during each of your Standby Phases."
                    });
                    Check("New-card rule: Cure Mermaid-shaped text compiles without a cardId branch",
                        synMer != null &&
                        synMer.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.StandbyPhase &&
                            c.Action == EffectActionKind.GainLifePoints &&
                            c.Amount == 800));

                    var breeze = db.Get(53530069);
                    var brProg = breeze != null ? CardTextEffectCompiler.Compile(breeze) : null;
                    Check("Corpus: Spirit of the Breeze compiles Standby LP in Attack Position",
                        brProg != null &&
                        brProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.GainLifePoints &&
                            c.RequiresThisAttackPosition &&
                            c.Amount == 1000));

                    var terra = db.Get(73628505);
                    var terraProg = terra != null ? CardTextEffectCompiler.Compile(terra) : null;
                    Check("Corpus: Terraforming compiles AddFromDeckToHand Field Spell search",
                        terraProg != null && terraProg.FullyCompiled &&
                        terraProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.Zone == EffectZoneFilter.DeckFieldSpells &&
                            c.RequiresTargetChoice),
                        terraProg == null
                            ? "null"
                            : $"full={terraProg.FullyCompiled} n={terraProg.ClauseList.Count} unparsed={string.Join("|", terraProg.UnparsedFragments ?? Array.Empty<string>())}");

                    var synTerra = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000004,
                        name = "Field Search (new-card shape)",
                        type = "Spell Card",
                        race = "Normal",
                        desc = "Add 1 Field Spell from your Deck to your hand."
                    });
                    Check("New-card rule: Terraforming-shaped text compiles without a cardId branch",
                        synTerra != null &&
                        synTerra.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.Zone == EffectZoneFilter.DeckFieldSpells));

                    var rota = db.Get(32807846);
                    var rotaProg = rota != null ? CardTextEffectCompiler.Compile(rota) : null;
                    Check("Corpus: Reinforcement of the Army compiles Level 4 Warrior search",
                        rotaProg != null && rotaProg.FullyCompiled &&
                        rotaProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.Zone == EffectZoneFilter.DeckMonstersRaceLevelLeq));

                    var hino = db.Get(46130346);
                    var hinoProg = hino != null ? CardTextEffectCompiler.Compile(hino) : null;
                    Check("Corpus: Hinotama compiles InflictDamageToOpponent 500",
                        hinoProg != null && hinoProg.FullyCompiled &&
                        hinoProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.InflictDamageToOpponent &&
                            c.Amount == 500));

                    var duster = db.Get(18144507);
                    var dusterProg = duster != null ? CardTextEffectCompiler.Compile(duster) : null;
                    Check("Corpus: Harpie's Feather Duster compiles destroy opponent S/T",
                        dusterProg != null && dusterProg.FullyCompiled &&
                        dusterProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.Destroy &&
                            c.Side == EffectSide.Opponent &&
                            c.Zone == EffectZoneFilter.FieldSpellTraps &&
                            !c.RequiresTargetChoice));

                    Check("Vocabulary: Protection is a shared kind",
                        Array.IndexOf(EffectVocabulary.SharedResolutions,
                            EffectResolutionKind.Protection) >= 0);
                }
            }

            // ── Duelist XP curve (GO pace, slowdown at 50) ──
            {
                Check("XP curve L1 < L20",
                    Data.DuelistXpCurve.XpToNextLevel(1) < Data.DuelistXpCurve.XpToNextLevel(20));
                Check("XP curve L40 < L50 (wall starts)",
                    Data.DuelistXpCurve.XpToNextLevel(40) < Data.DuelistXpCurve.XpToNextLevel(50));
                Check("XP curve L49 < L50 hard step",
                    Data.DuelistXpCurve.XpToNextLevel(49) < Data.DuelistXpCurve.XpToNextLevel(50));
                Check("XP curve L50 wall much higher than L20",
                    Data.DuelistXpCurve.XpToNextLevel(50) >
                    Data.DuelistXpCurve.XpToNextLevel(20) * 5);
                Check("XP curve soft cap returns 0",
                    Data.DuelistXpCurve.XpToNextLevel(100, storyModeComplete: false) == 0);
                Check("XP progress 0 when empty",
                    Mathf.Approximately(Data.DuelistXpCurve.Progress01(5, 0), 0f));
                var halfNeed = Data.DuelistXpCurve.XpToNextLevel(5) / 2;
                Check("XP progress ~0.5 mid-bar",
                    Data.DuelistXpCurve.Progress01(5, halfNeed) > 0.45f &&
                    Data.DuelistXpCurve.Progress01(5, halfNeed) < 0.55f);
                Check("Band at L50 is Master",
                    Data.DuelistXpCurve.BandName(50) == "Master");
            }

            // ── Trap Hole response legality (summon window) ──
            {
                // IsLegalResponseCard only needs a non-null engine + field occupant.
                var engine = new DuelEngine();
                var player = new DuelistState("You", true) { LifePoints = 8000 };
                var trapDef = new CardDef
                {
                    id = SpellTrapEffects.TrapHole,
                    name = "Trap Hole",
                    type = "Trap Card",
                    race = "Normal",
                    desc =
                        "When your opponent Normal or Flip Summons 1 monster with 1000 or more ATK: Target that monster; destroy that target."
                };
                var trap = new CardInstance
                {
                    InstanceId = 9001,
                    CardId = SpellTrapEffects.TrapHole,
                    FaceUp = false,
                    SetThisTurn = false,
                    Def = trapDef
                };
                player.SpellTrapZones[0].Occupant = trap;
                var summonedHi = MockMonster(1800, 1000, BattlePosition.Attack, true);
                var summonedLo = MockMonster(800, 1000, BattlePosition.Attack, true);
                Check("Trap Hole legal vs ATK 1800 summon",
                    SpellTrapEffects.IsLegalResponseCard(engine, player, trap,
                        ResponseTiming.MonsterSummoned, summonedHi));
                Check("Trap Hole illegal vs ATK 800 summon",
                    !SpellTrapEffects.IsLegalResponseCard(engine, player, trap,
                        ResponseTiming.MonsterSummoned, summonedLo));
                trap.SetThisTurn = true;
                Check("Trap Hole illegal same-turn Set",
                    !SpellTrapEffects.IsLegalResponseCard(engine, player, trap,
                        ResponseTiming.MonsterSummoned, summonedHi));
                trap.SetThisTurn = false;
            }

            // ── Bottomless Trap Hole (summon destroy + banish, not a target) ──
            {
                var engine = new DuelEngine();
                var player = new DuelistState("You", true) { LifePoints = 8000 };
                var trapDef = new CardDef
                {
                    id = 29401950,
                    name = "Bottomless Trap Hole",
                    type = "Trap Card",
                    race = "Normal",
                    desc =
                        "When your opponent Summons a monster(s) with 1500 or more ATK: Destroy that monster(s) with 1500 or more ATK, and if you do, banish it."
                };
                var prog = CardTextEffectCompiler.Compile(trapDef);
                Check("Bottomless Trap Hole FullyCompiled",
                    prog != null && prog.FullyCompiled,
                    prog == null
                        ? "null"
                        : $"full={prog.FullyCompiled} unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}");
                Check("Bottomless compiles as summon destroy+banish ATK 1500, not a target",
                    prog != null && prog.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.OpponentNormalOrFlipSummon &&
                        c.Action == EffectActionKind.Destroy &&
                        c.Amount == 1500 &&
                        c.BanishIfDestroyed &&
                        !c.RequiresTargetChoice));
                var trap = new CardInstance
                {
                    InstanceId = 9002,
                    CardId = 29401950,
                    FaceUp = false,
                    SetThisTurn = false,
                    Def = trapDef
                };
                player.SpellTrapZones[0].Occupant = trap;
                var summonedHi = MockMonster(1800, 1000, BattlePosition.Attack, true);
                var summonedLo = MockMonster(1400, 1200, BattlePosition.Attack, true);
                Check("Bottomless legal vs ATK 1800 summon",
                    SpellTrapEffects.IsLegalResponseCard(engine, player, trap,
                        ResponseTiming.MonsterSummoned, summonedHi));
                Check("Bottomless illegal vs ATK 1400 summon",
                    !SpellTrapEffects.IsLegalResponseCard(engine, player, trap,
                        ResponseTiming.MonsterSummoned, summonedLo));
                trap.SetThisTurn = true;
                Check("Bottomless illegal same-turn Set",
                    !SpellTrapEffects.IsLegalResponseCard(engine, player, trap,
                        ResponseTiming.MonsterSummoned, summonedHi));
                trap.SetThisTurn = false;
            }

            {
                var adhesionDef = new CardDef
                {
                    id = 62325062,
                    name = "Adhesion Trap Hole",
                    type = "Trap Card",
                    race = "Normal",
                    desc = "When your opponent Summons a monster(s): Halve that monster(s)'s original ATK."
                };
                var ap = CardTextEffectCompiler.Compile(adhesionDef);
                Check("Adhesion Trap Hole FullyCompiled halve original ATK, answers SS",
                    ap != null && ap.FullyCompiled &&
                    ap.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.HalveOriginalAtk &&
                        c.AnswersSpecialSummon && !c.RequiresTargetChoice));
                var torrDef = new CardDef
                {
                    id = 53582587,
                    name = "Torrential Tribute",
                    type = "Trap Card",
                    race = "Normal",
                    desc = "When a monster(s) is Summoned: Destroy all monsters on the field."
                };
                var tp = CardTextEffectCompiler.Compile(torrDef);
                Check("Torrential Tribute FullyCompiled destroy-all, answers SS and own summon",
                    tp != null && tp.FullyCompiled &&
                    tp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Destroy &&
                        c.Zone == EffectZoneFilter.FieldMonsters &&
                        c.AnswersSpecialSummon && c.AnswersControllerSummon));
                var eatDef = new CardDef
                {
                    id = 42578427,
                    name = "Eatgaboon",
                    type = "Trap Card",
                    race = "Normal",
                    desc =
                        "If the ATK of a monster summoned by your opponent (excluding Special Summon) is 500 points or less, the monster is destroyed."
                };
                var ep = CardTextEffectCompiler.Compile(eatDef);
                Check("Eatgaboon FullyCompiled opponent NS/FS ATK ≤ 500, not SS",
                    ep != null && ep.FullyCompiled &&
                    ep.ClauseList.Exists(c =>
                        c != null && c.AmountIsAtkMax && c.Amount == 500 &&
                        !c.AnswersSpecialSummon));
                var tokDef = new CardDef
                {
                    id = 83675475,
                    name = "Token Feastevil",
                    type = "Trap Card",
                    race = "Normal",
                    desc =
                        "When a Token(s) is Special Summoned: Destroy as many Tokens on the field as possible, and if you do, inflict 300 damage to your opponent for each Token destroyed."
                };
                var tokp = CardTextEffectCompiler.Compile(tokDef);
                Check("Token Feastevil FullyCompiled destroy tokens + 300 each",
                    tokp != null && tokp.FullyCompiled &&
                    tokp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.DestroyTokensInflictPer &&
                        c.RequiresSummonedIsToken && c.Amount == 300));
                var misDef = new CardDef
                {
                    id = 58392024,
                    name = "Mispolymerization",
                    type = "Trap Card",
                    race = "Normal",
                    desc =
                        "Activate only when a Fusion Monster is Special Summoned. Return all face-up Fusion Monsters to their respective Extra Decks."
                };
                var mp = CardTextEffectCompiler.Compile(misDef);
                Check("Mispolymerization FullyCompiled bounce face-up Fusions",
                    mp != null && mp.FullyCompiled &&
                    mp.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.ReturnAllFaceUpFusionsToExtra &&
                        c.RequiresSummonedIsFusion));

                var slateDef = new CardDef
                {
                    id = 78636495,
                    name = "Slate Warrior",
                    type = "Flip Effect Monster",
                    desc =
                        "FLIP: This card gains 500 ATK and DEF.\nIf this card is destroyed by battle: The monster that destroyed it loses 500 ATK and DEF."
                };
                var slp = CardTextEffectCompiler.Compile(slateDef);
                Check("Slate Warrior FullyCompiled Flip lingering + battle destroyer lingering",
                    slp != null && slp.FullyCompiled &&
                    slp.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.ApplyLingeringAtkDef && c.Amount == 500) &&
                    slp.ClauseList.Exists(c =>
                        c != null && c.RequiresThisDestroyedByBattle &&
                        c.ImplicitTargetIsBattleDestroyer && c.Amount == -500),
                    slp == null
                        ? "null"
                        : $"full={slp.FullyCompiled} unparsed={string.Join("|", slp.UnparsedFragments ?? Array.Empty<string>())}");

                var maliceDef = new CardDef
                {
                    id = 72657739,
                    name = "Malice Doll of Demise",
                    type = "Effect Monster",
                    desc =
                        "During your next Standby Phase after this card was sent from the field to the Graveyard by the effect of a Continuous Spell Card: Special Summon this card from the Graveyard."
                };
                var mlp = CardTextEffectCompiler.Compile(maliceDef);
                Check("Malice Doll FullyCompiled next Standby SS after Continuous Spell send",
                    mlp != null && mlp.FullyCompiled &&
                    mlp.ClauseList.Exists(c =>
                        c != null && c.ResolvesFromGy && c.RequiresSentByContinuousSpell &&
                        c.RequiresNextControllerStandby &&
                        c.Action == EffectActionKind.SpecialSummonFromGy));

                var yomiDef = new CardDef
                {
                    id = 51534754,
                    name = "Yomi Ship",
                    type = "Effect Monster",
                    desc =
                        "If this card is destroyed by battle and sent to the GY: Destroy the monster that destroyed this card."
                };
                var yp = CardTextEffectCompiler.Compile(yomiDef);
                Check("Yomi Ship FullyCompiled destroy the battle destroyer",
                    yp != null && yp.FullyCompiled &&
                    yp.ClauseList.Exists(c =>
                        c != null && c.ImplicitTargetIsBattleDestroyer &&
                        c.Action == EffectActionKind.Destroy));
            }

            // ── ERAZ band table / format tray ──
            {
                Check("ERAZ: original exists", ErazFormat.Band(ErazFormat.Original) != null);
                Check("ERAZ: TLM is not Original", !ErazFormat.InOriginalSetList("TLM"));
                Check("ERAZ: FET/LOB is Original",
                    ErazFormat.InOriginalSetList("LOB") && ErazFormat.InOriginalSetList("FET")
                    || ErazFormat.InOriginalSetList("LOB")); // FET may be missing from pre_link_sets; LOB must pass
                Check("ERAZ: TCG format shows tray", ErazFormat.ShowsBadgeTray("pvai"));
                Check("ERAZ: quick shows tray", ErazFormat.ShowsBadgeTray("quick"));
                Check("ERAZ: ddm hides tray", !ErazFormat.ShowsBadgeTray("ddm"));
                Check("ERAZ: genesys hides tray", !ErazFormat.ShowsBadgeTray("genesys"));
                Check("ERAZ: next after original is gx", ErazFormat.NextBand(ErazFormat.Original) == ErazFormat.Gx);
                var cfg = ArDuelMatchConfig.DefaultQuick();
                Check("Match default ERAZ is original", cfg.ErazBandId == ErazFormat.Original);
            }

            // ── ERAZ tutorial / season badge grants ──
            {
                var fresh = PlayerProgress.DefaultNew();
                Check("Badge: new account has no ERAZ badge",
                    !ErazProgress.HasBadge(fresh, ErazFormat.Original));
                ErazProgress.GrantTutorialBadge(fresh);
                Check("Badge: tutorial grants original",
                    ErazProgress.HasBadge(fresh, ErazFormat.Original));
                Check("Badge: tutorial does not grant gx",
                    !ErazProgress.HasBadge(fresh, ErazFormat.Gx));
                ErazProgress.GrantNextOnSeasonComplete(fresh);
                Check("Badge: season complete grants gx",
                    ErazProgress.HasBadge(fresh, ErazFormat.Gx));
                Check("Badge: cannot select 5ds yet",
                    !ErazProgress.CanSelectBand(fresh, ErazFormat.FiveDs));

                var legacy = PlayerProgress.DefaultNew();
                legacy.onboardingComplete = true;
                Check("Badge: finished tutorial without csv has no original",
                    !ErazProgress.HasBadge(legacy, ErazFormat.Original));
                ErazProgress.GrantTutorialBadgeIfOnboarded(legacy);
                Check("Badge: onboarded backfill grants original",
                    ErazProgress.HasBadge(legacy, ErazFormat.Original));
                Check("Badge: Hub/quick require original",
                    AppSession.RequiresOriginalBadgeForLaunch(ArDuelLaunchKind.Hub)
                    && AppSession.RequiresOriginalBadgeForLaunch(ArDuelLaunchKind.CreateMenu));
                Check("Badge: Practice/LabTest do not require original",
                    !AppSession.RequiresOriginalBadgeForLaunch(ArDuelLaunchKind.Practice)
                    && !AppSession.RequiresOriginalBadgeForLaunch(ArDuelLaunchKind.LabTest));
            }

            // ── ERAZ deck builder compile gate + copy cap ──
            {
                var celtic = new CardDef
                {
                    id = 91152256, name = "Celtic Guardian", type = "Normal Monster",
                    desc = "An elf who learned to wield a sword, he baffles enemies with lightning-swift attacks."
                };
                var haDes = new CardDef
                {
                    id = 53982768,
                    name = "Dark Ruler Ha Des",
                    type = "Effect Monster",
                    desc =
                        "Cannot be Special Summoned from the GY. Negate the effects of monsters destroyed by battle with Fiend monsters you control."
                };
                var prev = CardEffectStatus.ExcludeUnimplementedFromDecks;
                CardEffectStatus.ExcludeUnimplementedFromDecks = true;
                try
                {
                    Check("DeckRules: Celtic 3 copies OK",
                        ErazDeckRules.CanAddToDeck(celtic, 2, ErazFormat.Original, out _));
                    Check("DeckRules: Ha Des rejected",
                        !ErazDeckRules.CanAddToDeck(haDes, 0, ErazFormat.Original, out var haMsg)
                        && haMsg.IndexOf("not implemented", StringComparison.OrdinalIgnoreCase) >= 0);
                    Check("DeckRules: labOpen allows unimplemented Ha Des",
                        ErazDeckRules.CanAddToDeck(haDes, 0, ErazFormat.Original, out _, labOpen: true));
                    Check("DeckRules: labOpen still copy-caps at 3",
                        !ErazDeckRules.CanAddToDeck(haDes, 3, ErazFormat.Original, out var labCapMsg, labOpen: true)
                        && labCapMsg.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                finally
                {
                    CardEffectStatus.ExcludeUnimplementedFromDecks = prev;
                }

                var hNow = OfficialCardAuthority.TextHash(celtic);
                var hEra = OfficialCardAuthority.TextHash(celtic, ErazFormat.Original);
                Check("TextHash(era) matches current desc when no snapshot", hNow == hEra);

                var fake = new CardDef { id = 1, name = "X", type = "Effect Monster", desc = "Draw 1 card." };
                var a = OfficialCardAuthority.TextHash(fake, ErazFormat.Original);
                fake.desc = "Draw 2 cards.";
                var b = OfficialCardAuthority.TextHash(fake, ErazFormat.Original);
                Check("TextHash changes when desc changes", a != b);
            }

            // ── ERAZ release wall + lab slice exception ──
            {
                Check("Release: ddm still no tray", !ErazFormat.ShowsBadgeTray("ddm"));
                var labIds = EffectCoverageService.CollectDeckIds(
                    EffectCoverageService.StarterAndLabDeckFiles);
                Check("Lab slice contains Pot of Greed", labIds.Contains(55144522));
                // Do not force IsReleased(Original); false until FET pool is FullyCompiled.
                Check("Original not silently released without full compile",
                    !ErazFormat.IsReleased(ErazFormat.Original));

                var db = CardDatabase.Instance ?? CardDatabase.Load();
                var prev = CardEffectStatus.ExcludeUnimplementedFromDecks;
                CardEffectStatus.ExcludeUnimplementedFromDecks = true;
                try
                {
                    var pot = db != null ? db.Get(55144522) : null;
                    Check("DeckRules: Pot of Greed lab slice OK while Original unreleased",
                        pot != null &&
                        ErazDeckRules.CanAddToDeck(pot, 0, ErazFormat.Original, out _));

                    var barkDef = db != null ? db.Get(41925941) : null;
                    string barkMsg = "";
                    var barkRejected = barkDef != null
                        && CardEffectStatus.Classify(barkDef) == CardEffectStatusKind.Implemented
                        && !labIds.Contains(41925941)
                        && !ErazDeckRules.CanAddToDeck(barkDef, 0, ErazFormat.Original, out barkMsg);
                    Check("DeckRules: Implemented non-lab rejected for unreleased era (not UNIMPLEMENTED)",
                        barkRejected
                        && barkMsg.IndexOf("era", StringComparison.OrdinalIgnoreCase) >= 0
                        && barkMsg.IndexOf("[UNIMPLEMENTED]", StringComparison.OrdinalIgnoreCase) < 0,
                        barkMsg);

                    Check("InPool: Pot of Greed is Original",
                        ErazFormat.InPool(55144522, ErazFormat.Original));
                    Check("InPool: Sparkman is not Original",
                        !ErazFormat.InPool(20721928, ErazFormat.Original));
                    var spark = db != null ? db.Get(20721928) : null;
                    string sparkMsg = "";
                    var sparkRejected = spark != null
                        && CardEffectStatus.Classify(spark) == CardEffectStatusKind.Structural
                        && !ErazDeckRules.CanAddToDeck(spark, 0, ErazFormat.Original, out sparkMsg);
                    Check("DeckRules: post-TLM vanilla rejected from Original",
                        sparkRejected
                        && (sparkMsg.IndexOf("era", StringComparison.OrdinalIgnoreCase) >= 0
                            || sparkMsg.IndexOf("pool", StringComparison.OrdinalIgnoreCase) >= 0
                            || sparkMsg.IndexOf("release", StringComparison.OrdinalIgnoreCase) >= 0),
                        sparkMsg);
                }
                finally
                {
                    CardEffectStatus.ExcludeUnimplementedFromDecks = prev;
                }
            }

            // ── TCG pool + Advanced banlist (era decks keep 3-of) ──
            {
                Check("TCG pool file is loaded", TcgLegalPool.IsLoaded, $"count={TcgLegalPool.Count}");
                Check("TCG pool includes Blue-Eyes 89631139", TcgLegalPool.IsTcgPrint(89631139));
                Check("TCG pool flags Ankuriboh 595626 as OCG-only", TcgLegalPool.IsOcgOnly(595626));
                var ocgFake = new CardDef
                {
                    id = 595626,
                    name = "Ankuriboh",
                    type = "Effect Monster",
                    desc = "OCG-only fixture."
                };
                Check("DeckRules: OCG-only refused even in lab",
                    !ErazDeckRules.CanAddToDeck(ocgFake, 0, ErazFormat.Original, out var ocgMsg, labOpen: true)
                    && ocgMsg.IndexOf("OCG", StringComparison.OrdinalIgnoreCase) >= 0,
                    ocgMsg);
                Check("Advanced: Pot of Greed is Forbidden (0 copies)",
                    OfficialDataSources.MaxCopies(55144522, ErazFormat.Modern) == 0);
                Check("Original era: Pot of Greed still 3-of",
                    OfficialDataSources.MaxCopies(55144522, ErazFormat.Original) == 3);
                Check("Banlist file effectiveDate is 2026.05 TCG",
                    (OfficialDataSources.LoadBanlist().effectiveDate ?? "")
                    .IndexOf("2026.05", StringComparison.Ordinal) >= 0,
                    OfficialDataSources.LoadBanlist().effectiveDate);
            }

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            var summary = sb.ToString();
            if (fail == 0)
                Debug.Log("[WRLDZ TCG Tests]\n" + summary);
            else
                Debug.LogError("[WRLDZ TCG Tests]\n" + summary);
            return summary;
        }

        static CardInstance MockMonster(int atk, int def, BattlePosition pos, bool faceUp)
        {
            return new CardInstance
            {
                InstanceId = UnityEngine.Random.Range(1, 999999),
                CardId = 1,
                FaceUp = faceUp,
                Position = pos,
                Def = new CardDef
                {
                    id = 1,
                    name = $"Mock{atk}/{def}",
                    type = "Effect Monster",
                    atk = atk,
                    def = def,
                    level = 4
                }
            };
        }
    }
}
