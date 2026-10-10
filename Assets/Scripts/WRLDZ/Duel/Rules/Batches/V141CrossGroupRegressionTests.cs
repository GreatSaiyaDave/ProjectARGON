using System;
using System.Linq;
using WRLDZ.Data;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// v141 batch, cross-group audit: interactions between two integrated groups that neither group's own
    /// tests covered. Each check failed before its fix (mini-duels through the public engine entry points).
    /// <list type="bullet">
    /// <item>ContinuousNegation x TributeSummons: Soul Exchange's oath is a cost, so it binds even when Imperial
    /// Order negates the effect (TextEffectRuntime.ApplyClause applies the oath before the negation gates).</item>
    /// <item>ContinuousNegation x PerCountBurns: Imperial Order chained to Wave-Motion Cannon's ignition link negates
    /// the damage (the effect was activated in the Spell &amp; Trap Zone; PerCountBurnsRuntime asks NegatedBy).</item>
    /// <item>ContinuousNegation x TributeSummons: a negated face-up Magical Labyrinth cannot activate its Tribute
    /// ignition (ContinuousNegationRuntime.NegatesEffectActivationOnField in TextEffectRuntime.CanActivate).</item>
    /// <item>ContinuousNegation x engine: Mirror Force negated by Royal Decree lets the battle go on; its inline
    /// Chain Link is dropped before damage can open a "you take damage" window (DuelEngine
    /// ContinueAfterResponseActivation), so it no longer blocks the next attack or swallows the next Spell.</item>
    /// </list>
    /// </summary>
    public static class V141CrossGroupRegressionTests
    {
        const int SoulExchange = 68005187;
        const int ImperialOrder = 61740673;
        const int RoyalDecree = 51452091;
        const int WaveMotionCannon = 38992735;
        const int MagicalLabyrinth = 64389297;
        const int LabyrinthWall = 67284908;
        const int WallShadow = 63162310;
        const int MirrorForce = 44095762;
        const int NuminousHealer = 2130625;
        const int PotOfGreed = 55144522;
        const int Celtic = 91152256;           // Lv4 1400
        const int GeminiElf = 69140098;        // Lv4 1900
        const int GiantSoldier = 13039848;     // Lv3 1300/2000
        const int SummonedSkull = 70781052;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("V141 cross-group: " + name, ok, detail ?? "");

            SoulExchangeOathUnderImperialOrder(db, pDeck, aDeck, Check);
            WaveMotionCannonLinkNegatedByChainedImperialOrder(db, pDeck, aDeck, Check);
            MagicalLabyrinthIgnitionUnderImperialOrder(db, pDeck, aDeck, Check);
            NegatedMirrorForceLeavesNoStaleLink(db, pDeck, aDeck, Check);
        }

        /// <summary>The player's own Main Phase 1 on turn 3 or later (a Battle Phase is allowed).</summary>
        static bool ReachPlayerMain1(DuelEngine e)
        {
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                if (e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingResponse) { e.PassResponse(); continue; }
                if (e.IsAwaitingEffectTarget) { e.CancelEffectTargeting(); continue; }
                if (e.HasDeclaredAttack) { e.ResolveDeclaredAttack(); continue; }
                break;
            }
        }

        static CardInstance FaceUpTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static void SoulExchangeOathUnderImperialOrder(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var me = e.Player;
            var opp = e.Opponent;
            PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
            var target = PlaceMonster(e, me, GeminiElf, 0, BattlePosition.Attack, true);
            FaceUpTrap(e, me, ImperialOrder, 1);
            FieldSpellEffects.RefreshBoard(e);
            var se = PutInHand(e, opp, SoulExchange);
            var canBpBefore = e.CanConductBattlePhase;
            var activated = e.TryActivateSpellTrap(opp, se, fromHand: true);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(target);
            PassAll(e);
            Check("Soul Exchange under Imperial Order: the effect is negated, but its oath (a cost) still forbids the Battle Phase",
                reached && canBpBefore && activated && !e.CanConductBattlePhase && !e.TryEnterBattlePhase(opp) &&
                opp.MustTributeGrants.Count == 0,
                $"reached={reached} canBpBefore={canBpBefore} activated={activated} canBp={e.CanConductBattlePhase} " +
                $"grants={opp.MustTributeGrants.Count}");
        }

        static void WaveMotionCannonLinkNegatedByChainedImperialOrder(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var me = e.Player;
            var opp = e.Opponent;
            var cannon = PlaceSetTrap(e, opp, WaveMotionCannon, 0);
            cannon.FaceUp = true;
            cannon.TurnCounter = 2;
            var io = PlaceSetTrap(e, me, ImperialOrder, 0);
            FieldSpellEffects.RefreshBoard(e);
            var lp = me.LifePoints;
            var fired = e.TryActivateSpellTrap(opp, cannon, fromHand: false);
            var offered = e.IsAwaitingResponse && e.PendingResponse.Responder == me &&
                          e.PendingResponse.LegalCards != null && e.PendingResponse.LegalCards.Contains(io);
            var chained = offered && e.TryActivateSpellTrap(me, io, fromHand: false);
            PassAll(e);
            Check("Wave-Motion Cannon: Imperial Order chained to its ignition link negates the damage",
                reached && fired && offered && chained && me.LifePoints == lp && io.FaceUp &&
                me.SpellTrapsOnField().Contains(io) && opp.Graveyard.Contains(cannon) && !e.Chain.HasLinks,
                $"reached={reached} fired={fired} offered={offered} chained={chained} lp {lp}->{me.LifePoints} " +
                $"chain={e.Chain.Count}");
        }

        static void MagicalLabyrinthIgnitionUnderImperialOrder(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachPlayerMain1(e);
            ClearBoard(e);
            var me = e.Player;
            var opp = e.Opponent;
            me.Deck.Clear();
            me.Deck.AddRange(new[] { Celtic, WallShadow, Celtic });
            var wall = PlaceMonster(e, me, LabyrinthWall, 1, BattlePosition.Defense, true);
            var lab = PutInHand(e, me, MagicalLabyrinth);
            var started = e.TryActivateSpellTrap(me, lab, fromHand: true);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(wall);
            PassAll(e);
            var equipped = lab.EquippedTo == wall && me.SpellTrapsOnField().Contains(lab);
            var io = FaceUpTrap(e, opp, ImperialOrder, 0);
            FieldSpellEffects.RefreshBoard(e);
            var canUnderIo = e.CanActivateSpellTrap(me, lab, fromHand: false);
            var firedUnderIo = e.TryActivateSpellTrap(me, lab, fromHand: false);
            PassAll(e);
            var untouched = me.TryFindMonster(wall, out _) && me.Deck.Contains(WallShadow) &&
                            !me.MonstersOnField().Any(m => m.CardId == WallShadow);
            e.SendCardToGrave(opp, io);
            FieldSpellEffects.RefreshBoard(e);
            var canAfter = e.CanActivateSpellTrap(me, lab, fromHand: false);
            Check("Magical Labyrinth: negated by Imperial Order, its Tribute ignition cannot be activated (legal again once IO leaves)",
                reached && started && equipped && !canUnderIo && !firedUnderIo && untouched && canAfter,
                $"reached={reached} started={started} equipped={equipped} canUnderIo={canUnderIo} " +
                $"firedUnderIo={firedUnderIo} untouched={untouched} canAfter={canAfter}");
        }

        static void NegatedMirrorForceLeavesNoStaleLink(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = new DuelEngine { HumanVsHuman = true };
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            var reached = ReachPlayerMain1(e);
            ClearBoard(e);
            var me = e.Player;
            var opp = e.Opponent;
            var first = PlaceMonster(e, me, Celtic, 0, BattlePosition.Attack, true);
            var second = PlaceMonster(e, me, GeminiElf, 1, BattlePosition.Attack, true);
            var wall = PlaceMonster(e, opp, GiantSoldier, 0, BattlePosition.Defense, true);
            PlaceSetTrap(e, me, NuminousHealer, 0);          // opens a "you take damage" window for the attacker
            var mf = PlaceSetTrap(e, opp, MirrorForce, 0);
            FaceUpTrap(e, opp, RoyalDecree, 1);
            FieldSpellEffects.RefreshBoard(e);
            e.TryEnterBattlePhase(me);
            if (e.IsAwaitingResponse) e.PassResponse();
            var attacked = e.TryAttack(me, first, wall);
            var mfActivated = false;
            var damageWindow = false;
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingResponse)
                {
                    var pr = e.PendingResponse;
                    if (pr.Timing == ResponseTiming.YouTakeDamage) damageWindow = true;
                    if (!mfActivated && pr.Responder == opp && pr.LegalCards != null && pr.LegalCards.Contains(mf))
                    {
                        mfActivated = e.TryActivateSpellTrap(opp, mf, fromHand: false);
                        continue;
                    }

                    e.PassResponse();
                    continue;
                }

                if (e.HasDeclaredAttack) { e.ResolveDeclaredAttack(); continue; }
                break;
            }

            var noStaleLink = !e.Chain.HasLinks;
            var canAttackAgain = e.CanAttack(me, second);
            var bothStay = me.TryFindMonster(first, out _) && me.TryFindMonster(second, out _);
            e.TryEnterMainPhase2(me);
            var pot = PutInHand(e, me, PotOfGreed);
            var hand = me.Hand.Count;
            var potOk = e.TryActivateSpellTrap(me, pot, fromHand: true);
            PassAll(e);
            var drew = me.Graveyard.Contains(pot) && me.Hand.Count == hand + 1;
            Check("Mirror Force negated by Royal Decree: the battle goes on, no stale Chain Link, the next attacker may attack, Pot of Greed resolves",
                reached && attacked && mfActivated && damageWindow && noStaleLink && canAttackAgain && bothStay && potOk &&
                drew,
                $"reached={reached} attacked={attacked} mf={mfActivated} damageWindow={damageWindow} " +
                $"noStaleLink={noStaleLink} canAttackAgain={canAttackAgain} bothStay={bothStay} potOk={potOk} drew={drew}");
        }
    }
}
