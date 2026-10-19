using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "EenDarkWorldDiscards" (EEN) whole-card atoms, each anchored at both ends so a longer or reworded card
    /// stays uncompiled:
    /// <list type="bullet">
    /// <item>The "If this card is discarded to the Graveyard by a card effect:" monsters (ygopro SINGLE TRIGGER_F on
    /// EVENT_TO_GRAVE, previous location hand, REASON_DISCARD|REASON_EFFECT, either player's effect) — timing
    /// <see cref="EffectTiming.DiscardedToGyByCardEffect"/>: Beiige (Special Summon it), Broww (draw 1, +1 when the
    /// opponent's effect discarded it), Goldd (by the opponent: optionally target up to 2 opponent's cards; Special Summon
    /// it, then destroy them), Sillva (Special Summon it, then, by the opponent, the opponent puts 2 hand cards on the
    /// bottom of their Deck).</item>
    /// <item>Brron (c6214884): "When this card inflicts battle damage to your opponent: You can discard 1 card."</item>
    /// <item>Scarr (c5498296): "When this card is destroyed by battle and sent to the Graveyard: Add 1 Level 4 or lower
    /// "Dark World" monster from your Deck to your hand."</item>
    /// </list>
    /// Registered with one line in <see cref="AtomRegistration"/>'s BatchAtomSets. Runtime:
    /// <see cref="EenDarkWorldDiscardsRuntime"/>.
    /// </summary>
    public static class EenDarkWorldDiscardsAtoms
    {
        /// <summary>The trigger condition shared by the four Dark World discard monsters.</summary>
        const string Discarded =
            @"^\s*If this card is discarded to the (?:GY|Graveyard) by a card effect:\s*";

        /// <summary>"… discarded from your hand to your GY by an opponent's card effect" (the bonus condition).</summary>
        const string ByOpponent =
            @"discarded from your hand to your (?:GY|Graveyard) by (?:an|your) opponent's card effect";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Beiige. Lua: SpecialSummon(c, 0, tp, tp, false, false, POS_FACEUP) if it is still related.
            new AtomRegistration.Atom(
                Discarded + @"Special Summon it from the (?:GY|Graveyard)\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.DiscardedToGyByCardEffect,
                    Action = EffectActionKind.SpecialSummonThisFromGyFaceUpWhenDiscarded,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // Broww. Lua s.drtg: 1 card, 2 when rp ~= tp and tp == the previous controller (its own hand).
            new AtomRegistration.Atom(
                Discarded + @"Draw 1 card, plus another card if this card was " + ByOpponent + @"\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.DiscardedToGyByCardEffect,
                    Action = EffectActionKind.DrawPlusOneIfDiscardedByOpponentEffect,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    MakesChainLink = true
                }),
            // Goldd. Lua s.sptg: only when discarded by the opponent's effect and they control a card, SelectYesNo, then
            // SelectTarget 1-2 cards they control; s.spop: SpecialSummon > 0 and label 1 → BreakEffect, destroy the
            // targets still related.
            new AtomRegistration.Atom(
                Discarded + @"If it was " + ByOpponent + @", you can target up to (\d+) cards your opponent controls; " +
                @"Special Summon this card from the (?:GY|Graveyard), then destroy those targets(?: \(if any\))?\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.DiscardedToGyByCardEffect,
                    Action = EffectActionKind.SpecialSummonThisFromGyThenDestroyUpToTargetsIfDiscardedByOpponent,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.OppAnyCardOnField,
                    Amount = Parse(m, 1, 2),
                    MakesChainLink = true
                }),
            // Sillva. Lua s.spop: SpecialSummon > 0, rp ~= tp, tp == the previous controller and the opponent holds 2+
            // cards → BreakEffect; the opponent selects exactly 2 of their hand cards; SendtoDeck SEQ_DECKBOTTOM.
            new AtomRegistration.Atom(
                Discarded + @"Special Summon it from the (?:GY|Graveyard), then, if this card was " + ByOpponent +
                @", your opponent returns exactly (\d+) cards from their hand to the bottom of their Deck, in any order\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.DiscardedToGyByCardEffect,
                    Action = EffectActionKind
                        .SpecialSummonThisFromGyThenOpponentPutsHandCardsOnDeckBottomIfDiscardedByOpponent,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2),
                    MakesChainLink = true
                }),
            // Brron. Lua: TRIGGER_O on EVENT_BATTLE_DAMAGE with ep ~= tp; needs a hand card (s.target chk); s.operation
            // DiscardHand(tp, aux.TRUE, 1, 1, REASON_EFFECT|REASON_DISCARD) — chosen as it resolves, not targeted.
            new AtomRegistration.Atom(
                @"^\s*When this card inflicts battle damage to your opponent:\s*You can discard 1 card\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.YouCanDiscardCardsOfYourChoice,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Scarr. Lua: TRIGGER_F on EVENT_BATTLE_DESTROYED, in the GY by battle; SelectMatchingCard 1 Level <= 4
            // "Dark World" monster from the Deck (nothing when none), SendtoHand + ConfirmCards.
            new AtomRegistration.Atom(
                @"^\s*When this card is destroyed by battle and sent to the (?:GY|Graveyard):\s*" +
                @"Add 1 Level (\d+) or lower ""([^""]+)"" monster from your Deck to your hand\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.AddSeriesMonsterLevelLeqFromDeckToHand,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 4),
                    NamedCard = m.Groups[2].Value,
                    NamedCardIsSeries = true,
                    RequiresThisDestroyedByBattle = true,
                    FromDeck = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n)
                ? n
                : fallback;
    }
}
