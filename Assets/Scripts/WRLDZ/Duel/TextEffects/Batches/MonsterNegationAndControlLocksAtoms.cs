namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// MonsterNegationAndControlLocks batch: field-wide monster negation (Skill Drain), Spell immunity of non-Effect
    /// monsters (Non-Spellcasting Area), the control lock (Blindly Loyal Goblin, Mataza the Zapper), the Attack Position
    /// lock (Final Attack Orders) and the summon-time Defense change (Stumbling). Whole sentences only: every pattern
    /// starts at a sentence boundary and ends at one, and Skill Drain is matched as the whole card.
    /// Runtime: <see cref="MonsterNegationAndControlLocksRuntime"/> and <see cref="ContinuousNegationRuntime"/>.
    /// </summary>
    public static class MonsterNegationAndControlLocksAtoms
    {
        /// <summary>Sentence start: text start or after a sentence-ending mark.</summary>
        const string Start = @"(?<=^|[.!?]\s+)";
        /// <summary>Sentence end: a period, or the end of the text.</summary>
        const string End = @"(?:\.|\s*$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Skill Drain (the whole card): an LP activation cost (paid on activation, ActivationLpCostRuntime) and a
            // continuous negation of every face-up Effect Monster on the field while this card is face-up.
            AtomRegistration.Atom.Sequence(
                @"^\s*Activate by paying (\d+) (?:LP|Life Points)\.\s*" +
                @"Negate the effects of all face-up monsters while they are face-up on the field " +
                @"\(but their effects can still be activated\)\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        PayLpAmount = int.Parse(m.Groups[1].Value),
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ContinuousNegateMonsterEffectsOnField,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // Final Attack Orders: every face-up monster on the field is in Attack Position and locked there.
            new AtomRegistration.Atom(
                Start + @"All face-up monsters on the field are changed to Attack Position and their battle positions " +
                @"cannot be changed" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousForceAttackLockPosition,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Blindly Loyal Goblin ("As long as this card remains face-up on the field, control of this card cannot
            // switch") and Mataza the Zapper / Horus the Black Flame Dragon LV4 ("Control of this face-up card [on
            // the field] cannot switch" — the "on the field" phrase is optional; Horus prints the short form).
            new AtomRegistration.Atom(
                Start + @"(?:As long as this card remains face-up on the field, control of this card|" +
                @"Control of this face-up card(?: on the field)?) cannot switch" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ControlOfThisCannotSwitch,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Stumbling: a mandatory trigger from the Spell & Trap Zone on each Normal / Flip / Special Summon.
            new AtomRegistration.Atom(
                Start + @"Any monster that is Normal Summoned, Flip Summoned,? or Special Summoned is changed to " +
                @"Defense Position" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ChangeSummonedMonstersToDefense,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Non-Spellcasting Area: face-up non-Effect monsters are unaffected by other Spell effects.
            new AtomRegistration.Atom(
                Start + @"All face-up monsters, except for Effect Monsters, are unaffected by other Spell effects" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NonEffectMonstersUnaffectedByOtherSpells,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
        };
    }
}
