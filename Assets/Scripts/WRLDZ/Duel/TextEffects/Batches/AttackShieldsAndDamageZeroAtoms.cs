using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// AttackShieldsAndDamageZero batch atoms (whole sentences). Runtime: <see cref="AttackShieldsAndDamageZeroRuntime"/>
    /// (conditions, damage calculation, Special Summon hook), ContinuousProtections.CannotBeAttackTarget (the shield) and
    /// FieldSpellEffects (Theban Nightmare's gain, Goblin King's ATK/DEF).
    /// Arcane Archer of the Forest's Tribute-a-Type destroy is the Orca Mega-Fortress template
    /// (LegacyTextTemplates.RxTributeNamedDestroy), which accepts a Type word.
    /// </summary>
    public static class AttackShieldsAndDamageZeroAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";

        /// <summary>The monster Types, as printed (two-word Types before their first word).</summary>
        const string Race =
            @"(Aqua|Beast-Warrior|Winged Beast|Beast|Creator God|Cyberse|Dinosaur|Divine-Beast|Dragon|Fairy|Fiend|Fish|" +
            @"Illusion|Insect|Machine|Plant|Psychic|Pyro|Reptile|Rock|Sea Serpent|Spellcaster|Thunder|Warrior|Wyrm|Zombie)";

        /// <summary>The three printed forms of "this card cannot be selected as an attack target".</summary>
        const string NoAttack =
            @"(?:this card cannot be attacked|your opponent cannot target this card for attacks|" +
            @"your opponent cannot select this card as an attack target)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Solar Flare Dragon / Goblin King (c45985838, c18590133: EFFECT_CANNOT_BE_BATTLE_TARGET, condition a face-up
            // monster of the Type you control other than this card, aux.imval1) and Soul-Absorbing Bone Tower (c63012333,
            // the same with aux.imval2). The opponent may still not attack directly (AllowsDirectAttackWhileProtected false).
            new AtomRegistration.Atom(
                Start + @"(?:While|If) you control another " + Race + @"(?:-Type)? monster, " + NoAttack + End,
                m => Shield(AttackShieldsAndDamageZeroRuntime.YouControlAnotherFaceUpRace, m.Groups[1].Value, 0)),

            // Arcane Archer of the Forest (c55001420: s.ccon a face-up Plant you control, this card included).
            new AtomRegistration.Atom(
                Start + @"While there is an? " + Race + @"(?:-Type)? monster on your side of the field, " + NoAttack + End,
                m => Shield(AttackShieldsAndDamageZeroRuntime.YouControlFaceUpRace, m.Groups[1].Value, 0)),

            // Emissary of the Oasis (c6103294: s.cfilter1 face-up TYPE_NORMAL Level 3 or lower you control).
            new AtomRegistration.Atom(
                Start + @"While a face-up Level (\d) or lower Normal Monster is on your side of the field, " + NoAttack + End,
                m => Shield(AttackShieldsAndDamageZeroRuntime.YouControlFaceUpLowLevelNormal, null, Parse(m, 1))),

            // Emissary of the Oasis (e2 AVOID_BATTLE_DAMAGE on your Level 3 or lower Normal Monsters, e3 NO_BATTLE_DAMAGE on
            // your opponent's): its controller takes no battle damage from a battle involving one; the opponent still does.
            new AtomRegistration.Atom(
                Start + @"While this card is face-up on your side of the field, any Battle Damage to the controller of this " +
                @"card from (?:a )?battle involving a Level (\d) or lower Normal Monster becomes 0" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AvoidBattleDamageFromBattlesInvolving,
                    Side = EffectSide.Controller,
                    NormalMonsterMaxLevel = Parse(m, 1),
                    MakesChainLink = false
                }),

            // The Sanctuary in the Sky (c56433456: FZONE AVOID_BATTLE_DAMAGE on every Fairy on both fields).
            new AtomRegistration.Atom(
                Start + @"Battle [Dd]amage to the controller of an? " + Race + @"(?:-Type)? monster from (?:a )?battle " +
                @"involving that monster becomes 0" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AvoidBattleDamageFromBattlesInvolving,
                    Side = EffectSide.Both,
                    RaceFilter = m.Groups[1].Value,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Soul-Absorbing Bone Tower (e2 FIELD TRIGGER_F EVENT_SPSUMMON_SUCCESS: a face-up Zombie in eg, not this card;
            // DiscardDeck(1-tp, 2)).
            new AtomRegistration.Atom(
                Start + @"Each time an? " + Race + @"(?:-Type)? monster\(s\) (?:is|are) Special Summoned: Send the top (\d+) " +
                @"cards of your opponent's Deck to the (?:Graveyard|GY)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.MillOpponentWhenRaceSpecialSummoned,
                    Side = EffectSide.Opponent,
                    RaceFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2),
                    MakesChainLink = false
                }),

            // Goblin King (EFFECT_SET_ATTACK / SET_DEFENSE: face-up Fiends on both fields except this card x 1000).
            new AtomRegistration.Atom(
                Start + @"The ATK and DEF of this card are each equal to the number of " + Race + @"(?:-Type)? monsters on " +
                @"the field \(excluding this card\) x (\d+)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 2),
                    DefAmount = Parse(m, 2),
                    RaceFilter = m.Groups[1].Value,
                    CountSource = AttackShieldsAndDamageZeroRuntime.FaceUpRaceOnFieldExceptThis,
                    SelfStatIsSet = true,
                    MakesChainLink = false
                }),

            // Theban Nightmare (c51838385: UPDATE_ATTACK 1500 while hand + Spell & Trap Zone cards (sequence < 5) == 0).
            new AtomRegistration.Atom(
                Start + @"Gains (\d+) ATK while you have no cards in your hand nor in your Spell & Trap Zones" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1),
                    DefAmount = 0,
                    AffectsThisCardOnly = true,
                    WhileCondition = AttackShieldsAndDamageZeroRuntime.NoCardsInHandOrSpellTrapZones,
                    MakesChainLink = false
                }),
        };

        static EffectClause Shield(string condition, string race, int level) => new()
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = EffectActionKind.CannotBeAttackTarget,
            WhileCondition = condition,
            ConditionName = race,
            ConditionAmount = level,
            AllowsDirectAttackWhileProtected = false,
            MakesChainLink = false
        };

        static int Parse(Match m, int group) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n) ? n : 0;
    }
}
