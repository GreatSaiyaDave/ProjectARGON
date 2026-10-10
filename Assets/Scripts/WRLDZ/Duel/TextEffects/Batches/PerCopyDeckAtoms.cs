using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PerCopyDeck" (v163, PSV / PGD): cards that need to know one copy in a Deck or a hand apart from the
    /// others — whose card it is and whether it is face-up (<see cref="DeckPile"/>, <see cref="DeckCopyInfo"/>). Each
    /// pattern is anchored to the start and the end of the whole printed text, so a longer or reworded text stays
    /// uncompiled. Runtime: <see cref="PerCopyDeckRuntime"/>.
    /// </summary>
    public static class PerCopyDeckAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.\s*$";
        const string Gy = @"(?:GY|Graveyard)";

        /// <summary>Pharaoh's Treasure's draw trigger, up to (not including) its final period.</summary>
        const string PharaohDrawTrigger =
            @"If you draw this face-up card that was added to the Deck by this effect:\s*Send it to the " + Gy +
            @", then target 1 card in your " + Gy + @", except this card; add that target to your hand";

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Parasite Paracide (c27911549): SINGLE + FLIP; the operation (if this card is still related and was not
            // destroyed by battle) SendtoDeck(c, 1-tp, SEQ_DECKSHUFFLE), ShuffleDeck(1-tp), ReverseInDeck, and registers
            // on the card a SINGLE + TRIGGER_F EVENT_DRAW effect (reset RESETS_STANDARD minus RESET_TOHAND): its
            // controller (the player who drew it) Special Summons it to their own field in face-up Defense Position; if
            // that succeeds, Damage(tp, 1000), then a FIELD EFFECT_CHANGE_RACE on LOCATION_MZONE of its controller's side
            // (RACE_INSECT) that lasts while it stays face-up on the field (RESETS_STANDARD).
            AtomRegistration.Atom.Sequence(
                Begin + @"FLIP:\s*Put this card face-up in your opponent['’]s Deck and shuffle it\.\s*When your " +
                @"opponent draws this card, it is Special Summoned to their side of the field in face-up Defense " +
                @"Position, and inflicts (\d+) damage to them\.\s*Then, while this card is face-up on the field, all " +
                @"monsters your opponent controls become ([A-Za-z]+(?: [A-Za-z]+)?)-Type monsters" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.PutThisFaceUpInOpponentDeck,
                        Side = EffectSide.Controller,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.DrawnFaceUpFromDeck,
                        Action = EffectActionKind.SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 1, 1000),
                        RaceFilter = m.Groups[2].Value.Trim(),
                        SummonInDefense = true,
                        MakesChainLink = true
                    }
                }),

            // Pharaoh's Treasure (c63571750): ACTIVATE (EVENT_FREE_CHAIN), no target; the operation (if this card is still
            // related) CancelToGrave, SendtoDeck(c, tp, SEQ_DECKSHUFFLE), ReverseInDeck, and registers on the card a
            // SINGLE + TRIGGER_F EVENT_DRAW effect with EFFECT_FLAG_CARD_TARGET (reset RESETS_STANDARD minus RESET_TOHAND):
            // target 1 card in your GY (it cannot activate without one); on resolution, if both are still related,
            // SendtoGrave(this card) and, if it is in the GY, SendtoHand(the target). Two atoms, one per effect, each
            // anchored to the whole text through look-arounds (the activation does not target; the draw trigger does).
            new AtomRegistration.Atom(
                Begin + @"Shuffle this card face-up into your Deck\.(?=\s*" + PharaohDrawTrigger + Finish + ")",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ShuffleThisFaceUpIntoYourDeck,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            new AtomRegistration.Atom(
                @"(?<=^\s*Shuffle this card face-up into your Deck\.\s*)" + PharaohDrawTrigger + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.DrawnFaceUpFromDeck,
                    Action = EffectActionKind.SendDrawnThisToGyThenAddGyTargetToHand,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyCards,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),

            // Graverobber (c61705417): ACTIVATE (EVENT_FREE_CHAIN), EFFECT_FLAG_CARD_TARGET on 1 Spell in the opponent's
            // GY that can go to the hand; on resolution (if still related) SendtoHand(tc, tp) and a flag on it reset when
            // it goes to the GY / is banished / goes to the Deck or at the End Phase. Registered for this turn: at the End
            // Phase, if it is controlled by a player other than its owner and still flagged, SendtoGrave; when tp
            // activates it (EVENT_CHAINING) and that Chain Link resolves (EVENT_CHAIN_SOLVED), Damage(tp, 2000), once.
            new AtomRegistration.Atom(
                Begin + @"Select 1 Spell Card from your opponent['’]s " + Gy + @"\.\s*You can use it as your hand " +
                @"until the end of the turn\.\s*If you use it, you take (\d+) points of damage" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddOpponentGySpellToHandUsableThisTurn,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.OppGySpells,
                    Amount = Parse(m, 1, 2000),
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),
        };
    }
}
