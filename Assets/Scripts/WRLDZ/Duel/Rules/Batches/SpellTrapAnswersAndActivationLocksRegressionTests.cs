using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SpellTrapAnswersAndActivationLocks": Judgment of Anubis, Spell Vanishing, Cursed Seal of the Forbidden
    /// Spell, Trap Jammer, Sasuke Samurai #2 and Invader of Darkness — compile shapes, look-alike sentences that stay
    /// uncompiled, and mini-duels through the real Chain windows (TryActivateSpellTrap, TryActivateChainResponse,
    /// PassResponse, TrySelectEffectTarget) and activation gates (CanActivateSpellTrap). Goblin of Greed stays deferred.
    /// </summary>
    public static class SpellTrapAnswersAndActivationLocksRegressionTests
    {
        const int JudgmentOfAnubis = 55256016;
        const int SpellVanishing = 29735721;
        const int CursedSeal = 58851034;
        const int TrapJammer = 19252988;
        const int Sasuke2 = 11760174;
        const int InvaderOfDarkness = 56647086;
        const int GoblinOfGreed = 425934;

        const int MagicJammer = 77414722;
        const int SevenTools = 3819470;
        const int HeavyStorm = 19613556;
        const int Mst = 5318639;
        const int DarkHole = 53129443;
        const int PotOfGreed = 55144522;
        const int JarOfGreed = 83968380;
        const int Celtic = 91152256;      // Warrior Lv4 1400/1200
        const int MysticalElf = 15025844; // Spellcaster Lv4 800/2000

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("SpellTrapAnswersAndActivationLocks: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            AnubisDuels(db, pDeck, aDeck, Check);
            SpellVanishingDuels(db, pDeck, aDeck, Check);
            CursedSealDuels(db, pDeck, aDeck, Check);
            TrapJammerDuels(db, pDeck, aDeck, Check);
            SasukeDuels(db, pDeck, aDeck, Check);
            InvaderDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static EffectClause Only(CompiledCardProgram p) =>
            p != null && p.FullyCompiled && p.ClauseList.Count == 1 ? p.ClauseList[0] : null;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} " + string.Join(" | ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} after={c.AfterNegationAction} disc={c.RequiresDiscardCost}x{c.DiscardCostCount}" +
                    $"[{c.DiscardCostAttribute}] lp{c.PayLpAmount} bp={c.RequiresBattlePhase} cond={c.ActivationCondition}"));

        static CompiledCardProgram CompileText(string text, string type, string race, string frame) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990176, name = "Near-miss Card", type = type, frameType = frame, race = race, desc = text,
                atk = 900, def = 900, level = 3
            });

        static bool Has(string text, Func<EffectClause, bool> pred, string type, string race, string frame)
        {
            var prog = CompileText(text, type, race, frame);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && pred(c));
        }

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

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(e);
            Empty(e);
            return e;
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 12 &&
                            !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            Empty(e);
            return e;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        static int CountId(System.Collections.Generic.IEnumerable<CardInstance> pile, int id) =>
            pile.Count(c => c != null && c.CardId == id);

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            var anubis = Prog(db, JudgmentOfAnubis);
            var a = Only(anubis);
            Check("Judgment of Anubis: opponent's S/T-destroying Spell, discard 1, negate + destroy, then destroy-and-burn",
                Implemented(db, JudgmentOfAnubis) && a != null && a.Timing == EffectTiming.ChainLinkActivated &&
                a.Action == EffectActionKind.NegateActivation && a.NegateRespondsToSpell && !a.NegateRespondsToTrap &&
                a.NegateOpponentActivationOnly && a.NegateCardActivationOnly && a.NegateRequiresWouldDestroySpellTrapOnField &&
                a.RequiresDiscardCost && a.DiscardCostAttribute == "*" && a.DiscardCostCount == 1 && a.DestroyNegatedCard &&
                a.AfterNegationAction == EffectActionKind.MayDestroyOpponentFaceUpMonsterInflictItsAtk,
                Describe(anubis));

            var sv = Prog(db, SpellVanishing);
            var s = Only(sv);
            Check("Spell Vanishing: discard 2, negate + destroy a Spell, then the same-name send from hand and Deck",
                Implemented(db, SpellVanishing) && s != null && s.Timing == EffectTiming.ChainLinkActivated &&
                s.Action == EffectActionKind.NegateActivation && s.NegateRespondsToSpell && !s.NegateOpponentActivationOnly &&
                s.NegateCardActivationOnly && s.RequiresDiscardCost && s.DiscardCostCount == 2 && s.DestroyNegatedCard &&
                s.AfterNegationAction == EffectActionKind.SendNegatedCardNameFromOpponentHandAndDeckToGy,
                Describe(sv));

            var cs = Prog(db, CursedSeal);
            var c1 = Only(cs);
            Check("Cursed Seal: discard 1 Spell, negate + destroy a Spell, then the rest-of-Duel name lock",
                Implemented(db, CursedSeal) && c1 != null && c1.Timing == EffectTiming.ChainLinkActivated &&
                c1.Action == EffectActionKind.NegateActivation && c1.NegateRespondsToSpell &&
                !c1.NegateOpponentActivationOnly && c1.RequiresDiscardCost && c1.DiscardCostCount == 1 &&
                c1.DiscardCostAttribute == FaceUpQuickEffectsRuntime.DiscardSpellCard && c1.DestroyNegatedCard &&
                c1.AfterNegationAction == EffectActionKind.LockOpponentActivatingNegatedSpellNameForDuel,
                Describe(cs));

            var tj = Prog(db, TrapJammer);
            var t = Only(tj);
            Check("Trap Jammer: the opponent's Trap activation in the Battle Phase, negate + destroy, no cost",
                Implemented(db, TrapJammer) && t != null && t.Timing == EffectTiming.ChainLinkActivated &&
                t.Action == EffectActionKind.NegateActivation && t.NegateRespondsToTrap && !t.NegateRespondsToSpell &&
                t.NegateOpponentActivationOnly && t.NegateCardActivationOnly && t.RequiresBattlePhase &&
                t.DestroyNegatedCard && !t.RequiresDiscardCost && t.PayLpAmount == 0 &&
                t.AfterNegationAction == EffectActionKind.None,
                Describe(tj));

            var sk = Prog(db, Sasuke2);
            var k = Only(sk);
            Check("Sasuke Samurai #2: ignition, pay 800 LP, a player flag, both players' Spell/Trap lock this turn",
                Implemented(db, Sasuke2) && k != null && k.Timing == EffectTiming.Activate &&
                k.Action == EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn && k.PayLpAmount == 800 &&
                k.ActivationCondition == SpellTrapAnswersAndActivationLocksRuntime.TurnLockNotAppliedByYouThisTurn,
                Describe(sk));

            var inv = Prog(db, InvaderOfDarkness);
            var i = Only(inv);
            Check("Invader of Darkness: a continuous Quick-Play lock on the opponent",
                Implemented(db, InvaderOfDarkness) && i != null && i.Timing == EffectTiming.ContinuousWhileFaceUp &&
                i.Action == EffectActionKind.ContinuousOpponentCannotActivateQuickPlaySpells && !i.MakesChainLink,
                Describe(inv));

            // LocksRedirectsAndActivity batch: Goblin of Greed is compiled now (the discard-cost lock every activation site
            // asks); this guard pins that compiled shape.
            var gob = Prog(db, GoblinOfGreed);
            var g = Only(gob);
            Check("Goblin of Greed (LocksRedirectsAndActivity batch): a continuous both-player discard-cost lock",
                gob != null && gob.FullyCompiled && Implemented(db, GoblinOfGreed) && g != null &&
                g.Timing == EffectTiming.ContinuousWhileFaceUp &&
                g.Action == EffectActionKind.ContinuousCannotDiscardAsCost && g.Side == EffectSide.Both,
                Describe(gob));
        }

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            var tjText = db.Get(TrapJammer)?.desc ?? "";
            Check("Near miss: Trap Jammer's text compiles (control)",
                Has(tjText, c => c.RequiresBattlePhase && c.NegateRespondsToTrap, "Trap Card", "Counter", "trap"));
            Check("Near miss: 'during the Main Phase' is not Trap Jammer's template",
                !Has(tjText.Replace("Battle Phase", "Main Phase"), c => c.RequiresBattlePhase, "Trap Card", "Counter",
                    "trap"));

            var anubisText = db.Get(JudgmentOfAnubis)?.desc ?? "";
            Check("Near miss: Judgment of Anubis's text compiles (control)",
                Has(anubisText, c => c.NegateRequiresWouldDestroySpellTrapOnField, "Trap Card", "Counter", "trap"));
            Check("Near miss: without its destroy-and-burn rider it is not Judgment of Anubis's template",
                !Has(anubisText.Substring(0, Math.Max(0, anubisText.IndexOf(", then you can", StringComparison.Ordinal))) + ".",
                    c => c.NegateRequiresWouldDestroySpellTrapOnField, "Trap Card", "Counter", "trap"));
            Check("Near miss: 'would destroy a monster(s)' is not Judgment of Anubis's template",
                !Has(anubisText.Replace("a Spell/Trap Card(s) on the field", "a monster(s) on the field"),
                    c => c.NegateRequiresWouldDestroySpellTrapOnField ||
                         c.AfterNegationAction == EffectActionKind.MayDestroyOpponentFaceUpMonsterInflictItsAtk,
                    "Trap Card", "Counter", "trap"));

            var csText = db.Get(CursedSeal)?.desc ?? "";
            Check("Near miss: Cursed Seal's text compiles (control)",
                Has(csText, c => c.AfterNegationAction == EffectActionKind.LockOpponentActivatingNegatedSpellNameForDuel,
                    "Trap Card", "Counter", "trap"));
            Check("Near miss: a lock 'until the end of this turn' is not Cursed Seal's template",
                !Has(csText.Replace("for the rest of this Duel", "until the end of this turn"),
                    c => c.AfterNegationAction == EffectActionKind.LockOpponentActivatingNegatedSpellNameForDuel,
                    "Trap Card", "Counter", "trap"));

            var svText = db.Get(SpellVanishing)?.desc ?? "";
            Check("Near miss: Spell Vanishing's text compiles (control)",
                Has(svText, c => c.AfterNegationAction == EffectActionKind.SendNegatedCardNameFromOpponentHandAndDeckToGy,
                    "Trap Card", "Counter", "trap"));
            Check("Near miss: 'look at your opponent's hand' only is not Spell Vanishing's template",
                !Has(svText.Replace("hand and Deck", "hand"),
                    c => c.AfterNegationAction == EffectActionKind.SendNegatedCardNameFromOpponentHandAndDeckToGy,
                    "Trap Card", "Counter", "trap"));

            var skText = db.Get(Sasuke2)?.desc ?? "";
            Check("Near miss: Sasuke Samurai #2's text compiles (control)",
                Has(skText, c => c.Action == EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn,
                    "Effect Monster", "Warrior", "effect"));
            Check("Near miss: 'Spell Cards cannot be activated' only is not Sasuke Samurai #2's template",
                !Has(skText.Replace("Spell and Trap Cards", "Spell Cards"),
                    c => c.Action == EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn,
                    "Effect Monster", "Warrior", "effect"));

            var invText = db.Get(InvaderOfDarkness)?.desc ?? "";
            Check("Near miss: Invader of Darkness's text compiles (control)",
                Has(invText, c => c.Action == EffectActionKind.ContinuousOpponentCannotActivateQuickPlaySpells,
                    "Effect Monster", "Fiend", "effect"));
            Check("Near miss: 'Neither player can activate Quick-Play Spell Cards' is not Invader of Darkness's template",
                !Has(invText.Replace("Your opponent cannot", "Neither player can"),
                    c => c.Action == EffectActionKind.ContinuousOpponentCannotActivateQuickPlaySpells,
                    "Effect Monster", "Fiend", "effect"));
        }

        // ───────────────────────── Judgment of Anubis ─────────────────────────

        static void AnubisDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Heavy Storm would destroy Anubis itself: answered; then destroy the opponent's Celtic and burn 1400.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var anubis = PlaceSetTrap(e, p, JudgmentOfAnubis, 0);
                var fodder = PutInHand(e, p, MysticalElf);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var storm = PutInHand(e, o, HeavyStorm);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, storm, fromHand: true);
                var offered = Offers(e, p, anubis);
                var answered = offered && e.TryActivateChainResponse(p, anubis);
                PassAll(e);
                var asked = e.IsAwaitingEffectTarget;
                var picked = asked && e.TrySelectEffectTarget(celtic);
                PassAll(e);
                Check("Judgment of Anubis: negates and destroys Heavy Storm, then destroys Celtic and burns its 1400 ATK",
                    act && offered && answered && asked && picked && o.Graveyard.Contains(storm) &&
                    o.Graveyard.Contains(celtic) && !OnField(o, celtic) && o.LifePoints == lp - 1400 &&
                    p.Graveyard.Contains(fodder) && p.Graveyard.Contains(anubis) && e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} asked={asked} picked={picked} lp {lp}->{o.LifePoints} " +
                    State(e));
            }

            {
                // "you can": declined — no destruction, no damage, the Chain finishes.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var anubis = PlaceSetTrap(e, p, JudgmentOfAnubis, 0);
                PutInHand(e, p, MysticalElf);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var storm = PutInHand(e, o, HeavyStorm);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, storm, fromHand: true);
                var answered = Offers(e, p, anubis) && e.TryActivateChainResponse(p, anubis);
                PassAll(e);
                var asked = e.IsAwaitingEffectTarget;
                var declined = asked && e.CancelEffectTargeting();
                PassAll(e);
                Check("Judgment of Anubis: declining the optional destroy leaves the monster and LP alone",
                    act && answered && asked && declined && OnField(o, celtic) && o.LifePoints == lp &&
                    o.Graveyard.Contains(storm) && e.Chain.Count == 0 && !e.IsAwaitingEffectTarget,
                    $"act={act} answered={answered} asked={asked} declined={declined} {State(e)}");
            }

            {
                // MST targeting a Set card: the targeted path counts the recorded target.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var anubis = PlaceSetTrap(e, p, JudgmentOfAnubis, 0);
                PlaceSetTrap(e, p, JarOfGreed, 1);
                PutInHand(e, p, MysticalElf);
                var mst = PutInHand(e, o, Mst);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, mst, fromHand: true);
                Check("Judgment of Anubis: offered against MST targeting a Set Spell/Trap",
                    act && Offers(e, p, anubis), State(e));
                PassAll(e);
            }

            {
                // Dark Hole destroys monsters only: not offered (Magic Jammer is, as a control).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var anubis = PlaceSetTrap(e, p, JudgmentOfAnubis, 0);
                var jammer = PlaceSetTrap(e, p, MagicJammer, 1);
                PutInHand(e, p, MysticalElf);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var hole = PutInHand(e, o, DarkHole);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, hole, fromHand: true);
                Check("Judgment of Anubis: not offered against a Spell that destroys only monsters (Dark Hole)",
                    act && Offers(e, p, jammer) && !Offers(e, p, anubis), State(e));
                PassAll(e);
            }

            {
                // No card to discard: not offered.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var anubis = PlaceSetTrap(e, p, JudgmentOfAnubis, 0);
                var tools = PlaceSetTrap(e, p, SevenTools, 1);
                var storm = PutInHand(e, o, HeavyStorm);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, storm, fromHand: true);
                Check("Judgment of Anubis: not offered with no card in hand to discard",
                    act && !Offers(e, p, anubis), State(e) + $" tools={tools.Name}");
                PassAll(e);
            }
        }

        // ───────────────────────── Spell Vanishing ─────────────────────────

        static void SpellVanishingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sv = PlaceSetTrap(e, p, SpellVanishing, 0);
                var d1 = PutInHand(e, p, Celtic);
                var d2 = PutInHand(e, p, MysticalElf);
                var keep = PutInHand(e, p, Celtic);
                var pot = PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, Celtic);
                o.Deck.Add(PotOfGreed);
                o.Deck.Add(PotOfGreed);
                e.NotifyPublic();
                var deckBefore = o.DeckCount;
                var deckPots = o.Deck.Count(id => id == PotOfGreed); // the 2 added, plus any the opponent's Deck holds
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = Offers(e, p, sv);
                var answered = offered && e.TryActivateChainResponse(p, sv);
                // A human picks the two discards one by one.
                var first = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(d1);
                var second = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(d2);
                PassAll(e);
                Check("Spell Vanishing: discards 2, negates Pot of Greed, sends every Pot of Greed in hand and Deck to the GY",
                    act && offered && answered && first && second && p.Graveyard.Contains(d1) &&
                    p.Graveyard.Contains(d2) && p.Hand.Contains(keep) && p.Graveyard.Contains(sv) &&
                    CountId(o.Graveyard, PotOfGreed) == 2 + deckPots && CountId(o.Hand, PotOfGreed) == 0 &&
                    !o.Deck.Contains(PotOfGreed) && o.DeckCount == deckBefore - deckPots && o.Hand.Count == 1 &&
                    e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} first={first} second={second} " +
                    $"oGy={CountId(o.Graveyard, PotOfGreed)} deck {deckBefore}->{o.DeckCount} {State(e)}");
            }

            {
                // Cancel at the second discard: not activated, nothing discarded.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sv = PlaceSetTrap(e, p, SpellVanishing, 0);
                var d1 = PutInHand(e, p, Celtic);
                PutInHand(e, p, MysticalElf);
                PutInHand(e, p, Celtic);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var answered = Offers(e, p, sv) && e.TryActivateChainResponse(p, sv);
                var first = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(d1);
                var cancelled = e.IsAwaitingEffectTarget && e.CancelEffectTargeting();
                var stillSet = p.TryFindSpellTrap(sv, out _) && !sv.FaceUp;
                Check("Spell Vanishing: Cancel at the second discard activates nothing and discards nothing",
                    act && answered && first && cancelled && stillSet && p.Hand.Count == 3 && p.Graveyard.Count == 0,
                    $"first={first} cancelled={cancelled} stillSet={stillSet} hand={p.Hand.Count} {State(e)}");
                PassAll(e);
            }

            {
                // One card in hand: not offered (Magic Jammer is).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var sv = PlaceSetTrap(e, p, SpellVanishing, 0);
                var jammer = PlaceSetTrap(e, p, MagicJammer, 1);
                PutInHand(e, p, Celtic);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                Check("Spell Vanishing: not offered with only 1 card in hand",
                    act && Offers(e, p, jammer) && !Offers(e, p, sv), State(e));
                PassAll(e);
            }
        }

        // ───────────────────────── Cursed Seal of the Forbidden Spell ─────────────────────────

        static void CursedSealDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var seal = PlaceSetTrap(e, p, CursedSeal, 0);
                PlaceSetTrap(e, p, JarOfGreed, 2);
                var monster = PutInHand(e, p, Celtic);
                var spell = PutInHand(e, p, DarkHole);
                var myPot = PutInHand(e, p, PotOfGreed);
                var pot = PutInHand(e, o, PotOfGreed);
                var pot2 = PutInHand(e, o, PotOfGreed);
                var storm = PutInHand(e, o, HeavyStorm);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = Offers(e, p, seal);
                var answered = offered && e.TryActivateChainResponse(p, seal);
                // Two Spells in hand: a human picks the one to discard.
                var picked = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(spell);
                PassAll(e);
                var potLocked = !e.CanActivateSpellTrap(o, pot2, fromHand: true) &&
                                ContinuousNegationRuntime.ActivationLocked(e, o, pot2, out _);
                var stormFree = e.CanActivateSpellTrap(o, storm, fromHand: true);
                var mineFree = !ContinuousNegationRuntime.ActivationLocked(e, p, myPot, out _);
                Check("Cursed Seal: discards a Spell, negates and destroys Pot of Greed; the opponent can no longer " +
                      "activate \"Pot of Greed\" (other Spells and the controller are not locked)",
                    act && offered && answered && picked && p.Graveyard.Contains(spell) && p.Hand.Contains(monster) &&
                    o.Graveyard.Contains(pot) && p.Graveyard.Contains(seal) && potLocked && stormFree && mineFree &&
                    e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} picked={picked} potLocked={potLocked} " +
                    $"stormFree={stormFree} mineFree={mineFree} {State(e)}");
            }

            {
                // No Spell in hand: not offered (Magic Jammer is).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var seal = PlaceSetTrap(e, p, CursedSeal, 0);
                var jammer = PlaceSetTrap(e, p, MagicJammer, 1);
                PutInHand(e, p, Celtic);
                PutInHand(e, p, MysticalElf);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                Check("Cursed Seal: not offered with no Spell in hand",
                    act && Offers(e, p, jammer) && !Offers(e, p, seal), State(e));
                PassAll(e);
            }

            {
                // The AI answers the player's Pot of Greed and discards its Spell, never its monster.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var seal = PlaceSetTrap(e, o, CursedSeal, 0);
                var oMonster = PutInHand(e, o, Celtic);
                var oSpell = PutInHand(e, o, DarkHole);
                var pot = PutInHand(e, p, PotOfGreed);
                var pot2 = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(p, pot, fromHand: true);
                if (e.PendingResponse != null && e.PendingResponse.Responder == o && Offers(e, o, seal))
                    e.TryActivateChainResponse(o, seal);
                PassAll(e);
                var answered = o.Graveyard.Contains(seal);
                Check("Cursed Seal (AI): discards its Spell, not its monster; the player's Pot of Greed is then locked",
                    act && answered && o.Graveyard.Contains(oSpell) && o.Hand.Contains(oMonster) &&
                    p.Graveyard.Contains(pot) && !e.CanActivateSpellTrap(p, pot2, fromHand: true),
                    $"act={act} answered={answered} {State(e)}");
            }
        }

        // ───────────────────────── Trap Jammer ─────────────────────────

        static void TrapJammerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jammer = PlaceSetTrap(e, p, TrapJammer, 0);
                var jar = PlaceSetTrap(e, o, JarOfGreed, 0);
                e.NotifyPublic();
                var hand = o.Hand.Count;
                var act = reached && e.TryActivateSpellTrap(o, jar, fromHand: false);
                var offered = Offers(e, p, jammer);
                var answered = offered && e.TryActivateChainResponse(p, jammer);
                PassAll(e);
                Check("Trap Jammer: negates and destroys the opponent's Trap activated in the Battle Phase",
                    act && offered && answered && o.Graveyard.Contains(jar) && o.Hand.Count == hand &&
                    p.Graveyard.Contains(jammer) && e.Chain.Count == 0,
                    $"act={act} offered={offered} answered={answered} hand {hand}->{o.Hand.Count} {State(e)}");
            }

            {
                // The Main Phase: not offered (Seven Tools is).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jammer = PlaceSetTrap(e, p, TrapJammer, 0);
                var tools = PlaceSetTrap(e, p, SevenTools, 1);
                var jar = PlaceSetTrap(e, o, JarOfGreed, 0);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, jar, fromHand: false);
                Check("Trap Jammer: not offered outside the Battle Phase",
                    act && Offers(e, p, tools) && !Offers(e, p, jammer), State(e));
                PassAll(e);
            }
        }

        // ───────────────────────── Sasuke Samurai #2 ─────────────────────────

        static void SasukeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var sasuke = PlaceMonster(e, p, Sasuke2, 0, BattlePosition.Attack, true);
            var sasuke2 = PlaceMonster(e, p, Sasuke2, 1, BattlePosition.Attack, true);
            var pot = PutInHand(e, p, PotOfGreed);
            var oJar = PlaceSetTrap(e, o, JarOfGreed, 0);
            e.NotifyPublic();
            var before = e.CanActivateSpellTrap(p, pot, fromHand: true);
            var lp = p.LifePoints;
            var act = reached && e.CanActivateSpellTrap(p, sasuke, fromHand: false) &&
                      e.TryActivateSpellTrap(p, sasuke, fromHand: false);
            PassAll(e);
            var paid = p.LifePoints == lp - 800;
            var potLocked = !e.CanActivateSpellTrap(p, pot, fromHand: true);
            var jarLocked = ContinuousNegationRuntime.ActivationLocked(e, o, oJar, out _);
            var secondCopy = e.CanActivateSpellTrap(p, sasuke2, fromHand: false);
            Check("Sasuke Samurai #2: pays 800 LP; neither player can activate Spell/Trap Cards; once per turn for the player",
                before && act && paid && potLocked && jarLocked && !secondCopy,
                $"before={before} act={act} lp {lp}->{p.LifePoints} potLocked={potLocked} jarLocked={jarLocked} " +
                $"secondCopy={secondCopy} {State(e)}");

            // The next turn: the lock is over.
            for (var i = 0; i < 4 && e.TurnPlayer == p; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(p);
            }

            var over = e.TurnPlayer == o && !ContinuousNegationRuntime.ActivationLocked(e, o, oJar, out _) &&
                       !LocksAndZonesRuntime.SpellTrapActivationLocked(e, oJar, out _);
            Check("Sasuke Samurai #2: the lock ends with the turn", over, State(e));
        }

        // ───────────────────────── Invader of Darkness ─────────────────────────

        static void InvaderDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var invader = PlaceMonster(e, o, InvaderOfDarkness, 0, BattlePosition.Attack, true);
            PlaceSetTrap(e, o, JarOfGreed, 0);
            var mst = PutInHand(e, p, Mst);
            var pot = PutInHand(e, p, PotOfGreed);
            var setMst = PlaceSetTrap(e, p, Mst, 0);
            var oMst = PutInHand(e, o, Mst);
            e.NotifyPublic();
            var handLocked = !e.CanActivateSpellTrap(p, mst, fromHand: true);
            var setLocked = !e.CanActivateSpellTrap(p, setMst, fromHand: false);
            var potFree = e.CanActivateSpellTrap(p, pot, fromHand: true);
            var ownerFree = !ContinuousNegationRuntime.ActivationLocked(e, o, oMst, out _);
            invader.FaceUp = false;
            invader.Position = BattlePosition.Defense;
            e.NotifyPublic();
            var faceDownFree = e.CanActivateSpellTrap(p, mst, fromHand: true);
            Check("Invader of Darkness: the opponent cannot activate Quick-Play Spells (hand or Set); Normal Spells, its " +
                  "controller and a face-down Invader do not lock",
                reached && handLocked && setLocked && potFree && ownerFree && faceDownFree,
                $"handLocked={handLocked} setLocked={setLocked} potFree={potFree} ownerFree={ownerFree} " +
                $"faceDownFree={faceDownFree} {State(e)}");
        }
    }
}
