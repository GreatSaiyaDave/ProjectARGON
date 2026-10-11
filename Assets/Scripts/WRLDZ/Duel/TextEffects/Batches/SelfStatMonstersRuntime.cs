namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SelfStatMonsters" (v153). Atoms: <see cref="SelfStatMonstersAtoms"/>.
    /// <list type="bullet">
    /// <item>Nuvia the Wicked: <see cref="OppMonsterCount"/>, the GainSelfAtkPerCount source that counts every monster
    /// the opponent controls, face-down included (FieldSpellEffects.ApplySelfScale).</item>
    /// <item>Zombyra the Dark: <see cref="ForbidsDirectAttack"/> (<see cref="EffectActionKind.CannotAttackDirectly"/>,
    /// read by DuelEngine.CanAttackDirectly / TryAttack, RulesValidator and SimpleAi). The "loses ATK" battle
    /// sentence is not compiled and has no grant.</item>
    /// </list>
    /// The Battle Phase auras (The Rock Spirit, Spirit of Flames, Soul of Purity and Light) are ContinuousGainAtkDef
    /// clauses applied by FieldSpellEffects.ApplyStatClause on every board refresh.
    /// </summary>
    public static class SelfStatMonstersRuntime
    {
        /// <summary>
        /// GainSelfAtkPerCount <see cref="EffectClause.CountSource"/>: every monster the opponent of this card's
        /// controller controls, face-up or face-down (Nuvia the Wicked; Lua Duel.GetFieldGroupCount(tp, 0, LOCATION_MZONE)).
        /// </summary>
        public const string OppMonsterCount = "OppMonsterCount";

        /// <summary>
        /// EFFECT_CANNOT_DIRECT_ATTACK: <paramref name="attacker"/> is a face-up monster whose effects are not negated
        /// and whose FullyCompiled program carries <see cref="EffectActionKind.CannotAttackDirectly"/>. It then cannot
        /// declare a direct attack, whatever else would allow one.
        /// </summary>
        public static bool ForbidsDirectAttack(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker?.Def == null || !attacker.Def.IsMonster) return false;
            // PhaseTriggersAndIgnitionCosts batch: a Fusion Monster Magical Scientist Special Summoned (a grant from another
            // card, so the monster's own negation does not end it).
            if (PhaseTriggersAndIgnitionCostsRuntime.ForbidsDirectAttack(engine, attacker)) return true;
            if (!attacker.FaceUp || attacker.IsNegated) return false;
            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == EffectActionKind.CannotAttackDirectly)
                    return true;
            return false;
        }
    }
}
