using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Hardening review (2026-10-04) of the unreviewed SelfStatMonsters / PositionLocks work (2026-09-30 … 10-02).
    /// <list type="bullet">
    /// <item>The Rock Spirit, Soul of Purity and Light: the Battle Phase auras, and the hand procedure (the legacy
    /// "can only be Special Summoned by removing from play N ATTRIBUTE monster(s) in your Graveyard" clause that
    /// Fenrir already uses) offered to a human through the hand "Activate" path.</item>
    /// <item>Nuvia the Wicked: destroyed when Normal Summoned (not when Set); −200 ATK per monster the opponent
    /// controls, face-down included (ygopro c12953226 Duel.GetFieldGroupCount).</item>
    /// <item>Aqua Spirit / Garuda the Wind Spirit: the opponent's Standby / End Phase position change, asked of a
    /// human, auto-picked by the AI; Aqua Spirit's rest-of-turn lock.</item>
    /// <item>Spirit of Flames failed closed here (no procedure runtime); the EngineHygiene batch (v160) gave its banish
    /// procedure a runtime and restored its ATK atom, so it is now asserted Implemented. Curse of Fiend failed closed
    /// here too (its swap's FLIP effects of both players need one Chain); the SimultaneousTriggers batch (v160, SEGOC
    /// queue) implemented it again, and its check below now pins the compiled shape.</item>
    /// <item>Zombyra the Dark: since the BattleOutcomeTriggers batch (v162) its battle sentence compiles too (the -200 ATK
    /// trigger), so the card is Implemented and its direct-attack lock is live.</item>
    /// </list>
    /// </summary>
    public static class GrokSpiritsLocksHardeningRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int RockSpirit = 76305638;       // EARTH 1700/1000, +300 ATK in the opponent's Battle Phase
        const int SoulOfPurity = 77527210;     // LIGHT 2000/1800, opponent's monsters −300 ATK in their Battle Phase
        const int SpiritOfFlames = 13522325;
        const int Nuvia = 12953226;            // DARK 2000/800
        const int Zombyra = 88472456;
        const int AquaSpirit = 40916023;       // WATER 1600/1200
        const int Garuda = 12800777;           // WIND 1600/1200
        const int CurseOfFiend = 12470447;

        const int GiantSoldier = 13039848;     // EARTH Normal 1300/2000
        const int MysticalElf = 15025844;      // LIGHT Normal 800/2000
        const int BattleOx = 5053103;          // EARTH Normal 1700/1000
        const int CelticGuardian = 91152256;   // EARTH Normal 1400/1200
        const int TurtleTiger = 37313348;      // WATER Normal 1000/1500
        const int HarpieLady = 76812113;       // WIND Normal 1300/1400

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("GrokSpiritsLocks: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            SpiritProcedureForHuman(db, pDeck, aDeck, Check, RockSpirit, GiantSoldier, 1, "The Rock Spirit");
            SpiritProcedureForHuman(db, pDeck, aDeck, Check, SoulOfPurity, MysticalElf, 2, "Soul of Purity and Light");
            SpiritProcedureForHuman(db, pDeck, aDeck, Check, AquaSpirit, TurtleTiger, 1, "Aqua Spirit");
            SpiritProcedureForHuman(db, pDeck, aDeck, Check, Garuda, HarpieLady, 1, "Garuda the Wind Spirit");
            BattlePhaseAuras(db, pDeck, aDeck, Check);
            NuviaDuel(db, pDeck, aDeck, Check);
            AquaSpiritHuman(db, pDeck, aDeck, Check);
            AquaSpiritAi(db, pDeck, aDeck, Check);
            GarudaHuman(db, pDeck, aDeck, Check);
            FailedClosed(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            Shape(db, Check, RockSpirit, "The Rock Spirit: +300 ATK to itself in the opponent's Battle Phase; EARTH hand procedure",
                p => p.ClauseList.Count == 2 &&
                     p.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                              c.Action == EffectActionKind.ContinuousGainAtkDef && c.Amount == 300 &&
                                              c.DefAmount == 0 && c.AffectsThisCardOnly && c.RequiresBattlePhase &&
                                              c.OpponentTurnOnly && !c.ControllerTurnOnly) &&
                     IsProcedure(p, "EARTH", 1));
            Shape(db, Check, SoulOfPurity, "Soul of Purity and Light: opponent's monsters −300 ATK in their Battle Phase; 2 LIGHT procedure",
                p => p.ClauseList.Count == 2 &&
                     p.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                              c.Action == EffectActionKind.ContinuousGainAtkDef && c.Amount == -300 &&
                                              c.Side == EffectSide.Opponent && c.OpponentIsSubject &&
                                              !c.AffectsThisCardOnly && c.RequiresBattlePhase && c.OpponentTurnOnly) &&
                     IsProcedure(p, "LIGHT", 2));
            Shape(db, Check, Nuvia, "Nuvia the Wicked: −200 ATK per monster the opponent controls; destroyed when Normal Summoned",
                p => p.ClauseList.Count == 2 &&
                     p.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                              c.Action == EffectActionKind.GainSelfAtkPerCount && c.Amount == -200 &&
                                              c.CountSource == SelfStatMonstersRuntime.OppMonsterCount) &&
                     p.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardSummoned &&
                                              c.Action == EffectActionKind.DestroyThisCard &&
                                              c.RequiresThisNormalSummoned));
            Shape(db, Check, AquaSpirit, "Aqua Spirit: optional targeted change in the opponent's Standby Phase, locked; WATER procedure",
                p => p.ClauseList.Count == 2 &&
                     p.ClauseList.Exists(c => c.Timing == EffectTiming.StandbyPhase &&
                                              c.Action == EffectActionKind.ChangeOppFaceUpMonsterPositionAtPhase &&
                                              c.OpponentTurnOnly && c.IsOptional && c.RequiresTargetChoice &&
                                              c.LocksPositionRestOfTurn) &&
                     IsProcedure(p, "WATER", 1));
            Shape(db, Check, Garuda, "Garuda the Wind Spirit: optional targeted change in the opponent's End Phase, no lock; WIND procedure",
                p => p.ClauseList.Count == 2 &&
                     p.ClauseList.Exists(c => c.Timing == EffectTiming.EndPhase &&
                                              c.Action == EffectActionKind.ChangeOppFaceUpMonsterPositionAtPhase &&
                                              c.OpponentTurnOnly && c.IsOptional && c.RequiresTargetChoice &&
                                              !c.LocksPositionRestOfTurn) &&
                     IsProcedure(p, "WIND", 1));
        }

        // ChaosBanishSummons batch: the old-wording banish procedure is the card's own hand procedure (Lua
        // EFFECT_SPSUMMON_PROC: no Chain Link, cannot be negated), the Toons procedure clause EngineHygieneRuntime pays.
        static bool IsProcedure(CompiledCardProgram p, string attr, int count) =>
            p.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                     c.Action == EffectActionKind.SpecialSummonFromHandProcedure && !c.MakesChainLink &&
                                     c.Amount == 0 && c.BanishFromGyCount == count && !c.BanishFromGyUpTo &&
                                     string.Equals(c.AttributeFilter, attr, StringComparison.OrdinalIgnoreCase)) &&
            !p.ClauseList.Exists(c => c.Action == EffectActionKind.SpecialSummonThisFromHand);

        static void Shape(CardDatabase db, Chk Check, int id, string name, Func<CompiledCardProgram, bool> shape)
        {
            var def = db.Get(id);
            var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
            var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
            Check(name, prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && shape(prog),
                $"status={st} {Describe(prog)}");
        }

        // ───────────────────────────── hand procedure ─────────────────────────────

        /// <summary>
        /// A human Special Summons the Spirit from the hand by banishing <paramref name="count"/> monster(s) of its
        /// Attribute from the GY (hand "Activate"). It cannot be Normal Summoned or Set, and with too few such
        /// monsters (a monster of another Attribute does not count) it cannot be summoned.
        /// </summary>
        static void SpiritProcedureForHuman(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check, int spiritId,
            int fodderId, int count, string label)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var spirit = PutInHand(e, p, spiritId);
                for (var i = 0; i < count - 1; i++) p.Graveyard.Add(e.CreateCardInstance(fodderId));
                p.Graveyard.Add(e.CreateCardInstance(fodderId == GiantSoldier ? MysticalElf : GiantSoldier));
                var ns = e.CanNormalSummonOrSet(p, spirit);
                var can = e.CanSpecialSummonProcedure(p, spirit) || e.CanActivateSpellTrap(p, spirit, fromHand: true);
                Check($"{label}: cannot be Normal Summoned/Set; only {count - 1} of its Attribute in the GY (+1 other): no Special Summon",
                    !ns && !can && p.Hand.Contains(spirit),
                    $"ns={ns} can={can} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var spirit = PutInHand(e, p, spiritId);
                var fodder = new List<CardInstance>();
                for (var i = 0; i < count; i++)
                {
                    var f = e.CreateCardInstance(fodderId);
                    p.Graveyard.Add(f);
                    fodder.Add(f);
                }

                var other = e.CreateCardInstance(fodderId == GiantSoldier ? MysticalElf : GiantSoldier);
                p.Graveyard.Add(other);
                var can = e.CanSpecialSummonProcedure(p, spirit);
                var act = can && e.TrySpecialSummonProcedure(p, spirit);
                for (var i = 0; i < 6; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var pick = e.PendingActivation.LegalTargets.FirstOrDefault(t => fodder.Contains(t) && p.Graveyard.Contains(t));
                        if (pick == null || !e.TrySelectEffectTarget(pick)) break;
                    }
                    else if (e.IsAwaitingResponse) e.PassResponse();
                    else break;
                }

                var onField = p.TryFindMonster(spirit, out _) && spirit.FaceUp;
                var banished = fodder.All(f => p.Banished.Contains(f) && !p.Graveyard.Contains(f));
                Check($"{label}: a human Special Summons it from the hand by banishing {count} monster(s) of its Attribute",
                    can && act && onField && banished && p.Graveyard.Contains(other) && !p.Hand.Contains(spirit),
                    $"can={can} act={act} onField={onField} banished={banished} gy={Names(p.Graveyard)} " +
                    $"ban={Names(p.Banished)} {State(e)}");
            }
        }

        // ───────────────────────────── Battle Phase auras ─────────────────────────────

        static void BattlePhaseAuras(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var rock = PlaceMonster(e, p, RockSpirit, 0, BattlePosition.Defense, true);
            var soul = PlaceMonster(e, p, SoulOfPurity, 1, BattlePosition.Defense, true);
            var mine = PlaceMonster(e, p, CelticGuardian, 2, BattlePosition.Defense, true);
            var foe = PlaceMonster(e, o, BattleOx, 0, BattlePosition.Attack, true);
            FieldSpellEffects.RefreshBoard(e);
            var own = $"rock={rock.CurrentAtk} mine={mine.CurrentAtk} foe={foe.CurrentAtk} phase={e.Phase} turn={e.TurnPlayer?.Name}";
            var ownOk = rock.CurrentAtk == 1700 && mine.CurrentAtk == 1400 && foe.CurrentAtk == 1700;

            var main = ReachOpponentMain(e);
            FieldSpellEffects.RefreshBoard(e);
            var oppMain = $"rock={rock.CurrentAtk} foe={foe.CurrentAtk} phase={e.Phase}";
            var oppMainOk = main && rock.CurrentAtk == 1700 && foe.CurrentAtk == 1700;

            var bp = e.TryEnterBattlePhase(o);
            if (e.IsAwaitingResponse) e.PassResponse();
            FieldSpellEffects.RefreshBoard(e);
            var oppBp = $"rock={rock.CurrentAtk} soul={soul.CurrentAtk} mine={mine.CurrentAtk} foe={foe.CurrentAtk} phase={e.Phase}";
            var oppBpOk = bp && e.Phase == DuelPhase.Battle && rock.CurrentAtk == 2000 && soul.CurrentAtk == 2000 &&
                          mine.CurrentAtk == 1400 && foe.CurrentAtk == 1400;
            Check("The Rock Spirit +300 / Soul of Purity and Light −300 apply only in the opponent's Battle Phase",
                ownOk && oppMainOk && oppBpOk, $"own[{own}] oppMain[{oppMain}] oppBp[{oppBp}]");
        }

        // ───────────────────────────── Nuvia the Wicked ─────────────────────────────

        static void NuviaDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var nuvia = PutInHand(e, p, Nuvia);
                var ok = e.TryNormalSummon(p, nuvia, asSet: false);
                for (var i = 0; i < 6 && (e.IsAwaitingResponse || e.Chain.HasLinks); i++)
                    if (e.IsAwaitingResponse) e.PassResponse(); else break;
                Check("Nuvia the Wicked: Normal Summoned → destroyed",
                    ok && !p.TryFindMonster(nuvia, out _) && p.Graveyard.Contains(nuvia),
                    $"ok={ok} gy={Names(p.Graveyard)} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var nuvia = PutInHand(e, p, Nuvia);
                var ok = e.TryNormalSummon(p, nuvia, asSet: true);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Nuvia the Wicked: Set is not a Normal Summon → stays",
                    ok && p.TryFindMonster(nuvia, out _) && !nuvia.FaceUp, $"ok={ok} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var nuvia = PlaceMonster(e, p, Nuvia, 0, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                var none = nuvia.CurrentAtk;
                PlaceMonster(e, o, BattleOx, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, CelticGuardian, 1, BattlePosition.Defense, false);
                FieldSpellEffects.RefreshBoard(e);
                var two = nuvia.CurrentAtk;
                PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                var ownAdded = nuvia.CurrentAtk;
                Check("Nuvia the Wicked: −200 ATK per monster the opponent controls (face-down counts; own monsters do not)",
                    none == 2000 && two == 1600 && ownAdded == 1600, $"none={none} two={two} ownAdded={ownAdded}");
            }
        }

        // ───────────────────────────── Aqua Spirit / Garuda ─────────────────────────────

        /// <summary>
        /// The human's Aqua Spirit in the AI's Standby Phase: asked before the AI acts; the chosen monster goes to
        /// Defense Position and cannot change its position by hand for the rest of that turn.
        /// </summary>
        static void AquaSpiritHuman(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            PlaceMonster(e, p, AquaSpirit, 0, BattlePosition.Attack, true);
            var ox = PlaceMonster(e, o, BattleOx, 0, BattlePosition.Attack, true);
            var setFoe = PlaceMonster(e, o, CelticGuardian, 1, BattlePosition.Defense, false);
            e.TryEndTurnSafe(p);
            // The AI's turn reaches Main Phase 1 with the pick open; SimpleAi.RunTurn waits on IsAwaitingEffectTarget
            // before its first Main Phase action, so the change still lands before the AI does anything.
            var asked = e.TurnPlayer == o && (e.Phase == DuelPhase.Standby || e.Phase == DuelPhase.Main1) &&
                        e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Count == 1 &&
                        e.PendingActivation.LegalTargets.Contains(ox) && !e.PendingActivation.LegalTargets.Contains(setFoe);
            var askedState = State(e);
            var picked = asked && e.TrySelectEffectTarget(ox);
            if (e.IsAwaitingResponse) e.PassResponse();
            var changed = ox.Position == BattlePosition.Defense && ox.FaceUp;
            var main = e.TurnPlayer == o && e.InMainPhase;
            var locked = !e.CanChangePosition(o, ox);
            Check("Aqua Spirit (human): asked at the start of the AI's turn (face-up targets only); the pick goes to Defense and stays locked; play goes on",
                asked && picked && changed && main && locked,
                $"asked[{askedState}] picked={picked} ox={ox.Position}/{(ox.FaceUp ? "up" : "down")} locked={locked} {State(e)}");
        }

        static void AquaSpiritAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            PlaceMonster(e, o, AquaSpirit, 0, BattlePosition.Attack, true);
            var ox = PlaceMonster(e, p, BattleOx, 0, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
            e.TryEndTurnSafe(o);
            if (e.IsAwaitingResponse) e.PassResponse();
            var mine = e.TurnPlayer == p;
            var changed = ox.Position == BattlePosition.Defense && elf.Position == BattlePosition.Attack;
            var locked = !e.CanChangePosition(p, ox);
            var elfFree = e.InMainPhase && e.CanChangePosition(p, elf);
            Check("Aqua Spirit (AI): in the player's Standby Phase it turns the strongest face-up Attack Position monster to Defense, locked",
                reached && mine && changed && locked && elfFree,
                $"reached={reached} ox={ox.Position} elf={elf.Position} locked={locked} elfFree={elfFree} {State(e)}");
        }

        /// <summary>The human's Garuda in the AI's End Phase: asked; the pick changes position; no lock; the turn ends.</summary>
        static void GarudaHuman(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            PlaceMonster(e, p, Garuda, 0, BattlePosition.Attack, true);
            var ox = PlaceMonster(e, o, BattleOx, 0, BattlePosition.Defense, true);
            e.TryEndTurnSafe(o);
            var asked = e.TurnPlayer == o && e.Phase == DuelPhase.End && e.IsAwaitingEffectTarget &&
                        e.PendingActivation.LegalTargets.Contains(ox);
            var askedState = State(e);
            var picked = asked && e.TrySelectEffectTarget(ox);
            if (e.IsAwaitingResponse) e.PassResponse();
            Check("Garuda the Wind Spirit (human): asked in the AI's End Phase; the pick changes position; the turn then passes",
                reached && asked && picked && ox.Position == BattlePosition.Attack && ox.FaceUp && e.TurnPlayer == p,
                $"reached={reached} asked[{askedState}] picked={picked} ox={ox.Position} {State(e)}");
        }

        // ───────────────────────────── failed closed / left as is ─────────────────────────────

        static void FailedClosed(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Implemented again by the SimultaneousTriggers batch (v160): the FLIP effects it flips go on one Chain.
                var def = db.Get(CurseOfFiend);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check("Curse of Fiend: Implemented again (SimultaneousTriggers v160, SEGOC FLIP Chain)",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Exists(c => c != null &&
                                                c.Action == EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn),
                    $"status={st} {Describe(prog)}");
            }

            {
                // Spirit of Flames: its banish procedure got a runtime in the EngineHygiene batch (v160), with the ATK atom.
                var def = db.Get(SpiritOfFlames);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check("Spirit of Flames: Implemented — banish-1-FIRE hand procedure + 300 ATK in your Battle Phase (EngineHygiene)",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
                        c.BanishFromGyCount == 1 && c.AttributeFilter == "FIRE" && c.Amount == 0) &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                        c.Amount == 300 && c.AffectsThisCardOnly && c.RequiresBattlePhase && c.ControllerTurnOnly),
                    $"status={st} {Describe(prog)}");
            }

            {
                var def = db.Get(Zombyra);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Implemented;
                // BattleOutcomeTriggers batch (v162): the battle sentence compiles (ThisCardDestroysByBattle, -200 ATK lasting).
                Check("Zombyra the Dark: Implemented — direct-attack lock + 'loses 200 ATK' when it destroys a monster by battle",
                    def != null && prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 2 &&
                    prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.CannotAttackDirectly) &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ThisCardDestroysByBattle &&
                        c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp && c.Amount == -200),
                    $"status={st} {Describe(prog)}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Amount}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
