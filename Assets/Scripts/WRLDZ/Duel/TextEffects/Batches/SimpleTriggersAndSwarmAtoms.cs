using System.Linq;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimpleTriggersAndSwarm": simple monster triggers and "Special Summon any number of X from your hand".
    /// Every atom is one whole sentence (anchored), so a longer or reworded sentence stays uncompiled.
    /// Runtime: <see cref="SimpleTriggersAndSwarmRuntime"/>.
    /// <list type="bullet">
    /// <item>Tsukuyomi (c34853266): "If this card is Normal Summoned or flipped face-up: Target 1 face-up monster on the
    /// field; change that target to face-down Defense Position." (its Spirit return and "Cannot be Special Summoned"
    /// compile through the shared Spirit templates).</item>
    /// <item>Burning Algae (c41859700) / Outstanding Dog Marron (c11548522): "When this card is sent to the (your) GY"
    /// from anywhere (<see cref="EffectTiming.SentToGyFromAnywhere"/>).</item>
    /// <item>Little-Winguard (c90790253): optional, once per turn, during your End Phase: change this card's position.</item>
    /// <item>Don Turtle (c3493978) / Mustering of the Dark Scorpions (c68191243): Special Summon any number of a named
    /// card / an archetype's monsters from your hand.</item>
    /// <item>Dark Scorpion Combination (c20858318): with every named monster face-up on your field, they can attack
    /// directly this turn and their direct-attack battle damage to your opponent becomes N.</item>
    /// </list>
    /// </summary>
    public static class SimpleTriggersAndSwarmAtoms
    {
        const string Start = @"^\s*";
        const string End = @"\.?\s*$";
        const string Apos = @"['’]";
        const string Q = "[\"“”]";

        /// <summary>Start of a sentence: the text start, or right after ". ".</summary>
        const string SentenceStart = @"(?:^|(?<=[.!?]\s))";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string SentenceEnd = @"\.?(?=\s|$)";

        static readonly Regex RxQuoted = new(Q + @"([^""“”]+)" + Q, RegexOptions.Compiled);

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Tsukuyomi (c34853266). ygopro: SINGLE+TRIGGER_F on EVENT_SUMMON_SUCCESS and EVENT_FLIP, CARD_TARGET —
            // 1 face-up monster on either field that can be turned face-down (itself included); ChangePosition to
            // POS_FACEDOWN_DEFENSE. One sentence: the Spirit return and "Cannot be Special Summoned" are other templates.
            new AtomRegistration.Atom(
                SentenceStart + @"If this card is Normal Summoned or flipped face-up: Target 1 face-up monster on the " +
                @"field; change that target to face-down Defense Position" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SetTargetFaceDownDefense,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    TargetMayBeThisCard = true,
                    IsPsctTarget = true,
                    RequiresThisNormalOrFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),

            // Burning Algae (c41859700). ygopro: SINGLE+TRIGGER_F on EVENT_TO_GRAVE (from any location), PLAYER_TARGET
            // 1-tp; Duel.Recover(1-tp, 1000).
            new AtomRegistration.Atom(
                Start + @"When this card is sent to the (?:GY|Graveyard), increase your opponent" + Apos +
                @"s Life Points by (\d+)(?: points)?" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentToGyFromAnywhere,
                    Action = EffectActionKind.GainLifePoints,
                    Side = EffectSide.Opponent,
                    Amount = int.Parse(m.Groups[1].Value),
                    MakesChainLink = true
                }),

            // Outstanding Dog Marron (c11548522). ygopro: SINGLE+TRIGGER_F on EVENT_TO_GRAVE; if IsRelateToEffect,
            // SendtoDeck(c, nil, SEQ_DECKSHUFFLE, REASON_EFFECT).
            new AtomRegistration.Atom(
                Start + @"When this card is sent to your (?:GY|Graveyard), add it to your Deck and shuffle it" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentToGyFromAnywhere,
                    Action = EffectActionKind.ShuffleThisFromGyIntoDeck,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),

            // Little-Winguard (c90790253). ygopro: FIELD+TRIGGER_O on EVENT_PHASE+PHASE_END, range MZONE, count limit 1,
            // condition tp == turn player; ChangePosition(c, face-up Defense ← face-up Attack, face-up Attack otherwise).
            new AtomRegistration.Atom(
                Start + @"Once per turn, during your End Phase: You can change the battle position of this card" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.ChangeThisBattlePosition,
                    Side = EffectSide.Controller,
                    IsOptional = true,
                    OncePerTurn = true,
                    MakesChainLink = true
                }),

            // Don Turtle (c3493978). ygopro: SINGLE+TRIGGER_O on EVENT_SUMMON_SUCCESS and EVENT_FLIP_SUMMON_SUCCESS (not a
            // Special Summon, not a flip by battle or effect); chk: a free Monster Zone and a copy in the hand;
            // SelectMatchingCard 1..ft, SpecialSummon(g, POS_FACEUP).
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned or Flip Summoned, you can Special Summon any number of " +
                Q + @"([^""“”]+)" + Q + @"(?:\(s\))? from your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonAnyNumberFromHand,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value.Trim(),
                    FromHand = true,
                    RequiresNormalOrFlipSummon = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Mustering of the Dark Scorpions (c68191243). ygopro: ACTIVATE (free chain), condition a face-up "Don Zaloog"
            // on your field (LOCATION_ONFIELD); chk: a free Monster Zone and a SET_DARK_SCORPION monster in the hand that
            // can be Special Summoned; SelectUnselectGroup 1..ft with aux.dncheck (different names), POS_FACEUP.
            new AtomRegistration.Atom(
                Start + @"If you control a face-up " + Q + @"([^""“”]+)" + Q + @": You can Special Summon any number of " +
                Q + @"([^""“”]+)" + Q + @" monsters from your hand, but only 1 copy of each" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonAnyNumberFromHand,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[2].Value.Trim(),
                    NamedCardIsSeries = true,
                    SummonOnlyOneOfEachName = true,
                    FromHand = true,
                    ConditionName = m.Groups[1].Value.Trim(),
                    ActivationCondition = SimpleTriggersAndSwarmRuntime.FaceUpNamedAndHandSummonable,
                    MakesChainLink = true
                }),

            // Dark Scorpion Combination (c20858318). ygopro: ACTIVATE (free chain), condition every listed card face-up in
            // your Monster Zones; flags them until the End Phase; FIELD EFFECT_DIRECT_ATTACK for flagged monsters and FIELD
            // EFFECT_CHANGE_BATTLE_DAMAGE (to 1-tp) = 400 while a flagged attacker attacks directly (s.rdcon). The count of
            // names must equal "these N cards" (fail closed otherwise).
            new AtomRegistration.Atom(
                Start + @"You can only activate this card when there are ((?:" + Q + @"[^""“”]+" + Q + @",?\s*)+and\s*" +
                Q + @"[^""“”]+" + Q + @") face-up on your side of the field\.\s*" +
                @"During this turn, any of these (\d+) cards can attack your opponent" + Apos +
                @"s Life Points directly\.\s*In that case, the Battle Damage inflicted to your opponent by each of those " +
                @"cards becomes (\d+)(?: points)?" + End,
                m =>
                {
                    var names = RxQuoted.Matches(m.Groups[1].Value).Cast<Match>()
                        .Select(x => x.Groups[1].Value.Trim()).Where(n => n.Length > 0).ToList();
                    if (names.Count < 2 || names.Count != int.Parse(m.Groups[2].Value)) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn,
                        Side = EffectSide.Opponent,
                        ConditionName = string.Join("|", names),
                        Amount = int.Parse(m.Groups[3].Value),
                        ActivationCondition = SimpleTriggersAndSwarmRuntime.AllNamedFaceUpInYourMonsterZones,
                        MakesChainLink = true
                    };
                }),

            // Two-Man Cell Battle (c25578802): SOD Continuous Spell. ygopro: a free-chain EFFECT_TYPE_ACTIVATE that
            // only places the card, plus a FIELD+TRIGGER_O effect on EVENT_PHASE+PHASE_END with EFFECT_FLAG_BOTH_SIDE
            // and CountLimit(1) whose condition is Duel.IsTurnPlayer(tp) — so this is a both-side phase trigger whose
            // actor is the TURN player, not this card's controller (the engine's FiresAtEachPlayersPhase). The
            // printed sentence is only the trigger, so the atom emits exactly one clause.
            new AtomRegistration.Atom(
                SentenceStart + @"During each player" + Apos + @"s End Phase, the turn player can Special Summon " +
                @"(\d+) Level (\d+) Normal Monsters? from their hand" + SentenceEnd,
                m =>
                {
                    var count = int.Parse(m.Groups[1].Value);
                    if (count != 1) return null; // the effect is exactly one monster
                    var level = int.Parse(m.Groups[2].Value);
                    return new EffectClause
                    {
                        Timing = EffectTiming.EndPhase,
                        Action = EffectActionKind.SpecialSummonLevelNormalFromHandAtEachPlayersEndPhase,
                        Zone = EffectZoneFilter.ControllerHandMonsters,
                        FiresAtEachPlayersPhase = true,
                        ExactLevel = level,
                        IsOptional = true,
                        MakesChainLink = true
                    };
                }),
        };
    }
}
