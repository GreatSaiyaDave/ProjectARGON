using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "MfcLabCards2" (v168): next set of unimplemented cards from
    /// the lab/character duelist decks.
    /// <list type="bullet">
    /// <item>Winged Kuriboh: if destroyed and sent to GY, controller takes no battle damage for the rest of the turn.</item>
    /// <item>Harpie Lady 3: opponent's monster that battles this card cannot attack during their next 2 turns.</item>
    /// <item>Copycat: if Summoned, this card's ATK/DEF become equal to target opp monster's original ATK/DEF.</item>
    /// <item>Metalmorph: equip trap; target gains 300 ATK/DEF, plus damage-step ATK gain vs attack target.</item>
    /// <item>Riryoku: target 2 face-up monsters; halve ATK of 1, add lost ATK to the other until end of turn.</item>
    /// </list>
    /// </summary>
    public static class MfcLabCards2Atoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Winged Kuriboh (c57116034): if destroyed and sent to GY, no battle damage for rest of turn.
            new AtomRegistration.Atom(
                @"^\s*If this card on the field is destroyed and sent to the Graveyard:\s*" +
                @"For the rest of this turn, you take no battle damage\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.PreventControllerBattleDamage,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),

            // Harpie Lady 3 (c54415063): opponent's monster that battles this card cannot attack next 2 turns.
            new AtomRegistration.Atom(
                @"^\s*An opponent's monster that battles this card cannot declare an attack during your opponent's next (\d+) turns?\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AttackerCannotAttackNextTurn,
                    Side = EffectSide.Opponent,
                    Amount = Pos(m, 1),
                    MakesChainLink = true
                }),

            // Copycat (c26376390): if Summoned, this card's ATK/DEF become equal to target opp monster's original ATK/DEF.
            new AtomRegistration.Atom(
                @"^\s*If this card is Summoned:\s*Target 1 face-up monster your opponent controls;\s*" +
                @"this card's ATK/DEF become equal to that monster's original ATK/DEF\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    Amount = 0, // copied from target
                    DefAmount = 0, // copied from target
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    MakesChainLink = true
                }),

            // Metalmorph (c68540059): equip trap; target gains 300 ATK/DEF, plus damage-step ATK gain.
            new AtomRegistration.Atom(
                @"^\s*Target 1 face-up monster on the field; equip this card to that target\.\s*" +
                @"It gains 300 ATK/DEF\.\s*" +
                @"If it attacks, it gains ATK equal to half the ATK of the attack target, during damage calculation only\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    EquipAtkBonus = 300,
                    EquipDefBonus = 300,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Riryoku (c34016756): target 2 face-up monsters; halve ATK of 1, add lost ATK to the other until end of turn.
            new AtomRegistration.Atom(
                @"^\s*Target 2 face-up monsters on the field;\s*" +
                @"halve the ATK of 1 monster, and if you do, add that lost ATK to the other monster\.\s*" +
                @"These effects last until the end of this turn\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.HalfAtkAddToOtherUntilEndOfTurn,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    Side = EffectSide.Both,
                    TargetCount = 2,
                    RequiresTargetChoice = true,
                    // v208 QC: Lua SelectTarget(Card.IsFaceup …, 2, 2); resolution QcHollowActionsRuntime.ResolveRiryoku.
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),
        };

        static int Pos(System.Text.RegularExpressions.Match m, int i) =>
            int.TryParse(m.Groups[i].Value, out var n) ? n : 0;
    }
}
