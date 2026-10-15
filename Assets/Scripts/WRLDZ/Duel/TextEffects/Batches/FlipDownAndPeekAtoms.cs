namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "FlipDownAndPeek" (v162): flip-face-down ignition monsters and "look at" effects — 8-Claws Scorpion,
    /// Royal Keeper, Wandering Mummy, Giant Axe Mummy, Cobraman Sakuzy, A Man with Wdjat, Trap Dustshoot. Each atom is a
    /// whole sentence (or a card's whole text), anchored at a sentence start and end, so a longer or reworded sentence
    /// stays uncompiled. "Once per turn, you can flip this card into face-down Defense Position." already compiles in
    /// MonsterTriggerTemplates (RxOptSetFaceDown). Runtime: <see cref="FlipDownAndPeekRuntime"/>.
    /// </summary>
    public static class FlipDownAndPeekAtoms
    {
        /// <summary>Start of a sentence: the text start, or right after ". ".</summary>
        const string S = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string E = @"\.?(?=\s|$)";

        const string FlipDownMainPhase =
            @"You can flip this card into face-down Defense Position once per turn during your Main Phase";

        static int Parse(System.Text.RegularExpressions.Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;

        /// <summary>The flip-face-down ignition (ygopro: IGNITION, IsCanTurnSet, a flag that survives being Set: once per turn).</summary>
        static EffectClause FlipDown(bool thenShuffle) => new EffectClause
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.SetThisFaceDownDefense,
            OncePerTurn = true,
            IsOptional = true,
            MakesChainLink = true,
            ThenShuffleFaceDownMonstersInMainZones = thenShuffle
        };

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Wandering Mummy (42994702), its whole text. ygopro c42994702: the flip-down ignition; if this card was changed
            // to face-down Defense Position, Duel.ShuffleSetCard on the face-down monsters in its controller's Main Monster
            // Zones. Before the one-sentence atom below, which would leave the second sentence unparsed.
            new AtomRegistration.Atom(
                S + FlipDownMainPhase + @"\.\s*After you use this effect, rearrange the face-down Defense Position " +
                @"monsters in your Main Monster Zones, then return them in face-down Defense Position" + E,
                _ => FlipDown(thenShuffle: true)),
            // Cobraman Sakuzy (75109441), Giant Axe Mummy (78266168): the same ignition as "Once per turn, you can flip
            // this card into face-down Defense Position." in the older wording (ygopro: identical s.target / s.operation).
            new AtomRegistration.Atom(
                S + FlipDownMainPhase + E,
                _ => FlipDown(thenShuffle: false)),
            // 8-Claws Scorpion (14261867). ygopro c14261867: TRIGGER_F at EVENT_PRE_DAMAGE_CALCULATE while this card is the
            // attacker and its battle target's battle position is face-down Defense; EFFECT_SET_ATTACK_FINAL 2400 reset at
            // the end of damage calculation. A marker applied by FlipDownAndPeekRuntime.ApplyDamageCalculationAtk.
            new AtomRegistration.Atom(
                S + @"When this card attacks an opponent's face-down Defense Position monster, this card's ATK becomes " +
                @"(\d+) during damage calculation only" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AtkBecomesAmountWhenAttackingFaceDownDefenseThisCalculation,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2400),
                    MakesChainLink = false
                }),
            // Nightmare Penguin (c81306586) e2: SINGLE TRIGGER_F on EVENT_FLIP (Flip Summon, flipped by an attack, or
            // by a card effect) — target 1 card the opponent controls and return it to the hand. The card's other
            // sentence, the face-up WATER aura, is in SelfStatMonstersAtoms.
            new AtomRegistration.Atom(
                S + @"When this card is flipped face-up: Target 1 card your opponent controls;\s*" +
                @"return that target to the hand" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.ReturnToHand,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppAnyCardOnField,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    RequiresThisFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),
            // Royal Keeper (16509093). ygopro c16509093: SINGLE TRIGGER_F on EVENT_FLIP (Flip Summon, flipped by an attack —
            // after damage calculation — or by a card effect); +300 ATK and DEF, RESETS_STANDARD_DISABLE_PHASE_END.
            new AtomRegistration.Atom(
                S + @"When this card is flipped face-up, it gains (\d+) ATK and DEF until the end of the turn\.\s*" +
                @"\(If attacked, this effect resolves after damage calculation\.\)" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.GainAtkDefUntilEndOfTurn,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 300),
                    DefAmount = Parse(m, 1, 300),
                    RequiresThisFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),
            // Cobraman Sakuzy (75109441). ygopro c75109441: SINGLE TRIGGER_F on EVENT_FLIP; Duel.ConfirmCards on the
            // face-down cards in the opponent's LOCATION_SZONE (they stay Set).
            new AtomRegistration.Atom(
                S + @"When this card is flipped face-up, look at all Set Spell and Trap Cards on your opponent's side of " +
                @"the field and then return them to their original position" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.LookAtOpponentSetSpellTraps,
                    Side = EffectSide.Opponent,
                    RequiresThisFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),
            // Giant Axe Mummy (78266168). ygopro c78266168: SINGLE TRIGGER_F at EVENT_DAMAGE_STEP_END — this card was the
            // attack target, its battle position face-down Defense, and the attacker's ATK is lower than its DEF; destroy the
            // attacker if it still relates to the battle.
            new AtomRegistration.Atom(
                S + @"If the ATK of a monster on your opponent's side of the field that attacks this card in face-down " +
                @"Defense Position is lower than the DEF of this monster, the attacking monster is destroyed" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.DestroyAttackerWithLowerAtkThanThisDefWhenAttackedFaceDown,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.AttackingMonster,
                    MakesChainLink = true
                }),
            // A Man with Wdjat (51351302), its whole text. ygopro c51351302: SINGLE TRIGGER_F on EVENT_SUMMON_SUCCESS (a
            // Normal Summon only), and FIELD TRIGGER_F in its controller's Standby Phase from the Monster Zone (count limit
            // 1); EFFECT_FLAG_CARD_TARGET: 1 face-down card the opponent controls (monster or Spell/Trap); ConfirmCards if
            // it is still face-down.
            AtomRegistration.Atom.Sequence(
                S + @"When you Normal Summon this card, and during each of your Standby Phases, select 1 Set card on your " +
                @"opponent's side of the field\.\s*Pick it up and look at it, then return it to its original position" + E,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ThisCardSummoned,
                        Action = EffectActionKind.LookAtTargetSetCard,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.OppAnyCardOnField,
                        RequiresTargetFaceDown = true,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresThisNormalSummoned = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.LookAtTargetSetCard,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.OppAnyCardOnField,
                        RequiresTargetFaceDown = true,
                        IsPsctTarget = true,
                        MakesChainLink = true
                    }
                }),
            // Trap Dustshoot (64697231), its whole text. ygopro c64697231: Normal Trap, free chain; condition: the opponent
            // holds 4+ cards; at resolution ConfirmCards their hand, select 1 monster in it, SendtoDeck SEQ_DECKSHUFFLE.
            new AtomRegistration.Atom(
                S + @"Activate only if your opponent has (\d+) or more cards in their hand\.\s*Look at your opponent's " +
                @"hand, select 1 Monster Card in it, and return that card to its owner's Deck" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LookAtOpponentHandReturnChosenMonsterToDeck,
                    Side = EffectSide.Opponent,
                    RequiresOpponentHandAtLeast = Parse(m, 1, 4),
                    MakesChainLink = true
                }),
        };
    }
}
