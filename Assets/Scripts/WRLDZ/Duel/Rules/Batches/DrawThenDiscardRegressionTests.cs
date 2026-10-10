using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "DrawThenDiscard": Graceful Charity ("Draw 3 cards, then discard 2 cards."). It used to compile to a
    /// bare Draw 3 through the generic PSCT draw fallback, so both the player and the AI kept all 3 cards (a free +2).
    /// Now: one whole-text atom → <see cref="EffectActionKind.DrawThenDiscard"/>; the human picks the discards, the AI
    /// discards its lowest <see cref="AiHeuristicPolicy.DiscardPriority"/> cards; the fallback refuses any draw that
    /// goes on with ", then …" (Spiral Spear Strike's partial Draw 2 is gone).
    /// </summary>
    public static class DrawThenDiscardRegressionTests
    {
        const int GracefulCharity = 79571449;
        const int SpiralSpearStrike = 49328340;
        const int PotOfGreed = 55144522;
        const int JarOfGreed = 83968380;         // Normal Trap: draw 1
        const int ProtectorOfTheSanctuary = 24221739; // Continuous Trap: the opponent cannot draw outside the Draw Phase
        const int Appropriate = 48539234;        // Continuous Trap (v142 HandDisruption): after the opponent draws

        const int Celtic = 91152256;        // Normal Lv4 1400/1200 (DiscardPriority 24)
        const int Bewd = 89631139;          // Normal Lv8 3000/2500 (40)
        const int SummonedSkull = 70781052; // Normal Lv6 2500/1200 (35)
        const int SkullServant = 32274490;  // Normal Lv1 300/200 (13)

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("DrawThenDiscard: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(Check);
            HumanDiscardPick(db, pDeck, aDeck, Check);
            AiDiscard(db, pDeck, aDeck, Check);
            ActivationLegality(db, pDeck, aDeck, Check);
            CrossGroupAppropriate(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            var def = db.Get(GracefulCharity);
            var prog = CardTextEffectCompiler.Compile(def);
            var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
            Check("Graceful Charity: FullyCompiled — one Activate DrawThenDiscard (draw 3, then discard 2), not a bare Draw",
                prog != null && prog.FullyCompiled && prog.CanResolveAny && c != null &&
                c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.DrawThenDiscard &&
                c.Side == EffectSide.Controller && c.Amount == 3 && c.ThenDiscardCount == 2 &&
                c.ActivationCondition == DrawThenDiscardRuntime.CanDrawAmount &&
                c.CheckedAt == ConditionCheckedAt.Activation && c.MakesChainLink &&
                !c.RequiresTargetChoice && !c.StaysOnField && !c.RequiresDiscardCost && c.DiscardCostCount == 0 &&
                !prog.ClauseList.Exists(x => x != null && x.Action == EffectActionKind.Draw),
                c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
            Check("Graceful Charity: classified Implemented",
                CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(def).ToString());
            Check("Graceful Charity: the discard is part of the effect, not a cost",
                c != null && EffectVocabulary.CostOf(c) == EffectCostKind.None);
            Check("DrawThenDiscard maps to the shared Draw resolution",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.DrawThenDiscard }) ==
                EffectResolutionKind.Draw);

            var spiral = CardTextEffectCompiler.Compile(db.Get(SpiralSpearStrike));
            Check("Spiral Spear Strike: its \"Draw 2 cards, then discard 1 card\" no longer compiles to a bare Draw 2",
                spiral != null && !spiral.FullyCompiled &&
                !spiral.ClauseList.Exists(x => x != null &&
                                               (x.Action == EffectActionKind.Draw ||
                                                x.Action == EffectActionKind.DrawThenDiscard)) &&
                CardEffectStatus.Classify(db.Get(SpiralSpearStrike)) != CardEffectStatusKind.Implemented,
                $"clauses=[{string.Join("; ", spiral?.ClauseList.Select(EffectVocabulary.Describe) ?? Array.Empty<string>())}] " +
                $"status={CardEffectStatus.Classify(db.Get(SpiralSpearStrike))}");

            foreach (var (id, n) in new[] { (PotOfGreed, 2), (JarOfGreed, 1) })
            {
                var p = CardTextEffectCompiler.Compile(db.Get(id));
                Check($"{db.Get(id)?.name}: a flat draw still compiles to Draw {n}",
                    p != null && p.FullyCompiled &&
                    p.ClauseList.Exists(x => x != null && x.Action == EffectActionKind.Draw && x.Amount == n));
            }
        }

        static CompiledCardProgram CompileSpell(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990101, name = "Near-miss Spell", type = "Spell Card", frameType = "spell", race = "Normal",
                desc = text
            });

        static CompiledCardProgram CompileMonster(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990102, name = "Near-miss Monster", type = "Effect Monster", frameType = "effect",
                race = "Spellcaster", attribute = "DARK", level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool FullyWithAction(CompiledCardProgram p, EffectActionKind action) =>
            p != null && p.FullyCompiled && p.ClauseList.Exists(c => c != null && c.Action == action);

        static void NearMisses(CheckFn Check)
        {
            var two = CompileSpell("Draw 2 cards, then discard 1 card.");
            var c = two?.ClauseList.FirstOrDefault();
            Check("Same sentence, other numbers: Draw 2, then discard 1 → DrawThenDiscard 2 / 1",
                FullyWithAction(two, EffectActionKind.DrawThenDiscard) && two.ClauseList.Count == 1 &&
                c != null && c.Amount == 2 && c.ThenDiscardCount == 1);

            foreach (var (label, text) in new[]
                     {
                         ("an extra sentence", "Draw 2 cards, then discard 1 card. Your opponent gains 1000 LP."),
                         ("a condition before the draw", "If you control no monsters: Draw 2 cards, then discard 1 card."),
                         ("a filtered discard", "Draw 2 cards, then discard 1 Spell Card."),
                         ("another follow-up (return to the Deck)",
                             "Draw 1 card, then return 1 card from your hand to the bottom of the Deck."),
                     })
            {
                var p = CompileSpell(text);
                Check($"Near miss ({label}) fails closed: not FullyCompiled, no bare Draw",
                    p != null && !p.FullyCompiled &&
                    !p.ClauseList.Exists(x => x != null && x.Action == EffectActionKind.Draw),
                    $"full={p?.FullyCompiled} clauses=[{string.Join("; ", p?.ClauseList.Select(EffectVocabulary.Describe) ?? Array.Empty<string>())}]");
            }

            var ign = CompileMonster("Once per turn: You can draw 1 card, then discard 1 card.");
            Check("Near miss (monster ignition \"draw 1 card, then discard 1 card\") fails closed, no bare Draw",
                ign != null && !ign.FullyCompiled &&
                !ign.ClauseList.Exists(x => x != null && x.Action == EffectActionKind.Draw),
                $"full={ign?.FullyCompiled} clauses=[{string.Join("; ", ign?.ClauseList.Select(EffectVocabulary.Describe) ?? Array.Empty<string>())}]");
            var flatIgn = CompileMonster("Once per turn: You can draw 1 card.");
            Check("Control: the same monster ignition without \"then\" still compiles to Draw 1",
                FullyWithAction(flatIgn, EffectActionKind.Draw));
        }

        // ───────────────────────── duels ─────────────────────────

        static void StackDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            who.Deck.AddRange(ids);
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 12 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        /// <summary>The Player's Main Phase, empty board and hands, the given hand and Deck (top first).</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out CardInstance gc,
            int[] hand, int[] deck)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            gc = PutInHand(engine, engine.Player, GracefulCharity);
            foreach (var id in hand) PutInHand(engine, engine.Player, id);
            StackDeck(engine.Player, deck);
            return engine;
        }

        static string Names(System.Collections.Generic.IEnumerable<CardInstance> cards) =>
            string.Join(",", cards.Where(x => x != null).Select(x => x.Name));

        static void HumanDiscardPick(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Hand: Graceful Charity + Celtic. Deck: Bewd, Skull Servant, Summoned Skull, Celtic.
                var engine = PlayerMain(db, pDeck, aDeck, out var gc, new[] { Celtic },
                    new[] { Bewd, SkullServant, SummonedSkull, Celtic });
                var p = engine.Player;
                var mainOk = engine.TurnPlayer == p && engine.InMainPhase;
                var ok = engine.TryActivateSpellTrap(p, gc, fromHand: true);
                PassAll(engine);
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.AwaitingDrawThenDiscard && pend.Controller == p &&
                            pend.TargetPicksRemaining == 2 && pend.TargetKind == EffectTargetKind.DiscardFromHand &&
                            pend.LegalTargets.Count == 4 && pend.LegalTargets.All(p.Hand.Contains);
                Check("Graceful Charity (player): draws 3, then asks the player to choose 2 hand cards to discard",
                    mainOk && ok && asked && p.HandCount == 4 && p.Deck.Count == 1 && p.Graveyard.Contains(gc),
                    $"main={mainOk} ok={ok} hand={p.HandCount} deck={p.Deck.Count} gcInGy={p.Graveyard.Contains(gc)} " +
                    $"pending={(pend == null ? "none" : $"dtd={pend.AwaitingDrawThenDiscard} left={pend.TargetPicksRemaining} n={pend.LegalTargets.Count} kind={pend.TargetKind}")}");
                Check("Graceful Charity (player): the prompt asks for the discard",
                    pend != null && pend.Prompt.IndexOf("discard 2 more", StringComparison.OrdinalIgnoreCase) >= 0,
                    pend?.Prompt);

                var bewd = p.Hand.FirstOrDefault(x => x.CardId == Bewd);
                var first = bewd != null && engine.TrySelectEffectTarget(bewd);
                Check("Graceful Charity (player): nothing is discarded until the second card is chosen",
                    first && engine.IsAwaitingEffectTarget && engine.PendingActivation.TargetPicksRemaining == 1 &&
                    p.HandCount == 4 && !p.Graveyard.Contains(bewd),
                    $"first={first} pending={engine.IsAwaitingEffectTarget} hand={p.HandCount}");
                var skull = p.Hand.FirstOrDefault(x => x.CardId == SummonedSkull);
                var second = skull != null && engine.TrySelectEffectTarget(skull);
                Check("Graceful Charity (player): the 2 chosen cards go to the GY together; hand ends +0, not +2",
                    second && !engine.IsAwaitingEffectTarget && p.HandCount == 2 &&
                    p.Graveyard.Contains(bewd) && p.Graveyard.Contains(skull) &&
                    p.Hand.Count(x => x.CardId == Celtic) == 1 && p.Hand.Count(x => x.CardId == SkullServant) == 1,
                    $"second={second} pending={engine.IsAwaitingEffectTarget} hand=[{Names(p.Hand)}] gy=[{Names(p.Graveyard)}]");
            }

            {
                // Cancel after one pick: the discard cannot be skipped — the lowest-value card completes it.
                var engine = PlayerMain(db, pDeck, aDeck, out var gc, new[] { Celtic },
                    new[] { Bewd, SkullServant, SummonedSkull, Celtic });
                var p = engine.Player;
                engine.TryActivateSpellTrap(p, gc, fromHand: true);
                PassAll(engine);
                var bewd = p.Hand.FirstOrDefault(x => x.CardId == Bewd);
                if (bewd != null) engine.TrySelectEffectTarget(bewd);
                engine.CancelEffectTargeting();
                Check("Graceful Charity (player): Cancel after 1 pick still discards 2 (the pick + Skull Servant)",
                    !engine.IsAwaitingEffectTarget && p.HandCount == 2 && p.Graveyard.Contains(bewd) &&
                    p.Graveyard.Any(x => x.CardId == SkullServant) && p.Graveyard.Contains(gc),
                    $"pending={engine.IsAwaitingEffectTarget} hand=[{Names(p.Hand)}] gy=[{Names(p.Graveyard)}]");
            }

            {
                // Cancel with no pick: the 2 lowest-value cards (Skull Servant, Celtic) are discarded.
                var engine = PlayerMain(db, pDeck, aDeck, out var gc, new[] { Bewd },
                    new[] { SummonedSkull, SkullServant, Celtic, Celtic });
                var p = engine.Player;
                engine.TryActivateSpellTrap(p, gc, fromHand: true);
                PassAll(engine);
                var asked = engine.PendingActivation?.AwaitingDrawThenDiscard == true;
                engine.CancelEffectTargeting();
                Check("Graceful Charity (player): Cancel with no pick discards the 2 lowest-value cards",
                    asked && !engine.IsAwaitingEffectTarget && p.HandCount == 2 &&
                    p.Hand.Any(x => x.CardId == Bewd) && p.Hand.Any(x => x.CardId == SummonedSkull),
                    $"asked={asked} pending={engine.IsAwaitingEffectTarget} hand=[{Names(p.Hand)}]");
            }

            {
                // Only if all 3 were drawn (Lua: Draw(...)==3): a blocked draw discards nothing.
                var engine = PlayerMain(db, pDeck, aDeck, out var gc, new[] { Celtic, Bewd },
                    new[] { SummonedSkull, SkullServant, Celtic, Celtic });
                var p = engine.Player;
                var opp = engine.Opponent;
                var prot = engine.CreateCardInstance(ProtectorOfTheSanctuary);
                prot.FaceUp = true;
                prot.SetThisTurn = false;
                opp.SpellTrapZones[0].Occupant = prot;
                var clause = CardTextEffectCompiler.Compile(db.Get(GracefulCharity))?.ClauseList.FirstOrDefault();
                var handBefore = p.HandCount;
                var blocked = ContinuousProtections.BlocksDrawOutsideDrawPhase(engine, p);
                if (clause != null) DrawThenDiscardRuntime.Resolve(engine, p, gc, clause);
                Check("Graceful Charity: a draw stopped by Protector of the Sanctuary discards nothing",
                    blocked && clause != null && !engine.IsAwaitingEffectTarget && p.HandCount == handBefore &&
                    p.Deck.Count == 4,
                    $"blocked={blocked} pending={engine.IsAwaitingEffectTarget} hand {handBefore}->{p.HandCount} deck={p.Deck.Count}");
            }
        }

        static void AiDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var engine = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            var opp = engine.Opponent;
            opp.Hand.Clear();
            engine.Player.Hand.Clear();
            var gc = PutInHand(engine, opp, GracefulCharity);
            PutInHand(engine, opp, SummonedSkull);
            StackDeck(opp, Bewd, SkullServant, Celtic, Celtic, Celtic);
            var ok = engine.TryActivateSpellTrap(opp, gc, fromHand: true);
            PassAll(engine);
            Check("Graceful Charity (AI): draws 3 and discards its 2 lowest-value cards (Skull Servant, Celtic); no prompt",
                reached && ok && !engine.IsAwaitingEffectTarget && opp.HandCount == 2 &&
                opp.Hand.Any(x => x.CardId == Bewd) && opp.Hand.Any(x => x.CardId == SummonedSkull) &&
                opp.Graveyard.Any(x => x.CardId == SkullServant) && opp.Graveyard.Any(x => x.CardId == Celtic) &&
                opp.Graveyard.Contains(gc) && opp.Deck.Count == 2,
                $"reached={reached} ok={ok} pending={engine.IsAwaitingEffectTarget} hand=[{Names(opp.Hand)}] " +
                $"gy=[{Names(opp.Graveyard)}] deck={opp.Deck.Count}");
        }

        static void ActivationLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var gc, new[] { Celtic }, new[] { Bewd, SkullServant });
                var p = engine.Player;
                var two = engine.CanActivateSpellTrap(p, gc, fromHand: true);
                p.Deck.Add(SummonedSkull);
                var three = engine.CanActivateSpellTrap(p, gc, fromHand: true);
                Check("Graceful Charity: needs 3 cards in the Deck to activate (ygopro IsPlayerCanDraw(tp,3))",
                    !two && three, $"deck2={two} deck3={three}");

                var ok = engine.TryActivateSpellTrap(p, gc, fromHand: true);
                PassAll(engine);
                Check("Graceful Charity: with exactly 3 cards it draws them all (no deck-out) and asks for the discard",
                    ok && !engine.GameOver && p.Deck.Count == 0 &&
                    engine.PendingActivation?.AwaitingDrawThenDiscard == true,
                    $"ok={ok} over={engine.GameOver} deck={p.Deck.Count}");
            }

            {
                var engine = PlayerMain(db, pDeck, aDeck, out var gc, new[] { Celtic },
                    new[] { Bewd, SkullServant, SummonedSkull, Celtic });
                var p = engine.Player;
                var prot = engine.CreateCardInstance(ProtectorOfTheSanctuary);
                prot.FaceUp = true;
                prot.SetThisTurn = false;
                engine.Opponent.SpellTrapZones[0].Occupant = prot;
                Check("Graceful Charity: cannot be activated while the opponent's Protector of the Sanctuary stops draws",
                    !engine.CanActivateSpellTrap(p, gc, fromHand: true));
            }
        }

        /// <summary>
        /// With the v142 HandDisruption batch in: a Set Appropriate only answers a draw that was the last thing to
        /// happen. Graceful Charity discards after its draw, so Appropriate misses the timing (OCG ruling); a plain
        /// Pot of Greed is the control. Idle while Appropriate is not Implemented.
        /// </summary>
        static void CrossGroupAppropriate(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            if (CardEffectStatus.Classify(db.Get(Appropriate)) != CardEffectStatusKind.Implemented)
                return;

            {
                var engine = PlayerMain(db, pDeck, aDeck, out _, new[] { Celtic },
                    new[] { Bewd, SkullServant, SummonedSkull, Celtic, Celtic });
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.RemoveAll(x => x.CardId == GracefulCharity);
                var pot = PutInHand(engine, p, PotOfGreed);
                var app = PlaceSetTrap(engine, opp, Appropriate, 0);
                engine.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(engine);
                Check("× HandDisruption (control): after Pot of Greed the AI's Set Appropriate is activated",
                    app.FaceUp, $"appropriateFaceUp={app.FaceUp} oppHand={opp.HandCount}");
            }

            {
                var engine = PlayerMain(db, pDeck, aDeck, out var gc, new[] { Celtic },
                    new[] { Bewd, SkullServant, SummonedSkull, Celtic, Celtic });
                var p = engine.Player;
                var opp = engine.Opponent;
                var app = PlaceSetTrap(engine, opp, Appropriate, 0);
                engine.TryActivateSpellTrap(p, gc, fromHand: true);
                PassAll(engine);
                var asked = engine.PendingActivation?.AwaitingDrawThenDiscard == true;
                foreach (var id in new[] { Bewd, SummonedSkull })
                {
                    var t = p.Hand.FirstOrDefault(x => x.CardId == id);
                    if (t != null) engine.TrySelectEffectTarget(t);
                }

                PassAll(engine);
                Check("× HandDisruption: Graceful Charity discards after drawing, so a Set Appropriate misses the timing",
                    asked && !engine.IsAwaitingEffectTarget && p.HandCount == 2 && !app.FaceUp && opp.HandCount == 0,
                    $"asked={asked} hand={p.HandCount} appropriateFaceUp={app.FaceUp} oppHand={opp.HandCount}");
            }
        }
    }
}
