using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DerivedAtkMonsters" (v176): ATK taken from the Tributes of this card's Tribute Summon, or set from a
    /// count. Whole-sentence shapes, not a card-name list. Runtime: <see cref="DerivedAtkMonstersRuntime"/>.
    /// <list type="bullet">
    /// <item>Great Maju Garzett (c47942531) / Maju Garzett (c8794435): "The ATK of this card becomes twice the original ATK
    /// of 1 monster that you Tributed …" / "… equal to the combined original ATK of the 2 monsters you Tributed …" —
    /// set once at the Tribute Summon (not a Tribute Set) from the Tributes' printed ATK (GetTextAttack, "?" = 0).</item>
    /// <item>Guardian Tryce (c46037213): "When this card is destroyed and sent to the Graveyard: Special Summon the monster
    /// from the Graveyard that was used for its Tribute Summon." (TRIGGER_F; its summon gate is the structural
    /// PsctGrammar.SummonGateNamed sentence).</item>
    /// <item>Chaos Necromancer (c1434352): "The ATK of this card is the number of Monster Cards in your Graveyard x N." /
    /// Gren Maju Da Eiza (c36584821): "This card's ATK/DEF are each equal to the number of your banished cards x N." —
    /// GainSelfAtkPerCount with <see cref="EffectClause.SelfStatIsSet"/> (EFFECT_SET_ATTACK / _DEFENSE, "?" printed).</item>
    /// </list>
    /// </summary>
    public static class DerivedAtkMonstersAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)""]\s))";
        const string End = @"\.(?=\s|$)";
        const string Gy = @"(?:GY|Graveyard)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Great Maju Garzett: twice the original ATK of the (one) Tribute.
            new AtomRegistration.Atom(
                Start + @"The ATK of this card becomes twice the original ATK of 1 monster that you Tributed for the " +
                @"Tribute Summon of this card" + End,
                _ => TributeAtk(2, DerivedAtkMonstersRuntime.FirstTribute)),

            // Maju Garzett: the combined original ATK of its Tributes.
            new AtomRegistration.Atom(
                Start + @"The ATK of this card becomes equal to the combined original ATK of the (\d+) monsters you " +
                @"Tributed for the Tribute Summon of this card" + End,
                _ => TributeAtk(1, DerivedAtkMonstersRuntime.AllTributes)),

            // Guardian Tryce: mandatory "When destroyed and sent to the GY" revival of the Tribute(s).
            new AtomRegistration.Atom(
                Start + @"When this card is destroyed and sent to the " + Gy + @": Special Summon the monster from the " +
                Gy + @" that was used for its Tribute Summon" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonTributeMaterialsFromGy,
                    Side = EffectSide.Controller,
                    RequiresDestroyed = true,
                    IsOptional = false,
                    MakesChainLink = true
                }),

            // Chaos Necromancer: ATK = Monster Cards in your GY x N.
            new AtomRegistration.Atom(
                Start + @"The ATK of this card is the number of Monster Cards in your " + Gy + @" x (\d+)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 300),
                    DefAmount = 0,
                    CountSource = "MonstersInYourGy",
                    SelfStatIsSet = true,
                    MakesChainLink = false
                }),

            // Gren Maju Da Eiza: ATK and DEF = your banished cards x N.
            new AtomRegistration.Atom(
                Start + @"This card's ATK/DEF are each equal to the number of your banished cards x (\d+)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 400),
                    DefAmount = Parse(m, 1, 400),
                    CountSource = DerivedAtkMonstersRuntime.YourBanishedCards,
                    SelfStatIsSet = true,
                    MakesChainLink = false
                }),
        };

        static EffectClause TributeAtk(int multiplier, string which) => new()
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = EffectActionKind.ThisAtkBecomesTributedOriginalAtk,
            Side = EffectSide.Controller,
            Amount = multiplier,
            CountSource = which,
            MakesChainLink = false
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
