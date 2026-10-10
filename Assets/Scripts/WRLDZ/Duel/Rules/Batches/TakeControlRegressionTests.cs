using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TakeControl batch (v141 curriculum): Brain Control, the four Charmers (Aussa / Eria / Hiita / Wynn)
    /// and Invader of the Throne. Compile shapes, mini-duels through the real engine entry points
    /// (TryActivateSpellTrap, TryFlipSummon, TryAttack, TrySelectEffectTarget, EndTurn) and fail-closed
    /// near misses. Runtime: <see cref="TakeControlRuntime"/>.
    /// </summary>
    public static class TakeControlRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BrainControl = 87910978;
        const int Aussa = 37970940;       // EARTH Charmer (older wording)
        const int Eria = 74364659;        // WATER Charmer (PSCT)
        const int Hiita = 759393;         // FIRE Charmer (PSCT)
        const int Wynn = 37744402;        // WIND Charmer (older wording)
        const int Invader = 3056267;      // Invader of the Throne, 1350/1700
        const int RemoveBrainwashing = 94739788;
        const int BookOfMoon = 14087893;
        const int CreatureSwap = 31036355;
        const int ChangeOfHeart = 4031928;
        const int SnatchSteal = 45986603;

        const int Kojikocy = 1184620;     // EARTH 1500/1200
        const int Uraby = 1784619;        // EARTH 1500/800
        const int QueenOfAutumnLeaves = 4179849; // EARTH 1800/1500
        const int Hyosube = 2118022;      // WATER 1500/900
        const int WaterOmotics = 2483611; // WATER 1400/1200
        const int DarkfireSoldier1 = 5388481; // FIRE 1700/1150
        const int GreatAngus = 11813953;  // FIRE 1800/600
        const int FlyingKamakiri2 = 3134241; // WIND 1500/800
        const int Takuhee = 3170832;      // WIND 1450/1000
        const int BlackLusterSoldier = 5405694; // Ritual
        const int FlameSwordsman = 45231177;    // Fusion
        const int GateGuardian = 25833572;      // "cannot be Normal Summoned or Set"
        const int MysticSwordsmanLv4 = 74591968; // "Cannot be Normal Summoned (but can be Normal Set)"

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TakeControl: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            BrainControlDuels(db, pDeck, aDeck, Check);
            CharmerDuels(db, pDeck, aDeck, Check);
            InvaderDuels(db, pDeck, aDeck, Check);
            LayeredControlDuels(db, pDeck, aDeck, Check);
            NearMisses(db, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var bc = CardTextEffectCompiler.Compile(db.Get(BrainControl));
            Check("Brain Control FullyCompiled Activate TakeControlUntilEndPhase, pay 800, NS/Set-able face-up target",
                bc != null && bc.FullyCompiled && bc.ClauseList.Count == 1 &&
                bc.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                          c.Action == EffectActionKind.TakeControlUntilEndPhase &&
                                          c.Zone == EffectZoneFilter.OppFaceUpMonsters &&
                                          c.PayLpAmount == 800 && c.TargetMustBeNormalSummonable &&
                                          c.RequiresTargetChoice && c.IsPsctTarget),
                Describe(bc));
            Check("Brain Control classifies Implemented",
                CardEffectStatus.Classify(db.Get(BrainControl)) == CardEffectStatusKind.Implemented);

            foreach (var (id, attr) in new[] { (Aussa, "EARTH"), (Eria, "WATER"), (Hiita, "FIRE"), (Wynn, "WIND") })
            {
                var def = db.Get(id);
                var prog = CardTextEffectCompiler.Compile(def);
                Check($"{def?.name} FullyCompiled Flip TakeControlWhileSourceFaceUp {attr}",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Flip &&
                                                c.Action == EffectActionKind.TakeControlWhileSourceFaceUp &&
                                                c.Zone == EffectZoneFilter.OppFaceUpMonsters &&
                                                c.AttributeFilter == attr && c.RequiresTargetChoice &&
                                                !c.TargetMustBeNormalSummonable),
                    Describe(prog));
                Check($"{def?.name} classifies Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented);
            }

            var inv = CardTextEffectCompiler.Compile(db.Get(Invader));
            Check("Invader of the Throne FullyCompiled Flip SwitchControlWithThis, not in the Battle Phase",
                inv != null && inv.FullyCompiled && inv.ClauseList.Count == 1 &&
                inv.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Flip &&
                                           c.Action == EffectActionKind.SwitchControlWithThis &&
                                           c.Zone == EffectZoneFilter.OppMonsters &&
                                           c.CannotActivateInBattlePhase && c.RequiresTargetChoice),
                Describe(inv));
            Check("Invader of the Throne classifies Implemented",
                CardEffectStatus.Classify(db.Get(Invader)) == CardEffectStatusKind.Implemented);

            Check("New actions map to the TakeControl resolution kind",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.TakeControlWhileSourceFaceUp }) ==
                EffectResolutionKind.TakeControl &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.SwitchControlWithThis }) ==
                EffectResolutionKind.TakeControl);
        }

        // ───────────────────────────── Brain Control ─────────────────────────────

        static void BrainControlDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Chk Check)
        {
            // Only monsters that can be Normal Summoned/Set are legal; pay 800 LP; back at the End Phase.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var lp = p.LifePoints;
                var koji = PlaceMonster(engine, opp, Kojikocy, 0, BattlePosition.Attack, true);
                var bls = PlaceMonster(engine, opp, BlackLusterSoldier, 1, BattlePosition.Attack, true);
                var fusion = PlaceMonster(engine, opp, FlameSwordsman, 2, BattlePosition.Attack, true);
                var nomi = PlaceMonster(engine, opp, GateGuardian, 3, BattlePosition.Attack, true);
                var msLv4 = PlaceMonster(engine, opp, MysticSwordsmanLv4, 4, BattlePosition.Attack, true);
                var card = PutInHand(engine, p, BrainControl);
                var activated = engine.TryActivateSpellTrap(p, card, fromHand: true);
                var legal = engine.PendingActivation?.LegalTargets;
                Check("Brain Control: activates and asks for a target",
                    activated && engine.IsAwaitingEffectTarget, $"activated={activated}");
                Check("Brain Control: NS/Set-able targets only (not Ritual / Fusion / Nomi)",
                    legal != null && legal.Contains(koji) && legal.Contains(msLv4) &&
                    !legal.Contains(bls) && !legal.Contains(fusion) && !legal.Contains(nomi),
                    legal == null ? "no pending" : string.Join(",", legal.Select(t => t.Name)));
                var picked = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(koji);
                DrainCombat(engine);
                Check("Brain Control: pays 800 LP and takes control of the target",
                    picked && p.LifePoints == lp - 800 && p.TryFindMonster(koji, out _) &&
                    !opp.TryFindMonster(koji, out _) && koji.ControlRevertsAtEndPhase,
                    $"picked={picked} lp={p.LifePoints} onP={p.TryFindMonster(koji, out _)}");
                engine.EndTurn(p);
                Check("Brain Control: control returns in the End Phase",
                    opp.TryFindMonster(koji, out _) && !p.TryFindMonster(koji, out _),
                    $"onOpp={opp.TryFindMonster(koji, out _)}");
            }

            // Fail closed: no face-up NS/Set-able monster → cannot activate, no LP paid.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var lp = p.LifePoints;
                PlaceMonster(engine, opp, BlackLusterSoldier, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, Kojikocy, 1, BattlePosition.Defense, false);
                var card = PutInHand(engine, p, BrainControl);
                Check("Brain Control: only a Ritual Monster and a face-down monster → cannot activate",
                    !engine.CanActivateSpellTrap(p, card, fromHand: true) &&
                    !engine.TryActivateSpellTrap(p, card, fromHand: true) && p.LifePoints == lp &&
                    p.Hand.Contains(card),
                    $"lp={p.LifePoints}");

                PlaceMonster(engine, opp, Uraby, 2, BattlePosition.Attack, true);
                p.LifePoints = 700;
                Check("Brain Control: cannot pay 800 LP with 700 LP",
                    !engine.CanActivateSpellTrap(p, card, fromHand: true));
            }

            // A target turned face-down before resolution is not taken (Lua tc:IsFaceup()).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var koji = PlaceMonster(engine, opp, Kojikocy, 0, BattlePosition.Attack, true);
                var card = PutInHand(engine, p, BrainControl);
                if (engine.TryActivateSpellTrap(p, card, fromHand: true) && engine.IsAwaitingEffectTarget)
                {
                    koji.FaceUp = false;
                    koji.Position = BattlePosition.Defense;
                    engine.TrySelectEffectTarget(koji);
                    if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                    DrainCombat(engine);
                }

                Check("Brain Control: a target turned face-down before resolution stays with the opponent",
                    opp.TryFindMonster(koji, out _) && !p.TryFindMonster(koji, out _) &&
                    !koji.ControlRevertsAtEndPhase);
            }

            // AI activation: auto-picks the strongest legal target, no pending prompt.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(engine))
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var lp = opp.LifePoints;
                    var weak = PlaceMonster(engine, p, Kojikocy, 0, BattlePosition.Attack, true);
                    var strong = PlaceMonster(engine, p, QueenOfAutumnLeaves, 1, BattlePosition.Attack, true);
                    var ritual = PlaceMonster(engine, p, BlackLusterSoldier, 2, BattlePosition.Attack, true);
                    var card = PutInHand(engine, opp, BrainControl);
                    var ok = engine.TryActivateSpellTrap(opp, card, fromHand: true);
                    DrainCombat(engine);
                    Check("Brain Control (AI): takes the strongest NS/Set-able monster, pays 800",
                        ok && !engine.IsAwaitingEffectTarget && opp.TryFindMonster(strong, out _) &&
                        p.TryFindMonster(weak, out _) && p.TryFindMonster(ritual, out _) &&
                        opp.LifePoints == lp - 800,
                        $"ok={ok} await={engine.IsAwaitingEffectTarget} strongOnOpp={opp.TryFindMonster(strong, out _)} lp={opp.LifePoints}");
                }
                else
                    Check("Brain Control (AI) setup: reach the opponent's Main Phase", false,
                        $"turnP={engine.TurnPlayer == engine.Opponent} phase={engine.Phase}");
            }
        }

        // ───────────────────────────── Charmers ─────────────────────────────

        /// <summary>Player flips <paramref name="charmer"/>; returns the stolen monster (or null).</summary>
        static CardInstance FlipCharmerAndPick(DuelEngine engine, CardInstance charmer, CardInstance pick,
            out System.Collections.Generic.List<CardInstance> offered)
        {
            offered = null;
            if (!engine.TryFlipSummon(engine.Player, charmer)) return null;
            if (engine.IsAwaitingEffectTarget)
            {
                offered = engine.PendingActivation.LegalTargets.ToList();
                if (!engine.TrySelectEffectTarget(pick)) return null;
            }

            DrainCombat(engine);
            return engine.Player.TryFindMonster(pick, out _) ? pick : null;
        }

        static void CharmerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Chk Check)
        {
            // Each Charmer: only a face-up monster of its Attribute; control stays past the End Phase
            // and returns (not destroyed) once the Charmer leaves the field.
            foreach (var (charmerId, match, faceDownSame, other) in new[]
                     {
                         (Aussa, Kojikocy, Uraby, Hyosube),
                         (Eria, Hyosube, WaterOmotics, Kojikocy),
                         (Hiita, DarkfireSoldier1, GreatAngus, Kojikocy),
                         (Wynn, FlyingKamakiri2, Takuhee, Kojikocy),
                     })
            {
                var name = db.Get(charmerId)?.name ?? charmerId.ToString();
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var charmer = PlaceMonster(engine, p, charmerId, 0, BattlePosition.Defense, false);
                var prey = PlaceMonster(engine, opp, match, 0, BattlePosition.Attack, true);
                var setSame = PlaceMonster(engine, opp, faceDownSame, 1, BattlePosition.Defense, false);
                var off = PlaceMonster(engine, opp, other, 2, BattlePosition.Attack, true);
                var stolen = FlipCharmerAndPick(engine, charmer, prey, out var offered);
                // A human FLIP always prompts (TryResolveFlip), even for a lone legal target.
                Check($"{name}: offers only the face-up monster of its Attribute",
                    offered != null && offered.Count == 1 && offered[0] == prey,
                    offered == null ? "no prompt" : string.Join(",", offered.Select(t => t.Name)));
                Check($"{name}: FLIP takes control while face-up",
                    stolen == prey && prey.ControlBoundTo == charmer && prey.ControlOwner == opp &&
                    !prey.ControlRevertsAtEndPhase && opp.TryFindMonster(setSame, out _) &&
                    opp.TryFindMonster(off, out _),
                    $"stolen={stolen?.Name} bound={prey.ControlBoundTo?.Name}");
                engine.EndTurn(p);
                Check($"{name}: control does not end in the End Phase",
                    p.TryFindMonster(prey, out _) && prey.ControlBoundTo == charmer);
                engine.DestroyMonsterPublic(p, charmer);
                engine.NotifyPublic();
                Check($"{name}: Charmer leaves the field → control returns, the monster is not destroyed",
                    opp.TryFindMonster(prey, out _) && !p.TryFindMonster(prey, out _) &&
                    prey.ControlBoundTo == null && prey.ControlOwner == null &&
                    !opp.Graveyard.Contains(prey) && p.Graveyard.Contains(charmer),
                    $"onOpp={opp.TryFindMonster(prey, out _)} onP={p.TryFindMonster(prey, out _)} inGy={opp.Graveyard.Contains(prey)}");
            }

            // Charmer turned face-down (Book of Moon) → control returns.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var prey = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                FlipCharmerAndPick(engine, eria, prey, out _);
                var moon = PutInHand(engine, p, BookOfMoon);
                var act = engine.TryActivateSpellTrap(p, moon, fromHand: true);
                var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(eria);
                DrainCombat(engine);
                engine.NotifyPublic();
                Check("Eria: Charmer turned face-down → control returns to the owner",
                    act && sel && !eria.FaceUp && opp.TryFindMonster(prey, out _) &&
                    !p.TryFindMonster(prey, out _) && prey.ControlBoundTo == null,
                    $"act={act} sel={sel} eriaFaceUp={eria.FaceUp} onOpp={opp.TryFindMonster(prey, out _)}");
            }

            // The stolen monster turned face-down → the binding ends, its taker keeps it for good.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var prey = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                FlipCharmerAndPick(engine, eria, prey, out _);
                var moon = PutInHand(engine, p, BookOfMoon);
                var act = engine.TryActivateSpellTrap(p, moon, fromHand: true);
                var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(prey);
                DrainCombat(engine);
                engine.NotifyPublic();
                engine.DestroyMonsterPublic(p, eria);
                engine.NotifyPublic();
                Check("Eria: stolen monster turned face-down → its taker keeps it after the Charmer leaves",
                    act && sel && !prey.FaceUp && p.TryFindMonster(prey, out _) &&
                    prey.ControlBoundTo == null && prey.ControlOwner == opp,
                    $"act={act} sel={sel} preyFaceUp={prey.FaceUp} onP={p.TryFindMonster(prey, out _)}");
            }

            // A target turned face-down before resolution is not taken.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var prey = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, eria);
                if (engine.IsAwaitingEffectTarget)
                {
                    prey.FaceUp = false;
                    prey.Position = BattlePosition.Defense;
                    engine.TrySelectEffectTarget(prey);
                    if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                }

                DrainCombat(engine);
                Check("Eria: a target turned face-down before resolution stays with the opponent",
                    flipped && opp.TryFindMonster(prey, out _) && prey.ControlBoundTo == null);
            }

            // No free Monster Zone for the Charmer's controller → no legal target.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                for (var z = 1; z < p.MonsterZones.Length; z++)
                    PlaceMonster(engine, p, Kojikocy, z, BattlePosition.Attack, true);
                var prey = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, eria);
                DrainCombat(engine);
                Check("Eria: no free Monster Zone → no target, nothing taken",
                    flipped && !engine.IsAwaitingEffectTarget && opp.TryFindMonster(prey, out _));
            }

            // Battle: a Charmer flipped by an attack that destroys it selects no target.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                    Check("Aussa battle setup: reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                else
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var aussa = PlaceMonster(engine, p, Aussa, 0, BattlePosition.Defense, false);
                    var attacker = PlaceMonster(engine, opp, QueenOfAutumnLeaves, 0, BattlePosition.Attack, true);
                    attacker.ClearAttackFlags();
                    var prey = PlaceMonster(engine, opp, Kojikocy, 1, BattlePosition.Attack, true);
                    var atk = engine.TryAttack(opp, attacker, aussa);
                    DrainCombat(engine);
                    Check("Aussa: flipped and destroyed by battle → no target, nothing taken",
                        atk && !engine.IsAwaitingEffectTarget && p.Graveyard.Contains(aussa) &&
                        opp.TryFindMonster(prey, out _) && opp.TryFindMonster(attacker, out _),
                        $"atk={atk} await={engine.IsAwaitingEffectTarget} aussaInGy={p.Graveyard.Contains(aussa)}");
                }
            }

            // Battle: a Charmer that survives the attack takes control after damage calculation.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                    Check("Aussa battle setup (survives): reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                else
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var aussa = PlaceMonster(engine, p, Aussa, 0, BattlePosition.Defense, false);
                    var attacker = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true); // 1500 = DEF 1500
                    attacker.ClearAttackFlags();
                    var prey = PlaceMonster(engine, opp, Kojikocy, 1, BattlePosition.Attack, true);
                    var atk = engine.TryAttack(opp, attacker, aussa);
                    DrainCombat(engine);
                    var offered = engine.PendingActivation?.LegalTargets?.ToList();
                    var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(prey);
                    DrainCombat(engine);
                    Check("Aussa: flipped by an attack it survives → takes the EARTH monster (not the WATER attacker)",
                        atk && sel && offered != null && !offered.Contains(attacker) &&
                        p.TryFindMonster(prey, out _) && prey.ControlBoundTo == aussa &&
                        p.TryFindMonster(aussa, out _) && aussa.FaceUp && opp.TryFindMonster(attacker, out _),
                        $"atk={atk} sel={sel} offered={(offered == null ? "none" : string.Join(",", offered.Select(t => t.Name)))} preyOnP={p.TryFindMonster(prey, out _)}");
                }
            }

            // Remove Brainwashing: returns a Charmer's monster for good; while face-up it snaps a new take back.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var prey = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var rb = PlaceSetTrap(engine, p, RemoveBrainwashing, 0);
                FlipCharmerAndPick(engine, eria, prey, out _);
                var took = p.TryFindMonster(prey, out _);
                var act = engine.TryActivateSpellTrap(p, rb, fromHand: false);
                DrainCombat(engine);
                Check("Eria + Remove Brainwashing: the monster returns to its owner and the binding ends",
                    took && act && opp.TryFindMonster(prey, out _) && prey.ControlBoundTo == null,
                    $"took={took} act={act} onOpp={opp.TryFindMonster(prey, out _)}");
                engine.SendCardToGrave(p, rb);
                engine.NotifyPublic();
                Check("Eria + Remove Brainwashing: Remove Brainwashing leaving later does not give it back",
                    opp.TryFindMonster(prey, out _) && prey.ControlBoundTo == null, Where(engine, prey));
                engine.DestroyMonsterPublic(p, eria);
                engine.NotifyPublic();
                Check("Eria + Remove Brainwashing: the Charmer leaving later changes nothing",
                    opp.TryFindMonster(prey, out _) && !opp.Graveyard.Contains(prey));
            }
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var rb = PlaceSetTrap(engine, p, RemoveBrainwashing, 0);
                var rbAct = engine.TryActivateSpellTrap(p, rb, fromHand: false);
                DrainCombat(engine);
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var prey = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var lines = new System.Collections.Generic.List<string>();
                engine.OnLog += lines.Add;
                var flipped = engine.TryFlipSummon(p, eria);
                var offered = engine.PendingActivation?.LegalTargets?.ToList();
                var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(prey);
                DrainCombat(engine);
                engine.OnLog -= lines.Add;
                var tookLine = lines.Any(l => l.Contains($"{p.Name} takes control of {prey.Name}"));
                Check("Eria under a face-up Remove Brainwashing: the take is made, snaps back, and waits on Remove Brainwashing",
                    rbAct && rb.FaceUp && flipped && offered != null && offered.Contains(prey) && sel && tookLine &&
                    opp.TryFindMonster(prey, out _) && prey.ControlOwner == null && prey.ControlBoundTo == eria,
                    $"rbAct={rbAct} flipped={flipped} offered={(offered == null ? "none" : string.Join(",", offered.Select(t => t.Name)))} sel={sel} tookLine={tookLine} " +
                    Where(engine, prey));
                engine.SendCardToGrave(p, rb);
                engine.NotifyPublic();
                Check("Eria under Remove Brainwashing: once it leaves while Eria is face-up, Eria's control applies",
                    p.TryFindMonster(prey, out _) && prey.ControlOwner == opp && prey.ControlBoundTo == eria,
                    Where(engine, prey));
                engine.DestroyMonsterPublic(p, eria);
                engine.NotifyPublic();
                Check("Eria under Remove Brainwashing: … and Eria leaving then returns it to its owner",
                    opp.TryFindMonster(prey, out _) && prey.ControlOwner == null && prey.ControlBoundTo == null,
                    Where(engine, prey));
            }

            // AI Charmer: auto-picks the strongest legal target, no pending prompt.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(engine))
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var hiita = PlaceMonster(engine, opp, Hiita, 0, BattlePosition.Defense, false);
                    var fire1 = PlaceMonster(engine, p, DarkfireSoldier1, 0, BattlePosition.Attack, true);
                    var fire2 = PlaceMonster(engine, p, GreatAngus, 1, BattlePosition.Attack, true);
                    var earth = PlaceMonster(engine, p, QueenOfAutumnLeaves, 2, BattlePosition.Attack, true);
                    var ok = engine.TryFlipSummon(opp, hiita);
                    DrainCombat(engine);
                    Check("Hiita (AI): takes the strongest face-up FIRE monster, no prompt",
                        ok && !engine.IsAwaitingEffectTarget && opp.TryFindMonster(fire2, out _) &&
                        fire2.ControlBoundTo == hiita && p.TryFindMonster(fire1, out _) &&
                        p.TryFindMonster(earth, out _),
                        $"ok={ok} await={engine.IsAwaitingEffectTarget} fire2OnOpp={opp.TryFindMonster(fire2, out _)}");
                }
                else
                    Check("Hiita (AI) setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
            }
        }

        // ───────────────────────────── Invader of the Throne ─────────────────────────────

        static void InvaderDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Chk Check)
        {
            // Main Phase Flip Summon: switch with a Set monster, no free zone needed, permanent.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var invader = PlaceMonster(engine, p, Invader, 2, BattlePosition.Defense, false);
                foreach (var z in new[] { 0, 1, 3, 4 })
                    PlaceMonster(engine, p, Kojikocy, z, BattlePosition.Attack, true);
                var faceUp = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(engine, opp, Uraby, 3, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, invader);
                var offered = engine.PendingActivation?.LegalTargets?.ToList();
                Check("Invader: FLIP offers every monster the opponent controls (face-up or Set)",
                    flipped && offered != null && offered.Contains(faceUp) && offered.Contains(set) &&
                    offered.Count == 2,
                    offered == null ? "no prompt" : string.Join(",", offered.Select(t => t.Name)));
                var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(set);
                DrainCombat(engine);
                Check("Invader: switches control with the target (each takes the other's zone)",
                    sel && p.MonsterZones[2].Occupant == set && opp.MonsterZones[3].Occupant == invader &&
                    set.ControlOwner == opp && invader.ControlOwner == p && !set.FaceUp,
                    $"sel={sel} p2={p.MonsterZones[2].Occupant?.Name} o3={opp.MonsterZones[3].Occupant?.Name}");
                engine.EndTurn(p);
                Check("Invader: the switch is permanent (not undone in the End Phase)",
                    p.TryFindMonster(set, out _) && opp.TryFindMonster(invader, out _));
            }

            // Battle Phase: flipped by an attack → the effect cannot be activated.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                    Check("Invader battle setup: reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                else
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var invader = PlaceMonster(engine, p, Invader, 0, BattlePosition.Defense, false);
                    var attacker = PlaceMonster(engine, opp, Kojikocy, 0, BattlePosition.Attack, true); // 1500 < DEF 1700
                    attacker.ClearAttackFlags();
                    var other = PlaceMonster(engine, opp, QueenOfAutumnLeaves, 1, BattlePosition.Attack, true);
                    var atk = engine.TryAttack(opp, attacker, invader);
                    DrainCombat(engine);
                    Check("Invader: flipped during the Battle Phase → no switch, no prompt",
                        atk && !engine.IsAwaitingEffectTarget && invader.FaceUp &&
                        p.TryFindMonster(invader, out _) && opp.TryFindMonster(attacker, out _) &&
                        opp.TryFindMonster(other, out _),
                        $"atk={atk} await={engine.IsAwaitingEffectTarget} invOnP={p.TryFindMonster(invader, out _)} faceUp={invader.FaceUp}");
                }
            }

            // AI Invader: auto-picks the strongest monster, no pending prompt.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(engine))
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var invader = PlaceMonster(engine, opp, Invader, 0, BattlePosition.Defense, false);
                    var weak = PlaceMonster(engine, p, Kojikocy, 0, BattlePosition.Attack, true);
                    var strong = PlaceMonster(engine, p, QueenOfAutumnLeaves, 1, BattlePosition.Attack, true);
                    var ok = engine.TryFlipSummon(opp, invader);
                    DrainCombat(engine);
                    Check("Invader (AI): switches with the strongest monster, no prompt",
                        ok && !engine.IsAwaitingEffectTarget && opp.TryFindMonster(strong, out _) &&
                        p.TryFindMonster(invader, out _) && p.TryFindMonster(weak, out _),
                        $"ok={ok} await={engine.IsAwaitingEffectTarget} strongOnOpp={opp.TryFindMonster(strong, out _)}");
                }
                else
                    Check("Invader (AI) setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
            }
        }

        // ─────────────────── Charmers layered with other control effects ───────────────────
        // A Charmer's control is one layer over the monster's older control effects (ygopro: the most
        // recent EFFECT_SET_CONTROL that still applies decides the controller, else the owner). When it
        // ends, the effect it covered applies again; an effect layered over it falls back to that effect.

        /// <summary>From the opponent's turn to the player's Main Phase.</summary>
        static bool ReachPlayerMain(DuelEngine engine)
        {
            for (var i = 0; i < 6 && !(engine.TurnPlayer == engine.Player && engine.InMainPhase); i++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
            }

            return engine.TurnPlayer == engine.Player && engine.InMainPhase;
        }

        static string Where(DuelEngine engine, CardInstance m) =>
            $"{m?.Name}: onP={engine.Player.TryFindMonster(m, out _)} onOpp={engine.Opponent.TryFindMonster(m, out _)} " +
            $"ctrlOwner={m?.ControlOwner?.Name ?? "-"} bound={m?.ControlBoundTo?.Name ?? "-"} " +
            $"reverts={m?.ControlRevertsAtEndPhase} equipCtl={m?.TakenByEquipControl}";

        static void LayeredControlDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The owner takes its monster back from a Charmer with Brain Control (the AI's natural pick:
            // the strongest face-up monster), then the Charmer leaves: at the End Phase no control
            // effect is left, so the owner keeps it.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var took = FlipCharmerAndPick(engine, eria, theirs, out _) == theirs;
                engine.EndTurn(p);
                var reached = ReachOpponentMain(engine);
                opp.Hand.Clear();
                var bc = PutInHand(engine, opp, BrainControl);
                var act = engine.TryActivateSpellTrap(opp, bc, fromHand: true);
                DrainCombat(engine);
                var back = opp.TryFindMonster(theirs, out _);
                engine.DestroyMonsterPublic(p, eria);
                engine.NotifyPublic();
                var afterEria = opp.TryFindMonster(theirs, out _);
                engine.EndTurn(opp);
                Check("Layered: owner Brain Controls its monster back, the Charmer leaves → the owner keeps it after the End Phase",
                    took && reached && act && back && afterEria && opp.TryFindMonster(theirs, out _) &&
                    theirs.ControlOwner == null,
                    $"took={took} reached={reached} act={act} back={back} afterEria={afterEria} " + Where(engine, theirs));
            }

            // Mirror with Change of Heart: the AI's Charmer takes the player's monster, the player takes it
            // back until the End Phase, the Charmer leaves → the player keeps it after the End Phase.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentMain(engine))
                    Check("Layered (Change of Heart) setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                else
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var eria = PlaceMonster(engine, opp, Eria, 0, BattlePosition.Defense, false);
                    var mine = PlaceMonster(engine, p, Hyosube, 0, BattlePosition.Attack, true);
                    var flipped = engine.TryFlipSummon(opp, eria);
                    DrainCombat(engine);
                    var taken = opp.TryFindMonster(mine, out _) && mine.ControlBoundTo == eria;
                    engine.EndTurn(opp);
                    var reached = ReachPlayerMain(engine);
                    p.Hand.Clear();
                    var coh = PutInHand(engine, p, ChangeOfHeart);
                    var act = engine.TryActivateSpellTrap(p, coh, fromHand: true);
                    var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(mine);
                    DrainCombat(engine);
                    var back = p.TryFindMonster(mine, out _);
                    engine.DestroyMonsterPublic(opp, eria);
                    engine.NotifyPublic();
                    engine.EndTurn(p);
                    Check("Layered: owner Change-of-Hearts its monster back, the Charmer leaves → the owner keeps it after the End Phase",
                        flipped && taken && reached && act && sel && back && p.TryFindMonster(mine, out _) &&
                        mine.ControlOwner == null,
                        $"flipped={flipped} taken={taken} reached={reached} act={act} sel={sel} back={back} " + Where(engine, mine));
                }
            }

            // The owner takes its monster back with Snatch Steal, the Charmer leaves, then Snatch Steal
            // leaves → the owner keeps it (Snatch Steal's end must not hand it to the Charmer's controller).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var took = FlipCharmerAndPick(engine, eria, theirs, out _) == theirs;
                engine.EndTurn(p);
                var reached = ReachOpponentMain(engine);
                opp.Hand.Clear();
                var ss = PutInHand(engine, opp, SnatchSteal);
                var act = engine.TryActivateSpellTrap(opp, ss, fromHand: true);
                DrainCombat(engine);
                var back = opp.TryFindMonster(theirs, out _) && theirs.TakenByEquipControl;
                engine.DestroyMonsterPublic(p, eria);
                engine.NotifyPublic();
                var afterEria = opp.TryFindMonster(theirs, out _);
                engine.SendCardToGrave(opp, ss);
                engine.NotifyPublic();
                Check("Layered: owner Snatch-Steals its monster back, the Charmer leaves, then Snatch Steal leaves → the owner keeps it",
                    took && reached && act && back && afterEria && opp.TryFindMonster(theirs, out _) &&
                    theirs.ControlOwner == null,
                    $"took={took} reached={reached} act={act} back={back} afterEria={afterEria} " + Where(engine, theirs));
            }

            // Snatch Steal under the Charmer: the AI Snatch-Steals the player's monster, the player's Charmer
            // takes it back, the Charmer leaves → Snatch Steal (still equipped) applies again; when Snatch
            // Steal leaves, the owner gets it back.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentMain(engine))
                    Check("Layered (Snatch Steal under) setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                else
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var mine = PlaceMonster(engine, p, Hyosube, 0, BattlePosition.Attack, true);
                    var ss = PutInHand(engine, opp, SnatchSteal);
                    var act = engine.TryActivateSpellTrap(opp, ss, fromHand: true);
                    DrainCombat(engine);
                    var stolen = opp.TryFindMonster(mine, out _) && mine.TakenByEquipControl;
                    engine.EndTurn(opp);
                    var reached = ReachPlayerMain(engine);
                    p.Hand.Clear();
                    var eria = PlaceMonster(engine, p, Eria, 1, BattlePosition.Defense, false);
                    var retook = FlipCharmerAndPick(engine, eria, mine, out _) == mine;
                    engine.DestroyMonsterPublic(p, eria);
                    engine.NotifyPublic();
                    var ssAgain = opp.TryFindMonster(mine, out _) && mine.ControlOwner == p;
                    Check("Layered: Snatch Steal under the Charmer applies again when the Charmer leaves",
                        act && stolen && reached && retook && ssAgain,
                        $"act={act} stolen={stolen} reached={reached} retook={retook} " + Where(engine, mine));
                    engine.SendCardToGrave(opp, ss);
                    engine.NotifyPublic();
                    Check("Layered: … and when Snatch Steal then leaves, the owner gets the monster back",
                        ssAgain && p.TryFindMonster(mine, out _) && mine.ControlOwner == null, Where(engine, mine));
                }
            }

            // Change of Heart under the Charmer: the AI Change-of-Hearts the player's monster, attacks the
            // player's Set Eria (which survives and takes it back), then Eria leaves that turn → Change of
            // Heart applies again until the End Phase, then the owner gets it back.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentMain(engine))
                    Check("Layered (Change of Heart under) setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                else
                {
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Hand.Clear();
                    var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false); // DEF 1500
                    var mine = PlaceMonster(engine, p, Hyosube, 1, BattlePosition.Attack, true);
                    var attacker = PlaceMonster(engine, opp, Kojikocy, 0, BattlePosition.Attack, true); // 1500
                    var coh = PutInHand(engine, opp, ChangeOfHeart);
                    var act = engine.TryActivateSpellTrap(opp, coh, fromHand: true);
                    DrainCombat(engine);
                    var stolen = opp.TryFindMonster(mine, out _) && mine.ControlRevertsAtEndPhase;
                    var bp = engine.TryEnterBattlePhase(opp);
                    DrainCombat(engine);
                    attacker.ClearAttackFlags();
                    var atk = engine.TryAttack(opp, attacker, eria);
                    DrainCombat(engine);
                    var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(mine);
                    DrainCombat(engine);
                    var retook = p.TryFindMonster(mine, out _) && mine.ControlBoundTo == eria;
                    engine.DestroyMonsterPublic(p, eria);
                    engine.NotifyPublic();
                    var cohAgain = opp.TryFindMonster(mine, out _);
                    Check("Layered: Change of Heart under the Charmer applies again when the Charmer leaves that turn",
                        act && stolen && bp && atk && sel && retook && cohAgain,
                        $"act={act} stolen={stolen} bp={bp} atk={atk} sel={sel} retook={retook} " + Where(engine, mine));
                    engine.EndTurn(opp);
                    Check("Layered: … and at the End Phase the owner gets it back",
                        cohAgain && p.TryFindMonster(mine, out _) && mine.ControlOwner == null, Where(engine, mine));
                }
            }

            // A permanent switch under the Charmer: the player's Invader takes the AI's monster for good,
            // the AI's Eria takes it back, Eria leaves → the switch applies again.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var invader = PlaceMonster(engine, p, Invader, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, invader);
                var sel = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(theirs);
                DrainCombat(engine);
                var switched = p.TryFindMonster(theirs, out _);
                var aiEria = PlaceMonster(engine, opp, Eria, 1, BattlePosition.Defense, false);
                engine.EndTurn(p);
                var reached = ReachOpponentMain(engine);
                opp.Hand.Clear();
                var aiFlip = engine.TryFlipSummon(opp, aiEria);
                DrainCombat(engine);
                var retook = opp.TryFindMonster(theirs, out _) && theirs.ControlBoundTo == aiEria;
                engine.DestroyMonsterPublic(opp, aiEria);
                engine.NotifyPublic();
                Check("Layered: a permanent switch under the Charmer applies again when the Charmer leaves",
                    flipped && sel && switched && reached && aiFlip && retook && p.TryFindMonster(theirs, out _) &&
                    theirs.ControlOwner == opp,
                    $"flipped={flipped} sel={sel} switched={switched} reached={reached} aiFlip={aiFlip} retook={retook} " +
                    Where(engine, theirs));
            }

            // Two Charmers: the player's Eria takes the AI's monster, the AI's Eria takes it back. The AI's
            // Eria leaves → the player's Eria applies again; then the player's Eria leaves → the owner.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var took = FlipCharmerAndPick(engine, eria, theirs, out _) == theirs;
                var aiEria = PlaceMonster(engine, opp, Eria, 1, BattlePosition.Defense, false);
                engine.EndTurn(p);
                var reached = ReachOpponentMain(engine);
                opp.Hand.Clear();
                var aiFlip = engine.TryFlipSummon(opp, aiEria);
                DrainCombat(engine);
                var retook = opp.TryFindMonster(theirs, out _) && theirs.ControlBoundTo == aiEria;
                engine.DestroyMonsterPublic(opp, aiEria);
                engine.NotifyPublic();
                var firstAgain = p.TryFindMonster(theirs, out _) && theirs.ControlBoundTo == eria;
                Check("Layered: the second Charmer leaves → the first Charmer's control applies again",
                    took && reached && aiFlip && retook && firstAgain,
                    $"took={took} reached={reached} aiFlip={aiFlip} retook={retook} " + Where(engine, theirs));
                engine.DestroyMonsterPublic(p, eria);
                engine.NotifyPublic();
                Check("Layered: … then the first Charmer leaves → the owner gets it back",
                    firstAgain && opp.TryFindMonster(theirs, out _) && theirs.ControlOwner == null &&
                    theirs.ControlBoundTo == null, Where(engine, theirs));
            }

            // Remove Brainwashing while the owner holds its monster under Brain Control (over a Charmer):
            // the monster stays with its owner, and the control effects it had end for good (nothing
            // hands it to the Charmer's controller at the End Phase, even once Remove Brainwashing is gone).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var rb = PlaceSetTrap(engine, p, RemoveBrainwashing, 0);
                var took = FlipCharmerAndPick(engine, eria, theirs, out _) == theirs;
                engine.EndTurn(p);
                var reached = ReachOpponentMain(engine);
                opp.Hand.Clear();
                var bc = PutInHand(engine, opp, BrainControl);
                var act = engine.TryActivateSpellTrap(opp, bc, fromHand: true);
                DrainCombat(engine);
                var back = opp.TryFindMonster(theirs, out _);
                rb.SetThisTurn = false;
                var rbAct = engine.TryActivateSpellTrap(p, rb, fromHand: false);
                DrainCombat(engine);
                var stays = opp.TryFindMonster(theirs, out _);
                Check("Layered: Remove Brainwashing leaves the owner's monster with its owner",
                    took && reached && act && back && rbAct && stays,
                    $"took={took} reached={reached} act={act} back={back} rbAct={rbAct} " + Where(engine, theirs));
                engine.SendCardToGrave(p, rb);
                engine.NotifyPublic();
                engine.EndTurn(opp);
                Check("Layered: … and after Remove Brainwashing leaves, the End Phase does not hand it to the Charmer's controller",
                    stays && opp.TryFindMonster(theirs, out _) && p.TryFindMonster(eria, out _),
                    Where(engine, theirs));
            }

            // Stolen monster turned face-down while the owner holds it under Brain Control: the Charmer's
            // effect ends; Brain Control ends at the End Phase and the owner keeps it.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var eria = PlaceMonster(engine, p, Eria, 0, BattlePosition.Defense, false);
                var theirs = PlaceMonster(engine, opp, Hyosube, 0, BattlePosition.Attack, true);
                var took = FlipCharmerAndPick(engine, eria, theirs, out _) == theirs;
                engine.EndTurn(p);
                var reached = ReachOpponentMain(engine);
                opp.Hand.Clear();
                var bc = PutInHand(engine, opp, BrainControl);
                var act = engine.TryActivateSpellTrap(opp, bc, fromHand: true);
                DrainCombat(engine);
                var back = opp.TryFindMonster(theirs, out _);
                var moon = PutInHand(engine, opp, BookOfMoon);
                var moonAct = engine.TryActivateSpellTrap(opp, moon, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(theirs);
                DrainCombat(engine);
                engine.NotifyPublic();
                var faceDown = !theirs.FaceUp && theirs.ControlBoundTo == null;
                engine.EndTurn(opp);
                Check("Layered: the owner's Brain-Controlled monster turned face-down → the owner keeps it after the End Phase",
                    took && reached && act && back && moonAct && faceDown && opp.TryFindMonster(theirs, out _),
                    $"took={took} reached={reached} act={act} back={back} moonAct={moonAct} faceDown={faceDown} " +
                    Where(engine, theirs));
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            bool Claims(string type, string attr, string desc, Func<EffectClause, bool> shape)
            {
                var def = new CardDef
                {
                    id = 990141901, name = "ARGON TakeControl near miss", type = type, desc = desc,
                    race = "Spellcaster", attribute = attr, level = 3, atk = 500, def = 1500
                };
                var prog = CardTextEffectCompiler.Compile(def);
                return prog != null && prog.ClauseList.Exists(c => c != null && shape(c));
            }

            bool IsCharmer(EffectClause c) => c.Action == EffectActionKind.TakeControlWhileSourceFaceUp;
            bool IsSwitch(EffectClause c) => c.Action == EffectActionKind.SwitchControlWithThis;

            Check("Near miss: FLIP take control until the End Phase is not a Charmer",
                !Claims("Flip Effect Monster", "WATER",
                    "FLIP: Target 1 face-up WATER monster your opponent controls; take control of that target until the End Phase.",
                    IsCharmer));
            Check("Near miss: non-FLIP while-face-up take control is not the Charmer atom",
                !Claims("Effect Monster", "WATER",
                    "Target 1 face-up WATER monster your opponent controls; take control of that target while this card is face-up on the field.",
                    IsCharmer));
            {
                var def = new CardDef
                {
                    id = 990141902, name = "ARGON Charmer rider near miss", type = "Flip Effect Monster",
                    desc = "FLIP: Target 1 face-up WATER monster your opponent controls; take control of that target while this card is face-up on the field. That monster cannot attack.",
                    race = "Spellcaster", attribute = "WATER", level = 3, atk = 500, def = 1500
                };
                var prog = CardTextEffectCompiler.Compile(def);
                Check("Near miss: a Charmer sentence with an extra rider is not FullyCompiled",
                    prog == null || !prog.FullyCompiled, Describe(prog));
            }
            Check("Near miss: switch control without the Battle Phase sentence is not Invader",
                !Claims("Flip Effect Monster", "EARTH",
                    "FLIP: Select 1 monster on your opponent's side of the field and switch control of it with this card.",
                    IsSwitch));
            var swap = db.Get(CreatureSwap);
            var swapProg = swap != null ? CardTextEffectCompiler.Compile(swap) : null;
            Check("Near miss: Creature Swap is not SwitchControlWithThis",
                swapProg == null || !swapProg.ClauseList.Exists(c => c != null && IsSwitch(c)), Describe(swapProg));
            Check("Near miss: Pay LP take-control without the Normal Summon/Set clause has no NS/Set filter",
                !Claims("Spell Card", "",
                    "Pay 800 LP, then target 1 face-up monster your opponent controls; take control of that target until the End Phase.",
                    c => c.TargetMustBeNormalSummonable));
        }

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/{c.AttributeFilter}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
