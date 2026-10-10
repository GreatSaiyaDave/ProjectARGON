using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PerCountBurns" (v141): burns that deal a printed amount for each counted card, counted when
    /// the effect resolves. Each atom is one whole sentence (start of text or after a sentence end, through
    /// its final period), so a longer card that only contains part of it stays uncompiled.
    /// The count comes from <see cref="EffectClause.CountSource"/> (see <see cref="PerCountBurnsRuntime"/>);
    /// a card with nothing to count cannot be activated (Lua target chk), via
    /// <see cref="EffectClause.ActivationCondition"/>.
    /// </summary>
    public static class PerCountBurnsAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>
        /// Sentence end: the final period followed by whitespace or the end of the text, or the end of the text
        /// itself (no period). A longer sentence that only starts like an atom ("… in their GY x 2.") never
        /// matches, so its tail cannot be dropped.
        /// </summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Princess of Tsurugi (c51371017): FLIP, 500 x every card in the opponent's Spell & Trap Zones
            // (LOCATION_SZONE: face-down Sets, the Field Zone and Pendulum Zones included), counted at
            // resolution. No activation condition (target chk is always true).
            new AtomRegistration.Atom(
                S + @"FLIP:\s*Inflict (\d+) (?:points of )?damage to your opponent for each Spell and Trap Card " +
                @"on your opponent's side of the field" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 500),
                    CountSource = PerCountBurnsRuntime.OppSpellTrapCards,
                    MakesChainLink = true
                }),
            // D.D. Dynamite (c8628798): 300 x the opponent's banished cards (face-up or face-down), counted at
            // resolution; cannot be activated while they have none.
            new AtomRegistration.Atom(
                S + @"Inflict (\d+) (?:points of )?damage to your opponent for each of their " +
                @"(?:removed from play|banished) cards" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 300),
                    CountSource = PerCountBurnsRuntime.OppBanishedCards,
                    ActivationCondition = PerCountBurnsRuntime.OppBanishedAtLeast,
                    ConditionAmount = 1,
                    MakesChainLink = true
                }),
            // Cemetary Bomb (c51394546): 100 x cards in the opponent's GY, counted at resolution; cannot be
            // activated while their GY is empty.
            new AtomRegistration.Atom(
                S + @"Inflict (\d+) (?:points of )?damage to your opponent for each card in their " +
                @"(?:Graveyard|GY)" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 100),
                    CountSource = PerCountBurnsRuntime.OppGyCards,
                    ActivationCondition = PerCountBurnsRuntime.OppGyAtLeast,
                    ConditionAmount = 1,
                    MakesChainLink = true
                }),
            // Thunder Crash (c69196160): one sentence, two clauses in resolution order. Destroy every monster
            // you control (face-down too, not a target), and if you do, 300 x the monsters actually destroyed
            // (Duel.Destroy's return: an unaffected / indestructible monster is not counted). Needs a monster
            // you control to activate.
            AtomRegistration.Atom.Sequence(
                S + @"Destroy all monsters you control, and if you do, inflict (\d+) (?:points of )?damage " +
                @"to your opponent for each monster destroyed" + E,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Zone = EffectZoneFilter.FieldMonsters,
                        Side = EffectSide.Controller,
                        ActivationCondition = PerCountBurnsRuntime.YouControlMonstersAtLeast,
                        ConditionAmount = 1,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 1, 300),
                        CountSource = PerCountBurnsRuntime.PreviousClauseHits,
                        RequiresPreviousClauseHit = true,
                        MakesChainLink = true
                    }
                }),
            // Wave-Motion Cannon (c38992735): Continuous Spell. Its activation only resets the turn counter; each
            // of the controller's Standby Phases while it is face-up adds 1 (PerCountBurnsRuntime.
            // TickStandbyTurnCounters). Ignition from the field in your Main Phase, once the counter is above 0:
            // send this face-up card to the GY (cost); inflict 1000 x the counter, fixed at activation.
            new AtomRegistration.Atom(
                S + @"During your Main Phase:\s*You can send this face-up card to the (?:GY|Graveyard);\s*" +
                @"inflict (\d+) (?:points of )?damage to your opponent for each of your Standby Phases that have " +
                @"passed since this card was activated" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpFieldIgnition,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 1000),
                    CountSource = PerCountBurnsRuntime.ThisCardTurnCounter,
                    RequiresSendThisToGy = true,
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
