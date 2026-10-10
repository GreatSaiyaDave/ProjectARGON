using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "TargetNegationsAndHandQuickEffects" (v163): Curse of Royal (Counter Trap vs a Spell/Trap that targets and
    /// destroys 1 Spell/Trap), Gravekeeper's Watcher (hand Chain answer vs a discard effect), Charm of Shabti (free-chain hand
    /// Quick Effect: Gravekeeper's battle protection), Great Dezard (battle count, mandatory negation of targeting Spell/Trap
    /// activations, Tribute → Fushioh Richie) and Fushioh Richie (its procedure, the same negation, the flip-up revival).
    /// Each card: its compile shape, mini-duels through the engine entry points, near misses.
    /// Runtime: <see cref="TargetNegationsAndHandQuickEffectsRuntime"/>.
    /// </summary>
    public static class TargetNegationsAndHandQuickEffectsRegressionTests
    {
        const int CurseOfRoyal = 2926176;
        const int Watcher = 26084285;
        const int CharmOfShabti = 50412166;
        const int GreatDezard = 88989706;
        const int FushiohRichie = 39711336;

        const int Mst = 5318639;                  // Quick-Play: target 1 Spell/Trap on the field; destroy it
        const int HarpiesFeatherDuster = 18144507; // destroy all your opponent's Spells/Traps (no target)
        const int OfferingsToTheDoomed = 19230407; // Quick-Play: target 1 face-up monster; destroy it
        const int BookOfMoon = 14087893;           // Quick-Play: target 1 face-up monster; face-down Defense
        const int DustTornado = 60082869;          // Trap: target 1 Spell/Trap your opponent controls; destroy it
        const int GracefulCharity = 79571449;      // draw 3, then discard 2
        const int CardDestruction = 72892473;      // both players discard their hands, then draw that many
        const int PotOfGreed = 55144522;
        const int MonsterReborn = 83764719;
        const int SpearSoldier = 63695531;         // Gravekeeper's Spear Soldier, 1500/1000
        const int Celtic = 91152256;               // Celtic Guardian, 1400/1200 (Normal)
        const int Bewd = 89631139;                 // Blue-Eyes White Dragon, 3000/2500 (Normal)
        const int SkullServant = 32274490;         // Zombie Lv1 300/200 (Normal)

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("TargetNegationsAndHandQuickEffects: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            CurseOfRoyalDuels(db, pDeck, aDeck, Check);
            WatcherDuels(db, pDeck, aDeck, Check);
            CharmOfShabtiDuels(db, pDeck, aDeck, Check);
            GreatDezardDuels(db, pDeck, aDeck, Check);
            FushiohRichieDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "TargetNegations near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };

        static bool CompilesFully(CardDef def)
        {
            var prog = CardTextEffectCompiler.Compile(def);
            return prog != null && prog.FullyCompiled;
        }

        static void Empty(DuelEngine engine)
        {
            ClearBoard(engine);
            engine.ClearPendingResponse();
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }
        }

        /// <summary>The opponent's Main Phase 1, boards, hands and GYs empty.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            Empty(engine);
            return engine;
        }

        /// <summary>The Player's Main Phase 1 on turn 3+ (a Battle Phase is allowed), boards, hands and GYs empty.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 12 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            Empty(engine);
            return engine;
        }

        /// <summary>The opponent's Battle Phase (turn 4+), boards, hands and GYs empty.</summary>
        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
            reached = r0 && ReachOpponentBattle(engine);
            Empty(engine);
            return engine;
        }

        /// <summary>The next turn of <paramref name="who"/>, in its Main Phase 1.</summary>
        static bool NextMain1Of(DuelEngine engine, DuelistState who)
        {
            var start = engine.TurnNumber;
            for (var t = 0; t < 6 && !(engine.TurnPlayer == who && engine.TurnNumber > start &&
                                       engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == who && engine.TurnNumber > start && engine.Phase == DuelPhase.Main1) break;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            PassAll(engine);
            return engine.TurnPlayer == who && engine.TurnNumber > start && engine.Phase == DuelPhase.Main1;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static bool Offers(DuelEngine engine, DuelistState who, CardInstance card, ResponseTiming timing) =>
            engine.PendingResponse != null && engine.PendingResponse.Timing == timing &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        static string Chain(DuelEngine engine) => engine.Chain.Describe();

        static List<string> Capture(DuelEngine engine)
        {
            var lines = new List<string>();
            engine.OnLog += s => lines.Add(s);
            return lines;
        }

        static CardInstance InGy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var curse = Prog(db, CurseOfRoyal);
            var c = curse?.ClauseList.FirstOrDefault();
            Check("Curse of Royal compiles to a Counter Trap negation of a targeting S/T destroy (either player)",
                curse != null && curse.FullyCompiled && Implemented(db, CurseOfRoyal) && curse.ClauseList.Count == 1 &&
                c != null && c.Timing == EffectTiming.ChainLinkActivated && c.Action == EffectActionKind.NegateActivation &&
                c.DestroyNegatedCard && c.NegateRespondsToSpell && c.NegateRespondsToTrap && c.NegateCardActivationOnly &&
                !c.NegateOpponentActivationOnly &&
                c.ActivationCondition == TargetNegationsAndHandQuickEffectsRuntime.LinkTargetsOneSpellTrapToDestroy);

            var watcher = Prog(db, Watcher);
            var w = watcher?.ClauseList.FirstOrDefault();
            Check("Gravekeeper's Watcher compiles to a hand Chain answer (send it from the hand; negate and destroy)",
                watcher != null && watcher.FullyCompiled && Implemented(db, Watcher) && watcher.ClauseList.Count == 1 &&
                w != null && TargetNegationsAndHandQuickEffectsRuntime.IsHandChainAnswer(w) && w.RequiresSendThisToGy &&
                !w.RequiresDiscardSelf && w.DestroyNegatedCard && w.NegateOpponentActivationOnly &&
                w.ActivationCondition == TargetNegationsAndHandQuickEffectsRuntime.OpponentLinkMakesActivatorDiscard);

            var charm = Prog(db, CharmOfShabti);
            var s = charm?.ClauseList.FirstOrDefault();
            Check("Charm of Shabti compiles to a hand Quick Effect: discard it, Gravekeeper's battle protection",
                charm != null && charm.FullyCompiled && Implemented(db, CharmOfShabti) && charm.ClauseList.Count == 1 &&
                s != null && s.Timing == EffectTiming.HandQuickEffect &&
                s.Action == EffectActionKind.SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase &&
                s.RequiresDiscardSelf && s.NamedCard == "Gravekeeper's" && s.NamedCardIsSeries);

            var dezard = Prog(db, GreatDezard);
            var neg = dezard?.ClauseList.FirstOrDefault(x =>
                x?.Action == EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy);
            var ss = dezard?.ClauseList.FirstOrDefault(x => x?.Timing == EffectTiming.Activate);
            Check("Great Dezard compiles: negation after 1 battle kill, Tribute → \"Fushioh Richie\" after 2",
                dezard != null && dezard.FullyCompiled && Implemented(db, GreatDezard) && dezard.ClauseList.Count == 2 &&
                neg != null && neg.Timing == EffectTiming.ContinuousWhileFaceUp && neg.ConditionAmount == 1 &&
                ss != null && ss.Action == EffectActionKind.SpecialSummonChosenFromHandOrDeck &&
                ss.Zone == EffectZoneFilter.HandOrDeckNamed && ss.NamedCard == "Fushioh Richie" && ss.RequiresTributeThis &&
                ss.ChoiceDoesNotTarget && !ss.IsPsctTarget && ss.ConditionAmount == 2 &&
                ss.ActivationCondition == TargetNegationsAndHandQuickEffectsRuntime.ThisDestroyedMonstersByBattle);

            var richie = Prog(db, FushiohRichie);
            var rNeg = richie?.ClauseList.FirstOrDefault(x =>
                x?.Action == EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy);
            var rFlip = richie?.ClauseList.FirstOrDefault(x => x?.Timing == EffectTiming.ThisCardSummoned);
            Check("Fushioh Richie compiles: flip-down ignition, negation (no count), flip-up Zombie revival (target)",
                richie != null && richie.FullyCompiled && Implemented(db, FushiohRichie) && richie.ClauseList.Count == 3 &&
                richie.ClauseList.Exists(x => x?.Action == EffectActionKind.SetThisFaceDownDefense && x.OncePerTurn) &&
                rNeg != null && rNeg.ConditionAmount == 0 &&
                rFlip != null && rFlip.Action == EffectActionKind.SpecialSummonFromGy &&
                rFlip.Zone == EffectZoneFilter.ControllerGyMonsters && rFlip.RaceFilter == "Zombie" &&
                rFlip.RequiresTargetChoice && rFlip.IsPsctTarget && rFlip.IsOptional && rFlip.AlsoWhenFlippedFaceUp);
            Check("Fushioh Richie: only Great Dezard's effect completes its procedure",
                TributeSummonsRuntime.ProcedureCardName(db.Get(FushiohRichie)) == "Great Dezard");
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var curse = db.Get(CurseOfRoyal);
            Check("Near miss: Curse of Royal's text compiles (control)", CompilesFully(curse));
            Check("Near miss: \"destroying 1 monster\" is not Curse of Royal's template",
                !CompilesFully(Synthetic(curse, (curse.desc ?? "").Replace("destroying 1 Spell or Trap Card",
                    "destroying 1 monster"))));
            var watcher = db.Get(Watcher);
            Check("Near miss: \"could make them draw\" is not Gravekeeper's Watcher's template",
                !CompilesFully(Synthetic(watcher, (watcher.desc ?? "").Replace("discard", "draw"))));
            var charm = db.Get(CharmOfShabti);
            Check("Near miss: \"During your turn\" is not Charm of Shabti's template",
                !CompilesFully(Synthetic(charm, (charm.desc ?? "").Replace("During either player's turn",
                    "During your turn"))));
            var dezard = db.Get(GreatDezard);
            var dText = dezard.desc ?? "";
            var two = dText.IndexOf("Two:", StringComparison.Ordinal);
            Check("Near miss: Great Dezard's first sentence alone stays uncompiled",
                two > 0 && !CompilesFully(Synthetic(dezard, dText.Substring(0, two).Trim())));
            Check("Near miss: a Dezard text with a third numbered effect stays uncompiled",
                !CompilesFully(Synthetic(dezard, dText + " Three: Draw 1 card.")));
            var richie = db.Get(FushiohRichie);
            Check("Near miss: \"all Spell Cards that target this card\" is not the negation template",
                !CompilesFully(Synthetic(richie,
                    "As long as this monster remains face-up on the field, negate the activation and effects of all " +
                    "Spell Cards that target this card and destroy them.")));
        }

        // ───────────────────────────── Curse of Royal ─────────────────────────────

        static void CurseOfRoyalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's MST targets one of the Player's Set cards: Curse of Royal negates and destroys it.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var curse = PlaceSetTrap(engine, p, CurseOfRoyal, 0);
                var dust = PlaceSetTrap(engine, p, DustTornado, 1);
                var mst = PutInHand(engine, opp, Mst);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, mst, fromHand: true);
                var offered = Offers(engine, p, curse, ResponseTiming.ChainResponse);
                Check("Curse of Royal: offered against MST (targets and destroys 1 Spell/Trap)",
                    reached && act && offered, $"act={act} {Window(engine)} chain={Chain(engine)}");
                var answered = offered && engine.TryActivateChainResponse(p, curse);
                PassAll(engine);
                Check("Curse of Royal: MST is negated and destroyed — the Player's Set card stays",
                    answered && opp.Graveyard.Contains(mst) && p.Graveyard.Contains(curse) &&
                    p.TryFindSpellTrap(dust, out _) && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"answered={answered} mstGy={opp.Graveyard.Contains(mst)} dust={p.TryFindSpellTrap(dust, out _)}");
            }

            {
                // A Trap Card works the same way: the opponent's Dust Tornado on the Player's Set card.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var curse = PlaceSetTrap(engine, p, CurseOfRoyal, 0);
                PlaceSetTrap(engine, p, Mst, 1);
                var dust = PlaceSetTrap(engine, opp, DustTornado, 0);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, dust, fromHand: false);
                var offered = Offers(engine, p, curse, ResponseTiming.ChainResponse);
                var answered = offered && engine.TryActivateChainResponse(p, curse);
                PassAll(engine);
                Check("Curse of Royal: answers the opponent's Dust Tornado (a Trap Card) — negated and destroyed",
                    reached && act && answered && opp.Graveyard.Contains(dust) && p.Graveyard.Contains(curse),
                    $"act={act} offered={offered} answered={answered} {Window(engine)}");
            }

            {
                // A Spell that targets a monster (Offerings to the Doomed) is not answered.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var curse = PlaceSetTrap(engine, p, CurseOfRoyal, 0);
                var off = PutInHand(engine, opp, OfferingsToTheDoomed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, off, fromHand: true);
                Check("Curse of Royal: not offered against a Spell that destroys a monster (Offerings to the Doomed)",
                    reached && act && !Offers(engine, p, curse, ResponseTiming.ChainResponse), Window(engine));
                PassAll(engine);
            }

            {
                // Harpie's Feather Duster destroys Spells/Traps but targets nothing: not answered.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var curse = PlaceSetTrap(engine, p, CurseOfRoyal, 0);
                PlaceSetTrap(engine, p, DustTornado, 1);
                var hfd = PutInHand(engine, opp, HarpiesFeatherDuster);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, hfd, fromHand: true);
                Check("Curse of Royal: not offered against Harpie's Feather Duster (no target)",
                    reached && act && !Offers(engine, p, curse, ResponseTiming.ChainResponse), Window(engine));
                PassAll(engine);
            }

            {
                // The AI's Set Curse of Royal answers the Player's MST on its Set Dust Tornado.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var curse = PlaceSetTrap(engine, opp, CurseOfRoyal, 0);
                var dust = PlaceSetTrap(engine, opp, DustTornado, 1);
                var mst = PutInHand(engine, p, Mst);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, mst, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(dust);
                PassAll(engine);
                Check("Curse of Royal (AI): negates the Player's MST — its Dust Tornado stays Set",
                    reached && act && p.Graveyard.Contains(mst) && opp.Graveyard.Contains(curse) &&
                    opp.TryFindSpellTrap(dust, out _) && !dust.FaceUp,
                    $"act={act} mstGy={p.Graveyard.Contains(mst)} curseGy={opp.Graveyard.Contains(curse)} " +
                    $"dust={opp.TryFindSpellTrap(dust, out _)}");
            }
        }

        // ───────────────────────────── Gravekeeper's Watcher ─────────────────────────────

        static void WatcherDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Graceful Charity: the Player sends Watcher from the hand; Graceful Charity is negated.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var watcher = PutInHand(engine, p, Watcher);
                var gc = PutInHand(engine, opp, GracefulCharity);
                var deck = opp.Deck.Count;
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, gc, fromHand: true);
                var offered = Offers(engine, p, watcher, ResponseTiming.ChainResponse);
                Check("Gravekeeper's Watcher: offered from the hand against the opponent's Graceful Charity",
                    reached && act && offered, $"act={act} {Window(engine)}");
                var legal = offered && engine.CanActivateSpellTrap(p, watcher, fromHand: true);
                var used = legal && engine.TryActivateSpellTrap(p, watcher, fromHand: true);
                PassAll(engine);
                Check("Gravekeeper's Watcher: sent from the hand (cost); Graceful Charity negated and destroyed (no draw)",
                    used && p.Graveyard.Contains(watcher) && !p.Hand.Contains(watcher) && opp.Graveyard.Contains(gc) &&
                    opp.Deck.Count == deck && opp.Hand.Count == 0 && engine.Chain.Count == 0,
                    $"legal={legal} used={used} deck {deck}->{opp.Deck.Count} oppHand={opp.Hand.Count} " +
                    $"gcGy={opp.Graveyard.Contains(gc)}");
            }

            {
                // Card Destruction makes both players discard: answered.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var watcher = PutInHand(engine, p, Watcher);
                PutInHand(engine, p, Celtic);
                var cd = PutInHand(engine, opp, CardDestruction);
                PutInHand(engine, opp, Celtic);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, cd, fromHand: true);
                Check("Gravekeeper's Watcher: offered against Card Destruction (both players discard)",
                    reached && act && Offers(engine, p, watcher, ResponseTiming.ChainResponse), Window(engine));
                PassAll(engine);
            }

            {
                // Pot of Greed makes nobody discard: not answered.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var watcher = PutInHand(engine, p, Watcher);
                var pog = PutInHand(engine, opp, PotOfGreed);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, pog, fromHand: true);
                Check("Gravekeeper's Watcher: not offered against Pot of Greed",
                    reached && act && !Offers(engine, p, watcher, ResponseTiming.ChainResponse), Window(engine));
                PassAll(engine);
            }

            {
                // The AI's Watcher answers the Player's Graceful Charity; the Player's own Card Destruction is not
                // answerable by the Player's Watcher; in the open state Watcher cannot be activated.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var aiWatcher = PutInHand(engine, opp, Watcher);
                var myWatcher = PutInHand(engine, p, Watcher);
                var gc = PutInHand(engine, p, GracefulCharity);
                var deck = p.Deck.Count;
                engine.NotifyPublic();
                var open = engine.CanActivateSpellTrap(p, myWatcher, fromHand: true);
                var act = engine.TryActivateSpellTrap(p, gc, fromHand: true);
                PassAll(engine);
                Check("Gravekeeper's Watcher: not activatable in the open state (Main Phase, no Chain)",
                    reached && !open);
                Check("Gravekeeper's Watcher (AI): negates the Player's Graceful Charity",
                    reached && act && opp.Graveyard.Contains(aiWatcher) && p.Graveyard.Contains(gc) &&
                    p.Deck.Count == deck && p.Hand.Contains(myWatcher),
                    $"act={act} aiWatcherGy={opp.Graveyard.Contains(aiWatcher)} deck {deck}->{p.Deck.Count}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var myWatcher = PutInHand(engine, p, Watcher);
                var cd = PutInHand(engine, p, CardDestruction);
                PutInHand(engine, opp, Celtic);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, cd, fromHand: true);
                Check("Gravekeeper's Watcher: never offered against its controller's own activation",
                    reached && act && !Offers(engine, p, myWatcher, ResponseTiming.ChainResponse), Window(engine));
                PassAll(engine);
            }
        }

        // ───────────────────────────── Charm of Shabti ─────────────────────────────

        static void CharmOfShabtiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Blue-Eyes attacks the Player's Gravekeeper's Spear Soldier: Charm of Shabti in the attack
                // window — the Soldier survives, the Player takes the battle damage.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var spear = PlaceMonster(engine, p, SpearSoldier, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var charm = PutInHand(engine, p, CharmOfShabti);
                engine.NotifyPublic();
                var lp = p.LifePoints;
                var attacked = engine.TryAttack(opp, bewd, spear);
                var offered = Offers(engine, p, charm, ResponseTiming.AttackDeclared);
                Check("Charm of Shabti: offered from the hand when the opponent attacks",
                    reached && attacked && offered, $"attacked={attacked} {Window(engine)}");
                var used = offered && engine.TryActivateSpellTrap(p, charm, fromHand: true);
                var protectedSpear = TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(engine, spear);
                var protectedCeltic = TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(engine, celtic);
                DrainCombat(engine);
                Check("Charm of Shabti: discarded; the Gravekeeper's survives the battle (1500 damage taken)",
                    used && p.Graveyard.Contains(charm) && p.TryFindMonster(spear, out _) && p.LifePoints == lp - 1500 &&
                    opp.TryFindMonster(bewd, out _),
                    $"used={used} spear={p.TryFindMonster(spear, out _)} lp {lp}->{p.LifePoints} {Window(engine)}");
                Check("Charm of Shabti: covers only \"Gravekeeper's\" monsters",
                    protectedSpear && !protectedCeltic, $"spear={protectedSpear} celtic={protectedCeltic}");
            }

            {
                // The opponent enters their Battle Phase: the Player's open-state window offers Charm of Shabti.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var charm = PutInHand(engine, p, CharmOfShabti);
                PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                engine.TryEnterBattlePhase(opp);
                Check("Charm of Shabti: offered at the start of the opponent's Battle Phase",
                    reached && Offers(engine, p, charm, ResponseTiming.OpponentOpenState), Window(engine));
                PassAll(engine);
            }

            {
                // The Player's own Main Phase 1: activated (Spell Speed 2, the opponent may respond); the protection
                // covers its own attack this turn, and ends with the turn. Not in Main Phase 2.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var spear = PlaceMonster(engine, p, SpearSoldier, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var charm = PutInHand(engine, p, CharmOfShabti);
                var spare = PutInHand(engine, p, CharmOfShabti);
                engine.NotifyPublic();
                var legal = engine.CanActivateSpellTrap(p, charm, fromHand: true);
                var used = legal && engine.TryActivateSpellTrap(p, charm, fromHand: true);
                PassAll(engine);
                var prot = TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(engine, spear);
                Check("Charm of Shabti: activated in the Player's Main Phase 1 (discarded, protection registered)",
                    reached && legal && used && p.Graveyard.Contains(charm) && prot && engine.Chain.Count == 0,
                    $"legal={legal} used={used} prot={prot} chain={Chain(engine)}");
                var lp = p.LifePoints;
                var attacked = Attack(engine, p, spear, bewd);
                Check("Charm of Shabti: the Player's Gravekeeper's attacks Blue-Eyes and survives",
                    attacked && p.TryFindMonster(spear, out _) && p.LifePoints == lp - 1500,
                    $"attacked={attacked} spear={p.TryFindMonster(spear, out _)} lp {lp}->{p.LifePoints}");
                engine.TryEnterMainPhase2(p);
                PassAll(engine);
                Check("Charm of Shabti: not activatable in Main Phase 2",
                    engine.Phase == DuelPhase.Main2 && !engine.CanActivateSpellTrap(p, spare, fromHand: true),
                    $"phase={engine.Phase}");
                var next = NextMain1Of(engine, opp);
                Check("Charm of Shabti: the protection ends with the turn",
                    next && !TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(engine, spear),
                    $"next={next}");
            }

            {
                // The AI discards its Charm of Shabti when the Player's Blue-Eyes attacks its Gravekeeper's.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var spear = PlaceMonster(engine, opp, SpearSoldier, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var charm = PutInHand(engine, opp, CharmOfShabti);
                engine.NotifyPublic();
                var lp = opp.LifePoints;
                var attacked = Attack(engine, p, bewd, spear);
                Check("Charm of Shabti (AI): used against an attack that would destroy its Gravekeeper's",
                    reached && attacked && opp.Graveyard.Contains(charm) && opp.TryFindMonster(spear, out _) &&
                    opp.LifePoints == lp - 1500,
                    $"attacked={attacked} charmGy={opp.Graveyard.Contains(charm)} spear={opp.TryFindMonster(spear, out _)}");
            }
        }

        // ───────────────────────────── Great Dezard ─────────────────────────────

        static void GreatDezardDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 0 monsters destroyed by battle: the opponent's Book of Moon on Great Dezard resolves.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var dezard = PlaceMonster(engine, p, GreatDezard, 0, BattlePosition.Attack, true);
                var bom = PutInHand(engine, opp, BookOfMoon);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(opp, bom, fromHand: true);
                var chainLen = engine.Chain.Count;
                PassAll(engine);
                Check("Great Dezard (0 kills): no negation — Book of Moon turns it face-down",
                    reached && act && chainLen <= 1 && !dezard.FaceUp && opp.Graveyard.Contains(bom),
                    $"act={act} chain={chainLen} faceUp={dezard.FaceUp}");
            }

            {
                // 1 monster destroyed by battle (a real battle), then the opponent's Book of Moon targets it: a mandatory
                // Chain Link 2 negates and destroys Book of Moon. 2 kills: Tribute it for Fushioh Richie from the Deck.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var dezard = PlaceMonster(engine, p, GreatDezard, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var attacked = Attack(engine, p, dezard, celtic);
                var kills = TargetNegationsAndHandQuickEffectsRuntime.BattleDestructionsOf(dezard);
                Check("Great Dezard: destroying a monster by battle counts 1",
                    reached && attacked && opp.Graveyard.Contains(celtic) && kills == 1, $"kills={kills}");
                engine.TryEnterMainPhase2(p);
                PassAll(engine);
                Check("Great Dezard: its ignition needs 2 monsters destroyed by battle",
                    !engine.CanActivateSpellTrap(p, dezard, fromHand: false));

                var oppMain = NextMain1Of(engine, opp);
                var bom = PutInHand(engine, opp, BookOfMoon);
                engine.NotifyPublic();
                var log = Capture(engine);
                var act = engine.TryActivateSpellTrap(opp, bom, fromHand: true);
                PassAll(engine);
                // Nobody could respond, so the Chain resolved inside the activation: read the link from the log.
                var forced = log.Exists(l => l.Contains("Great Dezard's effect activates (Chain Link 2)"));
                var desc = string.Join(" | ", log.Where(l => l.Contains("Chain Link") || l.Contains("negate")));
                Check("Great Dezard (1 kill): a mandatory Chain Link 2 negates Book of Moon and destroys it",
                    oppMain && act && forced && dezard.FaceUp && p.TryFindMonster(dezard, out _) &&
                    opp.Graveyard.Contains(bom) && engine.Chain.Count == 0,
                    $"oppMain={oppMain} act={act} chain={desc} faceUp={dezard.FaceUp}");

                var pMain = NextMain1Of(engine, p);
                var celtic2 = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var attacked2 = Attack(engine, p, dezard, celtic2);
                kills = TargetNegationsAndHandQuickEffectsRuntime.BattleDestructionsOf(dezard);
                var handRichie = PutInHand(engine, p, FushiohRichie);
                p.Deck.Insert(0, FushiohRichie);
                var deck = p.Deck.Count;
                engine.TryEnterMainPhase2(p);
                PassAll(engine);
                var legal = engine.CanActivateSpellTrap(p, dezard, fromHand: false);
                var used = legal && engine.TryActivateSpellTrap(p, dezard, fromHand: false);
                var pend = engine.PendingActivation;
                var picks = pend?.LegalTargets?.ToList() ?? new List<CardInstance>();
                var deckPick = picks.FirstOrDefault(c => c != null && c != handRichie);
                var picked = deckPick != null && engine.TrySelectEffectTarget(deckPick);
                PassAll(engine);
                var richie = p.MonstersOnField().FirstOrDefault(m => m?.CardId == FushiohRichie);
                Check("Great Dezard (2 kills): Tribute it — the Player picks the Deck copy of Fushioh Richie",
                    pMain && attacked2 && kills == 2 && legal && used && picks.Count == 2 && picked &&
                    p.Graveyard.Contains(dezard) && richie != null && richie.FaceUp && richie.ProcedureCompleted &&
                    p.Hand.Contains(handRichie) && p.Deck.Count == deck - 1,
                    $"pMain={pMain} kills={kills} legal={legal} used={used} picks={picks.Count} picked={picked} " +
                    $"richie={richie != null} deck {deck}->{p.Deck.Count}");
            }

            {
                // Its controller's own Spell targeting it is negated too (Lua: no player check).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var dezard = PlaceMonster(engine, p, GreatDezard, 0, BattlePosition.Attack, true);
                TargetNegationsAndHandQuickEffectsRuntime.NoteBattleDestroying(engine, dezard);
                var bom = PutInHand(engine, p, BookOfMoon);
                engine.NotifyPublic();
                var log = Capture(engine);
                var act = engine.TryActivateSpellTrap(p, bom, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(dezard);
                PassAll(engine);
                Check("Great Dezard (1 kill): negates its own controller's Book of Moon on it",
                    reached && act && dezard.FaceUp && p.Graveyard.Contains(bom) &&
                    log.Exists(l => l.Contains("Great Dezard's effect activates (Chain Link 2)")),
                    $"act={act} faceUp={dezard.FaceUp} bomGy={p.Graveyard.Contains(bom)}");
            }

            {
                // Turned face-down, it loses its count.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var dezard = PlaceMonster(engine, p, GreatDezard, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                Attack(engine, p, dezard, celtic);
                var before = TargetNegationsAndHandQuickEffectsRuntime.BattleDestructionsOf(dezard);
                dezard.FaceUp = false;
                dezard.FaceUp = true;
                Check("Great Dezard: the count is lost when it is turned face-down",
                    reached && before == 1 && TargetNegationsAndHandQuickEffectsRuntime.BattleDestructionsOf(dezard) == 0,
                    $"before={before}");
            }

            {
                // The AI with 2 kills: its ignition Special Summons Fushioh Richie (auto-pick).
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var opp = engine.Opponent;
                var dezard = PlaceMonster(engine, opp, GreatDezard, 0, BattlePosition.Attack, true);
                TargetNegationsAndHandQuickEffectsRuntime.NoteBattleDestroying(engine, dezard);
                TargetNegationsAndHandQuickEffectsRuntime.NoteBattleDestroying(engine, dezard);
                opp.Deck.Insert(0, FushiohRichie);
                engine.NotifyPublic();
                var used = engine.CanActivateSpellTrap(opp, dezard, fromHand: false) &&
                           engine.TryActivateSpellTrap(opp, dezard, fromHand: false);
                PassAll(engine);
                var richie = opp.MonstersOnField().FirstOrDefault(m => m?.CardId == FushiohRichie);
                Check("Great Dezard (AI): Tribute → Fushioh Richie from the Deck, properly summoned",
                    reached && used && richie != null && richie.ProcedureCompleted && opp.Graveyard.Contains(dezard),
                    $"used={used} richie={richie != null}");
            }
        }

        // ───────────────────────────── Fushioh Richie ─────────────────────────────

        static void FushiohRichieDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Its summoning condition: no Normal Summon, no Special Summon by another card from the hand, Deck or (not
                // properly summoned) GY; a properly summoned copy may be revived.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var hand = PutInHand(engine, p, FushiohRichie);
                var gy = InGy(engine, p, FushiohRichie);
                var reborn = PutInHand(engine, p, MonsterReborn);
                engine.NotifyPublic();
                var ns = engine.CanNormalSummonOrSet(p, hand);
                var fromHand = SpecialSummonLegality.CanBeSpecialSummoned(engine, p, hand, null, SummonOrigin.Hand);
                var fromGy = SpecialSummonLegality.CanBeSpecialSummoned(engine, p, gy, null, SummonOrigin.Graveyard);
                var rebornLegal = engine.CanActivateSpellTrap(p, reborn, fromHand: true);
                gy.ProcedureCompleted = true;
                var revived = SpecialSummonLegality.CanBeSpecialSummoned(engine, p, gy, null, SummonOrigin.Graveyard);
                Check("Fushioh Richie: cannot be Normal Summoned; only Great Dezard summons it; revivable once proper",
                    reached && !ns && !fromHand && !fromGy && !rebornLegal && revived,
                    $"ns={ns} hand={fromHand} gy={fromGy} reborn={rebornLegal} revived={revived}");
            }

            {
                // Face-up, no battle count needed: the opponent's Book of Moon on it is negated and destroyed.
                var engine = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var richie = PlaceMonster(engine, p, FushiohRichie, 0, BattlePosition.Attack, true);
                richie.ProcedureCompleted = true;
                var bom = PutInHand(engine, opp, BookOfMoon);
                engine.NotifyPublic();
                var log = Capture(engine);
                var act = engine.TryActivateSpellTrap(opp, bom, fromHand: true);
                PassAll(engine);
                var forced = log.Exists(l => l.Contains("Fushioh Richie's effect activates (Chain Link 2)"));
                Check("Fushioh Richie: a mandatory Chain Link negates Book of Moon targeting it",
                    reached && act && forced && richie.FaceUp && opp.Graveyard.Contains(bom),
                    $"act={act} forced={forced} faceUp={richie.FaceUp}");
            }

            {
                // The AI's face-down Fushioh Richie is attacked and flipped face-up: it revives a Zombie from its GY.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var richie = PlaceMonster(engine, opp, FushiohRichie, 0, BattlePosition.Defense, false);
                richie.ProcedureCompleted = true;
                var skull = InGy(engine, opp, SkullServant);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var attacked = Attack(engine, p, celtic, richie);
                PassAll(engine);
                Check("Fushioh Richie (AI): flipped face-up by an attack → Special Summons a Zombie from its GY",
                    reached && attacked && richie.FaceUp && opp.TryFindMonster(skull, out _),
                    $"attacked={attacked} faceUp={richie.FaceUp} skull={opp.TryFindMonster(skull, out _)}");
            }

            {
                // Flip it face-down (its ignition), then Flip Summon it next turn: it Special Summons a Zombie from the GY.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var opp = engine.Opponent;
                var richie = PlaceMonster(engine, p, FushiohRichie, 0, BattlePosition.Attack, true);
                richie.ProcedureCompleted = true;
                var skull = InGy(engine, p, SkullServant);
                InGy(engine, p, Celtic);
                engine.NotifyPublic();
                var down = engine.CanActivateSpellTrap(p, richie, fromHand: false) &&
                           engine.TryActivateSpellTrap(p, richie, fromHand: false);
                PassAll(engine);
                var faceDown = !richie.FaceUp && richie.Position == BattlePosition.Defense;
                var again = engine.CanActivateSpellTrap(p, richie, fromHand: false);
                var next = NextMain1Of(engine, opp) && NextMain1Of(engine, p);
                var flipped = next && engine.TryFlipSummon(p, richie);
                var pend = engine.PendingActivation;
                var targets = pend?.LegalTargets?.ToList() ?? new List<CardInstance>();
                var picked = targets.Contains(skull) && engine.TrySelectEffectTarget(skull);
                PassAll(engine);
                Check("Fushioh Richie: flips itself face-down once per turn",
                    reached && down && faceDown && !again, $"down={down} faceDown={faceDown} again={again}");
                Check("Fushioh Richie: flipped face-up → Special Summon 1 Zombie from the GY (Zombies only)",
                    flipped && richie.FaceUp && picked && targets.Count == 1 && p.TryFindMonster(skull, out _),
                    $"next={next} flipped={flipped} targets={targets.Count} picked={picked} " +
                    $"skull={p.TryFindMonster(skull, out _)}");
            }
        }
    }
}
