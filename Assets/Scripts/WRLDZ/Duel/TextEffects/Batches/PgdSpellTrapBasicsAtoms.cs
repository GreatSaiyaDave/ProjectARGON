using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// PgdSpellTrapBasics batch (v162 PGD): plain Spells and Traps whose whole printed text is one atom, checked against
    /// the ygopro scripts. Every pattern is anchored to the start and the end of the card text, so a longer or different
    /// card never compiles part of its text here. Runtime: <see cref="PgdSpellTrapBasicsRuntime"/>.
    /// </summary>
    public static class PgdSpellTrapBasicsAtoms
    {
        /// <summary>A Monster Type word ("Dragon", "Winged Beast", "Beast-Warrior") before "-Type".</summary>
        const string TypeWord = @"((?:[A-Za-z]+[ -])*?[A-Za-z]+)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Book of Taiyou (c38699854): EFFECT_FLAG_CARD_TARGET on a face-down monster in either Monster Zone; if it
            // is still face-down, ChangePosition(POS_FACEUP_ATTACK) (no NO_FLIP_EFFECT: its FLIP effect activates).
            new AtomRegistration.Atom(
                @"^\s*Flip 1 face-down monster on the field into face-up Attack Position\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.FlipFaceDownTargetToFaceUpAttack,
                    Zone = EffectZoneFilter.FieldFaceDownMonsters,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),

            // Curse of Aging (c41398771): cost discard 1 card (Card.IsDiscardable, REASON_COST); needs a face-up monster
            // the opponent controls (s.target chk); every face-up monster they control at resolution gets -500 ATK and
            // -500 DEF (EFFECT_UPDATE_ATTACK / _DEFENSE, RESETS_STANDARD_PHASE_END).
            new AtomRegistration.Atom(
                @"^\s*Discard 1 card from your hand\.\s*Decrease the ATK and DEF of all monsters on your opponent['’]s " +
                @"side of the field by (\d+) points until the end of the turn this card is activated\.?\s*$",
                m =>
                {
                    var n = Parse(m, 1, 500);
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase,
                        Side = EffectSide.Opponent,
                        Amount = -n,
                        DefAmount = -n,
                        RequiresDiscardCost = true,
                        DiscardCostAttribute = "*",
                        DiscardCostCount = 1,
                        ActivationCondition = PgdSpellTrapBasicsRuntime.OpponentControlsFaceUpMonster,
                        MakesChainLink = true
                    };
                }),

            // D. Tribe (c2833249): needs a face-up monster you control; every face-up monster you control at resolution
            // becomes RACE_DRAGON (EFFECT_CHANGE_RACE, RESETS_STANDARD_PHASE_END).
            new AtomRegistration.Atom(
                @"^\s*All monsters on your side of the field are treated as " + TypeWord +
                @"-Type until the End Phase of this turn\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AllYourFaceUpMonstersBecomeTypeUntilEndPhase,
                    Side = EffectSide.Controller,
                    BecomesRace = m.Groups[1].Value.Trim(),
                    ActivationCondition = PgdSpellTrapBasicsRuntime.YouControlFaceUpMonster,
                    MakesChainLink = true
                }),

            // Pyramid Energy (c76754619): Quick-Play; needs a face-up monster you control; the option is chosen as it is
            // activated (Duel.SelectOption in s.target); every face-up monster you control at resolution gets +200 ATK,
            // or +500 DEF, until the End Phase.
            AtomRegistration.Atom.Sequence(
                @"^\s*You can select and activate 1 of the following effects:\s*●\s*Increase the ATK of all face-up " +
                @"monsters on your side of the field by (\d+) points until the End Phase of the turn this card is " +
                @"activated\.\s*●\s*Increase the DEF of all face-up monsters on your side of the field by (\d+) points " +
                @"until the End Phase of the turn this card is activated\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 1, 200),
                        DefAmount = 0,
                        EffectChoiceIndex = 1,
                        ActivationCondition = PgdSpellTrapBasicsRuntime.YouControlFaceUpMonster,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase,
                        Side = EffectSide.Controller,
                        Amount = 0,
                        DefAmount = Parse(m, 2, 500),
                        EffectChoiceIndex = 2,
                        ActivationCondition = PgdSpellTrapBasicsRuntime.YouControlFaceUpMonster,
                        MakesChainLink = true
                    }
                }),

            // Secret Pass to the Treasures (c77876207): EFFECT_FLAG_CARD_TARGET on a face-up monster you control with
            // 1000 or less ATK; if it is still face-up, EFFECT_DIRECT_ATTACK until the End Phase.
            new AtomRegistration.Atom(
                @"^\s*Select 1 face-up monster with an ATK equal to (\d+) points or less on your side of the field\.\s*" +
                @"During the turn this card is activated, the selected monster can attack your opponent['’]s Life " +
                @"Points directly\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetCanAttackDirectlyThisTurn,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    // The ATK ceiling. Not Amount: the target prompts read Amount as a number of targets.
                    ConditionAmount = Parse(m, 1, 1000),
                    MakesChainLink = true
                }),

            // Needle Ceiling (c38411870): free-chain Normal Trap; condition GetFieldGroupCount(MZONE, MZONE) >= 4 (face-down
            // monsters count) and a face-up monster on the field (s.target chk); destroy every face-up monster.
            new AtomRegistration.Atom(
                @"^\s*When there are (\d+) or more monsters on the field:\s*Destroy all face-up monsters\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyAllFaceUpMonstersOnField,
                    Side = EffectSide.Both,
                    ActivationCondition = PgdSpellTrapBasicsRuntime.FieldMonstersAtLeastWithFaceUp,
                    ConditionAmount = Parse(m, 1, 4),
                    MakesChainLink = true
                }),

            // Hieroglyph Lithograph (c10248192): cost Cost.PayLP(1000); EFFECT_HAND_LIMIT 7 registered to the activating
            // player (EFFECT_FLAG_PLAYER_TARGET, no reset: the rest of the Duel).
            new AtomRegistration.Atom(
                @"^\s*Pay (\d+) Life Points\.\s*During the current Duel, your hand size limit becomes (\d+)\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ControllerHandSizeLimitBecomesForDuel,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 2, 7),
                    PayLpAmount = Parse(m, 1, 1000),
                    MakesChainLink = true
                }),

            // Card Shuffle (c12183332): Continuous Spell. EFFECT_TYPE_IGNITION in the Spell & Trap Zone, SetCountLimit(1),
            // cost Cost.PayLP(300); at resolution (if this card is still there) SelectOption: shuffle your Deck or the
            // opponent's.
            new AtomRegistration.Atom(
                @"^\s*Pay (\d+) Life Points to use this card['’]s effect\.\s*Shuffle either your or your opponent['’]s " +
                @"Deck\.\s*You can only use this effect during your turn, once per turn\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpFieldIgnition,
                    Action = EffectActionKind.ShuffleYourOrOpponentsDeck,
                    Side = EffectSide.Either,
                    PayLpAmount = Parse(m, 1, 300),
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
