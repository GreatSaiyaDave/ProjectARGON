using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "MfcLabCards" (v168): whole-text atoms for unimplemented cards found in
    /// the lab/character duelist decks.
    /// <list type="bullet">
    /// <item>The Wicked Worm Beast: this face-up card returns to its owner's hand during the End Phase.</item>
    /// <item>Call of the Grave: when your opponent activates "Monster Reborn", negate that activation.</item>
    /// <item>Ancient Lamp: during your Main Phase, you can Special Summon "La Jinn" from your hand.</item>
    /// <item>Scapegoat: Special Summon 4 "Sheep Tokens" in Defense Position.</item>
    /// <item>Shadow Spell: target 1 face-up opp monster; it loses 700 ATK and cannot attack/change position.</item>
    /// </list>
    /// Runtime: <see cref="MfcLabCardsRuntime"/>.
    /// </summary>
    public static class MfcLabCardsAtoms
    {
        const string TypeWord = @"([A-Za-z]+(?:-(?!Type\b)[A-Za-z]+)*)(?:-Type)?";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // The Wicked Worm Beast (c6285791): EndPhase, this card returns to owner's hand.
            new AtomRegistration.Atom(
                @"^\s*This face-up card on the field is returned to its owner's hand during the End Phase of your turn\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.ReturnToHand,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),

            // Call of the Grave (c16970158): counter — negate opponent's activation of named card.
            new AtomRegistration.Atom(
                @"^\s*When your opponent activates ""([^""]+)"":\s*Negate the effect of that ""[^""]+""\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    OpponentTurnOnly = true,
                    Side = EffectSide.Opponent,
                    NamedCard = m.Groups[1].Value.Trim(),
                    MakesChainLink = true
                }),

            // Ancient Lamp (c54912977): during your Main Phase, you can SS "La Jinn" from your hand.
            new AtomRegistration.Atom(
                @"^\s*During your Main Phase:\s*You can Special Summon ""([^""]+)"" from your hand\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromHand,
                    NamedCard = m.Groups[1].Value.Trim(),
                    FromHand = true,
                    MakesChainLink = true
                }),

            // Scapegoat (c73915051): Special Summon 4 "Sheep Tokens" in Defense Position.
            new AtomRegistration.Atom(
                @"^\s*Special Summon (\d+) ""([^""]+)"" \(([^)]+)\) in Defense Position\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonToken,
                    Amount = int.TryParse(m.Groups[1].Value, out var n) ? n : 4,
                    TokenName = m.Groups[2].Value.Trim(),
                    TokenRace = ExtractRace(m.Groups[3].Value),
                    TokenAttribute = ExtractAttribute(m.Groups[3].Value),
                    TokenLevel = ExtractLevel(m.Groups[3].Value),
                    TokenAtk = ExtractAtk(m.Groups[3].Value),
                    TokenDef = ExtractDef(m.Groups[3].Value),
                    MakesChainLink = true
                }),

            // Shadow Spell (c29267084): target 1 face-up opp monster; -700 ATK, cannot attack/change position.
            new AtomRegistration.Atom(
                @"^\s*Activate this card by targeting 1 face-up monster your opponent controls;\s*" +
                @"it loses (\d+) ATK, also it cannot attack or change its battle position\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                    Side = EffectSide.Opponent,
                    Amount = Pos(m, 1),
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    AlsoCannotChangeBattlePosition = true,
                    MakesChainLink = true
                }),
        };

        static string ExtractRace(string paren)
        {
            var m = System.Text.RegularExpressions.Regex.Match(paren, @"([A-Za-z]+)-Type");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }
        static string ExtractAttribute(string paren)
        {
            var m = System.Text.RegularExpressions.Regex.Match(paren, @"/\s*([A-Z]+)\s*/");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }
        static int ExtractLevel(string paren)
        {
            var m = System.Text.RegularExpressions.Regex.Match(paren, @"Level\s*(\d+)");
            return int.TryParse(m.Groups[1].Value, out var n) ? n : 1;
        }
        static int ExtractAtk(string paren)
        {
            var m = System.Text.RegularExpressions.Regex.Match(paren, @"ATK\s*(-?\d+)");
            return int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        }
        static int ExtractDef(string paren)
        {
            var m = System.Text.RegularExpressions.Regex.Match(paren, @"DEF\s*(-?\d+)");
            return int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        }
        static int Pos(System.Text.RegularExpressions.Match m, int i) =>
            int.TryParse(m.Groups[i].Value, out var n) ? n : 0;
    }
}
