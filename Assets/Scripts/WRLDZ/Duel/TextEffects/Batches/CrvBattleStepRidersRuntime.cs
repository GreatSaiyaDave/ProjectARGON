using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// CrvBattleStepRiders batch runtime (atoms: <see cref="CrvBattleStepRidersAtoms"/>).
    /// <list type="bullet">
    /// <item><b>The battle record</b> (<see cref="OnDamageCalculation"/>, from DuelEngine.FinishDeferredDamageCalculation):
    /// both battlers and their sides, and the battle damage each player takes in that damage calculation
    /// (<see cref="NoteBattleDamage"/>, from TextEffectRuntime.NotifyInflictedBattleDamage; Lua Duel.GetBattleDamage).</item>
    /// <item>Steamroid / Etoile Cyber: <see cref="EffectActionKind.ThisAtkChangesDuringDamageStepByBattleRole"/> adds to
    /// <c>CardInstance.DamageStepAtkBonus</c> for that damage calculation (cleared by the engine afterwards, as every other
    /// Damage Step ATK).</item>
    /// <item>Nanobreaker: <see cref="StartOfDamageStepTargetAllowed"/> (TextEffectRuntime.TryStartOfDamageStepDestroy).</item>
    /// <item>Chthonian Soldier / Oxygeddon: <see cref="SendConditionHolds"/> (TextEffectRuntime.SentToGySendMatches: the
    /// trigger does not activate when its condition fails) and <see cref="InflictBattleDamageTaken"/>. Battle-destroyed GY
    /// triggers resolve inline, as every other one (known residual).</item>
    /// </list>
    /// Goblin Elite Attack Force / Indomitable Fighter Lei Lei reuse BattlePhaseEndRuntime; Drillroid reuses
    /// BattleAndPositionRecordsRuntime.FireBattleConfirm (DEF floor 0).
    /// </summary>
    public static class CrvBattleStepRidersRuntime
    {
        sealed class Battle
        {
            public CardInstance Attacker;
            public CardInstance Target;
            public DuelistState AttackerSide;
            public DuelistState TargetSide;
            public readonly Dictionary<DuelistState, int> DamageTaken = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, Battle> s_battle = new();

        enum Role
        {
            AttacksMonster,
            Attacked,
            AttacksDirectly
        }

        // ───────────────────────────── the battle ─────────────────────────────

        /// <summary>
        /// DuelEngine.FinishDeferredDamageCalculation, after the engine set this damage calculation's ATK and before the
        /// "before damage calculation" effects: note the battle (its battle damage starts at 0) and apply Steamroid /
        /// Etoile Cyber's Damage Step ATK to the battlers.
        /// </summary>
        public static void OnDamageCalculation(DuelEngine engine, DuelistState attackingPlayer, CardInstance attacker,
            CardInstance target)
        {
            if (engine == null) return;
            var b = s_battle.GetOrCreateValue(engine);
            b.Attacker = attacker;
            b.Target = target;
            b.AttackerSide = attackingPlayer;
            b.TargetSide = attackingPlayer != null ? engine.OpponentOf(attackingPlayer) : null;
            b.DamageTaken.Clear();
            ApplyDamageStepAtk(engine, attacker, target == null ? Role.AttacksDirectly : Role.AttacksMonster);
            if (target != null)
                ApplyDamageStepAtk(engine, target, Role.Attacked);
        }

        /// <summary>TextEffectRuntime.NotifyInflictedBattleDamage: <paramref name="damaged"/> took this battle damage.</summary>
        public static void NoteBattleDamage(DuelEngine engine, DuelistState damaged, int amount)
        {
            if (engine == null || damaged == null || amount <= 0 || !s_battle.TryGetValue(engine, out var b)) return;
            b.DamageTaken.TryGetValue(damaged, out var before);
            b.DamageTaken[damaged] = before + amount;
        }

        // ───────────────────────────── Steamroid / Etoile Cyber ─────────────────────────────

        static Role? RoleOf(EffectClause c)
        {
            if (c.RequiresDirectAttack) return Role.AttacksDirectly;
            if (c.RequiresThisIsAttackTarget) return Role.Attacked;
            if (c.DamageStepOnlyWhenAttacking) return Role.AttacksMonster;
            return null;
        }

        static void ApplyDamageStepAtk(DuelEngine engine, CardInstance self, Role role)
        {
            if (self?.Def == null || !self.FaceUp || self.IsNegated) return;
            var prog = CompiledEffectCache.GetOrCompile(self);
            if (prog == null || !prog.FullyCompiled) return;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (c == null || c.Action != EffectActionKind.ThisAtkChangesDuringDamageStepByBattleRole) continue;
                if (RoleOf(c) != role || c.Amount == 0) continue;
                self.DamageStepAtkBonus += c.Amount;
                engine.Log($"{self.Name}: {(c.Amount > 0 ? "+" : "")}{c.Amount} ATK during this Damage Step " +
                           $"({self.CurrentAtk}).");
            }
        }

        // ───────────────────────────── Nanobreaker ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.TryStartOfDamageStepDestroy: a start-of-Damage-Step Destroy clause with a Level cap
        /// (<see cref="EffectClause.MonsterLevelMax"/>, Nanobreaker) applies only while its attacker is face-up and unnegated and
        /// the attack target is face-up (<see cref="EffectClause.RequiresTargetFaceUp"/>, any position) with a Level of at most
        /// the cap (Lua IsLevelBelow: Xyz and Link Monsters have no Level). Clauses without a cap are not affected.
        /// </summary>
        public static bool StartOfDamageStepTargetAllowed(EffectClause c, CardInstance attacker, CardInstance defender)
        {
            if (c == null || c.MonsterLevelMax <= 0) return true;
            if (attacker == null || !attacker.FaceUp || attacker.IsNegated) return false;
            if (defender?.Def == null || (c.RequiresTargetFaceUp && !defender.FaceUp)) return false;
            if (defender.Def.IsXyzMonster || defender.Def.IsLinkMonster) return false;
            var level = defender.Level;
            return level > 0 && level <= c.MonsterLevelMax;
        }

        // ───────────────────────────── Chthonian Soldier / Oxygeddon ─────────────────────────────

        static DuelistState GyHolder(DuelEngine engine, CardInstance card)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side?.Graveyard != null && side.Graveyard.Contains(card))
                    return side;
            return null;
        }

        /// <summary>
        /// The battle damage <paramref name="you"/> took in the battle that destroyed <paramref name="card"/> as the attack
        /// target of <paramref name="you"/>'s opponent's monster; 0 when <paramref name="card"/> was not destroyed that way.
        /// </summary>
        static int BattleDamageTaken(DuelEngine engine, CardInstance card, DuelistState you)
        {
            if (engine == null || card == null || you == null || !s_battle.TryGetValue(engine, out var b)) return 0;
            if (!ReferenceEquals(b.Target, card) || b.Attacker == null || !ReferenceEquals(card.BattleDestroyer, b.Attacker))
                return 0;
            if (ReferenceEquals(b.AttackerSide, you)) return 0;
            return b.DamageTaken.TryGetValue(you, out var n) ? n : 0;
        }

        /// <summary>
        /// TextEffectRuntime.SentToGySendMatches: false when this batch's sent-to-GY trigger does not activate for this send —
        /// Chthonian Soldier was not destroyed as the attack target of an opponent's monster, or its controller took no battle
        /// damage from that battle (Lua chk: GetBattleDamage(tp) &gt; 0); Oxygeddon (a SentFromFieldToGy clause with
        /// <see cref="EffectClause.BattledMonsterRaceFilter"/>) did not battle a monster of that Type (Lua
        /// GetBattleTarget():IsRace, its current Type).
        /// </summary>
        public static bool SendConditionHolds(DuelEngine engine, CardInstance card, EffectClause c)
        {
            if (engine == null || c == null || c.Timing != EffectTiming.SentFromFieldToGy) return true;
            if (c.Action == EffectActionKind.InflictBattleDamageYouTookThisBattleToOpponent)
                return BattleDamageTaken(engine, card, GyHolder(engine, card)) > 0;
            if (!string.IsNullOrEmpty(c.BattledMonsterRaceFilter))
            {
                var other = card?.BattleDestroyer;
                return other?.Def != null && ClassicEraTemplates.RaceMatches(other.Race, c.BattledMonsterRaceFilter);
            }

            return true;
        }

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.InflictBattleDamageYouTookThisBattleToOpponent"/> (Chthonian Soldier):
        /// the battle damage <paramref name="who"/> took from that battle is inflicted to their opponent (effect damage).
        /// </summary>
        public static void InflictBattleDamageTaken(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            var n = BattleDamageTaken(engine, source, who);
            var opp = engine.OpponentOf(who);
            if (n <= 0 || opp == null) return;
            engine.Log($"{source.Name}: the {n} battle damage {who.Name} took from this battle is also inflicted to {opp.Name}.");
            engine.ApplyEffectDamage(opp, n, source.Name, source);
        }
    }
}
