using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// EenDarkWorldDiscards batch (EEN): Beiige, Broww, Goldd and Sillva ("If this card is discarded to the Graveyard by a
    /// card effect"), Brron ("When this card inflicts battle damage to your opponent: You can discard 1 card.") and Scarr
    /// (battle-destroyed search of a Level 4 or lower "Dark World" monster). Compile shapes, look-alike sentences that
    /// must stay uncompiled, and mini-duels through real entry points: the opponent's Delinquent Duo (by the opponent),
    /// Card Destruction (own effect; two Dark Worlds on one SEGOC Chain), a Lightning Vortex discard cost (no trigger),
    /// Brron's direct attack and Scarr attacking into a stronger monster.
    /// Runtime: <see cref="EenDarkWorldDiscardsRuntime"/>.
    /// </summary>
    public static class EenDarkWorldDiscardsRegressionTests
    {
        const int Beiige = 33731070;
        const int Broww = 79126789;
        const int Brron = 6214884;
        const int Goldd = 78004197;
        const int Scarr = 5498296;
        const int Sillva = 32619583;

        const int Zure = 7459013; // Level 4 "Dark World" Normal Monster
        const int CardDestruction = 72892473;
        const int Duo = 44763025;
        const int LightningVortex = 69162969;
        const int GracefulCharity = 79571449;
        const int Celtic = 91152256; // Level 4 EARTH 1400/1200 Normal Monster
        const int Mst = 5318639;

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(Check);
            BeiigeDuels(db, pDeck, aDeck, Check);
            BrowwDuels(db, pDeck, aDeck, Check);
            GolddDuels(db, pDeck, aDeck, Check);
            SillvaDuels(db, pDeck, aDeck, Check);
            SegocDuel(db, pDeck, aDeck, Check);
            BrronDuels(db, pDeck, aDeck, Check);
            ScarrDuels(db, pDeck, aDeck, Check);
        }

        static string Left(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} " +
                  $"clauses={string.Join(",", prog.ClauseList.Select(c => $"{c?.Timing}/{c?.Action}"))} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static bool One(CardDatabase db, int id, Func<EffectClause, bool> shape, out string detail)
        {
            var prog = CardTextEffectCompiler.Compile(db.Get(id));
            detail = Left(prog);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                   prog.ClauseList.Exists(c => c != null && shape(c)) &&
                   CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;
        }

        // ── Compile shapes ──

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var ok = One(db, Beiige, c => c.Timing == EffectTiming.DiscardedToGyByCardEffect &&
                                          c.Action == EffectActionKind.SpecialSummonThisFromGyFaceUpWhenDiscarded &&
                                          !c.IsOptional && c.MakesChainLink, out var d);
            Check("Dark World: Beiige compiles to a mandatory discarded-by-a-card-effect self Special Summon", ok, d);

            ok = One(db, Broww, c => c.Timing == EffectTiming.DiscardedToGyByCardEffect &&
                                     c.Action == EffectActionKind.DrawPlusOneIfDiscardedByOpponentEffect &&
                                     c.Amount == 1 && !c.IsOptional, out d);
            Check("Dark World: Broww compiles to draw 1 (+1 by the opponent's effect)", ok, d);

            ok = One(db, Goldd, c => c.Timing == EffectTiming.DiscardedToGyByCardEffect &&
                                     c.Action == EffectActionKind
                                         .SpecialSummonThisFromGyThenDestroyUpToTargetsIfDiscardedByOpponent &&
                                     c.Amount == 2 && c.Zone == EffectZoneFilter.OppAnyCardOnField,
                out d);
            Check("Dark World: Goldd compiles to Special Summon itself, then destroy up to 2 targets (by the opponent)",
                ok, d);

            ok = One(db, Sillva, c => c.Timing == EffectTiming.DiscardedToGyByCardEffect &&
                                      c.Action == EffectActionKind
                                          .SpecialSummonThisFromGyThenOpponentPutsHandCardsOnDeckBottomIfDiscardedByOpponent &&
                                      c.Amount == 2 && !c.IsOptional, out d);
            Check("Dark World: Sillva compiles to Special Summon itself, then the opponent returns exactly 2 (by them)",
                ok, d);

            ok = One(db, Brron, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                     c.Action == EffectActionKind.YouCanDiscardCardsOfYourChoice &&
                                     c.Amount == 1 && c.IsOptional && !c.RequiresTargetChoice, out d);
            Check("Dark World: Brron compiles to an optional battle-damage discard of 1 card (no target)", ok, d);

            ok = One(db, Scarr, c => c.Timing == EffectTiming.SentFromFieldToGy &&
                                     c.Action == EffectActionKind.AddSeriesMonsterLevelLeqFromDeckToHand &&
                                     c.RequiresThisDestroyedByBattle && c.Amount == 4 && c.NamedCard == "Dark World" &&
                                     c.NamedCardIsSeries && !c.IsOptional, out d);
            Check("Dark World: Scarr compiles to a mandatory battle-destroyed Level <= 4 \"Dark World\" monster search",
                ok, d);

            Check("Dark World: the new actions map to shared resolutions",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.SpecialSummonThisFromGyFaceUpWhenDiscarded }) ==
                EffectResolutionKind.SpecialSummon &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.DrawPlusOneIfDiscardedByOpponentEffect }) == EffectResolutionKind.Draw &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.YouCanDiscardCardsOfYourChoice }) == EffectResolutionKind.Discard &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.AddSeriesMonsterLevelLeqFromDeckToHand }) ==
                EffectResolutionKind.Search);
        }

        // ── Look-alike sentences stay uncompiled (whole-card atoms) ──

        static CompiledCardProgram Synthetic(int id, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = "Dark World near miss " + id,
                type = "Effect Monster",
                frameType = "effect",
                race = "Fiend",
                attribute = "DARK",
                level = 4,
                atk = 1600,
                def = 1300,
                desc = desc
            });

        static bool NoAction(CompiledCardProgram prog, params EffectActionKind[] kinds) =>
            prog == null || !prog.FullyCompiled || !prog.ClauseList.Exists(c => c != null && kinds.Contains(c.Action));

        static void NearMisses(Chk Check)
        {
            var ours = new[]
            {
                EffectActionKind.SpecialSummonThisFromGyFaceUpWhenDiscarded,
                EffectActionKind.DrawPlusOneIfDiscardedByOpponentEffect,
                EffectActionKind.SpecialSummonThisFromGyThenDestroyUpToTargetsIfDiscardedByOpponent,
                EffectActionKind.SpecialSummonThisFromGyThenOpponentPutsHandCardsOnDeckBottomIfDiscardedByOpponent,
                EffectActionKind.YouCanDiscardCardsOfYourChoice,
                EffectActionKind.AddSeriesMonsterLevelLeqFromDeckToHand
            };
            var cases = new[]
            {
                // A rider after the Summon.
                "If this card is discarded to the Graveyard by a card effect: Special Summon it from the Graveyard, and " +
                "if you do, it gains 500 ATK.",
                // Not "by a card effect" (a cost or the hand size rule would count).
                "If this card is discarded to the Graveyard: Special Summon it from the Graveyard.",
                // A different draw.
                "If this card is discarded to the Graveyard by a card effect: Draw 2 cards, plus another card if this card " +
                "was discarded from your hand to your Graveyard by an opponent's card effect.",
                // Up to 3, and banish.
                "If this card is discarded to the Graveyard by a card effect: If it was discarded from your hand to your " +
                "Graveyard by an opponent's card effect, you can target up to 2 cards your opponent controls; Special " +
                "Summon this card from the Graveyard, then banish those targets (if any).",
                // Brron with a rider.
                "When this card inflicts battle damage to your opponent: You can discard 1 card, then draw 1 card.",
                // Scarr with a rider / a different kind.
                "When this card is destroyed by battle and sent to the Graveyard: Add 1 Level 4 or lower \"Dark World\" " +
                "monster from your Deck to your hand, then discard 1 card.",
                "When this card is destroyed by battle and sent to the Graveyard: Add 1 Level 4 or lower \"Dark World\" " +
                "card from your Deck to your hand."
            };
            for (var i = 0; i < cases.Length; i++)
            {
                var prog = Synthetic(990700 + i, cases[i]);
                Check($"Dark World near miss #{i + 1} does not compile to this batch's actions", NoAction(prog, ours),
                    Left(prog));
            }
        }

        // ── Shared setup ──

        static void Empty(DuelEngine e)
        {
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                // Draws come from plain Normal Monsters, so no drawn card can answer a Chain.
                who.Deck.Clear();
                for (var i = 0; i < 10; i++) who.Deck.Add(Celtic);
            }
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 16; i++)
            {
                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                if (e.HasDeclaredAttack && !e.IsAwaitingEffectTarget)
                {
                    e.ResolveDeclaredAttack();
                    continue;
                }

                break;
            }

            e.NotifyPublic();
        }

        static bool OnField(DuelistState who, CardInstance c, BattlePosition? pos = null) =>
            who.TryFindMonster(c, out _) && c.FaceUp && (!pos.HasValue || c.Position == pos.Value);

        static string State(DuelEngine e) =>
            $"pHand={e.Player.Hand.Count} oHand={e.Opponent.Hand.Count} pGy={e.Player.Graveyard.Count} " +
            $"oGy={e.Opponent.Graveyard.Count} pMon={e.Player.MonstersOnField().Count()} " +
            $"oMon={e.Opponent.MonstersOnField().Count()} pending={e.IsAwaitingEffectTarget} " +
            $"legal={string.Join(",", e.PendingActivation?.LegalTargets.Select(x => x?.Name) ?? Array.Empty<string>())} " +
            $"chain={e.Chain.HasLinks}";

        /// <summary>The human answers the Special Summon pick of <paramref name="card"/> in <paramref name="pos"/>.</summary>
        static bool AnswerSummon(DuelEngine e, CardInstance card, BattlePosition pos)
        {
            if (!e.IsAwaitingEffectTarget || e.PendingActivation?.LegalTargets.Contains(card) != true) return false;
            e.SetPendingSpecialSummonPlacement(-1, pos);
            var ok = e.TrySelectEffectTarget(card);
            Settle(e);
            return ok;
        }

        // ── Beiige ──

        static void BeiigeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Own effect: the AI's Card Destruction discards its own Beiige → it Special Summons itself.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                PutInHand(e, p, Celtic);
                var beiige = PutInHand(e, o, Beiige);
                var cd = PutInHand(e, o, CardDestruction);
                var act = reached && e.TryActivateSpellTrap(o, cd, fromHand: true);
                Settle(e);
                Check("Beiige: discarded by its owner's own Card Destruction → Special Summoned from the GY",
                    act && OnField(o, beiige) && !o.Graveyard.Contains(beiige) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} {State(e)}");
            }

            // Cost: the AI's Lightning Vortex discards Beiige as its cost → no trigger.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var beiige = PutInHand(e, o, Beiige);
                var vortex = PutInHand(e, o, LightningVortex);
                var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var act = reached && e.TryActivateSpellTrap(o, vortex, fromHand: true);
                Settle(e);
                Check("Beiige: discarded as Lightning Vortex's cost → it does not Special Summon itself",
                    act && o.Graveyard.Contains(beiige) && !o.TryFindMonster(beiige, out _) &&
                    !p.TryFindMonster(mine, out _) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} {State(e)}");
            }

            // By the opponent: the AI's Delinquent Duo discards the player's lone Beiige → the player taps it and
            // chooses Summon DEF.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var beiige = PutInHand(e, p, Beiige);
                var duo = PutInHand(e, o, Duo);
                var act = reached && e.TryActivateSpellTrap(o, duo, fromHand: true);
                Settle(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(beiige);
                var answered = AnswerSummon(e, beiige, BattlePosition.Defense);
                Check("Beiige: discarded by the opponent's Delinquent Duo → its owner Special Summons it in face-up Defense",
                    act && asked && answered && OnField(p, beiige, BattlePosition.Defense) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} asked={asked} answered={answered} {State(e)}");
            }
        }

        // ── Broww ──

        static void BrowwDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // By the opponent's Delinquent Duo: draw 2.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var broww = PutInHand(e, p, Broww);
                var duo = PutInHand(e, o, Duo);
                var deck = p.Deck.Count;
                var act = reached && e.TryActivateSpellTrap(o, duo, fromHand: true);
                Settle(e);
                Check("Broww: discarded by the opponent's card effect → its owner draws 2",
                    act && p.Graveyard.Contains(broww) && p.Hand.Count == 2 && p.Deck.Count == deck - 2,
                    $"reached={reached} act={act} deck={p.Deck.Count}/{deck} {State(e)}");
            }

            // By its owner's own Card Destruction: draw 1 (after the redraw).
            {
                var e = Fresh(db, pDeck, aDeck);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var cd = PutInHand(e, p, CardDestruction);
                var broww = PutInHand(e, p, Broww);
                PutInHand(e, p, Celtic);
                PutInHand(e, o, Celtic);
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, cd, fromHand: true);
                Settle(e);
                Check("Broww: discarded by its owner's own Card Destruction → 2 redrawn + 1",
                    act && p.Graveyard.Contains(broww) && p.Hand.Count == 3 && p.Deck.Count == deck - 3 &&
                    o.Hand.Count == 1,
                    $"act={act} deck={p.Deck.Count}/{deck} {State(e)}");
            }

            // By its owner's own Graceful Charity (the player picks Broww among the 2 discards): draw 1.
            {
                var e = Fresh(db, pDeck, aDeck);
                Empty(e);
                var p = e.Player;
                var gc = PutInHand(e, p, GracefulCharity);
                var broww = PutInHand(e, p, Broww);
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, gc, fromHand: true);
                Settle(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(broww);
                var first = asked && e.TrySelectEffectTarget(broww);
                var other = e.PendingActivation?.LegalTargets.FirstOrDefault(c => c != null && c != broww);
                var second = first && other != null && e.TrySelectEffectTarget(other);
                Settle(e);
                Check("Broww: discarded by its owner's own Graceful Charity → draws 1 after the discard",
                    act && asked && second && p.Graveyard.Contains(broww) && p.Hand.Count == 3 &&
                    p.Deck.Count == deck - 4 && !e.IsAwaitingEffectTarget,
                    $"act={act} asked={asked} first={first} second={second} deck={p.Deck.Count}/{deck} {State(e)}");
            }
        }

        // ── Goldd ──

        static void GolddDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // By the opponent: the player targets 2 of the opponent's 3 cards as it activates, Summons it, then
            // destroys those 2.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var a = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var set = PlaceSetTrap(e, o, Mst, 1);
                var goldd = PutInHand(e, p, Goldd);
                var duo = PutInHand(e, o, Duo);
                var act = reached && e.TryActivateSpellTrap(o, duo, fromHand: true);
                Settle(e);
                var askedTarget = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(a) &&
                                  e.PendingActivation.LegalTargets.Contains(set) && !p.TryFindMonster(goldd, out _);
                var t1 = askedTarget && e.TrySelectEffectTarget(a);
                Settle(e);
                var askedMore = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(set) &&
                                !e.PendingActivation.LegalTargets.Contains(a);
                var t2 = askedMore && e.TrySelectEffectTarget(set);
                Settle(e);
                var summoned = AnswerSummon(e, goldd, BattlePosition.Attack);
                Check("Goldd: by the opponent's effect — 2 targets chosen as it activates; Summoned, then both destroyed",
                    act && askedTarget && t1 && askedMore && t2 && summoned && OnField(p, goldd, BattlePosition.Attack) &&
                    o.Graveyard.Contains(a) && o.Graveyard.Contains(set) && o.TryFindMonster(b, out _),
                    $"reached={reached} act={act} askedTarget={askedTarget} t1={t1} askedMore={askedMore} t2={t2} " +
                    $"summoned={summoned} {State(e)}");
            }

            // By the opponent, Cancel at the target: Summoned, nothing destroyed.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var a = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var goldd = PutInHand(e, p, Goldd);
                var duo = PutInHand(e, o, Duo);
                var act = reached && e.TryActivateSpellTrap(o, duo, fromHand: true);
                Settle(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(a);
                var declined = asked && e.CancelEffectTargeting();
                Settle(e);
                var summoned = AnswerSummon(e, goldd, BattlePosition.Defense);
                Check("Goldd: Cancel at the optional target — it is still Special Summoned, nothing is destroyed",
                    act && asked && declined && summoned && OnField(p, goldd, BattlePosition.Defense) &&
                    o.TryFindMonster(a, out _),
                    $"reached={reached} act={act} asked={asked} declined={declined} summoned={summoned} {State(e)}");
            }

            // Own effect (the player's Card Destruction): no target is offered; only the Summon.
            {
                var e = Fresh(db, pDeck, aDeck);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var a = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var cd = PutInHand(e, p, CardDestruction);
                var goldd = PutInHand(e, p, Goldd);
                PutInHand(e, o, Celtic);
                var act = e.TryActivateSpellTrap(p, cd, fromHand: true);
                Settle(e);
                var onlySummon = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1 &&
                                 e.PendingActivation.LegalTargets.Contains(goldd);
                var summoned = AnswerSummon(e, goldd, BattlePosition.Attack);
                Check("Goldd: discarded by its owner's own effect — Special Summoned with no target, nothing destroyed",
                    act && onlySummon && summoned && OnField(p, goldd) && o.TryFindMonster(a, out _),
                    $"act={act} onlySummon={onlySummon} summoned={summoned} {State(e)}");
            }

            // The AI's Goldd by the player's Delinquent Duo: it targets the player's 2 cards and destroys them.
            {
                var e = Fresh(db, pDeck, aDeck);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var goldd = PutInHand(e, o, Goldd);
                var duo = PutInHand(e, p, Duo);
                var act = e.TryActivateSpellTrap(p, duo, fromHand: true);
                Settle(e);
                Check("Goldd (AI): by the player's Delinquent Duo — Special Summoned, the player's 2 monsters destroyed",
                    act && OnField(o, goldd) && p.Graveyard.Contains(a) && p.Graveyard.Contains(b) &&
                    !e.IsAwaitingEffectTarget,
                    $"act={act} {State(e)}");
            }
        }

        // ── Sillva ──

        static void SillvaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // By the opponent: Summoned, then the AI puts exactly 2 of its 3 hand cards on the bottom of its Deck.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var sillva = PutInHand(e, p, Sillva);
                var duo = PutInHand(e, o, Duo);
                PutInHand(e, o, Mst);
                PutInHand(e, o, Mst);
                PutInHand(e, o, Mst);
                var deck = o.Deck.Count;
                var act = reached && e.TryActivateSpellTrap(o, duo, fromHand: true);
                Settle(e);
                var summoned = AnswerSummon(e, sillva, BattlePosition.Attack);
                var bottom = o.Deck.Count >= 2 && o.Deck[o.Deck.Count - 1] == Mst && o.Deck[o.Deck.Count - 2] == Mst;
                Check("Sillva: by the opponent's effect — Summoned, then the opponent returns exactly 2 cards to the Deck bottom",
                    act && summoned && OnField(p, sillva) && o.Hand.Count == 1 && o.Deck.Count == deck + 2 && bottom,
                    $"reached={reached} act={act} summoned={summoned} deck={o.Deck.Count}/{deck} bottom={bottom} {State(e)}");
            }

            // By the opponent, who holds only 1 card: Summoned, nothing returned.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var sillva = PutInHand(e, p, Sillva);
                var duo = PutInHand(e, o, Duo);
                var keep = PutInHand(e, o, Mst);
                var act = reached && e.TryActivateSpellTrap(o, duo, fromHand: true);
                Settle(e);
                var summoned = AnswerSummon(e, sillva, BattlePosition.Attack);
                Check("Sillva: the opponent holds fewer than 2 cards — Summoned, nothing returned",
                    act && summoned && OnField(p, sillva) && o.Hand.Count == 1 && o.Hand.Contains(keep),
                    $"reached={reached} act={act} summoned={summoned} {State(e)}");
            }

            // Own effect: Summoned only, the opponent keeps its redrawn hand.
            {
                var e = Fresh(db, pDeck, aDeck);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var cd = PutInHand(e, p, CardDestruction);
                var sillva = PutInHand(e, p, Sillva);
                PutInHand(e, o, Mst);
                PutInHand(e, o, Mst);
                PutInHand(e, o, Mst);
                var act = e.TryActivateSpellTrap(p, cd, fromHand: true);
                Settle(e);
                var summoned = AnswerSummon(e, sillva, BattlePosition.Attack);
                Check("Sillva: discarded by its owner's own effect — Summoned only, the opponent keeps 3 cards",
                    act && summoned && OnField(p, sillva) && o.Hand.Count == 3,
                    $"act={act} summoned={summoned} {State(e)}");
            }
        }

        // ── SEGOC: Card Destruction discards a Dark World monster from each hand ──

        static void SegocDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            Empty(e);
            var p = e.Player;
            var o = e.Opponent;
            var foe = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var goldd = PutInHand(e, p, Goldd);
            PutInHand(e, p, Celtic);
            var beiige = PutInHand(e, o, Beiige);
            var cd = PutInHand(e, o, CardDestruction);
            var order = new List<string>();

            void OnLog(string msg)
            {
                if (msg == null) return;
                if (msg.StartsWith("Goldd, Wu-Lord of Dark World: ") && msg.Contains("Special Summons it")) order.Add("Goldd");
                if (msg.StartsWith("Beiige, Vanguard of Dark World: ") && msg.Contains("Special Summons it"))
                    order.Add("Beiige");
            }

            e.OnLog += OnLog;
            var act = reached && e.TryActivateSpellTrap(o, cd, fromHand: true);
            Settle(e);
            // CL1 the turn player's Beiige, CL2 the player's Goldd: Goldd targets as it activates (Beiige is still in
            // the GY, so the Celtic is the only card the opponent controls).
            var askedTarget = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1 &&
                              e.PendingActivation.LegalTargets.Contains(foe);
            var t = askedTarget && e.TrySelectEffectTarget(foe);
            Settle(e);
            var summoned = AnswerSummon(e, goldd, BattlePosition.Attack);
            e.OnLog -= OnLog;
            Check("Dark World SEGOC: Card Destruction discards both — one Chain, Goldd (CL2) resolves before Beiige (CL1)",
                act && askedTarget && t && summoned && OnField(p, goldd) && OnField(o, beiige) &&
                o.Graveyard.Contains(foe) && order.SequenceEqual(new[] { "Goldd", "Beiige" }),
                $"reached={reached} act={act} askedTarget={askedTarget} t={t} summoned={summoned} " +
                $"order={string.Join(">", order)} {State(e)}");
        }

        // ── Brron ──

        static void BrronDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var withDarkWorld in new[] { true, false })
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                var brron = PlaceMonster(e, o, Brron, 0, BattlePosition.Attack, true);
                brron.ClearAttackFlags();
                var held = PutInHand(e, o, withDarkWorld ? Beiige : Celtic);
                var lp = p.LifePoints;
                if (e.IsAwaitingResponse) e.PassResponse();
                var attacked = reached && e.Phase == DuelPhase.Battle && e.TryAttack(o, brron, null);
                DrainCombat(e);
                Settle(e);
                if (withDarkWorld)
                    Check("Brron: inflicts battle damage → the AI discards its Beiige, which Special Summons itself",
                        attacked && p.LifePoints == lp - 1800 && OnField(o, held) && !o.Hand.Contains(held),
                        $"reached={reached} attacked={attacked} LP={p.LifePoints}/{lp} {State(e)}");
                else
                    Check("Brron: the AI declines to discard a card that gains nothing from it",
                        attacked && p.LifePoints == lp - 1800 && o.Hand.Contains(held),
                        $"reached={reached} attacked={attacked} LP={p.LifePoints}/{lp} {State(e)}");
            }
        }

        // ── Scarr ──

        static void ScarrDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var withTarget in new[] { true, false })
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                Empty(e);
                var p = e.Player;
                var o = e.Opponent;
                // A Level 5 "Dark World" monster never qualifies; Zure (Level 4) does.
                o.Deck.Add(Goldd);
                if (withTarget) o.Deck.Add(Zure);
                var scarr = PlaceMonster(e, o, Scarr, 0, BattlePosition.Attack, true);
                scarr.ClearAttackFlags();
                var wall = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                if (e.IsAwaitingResponse) e.PassResponse();
                var attacked = reached && e.Phase == DuelPhase.Battle && e.TryAttack(o, scarr, wall);
                DrainCombat(e);
                Settle(e);
                var destroyed = o.Graveyard.Contains(scarr);
                if (withTarget)
                    Check("Scarr: destroyed by battle → adds Level 4 \"Dark World\" Zure from the Deck (not Level 5 Goldd)",
                        attacked && destroyed && o.Hand.Count == 1 && o.Hand[0].CardId == Zure &&
                        !o.Deck.Contains(Zure) && o.Deck.Contains(Goldd),
                        $"reached={reached} attacked={attacked} destroyed={destroyed} {State(e)}");
                else
                    Check("Scarr: no Level 4 or lower \"Dark World\" monster in the Deck → nothing is added",
                        attacked && destroyed && o.Hand.Count == 0 && o.Deck.Contains(Goldd),
                        $"reached={reached} attacked={attacked} destroyed={destroyed} {State(e)}");
            }
        }
    }
}
