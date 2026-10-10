using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// EngineResiduals (v163): the open residuals of ENGINE_MAP section 4, checked against the ygopro scripts.
    /// <list type="bullet">
    /// <item>Magic Jammer c77414722: a human chooses the discard (Counter Trap chain answer); Cancel activates nothing.</item>
    /// <item>Raigeki Break c4178474: the activating card is not a legal target (Lua IsExistingTarget(…, e:GetHandler()));
    /// its discard cost no longer keeps it out of a chain window.</item>
    /// <item>Offerings to the Doomed c19230407: "Target 1 face-up monster" (Lua Card.IsFaceup at target and resolution).</item>
    /// <item>Book of Taiyou c38699854: the AI flips its own "flipped face-up" trigger monster (Royal Keeper) first.</item>
    /// <item>The Unfriendly Amazon c65475294 + Blind Destruction c32015116: a Dice Re-Roll prompt in the Standby Phase holds
    /// it, so the Tribute upkeep is still asked.</item>
    /// <item>Abyss Soldier c18318842: Cancel at a monster ignition's cost / target pick undoes the activation.</item>
    /// </list>
    /// </summary>
    public static class EngineResidualsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int MagicJammer = 77414722;
        const int RaigekiBreak = 4178474;
        const int Offerings = 19230407;
        const int BookOfTaiyou = 38699854;
        const int UnfriendlyAmazon = 65475294;
        const int BlindDestruction = 32015116;
        const int DiceReRoll = 83241722;
        const int AbyssSoldier = 18318842;
        const int SpellPurification = 1669772; // Normal Trap: Discard 1 card; destroy all face-up Continuous Spells

        const int RoyalKeeper = 16509093;     // When this card is flipped face-up: …
        const int Celtic = 91152256;          // Warrior Lv4 1400/1200
        const int MysticalElf = 15025844;     // Spellcaster Lv4 800/2000
        const int DarkBlade = 11321183;       // Warrior Lv4 1800/1500
        const int FeralImp = 41392891;        // Fiend Lv4 1300/1400
        const int Kuriboh = 40640057;
        const int FiendKraken = 77456781;     // WATER Aqua Lv4
        const int SevenColoredFish = 23771716; // WATER Fish Lv4
        const int PotOfGreed = 55144522;
        const int DianKeto = 84257640;
        const int MessengerOfPeace = 44656491; // Continuous Spell
        const int MysticalSpaceTyphoon = 5318639;
        const int MicroRay = 18190572;         // Normal Trap: Target 1 face-up monster on the field; its DEF becomes 0

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("EngineResiduals: " + name, ok, detail ?? "");

            Shapes(db, Check);

            RaigekiBreakNeverTargetsItself(db, pDeck, aDeck, Check);
            OfferingsTargetsFaceUpOnly(db, pDeck, aDeck, Check);
            OfferingsChecksFaceUpAtResolution(db, pDeck, aDeck, Check);
            BookOfTaiyouAiFlipsItsTriggerMonster(db, pDeck, aDeck, Check);

            MagicJammerHumanPicksTheDiscard(db, pDeck, aDeck, Check);
            MagicJammerCancelActivatesNothing(db, pDeck, aDeck, Check);

            RaigekiBreakChainsToALink(db, pDeck, aDeck, Check);
            RaigekiBreakChainCancels(db, pDeck, aDeck, Check);
            SpellPurificationAiChainsWithAutoDiscard(db, pDeck, aDeck, Check);

            MonsterIgnitionCancelUndoes(db, pDeck, aDeck, Check);

            DiceReRollHoldsStandbyForTheUpkeep(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool hvh = false)
        {
            var e = new DuelEngine { HumanVsHuman = hvh };
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        /// <summary>The opponent's (AI's) Main Phase 1, boards, hands and GYs empty.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached, bool hvh = false)
        {
            var e = new DuelEngine { HumanVsHuman = hvh };
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            reached = ReachOpponentMain(e);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        /// <summary>The player's Main Phase 1 on turn 3+, boards, hands and GYs empty.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = new DuelEngine();
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            for (var t = 0; t < 10 &&
                            !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static void EndTurn(DuelEngine e)
        {
            if (e.IsAwaitingResponse) e.PassResponse();
            e.RecoverStuckCombat();
            e.TryEndTurnSafe(e.TurnPlayer);
            if (e.IsAwaitingResponse) e.PassResponse();
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static CardInstance FaceUpSt(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static bool OnField(DuelistState who, CardInstance c) =>
            c != null && (who.TryFindMonster(c, out _) || who.TryFindSpellTrap(c, out _));

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse}/{e.PendingResponse?.Responder?.Name} links={e.Chain.Count} " +
            $"phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static bool MyChainWindow(DuelEngine e, DuelistState who) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            e.PendingResponse.Responder == who;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Chk Check)
        {
            var off = CardTextEffectCompiler.Compile(db.Get(Offerings));
            var c0 = off?.ClauseList?.ElementAtOrDefault(0);
            var c1 = off?.ClauseList?.ElementAtOrDefault(1);
            Check("Offerings to the Doomed: a face-up monster target on either field (destroy) + skip your next Draw Phase",
                off != null && off.FullyCompiled && off.ClauseList.Count == 2 && c0 != null &&
                c0.Action == EffectActionKind.Destroy && c0.Zone == EffectZoneFilter.FieldAnyMonster &&
                c0.RequiresTargetChoice && c0.RequiresTargetFaceUp && c1 != null &&
                c1.Action == EffectActionKind.SkipControllerNextDrawPhases && c1.Amount == 1 &&
                CardEffectStatus.Classify(db.Get(Offerings)) == CardEffectStatusKind.Implemented,
                $"c0={EffectVocabulary.Describe(c0)} faceUp={c0?.RequiresTargetFaceUp} c1={EffectVocabulary.Describe(c1)}");

            var rb = CardTextEffectCompiler.Compile(db.Get(RaigekiBreak));
            var rbc = rb?.ClauseList?.FirstOrDefault();
            Check("Raigeki Break: discard 1 (cost) → destroy 1 targeted card on the field; a chainable discard cost",
                rb != null && rb.FullyCompiled && rbc != null && rbc.Action == EffectActionKind.Destroy &&
                rbc.Zone == EffectZoneFilter.AnyCardOnField && rbc.RequiresDiscardCost &&
                EngineResidualsRuntime.IsChainableDiscardCost(rbc),
                EffectVocabulary.Describe(rbc));

            // Near misses: other costs stay out of the chain window.
            Check("Near miss: a 2-card or random discard, or a discard plus an LP cost, is not a chainable discard cost",
                !EngineResidualsRuntime.IsChainableDiscardCost(new EffectClause
                    { RequiresDiscardCost = true, DiscardCostCount = 2 }) &&
                !EngineResidualsRuntime.IsChainableDiscardCost(new EffectClause
                    { RequiresDiscardCost = true, DiscardCostRandom = true }) &&
                !EngineResidualsRuntime.IsChainableDiscardCost(new EffectClause
                    { RequiresDiscardCost = true, PayLpAmount = 500 }) &&
                EngineResidualsRuntime.IsChainableDiscardCost(new EffectClause
                    { RequiresDiscardCost = true, DiscardCostAttribute = "*" }));

            Check("Near miss: a monster's own \"card on the field\" target may include itself (Abyss Soldier, Lua nil); " +
                  "a Spell/Trap activation's may not",
                !TextEffectRuntime.SpellTrapActivationExcludesItself(
                    new EffectClause { Timing = EffectTiming.Activate }, new CardInstance { Def = db.Get(AbyssSoldier) }) &&
                TextEffectRuntime.SpellTrapActivationExcludesItself(
                    new EffectClause { Timing = EffectTiming.Activate }, new CardInstance { Def = db.Get(RaigekiBreak) }) &&
                !TextEffectRuntime.SpellTrapActivationExcludesItself(
                    new EffectClause { Timing = EffectTiming.SentFromFieldToGy }, new CardInstance { Def = db.Get(RaigekiBreak) }));

            foreach (var id in new[] { MagicJammer, RaigekiBreak, Offerings, BookOfTaiyou, UnfriendlyAmazon, BlindDestruction })
                Check($"Acceptance card stays Implemented: {db.Get(id)?.name}",
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented);
        }

        // ───────────────────────────── Raigeki Break: not itself ─────────────────────────────

        static void RaigekiBreakNeverTargetsItself(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
            var rb = PlaceSetTrap(e, p, RaigekiBreak, 0);
            var mySet = PlaceSetTrap(e, p, PotOfGreed, 1);
            var fodder = PutInHand(e, p, Celtic);
            var act = e.TryActivateSpellTrap(p, rb, fromHand: false);
            List<CardInstance> targets = null;
            for (var i = 0; i < 6 && e.IsAwaitingEffectTarget; i++)
            {
                var legal = Legal(e);
                if (legal.Contains(fodder))
                {
                    e.TrySelectEffectTarget(fodder);
                    continue;
                }

                targets = legal;
                e.TrySelectEffectTarget(blade);
            }

            PassAll(e);
            Check("Raigeki Break: its own card is not offered as the target (another Set card of yours is); the target is " +
                  "destroyed",
                reached && act && targets != null && !targets.Contains(rb) && targets.Contains(mySet) &&
                targets.Contains(blade) && !OnField(o, blade) && p.Graveyard.Contains(fodder) && p.Graveyard.Contains(rb),
                $"reached={reached} act={act} targets={Names(targets)} blade={OnField(o, blade)} {State(e)}");

            // Cancel at the target, after the discard: the activation never happened (the discard comes back).
            var e3 = PlayerMain1(db, pDeck, aDeck, out _);
            var p3 = e3.Player;
            var blade3 = PlaceMonster(e3, e3.Opponent, DarkBlade, 0, BattlePosition.Attack, true);
            var rb3 = PlaceSetTrap(e3, p3, RaigekiBreak, 0);
            var fodder3 = PutInHand(e3, p3, Celtic);
            PutInHand(e3, p3, Kuriboh);
            var act3 = e3.TryActivateSpellTrap(p3, rb3, fromHand: false);
            var paid3 = act3 && Legal(e3).Contains(fodder3) && e3.TrySelectEffectTarget(fodder3);
            var atTarget3 = Legal(e3).Contains(blade3);
            e3.CancelEffectTargeting();
            Check("Raigeki Break: Cancel at the target after the discard — the discarded card returns to the hand, the " +
                  "card is Set again, no Chain Link",
                paid3 && atTarget3 && p3.Hand.Contains(fodder3) && !p3.Graveyard.Contains(fodder3) && !rb3.FaceUp &&
                p3.TryFindSpellTrap(rb3, out _) && !e3.Chain.HasLinks && OnField(e3.Opponent, blade3),
                $"act={act3} paid={paid3} atTarget={atTarget3} hand={Names(p3.Hand)} {State(e3)}");

            // Lone target besides itself: still activatable, and only that card is offered.
            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            var rb2 = PlaceSetTrap(e2, e2.Player, RaigekiBreak, 0);
            PutInHand(e2, e2.Player, Celtic);
            Check("Raigeki Break: with no other card on the field it cannot be activated (itself is no target)",
                !e2.CanActivateSpellTrap(e2.Player, rb2, fromHand: false), State(e2));
        }

        // ───────────────────────────── Offerings to the Doomed ─────────────────────────────

        static void OfferingsTargetsFaceUpOnly(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
            var set = PlaceMonster(e, o, FeralImp, 1, BattlePosition.Defense, false);
            var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var off = PutInHand(e, p, Offerings);
            var act = e.TryActivateSpellTrap(p, off, fromHand: true);
            var targets = Legal(e);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(blade);
            PassAll(e);
            Check("Offerings to the Doomed: only face-up monsters are targets (yours too, not the Set one); the target is " +
                  "destroyed and your next Draw Phase is skipped",
                reached && act && targets.Contains(blade) && targets.Contains(mine) && !targets.Contains(set) &&
                !OnField(o, blade) && OnField(o, set) && (p.SkipNextDrawPhase || p.SkipNextDrawPhaseCount > 0) &&
                p.Graveyard.Contains(off),
                $"act={act} targets={Names(targets)} blade={OnField(o, blade)} skip={p.SkipNextDrawPhase}/" +
                $"{p.SkipNextDrawPhaseCount} {State(e)}");

            // Sibling: Micro Ray ("Target 1 face-up monster on the field") — the player chooses the target as it is activated.
            var e4 = PlayerMain1(db, pDeck, aDeck, out _);
            var elf4 = PlaceMonster(e4, e4.Opponent, MysticalElf, 0, BattlePosition.Defense, true);
            var blade4 = PlaceMonster(e4, e4.Opponent, DarkBlade, 1, BattlePosition.Attack, true);
            var set4 = PlaceMonster(e4, e4.Opponent, FeralImp, 2, BattlePosition.Defense, false);
            var ray = PlaceSetTrap(e4, e4.Player, MicroRay, 0);
            var act4 = e4.TryActivateSpellTrap(e4.Player, ray, fromHand: false);
            var targets4 = Legal(e4);
            var picked4 = targets4.Contains(elf4) && e4.TrySelectEffectTarget(elf4);
            PassAll(e4);
            Check("Micro Ray: with two face-up monsters the player picks the target at activation (Sets are not offered)",
                act4 && picked4 && targets4.Contains(blade4) && !targets4.Contains(set4) && elf4.CurrentDef == 0 &&
                blade4.CurrentDef == 1500,
                $"act={act4} targets={Names(targets4)} elf={elf4.CurrentDef} blade={blade4.CurrentDef} {State(e4)}");

            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            PlaceMonster(e2, e2.Opponent, FeralImp, 0, BattlePosition.Defense, false);
            var off2 = PutInHand(e2, e2.Player, Offerings);
            Check("Offerings to the Doomed: only a face-down monster on the field — cannot activate",
                !e2.CanActivateSpellTrap(e2.Player, off2, fromHand: true), State(e2));
        }

        static void OfferingsChecksFaceUpAtResolution(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Human vs human: the opponent's chain window stays open after the target is chosen; the target is turned
            // face-down there (as Book of Moon would), so the destruction does not happen. The Draw Phase skip still does.
            var e = FreshCleared(db, pDeck, aDeck, hvh: true);
            var p = e.Player;
            var o = e.Opponent;
            var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
            PlaceSetTrap(e, o, MysticalSpaceTyphoon, 0); // a legal answer keeps the opponent's window open
            var off = PutInHand(e, p, Offerings);
            var act = e.TryActivateSpellTrap(p, off, fromHand: true);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(blade);
            var window = MyChainWindow(e, o);
            blade.FaceUp = false;
            blade.Position = BattlePosition.Defense;
            PassAll(e);
            Check("Offerings to the Doomed: a target turned face-down before resolution is not destroyed; the Draw " +
                  "Phase is still skipped",
                act && window && OnField(o, blade) && (p.SkipNextDrawPhase || p.SkipNextDrawPhaseCount > 0) &&
                !e.Chain.HasLinks,
                $"act={act} window={window} blade={OnField(o, blade)} skip={p.SkipNextDrawPhase}/" +
                $"{p.SkipNextDrawPhaseCount} {State(e)}");
        }

        // ───────────────────────────── Book of Taiyou (AI) ─────────────────────────────

        static void BookOfTaiyouAiFlipsItsTriggerMonster(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var yourSet = PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, false);
            var keeper = PlaceMonster(e, o, RoyalKeeper, 0, BattlePosition.Defense, false);
            var book = PutInHand(e, o, BookOfTaiyou);
            var act = e.TryActivateSpellTrap(o, book, fromHand: true);
            PassAll(e);
            Check("Book of Taiyou (AI): flips its own Royal Keeper (\"when flipped face-up\" trigger) before the opponent's Set",
                reached && act && keeper.FaceUp && keeper.Position == BattlePosition.Attack && !yourSet.FaceUp &&
                SpiritMonstersRuntime.HasFlippedFaceUpTrigger(keeper),
                $"reached={reached} act={act} keeper={keeper.FaceUp}/{keeper.Position} yours={yourSet.FaceUp} {State(e)}");
        }

        // ───────────────────────────── Magic Jammer (human discard) ─────────────────────────────

        static void MagicJammerHumanPicksTheDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var jam = PlaceSetTrap(e, p, MagicJammer, 0);
            var celtic = PutInHand(e, p, Celtic);
            var kuriboh = PutInHand(e, p, Kuriboh);
            var dian = PutInHand(e, o, DianKeto);
            var oLp = o.LifePoints;
            var act = e.TryActivateSpellTrap(o, dian, fromHand: true);
            var window = MyChainWindow(e, p);
            var answered = window && e.TryActivateChainResponse(p, jam);
            var choices = Legal(e);
            var asked = e.IsAwaitingEffectTarget && choices.Contains(celtic) && choices.Contains(kuriboh) &&
                        !jam.FaceUp;
            // The player keeps the lower-value card: Celtic Guardian goes, Kuriboh stays (the AI would discard Kuriboh).
            var picked = asked && e.TrySelectEffectTarget(celtic);
            PassAll(e);
            Check("Magic Jammer (human): the player chooses the discard; the Spell is negated and destroyed",
                reached && act && window && answered && asked && picked && p.Graveyard.Contains(celtic) &&
                p.Hand.Contains(kuriboh) && o.LifePoints == oLp && o.Graveyard.Contains(dian) &&
                p.Graveyard.Contains(jam) && !e.Chain.HasLinks && e.PendingActivation == null,
                $"act={act} window={window} answered={answered} asked={asked} choices={Names(choices)} picked={picked} " +
                $"celtic={p.Graveyard.Contains(celtic)} kuriboh={p.Hand.Contains(kuriboh)} oLp={o.LifePoints} {State(e)}");

            // A lone card in hand is discarded with no prompt.
            var e2 = OpponentMain(db, pDeck, aDeck, out _);
            var jam2 = PlaceSetTrap(e2, e2.Player, MagicJammer, 0);
            var lone = PutInHand(e2, e2.Player, Celtic);
            var dian2 = PutInHand(e2, e2.Opponent, DianKeto);
            var lp2 = e2.Opponent.LifePoints;
            e2.TryActivateSpellTrap(e2.Opponent, dian2, fromHand: true);
            var answered2 = MyChainWindow(e2, e2.Player) && e2.TryActivateChainResponse(e2.Player, jam2);
            var noPrompt = !e2.IsAwaitingEffectTarget;
            PassAll(e2);
            Check("Magic Jammer (human): a lone card in hand is discarded with no prompt",
                answered2 && noPrompt && e2.Player.Graveyard.Contains(lone) && e2.Opponent.LifePoints == lp2,
                $"answered={answered2} noPrompt={noPrompt} {State(e2)}");
        }

        static void MagicJammerCancelActivatesNothing(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMain(db, pDeck, aDeck, out _);
            var p = e.Player;
            var o = e.Opponent;
            var jam = PlaceSetTrap(e, p, MagicJammer, 0);
            PutInHand(e, p, Celtic);
            PutInHand(e, p, Kuriboh);
            var dian = PutInHand(e, o, DianKeto);
            var oLp = o.LifePoints;
            e.TryActivateSpellTrap(o, dian, fromHand: true);
            var answered = MyChainWindow(e, p) && e.TryActivateChainResponse(p, jam);
            var asked = e.IsAwaitingEffectTarget;
            e.CancelEffectTargeting();
            var reoffered = MyChainWindow(e, p) && !jam.FaceUp && p.TryFindSpellTrap(jam, out _) &&
                            p.Hand.Count == 2 && p.Graveyard.Count == 0 && e.Chain.Count == 1;
            PassAll(e);
            Check("Magic Jammer (human): Cancel at the discard — not activated, nothing discarded, the window stays open; " +
                  "passing lets the Spell resolve",
                answered && asked && reoffered && o.LifePoints == oLp + 1000 && !e.Chain.HasLinks,
                $"answered={answered} asked={asked} reoffered={reoffered} oLp={o.LifePoints} {State(e)}");
        }

        // ───────────────────────────── discard-cost free-chain cards on a chain ─────────────────────────────

        static void RaigekiBreakChainsToALink(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
            var rb = PlaceSetTrap(e, p, RaigekiBreak, 0);
            var celtic = PutInHand(e, p, Celtic);
            var kuriboh = PutInHand(e, p, Kuriboh);
            var dian = PutInHand(e, o, DianKeto);
            var oLp = o.LifePoints;
            e.TryActivateSpellTrap(o, dian, fromHand: true);
            var legal = MyChainWindow(e, p) &&
                        e.PendingResponse.LegalCards != null && e.PendingResponse.LegalCards.Contains(rb);
            var answered = MyChainWindow(e, p) && e.TryActivateChainResponse(p, rb);
            var discardChoices = Legal(e);
            var askedDiscard = discardChoices.Contains(celtic) && discardChoices.Contains(kuriboh);
            var paid = askedDiscard && e.TrySelectEffectTarget(kuriboh);
            var targetChoices = Legal(e);
            var askedTarget = targetChoices.Contains(blade) && !targetChoices.Contains(rb);
            var targeted = askedTarget && e.TrySelectEffectTarget(blade);
            PassAll(e);
            Check("Raigeki Break chained to a Spell: offered in the chain window; the player discards (choice), targets, " +
                  "and it resolves before the Spell (no second discard)",
                reached && legal && answered && askedDiscard && paid && askedTarget && targeted &&
                !OnField(o, blade) && p.Graveyard.Contains(kuriboh) && p.Hand.Contains(celtic) && p.Hand.Count == 1 &&
                p.Graveyard.Contains(rb) && o.LifePoints == oLp + 1000 && !e.Chain.HasLinks,
                $"legal={legal} answered={answered} discard={Names(discardChoices)} paid={paid} " +
                $"target={Names(targetChoices)} targeted={targeted} blade={OnField(o, blade)} hand={Names(p.Hand)} " +
                $"oLp={o.LifePoints} {State(e)}");
        }

        static void RaigekiBreakChainCancels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMain(db, pDeck, aDeck, out _);
            var p = e.Player;
            var o = e.Opponent;
            var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
            var rb = PlaceSetTrap(e, p, RaigekiBreak, 0);
            var celtic = PutInHand(e, p, Celtic);
            var kuriboh = PutInHand(e, p, Kuriboh);
            var dian = PutInHand(e, o, DianKeto);
            e.TryActivateSpellTrap(o, dian, fromHand: true);

            var answered = MyChainWindow(e, p) && e.TryActivateChainResponse(p, rb);
            e.CancelEffectTargeting();
            var atDiscard = MyChainWindow(e, p) && !rb.FaceUp && p.Hand.Count == 2 && e.Chain.Count == 1;

            var again = MyChainWindow(e, p) && e.TryActivateChainResponse(p, rb);
            var paid = again && e.TrySelectEffectTarget(celtic);
            var atTarget = Legal(e).Contains(blade);
            e.CancelEffectTargeting();
            var refunded = MyChainWindow(e, p) && !rb.FaceUp && p.TryFindSpellTrap(rb, out _) &&
                           p.Hand.Contains(celtic) && p.Hand.Contains(kuriboh) && p.Graveyard.Count == 0 &&
                           e.Chain.Count == 1;
            PassAll(e);
            Check("Raigeki Break on a chain: Cancel at the discard activates nothing; Cancel at the target refunds the " +
                  "discard and takes the link back (the window stays open)",
                answered && atDiscard && again && paid && atTarget && refunded && OnField(o, blade) && !e.Chain.HasLinks,
                $"answered={answered} atDiscard={atDiscard} again={again} paid={paid} atTarget={atTarget} " +
                $"refunded={refunded} hand={Names(p.Hand)} {State(e)}");
        }

        static void SpellPurificationAiChainsWithAutoDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Human vs human, the opponent seat answering automatically (TryActivateChainResponse's 2-arg overload): the
            // discard is chosen for it, the link resolves first and destroys the face-up Continuous Spell.
            var e = FreshCleared(db, pDeck, aDeck, hvh: true);
            var p = e.Player;
            var o = e.Opponent;
            var messenger = FaceUpSt(e, p, MessengerOfPeace, 1);
            var purification = PlaceSetTrap(e, o, SpellPurification, 0);
            var fodder = PutInHand(e, o, Celtic);
            var dian = PutInHand(e, p, DianKeto);
            var pLp = p.LifePoints;
            e.TryActivateSpellTrap(p, dian, fromHand: true);
            var answered = MyChainWindow(e, o) && e.TryActivateChainResponse(o, purification);
            var noPrompt = !e.IsAwaitingEffectTarget;
            PassAll(e);
            Check("Spell Purification chained (automatic seat): discards its lone card, destroys the face-up Continuous " +
                  "Spell; the answered Spell still resolves",
                answered && noPrompt && o.Graveyard.Contains(fodder) && !OnField(p, messenger) &&
                p.LifePoints == pLp + 1000 && !e.Chain.HasLinks,
                $"answered={answered} noPrompt={noPrompt} fodder={o.Graveyard.Contains(fodder)} " +
                $"messenger={OnField(p, messenger)} pLp={p.LifePoints} {State(e)}");
        }

        // ───────────────────────────── monster ignition Cancel ─────────────────────────────

        static void MonsterIgnitionCancelUndoes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var soldier = PlaceMonster(e, p, AbyssSoldier, 0, BattlePosition.Attack, true);
            var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
            var kraken = PutInHand(e, p, FiendKraken);
            var fish = PutInHand(e, p, SevenColoredFish);

            var act1 = e.TryActivateSpellTrap(p, soldier, fromHand: false);
            var costAsked = Legal(e).Contains(kraken) && Legal(e).Contains(fish);
            e.CancelEffectTargeting();
            var undone1 = !e.Chain.HasLinks && e.PendingActivation == null && p.Hand.Count == 2 &&
                          !soldier.EffectUsedThisTurn;

            var act2 = e.TryActivateSpellTrap(p, soldier, fromHand: false);
            var paid = act2 && e.TrySelectEffectTarget(kraken);
            var targets = Legal(e);
            var targetAsked = targets.Contains(blade) && targets.Contains(soldier);
            e.CancelEffectTargeting();
            var undone2 = !e.Chain.HasLinks && e.PendingActivation == null && p.Hand.Contains(kraken) &&
                          p.Hand.Contains(fish) && !p.Graveyard.Contains(kraken) && !soldier.EffectUsedThisTurn;

            var act3 = e.TryActivateSpellTrap(p, soldier, fromHand: false);
            var paid3 = act3 && e.TrySelectEffectTarget(kraken);
            var hit = paid3 && e.TrySelectEffectTarget(blade);
            PassAll(e);
            Check("Abyss Soldier: Cancel at the cost pick, then at the target pick (discard refunded), leaves no Chain " +
                  "Link and no spent use; the third activation returns the target",
                reached && act1 && costAsked && undone1 && act2 && paid && targetAsked && undone2 && act3 && paid3 &&
                hit && o.Hand.Contains(blade) && p.Graveyard.Contains(kraken) && !e.Chain.HasLinks,
                $"reached={reached} act1={act1} costAsked={costAsked} undone1={undone1} act2={act2} paid={paid} " +
                $"targets={Names(targets)} undone2={undone2} act3={act3} hit={hit} blade={o.Hand.Contains(blade)} " +
                $"used={soldier.EffectUsedThisTurn} {State(e)}");
        }

        // ───────────────────────────── Dice Re-Roll in the Standby Phase ─────────────────────────────

        static void DiceReRollHoldsStandbyForTheUpkeep(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var amazon = PlaceMonster(e, p, UnfriendlyAmazon, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, p, MysticalElf, 2, BattlePosition.Defense, true);
            var blind = FaceUpSt(e, p, BlindDestruction, 0);
            // Dice Re-Roll's effect for the player's next turn (the card resolved during that turn).
            var drr = e.CreateCardInstance(DiceReRoll);
            p.Graveyard.Add(drr);
            var turnProp = typeof(DuelEngine).GetProperty("TurnNumber");
            var now = e.TurnNumber;
            turnProp?.SetValue(e, now + 2);
            CoinDiceInterruptsRuntime.GrantDiceReRoll(e, p, drr);
            turnProp?.SetValue(e, now);

            e.Rng.QueueDie(1);
            EndTurn(e);
            EndTurn(e);
            var redo = e.PendingActivation;
            var redoAsked = e.TurnPlayer == p && e.Phase == DuelPhase.Standby && redo != null &&
                            redo.LegalTargets.Contains(drr);
            e.CancelEffectTargeting(); // keep the 1: nothing is Level 1
            var up = e.PendingActivation;
            var upkeepAsked = up != null && up.AwaitingUpkeepTribute && up.LegalTargets.Contains(celtic) &&
                              !up.LegalTargets.Contains(amazon) && e.Phase == DuelPhase.Standby;
            var tributed = upkeepAsked && e.TrySelectEffectTarget(celtic);
            Check("Blind Destruction + Dice Re-Roll: the redo prompt holds the Standby Phase; once answered, The " +
                  "Unfriendly Amazon's Tribute upkeep is still asked, then Main Phase 1",
                reached && redoAsked && upkeepAsked && tributed && OnField(p, amazon) && !OnField(p, celtic) &&
                OnField(p, elf) && OnField(p, blind) && e.PendingActivation == null && e.Phase == DuelPhase.Main1,
                $"reached={reached} redoAsked={redoAsked} upkeepAsked={upkeepAsked} tributed={tributed} " +
                $"amazon={OnField(p, amazon)} celtic={OnField(p, celtic)} {State(e)}");
        }
    }
}
