using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonTriggersAndTributes" (v163, PGD): Summon / flip triggers (Byser Shock, Dark Dust Spirit), a
    /// monster host answering any Summon (King Tiger Wanghu), a 3-Tribute procedure (Moisture Creature), the "only 1
    /// face-up" rule and the Necrovalley immunity (Gravekeeper's Chief), a FLIP take control (Jowls of Dark Demise) and a
    /// Level-matched Extra Deck summon for a Tribute (Metamorphosis). Each atom is one whole sentence (or the whole text),
    /// anchored at a sentence start and end, so a longer or reworded sentence stays uncompiled.
    /// Runtime: <see cref="SummonTriggersAndTributesRuntime"/>.
    /// </summary>
    public static class SummonTriggersAndTributesAtoms
    {
        /// <summary>Start of a sentence: the text start, or right after ". ".</summary>
        const string Start = @"(?:^|(?<=[.!?]\s))";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        static int Parse(System.Text.RegularExpressions.Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var v) ? v : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Byser Shock (c17597059): SINGLE+TRIGGER_F on EVENT_SUMMON_SUCCESS, EVENT_SPSUMMON_SUCCESS and
            // EVENT_FLIP_SUMMON_SUCCESS; no target; Duel.SendtoHand(every IsFacedown card in LOCATION_ONFIELD, both sides).
            new AtomRegistration.Atom(
                Start + @"If this card is Summoned: Return all Set cards on the field to the hand" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.ReturnAllSetCardsOnFieldToHand,
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),
            // — 24432029 Genex Blastfan: When this card is Special Summoned, you can add 1 DARK "Genex" monster from your Deck to your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Special Summoned, you can add 1 (?:DARK|LIGHT|EARTH|WATER|FIRE|WIND) ""([^""]+)"" monster from your Deck to your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.AddNamedFromDeckToHand,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    Amount = 1,
                    FromDeck = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 29116732 Daybreaker: When this card is Special Summoned: You can Special Summon 1 "Daybreaker" from your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Special Summoned: You can Special Summon 1 ""([^""]+)"" from your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonFromHand,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 1533292 R-Genex Magma: When this card is Normal Summoned, you can add 1 Level 2 "R-Genex" monster from your Deck to your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned, you can add 1 Level \d+ ""([^""]+)"" (?:Effect )?Monster from your Deck to your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.AddNamedFromDeckToHand,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    Amount = 1,
                    FromDeck = true,
                    RequiresNormalOrFlipSummon = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 27004302 Gem-Armadillo: When this card is Normal Summoned: You can add 1 "Gem-Knight" monster from your Deck to your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned: You can add 1 ""([^""]+)"" monster from your Deck to your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.AddNamedFromDeckToHand,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    Amount = 1,
                    FromDeck = true,
                    RequiresNormalOrFlipSummon = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 13839120 Yellow Gadget: When this card is Normal or Special Summoned: You can add 1 "Green Gadget" from your Deck to your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal or Special Summoned: You can add 1 ""([^""]+)"" (?:monster )?from your Deck to your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.AddNamedFromDeckToHand,
                    NamedCard = m.Groups[1].Value,
                    Amount = 1,
                    FromDeck = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 8175346 Poki Draco: When this card is Normal Summoned: You can add 1 "Poki Draco" from your Deck to your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned: You can add 1 ""([^""]+)"" from your Deck to your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.AddNamedFromDeckToHand,
                    NamedCard = m.Groups[1].Value,
                    Amount = 1,
                    FromDeck = true,
                    RequiresNormalOrFlipSummon = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 28877602 Evoltile Odonto: When this card is Normal Summoned: You can Special Summon 1 "Evolsaur" monster from your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned: You can Special Summon 1 (?:Level \d+ )?""([^""]+)"" monster from your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonFromHand,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    RequiresNormalOrFlipSummon = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 18063928 Tin Goldfish: When this card is Normal Summoned: You can Special Summon 1 Level 4 monster from your hand.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned: You can Special Summon 1 Level (\d+) monster from your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonFromHand,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    AmountIsLevel = true,
                    RequiresNormalOrFlipSummon = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // King Tiger Wanghu (c83986578): FIELD+TRIGGER_F on EVENT_SUMMON_SUCCESS / EVENT_SPSUMMON_SUCCESS (not Flip),
            // range MZONE; condition: a face-up summoned monster with ATK <= 1400 and not this card; at resolution this
            // card must still be face-up and destroys the summoned monsters still face-up with ATK <= 1400.
            new AtomRegistration.Atom(
                @"^\s*When a monster\(s\) with (\d+) or less ATK is Normal Summoned or Special Summoned: Destroy " +
                @"those monsters with \1 or less ATK\.\s*This card must be face-up on the field to activate and to " +
                @"resolve this effect\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned,
                    Side = EffectSide.Both,
                    // The ATK ceiling. Not Amount: Amount is read as a target count by the prompts.
                    ConditionAmount = Parse(m, 1, 1400),
                    MakesChainLink = false
                }),

            // Moisture Creature (c75285069): aux.AddNormalSummonProcedure(c,true,true,3,3,SUMMON_TYPE_TRIBUTE+1) and a
            // SINGLE+TRIGGER_F on EVENT_SUMMON_SUCCESS with condition GetSummonType()==SUMMON_TYPE_TRIBUTE+1; destroy every
            // Spell/Trap the opponent controls (LOCATION_ONFIELD: S/T Zones and the Field Zone).
            new AtomRegistration.Atom(
                Start + @"If you Tribute Summon this monster by Tributing (\d+) monsters on the field, destroy all " +
                @"Spell and Trap Cards on your opponent['’]s side of the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.DestroyAllOppSpellTraps,
                    Side = EffectSide.Opponent,
                    RequiresThisTributeSummoned = true,
                    RequiresThisTributeSummonedByTributingExactly = Parse(m, 1, 3),
                    MakesChainLink = true
                }),

            // Dark Dust Spirit (c89111398): SINGLE+TRIGGER_F on EVENT_SUMMON_SUCCESS and EVENT_FLIP; destroy every face-up
            // monster on both fields except this card (Duel.Destroy(group): one event).
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned or flipped face-up: Destroy all other face-up monsters on " +
                @"the field" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.DestroyAllFaceUpMonstersOnField,
                    Side = EffectSide.Both,
                    ExceptThisCard = true,
                    RequiresThisNormalOrFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),

            // Gravekeeper's Chief (c62473983): c:SetUniqueOnField(1,0,id) — a rule, not an effect.
            new AtomRegistration.Atom(
                Start + @"You can only control 1 face-up ""([^""]+)""" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.YouCanOnlyControlOneFaceUpNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    MakesChainLink = false
                }),

            // Gravekeeper's Chief (c62473983): FIELD EFFECT_NECRO_VALLEY_IM, EFFECT_FLAG_PLAYER_TARGET, range MZONE,
            // target range (1,0) — its controller. Necrovalley's GY locks read it per GY owner.
            new AtomRegistration.Atom(
                Start + @"Your GY is unaffected by ""(Necrovalley)""" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.YourGyUnaffectedByNamedCard,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    MakesChainLink = false
                }),

            // Jowls of Dark Demise (c5257687): SINGLE+FLIP, EFFECT_FLAG_CARD_TARGET on 1 monster the opponent controls
            // (face-up or face-down, IsControlerCanBeChanged); Duel.GetControl(tc,tp,PHASE_END,1), and if it is taken,
            // EFFECT_DIRECT_ATTACK on it (RESETS_STANDARD_PHASE_END).
            new AtomRegistration.Atom(
                @"^\s*FLIP:\s*Take control of 1 monster on your opponent['’]s side of the field until the end of the " +
                @"turn that this card['’]s effect is activated\.\s*When the controlled monster attacks, it can attack " +
                @"your opponent['’]s Life Points directly\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.TakeControlUntilEndPhase,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Amount = 1,
                    TakenMonsterCanAttackDirectlyThisTurn = true,
                    MakesChainLink = true
                }),

            // Metamorphosis (c46411259): cost Tribute 1 monster whose Level has a Fusion Monster of that Level in the
            // Extra Deck that can be Special Summoned with a zone free after the Tribute (e:SetLabel(level) before the
            // release); at resolution Special Summon 1 Fusion Monster of exactly that Level (IsCanBeSpecialSummoned
            // nocheck=false: summoning conditions apply; not a Fusion Summon). The pick is not a target.
            new AtomRegistration.Atom(
                @"^\s*Tribute 1 monster\.\s*Special Summon 1 Fusion Monster from your Extra Deck with the same Level " +
                @"as the Tributed monster\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFusionFromExtra,
                    Side = EffectSide.Controller,
                    RequiresTributeCount = 1,
                    MatchTributedLevel = true,
                    TributedLevelOffset = 0,
                    MakesChainLink = true
                }),
            // Thestalos the Firestorm Monarch (c26205777): If this card is Tribute Summoned: Discard 1 random card from
            // your opponent's hand, then, if it was a Monster Card, inflict damage to your opponent equal to its original Level x 100.
            // Lua: SINGLE TRIGGER_F EVENT_SUMMON_SUCCESS, IsTributeSummoned; RandomSelect 1 of the opponent's hand, discard it,
            // a Monster Card → GetLevel()*100 damage. v208 QC: Amount is the damage per Level (QcHollowActionsRuntime
            // .ResolveDiscardRandomThenDamageByLevel), and only a Tribute Summon fires it.
            new AtomRegistration.Atom(
                Start + @"If this card is Tribute Summoned: Discard 1 random card from your opponent's hand, then, if it " +
                @"was a Monster Card, inflict damage to your opponent equal to its original Level x (\d+)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.DiscardRandomThenDamageByLevelIfMonster,
                    Side = EffectSide.Opponent,
                    RequiresThisTributeSummoned = true,
                    Amount = Pos(m, 1),
                    MakesChainLink = true
                }),
            // B.E.S. Big Core (c14148099): If this card is Normal Summoned: Place 3 counters on it.
            new AtomRegistration.Atom(
                // v208 QC: one whole sentence (Start/End), not the tail of a longer one.
                Start + @"If this card is Normal Summoned: Place (\d+) counters? on it" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.PlaceCounterOnThisCard,
                    Side = EffectSide.Controller,
                    Amount = Pos(m, 1),
                    // BattleRiders batch: Lua EVENT_SUMMON_SUCCESS only — a Flip or Special Summon places no counters.
                    RequiresThisNormalSummoned = true,
                    MakesChainLink = true
                }),
            // Fusilier Dragon, the Dual-Mode Beast (c51632798): You can Normal Summon/Set this card without Tributing,
            // but its original ATK and DEF become halved (engine already handles: TcgRules returns 0 tribute, DuelEngine
            // halves stats on no-tribute summon). Text program just needs to compile.
            new AtomRegistration.Atom(
                Start + @"You can Normal Summon/Set this card without Tributing, but its original ATK and DEF become " +
                @"halved" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Draw,
                    Amount = 0,
                    MakesChainLink = false
                }),
            // Behemoth the King of All Animals (c22996376): Can be Normal Summoned/Set with 1 Tribute.
            // If summoned with 1 Tribute, ATK becomes 2000 (engine: TcgRules returns 1 tribute,
            // DuelEngine sets ATK to 2000). Text program just needs to compile.
            new AtomRegistration.Atom(
                Start + @"This card can be Normal Summoned or Set with 1 Tribute\. In that case, the original " +
                @"ATK of this card becomes 2000\. When this card is Tribute Summoned, you can return from " +
                @"your Graveyard to the owner's hand a number of Beast-Type monsters equal to the number " +
                @"of Tributed monsters" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Draw,
                    Amount = 0,
                    MakesChainLink = false
                }),

        };


        static int Pos(Match m, int group) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 0;
    }
}
