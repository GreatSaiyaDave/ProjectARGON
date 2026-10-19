using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RevivalEquipsAndGearfried": Revival Equip Spells, a monster that equips itself to steal, Gearfried the
    /// Swordmaster and Release Restraint. Whole sentences only; runtime in <see cref="RevivalEquipsAndGearfriedRuntime"/>.
    /// <list type="bullet">
    /// <item>Fulfillment of the Contract (c48206762) / Re-Fusion (c74694807): EVENT_FREE_CHAIN activation, cost PayLPCost(800),
    /// CARD_TARGET 1 Ritual / Fusion Monster in your GY that IsCanBeSpecialSummoned(e,0,tp,false,false) (a properly summoned
    /// copy: SpecialSummonLegality), with a free Monster Zone; Special Summon it face-up in either position (POS_FACEUP) and
    /// equip this card to it (equip limit: that monster only). EVENT_LEAVE_FIELD: when this card is destroyed, banish the
    /// equipped monster if it is still in a Monster Zone (<see cref="EffectClause.BanishHostOnlyIfThisDestroyed"/>).</item>
    /// <item>Release Restraint (c75417459): cost Tribute 1 "Gearfried the Iron Knight"; Special Summon 1 "Gearfried the
    /// Swordmaster" from the hand or Deck (nocheck=true, CompleteProcedure): Dedication through Light and Darkness's
    /// named summon, without the GY.</item>
    /// <item>Gearfried the Swordmaster (c57046845): EnableReviveLimit + AddMustBeSpecialSummoned (the summon sentences are
    /// structural; TributeSummonsRuntime.ProcedureCardName makes Release Restraint its proper summon). e1 SINGLE TRIGGER_F
    /// on EVENT_EQUIP: target 1 monster your opponent controls; destroy it
    /// (<see cref="EffectActionKind.DestroyOppMonsterEachTimeThisIsEquipped"/>).</item>
    /// <item>Brain Jacker (c40267580): FLIP, CARD_TARGET aux.CheckStealEquip — equip this card to that monster (equip limit:
    /// that monster), EFFECT_SET_CONTROL while equipped (<see cref="EffectActionKind.EquipThisFromFieldToTargetTakeControl"/>);
    /// e2 FIELD TRIGGER_F in the Spell &amp; Trap Zone at the Standby Phase of its controller's opponent: that player gains
    /// 500 LP (<see cref="EffectClause.RequiresThisIsEquipCard"/>). One atom for the whole text: the gain sentence alone
    /// would mean something else on another card.</item>
    /// </list>
    /// </summary>
    public static class RevivalEquipsAndGearfriedAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";
        const string Q = @"""([^""]+)""";
        const string Gy = @"(?:GY|Graveyard)";

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Fulfillment of the Contract / Re-Fusion: the whole text (activation cost, target, Summon + equip, and the
            // "when destroyed" banish).
            new AtomRegistration.Atom(
                @"^\s*Activate this card by paying (\d+) (?:LP|Life Points), then target 1 (Ritual|Fusion) Monster in your " +
                Gy + @"; Special Summon it and equip it with this card\.\s*When this card is destroyed, banish the equipped " +
                @"monster\.?\s*$",
                m =>
                {
                    var ritual = string.Equals(m.Groups[2].Value, "Ritual", System.StringComparison.OrdinalIgnoreCase);
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.SpecialSummonFromGy,
                        Side = EffectSide.Controller,
                        Zone = EffectZoneFilter.ControllerGyMonsters,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = 1,
                        PayLpAmount = Parse(m, 1, 800),
                        RequiresRitualMonsterTarget = ritual,
                        RequiresFusionMonsterTarget = !ritual,
                        // POS_FACEUP: the player chooses Attack or Defense (neither SummonInAttack nor SummonInDefense).
                        StaysOnField = true,
                        BanishHostWhenThisLeaves = true,
                        BanishHostOnlyIfThisDestroyed = true,
                        MakesChainLink = true
                    };
                }),

            // Release Restraint: cost Tribute 1 named monster; Special Summon 1 named monster from the hand or Deck.
            new AtomRegistration.Atom(
                Start + @"Tribute 1 " + Q + @"; Special Summon 1 " + Q + @" from your hand or Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[2].Value,
                    FromHand = true,
                    FromDeck = true,
                    RequiresTributeCount = 1,
                    TributeNamedCard = m.Groups[1].Value,
                    MakesChainLink = true
                }),

            // Gearfried the Swordmaster, sentence 3: a mandatory trigger on each equip event.
            new AtomRegistration.Atom(
                Start + @"Each time an Equip Card\(s\) is equipped to this card: Target 1 monster your opponent controls; " +
                @"destroy that target" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DestroyOppMonsterEachTimeThisIsEquipped,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppMonsters,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Brain Jacker: the whole text (FLIP equip + control, then the opponent's Standby gain while equipped).
            AtomRegistration.Atom.Sequence(
                @"^\s*FLIP: Equip this card to a monster on your opponent's side of the field and treat it as an Equip " +
                @"Card\.\s*Take control of the equipped monster\.\s*Your opponent gains (\d+) Life Points during each of " +
                @"their Standby Phases\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.EquipThisFromFieldToTargetTakeControl,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.OppFaceUpMonsters,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = 1,
                        TakeControlOfTarget = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.GainLifePoints,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 1, 500),
                        OpponentTurnOnly = true,
                        RequiresThisIsEquipCard = true,
                        MakesChainLink = true
                    }
                }),
        };
    }
}
