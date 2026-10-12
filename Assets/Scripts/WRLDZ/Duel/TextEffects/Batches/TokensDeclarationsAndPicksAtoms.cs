using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TokensDeclarationsAndPicks batch: Token Spells with an all-or-nothing Summon and a summon oath (Stray Lambs,
    /// Scapegoat, Multiplication of Ants), an Attribute declaration (DNA Transplant), an opponent's Deck pick (Jade Insect
    /// Whistle), a random hand Special Summon (A Hero Emerges), an End Phase self-destruct boost (Wild Nature's Release) and
    /// a counter burn (Balloon Lizard). Every atom is the whole card text, so a card with any other sentence is not
    /// compiled here. Runtime: <see cref="TokensDeclarationsAndPicksRuntime"/>.
    /// </summary>
    public static class TokensDeclarationsAndPicksAtoms
    {
        /// <summary>One Token's printed stats: "Name Token(s)" (Type[-Type]/ATTRIBUTE/Level N/ATK N/DEF N).</summary>
        const string TokenStats =
            @"""(?<name>[^""]+?) Tokens?"" \((?<race>[A-Za-z-]+?)(?:-Type)?/(?<attr>[A-Z]+)/Level (?<lv>\d+)/ATK (?<atk>\d+)/DEF (?<def>\d+)\)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Stray Lambs (c60764581) / Scapegoat (c73915051): the cost (s.cost) needs no Normal, Flip or Special Summon by
            // the activator this turn and registers the oath (no Normal / Flip Summon; no Special Summon except by this
            // effect) until the End Phase; s.target / s.activate need N free Monster Zones and Summon all N Tokens in
            // face-up Defense Position or none. Scapegoat's Tokens also cannot be Tributed for a Tribute Summon
            // (EFFECT_UNRELEASABLE_SUM).
            new AtomRegistration.Atom(
                @"^\s*Special Summon (?<n>\d+) " + TokenStats + @" in Defense Position\.\s*" +
                @"(?<notrib>They cannot be Tributed for a Tribute Summon\.\s*)?" +
                @"You cannot Summon other monsters the turn you activate this card \(but you can (?:Normal )?Set\)\.?\s*$",
                m =>
                {
                    var c = Tokens(m, int.Parse(m.Groups["n"].Value));
                    c.SummonInDefense = true;
                    c.TokenCannotTribute = m.Groups["notrib"].Success;
                    c.ForbidsOtherSummonsThisTurn = true;
                    return c;
                }),
            // Multiplication of Ants (c22493811): Tribute 1 Insect as the cost (CheckReleaseGroupCost; with 1 free zone
            // only a monster in your Main Monster Zone), then 2 Tokens (POS_FACEUP) that cannot be Tributed for a Tribute
            // Summon (EFFECT_UNRELEASABLE_SUM), all or none.
            new AtomRegistration.Atom(
                @"^\s*Tribute 1 (?<trib>[A-Za-z-]+?)-Type Monster on your side of the field\.\s*" +
                @"Special Summon (?<n>\d+) " + TokenStats + @" on your side of the field\.\s*" +
                @"The tokens cannot be used as a Tribute for a Tribute Summon\.?\s*$",
                m =>
                {
                    var c = Tokens(m, int.Parse(m.Groups["n"].Value));
                    c.RequiresTributeCount = 1;
                    c.TributeRaceFilter = m.Groups["trib"].Value;
                    c.TokenCannotTribute = true;
                    return c;
                }),
            // DNA Transplant (c56769674): the Attribute is announced in the activation's target step (not a target);
            // while the card is face-up every face-up monster on the field has it (EFFECT_CHANGE_ATTRIBUTE).
            AtomRegistration.Atom.Sequence(
                @"^\s*Activate by declaring 1 monster Attribute\.\s*All face-up monsters become that Attribute\.?\s*$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.DeclareMonsterAttribute,
                        RequiresTargetChoice = true,
                        ChoiceDoesNotTarget = true,
                        IsPsctTarget = false,
                        Zone = EffectZoneFilter.DeclaredAttributes,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.AllFaceUpMonstersBecomeDeclaredAttribute,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // Jade Insect Whistle (c95214051): the opponent must have a Deck (s.target); at resolution the opponent
            // selects 1 Insect from their Deck (if any), shuffles it and places that card on top (ConfirmDecktop).
            new AtomRegistration.Atom(
                @"^\s*Your opponent selects 1 (?<race>[A-Za-z-]+?)-Type Monster from their Deck\.\s*" +
                @"After shuffling the Deck, place the card on top of the Deck\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentPlacesRaceMonsterFromTheirDeckOnTop,
                    RaceFilter = m.Groups["race"].Value,
                    Side = EffectSide.Opponent,
                    ActivationCondition = DcrSpellTrapUtilityRuntime.OpponentDeckNotEmpty,
                    MakesChainLink = true
                }),
            // A Hero Emerges (c21597117): Normal Trap at an opponent's attack declaration (EVENT_ATTACK_ANNOUNCE in the
            // opponent's turn); needs a free Monster Zone and a hand monster that can be Special Summoned (s.target).
            new AtomRegistration.Atom(
                @"^\s*When an opponent['’]s monster declares an attack:\s*Your opponent chooses 1 random card from your " +
                @"hand, then if it is a monster that can be Special Summoned, Special Summon it\.\s*" +
                @"Otherwise, send it to the (?:Graveyard|GY)\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.AttackDeclared,
                    Action = EffectActionKind.RandomHandCardSpecialSummonOrSendToGy,
                    Side = EffectSide.Controller,
                    ActivationCondition = TokensDeclarationsAndPicksRuntime.HandMonsterCanBeSpecialSummoned,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Wild Nature's Release (c61166988): target 1 face-up Beast / Beast-Warrior with DEF above 0 on either field;
            // still face-up at resolution, it gains ATK equal to its current DEF (RESETS_STANDARD_PHASE_END) and is
            // destroyed in this turn's End Phase.
            new AtomRegistration.Atom(
                @"^\s*Target 1 (?<r1>[A-Za-z-]+?) or (?<r2>[A-Za-z-]+?)-Type monster; it gains ATK equal to its current " +
                @"DEF until the End Phase\.\s*During the End Phase, destroy it\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RaceFilter = m.Groups["r1"].Value + "|" + m.Groups["r2"].Value,
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),
            // Balloon Lizard (c39892082): a mandatory Standby trigger in its controller's Standby Phase (+1 counter), and
            // destroyed with counters on it, damage = counters x 400 to the reason player (EVENT_DESTROYED, TRIGGER_F).
            AtomRegistration.Atom.Sequence(
                @"^\s*Put 1 counter on this card during each of your Standby Phases\.\s*When this card is destroyed, " +
                @"inflict damage to the controller of the card that destroyed it equal to the number of counters X " +
                @"(?<amt>\d+) points\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.PlaceCounterOnThisCard,
                        Amount = 1,
                        Side = EffectSide.Controller,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.InflictCountersTimesAmountToDestroyer,
                        Amount = int.Parse(m.Groups["amt"].Value),
                        RequiresDestroyed = true,
                        IsOptional = false,
                        Side = EffectSide.Controller,
                        MakesChainLink = true
                    }
                }),
        };

        /// <summary>The all-or-nothing Token clause shared by the Token atoms (the Tokens go to the activator's field).</summary>
        static EffectClause Tokens(Match m, int count) => new()
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.SpecialSummonToken,
            Side = EffectSide.Controller,
            TokenCount = count,
            TokenName = m.Groups["name"].Value.Trim() + " Token",
            TokenRace = m.Groups["race"].Value,
            TokenAttribute = m.Groups["attr"].Value,
            TokenLevel = int.Parse(m.Groups["lv"].Value),
            TokenAtk = int.Parse(m.Groups["atk"].Value),
            TokenDef = int.Parse(m.Groups["def"].Value),
            TokensNeedAllZones = true,
            ActivationCondition = TokensDeclarationsAndPicksRuntime.TokenSummonLegal,
            MakesChainLink = true
        };
    }
}
