using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SpellEventsAndHandSendTriggers batch: Elephant Statue of Blessing / of Disaster and Regenerating Mummy (sent from the
    /// hand to the GY by an opponent's card effect), Curse of Darkness and Tower of Babel (each Spell Card activation that
    /// resolves), Spirit of the Pot of Greed (a trigger above "Pot of Greed") and Fiend's Hand Mirror (redirect a Spell that
    /// targets 1 Spell/Trap). Compile shapes, mini-duels through real engine entry points (TryActivateSpellTrap, the chain
    /// response window, TryFlipSummon) and fail-closed near misses. Runtime: <see cref="SpellEventsAndHandSendTriggersRuntime"/>.
    /// </summary>
    public static class SpellEventsAndHandSendTriggersRegressionTests
    {
        const int CurseOfDarkness = 84970821;
        const int TowerOfBabel = 94256039;
        const int SpiritOfThePot = 4896788;
        const int FiendsHandMirror = 58607704;
        const int StatueOfBlessing = 85166216;
        const int StatueOfDisaster = 12160911;
        const int RegeneratingMummy = 70821187;

        const int PotOfGreed = 55144522;
        const int CardDestruction = 72892473;
        const int Confiscation = 17375316;
        const int CrushCardVirus = 57728570;
        const int SkullServant = 32274490; // DARK Zombie 300 ATK (Crush Card Virus's Tribute)
        const int MagicJammer = 77414722;
        const int Mst = 5318639;
        const int OfferingsToTheDoomed = 19230407; // Quick-Play: target 1 face-up monster on the field; destroy it
        const int Mandragola = 7802006;
        const int Celtic = 91152256;
        const int Bewd = 89631139;

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            void Check(string name, bool ok, string detail = "") => check("SpellEventsAndHandSendTriggers: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);
            HandSendDuels(db, pDeck, aDeck, Check);
            CurseDuels(db, pDeck, aDeck, Check);
            TowerDuels(db, pDeck, aDeck, Check);
            SpiritDuels(db, pDeck, aDeck, Check);
            FiendsMirrorDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static CardInstance FaceUpTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static bool ChainWindowOffers(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        static CompiledCardProgram Comp(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram Synthetic(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = "SpellEventsAndHandSendTriggers near miss " + id,
                type = type,
                frameType = type.Contains("Trap") ? "trap" : "effect",
                race = race,
                attribute = type.Contains("Trap") ? null : "EARTH",
                desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Left(CompiledCardProgram p) =>
            p == null ? "null" : $"fully={p.FullyCompiled} clauses=[{string.Join(",", p.ClauseList.Select(c => c?.Timing + ":" + c?.Action))}]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, action, side, amount) in new[]
                     {
                         (StatueOfBlessing, EffectActionKind.GainLifePoints, EffectSide.Controller, 2000),
                         (StatueOfDisaster, EffectActionKind.InflictDamageToOpponent, EffectSide.Opponent, 2000),
                         (RegeneratingMummy, EffectActionKind.ReturnThisFromGyToHand, EffectSide.Controller, 0),
                     })
            {
                var p = Comp(db, id);
                Check($"{db.Get(id)?.name}: one mandatory sent-from-hand-by-opponent trigger ({action})",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    Has(p, c => c.Timing == EffectTiming.SentFromHandToGyByOpponentEffect && c.Action == action &&
                                c.Side == side && c.Amount == amount && !c.IsOptional && c.MakesChainLink) &&
                    !Has(p, c => c.Timing == EffectTiming.DiscardedFromHandByOpponentEffect) &&
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented, Left(p));
            }

            var curse = Comp(db, CurseOfDarkness);
            Check("Curse of Darkness: one continuous 1000-damage marker (no Chain Link), Implemented",
                curse != null && curse.FullyCompiled && curse.ClauseList.Count == 1 &&
                Has(curse, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.InflictDamageToSpellActivatorAfterResolve &&
                                c.Amount == 1000 && !c.MakesChainLink) &&
                CardEffectStatus.Classify(db.Get(CurseOfDarkness)) == CardEffectStatusKind.Implemented, Left(curse));

            var tower = Comp(db, TowerOfBabel);
            Check("Tower of Babel: the shared Spell Counter clause + the 4th-counter 3000-damage trigger, Implemented",
                tower != null && tower.FullyCompiled && tower.ClauseList.Count == 2 &&
                Has(tower, c => c.Action == EffectActionKind.PlaceSpellCounters && c.PlaceCounterOnSpellActivate &&
                                c.Amount == 1 && c.CounterMax == 0) &&
                Has(tower, c => c.Action == EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator &&
                                c.ConditionAmount == 4 && c.Amount == 3000) &&
                CardEffectStatus.Classify(db.Get(TowerOfBabel)) == CardEffectStatusKind.Implemented, Left(tower));

            var spirit = Comp(db, SpiritOfThePot);
            Check("Spirit of the Pot of Greed: one marker naming \"Pot of Greed\", draw 1, Implemented",
                spirit != null && spirit.FullyCompiled && spirit.ClauseList.Count == 1 &&
                Has(spirit, c => c.Action == EffectActionKind.ActivatorOfNamedCardMayDrawWhileAttackPosition &&
                                 c.NamedCard == "Pot of Greed" && c.Amount == 1) &&
                CardEffectStatus.Classify(db.Get(SpiritOfThePot)) == CardEffectStatusKind.Implemented, Left(spirit));

            var fiend = Comp(db, FiendsHandMirror);
            Check("Fiend's Hand Mirror: targeted redirect to another Spell/Trap, answering an opponent's Spell, Implemented",
                fiend != null && fiend.FullyCompiled && fiend.ClauseList.Count == 1 &&
                Has(fiend, c => c.Timing == EffectTiming.Activate &&
                                c.Action == EffectActionKind.RedirectToAnotherAppropriateTarget &&
                                c.Zone == EffectZoneFilter.AnotherAppropriateSpellTrapTarget && c.IsPsctTarget &&
                                c.RequiresTargetChoice &&
                                c.ActivationCondition == RedirectAndReplaceRuntime.OpponentSpellTargetsOneSpellTrap &&
                                c.ConditionName == RedirectAndReplaceRuntime.AnswersSpell) &&
                CardEffectStatus.Classify(db.Get(FiendsHandMirror)) == CardEffectStatusKind.Implemented, Left(fiend));
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var anyHandSend = Synthetic(90177001, "Effect Monster", "Rock",
                "When this card is sent from your hand to your Graveyard: You gain 2000 Life Points.");
            Check("near miss: a hand send with no opponent clause is not the trigger",
                !Has(anyHandSend, c => c.Timing == EffectTiming.SentFromHandToGyByOpponentEffect), Left(anyHandSend));

            var fromField = Synthetic(90177002, "Effect Monster", "Rock",
                "When this card is sent from the field to your Graveyard by an opponent's card effect: You gain 2000 Life Points.");
            Check("near miss: sent from the field is not the hand trigger",
                !Has(fromField, c => c.Timing == EffectTiming.SentFromHandToGyByOpponentEffect), Left(fromField));

            var statuePlus = Synthetic(90177003, "Effect Monster", "Rock",
                "When this card is sent from your hand to your Graveyard by an opponent's card effect: You gain 2000 Life Points. Draw 1 card.");
            Check("near miss: an extra sentence after the Statue's text is not FullyCompiled",
                statuePlus == null || !statuePlus.FullyCompiled, Left(statuePlus));

            var cursePlus = Synthetic(90177004, "Trap Card", "Continuous",
                "Each time a Spell Card is activated, immediately after it resolves, the player that activated it takes 1000 damage. You cannot conduct your Battle Phase.");
            Check("near miss: Curse of Darkness with an extra sentence is not FullyCompiled",
                cursePlus == null || !cursePlus.FullyCompiled, Left(cursePlus));

            var towerNoDamage = Synthetic(90177005, "Trap Card", "Continuous",
                "Each time a Spell Card is activated, place 1 Spell Counter on this card when that Spell Card resolves. If the 4th Spell Counter is placed on this card: Destroy this card.");
            Check("near miss: a Tower without its damage clause gets no Tower trigger",
                !Has(towerNoDamage, c => c.Action == EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator) &&
                (towerNoDamage == null || !towerNoDamage.FullyCompiled), Left(towerNoDamage));

            var spiritDef = Synthetic(90177006, "Effect Monster", "Fairy",
                "While this card is in face-up Defense Position on the field, if a player activates \"Pot of Greed\", they can draw 1 more card.");
            Check("near miss: a Defense Position Spirit is not compiled",
                !Has(spiritDef, c => c.Action == EffectActionKind.ActivatorOfNamedCardMayDrawWhileAttackPosition), Left(spiritDef));

            var fiendTrap = Synthetic(90177007, "Trap Card", "Normal",
                "Switch the opponent's Trap Card effect that targets 1 Spell or Trap Card to another correct target.");
            Check("near miss: a Trap-answering Fiend's Hand Mirror is not compiled",
                !Has(fiendTrap, c => c.Action == EffectActionKind.RedirectToAnotherAppropriateTarget), Left(fiendTrap));
        }

        // ───────────────────────────── Statues / Mummy ─────────────────────────────

        static void HandSendDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Card Destruction discards the Statue of Blessing: its owner gains 2000.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var statue = PutInHand(e, p, StatueOfBlessing);
                PutInHand(e, p, Celtic);
                PutInHand(e, o, Celtic);
                var cd = PutInHand(e, o, CardDestruction);
                var lp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, cd, fromHand: true);
                PassAll(e);
                Check("Elephant Statue of Blessing: discarded by the opponent's Card Destruction → its owner gains 2000",
                    act && p.Graveyard.Contains(statue) && p.LifePoints == lp + 2000,
                    $"reached={reached} act={act} gy={p.Graveyard.Contains(statue)} lp={p.LifePoints} was={lp}");
            }

            {
                // Fail closed: the owner's own Card Destruction (rp == tp) does not trigger it.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var statue = PutInHand(e, p, StatueOfBlessing);
                PutInHand(e, o, Celtic);
                var cd = PutInHand(e, p, CardDestruction);
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, cd, fromHand: true);
                PassAll(e);
                Check("Elephant Statue of Blessing (near miss): its owner's own Card Destruction gains nothing",
                    act && p.Graveyard.Contains(statue) && p.LifePoints == lp,
                    $"act={act} gy={p.Graveyard.Contains(statue)} lp={p.LifePoints} was={lp}");
            }

            {
                // The opponent's Crush Card Virus destroys the Statue (1500 ATK) in the hand: not a discard, still a
                // send from the hand to the GY by the opponent's card effect.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var statue = PutInHand(e, p, StatueOfBlessing);
                PlaceMonster(e, o, SkullServant, 0, BattlePosition.Attack, true);
                var virus = PlaceSetTrap(e, o, CrushCardVirus, 0);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, virus, fromHand: false);
                for (var i = 0; i < 4 && e.IsAwaitingEffectTarget && e.PendingActivation?.Controller == o; i++)
                    e.TrySelectEffectTarget(e.PendingActivation.LegalTargets.FirstOrDefault());
                PassAll(e);
                Check("Elephant Statue of Blessing: destroyed in the hand by the opponent's Crush Card Virus → its owner gains 2000",
                    act && p.Graveyard.Contains(statue) && p.LifePoints == lp + 2000,
                    $"reached={reached} act={act} gy={p.Graveyard.Contains(statue)} lp={p.LifePoints} was={lp}");
            }

            {
                // The opponent's Confiscation discards the Statue of Disaster: 2000 damage to the opponent (who also paid 1000).
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var statue = PutInHand(e, p, StatueOfDisaster);
                var conf = PutInHand(e, o, Confiscation);
                var lp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, conf, fromHand: true);
                PassAll(e);
                Check("Elephant Statue of Disaster: discarded by the opponent's Confiscation → 2000 damage to that opponent",
                    act && p.Graveyard.Contains(statue) && o.LifePoints == lp - 1000 - 2000,
                    $"reached={reached} act={act} gy={p.Graveyard.Contains(statue)} oppLp={o.LifePoints} was={lp}");
            }

            {
                // The opponent's Confiscation discards Regenerating Mummy: it returns to its owner's hand.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mummy = PutInHand(e, p, RegeneratingMummy);
                var conf = PutInHand(e, o, Confiscation);
                var act = reached && e.TryActivateSpellTrap(o, conf, fromHand: true);
                PassAll(e);
                Check("Regenerating Mummy: discarded by the opponent's Confiscation → back to its owner's hand",
                    act && p.Hand.Contains(mummy) && !p.Graveyard.Contains(mummy) && o.Graveyard.Contains(conf),
                    $"reached={reached} act={act} hand={p.Hand.Contains(mummy)} gy={p.Graveyard.Contains(mummy)}");
            }
        }

        // ───────────────────────────── Curse of Darkness ─────────────────────────────

        static void CurseDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Its controller's own Pot of Greed: the controller takes 1000 once it resolved.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                FaceUpTrap(e, p, CurseOfDarkness, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                Check("Curse of Darkness: its controller's own Pot of Greed resolves → 1000 damage to that player",
                    act && p.Graveyard.Contains(pot) && p.Hand.Count == 2 && p.LifePoints == lp - 1000,
                    $"act={act} hand={p.Hand.Count} lp={p.LifePoints} was={lp}");
            }

            {
                // The opponent's Pot of Greed: the opponent takes 1000.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, p, CurseOfDarkness, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                PassAll(e);
                Check("Curse of Darkness: the opponent's Pot of Greed → 1000 damage to the opponent only",
                    act && o.LifePoints == lp - 1000 && p.LifePoints == pLp,
                    $"reached={reached} act={act} oppLp={o.LifePoints} was={lp} pLp={p.LifePoints}");
            }

            {
                // aux.chainreg: Curse activated in answer to the Pot (Chain Link 2) did not apply when the Pot was
                // activated — no damage when the Pot resolves.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var curse = PlaceSetTrap(e, p, CurseOfDarkness, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = ChainWindowOffers(e, p, curse);
                var chained = offered && e.TryActivateSpellTrap(p, curse, fromHand: false);
                PassAll(e);
                Check("Curse of Darkness (near miss): chained to the Pot it was not face-up when the Pot was activated — no damage",
                    act && chained && curse.FaceUp && p.TryFindSpellTrap(curse, out _) && o.Hand.Count == 2 &&
                    o.LifePoints == lp,
                    $"act={act} offered={offered} chained={chained} oppLp={o.LifePoints} was={lp} {Window(e)}");
            }

            {
                // A negated activation never resolves: the opponent's Pot negated by the Player's Magic Jammer.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpTrap(e, p, CurseOfDarkness, 0);
                var jammer = PlaceSetTrap(e, p, MagicJammer, 1);
                PutInHand(e, p, Celtic);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = ChainWindowOffers(e, p, jammer);
                var negated = offered && e.TryActivateSpellTrap(p, jammer, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(e.PendingActivation.LegalTargets.FirstOrDefault());
                PassAll(e);
                Check("Curse of Darkness (near miss): a negated Spell activation deals no damage",
                    act && negated && o.Graveyard.Contains(pot) && o.Hand.Count == 0 && o.LifePoints == lp &&
                    p.LifePoints == pLp,
                    $"act={act} offered={offered} negated={negated} oppHand={o.Hand.Count} oppLp={o.LifePoints} was={lp} {Window(e)}");
            }
        }

        // ───────────────────────────── Tower of Babel ─────────────────────────────

        static void TowerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 1 → 2 counters: nothing else happens.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var tower = FaceUpTrap(e, p, TowerOfBabel, 0);
                tower.Counters = 1;
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                Check("Tower of Babel: a resolved Spell adds 1 Spell Counter (1 → 2), no destruction",
                    act && tower.Counters == 2 && p.TryFindSpellTrap(tower, out _) && p.LifePoints == lp,
                    $"act={act} counters={tower.Counters} lp={p.LifePoints}");
            }

            {
                // The 4th counter from the opponent's Pot of Greed: the Tower is destroyed and the opponent takes 3000.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var tower = FaceUpTrap(e, p, TowerOfBabel, 0);
                tower.Counters = 3;
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var pLp = p.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                PassAll(e);
                Check("Tower of Babel: the 4th Spell Counter (the opponent's Pot) destroys it → 3000 damage to the opponent",
                    act && p.Graveyard.Contains(tower) && o.LifePoints == lp - 3000 && p.LifePoints == pLp &&
                    o.Hand.Count == 2,
                    $"reached={reached} act={act} towerGy={p.Graveyard.Contains(tower)} counters={tower.Counters} " +
                    $"oppLp={o.LifePoints} was={lp} pLp={p.LifePoints}");
            }

            {
                // Its controller's own 4th Spell: the controller takes the 3000.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var tower = FaceUpTrap(e, p, TowerOfBabel, 0);
                tower.Counters = 3;
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                Check("Tower of Babel: its controller's own 4th Spell → destroyed, 3000 damage to its controller",
                    act && p.Graveyard.Contains(tower) && p.LifePoints == lp - 3000,
                    $"act={act} towerGy={p.Graveyard.Contains(tower)} lp={p.LifePoints} was={lp}");
            }

            {
                // Another card's effect brings it to 4 (Magical Plant Mandragola's FLIP): destroyed, no damage.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var tower = FaceUpTrap(e, p, TowerOfBabel, 0);
                tower.Counters = 3;
                var mand = PlaceMonster(e, p, Mandragola, 0, BattlePosition.Defense, false);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var flipped = e.TryFlipSummon(p, mand);
                PassAll(e);
                Check("Tower of Babel: 4 Spell Counters by another card's effect (Mandragola) → destroyed, no damage",
                    flipped && p.Graveyard.Contains(tower) && p.LifePoints == lp && o.LifePoints == oLp,
                    $"flipped={flipped} towerGy={p.Graveyard.Contains(tower)} counters={tower.Counters} lp={p.LifePoints}");
            }

            {
                // aux.chainreg: the Tower chained to the opponent's Pot gets no counter for it.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var tower = PlaceSetTrap(e, p, TowerOfBabel, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                var offered = ChainWindowOffers(e, p, tower);
                var chained = offered && e.TryActivateSpellTrap(p, tower, fromHand: false);
                PassAll(e);
                Check("Tower of Babel (near miss): chained to the Pot, it gets no Spell Counter for it",
                    act && chained && tower.FaceUp && p.TryFindSpellTrap(tower, out _) && tower.Counters == 0 &&
                    o.Hand.Count == 2,
                    $"act={act} offered={offered} chained={chained} counters={tower.Counters} {Window(e)}");
            }
        }

        // ───────────────────────────── Spirit of the Pot of Greed ─────────────────────────────

        static void SpiritDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Its controller's Pot of Greed: a trigger link above the Pot; the controller taps to draw 1, then the Pot draws 2.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var spirit = PlaceMonster(e, p, SpiritOfThePot, 0, BattlePosition.Attack, true);
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(spirit);
                var handAtAsk = p.Hand.Count;
                if (asked) e.TrySelectEffectTarget(spirit);
                PassAll(e);
                Check("Spirit of the Pot of Greed: the Pot's activator may draw 1 more (asked before the Pot draws 2)",
                    act && asked && handAtAsk == 0 && p.Hand.Count == 3 && p.Deck.Count == deck - 3 &&
                    p.Graveyard.Contains(pot) && e.Chain.Count == 0 && !e.IsAwaitingEffectTarget,
                    $"act={act} asked={asked} handAtAsk={handAtAsk} hand={p.Hand.Count} deck={p.Deck.Count} was={deck} chain={e.Chain.Count}");
            }

            {
                // Declined: only the Pot's 2 cards.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var spirit = PlaceMonster(e, p, SpiritOfThePot, 0, BattlePosition.Attack, true);
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(spirit);
                if (asked) e.CancelEffectTargeting();
                PassAll(e);
                Check("Spirit of the Pot of Greed: CANCEL declines the extra draw (the Pot still draws 2)",
                    act && asked && p.Hand.Count == 2 && p.Graveyard.Contains(pot) && !e.IsAwaitingEffectTarget,
                    $"act={act} asked={asked} hand={p.Hand.Count}");
            }

            {
                // In Defense Position it does nothing.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, SpiritOfThePot, 0, BattlePosition.Defense, true);
                var pot = PutInHand(e, p, PotOfGreed);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, pot, fromHand: true);
                PassAll(e);
                Check("Spirit of the Pot of Greed (near miss): face-up Defense Position → no trigger, the Pot draws 2",
                    act && p.Hand.Count == 2 && !e.IsAwaitingEffectTarget, $"act={act} hand={p.Hand.Count}");
            }

            {
                // The opponent (AI) activates Pot of Greed next to the Player's Spirit: the opponent draws 1 more.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, SpiritOfThePot, 0, BattlePosition.Attack, true);
                var pot = PutInHand(e, o, PotOfGreed);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, pot, fromHand: true);
                PassAll(e);
                Check("Spirit of the Pot of Greed: the opponent's Pot of Greed → the opponent (AI) draws 1 more",
                    act && o.Hand.Count == 3 && p.Hand.Count == 0 && !e.IsAwaitingEffectTarget && e.Chain.Count == 0,
                    $"reached={reached} act={act} oppHand={o.Hand.Count} pHand={p.Hand.Count} {Window(e)}");
            }

            {
                // Another Spell (Card Destruction is not "Pot of Greed") does not trigger it.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, SpiritOfThePot, 0, BattlePosition.Attack, true);
                PutInHand(e, p, Celtic);
                PutInHand(e, o, Celtic);
                var cd = PutInHand(e, p, CardDestruction);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, cd, fromHand: true);
                PassAll(e);
                Check("Spirit of the Pot of Greed (near miss): a Spell other than \"Pot of Greed\" does not trigger it",
                    act && p.Hand.Count == 1 && !e.IsAwaitingEffectTarget, $"act={act} hand={p.Hand.Count}");
            }
        }

        // ───────────────────────────── Fiend's Hand Mirror ─────────────────────────────

        static void FiendsMirrorDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var mirror = PlaceSetTrap(e, e.Player, FiendsHandMirror, 0);
                PlaceSetTrap(e, e.Player, MagicJammer, 1);
                e.NotifyPublic();
                Check("Fiend's Hand Mirror: cannot be activated at open game state",
                    reached && !e.CanActivateSpellTrap(e.Player, mirror, fromHand: false));
            }

            {
                // The opponent's MST targets one of the Player's Spell/Traps; the Mirror switches it to the opponent's own
                // Set card, which is destroyed instead.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mirror = PlaceSetTrap(e, p, FiendsHandMirror, 0);
                var curse = FaceUpTrap(e, p, CurseOfDarkness, 1);
                var theirs = PlaceSetTrap(e, o, MagicJammer, 0);
                var mst = PutInHand(e, o, Mst);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, mst, fromHand: true);
                var link = e.Chain.Last;
                var current = link != null && ReferenceEquals(link.Card, mst) ? link.Target : null;
                var offered = ChainWindowOffers(e, p, mirror);
                Check("Fiend's Hand Mirror: offered when the opponent's Spell targets exactly 1 Spell/Trap",
                    act && current != null && offered, $"act={act} current={current?.Name} {Window(e)}");
                var activated = offered && e.TryActivateSpellTrap(p, mirror, fromHand: false);
                var legal = e.PendingActivation?.LegalTargets;
                var pickOk = activated && e.IsAwaitingEffectTarget && legal != null && legal.Contains(theirs) &&
                             !legal.Contains(current) && !legal.Contains(mst) &&
                             legal.TrueForAll(t => t != null && t.Def != null && (t.Def.IsSpell || t.Def.IsTrap));
                Check("Fiend's Hand Mirror: the new targets are other Spell/Traps on the field, never the current one",
                    pickOk, legal == null ? "no pick" : $"legal=[{string.Join(",", legal.Select(c => c?.Name))}]");
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(theirs);
                PassAll(e);
                var currentSafe = current != null && (ReferenceEquals(current, mirror)
                    ? p.Graveyard.Contains(mirror)
                    : p.TryFindSpellTrap(current, out _));
                Check("Fiend's Hand Mirror: the MST resolves on the new target — the opponent's own card is destroyed",
                    o.Graveyard.Contains(theirs) && currentSafe && p.TryFindSpellTrap(curse, out _) &&
                    p.Graveyard.Contains(mirror) && o.Graveyard.Contains(mst) && e.Chain.Count == 0,
                    $"theirsGy={o.Graveyard.Contains(theirs)} currentSafe={currentSafe} curse={p.TryFindSpellTrap(curse, out _)} " +
                    $"mirrorGy={p.Graveyard.Contains(mirror)} chain={e.Chain.Count} {Window(e)}");
            }

            {
                // The AI's Mirror: the Player's MST targets the AI's Set card; the AI switches it to the Mirror itself, which
                // has left the field when the MST resolves — the Set card survives.
                var e = PlayerMain(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceSetTrap(e, o, FiendsHandMirror, 0);
                var target = PlaceSetTrap(e, o, MagicJammer, 1);
                var mst = PutInHand(e, p, Mst);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.Controller == p) e.TrySelectEffectTarget(target);
                PassAll(e);
                var mirrorGy = o.Graveyard.Any(c => c != null && c.CardId == FiendsHandMirror);
                Check("Fiend's Hand Mirror (AI): redirected to the Mirror itself — the targeted Set card survives",
                    act && mirrorGy && o.TryFindSpellTrap(target, out _) && p.Graveyard.Contains(mst),
                    $"act={act} mirrorGy={mirrorGy} targetOnField={o.TryFindSpellTrap(target, out _)} {Window(e)}");
            }

            {
                // A Spell targeting a monster (Offerings to the Doomed) does not open Fiend's Hand Mirror.
                var e = OpponentMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var o = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var mirror = PlaceSetTrap(e, p, FiendsHandMirror, 0);
                PlaceSetTrap(e, p, MagicJammer, 1);
                var offerings = PutInHand(e, o, OfferingsToTheDoomed);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(o, offerings, fromHand: true);
                var offered = ChainWindowOffers(e, p, mirror);
                PassAll(e);
                Check("Fiend's Hand Mirror (near miss): not offered against a Spell that targets a monster",
                    act && !offered && p.Graveyard.Contains(bewd), $"act={act} offered={offered} {Window(e)}");
            }
        }
    }
}
