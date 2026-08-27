namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Official effect classifications (Yugipedia / Rulebook).
    /// Used for timing, SEGOC ordering, and activation legality.
    /// </summary>
    public enum EffectClass
    {
        None = 0,
        /// <summary>Ignition Effect — Main Phase, turn player only (Spell Speed 1).</summary>
        Ignition,
        /// <summary>Trigger Effect — when condition is met (Spell Speed 1; optional/mandatory).</summary>
        Trigger,
        /// <summary>Trigger-like (Continuous Trap, etc.) — same windows as Trigger.</summary>
        TriggerLike,
        /// <summary>Quick Effect — Spell Speed 2 (monsters, some Continuous Spells).</summary>
        Quick,
        /// <summary>Continuous Effect — applies while face-up; no activation (Spell Speed N/A).</summary>
        Continuous,
        /// <summary>Unclassified / condition text (not an activated effect).</summary>
        Unclassified,
        /// <summary>Summoning condition / procedure text.</summary>
        SummoningCondition
    }

    /// <summary>Official Spell Speeds (Rulebook).</summary>
    public enum SpellSpeed
    {
        /// <summary>Not an activated effect (Continuous, conditions).</summary>
        None = 0,
        Speed1 = 1,
        Speed2 = 2,
        Speed3 = 3
    }

    public enum CardLocation
    {
        Unknown = 0,
        Deck,
        Hand,
        MonsterZone,
        SpellTrapZone,
        FieldZone,
        PendulumZone,
        Graveyard,
        Banished,
        ExtraDeck,
        // Token only exists on field
    }

    public enum SummonKind
    {
        None = 0,
        NormalSummon,
        NormalSet,
        TributeSummon,
        TributeSet,
        FlipSummon,
        SpecialSummon,
        FusionSummon,
        RitualSummon,
        SynchroSummon,
        XyzSummon,
        PendulumSummon,
        LinkSummon
    }

    /// <summary>
    /// Battle Phase steps (official Rulebook / Yugipedia "Battle Phase").
    /// </summary>
    public enum BattleStep
    {
        /// <summary>Not in Battle Phase.</summary>
        None = 0,
        /// <summary>Start Step — effects that activate here; no attacks yet.</summary>
        StartStep,
        /// <summary>Battle Step — declare attack; Fast Effects; possible replay.</summary>
        BattleStep,
        /// <summary>Damage Step — sub-steps below.</summary>
        DamageStep,
        /// <summary>End Step — after all attacks; before leaving Battle Phase.</summary>
        EndStep
    }

    /// <summary>
    /// Damage Step sub-structure (Yugipedia "Damage Step").
    /// Only certain effects are legal in each sub-step.
    /// </summary>
    public enum DamageSubStep
    {
        None = 0,
        /// <summary>Start of Damage Step — face-down target flips here before calculation.</summary>
        Start,
        /// <summary>Before damage calculation — ATK/DEF modifiers that apply here.</summary>
        BeforeDamageCalculation,
        /// <summary>Perform damage calculation.</summary>
        DamageCalculation,
        /// <summary>After damage calculation — before sending to GY.</summary>
        AfterDamageCalculation,
        /// <summary>End of Damage Step — cards destroyed by battle leave; end-of-DS triggers.</summary>
        End
    }

    /// <summary>Chain resolution event for continuous / trigger bookkeeping.</summary>
    public enum ChainEvent
    {
        None = 0,
        Activation,
        Resolution,
        Negation,
        SummonAttempt,
        SummonSuccess,
        SummonNegated,
        AttackDeclared,
        AttackNegated,
        BattleDamageApplied,
        DestroyedByBattle,
        DestroyedByEffect,
        SentToGrave,
        PhaseStart,
        PhaseEnd
    }

    /// <summary>Field zone kinds for rules validation (independent of AR layer).</summary>
    public enum RulesZoneKind
    {
        Monster = 0,
        SpellTrap = 1,
        FieldSpell = 2,
        PendulumLeft = 3,
        PendulumRight = 4
    }
}
