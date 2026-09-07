using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Sweep every cards_db entry: FullyCompiled cards must fire on a legal board;
    /// unimplemented cards must refuse. Does not invent resolutions.
    /// </summary>
    public static class CorpusTriggerStressTests
    {
        const int Celtic = 91152256;
        const int Bewd = 89631139;
        const int Angus = 11813953;
        const int Umi = 22702055;
        const int Alo = 295517;
        const int ArchfiendSoldier = 49881766;
        const int Fenrir = 218704;
        const int PotOfGreed = 55144522;
        const int MirrorForce = 44095762;

        public static string Run()
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

            var db = CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                sb.AppendLine("FAIL  CardDatabase load");
                sb.AppendLine("--- 0 passed, 1 failed ---");
                return sb.ToString();
            }

            var pDeck = CardDatabase.LoadDeck("lab_rules_player.json");
            var aDeck = CardDatabase.LoadDeck("lab_rules_ai.json");
            if (pDeck == null || aDeck == null)
            {
                sb.AppendLine("FAIL  lab decks missing");
                sb.AppendLine("--- 0 passed, 1 failed ---");
                return sb.ToString();
            }

            var structural = 0;
            var implemented = 0;
            var stub = 0;
            var unimplemented = 0;
            var loudFail = new List<string>();
            var silentUnimp = new List<string>();
            var triggerFail = new List<string>();
            var flipFail = new List<string>();
            var trapFail = new List<string>();
            var fieldFail = new List<string>();
            var ignFail = new List<string>();
            var standbyFail = new List<string>();

            var engine = Fresh(db, pDeck, aDeck);

            foreach (var def in db.GetAllCards())
            {
                if (def == null) continue;

                // Isolate each card: a prior card's resolution can end the duel
                // (GameOver) or leave a non-Main phase, which would make every
                // later activation illegal. Re-seat a clean Main-Phase duel when
                // the shared engine is no longer in a usable state.
                if (engine.GameOver || !engine.InMainPhase || engine.TurnPlayer != engine.Player)
                    engine = Fresh(db, pDeck, aDeck);

                var kind = CardEffectStatus.Classify(def);
                switch (kind)
                {
                    case CardEffectStatusKind.Structural:
                        structural++;
                        break;
                    case CardEffectStatusKind.Implemented:
                        implemented++;
                        break;
                    case CardEffectStatusKind.Stub:
                        stub++;
                        break;
                    default:
                        unimplemented++;
                        break;
                }

                if (kind == CardEffectStatusKind.Unimplemented || kind == CardEffectStatusKind.Stub)
                {
                    if (OfficialEffectRegistry.ProgramMayActivate(def))
                        silentUnimp.Add(def.name ?? $"#{def.id}");
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    ClearBoard(engine);
                    engine.Player.Hand.Clear();
                    if ((def.IsSpell || def.IsTrap) && !def.IsFieldSpell)
                    {
                        var c = PutInHand(engine, engine.Player, def.id);
                        if (engine.CanActivateSpellTrap(engine.Player, c, fromHand: true))
                            loudFail.Add(def.name ?? $"#{def.id}");
                    }
                    else if (def.IsMonster && !def.IsExtraDeck)
                    {
                        var m = PlaceMonster(engine, engine.Player, def.id, 2,
                            BattlePosition.Attack, true);
                        if (engine.CanActivateSpellTrap(engine.Player, m, fromHand: false))
                            loudFail.Add(def.name ?? $"#{def.id}");
                    }

                    continue;
                }

                if (kind != CardEffectStatusKind.Implemented) continue;
                var prog = CardTextEffectCompiler.Compile(def);
                if (prog == null || !prog.FullyCompiled) continue;
                if (HasUnique(prog)) continue;

                if (engine.IsAwaitingResponse) engine.PassResponse();
                ClearBoard(engine);
                engine.Player.Hand.Clear();
                engine.Player.Deck.Clear();
                for (var i = 0; i < 8; i++) engine.Player.Deck.Add(Celtic);

                try
                {
                    if (def.IsFieldSpell)
                    {
                        if (!TryField(engine, def, fieldFail)) continue;
                    }
                    else if (def.IsEquipSpell)
                    {
                        // Covered by InteractionRegressionTests Equip stress.
                    }
                    else if (def.IsSpell && !def.IsTrap)
                    {
                        if (!TrySpell(engine, def, prog, triggerFail)) continue;
                    }
                    else if (def.IsTrap)
                    {
                        if (!TryTrap(engine, def, prog, trapFail)) continue;
                    }
                    else if (def.IsMonster && !def.IsExtraDeck)
                    {
                        if (prog.HasTiming(EffectTiming.Flip))
                        {
                            if (!TryFlip(engine, def, flipFail)) continue;
                        }

                        if (prog.HasTiming(EffectTiming.Activate) &&
                            !prog.ClauseList.Exists(c => c != null && c.ActivatesFromHand))
                        {
                            if (!TryIgnition(engine, def, prog, ignFail)) continue;
                        }

                        if (prog.HasTiming(EffectTiming.StandbyPhase) ||
                            prog.HasTiming(EffectTiming.ContinuousWhileFaceUp))
                        {
                            if (!TryStandbyOrAura(engine, def, standbyFail)) continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    triggerFail.Add((def.name ?? $"#{def.id}") + " ex:" + ex.GetType().Name);
                }
            }

            Check("Corpus: cards_db classified",
                structural + implemented + stub + unimplemented == db.Count,
                $"s={structural} impl={implemented} stub={stub} unimp={unimplemented} n={db.Count}");
            Check("Corpus: unimplemented/stub cannot ProgramMayActivate",
                silentUnimp.Count == 0, Join(silentUnimp));
            Check("Corpus: unimplemented/stub refuse Activate (fail loud)",
                loudFail.Count == 0, Join(loudFail));
            Check("Corpus: FullyCompiled Field Spells Activate",
                fieldFail.Count == 0, Join(fieldFail));
            Check("Corpus: FullyCompiled Spells Activate on a legal board",
                triggerFail.Count == 0, Join(triggerFail));
            Check("Corpus: FullyCompiled Traps are legal in their window",
                trapFail.Count == 0, Join(trapFail));
            Check("Corpus: FullyCompiled Flip monsters Flip Summon",
                flipFail.Count == 0, Join(flipFail));
            Check("Corpus: FullyCompiled ignition monsters Activate from field",
                ignFail.Count == 0, Join(ignFail));
            Check("Corpus: FullyCompiled Standby/aura monsters do not throw",
                standbyFail.Count == 0, Join(standbyFail));
            Check("Corpus: implemented pool is non-empty",
                implemented >= 80, $"implemented={implemented}");

            sb.AppendLine(
                $"corpus n={db.Count} structural={structural} implemented={implemented} " +
                $"stub={stub} unimplemented={unimplemented}");
            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            var summary = sb.ToString();
            if (fail == 0)
                Debug.Log("[WRLDZ Corpus Trigger Stress]\n" + summary);
            else
                Debug.LogError("[WRLDZ Corpus Trigger Stress]\n" + summary);
            return summary;
        }

        static bool HasUnique(CompiledCardProgram prog)
        {
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                if (EffectVocabulary.IsUniqueException(c.Action)) return true;
            }

            return false;
        }

        static bool NeedsHardBoard(EffectClause c)
        {
            if (c == null) return false;
            return c.Action == EffectActionKind.RitualSummon || // needs named monster in hand + Tributes
                   c.RequiresLordOfDOnField ||
                   c.RequiresSendNamedToGy ||
                   c.RequiresTributeThis ||
                   c.RequiresTributeCount > 0 ||
                   c.RequiresDiscardSelf ||
                   c.RequiresSendThisToGy ||
                   c.RequiresSendHandToGy ||
                   c.RequiresSendOtherYouControl ||
                   c.PayLpAmount > 0 ||
                   c.BanishFromGyCount > 0 ||
                   c.RequiresRemoveSpellCounters > 0 ||
                   c.RequiresLpCostMultiple > 0 ||
                   (c.RequiresDiscardCost &&
                    c.Action != EffectActionKind.AddFromGyToHand) ||
                   c.Action == EffectActionKind.FusionSummonRegistered ||
                   c.Action == EffectActionKind.SpecialSummonFusionFromExtra;
        }

        static void Provision(DuelEngine engine, CompiledCardProgram prog)
        {
            var p = engine.Player;
            var opp = engine.Opponent;
            PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
            PlaceMonster(engine, opp, Celtic, 2, BattlePosition.Attack, true);
            PlaceSetTrap(engine, opp, 5318639, 1);
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                    c.RequiresFaceUpName.IndexOf("Umi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var umi = engine.CreateCardInstance(Umi);
                    umi.FaceUp = true;
                    if (p.FieldSpellZone != null)
                        p.FieldSpellZone.Occupant = umi;
                }

                if (c.RequiresControllerNamedCard &&
                    !string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                    c.RequiresFaceUpName.IndexOf("Archfiend", StringComparison.OrdinalIgnoreCase) >= 0)
                    PlaceMonster(engine, p, ArchfiendSoldier, 1, BattlePosition.Attack, true);

                if (c.Zone == EffectZoneFilter.DeckFieldSpells)
                    p.Deck.Insert(0, Alo);

                if (c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                    !string.IsNullOrEmpty(c.NamedCard) &&
                    string.Equals(c.NamedCard, prog.CardName, StringComparison.OrdinalIgnoreCase) &&
                    prog.CardId > 0)
                    p.Deck.Insert(0, prog.CardId);

                if (c.RequiresDiscardCost)
                {
                    var fodder = Celtic;
                    var attr = c.DiscardCostAttribute;
                    if (!string.IsNullOrEmpty(attr) && attr != "*")
                    {
                        if (string.Equals(attr, "WATER", StringComparison.OrdinalIgnoreCase))
                            fodder = Fenrir;
                        else if (string.Equals(attr, "LIGHT", StringComparison.OrdinalIgnoreCase))
                            fodder = Bewd;
                    }

                    PutInHand(engine, p, fodder);
                }

                if (c.Zone == EffectZoneFilter.ControllerGyMonsters ||
                    c.Zone == EffectZoneFilter.EitherGyMonsters)
                {
                    p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                    p.Graveyard.Add(engine.CreateCardInstance(Bewd));
                }

                if (c.Zone == EffectZoneFilter.ControllerGySpells)
                    p.Graveyard.Add(engine.CreateCardInstance(PotOfGreed));
                if (c.Zone == EffectZoneFilter.ControllerGyTraps)
                    p.Graveyard.Add(engine.CreateCardInstance(MirrorForce));
            }
        }

        static bool TryField(DuelEngine engine, CardDef def, List<string> fail)
        {
            var card = PutInHand(engine, engine.Player, def.id);
            if (!engine.CanActivateSpellTrap(engine.Player, card, fromHand: true) ||
                !engine.TryActivateSpellTrap(engine.Player, card, fromHand: true))
            {
                fail.Add(def.name ?? $"#{def.id}");
                return false;
            }

            if (engine.Player.FieldSpellZone?.Occupant != card &&
                !engine.Player.Graveyard.Contains(card))
            {
                fail.Add((def.name ?? $"#{def.id}") + " not on field");
                return false;
            }

            return true;
        }

        static bool TrySpell(DuelEngine engine, CardDef def, CompiledCardProgram prog, List<string> fail)
        {
            // FullyCompiled Continuous Spells: playing the card is the activation even
            // when the printed effect is only End Phase / Standby / while-face-up.
            if (def.IsContinuousSpellOrTrap && !prog.HasTiming(EffectTiming.Activate))
            {
                var st = PutInHand(engine, engine.Player, def.id);
                if (!engine.CanActivateSpellTrap(engine.Player, st, fromHand: true))
                {
                    fail.Add(def.name ?? $"#{def.id}");
                    return false;
                }

                if (!engine.TryActivateSpellTrap(engine.Player, st, fromHand: true))
                {
                    fail.Add((def.name ?? $"#{def.id}") + " activate");
                    return false;
                }

                return true;
            }

            var act = prog.ClausesFor(EffectTiming.Activate);
            if (act.Count == 0) return true;
            if (act.Exists(NeedsHardBoard)) return true;
            if (act.Exists(c => c != null && c.OpponentTurnOnly)) return true;
            Provision(engine, prog);
            var card = PutInHand(engine, engine.Player, def.id);
            if (!engine.CanActivateSpellTrap(engine.Player, card, fromHand: true))
            {
                fail.Add(def.name ?? $"#{def.id}");
                return false;
            }

            if (!engine.TryActivateSpellTrap(engine.Player, card, fromHand: true))
            {
                fail.Add((def.name ?? $"#{def.id}") + " activate");
                return false;
            }

            if (!ResolvePendingPicks(engine, def, fail, "target"))
                return false;

            return true;
        }

        static bool TryTrap(DuelEngine engine, CardDef def, CompiledCardProgram prog, List<string> fail)
        {
            var act = prog.ClausesFor(EffectTiming.Activate);
            if (act.Exists(c => c != null && c.OpponentTurnOnly))
                return true;
            if (act.Exists(NeedsHardBoard))
                return true;
            if (prog.HasTiming(EffectTiming.DamageCalculation) ||
                prog.HasTiming(EffectTiming.YouTakeLifePointDamage))
                return true;

            Provision(engine, prog);
            var st = PlaceSetTrap(engine, engine.Player, def.id, 2);
            st.SetThisTurn = false;
            var tried = false;
            var ok = false;
            if (prog.HasTiming(EffectTiming.AttackDeclared))
            {
                tried = true;
                ok = SpellTrapEffects.IsLegalResponseCard(engine, engine.Player, st,
                    ResponseTiming.AttackDeclared, null);
            }

            if (!ok && prog.HasTiming(EffectTiming.OpponentNormalOrFlipSummon))
            {
                tried = true;
                var cl = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
                CardInstance summoned;
                if (cl.Count > 0 && cl[0] != null && cl[0].RequiresSummonedIsToken)
                {
                    summoned = engine.CreateToken("Corpus Token", "Warrior", "EARTH", 1, 0, 0);
                    engine.Opponent.MonsterZones[3].Occupant = summoned;
                    summoned.WasSpecialSummoned = true;
                }
                else if (cl.Count > 0 && cl[0] != null && cl[0].RequiresSummonedIsFusion)
                {
                    summoned = PlaceMonster(engine, engine.Opponent, 11901678, 3,
                        BattlePosition.Attack, true);
                    summoned.WasSpecialSummoned = true;
                }
                else
                {
                    var sid = Bewd;
                    if (cl.Count > 0 && cl[0] != null && cl[0].AmountIsDefMax)
                        sid = 51275027; // Unhappy Maiden DEF 100
                    else if (cl.Count > 0 && cl[0] != null && cl[0].AmountIsAtkMax)
                        sid = 13179332; // Charcoal Inpachi ATK 100
                    summoned = PlaceMonster(engine, engine.Opponent, sid, 3,
                        BattlePosition.Attack, true);
                    if (cl.Count > 0 && cl[0] != null && cl[0].AnswersSpecialSummon &&
                        !cl[0].AnswersControllerSummon)
                    {
                        // leave as NS
                    }
                }

                ok = SpellTrapEffects.IsLegalResponseCard(engine, engine.Player, st,
                    ResponseTiming.MonsterSummoned, summoned);
            }

            if (!ok && prog.HasTiming(EffectTiming.Activate))
            {
                tried = true;
                ok = engine.CanActivateSpellTrap(engine.Player, st, fromHand: false);
            }

            if (!ok && def.IsContinuousSpellOrTrap && prog.FullyCompiled &&
                !prog.HasTiming(EffectTiming.AttackDeclared) &&
                !prog.HasTiming(EffectTiming.OpponentNormalOrFlipSummon) &&
                !prog.HasTiming(EffectTiming.Activate))
            {
                tried = true;
                ok = engine.CanActivateSpellTrap(engine.Player, st, fromHand: false);
            }

            if (!tried)
                return true;
            if (!ok)
            {
                fail.Add(def.name ?? $"#{def.id}");
                return false;
            }

            return true;
        }

        static bool TryFlip(DuelEngine engine, CardDef def, List<string> fail)
        {
            var prog = CardTextEffectCompiler.Compile(def);
            if (prog != null) Provision(engine, prog);
            var m = PlaceMonster(engine, engine.Player, def.id, 2, BattlePosition.Defense, false);
            m.SetThisTurn = false;
            m.SummonedThisTurn = false;
            if (!engine.CanFlipSummon(engine.Player, m) ||
                !engine.TryFlipSummon(engine.Player, m))
            {
                fail.Add(def.name ?? $"#{def.id}");
                return false;
            }

            if (engine.IsAwaitingEffectTarget)
            {
                var pick = engine.PendingActivation.LegalTargets.FirstOrDefault();
                if (pick != null)
                    engine.TrySelectEffectTarget(pick);
                else
                    engine.CancelEffectTargeting();
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return true;
        }

        static bool TryIgnition(DuelEngine engine, CardDef def, CompiledCardProgram prog, List<string> fail)
        {
            var act = prog.ClausesFor(EffectTiming.Activate);
            if (act.Exists(NeedsHardBoard)) return true;
            if (act.Exists(c => c != null && (c.IsQuickEffect || c.OpponentTurnOnly)))
                return true;
            Provision(engine, prog);
            var m = PlaceMonster(engine, engine.Player, def.id, 0, BattlePosition.Attack, true);
            m.SummonedThisTurn = false;
            m.EffectUsedThisTurn = false;
            if (!engine.CanActivateSpellTrap(engine.Player, m, fromHand: false))
            {
                fail.Add(def.name ?? $"#{def.id}");
                return false;
            }

            if (!engine.TryActivateSpellTrap(engine.Player, m, fromHand: false))
            {
                fail.Add((def.name ?? $"#{def.id}") + " activate");
                return false;
            }

            if (!ResolvePendingPicks(engine, def, fail, "target"))
                return false;

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return true;
        }

        static bool TryStandbyOrAura(DuelEngine engine, CardDef def, List<string> fail)
        {
            PlaceMonster(engine, engine.Player, def.id, 2, BattlePosition.Attack, true);
            try
            {
                TextEffectRuntime.FirePhaseTriggers(engine, engine.Player, EffectTiming.StandbyPhase);
                engine.NotifyPublic();
            }
            catch (Exception ex)
            {
                fail.Add((def.name ?? $"#{def.id}") + " " + ex.GetType().Name);
                return false;
            }

            return true;
        }

        static DuelEngine Fresh(CardDatabase db, DeckFile p, DeckFile a)
        {
            var engine = new DuelEngine();
            engine.StartDuel(db, p, a, cinematicOpening: false);
            return engine;
        }

        static void ClearBoard(DuelEngine engine)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                for (var i = 0; i < who.MonsterZones.Length; i++)
                    who.MonsterZones[i].Occupant = null;
                for (var i = 0; i < who.SpellTrapZones.Length; i++)
                    who.SpellTrapZones[i].Occupant = null;
                if (who.FieldSpellZone != null)
                    who.FieldSpellZone.Occupant = null;
                who.NormalSummonUsed = false;
            }

            engine.ClearPendingActivation();
            engine.PendingTributes.Clear();
        }

        static CardInstance PutInHand(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Hand.Add(c);
            return c;
        }

        static CardInstance PlaceMonster(DuelEngine engine, DuelistState who, int id, int zone,
            BattlePosition pos, bool faceUp)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = faceUp;
            c.Position = pos;
            c.SetThisTurn = false;
            c.SummonedThisTurn = false;
            who.MonsterZones[zone].Occupant = c;
            return c;
        }

        static CardInstance PlaceSetTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = false;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static bool ResolvePendingPicks(DuelEngine engine, CardDef def, List<string> fail,
            string tag)
        {
            for (var n = 0; n < 4 && engine.IsAwaitingEffectTarget; n++)
            {
                var pick = engine.PendingActivation?.LegalTargets?.FirstOrDefault();
                if (pick == null || !engine.TrySelectEffectTarget(pick))
                {
                    fail.Add((def.name ?? $"#{def.id}") + " " + tag);
                    return false;
                }
            }

            if (engine.IsAwaitingEffectTarget)
            {
                fail.Add((def.name ?? $"#{def.id}") + " " + tag + " stuck");
                return false;
            }

            return true;
        }

        static string Join(List<string> xs)
        {
            if (xs == null || xs.Count == 0) return "";
            return string.Join(", ", xs.Take(10)) + (xs.Count > 10 ? $" (+{xs.Count - 10})" : "");
        }
    }
}
