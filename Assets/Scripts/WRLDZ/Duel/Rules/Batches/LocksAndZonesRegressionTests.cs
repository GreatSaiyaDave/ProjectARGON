using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "LocksAndZones" (v142): Cold Wave (start-of-Main-Phase-1 Spell/Trap lock until your next
    /// turn), Dimensionhole (banish your monster until your next Standby Phase; its zone cannot be used) and
    /// Light of Intervention (no monster can be Set or turned face-down; a Set is a face-up Defense Normal
    /// Summon). Ground Collapse stays unimplemented (see the batch notes); its fail-closed check is here too.
    /// Curse of Fiend failed closed on 2026-10-04 and is Implemented again since the SimultaneousTriggers batch (v160:
    /// its FLIP effects go on one Chain through the SEGOC queue); the guard below pins its compiled shape.
    /// </summary>
    public static class LocksAndZonesRegressionTests
    {
        const int ColdWave = 60682203;
        const int Dimensionhole = 22959079;
        const int GroundCollapse = 90502999;
        const int CurseOfFiend = 12470447;
        const int LightOfIntervention = 62867251;

        const int PotOfGreed = 55144522;
        const int MirrorForce = 44095762;
        const int TrapHole = 4206964;
        const int Yami = 59197169;
        const int StealthBird = 3510565;       // "Once per turn: You can change this card to face-down Defense Position."
        const int BookOfMoon = 14087893;
        const int SwordsOfConcealingLight = 12923641;
        const int RoyalDecree = 51452091;
        const int MagicalHats = 81210420;
        const int Mst = 5318639;
        const int MagicJammer = 77414722;      // Counter Trap: discard 1 card; negate a Spell Card's activation
        const int Kuriboh = 40640057;
        const int ManEaterBug = 54652250;      // FLIP: target 1 monster on the field; destroy it
        const int Celtic = 91152256;           // Warrior Lv4 1400/1200
        const int GiantSoldier = 13039848;     // Rock Lv3 1300/2000
        const int SkullServant = 32274490;     // Zombie Lv1 300/200

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("LocksAndZones: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            ColdWaveDuels(db, pDeck, aDeck, Check);
            DimensionholeDuels(db, pDeck, aDeck, Check);
            LightOfInterventionDuels(db, pDeck, aDeck, Check);
            ColdWaveTriggerLock(db, pDeck, aDeck, Check);
            ReviewFixDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static EffectClause OnlyClause(CardDatabase db, int id, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
        }

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            var cw = OnlyClause(db, ColdWave, out var cwProg);
            Check("Cold Wave: FullyCompiled, one untargeted Activate lock gated on the start of Main Phase 1",
                cwProg != null && cwProg.FullyCompiled && cwProg.CanResolveAny && cw != null &&
                cw.Timing == EffectTiming.Activate &&
                cw.Action == EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn &&
                cw.Side == EffectSide.Both && !cw.RequiresTargetChoice && cw.MakesChainLink &&
                cw.ActivationCondition == LocksAndZonesRuntime.StartOfMainPhase1 &&
                cw.CheckedAt == ConditionCheckedAt.Activation && !cw.StaysOnField,
                cw == null ? $"clauses={cwProg?.ClauseList.Count}" : EffectVocabulary.Describe(cw));
            Check("Cold Wave: classified Implemented",
                CardEffectStatus.Classify(db.Get(ColdWave)) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(db.Get(ColdWave)).ToString());

            var dh = OnlyClause(db, Dimensionhole, out var dhProg);
            Check("Dimensionhole: FullyCompiled, one Activate targeting 1 monster you control",
                dhProg != null && dhProg.FullyCompiled && dhProg.CanResolveAny && dh != null &&
                dh.Timing == EffectTiming.Activate &&
                dh.Action == EffectActionKind.BanishTargetUntilYourNextStandbyPhase &&
                dh.Zone == EffectZoneFilter.ControllerAnyMonsters && dh.Side == EffectSide.Controller &&
                dh.RequiresTargetChoice && dh.IsPsctTarget && dh.Amount == 1 && dh.MakesChainLink &&
                string.IsNullOrEmpty(dh.ActivationCondition) && !dh.StaysOnField,
                dh == null ? $"clauses={dhProg?.ClauseList.Count}" : EffectVocabulary.Describe(dh));
            Check("Dimensionhole: classified Implemented",
                CardEffectStatus.Classify(db.Get(Dimensionhole)) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(db.Get(Dimensionhole)).ToString());

            Check("New actions map to shared resolutions (Protection / Banish)",
                EffectVocabulary.ResolutionOf(new EffectClause
                        { Action = EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn }) ==
                    EffectResolutionKind.Protection &&
                EffectVocabulary.ResolutionOf(new EffectClause
                        { Action = EffectActionKind.BanishTargetUntilYourNextStandbyPhase }) ==
                    EffectResolutionKind.Banish &&
                EffectVocabulary.ResolutionOf(new EffectClause
                        { Action = EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown }) ==
                    EffectResolutionKind.Protection);

            var loi = OnlyClause(db, LightOfIntervention, out var loiProg);
            Check("Light of Intervention: FullyCompiled, one continuous both-player Set / face-down lock",
                loiProg != null && loiProg.FullyCompiled && loiProg.CanResolveAny && loi != null &&
                loi.Timing == EffectTiming.ContinuousWhileFaceUp &&
                loi.Action == EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown &&
                loi.Side == EffectSide.Both && loi.StaysOnField && !loi.RequiresTargetChoice,
                loi == null ? $"clauses={loiProg?.ClauseList.Count}" : EffectVocabulary.Describe(loi));
            Check("Light of Intervention: classified Implemented",
                CardEffectStatus.Classify(db.Get(LightOfIntervention)) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(db.Get(LightOfIntervention)).ToString());

            // Deferred: Ground Collapse stays fail-closed.
            {
                var def = db.Get(GroundCollapse);
                var prog = CardTextEffectCompiler.Compile(def);
                Check($"{def?.name}: stays fail-closed (not Implemented)",
                    (prog == null || !prog.FullyCompiled) &&
                    CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(def).ToString());
            }

            {
                // Failed closed by the GrokSpiritsLocks hardening (2026-10-04) for want of one Chain for the swap's
                // simultaneous FLIP effects; Implemented again by the SimultaneousTriggers batch (v160, SEGOC queue).
                var def = db.Get(CurseOfFiend);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Curse of Fiend: compiles to the whole-text mass change + lock (SimultaneousTriggers, v160)",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn && c.OnlyDuringOwnStandby &&
                    c.ActivationCondition == PositionLocksRuntime.SetInYourStandbyPhase &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    def != null ? CardEffectStatus.Classify(def).ToString() : "missing");
            }
        }

        static CompiledCardProgram CompileSpell(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990142, name = "Near-miss Spell", type = "Spell Card", frameType = "spell", race = "Normal",
                desc = text
            });

        static bool CompilesTo(string text, EffectActionKind action)
        {
            var prog = CompileSpell(text);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        static bool AnyClauseWith(string text, EffectActionKind action)
        {
            var prog = CompileSpell(text);
            return prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        static CompiledCardProgram CompileContinuousTrap(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990143, name = "Near-miss Trap", type = "Trap Card", frameType = "trap", race = "Continuous",
                desc = text
            });

        static bool CompilesToTrap(string text, EffectActionKind action)
        {
            var prog = CompileContinuousTrap(text);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        static bool AnyTrapClauseWith(string text, EffectActionKind action)
        {
            var prog = CompileContinuousTrap(text);
            return prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            const string coldWave =
                "This card can only be activated at the start of Main Phase 1. Until your next turn, you and your " +
                "opponent cannot play or Set any Spell or Trap Cards.";
            Check("Near miss: Cold Wave's own text compiles (control)",
                CompilesTo(coldWave, EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn));
            Check("Near miss: an extra sentence after Cold Wave's text is not compiled",
                !AnyClauseWith(coldWave + " Then draw 1 card.", EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn));
            Check("Near miss: the lock without the start-of-Main-Phase-1 condition is not compiled",
                !AnyClauseWith(
                    "Until your next turn, you and your opponent cannot play or Set any Spell or Trap Cards.",
                    EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn));
            Check("Near miss: a one-player (opponent only) lock is not this template",
                !AnyClauseWith(
                    "This card can only be activated at the start of Main Phase 1. Until your next turn, your " +
                    "opponent cannot play or Set any Spell or Trap Cards.",
                    EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn));
            Check("Near miss: a Spell-only lock is not this template",
                !AnyClauseWith(
                    "This card can only be activated at the start of Main Phase 1. Until your next turn, you and " +
                    "your opponent cannot play or Set any Spell Cards.",
                    EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn));

            const string dimensionhole =
                "Select 1 monster on your side of the field and remove it from play until your next Standby " +
                "Phase. While the monster is removed from play, the Monster Card Zone of the selected monster " +
                "cannot be used.";
            Check("Near miss: Dimensionhole's own text compiles (control)",
                CompilesTo(dimensionhole, EffectActionKind.BanishTargetUntilYourNextStandbyPhase));
            Check("Near miss: the banish alone (no zone sentence) is not this template",
                !AnyClauseWith(
                    "Select 1 monster on your side of the field and remove it from play until your next Standby " +
                    "Phase.", EffectActionKind.BanishTargetUntilYourNextStandbyPhase));
            Check("Near miss: an End Phase return (Interdimensional Matter Transporter shape) is not this template",
                !AnyClauseWith(
                    "Select 1 monster on your side of the field and remove it from play until the End Phase. While " +
                    "the monster is removed from play, the Monster Card Zone of the selected monster cannot be used.",
                    EffectActionKind.BanishTargetUntilYourNextStandbyPhase));
            const string loiText =
                "While this card is on the field, Monster Cards cannot be Set or flipped face-down. If a Monster " +
                "Card would be Set, it must be Normal Summoned in face-up Defense Position.";
            Check("Near miss: Light of Intervention's own text compiles (control)",
                CompilesToTrap(loiText, EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown));
            Check("Near miss: the Set lock alone (no face-up Defense replacement) is not this template",
                !AnyTrapClauseWith("While this card is on the field, Monster Cards cannot be Set or flipped face-down.",
                    EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown));
            Check("Near miss: a face-up Attack replacement is not this template",
                !AnyTrapClauseWith(
                    "While this card is on the field, Monster Cards cannot be Set or flipped face-down. If a Monster " +
                    "Card would be Set, it must be Normal Summoned in face-up Attack Position.",
                    EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown));
            Check("Near miss: the opponent's monster is not this template",
                !AnyClauseWith(
                    "Select 1 monster on your opponent's side of the field and remove it from play until your next " +
                    "Standby Phase. While the monster is removed from play, the Monster Card Zone of the selected " +
                    "monster cannot be used.", EffectActionKind.BanishTargetUntilYourNextStandbyPhase));
        }

        // ───────────────────────── helpers ─────────────────────────

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static void EndTurn(DuelEngine engine)
        {
            PassAll(engine);
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            PassAll(engine);
        }

        /// <summary>End turns until <paramref name="who"/>'s Main Phase 1 of a turn after the current one.</summary>
        static bool NextMainOf(DuelEngine engine, DuelistState who)
        {
            var start = engine.TurnNumber;
            for (var t = 0; t < 4; t++)
            {
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby && engine.TurnPlayer == who) engine.ContinueAfterStandby();
                PassAll(engine);
                if (engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber > start)
                    return true;
            }

            return false;
        }

        /// <summary>Run the AI's whole turn (SimpleAi) headlessly; the player passes every window.</summary>
        static bool RunAiTurn(DuelEngine e, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    if (!it.MoveNext()) return true;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        static bool Activate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            CardInstance target = null)
        {
            if (!engine.TryActivateSpellTrap(who, card, fromHand)) return false;
            if (engine.IsAwaitingEffectTarget && target != null) engine.TrySelectEffectTarget(target);
            PassAll(engine);
            return !engine.IsAwaitingEffectTarget;
        }

        // ───────────────────────── Cold Wave ─────────────────────────

        static void ColdWaveDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // Main line: Cold Wave at the start of the player's Main Phase 1 locks both players through the
            // opponent's next turn; Spell/Trap activations and Sets come back on the player's next turn.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var pot = PutInHand(engine, p, PotOfGreed);
                var hole = PutInHand(engine, p, TrapHole);
                var mirror = PlaceSetTrap(engine, p, MirrorForce, 0);
                var oTrap = PutInHand(engine, o, TrapHole);
                var attacker = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);

                var reached = NextMainOf(engine, p);
                var turn = engine.TurnNumber;
                var legalAtStart = reached && engine.CanActivateSpellTrap(p, cw, true);
                Check("Cold Wave: legal at the start of your Main Phase 1",
                    legalAtStart, $"reached={reached} turn={turn} phase={engine.Phase}");

                var activated = legalAtStart && Activate(engine, p, cw, fromHand: true);
                Check("Cold Wave: resolves and goes to the GY; the lock lasts through the next turn",
                    activated && p.Graveyard.Contains(cw) && p.SpellTrapPlayLockedThroughTurn == turn + 1 &&
                    LocksAndZonesRuntime.SpellTrapLockActive(engine),
                    $"activated={activated} gy={p.Graveyard.Contains(cw)} through={p.SpellTrapPlayLockedThroughTurn} turn={turn}");

                Check("Cold Wave: your Spells cannot be activated this turn",
                    !engine.CanActivateSpellTrap(p, pot, true) && !engine.TryActivateSpellTrap(p, pot, true) &&
                    p.Hand.Contains(pot));
                Check("Cold Wave: your Set Trap cannot be activated this turn",
                    !engine.CanActivateSpellTrap(p, mirror, false));
                Check("Cold Wave: you cannot Set a Spell/Trap this turn",
                    !engine.CanSetSpellTrap(p, hole) && !engine.TrySetSpellTrap(p, hole) && p.Hand.Contains(hole));
                // Summoning still works (the lock is Spell/Trap only).
                var summoner = PutInHand(engine, p, SkullServant);
                var summoned = engine.TryNormalSummon(p, summoner, false);
                PassAll(engine);
                Check("Cold Wave: monsters can still be Summoned", summoned && p.TryFindMonster(summoner, out _));

                // Opponent's turn: still locked for both players.
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                var oppTurn = engine.TurnPlayer == o && engine.InMainPhase && engine.TurnNumber == turn + 1;
                Check("Cold Wave: the opponent cannot Set a Spell/Trap during their next turn",
                    oppTurn && !engine.CanSetSpellTrap(o, oTrap) && LocksAndZonesRuntime.SpellTrapLockActive(engine),
                    $"oppTurn={oppTurn} turn={engine.TurnNumber}");
                // The opponent attacks: the player's Set Mirror Force cannot answer it.
                var battle = oppTurn && engine.TryEnterBattlePhase(o);
                var mirrorOffered = false;
                var attacked = false;
                if (battle && engine.CanAttack(o, attacker))
                {
                    var target = p.MonstersOnField().FirstOrDefault();
                    attacked = engine.TryAttack(o, attacker, target);
                    mirrorOffered = engine.PendingResponse?.LegalCards != null &&
                                    engine.PendingResponse.LegalCards.Contains(mirror);
                    DrainCombat(engine);
                }

                Check("Cold Wave: your Set Trap cannot answer the opponent's attack during their turn",
                    battle && attacked && !mirrorOffered && p.SpellTrapsOnField().Contains(mirror) &&
                    !mirror.FaceUp && o.TryFindMonster(attacker, out _),
                    $"battle={battle} attacked={attacked} offered={mirrorOffered}");

                // Your next turn: the lock is over.
                var back = NextMainOf(engine, p);
                Check("Cold Wave: the lock ends when your next turn begins",
                    back && engine.TurnNumber == turn + 2 && !LocksAndZonesRuntime.SpellTrapLockActive(engine) &&
                    engine.CanSetSpellTrap(p, hole) && engine.CanActivateSpellTrap(p, pot, true),
                    $"back={back} turn={engine.TurnNumber}");
            }

            // The AI plays its turn under the lock: it neither activates nor Sets Spells/Traps, and the turn ends.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                o.Deck.Clear();
                for (var i = 0; i < 10; i++) o.Deck.Add(TrapHole);
                var cw = PutInHand(engine, p, ColdWave);
                var pot = PutInHand(engine, o, PotOfGreed);
                var monster = PutInHand(engine, o, Celtic);
                var reached = NextMainOf(engine, p);
                var locked = reached && Activate(engine, p, cw, fromHand: true) &&
                             LocksAndZonesRuntime.SpellTrapLockActive(engine);
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                var aiTurn = engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1;
                var done = aiTurn && RunAiTurn(engine, 400);
                Check("Cold Wave: the AI plays its turn under the lock (no Spell/Trap played or Set; its turn ends)",
                    locked && done && engine.TurnPlayer == p && o.Hand.Contains(pot) &&
                    !o.SpellTrapsOnField().Any() && o.TryFindMonster(monster, out _),
                    $"locked={locked} aiTurn={aiTurn} done={done} tp={engine.TurnPlayer?.Name} potInHand={o.Hand.Contains(pot)} " +
                    $"st={o.SpellTrapsOnField().Count()} summoned={o.TryFindMonster(monster, out _)}");
            }

            // Negated by Magic Jammer (the AI answers the activation): no lock.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var jammer = PlaceSetTrap(engine, o, MagicJammer, 2);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh);
                PutInHand(engine, o, Celtic);
                var reached = NextMainOf(engine, p);
                var tried = reached && Activate(engine, p, cw, fromHand: true);
                DrainCombat(engine);
                Check("Cold Wave: negated by Magic Jammer — no lock",
                    tried && o.Graveyard.Contains(jammer) && p.Graveyard.Contains(cw) &&
                    !LocksAndZonesRuntime.SpellTrapLockActive(engine) && p.SpellTrapPlayLockedThroughTurn == 0,
                    $"reached={reached} tried={tried} jammerGy={o.Graveyard.Contains(jammer)} lock={p.SpellTrapPlayLockedThroughTurn}");
            }

            // Only at the START of Main Phase 1: after any other action it cannot be activated.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var monster = PutInHand(engine, p, Celtic);
                var reached = NextMainOf(engine, p);
                var before = reached && engine.CanActivateSpellTrap(p, cw, true);
                var summoned = before && engine.TryNormalSummon(p, monster, false);
                PassAll(engine);
                Check("Cold Wave: not legal once something happened in Main Phase 1 (a Summon)",
                    before && summoned && !engine.CanActivateSpellTrap(p, cw, true) &&
                    !engine.TryActivateSpellTrap(p, cw, true) && p.Hand.Contains(cw),
                    $"reached={reached} before={before} summoned={summoned}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var reached = NextMainOf(engine, p);
                var main2 = reached && engine.TryEnterMainPhase2(p);
                Check("Cold Wave: not legal in Main Phase 2",
                    main2 && engine.Phase == DuelPhase.Main2 && !engine.CanActivateSpellTrap(p, cw, true),
                    $"reached={reached} phase={engine.Phase}");
            }

            // A Set Cold Wave is legal at the start too, and a Field Spell cannot be placed under the lock.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var cw = PlaceSetTrap(engine, p, ColdWave, 1);
                var yami = PutInHand(engine, p, Yami);
                var reached = NextMainOf(engine, p);
                var activated = reached && engine.CanActivateSpellTrap(p, cw, false) &&
                                Activate(engine, p, cw, fromHand: false);
                Check("Cold Wave: a Set copy activates at the start of Main Phase 1",
                    activated && p.Graveyard.Contains(cw) && LocksAndZonesRuntime.SpellTrapLockActive(engine),
                    $"reached={reached} activated={activated}");
                Check("Cold Wave: a Field Spell cannot be activated or placed under the lock",
                    !engine.CanActivateSpellTrap(p, yami, true) && !engine.TryPlaceFieldSpell(p, yami) &&
                    p.Hand.Contains(yami) && p.FieldSpellZone.Occupant == null);
            }
        }

        // ───────────────────────── Dimensionhole ─────────────────────────

        static void DimensionholeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // Main line: banish, the zone is unusable, it returns in your next Standby Phase to the same zone.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                var celtic = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                var other = PutInHand(engine, p, SkullServant);
                var turn = engine.TurnNumber;
                var legal = engine.CanActivateSpellTrap(p, hole, true);
                var activated = legal && Activate(engine, p, hole, fromHand: true, target: celtic);
                var zone = p.MonsterZones[2];
                Check("Dimensionhole: banishes your monster and its zone cannot be used",
                    activated && p.Banished.Contains(celtic) && zone.Occupant == null &&
                    zone.UnusableFor == celtic && !zone.IsEmpty && p.Graveyard.Contains(hole) &&
                    p.TemporaryBanishments.Count == 1,
                    $"legal={legal} activated={activated} banished={p.Banished.Contains(celtic)} lock={zone.UnusableFor?.Name}");
                Check("Dimensionhole: the unusable zone is not offered for a Summon",
                    engine.FirstEmpty(p.MonsterZones) != 2 &&
                    !engine.ValidatePlacement(p, other, RulesZoneKind.Monster, 2, preferSet: false).Legal);
                var summoned = engine.TryNormalSummonToZone(p, other, false, 2);
                PassAll(engine);
                Check("Dimensionhole: a Summon aimed at that zone lands in another zone",
                    summoned && p.TryFindMonster(other, out var otherZone) && otherZone != 2 &&
                    p.MonsterZones[2].Occupant == null);

                // Opponent's turn: still banished, zone still unusable.
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                Check("Dimensionhole: still banished during the opponent's turn",
                    engine.TurnPlayer == o && p.Banished.Contains(celtic) && p.MonsterZones[2].UnusableFor == celtic,
                    $"turn={engine.TurnNumber} player={engine.TurnPlayer?.Name}");

                // Your next Standby Phase: it returns to the same zone, in Attack Position, not as a Summon.
                EndTurn(engine);
                var standbyTurn = engine.TurnNumber;
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                Check("Dimensionhole: returns during your next Standby Phase to the same zone and position",
                    standbyTurn == turn + 2 && p.MonsterZones[2].Occupant == celtic && celtic.FaceUp &&
                    celtic.Position == BattlePosition.Attack && !p.Banished.Contains(celtic) &&
                    p.MonsterZones[2].UnusableFor == null && p.TemporaryBanishments.Count == 0,
                    $"turn={standbyTurn} occ={p.MonsterZones[2].Occupant?.Name} pos={celtic.Position}");
                Check("Dimensionhole: the returned monster was not Summoned — it may change position this turn",
                    engine.TurnPlayer == p && engine.InMainPhase && !celtic.SummonedThisTurn &&
                    !celtic.WasSpecialSummoned && engine.CanChangePosition(p, celtic));
            }

            // Your next Standby Phase is skipped (Solomon's Lawbook): the monster does not return then, and the
            // return lapses (ygopro: reset after two Standby Phases) — its zone stays unusable while it stays banished.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                var celtic = PlaceMonster(engine, p, Celtic, 4, BattlePosition.Attack, true);
                var activated = Activate(engine, p, hole, fromHand: true, target: celtic);
                p.SkipNextStandbyPhase = true;
                var first = NextMainOf(engine, p);
                var stillBanished = p.Banished.Contains(celtic) && p.MonsterZones[4].UnusableFor == celtic;
                var second = NextMainOf(engine, p);
                Check("Dimensionhole: a skipped Standby Phase means no return (the zone stays unusable while banished)",
                    activated && first && stillBanished && second && p.Banished.Contains(celtic) &&
                    p.MonsterZones[4].Occupant == null && p.MonsterZones[4].UnusableFor == celtic,
                    $"activated={activated} first={first} stillBanished={stillBanished} second={second} " +
                    $"banished={p.Banished.Contains(celtic)} occ={p.MonsterZones[4].Occupant?.Name}");
            }

            // A face-down Defense monster returns face-down; with every other zone full, nothing can be Summoned.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                var setMonster = PlaceMonster(engine, p, GiantSoldier, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, p, SkullServant, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, p, SkullServant, 2, BattlePosition.Attack, true);
                PlaceMonster(engine, p, SkullServant, 3, BattlePosition.Attack, true);
                PlaceMonster(engine, p, SkullServant, 4, BattlePosition.Attack, true);
                var extra = PutInHand(engine, p, Celtic);
                var tried = engine.TryActivateSpellTrap(p, hole, true);
                var prompt = engine.PendingActivation;
                var offered = prompt?.LegalTargets?.Count ?? 0;
                var faceDownOffered = prompt?.LegalTargets != null && prompt.LegalTargets.Contains(setMonster);
                var picked = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(setMonster);
                PassAll(engine);
                var activated = tried && picked && !engine.IsAwaitingEffectTarget;
                Check("Dimensionhole: you choose the target among every monster you control (face-down included)",
                    tried && offered == 5 && faceDownOffered && picked,
                    $"tried={tried} offered={offered} faceDown={faceDownOffered} picked={picked}");
                Check("Dimensionhole: with the other four zones full, the banished monster's zone blocks any Summon",
                    activated && p.Banished.Contains(setMonster) && engine.FirstEmpty(p.MonsterZones) < 0 &&
                    !engine.CanNormalSummonOrSet(p, extra),
                    $"activated={activated} banished={p.Banished.Contains(setMonster)}");
                var back = NextMainOf(engine, p);
                Check("Dimensionhole: a face-down Defense monster returns face-down Defense to its zone",
                    back && p.MonsterZones[0].Occupant == setMonster && !setMonster.FaceUp &&
                    setMonster.Position == BattlePosition.Defense && p.MonsterZones[0].UnusableFor == null,
                    $"back={back} occ={p.MonsterZones[0].Occupant?.Name} faceUp={setMonster.FaceUp}");
            }

            // The monster leaves the banishment another way: the zone is free again and nothing returns.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                var celtic = PlaceMonster(engine, p, Celtic, 3, BattlePosition.Attack, true);
                var activated = Activate(engine, p, hole, fromHand: true, target: celtic);
                p.Banished.Remove(celtic);
                p.Deck.Add(celtic.CardId);
                engine.NotifyPublic();
                Check("Dimensionhole: once the monster is no longer banished its zone can be used again",
                    activated && p.MonsterZones[3].UnusableFor == null && p.MonsterZones[3].IsEmpty &&
                    p.TemporaryBanishments.Count == 0,
                    $"activated={activated} lock={p.MonsterZones[3].UnusableFor?.Name}");
                var back = NextMainOf(engine, p);
                Check("Dimensionhole: nothing returns for a monster that already left the banishment",
                    back && p.MonsterZones[3].Occupant == null && !p.MonstersOnField().Contains(celtic));
            }

            // A Token banished this way ceases to exist: no return, no locked zone.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                var token = engine.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
                token.Position = BattlePosition.Defense;
                token.FaceUp = true;
                p.MonsterZones[1].Occupant = token;
                var activated = Activate(engine, p, hole, fromHand: true, target: token);
                Check("Dimensionhole: a Token ceases to exist and its zone stays usable",
                    activated && !p.Banished.Contains(token) && p.MonsterZones[1].Occupant == null &&
                    p.MonsterZones[1].IsEmpty && p.TemporaryBanishments.Count == 0,
                    $"activated={activated} banished={p.Banished.Contains(token)}");
            }

            // Its activation is a Chain Link the opponent can answer: a human opponent gets the chain window
            // (the target is already chosen); the AI's Set Magic Jammer negates it — nothing is banished.
            {
                var engine = Fresh(db, pDeck, aDeck);
                engine.HumanVsHuman = true;
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                var a = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, p, SkullServant, 3, BattlePosition.Attack, true);
                PlaceSetTrap(engine, o, MagicJammer, 2);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh);
                var tried = engine.TryActivateSpellTrap(p, hole, true);
                var picked = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(a);
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o;
                var notYet = p.TryFindMonster(a, out _);
                PassAll(engine);
                Check("Dimensionhole: the opponent may chain to it before the target is banished",
                    tried && picked && window && notYet && p.Banished.Contains(a) && p.MonsterZones[1].UnusableFor == a,
                    $"tried={tried} picked={picked} window={window} notYet={notYet} banished={p.Banished.Contains(a)}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                var a = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var jammer = PlaceSetTrap(engine, o, MagicJammer, 2);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh);
                PutInHand(engine, o, Celtic);
                var tried = Activate(engine, p, hole, fromHand: true, target: a);
                DrainCombat(engine);
                Check("Dimensionhole: negated by Magic Jammer — the monster stays and its zone stays usable",
                    tried && o.Graveyard.Contains(jammer) && p.Graveyard.Contains(hole) && p.MonsterZones[1].Occupant == a &&
                    p.MonsterZones[1].UnusableFor == null && p.TemporaryBanishments.Count == 0,
                    $"tried={tried} jammerGy={o.Graveyard.Contains(jammer)} occ={p.MonsterZones[1].Occupant?.Name}");
            }

            // No monster you control: it cannot be activated (the opponent's monsters are not legal targets).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var hole = PutInHand(engine, p, Dimensionhole);
                PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                Check("Dimensionhole: needs a monster you control (the opponent's are not targets)",
                    !engine.CanActivateSpellTrap(p, hole, true) && !engine.TryActivateSpellTrap(p, hole, true) &&
                    p.Hand.Contains(hole));
            }
        }

        // ───────────────────────── Light of Intervention ─────────────────────────

        /// <summary>Activate a Set Light of Intervention for <paramref name="who"/> (its activation resolves).</summary>
        static CardInstance ActivateLight(DuelEngine engine, DuelistState who, int zone, out bool ok)
        {
            var light = PlaceSetTrap(engine, who, LightOfIntervention, zone);
            ok = engine.CanActivateSpellTrap(who, light, false) && Activate(engine, who, light, fromHand: false) &&
                 light.FaceUp && who.TryFindSpellTrap(light, out _) &&
                 LocksAndZonesRuntime.MonstersCannotBeSetOrTurnedFaceDown(engine);
            return light;
        }

        static void LightOfInterventionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // A Set becomes a Normal Summon in face-up Defense Position (either player); without the card, a Set is a Set.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var light = ActivateLight(engine, p, 0, out var on);
                Check("Light of Intervention: a Set Continuous Trap activates and stays face-up applying",
                    on, $"faceUp={light.FaceUp} applies={LocksAndZonesRuntime.MonstersCannotBeSetOrTurnedFaceDown(engine)}");

                var celtic = PutInHand(engine, p, Celtic);
                var set = engine.TryNormalSummon(p, celtic, asSet: true);
                PassAll(engine);
                Check("Light of Intervention: your Set is a Normal Summon in face-up Defense Position",
                    set && p.TryFindMonster(celtic, out _) && celtic.FaceUp &&
                    celtic.Position == BattlePosition.Defense && celtic.SummonedThisTurn && !celtic.SetThisTurn &&
                    celtic.NormalOrFlipSummonedTurn == engine.TurnNumber && p.NormalSummonUsed,
                    $"set={set} faceUp={celtic.FaceUp} pos={celtic.Position} summoned={celtic.SummonedThisTurn}");

                // Opponent's turn: their Set is a Normal Summon too — your Trap Hole can answer it.
                var trapHole = PlaceSetTrap(engine, p, TrapHole, 1);
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                var oppMonster = PutInHand(engine, o, Celtic);
                var oppTurn = engine.TurnPlayer == o && engine.InMainPhase;
                var oppSet = oppTurn && engine.TryNormalSummon(o, oppMonster, asSet: true);
                var window = engine.PendingResponse?.Timing == ResponseTiming.MonsterSummoned &&
                             engine.PendingResponse.LegalCards.Contains(trapHole);
                Check("Light of Intervention: the opponent's Set is a Normal Summon in face-up Defense (Trap Hole may answer it)",
                    oppSet && oppMonster.FaceUp && oppMonster.Position == BattlePosition.Defense && window,
                    $"oppTurn={oppTurn} oppSet={oppSet} faceUp={oppMonster.FaceUp} window={window}");
                PassAll(engine);

                // The card leaves the field: Sets are Sets again.
                engine.SendCardToGrave(p, light);
                engine.NotifyPublic();
                var later = PutInHand(engine, o, SkullServant);
                o.NormalSummonUsed = false;
                var setAgain = engine.TryNormalSummon(o, later, asSet: true);
                PassAll(engine);
                Check("Light of Intervention: once it leaves the field, a Set is face-down again",
                    !LocksAndZonesRuntime.MonstersCannotBeSetOrTurnedFaceDown(engine) && setAgain && !later.FaceUp &&
                    later.Position == BattlePosition.Defense && later.SetThisTurn,
                    $"setAgain={setAgain} faceUp={later.FaceUp}");
            }

            // Nothing can be turned face-down: Book of Moon has no target, a self-Set effect cannot be activated,
            // Swords of Concealing Light leaves the monsters face-up, a face-down Special Summon comes face-up.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var oppCeltic = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var bird = PlaceMonster(engine, p, StealthBird, 1, BattlePosition.Attack, true);
                var moon = PutInHand(engine, p, BookOfMoon);
                var moonBefore = engine.CanActivateSpellTrap(p, moon, true);
                var birdBefore = engine.CanActivateSpellTrap(p, bird, false);
                ActivateLight(engine, p, 0, out var on);
                Check("Light of Intervention: Book of Moon has no legal target (control: legal without it)",
                    on && moonBefore && !engine.CanActivateSpellTrap(p, moon, true) &&
                    !engine.TryActivateSpellTrap(p, moon, true) && p.Hand.Contains(moon) && oppCeltic.FaceUp,
                    $"on={on} before={moonBefore}");
                Check("Light of Intervention: \"change this card to face-down\" cannot be activated (control: legal without it)",
                    birdBefore && !engine.CanActivateSpellTrap(p, bird, false) && bird.FaceUp,
                    $"before={birdBefore}");

                var swords = PutInHand(engine, p, SwordsOfConcealingLight);
                var swordsOk = engine.CanActivateSpellTrap(p, swords, true) && Activate(engine, p, swords, fromHand: true);
                Check("Light of Intervention: Swords of Concealing Light resolves but the opponent's monster stays face-up",
                    swordsOk && oppCeltic.FaceUp && oppCeltic.Position == BattlePosition.Attack,
                    $"swords={swordsOk} faceUp={oppCeltic.FaceUp} pos={oppCeltic.Position}");

                var reborn = engine.CreateCardInstance(GiantSoldier);
                var ss = engine.SpecialSummonToField(p, reborn, BattlePosition.Defense, faceUp: false);
                PassAll(engine);
                Check("Light of Intervention: a face-down Special Summon is made in face-up Defense Position",
                    ss && p.TryFindMonster(reborn, out _) && reborn.FaceUp && reborn.Position == BattlePosition.Defense,
                    $"ss={ss} faceUp={reborn.FaceUp} pos={reborn.Position}");
            }

            // Negated (Royal Decree): Sets are Sets.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var light = PlaceSetTrap(engine, p, LightOfIntervention, 0);
                light.FaceUp = true;
                PlaceSetTrap(engine, engine.Opponent, RoyalDecree, 3).FaceUp = true;
                engine.NotifyPublic();
                var celtic = PutInHand(engine, p, Celtic);
                var set = engine.TryNormalSummon(p, celtic, asSet: true);
                PassAll(engine);
                Check("Light of Intervention: negated by Royal Decree, a Set is face-down",
                    !LocksAndZonesRuntime.MonstersCannotBeSetOrTurnedFaceDown(engine) && set && !celtic.FaceUp,
                    $"set={set} faceUp={celtic.FaceUp}");
            }

            // The AI "Sets" its Flip monster: it is Normal Summoned face-up in Defense Position (no FLIP effect).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                o.Deck.Clear();
                for (var i = 0; i < 10; i++) o.Deck.Add(TrapHole);
                var mine = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                ActivateLight(engine, p, 0, out var on);
                var bug = PutInHand(engine, o, ManEaterBug);
                var reached = NextMainOf(engine, o);
                var done = reached && RunAiTurn(engine, 400);
                Check("Light of Intervention: the AI's Set Flip monster is Normal Summoned face-up Defense (no FLIP)",
                    on && done && o.TryFindMonster(bug, out _) && bug.FaceUp && bug.Position == BattlePosition.Defense &&
                    p.TryFindMonster(mine, out _),
                    $"on={on} reached={reached} done={done} bugOnField={o.TryFindMonster(bug, out _)} faceUp={bug.FaceUp} " +
                    $"pos={bug.Position} mine={p.TryFindMonster(mine, out _)}");
            }

            // Magical Hats under Light of Intervention (ygopro c81210420): the chosen monster goes to face-up Defense,
            // the hats come face-up, nothing is shuffled face-down.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                {
                    Check("Light of Intervention + Magical Hats: setup", false, $"phase={engine.Phase}");
                    return;
                }

                ClearBoard(engine);
                var p = engine.Player;
                var host = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                p.Deck.Insert(0, PotOfGreed);
                p.Deck.Insert(0, Mst);
                var light = PlaceSetTrap(engine, p, LightOfIntervention, 4);
                light.FaceUp = true;
                var hats = PlaceSetTrap(engine, p, MagicalHats, 0);
                engine.NotifyPublic();
                PassAll(engine);
                var act = engine.TryActivateSpellTrap(p, hats, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(host);
                if (engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                    engine.PendingActivation.LegalTargets.Count >= 2)
                {
                    var a = engine.PendingActivation.LegalTargets[0];
                    var b = engine.PendingActivation.LegalTargets[1];
                    engine.TrySelectEffectTarget(a);
                    engine.TrySelectEffectTarget(b);
                }

                PassAll(engine);
                var dummies = p.MonstersOnField().Where(m => m.MagicalHatDummy).ToList();
                Check("Light of Intervention + Magical Hats: the monster goes face-up Defense, the hats are face-up",
                    act && dummies.Count == 2 && dummies.All(m => m.FaceUp && m.Position == BattlePosition.Defense) &&
                    host.FaceUp && host.Position == BattlePosition.Defense && p.MonsterZones[2].Occupant == host,
                    $"act={act} dummies={dummies.Count} hostFaceUp={host.FaceUp} hostPos={host.Position}");
            }
        }

        // ───────────────────────── Cold Wave: Spell/Trap trigger effects (review fixes) ─────────────────────────

        const int InfiniteDismissal = 54109233;  // Continuous Trap, End Phase TRIGGER_F
        const int BurningLand = 24294108;        // Continuous Spell, Standby TRIGGER_F
        const int HeartOfTheUnderdog = 35762283; // Continuous Spell, Draw TRIGGER_O
        const int MessengerOfPeace = 44656491;   // Continuous Spell, Standby maintenance (EFFECT_TYPE_CONTINUOUS)
        const int SolemnWishes = 35346968;       // Continuous Trap, draw LP gain (EFFECT_TYPE_CONTINUOUS)
        const int RobbinGoblin = 88279736;       // Continuous Trap, battle damage TRIGGER_F
        const int BlackPendant = 65169794;       // Equip Spell, sent to the GY TRIGGER_F
        const int SpringOfRebirth = 94425169;    // Continuous Spell, returned to hand TRIGGER_F
        const int PreciousCards = 68304813;      // Continuous Spell, Tribute Summon TRIGGER_F
        const int HarpiesHuntingGround = 75782277; // Field Spell, Harpie Summon TRIGGER_F
        const int HarpieLady = 76812113;
        const int KarateMan = 23289281;          // ignition: ATK becomes double until the End Phase
        const int ChangeOfHeart = 4031928;

        /// <summary>
        /// Cold Wave locks the activated effects of Spell/Trap Cards too (ygopro c60682203: EFFECT_CANNOT_ACTIVATE,
        /// value re:IsSpellTrapEffect). Face-up trigger effects (TRIGGER_O / TRIGGER_F) do not happen while it lasts;
        /// continuous ones (maintenance costs, Solemn Wishes) still apply. Every case has a control without the lock.
        /// </summary>
        static void ColdWaveTriggerLock(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // Infinite Dismissal (End Phase trigger) during the turn Cold Wave resolved.
            foreach (var withColdWave in new[] { false, true })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var dismissal = PlaceSetTrap(engine, o, InfiniteDismissal, 2);
                dismissal.FaceUp = true;
                var cw = PutInHand(engine, p, ColdWave);
                var servant = PutInHand(engine, p, SkullServant);
                var reached = NextMainOf(engine, p);
                var locked = !withColdWave ||
                             (Activate(engine, p, cw, true) && LocksAndZonesRuntime.SpellTrapLockActive(engine));
                var summoned = engine.TryNormalSummon(p, servant, false);
                PassAll(engine);
                EndTurn(engine);
                var survived = p.TryFindMonster(servant, out _);
                Check($"Cold Wave: Infinite Dismissal's End Phase trigger {(withColdWave ? "cannot be activated under the lock" : "destroys the monster (control)")}",
                    reached && locked && summoned && survived == withColdWave,
                    $"reached={reached} locked={locked} summoned={summoned} survived={survived}");
            }

            // Burning Land (Standby trigger) during the opponent's turn under the lock.
            foreach (var withColdWave in new[] { false, true })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var land = PlaceSetTrap(engine, o, BurningLand, 2);
                land.FaceUp = true;
                var reached = NextMainOf(engine, p);
                var locked = !withColdWave ||
                             (Activate(engine, p, cw, true) && LocksAndZonesRuntime.SpellTrapLockActive(engine));
                var lpBefore = o.LifePoints;
                var turn = engine.TurnNumber;
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                var oppTurn = engine.TurnPlayer == o && engine.TurnNumber == turn + 1;
                var damaged = o.LifePoints < lpBefore;
                Check($"Cold Wave: Burning Land's Standby trigger {(withColdWave ? "cannot be activated during the opponent's locked turn" : "inflicts 500 (control)")}",
                    reached && locked && oppTurn && damaged == !withColdWave &&
                    LocksAndZonesRuntime.SpellTrapLockActive(engine) == withColdWave,
                    $"reached={reached} locked={locked} oppTurn={oppTurn} lp {lpBefore}->{o.LifePoints}");
            }

            // Heart of the Underdog (Draw Phase TRIGGER_O) during the opponent's locked turn.
            foreach (var withColdWave in new[] { false, true })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var heart = PlaceSetTrap(engine, o, HeartOfTheUnderdog, 1);
                heart.FaceUp = true;
                o.Deck.Clear();
                o.Deck.AddRange(new[] { Celtic, Kuriboh, Celtic, Kuriboh, Kuriboh, Kuriboh, Kuriboh, Kuriboh });
                var reached = NextMainOf(engine, p);
                var locked = !withColdWave ||
                             (Activate(engine, p, cw, true) && LocksAndZonesRuntime.SpellTrapLockActive(engine));
                o.Deck.Clear();
                o.Deck.AddRange(new[] { Celtic, Kuriboh, Kuriboh, Kuriboh });
                var before = o.Hand.Count;
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                var drawn = o.Hand.Count - before;
                Check($"Cold Wave: Heart of the Underdog {(withColdWave ? "cannot draw the extra card under the lock" : "draws 1 more for a Normal Monster (control)")}",
                    reached && locked && engine.TurnPlayer == o && drawn == (withColdWave ? 1 : 2),
                    $"reached={reached} locked={locked} drawn={drawn} tp={engine.TurnPlayer?.Name}");
            }

            // Continuous (not activated) effects still apply under the lock.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var messenger = PlaceSetTrap(engine, o, MessengerOfPeace, 1);
                messenger.FaceUp = true;
                var wishes = PlaceSetTrap(engine, o, SolemnWishes, 2);
                wishes.FaceUp = true;
                var reached = NextMainOf(engine, p);
                var locked = reached && Activate(engine, p, cw, true) && LocksAndZonesRuntime.SpellTrapLockActive(engine);
                var lpBefore = o.LifePoints;
                EndTurn(engine);
                if (engine.Phase == DuelPhase.Standby) engine.ContinueAfterStandby();
                PassAll(engine);
                // Opponent's turn: the normal draw gains 500 (Solemn Wishes), the Standby pays 100 (Messenger).
                Check("Cold Wave: continuous effects still apply under the lock (Solemn Wishes +500, Messenger of Peace upkeep -100)",
                    locked && engine.TurnPlayer == o && LocksAndZonesRuntime.SpellTrapLockActive(engine) &&
                    o.LifePoints == lpBefore + 500 - 100 && o.SpellTrapsOnField().Contains(messenger),
                    $"locked={locked} tp={engine.TurnPlayer?.Name} lp {lpBefore}->{o.LifePoints}");
            }

            // Battle damage trigger (Robbin' Goblin), GY trigger (Black Pendant), return-to-hand (Spring of Rebirth),
            // Tribute Summon (Precious Cards from Beyond) and Field Spell Summon trigger (Harpies' Hunting Ground).
            foreach (var withLock in new[] { false, true })
            {
                var tag = withLock ? "cannot be activated under the lock" : "control";

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    p.Hand.Clear();
                    o.Hand.Clear();
                    var goblin = PlaceSetTrap(engine, p, RobbinGoblin, 1);
                    goblin.FaceUp = true;
                    var attacker = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                    PutInHand(engine, o, Kuriboh);
                    PutInHand(engine, o, Celtic);
                    var reached = NextMainOf(engine, p);
                    if (withLock) LocksAndZonesRuntime.RegisterSpellTrapLock(engine, p, null);
                    var handBefore = o.Hand.Count;
                    var lpBefore = o.LifePoints;
                    var battle = reached && engine.TryEnterBattlePhase(p);
                    var attacked = battle && engine.TryAttack(p, attacker, null);
                    DrainCombat(engine);
                    Check($"Cold Wave: Robbin' Goblin's battle damage trigger ({tag})",
                        attacked && o.LifePoints < lpBefore && o.Hand.Count == handBefore - (withLock ? 0 : 1),
                        $"reached={reached} battle={battle} attacked={attacked} lp {lpBefore}->{o.LifePoints} hand {handBefore}->{o.Hand.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    var host = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                    p.Hand.Clear();
                    var pendant = PutInHand(engine, p, BlackPendant);
                    var reached = NextMainOf(engine, p);
                    var equipped = reached && Activate(engine, p, pendant, true, host) && pendant.EquippedTo == host;
                    if (withLock) LocksAndZonesRuntime.RegisterSpellTrapLock(engine, p, null);
                    var lpBefore = o.LifePoints;
                    engine.SendCardToGrave(p, pendant);
                    PassAll(engine);
                    Check($"Cold Wave: Black Pendant's sent-to-the-GY trigger ({tag})",
                        equipped && p.Graveyard.Contains(pendant) && o.LifePoints == lpBefore - (withLock ? 0 : 500),
                        $"reached={reached} equipped={equipped} gy={p.Graveyard.Contains(pendant)} lp {lpBefore}->{o.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var spring = PlaceSetTrap(engine, p, SpringOfRebirth, 1);
                    spring.FaceUp = true;
                    var precious = PlaceSetTrap(engine, p, PreciousCards, 2);
                    precious.FaceUp = true;
                    if (withLock) LocksAndZonesRuntime.RegisterSpellTrapLock(engine, p, null);
                    var lpBefore = p.LifePoints;
                    TextEffectRuntime.NotifyMonstersReturnedToOwnerHand(engine, 1);
                    var handBefore = p.Hand.Count;
                    var summoned = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                    TextEffectRuntime.NotifyTributeSummoned(engine, p, summoned, 2);
                    Check($"Cold Wave: Spring of Rebirth and Precious Cards from Beyond triggers ({tag})",
                        p.LifePoints == lpBefore + (withLock ? 0 : 500) && p.Hand.Count == handBefore + (withLock ? 0 : 2),
                        $"lp {lpBefore}->{p.LifePoints} hand {handBefore}->{p.Hand.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    p.Hand.Clear();
                    o.Hand.Clear();
                    var reached = NextMainOf(engine, p);
                    ClearBoard(engine);
                    var field = engine.CreateCardInstance(HarpiesHuntingGround);
                    field.FaceUp = true;
                    p.FieldSpellZone.Occupant = field;
                    var mirror = PlaceSetTrap(engine, o, MirrorForce, 3);
                    var harpie = PutInHand(engine, p, HarpieLady);
                    if (withLock) LocksAndZonesRuntime.RegisterSpellTrapLock(engine, p, null);
                    engine.NotifyPublic();
                    var summoned = reached && engine.TryNormalSummon(p, harpie, false);
                    PassAll(engine);
                    var prompted = engine.IsAwaitingEffectTarget;
                    if (prompted) engine.TrySelectEffectTarget(mirror);
                    PassAll(engine);
                    var destroyed = o.Graveyard.Contains(mirror);
                    Check($"Cold Wave: Harpies' Hunting Ground's Summon trigger ({tag})",
                        summoned && destroyed == !withLock && prompted == !withLock,
                        $"reached={reached} summoned={summoned} prompted={prompted} destroyed={destroyed}");
                }
            }
        }

        // ───────────────────────── other review fixes ─────────────────────────

        static void ReviewFixDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            // Cold Wave: an activation that changes nothing the state fingerprint sees (Karate Man's ignition only
            // doubles its ATK) is still phase activity (ygopro Duel.CheckPhaseActivity).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var karate = PlaceMonster(engine, p, KarateMan, 2, BattlePosition.Attack, true);
                var reached = NextMainOf(engine, p);
                var before = reached && engine.CanActivateSpellTrap(p, cw, true);
                var atk0 = karate.CurrentAtk;
                var used = before && engine.TryActivateSpellTrap(p, karate, false);
                PassAll(engine);
                var atk1 = karate.CurrentAtk;
                Check("Cold Wave: not legal after Karate Man's ignition resolved at the start of Main Phase 1",
                    before && used && atk1 != atk0 && !engine.CanActivateSpellTrap(p, cw, true),
                    $"reached={reached} before={before} used={used} atk {atk0}->{atk1}");
            }

            // Cold Wave: a target pick that is cancelled is not an activation — Cold Wave stays legal.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var cw = PutInHand(engine, p, ColdWave);
                var moon = PutInHand(engine, p, BookOfMoon);
                PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                PlaceMonster(engine, o, SkullServant, 3, BattlePosition.Attack, true);
                var reached = NextMainOf(engine, p);
                var started = reached && engine.TryActivateSpellTrap(p, moon, true);
                var awaiting = engine.IsAwaitingEffectTarget;
                var cancelled = awaiting && engine.CancelEffectTargeting();
                Check("Cold Wave: still legal after a cancelled activation at the start of Main Phase 1",
                    started && awaiting && cancelled && p.Hand.Contains(moon) && engine.CanActivateSpellTrap(p, cw, true),
                    $"reached={reached} started={started} awaiting={awaiting} cancelled={cancelled} moonInHand={p.Hand.Contains(moon)}");
            }

            // Magical Hats needs 2 USABLE Main Monster Zones: a Dimensionhole-locked zone does not count.
            foreach (var locked in new[] { false, true })
            {
                var engine = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                var victim = PlaceMonster(engine, p, SkullServant, 3, BattlePosition.Attack, true);
                if (locked)
                    LocksAndZonesRuntime.BanishUntilNextStandby(engine, p, engine.CreateCardInstance(Dimensionhole), victim);
                else
                    p.MonsterZones[3].Occupant = null;
                p.Deck.Insert(0, PotOfGreed);
                p.Deck.Insert(0, Mst);
                var hats = PlaceSetTrap(engine, p, MagicalHats, 0);
                engine.NotifyPublic();
                var usable = p.MonsterZones.Count(z => z.IsEmpty);
                var can = engine.CanActivateSpellTrap(p, hats, false);
                Check($"Magical Hats: {(locked ? "not activatable with 1 usable zone (the other is Dimensionhole-locked)" : "activatable with 2 usable zones (control)")}",
                    battle && usable == (locked ? 1 : 2) && can == !locked &&
                    (!locked || p.MonsterZones[3].UnusableFor == victim),
                    $"battle={battle} usable={usable} can={can}");
            }

            // Dimensionhole on a monster you control but do not own (Change of Heart): it returns to its owner's field.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var coh = PutInHand(engine, p, ChangeOfHeart);
                var hole = PutInHand(engine, p, Dimensionhole);
                var theirs = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var reached = NextMainOf(engine, p);
                var stole = reached && Activate(engine, p, coh, true, theirs) && p.TryFindMonster(theirs, out _);
                p.TryFindMonster(theirs, out var zone);
                var banished = stole && Activate(engine, p, hole, true, theirs) && o.Banished.Contains(theirs) &&
                               p.MonsterZones[zone].UnusableFor == theirs;
                var back = NextMainOf(engine, p);
                var onOwner = o.TryFindMonster(theirs, out _);
                var onMine = p.TryFindMonster(theirs, out _);
                Check("Dimensionhole: a monster you control but do not own returns to its owner's field (control ends)",
                    banished && back && onOwner && !onMine && theirs.ControlOwner == null && theirs.FaceUp &&
                    p.MonsterZones[zone].UnusableFor == null && p.MonsterZones[zone].IsEmpty,
                    $"stole={stole} banished={banished} back={back} onOwner={onOwner} onMine={onMine} " +
                    $"controlOwner={theirs.ControlOwner?.Name} zoneFree={p.MonsterZones[zone].IsEmpty}");
                var back2 = NextMainOf(engine, p);
                Check("Dimensionhole: the returned monster stays with its owner",
                    back2 && o.TryFindMonster(theirs, out _) && !p.TryFindMonster(theirs, out _),
                    $"back2={back2}");
            }
        }
    }
}
