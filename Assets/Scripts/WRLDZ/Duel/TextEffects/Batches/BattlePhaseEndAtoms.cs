using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattlePhaseEnd" (v142): end-of-Battle-Phase and battle-aftermath effects — Goblin Attack Force
    /// and Giant Orc (defense after attacking + position lock), Steel Scorpion (delayed destruction of its
    /// attacker) and Reflect Bounder (burn before damage calculation, then destroy itself). Each atom is the
    /// card's whole text (start of text or after a sentence end, through its final period), so a longer card
    /// that only contains part of it stays uncompiled. Runtime: <see cref="BattlePhaseEndRuntime"/>.
    /// Sword Hunter and Vampire Baby are deferred (no atom): the engine has no "monster Card treated as an Equip
    /// Spell" state, and no optional end-of-Battle-Phase prompt for a Special Summon the player chooses.
    /// </summary>
    public static class BattlePhaseEndAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Goblin Attack Force (c78658564): EVENT_PHASE+PHASE_BATTLE continuous effect, condition
            // GetAttackedCount() > 0: to face-up Defense if in Attack Position, then EFFECT_CANNOT_CHANGE_POSITION
            // until the 3rd End Phase (the End Phase of your next turn). Not a chain link.
            new AtomRegistration.Atom(
                S + @"If this card attacks, it is changed to Defense Position at the end of the Battle Phase, " +
                @"and its battle position cannot be changed until the End Phase of your next turn" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfBattlePhase,
                    Action = EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition,
                    MakesChainLink = false
                }),
            // Giant Orc (c73698349): the same Lua as Goblin Attack Force, older wording in two sentences.
            new AtomRegistration.Atom(
                S + @"If this card attacks, it is changed to Defense Position at the end of the Battle Phase\. " +
                @"You cannot change this battle position until the end of your next turn" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfBattlePhase,
                    Action = EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition,
                    MakesChainLink = false
                }),
            // Steel Scorpion (c13599884): EVENT_BATTLED trigger while it is the attack target of a non-Machine
            // monster; the attacker is destroyed at the End Phase of this card's controller's opponent's 2nd turn
            // after the attack (a counter on the attacker, EFFECT_FLAG_CANNOT_DISABLE, kept after this card leaves).
            new AtomRegistration.Atom(
                S + @"A non ([A-Za-z]+(?: [A-Za-z]+)?)-Type Monster attacking ""[^""]+"" will be destroyed at the " +
                @"End Phase of your opponent's (\d+)(?:st|nd|rd|th) turn after the attack" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.AfterDamageCalculation,
                    Action = EffectActionKind.DestroyAttackerAtOpponentsLaterEndPhase,
                    Zone = EffectZoneFilter.AttackingMonster,
                    ExcludeRaceFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2, 2),
                    RequiresThisIsAttackTarget = true,
                    MakesChainLink = true
                }),
            // Reflect Bounder (c2851070): EVENT_BATTLE_CONFIRM trigger while it is the attack target in face-up
            // Attack Position: burn the attacking monster's ATK to the opponent; if their LP went down, destroy
            // this card after damage calculation (EVENT_BATTLED).
            new AtomRegistration.Atom(
                S + @"Before damage calculation, if this card is being attacked by an opponent's monster, and was in " +
                @"face-up Attack Position at the start of the Damage Step: Inflict damage to your opponent equal to " +
                @"the attacking monster's ATK\. If you do, after damage calculation: Destroy this card" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.BeforeDamageCalculation,
                    Action = EffectActionKind.InflictDamageEqualToAtk,
                    Zone = EffectZoneFilter.AttackingMonster,
                    Side = EffectSide.Opponent,
                    RequiresThisIsAttackTarget = true,
                    RequiresThisAttackPosition = true,
                    DestroyThisAfterDamageCalculation = true,
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
