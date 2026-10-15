using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "ChainLinkAnswers": Jar Robber, Spell Shield Type-8, My Body as a Shield, Chaos Command Magician and
    /// Dramatic Rescue — compile shapes, look-alike sentences that stay uncompiled, and mini-duels through the real Chain
    /// windows (TryActivateSpellTrap, TryFlipSummon, TryActivateChainResponse, PassResponse).
    /// </summary>
    public static class ChainLinkAnswersRegressionTests
    {
        const int JarRobber = 33784505;
        const int SpellShield = 38275183;
        const int MyBodyAsAShield = 69279219;
        const int ChaosCommandMagician = 72630549;
        const int DramaticRescue = 80193355;
        const int RiryokuField = 70344351;

        const int PotOfGreed = 55144522;
        const int OfferingsToTheDoomed = 19230407; // Quick-Play: target 1 face-up monster on the field; destroy it
        const int DarkHole = 53129443;
        const int Mst = 5318639;
        const int ManEaterBug = 54652250;          // FLIP: target 1 monster on the field; destroy it
        const int AmazonessFighter = 55821894;     // Warrior Lv4 1500/1300
        const int Celtic = 91152256;               // Warrior Lv4 1400/1200
        const int Muko = 80863132;
        const int GracefulCharity = 79571449;      // draw 3 then discard 2 — a draw with a rider, not a pure draw
        const int MysticalElf = 15025844;          // Spellcaster Lv4 800/2000

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("ChainLinkAnswers: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            JarRobberDuels(db, pDeck, aDeck, Check);
            MukoDuels(db, pDeck, aDeck, Check);
            SpellShieldDuels(db, pDeck, aDeck, Check);
            MyBodyAsAShieldDuels(db, pDeck, aDeck, Check);
            ChaosCommandMagicianDuels(db, pDeck, aDeck, Check);
            DramaticRescueDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} " + string.Join(" | ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} b{c.EffectChoiceIndex} lp{c.PayLpAmount} send{c.CostSendSpellsFromHandCount}"));

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            var jar = Prog(db, JarRobber);
            var j = jar?.ClauseList.Count == 1 ? jar.ClauseList[0] : null;
            Check("Jar Robber: answers \"Pot of Greed\"'s activation, negates its effect, draws 1",
                jar != null && jar.FullyCompiled && Implemented(db, JarRobber) && j != null &&
                j.Timing == EffectTiming.ChainLinkActivated &&
                j.Action == EffectActionKind.NegateEffectOfNamedActivationThenDraw && j.NamedCard == "Pot of Greed" &&
                j.Amount == 1 && j.NegateCardActivationOnly, Describe(jar));

            var shield = Prog(db, SpellShield);
            var b1 = shield?.ClauseList.FirstOrDefault(c => c.EffectChoiceIndex == 1);
            var b2 = shield?.ClauseList.FirstOrDefault(c => c.EffectChoiceIndex == 2);
            Check("Spell Shield Type-8: two bullets — Riryoku's single-monster-target condition, or a Spell-send cost",
                shield != null && shield.FullyCompiled && Implemented(db, SpellShield) && shield.ClauseList.Count == 2 &&
                b1 != null && b2 != null &&
                b1.Timing == EffectTiming.ChainLinkActivated && b1.Action == EffectActionKind.NegateActivation &&
                b1.NegateRequiresSingleMonsterTarget && b1.CostSendSpellsFromHandCount == 0 && b1.DestroyNegatedCard &&
                b2.Timing == EffectTiming.ChainLinkActivated && b2.Action == EffectActionKind.NegateActivation &&
                !b2.NegateRequiresSingleMonsterTarget && b2.CostSendSpellsFromHandCount == 1 && b2.DestroyNegatedCard &&
                !b2.RequiresDiscardCost && b1.NegateRespondsToSpell && b2.NegateRespondsToSpell &&
                b1.NegateCardActivationOnly && b2.NegateCardActivationOnly, Describe(shield));

            var body = Prog(db, MyBodyAsAShield);
            var m = body?.ClauseList.Count == 1 ? body.ClauseList[0] : null;
            Check("My Body as a Shield: pay 1500 LP; negate an opponent's destroying activation and destroy it",
                body != null && body.FullyCompiled && Implemented(db, MyBodyAsAShield) && m != null &&
                m.Timing == EffectTiming.ChainLinkActivated && m.Action == EffectActionKind.NegateActivation &&
                m.PayLpAmount == 1500 && m.NegateRequiresWouldDestroyMonsterOnField && m.NegateOpponentActivationOnly &&
                m.DestroyNegatedCard && !m.NegateCardActivationOnly, Describe(body));

            var ccm = Prog(db, ChaosCommandMagician);
            var c1 = ccm?.ClauseList.Count == 1 ? ccm.ClauseList[0] : null;
            Check("Chaos Command Magician: negates a monster effect that targets only this card",
                ccm != null && ccm.FullyCompiled && Implemented(db, ChaosCommandMagician) && c1 != null &&
                c1.Timing == EffectTiming.ContinuousWhileFaceUp &&
                c1.Action == EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis && c1.AffectsThisCardOnly,
                Describe(ccm));

            var rescue = Prog(db, DramaticRescue);
            var r = rescue?.ClauseList.Count == 1 ? rescue.ClauseList[0] : null;
            Check("Dramatic Rescue: answers a targeting of an \"Amazoness\" monster; return it, Special Summon from hand",
                rescue != null && rescue.FullyCompiled && Implemented(db, DramaticRescue) && r != null &&
                r.Timing == EffectTiming.ChainLinkActivated &&
                r.Action == EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand &&
                r.NamedCard == "Amazoness" && r.NamedCardIsSeries, Describe(rescue));
        }

        // ───────────────────────── near misses ─────────────────────────

        static CompiledCardProgram CompileText(string text, string type, string race, string frame) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990175, name = "Near-miss Card", type = type, frameType = frame, race = race, desc = text,
                atk = 900, def = 900, level = 3
            });

        static bool Has(string text, Func<EffectClause, bool> pred, string type, string race, string frame)
        {
            var prog = CompileText(text, type, race, frame);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && pred(c));
        }

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            var jarText = db.Get(JarRobber)?.desc ?? "";
            Check("Near miss: Jar Robber's text compiles (control)",
                Has(jarText, c => c.Action == EffectActionKind.NegateEffectOfNamedActivationThenDraw,
                    "Spell Card", "Quick-Play", "spell"));
            Check("Near miss: 'draw 2 cards' is not Jar Robber's template",
                !Has(jarText.Replace("draw 1 card", "draw 2 cards"),
                    c => c.Action == EffectActionKind.NegateEffectOfNamedActivationThenDraw,
                    "Spell Card", "Quick-Play", "spell"));
            Check("Near miss: two different names are not Jar Robber's template",
                !Has(jarText.Replace("Negate the effect of the \"Pot of Greed\"", "Negate the effect of the \"Dark Hole\""),
                    c => c.Action == EffectActionKind.NegateEffectOfNamedActivationThenDraw,
                    "Spell Card", "Quick-Play", "spell"));

            var bodyText = db.Get(MyBodyAsAShield)?.desc ?? "";
            Check("Near miss: My Body as a Shield's text compiles (control)",
                Has(bodyText, c => c.NegateRequiresWouldDestroyMonsterOnField, "Spell Card", "Quick-Play", "spell"));
            Check("Near miss: 'would destroy a Spell/Trap Card(s)' is not My Body as a Shield's template",
                !Has(bodyText.Replace("destroy a monster(s) on the field", "destroy a Spell/Trap Card(s) on the field"),
                    c => c.NegateRequiresWouldDestroyMonsterOnField, "Spell Card", "Quick-Play", "spell"));
            Check("Near miss: without its LP cost it is not My Body as a Shield's template",
                !Has(bodyText.Replace("Pay 1500 LP; negate", "Negate"),
                    c => c.NegateRequiresWouldDestroyMonsterOnField, "Spell Card", "Quick-Play", "spell"));

            var ccmText = db.Get(ChaosCommandMagician)?.desc ?? "";
            Check("Near miss: Chaos Command Magician's text compiles (control)",
                Has(ccmText, c => c.Action == EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis,
                    "Effect Monster", "Spellcaster", "effect"));
            Check("Near miss: 'a Spell Card that targets this 1 card' is not Chaos Command Magician's template",
                !Has(ccmText.Replace("Monster Card", "Spell Card"),
                    c => c.Action == EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis,
                    "Effect Monster", "Spellcaster", "effect"));
            Check("Near miss: an extra sentence after Chaos Command Magician's text is not compiled",
                !Has(ccmText + " Then destroy that monster.",
                    c => c.Action == EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis,
                    "Effect Monster", "Spellcaster", "effect"));

            var rescueText = db.Get(DramaticRescue)?.desc ?? "";
            Check("Near miss: Dramatic Rescue's text compiles (control)",
                Has(rescueText, c => c.Action == EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand,
                    "Trap Card", "Normal", "trap"));
            Check("Near miss: without the Special Summon it is not Dramatic Rescue's template",
                !Has(rescueText.Replace(" and Special Summon 1 other monster from your hand", ""),
                    c => c.Action == EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand,
                    "Trap Card", "Normal", "trap"));

            // Spell Shield Type-8 used to be pinned as "not Riryoku Field's template": its bullet 1 shares Riryoku's
            // condition, but only as the first of two bullets; Riryoku Field's whole-sentence atom does not match it.
            var shieldText = db.Get(SpellShield)?.desc ?? "";
            var rirText = db.Get(RiryokuField)?.desc ?? "";
            var rir = Prog(db, RiryokuField);
            Check("Near miss: Riryoku Field keeps its single (non-bullet) answer",
                rir != null && rir.FullyCompiled && rir.ClauseList.Count == 1 &&
                rir.ClauseList[0].NegateRequiresSingleMonsterTarget && rir.ClauseList[0].EffectChoiceIndex == 0,
                Describe(rir));
            var shield = Prog(db, SpellShield);
            Check("Near miss: Spell Shield Type-8 is not Riryoku Field's template (its single-target answer is bullet 1)",
                shield != null && shield.ClauseList.Count == 2 &&
                shield.ClauseList.Where(c => c.NegateRequiresSingleMonsterTarget).All(c => c.EffectChoiceIndex == 1) &&
                !rirText.Equals(shieldText, StringComparison.OrdinalIgnoreCase), Describe(shield));
            var firstBulletOnly = shieldText.Substring(0, Math.Max(0, shieldText.IndexOf("● When a Spell Card is activated:",
                StringComparison.Ordinal))).TrimEnd();
            Check("Near miss: Spell Shield Type-8 with only its first bullet stays uncompiled",
                firstBulletOnly.Length > 0 &&
                !Has(firstBulletOnly, c => c.Timing == EffectTiming.ChainLinkActivated, "Trap Card", "Counter", "trap"),
                firstBulletOnly);
            Check("Near miss: Spell Shield Type-8 with a discard (not a send) cost stays uncompiled",
                !Has(shieldText.Replace("Send 1 Spell Card from your hand to the Graveyard", "Discard 1 card"),
                    c => c.Timing == EffectTiming.ChainLinkActivated, "Trap Card", "Counter", "trap"));
        }

        // ───────────────────────── helpers ─────────────────────────

        static bool Offers(DuelEngine e, DuelistState who, CardInstance card) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            e.PendingResponse.Responder == who && e.PendingResponse.LegalCards.Contains(card);

        static string State(DuelEngine e) =>
            (e.PendingResponse == null
                ? "no window"
                : $"timing={e.PendingResponse.Timing} responder={e.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", e.PendingResponse.LegalCards.Select(c => c?.Name))}]") +
            $" chain={e.Chain.Count} pick={e.IsAwaitingEffectTarget} turn={e.TurnPlayer?.Name} {e.Phase}";

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static void Empty(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e);
            Empty(e);
            return e;
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
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

            Empty(e);
            return e;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────── Jar Robber ─────────────────────────

        static void MukoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Muko));
                Check("Muko: FullyCompiled — a chain answer that replaces the answered draw; Implemented",
                    prog != null && prog.FullyCompiled &&
                    CardEffectStatus.Classify(db.Get(Muko)) == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.ReplaceDrawLinkWithDiscard &&
                    prog.ClauseList[0].Timing == EffectTiming.ChainLinkActivated && prog.ClauseList[0].ChainResponseOnly,
                    prog == null ? "null" : string.Join(" | ", prog.UnparsedFragments ?? Array.Empty<string>()));
            }

            {
                // The opponent's Pot of Greed resolves by discarding 2 instead of drawing 2.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var muko = PlaceSetTrap(e, p, Muko, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, Celtic);
                var deckBefore = o.Deck.Count;
                var oppHandBefore = o.Hand.Count;
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = Offers(e, p, muko);
                var answered = offered && e.TryActivateChainResponse(p, muko);
                PassAll(e);
                Check("Muko: the answered draw is replaced — the cards are discarded, not drawn",
                    act && offered && answered && o.Graveyard.Contains(pot) && p.Graveyard.Contains(muko) &&
                    o.Deck.Count == deckBefore - 2 && o.Hand.Count == oppHandBefore - 1,
                    $"offered={offered} answered={answered} deck {deckBefore}->{o.Deck.Count} " +
                    $"oppHand {oppHandBefore}->{o.Hand.Count} potGY={o.Graveyard.Contains(pot)} " +
                    $"mukoGY={p.Graveyard.Contains(muko)} {State(e)}");
            }

            {
                // A draw with a rider (Graceful Charity) is not a pure draw, so Muko cannot answer it.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var muko = PlaceSetTrap(e, p, Muko, 0);
                var charity = PutInHand(e, o, GracefulCharity);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, charity, fromHand: true);
                Check("Muko: not offered against a draw whose operation is not only a draw",
                    act && !Offers(e, p, muko),
                    $"charity={charity?.Name} {State(e)}");
                PassAll(e);
            }
        }

        static void JarRobberDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jar = PlaceSetTrap(e, p, JarRobber, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, Celtic);
                e.NotifyPublic();
                var oppHand = o.Hand.Count;
                var myHand = p.Hand.Count;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = Offers(e, p, jar);
                Check("Jar Robber: offered when \"Pot of Greed\" is activated", act && offered, State(e));
                var answered = offered && e.TryActivateChainResponse(p, jar);
                PassAll(e);
                Check("Jar Robber: Pot of Greed's effect is negated (no draw for it, it goes to the GY); you draw 1",
                    answered && o.Graveyard.Contains(pot) && o.Hand.Count == oppHand - 1 && p.Hand.Count == myHand + 1 &&
                    p.Graveyard.Contains(jar) && e.Chain.Count == 0 && !e.IsAwaitingResponse,
                    $"answered={answered} oppHand {oppHand}->{o.Hand.Count} myHand {myHand}->{p.Hand.Count} {State(e)}");
            }

            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var jar = PlaceSetTrap(e, p, JarRobber, 0);
                var mst = PlaceSetTrap(e, p, Mst, 1);
                var hole = PutInHand(e, o, DarkHole);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, hole, fromHand: true);
                Check("Jar Robber: not offered against another Spell (Dark Hole)",
                    act && Offers(e, p, mst) && !Offers(e, p, jar), State(e));
                PassAll(e);
            }

            {
                // A ChainLinkActivated-only Quick-Play cannot be activated in the open state (nothing to answer).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var jar = PutInHand(e, p, JarRobber);
                var act = reached && e.TryActivateSpellTrap(p, jar, fromHand: true);
                Check("Jar Robber: not activatable from the hand with no Chain (no soft-lock)",
                    reached && !act && p.Hand.Contains(jar) && !e.IsAwaitingEffectTarget && e.Chain.Count == 0,
                    $"act={act} {State(e)}");
            }
        }

        // ───────────────────────── Spell Shield Type-8 ─────────────────────────

        static void SpellShieldDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Bullet 1 (a Spell targeting exactly 1 monster): a human with a Spell in hand chooses; tapping the card
                // takes bullet 1, so nothing is sent.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var shield = PlaceSetTrap(e, p, SpellShield, 0);
                var spare = PutInHand(e, p, PotOfGreed);
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                var offered = Offers(e, p, shield);
                var asked = offered && e.TryActivateChainResponse(p, shield) && e.IsAwaitingEffectTarget &&
                            e.PendingActivation.LegalTargets.Contains(shield);
                var picked = asked && e.TrySelectEffectTarget(shield);
                PassAll(e);
                Check("Spell Shield Type-8 bullet 1: Offerings is negated and destroyed, no Spell sent",
                    act && offered && asked && picked && OnField(p, celtic) && o.Graveyard.Contains(off) &&
                    p.Hand.Contains(spare) && p.Graveyard.Contains(shield) && e.Chain.Count == 0,
                    $"act={act} offered={offered} asked={asked} picked={picked} {State(e)}");
            }

            {
                // Bullet 2 (any Spell, Dark Hole targets nothing): the lone Spell in hand is sent as the cost.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var shield = PlaceSetTrap(e, p, SpellShield, 0);
                var spare = PutInHand(e, p, PotOfGreed);
                var hole = PutInHand(e, o, DarkHole);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, hole, fromHand: true);
                var offered = Offers(e, p, shield);
                var answered = offered && e.TryActivateChainResponse(p, shield);
                PassAll(e);
                Check("Spell Shield Type-8 bullet 2: a Spell is sent from the hand; Dark Hole is negated and destroyed",
                    act && answered && OnField(p, celtic) && o.Graveyard.Contains(hole) && p.Graveyard.Contains(spare) &&
                    p.Graveyard.Contains(shield) && e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} {State(e)}");
            }

            {
                // Bullet 2 needs a Spell in hand: with only a monster in hand, Dark Hole cannot be answered.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var shield = PlaceSetTrap(e, p, SpellShield, 0);
                var mst = PlaceSetTrap(e, p, Mst, 1);
                PutInHand(e, p, MysticalElf);
                var hole = PutInHand(e, o, DarkHole);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, hole, fromHand: true);
                Check("Spell Shield Type-8: no bullet is legal against Dark Hole without a Spell in hand",
                    act && Offers(e, p, mst) && !Offers(e, p, shield), State(e));
                PassAll(e);
            }

            {
                // The AI answers with the free bullet 1 and keeps its Spell.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                var shield = PlaceSetTrap(e, o, SpellShield, 0);
                var aiSpell = PutInHand(e, o, PotOfGreed);
                var off = PutInHand(e, p, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(p, off, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(elf);
                PassAll(e);
                Check("Spell Shield Type-8 (AI): answers with bullet 1, keeps its Spell; the Spell is destroyed",
                    act && OnField(o, elf) && p.Graveyard.Contains(off) && o.Graveyard.Contains(shield) &&
                    o.Hand.Contains(aiSpell) && e.Chain.Count == 0, $"act={act} {State(e)}");
            }
        }

        // ───────────────────────── My Body as a Shield ─────────────────────────

        static void MyBodyAsAShieldDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var body = PlaceSetTrap(e, p, MyBodyAsAShield, 0);
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                var offered = Offers(e, p, body);
                var answered = offered && e.TryActivateChainResponse(p, body);
                PassAll(e);
                Check("My Body as a Shield: pays 1500 LP; the opponent's destroying Spell is negated and destroyed",
                    act && offered && answered && OnField(p, celtic) && o.Graveyard.Contains(off) &&
                    p.Graveyard.Contains(body) && p.LifePoints == lp - 1500 && e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} lp {lp}->{p.LifePoints} {State(e)}");
            }

            {
                // Not enough LP: not offered.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var body = PlaceSetTrap(e, p, MyBodyAsAShield, 0);
                var mst = PlaceSetTrap(e, p, Mst, 1);
                p.LifePoints = 1000;
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                Check("My Body as a Shield: not offered with less than 1500 LP",
                    act && Offers(e, p, mst) && !Offers(e, p, body), State(e));
                PassAll(e);
            }

            {
                // MST destroys a Spell/Trap, not a monster: not offered.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var body = PlaceSetTrap(e, p, MyBodyAsAShield, 0);
                var jar = PlaceSetTrap(e, p, JarRobber, 1);
                var mst = PutInHand(e, o, Mst);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(jar);
                Check("My Body as a Shield: not offered against an effect that destroys only a Spell/Trap (MST)",
                    act && !Offers(e, p, body), State(e));
                PassAll(e);
            }

            {
                // A monster effect: the opponent's Man-Eater Bug FLIP targets Celtic; its activation is negated and the
                // Bug destroyed.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var body = PlaceSetTrap(e, p, MyBodyAsAShield, 0);
                var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
                e.NotifyPublic();
                var flipped = reached && e.TryFlipSummon(o, bug);
                var offered = Offers(e, p, body) && TriggerChainResponsesRuntime.IsTriggerLink(e.Chain.Last);
                var answered = offered && e.TryActivateChainResponse(p, body);
                PassAll(e);
                Check("My Body as a Shield: negates Man-Eater Bug's FLIP effect and destroys the Bug",
                    flipped && offered && answered && OnField(p, celtic) && o.Graveyard.Contains(bug) &&
                    p.Graveyard.Contains(body) && e.Chain.Count == 0,
                    $"flipped={flipped} offered={offered} answered={answered} {State(e)}");
            }

            {
                // From the hand during your own turn: your MST targets the opponent's Set Offerings to the Doomed, which
                // the AI chains against your monster; you answer it with My Body as a Shield from the hand.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var fighter = PlaceMonster(e, p, AmazonessFighter, 0, BattlePosition.Attack, true);
                var off = PlaceSetTrap(e, o, OfferingsToTheDoomed, 0);
                var body = PutInHand(e, p, MyBodyAsAShield);
                var mst = PutInHand(e, p, Mst);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var log = new System.Collections.Generic.List<string>();
                e.OnLog += l => log.Add(l);
                var act = reached && e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(off);
                var chained = e.Chain.Last != null && ReferenceEquals(e.Chain.Last.Card, off) &&
                              ReferenceEquals(e.Chain.Last.Target, fighter);
                var offered = Offers(e, p, body);
                var answered = offered && e.TryActivateSpellTrap(p, body, fromHand: true);
                PassAll(e);
                Check("My Body as a Shield: from the hand on your turn — the chained Offerings is negated and destroyed",
                    act && chained && offered && answered && OnField(p, fighter) && o.Graveyard.Contains(off) &&
                    p.Graveyard.Contains(body) && !p.Hand.Contains(body) && p.LifePoints == lp - 1500 &&
                    e.Chain.Count == 0,
                    $"act={act} chained={chained} offered={offered} answered={answered} fighter={OnField(p, fighter)} " +
                    $"{State(e)} log: {string.Join(" / ", log.Skip(Math.Max(0, log.Count - 12)))}");
            }
        }

        // ───────────────────────── Chaos Command Magician ─────────────────────────

        static void ChaosCommandMagicianDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // No one can respond: the FLIP effect resolves at once and is negated as it applies.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ccm = PlaceMonster(e, p, ChaosCommandMagician, 0, BattlePosition.Attack, true);
                var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
                e.NotifyPublic();
                var log = new System.Collections.Generic.List<string>();
                e.OnLog += l => log.Add(l);
                var flipped = reached && e.TryFlipSummon(o, bug);
                Check("Chaos Command Magician: Man-Eater Bug's FLIP effect that targets it is negated (no Chain)",
                    flipped && OnField(p, ccm) && OnField(o, bug) && e.Chain.Count == 0 && !e.IsAwaitingResponse,
                    $"flipped={flipped} ccm={OnField(p, ccm)} bug={OnField(o, bug)} {State(e)} log: " +
                    string.Join(" / ", log.Skip(Math.Max(0, log.Count - 12))));
            }

            {
                // A Chain Link: you could respond (a Set MST) and pass; the link is negated as it resolves.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ccm = PlaceMonster(e, p, ChaosCommandMagician, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, Mst, 0);
                var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
                e.NotifyPublic();
                var flipped = reached && e.TryFlipSummon(o, bug);
                var linked = e.Chain.Last != null && TriggerChainResponsesRuntime.IsTriggerLink(e.Chain.Last) &&
                             ReferenceEquals(e.Chain.Last.Target, ccm);
                PassAll(e);
                Check("Chaos Command Magician: the FLIP effect's Chain Link that targets only it is negated",
                    flipped && linked && OnField(p, ccm) && e.Chain.Count == 0 && !e.IsAwaitingResponse,
                    $"flipped={flipped} linked={linked} {State(e)}");
            }

            {
                // A Spell that targets it is not a monster effect: destroyed.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ccm = PlaceMonster(e, p, ChaosCommandMagician, 0, BattlePosition.Attack, true);
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                PassAll(e);
                Check("Chaos Command Magician: a Spell that targets it is not negated (it is destroyed)",
                    act && p.Graveyard.Contains(ccm), $"act={act} {State(e)}");
            }

            {
                // Face-down, its effect does not apply.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ccm = PlaceMonster(e, p, ChaosCommandMagician, 0, BattlePosition.Defense, false);
                var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
                e.NotifyPublic();
                var flipped = reached && e.TryFlipSummon(o, bug);
                Check("Chaos Command Magician: a face-down copy is destroyed by Man-Eater Bug",
                    flipped && p.Graveyard.Contains(ccm), $"flipped={flipped} {State(e)}");
            }

            {
                // Your own Man-Eater Bug: a FLIP effect that targets another monster still resolves.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ccm = PlaceMonster(e, o, ChaosCommandMagician, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var bug = PlaceMonster(e, p, ManEaterBug, 0, BattlePosition.Defense, false);
                e.NotifyPublic();
                var flipped = reached && e.TryFlipSummon(p, bug);
                var picked = flipped && e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(celtic);
                PassAll(e);
                Check("Chaos Command Magician: a FLIP effect that targets another monster is not negated",
                    picked && o.Graveyard.Contains(celtic) && OnField(o, ccm), $"flipped={flipped} picked={picked} {State(e)}");
            }
        }

        // ───────────────────────── Dramatic Rescue ─────────────────────────

        static void DramaticRescueDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var fighter = PlaceMonster(e, p, AmazonessFighter, 0, BattlePosition.Attack, true);
                var rescue = PlaceSetTrap(e, p, DramaticRescue, 0);
                var celtic = PutInHand(e, p, Celtic);
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                var offered = Offers(e, p, rescue);
                var answered = offered && e.TryActivateChainResponse(p, rescue);
                PassAll(e);
                // The Special Summon's position: tap the monster for Defense (Cancel = Attack).
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(celtic);
                var tapped = asked && e.TrySelectEffectTarget(celtic);
                PassAll(e);
                Check("Dramatic Rescue: the targeted Amazoness returns to the hand, another monster is Special Summoned",
                    act && offered && answered && tapped && p.Hand.Contains(fighter) && OnField(p, celtic) &&
                    celtic.FaceUp && celtic.Position == BattlePosition.Defense && o.Graveyard.Contains(off) &&
                    p.Graveyard.Contains(rescue) && e.Chain.Count == 0 && !e.IsAwaitingEffectTarget,
                    $"act={act} offered={offered} answered={answered} asked={asked} {State(e)}");
            }

            {
                // A target that is not an "Amazoness" monster: not offered.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var rescue = PlaceSetTrap(e, p, DramaticRescue, 0);
                var mst = PlaceSetTrap(e, p, Mst, 1);
                PutInHand(e, p, MysticalElf);
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                Check("Dramatic Rescue: not offered when the target is not an \"Amazoness\" monster",
                    act && Offers(e, p, mst) && !Offers(e, p, rescue), State(e));
                PassAll(e);
            }

            {
                // No monster in hand to Special Summon: not offered.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, AmazonessFighter, 0, BattlePosition.Attack, true);
                var rescue = PlaceSetTrap(e, p, DramaticRescue, 0);
                var mst = PlaceSetTrap(e, p, Mst, 1);
                PutInHand(e, p, PotOfGreed);
                var off = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, off, fromHand: true);
                Check("Dramatic Rescue: not offered without a monster in hand to Special Summon",
                    act && Offers(e, p, mst) && !Offers(e, p, rescue), State(e));
                PassAll(e);
            }

            {
                // The AI rescues its own Amazoness from the Player's Offerings (the AI picks the summon and position).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var fighter = PlaceMonster(e, o, AmazonessFighter, 0, BattlePosition.Attack, true);
                var rescue = PlaceSetTrap(e, o, DramaticRescue, 0);
                var elf = PutInHand(e, o, MysticalElf);
                var off = PutInHand(e, p, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(p, off, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(fighter);
                PassAll(e);
                Check("Dramatic Rescue (AI): its Amazoness returns to its hand and Mystical Elf is Special Summoned",
                    act && o.Hand.Contains(fighter) && OnField(o, elf) && elf.FaceUp &&
                    elf.Position == BattlePosition.Defense && p.Graveyard.Contains(off) && o.Graveyard.Contains(rescue) &&
                    e.Chain.Count == 0 && !e.IsAwaitingEffectTarget, $"act={act} {State(e)}");
            }
        }
    }
}
