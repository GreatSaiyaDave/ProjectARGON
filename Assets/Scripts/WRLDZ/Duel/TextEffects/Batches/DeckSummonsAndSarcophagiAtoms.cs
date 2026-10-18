using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DeckSummonsAndSarcophagi" (AST): Special Summons from the Deck — excavation (Enchanting Fitting Room,
    /// Monster Gate), a Tribute-cost summon linked to a Continuous Trap (Ninjitsu Art of Transformation), an End Phase
    /// summon per battle record (Human-Wave Tactics) — and the Sarcophagus family (The First Sarcophagus's placement,
    /// leave-field send and "Spirit of the Pharaoh" summon; Spirit of the Pharaoh's GY revival). Every pattern is the
    /// whole sentence (or the whole card), so a longer card that merely contains one of these phrases never compiles
    /// part of its text here. Runtime: <see cref="DeckSummonsAndSarcophagiRuntime"/>.
    /// </summary>
    public static class DeckSummonsAndSarcophagiAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        static readonly Regex RxQuoted = new Regex(@"""([^""]+)""", RegexOptions.Compiled);

        static int Parse(Match m, int group, int fallback) =>
            m != null && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;

        /// <summary>"Beast, Winged Beast, or Insect" → "Beast|Winged Beast|Insect" (ClassicEraTemplates.RaceMatches).</summary>
        static string RaceList(string printed)
        {
            var parts = Regex.Split(printed ?? "", @"\s*,\s*(?:or\s+)?|\s+or\s+");
            var list = new List<string>();
            foreach (var p in parts)
            {
                var t = Regex.Replace(p.Trim(), @"-Type$", "", RegexOptions.IgnoreCase);
                if (t.Length > 0) list.Add(t);
            }

            return list.Count > 0 ? string.Join("|", list) : null;
        }

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Enchanting Fitting Room (c30531525): cost Cost.PayLP(800); target chk: a free Monster Zone and 4+ cards in
            // the Deck; ConfirmDecktop(4), the Level 3 or lower Normal Monsters that can be Special Summoned are summoned
            // POS_FACEUP (too few zones: Select ft of them, the others to the GY), then ShuffleDeck.
            new AtomRegistration.Atom(
                Start + @"Pay (\d+) LP;\s*excavate the top (\d+) cards of your Deck and Special Summon all excavated " +
                @"Level (\d+) or lower Normal Monsters, also, after that, shuffle the rest back into your Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ExcavateTopSpecialSummonLowLevelNormalsShuffleRest,
                    Side = EffectSide.Controller,
                    PayLpAmount = Parse(m, 1, 800),
                    Amount = Parse(m, 2, 4),
                    SummonLevelMax = Parse(m, 3, 3),
                    RequiresNormalMonster = true,
                    ActivationCondition = DeckSummonsAndSarcophagiRuntime.FreeZoneAndDeckHoldsExcavation,
                    ConditionAmount = Parse(m, 2, 4),
                    MakesChainLink = true
                }),

            // Monster Gate (c43040603): cost Tribute 1 monster that leaves you a usable zone (cfilter GetMZoneCount>0);
            // target chk: a monster in the Deck that IsSummonableCard; excavate until the top-most such monster, Special
            // Summon it POS_FACEUP when a zone is free and it can be Special Summoned (else it goes with the others),
            // DiscardDeck the other excavated cards; none in the Deck: confirm the Deck and shuffle.
            new AtomRegistration.Atom(
                Start + @"Tribute 1 monster;\s*excavate cards from the top of your Deck until you excavate a monster that " +
                @"can be Normal Summoned/Set\.\s*Special Summon it, also send the other excavated cards to the " +
                @"(?:GY|Graveyard)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ExcavateUntilNormalSummonableSpecialSummonItSendRestToGy,
                    Side = EffectSide.Controller,
                    RequiresTributeCount = 1,
                    ActivationCondition = DeckSummonsAndSarcophagiRuntime.NormalSummonableMonsterInDeck,
                    MakesChainLink = true
                }),

            // Ninjitsu Art of Transformation (c70861343), the whole card: activation cost Tribute 1 face-up "Ninja" with
            // a Level whose Level + 3 has a Beast / Winged Beast / Insect match in the hand or Deck (e:SetLabel(lv+3));
            // resolution only while this card is on the field (IsRelateToEffect): Special Summon 1 such monster POS_FACEUP
            // and link it (SetCardTarget); e2 EVENT_LEAVE_FIELD destroys that monster.
            new AtomRegistration.Atom(
                @"^\s*Activate this card by Tributing 1 face-up ""([^""]+)"" monster;\s*Special Summon 1 " +
                @"([A-Za-z][A-Za-z ,-]*?) monster from your hand or Deck whose Level is less than or equal to the Level " +
                @"the Tributed monster had on the field \+\s*(\d+)\.\s*When this card leaves the field, destroy that " +
                @"monster\.?\s*$",
                m =>
                {
                    var races = RaceList(m.Groups[2].Value);
                    if (races == null) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.SpecialSummonRaceLevelUpToTributedPlusFromHandOrDeckLinked,
                        Side = EffectSide.Controller,
                        RequiresTributeCount = 1,
                        TributeFaceUpOnly = true,
                        TributeSeriesName = m.Groups[1].Value,
                        RaceFilter = races,
                        TributedLevelOffset = Parse(m, 3, 3),
                        DestroyHostWhenThisLeaves = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    };
                }),

            // Human-Wave Tactics (c30353551), the whole card: TRIGGER_F at every End Phase (count 1) while the count of
            // its controller's Level 2 or lower Normal Monsters destroyed by battle this turn (EVENT_BATTLED +
            // STATUS_BATTLE_DESTROYED) is above 0; Select exactly that many from the Deck (fewer when fewer exist),
            // Special Summon them POS_FACEUP (too few zones: Select ft, the others to the GY), then the Deck is shuffled.
            new AtomRegistration.Atom(
                @"^\s*During the End Phase of each turn, select a number of Level (\d+) or lower Normal Monsters from your " +
                @"Deck equal to the number of your Level \1 or lower Normal Monsters destroyed as a result of battle " +
                @"during that turn, and Special Summon them to your side of the field\.\s*The Deck is then shuffled\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.SpecialSummonLowLevelNormalsFromDeckPerOwnBattleDestroyedAtEndPhase,
                    Side = EffectSide.Controller,
                    SummonLevelMax = Parse(m, 1, 2),
                    RequiresNormalMonster = true,
                    FiresAtEachPlayersPhase = true,
                    OncePerTurn = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // The First Sarcophagus (c31076103), the whole card: e2 (TRIGGER_F, the opponent's End Phase, count 1, while
            // this copy placed fewer than 2) places "The Second Sarcophagus", then "The Third Sarcophagus", face-up from
            // the hand or Deck; e3 / e4 (EVENT_LEAVE_FIELD) send the others when one leaves; inside plop, once this card
            // and both placed cards are face-up on your field: send all of them to the GY and, only if all were sent and
            // a zone is free, Special Summon 1 "Spirit of the Pharaoh" from the hand or Deck ignoring its Summoning
            // conditions (IsCanBeSpecialSummoned(e,0,tp,true,false), CompleteProcedure).
            AtomRegistration.Atom.Sequence(
                @"^\s*During each of your opponent's End Phases, place 1 card face-up on your side of the field from your " +
                @"hand or Deck in this order: ((?:""[^""]+"",\s*)+""[^""]+"")\.\s*" +
                @"If 1 (\w+) card is removed from your side of the field, all of them on your side of the field are sent " +
                @"to the (?:GY|Graveyard)\.\s*" +
                @"When you have all (\d+) \2 cards on your side of the field, send all \2 cards on your side of the field " +
                @"to the (?:GY|Graveyard) to Special Summon 1 ""([^""]+)"" from your hand or Deck\.?\s*$",
                m =>
                {
                    var family = m.Groups[2].Value;
                    var names = new List<string>();
                    foreach (Match q in RxQuoted.Matches(m.Groups[1].Value))
                        names.Add(q.Groups[1].Value);
                    // Fail closed: every placed card belongs to the named family, and "all N" is this card + those placed.
                    if (names.Count < 1 || !int.TryParse(m.Groups[3].Value, out var all) || all != names.Count + 1)
                        return null;
                    foreach (var n in names)
                        if (n.IndexOf(family, System.StringComparison.OrdinalIgnoreCase) < 0)
                            return null;
                    var named = string.Join("|", names);
                    return new[]
                    {
                        new EffectClause
                        {
                            Timing = EffectTiming.EndPhase,
                            Action = EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck,
                            Side = EffectSide.Controller,
                            OpponentTurnOnly = true,
                            OncePerTurn = true,
                            NamedCard = named,
                            Amount = names.Count,
                            StaysOnField = true,
                            MakesChainLink = true
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField,
                            Side = EffectSide.Controller,
                            NamedCard = named,
                            StaysOnField = true,
                            MakesChainLink = false
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.EndPhase,
                            Action = EffectActionKind.SendThisAndPlacedToGyThenSpecialSummonNamedFromHandOrDeck,
                            Side = EffectSide.Controller,
                            OpponentTurnOnly = true,
                            NamedCard = m.Groups[4].Value,
                            Amount = all,
                            FromHand = true,
                            FromDeck = true,
                            StaysOnField = true,
                            MakesChainLink = false
                        }
                    };
                }),

            // Spirit of the Pharaoh (c25343280), sentence 3: SINGLE TRIGGER_O on EVENT_SPSUMMON_SUCCESS, CARD_TARGET 1 to
            // min(4, free zones) Level 2 or lower Zombie Normal Monsters in your GY; operation: nothing if fewer free zones
            // than targets, else Special Summon them POS_FACEUP.
            new AtomRegistration.Atom(
                Start + @"When this card is Special Summoned, you can Special Summon up to (\d+) Level (\d+) or lower " +
                @"([A-Za-z][A-Za-z -]*?)(?:-Type)? Normal Monsters from your (?:GY|Graveyard)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonUpToTargetedLowLevelRaceNormalsFromGy,
                    Side = EffectSide.Controller,
                    RequiresThisSpecialSummoned = true,
                    IsOptional = true,
                    Amount = Parse(m, 1, 4),
                    SummonLevelMax = Parse(m, 2, 2),
                    RaceFilter = m.Groups[3].Value.Trim(),
                    RequiresNormalMonster = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),
        };
    }
}
