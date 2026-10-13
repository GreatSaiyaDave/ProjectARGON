using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// A Standby-growth record (StandbyGrowthAndMaintenance batch, v142), kept on the card it belongs to
    /// (<see cref="CardInstance.StandbyGrowth"/>). It is valid only for the field presence it was recorded
    /// for: the card's <see cref="CardInstance.FieldEntryStamp"/> and <see cref="CardInstance.FaceDownStamp"/>
    /// must not have changed since.
    /// </summary>
    public sealed class StandbyGrowthGrant
    {
        public int EntryStamp;
        public int FaceDownStamp;
        /// <summary>Turn number the record started in (Castle: the FLIP resolved then).</summary>
        public int StartTurn;
        /// <summary>Standby Phases of the controller counted so far.</summary>
        public int Steps;
        /// <summary>Turn number of the last counted Standby Phase (one per turn).</summary>
        public int LastStepTurn = -1;
        /// <summary>
        /// Castle of Dark Illusions: every monster on the field when the FLIP resolved, with its
        /// <see cref="CardInstance.FieldEntryStamp"/> then ("those Zombie-Type monsters"; ygopro GetFieldID).
        /// Null for a self-growth record (Pumpking).
        /// </summary>
        public List<(CardInstance card, int entryStamp)> Targets;
    }

    /// <summary>Mirror Wall: one opponent's monster that attacked while the wall was face-up (one field presence).</summary>
    public sealed class AttackerHalvingMark
    {
        public CardInstance Monster;
        public int EntryStamp;
        public int FaceDownStamp;
    }

    /// <summary>
    /// Card batch "StandbyGrowthAndMaintenance" (v142) runtime:
    /// <list type="bullet">
    /// <item>Castle of Dark Illusions (<see cref="EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp"/>):
    /// the FLIP records the monsters on the field; each board refresh gives the still-present face-up Zombies among
    /// them Amount + StandbyGrowthAmount × counted Standby Phases; the controller's next StandbyGrowthSteps Standby
    /// Phases each count one. It all ends when the Castle leaves the field, is turned face-down or is negated.</item>
    /// <item>Pumpking the King of Ghosts (<see cref="EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby"/>):
    /// while a face-up "Castle of Dark Illusions" is on the field, Amount + StandbyGrowthAmount × its counted
    /// Standby Phases (at most StandbyGrowthSteps); with no Castle the gain disappears and the count restarts.</item>
    /// <item>Mirror Wall (<see cref="EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField"/>): marks the
    /// opponent's attackers (at attack declaration while face-up, and the current attacker when its activation
    /// resolves); each refresh halves the final ATK of every marked monster the opponent still controls.</item>
    /// <item>Skull Invitation (<see cref="EffectActionKind.InflictDamageToOwnerPerCardSentToGy"/>): compares the
    /// Graveyards at each checkpoint (every board refresh, before each Chain Link resolves, and before each clause
    /// applies) with the last checkpoint; every card (never a Token) that arrived while the Skull Invitation was
    /// active costs its owner Amount. The AI chains a Set copy to the opponent's one-shot Spell/Trap.</item>
    /// <item>Optional upkeep ("pay N LP or destroy this card", <see cref="EffectClause.UpkeepIsOptional"/>): a human
    /// is asked in their Standby Phase, which waits for the answer (<see cref="OfferUpkeepChoice"/>); the AI pays
    /// only while it keeps more than N LP.</item>
    /// <item>Chain loop guard (<see cref="ContinuousActivationLeftField"/>): a Continuous Spell/Trap whose own
    /// activation link resolves after the card left the field (an MST chained to Skull Invitation / Mirror Wall)
    /// resolves with no effect and is not re-seated from the GY; a "When this card is activated:" effect (Burning
    /// Land) still resolves, and the card stays in the GY (<see cref="SettleOffFieldActivation"/>).</item>
    /// </list>
    /// </summary>
    public static class StandbyGrowthAndMaintenanceRuntime
    {
        // ─────────────────────────────── per-engine state ───────────────────────────────

        sealed class SkullRegistration
        {
            public CardInstance Card;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        sealed class EngineState
        {
            /// <summary>Monsters whose <see cref="CardInstance.AtkHalvings"/> this runtime set (reset on each refresh).</summary>
            public readonly HashSet<CardInstance> Halved = new();

            /// <summary>Skull Invitation: every card in either GY at the last checkpoint.</summary>
            public HashSet<CardInstance> SeenInGy = new();
            public bool SeenValid;
            public readonly List<SkullRegistration> Skulls = new();
            public bool CheckpointBusy;

            /// <summary>
            /// One-shot Spell/Trap cards of Chain Links that already resolved while the chain is still resolving.
            /// By the rules they reach the GY only once the whole chain has resolved, so a checkpoint during the
            /// chain treats them as not sent yet (the engine may move them earlier).
            /// </summary>
            public readonly HashSet<CardInstance> HeldUntilChainEnds = new();

            /// <summary>
            /// A Continuous card whose "When this card is activated:" link is resolving although the card already
            /// left the field (<see cref="ContinuousActivationLeftField"/>); see <see cref="SettleOffFieldActivation"/>.
            /// </summary>
            public CardInstance OffFieldActivation;
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> States = new();

        static EngineState State(DuelEngine engine) => States.GetValue(engine, _ => new EngineState());

        // ─────────────────────────────── helpers ───────────────────────────────

        static bool OnFieldAsMonster(DuelEngine engine, CardInstance c, out DuelistState controller)
        {
            controller = null;
            if (engine == null || c == null) return false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who != null && who.TryFindMonster(c, out _))
                {
                    controller = who;
                    return true;
                }
            }

            return false;
        }

        static bool InSpellTrapZone(DuelEngine engine, CardInstance c, out DuelistState controller)
        {
            controller = null;
            if (engine == null || c == null) return false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who != null && who.TryFindSpellTrap(c, out _))
                {
                    controller = who;
                    return true;
                }
            }

            return false;
        }

        static EffectClause FirstClause(CardInstance card, EffectTiming timing, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Timing == timing && c.Action == action) return c;
            return null;
        }

        static bool SamePresence(CardInstance card, StandbyGrowthGrant g) =>
            card != null && g != null && card.FieldEntryStamp == g.EntryStamp && card.FaceDownStamp == g.FaceDownStamp;

        // The monster's current Type (Lua IsRace): DNA Surgery's declared Type counts (v142 cross-group audit).
        static bool RaceMatches(CardInstance c, string race) =>
            string.IsNullOrEmpty(race) || (c?.Def != null && ClassicEraTemplates.RaceMatches(c.Race, race));

        // ─────────────────────────────── Castle of Dark Illusions ───────────────────────────────

        /// <summary>
        /// FLIP resolution (ApplyClause arm): only if this card is still face-up on the field (Lua
        /// <c>c:IsRelateToEffect(e) and c:IsFaceup()</c>). Records every monster on the field now (both sides,
        /// face-down included: the Type check is continuous, so a Zombie flipped face-up later gains too).
        /// A later FLIP of the same copy starts a new record.
        /// </summary>
        public static void ApplyRaceGrowthFlip(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || source == null || clause == null) return;
            if (!source.FaceUp || !OnFieldAsMonster(engine, source, out _))
            {
                engine.Log($"{source.Name}: no longer face-up on the field — no effect.");
                return;
            }

            var targets = new List<(CardInstance, int)>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m != null) targets.Add((m, m.FieldEntryStamp));
            }

            source.StandbyGrowth = new StandbyGrowthGrant
            {
                EntryStamp = source.FieldEntryStamp,
                FaceDownStamp = source.FaceDownStamp,
                StartTurn = engine.TurnNumber,
                Steps = 0,
                Targets = targets
            };
            engine.Log($"{source.Name}: all {clause.RaceFilter} monsters on the field gain {clause.Amount} ATK and DEF.");
        }

        /// <summary>The Castle's record, or null once it has ended (and it is dropped for good).</summary>
        static StandbyGrowthGrant LiveRaceGrant(DuelEngine engine, CardInstance castle, out EffectClause clause)
        {
            clause = null;
            var g = castle?.StandbyGrowth;
            if (g?.Targets == null) return null;
            clause = FirstClause(castle, EffectTiming.Flip,
                EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp);
            if (clause == null || !castle.FaceUp || castle.IsNegated || !SamePresence(castle, g) ||
                !OnFieldAsMonster(engine, castle, out _))
            {
                // RESETS_STANDARD_DISABLE: leaving the field, turning face-down or being negated ends it for good.
                castle.StandbyGrowth = null;
                return null;
            }

            return g;
        }

        static void ApplyRaceGrowth(DuelEngine engine, CardInstance castle)
        {
            var g = LiveRaceGrant(engine, castle, out var clause);
            if (g == null) return;
            var atk = clause.Amount + clause.StandbyGrowthAmount * g.Steps;
            var def = clause.DefAmount + clause.StandbyGrowthAmount * g.Steps;
            foreach (var (m, stamp) in g.Targets)
            {
                if (m?.Def == null || !m.Def.IsMonster || !m.FaceUp || m.FieldEntryStamp != stamp) continue;
                if (!OnFieldAsMonster(engine, m, out _)) continue;
                if (!RaceMatches(m, clause.RaceFilter)) continue;
                m.AtkModifier += atk;
                m.DefModifier += def;
            }
        }

        // ─────────────────────────────── Pumpking ───────────────────────────────

        static void ApplyNamedSelfGrowth(DuelEngine engine, CardInstance self)
        {
            var clause = FirstClause(self, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby);
            if (clause == null) return;
            if (!self.FaceUp || self.IsNegated || engine.IsSummonPending(self) ||
                !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, clause.NamedCard))
            {
                // "If "Castle of Dark Illusions" is not on the field, this ATK/DEF gain disappears and this
                // effect resets"; a face-down or negated copy loses it too (RESETS_STANDARD_DISABLE).
                self.StandbyGrowth = null;
                return;
            }

            var steps = SamePresence(self, self.StandbyGrowth) ? self.StandbyGrowth.Steps : 0;
            self.AtkModifier += clause.Amount + clause.StandbyGrowthAmount * steps;
            self.DefModifier += clause.DefAmount + clause.StandbyGrowthAmount * steps;
        }

        // ─────────────────────────────── Standby Phase ───────────────────────────────

        /// <summary>
        /// Start of <paramref name="turnPlayer"/>'s Standby Phase (TextEffectRuntime.FirePhaseTriggers): each
        /// Castle of Dark Illusions they control whose FLIP record is live counts this Standby Phase (only Standby
        /// Phases after the FLIP, at most StandbyGrowthSteps); each face-up Pumpking they control counts it while a
        /// face-up "Castle of Dark Illusions" is on the field (at most StandbyGrowthSteps, once per turn).
        /// </summary>
        public static void TickStandby(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null) return;
            foreach (var m in turnPlayer.MonstersOnField().ToList())
            {
                if (m?.Def == null) continue;

                var g = LiveRaceGrant(engine, m, out var castleClause);
                if (g != null && engine.TurnNumber > g.StartTurn && g.LastStepTurn != engine.TurnNumber &&
                    g.Steps < castleClause.StandbyGrowthSteps)
                {
                    g.Steps++;
                    g.LastStepTurn = engine.TurnNumber;
                    engine.Log($"{m.Name}: those {castleClause.RaceFilter} monsters gain " +
                               $"{castleClause.StandbyGrowthAmount} more ATK and DEF " +
                               $"({g.Steps} of {castleClause.StandbyGrowthSteps} Standby Phases).");
                }

                var self = FirstClause(m, EffectTiming.ContinuousWhileFaceUp,
                    EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby);
                if (self == null) continue;
                if (!m.FaceUp || m.IsNegated || !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, self.NamedCard))
                {
                    m.StandbyGrowth = null;
                    continue;
                }

                if (!SamePresence(m, m.StandbyGrowth))
                    m.StandbyGrowth = new StandbyGrowthGrant
                    {
                        EntryStamp = m.FieldEntryStamp,
                        FaceDownStamp = m.FaceDownStamp,
                        StartTurn = engine.TurnNumber
                    };
                var own = m.StandbyGrowth;
                if (own.LastStepTurn == engine.TurnNumber || own.Steps >= self.StandbyGrowthSteps) continue;
                own.Steps++;
                own.LastStepTurn = engine.TurnNumber;
                engine.Log($"{m.Name}: gains {self.StandbyGrowthAmount} ATK/DEF " +
                           $"({own.Steps} of {self.StandbyGrowthSteps} Standby Phases).");
            }
        }

        // ─────────────────────────────── Mirror Wall ───────────────────────────────

        static bool HasMirrorWallContinuous(CardInstance st) =>
            FirstClause(st, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField) != null;

        static bool AddMark(DuelEngine engine, CardInstance wall, CardInstance monster)
        {
            wall.AttackerHalvingMarks ??= new List<AttackerHalvingMark>();
            foreach (var mk in wall.AttackerHalvingMarks)
            {
                if (mk != null && ReferenceEquals(mk.Monster, monster) && mk.EntryStamp == monster.FieldEntryStamp &&
                    mk.FaceDownStamp == monster.FaceDownStamp)
                    return false;
            }

            wall.AttackerHalvingMarks.Add(new AttackerHalvingMark
            {
                Monster = monster,
                EntryStamp = monster.FieldEntryStamp,
                FaceDownStamp = monster.FaceDownStamp
            });
            // Applies at once: the battle this monster declared may be calculated before the next refresh.
            monster.AtkHalvings++;
            State(engine).Halved.Add(monster);
            engine.Log($"{wall.Name}: {monster.Name}'s ATK is halved while {wall.Name} remains on the field.");
            return true;
        }

        static bool MarkLive(DuelEngine engine, AttackerHalvingMark mk) =>
            mk?.Monster != null && mk.Monster.FaceUp && mk.Monster.FieldEntryStamp == mk.EntryStamp &&
            mk.Monster.FaceDownStamp == mk.FaceDownStamp && OnFieldAsMonster(engine, mk.Monster, out _);

        /// <summary>
        /// Mirror Wall's activation resolves (Lua e1: the target function clears the group, the operation adds
        /// the monster attacking right now if the opponent controls it face-up on the field).
        /// </summary>
        public static void ResolveMirrorWallActivation(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null) return;
            source.AttackerHalvingMarks = new List<AttackerHalvingMark>();
            var attacker = engine.DeclaredAttacker;
            if (attacker == null || !attacker.FaceUp) return;
            if (!OnFieldAsMonster(engine, attacker, out var atkController) ||
                atkController != engine.OpponentOf(who))
                return;
            if (!source.FaceUp || !InSpellTrapZone(engine, source, out _)) return;
            AddMark(engine, source, attacker);
        }

        /// <summary>
        /// An attack was declared (TextEffectRuntime.FireAttackDeclared): every face-up, not negated Mirror Wall
        /// of the attacker's opponent adds the attacker (Lua e2, EVENT_ATTACK_ANNOUNCE).
        /// </summary>
        public static void NotifyAttackDeclared(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker?.Def == null || !attacker.FaceUp) return;
            if (!OnFieldAsMonster(engine, attacker, out var atkController)) return;
            var walls = engine.OpponentOf(atkController);
            if (walls == null) return;
            foreach (var st in walls.SpellTrapsOnField().ToList())
            {
                if (st == null || !st.FaceUp || st.IsNegated || !HasMirrorWallContinuous(st)) continue;
                AddMark(engine, st, attacker);
            }
        }

        static void ApplyMirrorWalls(DuelEngine engine, EngineState state)
        {
            foreach (var c in state.Halved)
                if (c != null) c.AtkHalvings = 0;
            state.Halved.Clear();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m != null) m.AtkHalvings = 0;
            }

            foreach (var wallSide in new[] { engine.Player, engine.Opponent })
            {
                if (wallSide == null) continue;
                var opp = engine.OpponentOf(wallSide);
                foreach (var st in wallSide.SpellTrapsOnField())
                {
                    if (st?.AttackerHalvingMarks == null || st.AttackerHalvingMarks.Count == 0) continue;
                    // A mark ends for good once its monster leaves the field or is turned face-down.
                    st.AttackerHalvingMarks.RemoveAll(mk => !MarkLive(engine, mk));
                    if (!st.FaceUp || st.IsNegated || !HasMirrorWallContinuous(st)) continue;
                    foreach (var mk in st.AttackerHalvingMarks)
                    {
                        // EFFECT_SET_ATTACK_FINAL with target range (0, LOCATION_MZONE): the opponent's side only.
                        if (opp == null || !opp.TryFindMonster(mk.Monster, out _)) continue;
                        mk.Monster.AtkHalvings++;
                        state.Halved.Add(mk.Monster);
                    }
                }
            }
        }

        /// <summary>
        /// AI (AiAutoRespond, attack declaration): activate a Set Mirror Wall against an attacker worth halving,
        /// when the AI can still afford its upkeep.
        /// </summary>
        public static bool AiWantsAttackResponse(DuelEngine engine, DuelistState who, CardInstance card,
            CardInstance attacker)
        {
            if (engine == null || who == null || card == null || attacker == null) return false;
            if (FirstClause(card, EffectTiming.Activate, EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField) ==
                null)
                return false;
            var upkeep = 0;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == EffectActionKind.PayLpOrDestroyThis)
                    upkeep = System.Math.Max(upkeep, c.PayLpAmount);
            return attacker.CurrentAtk >= 1500 && who.LifePoints > upkeep;
        }

        // ─────────────────────────────── optional upkeep choice ───────────────────────────────

        /// <summary>What happens to an optional "pay N LP or destroy this card" upkeep with LP to spare.</summary>
        public enum UpkeepDecision
        {
            Pay,
            Decline,
            /// <summary>Queued for a human controller; <see cref="OfferUpkeepChoice"/> asks them.</summary>
            Asked
        }

        sealed class QueuedUpkeep
        {
            public DuelistState Who;
            public CardInstance Card;
            public int Amount;
            public int EntryStamp;
            public int FaceDownStamp;
            public int Turn;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<QueuedUpkeep>> UpkeepQueues = new();

        static List<QueuedUpkeep> Queue(DuelEngine engine) => UpkeepQueues.GetValue(engine, _ => new List<QueuedUpkeep>());

        /// <summary>
        /// TextEffectRuntime.ResolvePayLpOrDestroyThis, an optional upkeep (Mirror Wall, Messenger of Peace, Spirit's
        /// Invitation, Mask of Brutality) whose controller has more than <paramref name="amount"/> LP (Lua
        /// <c>CheckLPCost</c> + <c>SelectYesNo</c>):
        /// <list type="bullet">
        /// <item>a human in their own live Standby Phase is asked — the choice is queued and
        /// DuelEngine.BeginTurn holds the Standby Phase while <see cref="OfferUpkeepChoice"/> asks it;</item>
        /// <item>a human outside a live Standby Phase (a direct FirePhaseTriggers call: no phase to hold) pays, as
        /// before;</item>
        /// <item>the AI pays only while it keeps more than <paramref name="amount"/> LP afterwards
        /// (<see cref="AiPaysOptionalUpkeep"/>).</item>
        /// </list>
        /// </summary>
        public static UpkeepDecision OptionalUpkeepChoice(DuelEngine engine, DuelistState who, CardInstance card,
            int amount)
        {
            if (engine == null || who == null || card == null) return UpkeepDecision.Pay;
            if (!engine.IsHumanControlled(who))
                return AiPaysOptionalUpkeep(who, amount) ? UpkeepDecision.Pay : UpkeepDecision.Decline;
            if (engine.Phase != DuelPhase.Standby || engine.TurnPlayer != who || engine.GameOver)
                return UpkeepDecision.Pay;
            var queue = Queue(engine);
            queue.RemoveAll(q => q.Turn != engine.TurnNumber);
            if (!queue.Exists(q => ReferenceEquals(q.Card, card)))
                queue.Add(new QueuedUpkeep
                {
                    Who = who,
                    Card = card,
                    Amount = amount,
                    EntryStamp = card.FieldEntryStamp,
                    FaceDownStamp = card.FaceDownStamp,
                    Turn = engine.TurnNumber
                });
            engine.Log($"{card.Name}: pay {amount} LP or destroy this card — {who.Name} chooses.");
            return UpkeepDecision.Asked;
        }

        /// <summary>AI: pay an optional upkeep of <paramref name="amount"/> only while more than that is left after.</summary>
        public static bool AiPaysOptionalUpkeep(DuelistState who, int amount) =>
            who != null && who.LifePoints - amount > amount;

        static bool StillOnField(QueuedUpkeep q) =>
            q?.Who != null && q.Card != null && q.Card.FaceUp && q.Card.FieldEntryStamp == q.EntryStamp &&
            q.Card.FaceDownStamp == q.FaceDownStamp &&
            (q.Who.TryFindSpellTrap(q.Card, out _) || q.Who.TryFindMonster(q.Card, out _));

        /// <summary>
        /// DuelEngine.BeginTurn after the Standby Phase triggers, and after each answer: asks the next queued upkeep
        /// choice (the LP-cost picker: PAY N, or Cancel to destroy the card). True while a choice is waiting, so the
        /// Standby Phase is held. A choice whose card has left the field is dropped; one whose controller no longer
        /// has more than N LP (a Standby burn resolved after it was queued) destroys the card, as the upkeep does
        /// with too few LP or the last LP. If another prompt is already open, the queued upkeeps are paid as before,
        /// since only one prompt can wait at a time.
        /// </summary>
        public static bool OfferUpkeepChoice(DuelEngine engine)
        {
            if (engine == null) return false;
            var queue = Queue(engine);
            queue.RemoveAll(q => q.Turn != engine.TurnNumber || !StillOnField(q));
            while (queue.Count > 0 && !engine.GameOver && queue[0].Who.LifePoints <= queue[0].Amount)
            {
                var poor = queue[0];
                queue.RemoveAt(0);
                engine.Log($"{poor.Card.Name}: {poor.Who.Name} has only {poor.Who.LifePoints} LP left for the " +
                           $"{poor.Amount} LP upkeep — destroy this card.");
                // EquipAndSpellProtection batch: a Magic Reflector counter is removed instead (REASON_COST).
                if (!EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, poor.Card))
                    engine.SendCardToGrave(poor.Who, poor.Card);
            }

            if (queue.Count == 0 || engine.GameOver) return false;
            if (engine.PendingActivation != null)
            {
                foreach (var q in queue.ToList())
                    if (q.Who.LifePoints > q.Amount)
                        engine.PayLifePoints(q.Who, q.Amount,
                            $"Cost: pay {q.Amount} LP or destroy {q.Card.Name} → {q.Who.Name} at {q.Who.LifePoints - q.Amount} LP.");
                    else if (!EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, q.Card))
                        engine.SendCardToGrave(q.Who, q.Card);
                queue.Clear();
                return false;
            }

            var next = queue[0];
            var pending = new PendingActivation
            {
                Controller = next.Who,
                Card = next.Card,
                IsMonsterEffect = next.Card.Def != null && next.Card.Def.IsMonster,
                UsesTextProgram = true,
                AwaitingUpkeepChoice = true,
                AwaitingLpCost = true,
                CostNumeric = next.Amount
            };
            pending.LpCostChoices.Add(next.Amount);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            return true;
        }

        /// <summary>
        /// The human answered the upkeep choice (PAY N through TrySelectLpCost, or Cancel): pay and keep the card,
        /// or destroy it. Then the next queued choice is asked, or the Standby Phase goes on
        /// (DuelEngine.ContinueStandbyAfterUpkeepChoices).
        /// </summary>
        public static bool AnswerUpkeepChoice(DuelEngine engine, bool pay)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingUpkeepChoice) return false;
            var queue = Queue(engine);
            var q = queue.Find(x => ReferenceEquals(x.Card, p.Card));
            queue.Remove(q);
            engine.ClearPendingActivation();
            if (q != null && StillOnField(q))
            {
                if (pay && q.Who.LifePoints >= q.Amount)
                    engine.PayLifePoints(q.Who, q.Amount,
                        $"Cost: pay {q.Amount} LP to keep {q.Card.Name} → {q.Who.Name} at {q.Who.LifePoints - q.Amount} LP.");
                else
                {
                    engine.Log($"{q.Card.Name}: {q.Who.Name} does not pay {q.Amount} LP — destroy this card.");
                    if (!EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, q.Card))
                        engine.SendCardToGrave(q.Who, q.Card);
                }
            }

            if (OfferUpkeepChoice(engine))
            {
                engine.NotifyPublic();
                return true;
            }

            engine.ContinueStandbyAfterUpkeepChoices();
            engine.NotifyPublic();
            return true;
        }

        // ─────────────────────────────── Skull Invitation ───────────────────────────────

        static List<(CardInstance card, int amount)> ActiveSkulls(DuelEngine engine)
        {
            var list = new List<(CardInstance, int)>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st == null || !st.FaceUp || st.IsNegated) continue;
                    var c = FirstClause(st, EffectTiming.ContinuousWhileFaceUp,
                        EffectActionKind.InflictDamageToOwnerPerCardSentToGy);
                    if (c != null) list.Add((st, c.Amount));
                }
            }

            return list;
        }

        /// <summary>
        /// Every card in either GY now, except the held one-shot link cards and Tokens: a Token is never sent to
        /// the GY (it ceases to exist, so ygopro raises no EVENT_TO_GRAVE for it), although the engine's
        /// SendCardToGrave (Tributes, "send to the GY" costs) still files it in the GY list.
        /// </summary>
        static HashSet<CardInstance> CurrentGyCards(DuelEngine engine, EngineState state)
        {
            var set = new HashSet<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side?.Graveyard == null) continue;
                foreach (var c in side.Graveyard)
                    if (c != null && !c.IsToken && !state.HeldUntilChainEnds.Contains(c)) set.Add(c);
            }

            return set;
        }

        /// <summary>A Normal / Quick-Play Spell or Normal / Counter Trap that leaves the field once it resolved.</summary>
        static bool IsOneShotSpellTrap(CardInstance card)
        {
            var def = card?.Def;
            if (def == null || (!def.IsSpell && !def.IsTrap)) return false;
            if (def.IsContinuousSpellOrTrap || def.IsFieldSpell || def.IsEquipSpell) return false;
            return !SpellTrapEffects.StaysOnFieldAfterActivate(card, CompiledEffectCache.GetOrCompile(def));
        }

        static void Register(DuelEngine engine, EngineState state, CardInstance skull)
        {
            if (state.Skulls.Exists(r => ReferenceEquals(r.Card, skull))) return;
            if (!state.SeenValid)
            {
                state.SeenInGy = CurrentGyCards(engine, state);
                state.SeenValid = true;
            }

            state.Skulls.Add(new SkullRegistration
            {
                Card = skull,
                EntryStamp = skull.FieldEntryStamp,
                FaceDownStamp = skull.FaceDownStamp
            });
        }

        /// <summary>
        /// Skull Invitation checkpoint: every card that is in a GY now and was not at the last checkpoint was
        /// sent there since. Each Skull Invitation that was already active at the last checkpoint and still is
        /// inflicts Amount × the new cards each player owns to that player (Lua: both damages together).
        /// A newly active one starts counting from here. Runs at the end of every board refresh, before each
        /// Chain Link resolves and before each clause applies (TextEffectRuntime.ApplyClause), so a Skull Invitation
        /// destroyed by a later Chain Link, or by a later step of the same resolution (Final Destiny's discards paid
        /// at resolution), still punishes the cards sent before it left; one clause's simultaneous sends (Heavy
        /// Storm) are still seen together. The resolving links' own one-shot cards count only once the chain is over
        /// (when they reach the GY by the rules). Re-entrant calls (a damage window) are skipped.
        /// </summary>
        public static void GySendCheckpoint(DuelEngine engine) => GySendCheckpoint(engine, null);

        /// <param name="resolvingLinkCard">The card of the Chain Link about to resolve (DuelEngine's chain loop):
        /// a one-shot Spell/Trap is held back until the chain has finished resolving.</param>
        public static void GySendCheckpoint(DuelEngine engine, CardInstance resolvingLinkCard)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            // Fatal Abacus (ContinuousTrapsAndSpiritSupport batch): monsters sent from the field since the last checkpoint.
            ContinuousTrapsAndSpiritSupportRuntime.FlushAbacus(engine);
            // Coffin Seller (EffectDamageEvents batch, v162): monsters sent to its controller's opponent's GY since then.
            EffectDamageEventsRuntime.GySendCheckpoint(engine);
            // GyHandAndSummonEventTriggers batch: Backfire / Blessings of the Nile — the sends recorded since the
            // last checkpoint were one event, so each watching copy queues one trigger.
            TextEffects.GyHandAndSummonEventTriggersRuntime.FlushEventTriggers(engine);
            var state = State(engine);
            if (!engine.IsResolvingChain)
                state.HeldUntilChainEnds.Clear();
            else if (IsOneShotSpellTrap(resolvingLinkCard) && !state.SeenInGy.Contains(resolvingLinkCard))
                state.HeldUntilChainEnds.Add(resolvingLinkCard);
            if (state.CheckpointBusy) return;
            var active = ActiveSkulls(engine);
            state.Skulls.RemoveAll(r =>
                !active.Exists(a => ReferenceEquals(a.card, r.Card)) ||
                r.Card.FieldEntryStamp != r.EntryStamp || r.Card.FaceDownStamp != r.FaceDownStamp);
            if (state.Skulls.Count == 0)
            {
                state.SeenValid = false;
                foreach (var (card, _) in active)
                    Register(engine, state, card);
                return;
            }

            var now = CurrentGyCards(engine, state);
            var sent = new List<CardInstance>();
            if (state.SeenValid)
            {
                foreach (var c in now)
                    if (!state.SeenInGy.Contains(c)) sent.Add(c);
            }

            state.SeenInGy = now;
            state.SeenValid = true;
            var counting = state.Skulls.ToList();
            foreach (var (card, _) in active)
                Register(engine, state, card);
            if (sent.Count == 0) return;

            state.CheckpointBusy = true;
            try
            {
                foreach (var reg in counting)
                {
                    if (engine.GameOver) break;
                    var controller = engine.ControllerOf(reg.Card);
                    if (controller == null) continue;
                    var opp = engine.OpponentOf(controller);
                    var amount = active.First(a => ReferenceEquals(a.card, reg.Card)).amount;
                    var toController = sent.Count(c => controller.Graveyard.Contains(c));
                    var toOpponent = sent.Count(c => opp != null && opp.Graveyard.Contains(c));
                    if (toController + toOpponent == 0) continue;
                    engine.Log($"{reg.Card.Name}: {toController + toOpponent} card(s) sent to the GY — " +
                               $"{amount} damage to its owner for each.");
                    engine.ApplyEffectDamageBoth(opp, amount * toOpponent, controller, amount * toController,
                        reg.Card.Name);
                }
            }
            finally
            {
                state.CheckpointBusy = false;
            }
        }

        /// <summary>
        /// AI (AiAutoRespond, chain response): activate a Set Skull Invitation when the opponent has a one-shot
        /// Spell/Trap link on the chain — that card goes to its owner's GY once the chain has resolved, so it
        /// costs them 300. The caller skips it when the chain would remove the Skull Invitation first.
        /// </summary>
        public static bool AiWantsGySendWatch(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine?.Chain == null || who == null || card == null || card.FaceUp) return false;
            if (!who.TryFindSpellTrap(card, out _)) return false;
            if (FirstClause(card, EffectTiming.Activate, EffectActionKind.InflictDamageToOwnerPerCardSentToGy) == null)
                return false;
            var opp = engine.OpponentOf(who);
            if (opp == null) return false;
            foreach (var link in engine.Chain.Links)
                if (link != null && !link.Negated && link.Controller == opp && IsOneShotSpellTrap(link.Card))
                    return true;
            return false;
        }

        /// <summary>
        /// Skull Invitation's activation resolves: cards sent before now are settled for any other Skull
        /// Invitation, and this one watches from here on.
        /// </summary>
        public static void StartGySendWatch(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || source == null) return;
            GySendCheckpoint(engine);
            if (!source.FaceUp || source.IsNegated || !InSpellTrapZone(engine, source, out _)) return;
            Register(engine, State(engine), source);
        }

        // ─────────────────────────────── chain resolution ───────────────────────────────

        /// <summary>
        /// DuelEngine's chain loop, before a link resolves: true when the link is a Continuous Spell/Trap's own
        /// activation (from the hand or a Set card, not an effect of a card that was already face-up) and the
        /// card is no longer on its controller's field — it was destroyed, banished or returned by a later
        /// Chain Link. Its activation then resolves with no effect (ygopro <c>c:IsRelateToEffect(e)</c>), and it
        /// must not be re-seated from the GY by the stays-on-field finish (Skull Invitation / Mirror Wall /
        /// Call of the Haunted with an MST chained to them). Mirrors the guard the activation-less Continuous
        /// path already had (Royal Decree answering a Dust Tornado).
        /// A "When this card is activated:" effect (<see cref="EffectClause.ResolvesEvenIfThisLeftField"/>, Burning
        /// Land) is not tied to the card: that link resolves normally (false here), and
        /// <see cref="SettleOffFieldActivation"/> then takes the card back out of the Spell &amp; Trap Zone the
        /// resolution seated it in, so it is only where it went.
        /// </summary>
        public static bool ContinuousActivationLeftField(DuelEngine engine, Rules.ChainLink link)
        {
            var card = link?.Card;
            var def = card?.Def;
            var who = link?.Controller;
            if (engine == null || def == null || who == null || def.IsMonster) return false;
            if (!def.IsContinuousSpellOrTrap) return false;
            if (!link.FromHand && !link.WasSetOnField) return false;
            if (who.TryFindSpellTrap(card, out _) || (who.Hand != null && who.Hand.Contains(card)) ||
                (who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card))
                return false;
            var other = engine.OpponentOf(who);
            if (other != null && other.TryFindSpellTrap(card, out _)) return false;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog != null && prog.ClauseList.Exists(c =>
                    c != null && c.Timing == EffectTiming.Activate && c.ResolvesEvenIfThisLeftField))
            {
                State(engine).OffFieldActivation = card;
                return false;
            }

            return true;
        }

        /// <summary>
        /// DuelEngine's chain loop, after a link resolved: when it was the activation of a card that had already
        /// left the field (<see cref="ContinuousActivationLeftField"/>, Burning Land) and the resolution seated
        /// it in a Spell &amp; Trap Zone again while it is in the GY or banished, that seat is removed — the card
        /// is only where it went.
        /// </summary>
        public static void SettleOffFieldActivation(DuelEngine engine, Rules.ChainLink link)
        {
            if (engine == null || link?.Card == null) return;
            var state = State(engine);
            if (!ReferenceEquals(state.OffFieldActivation, link.Card)) return;
            state.OffFieldActivation = null;
            var card = link.Card;
            if (!InGraveyardOrBanished(engine, card)) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.TryFindSpellTrap(card, out var zi))
                    side.SpellTrapZones[zi].Occupant = null;
                if (side.FieldSpellZone != null && ReferenceEquals(side.FieldSpellZone.Occupant, card))
                    side.FieldSpellZone.Occupant = null;
            }

            engine.Log($"{card.Name} already left the field — its activation resolved, and it stays there.");
        }

        /// <summary>The card is in either player's GY or banished pile (it has left the field).</summary>
        public static bool InGraveyardOrBanished(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if ((side.Graveyard != null && side.Graveyard.Contains(card)) ||
                    (side.Banished != null && side.Banished.Contains(card)))
                    return true;
            }

            return false;
        }

        // ─────────────────────────────── board refresh ───────────────────────────────

        /// <summary>
        /// FieldSpellEffects.RefreshBoard, after every modifier was cleared: Castle and Pumpking growth, then
        /// Mirror Wall's halving counts (read by <see cref="CardInstance.CurrentAtk"/> as the final step).
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var state = State(engine);
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m?.Def == null) continue;
                    if (m.StandbyGrowth?.Targets != null)
                        ApplyRaceGrowth(engine, m);
                    ApplyNamedSelfGrowth(engine, m);
                    ApplyNamedGyGrowth(engine, m);
                }
            }

            ApplyMirrorWalls(engine, state);
        }

        static void ApplyNamedGyGrowth(DuelEngine engine, CardInstance self)
        {
            var clause = FirstClause(self, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.GainAtkDefPerNamedInBothGyWhileFaceUp);
            if (clause == null)
            {
                clause = FirstClause(self, EffectTiming.ContinuousWhileFaceUp,
                    EffectActionKind.GainAtkDefPerNamedCardInBothGyWhileFaceUp);
            }
            if (clause == null) return;
            if (!self.FaceUp || self.IsNegated)
            {
                self.StandbyGrowth = null;
                return;
            }

            var named = string.IsNullOrEmpty(clause.NamedCard) ? string.Empty : clause.NamedCard;
            var altNamed = string.IsNullOrEmpty(clause.AltNamedCard) ? string.Empty : clause.AltNamedCard;
            if (string.IsNullOrEmpty(named) && string.IsNullOrEmpty(altNamed)) return;

            var controller = engine.ControllerOf(self);
            var opp = engine.OpponentOf(controller);
            var db = CardDatabase.Instance ?? CardDatabase.Load();
            int CountIn(DuelistState who)
            {
                if (who == null || who.Graveyard == null) return 0;
                var count = 0;
                foreach (var c in who.Graveyard)
                {
                    if (c?.Def?.name == null) continue;
                    var name = c.Def.name;
                    if (string.Equals(name, named, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, altNamed, System.StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                return count;
            }

            var total = CountIn(controller) + (opp != null ? CountIn(opp) : 0);
            if (total <= 0)
            {
                self.StandbyGrowth = null;
                return;
            }

            self.AtkModifier += clause.Amount * total;
            self.DefModifier += clause.DefAmount * total;
        }
    }
}
