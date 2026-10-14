using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "AttackObligations" runtime: the must-attack obligation (ygopro EFFECT_MUST_ATTACK) as one engine rule.
    /// <para>
    /// A monster the turn player controls that "must attack if able" keeps its controller in the Battle Phase while it can
    /// still legally declare an attack (ygopro processor: <c>core.to_m2 = core.to_ep = FALSE</c> while a must-attack monster
    /// is in <c>attackable_cards</c>), and Main Phase 1 cannot skip the Battle Phase while such a monster could attack
    /// (<c>process_idle_command</c>: no End Phase from Main Phase 1). The obligation ends once the monster has used its
    /// attacks or can no longer declare one (no legal target, a lock, an attack cost it cannot pay). DuelEngine asks
    /// <see cref="RefuseLeavingPhase"/> in TryEnterMainPhase2, EndTurn and TryEndTurnSafe, so the UI's End Battle / End Turn
    /// simply become illegal; SimpleAi and DuelStressAgent satisfy the duty through <see cref="TryChooseObligedAttack"/>.
    /// A card effect that ends the Battle Phase (DuelEngine.ForceEndBattlePhase) still ends it.
    /// </para>
    /// <list type="bullet">
    /// <item>Berserk Gorilla: <see cref="EffectActionKind.MustAttackIfAble"/> marker; <see cref="SweepSelfDestroy"/> (each
    /// board refresh) destroys it in face-up Defense Position.</item>
    /// <item>Amazoness Archers: <see cref="ApplyAllOpponentMonstersMustAttack"/>.</item>
    /// <item>Diffusion Wave-Motion: <see cref="ApplyTargetMustAttackAll"/> — attack-all through
    /// SpiritMonstersRuntime.AttacksAllOpponentMonsters (<see cref="GrantsAttackAll"/>), the others' lock through
    /// DuelEngine.ContinuousCannotAttackBlocks (<see cref="AttackForbidden"/>), the battle negation through
    /// NegationMonstersRuntime.BattleNegator (<see cref="BattleNegator"/>).</item>
    /// <item>Staunch Defender: <see cref="ApplyOpponentMustAttackTarget"/> — <see cref="ForbidsAttackTarget"/> /
    /// <see cref="ForbidsDirectAttack"/> in every attack-target check, cleared by <see cref="EndBattlePhase"/>.</item>
    /// </list>
    /// </summary>
    public static class AttackObligationsRuntime
    {
        /// <summary>Amazoness Archers: you control a face-up monster of the series in <see cref="EffectClause.ConditionName"/>.</summary>
        public const string YouControlFaceUpSeriesMonster = "AttackObligations.YouControlFaceUpSeriesMonster";

        /// <summary>
        /// Diffusion Wave-Motion: the opponent controls a monster (printed) and you can still enter your Battle Phase this
        /// turn (Lua s.condition Duel.IsAbleToEnterBP).
        /// </summary>
        public const string OpponentControlsMonsterBeforeBattlePhase = "AttackObligations.OpponentControlsMonsterBeforeBattlePhase";

        /// <summary>
        /// One field presence of a monster for this turn: the effect ends when it leaves the field, is turned face-down, or
        /// the turn ends (ygopro RESETS_STANDARD_PHASE_END).
        /// </summary>
        sealed class Mark
        {
            public DuelEngine Engine;
            public CardInstance Card;
            public int Entry;
            public int FaceDown;
            public int Turn;
            public CardInstance Source;
        }

        /// <summary>Diffusion Wave-Motion: the player's monsters other than that field presence cannot attack this turn.</summary>
        sealed class OthersLock
        {
            public DuelistState Player;
            public CardInstance Exempt;
            public int ExemptEntry;
            public int Turn;
        }

        /// <summary>Staunch Defender: <see cref="Attacker"/> may attack only <see cref="Selected"/>, and must, this Battle Phase.</summary>
        sealed class TargetLock
        {
            public DuelistState Attacker;
            public CardInstance Selected;
            public int SelectedEntry;
            public int Turn;
            public CardInstance Source;
        }

        sealed class State
        {
            public readonly List<Mark> MustAttack = new List<Mark>();
            public readonly List<OthersLock> OthersCannotAttack = new List<OthersLock>();
            public readonly List<TargetLock> TargetLocks = new List<TargetLock>();
            /// <summary>Declarations an autopilot tried and the engine refused this turn (never retried as a duty).</summary>
            public readonly List<(CardInstance Attacker, CardInstance Target, int Turn)> Refused =
                new List<(CardInstance, CardInstance, int)>();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new ConditionalWeakTable<DuelEngine, State>();

        /// <summary>Diffusion Wave-Motion's attack-all grant, keyed by the monster (SpiritMonstersRuntime has no engine there).</summary>
        static readonly ConditionalWeakTable<CardInstance, Mark> s_attackAll = new ConditionalWeakTable<CardInstance, Mark>();

        static State StateOf(DuelEngine engine) => s_state.GetOrCreateValue(engine);

        static bool OnField(DuelEngine engine, CardInstance card) =>
            card != null && ((engine.Player?.TryFindMonster(card, out _) ?? false) ||
                             (engine.Opponent?.TryFindMonster(card, out _) ?? false));

        static bool Live(Mark k)
        {
            var e = k?.Engine;
            var c = k?.Card;
            if (e == null || c == null || e.GameOver || e.TurnNumber != k.Turn) return false;
            if (!c.FaceUp || c.FieldEntryStamp != k.Entry || c.FaceDownStamp != k.FaceDown) return false;
            return OnField(e, c);
        }

        static Mark NewMark(DuelEngine engine, CardInstance card, CardInstance source) => new Mark
        {
            Engine = engine, Card = card, Entry = card.FieldEntryStamp, FaceDown = card.FaceDownStamp,
            Turn = engine.TurnNumber, Source = source
        };

        static bool LockLive(DuelEngine engine, TargetLock l) =>
            l != null && !engine.GameOver && l.Turn == engine.TurnNumber && engine.Phase == DuelPhase.Battle;

        /// <summary>
        /// Staunch Defender's selected monster is still on the field as the same card (ygopro RELATE_TO_EFFECT): the
        /// lock then both forbids every other target and obliges an attack onto it.
        /// </summary>
        static bool SelectedStillAttackable(DuelEngine engine, TargetLock l) =>
            l?.Selected != null && l.Selected.FaceUp && l.Selected.FieldEntryStamp == l.SelectedEntry &&
            (engine.ControllerOf(l.Selected)?.TryFindMonster(l.Selected, out _) ?? false);

        /// <summary>A face-up, non-negated monster whose fully compiled program has this continuous marker.</summary>
        static EffectClause LiveClause(DuelEngine engine, CardInstance m, EffectActionKind action)
        {
            if (m?.Def == null || !m.FaceUp || m.IsNegated) return null;
            if (engine != null && engine.IsSummonPending(m)) return null;
            var prog = CompiledEffectCache.GetOrCompile(m);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
        }

        // ───────────────────────────── the obligation ─────────────────────────────

        /// <summary>
        /// <paramref name="m"/>, a face-up monster <paramref name="who"/> controls, must attack if able (Berserk Gorilla's own
        /// effect, Amazoness Archers / Diffusion Wave-Motion this turn, Staunch Defender this Battle Phase).
        /// </summary>
        public static bool MustAttack(DuelEngine engine, DuelistState who, CardInstance m)
        {
            if (engine == null || who == null || m == null || !m.FaceUp) return false;
            if (!who.TryFindMonster(m, out _)) return false;
            if (LiveClause(engine, m, EffectActionKind.MustAttackIfAble) != null) return true;
            if (!s_state.TryGetValue(engine, out var st)) return false;
            if (st.MustAttack.Exists(k => ReferenceEquals(k.Card, m) && Live(k))) return true;
            // Staunch Defender's lock is a duty only while the selected monster is still on the field to attack:
            // once it left, every target is forbidden (EFFECT_MUST_ATTACK_MONSTER), so the obligation is over and
            // the Battle Phase may end — otherwise the duty could never be met and the turn would stall.
            return st.TargetLocks.Exists(l => LockLive(engine, l) && ReferenceEquals(l.Attacker, who) &&
                                             SelectedStillAttackable(engine, l));
        }

        /// <summary>
        /// The declarations <paramref name="attacker"/> could make right now — DuelEngine.TryAttack's legality: CanAttack,
        /// the declaration costs, the Ring of Magnetism / Magnetism host, the direct-attack rules and each target rule.
        /// </summary>
        public static List<CardInstance> AttackOptions(DuelEngine engine, DuelistState who, CardInstance attacker,
            out bool direct)
        {
            direct = false;
            var list = new List<CardInstance>();
            if (engine == null || who == null || attacker == null || !engine.CanAttack(who, attacker)) return list;
            if (!attacker.AttackLpCostPaidThisDeclaration)
            {
                // TextEffectRuntime.PayAttackDeclarationCost refuses these after CanAttack.
                var mill = TextEffectRuntime.AttackMillCost(engine, who);
                if (mill > 0 && (who.Deck == null || who.Deck.Count < mill)) return list;
                var lp = TextEffectRuntime.DeclareAttackLpCost(attacker);
                if (lp > 0 && who.LifePoints < lp) return list;
            }

            var opp = engine.OpponentOf(who);
            if (opp == null) return list;
            var magnet = engine.MagnetismHosts(who);
            var forced = TextEffectRuntime.ForcedAttackTargets(opp);
            direct = magnet.Count == 0 && forced.Count == 0 && engine.CanAttackDirectly(who, attacker);
            foreach (var t in opp.MonstersOnField())
            {
                if (t == null) continue;
                // MustAttackDirectlyThisTurn, "cannot be attacked", Magnetism, Ring of Magnetism, Staunch Defender.
                if (!RedirectAndReplaceRuntime.AttackerCouldAttack(engine, who, attacker, t)) continue;
                if (ToonsRuntime.ForbidsAttackTarget(engine, attacker, t)) continue;
                if (SpiritMonstersRuntime.ForbidsAttackTarget(engine, attacker, t)) continue;
                list.Add(t);
            }

            return list;
        }

        static bool Refused(State st, DuelEngine engine, CardInstance attacker, CardInstance target) =>
            st != null && st.Refused.Exists(r => r.Turn == engine.TurnNumber && ReferenceEquals(r.Attacker, attacker) &&
                                                 ReferenceEquals(r.Target, target));

        /// <summary>The obligated monster can still declare an attack it has not been refused.</summary>
        static bool CanStillAttack(DuelEngine engine, DuelistState who, CardInstance m)
        {
            var targets = AttackOptions(engine, who, m, out var direct);
            s_state.TryGetValue(engine, out var st);
            if (direct && !Refused(st, engine, m, null)) return true;
            return targets.Exists(t => !Refused(st, engine, m, t));
        }

        /// <summary>
        /// Main Phase 1: the monster could attack in the Battle Phase (ygopro is_capable_attack: face-up Attack Position,
        /// attacks left, no attack lock — the same locks DuelEngine.CanAttack reads). Its targets are checked once the Battle
        /// Phase starts.
        /// </summary>
        static bool CapableOfAttacking(DuelEngine engine, DuelistState who, CardInstance m) =>
            m.FaceUp && m.Position == BattlePosition.Attack &&
            m.AttacksDeclaredThisTurn < engine.MaxAttacksThisTurn(m) &&
            !who.CannotDeclareAttackThisTurn &&
            !(m.CannotAttackDuringTurnNumber > 0 && m.CannotAttackDuringTurnNumber == engine.TurnNumber) &&
            !(engine.OpponentHasSwordsOfRevealingLight(who) &&
              !MonsterNegationAndControlLocksRuntime.IgnoresSpellAttackLock(engine, who, m)) &&
            !engine.ContinuousCannotAttackBlocks(who, m) &&
            !TextEffectRuntime.AttackForbiddenByEffect(engine, m);

        /// <summary>
        /// The monster that keeps <paramref name="who"/> from leaving the current phase: in the Battle Phase, an obligated
        /// monster that can still declare an attack; in Main Phase 1 of a turn with a Battle Phase, an obligated monster that
        /// could attack. Null when the phase may end.
        /// </summary>
        public static CardInstance BlockingMonster(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || engine.GameOver || !ReferenceEquals(engine.TurnPlayer, who)) return null;
            if (engine.Phase == DuelPhase.Battle)
                return who.MonstersOnField().FirstOrDefault(m =>
                    m != null && MustAttack(engine, who, m) && CanStillAttack(engine, who, m));
            if (engine.Phase == DuelPhase.Main1 && engine.CanConductBattlePhase)
                return who.MonstersOnField().FirstOrDefault(m =>
                    m != null && MustAttack(engine, who, m) && CapableOfAttacking(engine, who, m));
            return null;
        }

        /// <summary>
        /// DuelEngine.TryEnterMainPhase2 / EndTurn / TryEndTurnSafe: true (and a log line) when an obligated monster keeps
        /// <paramref name="who"/> in this phase.
        /// </summary>
        public static bool RefuseLeavingPhase(DuelEngine engine, DuelistState who)
        {
            var m = BlockingMonster(engine, who);
            if (m == null) return false;
            engine.Log(engine.Phase == DuelPhase.Battle
                ? $"{m.Name} must attack if able — {who.Name} cannot end the Battle Phase yet."
                : $"{m.Name} must attack if able — {who.Name} must conduct the Battle Phase.");
            return true;
        }

        /// <summary>
        /// An autopilot (SimpleAi, DuelStressAgent) tried this declaration and the engine refused it: it is not offered as a
        /// duty again this turn, so the turn always ends (a monster whose every declaration is refused cannot attack).
        /// </summary>
        public static void NoteRefused(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null) return;
            StateOf(engine).Refused.Add((attacker, target, engine.TurnNumber));
        }

        /// <summary>
        /// The next attack an obligated monster of <paramref name="who"/> must declare (Battle Phase only): a direct attack
        /// when it may, otherwise the target with the smallest known disadvantage (face-down monsters judged by
        /// <see cref="AiHeuristicPolicy.FaceDownDefEstimate"/>, never by their hidden DEF).
        /// </summary>
        public static bool TryChooseObligedAttack(DuelEngine engine, DuelistState who, out CardInstance attacker,
            out CardInstance target)
        {
            attacker = null;
            target = null;
            if (engine == null || who == null || engine.GameOver || engine.Phase != DuelPhase.Battle ||
                !ReferenceEquals(engine.TurnPlayer, who))
                return false;
            s_state.TryGetValue(engine, out var st);
            foreach (var m in who.MonstersOnField().Where(x => x != null).OrderByDescending(x => x.CurrentAtk).ToList())
            {
                if (!MustAttack(engine, who, m)) continue;
                var targets = AttackOptions(engine, who, m, out var direct);
                if (direct && !Refused(st, engine, m, null))
                {
                    attacker = m;
                    return true;
                }

                var atk = m.CurrentAtk;
                CardInstance best = null;
                var bestScore = int.MaxValue;
                foreach (var t in targets)
                {
                    if (Refused(st, engine, m, t)) continue;
                    var stat = !t.FaceUp
                        ? AiHeuristicPolicy.FaceDownDefEstimate
                        : t.Position == BattlePosition.Attack ? t.CurrentAtk : t.CurrentDef;
                    var score = stat - atk;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    best = t;
                }

                if (best == null) continue;
                attacker = m;
                target = best;
                return true;
            }

            return false;
        }

        // ───────────────────────────── attack locks and target rules ─────────────────────────────

        /// <summary>DuelEngine.ContinuousCannotAttackBlocks: Diffusion Wave-Motion's "other monsters you control cannot attack".</summary>
        public static bool AttackForbidden(DuelEngine engine, DuelistState attackerController, CardInstance attacker)
        {
            if (engine == null || attackerController == null || attacker == null) return false;
            if (!s_state.TryGetValue(engine, out var st)) return false;
            return st.OthersCannotAttack.Exists(l =>
                l.Turn == engine.TurnNumber && ReferenceEquals(l.Player, attackerController) &&
                !(ReferenceEquals(l.Exempt, attacker) && attacker.FieldEntryStamp == l.ExemptEntry));
        }

        /// <summary>
        /// Staunch Defender: while its lock binds the attacker's controller, the attacker may attack only the selected
        /// monster (ygopro EFFECT_MUST_ATTACK_MONSTER) — no other monster, and none at all once the selected one left the field.
        /// </summary>
        public static bool ForbidsAttackTarget(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null) return false;
            if (!s_state.TryGetValue(engine, out var st) || st.TargetLocks.Count == 0) return false;
            var who = engine.ControllerOf(attacker);
            if (who == null) return false;
            foreach (var l in st.TargetLocks)
            {
                if (!LockLive(engine, l) || !ReferenceEquals(l.Attacker, who)) continue;
                if (!ReferenceEquals(target, l.Selected) || target.FieldEntryStamp != l.SelectedEntry) return true;
            }

            return false;
        }

        /// <summary>Staunch Defender: an attack must target the selected monster, never the player.</summary>
        public static bool ForbidsDirectAttack(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            return s_state.TryGetValue(engine, out var st) &&
                   st.TargetLocks.Exists(l => LockLive(engine, l) && ReferenceEquals(l.Attacker, who));
        }

        /// <summary>
        /// DuelEngine.MaxAttacksThisTurn: how many attacks <paramref name="attacker"/> is granted this turn by
        /// Diffusion Wave-Motion (EFFECT_ATTACK_ALL — every monster its opponent controls once each), or 1 when that
        /// grant does not apply (so the caller's ordinary cap stands).
        /// </summary>
        public static int RequiredAttackCount(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker == null || !GrantsAttackAll(attacker)) return 1;
            var who = engine.ControllerOf(attacker);
            var opp = who != null ? engine.OpponentOf(who) : null;
            if (opp == null) return 1;

            // "must attack all monsters your opponent controls once each": the monsters this turn's declarations
            // already covered (a monster destroyed by such an attack leaves the field, so it must still count), plus
            // the ones still on the field. Read from the append-only history, never from live board crumbs.
            var onField = opp.MonstersOnField().Where(m => m != null).ToList();
            var covered = engine.GameEvents.Events
                .Where(e => e.Kind == DuelGameEventKind.DeclaredAttack && e.TurnNumber == engine.TurnNumber &&
                            ReferenceEquals(e.Card, attacker) && e.Related != null)
                .Select(e => e.Related).Distinct().ToList();
            return Math.Max(1, covered.Count + onField.Count(m => !covered.Contains(m)));
        }

        /// <summary>SpiritMonstersRuntime.AttacksAllOpponentMonsters: Diffusion Wave-Motion's attack-all grant (EFFECT_ATTACK_ALL).</summary>
        public static bool GrantsAttackAll(CardInstance attacker) =>
            attacker != null && s_attackAll.TryGetValue(attacker, out var k) && Live(k);

        /// <summary>
        /// NegationMonstersRuntime.BattleNegator: <paramref name="battler"/> attacks under Diffusion Wave-Motion, so the effects
        /// of <paramref name="other"/>, destroyed by that battle, cannot activate and are negated. Returns the Spell (or the
        /// battler) for the log; null otherwise.
        /// </summary>
        public static CardInstance BattleNegator(DuelEngine engine, CardInstance battler, CardInstance other)
        {
            if (engine == null || battler == null || other == null) return null;
            if (!s_attackAll.TryGetValue(battler, out var k) || !ReferenceEquals(k.Engine, engine) || !Live(k)) return null;
            return k.Source ?? battler;
        }

        /// <summary>DuelEngine.EndBattlePhase: Staunch Defender's lock ends with the Battle Phase (RESET_PHASE|PHASE_BATTLE).</summary>
        public static void EndBattlePhase(DuelEngine engine)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st))
                st.TargetLocks.Clear();
        }

        // ───────────────────────────── resolutions ─────────────────────────────

        /// <summary>
        /// Amazoness Archers: every monster the opponent controls that is not in face-up Attack Position is changed to it
        /// (FLIP effects are not activated); each face-up monster they control then loses ATK (until it leaves the field) and
        /// must attack this turn, if able.
        /// </summary>
        public static void ApplyAllOpponentMonstersMustAttack(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null || clause == null) return;
            foreach (var m in opp.MonstersOnField().ToList())
            {
                if (m == null || (m.FaceUp && m.Position == BattlePosition.Attack)) continue;
                TextEffectRuntime.SetFaceUpPosition(engine, m, BattlePosition.Attack, suppressFlip: true);
            }

            var st = StateOf(engine);
            var loss = Math.Abs(clause.Amount);
            var hit = 0;
            foreach (var m in opp.MonstersOnField().ToList())
            {
                if (m == null || !m.FaceUp) continue;
                m.LingeringAtkModifier -= loss;
                st.MustAttack.Add(NewMark(engine, m, source));
                hit++;
            }

            engine.Log(hit == 0
                ? $"{source?.Name ?? "Effect"}: {opp.Name} controls no face-up monster."
                : $"{source?.Name ?? "Effect"}: {opp.Name}'s monsters lose {loss} ATK and must attack this turn, if able.");
            engine.NotifyPublic();
        }

        /// <summary>
        /// Diffusion Wave-Motion: the target (still a face-up monster you control) must attack every monster the opponent
        /// controls once each this turn; the controller's other monsters cannot attack this turn; monsters it destroys by
        /// battle have their effects negated.
        /// </summary>
        public static void ApplyTargetMustAttackAll(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null) return;
            if (target == null || !who.TryFindMonster(target, out _) || !target.FaceUp)
            {
                engine.Log($"{source?.Name ?? "Card"}: the target is no longer a face-up monster you control — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var st = StateOf(engine);
            st.OthersCannotAttack.Add(new OthersLock
            {
                Player = who, Exempt = target, ExemptEntry = target.FieldEntryStamp, Turn = engine.TurnNumber
            });
            var mark = NewMark(engine, target, source);
            st.MustAttack.Add(mark);
            s_attackAll.Remove(target);
            s_attackAll.Add(target, mark);
            engine.Log($"{source?.Name ?? "Card"}: {target.Name} must attack every monster {engine.OpponentOf(who)?.Name} " +
                       "controls once each this turn; your other monsters cannot attack.");
            engine.NotifyPublic();
        }

        /// <summary>
        /// Staunch Defender: until the end of this Battle Phase the opponent may attack only the target, and each of their
        /// monsters must attack it, if able.
        /// </summary>
        public static void ApplyOpponentMustAttackTarget(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null) return;
            if (target == null || !who.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name ?? "Card"}: the selected monster is no longer on your field — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var opp = engine.OpponentOf(who);
            StateOf(engine).TargetLocks.Add(new TargetLock
            {
                Attacker = opp, Selected = target, SelectedEntry = target.FieldEntryStamp, Turn = engine.TurnNumber,
                Source = source
            });
            engine.Log($"{source?.Name ?? "Card"}: this Battle Phase {opp?.Name} can only attack {target.Name}, and must " +
                       "attack it with every monster that can.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Berserk Gorilla: self-destruction ─────────────────────────────

        static bool s_sweeping;

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: a face-up monster with <see cref="EffectActionKind.SelfDestroyWhileFaceUpDefense"/>
        /// in Defense Position is destroyed (ygopro EFFECT_SELF_DESTROY; not during damage calculation).
        /// </summary>
        public static void SweepSelfDestroy(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver || s_sweeping) return;
            if (engine.PendingActivation != null) return;
            if (engine.BattleStep == BattleStep.DamageStep &&
                (engine.DamageSubStep == DamageSubStep.Start ||
                 engine.DamageSubStep == DamageSubStep.BeforeDamageCalculation ||
                 engine.DamageSubStep == DamageSubStep.DamageCalculation))
                return;
            var doomed = new List<(DuelistState Side, CardInstance Card)>();
            foreach (var side in new[] { engine.TurnPlayer ?? engine.Player, engine.OpponentOf(engine.TurnPlayer ?? engine.Player) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null || m.Position != BattlePosition.Defense) continue;
                    if (LiveClause(engine, m, EffectActionKind.SelfDestroyWhileFaceUpDefense) != null)
                        doomed.Add((side, m));
                }
            }

            if (doomed.Count == 0) return;
            s_sweeping = true;
            try
            {
                foreach (var (side, card) in doomed)
                {
                    if (engine.GameOver) return;
                    if (!side.TryFindMonster(card, out _) || !card.FaceUp || card.Position != BattlePosition.Defense) continue;
                    engine.Log($"{card.Name} is in face-up Defense Position — it is destroyed.");
                    TextEffectRuntime.DestroyCard(engine, card, card, byPlayer: side);
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }

        // ───────────────────────────── AI ─────────────────────────────

        /// <summary>A monster's stat in battle as the attack target: ATK in Attack Position, DEF in Defense Position.</summary>
        static int BattleStat(CardInstance m) => m.Position == BattlePosition.Attack ? m.CurrentAtk : m.CurrentDef;

        static bool HasAction(CardInstance card, EffectActionKind action)
        {
            var prog = card?.Def == null ? null : CompiledEffectCache.GetOrCompile(card.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        /// <summary>
        /// SpellTrapEffects.AiAutoRespond (an opponent's attack declaration): Amazoness Archers always (every attacker loses
        /// ATK); Staunch Defender when a face-up monster of the AI's beats every face-up Attack Position monster of the
        /// attacker's (the monster <see cref="TryAutoPick"/> selects).
        /// </summary>
        public static bool AiWantsAttackResponse(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card == null) return false;
            if (HasAction(card, EffectActionKind.OpponentMonstersToFaceUpAttackLoseAtkMustAttack)) return true;
            if (!HasAction(card, EffectActionKind.OpponentAttacksOnlyTargetAndMustAttackIt)) return false;
            var best = who.MonstersOnField().Where(m => m != null && m.FaceUp).OrderByDescending(BattleStat).FirstOrDefault();
            var opp = engine.OpponentOf(who);
            if (best == null || opp == null) return false;
            var attackers = opp.MonstersOnField().Where(m => m != null && m.FaceUp && m.Position == BattlePosition.Attack)
                .ToList();
            return attackers.Count > 0 && attackers.All(a => BattleStat(best) > a.CurrentAtk);
        }

        /// <summary>TextEffectRuntime.AutoPick: Staunch Defender's best battler; Diffusion Wave-Motion's strongest attacker.</summary>
        public static bool TryAutoPick(EffectClause c, List<CardInstance> targets, out CardInstance pick)
        {
            pick = null;
            if (c == null || targets == null || targets.Count == 0) return false;
            if (c.Action == EffectActionKind.OpponentAttacksOnlyTargetAndMustAttackIt)
                pick = targets.OrderByDescending(BattleStat).First();
            else if (c.Action == EffectActionKind.TargetMustAttackAllOthersCannotAttack)
                pick = targets.OrderByDescending(t => t.CurrentAtk).First();
            return pick != null;
        }

        // ───────────────────────────── activation conditions and target filters ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case YouControlFaceUpSeriesMonster:
                    ok = who != null && !string.IsNullOrEmpty(c.ConditionName) && who.MonstersOnField().Any(m =>
                        m?.Def != null && m.FaceUp && !engine.IsSummonPending(m) &&
                        TextEffectRuntime.CardMatchesSeries(m.Def, c.ConditionName));
                    reason = $"You must control a face-up \"{c.ConditionName}\" monster.";
                    return true;
                case OpponentControlsMonsterBeforeBattlePhase:
                {
                    var opp = engine.OpponentOf(who);
                    ok = opp != null && opp.MonsterCount > 0 && ReferenceEquals(engine.TurnPlayer, who) &&
                         engine.CanConductBattlePhase;
                    reason = "Only while your opponent controls a monster, before a Battle Phase you can still conduct.";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// SpellTrapEffects.AttackWindowClauseLegal (the attack-declaration window lists only cards that can activate):
        /// Amazoness Archers' face-up series monster; Staunch Defender's face-up monster of the responder's to target.
        /// </summary>
        public static bool AttackAnswerLegal(DuelEngine engine, EffectClause c)
        {
            var who = engine?.PendingResponse?.Responder;
            if (c == null || who == null) return true;
            if (c.ActivationCondition == YouControlFaceUpSeriesMonster)
                return TryActivationCondition(engine, who, null, c, out var ok, out _) && ok;
            if (c.Action == EffectActionKind.OpponentAttacksOnlyTargetAndMustAttackIt)
                return TextEffectRuntime.CollectTargets(engine, who, c, null).Count > 0;
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.CollectTargets: Diffusion Wave-Motion — a face-up monster of Level <see cref="EffectClause.Amount"/>
        /// or higher (and the clause's Type); Staunch Defender — a face-up monster.
        /// </summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null) return;
            if (c.Action == EffectActionKind.TargetMustAttackAllOthersCannotAttack)
                list.RemoveAll(t => t?.Def == null || !t.FaceUp || !t.Def.IsMonster || t.Level < Math.Max(1, c.Amount) ||
                                    (!string.IsNullOrEmpty(c.RaceFilter) &&
                                     (t.Race == null || !ClassicEraTemplates.RaceMatches(t.Race, c.RaceFilter))));
            else if (c.Action == EffectActionKind.OpponentAttacksOnlyTargetAndMustAttackIt)
                list.RemoveAll(t => t?.Def == null || !t.FaceUp);
        }

        // ───────────────────────── Battle negated effects ─────────────────────────

        /// <summary>
        /// DuelEngine's EVENT_BATTLED: Diffusion Wave-Motion. A monster it destroys by battle with its granted
        /// attacker cannot activate its effects ("The effects of monsters destroyed by these attacks cannot
        /// activate and are negated"). The grant lives in <see cref="s_attackAll"/>, so the Spell being in the
        /// Graveyard does not matter — <see cref="Live"/> keeps it for the turn.
        /// </summary>
        public static void NoteBattled(DuelEngine engine, DuelistState who, DuelistState opp,
            CardInstance attacker, CardInstance targetOrNull, bool destroyDefender)
        {
            if (engine == null || !destroyDefender || targetOrNull?.Def == null || attacker == null) return;
            var negator = BattleNegator(engine, attacker, targetOrNull);
            if (negator == null) return;
            targetOrNull.EffectsNegatedAfterBattle = true;
            engine.Log($"{negator.Name}: the effects of {targetOrNull.Name} are negated (destroyed by battle with " +
                       $"{attacker.Name}).");
        }
    }
}
