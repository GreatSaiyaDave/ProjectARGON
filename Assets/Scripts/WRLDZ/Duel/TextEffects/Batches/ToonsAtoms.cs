using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Toons batch (v142 curriculum, SRL): whole-sentence atoms for the Toon monster rules printed on Manga Ryu-Ran,
    /// Blue-Eyes Toon Dragon, Toon Summoned Skull and Toon Mermaid (official scripts c38369349 / c53183600 /
    /// c91842653 / c65458948 register the same effects). Every pattern is anchored to a sentence start and a
    /// sentence end, so a longer card that merely contains one of these phrases never compiles part of its text
    /// here. "Cannot be Normal Summoned/Set." stays with the compiler's structural summon-lock handling
    /// (PsctGrammar.BlocksNormalSummonOrSet). Runtime: <see cref="ToonsRuntime"/>.
    /// </summary>
    public static class ToonsAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // EFFECT_SPSUMMON_PROC from LOCATION_HAND + EnableReviveLimit: a face-up "Toon World" you control
            // (LOCATION_ONFIELD, any position of the zone), and either a free Monster Zone (Toon Mermaid) or
            // Duel.CheckReleaseGroup of 1 / 2 monsters you control that are released as the cost (Toon Summoned
            // Skull / Manga Ryu-Ran / Blue-Eyes Toon Dragon). Not Gate Guardian ("by Tributing "Sanga…"") and not
            // Lava Golem ("to your opponent's field").
            new AtomRegistration.Atom(
                Start + @"Must first be Special Summoned \(from your hand\)" +
                @"(?: by Tributing (\d+) monsters?)?, while you control ""([^""]+)""" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonFromHandProcedure,
                    Amount = m.Groups[1].Success ? Parse(m, 1, 0) : 0,
                    RequiresFaceUpName = m.Groups[2].Value,
                    RequiresControllerNamedCard = true,
                    FromHand = true,
                    MakesChainLink = false
                }),
            // EVENT_SPSUMMON_SUCCESS → EFFECT_CANNOT_ATTACK until the End Phase (RESETS_STANDARD_PHASE_END).
            // Not "Cannot attack the turn it is Summoned." (Toon Gemini Elf: every kind of Summon).
            new AtomRegistration.Atom(
                Start + @"Cannot attack the turn it is Special Summoned" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotAttackTurnSpecialSummoned,
                    MakesChainLink = false
                }),
            // EFFECT_ATTACK_COST: Duel.CheckLPCost(tp, 500) to declare, then Duel.PayLPCost — the attack cost Dark
            // Elf's sentence compiles to (checked by CanAttack, paid once per declaration after every legality
            // check, never on a refused attack; nothing while this card's effects are negated).
            new AtomRegistration.Atom(
                Start + @"You must pay (\d+) LP to declare an attack with this monster" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.PayLpToDeclareAttack,
                    Amount = Parse(m, 1, 500),
                    MakesChainLink = false
                }),
            // EFFECT_TYPE_FIELD + CONTINUOUS on EVENT_LEAVE_FIELD (range MZONE): a face-up card with that name on
            // either field left it by destruction (REASON_DESTROY) → Duel.Destroy(this card). No chain link.
            new AtomRegistration.Atom(
                Start + @"If ""([^""]+)"" on the field is destroyed, destroy this card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DestroyThisWhenNamedDestroyed,
                    NamedCard = m.Groups[1].Value,
                    MakesChainLink = false
                }),
            // EFFECT_DIRECT_ATTACK while the opponent controls no face-up Toon monster; otherwise
            // EFFECT_CANNOT_DIRECT_ATTACK and EFFECT_CANNOT_SELECT_BATTLE_TARGET for anything that is not a
            // face-up Toon monster.
            new AtomRegistration.Atom(
                Start + @"Can attack your opponent directly, unless they control a Toon monster, in which case " +
                @"this card must target a Toon monster for its attacks" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon,
                    MakesChainLink = false
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
