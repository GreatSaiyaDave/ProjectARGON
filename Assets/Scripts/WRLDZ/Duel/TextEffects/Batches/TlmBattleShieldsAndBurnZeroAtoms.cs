using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TlmBattleShieldsAndBurnZero batch atoms (whole sentences): continuous attack-target shields, damage-to-0 riders and
    /// a Set-Trap burn. Runtime: <see cref="TlmBattleShieldsAndBurnZeroRuntime"/>; the shields are read by
    /// ContinuousProtections.CannotBeAttackTarget, Monk Fighter's battle damage by FieldSpellEffects.ControllerAvoidsBattleDamage.
    /// </summary>
    public static class TlmBattleShieldsAndBurnZeroAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Monk Fighter (c3810071: SINGLE EFFECT_AVOID_BATTLE_DAMAGE, value 1): its controller takes no battle damage from
            // a battle this card is in, attacking or attacked; the opponent still does. Same shape as Amazoness Fighter
            // (FieldSpellEffects.ControllerAvoidsBattleDamage: face-up, not negated, attacker or attack target).
            new AtomRegistration.Atom(
                Start + @"Battle [Dd]amage to this card" + Apos + @"s controller from (?:a )?battle involving this card " +
                @"becomes 0" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.PreventControllerBattleDamage,
                    OnlyBattlesInvolvingThis = true,
                    MakesChainLink = false
                }),

            // Hieracosphinx (c82260502: FIELD EFFECT_CANNOT_SELECT_BATTLE_TARGET, range (0, MZONE) = the opponent's attackers,
            // atlimit IsPosition(POS_FACEDOWN_DEFENSE)). Not Feint Plan's both-players this-turn lock.
            new AtomRegistration.Atom(
                Start + @"Your opponent cannot select a face-down Defense Position monster as an attack target" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OpponentCannotSelectYourFaceDownDefenseMonstersAsAttackTarget,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Grave Ohja (c40937767) e1: SINGLE EFFECT_CANNOT_BE_BATTLE_TARGET, aux.imval1, condition: its controller has a
            // POS_FACEDOWN_DEFENSE monster in its Monster Zone. A direct attack stays blocked.
            new AtomRegistration.Atom(
                Start + @"While there is a face-down Defense Position monster(?:\(s\))? on your side of the field, no player " +
                @"can select this card as an attack target" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeAttackTarget,
                    WhileCondition = TlmBattleShieldsAndBurnZeroRuntime.YouControlFaceDownDefense,
                    AllowsDirectAttackWhileProtected = false,
                    MakesChainLink = false
                }),

            // Grave Ohja (c40937767) e2: FIELD TRIGGER_F EVENT_FLIP_SUMMON_SUCCESS, ep == tp, eg:GetFirst() ~= this card;
            // Damage(1-tp, 300). One mandatory trigger per Flip Summon (DuelEngine.CompleteFlipSummon).
            new AtomRegistration.Atom(
                Start + @"Each time a monster on your side of the field is Flip Summoned, inflict (\d+) damage to your " +
                @"opponent" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageToOpponentEachTimeYourMonsterFlipSummoned,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1),
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Des Wombat (c9637706: FIELD EFFECT_CHANGE_DAMAGE → 0 for REASON_EFFECT + EFFECT_NO_EFFECT_DAMAGE, player target
            // (1, 0) = its controller, range MZONE).
            new AtomRegistration.Atom(
                Start + @"Any damage to you from a card effect becomes 0" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.EffectDamageToControllerBecomesZero,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Kozaky's Self-Destruct Button (c21908319): no EFFECT_TYPE_ACTIVATE (it can only be Set); SINGLE TRIGGER_F
            // EVENT_DESTROYED, IsPreviousLocation(LOCATION_SZONE) and IsPreviousPosition(POS_FACEDOWN); Damage(rp, 1000) —
            // rp is the player whose card destroyed it (its own controller included).
            new AtomRegistration.Atom(
                Start + @"Inflict (\d+) damage to the player who destroyed this Set card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.InflictDamageToPlayerWhoDestroyedThisSetCard,
                    Amount = Parse(m, 1),
                    RequiresDestroyedWhileFaceDown = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n) ? n : 0;
    }
}
