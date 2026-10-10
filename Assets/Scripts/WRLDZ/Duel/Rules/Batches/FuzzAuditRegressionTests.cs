using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// v141 fuzz audit (random legal play, every duel checked for duplicated CardInstances): three engine bugs that
    /// also reproduce on the pre-batch engine. Each check failed before its fix.
    /// <list type="bullet">
    /// <item>A face-up Spell/Trap on the field could be activated again as a new Chain Link (Swords of Revealing
    /// Light reset its countdown; Snatch Steal answering Dust Tornado ended up in the GY and its zone). Fix:
    /// TextEffectRuntime.FaceUpCardCannotActivateAgain in CanActivate / CanActivateFreeChainOnChain and at the
    /// OfficialEffectRegistry.CanActivateOfficial entry. Magical Labyrinth's face-up Tribute ignition stays legal
    /// (V141CrossGroupRegressionTests), as does Wave-Motion Cannon's (PerCountBurnsRegressionTests).</item>
    /// <item>Monster Reborn answered by Call of the Haunted on the same GY monster summoned it a second time.
    /// Fix: a GY-target Special Summon re-checks at resolution that the target is still in a GY (ygopro
    /// IsRelateToEffect), in TextEffectRuntime (SpecialSummonFromGy) and SpellTrapEffects (MonsterInEitherGy).</item>
    /// <item>A monster Special Summoned from the opponent's GY went to its controller's GY / hand when it left the
    /// field. Fix: DuelEngine.RecordOwnerOnFieldEntry sets CardInstance.ControlOwner to the GY's player, which
    /// PileOwner and the Tribute-owner bookkeeping already read.</item>
    /// </list>
    /// </summary>
    public static class FuzzAuditRegressionTests
    {
        const int MessengerOfPeace = 44656491;
        const int MaskOfRestrict = 29549364;
        const int RemoveBrainwashing = 94739788;
        const int GravityBind = 85742772;
        const int SpiritBarrier = 53239672;
        const int LevelLimitAreaB = 3136426;
        const int BurningLand = 24294108;
        const int SnatchSteal = 45986603;
        const int PrematureBurial = 70828912;
        const int SwordsOfRevealingLight = 72302403;
        const int DustTornado = 60082869;
        const int MonsterReborn = 83764718;
        const int CallOfTheHaunted = 97077563;
        const int Sangan = 26202165;
        const int Celtic = 91152256;          // Lv4 1400
        const int GiantSoldier = 13039848;    // Normal Lv3 1300/2000
        const int SummonedSkull = 70781052;   // Lv6: 1 Tribute

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("Fuzz audit: " + name, ok, detail ?? "");

            FaceUpCardsCannotBeActivatedAgain(db, pDeck, aDeck, Check);
            SwordsOfRevealingLightCountdownNotReset(db, pDeck, aDeck, Check);
            SnatchStealCannotAnswerDustTornadoAgain(db, pDeck, aDeck, Check);
            MonsterRebornAfterChainedCallOfTheHaunted(db, pDeck, aDeck, Check);
            RebornFromOpponentsGyGoesBackToOwner(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman)
        {
            var e = Fresh(db, pDeck, aDeck);
            e.HumanVsHuman = humanVsHuman;
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static CardInstance PlaceFaceUp(DuelEngine e, DuelistState who, int id, int zone, CardInstance equippedTo = null)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            if (equippedTo != null)
            {
                c.EquippedTo = equippedTo;
                equippedTo.Equips.Add(c);
            }
            return c;
        }

        static IEnumerable<string> Places(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) yield return tag + ".hand";
                if (who.Graveyard.Contains(c)) yield return tag + ".gy";
                if (who.Banished.Contains(c)) yield return tag + ".ban";
                if (who.TryFindMonster(c, out _)) yield return tag + ".mz";
                if (who.TryFindSpellTrap(c, out _)) yield return tag + ".stz";
            }
        }

        static string Where(DuelEngine e, CardInstance c) => c == null ? "-" : string.Join("+", Places(e, c));

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingResponse) { e.PassResponse(); continue; }
                if (e.IsAwaitingEffectTarget) { e.CancelEffectTargeting(); continue; }
                break;
            }
        }

        /// <summary>
        /// Drive an activation: every target prompt picks <paramref name="target"/>; the first chain window that
        /// offers <paramref name="answer"/> to <paramref name="answerer"/> is answered with it; every other window passes.
        /// </summary>
        static bool Drive(DuelEngine e, CardInstance target, DuelistState answerer, CardInstance answer)
        {
            var answered = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    if (!e.TrySelectEffectTarget(target)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    var pr = e.PendingResponse;
                    if (!answered && answer != null && pr.Timing == ResponseTiming.ChainResponse &&
                        pr.Responder == answerer && pr.LegalCards.Contains(answer))
                    {
                        answered = e.TryActivateSpellTrap(answerer, answer, fromHand: false);
                        if (answered) continue;
                    }
                    e.PassResponse();
                    continue;
                }

                break;
            }
            return answered;
        }

        // ─────────────── 1. a face-up Spell/Trap cannot be activated again ───────────────

        static void FaceUpCardsCannotBeActivatedAgain(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var id in new[]
                     {
                         MessengerOfPeace, MaskOfRestrict, RemoveBrainwashing, GravityBind, SpiritBarrier,
                         LevelLimitAreaB, BurningLand, SnatchSteal, PrematureBurial, SwordsOfRevealingLight,
                         CallOfTheHaunted
                     })
            {
                var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: false);
                var me = e.Player;
                var opp = e.Opponent;
                var mine = PlaceMonster(e, me, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                me.Graveyard.Add(e.CreateCardInstance(GiantSoldier));
                opp.Graveyard.Add(e.CreateCardInstance(GiantSoldier));
                var equips = id == SnatchSteal || id == PrematureBurial || id == CallOfTheHaunted;
                var card = PlaceFaceUp(e, me, id, 0, equips ? mine : null);
                var lp = me.LifePoints;
                var can = e.CanActivateSpellTrap(me, card, fromHand: false);
                var fired = e.TryActivateSpellTrap(me, card, fromHand: false);
                PassAll(e);
                Check($"{card.Name}: face-up on the field, it cannot be activated again",
                    !can && !fired && !e.Chain.HasLinks && me.LifePoints == lp &&
                    Where(e, card) == "P.stz" && me.MonstersOnField().Count() == 1,
                    $"can={can} fired={fired} links={e.Chain.Count} lp={lp}->{me.LifePoints} at={Where(e, card)} " +
                    $"monsters={me.MonstersOnField().Count()}");
            }
        }

        static void SwordsOfRevealingLightCountdownNotReset(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: false);
            var me = e.Player;
            var opp = e.Opponent;
            var set = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Defense, false);
            var swords = PutInHand(e, me, SwordsOfRevealingLight);
            var played = e.TryActivateSpellTrap(me, swords, fromHand: true);
            PassAll(e);
            var placed = me.TryFindSpellTrap(swords, out _) && swords.FaceUp && set.FaceUp;
            swords.ContinuousTurnsRemaining = 1; // two of its three turns have passed
            var again = e.TryActivateSpellTrap(me, swords, fromHand: false);
            PassAll(e);
            Check("Swords of Revealing Light: re-activating the face-up card is refused and its countdown is kept",
                played && placed && !again && swords.ContinuousTurnsRemaining == 1 && !e.Chain.HasLinks,
                $"played={played} placed={placed} again={again} turns={swords.ContinuousTurnsRemaining} " +
                $"links={e.Chain.Count}");
        }

        static void SnatchStealCannotAnswerDustTornadoAgain(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Fuzz duel: the face-up Snatch Steal was activated again (Chain Link 1), the AI chained Dust Tornado on
            // it, and the re-activation then put the destroyed card back: it sat in the GY and its zone.
            var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: false);
            var me = e.Player;
            var opp = e.Opponent;
            var prey = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
            var ss = PutInHand(e, me, SnatchSteal);
            var stole = e.TryActivateSpellTrap(me, ss, fromHand: true);
            Drive(e, prey, null, null);
            var controlled = me.TryFindMonster(prey, out _) && ss.EquippedTo == prey && ss.FaceUp;
            var dt = PlaceSetTrap(e, opp, DustTornado, 0);
            var reactivated = e.TryActivateSpellTrap(me, ss, fromHand: false);
            PassAll(e);
            Check("Snatch Steal: with a Dust Tornado Set against it, the face-up card cannot be activated again (the fuzz duel left it in the GY and its zone)",
                stole && controlled && !reactivated && Where(e, ss) == "P.stz" && Where(e, prey) == "P.mz" &&
                Where(e, dt) == "O.stz" && !e.Chain.HasLinks,
                $"stole={stole} controlled={controlled} reactivated={reactivated} ss={Where(e, ss)} " +
                $"prey={Where(e, prey)} dt={Where(e, dt)} links={e.Chain.Count}");
        }

        // ─────────────── 2. GY-target Special Summon re-checks the target ───────────────

        static void MonsterRebornAfterChainedCallOfTheHaunted(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: true);
            var me = e.Player;
            var opp = e.Opponent;
            var sangan = e.CreateCardInstance(Sangan);
            opp.Graveyard.Add(sangan);
            var coth = PlaceSetTrap(e, opp, CallOfTheHaunted, 0);
            var mr = PutInHand(e, me, MonsterReborn);
            var played = e.TryActivateSpellTrap(me, mr, fromHand: true);
            var chained = Drive(e, sangan, opp, coth);
            Check("Monster Reborn: Call of the Haunted chained on the same GY monster summons it; Monster Reborn then does nothing",
                played && chained && Where(e, sangan) == "O.mz" && !me.MonstersOnField().Any() &&
                Where(e, mr) == "P.gy" && coth.EquippedTo == sangan && Where(e, coth) == "O.stz",
                $"played={played} chained={chained} sangan={Where(e, sangan)} mine={me.MonstersOnField().Count()} " +
                $"mr={Where(e, mr)} coth={Where(e, coth)} link={coth.EquippedTo?.Name ?? "-"}");
        }

        // ─────────────── 3. a monster from the opponent's GY keeps its owner ───────────────

        static void RebornFromOpponentsGyGoesBackToOwner(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            CardInstance Reborn(DuelEngine e, out bool ok)
            {
                var soldier = e.CreateCardInstance(GiantSoldier);
                e.Opponent.Graveyard.Add(soldier);
                var mr = PutInHand(e, e.Player, MonsterReborn);
                var played = e.TryActivateSpellTrap(e.Player, mr, fromHand: true);
                Drive(e, soldier, null, null);
                ok = played && Where(e, soldier) == "P.mz" && soldier.ControlOwner == e.Opponent;
                return soldier;
            }

            {
                var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: false);
                var soldier = Reborn(e, out var ok);
                e.DestroyMonsterPublic(e.Player, soldier);
                Check("Monster Reborn from the opponent's GY: destroyed, the monster goes to its owner's GY",
                    ok && Where(e, soldier) == "O.gy" && soldier.ControlOwner == null,
                    $"reborn={ok} at={Where(e, soldier)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: false);
                var soldier = Reborn(e, out var ok);
                var skull = PutInHand(e, e.Player, SummonedSkull);
                e.ClearTributes();
                e.ToggleTribute(e.Player, soldier);
                var summoned = e.TryNormalSummon(e.Player, skull, asSet: false);
                PassAll(e);
                Check("Monster Reborn from the opponent's GY: Tributed, the monster goes to its owner's GY",
                    ok && summoned && Where(e, skull) == "P.mz" && Where(e, soldier) == "O.gy",
                    $"reborn={ok} summoned={summoned} skull={Where(e, skull)} soldier={Where(e, soldier)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: false);
                var soldier = Reborn(e, out var ok);
                var bounced = e.ReturnCardToHand(soldier);
                Check("Monster Reborn from the opponent's GY: returned to the hand, it goes to its owner's hand",
                    ok && bounced && Where(e, soldier) == "O.hand",
                    $"reborn={ok} bounced={bounced} at={Where(e, soldier)}");
            }

            {
                // From your own GY nothing changes: no owner record, your GY.
                var e = FreshCleared(db, pDeck, aDeck, humanVsHuman: false);
                var soldier = e.CreateCardInstance(GiantSoldier);
                e.Player.Graveyard.Add(soldier);
                var mr = PutInHand(e, e.Player, MonsterReborn);
                var played = e.TryActivateSpellTrap(e.Player, mr, fromHand: true);
                Drive(e, soldier, null, null);
                var onField = Where(e, soldier) == "P.mz" && soldier.ControlOwner == null;
                e.DestroyMonsterPublic(e.Player, soldier);
                Check("Monster Reborn from your own GY: no owner record, destroyed to your own GY",
                    played && onField && Where(e, soldier) == "P.gy",
                    $"played={played} onField={onField} at={Where(e, soldier)}");
            }
        }
    }
}
