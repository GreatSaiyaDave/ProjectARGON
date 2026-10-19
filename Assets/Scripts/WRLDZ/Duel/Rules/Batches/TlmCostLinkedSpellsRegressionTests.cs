using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TlmCostLinkedSpells batch (v211-tlm-fet): Battery Charger, Minefield Eruption, Card of Sanctity, Gift of the Martyr,
    /// Double Attack, Hero Signal. Compile shapes through the real compiler, near misses that must not compile to these
    /// shapes, and mini-duels through the engine entry points (TryActivateSpellTrap, TrySelectEffectTarget,
    /// CancelEffectTargeting, TryAttack and the end-of-Damage-Step window). Runtime: <see cref="TlmCostLinkedSpellsRuntime"/>.
    /// </summary>
    public static class TlmCostLinkedSpellsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BatteryCharger = 61181383;     // Normal Spell
        const int MinefieldEruption = 85519211;  // Normal Trap
        const int CardOfSanctity = 42664989;     // Normal Spell
        const int GiftOfTheMartyr = 98792570;    // Normal Spell
        const int DoubleAttack = 34187685;       // Normal Spell
        const int HeroSignal = 22020907;         // Normal Trap

        const int BatterymanAA = 63142001;       // Lv3 0/0 "Batteryman"
        const int MineGolem = 76321376;          // Lv3 1000/1900
        const int Celtic = 91152256;             // Celtic Guardian Lv4 1400/1200
        const int GiantSoldier = 13039848;       // Lv3 1300/2000
        const int Kuriboh = 40640057;            // Lv1 300/200
        const int PotOfGreed = 55144522;
        const int SummonedSkull = 70781052;      // Lv6 2500/1200
        const int Avian = 21844577;              // Elemental HERO Lv3
        const int Clayman = 84327329;            // Elemental HERO Lv4 800/2000
        const int Sparkman = 20721928;           // Elemental HERO Lv4 1600/1400
        const int Sangan = 26202165;             // Lv3 1000/600, "sent from the field to the GY" search

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TlmCostLinkedSpells: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            BatteryChargerDuels(db, pDeck, aDeck, Check);
            MinefieldEruptionDuels(db, pDeck, aDeck, Check);
            CardOfSanctityDuels(db, pDeck, aDeck, Check);
            GiftOfTheMartyrDuels(db, pDeck, aDeck, Check);
            DoubleAttackDuels(db, pDeck, aDeck, Check);
            HeroSignalDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Clean(DuelEngine e)
        {
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        /// <summary>A fresh duel with empty boards, hands and GYs; the player is in their turn-1 Main Phase 1.</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck) => Clean(Fresh(db, pDeck, aDeck));

        /// <summary>The Player's Main Phase 1 of turn 3 (a turn they can conduct a Battle Phase), empty boards.</summary>
        static DuelEngine PlayerMainTurn3(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                e.TryEndTurnSafe(e.TurnPlayer);
            }

            return Clean(e);
        }

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(e);
            return Clean(e);
        }

        static CardInstance Attacker(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);
            m.ClearAttackFlags();
            return m;
        }

        static CardInstance AddToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt; pass every window.</summary>
        static bool Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked;
        }

        static void Settle(DuelEngine e)
        {
            Drive(e, _ => false);
            DrainCombat(e);
        }

        static void EndTurn(DuelEngine e)
        {
            Drive(e, _ => false);
            if (e.StandbyPausedFor != null) e.ContinueAfterStandby();
            e.TryEndTurnSafe(e.TurnPlayer);
            Drive(e, _ => false);
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            if (c == null) return "-";
            var at = new List<string>();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) at.Add(tag + ".hand");
                if (who.Graveyard.Contains(c)) at.Add(tag + ".gy");
                if (who.Banished.Contains(c)) at.Add(tag + ".ban");
                if (who.TryFindMonster(c, out _)) at.Add(tag + ".mz");
                if (who.TryFindSpellTrap(c, out _)) at.Add(tag + ".stz");
            }

            return at.Count == 0 ? "none" : string.Join("+", at);
        }

        static string St(DuelEngine e)
        {
            var pend = e.PendingActivation;
            return $"await={e.IsAwaitingEffectTarget} resp={e.IsAwaitingResponse}/{e.PendingResponse?.Timing} " +
                   $"pend={(pend == null ? "-" : $"{pend.AwaitingIgnitionCost}/{pend.AwaitingDiscardCost}/{pend.TargetKind}/{pend.LegalTargets.Count}")}";
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static CardInstance FindOnField(DuelistState who, int id) =>
            who.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == id);

        /// <summary>
        /// Answer every prompt with <paramref name="want"/>; in a chain response window of <paramref name="p"/> that offers
        /// <paramref name="signal"/> (a Chain is open), activate it; pass every other window.
        /// </summary>
        static (bool chained, bool act, int links, string trace) ChainHeroSignal(DuelEngine e, DuelistState p,
            CardInstance signal, Func<CardInstance, bool> want)
        {
            bool chained = false, act = false;
            var links = 0;
            var trace = new List<string>();
            for (var i = 0; i < 16; i++)
            {
                trace.Add($"{e.PendingResponse?.Timing}/{(e.PendingResponse?.Responder == p ? "P" : "O")}" +
                          $"/c{e.Chain.Count}/a{(e.IsAwaitingEffectTarget ? 1 : 0)}");
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                    continue;
                }

                if (!e.IsAwaitingResponse) break;
                var pr = e.PendingResponse;
                if (!chained && pr?.Timing == ResponseTiming.ChainResponse && pr.Responder == p &&
                    (pr.LegalCards?.Contains(signal) ?? false))
                {
                    chained = true;
                    links = e.Chain.Count;
                    act = e.TryActivateSpellTrap(p, signal, fromHand: false);
                    continue;
                }

                e.PassResponse();
            }

            return (chained, act, links, string.Join(" ", trace));
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            bool Impl(int id) => CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

            var bc = Prog(db, BatteryCharger);
            var bcc = bc?.ClauseList.Count == 1 ? bc.ClauseList[0] : null;
            Check("Battery Charger: Pay 500 LP; target 1 \"Batteryman\" monster in your GY; Special Summon it",
                bc != null && bc.FullyCompiled && Impl(BatteryCharger) && bcc != null &&
                bcc.Timing == EffectTiming.Activate && bcc.Action == EffectActionKind.SpecialSummonFromGy &&
                bcc.Zone == EffectZoneFilter.ControllerGyMonsters && bcc.TargetSeriesName == "Batteryman" &&
                bcc.RequiresTargetChoice && bcc.IsPsctTarget && bcc.PayLpAmount == 500 &&
                !bcc.SummonInAttack && !bcc.SummonInDefense && TargetingTruthRuntime.ClauseTargets(bcc) &&
                TlmCostLinkedSpellsRuntime.UsesCompiledActivationPending(bcc),
                Describe(bc));

            var me = Prog(db, MinefieldEruption);
            var burn = me?.ClauseList.Count == 2 ? me.ClauseList[0] : null;
            var wipe = me?.ClauseList.Count == 2 ? me.ClauseList[1] : null;
            Check("Minefield Eruption: 1000 x face-up Mine Golem you control at resolution, then destroy them",
                me != null && me.FullyCompiled && Impl(MinefieldEruption) && burn != null && wipe != null &&
                burn.Action == EffectActionKind.InflictDamageToOpponent && burn.Amount == 1000 &&
                burn.CountSource == TlmCostLinkedSpellsRuntime.FaceUpNamedYouControl && burn.ConditionName == "Mine Golem" &&
                burn.ActivationCondition == TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed &&
                wipe.Action == EffectActionKind.DestroyAllFaceUpNamedYouControl && wipe.ConditionName == "Mine Golem" &&
                !burn.RequiresTargetChoice && !wipe.RequiresTargetChoice,
                Describe(me));

            var cs = Prog(db, CardOfSanctity);
            var csc = cs?.ClauseList.Count == 1 ? cs.ClauseList[0] : null;
            Check("Card of Sanctity: banish-all-other cost; draw until 2 cards in hand",
                cs != null && cs.FullyCompiled && Impl(CardOfSanctity) && csc != null &&
                csc.Action == EffectActionKind.DrawUntilHandSize && csc.Amount == 2 && csc.Side == EffectSide.Controller &&
                csc.CostBanishAllOtherHandAndField && EffectVocabulary.CostOf(csc) == EffectCostKind.Banish &&
                csc.ActivationCondition == TlmCostLinkedSpellsRuntime.OtherCardsInHandAndOnField &&
                csc.CheckedAt == ConditionCheckedAt.Activation,
                Describe(cs));

            var gm = Prog(db, GiftOfTheMartyr);
            var gmc = gm?.ClauseList.Count == 1 ? gm.ClauseList[0] : null;
            Check("Gift of the Martyr: send-you-control cost; target 1 face-up monster you control; + the sent monster's ATK",
                gm != null && gm.FullyCompiled && Impl(GiftOfTheMartyr) && gmc != null &&
                gmc.Action == EffectActionKind.TargetGainsCostTextAtkUntilEndOfTurn &&
                gmc.Zone == EffectZoneFilter.ControllerAnyMonsters && gmc.RequiresTargetChoice && gmc.IsPsctTarget &&
                gmc.RequiresTargetFaceUp && gmc.RequiresSendOtherYouControl &&
                EffectVocabulary.CostOf(gmc) == EffectCostKind.SendToGy,
                Describe(gm));

            var da = Prog(db, DoubleAttack);
            var dac = da?.ClauseList.Count == 1 ? da.ClauseList[0] : null;
            Check("Double Attack: discard a Monster; target a lower-Level face-up monster you control; 1 extra attack",
                da != null && da.FullyCompiled && Impl(DoubleAttack) && dac != null &&
                dac.Action == EffectActionKind.TargetGainsExtraAttackThisTurn && dac.Amount == 1 &&
                dac.Zone == EffectZoneFilter.ControllerAnyMonsters && dac.RequiresTargetChoice && dac.IsPsctTarget &&
                dac.RequiresTargetFaceUp && dac.RequiresDiscardCost && dac.DiscardCostAttribute == "" &&
                dac.ActivationCondition == TlmCostLinkedSpellsRuntime.CanEnterBattlePhase,
                Describe(da));

            var hs = Prog(db, HeroSignal);
            var hsc = hs?.ClauseList.Count == 1 ? hs.ClauseList[0] : null;
            Check("Hero Signal: battle-destroyed window; Special Summon 1 Level 4 or lower Elemental HERO (no target)",
                hs != null && hs.FullyCompiled && Impl(HeroSignal) && hsc != null &&
                hsc.Action == EffectActionKind.SpecialSummonLowLevelSeriesFromHandOrDeck && hsc.SummonLevelMax == 4 &&
                hsc.TargetSeriesName == "Elemental HERO" && !hsc.RequiresTargetChoice &&
                hsc.ActivationCondition == TlmCostLinkedSpellsRuntime.MonsterYouControlledDestroyedByBattle &&
                hsc.CheckedAt == ConditionCheckedAt.Activation &&
                BattleWindowsRuntime.AnswersWindow(hs, ResponseTiming.EndOfDamageStep),
                Describe(hs));
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(Chk Check)
        {
            var n = 0;
            CompiledCardProgram Probe(string type, string race, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 990211000 + ++n, name = "TlmCostLinkedSpells near miss " + n, type = type, race = race, desc = desc
                });

            bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) => p != null && p.ClauseList.Exists(c => c != null && f(c));

            var bc = Probe("Spell Card", "Normal",
                "Pay 500 Life Points. Special Summon 1 \"Batteryman\" monster from your Graveyard in Defense Position.");
            Check("near miss: a longer Battery Charger sentence is not this atom",
                !Has(bc, c => c.TargetSeriesName == "Batteryman" && c.Action == EffectActionKind.SpecialSummonFromGy) ||
                !bc.FullyCompiled,
                Describe(bc));

            var me = Probe("Trap Card", "Normal",
                "Inflict 1000 damage to your opponent for each face-up \"Mine Golem\" on your side of the field.");
            Check("near miss: the burn sentence alone (no destruction) is not compiled",
                !Has(me, c => c.CountSource == TlmCostLinkedSpellsRuntime.FaceUpNamedYouControl ||
                              c.Action == EffectActionKind.DestroyAllFaceUpNamedYouControl),
                Describe(me));

            var cs = Probe("Spell Card", "Normal",
                "Banish all other cards you control and in your hand; draw until you have 2 cards in your hand.");
            Check("near miss: Card of Sanctity without \"(min. 1 from each)\" is not compiled",
                !Has(cs, c => c.CostBanishAllOtherHandAndField),
                Describe(cs));

            var gm = Probe("Spell Card", "Normal",
                "Send 1 monster on your side of the field to the Graveyard. Select 1 monster on your side of the field. " +
                "The selected monster gains ATK equal to the DEF of the sent monster until the end of this turn.");
            Check("near miss: a DEF-based Gift of the Martyr is not compiled",
                !Has(gm, c => c.Action == EffectActionKind.TargetGainsCostTextAtkUntilEndOfTurn),
                Describe(gm));

            var da = Probe("Spell Card", "Normal",
                "Discard 1 Monster Card from your hand to the Graveyard. Select 1 monster on your side of the field with " +
                "a higher Level than the discarded monster. The selected monster can attack twice this turn.");
            Check("near miss: a higher-Level Double Attack is not compiled",
                !Has(da, c => c.Action == EffectActionKind.TargetGainsExtraAttackThisTurn),
                Describe(da));

            var hs = Probe("Trap Card", "Normal",
                "When a monster you control is destroyed by a card effect and sent to the Graveyard: Special Summon 1 " +
                "Level 4 or lower \"Elemental HERO\" monster from your hand or Deck.");
            Check("near miss: a card-effect Hero Signal is not compiled",
                !Has(hs, c => c.Action == EffectActionKind.SpecialSummonLowLevelSeriesFromHandOrDeck),
                Describe(hs));
        }

        // ───────────────────────────── Battery Charger ─────────────────────────────

        static void BatteryChargerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Only the Player's own "Batteryman" monster is offered (not the opponent's, not a non-Batteryman); the LP
                // are paid at activation and it comes back in the chosen Defense Position.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var aa = AddToGy(e, p, BatterymanAA);
                var celtic = AddToGy(e, p, Celtic);
                var oppAa = AddToGy(e, e.Opponent, BatterymanAA);
                var bc = PutInHand(e, p, BatteryCharger);
                var act = e.TryActivateSpellTrap(p, bc, fromHand: true);
                var legal = Legal(e);
                var onlyAa = legal.Count == 1 && legal[0] == aa;
                var paid = p.LifePoints == 7500;
                var state = St(e);
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                Drive(e, c => c == aa);
                Settle(e);
                Check("Battery Charger: pay 500, only your Batteryman is offered, Special Summoned in the chosen position",
                    act && onlyAa && paid && p.TryFindMonster(aa, out _) && aa.FaceUp &&
                    aa.Position == BattlePosition.Defense && p.Graveyard.Contains(celtic) &&
                    e.Opponent.Graveyard.Contains(oppAa) && p.Graveyard.Contains(bc) && p.LifePoints == 7500,
                    $"act={act} legal=[{string.Join(",", legal.Select(c => Where(e, c) + ":" + c.Name))}] paid={paid} " +
                    $"aa={Where(e, aa)}/{aa.Position} bc={Where(e, bc)} lp={p.LifePoints} {state}");
            }

            {
                // No "Batteryman" monster in your GY (only one in the opponent's): it cannot be activated.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                AddToGy(e, p, Celtic);
                AddToGy(e, e.Opponent, BatterymanAA);
                var bc = PutInHand(e, p, BatteryCharger);
                Check("Battery Charger: refused without a Batteryman in your own GY",
                    !e.CanActivateSpellTrap(p, bc, fromHand: true) && !e.TryActivateSpellTrap(p, bc, fromHand: true) &&
                    p.LifePoints == 8000 && p.Hand.Contains(bc),
                    $"lp={p.LifePoints} bc={Where(e, bc)} {St(e)}");
            }

            {
                // Cancel at the target pick: the 500 LP come back and the card returns to the hand.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                AddToGy(e, p, BatterymanAA);
                AddToGy(e, p, BatterymanAA);
                var bc = PutInHand(e, p, BatteryCharger);
                var act = e.TryActivateSpellTrap(p, bc, fromHand: true);
                var asked = e.IsAwaitingEffectTarget && Legal(e).Count == 2;
                var cancelled = asked && e.CancelEffectTargeting();
                Settle(e);
                Check("Battery Charger: Cancel at the target refunds the LP cost",
                    act && asked && cancelled && p.LifePoints == 8000 && p.Hand.Contains(bc) && !e.Chain.HasLinks &&
                    p.MonsterCount == 0,
                    $"act={act} asked={asked} cancelled={cancelled} lp={p.LifePoints} bc={Where(e, bc)}");
            }
        }

        // ───────────────────────────── Minefield Eruption ─────────────────────────────

        static void MinefieldEruptionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Two face-up Mine Golems (a face-down one and the opponent's face-up one do not count): 2000 damage, then
                // both face-up ones are destroyed.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var g1 = PlaceMonster(e, p, MineGolem, 0, BattlePosition.Attack, true);
                var g2 = PlaceMonster(e, p, MineGolem, 1, BattlePosition.Defense, true);
                var g3 = PlaceMonster(e, p, MineGolem, 2, BattlePosition.Defense, false);
                var og = PlaceMonster(e, o, MineGolem, 0, BattlePosition.Attack, true);
                var trap = PlaceSetTrap(e, p, MinefieldEruption, 0);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                Settle(e);
                Check("Minefield Eruption: 1000 per face-up Mine Golem you control, then those are destroyed",
                    act && o.LifePoints == 6000 && p.LifePoints == 8000 && p.Graveyard.Contains(g1) &&
                    p.Graveyard.Contains(g2) && p.TryFindMonster(g3, out _) && o.TryFindMonster(og, out _) &&
                    p.Graveyard.Contains(trap),
                    $"act={act} oppLp={o.LifePoints} lp={p.LifePoints} g1={Where(e, g1)} g2={Where(e, g2)} " +
                    $"g3={Where(e, g3)} og={Where(e, og)} trap={Where(e, trap)}");
            }

            {
                // Only a face-down Mine Golem (and the opponent's face-up one): it cannot be activated.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, MineGolem, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, MineGolem, 0, BattlePosition.Attack, true);
                var trap = PlaceSetTrap(e, p, MinefieldEruption, 0);
                e.NotifyPublic();
                Check("Minefield Eruption: refused without a face-up Mine Golem you control",
                    !e.CanActivateSpellTrap(p, trap, fromHand: false) && !trap.FaceUp &&
                    e.Opponent.LifePoints == 8000,
                    $"trap={Where(e, trap)} faceUp={trap.FaceUp}");
            }
        }

        // ───────────────────────────── Card of Sanctity ─────────────────────────────

        static void CardOfSanctityDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Every other card in the hand and on the field is banished at activation; then the Player draws 2.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var pot = PutInHand(e, p, PotOfGreed);
                var kuri = PutInHand(e, p, Kuriboh);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var set = PlaceSetTrap(e, p, MinefieldEruption, 1);
                var cs = PutInHand(e, p, CardOfSanctity);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, cs, fromHand: true);
                var banishedAtActivation = p.Banished.Contains(pot) && p.Banished.Contains(kuri) &&
                                           p.Banished.Contains(celtic) && p.Banished.Contains(set);
                Settle(e);
                Check("Card of Sanctity: the hand and field are banished as the cost, then the Player draws 2",
                    act && banishedAtActivation && p.Hand.Count == 2 && p.Deck.Count == deckBefore - 2 &&
                    p.Graveyard.Contains(cs) && p.MonsterCount == 0,
                    $"act={act} banished={banishedAtActivation} hand={p.Hand.Count} deck={deckBefore}->{p.Deck.Count} " +
                    $"cs={Where(e, cs)} pot={Where(e, pot)} celtic={Where(e, celtic)} set={Where(e, set)}");
            }

            {
                // "(min. 1 from each)": a hand card but nothing else on the field, or a field card but no other hand
                // card — it cannot be activated.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PutInHand(e, p, PotOfGreed);
                var cs = PutInHand(e, p, CardOfSanctity);
                var handOnly = e.CanActivateSpellTrap(p, cs, fromHand: true);
                p.Hand.RemoveAll(h => h.CardId == PotOfGreed);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var fieldOnly = e.CanActivateSpellTrap(p, cs, fromHand: true);
                Check("Card of Sanctity: needs at least 1 other card in the hand and 1 on the field",
                    !handOnly && !fieldOnly && p.Hand.Contains(cs),
                    $"handOnly={handOnly} fieldOnly={fieldOnly}");
            }
        }

        // ───────────────────────────── Gift of the Martyr ─────────────────────────────

        static void GiftOfTheMartyrDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The Player sends Giant Soldier (printed 1300); Celtic Guardian gains 1300 ATK until the end of the turn.
                // Batteryman AA (printed 0 ATK) is never offered as the cost.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var giant = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var aa = PlaceMonster(e, p, BatterymanAA, 2, BattlePosition.Attack, true);
                var gift = PutInHand(e, p, GiftOfTheMartyr);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, gift, fromHand: true);
                var costAsked = e.PendingActivation?.TargetKind == EffectTargetKind.SendMonsterYouControlToGy;
                var costOffer = Legal(e);
                var noZeroAtk = costAsked && costOffer.Contains(giant) && costOffer.Contains(celtic) && !costOffer.Contains(aa);
                Drive(e, c => c == giant || c == celtic);
                Settle(e);
                var boosted = celtic.CurrentAtk == 1400 + 1300;
                Check("Gift of the Martyr: the sent monster's printed ATK goes to the targeted face-up monster",
                    act && noZeroAtk && p.Graveyard.Contains(giant) && boosted && aa.CurrentAtk == 1000 && // its own "1000 ATK for each" aura (TlmDerivedStats), untouched by the Gift
                    p.Graveyard.Contains(gift),
                    $"act={act} costAsked={costAsked} offer=[{string.Join(",", costOffer.Select(c => c.Name))}] " +
                    $"giant={Where(e, giant)} celtic={celtic.CurrentAtk} aa={aa.CurrentAtk} gift={Where(e, gift)}");
                EndTurn(e);
                Check("Gift of the Martyr: the gain ends with the turn",
                    celtic.CurrentAtk == 1400, $"turn={e.TurnNumber} celtic={celtic.CurrentAtk}");
            }

            {
                // A lone monster cannot be both the cost and the target: it cannot be activated.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var gift = PutInHand(e, p, GiftOfTheMartyr);
                e.NotifyPublic();
                Check("Gift of the Martyr: refused with a single monster",
                    !e.CanActivateSpellTrap(p, gift, fromHand: true) && p.Hand.Contains(gift) && p.MonsterCount == 1,
                    St(e));
            }

            {
                // The cost is paid before the target pick: Cancel there picks the target for you (highest ATK).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var giant = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var kuri = PlaceMonster(e, p, Kuriboh, 2, BattlePosition.Attack, true);
                var gift = PutInHand(e, p, GiftOfTheMartyr);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, gift, fromHand: true);
                var sent = act && e.TrySelectEffectTarget(giant);
                var targetAsked = e.IsAwaitingEffectTarget && Legal(e).Contains(celtic) && Legal(e).Contains(kuri);
                var cancelled = targetAsked && e.CancelEffectTargeting();
                Settle(e);
                Check("Gift of the Martyr: Cancel after the cost is paid still resolves (target picked for you)",
                    act && sent && targetAsked && cancelled && p.Graveyard.Contains(giant) &&
                    celtic.CurrentAtk == 2700 && kuri.CurrentAtk == 300 && p.Graveyard.Contains(gift),
                    $"act={act} sent={sent} asked={targetAsked} cancelled={cancelled} giant={Where(e, giant)} " +
                    $"celtic={celtic.CurrentAtk} kuri={kuri.CurrentAtk} gift={Where(e, gift)} {St(e)}");
            }
        }

        // ───────────────────────────── Double Attack ─────────────────────────────

        static void DoubleAttackDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Discard Summoned Skull (Level 6; Kuriboh, Level 1, is not offered); Celtic Guardian (Level 4) attacks
                // directly twice.
                var e = PlayerMainTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                celtic.ClearAttackFlags();
                var skull = PutInHand(e, p, SummonedSkull);
                var kuri = PutInHand(e, p, Kuriboh);
                var da = PutInHand(e, p, DoubleAttack);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, da, fromHand: true);
                var offer = Legal(e);
                var discardOffer = offer.Contains(skull) && !offer.Contains(kuri);
                Drive(e, c => c == skull || c == celtic);
                Settle(e);
                var granted = e.ExtraAttacksAllowed(celtic) == 1;
                var bp = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                e.TryAttack(p, celtic, null);
                DrainCombat(e);
                e.TryAttack(p, celtic, null);
                DrainCombat(e);
                Check("Double Attack: discard a higher-Level monster; the lower-Level target attacks twice",
                    reached && act && discardOffer && p.Graveyard.Contains(skull) && p.Hand.Contains(kuri) && granted &&
                    bp && o.LifePoints == 8000 - 2 * 1400 && p.Graveyard.Contains(da),
                    $"reached={reached} act={act} offer=[{string.Join(",", offer.Select(c => c.Name))}] " +
                    $"skull={Where(e, skull)} granted={granted} bp={bp} oppLp={o.LifePoints} da={Where(e, da)}");
            }

            {
                // No monster in hand with a Level above the face-up monster's: refused. Turn 1 (no Battle Phase): refused.
                var e = PlayerMainTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PutInHand(e, p, GiantSoldier);
                var da = PutInHand(e, p, DoubleAttack);
                e.NotifyPublic();
                var lowRefused = !e.CanActivateSpellTrap(p, da, fromHand: true);

                var e1 = Setup(db, pDeck, aDeck);
                var p1 = e1.Player;
                PlaceMonster(e1, p1, Celtic, 0, BattlePosition.Attack, true);
                PutInHand(e1, p1, SummonedSkull);
                var da1 = PutInHand(e1, p1, DoubleAttack);
                e1.NotifyPublic();
                var turn1Refused = !e1.CanActivateSpellTrap(p1, da1, fromHand: true);
                Check("Double Attack: refused without a higher-Level discard, and on turn 1 (no Battle Phase)",
                    reached && lowRefused && turn1Refused && p.Hand.Contains(da),
                    $"reached={reached} low={lowRefused} turn1={turn1Refused} turn={e1.TurnNumber}");
            }
        }

        // ───────────────────────────── Hero Signal ─────────────────────────────

        static void HeroSignalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Summoned Skull destroys Celtic Guardian: at the end of the Damage Step Hero Signal Special
                // Summons Sparkman from the Deck in the chosen position (Avian is the other Level 4 or lower offer).
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.Add(Avian);
                p.Deck.Add(Sparkman);
                p.Deck.Add(PotOfGreed);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var signal = PlaceSetTrap(e, p, HeroSignal, 0);
                var skull = Attacker(e, o, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(o, skull, celtic);
                for (var i = 0; i < 4 && e.IsAwaitingResponse &&
                                !(e.PendingResponse?.LegalCards?.Contains(signal) ?? false); i++)
                    e.PassResponse();
                var offered = e.PendingResponse?.Timing == ResponseTiming.EndOfDamageStep &&
                              e.PendingResponse.LegalCards.Contains(signal);
                var act = offered && e.TryActivateSpellTrap(p, signal, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++)
                    e.PassResponse();
                var offer = Legal(e);
                var choices = offer.Count == 2 && offer.Any(c => c.CardId == Avian) && offer.Any(c => c.CardId == Sparkman);
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var spark = offer.FirstOrDefault(c => c.CardId == Sparkman);
                var picked = spark != null && e.TrySelectEffectTarget(spark);
                Settle(e);
                var summoned = FindOnField(p, Sparkman);
                Check("Hero Signal: a battle-destroyed monster you controlled — Special Summon an Elemental HERO from the Deck",
                    reached && offered && act && choices && picked && summoned != null && summoned.FaceUp &&
                    summoned.Position == BattlePosition.Defense && p.Deck.Count == 2 && !p.Deck.Contains(Sparkman) &&
                    p.Graveyard.Contains(celtic) && p.Graveyard.Contains(signal),
                    $"reached={reached} offered={offered} act={act} offer=[{string.Join(",", offer.Select(c => c.Name))}] " +
                    $"picked={picked} summoned={summoned?.Name}/{summoned?.Position} deck={p.Deck.Count} " +
                    $"signal={Where(e, signal)} {St(e)}");
            }

            {
                // A monster the Player took from the opponent is destroyed by battle and goes to the opponent's GY: its
                // previous controller is the Player, so Hero Signal Special Summons Clayman from the hand.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                var borrowed = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                borrowed.ControlOwner = o;
                var clay = PutInHand(e, p, Clayman);
                var signal = PlaceSetTrap(e, p, HeroSignal, 0);
                var skull = Attacker(e, o, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(o, skull, borrowed);
                for (var i = 0; i < 4 && e.IsAwaitingResponse &&
                                !(e.PendingResponse?.LegalCards?.Contains(signal) ?? false); i++)
                    e.PassResponse();
                var inOppGy = o.Graveyard.Contains(borrowed);
                var offered = e.PendingResponse?.LegalCards?.Contains(signal) ?? false;
                var act = offered && e.TryActivateSpellTrap(p, signal, fromHand: false);
                Drive(e, c => c == clay);
                Settle(e);
                Check("Hero Signal: a monster you controlled that goes to the opponent's GY counts (previous controller)",
                    reached && inOppGy && offered && act && p.TryFindMonster(clay, out _) && clay.FaceUp,
                    $"reached={reached} inOppGy={inOppGy} offered={offered} act={act} clay={Where(e, clay)} {St(e)}");
            }

            {
                // The Player's own attack destroys the opponent's monster: Hero Signal is not offered (the destroyed
                // monster was the opponent's). The AI's Hero Signal answers instead and summons from its Deck.
                var e = PlayerMainTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.Add(Clayman);
                o.Deck.Clear();
                o.Deck.Add(Clayman);
                o.Deck.Add(PotOfGreed);
                var mine = PlaceSetTrap(e, p, HeroSignal, 0);
                var theirs = PlaceSetTrap(e, o, HeroSignal, 0);
                var skull = Attacker(e, p, SummonedSkull, 0);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                e.TryAttack(p, skull, celtic);
                var playerOffered = false;
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    if (e.PendingResponse?.Responder == p && (e.PendingResponse.LegalCards?.Contains(mine) ?? false))
                        playerOffered = true;
                    e.PassResponse();
                }

                Settle(e);
                var aiSummoned = FindOnField(o, Clayman);
                Check("Hero Signal: not for the opponent's battle-destroyed monster; the AI's copy answers its own loss",
                    reached && bp && o.Graveyard.Contains(celtic) && !playerOffered && !mine.FaceUp &&
                    p.TryFindSpellTrap(mine, out _) && aiSummoned != null && o.Graveyard.Contains(theirs) &&
                    o.Deck.Count == 1,
                    $"reached={reached} bp={bp} celtic={Where(e, celtic)} playerOffered={playerOffered} " +
                    $"mine={Where(e, mine)} ai={aiSummoned?.Name} theirs={Where(e, theirs)} oppDeck={o.Deck.Count} {St(e)}");
            }

            {
                // No Level 4 or lower Elemental HERO in the hand or Deck: not offered.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.Add(PotOfGreed);
                PutInHand(e, p, Celtic);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var signal = PlaceSetTrap(e, p, HeroSignal, 0);
                var skull = Attacker(e, o, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(o, skull, celtic);
                var offered = false;
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    offered |= e.PendingResponse?.LegalCards?.Contains(signal) ?? false;
                    e.PassResponse();
                }

                Settle(e);
                Check("Hero Signal: not offered without a Level 4 or lower Elemental HERO to Special Summon",
                    reached && p.Graveyard.Contains(celtic) && !offered && !signal.FaceUp,
                    $"reached={reached} celtic={Where(e, celtic)} offered={offered}");
            }

            {
                // Review finding (EVENT_BATTLE_DESTROYED): the opponent's Celtic Guardian and the Player's destroy each other.
                // The AI's Hero Signal answers its own loss in the end-of-Damage-Step window (Chain Link 1); the battle
                // destruction is still the last thing to happen, so the Player's Hero Signal chains to it (Chain Link 2)
                // and summons Sparkman from the Deck. Both resolve.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.Add(Sparkman);
                o.Deck.Clear();
                o.Deck.Add(Clayman);
                o.Deck.Add(PotOfGreed);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var signal = PlaceSetTrap(e, p, HeroSignal, 0);
                var theirs = PlaceSetTrap(e, o, HeroSignal, 0);
                var attacker = Attacker(e, o, Celtic, 0);
                e.NotifyPublic();
                e.TryAttack(o, attacker, celtic);
                var (chained, act, links, trace) = ChainHeroSignal(e, p, signal, c => c.CardId == Sparkman);
                Settle(e);
                var spark = FindOnField(p, Sparkman);
                var clay = FindOnField(o, Clayman);
                Check("Hero Signal: chained (Chain Link 2) to a Chain built right after the same battle destruction",
                    reached && p.Graveyard.Contains(celtic) && o.Graveyard.Contains(attacker) && chained && act &&
                    links == 1 && spark != null && spark.FaceUp && clay != null && p.Graveyard.Contains(signal) &&
                    o.Graveyard.Contains(theirs),
                    $"reached={reached} celtic={Where(e, celtic)} attacker={Where(e, attacker)} chained={chained} act={act} " +
                    $"links={links} spark={spark?.Name} clay={clay?.Name} signal={Where(e, signal)} theirs={Where(e, theirs)} " +
                    $"trace={trace}");
            }

            {
                // The reviewer's Sangan case: the engine resolves a "sent to the GY" trigger of the Damage Step where it
                // happens (TriggerChainResponsesRuntime seats trigger links only in open play), so no Chain is built for
                // it. Hero Signal still answers that battle destruction in the window that follows.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.Add(Sparkman);
                p.Deck.Add(Kuriboh);
                var sangan = PlaceMonster(e, p, Sangan, 0, BattlePosition.Attack, true);
                var signal = PlaceSetTrap(e, p, HeroSignal, 0);
                var skull = Attacker(e, o, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(o, skull, sangan);
                var offered = false;
                var act = false;
                var timings = new List<string>();
                for (var i = 0; i < 12; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var pick = Legal(e).FirstOrDefault(c => c.CardId == Kuriboh || c.CardId == Sparkman);
                        if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                        continue;
                    }

                    if (!e.IsAwaitingResponse) break;
                    timings.Add($"{e.PendingResponse?.Timing}/c{e.Chain.Count}");
                    if (!offered && e.PendingResponse?.Responder == p &&
                        (e.PendingResponse.LegalCards?.Contains(signal) ?? false))
                    {
                        offered = true;
                        act = e.TryActivateSpellTrap(p, signal, fromHand: false);
                        continue;
                    }

                    e.PassResponse();
                }

                Settle(e);
                var spark = FindOnField(p, Sparkman);
                Check("Hero Signal: Sangan's Damage Step trigger resolves inline; Hero Signal still answers its destruction",
                    reached && p.Graveyard.Contains(sangan) && p.Hand.Any(c => c.CardId == Kuriboh) && offered && act &&
                    spark != null && p.Graveyard.Contains(signal),
                    $"reached={reached} sangan={Where(e, sangan)} hand=[{string.Join(",", p.Hand.Select(c => c.Name))}] " +
                    $"offered={offered} act={act} spark={spark?.Name} signal={Where(e, signal)} " +
                    $"windows=[{string.Join(" ", timings)}]");
            }

            {
                // The AI side of the same rule: on the Player's turn both Celtic Guardians destroy each other. The
                // Player's Hero Signal answers in the end-of-Damage-Step window (Chain Link 1) and the AI chains its own
                // copy (Chain Link 2, AiAutoRespond → AiWantsHeroSignal), summoning Clayman from its Deck; then the
                // Player's link summons Sparkman.
                var e = PlayerMainTurn3(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.Add(Sparkman);
                o.Deck.Clear();
                o.Deck.Add(Clayman);
                o.Deck.Add(PotOfGreed);
                var mine = PlaceSetTrap(e, p, HeroSignal, 0);
                var theirs = PlaceSetTrap(e, o, HeroSignal, 0);
                var attacker = Attacker(e, p, Celtic, 0);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                e.TryAttack(p, attacker, celtic);
                var act = false;
                var window = "";
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    if (e.PendingResponse?.Responder == p && (e.PendingResponse.LegalCards?.Contains(mine) ?? false))
                    {
                        window = $"{e.PendingResponse.Timing}/c{e.Chain.Count}";
                        act = e.TryActivateSpellTrap(p, mine, fromHand: false);
                        break;
                    }

                    e.PassResponse();
                }

                Drive(e, c => c.CardId == Sparkman);
                Settle(e);
                var spark = FindOnField(p, Sparkman);
                var clay = FindOnField(o, Clayman);
                Check("Hero Signal: the AI chains its copy (Chain Link 2) to the Player's, after the same battle destruction",
                    reached && bp && act && window == $"{ResponseTiming.EndOfDamageStep}/c0" &&
                    p.Graveyard.Contains(attacker) && o.Graveyard.Contains(celtic) && spark != null && clay != null &&
                    o.Graveyard.Contains(theirs) && p.Graveyard.Contains(mine) && o.Deck.Count == 1,
                    $"reached={reached} bp={bp} act={act} window={window} attacker={Where(e, attacker)} " +
                    $"celtic={Where(e, celtic)} spark={spark?.Name} clay={clay?.Name} theirs={Where(e, theirs)} " +
                    $"mine={Where(e, mine)} oppDeck={o.Deck.Count} {St(e)}");
            }
        }
    }
}
