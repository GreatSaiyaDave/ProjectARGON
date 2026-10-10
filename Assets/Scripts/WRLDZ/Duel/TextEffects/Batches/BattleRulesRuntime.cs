using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleRules" (v161) runtime (atoms: <see cref="BattleRulesAtoms"/>).
    /// <list type="bullet">
    /// <item><b>Cave Dragon</b>: <see cref="NormalSummonForbiddenByOwnText"/> (SummonProcedures.CheckNormalOrTribute) and
    /// <see cref="AttackForbidden"/> (DuelEngine.ContinuousCannotAttackBlocks).</item>
    /// <item><b>Thunder Nyan Nyan</b>: <see cref="SweepSelfDestroy"/> on each board refresh (FieldSpellEffects.RefreshBoard).</item>
    /// <item><b>Array of Revealing Light</b>: the Type is declared as the Field Spell is activated (Lua AnnounceRace in
    /// the target step): <see cref="BeginFieldSpellDeclaration"/> (DuelEngine.TryActivateSpellTrapCore: a human declares
    /// first, then the pick re-enters the activation) and <see cref="OnFieldSpellPlaced"/> (DuelEngine.TryPlaceFieldSpell:
    /// the declaration is recorded on the card; the AI declares there, and a direct placement asks a human there). The
    /// declared-Type ContinuousCannotAttack clause with RequiresSummonedOrFlippedThisTurn is read by
    /// DuelEngine.ContinuousCannotAttackBlocks.</item>
    /// <item><b>After the Struggle</b>: <see cref="ArmAfterTheStruggle"/> at resolution, <see cref="NoteBattled"/> after each
    /// damage calculation, <see cref="EndBattlePhase"/> in DuelEngine.EndBattlePhase.</item>
    /// <item><b>The Secret of the Bandit</b>: <see cref="ArmBandit"/> at resolution, <see cref="OnBattleDamage"/> from
    /// TextEffectRuntime.NotifyBattleDamageInflicted.</item>
    /// <item><b>Patrician of Darkness</b>: <see cref="TryDefenderChoosesTarget"/> in DuelEngine.TryAttack; a human defender
    /// picks through the target sheet (<see cref="TryResolvePick"/> / <see cref="TryCancelPick"/>).</item>
    /// </list>
    /// </summary>
    public static class BattleRulesRuntime
    {
        /// <summary>One field presence of a monster: the flag resets when it leaves the field or is turned face-down
        /// (ygopro RESETS_STANDARD: RESET_LEAVE / RESET_TOFIELD / RESET_TURN_SET).</summary>
        sealed class Mark
        {
            public CardInstance Monster;
            public int EntryStamp;
            public int FaceDownStamp;
            public int Turn;
        }

        sealed class BanditWatch
        {
            public Mark Mark;
            public DuelistState Activator;
            public CardInstance Source;
        }

        sealed class PatricianChoice
        {
            public CardInstance Attacker;
            public CardInstance Target;
            public int Turn;
            public int Declarations;
        }

        enum PickKind
        {
            /// <summary>Patrician of Darkness: the defender picks the attack target.</summary>
            PatricianTarget,
            /// <summary>Array of Revealing Light: a human declares the Type before the Field Spell is activated.</summary>
            FieldSpellDeclarationBeforeActivation,
            /// <summary>Array of Revealing Light placed straight in the Field Zone (AR disk): the Type is declared now.</summary>
            FieldSpellDeclarationOnField
        }

        sealed class Pick
        {
            public PickKind Kind;
            public DuelistState AttackingPlayer;
            public DuelistState DefendingPlayer;
            public CardInstance Attacker;
            public CardInstance Source;
        }

        sealed class State
        {
            /// <summary>After the Struggle: the players whose resolved copy waits for this turn's Battle Phase end.</summary>
            public readonly List<(DuelistState Activator, CardInstance Source, int Turn)> StruggleArmed = new();
            public readonly List<Mark> Battled = new();
            public readonly List<BanditWatch> Bandits = new();
            public PatricianChoice Chosen;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, Pick> s_picks = new();
        /// <summary>Array of Revealing Light: the Type token a human declared before the activation re-entered.</summary>
        static readonly ConditionalWeakTable<CardInstance, CardInstance> s_declaredBeforeActivation = new();
        [ThreadStatic] static bool s_sweeping;

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static Mark MarkOf(DuelEngine engine, CardInstance m) => new()
        {
            Monster = m,
            EntryStamp = m.FieldEntryStamp,
            FaceDownStamp = m.FaceDownStamp,
            Turn = engine.TurnNumber
        };

        /// <summary>The marked presence is still on the field, face-up since the mark, and the mark is this turn's.</summary>
        static bool Live(DuelEngine engine, Mark k) =>
            k?.Monster != null && k.Turn == engine.TurnNumber && k.Monster.FieldEntryStamp == k.EntryStamp &&
            k.Monster.FaceDownStamp == k.FaceDownStamp && engine.ControllerOf(k.Monster) != null &&
            engine.ControllerOf(k.Monster).TryFindMonster(k.Monster, out _);

        /// <summary>A face-up monster on the field whose FullyCompiled program applies a clause of this action.</summary>
        static EffectClause ActiveClause(DuelEngine engine, CardInstance m, EffectActionKind action)
        {
            if (m?.Def == null || !m.FaceUp || m.IsNegated) return null;
            if (engine != null && engine.IsSummonPending(m)) return null;
            var prog = CompiledEffectCache.GetOrCompile(m.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
        }

        // ───────────────────────── Cave Dragon ─────────────────────────

        /// <summary>
        /// SummonProcedures.CheckNormalOrTribute (Normal or Tribute Summon only, never a Set): "Cannot be Normal
        /// Summoned while you control a monster." — <paramref name="who"/> controls any monster, face-up or face-down.
        /// </summary>
        public static bool NormalSummonForbiddenByOwnText(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = null;
            if (who == null || card?.Def == null || !card.Def.IsMonster) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            if (!prog.ClauseList.Exists(c =>
                    c != null && c.Action == EffectActionKind.CannotBeNormalSummonedWhileControllerHasMonster))
                return false;
            if (!who.MonstersOnField().Any(m => m != null)) return false;
            reason = $"{card.Name} cannot be Normal Summoned while you control a monster (it can still be Set).";
            return true;
        }

        /// <summary>
        /// DuelEngine.ContinuousCannotAttackBlocks: "This card cannot declare an attack unless you control another
        /// X-Type monster." — no other face-up monster of that (live) Type on its controller's field.
        /// </summary>
        public static bool AttackForbidden(DuelEngine engine, DuelistState controller, CardInstance attacker)
        {
            if (engine == null || attacker == null) return false;
            var c = ActiveClause(engine, attacker, EffectActionKind.CannotAttackUnlessControllerHasAnotherFaceUpRace);
            if (c == null || string.IsNullOrEmpty(c.RaceFilter)) return false;
            var side = engine.ControllerOf(attacker) ?? controller;
            if (side == null) return true;
            return !side.MonstersOnField().Any(m =>
                m != null && !ReferenceEquals(m, attacker) && m.FaceUp &&
                ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter));
        }

        // ───────────────────────── Thunder Nyan Nyan ─────────────────────────

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (after the auras): a face-up, non-negated monster with
        /// <see cref="EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute"/> is destroyed (by its own effect)
        /// while its controller controls a face-up monster whose live Attribute is not the printed one. Not during a
        /// pick, nor in the Damage Step before damage calculation is over.
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
            var doomed = new List<(DuelistState Side, CardInstance Card, string Attribute)>();
            foreach (var side in new[] { engine.TurnPlayer ?? engine.Player, engine.OpponentOf(engine.TurnPlayer ?? engine.Player) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    var c = ActiveClause(engine, m, EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute);
                    if (c == null || string.IsNullOrEmpty(c.AttributeFilter)) continue;
                    var other = side.MonstersOnField().FirstOrDefault(o =>
                        o != null && o.FaceUp && !engine.IsSummonPending(o) &&
                        !string.Equals(o.Attribute, c.AttributeFilter, StringComparison.OrdinalIgnoreCase));
                    if (other != null) doomed.Add((side, m, other.Attribute));
                }
            }

            if (doomed.Count == 0) return;
            s_sweeping = true;
            try
            {
                foreach (var (side, card, attribute) in doomed)
                {
                    if (engine.GameOver) return;
                    if (!side.TryFindMonster(card, out _) || !card.FaceUp) continue;
                    engine.Log($"{card.Name}: {side.Name} controls a non-{CardAttr(card)} ({attribute}) monster — it is destroyed.");
                    TextEffectRuntime.DestroyCard(engine, card, card, byPlayer: side);
                }
            }
            finally
            {
                s_sweeping = false;
            }
        }

        static string CardAttr(CardInstance card)
        {
            var c = ActiveClauseIgnoringState(card, EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute);
            return c?.AttributeFilter ?? "?";
        }

        static EffectClause ActiveClauseIgnoringState(CardInstance m, EffectActionKind action)
        {
            var prog = m?.Def == null ? null : CompiledEffectCache.GetOrCompile(m.Def);
            return prog?.ClauseList.FirstOrDefault(c => c != null && c.Action == action);
        }

        // ───────────────────────── After the Struggle ─────────────────────────

        /// <summary>Resolution: for the rest of this turn, the monsters that go through damage calculation are flagged,
        /// and the flagged ones still on the field are destroyed at the end of the Battle Phase.</summary>
        public static void ArmAfterTheStruggle(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            StateOf(engine).StruggleArmed.Add((who, source, engine.TurnNumber));
            engine.Log($"{source?.Name ?? "After the Struggle"}: every monster involved in damage calculation this turn " +
                       "is destroyed at the end of the Battle Phase.");
        }

        /// <summary>DuelEngine damage step, right after damage calculation (Lua EVENT_BATTLED): flag both battlers.</summary>
        public static void NoteBattled(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.StruggleArmed.RemoveAll(a => a.Turn != engine.TurnNumber);
            st.Battled.RemoveAll(k => k.Turn != engine.TurnNumber);
            if (st.StruggleArmed.Count == 0) return;
            foreach (var m in new[] { attacker, target })
            {
                if (m == null || engine.ControllerOf(m) == null) continue;
                if (st.Battled.Exists(k => Live(engine, k) && ReferenceEquals(k.Monster, m))) continue;
                st.Battled.Add(MarkOf(engine, m));
            }
        }

        /// <summary>DuelEngine.EndBattlePhase: each armed copy destroys the flagged monsters still on the field, once.</summary>
        public static void EndBattlePhase(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st)) return;
            var armed = st.StruggleArmed.Where(a => a.Turn == engine.TurnNumber).ToList();
            st.StruggleArmed.Clear();
            if (armed.Count == 0)
            {
                st.Battled.Clear();
                return;
            }

            var doomed = st.Battled.Where(k => Live(engine, k)).Select(k => k.Monster).Distinct().ToList();
            st.Battled.Clear();
            var by = armed[0].Activator;
            var source = armed[0].Source;
            if (doomed.Count == 0)
            {
                engine.Log("After the Struggle: no monster that battled this turn is still on the field.");
                return;
            }

            engine.Log($"After the Struggle: {string.Join(", ", doomed.Select(m => m.FaceUp ? m.Name : "a face-down monster"))} " +
                       "battled this turn — destroyed.");
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var m in doomed)
                {
                    if (engine.GameOver) return;
                    if (engine.ControllerOf(m) == null) continue;
                    TextEffectRuntime.DestroyCard(engine, m, source, byPlayer: by);
                }
            });
            engine.NotifyPublic();
        }

        // ───────────────────────── The Secret of the Bandit ─────────────────────────

        /// <summary>
        /// Resolution with the selected monster: if it is still face-up on the field (and not already watched by a
        /// copy this turn: the Lua flag check), every battle damage it inflicts to <paramref name="who"/>'s opponent
        /// this turn makes that player discard 1 random card.
        /// </summary>
        public static void ArmBandit(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target)
        {
            if (engine == null || who == null) return;
            var name = source?.Name ?? "The Secret of the Bandit";
            if (target == null || !target.FaceUp || engine.ControllerOf(target) == null ||
                !engine.ControllerOf(target).TryFindMonster(target, out _))
            {
                engine.Log($"{name}: the selected monster is no longer face-up on the field — no effect.");
                return;
            }

            if (source != null && ContinuousProtections.IsUnaffectedBy(engine, target, source))
            {
                engine.Log($"{target.Name} is unaffected by {name}.");
                return;
            }

            var st = StateOf(engine);
            st.Bandits.RemoveAll(b => !Live(engine, b.Mark));
            if (st.Bandits.Exists(b => ReferenceEquals(b.Mark.Monster, target)))
            {
                engine.Log($"{name}: {target.Name} already carries this effect this turn — no effect.");
                return;
            }

            st.Bandits.Add(new BanditWatch { Mark = MarkOf(engine, target), Activator = who, Source = source });
            engine.Log($"{name}: each time {target.Name} inflicts battle damage to {engine.OpponentOf(who)?.Name} this " +
                       "turn, they discard 1 random card.");
        }

        /// <summary>TextEffectRuntime.NotifyBattleDamageInflicted: <paramref name="monster"/> inflicted battle damage to
        /// <paramref name="victim"/>.</summary>
        public static void OnBattleDamage(DuelEngine engine, CardInstance monster, DuelistState victim)
        {
            if (engine == null || monster == null || victim == null || engine.GameOver) return;
            if (!s_state.TryGetValue(engine, out var st) || st.Bandits.Count == 0) return;
            st.Bandits.RemoveAll(b => b.Mark.Turn != engine.TurnNumber);
            foreach (var b in st.Bandits.ToList())
            {
                if (!ReferenceEquals(b.Mark.Monster, monster) || !Live(engine, b.Mark) || !monster.FaceUp) continue;
                if (engine.OpponentOf(b.Activator) != victim) continue;
                engine.Log($"{b.Source?.Name ?? "The Secret of the Bandit"}: {monster.Name} inflicted battle damage — " +
                           $"{victim.Name} discards 1 random card.");
                if (victim.Hand.Count == 0)
                {
                    engine.Log($"{victim.Name} has no card in hand to discard.");
                    continue;
                }

                TextEffectRuntime.DiscardRandomFromOpponentHand(engine, victim, b.Source,
                    new EffectClause { Action = EffectActionKind.DiscardRandomFromOpponentHand, Amount = 1 });
            }
        }

        /// <summary>TextEffectRuntime.AutoPick: the AI selects its own strongest face-up monster (the one that attacks).</summary>
        public static CardInstance AiPickBanditTarget(DuelistState who, List<CardInstance> targets) =>
            targets.Where(t => who != null && who.TryFindMonster(t, out _))
                .OrderByDescending(t => t.CurrentAtk).FirstOrDefault()
            ?? targets.OrderByDescending(t => t.CurrentAtk).First();

        // ───────────────────────── Array of Revealing Light ─────────────────────────

        /// <summary>A Field Spell whose complete program declares a Monster Type when it is activated.</summary>
        static bool DeclaresTypeOnActivation(CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsFieldSpell) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return prog != null && prog.FullyCompiled && prog.ClausesFor(EffectTiming.Activate)
                .Any(c => c != null && c.Action == EffectActionKind.DeclareMonsterType);
        }

        static PendingActivation DeclarationPending(DuelEngine engine, DuelistState who, CardInstance card, PickKind kind)
        {
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = kind == PickKind.FieldSpellDeclarationBeforeActivation,
                UsesTextProgram = true,
                TargetKind = EffectTargetKind.DeclareMonsterType,
                TargetPicksRemaining = 1,
                PromptOverride = $"{card.Name}: declare 1 Type of monster."
            };
            pending.LegalTargets.AddRange(TypeDeclarationRuntime.MonsterTypeTokens(engine));
            s_picks.Add(pending, new Pick { Kind = kind, Source = card });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return pending;
        }

        /// <summary>
        /// DuelEngine.TryActivateSpellTrapCore, before the Field Spell is seated: a human activating a Field Spell that
        /// declares a Type picks the Type first (the opponent learns it before responding); the pick re-enters the
        /// activation. True when the declaration is now awaited. The AI declares in <see cref="OnFieldSpellPlaced"/>.
        /// </summary>
        public static bool BeginFieldSpellDeclaration(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand)
        {
            if (engine == null || who == null || !who.IsPlayer || !fromHand || !who.Hand.Contains(card)) return false;
            if (!DeclaresTypeOnActivation(card) || s_declaredBeforeActivation.TryGetValue(card, out _)) return false;
            if (engine.PendingActivation != null) return false;
            DeclarationPending(engine, who, card, PickKind.FieldSpellDeclarationBeforeActivation);
            return true;
        }

        /// <summary>
        /// DuelEngine.TryPlaceFieldSpell, once the card is face-up in its Field Zone: record the declared Type on it — the
        /// human's pre-activation pick, the AI's pick (<see cref="TypeDeclarationRuntime.AiPickMonsterType"/>), or, for a
        /// human whose Field Spell was placed without the activation path (AR disk), a declaration asked now.
        /// </summary>
        public static void OnFieldSpellPlaced(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || !DeclaresTypeOnActivation(card)) return;
            if (s_declaredBeforeActivation.TryGetValue(card, out var token))
            {
                s_declaredBeforeActivation.Remove(card);
                TypeDeclarationRuntime.RecordDeclaredType(engine, card, token);
                return;
            }

            if (who.IsPlayer)
            {
                if (engine.PendingActivation == null)
                    DeclarationPending(engine, who, card, PickKind.FieldSpellDeclarationOnField);
                return;
            }

            var tokens = TypeDeclarationRuntime.MonsterTypeTokens(engine);
            TypeDeclarationRuntime.RecordDeclaredType(engine, card,
                TypeDeclarationRuntime.AiPickMonsterType(engine, who, tokens));
        }

        /// <summary>The human declared the Type (a Field Spell's declaration pick).</summary>
        static bool FinishDeclaration(DuelEngine engine, PendingActivation pending, Pick pick, CardInstance token)
        {
            if (!TypeDeclarationRuntime.IsTypeToken(token))
            {
                engine.Log("Declare one of the listed Types.");
                return false;
            }

            s_picks.Remove(pending);
            var who = pending.Controller;
            var card = pick.Source;
            var paidToll = pending.HandActionLpPaid;
            engine.ClearPendingActivation();
            if (pick.Kind == PickKind.FieldSpellDeclarationOnField)
            {
                TypeDeclarationRuntime.RecordDeclaredType(engine, card, token);
                engine.NotifyPublic();
                return true;
            }

            engine.Log($"{card.Name}: declares {token.Def.race}.");
            s_declaredBeforeActivation.Remove(card);
            s_declaredBeforeActivation.Add(card, token);
            // Chain Energy (RuleLocks batch): the hand toll paid as this activation began stays with it; the re-entered
            // activation does not ask for it again, and it is refunded if the activation is not made.
            var markedPaid = paidToll > 0 && engine.HandActionCostPaid.Add(card);
            bool ok;
            try
            {
                ok = engine.TryActivateSpellTrap(who, card, fromHand: true);
            }
            finally
            {
                if (markedPaid) engine.HandActionCostPaid.Remove(card);
                s_declaredBeforeActivation.Remove(card);
            }

            if (!ok && paidToll > 0 && who != null && who.Hand.Contains(card))
            {
                who.LifePoints += paidToll;
                // A refunded cost was never paid: Life Absorbing Machine must not count it (v161 cross-group audit).
                ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost(engine, who, paidToll);
                engine.Log($"Chain Energy: {card.Name} was not activated — {paidToll} LP refunded ({who.LifePoints} LP).");
            }

            return ok;
        }

        /// <summary>
        /// CANCEL on a Field Spell's declaration: before the activation it is undone (nothing was placed; Chain Energy's
        /// toll is refunded); on a Field Spell already placed the declaration is mandatory and made for the player.
        /// </summary>
        static bool CancelDeclaration(DuelEngine engine, PendingActivation pending, Pick pick)
        {
            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            var card = pick.Source;
            if (pick.Kind == PickKind.FieldSpellDeclarationOnField)
            {
                var who = pending.Controller;
                var auto = TypeDeclarationRuntime.AiPickMonsterType(engine, who, pending.LegalTargets.ToList());
                engine.Log($"{card?.Name}: the declaration is mandatory — {auto?.Def?.race} is declared.");
                TypeDeclarationRuntime.RecordDeclaredType(engine, card, auto);
                engine.NotifyPublic();
                return true;
            }

            if (pending.HandActionLpPaid > 0 && pending.Controller != null)
            {
                pending.Controller.LifePoints += pending.HandActionLpPaid;
                ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost(engine, pending.Controller, pending.HandActionLpPaid);
                engine.Log($"Chain Energy: {pending.HandActionLpPaid} LP refunded ({pending.Controller.LifePoints} LP).");
            }

            engine.Log($"{card?.Name}: activation cancelled.");
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────── Patrician of Darkness ─────────────────────────

        static CardInstance ActivePatrician(DuelEngine engine, DuelistState defender) =>
            defender?.MonstersOnField().FirstOrDefault(m =>
                ActiveClause(engine, m, EffectActionKind.YouChooseOpponentsAttackTargets) != null);

        static int Declarations(DuelEngine engine) =>
            engine.GameEvents.Events.Count(e => e.Kind == DuelGameEventKind.DeclaredAttack);

        /// <summary>
        /// The monsters <paramref name="attacker"/> may legally declare an attack on (DuelEngine.TryAttack's target checks):
        /// Ring of Magnetism, "cannot be attacked", attacks that must be direct, the Toon and Spirit target rules.
        /// </summary>
        public static List<CardInstance> LegalAttackTargets(DuelEngine engine, DuelistState attackingPlayer,
            CardInstance attacker)
        {
            var defender = engine?.OpponentOf(attackingPlayer);
            if (defender == null || attacker == null) return new List<CardInstance>();
            return defender.MonstersOnField().Where(m =>
                    m != null &&
                    RedirectAndReplaceRuntime.AttackerCouldAttack(engine, attackingPlayer, attacker, m) &&
                    !ToonsRuntime.ForbidsAttackTarget(engine, attacker, m) &&
                    !SpiritMonstersRuntime.ForbidsAttackTarget(engine, attacker, m))
                .ToList();
        }

        /// <summary>
        /// DuelEngine.TryAttack, after the attacker's declaration passed every legality check and before any attack cost:
        /// when the defender controls a face-up Patrician of Darkness and the attacker chose to attack a monster, the
        /// defender chooses the attack target among the attacker's legal targets (a direct attack stays the attacker's
        /// choice). Returns true when the declaration must change: <paramref name="paused"/> = a human defender is picking
        /// (TryAttack returns and the pick re-declares), otherwise TryAttack re-declares on <paramref name="pick"/>.
        /// </summary>
        public static bool TryDefenderChoosesTarget(DuelEngine engine, DuelistState who, CardInstance attacker,
            CardInstance target, out CardInstance pick, out bool paused)
        {
            pick = target;
            paused = false;
            if (engine == null || who == null || attacker == null || target == null) return false;
            var defender = engine.OpponentOf(who);
            var patrician = ActivePatrician(engine, defender);
            if (patrician == null) return false;
            var st = StateOf(engine);
            var decl = Declarations(engine);
            var chosen = st.Chosen;
            if (chosen != null && ReferenceEquals(chosen.Attacker, attacker) && chosen.Turn == engine.TurnNumber &&
                chosen.Declarations == decl)
            {
                if (ReferenceEquals(chosen.Target, target)) return false; // this declaration's target was chosen already
            }

            st.Chosen = null;
            var candidates = LegalAttackTargets(engine, who, attacker);
            if (candidates.Count <= 1)
            {
                st.Chosen = new PatricianChoice { Attacker = attacker, Target = target, Turn = engine.TurnNumber, Declarations = decl };
                return false;
            }

            if (engine.IsHumanControlled(defender))
            {
                var pending = new PendingActivation
                {
                    Controller = defender,
                    Card = patrician,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetKind = EffectTargetKind.YourMonster,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"{patrician.Name}: choose the monster {attacker.Name} attacks."
                };
                pending.LegalTargets.AddRange(candidates);
                s_picks.Add(pending, new Pick
                {
                    Kind = PickKind.PatricianTarget, AttackingPlayer = who, DefendingPlayer = defender,
                    Attacker = attacker, Source = patrician
                });
                engine.SetPendingActivation(pending);
                engine.Log($"{attacker.Name} attacks — {pending.Prompt}");
                engine.NotifyPublic();
                paused = true;
                return true;
            }

            pick = AiPickDefender(attacker, candidates);
            st.Chosen = new PatricianChoice { Attacker = attacker, Target = pick, Turn = engine.TurnNumber, Declarations = decl };
            engine.Log($"{patrician.Name}: {defender.Name} chooses the target of {attacker.Name}'s attack — " +
                       $"{(pick.FaceUp ? pick.Name : "a face-down monster")}.");
            return true;
        }

        /// <summary>
        /// The defender's deterministic pick: a monster that wins or survives the battle (highest battle stat first);
        /// otherwise the one that costs least — a Defense Position monster (no battle damage), then the lowest ATK+DEF.
        /// </summary>
        public static CardInstance AiPickDefender(CardInstance attacker, List<CardInstance> candidates)
        {
            var atk = BattleMechanics.AttackValue(attacker);
            int Stat(CardInstance m) => m.Position == BattlePosition.Attack
                ? BattleMechanics.AttackValue(m)
                : BattleMechanics.DefenseValue(m);
            bool Holds(CardInstance m) => m.Position == BattlePosition.Attack ? Stat(m) > atk : Stat(m) >= atk;
            var holder = candidates.Where(Holds).OrderByDescending(Stat).FirstOrDefault();
            if (holder != null) return holder;
            return candidates
                .OrderBy(m => m.Position == BattlePosition.Attack ? 1 : 0)
                .ThenBy(m => (m.Def?.atk ?? 0) + (m.Def?.def ?? 0))
                .First();
        }

        /// <summary>SpellTrapEffects.TryResolveWithTarget hook: the human defender picked the attack target.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var pick)) return false;
            var chosen = engine.ResolveLegalEffectTarget(target);
            if (chosen == null)
            {
                engine.Log(pick.Kind == PickKind.PatricianTarget
                    ? "Choose one of the highlighted monsters."
                    : "Declare one of the listed Types.");
                return true;
            }

            if (pick.Kind != PickKind.PatricianTarget)
            {
                ok = FinishDeclaration(engine, pending, pick, chosen);
                return true;
            }

            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            ok = true;
            Redeclare(engine, pick, chosen);
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the choice is mandatory — CANCEL makes it for the player.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var pick)) return false;
            if (pick.Kind != PickKind.PatricianTarget) return CancelDeclaration(engine, pending, pick);
            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            var candidates = pending.LegalTargets.Where(m => m != null).ToList();
            if (candidates.Count == 0)
            {
                engine.Log($"{pick.Source?.Name}: no monster can be attacked — the attack is not declared.");
                engine.NotifyPublic();
                return true;
            }

            var auto = AiPickDefender(pick.Attacker, candidates);
            engine.Log($"{pick.Source?.Name}: the choice is mandatory — {auto.Name} is chosen.");
            Redeclare(engine, pick, auto);
            return true;
        }

        static void Redeclare(DuelEngine engine, Pick pick, CardInstance chosen)
        {
            var st = StateOf(engine);
            st.Chosen = new PatricianChoice
            {
                Attacker = pick.Attacker, Target = chosen, Turn = engine.TurnNumber, Declarations = Declarations(engine)
            };
            engine.Log($"{pick.Source?.Name}: {pick.DefendingPlayer?.Name} chooses {chosen.Name} as the target of " +
                       $"{pick.Attacker?.Name}'s attack.");
            if (engine.GameOver || pick.AttackingPlayer == null || pick.Attacker == null ||
                !engine.TryAttack(pick.AttackingPlayer, pick.Attacker, chosen))
            {
                engine.Log($"{pick.Attacker?.Name} can no longer attack {chosen.Name} — the attack is not declared.");
                engine.NotifyPublic();
            }
        }
    }
}
