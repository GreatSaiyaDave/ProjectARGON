using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleWindows" runtime: attack-declaration triggers, the battle-confirm switch, the end-of-Damage-Step
    /// window and the "monster sent from the field to your GY" window. Reads compiled programs only — no card-id
    /// branches. Atoms: <see cref="BattleWindowsAtoms"/>.
    /// <list type="bullet">
    /// <item><b>Attack declaration</b> (<see cref="FireOpponentAttackDeclaredTriggers"/>, DuelEngine.TryAttack, after the
    /// attacker's own "declares an attack" effects and before the defender's response window): every face-up, not negated
    /// Fairy Box / Jam Defender of the defender triggers, in field order, resolving at once like the engine's other
    /// attack-declaration triggers. Fairy Box: its controller calls a coin (a human gets the HEADS / TAILS prompt, the AI
    /// calls Heads); right, the attacker's ATK becomes 0 until the end of the Battle Phase
    /// (<see cref="CardInstance.AtkZeroUntilEndOfBattlePhase"/>, kept by <see cref="ApplyBoard"/>, cleared by
    /// <see cref="EndBattlePhase"/>, when the monster leaves the field or is turned face-down). Jam Defender (optional,
    /// only for an attack on a monster you control): a human picks a face-up named monster or CANCELs; the AI uses it
    /// when the attacked monster would not survive. A pause continues the attack through
    /// DuelEngine.ContinueDeclaredAttack.</item>
    /// <item><b>Dreamsprite</b> (<see cref="BeginBattleConfirmSwitch"/>, DuelEngine.ResolveDeclaredAttack after the
    /// face-down flip): mandatory; it targets another monster its controller controls (a lone one, or the AI's pick,
    /// at once; a human picks among several) and, if the attacker can still attack and is affected, the attacker battles
    /// that monster instead (a face-down one is flipped face-up as in any battle); the Damage Step then goes on through
    /// DuelEngine.ContinueBeforeDamageCalculation.</item>
    /// <item><b>End of the Damage Step</b> (<see cref="BeginEndOfDamageStep"/>, DuelEngine.ApplyBattleDestructions after
    /// battle destruction): the <see cref="ResponseTiming.EndOfDamageStep"/> window, turn player first, each duelist only
    /// when they have a legal answer; after an answer resolves both may answer again. Destruction Punch
    /// (<see cref="AttackerAtkBelowYourDefenderDefAtEndOfDamageStep"/>) is legal in the first round only, by the
    /// defender, while the attacker and the attacked Defense Position monster are still on the field from that battle and
    /// the monster's DEF is higher than the attacker's ATK; it destroys the attacker.</item>
    /// <item><b>Michizure</b> (<see cref="MonsterSentFromFieldToYourGy"/>): every monster that leaves a Monster Zone for
    /// a GY is recorded for that GY's owner (<see cref="NoteMonsterSentFromField"/>: DuelEngine.SendCardToGrave,
    /// DestroyMonster, a Summon negated and destroyed). Like Gust (SpellTrapDestroyedResponse) only the last thing to
    /// happen can be answered: the records are dropped when another Chain Link starts resolving, when a queued FLIP effect
    /// resolves, when an attack moves on to the Damage Step, when a window that offered them closes, and when the phase
    /// ends before one could open. They are answerable in the end-of-Damage-Step window (sends in that Damage Step), in
    /// the idle <see cref="ResponseTiming.MonsterSentFromFieldToYourGy"/> window (DuelEngine.OfferMonsterSentWindow, also
    /// right after an activation resolved inline, DuelEngine.ClearIdleChain), and in a chain response window of the chain
    /// being built. A cost send (paid in TextEffectRuntime.PayAutoIgnitionCosts / PayOneIgnitionCost, <see cref="BeginCost"/>)
    /// is answerable only that way, while its activation is on the chain: when the opponent has nothing to chain, the
    /// activator still gets the window (DuelEngine.OpenChainResponseWindow, <see cref="AnswersSendThisChainMade"/>). A
    /// monster ignition that resolves inline builds no chain, so its cost send is dropped (<see cref="DropCostSends"/>).</item>
    /// <item><b>Dark Spirit of the Silent</b> (<see cref="ResolveDarkSpirit"/>): negates the attack; then the other
    /// targeted monster, if still face-up, is changed to Attack Position if it is in Defense Position, and once the chain
    /// has resolved it battles the original attack target (or the opponent directly) if it can attack, is affected and
    /// that target is still on the field (<see cref="TryStartReplacementBattle"/>, DuelEngine.RunReplacementBattle).</item>
    /// </list>
    /// </summary>
    public static class BattleWindowsRuntime
    {
        /// <summary>ActivationCondition (Michizure): a monster was just sent from the field to your GY.</summary>
        public const string MonsterSentFromFieldToYourGy = "MonsterSentFromFieldToYourGy";

        /// <summary>
        /// ActivationCondition (Destruction Punch): the end of a Damage Step in which the opponent's attacker battled your
        /// Defense Position monster whose DEF is higher than the attacker's ATK.
        /// </summary>
        public const string AttackerAtkBelowYourDefenderDefAtEndOfDamageStep =
            "AttackerAtkBelowYourDefenderDefAtEndOfDamageStep";

        sealed class DeclTrigger
        {
            public CardInstance Source;
            public DuelistState Controller;
            public EffectClause Clause;
        }

        sealed class SentRecord
        {
            public DuelistState Owner;
            public CardInstance Card;
            public int Turn;
            public DuelPhase Phase;
            /// <summary>Sent while a chain resolved (not by an activation being made, e.g. a Tribute cost).</summary>
            public bool DuringResolution;
            /// <summary>
            /// Sent as an activation's cost (TextEffectRuntime.PayAutoIgnitionCosts / PayOneIgnitionCost): answerable only
            /// by chaining to that activation while <see cref="Link"/> is still on the chain being built.
            /// </summary>
            public bool ByCost;
            /// <summary>The last Chain Link when the cost was paid (the activation's own link, or the one it chains to).</summary>
            public ChainLink Link;
        }

        sealed class AtkZeroGrant
        {
            public CardInstance Monster;
            public int EntryStamp;
            public int FaceDownStamp;
            public int Turn;
        }

        sealed class EndOfDamageStepBattleState
        {
            public DuelistState AttackingPlayer;
            public DuelistState DefendingPlayer;
            public CardInstance Attacker;
            public CardInstance Target;
            public int AttackerEntryStamp;
            public int TargetEntryStamp;
            /// <summary>0 until an answer made in this window has resolved.</summary>
            public int Round;
            public readonly List<DuelistState> Passed = new();
        }

        sealed class ReplacementBattle
        {
            public DuelistState AttackingPlayer;
            public CardInstance Monster;
            public int MonsterEntryStamp;
            public CardInstance Target;
            public int TargetEntryStamp;
            public CardInstance Source;
        }

        sealed class State
        {
            public readonly List<DeclTrigger> DeclQueue = new();
            public CardInstance DeclAttacker;
            public readonly List<AtkZeroGrant> AtkZero = new();
            public readonly List<SentRecord> Sent = new();
            public readonly List<SentRecord> Offered = new();
            public EndOfDamageStepBattleState EndOfDamageStep;
            public ReplacementBattle Replacement;
            /// <summary>Depth of the cost payments in progress (<see cref="BeginCost"/>).</summary>
            public int CostDepth;
        }

        enum PickKind
        {
            FairyBoxCall,
            JamDefenderTarget,
            DreamspriteTarget
        }

        sealed class Pick
        {
            public PickKind Kind;
            public DeclTrigger Trigger;
            public DuelistState AttackingPlayer;
            public DuelistState DefendingPlayer;
            public CardInstance Attacker;
            public CardInstance Target;
            public CardInstance Flipped;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, Pick> s_picks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static bool OnFieldAsMonster(DuelEngine engine, CardInstance m) =>
            m != null && ((engine.Player?.TryFindMonster(m, out _) ?? false) ||
                          (engine.Opponent?.TryFindMonster(m, out _) ?? false));

        static EffectClause FirstClause(CardInstance card, EffectTiming timing, EffectActionKind action)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card) : null;
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(timing).FirstOrDefault(c => c != null && c.Action == action);
        }

        static bool HasActivationCondition(CompiledCardProgram prog, string condition) =>
            prog != null && prog.FullyCompiled &&
            prog.ClausesFor(EffectTiming.Activate).Exists(c => c != null && c.ActivationCondition == condition);

        /// <summary>A face-up Continuous Trap of <paramref name="who"/> whose effects apply now (not negated).</summary>
        static bool TriggerSourceActive(DuelEngine engine, DuelistState who, CardInstance card) =>
            card?.Def != null && card.FaceUp && !card.IsNegated && who.TryFindSpellTrap(card, out _) &&
            !ContinuousNegationRuntime.NegatesEffectActivationOnField(engine, card);

        /// <summary>Rough battle check: <paramref name="defender"/> is not destroyed by <paramref name="attacker"/>.</summary>
        static bool SurvivesAttack(CardInstance attacker, CardInstance defender)
        {
            if (attacker == null || defender == null) return false;
            var atk = attacker.CurrentAtk;
            if (defender.Position == BattlePosition.Attack)
                return defender.FaceUp && defender.CurrentAtk > atk;
            var def = defender.FaceUp ? defender.CurrentDef : System.Math.Max(0, defender.Def?.def ?? 0);
            return def >= atk;
        }

        static int Value(CardInstance m) =>
            m == null
                ? 0
                : m.FaceUp
                    ? System.Math.Max(m.CurrentAtk, m.CurrentDef)
                    : System.Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0);

        // ───────────────────────── attack declaration: Fairy Box / Jam Defender ─────────────────────────

        /// <summary>
        /// An opponent's monster declared an attack (DuelEngine.TryAttack): the defender's face-up Fairy Box / Jam
        /// Defender trigger. True when a human's coin call or target pick paused the attack (the runtime continues it once
        /// answered); false when the attack goes on now.
        /// </summary>
        public static bool FireOpponentAttackDeclaredTriggers(DuelEngine engine, DuelistState attackingPlayer,
            CardInstance attacker)
        {
            if (engine == null || attackingPlayer == null || attacker == null || engine.GameOver) return false;
            var st = StateOf(engine);
            st.DeclQueue.Clear();
            st.DeclAttacker = null;
            var defender = engine.OpponentOf(attackingPlayer);
            if (defender == null) return false;
            foreach (var card in defender.SpellTrapsOnField().ToList())
            {
                if (!TriggerSourceActive(engine, defender, card)) continue;
                var prog = CompiledEffectCache.GetOrCompile(card);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null) continue;
                    if (c.Action != EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase &&
                        c.Action != EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl &&
                        // CallsDiceAndSetTriggers batch (v163): Ordeal of a Traveler.
                        c.Action != EffectActionKind.OpponentCallsRandomHandCardTypeWrongReturnsAttacker)
                        continue;
                    st.DeclQueue.Add(new DeclTrigger { Source = card, Controller = defender, Clause = c });
                }
            }

            if (st.DeclQueue.Count == 0) return false;
            st.DeclAttacker = attacker;
            return RunDeclarationTriggers(engine, st);
        }

        /// <summary>Resolves the queued declaration triggers in order. True = paused for a human's choice.</summary>
        static bool RunDeclarationTriggers(DuelEngine engine, State st)
        {
            while (st.DeclQueue.Count > 0)
            {
                var t = st.DeclQueue[0];
                st.DeclQueue.RemoveAt(0);
                if (engine.GameOver || engine.DeclaredAttacker == null ||
                    !ReferenceEquals(engine.DeclaredAttacker, st.DeclAttacker))
                {
                    st.DeclQueue.Clear();
                    return false;
                }

                if (!TriggerSourceActive(engine, t.Controller, t.Source)) continue;
                if (t.Clause.Action == EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase)
                {
                    if (StartFairyBox(engine, t)) return true;
                }
                else if (t.Clause.Action == EffectActionKind.OpponentCallsRandomHandCardTypeWrongReturnsAttacker)
                {
                    // CallsDiceAndSetTriggers batch (v163): Ordeal of a Traveler; a human's choice continues the
                    // declaration triggers through ContinueDeclarationTriggers.
                    if (CallsDiceAndSetTriggersRuntime.StartOrdeal(engine, t.Source, t.Controller,
                            ContinueDeclarationTriggers))
                        return true;
                }
                else if (StartJamDefender(engine, t))
                    return true;
            }

            st.DeclAttacker = null;
            return false;
        }

        /// <summary>A paused declaration trigger was answered: the next one, or the attack goes on.</summary>
        static void ContinueDeclarationTriggers(DuelEngine engine)
        {
            var st = StateOf(engine);
            if (RunDeclarationTriggers(engine, st))
            {
                engine.NotifyPublic();
                return;
            }

            if (engine.HasDeclaredAttack)
                engine.ContinueDeclaredAttack();
            else
                engine.NotifyPublic();
        }

        static bool StartFairyBox(DuelEngine engine, DeclTrigger t)
        {
            var who = t.Controller;
            var attacker = engine.DeclaredAttacker;
            engine.Log($"{t.Source.Name}: {attacker?.Name ?? "the attacker"} declared an attack — {who.Name} tosses a " +
                       "coin and calls it.");
            if (engine.IsHumanControlled(who))
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = t.Source,
                    UsesTextProgram = true,
                    AwaitingCoinCall = true,
                    PromptOverride = $"{t.Source.Name}: call the coin toss — if you call it right, " +
                                     $"{attacker?.Name ?? "the attacking monster"}'s ATK becomes 0 until the end of the " +
                                     "Battle Phase."
                };
                s_picks.Add(pending, new Pick { Kind = PickKind.FairyBoxCall, Trigger = t });
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            ResolveFairyBox(engine, t, callHeads: true);
            return false;
        }

        /// <summary>
        /// Fairy Box's toss and its result. The toss is a coin-toss event (CoinDiceInterrupts batch): Second Coin Toss
        /// may redo it first; <paramref name="after"/> runs once the result has applied (the declaration goes on).
        /// </summary>
        static void ResolveFairyBox(DuelEngine engine, DeclTrigger t, bool callHeads, System.Action after = null)
        {
            var attacker = engine.DeclaredAttacker;
            CoinDiceInterruptsRuntime.TossCoin(engine, t.Controller, t.Source, toss => toss == callHeads, toss =>
            {
                ApplyFairyBoxToss(engine, t, attacker, callHeads, toss);
                after?.Invoke();
            });
        }

        static void ApplyFairyBoxToss(DuelEngine engine, DeclTrigger t, CardInstance attacker, bool callHeads, bool toss)
        {
            var who = t.Controller;
            engine.Log($"{who.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(toss ? "Heads" : "Tails")}.");
            if (toss != callHeads)
            {
                engine.Log($"{t.Source.Name}: wrong call — no effect.");
                return;
            }

            if (attacker == null || !OnFieldAsMonster(engine, attacker))
            {
                engine.Log($"{t.Source.Name}: the attacking monster is no longer on the field — no effect.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, attacker, t.Source))
            {
                engine.Log($"{attacker.Name} is unaffected by {t.Source.Name}.");
                return;
            }

            var st = StateOf(engine);
            attacker.AtkZeroUntilEndOfBattlePhase = true;
            st.AtkZero.Add(new AtkZeroGrant
            {
                Monster = attacker,
                EntryStamp = attacker.FieldEntryStamp,
                FaceDownStamp = attacker.FaceDownStamp,
                Turn = engine.TurnNumber
            });
            engine.Log($"{t.Source.Name}: right call — {attacker.Name}'s ATK becomes 0 until the end of the Battle Phase.");
        }

        static List<CardInstance> JamDefenderCandidates(DuelEngine engine, DeclTrigger t, CardInstance attacker,
            DuelistState attackingPlayer, CardInstance current)
        {
            var list = new List<CardInstance>();
            if (string.IsNullOrEmpty(t.Clause.NamedCard)) return list;
            foreach (var m in t.Controller.MonstersOnField())
            {
                if (m == null || !m.FaceUp || ReferenceEquals(m, current)) continue;
                if (!m.IsNamed(t.Clause.NamedCard)) continue;
                if (!RedirectAndReplaceRuntime.AttackerCouldAttack(engine, attackingPlayer, attacker, m)) continue;
                if (ContinuousProtections.TargetedEffectBlocked(engine, m, t.Source)) continue;
                list.Add(m);
            }

            return list;
        }

        static bool StartJamDefender(DuelEngine engine, DeclTrigger t)
        {
            var who = t.Controller;
            var attacker = engine.DeclaredAttacker;
            var attackingPlayer = engine.DeclaredAttackingPlayer;
            var current = engine.DeclaredAttackTarget;
            // "declares an attack on a monster you control" (Lua: the opponent's turn and an attack target).
            if (attacker == null || attackingPlayer == null || attackingPlayer == who || current == null ||
                !who.TryFindMonster(current, out _))
                return false;
            var candidates = JamDefenderCandidates(engine, t, attacker, attackingPlayer, current);
            if (candidates.Count == 0) return false;
            if (engine.IsHumanControlled(who))
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = t.Source,
                    UsesTextProgram = true,
                    TargetKind = EffectTargetKind.YourMonster,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"{t.Source.Name}: you can switch the attack target to 1 \"{t.Clause.NamedCard}\" " +
                                     "you control — tap it, or CANCEL."
                };
                pending.LegalTargets.AddRange(candidates);
                s_picks.Add(pending, new Pick { Kind = PickKind.JamDefenderTarget, Trigger = t });
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            // AI: only when the attacked monster would not survive the attack.
            if (SurvivesAttack(attacker, current))
            {
                engine.Log($"{t.Source.Name}: {who.Name} does not use its effect.");
                return false;
            }

            var pick = candidates.Where(m => SurvivesAttack(attacker, m)).OrderByDescending(Value).FirstOrDefault()
                       ?? candidates.OrderByDescending(Value).First();
            ResolveJamDefender(engine, t, pick);
            return false;
        }

        static void ResolveJamDefender(DuelEngine engine, DeclTrigger t, CardInstance pick)
        {
            var who = t.Controller;
            var attacker = engine.DeclaredAttacker;
            var attackingPlayer = engine.DeclaredAttackingPlayer;
            engine.Log($"{t.Source.Name}: {who.Name} activates its effect, targeting {pick.Name}.");
            if (!TriggerSourceActive(engine, who, t.Source))
            {
                engine.Log($"{t.Source.Name} is no longer face-up on the field — no effect.");
                return;
            }

            if (attacker == null || attackingPlayer == null || engine.DeclaredAttackTarget == null ||
                !pick.FaceUp || !who.TryFindMonster(pick, out _))
            {
                engine.Log($"{t.Source.Name}: the attack can no longer be switched — no effect.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, attacker, t.Source))
            {
                engine.Log($"{attacker.Name} is unaffected — the attack target does not change.");
                return;
            }

            if (!RedirectAndReplaceRuntime.AttackerCouldAttack(engine, attackingPlayer, attacker, pick))
            {
                engine.Log($"{attacker.Name} cannot attack {pick.Name} — no effect.");
                return;
            }

            engine.ChangeDeclaredAttackTarget(pick);
        }

        // ───────────────────────── Fairy Box: ATK 0 until the end of the Battle Phase ─────────────────────────

        static bool GrantLive(DuelEngine engine, AtkZeroGrant g) =>
            g?.Monster != null && g.Monster.FaceUp && g.Monster.FieldEntryStamp == g.EntryStamp &&
            g.Monster.FaceDownStamp == g.FaceDownStamp && g.Turn == engine.TurnNumber &&
            engine.Phase == DuelPhase.Battle && OnFieldAsMonster(engine, g.Monster);

        /// <summary>Each board refresh (FieldSpellEffects.RefreshBoard): keep the live grants, drop the rest.</summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.AtkZero.Count == 0) return;
            foreach (var g in st.AtkZero)
                if (g?.Monster != null)
                    g.Monster.AtkZeroUntilEndOfBattlePhase = false;
            st.AtkZero.RemoveAll(g => !GrantLive(engine, g));
            foreach (var g in st.AtkZero)
                g.Monster.AtkZeroUntilEndOfBattlePhase = true;
        }

        /// <summary>DuelEngine.EndBattlePhase: every "until the end of the Battle Phase" ATK of 0 ends.</summary>
        public static void EndBattlePhase(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            foreach (var g in st.AtkZero)
                if (g?.Monster != null)
                    g.Monster.AtkZeroUntilEndOfBattlePhase = false;
            st.AtkZero.Clear();
        }

        // ───────────────────────── Dreamsprite: the attacker battles another monster ─────────────────────────

        /// <summary>
        /// Before damage calculation (after the face-down flip): an attacked Dreamsprite moves the battle. True when it
        /// applied — the runtime has continued the Damage Step (DuelEngine.ContinueBeforeDamageCalculation) or is waiting
        /// for the player's pick; false = no such effect, the engine goes on as usual.
        /// </summary>
        public static bool BeginBattleConfirmSwitch(DuelEngine engine, DuelistState attackingPlayer,
            DuelistState defendingPlayer, CardInstance attacker, CardInstance target, CardInstance flippedByBattle)
        {
            if (engine == null || engine.GameOver || attackingPlayer == null || defendingPlayer == null ||
                attacker == null || target?.Def == null)
                return false;
            if (!defendingPlayer.TryFindMonster(target, out _) || !attackingPlayer.TryFindMonster(attacker, out _))
                return false;
            if (!target.FaceUp || target.IsNegated) return false;
            var clause = FirstClause(target, EffectTiming.BeforeDamageCalculation,
                EffectActionKind.BattleAnotherOwnMonsterInstead);
            if (clause == null || !clause.RequiresThisIsAttackTarget) return false;
            var candidates = defendingPlayer.MonstersOnField()
                .Where(m => m != null && !ReferenceEquals(m, target) &&
                            !ContinuousProtections.TargetedEffectBlocked(engine, m, target))
                .ToList();
            engine.Log($"{target.Name}: attacked by {attacker.Name} — its effect activates.");
            if (candidates.Count == 0)
            {
                engine.Log($"{target.Name}: no other monster to designate — no effect.");
                return false;
            }

            if (candidates.Count > 1 && engine.IsHumanControlled(defendingPlayer))
            {
                var pending = new PendingActivation
                {
                    Controller = defendingPlayer,
                    Card = target,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetKind = EffectTargetKind.YourMonster,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"{target.Name}: choose another monster you control — {attacker.Name} battles it " +
                                     "instead."
                };
                pending.LegalTargets.AddRange(candidates);
                s_picks.Add(pending, new Pick
                {
                    Kind = PickKind.DreamspriteTarget,
                    AttackingPlayer = attackingPlayer,
                    DefendingPlayer = defendingPlayer,
                    Attacker = attacker,
                    Target = target,
                    Flipped = flippedByBattle
                });
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            var pick = candidates.Count == 1 ? candidates[0] : AiPickBattleSwitch(attacker, candidates);
            FinishBattleSwitch(engine, attackingPlayer, defendingPlayer, attacker, target, flippedByBattle, pick);
            return true;
        }

        static CardInstance AiPickBattleSwitch(CardInstance attacker, List<CardInstance> candidates) =>
            candidates.Where(m => SurvivesAttack(attacker, m)).OrderByDescending(Value).FirstOrDefault()
            ?? candidates.OrderBy(Value).First();

        static void FinishBattleSwitch(DuelEngine engine, DuelistState attackingPlayer, DuelistState defendingPlayer,
            CardInstance attacker, CardInstance target, CardInstance flippedByBattle, CardInstance pick)
        {
            engine.Log($"{target.Name}: targets {(pick.FaceUp ? pick.Name : "a face-down monster")}.");
            // Lua: tc:IsRelateToEffect(e) and a:CanAttack() and not a:IsImmuneToEffect(e) → Duel.CalculateDamage(a, tc).
            if (pick != null && defendingPlayer.TryFindMonster(pick, out _) &&
                attackingPlayer.TryFindMonster(attacker, out _) && attacker.FaceUp &&
                attacker.Position == BattlePosition.Attack &&
                !ContinuousProtections.IsUnaffectedBy(engine, attacker, target))
            {
                CardInstance flipped = null;
                if (!pick.FaceUp)
                {
                    pick.FaceUp = true;
                    pick.Position = BattlePosition.Defense;
                    flipped = pick;
                    engine.Log($"[Damage Step] Flipped {pick.Name} face-up in Defense Position " +
                               $"(DEF {BattleMechanics.DefenseValue(pick)}).");
                }

                engine.Log($"{attacker.Name} battles {pick.Name} instead of {target.Name}.");
                // ygopro Duel.CalculateDamage(a, tc): tc is now the attack target (Damage Calculation window, presentation).
                if (ReferenceEquals(engine.DeclaredAttacker, attacker))
                    engine.ChangeDeclaredAttackTarget(pick);
                engine.ContinueBeforeDamageCalculation(attackingPlayer, defendingPlayer, attacker, pick, flipped,
                    switchedBattle: true);
                return;
            }

            engine.Log($"{target.Name}: the battle cannot be moved — no effect.");
            engine.ContinueBeforeDamageCalculation(attackingPlayer, defendingPlayer, attacker, target, flippedByBattle);
        }

        // ───────────────────────── end of the Damage Step ─────────────────────────

        /// <summary>
        /// DuelEngine.ApplyBattleDestructions, after battle destruction: the end-of-Damage-Step window may open. False when
        /// it cannot open now (a choice or another window is pending, or the duel is decided).
        /// </summary>
        public static bool BeginEndOfDamageStep(DuelEngine engine, DuelistState attackingPlayer,
            DuelistState defendingPlayer, CardInstance attacker, CardInstance target)
        {
            if (engine == null) return false;
            var st = StateOf(engine);
            st.EndOfDamageStep = null;
            if (engine.GameOver || attackingPlayer == null || defendingPlayer == null) return false;
            if (engine.PendingActivation != null || engine.PendingResponse != null) return false;
            if (attackingPlayer.LifePoints <= 0 || defendingPlayer.LifePoints <= 0) return false;
            st.EndOfDamageStep = new EndOfDamageStepBattleState
            {
                AttackingPlayer = attackingPlayer,
                DefendingPlayer = defendingPlayer,
                Attacker = attacker,
                Target = target,
                AttackerEntryStamp = attacker?.FieldEntryStamp ?? 0,
                TargetEntryStamp = target?.FieldEntryStamp ?? 0
            };
            return true;
        }

        public static bool EndOfDamageStepOpen(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.EndOfDamageStep != null;

        public static void EndOfDamageStepBattle(DuelEngine engine, out DuelistState attackingPlayer,
            out CardInstance attacker, out CardInstance target)
        {
            var e = engine != null && s_state.TryGetValue(engine, out var st) ? st.EndOfDamageStep : null;
            attackingPlayer = e?.AttackingPlayer;
            attacker = e?.Attacker;
            target = e?.Target;
        }

        public static bool EndOfDamageStepPassed(DuelEngine engine, DuelistState who) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.EndOfDamageStep != null &&
            st.EndOfDamageStep.Passed.Contains(who);

        public static void NoteEndOfDamageStepPass(DuelEngine engine, DuelistState who)
        {
            var e = engine != null && s_state.TryGetValue(engine, out var st) ? st.EndOfDamageStep : null;
            if (e != null && who != null && !e.Passed.Contains(who))
                e.Passed.Add(who);
        }

        /// <summary>An answer made in the window resolved: a new round, both duelists may answer what it did.</summary>
        public static void NoteEndOfDamageStepChainResolved(DuelEngine engine)
        {
            var e = engine != null && s_state.TryGetValue(engine, out var st) ? st.EndOfDamageStep : null;
            if (e == null) return;
            e.Round++;
            e.Passed.Clear();
        }

        /// <summary>The Damage Step ends: its sends to the GY can no longer be answered.</summary>
        public static void FinishEndOfDamageStep(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.EndOfDamageStep = null;
            st.Sent.Clear();
            st.Offered.Clear();
        }

        static bool AttackerStillFromThatBattle(EndOfDamageStepBattleState e) =>
            e?.Attacker != null && e.AttackingPlayer != null && e.AttackingPlayer.TryFindMonster(e.Attacker, out _) &&
            e.Attacker.FieldEntryStamp == e.AttackerEntryStamp;

        /// <summary>
        /// Activation condition <see cref="AttackerAtkBelowYourDefenderDefAtEndOfDamageStep"/> (Lua: d and
        /// a:IsControler(1-tp) and a:IsRelateToBattle() and d:IsDefensePos() and d:IsRelateToBattle() and
        /// d:GetDefense() &gt; a:GetAttack()), in the end-of-Damage-Step window or a chain built in it, first round only.
        /// </summary>
        public static bool DestructionPunchWindowOpen(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = "Only at the end of the Damage Step, when the ATK of your opponent's attacking monster is lower " +
                     "than the DEF of your attacked Defense Position monster.";
            var e = engine != null && s_state.TryGetValue(engine, out var st) ? st.EndOfDamageStep : null;
            if (e == null || e.Round > 0 || who == null) return false;
            var pr = engine.PendingResponse;
            if (pr == null || pr.Responder != who) return false;
            if (pr.Timing != ResponseTiming.EndOfDamageStep &&
                !(pr.Timing == ResponseTiming.ChainResponse && engine.ResumeAfterChain == ResponseTiming.EndOfDamageStep))
                return false;
            if (e.DefendingPlayer != who || e.AttackingPlayer == who) return false;
            var d = e.Target;
            if (d == null || !AttackerStillFromThatBattle(e)) return false;
            if (!who.TryFindMonster(d, out _) || d.FieldEntryStamp != e.TargetEntryStamp) return false;
            if (d.Position != BattlePosition.Defense) return false;
            return d.CurrentDef > e.Attacker.CurrentAtk;
        }

        /// <summary>Destruction Punch resolves: destroy that battle's attacker if it is still on the field.</summary>
        public static void ResolveDestructionPunch(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var e = engine != null && s_state.TryGetValue(engine, out var st) ? st.EndOfDamageStep : null;
            if (e == null || !AttackerStillFromThatBattle(e))
            {
                engine?.Log($"{source?.Name}: the attacking monster is no longer on the field — no effect.");
                return;
            }

            engine.Log($"{source?.Name}: destroy {e.Attacker.Name}.");
            TextEffectRuntime.DestroyCard(engine, e.Attacker, source);
        }

        /// <summary>AI: Destruction Punch whenever it is legal — it only ever destroys the opponent's attacker.</summary>
        public static bool AiWantsDestructionPunch(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return HasActivationCondition(prog, AttackerAtkBelowYourDefenderDefAtEndOfDamageStep) &&
                   DestructionPunchWindowOpen(engine, who, out _);
        }

        // ───────────────────────── Michizure: monsters sent from the field to the GY ─────────────────────────

        /// <summary>
        /// A monster left a Monster Zone and arrived in <paramref name="gyOwner"/>'s GY (by battle, effect, Tribute or
        /// cost). Recorded for that player's Michizure.
        /// </summary>
        public static void NoteMonsterSentFromField(DuelEngine engine, DuelistState gyOwner, CardInstance card)
        {
            if (engine == null || gyOwner == null || card?.Def == null || card.IsToken) return;
            if (!gyOwner.Graveyard.Contains(card)) return;
            // Fatal Abacus (ContinuousTrapsAndSpiritSupport batch): the same field → GY event.
            ContinuousTrapsAndSpiritSupportRuntime.NoteMonsterSentFromField(engine, gyOwner, card);
            var st = StateOf(engine);
            var resolving = engine.IsResolvingChain || engine.Chain.IsResolving;
            var byCost = !resolving && st.CostDepth > 0;
            st.Sent.Add(new SentRecord
            {
                Owner = gyOwner,
                Card = card,
                Turn = engine.TurnNumber,
                Phase = engine.Phase,
                DuringResolution = resolving,
                ByCost = byCost,
                Link = byCost ? engine.Chain.Last : null
            });
        }

        /// <summary>An activation starts paying its costs (TextEffectRuntime): monsters sent now are cost sends.</summary>
        public static void BeginCost(DuelEngine engine)
        {
            if (engine != null) StateOf(engine).CostDepth++;
        }

        /// <summary>The cost payment of <see cref="BeginCost"/> is over.</summary>
        public static void EndCost(DuelEngine engine)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st) && st.CostDepth > 0) st.CostDepth--;
        }

        /// <summary>
        /// An activation finished without a chain being built for it (DuelEngine.ClearIdleChain: a monster ignition
        /// resolves inline, with no response window): its cost sends can no longer be chained to and are dropped.
        /// </summary>
        public static void DropCostSends(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Sent.RemoveAll(r => r.ByCost);
            st.Offered.RemoveAll(r => r.ByCost);
        }

        /// <summary>How many sends are recorded now (<see cref="TieCostSendsToLink"/> marks the ones after it).</summary>
        public static int SentCount(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) ? st.Sent.Count : 0;

        /// <summary>
        /// EngineHygiene batch (v159 audit follow-up): a chain answer paid its cost (Maryokutai's Tribute) in
        /// DuelEngine.TryActivateChainResponse before its Chain Link existed. The sends recorded since
        /// <paramref name="mark"/> are that activation's cost sends, tied to <paramref name="link"/>: Michizure answers them
        /// by chaining to it (also as the activator's own window), never later.
        /// </summary>
        public static void TieCostSendsToLink(DuelEngine engine, int mark, ChainLink link)
        {
            if (engine == null || link == null || !s_state.TryGetValue(engine, out var st)) return;
            for (var i = System.Math.Max(0, mark); i < st.Sent.Count; i++)
            {
                if (st.Sent[i].DuringResolution) continue;
                st.Sent[i].ByCost = true;
                st.Sent[i].Link = link;
            }
        }

        /// <summary>A cost send's activation is still on the chain being built.</summary>
        static bool CostLinkPending(DuelEngine engine, SentRecord r) =>
            r.Link == null ? engine.Chain.HasLinks : engine.Chain.Links.Contains(r.Link) && !r.Link.Resolved;

        public static bool HasSentRecords(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.Sent.Count > 0;

        /// <summary>
        /// The recorded sends can no longer be answered: another Chain Link starts resolving, a queued FLIP effect
        /// resolves, or an attack moves on to the Damage Step.
        /// </summary>
        public static void DropSentRecords(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Sent.Clear();
            st.Offered.Clear();
        }

        static IEnumerable<SentRecord> FreshSends(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || !s_state.TryGetValue(engine, out var st)) yield break;
            foreach (var r in st.Sent)
                if (r.Owner == who && r.Turn == engine.TurnNumber && r.Phase == engine.Phase)
                    yield return r;
        }

        /// <summary>
        /// The idle window for <paramref name="victim"/> (DuelEngine.OfferMonsterSentWindow): the sends it answers. Sends
        /// of an earlier turn or phase were missed and are dropped. False when there is nothing to answer.
        /// </summary>
        public static bool BeginSentWindow(DuelEngine engine, DuelistState victim)
        {
            if (engine == null || victim == null) return false;
            var st = StateOf(engine);
            st.Offered.Clear();
            st.Sent.RemoveAll(r => r.Owner == victim && (r.Turn != engine.TurnNumber || r.Phase != engine.Phase));
            // A cost send is answered only by chaining to its activation (AnswersSendThisChainMade).
            st.Offered.AddRange(st.Sent.Where(r => r.Owner == victim && !r.ByCost));
            return st.Offered.Count > 0;
        }

        /// <summary>The idle window closed (passed, answered or never opened): the sends it offered are gone.</summary>
        public static void EndSentWindow(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            if (st.Offered.Count > 0)
                st.Sent.RemoveAll(r => st.Offered.Contains(r));
            st.Offered.Clear();
        }

        /// <summary>
        /// Activation condition <see cref="MonsterSentFromFieldToYourGy"/>: in <paramref name="who"/>'s idle window, in the
        /// end-of-Damage-Step window (a send in that Damage Step), or in a chain response window of the chain whose
        /// activation made the send (not one made while a chain resolved).
        /// </summary>
        public static bool MonsterSentWindowOpen(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = "Only when a monster is sent from the field to your GY.";
            var pr = engine?.PendingResponse;
            if (pr == null || who == null || pr.Responder != who || !s_state.TryGetValue(engine, out var st))
                return false;
            switch (pr.Timing)
            {
                case ResponseTiming.MonsterSentFromFieldToYourGy:
                    return st.Offered.Exists(r => r.Owner == who) || FreshSends(engine, who).Any(r => !r.ByCost);
                case ResponseTiming.EndOfDamageStep:
                    return FreshSends(engine, who).Any(r => !r.ByCost);
                case ResponseTiming.ChainResponse:
                    return !engine.Chain.IsResolving && !engine.IsResolvingChain &&
                           FreshSends(engine, who).Any(r =>
                               !r.DuringResolution && (!r.ByCost || CostLinkPending(engine, r)));
                default:
                    return false;
            }
        }

        /// <summary>
        /// BattleDestroyedGyRecovery batch (v162), Rope of Life: the monsters <paramref name="who"/>'s open
        /// <see cref="ResponseTiming.MonsterSentFromFieldToYourGy"/> or <see cref="ResponseTiming.EndOfDamageStep"/> window
        /// answers (the same sends <see cref="MonsterSentWindowOpen"/> accepts there; cost sends excluded).
        /// </summary>
        public static List<CardInstance> AnswerableSends(DuelEngine engine, DuelistState who)
        {
            var list = new List<CardInstance>();
            var pr = engine?.PendingResponse;
            if (pr == null || who == null || pr.Responder != who || !s_state.TryGetValue(engine, out var st))
                return list;
            IEnumerable<SentRecord> recs = pr.Timing switch
            {
                ResponseTiming.MonsterSentFromFieldToYourGy =>
                    st.Offered.Where(r => r.Owner == who).Concat(FreshSends(engine, who).Where(r => !r.ByCost)),
                ResponseTiming.EndOfDamageStep => FreshSends(engine, who).Where(r => !r.ByCost),
                _ => Enumerable.Empty<SentRecord>()
            };
            foreach (var r in recs)
                if (r.Card != null && !list.Contains(r.Card))
                    list.Add(r.Card);
            return list;
        }

        /// <summary>
        /// <paramref name="prog"/> is a Michizure-type Trap whose condition holds in this chain response window: it may be
        /// offered even onto the responder's own activation (whose Tribute cost sent the monster).
        /// </summary>
        public static bool AnswersSendThisChainMade(DuelEngine engine, DuelistState who, CompiledCardProgram prog) =>
            engine?.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            HasActivationCondition(prog, MonsterSentFromFieldToYourGy) && MonsterSentWindowOpen(engine, who, out _);

        /// <summary>The program answers the <paramref name="timing"/> window of this batch.</summary>
        public static bool AnswersWindow(CompiledCardProgram prog, ResponseTiming timing)
        {
            // BattleDestroyedGyRecovery batch (v162): Rope of Life answers the same windows, for battle sends only
            // (BattleDestroyedGyRecoveryRuntime.TryActivationCondition).
            if ((timing == ResponseTiming.MonsterSentFromFieldToYourGy || timing == ResponseTiming.EndOfDamageStep) &&
                HasActivationCondition(prog, BattleDestroyedGyRecoveryRuntime.MonsterDestroyedByBattleSentToYourGy))
                return true;
            if (timing == ResponseTiming.MonsterSentFromFieldToYourGy)
                return HasActivationCondition(prog, MonsterSentFromFieldToYourGy);
            if (timing == ResponseTiming.EndOfDamageStep)
                return HasActivationCondition(prog, MonsterSentFromFieldToYourGy) ||
                       HasActivationCondition(prog, AttackerAtkBelowYourDefenderDefAtEndOfDamageStep);
            return false;
        }

        /// <summary>
        /// The program is one of this batch's Traps that answer only a battle window (Destruction Punch, Michizure, Dark
        /// Spirit of the Silent): never legal in an open game state or a window without its event.
        /// </summary>
        public static bool AnswersOnlyBattleWindows(CompiledCardProgram prog) =>
            AnswersWindow(prog, ResponseTiming.EndOfDamageStep) ||
            (prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead));

        /// <summary>
        /// AI: Michizure when it can take an opponent's monster with it (the target auto-pick prefers the opponent's
        /// strongest monster), never to destroy only its own.
        /// </summary>
        public static bool AiWantsMichizure(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!HasActivationCondition(prog, MonsterSentFromFieldToYourGy)) return false;
            if (!MonsterSentWindowOpen(engine, who, out _)) return false;
            var opp = engine.OpponentOf(who);
            return opp != null && opp.MonstersOnField().Any(m => m != null);
        }

        // ───────────────────────── Dark Spirit of the Silent ─────────────────────────

        static List<CardInstance> DarkSpiritCandidates(DuelEngine engine, DuelistState attackingPlayer,
            CardInstance attacker) =>
            attackingPlayer == null
                ? new List<CardInstance>()
                : attackingPlayer.MonstersOnField()
                    .Where(m => m != null && m.FaceUp && !ReferenceEquals(m, attacker) &&
                                !engine.IsDragonTargetProtected(m) &&
                                // EquipLocksAndLeaveField batch: Metalsilver Armor (the responder targets).
                                !EquipLocksAndLeaveFieldRuntime.ShieldedFromTargeting(engine,
                                    engine.OpponentOf(attackingPlayer), m))
                    .ToList();

        /// <summary>
        /// SpellTrapEffects.AttackWindowClauseLegal: Dark Spirit of the Silent answers an opponent's attack during their
        /// turn, with another face-up monster of theirs to target.
        /// </summary>
        public static bool DarkSpiritAnswerLegal(DuelEngine engine)
        {
            var pr = engine?.PendingResponse;
            if (pr == null || pr.Timing != ResponseTiming.AttackDeclared || pr.Responder == null) return false;
            var attackingPlayer = pr.AttackingPlayer ?? engine.DeclaredAttackingPlayer;
            var attacker = pr.Attacker ?? engine.DeclaredAttacker;
            if (attackingPlayer == null || attacker == null || attackingPlayer == pr.Responder) return false;
            if (engine.TurnPlayer == pr.Responder) return false;
            if (!attackingPlayer.TryFindMonster(attacker, out _)) return false;
            // The attacker is a target too (Lua a:IsCanBeEffectTarget(e)).
            if (engine.IsDragonTargetProtected(attacker)) return false;
            // EquipLocksAndLeaveField batch: Metalsilver Armor.
            if (EquipLocksAndLeaveFieldRuntime.ShieldedFromTargeting(engine, pr.Responder, attacker)) return false;
            return DarkSpiritCandidates(engine, attackingPlayer, attacker).Count > 0;
        }

        /// <summary>
        /// Dark Spirit of the Silent resolves with the other monster <paramref name="chosen"/>: the attack is negated
        /// (true); the other monster, if still face-up on the field, is changed to Attack Position if it is in Defense
        /// Position and is remembered for the battle that follows once the chain has resolved.
        /// </summary>
        public static bool ResolveDarkSpirit(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance chosen)
        {
            if (engine == null) return false;
            var st = StateOf(engine);
            st.Replacement = null;
            var attacker = engine.DeclaredAttacker;
            var attackingPlayer = engine.DeclaredAttackingPlayer;
            if (attacker == null || attackingPlayer == null)
            {
                engine.Log($"{source?.Name}: there is no attack to negate — no effect.");
                return false;
            }

            var original = engine.DeclaredAttackTarget;
            engine.Log($"{source?.Name}: {attacker.Name}'s attack is negated.");
            if (chosen == null || ReferenceEquals(chosen, attacker) || !chosen.FaceUp ||
                !attackingPlayer.TryFindMonster(chosen, out _))
            {
                engine.Log($"{source?.Name}: the other monster is no longer face-up on the field — it does not attack.");
                return true;
            }

            if (chosen.Position == BattlePosition.Defense)
            {
                engine.ApplyEffectPositionChange(chosen, BattlePosition.Attack, faceUp: true);
                engine.Log($"{chosen.Name} is changed to Attack Position.");
            }

            st.Replacement = new ReplacementBattle
            {
                AttackingPlayer = attackingPlayer,
                Monster = chosen,
                MonsterEntryStamp = chosen.FieldEntryStamp,
                Target = original,
                TargetEntryStamp = original?.FieldEntryStamp ?? 0,
                Source = source
            };
            return true;
        }

        /// <summary>
        /// DuelEngine.ContinueAfterResponseActivation, after a negated attack: Dark Spirit's monster battles the original
        /// attack target (or attacks directly if there was none). True when that battle started.
        /// </summary>
        public static bool TryStartReplacementBattle(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Replacement == null) return false;
            var r = st.Replacement;
            st.Replacement = null;
            if (engine.GameOver || engine.Phase != DuelPhase.Battle) return false;
            var m = r.Monster;
            var defender = engine.OpponentOf(r.AttackingPlayer);
            if (m == null || m.FieldEntryStamp != r.MonsterEntryStamp || !engine.CanBattleByEffect(r.AttackingPlayer, m))
            {
                engine.Log($"{r.Source?.Name}: {m?.Name ?? "that monster"} cannot attack — no battle.");
                return false;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, m, r.Source))
            {
                engine.Log($"{m.Name} is unaffected by {r.Source?.Name} — no battle.");
                return false;
            }

            if (r.Target != null && (defender == null || !defender.TryFindMonster(r.Target, out _) ||
                                     r.Target.FieldEntryStamp != r.TargetEntryStamp))
            {
                engine.Log($"{r.Source?.Name}: the original attack target is no longer on the field — no battle.");
                return false;
            }

            return engine.RunReplacementBattle(r.AttackingPlayer, m, r.Target);
        }

        /// <summary>A negated attack that started no replacement battle: forget any leftover.</summary>
        public static void ForgetReplacementBattle(DuelEngine engine)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st))
                st.Replacement = null;
        }

        /// <summary>AutoPick: the weakest of the attacker's other face-up monsters battles instead.</summary>
        public static CardInstance AiPickDarkSpirit(DuelEngine engine, List<CardInstance> targets) =>
            targets.OrderBy(t => t.CurrentAtk).First();

        /// <summary>
        /// AI: answer an attack that would destroy its monster or deal 1000+ damage when the opponent has a weaker
        /// face-up monster (lower ATK than the attacker) to battle instead.
        /// </summary>
        public static bool AiWantsDarkSpirit(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine?.PendingResponse == null || card?.Def == null) return false;
            if (FirstClause(card, EffectTiming.AttackDeclared,
                    EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead) == null)
                return false;
            if (!DarkSpiritAnswerLegal(engine)) return false;
            var attacker = engine.PendingResponse.Attacker ?? engine.DeclaredAttacker;
            var target = engine.PendingResponse.AttackTarget ?? engine.DeclaredAttackTarget;
            var harmful = target == null ? attacker.CurrentAtk >= 1000 : !SurvivesAttack(attacker, target);
            if (!harmful) return false;
            var attackingPlayer = engine.PendingResponse.AttackingPlayer ?? engine.DeclaredAttackingPlayer;
            return DarkSpiritCandidates(engine, attackingPlayer, attacker).Exists(m => m.CurrentAtk < attacker.CurrentAtk);
        }

        // ───────────────────────── player choices ─────────────────────────

        /// <summary>
        /// TextEffectRuntime.TryFinishCoinCall hook: the player called Fairy Box's coin. False when the open call is not
        /// this batch's.
        /// </summary>
        public static bool TryFinishCoinCall(DuelEngine engine, bool callHeads)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var pick) || pick.Kind != PickKind.FairyBoxCall)
                return false;
            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            if (TriggerSourceActive(engine, pick.Trigger.Controller, pick.Trigger.Source))
                ResolveFairyBox(engine, pick.Trigger, callHeads, () => ContinueDeclarationTriggers(engine));
            else
            {
                engine.Log($"{pick.Trigger.Source?.Name} is no longer face-up on the field — no effect.");
                ContinueDeclarationTriggers(engine);
            }

            return true;
        }

        /// <summary>
        /// SpellTrapEffects.TryResolveWithTarget hook: the player picked Jam Defender's / Dreamsprite's new monster. False
        /// when the open pick is not this batch's.
        /// </summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var pick)) return false;
            if (pick.Kind == PickKind.FairyBoxCall) return false;
            var chosen = engine.ResolveLegalEffectTarget(target);
            if (chosen == null)
            {
                engine.Log("Choose one of the highlighted monsters.");
                return true;
            }

            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            ok = true;
            if (pick.Kind == PickKind.JamDefenderTarget)
            {
                ResolveJamDefender(engine, pick.Trigger, chosen);
                ContinueDeclarationTriggers(engine);
                return true;
            }

            FinishBattleSwitch(engine, pick.AttackingPlayer, pick.DefendingPlayer, pick.Attacker, pick.Target,
                pick.Flipped, chosen);
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending hook: Jam Defender is declined; Fairy Box's call and Dreamsprite's pick are
        /// mandatory, so CANCEL makes them for the player (Heads / the first legal monster).
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var pick)) return false;
            switch (pick.Kind)
            {
                case PickKind.FairyBoxCall:
                    return TryFinishCoinCall(engine, callHeads: true);
                case PickKind.JamDefenderTarget:
                    s_picks.Remove(pending);
                    engine.ClearPendingActivation();
                    engine.Log($"{pick.Trigger.Source?.Name}: {pick.Trigger.Controller?.Name} does not use its effect.");
                    ContinueDeclarationTriggers(engine);
                    return true;
                default:
                    var first = pending.LegalTargets.FirstOrDefault();
                    if (first == null)
                    {
                        s_picks.Remove(pending);
                        engine.ClearPendingActivation();
                        engine.ContinueBeforeDamageCalculation(pick.AttackingPlayer, pick.DefendingPlayer, pick.Attacker,
                            pick.Target, pick.Flipped);
                        return true;
                    }

                    engine.Log($"{pick.Target?.Name}: the effect is mandatory — {first.Name} is chosen.");
                    return TryResolvePick(engine, first, out _);
            }
        }
    }
}
