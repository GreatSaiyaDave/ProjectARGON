using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimpleTriggersAndSpells" (v210 FET): whole-text atoms, each anchored to the card's whole text, so a
    /// longer card with the same phrase is not absorbed. Every card has a close Implemented precedent.
    /// <list type="bullet">
    /// <item>Abare Ushioni: a once-per-turn ignition coin call; right: burn the opponent, wrong: burn yourself.</item>
    /// <item>Blade Rabbit: Dream Clown's position-change trigger, worded "destroy 1 monster on your opponent's side".</item>
    /// <item>Granmarg the Rock Monarch: Zaborg's Tribute Summon trigger on a Set card on the field.</item>
    /// <item>Good Goblin Housekeeping: draw (named copies in your GY) + 1, then 1 hand card to the Deck bottom.</item>
    /// <item>The Big March of Animals: each face-up Beast you control gains 200 x your face-up Beasts this turn.</item>
    /// <item>Elemental Burst: Tribute one each of four Attributes; destroy every card the opponent controls.</item>
    /// </list>
    /// Runtime: <see cref="SimpleTriggersAndSpellsRuntime"/>.
    /// </summary>
    public static class SimpleTriggersAndSpellsAtoms
    {
        const string Apos = @"['’]";
        const string Q = @"[""“”]";
        const string TypeWord = @"([A-Za-z]+(?:-(?!Type\b)[A-Za-z]+)*)(?:-Type)?";
        const string Attr = @"(WIND|WATER|FIRE|EARTH|LIGHT|DARK)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Abare Ushioni (c89718302): EFFECT_TYPE_IGNITION in the Monster Zone, SetCountLimit(1) (per copy, not a name
            // OPT), no target; at resolution Duel.CallCoin(tp): right → Damage(1-tp, 1000), wrong → Damage(tp, 1000).
            // The same amount both ways (\2 = \1), else it stays unparsed.
            new AtomRegistration.Atom(
                @"^\s*Once per turn, you can toss a coin and call it\.\s*If you call it right, inflict (\d+) " +
                @"(?:points of )?damage to your opponent(?:" + Apos + @"s Life Points)?\.\s*If you call it wrong, " +
                @"you take \1 (?:points of )?damage\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.CallCoinRightBurnOpponentWrongBurnSelf,
                    Side = EffectSide.Both,
                    Amount = Parse(m, 1),
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerInstance,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Blade Rabbit (c58268433): SINGLE TRIGGER_F on EVENT_CHANGE_POS — previous position Attack, now face-up
            // Defense; EFFECT_FLAG_CARD_TARGET on 1 monster the opponent controls (face-down too); destroy it if still
            // related. Dream Clown's clause (LegacyTextTemplates RxAttackToFaceUpDefenseDestroy) and runtime
            // (TextEffectRuntime.NotifyPositionChanged).
            new AtomRegistration.Atom(
                @"^\s*When this card is changed from Attack Position to face-up Defense Position, destroy 1 monster on " +
                @"your opponent" + Apos + @"s side of the field\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.PositionChanged,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.OppMonsters,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    PositionFromAttack = true,
                    PositionToDefense = true,
                    RequireDestinationFaceUp = true,
                    MakesChainLink = false
                }),

            // Granmarg the Rock Monarch (c60229110): SINGLE TRIGGER_F on EVENT_SUMMON_SUCCESS, IsTributeSummoned;
            // SelectTarget(Card.IsFacedown, LOCATION_ONFIELD, both sides); destroy it if still related and face-down.
            // Zaborg's / Mobius' Tribute Summon trigger shape with the Set-card zone.
            new AtomRegistration.Atom(
                @"^\s*If this card is Tribute Summoned:\s*Target 1 Set card on the field;\s*destroy that target\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSetCards,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresThisTributeSummoned = true,
                    MakesChainLink = true
                }),

            // Good Goblin Housekeeping (c9744376): free-chain Normal Trap; chk IsPlayerCanDraw(tp, 1); at resolution
            // d = copies of the named card in your GY + 1 (the resolving copy is still on the field), Draw(p, d),
            // BreakEffect, then SelectMatchingCard(p, hand, 1, 1) → SendtoDeck(SEQ_DECKBOTTOM). Mandatory.
            new AtomRegistration.Atom(
                @"^\s*Draw cards equal to the number of " + Q + @"([^""“”]+)" + Q + @" cards in your Graveyard \+(\d+), " +
                @"then select 1 card from your hand and return it to the bottom of your Deck\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DrawNamedInGyPlusAmountThenHandCardToDeckBottom,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value.Trim(),
                    Amount = Parse(m, 2),
                    // ygopro IsPlayerCanDraw(tp, 1): Amount (the "+1") is the draw the activation needs.
                    ActivationCondition = DrawThenDiscardRuntime.CanDrawAmount,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // The Big March of Animals (c1689516): Quick-Play; chk a face-up Beast you control; at resolution
            // atk = #(face-up Beasts you control) x 200, EFFECT_UPDATE_ATTACK atk on each of them,
            // RESETS_STANDARD_PHASE_END. Lua counts face-up monsters only (a Set monster has no Type).
            new AtomRegistration.Atom(
                @"^\s*Until the end of this turn, all face-up " + TypeWord + @" monsters on your side of the field gain " +
                @"(\d+) ATK for each " + TypeWord + @" monster on your side of the field\.?\s*$",
                m => SameRace(m)
                    ? new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.FaceUpRaceYouControlGainAtkPerFaceUpRaceUntilEndOfTurn,
                        Side = EffectSide.Controller,
                        RaceFilter = m.Groups[1].Value.Trim(),
                        Amount = Parse(m, 2),
                        ActivationCondition = "YouControlFaceUpRace",
                        CheckedAt = ConditionCheckedAt.Activation,
                        MakesChainLink = true
                    }
                    : null),

            // Elemental Burst (c61411502): free-chain Normal Trap; cost CheckReleaseGroupCost(Card.IsAttribute, 4, with
            // different Attributes among WIND/WATER/FIRE/EARTH) → Release(REASON_COST); chk the opponent controls a card;
            // Destroy(every card the opponent controls) — one event (Ojama Delta Hurricane's clause).
            new AtomRegistration.Atom(
                @"^\s*Tribute 1 each of " + Attr + @", " + Attr + @", " + Attr + @",? and " + Attr + @" monsters to " +
                @"activate this card\.\s*Destroy all cards on your opponent" + Apos + @"s side of the field\.?\s*$",
                m => Distinct(m, 4, out var attrs)
                    ? new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.OppAnyCardOnField,
                        RequiresTributeCount = 4,
                        TributeOneEachAttribute = attrs,
                        ActivationCondition = SpellCounterSpendersRuntime.OpponentControlsACard,
                        MakesChainLink = true
                    }
                    : null),
        };

        static int Parse(Match m, int group) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 1;

        /// <summary>"gain N ATK for each X monster": the counted Type is the boosted Type (else unparsed).</summary>
        static bool SameRace(Match m) =>
            string.Equals(m.Groups[1].Value.Trim(), m.Groups[3].Value.Trim(), System.StringComparison.OrdinalIgnoreCase);

        /// <summary>The <paramref name="n"/> captured Attributes are distinct; <paramref name="joined"/> = "A|B|C|D".</summary>
        static bool Distinct(Match m, int n, out string joined)
        {
            var set = new System.Collections.Generic.List<string>();
            for (var g = 1; g <= n; g++)
            {
                var a = m.Groups[g].Value.Trim().ToUpperInvariant();
                if (a.Length == 0 || set.Contains(a))
                {
                    joined = null;
                    return false;
                }

                set.Add(a);
            }

            joined = string.Join("|", set);
            return true;
        }
    }
}
