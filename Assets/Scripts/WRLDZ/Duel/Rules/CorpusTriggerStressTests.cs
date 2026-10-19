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
                   // ChoiceAmongEffects batch: Fuh-Rin-Ka-Zan (four Attributes face-up), Penalty Game! (exactly 4 cards in the
                   // opponent's hand); ChoiceAmongEffectsRegressionTests drive them.
                   c.ActivationCondition == ChoiceAmongEffectsRuntime.FaceUpAttributesOnField ||
                   c.ActivationCondition == ChoiceAmongEffectsRuntime.OpponentHandExactly ||
                   // DcrSpellTrapUtility batch: Frozen Soul's LP gap, Dragged Down into the Grave's two hands, Spell
                   // Reproduction's 2 Spells in hand, Ray of Hope's 2 LIGHT monsters in your GY
                   // (DcrSpellTrapUtilityRegressionTests drive them). LvChain batch: The Graveyard in the Fourth
                   // Dimension's 2 "LV" monsters in your GY (LvChainRegressionTests drives it).
                   c.ActivationCondition == DcrSpellTrapUtilityRuntime.YourLpAtLeastLowerThanOpponent ||
                   c.ActivationCondition == DcrSpellTrapUtilityRuntime.BothHandsHaveACard ||
                   DcrSpellTrapUtilityRuntime.IsSpellSendCost(c) ||
                   (c.Action == EffectActionKind.ReturnChosenToDeckShuffle &&
                    (!string.IsNullOrEmpty(c.AttributeFilter) || !string.IsNullOrEmpty(c.TargetSeriesName))) ||
                   // Pyro Clock of Destiny (PhaseTimersAndDeckTop v163): needs a running turn count (Swords of Revealing
                   // Light …); PhaseTimersAndDeckTopRegressionTests drive it.
                   c.ActivationCondition == PhaseTimersAndDeckTopRuntime.TurnCountExists ||
                   // Huge Revolution (FlipMassAndOpponentPicks): three named monsters face-up on your field
                   // (FlipMassAndOpponentPicksRegressionTests drive it).
                   c.ActivationCondition == FlipMassAndOpponentPicksRuntime.YourMainPhaseWithNamedFaceUp ||
                   // SimpleTriggersAndSwarm batch: Mustering of the Dark Scorpions (a face-up Don Zaloog and a Dark Scorpion in
                   // the hand), Dark Scorpion Combination (5 named monsters face-up); SimpleTriggersAndSwarmRegressionTests
                   // drive them.
                   c.ActivationCondition == SimpleTriggersAndSwarmRuntime.FaceUpNamedAndHandSummonable ||
                   c.ActivationCondition == SimpleTriggersAndSwarmRuntime.AllNamedFaceUpInYourMonsterZones ||
                   // Contract with Exodia (5 named cards in your GY) and A Deal with Dark Ruler (a Level 8+ monster of
                   // yours sent to the GY this turn): NamedSummonPairsRegressionTests drive them.
                   c.ActivationCondition == NamedSummonPairsRuntime.AllNamedInYourGy ||
                   c.ActivationCondition == NamedSummonPairsRuntime.YourLevelOrHigherMonsterSentToGyThisTurn ||
                   // ConditionalSpellsAndTraps batch: GY / banished counts, named Ojamas, another Spell/Trap you control
                   // (ConditionalSpellsAndTrapsRegressionTests drive them).
                   ConditionalSpellsAndTrapsRuntime.NeedsHardBoard(c) ||
                   // NormalMonsterSpells batch: Normal Monster boards (NormalMonsterSpellsRegressionTests drive them).
                   NormalMonsterSpellsRuntime.NeedsHardBoard(c) ||
                   // RecoveryAndPhaseCosts batch: Dimension Distortion (an empty GY), Reload (another card in the hand), Salvage
                   // (2 monsters of one Attribute with N or less ATK in your GY); RecoveryAndPhaseCostsRegressionTests drive them.
                   c.ActivationCondition == RecoveryAndPhaseCostsRuntime.NoCardsInYourGy ||
                   c.ActivationCondition == RecoveryAndPhaseCostsRuntime.AnotherCardInYourHand ||
                   (c.Action == EffectActionKind.AddFromGyToHand && c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                    !string.IsNullOrEmpty(c.AttributeFilter)) ||
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
                   // SpellCounterSpenders batch: Spell Counters on your side (Mega Ton Magical Cannon, Miracle Restoring,
                   // Anti-Spell); SpellCounterSpendersRegressionTests drive them on a provisioned board.
                   c.RemoveSpellCountersOnYourSide > 0 ||
                   // DestroyWatchersAndSpellShields batch: Blast Magician's per-ATK counter cost
                   // (DestroyWatchersAndSpellShieldsRegressionTests drive it with counters placed).
                   c.SpellCountersPerTargetAtk > 0 ||
                   c.RequiresLpCostMultiple > 0 ||
                   (c.RequiresDiscardCost &&
                    c.Action != EffectActionKind.AddFromGyToHand) ||
                   c.Action == EffectActionKind.FusionSummonRegistered ||
                   c.Action == EffectActionKind.SpecialSummonFusionFromExtra ||
                   c.Action == EffectActionKind.RitualSummon ||
                   c.Action == EffectActionKind.SpecialSummonFromHand ||
                   c.RequiresSecondTarget ||
                   // UnionRiders batch (v175): Combination Attack needs the Battle Phase and a monster that attacked with a
                   // Union equipped (UnionRidersRegressionTests drive it).
                   c.Action == EffectActionKind.SpecialSummonTargetsUnionsThenItAttacksAgain ||
                   // Machine Duplication (DeckReturnsAndMassEffects batch, v204): needs the same-named card
                   // in the Deck, which the corpus cannot seed (TargetCount=1 is then the activation gate).
                   (c.Action == EffectActionKind.SpecialSummonFromDeck && c.SummonSameNameAsTarget) ||
                   // DeckSwarmsAndFusionMaterials batch: the Hex-Sealed Fusions need a Fusion Monster of their Attribute in
                   // the Extra Deck and its other named materials on the field (DeckSwarmsAndFusionMaterialsRegressionTests
                   // drive them).
                   c.ActivationCondition == DeckSwarmsAndFusionMaterialsRuntime.AttributeFusionMaterialsOnField ||
                   // TokensDeclarationsAndPicks batch: Stray Lambs / Scapegoat need a turn with no Summon by the activator (the
                   // shared corpus engine summons as it goes) and Wild Nature's Release a face-up Beast / Beast-Warrior with
                   // DEF; TokensDeclarationsAndPicksRegressionTests drive them.
                   c.ForbidsOtherSummonsThisTurn ||
                   c.Action == EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase ||
                    // GyHandAndSummonEventTriggers batch: Soul Reversal needs 1 Flip monster in your GY (targeted)
                    // and Beckoning Light a card in the hand plus that many of the Attribute in your GY;
                    // GyHandAndSummonEventTriggersRegressionTests drive both boards.
                    string.Equals(c.TargetCardKind,
                        TextEffects.GyHandAndSummonEventTriggersRuntime.FlipKind,
                        System.StringComparison.Ordinal) ||
                    c.ActivationCondition ==
                        TextEffects.GyHandAndSummonEventTriggersRuntime.HandAndAttributeMonstersInGy ||
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

                // Lighten the Load (RecoveryAndPhaseCosts batch): its activation condition wants a Level 7 or higher
                // monster in the hand, so the probe board gets one. Without it the card would be offered with nothing
                // it could ever pick.
                if (c.ActivationCondition == RecoveryAndPhaseCostsRuntime.HandHasLevelOrHigherMonster)
                {
                    var floor = System.Math.Max(1, c.MinLevelOrHigher);
                    if (!p.Hand.Any(h => h != null && h.Def != null && h.Def.IsMonster && h.Def.level >= floor))
                        PutInHand(engine, p, FirstMonsterId(engine, d => d.IsMonster && d.level >= floor));
                }

                // Double Snare (NegationMonsters batch, v161): a face-up card that negates Trap effects (Jinzo).
                if (c.Zone == EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects && opp.MonsterZones[3].IsEmpty)
                    PlaceMonster(engine, opp, 77585513, 3, BattlePosition.Attack, true);

                // Xing Zhen Hu (v208 QC): exactly 2 Set Spell/Trap Cards on the field other than itself.
                if (c.Zone == EffectZoneFilter.FaceDownSpellTraps && c.TargetCount > 1 && opp.SpellTrapZones[2].IsEmpty)
                    PlaceSetTrap(engine, opp, 4206964, 2); // Trap Hole

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

                // Mind Wipe (RecoveryAndPhaseCosts batch): "Activate only if your opponent has 3 or less
                // cards in their hand" — the shared corpus engine's opponent hand is grown by other probes
                // (Heavy Slump, Bubble Crash, …), so trim it to 3 for a legal activation.
                if (c.ActivationCondition == RecoveryAndPhaseCostsRuntime.OpponentHasAtMostThreeInHand)
                {
                    while (opp.Hand.Count > 3) opp.Hand.RemoveAt(opp.Hand.Count - 1);
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

                // Ninjitsu Art of Decoy (ContinuousCannotBeDestroyedByBattle): shadows the "Archfiend" case —
                // target 1 face-up "Ninja" monster you control (Ninja Grandmaster Sasuke, SOD).
                // Level Up! (LvChain batch, v194): "Send 1 face-up "LV" monster you control to the GY; Special
                // Summon 1 monster from your hand or Deck that is listed in the sent monster's text." The shared board's
                // Bewd and Celtic are not LV monsters, so seat one (Horus the Black Flame Dragon LV4 lists LV6).
                if (c.Action == EffectActionKind.SpecialSummonLvProgressionFromSentMonster &&
                    p.MonsterZones[2].IsEmpty)
                    PlaceMonster(engine, p, 75830094, 2, BattlePosition.Attack, true); // Horus LV4

                // Mind Crush (TypeDeclaration batch, v197): "Declare 1 card name; if that card is in your opponent's
                // hand, they must discard all copies of it, otherwise you discard 1 random card." The activation needs
                // a card in the opponent's hand (Their BothHandsHaveACard target chk), so seat one.
                if (c.ActivationCondition ==
                        WRLDZ.Duel.TextEffects.TypeDeclarationRuntime.BothHandsHaveACard)
                {
                    if (engine.Player.Hand.Count == 0)
                        engine.Player.Hand.Add(engine.CreateCardInstance(Celtic));
                    if (engine.Opponent.Hand.Count == 0)
                        engine.Opponent.Hand.Add(engine.CreateCardInstance(Celtic));
                }

                if (c.Action == EffectActionKind.ContinuousCannotBeDestroyedByBattle &&
                    c.Zone == EffectZoneFilter.ControllerMonsters && p.MonsterZones[1].IsEmpty)
                    PlaceMonster(engine, p, 4041838, 1, BattlePosition.Attack, true); // Ninja Grandmaster Sasuke

                // Inferno Fire Blast: target 1 "Red-Eyes B. Dragon" in your Monster Zone.
                if (c.TargetCannotAttackThisTurn && c.Zone == EffectZoneFilter.ControllerMonsters &&
                    p.MonsterZones[1].IsEmpty)
                    PlaceMonster(engine, p, 74677422, 1, BattlePosition.Attack, true); // Red-Eyes B. Dragon

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

                // ChaosBanishSummons batch: Chaosrider Gustaph — a Spell in your GY to banish.
                if (c.ActivationCondition == ChaosBanishSummonsRuntime.YourGySpellBanishable &&
                    !p.Graveyard.Exists(g => g?.Def != null && g.Def.IsSpell))
                    p.Graveyard.Add(engine.CreateCardInstance(55144522)); // Pot of Greed

                // ChaosBanishSummons batch: Primal Seed — a face-up named monster (its condition) and 2 banished cards.
                if (c.Zone == EffectZoneFilter.ControllerBanishedCards)
                {
                    var namedDef = c.ActivationCondition == ChaosBanishSummonsRuntime.NamedFaceUpOnEitherField &&
                                   !string.IsNullOrEmpty(c.ConditionName) && opp.MonsterZones[3].IsEmpty
                        ? engine.Database?.GetAllCards().FirstOrDefault(d =>
                            d != null && string.Equals(d.name, c.ConditionName.Split('|')[0], StringComparison.Ordinal))
                        : null;
                    if (namedDef != null)
                        PlaceMonster(engine, opp, namedDef.id, 3, BattlePosition.Attack, true);
                    while (p.Banished.Count < Math.Max(1, c.TargetCount))
                    {
                        var ban = engine.CreateCardInstance(Celtic);
                        ban.FaceUp = true;
                        p.Banished.Add(ban);
                    }
                }

                // RdsRedoSpellTrap batch: Triangle Ecstasy Spark — a face-up "Harpie Lady Sisters" (its named condition) on the
                // field.
                if (c.ActivationCondition == ChaosBanishSummonsRuntime.NamedFaceUpOnEitherField &&
                    c.Zone != EffectZoneFilter.ControllerBanishedCards && !string.IsNullOrEmpty(c.ConditionName))
                {
                    var names = c.ConditionName.Split('|');
                    var present = p.MonstersOnField().Concat(opp.MonstersOnField())
                        .Any(m => m != null && m.FaceUp && names.Any(m.IsNamed));
                    var zone = Array.FindIndex(p.MonsterZones, z => z.IsEmpty);
                    var namedDef = present || zone < 0
                        ? null
                        : engine.Database?.GetAllCards().FirstOrDefault(d =>
                            d != null && string.Equals(d.name, names[0], StringComparison.Ordinal));
                    if (namedDef != null)
                        PlaceMonster(engine, p, namedDef.id, zone, BattlePosition.Attack, true);
                }

                // BattleAndPositionRecords batch: Curse of Anubis — a face-up Attack Position Effect Monster (Witch of the
                // Black Forest).
                if (c.ActivationCondition == BattleAndPositionRecordsRuntime.FaceUpAttackEffectMonsterOnField &&
                    opp.MonsterZones[4].IsEmpty)
                    PlaceMonster(engine, opp, 78010363, 4, BattlePosition.Attack, true);

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

                // UnionRiders batch (v175): Lunatite equips to any other face-up monster you control; Union Rider takes a
                // face-up Union Monster the opponent controls (Kiryu).
                // Heavy Mech Support Platform (RDS): the typed Union carries a Type filter instead of a host name, so the
                // "any face-up monster" it wants must be one of that Type.
                if (c.UnionEquipsAnyFaceUpMonster && p.MonsterZones[1].IsEmpty)
                {
                    var wanted = string.IsNullOrEmpty(c.RaceFilter)
                        ? Celtic
                        : FirstMonsterId(engine, d => ClassicEraTemplates.RaceMatches(d.race, c.RaceFilter));
                    PlaceMonster(engine, p, wanted > 0 ? wanted : Celtic, 1, BattlePosition.Attack, true);
                }

                // Machine Duplication (DeckReturnsAndMassEffects): needs 1 Machine monster you control with 500 or less
                // ATK on the field so the clause passes IsCorrectEquipTarget + TryActivateSpellTrap. The deck is not
                // needed on the probe board — TargetCount=1 is the gate.
                if (c.Action == EffectActionKind.SpecialSummonFromDeck && c.RaceFilter == "Machine" &&
                    c.AmountIsAtkMax && c.SummonUpTo > 0)
                {
                    var smallMachine = FirstMonsterId(engine, d =>
                        d != null && d.IsMonster && !d.IsExtraDeck &&
                        ClassicEraTemplates.RaceMatches(d.race, "Machine") && d.atk <= 500);
                    if (smallMachine > 0 && p.MonsterZones[1].IsEmpty)
                        PlaceMonster(engine, p, smallMachine, 1, BattlePosition.Attack, true);
                    if (smallMachine > 0 && p.MonsterZones[0].IsEmpty)
                        PlaceMonster(engine, p, smallMachine, 0, BattlePosition.Attack, true);
                }

                if (c.Action == EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis && opp.MonsterZones[3].IsEmpty)
                    PlaceMonster(engine, opp, 84814897, 3, BattlePosition.Attack, true);

                // RdsRedoBattle batch: Rare Metalmorph links to a face-up monster of its Type on the field (a Machine).
                if (c.Action == EffectActionKind.LinkThisToTarget && !string.IsNullOrEmpty(c.RaceFilter) &&
                    p.MonsterZones[3].IsEmpty)
                {
                    var typed = FirstMonsterId(engine, d => d != null && d.IsMonster && !d.IsExtraDeck &&
                                                            ClassicEraTemplates.RaceMatches(d.race, c.RaceFilter));
                    if (typed > 0) PlaceMonster(engine, p, typed, 3, BattlePosition.Attack, true);
                }
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
                // SpellTrapSealsAndImmunity batch: an activation condition this board does not provision (Talisman of Trap
                // Sealing's face-up "Sealmaster Meisei"); the batch tests drive it.
                if (prog.ClauseList.Exists(c => c != null && !string.IsNullOrEmpty(c.ActivationCondition) &&
                                                NeedsHardBoard(c)))
                    return true;
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
            // Thunder of Ruler (ZoneCapsAndPhaseWindows batch): only in the opponent's Standby Phase
            // (ZoneCapsAndPhaseWindowsRegressionTests drive that window).
            if (StandbyWindowRuntime.AnswersWindow(prog))
                return true;
            // Pineapple Blast (FlipMassAndOpponentPicks): answers only its controller's OWN Normal Summon while the opponent
            // controls more monsters (FlipMassAndOpponentPicksRegressionTests drive that window); this probe summons for
            // the opponent.
            if (prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon).Exists(c => c != null &&
                    c.ActivationCondition == FlipMassAndOpponentPicksRuntime.YourNormalSummonOpponentControlsMore))
                return true;
            if (act.Exists(NeedsHardBoard))
                return true;
            // TokensDeclarationsAndPicks batch: A Hero Emerges needs a summonable monster in the hand (this probe empties
            // it); TokensDeclarationsAndPicksRegressionTests drive its attack window.
            if (prog.ClausesFor(EffectTiming.AttackDeclared).Exists(c => c != null &&
                    c.ActivationCondition == TokensDeclarationsAndPicksRuntime.HandMonsterCanBeSpecialSummoned))
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

                // SimpleTriggersAndSpells batch: a coin call made as the effect resolves (Abare Ushioni) is answered.
                if (pending != null && pending.AwaitingCoinCall)
                {
                    if (!engine.TrySelectCoinCall(true))
                    {
                        fail.Add((def.name ?? $"#{def.id}") + " " + tag + " coin call");
                        return false;
                    }

                    continue;
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
