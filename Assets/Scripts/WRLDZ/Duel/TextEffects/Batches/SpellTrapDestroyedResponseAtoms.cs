using System;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellTrapDestroyedResponse" (v142). Each atom is a card's whole text (anchored at both ends),
    /// so a longer or reworded card stays uncompiled. Runtime: <see cref="SpellTrapDestroyedResponseRuntime"/>.
    /// <list type="bullet">
    /// <item>Gust (73079365) / Driving Snow (473469): Normal Traps (ygopro EFFECT_TYPE_ACTIVATE on EVENT_TO_GRAVE,
    /// EFFECT_FLAG_CARD_TARGET) — activated when 1 or more of your Spell / Trap Cards were destroyed by a card effect
    /// your opponent controls and sent from the field to the GY; target 1 Spell/Trap on the field other than this card
    /// and destroy it. "You can activate" / "You can only activate" are the same activation condition.</item>
    /// <item>Weather Report (72053645): FLIP — destroy all of the opponent's face-up copies of the named card (not
    /// targeted); if one is destroyed, the controller may conduct their Battle Phase twice (EFFECT_BP_TWICE).</item>
    /// </list>
    /// </summary>
    public static class SpellTrapDestroyedResponseAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            new AtomRegistration.Atom(
                @"^\s*You can (?:only )?activate this card when 1 or more of your (Spell|Trap) Cards are destroyed " +
                @"and sent from the field to the (?:Graveyard|GY) by a card effect your opponent controls\. " +
                @"Destroy 1 Spell or Trap Card on the field\.?\s*$",
                m => DestroyedResponse(m.Groups[1].Value)),
            AtomRegistration.Atom.Sequence(
                @"^\s*FLIP:\s*Destroys all of your opponent['’]s face-up ""([^""]+)"" on the field\. " +
                @"If ""\1"" is destroyed, you can perform your Battle Phase twice this turn " +
                @"\(or your next turn, if activated during your opponent['’]s turn\)\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.Destroy,
                        Zone = EffectZoneFilter.FaceUpNamedSpellTraps,
                        Side = EffectSide.Opponent,
                        NamedCard = m.Groups[1].Value,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.ConductBattlePhaseTwice,
                        Side = EffectSide.Controller,
                        RequiresPreviousClauseHit = true,
                        MakesChainLink = true
                    }
                }),
        };

        /// <summary>
        /// Activate (Normal Trap, Spell Speed 2): only in the window after 1+ of your <paramref name="kind"/> Cards
        /// were destroyed by an opponent's card effect; targets 1 Spell/Trap on the field other than this card.
        /// </summary>
        static EffectClause DestroyedResponse(string kind)
        {
            var k = string.Equals(kind, SpellTrapDestroyedResponseRuntime.SpellCards, StringComparison.OrdinalIgnoreCase)
                ? SpellTrapDestroyedResponseRuntime.SpellCards
                : SpellTrapDestroyedResponseRuntime.TrapCards;
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldSpellTraps,
                Side = EffectSide.Opponent,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                ExceptThisCard = true,
                ActivationCondition = SpellTrapDestroyedResponseRuntime.YourCardsDestroyedByOpponentEffect,
                ConditionName = k,
                CheckedAt = ConditionCheckedAt.Activation,
                MakesChainLink = true
            };
        }
    }
}
