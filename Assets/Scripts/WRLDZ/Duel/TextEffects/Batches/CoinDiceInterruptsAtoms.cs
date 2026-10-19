namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CoinDiceInterrupts" (v161 LOD-b): whole-text atoms, not a passcode list.
    /// <list type="bullet">
    /// <item>"When a coin toss is performed, you can choose to redo the coin toss. (If the toss involved multiple coin
    /// flips, redo all of them.) You can only use the effect of "X" once per turn." (Second Coin Toss, Continuous Spell:
    /// a <see cref="EffectTiming.ContinuousWhileFaceUp"/> marker read on each of its controller's coin tosses.)</item>
    /// <item>"One time, during the rest of this turn, if either player(s) rolls a die (or dice), you can have the
    /// player(s) re-roll. (You can only gain this effect once per turn.)" (Dice Re-Roll, Normal Trap.)</item>
    /// </list>
    /// Runtime: <see cref="CoinDiceInterruptsRuntime"/>.
    /// </summary>
    public static class CoinDiceInterruptsAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Second Coin Toss (c36562627): EVENT_TOSS_COIN_NEGATE, SetRange(LOCATION_SZONE), condition ep==tp and no
            // flag; the activation itself (EVENT_FREE_CHAIN) does nothing else.
            new AtomRegistration.Atom(
                @"^\s*When a coin toss is performed, you can choose to redo the coin toss\.\s*\(If the toss involved " +
                @"multiple coin flips, redo all of them\.\)\s*You can only use the effect of ""[^""]+"" once per turn\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.RedoOwnCoinTossOncePerTurn,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    // OncePerTurnHard batch: the printed hard once per turn (Lua player flag (tp, id)); the redo itself is
                    // limited per player by CoinDiceInterruptsRuntime (CoinRedoUsedTurn).
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerCardName,
                    MakesChainLink = false
                }),

            // Dice Re-Roll (c83241722): EVENT_FREE_CHAIN activation registers EVENT_TOSS_DICE_NEGATE for tp until the End
            // Phase (any player's roll; Duel.TossDice(ep, ...) re-rolls them all), flag set when it is used.
            new AtomRegistration.Atom(
                @"^\s*One time, during the rest of this turn, if either player\(s\) rolls a die \(or dice\), you can " +
                @"have the player\(s\) re-roll\.\s*\(You can only gain this effect once per turn\.\)\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GrantDiceReRollThisTurn,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
        };
    }
}
