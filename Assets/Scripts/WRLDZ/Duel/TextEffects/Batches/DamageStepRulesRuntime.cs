using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DamageStepRules" runtime (atoms: <see cref="DamageStepRulesAtoms"/>). Reads compiled programs only — no
    /// card-id branches; only a FullyCompiled program's clauses apply (<see cref="LiveClauses"/>).
    /// <list type="bullet">
    /// <item><b>The battle</b> (<see cref="BeginBattle"/> from DuelEngine.ContinueBeforeDamageCalculation and
    /// FinishDeferredDamageCalculation; <see cref="EndBattle"/> from DuelEngine.FinishDamageStep; <see cref="ApplyBoard"/>
    /// from FieldSpellEffects.RefreshBoard): the attacker and the attack target of the Damage Step in progress, with
    /// their field-entry stamps (ygopro IsRelateToBattle).</item>
    /// <item><b>Cat's Ear Tribe</b> (<see cref="EffectActionKind.OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep"/>):
    /// while the Damage Step lasts, during its controller's opponent's turn, with this face-up, unnegated card the attack
    /// target and both battlers still on the field from that battle, the attacker's original ATK is the clause's amount
    /// (CardInstance.OriginalAtkOverride; the value it had before is put back as soon as the condition ends).</item>
    /// <item><b>Thousand Needles</b> (<see cref="EffectActionKind.DestroyAttackerAtEndOfDamageStep"/> on
    /// <see cref="EffectTiming.EndOfDamageStep"/>, from TextEffectRuntime.NotifyEndOfDamageStep): this unnegated card
    /// was the attack target and is in Defense Position, the attacker is still on the field from that battle and its ATK
    /// is lower than this card's DEF → the attacker is destroyed (<see cref="TryApplyDestroyAttacker"/>).</item>
    /// <item><b>Continuous Destruction Punch</b> (the same action on a face-up Continuous Spell,
    /// <see cref="EffectTiming.ContinuousWhileFaceUp"/>; <see cref="FireEndOfDamageStep"/>): the opponent's attacker and
    /// your attacked Defense Position monster are still on the field from that battle and the attacker's ATK is lower
    /// than that monster's DEF → the attacker is destroyed. A continuous effect: no Chain Link.</item>
    /// <item><b>Ultimate Obedient Fiend</b> (<see cref="AttackForbidden"/>, DuelEngine.ContinuousCannotAttackBlocks):
    /// it cannot attack while its controller has more than 1 card on the field or any card in hand.</item>
    /// <item><b>Meteorain</b> (<see cref="YourTurn"/>, <see cref="GrantTurnPiercing"/>, <see cref="TurnGrantsPiercing"/>):
    /// for the rest of the turn, the monsters its controller controls inflict piercing battle damage.</item>
    /// <item><b>Kishido Spirit</b> (<see cref="BattleIndestructible"/>, at damage calculation): a monster you control is
    /// not destroyed by battle with a monster whose ATK equals its own.</item>
    /// <item><b>Amazoness Swords Woman</b> (<see cref="ReflectBattleDamage"/>, at damage calculation): the battle damage
    /// its controller would take from its battles (after that player's own prevention and halving) is taken by the
    /// opponent instead, unless the opponent takes no battle damage. No monster "inflicts" the reflected damage, so no
    /// "inflicts battle damage" trigger fires for it (ygopro keeps the battle's reason card, whose controller is now the
    /// damaged player).</item>
    /// </list>
    /// </summary>
    public static class DamageStepRulesRuntime
    {
        /// <summary>ActivationCondition (Meteorain, ygopro Duel.IsTurnPlayer(tp)): only during your turn.</summary>
        public const string YourTurn = "DamageStepRules.YourTurn";

        sealed class State
        {
            public readonly List<(DuelistState Who, int Turn)> PiercingGrants = new();

            public DuelistState AttackingPlayer;
            public CardInstance Attacker;
            public CardInstance Target;
            public int AttackerEntry;
            public int TargetEntry;

            /// <summary>The monster whose original ATK Cat's Ear Tribe set, and the override it had before.</summary>
            public CardInstance CatsEarApplied;
            public int CatsEarPrior = int.MinValue;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetOrCreateValue(engine);

        static IEnumerable<EffectClause> LiveClauses(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == action)
                    yield return c;
        }

        /// <summary>A face-up Spell/Trap of <paramref name="side"/> whose <paramref name="action"/> applies now.</summary>
        static bool LiveSpellTrap(DuelEngine engine, CardInstance st, EffectActionKind action) =>
            st != null && ContinuousNegationRuntime.NegatedBy(engine, st) == null && LiveClauses(st, action).Any();

        static bool InDamageStep(DuelEngine engine) =>
            engine != null && engine.BattleStep == BattleStep.DamageStep && engine.DamageSubStep != DamageSubStep.None;

        // ───────────────────────────── the battle ─────────────────────────────

        /// <summary>
        /// The Damage Step of <paramref name="attacker"/> against <paramref name="target"/> (null = direct) goes on to damage
        /// calculation. Idempotent for the same battle; Cat's Ear Tribe's ATK applies from here.
        /// </summary>
        public static void BeginBattle(DuelEngine engine, DuelistState attackingPlayer, CardInstance attacker,
            CardInstance target)
        {
            if (engine == null) return;
            var st = StateOf(engine);
            if (!ReferenceEquals(st.Attacker, attacker) || !ReferenceEquals(st.Target, target) ||
                st.AttackingPlayer != attackingPlayer)
            {
                st.AttackingPlayer = attackingPlayer;
                st.Attacker = attacker;
                st.Target = target;
                st.AttackerEntry = attacker?.FieldEntryStamp ?? 0;
                st.TargetEntry = target?.FieldEntryStamp ?? 0;
            }

            SyncCatsEar(engine, st);
            // BattleDestroyedTriggers batch: the battlers' battle positions (Shinato).
            BattleDestroyedTriggersRuntime.NoteBattle(engine, attacker, target);
            // EndOfDamageStepRiders batch: the battlers, their stamps, Types and positions (Getsu Fuhma, Insect Princess).
            EndOfDamageStepRidersRuntime.NoteBattle(engine, attackingPlayer, attacker, target);
        }

        /// <summary>
        /// ChaosBanishSummons batch: the attacking monster of the battle whose Damage Step is under way (still set while its
        /// battle destructions and their EVENT_BATTLE_DESTROYING triggers apply, after DuelEngine cleared DeclaredAttacker);
        /// null outside a Damage Step.
        /// </summary>
        public static CardInstance BattleAttacker(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) ? st.Attacker : null;

        /// <summary>DuelEngine.FinishDamageStep: the Damage Step is over.</summary>
        public static void EndBattle(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.AttackingPlayer = null;
            st.Attacker = null;
            st.Target = null;
            SyncCatsEar(engine, st);
        }

        /// <summary>FieldSpellEffects.RefreshBoard: the Damage Step ended another way, or a battler left the field.</summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            if (!InDamageStep(engine))
            {
                st.AttackingPlayer = null;
                st.Attacker = null;
                st.Target = null;
            }

            SyncCatsEar(engine, st);
        }

        static bool AttackerStillFromBattle(State st) =>
            st.Attacker != null && st.AttackingPlayer != null && st.AttackingPlayer.TryFindMonster(st.Attacker, out _) &&
            st.Attacker.FieldEntryStamp == st.AttackerEntry;

        static bool TargetStillFromBattle(DuelEngine engine, State st)
        {
            var defSide = st.AttackingPlayer != null ? engine.OpponentOf(st.AttackingPlayer) : null;
            return st.Target != null && defSide != null && defSide.TryFindMonster(st.Target, out _) &&
                   st.Target.FieldEntryStamp == st.TargetEntry;
        }

        // ───────────────────────────── Cat's Ear Tribe ─────────────────────────────

        static void SyncCatsEar(DuelEngine engine, State st)
        {
            CardInstance want = null;
            var amount = 0;
            if (InDamageStep(engine) && st.AttackingPlayer != null && engine.TurnPlayer == st.AttackingPlayer &&
                AttackerStillFromBattle(st) && TargetStillFromBattle(engine, st))
            {
                var clause = LiveClauses(st.Target,
                    EffectActionKind.OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep).FirstOrDefault();
                if (clause != null)
                {
                    want = st.Attacker;
                    amount = clause.Amount;
                }
            }

            if (st.CatsEarApplied != null && !ReferenceEquals(st.CatsEarApplied, want))
            {
                st.CatsEarApplied.OriginalAtkOverride = st.CatsEarPrior;
                engine.Log($"{st.CatsEarApplied.Name}: its original ATK is no longer changed.");
                st.CatsEarApplied = null;
                st.CatsEarPrior = int.MinValue;
            }

            if (want != null && st.CatsEarApplied == null)
            {
                st.CatsEarPrior = want.OriginalAtkOverride;
                want.OriginalAtkOverride = amount;
                st.CatsEarApplied = want;
                engine.Log($"{st.Target.Name}: the original ATK of {want.Name} becomes {amount} during the Damage Step " +
                           $"({want.CurrentAtk}).");
            }
        }

        // ───────────────────────────── end of the Damage Step ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.ApplyClause, <see cref="EffectActionKind.DestroyAttackerAtEndOfDamageStep"/>: true when this
        /// batch handles the clause — Thousand Needles' trigger (<see cref="EffectTiming.EndOfDamageStep"/>) and Continuous
        /// Destruction Punch's continuous marker (nothing to do when it is activated). False: Destruction Punch's window.
        /// </summary>
        public static bool TryApplyDestroyAttacker(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance attacker)
        {
            if (clause == null) return false;
            if (clause.Timing == EffectTiming.ContinuousWhileFaceUp) return true;
            if (clause.Timing != EffectTiming.EndOfDamageStep) return false;
            if (engine == null || who == null || source == null || !s_state.TryGetValue(engine, out var st)) return true;
            // Lua: Duel.GetAttackTarget()==c, in Defense Position; the attacker still relates to the battle.
            if (!ReferenceEquals(st.Target, source) || attacker == null || !ReferenceEquals(st.Attacker, attacker) ||
                st.AttackingPlayer == who)
                return true;
            if (source.IsNegated || !who.TryFindMonster(source, out _) || source.FieldEntryStamp != st.TargetEntry ||
                source.Position != BattlePosition.Defense)
                return true;
            if (!AttackerStillFromBattle(st))
            {
                engine.Log($"{source.Name}: the attacking monster is no longer on the field.");
                return true;
            }

            var atk = attacker.CurrentAtk;
            var def = source.CurrentDef;
            if (atk >= def) return true;
            engine.Log($"{source.Name}: {attacker.Name}'s ATK ({atk}) is lower than its DEF ({def}) — destroy the " +
                       "attacking monster.");
            TextEffectRuntime.DestroyCard(engine, attacker, source, byPlayer: who);
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.NotifyEndOfDamageStep (before the monsters' end-of-Damage-Step triggers): Continuous Destruction
        /// Punch — a continuous effect of the defending player's face-up Continuous Spell.
        /// </summary>
        public static void FireEndOfDamageStep(DuelEngine engine, DuelistState atkPlayer, DuelistState defPlayer,
            CardInstance attacker, CardInstance defender)
        {
            if (engine == null || atkPlayer == null || defPlayer == null || attacker == null || defender == null) return;
            foreach (var card in defPlayer.SpellTrapsOnField().ToList())
            {
                if (!LiveSpellTrap(engine, card, EffectActionKind.DestroyAttackerAtEndOfDamageStep)) continue;
                if (!atkPlayer.TryFindMonster(attacker, out _) || !defPlayer.TryFindMonster(defender, out _)) return;
                if (s_state.TryGetValue(engine, out var st) && ReferenceEquals(st.Attacker, attacker) &&
                    ReferenceEquals(st.Target, defender) &&
                    (attacker.FieldEntryStamp != st.AttackerEntry || defender.FieldEntryStamp != st.TargetEntry))
                    return;
                if (defender.Position != BattlePosition.Defense) return;
                var atk = attacker.CurrentAtk;
                var def = defender.CurrentDef;
                if (atk >= def) return;
                engine.Log($"{card.Name}: {attacker.Name}'s ATK ({atk}) is lower than the DEF of {defender.Name} ({def}) — " +
                           "destroy the attacking monster.");
                TextEffectRuntime.DestroyCard(engine, attacker, card, byPlayer: defPlayer);
                return;
            }
        }

        // ───────────────────────────── Ultimate Obedient Fiend ─────────────────────────────

        /// <summary>
        /// DuelEngine.ContinuousCannotAttackBlocks: a face-up monster with
        /// <see cref="EffectActionKind.CannotAttackUnlessOnlyCardOnFieldAndEmptyHand"/> cannot attack while its controller
        /// has another card on the field (Monster, Spell &amp; Trap or Field Zone) or any card in hand.
        /// </summary>
        public static bool AttackForbidden(DuelEngine engine, DuelistState controller, CardInstance attacker)
        {
            if (engine == null || attacker == null) return false;
            if (!LiveClauses(attacker, EffectActionKind.CannotAttackUnlessOnlyCardOnFieldAndEmptyHand).Any()) return false;
            var side = engine.ControllerOf(attacker) ?? controller;
            if (side == null) return true;
            var onField = side.MonsterCount + side.SpellTrapsOnField().Count();
            return onField > 1 || side.Hand.Count > 0;
        }

        // ───────────────────────────── Meteorain ─────────────────────────────

        /// <summary>Activation-condition hook (TextEffectRuntime.ActivationConditionsMet default chain).</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause c, out bool ok,
            out string reason)
        {
            ok = false;
            reason = null;
            if (c == null || c.ActivationCondition != YourTurn) return false;
            ok = engine != null && who != null && engine.TurnPlayer == who;
            reason = "Only during your turn.";
            return true;
        }

        /// <summary>Meteorain resolves: the monsters <paramref name="who"/> controls inflict piercing damage this turn.</summary>
        public static void GrantTurnPiercing(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            StateOf(engine).PiercingGrants.Add((who, engine.TurnNumber));
            engine.Log($"{source?.Name ?? "Effect"}: this turn, {who.Name}'s monsters inflict piercing battle damage.");
        }

        /// <summary>DuelEngine damage calculation: <paramref name="attacker"/>'s controller has a Meteorain grant this turn.</summary>
        public static bool TurnGrantsPiercing(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker == null || !s_state.TryGetValue(engine, out var st)) return false;
            var ctrl = engine.ControllerOf(attacker);
            return ctrl != null && st.PiercingGrants.Exists(g => g.Who == ctrl && g.Turn == engine.TurnNumber);
        }

        // ───────────────────────────── Kishido Spirit ─────────────────────────────

        /// <summary>
        /// DuelEngine damage calculation: <paramref name="mine"/> (controlled by <paramref name="side"/>) cannot be destroyed
        /// by battle with <paramref name="other"/> — a face-up Kishido Spirit of <paramref name="side"/> applies and the two
        /// monsters' ATK (this damage calculation's) are equal.
        /// </summary>
        public static bool BattleIndestructible(DuelEngine engine, DuelistState side, CardInstance mine,
            CardInstance other)
        {
            if (engine == null || side == null || mine == null || other == null) return false;
            if (!side.TryFindMonster(mine, out _)) return false;
            // BattleDestroyedTriggers batch: the monster's own protection (Kaiser Glider's equal ATK, Different Dimension
            // Dragon's 1900 or less ATK).
            if (BattleDestroyedTriggersRuntime.BattleIndestructible(engine, side, mine, other)) return true;
            if (mine.CurrentAtk != other.CurrentAtk) return false;
            var source = side.SpellTrapsOnField().FirstOrDefault(st =>
                LiveSpellTrap(engine, st, EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster));
            if (source == null) return false;
            engine.Log($"{source.Name}: {mine.Name} cannot be destroyed by battle with {other.Name} (equal ATK " +
                       $"{mine.CurrentAtk}).");
            return true;
        }

        // ───────────────────────────── Amazoness Swords Woman ─────────────────────────────

        static bool Reflects(DuelistState side, CardInstance card) =>
            side != null && card != null && side.TryFindMonster(card, out _) &&
            LiveClauses(card, EffectActionKind.ReflectControllerBattleDamageFromBattlesInvolvingThis).Any();

        /// <summary>
        /// DuelEngine damage calculation, after the battle damage of each player is known (prevention and halving
        /// applied): the damage the controller of a reflecting battler would take moves to the opponent
        /// (<paramref name="reflectedToDefending"/> / <paramref name="reflectedToAttacking"/>; 0 when that player takes no
        /// battle damage). Damage turned into effect damage (Gravekeeper's Vassal) is not battle damage and stays.
        /// </summary>
        public static void ReflectBattleDamage(DuelEngine engine, DuelistState who, DuelistState opp,
            CardInstance attacker, CardInstance target, bool atkDamageIsBattle, bool defDamageIsBattle,
            bool noDmgAtk, bool noDmgDef, ref int toAttacking, ref int toDefending,
            out int reflectedToAttacking, out int reflectedToDefending)
        {
            reflectedToAttacking = 0;
            reflectedToDefending = 0;
            if (engine == null || who == null || opp == null) return;
            if (toAttacking > 0 && atkDamageIsBattle && Reflects(who, attacker))
            {
                engine.Log($"{attacker.Name}: {opp.Name} takes the {toAttacking} battle damage {who.Name} would have " +
                           "taken instead" + (noDmgDef ? $" — {opp.Name} takes no battle damage." : "."));
                reflectedToDefending = noDmgDef ? 0 : toAttacking;
                toAttacking = 0;
            }

            if (toDefending > 0 && defDamageIsBattle && target != null && Reflects(opp, target))
            {
                engine.Log($"{target.Name}: {who.Name} takes the {toDefending} battle damage {opp.Name} would have " +
                           "taken instead" + (noDmgAtk ? $" — {who.Name} takes no battle damage." : "."));
                reflectedToAttacking = noDmgAtk ? 0 : toDefending;
                toDefending = 0;
            }
        }
    }
}
