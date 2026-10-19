using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// v208 QC "HollowActions": RDS-era cards whose EffectActionKind was only classified (EffectVocabulary) and emitted by an
    /// atom, with no runtime arm — Implemented, in decks, and inert. Each card is either made exact (mini-duels through the
    /// engine entry points: TryActivateSpellTrap, TrySelectEffectTarget, TryNormalSummon, TryAttack, response windows) or
    /// failed closed (pinned as not Implemented, so a re-added hollow atom fails here). Runtime:
    /// <see cref="QcHollowActionsRuntime"/>; the compiler guard <see cref="EffectVocabulary.HasNoRuntime"/>.
    /// </summary>
    public static class QcHollowActionsRegressionTests
    {
        const int XingZhenHu = 76515293;
        const int CursedFig = 18489208;
        const int TriangleEcstasySpark = 12181376;
        const int HarpieLady2 = 27927359;
        const int HarpieLady3 = 54415063;
        const int HarpieLady = 76812113;      // Normal Monster 1300/1400 — no battle lock
        const int Ballista = 242146;
        const int Flint = 75560629;
        const int NecklaceOfCommand = 48576971;
        const int Tragedy = 35686188;
        const int Thestalos = 26205777;
        const int CoveringFire = 74458486;
        const int ChainBurst = 48276469;
        const int Homunculus = 40410110;
        const int SasukeSamurai4 = 64538655;
        const int BesBigCore = 14148099;
        const int BesBigCoreMk2 = 75937826;
        const int BesBigCoreMk3 = 82821760;
        const int Riryoku = 34016756;
        const int TrapHole = 4206964;
        const int Celtic = 91152256;          // Normal Lv4 1400/1200
        const int ManEaterBug = 54652250;     // Flip Effect 450/600
        const int Sangan = 26202165;          // Effect (not Flip) 1000/600
        const int MysticalElf = 15025844;     // Normal 800/2000
        const int Mst = 5318639;
        const int Bewd = 89631139;            // Normal 3000/2500
        const int BookOfMoon = 14087893;      // Quick-Play: target 1 face-up monster, face-down Defense Position

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            void Check(string name, bool ok, string detail = null) => check("QcHollowActions: " + name, ok, detail ?? "");

            CardEffectStatusKind Status(int id) =>
                db.Get(id) != null ? CardEffectStatus.Classify(db.Get(id)) : CardEffectStatusKind.Unimplemented;

            string Shape(int id)
            {
                var p = CardTextEffectCompiler.Compile(db.Get(id));
                if (p == null) return "null";
                var cls = string.Join("; ", p.ClauseList.Select(c => $"{c.Timing}:{c.Action}/{c.Zone}"));
                return $"{Status(id)} full={p.FullyCompiled} [{cls}] unparsed=[{string.Join("|", p.UnparsedFragments ?? Array.Empty<string>())}]";
            }

            // ── fail closed: back to their v206 state (not Implemented, not in decks) ──
            // RdsRedoBattle batch (2026-10-18): Covering Fire and Sasuke Samurai #4 are Implemented again with real runtimes
            // (RdsRedoBattleRegressionTests); the rest stay closed.
            // RdsRedoSpellTrap batch: Triangle Ecstasy Spark, Flint, Necklace of Command and Chain Burst were redone with real
            // runtimes (RdsRedoSpellTrapRegressionTests pins their shapes and mini-duels).
            foreach (var id in new[] { CursedFig, Tragedy, BesBigCoreMk2, BesBigCoreMk3 })
            {
                Check($"{db.Get(id)?.name ?? id.ToString()}: not Implemented (fail closed; no runtime does what it prints)",
                    Status(id) != CardEffectStatusKind.Implemented && !CardEffectStatus.MayIncludeInDeck(db.Get(id)),
                    Shape(id));
            }

            // BattleRiders batch (v210): B.E.S. Big Core compiles whole — the Normal Summon counter sentence (gated to a
            // Normal Summon), the shared battle indestructibility and the end-of-Damage-Step removal-or-destroy trigger
            // (BattleRidersRegressionTests drive it). No clause from a partial match.
            {
                var bc = CardTextEffectCompiler.Compile(db.Get(BesBigCore));
                Check("B.E.S. Big Core: compiled whole (Normal Summon counters + end-of-Damage-Step removal or destroy)",
                    bc != null && bc.FullyCompiled && bc.ClauseList.Count == 3 &&
                    bc.ClauseList.Exists(c => c.Action == EffectActionKind.PlaceCounterOnThisCard &&
                                              c.RequiresThisNormalSummoned) &&
                    bc.ClauseList.Exists(c => c.Action == EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep &&
                                              c.Timing == EffectTiming.EndOfDamageStep) &&
                    Status(BesBigCore) == CardEffectStatusKind.Implemented,
                    Shape(BesBigCore));
            }

            {
                // RdsRedoSpellTrap batch: the hollow atom is gone; the card compiles to real actions.
                var p = CardTextEffectCompiler.Compile(db.Get(TriangleEcstasySpark));
                Check("Triangle Ecstasy Spark: no longer compiles to its retired hollow action",
                    p != null && !p.ClauseList.Any(c => c != null &&
                        c.Action == EffectActionKind.BoostHarpieLadySistersAtkAndLockTraps),
                    Shape(TriangleEcstasySpark));
            }

            foreach (var a in new[]
                     {
                         EffectActionKind.BoostHarpieLadySistersAtkAndLockTraps,
                         EffectActionKind.NegateFlipMonsterEffectsThisDestroysByBattle,
                         EffectActionKind.EquippedLosesAtkAndCannotAttackChangePos,
                         EffectActionKind.DestroyAllOppDefenseWhenOppAttackBecomesDefense
                         // RdsRedoBattle batch: TargetGainsAtkEqualToAnotherTargetDuringDamageStep and
                         // CoinCallDestroyBattledMonsterOnCorrect have runtimes again (RdsRedoBattleRuntime).
                     })
                Check($"{a}: listed as having no runtime", EffectVocabulary.HasNoRuntime(a));

            XingZhenHuDuels(db, pDeck, aDeck, Check, Shape, Status);
            XingZhenHuAi(db, pDeck, aDeck, Check);
            HarpieLady2Duels(db, pDeck, aDeck, Check, Shape, Status);
            HarpieLady3Duels(db, pDeck, aDeck, Check, Shape, Status);
            BallistaDuels(db, pDeck, aDeck, Check, Shape, Status);
            ThestalosDuels(db, pDeck, aDeck, Check, Shape, Status);
            RiryokuDuels(db, pDeck, aDeck, Check, Shape, Status);
            HomunculusDuels(db, pDeck, aDeck, Check, Shape, Status);
        }

        delegate void CheckFn(string name, bool ok, string detail = null);

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 of turn 3, empty boards, hands and GYs.</summary>
        static DuelEngine PlayerMain3(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
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
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return engine;
        }

        /// <summary>End the current turn; true once the next turn's Main Phase 1 is reached.</summary>
        static bool NextTurnMain(DuelEngine engine)
        {
            var turn = engine.TurnNumber;
            for (var i = 0; i < 4 && engine.TurnNumber == turn; i++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnNumber == turn + 1 && engine.Phase == DuelPhase.Main1;
        }

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool Pick(DuelEngine engine, CardInstance target) =>
            engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(target) &&
            engine.TrySelectEffectTarget(target);

        static bool OnField(DuelistState who, CardInstance c) => who != null && c != null && who.TryFindMonster(c, out _);

        static string Board(DuelEngine e) =>
            $"turn={e.TurnNumber} phase={e.Phase} pending={(e.PendingActivation != null ? e.PendingActivation.Prompt : "none")} " +
            $"window={(e.PendingResponse != null ? e.PendingResponse.Timing.ToString() : "none")} chain={e.Chain?.Count ?? 0}";

        // ───────────────────────────── Xing Zhen Hu ─────────────────────────────

        static void XingZhenHuDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check,
            Func<int, string> Shape, Func<int, CardEffectStatusKind> Status)
        {
            var prog = CardTextEffectCompiler.Compile(db.Get(XingZhenHu));
            var c = prog?.ClauseList.FirstOrDefault();
            Check("Xing Zhen Hu: Implemented — Activate, exactly 2 face-down Spell/Trap targets on either field",
                Status(XingZhenHu) == CardEffectStatusKind.Implemented && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                c != null && c.Timing == EffectTiming.Activate &&
                c.Action == EffectActionKind.TargetTwoFaceDownSpellTrapsLockActivation &&
                c.Zone == EffectZoneFilter.FaceDownSpellTraps && c.TargetCount == 2 && c.RequiresTargetChoice,
                Shape(XingZhenHu));
            Check("Cursed Fig: no longer compiled by Xing Zhen Hu's atom (its GY trigger is not an ignition)",
                Status(CursedFig) == CardEffectStatusKind.Unimplemented, Shape(CursedFig));

            // Not enough targets: 1 Set card besides itself.
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var xzh = PlaceSetTrap(engine, p, XingZhenHu, 0);
                PlaceSetTrap(engine, o, TrapHole, 0);
                Check("Xing Zhen Hu: cannot be activated with only 1 other Set card on the field",
                    reached && !engine.CanActivateSpellTrap(p, xzh, fromHand: false), Board(engine));
            }

            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var xzh = PlaceSetTrap(engine, p, XingZhenHu, 0);
                var hole1 = PlaceSetTrap(engine, o, TrapHole, 0);
                var hole2 = PlaceSetTrap(engine, o, TrapHole, 1);
                var hole3 = PlaceSetTrap(engine, o, TrapHole, 2);
                var can = engine.CanActivateSpellTrap(p, xzh, fromHand: false);
                var act = can && engine.TryActivateSpellTrap(p, xzh, fromHand: false);
                var offeredSelf = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(xzh);
                var p1 = Pick(engine, hole1);
                var p2 = Pick(engine, hole2);
                PassAll(engine);
                var faceUp = xzh.FaceUp && p.TryFindSpellTrap(xzh, out _);
                // A 1400 ATK monster of the Player's (the corpus probe's summon shape): only the untargeted Trap Hole is legal.
                var probe = PlaceMonster(engine, p, Celtic, 4, BattlePosition.Attack, true);
                var l1 = SpellTrapEffects.IsLegalResponseCard(engine, o, hole1, ResponseTiming.MonsterSummoned, probe);
                var l2 = SpellTrapEffects.IsLegalResponseCard(engine, o, hole2, ResponseTiming.MonsterSummoned, probe);
                var l3 = SpellTrapEffects.IsLegalResponseCard(engine, o, hole3, ResponseTiming.MonsterSummoned, probe);
                Check("Xing Zhen Hu: the 2 Set cards it targets cannot be activated; an untargeted one can",
                    reached && act && !offeredSelf && p1 && p2 && faceUp && !l1 && !l2 && l3,
                    $"can={can} act={act} self={offeredSelf} picks={p1}/{p2} faceUp={faceUp} legal={l1}/{l2}/{l3} {Board(engine)}");

                // A real Normal Summon: the AI answers with the only Trap Hole it may activate.
                p.Hand.Clear();
                var celtic = PutInHand(engine, p, Celtic);
                var ns = engine.TryNormalSummon(p, celtic, asSet: false);
                PassAll(engine);
                var used3 = o.Graveyard.Contains(hole3);
                var kept = o.TryFindSpellTrap(hole1, out _) && !hole1.FaceUp && o.TryFindSpellTrap(hole2, out _) && !hole2.FaceUp;
                Check("Xing Zhen Hu (AI): answering a Normal Summon, the opponent can use only its untargeted Trap Hole",
                    ns && used3 && kept && !OnField(p, celtic), $"ns={ns} used3={used3} kept={kept} {Board(engine)}");

                // Negated (Royal Decree-style: its effects do not apply) → the lock is suspended; restored after.
                xzh.IsNegated = true;
                var whileNegated = SpellTrapEffects.IsLegalResponseCard(engine, o, hole1, ResponseTiming.MonsterSummoned, probe);
                xzh.IsNegated = false;
                var afterNegation = SpellTrapEffects.IsLegalResponseCard(engine, o, hole1, ResponseTiming.MonsterSummoned, probe);
                Check("Xing Zhen Hu: while its effects are negated its targets can be activated; the lock resumes after",
                    whileNegated && !afterNegation, $"negated={whileNegated} after={afterNegation}");

                // Xing Zhen Hu leaves the field → the targets are free for good.
                engine.SendCardToGrave(p, xzh);
                var freed = SpellTrapEffects.IsLegalResponseCard(engine, o, hole1, ResponseTiming.MonsterSummoned, probe);
                Check("Xing Zhen Hu: once it leaves the field its former targets can be activated",
                    freed && p.Graveyard.Contains(xzh), $"freed={freed}");
            }
        }

        static void XingZhenHuAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // The AI activates it in its Main Phase with its own Set Trap Hole and two of the Player's on the field: it
            // locks the Player's two.
            var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var atOpp = reached && ReachOpponentMain(engine);
            ClearBoard(engine);
            o.Hand.Clear();
            var xzh = PlaceSetTrap(engine, o, XingZhenHu, 0);
            var own = PlaceSetTrap(engine, o, TrapHole, 1);
            var mine1 = PlaceSetTrap(engine, p, TrapHole, 0);
            var mine2 = PlaceSetTrap(engine, p, TrapHole, 1);
            var act = atOpp && engine.TryActivateSpellTrap(o, xzh, fromHand: false);
            PassAll(engine);
            var probe = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
            var l1 = SpellTrapEffects.IsLegalResponseCard(engine, p, mine1, ResponseTiming.MonsterSummoned, probe);
            var l2 = SpellTrapEffects.IsLegalResponseCard(engine, p, mine2, ResponseTiming.MonsterSummoned, probe);
            var ownLocked = QcHollowActionsRuntime.SetCardActivationLocked(engine, own, out _);
            Check("Xing Zhen Hu (AI): it targets the Player's Set cards, not its own",
                act && xzh.FaceUp && !l1 && !l2 && !ownLocked,
                $"act={act} faceUp={xzh.FaceUp} legal={l1}/{l2} ownLocked={ownLocked} {Board(engine)}");
        }

        // ───────────────────────────── Harpie Lady 2 ─────────────────────────────

        static void HarpieLady2Duels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check,
            Func<int, string> Shape, Func<int, CardEffectStatusKind> Status)
        {
            var prog = CardTextEffectCompiler.Compile(db.Get(HarpieLady2));
            Check("Harpie Lady 2: Implemented — the Twin-Headed Wolf battle negation of Flip monsters, this card only",
                Status(HarpieLady2) == CardEffectStatusKind.Implemented && prog.FullyCompiled &&
                prog.ClauseList.Exists(c => c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle &&
                                            c.AffectsThisCardOnly && c.TargetCardKind == "Flip" &&
                                            string.IsNullOrEmpty(c.CountRaceFilter)) &&
                prog.ClauseList.Exists(c => c.Action == EffectActionKind.AlwaysTreatedAsName),
                Shape(HarpieLady2));

            (bool attacked, CardInstance bug, DuelEngine engine) Battle(int attackerId, int defenderId)
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, p, attackerId, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var d = PlaceMonster(engine, o, defenderId, 0, BattlePosition.Defense, false);
                var ok = reached && AttackAndDrain(engine, p, a, d);
                if (engine.IsAwaitingEffectTarget)
                {
                    var t = engine.PendingActivation.LegalTargets.FirstOrDefault();
                    if (t != null) engine.TrySelectEffectTarget(t);
                }

                PassAll(engine);
                return (ok, d, engine);
            }

            {
                var (ok, bug, engine) = Battle(HarpieLady2, ManEaterBug);
                var inGy = engine.Opponent.Graveyard.Contains(bug);
                var lady = engine.Player.MonstersOnField().Count();
                Check("Harpie Lady 2: a Flip monster she destroys by battle has its effects negated (its FLIP does not resolve)",
                    ok && inGy && bug.EffectsNegatedAfterBattle && lady == 2,
                    $"ok={ok} gy={inGy} negated={bug.EffectsNegatedAfterBattle} myMonsters={lady} {Board(engine)}");
            }

            {
                var (ok, bug, engine) = Battle(HarpieLady, ManEaterBug);
                Check("Near miss: plain Harpie Lady does not negate the Flip monster she destroys",
                    ok && engine.Opponent.Graveyard.Contains(bug) && !bug.EffectsNegatedAfterBattle,
                    $"ok={ok} negated={bug.EffectsNegatedAfterBattle}");
            }

            {
                var (ok, sangan, engine) = Battle(HarpieLady2, Sangan);
                Check("Near miss: Harpie Lady 2 does not negate a non-Flip Effect Monster she destroys (Sangan)",
                    ok && engine.Opponent.Graveyard.Contains(sangan) && !sangan.EffectsNegatedAfterBattle,
                    $"ok={ok} negated={sangan.EffectsNegatedAfterBattle}");
            }
        }

        // ───────────────────────────── Harpie Lady 3 ─────────────────────────────

        static void HarpieLady3Duels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check,
            Func<int, string> Shape, Func<int, CardEffectStatusKind> Status)
        {
            Check("Harpie Lady 3: Implemented (EVENT_BATTLED attack lock)",
                Status(HarpieLady3) == CardEffectStatusKind.Implemented, Shape(HarpieLady3));

            bool CanAttackInOppBattle(DuelEngine engine, CardInstance m)
            {
                if (engine.TurnPlayer != engine.Opponent) return false;
                if (engine.Phase == DuelPhase.Main1) engine.TryEnterBattlePhase(engine.Opponent);
                PassAll(engine);
                return engine.Phase == DuelPhase.Battle && engine.CanAttack(engine.Opponent, m);
            }

            // The Player's turn 3: Harpie Lady 3 attacks a 1400 ATK monster and is destroyed; the monster that battled her
            // cannot attack during the opponent's turns 4 and 6; it can on turn 8.
            foreach (var (ladyId, label) in new[] { (HarpieLady3, "Harpie Lady 3"), (HarpieLady, "Near miss: plain Harpie Lady") })
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lady = PlaceMonster(engine, p, ladyId, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var ok = reached && AttackAndDrain(engine, p, lady, celtic);
                var ladyGone = !OnField(p, lady);
                var t4 = NextTurnMain(engine) && CanAttackInOppBattle(engine, celtic);
                var t5 = NextTurnMain(engine);
                var t6 = NextTurnMain(engine) && CanAttackInOppBattle(engine, celtic);
                var t7 = NextTurnMain(engine);
                var t8 = NextTurnMain(engine) && CanAttackInOppBattle(engine, celtic);
                var survived = OnField(o, celtic);
                if (ladyId == HarpieLady3)
                    Check("Harpie Lady 3: the monster that battled her cannot attack during the opponent's next 2 turns (4, 6), then can (8)",
                        ok && ladyGone && survived && !t4 && t5 && !t6 && t7 && t8,
                        $"ok={ok} ladyGone={ladyGone} survived={survived} t4={t4} t6={t6} t8={t8} {Board(engine)}");
                else
                    Check(label + " leaves the monster that battled her free to attack on turn 4",
                        ok && survived && t4, $"ok={ok} t4={t4} {Board(engine)}");
            }

            // The opponent's turn 4: its monster attacks Harpie Lady 3 (Defense, DEF 1400 > ATK 1000): the current turn counts
            // (RESET_SELF_TURN), so the lock lasts through turn 6 and ends for turn 8.
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lady = PlaceMonster(engine, p, HarpieLady3, 0, BattlePosition.Defense, true);
                var sangan = PlaceMonster(engine, o, Sangan, 0, BattlePosition.Attack, true);
                var ok = reached && ReachOpponentBattle(engine);
                ok = ok && engine.TryAttack(o, sangan, lady);
                DrainCombat(engine);
                PassAll(engine);
                var t5 = NextTurnMain(engine);
                var t6 = NextTurnMain(engine) && CanAttackInOppBattle(engine, sangan);
                var t7 = NextTurnMain(engine);
                var t8 = NextTurnMain(engine) && CanAttackInOppBattle(engine, sangan);
                Check("Harpie Lady 3: attacked on the opponent's turn, that turn counts — locked on turn 6, free on turn 8",
                    ok && OnField(p, lady) && OnField(o, sangan) && t5 && !t6 && t7 && t8,
                    $"ok={ok} lady={OnField(p, lady)} sangan={OnField(o, sangan)} t6={t6} t8={t8} {Board(engine)}");
            }
        }

        // ───────────────────────────── Ballista of Rampart Smashing ─────────────────────────────

        static void BallistaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check,
            Func<int, string> Shape, Func<int, CardEffectStatusKind> Status)
        {
            Check("Ballista of Rampart Smashing: Implemented (equip + damage-calculation gain vs a face-down Defense target)",
                Status(Ballista) == CardEffectStatusKind.Implemented, Shape(Ballista));

            foreach (var (faceDown, equip) in new[] { (true, true), (false, true), (true, false) })
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, !faceDown);
                var equipped = true;
                if (equip)
                {
                    var b = PutInHand(engine, p, Ballista);
                    equipped = engine.TryActivateSpellTrap(p, b, fromHand: true);
                    if (engine.IsAwaitingEffectTarget) Pick(engine, celtic);
                    PassAll(engine);
                    equipped = equipped && celtic.Equips.Any(e => e.CardId == Ballista);
                }

                var lp = p.LifePoints;
                var ok = reached && equipped && AttackAndDrain(engine, p, celtic, elf);
                var elfGone = !OnField(o, elf);
                var after = celtic.CurrentAtk;
                if (faceDown && equip)
                    Check("Ballista: attacking a face-down Defense monster, the equipped monster has +1500 ATK for that damage calculation",
                        ok && elfGone && after == 1400 && p.LifePoints == lp,
                        $"ok={ok} elfGone={elfGone} atkAfter={after} lp={lp}->{p.LifePoints} {Board(engine)}");
                else if (equip)
                    Check("Near miss: Ballista gives nothing against a face-up Defense monster (1400 vs DEF 2000)",
                        ok && !elfGone && p.LifePoints == lp - 600, $"ok={ok} elfGone={elfGone} lp={lp}->{p.LifePoints}");
                else
                    Check("Control: without Ballista the face-down DEF 2000 monster survives the 1400 ATK attack",
                        ok && !elfGone && p.LifePoints == lp - 600, $"ok={ok} elfGone={elfGone} lp={lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Thestalos the Firestorm Monarch ─────────────────────────────

        static void ThestalosDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check,
            Func<int, string> Shape, Func<int, CardEffectStatusKind> Status)
        {
            var prog = CardTextEffectCompiler.Compile(db.Get(Thestalos));
            var trig = prog?.ClauseList.Find(c => c.Action == EffectActionKind.DiscardRandomThenDamageByLevelIfMonster);
            Check("Thestalos: Tribute Summoned trigger — discard 1 random card, Level x 100 damage if a Monster",
                Status(Thestalos) == CardEffectStatusKind.Implemented && trig != null &&
                trig.Timing == EffectTiming.ThisCardSummoned && trig.RequiresThisTributeSummoned && trig.Amount == 100,
                Shape(Thestalos));

            foreach (var (handId, dmg, label) in new[]
                     {
                         (Celtic, 400, "a Monster Card (Level 4): 400 damage"),
                         (Mst, 0, "a Spell Card: no damage")
                     })
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fodder = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var thestalos = PutInHand(engine, p, Thestalos);
                var victim = PutInHand(engine, o, handId);
                var oppLp = o.LifePoints;
                engine.PendingTributes.Add(fodder);
                var ns = reached && engine.TryNormalSummon(p, thestalos, false);
                PassAll(engine);
                var discarded = o.Graveyard.Contains(victim) && !o.Hand.Contains(victim);
                Check($"Thestalos: Tribute Summoned, the opponent discards at random — {label}",
                    ns && OnField(p, thestalos) && discarded && o.LifePoints == oppLp - dmg,
                    $"ns={ns} discarded={discarded} lp={oppLp}->{o.LifePoints} {Board(engine)}");
            }
        }

        // ───────────────────────────── Riryoku ─────────────────────────────

        static void RiryokuDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check,
            Func<int, string> Shape, Func<int, CardEffectStatusKind> Status)
        {
            var prog = CardTextEffectCompiler.Compile(db.Get(Riryoku));
            var c = prog?.ClauseList.FirstOrDefault();
            Check("Riryoku: Implemented — exactly 2 face-up monster targets",
                Status(Riryoku) == CardEffectStatusKind.Implemented && c != null &&
                c.Action == EffectActionKind.HalfAtkAddToOtherUntilEndOfTurn && c.TargetCount == 2 &&
                c.RequiresTargetFaceUp && c.Zone == EffectZoneFilter.FieldAnyMonster,
                Shape(Riryoku));

            // The Player halves the opponent's Blue-Eyes and gives 1500 to its Celtic Guardian, until the end of the turn.
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, o, Bewd, 0, BattlePosition.Attack, true);
                var setOne = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Defense, false);
                var r = PutInHand(engine, p, Riryoku);
                var act = reached && engine.TryActivateSpellTrap(p, r, fromHand: true);
                var faceDownOffered = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(setOne);
                var p1 = Pick(engine, bewd);
                var p2 = Pick(engine, celtic);
                PassAll(engine);
                // Resolution: the Player chooses the monster whose ATK is halved.
                var asked = engine.IsAwaitingEffectTarget &&
                            engine.PendingActivation.LegalTargets.Contains(bewd) &&
                            engine.PendingActivation.LegalTargets.Contains(celtic);
                var chose = asked && engine.TrySelectEffectTarget(bewd);
                PassAll(engine);
                var atkB = bewd.CurrentAtk;
                var atkC = celtic.CurrentAtk;
                Check("Riryoku: at resolution the Player halves Blue-Eyes (1500) and Celtic Guardian gains 1500 (2900)",
                    act && !faceDownOffered && p1 && p2 && asked && chose && atkB == 1500 && atkC == 2900 &&
                    p.Graveyard.Exists(g => g.CardId == Riryoku),
                    $"act={act} fdOffered={faceDownOffered} picks={p1}/{p2} asked={asked} chose={chose} bewd={atkB} celtic={atkC} {Board(engine)}");
                var next = NextTurnMain(engine);
                Check("Riryoku: the changes last until the end of the turn",
                    next && bewd.CurrentAtk == 3000 && celtic.CurrentAtk == 1400,
                    $"next={next} bewd={bewd.CurrentAtk} celtic={celtic.CurrentAtk}");
            }

            // The AI activates it in its own Main Phase: it halves the Player's Blue-Eyes and boosts its Celtic Guardian. With the
            // Player chaining Book of Moon to Blue-Eyes, only Celtic Guardian is still a face-up target: it alone is halved.
            foreach (var chainBook in new[] { false, true })
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var book = PlaceSetTrap(engine, p, BookOfMoon, 0);
                var atOpp = reached && ReachOpponentMain(engine);
                ClearBoard(engine);
                p.MonsterZones[0].Occupant = bewd;
                p.SpellTrapZones[0].Occupant = chainBook ? book : null;
                o.Hand.Clear();
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var r = PutInHand(engine, o, Riryoku);
                var act = atOpp && engine.TryActivateSpellTrap(o, r, fromHand: true);
                var offered = engine.PendingResponse != null && engine.PendingResponse.Responder == p &&
                              engine.PendingResponse.LegalCards.Contains(book);
                var chained = false;
                if (chainBook && offered)
                {
                    chained = engine.TryActivateSpellTrap(p, book, fromHand: false);
                    if (engine.IsAwaitingEffectTarget) chained = Pick(engine, bewd) && chained;
                }

                PassAll(engine);
                if (chainBook)
                    Check("Riryoku: a target turned face-down in response is not affected; the other is halved, nothing gained",
                        act && offered && chained && !bewd.FaceUp && celtic.CurrentAtk == 700,
                        $"act={act} offered={offered} chained={chained} bewdFaceUp={bewd.FaceUp} celtic={celtic.CurrentAtk} {Board(engine)}");
                else
                    Check("Riryoku (AI): halves the Player's Blue-Eyes and adds 1500 to its own Celtic Guardian",
                        act && bewd.CurrentAtk == 1500 && celtic.CurrentAtk == 2900,
                        $"act={act} bewd={bewd.CurrentAtk} celtic={celtic.CurrentAtk} {Board(engine)}");
            }

            // Only 1 face-up monster: Riryoku cannot be activated.
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, engine.Opponent, MysticalElf, 0, BattlePosition.Defense, false);
                var r = PutInHand(engine, p, Riryoku);
                Check("Riryoku: cannot be activated with fewer than 2 face-up monsters",
                    reached && !engine.CanActivateSpellTrap(p, r, fromHand: true), Board(engine));
            }
        }

        // ───────────────────────────── Homunculus the Alchemic Being ─────────────────────────────

        static void HomunculusDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check,
            Func<int, string> Shape, Func<int, CardEffectStatusKind> Status)
        {
            Check("Homunculus the Alchemic Being: Implemented (once per turn ignition, change its Attribute)",
                Status(Homunculus) == CardEffectStatusKind.Implemented, Shape(Homunculus));

            DuelEngine Changed(out CardInstance h, out bool ok, out string detail)
            {
                var engine = PlayerMain3(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                h = PlaceMonster(engine, p, Homunculus, 0, BattlePosition.Attack, true);
                var act = reached && engine.TryActivateSpellTrap(p, h, fromHand: false);
                PassAll(engine);
                var dark = engine.IsAwaitingEffectTarget
                    ? engine.PendingActivation.LegalTargets.FirstOrDefault(t =>
                        string.Equals(t?.Def?.attribute, "DARK", StringComparison.OrdinalIgnoreCase))
                    : null;
                var light = engine.IsAwaitingEffectTarget &&
                            engine.PendingActivation.LegalTargets.Exists(t =>
                                string.Equals(t?.Def?.attribute, "LIGHT", StringComparison.OrdinalIgnoreCase));
                var picked = dark != null && engine.TrySelectEffectTarget(dark);
                PassAll(engine);
                ok = act && picked && !light && h.Attribute == "DARK";
                detail = $"act={act} darkOffered={dark != null} lightOffered={light} picked={picked} attr={h.Attribute} {Board(engine)}";
                return engine;
            }

            {
                var engine = Changed(out var h, out var ok, out var detail);
                Check("Homunculus: becomes the declared Attribute (DARK; its own LIGHT is not offered)", ok, detail);
                h.FaceUp = false;
                h.FaceUp = true;
                Check("Homunculus: turned face-down and back, it is LIGHT again (RESETS_STANDARD)",
                    ok && h.Attribute == "LIGHT", $"attr={h.Attribute}");
            }

            {
                var engine = Changed(out var h, out var ok, out var detail);
                h.IsNegated = true;
                var negated = h.Attribute;
                h.IsNegated = false;
                Check("Homunculus: once its effects are negated the change is gone, also after the negation ends (RESET_DISABLE)",
                    ok && negated == "LIGHT" && h.Attribute == "LIGHT", $"{detail} negated={negated} after={h.Attribute}");
            }

            {
                var engine = Changed(out var h, out var ok, out var detail);
                engine.DestroyMonsterPublic(engine.Player, h);
                var inGy = engine.Player.Graveyard.Contains(h);
                Check("Homunculus: in the GY it has its printed Attribute (the change ended when it left the field)",
                    ok && inGy && h.Attribute == "LIGHT", $"{detail} gy={inGy} attr={h.Attribute}");
            }
        }
    }
}
