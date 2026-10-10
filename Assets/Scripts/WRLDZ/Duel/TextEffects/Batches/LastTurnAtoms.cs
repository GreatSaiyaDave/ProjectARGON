namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "LastTurn" (v163, LOD): Last Turn's whole printed text, anchored to its start and its end, so a longer
    /// or reworded text stays uncompiled. Runtime: <see cref="LastTurnRuntime"/>.
    /// </summary>
    public static class LastTurnAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.\s*$";
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Last Turn (c28566710): ACTIVATE, EVENT_FREE_CHAIN; condition GetLP(tp) <= 1000 and GetTurnPlayer() ~= tp;
            // target check: tp controls a monster (no card target). Operation: tp selects 1 monster in its Monster Zone;
            // every other card in both hands and on both fields goes to the GY in one SendtoGrave (REASON_EFFECT); then
            // (BreakEffect) 1-tp selects 1 monster in its Deck that can be Special Summoned, Special Summons it in face-up
            // Attack Position, EFFECT_AVOID_BATTLE_DAMAGE for both players until the end of the Damage Step, and
            // ForceAttack(it, the selected monster). An EVENT_PHASE + PHASE_END continuous effect (count limit 1, this
            // turn): the player who alone has monsters wins (WIN_REASON_LAST_TURN), otherwise Duel.Win(PLAYER_NONE).
            new AtomRegistration.Atom(
                Begin + @"This card can only be activated during your opponent" + Apos + @"s turn when your Life Points " +
                @"are (\d+) or less\.\s*Select 1 monster on your side of the field and send all other cards on the " +
                @"field and in their respective owners" + Apos + @" hands to their respective (?:GYs|Graveyards)\.\s*" +
                @"After that, your opponent selects and Special Summons 1 monster from their Deck in face-up Attack " +
                @"Position and attacks your selected monster\.\s*\(Any Battle Damage from this battle is treated as " +
                @"0\.\)\s*The player whose monster remains alone on the field at the End Phase of this turn wins the " +
                @"Duel\.\s*Any other case results in a DRAW" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SelectedMonsterShowdownLoneMonsterWins,
                    Side = EffectSide.Controller,
                    ActivationCondition = LastTurnRuntime.OpponentTurnYourLpAtMost,
                    ConditionAmount = int.TryParse(m.Groups[1].Value, out var lp) && lp > 0 ? lp : 1000,
                    OpponentTurnOnly = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };
    }
}
