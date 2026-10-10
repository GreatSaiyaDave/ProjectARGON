using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "EffectDamageEvents" (v162): the central effect-damage event — Barrel Behind the Door (reflect the
    /// damage of the answered Chain Link), Trap of Board Eraser (negate it, then the opponent discards), Dark Room of
    /// Nightmare (each time the opponent takes effect damage), Gravekeeper's Vassal (its battle damage is effect damage)
    /// and Coffin Seller (each time a monster(s) is sent to the opponent's GY). Every atom is the whole card text, anchored
    /// at the start of a sentence and the final period, so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="EffectDamageEventsRuntime"/>.
    /// </summary>
    public static class EffectDamageEventsAtoms
    {
        /// <summary>Text end: the final period followed by optional whitespace and the end of the text.</summary>
        const string End = @"\.\s*$";

        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Barrel Behind the Door (c78783370): Counter Trap, EVENT_CHAINING; the activated effect (a monster effect, or a
            // Spell/Trap Card's activation) has CATEGORY_DAMAGE to tp or both players (or a recovery that EFFECT_REVERSE_RECOVER
            // turns into damage to tp). Operation: EFFECT_REFLECT_DAMAGE for tp, keyed to that link's chain id, RESET_CHAIN.
            new AtomRegistration.Atom(
                @"^Activate only when a card" + Apos + @"s effect that would inflict damage to you is activated\.\s*" +
                @"Your opponent takes the damage instead" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.ReflectEffectDamageOfAnsweredLink,
                    Side = EffectSide.Controller,
                    ChainResponseOnly = true,
                    MakesChainLink = true
                }),
            // Trap of Board Eraser (c3055837): Counter Trap, EVENT_CHAINING on an effect that inflicts damage (printed: "to
            // your Life Points"; the Lua condition is laxer and accepts damage to either player — the printed text is
            // followed); target step: the opponent holds a card. Operation: the opponent discards 1 of their choice
            // (DiscardHand 1-tp, REASON_EFFECT), then EFFECT_CHANGE_DAMAGE → 0 for tp on that link (RESET_CHAIN).
            new AtomRegistration.Atom(
                @"^You can only activate this card when an effect that inflicts damage to your Life Points is activated " +
                @"\(except Battle Damage\)\.\s*" +
                @"Negate the Effect Damage you receive and your opponent then selects 1 card from (?:his/her|their) hand " +
                @"and discards it" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateEffectDamageOfAnsweredLinkThenOpponentDiscards,
                    Side = EffectSide.Controller,
                    ChainResponseOnly = true,
                    MakesChainLink = true
                }),
            // Dark Room of Nightmare (c85562745): TRIGGER_F on EVENT_DAMAGE (range SZONE): ep ~= tp, not battle damage, a
            // reason effect whose card is not "Dark Room of Nightmare"; Duel.Damage(1-tp, 300) if the card is still related.
            new AtomRegistration.Atom(
                @"^Each time your opponent takes damage from a card effect, except ""Dark Room of Nightmare"", inflict " +
                @"(\d+) damage to your opponent" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageEachTimeOpponentTakesEffectDamage,
                    Amount = Parse(m, 1, 300),
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Gravekeeper's Vassal (c99690140): EFFECT_BATTLE_DAMAGE_TO_EFFECT on itself.
            new AtomRegistration.Atom(
                @"^Any battle damage this card inflicts to your opponent is treated as effect damage instead" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.BattleDamageThisInflictsIsEffectDamage,
                    Side = EffectSide.Opponent,
                    MakesChainLink = false
                }),
            // Coffin Seller (c65830223): TRIGGER_F on EVENT_TO_GRAVE (range SZONE) when the event's cards include a monster
            // in 1-tp's control (its GY); one trigger per event; Duel.Damage(1-tp, 300) if the card is still related.
            new AtomRegistration.Atom(
                @"^Each time a monster\(s\) is sent to your opponent" + Apos + @"s (?:GY|Graveyard): Inflict (\d+) damage " +
                @"to your opponent" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageEachTimeMonsterSentToOpponentGy,
                    Amount = Parse(m, 1, 300),
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            m.Groups[group].Success && int.TryParse(m.Groups[group].Value, out var v) ? v : fallback;
    }
}
