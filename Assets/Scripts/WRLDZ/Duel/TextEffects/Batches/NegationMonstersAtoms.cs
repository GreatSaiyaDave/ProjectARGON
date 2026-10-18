using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "NegationMonsters" (v161): monsters that negate Spell/Trap effects targeting them (Freed the Matchless
    /// General, Frontier Wiseman, Tyrant Dragon), Freed's Draw Phase search, Tyrant Dragon's conditional second attack,
    /// the negation / banishment of monsters destroyed by battle (Dark Ruler Ha Des, Twin-Headed Wolf, Lesser Fiend), and
    /// Double Snare's target class. Every atom is one whole sentence (Freed's search: the sentence and its "must be
    /// face-up" rider). The runtime is <see cref="NegationMonstersRuntime"/>.
    /// </summary>
    public static class NegationMonstersAtoms
    {
        /// <summary>Sentence start: the text's start, or after a sentence's end (". ", or ".)" of a parenthetical).</summary>
        const string SentenceStart = @"(?:^|(?<=[.!?)]\s))";
        const string SentenceEnd = @"\.?(?=\s|$)";
        const string TypeWord = @"([A-Za-z][A-Za-z -]*?)(?:-Type)?";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Freed the Matchless General (c49681811) / Frontier Wiseman (c38742075) / Tyrant Dragon (c94568601): e2
            // EVENT_CHAIN_SOLVING (FIELD+CONTINUOUS, LOCATION_MZONE): re:IsSpellEffect() (IsTrapEffect()) with
            // EFFECT_FLAG_CARD_TARGET whose CHAININFO_TARGET_CARDS contain this card (Wiseman: a face-up Warrior you
            // control in the Monster Zone) → Duel.NegateEffect(ev), then Destroy(re:GetHandler()) while it is still
            // related. Either player's effect (no rp check).
            new AtomRegistration.Atom(
                SentenceStart + @"Negate any (Spell|Trap) effects that target (this card(?: on the field)?|an? " + TypeWord +
                @" monster you control), and if you do, destroy that (card|Spell(?: Card)?|Trap(?: Card)?)" + SentenceEnd,
                m =>
                {
                    var family = m.Groups[1].Value;
                    var destroyed = m.Groups[4].Value;
                    // "destroy that Spell Card" must name the same family as the negated effect.
                    if (!destroyed.Equals("card", System.StringComparison.OrdinalIgnoreCase) &&
                        !destroyed.StartsWith(family, System.StringComparison.OrdinalIgnoreCase))
                        return null;
                    var thisCard = m.Groups[2].Value.StartsWith("this card", System.StringComparison.OrdinalIgnoreCase);
                    var spell = family.Equals("Spell", System.StringComparison.OrdinalIgnoreCase);
                    return new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy,
                        Side = EffectSide.Controller,
                        NegateRespondsToSpell = spell,
                        NegateRespondsToTrap = !spell,
                        DestroyNegatedCard = true,
                        AffectsThisCardOnly = thisCard,
                        RaceFilter = thisCard ? null : m.Groups[3].Value.Trim(),
                        // A monster's Type is known only face-up (Lua cfilter IsFaceup()).
                        RequiresTargetFaceUp = !thisCard,
                        StaysOnField = true,
                        MakesChainLink = false
                    };
                }),
            // Freed the Matchless General (c49681811): e4 FIELD+TRIGGER_O, EVENT_PREDRAW, LOCATION_MZONE. Condition: your
            // turn, a card in your Deck, a draw count above 0. Target step: a Level 4 or lower Warrior in your Deck; sets
            // EFFECT_DRAW_COUNT 0 for this Draw Phase (the normal draw is given up). Operation: only while this card is
            // still face-up on the field, add 1 such monster from your Deck to your hand.
            new AtomRegistration.Atom(
                SentenceStart + @"During your Draw Phase, instead of conducting your normal draw: You can add 1 Level (\d+) " +
                @"or lower " + TypeWord + @" monster from your Deck to your hand\. This card must be face-up on the field " +
                @"to activate and to resolve this effect" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AddFromDeckInsteadOfNormalDraw,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.DeckMonstersRaceLevelLeq,
                    Amount = int.TryParse(m.Groups[1].Value, out var lv) ? lv : 4,
                    RaceFilter = m.Groups[2].Value.Trim(),
                    ChoiceDoesNotTarget = true,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Tyrant Dragon (c94568601): e4 SINGLE EFFECT_EXTRA_ATTACK value 1 with condition s.atkcon — the opponent
            // controls a monster (Duel.GetFieldGroupCount(tp,0,LOCATION_MZONE)>0), read when the next attack is declared.
            new AtomRegistration.Atom(
                SentenceStart + @"During your Battle Phase, if your opponent controls a monster after this card['’]s first " +
                @"attack, this card can make a second attack" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ExtraAttacks,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    ActivationCondition = NegationMonstersRuntime.OpponentControlsMonster,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Dark Ruler Ha Des (c53982768): e2 FIELD+CONTINUOUS EVENT_BATTLED, LOCATION_MZONE: the attacker (attack
            // target) is a Fiend you control and the other battler is battle-destroyed → EFFECT_DISABLE +
            // EFFECT_DISABLE_EFFECT with RESET_EVENT|RESETS_STANDARD_EXC_GRAVE on that monster.
            new AtomRegistration.Atom(
                SentenceStart + @"Negate the effects of monsters destroyed by battle with " + TypeWord +
                @" monsters you control" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle,
                    Side = EffectSide.Controller,
                    RaceFilter = m.Groups[1].Value.Trim(),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Twin-Headed Wolf (c88132637): e1 SINGLE+CONTINUOUS EVENT_BATTLED, LOCATION_MZONE, condition: another face-up
            // Fiend you control (aux.FaceupFilter(Card.IsRace,RACE_FIEND), except this card). The battle target is
            // battle-destroyed and IsType(TYPE_FLIP) → EFFECT_DISABLE + EFFECT_DISABLE_EFFECT, RESETS_STANDARD_EXC_GRAVE.
            new AtomRegistration.Atom(
                SentenceStart + @"While you control another " + TypeWord + @" monster, negate the effects of Flip monsters " +
                @"destroyed by battle with this card" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle,
                    Side = EffectSide.Controller,
                    AffectsThisCardOnly = true,
                    TargetCardKind = "Flip",
                    CountRaceFilter = m.Groups[1].Value.Trim(),
                    ExceptThisCard = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Lesser Fiend (c16475472): e1 SINGLE EFFECT_BATTLE_DESTROY_REDIRECT, value LOCATION_REMOVED, SINGLE_RANGE
            // LOCATION_MZONE: a monster this card destroys by battle is banished instead of being sent to the GY.
            new AtomRegistration.Atom(
                SentenceStart + @"Banish any monster destroyed by battle with this card" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.BanishMonstersDestroyedByBattleWithThis,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Double Snare (c3682106): EFFECT_TYPE_ACTIVATE, EVENT_FREE_CHAIN, EFFECT_FLAG_CARD_TARGET: target 1 face-up
            // card on either field that has the Double Snare marker (aux.DoubleSnareValidity, EFFECT_FLAG_CANNOT_DISABLE),
            // except this card; at resolution destroy it while it is still face-up and related.
            new AtomRegistration.Atom(
                SentenceStart + @"Destroy 1 face-up card on the field that has an effect that negates Trap Cards['’] " +
                @"effects" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects,
                    RequiresTargetChoice = true,
                    // Pre-PSCT wording, but the Lua targets (EFFECT_FLAG_CARD_TARGET).
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    ExceptThisCard = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),
            // QC LvAndPins (2026-10-18): Silent Swordsman LV3's "Negate your opponent's Spell effects that target this
            // card." atom (cc5a4dd9d) was removed — the sentence stays leftover, so LV3 is not FullyCompiled. Its marker
            // never applied: TryNegateTargetingLink's RespondsTo needs NegateRespondsToSpell, which the atom did not set,
            // so an opponent's targeting Spell still resolved on it; and the shared answer is Freed's "negate AND destroy"
            // (plus the SweepPersistentTargets self-destroy), while LV3's Lua (c1995985 s.disop, EVENT_CHAIN_SOLVING) only
            // calls Duel.NegateEffect. Compiling it needs a negate-only arm first.
        };
    }
}
