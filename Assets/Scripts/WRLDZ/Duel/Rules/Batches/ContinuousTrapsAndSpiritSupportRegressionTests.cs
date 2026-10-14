using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// ContinuousTrapsAndSpiritSupport batch (v161): Fatal Abacus, Life Absorbing Machine, Ominous Fortunetelling,
    /// Fengsheng Mirror, Spirit's Invitation and Spiritual Energy Settle Machine. Compile shapes, mini-duels through the
    /// real engine entry points (TryActivateSpellTrap, TryNormalSummon, TryAttack, EndTurn through the End / Draw /
    /// Standby Phases, TrySelectEffectTarget / CancelEffectTargeting) with a real Spirit (Yata-Garasu), and near misses.
    /// Runtime: <see cref="ContinuousTrapsAndSpiritSupportRuntime"/>.
    /// </summary>
    public static class ContinuousTrapsAndSpiritSupportRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int FatalAbacus = 77910045;
        const int LifeAbsorbingMachine = 14318794;
        const int OminousFortunetelling = 56995655;
        const int FengshengMirror = 37406863;
        const int SpiritsInvitation = 92394653;
        const int SettleMachine = 99173029;

        const int YataGarasu = 3078576;       // Spirit Lv2 200/100
        const int HinoKaguTsuchi = 75745607;  // Spirit Lv8 2800/2900
        const int Celtic = 91152256;          // 1400/1200
        const int OjamaGreen = 12482652;      // 0/1000
        const int DarkHole = 53129443;
        const int Mst = 5318639;
        const int PrematureBurial = 70828912; // pay 800 LP (cost)
        const int PotOfGreed = 55144522;
        const int SkullInvitation = 98139712;
        const int MindOnAir = 66690411;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("ContinuousTrapsAndSpiritSupport: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            AbacusDuels(db, pDeck, aDeck, Check);
            MachineDuels(db, pDeck, aDeck, Check);
            FortuneDuels(db, pDeck, aDeck, Check);
            FengshengDuels(db, pDeck, aDeck, Check);
            InvitationDuels(db, pDeck, aDeck, Check);
            SettleDuels(db, pDeck, aDeck, Check);
            NearMisses(db, Check);
            MindOnAirDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static bool Has(CompiledCardProgram prog, EffectActionKind action) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);

        /// <summary>The player's Main Phase 1 on turn 3+ (a Battle Phase is allowed), boards and hands empty.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                      engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static void MindOnAirDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {

            // Compile shape: the whole text is one continuous rule — no cost, no target.
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(MindOnAir));
                Check("Mind on Air: FullyCompiled — a Continuous Trap, opponent plays with their hand revealed; Implemented",
                    prog != null && prog.FullyCompiled &&
                    CardEffectStatus.Classify(db.Get(MindOnAir)) == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.OpponentMustPlayWithHandRevealed &&
                    prog.ClauseList[0].Timing == EffectTiming.Activate &&
                    prog.ClauseList[0].Side == EffectSide.Controller &&
                    prog.ClauseList[0].StaysOnField && !prog.ClauseList[0].RequiresTargetChoice &&
                    prog.ClauseList[0].PayLpAmount == 0 && prog.ClauseList[0].RequiresDiscardCost == false,
                    prog == null ? "null" : string.Join(" | ", prog.UnparsedFragments ?? Array.Empty<string>()));
            }

            // Face-up on the field: the controller's opponent has their hand revealed.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PutInHand(engine, p, Celtic);
                PutInHand(engine, p, PotOfGreed);
                PutInHand(engine, o, Celtic);
                PutInHand(engine, o, PotOfGreed);
                var air = PlaceFaceUpSpellTrap(engine, p, MindOnAir, 0);
                engine.NotifyPublic();
                var oppRevealed = WRLDZ.Duel.TextEffects.ContinuousTrapsAndSpiritSupportRuntime
                    .HandRevealed(engine, o);
                var mineHidden = !WRLDZ.Duel.TextEffects.ContinuousTrapsAndSpiritSupportRuntime
                    .HandRevealed(engine, p);
                Check("Mind on Air: while it is face-up the controller's opponent plays with their hand revealed",
                    reached && oppRevealed && mineHidden,
                    $"reached={reached} oppRevealed={oppRevealed} mineRevealed={!mineHidden}");

                // The owner of the hand is the reflected state, not the reader.
                Check("Mind on Air: DuelistState.HandRevealed is set on the opponent, not on the viewer",
                    o.HandRevealed && !p.HandRevealed, $"opp={o.HandRevealed} me={p.HandRevealed}");

                // The Trap leaves the field: the reveal ends (a continuous rule, not a turn-scoped one).
                engine.SendCardToGrave(p, air);
                FieldSpellEffects.RefreshBoard(engine);
                Check("Mind on Air: once the Trap leaves the field the hand is hidden again",
                    !o.HandRevealed && !p.HandRevealed && !WRLDZ.Duel.TextEffects.ContinuousTrapsAndSpiritSupportRuntime
                        .HandRevealed(engine, o),
                    $"opp={o.HandRevealed} me={p.HandRevealed}");
            }

            // A negated Mind on Air reveals nothing.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var air = PlaceFaceUpSpellTrap(engine, p, MindOnAir, 0);
                air.IsNegated = true;
                engine.NotifyPublic();
                FieldSpellEffects.RefreshBoard(engine);
                Check("Mind on Air: a negated Trap reveals nothing",
                    reached && !o.HandRevealed, $"opp={o.HandRevealed}");
            }

            // The opponent's own Mind on Air reveals the controller's hand.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, MindOnAir, 0);
                engine.NotifyPublic();
                Check("Mind on Air: the opponent's copy reveals the player's hand",
                    reached && p.HandRevealed && !o.HandRevealed,
                    $"me={p.HandRevealed} opp={o.HandRevealed}");
            }
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance Token(DuelEngine engine, string name) =>
            engine.PendingActivation?.LegalTargets.FirstOrDefault(t => t?.Def != null && t.Def.name == name);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(FatalAbacus));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Fatal Abacus: one continuous burn-per-monster-sent-from-the-field clause (500)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy && c.Amount == 500 &&
                    !c.MakesChainLink && CardEffectStatus.Classify(db.Get(FatalAbacus)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(LifeAbsorbingMachine));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Life Absorbing Machine: one Standby gain-half-of-last-turn's-LP-costs clause",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.StandbyPhase && c.Action == EffectActionKind.GainHalfLpCostsPaidLastTurn &&
                    c.Side == EffectSide.Controller,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(OminousFortunetelling));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Ominous Fortunetelling: one Standby call-the-type burn clause (700)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.StandbyPhase &&
                    c.Action == EffectActionKind.CallTypeOfRandomOpponentHandCardBurn && c.Amount == 700,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(FengshengMirror));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Fengsheng Mirror: one Activate look-and-discard-a-Spirit clause, no target",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.LookAtOpponentHandDiscardChosenSpirit &&
                    !c.RequiresTargetChoice && c.Zone == EffectZoneFilter.None,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(SpiritsInvitation));
                Check("Spirit's Invitation: Spirit-return trigger marker + Standby pay 500 LP or destroy",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 &&
                    prog.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                                c.Action == EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand) &&
                    prog.ClauseList.Exists(c => c.Timing == EffectTiming.StandbyPhase &&
                                                c.Action == EffectActionKind.PayLpOrDestroyThis && c.PayLpAmount == 500) &&
                    CardEffectStatus.Classify(db.Get(SpiritsInvitation)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(SettleMachine));
                Check("Spiritual Energy Settle Machine: no-return marker + End Phase discard-or-destroy + leave-field return",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 3 &&
                    prog.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                                c.Action == EffectActionKind.SpiritsDoNotReturnWhileThisFaceUp) &&
                    prog.ClauseList.Exists(c => c.Timing == EffectTiming.EndPhase &&
                                                c.Action == EffectActionKind.EndPhaseDiscardOrDestroyThis) &&
                    prog.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                                c.Action == EffectActionKind.ReturnFaceUpSpiritsWhenThisLeavesField),
                    Describe(prog));
            }
            {
                // Skull Invitation keeps its own every-card burn (the Abacus atom does not take it).
                var prog = CardTextEffectCompiler.Compile(db.Get(SkullInvitation));
                Check("Skull Invitation: still its every-card burn, not the Abacus monster-only burn",
                    prog != null && prog.FullyCompiled && Has(prog, EffectActionKind.InflictDamageToOwnerPerCardSentToGy) &&
                    !Has(prog, EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy),
                    Describe(prog));
            }
        }

        // ───────────────────────────── Fatal Abacus ─────────────────────────────

        static void AbacusDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Activated from a Set; Dark Hole sends 1 of yours and 2 of theirs: 500 to you, 1000 to them.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var set = PlaceSetTrap(engine, p, FatalAbacus, 0);
                var act = engine.TryActivateSpellTrap(p, set, fromHand: false);
                Settle(engine);
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, OjamaGreen, 1, BattlePosition.Defense, false);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                var hole = PutInHand(engine, p, DarkHole);
                var used = engine.TryActivateSpellTrap(p, hole, true);
                Settle(engine);
                Check("Fatal Abacus: Dark Hole destroys 1 of yours and 2 of theirs (one Set) — 500 to you, 1000 to them",
                    reached && act && set.FaceUp && used && p.LifePoints == pLp - 500 && o.LifePoints == oLp - 1000,
                    $"reached={reached} act={act} faceUp={set.FaceUp} used={used} p={pLp}->{p.LifePoints} o={oLp}->{o.LifePoints}");
            }

            {
                // Destroyed by battle: the owner of the destroyed monster takes 500 too.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, FatalAbacus, 0);
                var mine = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                engine.TryEnterBattlePhase(p);
                Settle(engine);
                var atk = engine.TryAttack(p, mine, ojama);
                DrainCombat(engine);
                engine.NotifyPublic();
                Check("Fatal Abacus (opponent's): a monster destroyed by battle costs its owner 500 (plus the battle damage)",
                    reached && atk && o.Graveyard.Contains(ojama) && o.LifePoints == oLp - 1400 - 500 &&
                    p.LifePoints == pLp,
                    $"atk={atk} inGy={o.Graveyard.Contains(ojama)} o={oLp}->{o.LifePoints} p={pLp}->{p.LifePoints}");
            }

            {
                // Fail closed on state: a Set (not activated) Abacus does nothing; a card sent from the hand is no monster
                // from the field.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSetTrap(engine, p, FatalAbacus, 0);
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                var hole = PutInHand(engine, p, DarkHole);
                engine.TryActivateSpellTrap(p, hole, true);
                Settle(engine);
                var setNoDamage = p.LifePoints == pLp && o.LifePoints == oLp;

                var engine2 = PlayerMain1(db, pDeck, aDeck, out _);
                PlaceFaceUpSpellTrap(engine2, engine2.Player, FatalAbacus, 0);
                var discard = PutInHand(engine2, engine2.Player, Celtic);
                var lp2 = engine2.Player.LifePoints;
                MonsterEffects.DiscardFromHandByInstance(engine2.Player, discard);
                engine2.NotifyPublic();
                Check("Fatal Abacus: no damage while Set, none for a monster discarded from the hand",
                    reached && setNoDamage && engine2.Player.LifePoints == lp2 && engine2.Player.Graveyard.Contains(discard),
                    $"set p={pLp}->{p.LifePoints} o={oLp}->{o.LifePoints} hand-discard lp={lp2}->{engine2.Player.LifePoints}");
            }
        }

        // ───────────────────────────── Life Absorbing Machine ─────────────────────────────

        static void MachineDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, LifeAbsorbingMachine, 0);
                var ghost = engine.CreateCardInstance(Celtic);
                p.Graveyard.Add(ghost);
                var burial = PutInHand(engine, p, PrematureBurial);
                var lp0 = p.LifePoints;
                var used = engine.TryActivateSpellTrap(p, burial, true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(ghost);
                Settle(engine);
                var paid = lp0 - p.LifePoints;
                // An odd LP cost (half rounded up), effect damage (not a cost) in the same turn.
                engine.PayLifePoints(p, 301, "Cost: test payment of 301 LP.");
                engine.ApplyEffectDamage(p, 300, "test burn");
                EndTurn(engine); // the opponent's turn
                var oppTurn = engine.TurnPlayer == o;
                // A cost paid during the opponent's turn is not "your" turn's payment.
                engine.PayLifePoints(p, 1000, "Cost: test payment during the opponent's turn.");
                var beforeStandby = p.LifePoints;
                EndTurn(engine); // the player's turn: Standby Phase
                var gained = p.LifePoints - beforeStandby;
                Check("Life Absorbing Machine: gains half of each LP cost paid during your last turn (800 → 400, 301 → 151)",
                    reached && used && paid == 800 && oppTurn && engine.TurnPlayer == p && gained == 400 + 151,
                    $"used={used} paid={paid} oppTurn={oppTurn} gained={gained} turnPlayer={engine.TurnPlayer?.Name}");

                // The next round: nothing paid during that turn → nothing gained.
                EndTurn(engine);
                var before2 = p.LifePoints;
                EndTurn(engine);
                Check("Life Absorbing Machine: no LP paid during your last turn → no gain",
                    engine.TurnPlayer == p && p.LifePoints == before2,
                    $"lp={before2}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Ominous Fortunetelling ─────────────────────────────

        static void FortuneDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (call, right) in new[] { ("Monster", true), ("Trap", false) })
            {
                // A human calls in their Standby Phase, which waits for the call.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, OminousFortunetelling, 0);
                EndTurn(engine); // the opponent's turn
                o.Hand.Clear();
                PutInHand(engine, o, Celtic);
                var oLp = o.LifePoints;
                EndTurn(engine); // the player's Standby Phase
                var pend = engine.PendingActivation;
                var standby = engine.Phase == DuelPhase.Standby && engine.TurnPlayer == p;
                var names = pend == null ? "" : string.Join(",", pend.LegalTargets.Select(t => t.Name));
                var token = Token(engine, call);
                var ok = token != null && engine.TrySelectEffectTarget(token);
                Check($"Ominous Fortunetelling: the human calls {call} in the Standby Phase — " +
                      (right ? "right: 700 damage" : "wrong: no damage") + ", then Main Phase 1",
                    reached && standby && names == "Monster,Spell,Trap" && ok &&
                    o.LifePoints == (right ? oLp - 700 : oLp) && engine.Phase == DuelPhase.Main1 &&
                    engine.PendingActivation == null,
                    $"standby={standby} tokens={names} ok={ok} o={oLp}->{o.LifePoints} phase={engine.Phase}");
            }

            foreach (var (deckSpells, handId, right) in new[] { (0, PotOfGreed, true), (3, Celtic, false) })
            {
                // The AI calls the type most common among the opponent's cards.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, o, OminousFortunetelling, 0);
                p.Deck.Clear();
                for (var i = 0; i < deckSpells; i++) p.Deck.Add(PotOfGreed);
                PutInHand(engine, p, handId);
                var pLp = p.LifePoints;
                EndTurn(engine); // the AI's Standby Phase
                Check("Ominous Fortunetelling (AI): " + (right ? "a right call burns 700" : "a wrong call burns nothing") +
                      " and the turn goes on",
                    reached && engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1 &&
                    engine.PendingActivation == null && p.LifePoints == (right ? pLp - 700 : pLp),
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} p={pLp}->{p.LifePoints}");
            }

            {
                // The opponent's empty hand: nothing to call, no prompt.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, OminousFortunetelling, 0);
                EndTurn(engine);
                o.Hand.Clear();
                var oLp = o.LifePoints;
                EndTurn(engine);
                Check("Ominous Fortunetelling: the opponent has no hand — no call, no damage",
                    reached && engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1 &&
                    engine.PendingActivation == null && o.LifePoints == oLp,
                    $"phase={engine.Phase} pending={engine.PendingActivation != null} o={oLp}->{o.LifePoints}");
            }
        }

        // ───────────────────────────── Fengsheng Mirror ─────────────────────────────

        static void FengshengDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // One Spirit: discarded with no prompt; the other card stays.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yata = PutInHand(engine, o, YataGarasu);
                var celtic = PutInHand(engine, o, Celtic);
                var mirror = PutInHand(engine, p, FengshengMirror);
                var used = engine.TryActivateSpellTrap(p, mirror, true);
                Settle(engine);
                Check("Fengsheng Mirror: the lone Spirit in the opponent's hand is discarded",
                    reached && used && o.Graveyard.Contains(yata) && o.Hand.Contains(celtic) &&
                    !engine.IsAwaitingEffectTarget && p.Graveyard.Contains(mirror),
                    $"used={used} yataGy={o.Graveyard.Contains(yata)} celticHand={o.Hand.Contains(celtic)}");
            }

            {
                // Two Spirits: the activator picks (CardInOppHand); no Spirit: the hand is only looked at.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var yata = PutInHand(engine, o, YataGarasu);
                var hino = PutInHand(engine, o, HinoKaguTsuchi);
                var celtic = PutInHand(engine, o, Celtic);
                var mirror = PutInHand(engine, p, FengshengMirror);
                var used = engine.TryActivateSpellTrap(p, mirror, true);
                Settle(engine);
                var pend = engine.PendingActivation;
                var legal = pend == null ? "" : string.Join(",", pend.LegalTargets.Select(t => t.Name).OrderBy(n => n));
                var celticRefused = !engine.TrySelectEffectTarget(celtic);
                var picked = engine.TrySelectEffectTarget(yata);
                Settle(engine);
                Check("Fengsheng Mirror: with two Spirits the activator picks one (only Spirits are offered)",
                    reached && used && pend != null && pend.TargetKind == EffectTargetKind.CardInOppHand &&
                    legal == "Hino-Kagu-Tsuchi,Yata-Garasu" && celticRefused && picked &&
                    o.Graveyard.Contains(yata) && o.Hand.Contains(hino) && o.Hand.Contains(celtic),
                    $"used={used} legal={legal} picked={picked} yataGy={o.Graveyard.Contains(yata)}");

                var engine2 = PlayerMain1(db, pDeck, aDeck, out _);
                var c2 = PutInHand(engine2, engine2.Opponent, Celtic);
                var m2 = PutInHand(engine2, engine2.Player, FengshengMirror);
                var used2 = engine2.TryActivateSpellTrap(engine2.Player, m2, true);
                Settle(engine2);
                Check("Fengsheng Mirror: no Spirit — activates, nothing is discarded",
                    used2 && engine2.Opponent.Hand.Contains(c2) && engine2.Opponent.Graveyard.Count == 0 &&
                    !engine2.IsAwaitingEffectTarget,
                    $"used={used2} hand={engine2.Opponent.Hand.Count} gy={engine2.Opponent.Graveyard.Count}");
            }

            {
                // The AI activates it: it discards the highest-Level Spirit of the human's hand.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var yata = PutInHand(engine, p, YataGarasu);
                var hino = PutInHand(engine, p, HinoKaguTsuchi);
                var mirror = PutInHand(engine, o, FengshengMirror);
                var used = engine.TryActivateSpellTrap(o, mirror, true);
                Settle(engine);
                Check("Fengsheng Mirror (AI): discards the highest-Level Spirit, no prompt for the human",
                    reached && used && p.Graveyard.Contains(hino) && p.Hand.Contains(yata) && !engine.IsAwaitingEffectTarget,
                    $"reached={reached} used={used} hinoGy={p.Graveyard.Contains(hino)} yataHand={p.Hand.Contains(yata)}");
            }
        }

        // ───────────────────────────── Spirit's Invitation ─────────────────────────────

        static void InvitationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Acceptance: your Yata-Garasu returns in the End Phase → the AI returns its least valuable monster.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var set = PlaceSetTrap(engine, p, SpiritsInvitation, 0);
                var act = engine.TryActivateSpellTrap(p, set, fromHand: false);
                Settle(engine);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 1, BattlePosition.Defense, true);
                var yata = PutInHand(engine, p, YataGarasu);
                var ns = engine.TryNormalSummon(p, yata, false);
                Settle(engine);
                var onField = p.TryFindMonster(yata, out _);
                EndTurn(engine);
                Check("Spirit's Invitation: Yata-Garasu returns in the End Phase → the opponent returns 1 monster they control",
                    reached && act && set.FaceUp && ns && onField && p.Hand.Contains(yata) && o.Hand.Contains(ojama) &&
                    o.TryFindMonster(celtic, out _) && engine.TurnPlayer == o,
                    $"act={act} ns={ns} onField={onField} yataHand={p.Hand.Contains(yata)} ojamaHand={o.Hand.Contains(ojama)} " +
                    $"celticField={o.TryFindMonster(celtic, out _)} turn={engine.TurnPlayer?.Name}");
            }

            {
                // The AI's Invitation: the human picks which of their monsters returns; the End Phase waits for it.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                PlaceFaceUpSpellTrap(engine, o, SpiritsInvitation, 0);
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, p, OjamaGreen, 1, BattlePosition.Defense, false);
                var yata = PutInHand(engine, o, YataGarasu);
                var ns = engine.TryNormalSummon(o, yata, false);
                Settle(engine);
                EndTurn(engine);
                var pend = engine.PendingActivation;
                var paused = engine.TurnPlayer == o && engine.Phase == DuelPhase.End;
                var asks = pend != null && pend.Controller == p && pend.TargetKind == EffectTargetKind.YourMonster &&
                           pend.LegalTargets.Count == 2;
                var picked = engine.TrySelectEffectTarget(celtic);
                Settle(engine);
                Check("Spirit's Invitation (AI's): the human picks the monster to return (face-down too); the turn then passes",
                    reached && ns && o.Hand.Contains(yata) && paused && asks && picked && p.Hand.Contains(celtic) &&
                    p.TryFindMonster(ojama, out _) && engine.TurnPlayer == p,
                    $"ns={ns} yataHand={o.Hand.Contains(yata)} paused={paused} asks={asks} picked={picked} " +
                    $"celticHand={p.Hand.Contains(celtic)} turn={engine.TurnPlayer?.Name}");
            }

            {
                // Only a face-up Spirit counts: a Set Spirit or a face-up non-Spirit bounced to your hand does not trigger.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceFaceUpSpellTrap(engine, p, SpiritsInvitation, 0);
                var theirs = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var setYata = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Defense, false);
                var mine = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                engine.ReturnCardToHand(setYata);
                engine.ReturnCardToHand(mine);
                engine.NotifyPublic();
                var upYata = PlaceMonster(engine, p, YataGarasu, 2, BattlePosition.Attack, true);
                engine.ReturnCardToHand(upYata);
                engine.NotifyPublic();
                Check("Spirit's Invitation: a Set Spirit or a non-Spirit returned does not trigger; a face-up Spirit does",
                    reached && p.Hand.Contains(setYata) && p.Hand.Contains(mine) && p.Hand.Contains(upYata) &&
                    o.Hand.Contains(theirs) && !o.TryFindMonster(theirs, out _),
                    $"theirsHand={o.Hand.Contains(theirs)}");
            }

            {
                // Negated (Set again or face-down) = no trigger: a face-down Invitation does nothing.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSetTrap(engine, p, SpiritsInvitation, 0);
                var theirs = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var upYata = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Attack, true);
                engine.ReturnCardToHand(upYata);
                engine.NotifyPublic();
                Check("Spirit's Invitation: a Set (not activated) copy does not trigger",
                    reached && p.Hand.Contains(upYata) && o.TryFindMonster(theirs, out _));
            }
        }

        // ───────────────────────────── Spiritual Energy Settle Machine ─────────────────────────────

        static void SettleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Yata stays through the End Phase; the human discards to keep the Machine.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var machine = PutInHand(engine, p, SettleMachine);
                var act = engine.TryActivateSpellTrap(p, machine, true);
                Settle(engine);
                var yata = PutInHand(engine, p, YataGarasu);
                var ns = engine.TryNormalSummon(p, yata, false);
                Settle(engine);
                var fodder = PutInHand(engine, p, Celtic);
                EndTurn(engine);
                var pend = engine.PendingActivation;
                var asks = pend != null && pend.Controller == p && pend.TargetKind == EffectTargetKind.DiscardFromHand &&
                           engine.Phase == DuelPhase.End && pend.LegalTargets.Contains(fodder);
                var picked = engine.TrySelectEffectTarget(fodder);
                Settle(engine);
                Check("Settle Machine: Yata-Garasu stays in the End Phase; the human discards 1 card to keep the Machine",
                    reached && act && machine.FaceUp && ns && asks && picked && p.TryFindMonster(yata, out _) &&
                    p.Graveyard.Contains(fodder) && p.TryFindSpellTrap(machine, out _) &&
                    engine.TurnPlayer == engine.Opponent,
                    $"act={act} ns={ns} asks={asks} picked={picked} yataField={p.TryFindMonster(yata, out _)} " +
                    $"fodderGy={p.Graveyard.Contains(fodder)} machine={p.TryFindSpellTrap(machine, out _)}");
            }

            {
                // Cancel = do not discard: the Machine is destroyed and the face-up Spirits return at once.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var machine = PlaceFaceUpSpellTrap(engine, p, SettleMachine, 0);
                var yata = PutInHand(engine, p, YataGarasu);
                var ns = engine.TryNormalSummon(p, yata, false);
                Settle(engine);
                var theirSpirit = PlaceMonster(engine, o, HinoKaguTsuchi, 0, BattlePosition.Attack, true);
                var fodder = PutInHand(engine, p, Celtic);
                EndTurn(engine);
                var asked = engine.PendingActivation != null;
                var cancelled = engine.CancelEffectTargeting();
                Settle(engine);
                Check("Settle Machine: Cancel at the upkeep destroys it; every face-up Spirit returns to its owner's hand",
                    reached && ns && asked && cancelled && p.Graveyard.Contains(machine) && p.Hand.Contains(yata) &&
                    p.Hand.Contains(fodder) && o.Hand.Contains(theirSpirit) && engine.TurnPlayer == o,
                    $"ns={ns} asked={asked} cancelled={cancelled} machineGy={p.Graveyard.Contains(machine)} " +
                    $"yataHand={p.Hand.Contains(yata)} hinoHand={o.Hand.Contains(theirSpirit)} turn={engine.TurnPlayer?.Name}");
            }

            {
                // Mystical Space Typhoon destroys it in the Main Phase: the face-up Spirits return; a Set one stays.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var machine = PlaceFaceUpSpellTrap(engine, p, SettleMachine, 0);
                var upYata = PlaceMonster(engine, p, YataGarasu, 0, BattlePosition.Attack, true);
                var setHino = PlaceMonster(engine, p, HinoKaguTsuchi, 1, BattlePosition.Defense, false);
                engine.NotifyPublic(); // the Machine is seen active
                var mst = PutInHand(engine, p, Mst);
                var used = engine.TryActivateSpellTrap(p, mst, true);
                Settle(engine);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(machine);
                Settle(engine);
                engine.NotifyPublic();
                Check("Settle Machine: destroyed by MST — the face-up Spirit returns, the Set one stays",
                    reached && used && p.Graveyard.Contains(machine) && p.Hand.Contains(upYata) &&
                    p.TryFindMonster(setHino, out _),
                    $"used={used} machineGy={p.Graveyard.Contains(machine)} yataHand={p.Hand.Contains(upYata)} " +
                    $"hinoField={p.TryFindMonster(setHino, out _)}");
            }

            {
                // The AI keeps it only while it holds a face-up Spirit; with an empty hand it is destroyed.
                foreach (var (withSpirit, withHand, kept) in new[] { (true, true, true), (false, true, false), (true, false, false) })
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    var reached = ReachOpponentMain(engine);
                    ClearBoard(engine);
                    var o = engine.Opponent;
                    o.Hand.Clear();
                    var machine = PlaceFaceUpSpellTrap(engine, o, SettleMachine, 0);
                    engine.NotifyPublic();
                    CardInstance spirit = null;
                    if (withSpirit) spirit = PlaceMonster(engine, o, YataGarasu, 0, BattlePosition.Attack, true);
                    CardInstance fodder = null;
                    if (withHand) fodder = PutInHand(engine, o, Celtic);
                    EndTurn(engine);
                    var machineKept = o.TryFindSpellTrap(machine, out _);
                    Check($"Settle Machine (AI): spirit={withSpirit} hand={withHand} → " + (kept ? "discards and keeps it" : "lets it be destroyed"),
                        reached && machineKept == kept && engine.TurnPlayer == engine.Player &&
                        (fodder == null || o.Graveyard.Contains(fodder) == kept) &&
                        (spirit == null || o.TryFindMonster(spirit, out _) == kept),
                        $"kept={machineKept} turn={engine.TurnPlayer?.Name} fodderGy={fodder != null && o.Graveyard.Contains(fodder)}");
                }
            }

            {
                // Spirit's Invitation + Settle Machine: the Machine's leave-field return triggers the Invitation.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var machine = PlaceFaceUpSpellTrap(engine, p, SettleMachine, 0);
                PlaceFaceUpSpellTrap(engine, p, SpiritsInvitation, 1);
                var theirs = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var yata = PutInHand(engine, p, YataGarasu);
                var ns = engine.TryNormalSummon(p, yata, false);
                Settle(engine);
                EndTurn(engine); // p's hand is empty: the Machine is destroyed, Yata returns, the Invitation bounces
                Check("Settle Machine + Spirit's Invitation: no card to discard → Machine destroyed, Yata returns, " +
                      "the opponent returns their monster",
                    reached && ns && p.Graveyard.Contains(machine) && p.Hand.Contains(yata) && o.Hand.Contains(theirs) &&
                    engine.TurnPlayer == o,
                    $"ns={ns} machineGy={p.Graveyard.Contains(machine)} yataHand={p.Hand.Contains(yata)} " +
                    $"theirsHand={o.Hand.Contains(theirs)} turn={engine.TurnPlayer?.Name}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            CompiledCardProgram Syn(int id, string type, string race, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id, name = "Near miss " + id, type = type, race = race, desc = desc
                });

            var abacusHand = Syn(90610001, "Trap Card", "Continuous",
                "Each time 1 monster is sent from the hand to the Graveyard, inflict 500 points of damage per card to " +
                "the respective card owner's Life Points.");
            Check("Near miss: \"sent from the hand\" is not Fatal Abacus",
                !Has(abacusHand, EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy) &&
                !(abacusHand?.FullyCompiled ?? false),
                Describe(abacusHand));

            var machineOpp = Syn(90610002, "Trap Card", "Continuous",
                "During your Standby Phase, gain Life Points equal to half of the total Life Points your opponent paid " +
                "during their last turn.");
            Check("Near miss: the opponent's LP payments are not Life Absorbing Machine",
                !Has(machineOpp, EffectActionKind.GainHalfLpCostsPaidLastTurn) && !(machineOpp?.FullyCompiled ?? false),
                Describe(machineOpp));

            var fortuneNoOpt = Syn(90610003, "Trap Card", "Continuous",
                "During your Standby Phase, select 1 random card from your opponent's hand. Call the type of card " +
                "(Monster, Spell, or Trap). If you call it right, inflict 700 points of damage to your opponent's Life Points.");
            Check("Near miss: Ominous Fortunetelling's text without its last sentence is not compiled",
                !Has(fortuneNoOpt, EffectActionKind.CallTypeOfRandomOpponentHandCardBurn) &&
                !(fortuneNoOpt?.FullyCompiled ?? false),
                Describe(fortuneNoOpt));

            var mirrorAny = Syn(90610004, "Spell Card", "Normal",
                "Look at your opponent's hand. Select and discard 1 Fiend monster to the Graveyard if Fiend monsters exist " +
                "in his/her hand.");
            Check("Near miss: a Fiend discard is not Fengsheng Mirror's Spirit discard",
                !Has(mirrorAny, EffectActionKind.LookAtOpponentHandDiscardChosenSpirit) && !(mirrorAny?.FullyCompiled ?? false),
                Describe(mirrorAny));

            var invitationAny = Syn(90610005, "Trap Card", "Continuous",
                "Each time your monster returns to your hand, select 1 monster on your opponent's side of the field and " +
                "return it to its owner's hand.");
            Check("Near miss: \"your monster\" (not a Spirit) is not Spirit's Invitation",
                !Has(invitationAny, EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand) &&
                !(invitationAny?.FullyCompiled ?? false),
                Describe(invitationAny));

            var settleStandby = Syn(90610006, "Spell Card", "Continuous",
                "Discard 1 card from your hand during each of your Standby Phases. If you do not, destroy this card.");
            Check("Near miss: a Standby discard upkeep is not Settle Machine's End Phase upkeep",
                !Has(settleStandby, EffectActionKind.EndPhaseDiscardOrDestroyThis),
                Describe(settleStandby));
        }
    }
}