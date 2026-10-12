using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "IgnitionCostsAndSimpleSpells": monster ignitions and plain Spells/Traps whose cost is a Tribute, a send
    /// or a discard, a 3-coin toss and a per-count burn. Every atom owns the card's whole text (anchored ^…$), so a longer or
    /// reworded card stays uncompiled. Runtime: <see cref="IgnitionCostsAndSimpleSpellsRuntime"/>.
    /// <list type="bullet">
    /// <item>3-Hump Lacooda (c86988864): 3+ face-up copies in your Monster Zone; Tribute 2 face-up copies; draw 3.</item>
    /// <item>Avatar of The Pot (c99284890): send 1 "Pot of Greed" from your hand (a send, not a discard); draw 3.</item>
    /// <item>Lady Ninja Yae (c82005435): discard 1 WIND monster; every Spell/Trap the opponent controls to the hand.</item>
    /// <item>Sand Gambler (c50593156): once per turn, 3 coins; all Heads / all Tails destroys one side's monsters.</item>
    /// <item>Mystik Wok (c80161395): Tribute 1 monster (ATK or DEF above 0); gain its ATK or DEF, chosen on activation.</item>
    /// <item>Solar Ray (c44472639): 600 damage for each face-up LIGHT monster you control, counted at resolution.</item>
    /// <item>Special Hurricane (c42598242): discard 1 other card; destroy every Special Summoned monster on the field.</item>
    /// </list>
    /// </summary>
    public static class IgnitionCostsAndSimpleSpellsAtoms
    {
        const string Gy = @"(?:Graveyard|GY)";
        const string Apos = @"['’]";
        const string Q = @"[""“”]";
        const string S = @"^\s*";
        const string E = @"\.?\s*$";
        const string Attr = @"(DARK|LIGHT|EARTH|WATER|FIRE|WIND|DIVINE)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // 3-Hump Lacooda (c86988864): EFFECT_TYPE_IGNITION, no count limit. Condition: 3+ face-up "3-Hump Lacooda" in
            // your Monster Zone (this card counts). Cost: SelectReleaseGroupCost(cfilter, 2, 2) — Tribute 2 FACE-UP copies
            // (this card may be one). Target check: IsPlayerCanDraw(tp, 3). Operation: draw 3.
            new AtomRegistration.Atom(
                S + @"If there are (\d+) face-up " + Q + @"([^""“”]+)" + Q + @" cards on your side of the field, " +
                @"Tribute (\d+) of them to draw (\d+) cards" + E,
                m =>
                {
                    var need = Parse(m, 1, 3);
                    var tributes = Parse(m, 3, 2);
                    if (tributes < 1 || tributes > need) return null; // fail closed: not "N of them"
                    var name = m.Groups[2].Value.Trim();
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Draw,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 4, 3),
                        RequiresTributeCount = tributes,
                        TributeFaceUpOnly = true,
                        TributeNamedCard = name,
                        ActivationCondition = IgnitionCostsAndSimpleSpellsRuntime.FaceUpNamedYouControlAtLeast,
                        ConditionName = name,
                        ConditionAmount = need,
                        CheckedAt = ConditionCheckedAt.Activation,
                        IsOptional = true,
                        MakesChainLink = true
                    };
                }),

            // Avatar of The Pot (c99284890): EFFECT_TYPE_IGNITION. Cost: Duel.DiscardHand(tp, IsCode(Pot of Greed), 1, 1,
            // REASON_COST) — no REASON_DISCARD, so a SEND from the hand (DiscardCostIsSend; no discard event). Target
            // check: IsPlayerCanDraw(tp, 3). Operation: draw 3.
            new AtomRegistration.Atom(
                S + @"By sending 1 " + Q + @"([^""“”]+)" + Q + @" from your hand to the " + Gy + @", draw (\d+) cards " +
                @"from your Deck" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 2, 3),
                    RequiresDiscardCost = true,
                    DiscardCostIsSend = true,
                    DiscardCostCount = 1,
                    DiscardCostAttribute = IgnitionCostsAndSimpleSpellsRuntime.NamedHandCard(m.Groups[1].Value.Trim()),
                    ActivationCondition = DrawThenDiscardRuntime.CanDrawAmount,
                    CheckedAt = ConditionCheckedAt.Activation,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Lady Ninja Yae (c82005435): EFFECT_TYPE_IGNITION. Cost: discard 1 WIND monster from the hand (REASON_COST |
            // REASON_DISCARD). Target check: 1+ Spell/Trap the opponent controls (LOCATION_ONFIELD: Spell & Trap Zones and
            // the Field Zone, face-up or Set). Operation: every such card at resolution to its owner's hand (no target).
            new AtomRegistration.Atom(
                S + @"By discarding 1 " + Attr + @" monster from your hand to the " + Gy + @", return all Spell and Trap " +
                @"Cards on your opponent" + Apos + @"s side of the field to the owner" + Apos + @"s hand" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnToHand,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = false,
                    RequiresDiscardCost = true,
                    DiscardCostCount = 1,
                    DiscardCostAttribute = m.Groups[1].Value.ToUpperInvariant(),
                    ActivationCondition = IgnitionCostsAndSimpleSpellsRuntime.OpponentControlsSpellTrap,
                    CheckedAt = ConditionCheckedAt.Activation,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Sand Gambler (c50593156): EFFECT_TYPE_IGNITION, SetCountLimit(1) (soft once per turn). Operation: TossCoin(tp, 3);
            // 3 Heads: destroy every monster the opponent controls; 3 Tails: destroy every monster you control (itself
            // included); otherwise nothing. Ignitions are Main Phase only.
            new AtomRegistration.Atom(
                S + @"Toss a coin (\d+) times\.\s*If all \1 results are Heads, destroy all monsters on your opponent" + Apos +
                @"s side of the field\.\s*If all \1 results are Tails, destroy all monsters on your side of the field\.\s*" +
                @"You can only activate this effect once per turn, during your Main Phase" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours,
                    Side = EffectSide.Both,
                    CoinCount = Parse(m, 1, 3),
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Mystik Wok (c80161395): Quick-Play, EFFECT_FLAG_PLAYER_TARGET. In s.target (activation): SelectReleaseGroupCost
            // 1 monster with GetAttack() > 0 or GetDefense() > 0, Release(REASON_COST); then SelectEffect ATK / DEF (only the
            // one above 0 when the other is 0); the value is the chain parameter. Operation: Recover that much.
            new AtomRegistration.Atom(
                S + @"Tribute 1 monster\.\s*Select either its ATK or DEF and gain that many Life Points" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainLpEqualToTributedAtkOrDef,
                    Side = EffectSide.Controller,
                    RequiresTributeCount = 1,
                    RequiresTargetChoice = false,
                    // The Tribute is paid only on the card's own activation path (not a response-window seat or a chain
                    // answer, which pay no generic Tribute): those windows refuse it.
                    ActivationCondition = IgnitionCostsAndSimpleSpellsRuntime.TributeCostPaidOnActivation,
                    MakesChainLink = true
                }),

            // Solar Ray (c44472639): Normal Trap, EVENT_FREE_CHAIN. Target check: 1+ face-up LIGHT monster you control.
            // Operation: 600 x face-up LIGHT monsters you control, counted at resolution (current Attribute).
            new AtomRegistration.Atom(
                S + @"Inflict (\d+) points of damage to your opponent" + Apos + @"s Life Points for each face-up " + Attr +
                @" monster on your side of the field" + E,
                m =>
                {
                    var attribute = m.Groups[2].Value.ToUpperInvariant();
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 1, 600),
                        CountSource = IgnitionCostsAndSimpleSpellsRuntime.YourFaceUpMonstersOfConditionAttribute,
                        ActivationCondition = IgnitionCostsAndSimpleSpellsRuntime.YouControlFaceUpAttribute,
                        ConditionName = attribute,
                        ConditionAmount = 1,
                        MakesChainLink = true
                    };
                }),

            // Special Hurricane (c42598242): Normal Spell, EVENT_FREE_CHAIN. Cost: discard 1 card other than itself
            // (Card.IsDiscardable, e:GetHandler() excluded; REASON_COST | REASON_DISCARD). Target check: 1+ Special Summoned
            // monster on either field. Operation: destroy every Special Summoned monster on the field (no target).
            new AtomRegistration.Atom(
                S + @"Discard 1 card from your hand\.\s*Destroy all Special Summoned monsters on the field" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroySpecialSummonedMonsters,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = false,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = 1,
                    ActivationCondition = RuleLocksRuntime.SpecialSummonedMonsterOnField,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : fallback;
    }
}
