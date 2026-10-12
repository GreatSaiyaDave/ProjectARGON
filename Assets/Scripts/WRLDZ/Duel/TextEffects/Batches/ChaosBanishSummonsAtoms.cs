using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ChaosBanishSummons batch. Whole-sentence atoms for the LIGHT + DARK / WIND banish procedures, banish costs and
    /// effects, and the banish event (<see cref="ChaosBanishSummonsRuntime"/>, <see cref="BanishEvents"/>):
    /// <list type="bullet">
    /// <item>"Must first be Special Summoned (from your hand) by banishing 1 LIGHT and 1 DARK monster from your GY."
    /// (Chaos Sorcerer, Black Luster Soldier - Envoy of the Beginning; Lua EFFECT_SPSUMMON_PROC with rescon: exactly one of
    /// each Attribute) — the Toons procedure clause with <see cref="EffectClause.SecondAttributeFilter"/>.</item>
    /// <item>Chaos Sorcerer's ignition (face-up target, oath "cannot attack the turn you activate this effect").</item>
    /// <item>Black Luster Soldier - Envoy of the Beginning: "Once per turn, you can activate 1 of these effects." with its
    /// ignition (any monster) and its "second attack in a row" battle trigger.</item>
    /// <item>Silpheed: "When this card is destroyed as a result of battle and sent to the Graveyard, your opponent
    /// discards 1 random card from their hand." (its procedure is LegacyTextTemplates' old-wording banish procedure).</item>
    /// <item>Strike Ninja's Quick Effect, Chaosrider Gustaph's ignition, Soul Absorption, Primal Seed.</item>
    /// </list>
    /// </summary>
    public static class ChaosBanishSummonsAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";
        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";
        const string Apos = @"['’]";
        const string Attributes = @"LIGHT|DARK|EARTH|WATER|FIRE|WIND|DIVINE";

        const string BlsHeader = @"Once per turn, you can activate 1 of these effects\.\s*";
        const string BlsIgnition = @"●\s*Target 1 monster on the field; banish it\.\s*This card cannot attack the turn this " +
                                   @"effect is activated\.";
        const string BlsTrigger = @"●\s*If this attacking card destroys an opponent['’]s monster by battle: It can make a " +
                                  @"second attack in a row\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Chaos Sorcerer / BLS - Envoy of the Beginning / Chaos Emperor Dragon (c9596126 e0: EFFECT_SPSUMMON_PROC from
            // LOCATION_HAND; s.rescon: 2 cards, one LIGHT and one DARK; Duel.Remove(sg, POS_FACEUP, REASON_COST)).
            new AtomRegistration.Atom(
                Start + @"Must first be Special Summoned \(from your hand\) by banishing 1 (" + Attributes + @") and 1 (" +
                Attributes + @") monster from your (?:GY|Graveyard)" + End,
                m =>
                {
                    var a = m.Groups[1].Value.ToUpperInvariant();
                    var b = m.Groups[2].Value.ToUpperInvariant();
                    if (a == b) return null; // "1 LIGHT and 1 LIGHT" is "2 LIGHT monsters", another sentence
                    return new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.SpecialSummonFromHandProcedure,
                        Amount = 0, // no Tribute (DuelUI reads HandProcedureOf(...).Amount)
                        BanishFromGyCount = 2,
                        AttributeFilter = a,
                        SecondAttributeFilter = b,
                        FromHand = true,
                        MakesChainLink = false
                    };
                }),

            // Chaos Sorcerer (c9596126 e1): IGNITION, CountLimit(1), EFFECT_FLAG_CARD_TARGET; rmcost: no attack declared
            // this turn, then EFFECT_CANNOT_ATTACK (OATH); target a face-up monster in either Monster Zone (this card too);
            // rmop: still face-up → Duel.Remove(tc, POS_FACEUP, REASON_EFFECT).
            new AtomRegistration.Atom(
                Start + @"Once per turn: You can target 1 face-up monster on the field; banish that target\.\s*" +
                @"This card cannot attack the turn you activate this effect" + End,
                _ => IgnitionBanish(faceUpOnly: true)),

            // Black Luster Soldier - Envoy of the Beginning (c72989439 e2 + e3). The ignition (CountLimit(1), any monster in
            // either Monster Zone, face-down too, this card too) registers a flag; the EVENT_BATTLE_DESTROYING trigger needs
            // Duel.GetAttacker()==c, aux.bdocon, no flag and c:CanChainAttack() → Duel.ChainAttack(). Two atoms over the
            // whole "1 of these effects" block (each needs the other bullet), so each clause keeps its own sentence.
            new AtomRegistration.Atom(
                Start + BlsHeader + BlsIgnition + @"(?=\s*" + BlsTrigger + @")",
                _ => IgnitionBanish(faceUpOnly: false)),
            new AtomRegistration.Atom(
                @"(?<=" + BlsHeader + BlsIgnition + @"\s*)" + BlsTrigger,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.MakeSecondAttackInARow,
                    Side = EffectSide.Controller,
                    IsOptional = true,
                    // "Once per turn, you can activate 1 of these effects": shares the ignition's use this turn.
                    OncePerTurn = true,
                    MakesChainLink = true
                }),

            // Silpheed (c73001017 e2): SINGLE TRIGGER_F on EVENT_BATTLE_DESTROYED, condition IsLocation(GRAVE) and
            // IsReason(REASON_BATTLE); RandomSelect 1 card of the opponent's hand, REASON_DISCARD + REASON_EFFECT.
            new AtomRegistration.Atom(
                Start + @"When this card is destroyed as a result of battle and sent to the (?:GY|Graveyard), your opponent " +
                @"discards 1 random card from (?:their|his/her|his or her) hand" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.DiscardRandomFromOpponentHand,
                    Side = EffectSide.Opponent,
                    Amount = 1,
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),

            // Strike Ninja (c41006930): QUICK_O, EVENT_FREE_CHAIN, LOCATION_MZONE, CountLimit(1,id); cost: banish 2 face-up
            // DARK monsters from your GY (aux.SpElimFilter: under Spirit Elimination face-up DARK monsters you control),
            // except this card; rmop: this card, still face-up, banished (REASON_TEMPORARY) and returned in the End Phase.
            new AtomRegistration.Atom(
                Begin + @"\(Quick Effect\):\s*You can banish (\d+) (" + Attributes + @") monsters from your (?:GY|Graveyard); " +
                @"banish this face-up card until the End Phase\.\s*" +
                @"You can only use this effect of ""[^""]+"" once per turn" + Finish,
                m =>
                {
                    if (!int.TryParse(m.Groups[1].Value, out var n) || n < 1) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.MonsterQuickEffect,
                        Action = EffectActionKind.BanishThisUntilEndPhase,
                        Side = EffectSide.Controller,
                        BanishFromGyCount = n,
                        AttributeFilter = m.Groups[2].Value.ToUpperInvariant(),
                        IsQuickEffect = true,
                        IsOptional = true,
                        OncePerTurn = true,
                        OptScope = OncePerTurnScope.PerCardName,
                        MakesChainLink = true
                    };
                }),

            // Chaosrider Gustaph (c47829960): IGNITION, CountLimit(1), no cost, no target; rmop: SelectMatchingCard 1-2
            // Spells in your GY, Remove (REASON_EFFECT), then EFFECT_UPDATE_ATTACK ct*300 with RESET_OPPO_TURN.
            new AtomRegistration.Atom(
                Begin + @"Once per turn, you can remove from play up to (\d+) Spell Cards from your Graveyard\.\s*" +
                @"This card gains (\d+) ATK for each card removed, until the end of your opponent" + Apos +
                @"s next turn" + Finish,
                m =>
                {
                    if (!int.TryParse(m.Groups[1].Value, out var cap) || cap < 1) return null;
                    if (!int.TryParse(m.Groups[2].Value, out var per) || per < 1) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.BanishUpToSpellsFromYourGyGainAtkEach,
                        Side = EffectSide.Controller,
                        Amount = per,
                        PickUpTo = cap,
                        ActivationCondition = ChaosBanishSummonsRuntime.YourGySpellBanishable,
                        OncePerTurn = true,
                        IsOptional = true,
                        MakesChainLink = true
                    };
                }),

            // Soul Absorption (c68073522): Continuous Spell; FIELD TRIGGER_F on EVENT_REMOVE in the S/T Zone, condition a
            // non-Token card among eg; Recover(tp, count*500) while this card is still on the field.
            new AtomRegistration.Atom(
                Begin + @"If a card\(s\) is banished: Gain (\d+) LP for each" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainLpPerCardBanished,
                    Side = EffectSide.Controller,
                    Amount = int.TryParse(m.Groups[1].Value, out var lp) ? lp : 500,
                    MakesChainLink = true
                }),

            // Primal Seed (c23701465): condition a face-up card of either name on either field (LOCATION_ONFIELD both
            // sides); SelectTarget exactly 2 of your banished cards (IsAbleToHand); the ones still there go to the hand.
            new AtomRegistration.Atom(
                Begin + @"If ""([^""]+)"" or ""([^""]+)"" is on the field: Target 2 of your banished cards; add both " +
                @"those targets to your hand" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddBanishedTargetsToHand,
                    Zone = EffectZoneFilter.ControllerBanishedCards,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 2,
                    TargetUpTo = false,
                    ActivationCondition = ChaosBanishSummonsRuntime.NamedFaceUpOnEitherField,
                    ConditionName = m.Groups[1].Value + "|" + m.Groups[2].Value,
                    MakesChainLink = true
                }),
        };

        /// <summary>The Chaos ignition: target 1 monster on the field (this card too) and banish it; oath and condition.</summary>
        static EffectClause IgnitionBanish(bool faceUpOnly) => new EffectClause
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.BanishTargetedFieldMonster,
            Zone = EffectZoneFilter.FieldAnyMonster,
            Side = EffectSide.Both,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            RequiresTargetFaceUp = faceUpOnly,
            TargetMayBeThisCard = true,
            OncePerTurn = true,
            IsOptional = true,
            ThisCannotAttackTurnActivated = true,
            ActivationCondition = ChaosBanishSummonsRuntime.ThisDeclaredNoAttackThisTurn,
            MakesChainLink = true
        };
    }
}
