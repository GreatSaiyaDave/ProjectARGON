namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleDestroyedTriggers": battle-outcome monster triggers and battle protections printed in DCR / IOC
    /// wordings. Every atom is a whole sentence (or a whole card text), anchored at both ends, so a longer or reworded card
    /// stays uncompiled. Runtime: <see cref="BattleDestroyedTriggersRuntime"/>.
    /// <list type="bullet">
    /// <item>Kelbek (c54878498): SINGLE TRIGGER_F at EVENT_BATTLED — it was the attack target and the attacker is not
    /// destroyed by this battle: the attacker returns to the hand (no target). Wall of Illusion's clause, plus
    /// <see cref="EffectClause.RequiresBattledMonsterNotDestroyed"/>.</item>
    /// <item>Keldo (c80441106): EVENT_BATTLE_DESTROYED in the GY — target exactly 2 cards in the opponent's GY (only if 2
    /// can be targeted) and shuffle them into the Deck.</item>
    /// <item>Agido (c16135253): EVENT_BATTLE_DESTROYED in the GY — roll a die, then you can Special Summon 1 Fairy from your
    /// GY whose Level is the roll (Level 6 or higher on a 6).</item>
    /// <item>Kaiser Glider (c52824910): not destroyed by battle with a monster of equal ATK (Kishido Spirit's clause, on the
    /// monster itself); destroyed and sent to the GY: target 1 monster on the field, return it to the hand.</item>
    /// <item>Different Dimension Dragon (c50939127): not destroyed by Spell/Trap effects that do not target it; not destroyed
    /// by battle with a monster that has 1900 or less ATK.</item>
    /// <item>Shinato, King of a Higher Plane (c86327225): EVENT_BATTLE_DESTROYING — the destroyed monster was in Defense
    /// Position and is in the GY: burn its original ATK (its two Ritual sentences are absorbed by the compiler).</item>
    /// <item>Des Kangaroo (c78613627): EVENT_DAMAGE_STEP_END — attacked in Defense Position by a monster whose ATK is lower
    /// than its DEF: destroy the attacker (Thousand Needles' clause).</item>
    /// </list>
    /// </summary>
    public static class BattleDestroyedTriggersAtoms
    {
        const string S = @"(?<=^|\.\s)";
        const string E = @"(?:\.|$)(?=\s|$)";
        const string Gy = @"(?:GY|Graveyard)";
        const string Apos = @"['’]";

        static int Int(System.Text.RegularExpressions.Match m, int group) =>
            int.TryParse(m.Groups[group].Value, out var n) ? n : 0;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Kelbek (c54878498), its whole text. Lua: Duel.GetAttackTarget()==c and not the attacker STATUS_BATTLE_DESTROYED;
            // SendtoHand(attacker) if it still relates to the battle. "Damage calculation is applied normally" is the reminder
            // that the battle itself is unchanged (the trigger is after damage calculation).
            new AtomRegistration.Atom(
                @"^A monster that attacks this card returns to the hand\.\s*Damage calculation is applied normally\.?$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.AfterDamageCalculation,
                    Action = EffectActionKind.ReturnToHand,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.AttackingMonster,
                    RequiresThisIsAttackTarget = true,
                    RequiresBattledMonsterNotDestroyed = true,
                    MakesChainLink = true
                }),
            // Keldo (c80441106). Lua: SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED, in the GY by REASON_BATTLE; EFFECT_FLAG_CARD_TARGET:
            // exactly 2 cards in the opponent's GY when 2 or more can be targeted (else no target, nothing happens);
            // SendtoDeck(SEQ_DECKSHUFFLE) of the targets still in the GY.
            new AtomRegistration.Atom(
                S + @"When this card is destroyed and sent to the " + Gy + @" as a result of battle, select (\d+) cards from " +
                @"your opponent" + Apos + @"s " + Gy + @", add them to your opponent" + Apos + @"s Deck and the Deck is then " +
                @"shuffled" + E,
                m =>
                {
                    var n = Int(m, 1);
                    return n < 2
                        ? null
                        : new EffectClause
                        {
                            Timing = EffectTiming.SentFromFieldToGy,
                            Action = EffectActionKind.ShuffleTargetsFromOpponentGyIntoDeck,
                            Side = EffectSide.Opponent,
                            Zone = EffectZoneFilter.OppGyCards,
                            TargetCount = n,
                            RequiresThisDestroyedByBattle = true,
                            MakesChainLink = true
                        };
                }),
            // Agido (c16135253), its whole text. Lua: SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED, in the GY by REASON_BATTLE; no
            // free Monster Zone: nothing (no roll); TossDice; Fairy monsters in the GY (NecroValleyFilter) of Level = roll (a 6:
            // Level 6 or higher) that can be Special Summoned; SelectYesNo, then select 1 (not a target), POS_FACEUP.
            new AtomRegistration.Atom(
                @"^When this card is destroyed and sent to the " + Gy + @" as a result of battle, roll a six-sided die\.\s*" +
                @"You can Special Summon 1 (\w+)-Type monster from your " + Gy + @" whose Level is equal to the number " +
                @"rolled\.\s*\(If the result is 6, you can Special Summon a Level 6 or higher monster\.\)$",
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.RollDieSpecialSummonFromGyWithLevelOfRoll,
                    Side = EffectSide.Controller,
                    RaceFilter = m.Groups[1].Value,
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),
            // Kaiser Glider (c52824910), first sentence: e1 SINGLE EFFECT_INDESTRUCTABLE_BATTLE, value: the battling monster's
            // ATK equals this card's. The Kishido Spirit clause on the monster itself (DamageStepRulesRuntime.BattleIndestructible).
            new AtomRegistration.Atom(
                @"^Cannot be destroyed by battle with a monster that has the same ATK" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Kaiser Glider (c52824910), second sentence: e2 SINGLE TRIGGER_F EVENT_TO_GRAVE, IsReason(REASON_DESTROY) (battle
            // or card effect); EFFECT_FLAG_CARD_TARGET: 1 monster in either Monster Zone; SendtoHand if it still relates.
            new AtomRegistration.Atom(
                S + @"If this card is destroyed and sent to the " + Gy + @": Target 1 monster on the field; return that target " +
                @"to the hand" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.ReturnToHand,
                    Side = EffectSide.Either,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresDestroyed = true,
                    MakesChainLink = true
                }),
            // Different Dimension Dragon (c50939127), first sentence: e1 SINGLE EFFECT_INDESTRUCTABLE_EFFECT (range MZONE), value:
            // a Spell/Trap effect without EFFECT_FLAG_CARD_TARGET.
            new AtomRegistration.Atom(
                S + @"This card cannot be destroyed by Spell/Trap effects that do not target it" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Different Dimension Dragon (c50939127), second sentence: e2 SINGLE EFFECT_INDESTRUCTABLE_BATTLE, value:
            // IsAttackBelow(1900) of the battling monster.
            new AtomRegistration.Atom(
                S + @"This card cannot be destroyed by battle with a monster that has (\d+) or less ATK" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeDestroyedByBattleWithMonsterAtkAtMost,
                    Side = EffectSide.Controller,
                    Amount = Int(m, 1),
                    MakesChainLink = false
                }),
            // Shinato, King of a Higher Plane (c86327225), third sentence: SINGLE TRIGGER_F EVENT_BATTLE_DESTROYING; condition:
            // this card still relates to the battle, the battle target is a monster in the GY whose battle position was
            // Defense; Damage(1-tp, its GetBaseAttack()).
            new AtomRegistration.Atom(
                S + @"When this card destroys a Defense Position monster and sends it to the " + Gy + @" as a result of " +
                @"battle, inflict damage to your opponent" + Apos + @"s Life Points equal to the original ATK of the " +
                @"destroyed monster" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    AmountFromTargetOriginalAtk = true,
                    RequiresDestroyedWasDefensePosition = true,
                    MakesChainLink = true
                }),
            // Des Kangaroo (c78613627), its whole text. Lua: SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END — this card is the attack
            // target, its battle position Defense (face-up or face-down), the attacker's ATK lower than its DEF; Destroy the
            // attacker if it still relates to the battle. Thousand Needles' clause (DamageStepRulesRuntime.TryApplyDestroyAttacker).
            new AtomRegistration.Atom(
                @"^If the ATK of a monster that attacks this monster in Defense Position is lower than the DEF of this card, " +
                @"destroy the attacking monster\.\s*\(Damage calculation is applied normally\.\)$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.DestroyAttackerAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.AttackingMonster,
                    MakesChainLink = true
                }),
        };
    }
}
