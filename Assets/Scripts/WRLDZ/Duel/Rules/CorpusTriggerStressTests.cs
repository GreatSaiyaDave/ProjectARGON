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
        const int CallOfTheHaunted = 97077563; // face-up Continuous Trap for Remove Trap
        const int GaiaTheDragonChampion = 66889139; // face-up Fusion Monster for De-Fusion (FlipCostsAndFusion batch)

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
                   c.OnlyDuringOwnStandby ||
                   // Token Thanksgiving: needs a Token on the field (Lua target chk).
                   (c.Action == EffectActionKind.DestroyTokensInflictPer && c.TokenDestroyGainsLp) ||
                   // Cold Wave (LocksAndZones v142): only before anything happens in Main Phase 1 — this corpus
                   // board is built mid-phase; the batch tests drive it from the real start of the phase.
                   c.ActivationCondition == LocksAndZonesRuntime.StartOfMainPhase1 ||
                   // Chosen One (NegationsAndGySummons v159): needs a monster and 2 other Spell/Traps in the hand
                   // (NegationsAndGySummonsRegressionTests drive it).
                   c.ActivationCondition == NegationsAndGySummonsRuntime.ChosenOneHand ||
                   // Pyro Clock of Destiny (PhaseTimersAndDeckTop v163): needs a running turn count (Swords of Revealing
                   // Light …); PhaseTimersAndDeckTopRegressionTests drive it.
                   c.ActivationCondition == PhaseTimersAndDeckTopRuntime.TurnCountExists ||
                   c.Timing == EffectTiming.PositionChanged ||
                   c.Timing == EffectTiming.ThisCardDeclaresAttack ||
                   c.Action == EffectActionKind.PayLpToDeclareAttack ||
                   !string.IsNullOrEmpty(c.RequiresFaceUpName) ||
                   c.RequiresDiscardSelf ||
                   c.RequiresSendThisToGy ||
                   c.RequiresSendHandToGy ||
                   c.RequiresSendOtherYouControl ||
                   c.RequiresSendEquipSpellOnThisToGy ||
                   c.PayLpAmount > 0 ||
                   c.BanishFromGyCount > 0 ||
                   c.RequiresRemoveSpellCounters > 0 ||
                   c.RequiresLpCostMultiple > 0 ||
                   (c.RequiresDiscardCost &&
                    c.Action != EffectActionKind.AddFromGyToHand) ||
                   c.Action == EffectActionKind.FusionSummonRegistered ||
                   c.Action == EffectActionKind.SpecialSummonFusionFromExtra ||
                   c.Action == EffectActionKind.RitualSummon ||
                   c.Action == EffectActionKind.SpecialSummonFromHand ||
                   c.RequiresSecondTarget ||
                   (!string.IsNullOrEmpty(c.RaceFilter) &&
                    (c.Zone == EffectZoneFilter.ControllerGyMonsters ||
                     c.Zone == EffectZoneFilter.EitherGyMonsters ||
                     c.Zone == EffectZoneFilter.OppGyMonsters));
        }

        static void Provision(DuelEngine engine, CompiledCardProgram prog)
        {
            var p = engine.Player;
            var opp = engine.Opponent;
            var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
            PlaceMonster(engine, opp, Celtic, 2, BattlePosition.Attack, true);
            PlaceSetTrap(engine, opp, 5318639, 1);
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                // Collected Power (EquipFamily batch): the window needs a face-up Equip Card on the field.
                // Tailor of the Fickle (EquipModel batch): an Equip Card with another correct target (the opponent's
                // face-up Celtic Guardian takes Axe of Despair).
                // Magic Reflector (EquipAndSpellProtection batch): another face-up Spell you control.
                // Mask of Dispel (StandbyBurnsAndDice batch, v162): another face-up Spell on the field.
                if ((c.ActivationCondition == EquipFamilyRuntime.FaceUpEquipCardOnField ||
                     c.Zone == EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget ||
                     c.Zone == EffectZoneFilter.ControllerFaceUpSpellsExceptThis ||
                     c.Zone == EffectZoneFilter.FieldFaceUpSpells) && p.SpellTrapZones[0].IsEmpty)
                {
                    var axe = engine.CreateCardInstance(40619825); // Axe of Despair
                    axe.FaceUp = true;
                    p.SpellTrapZones[0].Occupant = axe;
                    axe.EquippedTo = bewd;
                    bewd.Equips.Add(axe);
                }

                // Double Snare (NegationMonsters batch, v161): a face-up card that negates Trap effects (Jinzo).
                if (c.Zone == EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects && opp.MonsterZones[3].IsEmpty)
                    PlaceMonster(engine, opp, 77585513, 3, BattlePosition.Attack, true);

                // Emergency Provisions (SimpleSpellTraps batch): another Spell/Trap you control to send as the cost.
                if (c.ActivationCondition == SimpleSpellTrapsRuntime.OtherSpellTrapYouControl && p.SpellTrapZones[1].IsEmpty)
                    PlaceSetTrap(engine, p, 4206964, 1); // Trap Hole

                // Fiend Comedian (SimpleSpellTraps batch): a card in the opponent's GY, and no more of them than the
                // Player's 8-card Deck covers. The shared corpus engine's GYs change with earlier cards (Fiber Jar
                // shuffles every GY into the Deck — DeckReturnsAndMassEffects batch, v161), so seat it explicitly.
                if (c.ActivationCondition == SimpleSpellTrapsRuntime.OppGyCardsAndDeckCovers)
                {
                    opp.Graveyard.Clear();
                    opp.Graveyard.Add(engine.CreateCardInstance(Celtic));
                }

                // Bubble Crash (DeckReturnsAndMassEffects batch, v161): a player with N or more cards in hand + field.
                if (c.ActivationCondition == DeckReturnsAndMassEffectsRuntime.AnyPlayerHandAndFieldAtLeast)
                {
                    var need = c.ConditionAmount > 0 ? c.ConditionAmount : 6;
                    while (opp.Hand.Count + opp.MonstersOnField().Count() + opp.SpellTrapsOnField().Count() < need)
                        opp.Hand.Add(engine.CreateCardInstance(Celtic));
                }

                // Needle Ceiling (PgdSpellTrapBasics batch, v162): N or more monsters on the field.
                if (c.ActivationCondition == PgdSpellTrapBasicsRuntime.FieldMonstersAtLeastWithFaceUp)
                {
                    if (p.MonsterZones[1].IsEmpty) PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                    if (opp.MonsterZones[4].IsEmpty) PlaceMonster(engine, opp, Celtic, 4, BattlePosition.Attack, true);
                }

                // Secret Pass to the Treasures (PgdSpellTrapBasics batch, v162): a face-up monster you control with
                // 1000 or less ATK (Mystical Elf, 800).
                if (c.Action == EffectActionKind.TargetCanAttackDirectlyThisTurn && p.MonsterZones[2].IsEmpty)
                    PlaceMonster(engine, p, 15025844, 2, BattlePosition.Attack, true);

                // Graverobber (PerCopyDeck batch, v163): a Spell in the opponent's GY to target.
                if (c.Zone == EffectZoneFilter.OppGySpells && !opp.Graveyard.Exists(g => g?.Def != null && g.Def.IsSpell))
                    opp.Graveyard.Add(engine.CreateCardInstance(5318639)); // Mystical Space Typhoon

                // MfcSimpleSpellsB batch (v174): Emblem of Dragon Destroyer — a monster named
                // NamedCard in the Deck (or GY) to search. The shared board only holds Celtics,
                // so seat the named card in the Deck; the batch's own duels cover the GY fallback
                // and the nothing-to-find refusal.
                if (c.Zone == EffectZoneFilter.ControllerDeckOrGyNamed && !string.IsNullOrEmpty(c.NamedCard) &&
                    engine.Database != null)
                {
                    var want = c.NamedCard;
                    var has = false;
                    foreach (var id in p.Deck)
                    {
                        if (engine.Database.TryGet(id, out var d) && d != null && d.name != null &&
                            d.name.Equals(want, StringComparison.OrdinalIgnoreCase)) { has = true; break; }
                    }
                    if (!has)
                    {
                        foreach (var cand in engine.Database.GetAllCards())
                        {
                            if (cand == null || cand.name == null) continue;
                            if (cand.name.Equals(want, StringComparison.OrdinalIgnoreCase))
                            {
                                p.Deck.Add(cand.id);
                                break;
                            }
                        }
                    }
                }

                // Heavy Slump: the window needs the opponent to hold N cards.
                if (c.RequiresOpponentHandAtLeast > 0)
                {
                    while (opp.Hand.Count < c.RequiresOpponentHandAtLeast)
                        opp.Hand.Add(engine.CreateCardInstance(Celtic));
                }

                if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                    c.RequiresFaceUpName.IndexOf("Umi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var umi = engine.CreateCardInstance(Umi);
                    umi.FaceUp = true;
                    if (p.FieldSpellZone != null)
                        p.FieldSpellZone.Occupant = umi;
                }

                if (!string.IsNullOrEmpty(c.RequiresFaceUpName) && engine.Database != null)
                {
                    var want = c.RequiresFaceUpName;
                    if (want.IndexOf("Umi", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        CardDef exact = null;
                        CardDef fuzzy = null;
                        foreach (var cand in engine.Database.GetAllCards())
                        {
                            if (cand == null || !cand.IsMonster || cand.IsExtraDeck) continue;
                            if (cand.name == null) continue;
                            if (cand.name.Equals(want, StringComparison.OrdinalIgnoreCase))
                            {
                                exact = cand;
                                break;
                            }
                            if (fuzzy == null &&
                                cand.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                                fuzzy = cand;
                        }

                        var pick = exact ?? fuzzy;
                        if (pick != null)
                            PlaceMonster(engine, p, pick.id, 1, BattlePosition.Attack, true);
                    }
                }

                // Battle-Scarred targets an "Archfiend" monster you control.
                if (!string.IsNullOrEmpty(c.TargetSeriesName) &&
                    c.TargetSeriesName.IndexOf("Archfiend", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    c.Zone == EffectZoneFilter.ControllerMonsters)
                    PlaceMonster(engine, p, ArchfiendSoldier, 1, BattlePosition.Attack, true);

                if (c.Zone == EffectZoneFilter.DeckFieldSpells)
                    p.Deck.Insert(0, Alo);

                // Acid Trap Hole targets a face-down monster (FieldFaceDownMonsters).
                if (c.Zone == EffectZoneFilter.FieldFaceDownMonsters)
                    PlaceMonster(engine, opp, Celtic, 4, BattlePosition.Defense, false);

                // FlipCostsAndFusion batch: De-Fusion targets a face-up Fusion Monster (FieldFaceUpFusionMonsters).
                if (c.Zone == EffectZoneFilter.FieldFaceUpFusionMonsters)
                    PlaceMonster(engine, opp, GaiaTheDragonChampion, 1, BattlePosition.Attack, true);

                // TargetedClassics Soft: Miracle Dig — enough face-up banished monsters to target.
                if (c.Zone == EffectZoneFilter.ControllerBanishedFaceUpMonsters)
                {
                    var need = Math.Max(Math.Max(1, c.TargetCount), Math.Max(1, c.ConditionAmount));
                    while (TargetedClassicsRuntime.CountFaceUpBanishedMonsters(p) < need)
                    {
                        var ban = engine.CreateCardInstance(Celtic);
                        ban.FaceUp = true;
                        p.Banished.Add(ban);
                    }
                }

                // TargetedClassics Soft: Remove Trap — a face-up Trap Card on the field.
                if (c.Zone == EffectZoneFilter.FieldSpellTraps && c.RequiresTargetFaceUp &&
                    string.Equals(c.TargetCardKind, "Trap", StringComparison.OrdinalIgnoreCase))
                {
                    var fuTrap = PlaceSetTrap(engine, opp, CallOfTheHaunted, 0);
                    fuTrap.FaceUp = true;
                }

                if ((c.Action == EffectActionKind.AddNamedFromDeckToHand ||
                     c.Action == EffectActionKind.SpecialSummonNamed) &&
                    !string.IsNullOrEmpty(c.NamedCard) && engine.Database != null)
                {
                    var want = c.NamedCard;
                    var placed = false;
                    CardDef exact = null;
                    CardDef fuzzy = null;
                    foreach (var cand in engine.Database.GetAllCards())
                    {
                        if (cand == null) continue;
                        var n = cand.name ?? "";
                        if (n.Equals(want, StringComparison.OrdinalIgnoreCase))
                        {
                            exact = cand;
                            break;
                        }
                        if (fuzzy == null && c.NamedCardIsSeries &&
                            n.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                            fuzzy = cand;
                    }

                    var pick = exact ?? fuzzy;
                    if (pick != null)
                    {
                        p.Deck.Insert(0, pick.id);
                        placed = true;
                    }

                    if (!placed && prog.CardId > 0)
                        p.Deck.Insert(0, prog.CardId);
                }

                // Rock Bombardment sentence: send 1 <Type> from the Deck as cost. The corpus
                // board otherwise has no Rock, so a FullyCompiled trap looks illegal.
                if (c.RequiresSendRaceFromDeck)
                {
                    var raceId = FirstMonsterId(engine, d =>
                        ClassicEraTemplates.RaceMatches(d.race, c.RaceFilter));
                    if (raceId > 0) p.Deck.Insert(0, raceId);
                }

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
                    // SummonTriggersAndTributes batch (v163): a named GY Special Summon (Rite of Spirit's "Gravekeeper's")
                    // only offers monsters with that name.
                    if (c.Action == EffectActionKind.SpecialSummonFromGy && !string.IsNullOrEmpty(c.NamedCard) &&
                        engine.Database != null)
                    {
                        var named = engine.Database.GetAllCards().FirstOrDefault(d =>
                            d != null && d.IsMonster && !d.IsExtraDeck && d.name != null &&
                            (c.NamedCardIsSeries
                                ? d.name.IndexOf(c.NamedCard, StringComparison.OrdinalIgnoreCase) >= 0
                                : string.Equals(d.name, c.NamedCard, StringComparison.OrdinalIgnoreCase)));
                        if (named != null)
                            p.Graveyard.Add(engine.CreateCardInstance(named.id));
                    }
                }

                // Classic-era target zones (Gravedigger Ghoul / Soul Release / Stop Defense /
                // Two-Pronged Attack): give each targeted clause enough legal targets.
                if (c.Zone == EffectZoneFilter.OppGyMonsters || c.Zone == EffectZoneFilter.AnyGyCards)
                {
                    opp.Graveyard.Add(engine.CreateCardInstance(Celtic));
                    opp.Graveyard.Add(engine.CreateCardInstance(Bewd));
                }

                if (c.Zone == EffectZoneFilter.ControllerHandMonsters &&
                    c.Action == EffectActionKind.DiscardChosenFromHand)
                    PutInHand(engine, p, Celtic);

                if (c.Zone == EffectZoneFilter.OppDefensePositionMonsters)
                    PlaceMonster(engine, opp, Celtic, 3, BattlePosition.Defense, false);

                if (c.Zone == EffectZoneFilter.ControllerAnyMonsters)
                    for (var z = 1; z < p.MonsterZones.Length && p.MonsterCount < Math.Max(1, c.TargetCount); z++)
                        if (p.MonsterZones[z].IsEmpty)
                            PlaceMonster(engine, p, Celtic, z, BattlePosition.Attack, true);

                // PSV tranche: N-card discard costs and "Activate only when …" conditions.
                for (var i = 0; i < c.DiscardCostCount; i++)
                    PutInHand(engine, p, Celtic);
                switch (c.ActivationCondition)
                {
                    case "OppLpAtMost":
                        opp.LifePoints = Math.Min(opp.LifePoints, c.ConditionAmount);
                        break;
                    case "YourGyMonstersAtLeast":
                        while (p.Graveyard.Count(g => g?.Def != null && g.Def.IsMonster) < c.ConditionAmount)
                            p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                        break;
                    case "FaceDownDefOrFaceUpEffect":
                        PlaceMonster(engine, opp, Celtic, 3, BattlePosition.Defense, false);
                        break;
                    case "HandHasOtherCard":
                        PutInHand(engine, p, Celtic);
                        break;
                    // PerCountBurns (v141): D.D. Dynamite / Cemetary Bomb need something to count.
                    case PerCountBurnsRuntime.OppBanishedAtLeast:
                        while (opp.Banished.Count < Math.Max(1, c.ConditionAmount))
                            opp.Banished.Add(engine.CreateCardInstance(Celtic));
                        break;
                    case PerCountBurnsRuntime.OppGyAtLeast:
                        while (opp.Graveyard.Count < Math.Max(1, c.ConditionAmount))
                            opp.Graveyard.Add(engine.CreateCardInstance(Celtic));
                        break;
                    // HandDisruption (v142): Gamble needs a small hand against a big one; Painful Choice a Deck of N.
                    case HandDisruptionRuntime.HandSizes:
                        while (p.Hand.Count > Math.Max(0, c.ConditionAmount))
                            p.Hand.RemoveAt(p.Hand.Count - 1);
                        break;
                    case HandDisruptionRuntime.ControllerDeckAtLeast:
                        while (p.Deck.Count < Math.Max(1, c.ConditionAmount))
                            p.Deck.Add(Celtic);
                        break;
                    // TargetedClassics Soft: Miracle Dig needs 5+ face-up banished monsters.
                    case TargetedClassicsRuntime.YourFaceUpBanishedMonstersAtLeast:
                        while (TargetedClassicsRuntime.CountFaceUpBanishedMonsters(p) <
                               Math.Max(1, c.ConditionAmount))
                        {
                            var ban = engine.CreateCardInstance(Celtic);
                            ban.FaceUp = true;
                            p.Banished.Add(ban);
                        }
                        break;
                    case "YouControlFaceUpRace":
                    {
                        var raceId = FirstMonsterId(engine, d =>
                            ClassicEraTemplates.RaceMatches(d.race, c.RaceFilter));
                        if (raceId > 0) PlaceMonster(engine, p, raceId, 3, BattlePosition.Attack, true);
                        break;
                    }
                    case PoisonMeteorCestusRuntime.OppLpGreaterThan:
                        if (opp.LifePoints <= c.ConditionAmount)
                            opp.LifePoints = c.ConditionAmount + 1;
                        break;
                    case PoisonMeteorCestusRuntime.YouControlRace:
                    {
                        var race = string.IsNullOrEmpty(c.ConditionName) ? c.RaceFilter : c.ConditionName;
                        var raceId = FirstMonsterId(engine, d =>
                            ClassicEraTemplates.RaceMatches(d.race, race));
                        if (raceId > 0) PlaceMonster(engine, p, raceId, 3, BattlePosition.Attack, true);
                        break;
                    }
                    case "FaceUpNamedMonster":
                    {
                        var namedId = FirstMonsterId(engine, d =>
                            string.Equals(d.name, c.ConditionName, StringComparison.OrdinalIgnoreCase));
                        if (namedId > 0) PlaceMonster(engine, p, namedId, 3, BattlePosition.Attack, true);
                        break;
                    }
                }

                if (c.Zone == EffectZoneFilter.HandOrDeckNamed && !string.IsNullOrEmpty(c.NamedCard))
                {
                    var first = c.NamedCard.Split('|')[0].Trim();
                    var deckId = FirstMonsterId(engine, d =>
                        string.Equals(d.name, first, StringComparison.OrdinalIgnoreCase));
                    if (deckId > 0) p.Deck.Insert(0, deckId);
                }

                if (c.Zone == EffectZoneFilter.ControllerGySpells)
                    p.Graveyard.Add(engine.CreateCardInstance(PotOfGreed));
                if (c.Zone == EffectZoneFilter.ControllerGyTraps)
                    p.Graveyard.Add(engine.CreateCardInstance(MirrorForce));
                // SimpleTargetUtility batch (v173): Fairy of the Spring — an Equip Spell in the player's
                // GY to target.
                if (c.Zone == EffectZoneFilter.ControllerGyEquipSpells &&
                    !p.Graveyard.Exists(g => g?.Def != null && g.Def.IsSpell && g.Def.IsEquipSpell))
                    p.Graveyard.Add(engine.CreateCardInstance(40619825)); // Axe of Despair

                var hostName = c.EquipHostName ??
                               (c.Action == EffectActionKind.EquipThisToTarget ? c.NamedCard : null);
                if (!string.IsNullOrEmpty(hostName) && engine.Database != null)
                {
                    foreach (var cand in engine.Database.GetAllCards())
                    {
                        if (cand == null || !cand.IsMonster || cand.IsExtraDeck) continue;
                        if (cand.name != null &&
                            cand.name.Equals(hostName, StringComparison.OrdinalIgnoreCase))
                        {
                            PlaceMonster(engine, p, cand.id, 1, BattlePosition.Attack, true);
                            break;
                        }
                    }
                }

                if (c.Action == EffectActionKind.ChangeBattlePosition && c.ForceAttackPosition)
                    PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Defense, false);
            }
        }

        static int FirstMonsterId(DuelEngine engine, Func<CardDef, bool> ok)
        {
            if (engine?.Database == null) return 0;
            foreach (var d in engine.Database.GetAllCards())
                if (d != null && d.IsMonster && !d.IsExtraDeck && ok(d))
                    return d.id;
            return 0;
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

            // A Field Spell that declares a Type as it is activated (Array of Revealing Light, BattleRules batch) asks the
            // player first.
            if (!ResolvePendingPicks(engine, def, fail, "field pick")) return false;

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
            // Soft residual Fake Trap: Activate-only vs opp destroy-your-Trap chain. Soft-skip
            // open-state corpus (NeedsHardBoard Soft-equivalent; Soft ≠ hard).
            if (act.Exists(c => c != null &&
                    c.ActivationCondition == "OppEffectWouldDestroyYourTrap"))
                return true;
            // World Suppression / Mystic Probe / Metal Detector: only in a chain window, answering the
            // activation of a card of that kind (TurnNegationTrapsRegressionTests drive that window).
            if (act.Exists(c => c != null &&
                    c.ActivationCondition == TurnNegationTrapsRuntime.ChainLinkActivatesCardFamily))
                return true;
            // Gust / Driving Snow: only in the window after an opponent's effect destroyed your Spell / Trap
            // (SpellTrapDestroyedResponseRegressionTests drive that window).
            if (act.Exists(c => c != null &&
                    c.ActivationCondition == SpellTrapDestroyedResponseRuntime.YourCardsDestroyedByOpponentEffect))
                return true;
            // Appropriate (HandDisruption v142): only in the "opponent drew outside either Draw Phase" window
            // (HandDisruptionRegressionTests drive that window).
            if (HandDisruptionRuntime.AnswersDrawWindow(prog))
                return true;
            // Fairy's Hand Mirror / Shift: only answering an opponent's targeting link (Shift: or attack)
            // (RedirectAndReplaceRegressionTests drive those windows).
            if (act.Exists(c => c != null && RedirectAndReplaceRuntime.IsRedirectCondition(c.ActivationCondition)))
                return true;
            // BattleWindows batch: Destruction Punch (the end of the Damage Step), Michizure (a monster sent from the field
            // to your GY) and Dark Spirit of the Silent (an opponent's attack, with another face-up monster of theirs)
            // answer only their own windows (BattleWindowsRegressionTests drive them).
            if (BattleWindowsRuntime.AnswersOnlyBattleWindows(prog))
                return true;
            // EngineHygiene batch: Forced Requisition answers only "you discarded from your hand"
            // (EngineHygieneRegressionTests drive that window).
            if (DiscardEvents.AnswersDiscardWindow(prog))
                return true;
            // Armored Glass (EquipModel batch): only in the window right after a monster is equipped with an Equip Card
            // (EquipModelRegressionTests drive that window).
            if (EquipModelRuntime.AnswersEquipWindow(prog))
                return true;
            // Major Riot (ForcedActivationAndSubstitution batch, v163): only in the window right after an opponent's
            // effect returned your monster(s) to your hand (ForcedActivationAndSubstitutionRegressionTests drive it).
            if (MajorRiotRuntime.AnswersWindow(prog))
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
                    var setOnly = cl.Count > 0 && cl[0] != null && cl[0].RequiresSummonedFaceDown;
                    summoned = PlaceMonster(engine, engine.Opponent, sid, 3,
                        setOnly ? BattlePosition.Defense : BattlePosition.Attack, !setOnly);
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
            // Multi-target cards (Soul Release: up to 5) need more than 4 picks.
            for (var n = 0; n < 12 && engine.IsAwaitingEffectTarget; n++)
            {
                var pending = engine.PendingActivation;
                if (pending != null && pending.TargetPicksAreUpTo &&
                    pending.ChosenTargets != null && pending.ChosenTargets.Count > 0)
                {
                    if (!TextEffectRuntime.TryFinishUpToTargets(engine))
                    {
                        fail.Add((def.name ?? $"#{def.id}") + " " + tag + " up-to");
                        return false;
                    }
                    break;
                }

                var pick = pending?.LegalTargets?.FirstOrDefault();
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
