using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EngineHygiene batch (v160). Whole-sentence atoms for the cards that needed an engine subsystem first:
    /// <list type="bullet">
    /// <item>"Must first be Special Summoned (from your hand) by banishing N ATTRIBUTE monster(s) from your GY." — the
    /// card's own hand procedure (Lua EFFECT_SPSUMMON_PROC, banish as the cost), compiled to the Toons procedure clause
    /// <see cref="EffectActionKind.SpecialSummonFromHandProcedure"/> with <see cref="EffectClause.BanishFromGyCount"/> /
    /// <see cref="EffectClause.AttributeFilter"/> (Gigantes, Spirit of Flames, Inferno). DuelEngine.CanSpecialSummonProcedure
    /// offers it (the DuelUI hand "Special Summon" button); <see cref="EngineHygieneRuntime"/> pays the banish. Dark
    /// Necrofear's Type variant stays on <see cref="EquipFamilyAtoms"/>; "1 LIGHT and 1 DARK" (Chaos Sorcerer) is not
    /// matched.</item>
    /// <item>Spirit of Flames' Battle Phase ATK (restored with its procedure).</item>
    /// <item>Inferno: "If this card destroys an opponent's monster by battle and sends it to the GY: Inflict N damage to
    /// your opponent." (the attacker or the defender; DuelEngine battle destruction).</item>
    /// <item>Ekibyo Drakmord: the cannot-attack curse with its End Phase countdown
    /// (<see cref="EffectActionKind.DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis"/>).</item>
    /// <item>Forced Requisition: activated only in the "you discarded" window, then each discard of yours makes the
    /// opponent discard as many (<see cref="DiscardEvents"/>).</item>
    /// <item>Super Rejuvenation: the End Phase draw per Dragon discarded or Tributed this turn.</item>
    /// </list>
    /// </summary>
    public static class EngineHygieneAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";
        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";
        const string Apos = @"['’]";
        const string Attributes = @"LIGHT|DARK|EARTH|WATER|FIRE|WIND|DIVINE";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Gigantes / Spirit of Flames / Inferno: a single Attribute (Chaos Sorcerer's "1 LIGHT and 1 DARK" is not
            // matched). Lua: EFFECT_SPSUMMON_PROC from LOCATION_HAND, Duel.Remove(g, POS_FACEUP, REASON_COST).
            new AtomRegistration.Atom(
                Start + @"Must first be Special Summoned \(from your hand\) by banishing (\d+) (" + Attributes +
                @") monsters? from your (?:GY|Graveyard)" + End,
                m =>
                {
                    var n = Parse(m, 1, 0);
                    if (n <= 0) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.SpecialSummonFromHandProcedure,
                        Amount = 0, // no Tribute
                        BanishFromGyCount = n,
                        AttributeFilter = m.Groups[2].Value.ToUpperInvariant(),
                        FromHand = true,
                        MakesChainLink = false
                    };
                }),

            // Spirit of Flames (c13522325 e2): EFFECT_UPDATE_ATTACK, condition turn player == controller and
            // Duel.IsBattlePhase().
            new AtomRegistration.Atom(
                Start + @"This card gains (\d+) ATK during your Battle Phase only" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 300),
                    DefAmount = 0,
                    AffectsThisCardOnly = true,
                    RequiresBattlePhase = true,
                    ControllerTurnOnly = true,
                    MakesChainLink = false
                }),

            // Inferno (c74823665 e2): EVENT_BATTLE_DESTROYING, TRIGGER_F; c:IsRelateToBattle() and the battle target
            // is in the GY by battle. The snippet keeps "sends it to the GY" (RedirectAndReplaceRuntime.NeedsGyButWasBanished).
            new AtomRegistration.Atom(
                Start + @"If this card destroys an opponent" + Apos + @"s monster by battle and sends it to the " +
                @"(?:GY|Graveyard): Inflict (\d+) damage to your opponent" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 0),
                    MakesChainLink = true
                }),

            // Ekibyo Drakmord (c69954399): AddEquipProcedure (any face-up monster), EFFECT_CANNOT_ATTACK, a turn counter
            // ticked at each End Phase of the equipped monster's controller (EFFECT_FLAG_CANNOT_DISABLE), the destroy at
            // count N, then this card returns to the hand.
            // — 17185260 Inferno Hammer: When this card destroys your opponent's monster and sends it to the Graveyard as a result of battle, you can select 1 face-up monster on your opponent's side of the field and flip it into face-down Defense Position.

            AtomRegistration.Atom.Sequence(
                Begin + @"The equipped monster cannot attack\.\s*" +
                @"Destroy the equipped monster at the end of its controller" + Apos + @"s (\d+)(?:st|nd|rd|th) turn " +
                @"after this card is activated\.\s*" +
                @"At that time, this card is returned to the owner" + Apos + @"s hand" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquippedCannotAttack,
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis,
                        Amount = Parse(m, 1, 2),
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Forced Requisition (c74923978): e1 EFFECT_TYPE_ACTIVATE on EVENT_DISCARD (a card of yours was discarded);
            // e2 TRIGGER_F in the S/T Zone on each later EVENT_DISCARD: DiscardHand(1-tp, nil, ct, ct). The activation
            // only places the card; the continuous part is read by DiscardEvents from the face-up card.
            new AtomRegistration.Atom(
                Begin + @"You can activate this card when you discard from your hand\.\s*" +
                @"After that, each time you discard from your hand, your opponent must also discard the same number " +
                @"of cards from (?:his/her|their) hand" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard,
                    Side = EffectSide.Opponent,
                    ActivationCondition = DiscardEvents.YouDiscardedFromHand,
                    CheckedAt = ConditionCheckedAt.Activation,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Super Rejuvenation (c27770341): Quick-Play; registers an End Phase draw of s[tp] (Dragons this player
            // discarded or Tributed this turn, counted from the start of the turn).
            new AtomRegistration.Atom(
                Begin + @"During the End Phase of the turn this card was activated, draw a number of cards equal to the " +
                @"combined number of Dragon monsters you discarded or Tributed from your hand or field this turn" +
                Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DrawAtEndPhasePerDragonsDiscardedOrTributedThisTurn,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
