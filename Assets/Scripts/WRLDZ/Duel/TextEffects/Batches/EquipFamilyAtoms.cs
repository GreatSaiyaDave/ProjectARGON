using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipFamily batch: Equip declarations and Equip moves. Each atom is the card's whole text (sentence shapes, not a
    /// passcode list); the runtime is <see cref="EquipFamilyRuntime"/>.
    /// <list type="bullet">
    /// <item>Dragonic Attack (c32437102): equip only to a Type monster (either field, Lua aux.AddEquipProcedure with no
    /// player); the equipped monster is treated as another Type (EFFECT_CHANGE_RACE) and gains N ATK and DEF.</item>
    /// <item>Scroll of Bewitchment (c10352095): equip any face-up monster; declare an Attribute other than its current one
    /// in the equip target step (AnnounceAnotherAttribute); the equipped monster has that Attribute.</item>
    /// <item>Collected Power (c7565547): target 1 face-up monster on the field; every face-up Equip Card on the field is
    /// equipped to it when it is a correct target, otherwise destroyed.</item>
    /// <item>Dark Necrofear (c31829185): its hand procedure (banish N monsters of a Type from the GY), and its End Phase
    /// trigger in the GY that equips it to an opponent's monster and takes control of that monster.</item>
    /// </list>
    /// </summary>
    public static class EquipFamilyAtoms
    {
        /// <summary>A Monster Type in old-print form ("Warrior", "Beast-Warrior", "Winged Beast").</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";

        /// <summary>The classic Monster Types (a Type, never an Attribute).</summary>
        const string MonsterTypes =
            "Aqua|Beast-Warrior|Beast|Dinosaur|Dragon|Fairy|Fiend|Fish|Insect|Machine|Plant|Psychic|Pyro|Reptile|" +
            "Rock|Sea Serpent|Spellcaster|Thunder|Warrior|Winged Beast|Zombie|Wyrm|Cyberse|Divine-Beast";

        const string TypeWord = @"([A-Za-z]+(?: [A-Za-z]+)?(?:-(?!Type\b)[A-Za-z]+)*)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            new AtomRegistration.Atom(
                @"^\s*This card can only be used to equip " + TypeWord + @"-Type monsters\.\s*" +
                @"The equipped monster is treated as (?:an? )?" + TypeWord + @"-Type and increases the ATK and DEF of " +
                @"the equipped monster by (\d+) points\.?\s*$",
                m =>
                {
                    var n = Pos(m, 3);
                    if (n <= 0) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        // aux.AddEquipProcedure(c, nil, …): a face-up monster on either field.
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        RaceFilter = m.Groups[1].Value.Trim(),
                        EquipChangesRaceTo = m.Groups[2].Value.Trim(),
                        EquipAtkBonus = n,
                        EquipDefBonus = n,
                        StaysOnField = true,
                        MakesChainLink = true
                    };
                }),

            new AtomRegistration.Atom(
                @"^\s*Select 1 Attribute\.\s*Change the Attribute of the equipped monster to the one you select\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    EquipDeclaresAnotherAttribute = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            new AtomRegistration.Atom(
                @"^\s*Target 1 face-up monster on the field;\s*equip it with all Equip Cards on the field\.\s*" +
                @"If any of those Equip Cards is now equipped to an incorrect target, destroy that Equip Card\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipAllEquipCardsToTarget,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    // Lua chk: a face-up TYPE_EQUIP card in a Spell & Trap Zone (either player).
                    ActivationCondition = EquipFamilyRuntime.FaceUpEquipCardOnField,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Dark Necrofear's procedure (Lua EFFECT_SPSUMMON_PROC from the hand: banish N monsters of a Type from
            // the GY as the cost). A Fenrir-style hand procedure that does not use the chain (The Fiend Megacyber).
            // A Type, not an Attribute: the Attribute variant (Gigantes / Spirit of Flames / Inferno) is
            // EngineHygieneAtoms' procedure clause (DuelEngine.CanSpecialSummonProcedure).
            new AtomRegistration.Atom(
                Start + @"Must first be Special Summoned \(from your hand\) by banishing (\d+) (" + MonsterTypes +
                @") monsters? from your (?:GY|Graveyard)" + End,
                m =>
                {
                    var n = Pos(m, 1);
                    if (n <= 0) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.SpecialSummonThisFromHand,
                        ActivatesFromHand = true,
                        BanishFromGyCount = n,
                        RaceFilter = m.Groups[2].Value,
                        IsOptional = true,
                        MakesChainLink = false
                    };
                }),

            // Dark Necrofear: mandatory End Phase trigger in the GY (either player's End Phase, once per turn).
            new AtomRegistration.Atom(
                Start + @"During the End Phase, if this card is in your (?:GY|Graveyard) because it was destroyed in " +
                @"your Monster Zone by an opponent's card and sent there this turn: Target 1 face-up monster your " +
                @"opponent controls; equip this card to that target\.\s*While this card is equipped to a monster by " +
                @"this effect, take control of that monster" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.EquipThisFromGyToTargetTakeControl,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    ResolvesFromGy = true,
                    RequiresDestroyedByOpponentThisTurn = true,
                    TakeControlOfTarget = true,
                    MakesChainLink = true
                }),
            // Necklace of Command (c48576971): When a monster you control that's equipped with this card is destroyed by battle,
            // and this card is sent to the Graveyard, you can either draw 1 card or discard 1 random card from your opponent's hand.
            new AtomRegistration.Atom(
                @"When a monster you control that's equipped with this card is destroyed by battle, and this card is sent to the Graveyard.*Draw 1 card.*Discard 1 random card",
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.DrawOrDiscardRandomFromOpponentHand,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    MakesChainLink = true,
                    IsOptional = true
                }),
        };

        static int Pos(Match m, int group) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 0;
    }
}
