using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "AttackObligations": the must-attack obligation (ygopro EFFECT_MUST_ATTACK), enforced by
    /// <see cref="AttackObligationsRuntime"/> (End Battle / End Turn legality and the AI's attack duty). Every atom is
    /// anchored to the card's whole text.
    /// <list type="bullet">
    /// <item>Berserk Gorilla (c39168895): self-destruction in face-up Defense Position + "This card must attack if able."</item>
    /// <item>Amazoness Archers (c67987611): the opponent's monsters to face-up Attack Position, -500 ATK, must attack.</item>
    /// <item>Diffusion Wave-Motion (c87880531): the target attacks every opponent's monster once each; others cannot attack.</item>
    /// <item>Staunch Defender (c92854392): the opponent attacks only the target, with every monster that can.</item>
    /// </list>
    /// </summary>
    public static class AttackObligationsAtoms
    {
        const string Apos = @"['’]";
        const string Q = @"[""“”]";

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var v) ? v : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Berserk Gorilla (c39168895): e1 SINGLE EFFECT_MUST_ATTACK; e3 SINGLE EFFECT_SELF_DESTROY (range MZONE) while
            // IsDefensePos — a continuous effect, so only while it is face-up. Both are rules read by the runtime.
            AtomRegistration.Atom.Sequence(
                @"^\s*If this card is in face-up Defense Position, destroy this card\.\s*" +
                @"This card must attack if able\.?\s*$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.SelfDestroyWhileFaceUpDefense,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.MustAttackIfAble,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    }
                }),

            // Amazoness Archers (c67987611): EVENT_ATTACK_ANNOUNCE during the opponent's turn while you control a face-up
            // "Amazoness" monster (s.cfilter IsFaceup + IsSetCard); no target. ChangePosition(noflip) of every monster not in
            // face-up Attack Position, then every face-up one: EFFECT_UPDATE_ATTACK -500 (RESETS_STANDARD) and
            // EFFECT_MUST_ATTACK (RESETS_STANDARD_PHASE_END).
            new AtomRegistration.Atom(
                @"^\s*When an opponent" + Apos + @"s monster declares an attack, while you control an? " + Q +
                @"([^""“”]+)" + Q + @" monster: Change all monsters your opponent currently controls to face-up Attack " +
                @"Position \(Flip Effects are not activated\), they lose (\d+) ATK, also they must attack this turn, " +
                @"if able\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.AttackDeclared,
                    Action = EffectActionKind.OpponentMonstersToFaceUpAttackLoseAtkMustAttack,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 2, 500),
                    ConditionName = m.Groups[1].Value.Trim(),
                    ActivationCondition = AttackObligationsRuntime.YouControlFaceUpSeriesMonster,
                    SuppressFlipEffects = true,
                    MakesChainLink = true
                }),

            // Diffusion Wave-Motion (c87880531): EFFECT_FLAG_CARD_TARGET, Cost.PayLP(1000), condition IsAbleToEnterBP (and
            // the printed "If your opponent controls a monster"); target 1 face-up Level 7+ Spellcaster you control. At
            // resolution (face-up, still yours): EFFECT_CANNOT_ATTACK for your other monsters (by field id) this turn,
            // EFFECT_MUST_ATTACK + EFFECT_ATTACK_ALL on it, and EVENT_BATTLED: a monster it destroys by battle cannot
            // activate its effects.
            new AtomRegistration.Atom(
                @"^\s*(?:\(This card is not treated as an? " + Q + @"[^""“”]+" + Q + @" card\.\)\s*)?" +
                @"If your opponent controls a monster: Pay (\d+) LP, then target 1 Level (\d+) or higher (\w+) monster " +
                @"you control; this turn, it must attack all monsters your opponent controls once each, also other " +
                @"monsters you control cannot attack\.\s*The effects of monsters destroyed by these attacks cannot " +
                @"activate and are negated\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetMustAttackAllOthersCannotAttack,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    PayLpAmount = Parse(m, 1, 1000),
                    Amount = Parse(m, 2, 7),
                    RaceFilter = m.Groups[3].Value,
                    ActivationCondition = AttackObligationsRuntime.OpponentControlsMonsterBeforeBattlePhase,
                    MakesChainLink = true
                }),

            // Staunch Defender (c92854392): EVENT_ATTACK_ANNOUNCE during the opponent's turn; EFFECT_FLAG_CARD_TARGET on 1
            // face-up monster you control. At resolution (still related): FIELD EFFECT_MUST_ATTACK and
            // EFFECT_MUST_ATTACK_MONSTER (that monster's real field id) on the opponent's monsters until the end of the
            // Battle Phase (RESET_PHASE|PHASE_BATTLE).
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card when your opponent declares an attack\.\s*" +
                @"Select 1 face-up monster on your side of the field\.\s*During this turn, your opponent can only " +
                @"designate the selected monster as an attack target and your opponent must attack the selected monster " +
                @"with all face-up monsters\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.AttackDeclared,
                    Action = EffectActionKind.OpponentAttacksOnlyTargetAndMustAttackIt,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),
        };
    }
}
