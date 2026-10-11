namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "FlipMassAndOpponentPicks": FLIP and one-shot mass resolutions, an excavation, and picks made by the
    /// player who did NOT activate the effect (<see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(DuelEngine, DuelistState, CardInstance, CardPickRequest)"/>
    /// with that player as the chooser). Each atom consumes the card's whole text, so the generic sentence loop never
    /// re-parses a fragment. Runtime: <see cref="FlipMassAndOpponentPicksRuntime"/>.
    /// <list type="bullet">
    /// <item>The Spell Absorbing Life (c99517131): flip every face-down monster face-up (no FLIP effects), then gain
    /// 400 LP per face-up Effect Monster on both fields, counted after the flips.</item>
    /// <item>Dark Cat with White Tail (c8634636): FLIP, target 2 monsters the opponent controls and 1 you control;
    /// return them to their owners' hands.</item>
    /// <item>Huge Revolution (c65396880): your Main Phase, with the three named monsters face-up on your field; send the
    /// opponent's hand to the GY and destroy every card they control.</item>
    /// <item>Magical Merchant (c32362575): FLIP, excavate until a Spell/Trap, add it, send the rest to the GY.</item>
    /// <item>Pixie Knight (c35429292): sent to the GY by battle; the opponent targets 1 Spell in your GY and it goes on
    /// top of your Deck.</item>
    /// <item>Pineapple Blast (c90669991): answers your own Normal Summon while the opponent controls more monsters; the
    /// opponent chooses which of their monsters are destroyed so the counts match.</item>
    /// <item>Dimension Jar (c73414375): FLIP, the turn player and then the other player can each banish up to 3
    /// monsters from their opponent's GY.</item>
    /// </list>
    /// </summary>
    public static class FlipMassAndOpponentPicksAtoms
    {
        const string Start = @"^\s*";
        const string End = @"\.?\s*$";
        const string Apos = @"['’]";
        const string Q = "[\"“”]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // The Spell Absorbing Life (c99517131): Lua s.activate — ChangePosition(all face-down, face-up, noflip), then
            // Recover(tp, 400 × face-up Effect Monsters on both fields) counted after the flip. s.target needs a monster on
            // the field (AnyMonsterOnField).
            AtomRegistration.Atom.Sequence(
                Start + @"Flip all face-down Defense Position Monster Cards on the field face-up\.\s*" +
                @"Flip Effects are not activated at this time\.\s*" +
                @"Increase your Life Points by (\d+) points for each Effect Monster Card on the field" + End,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.FlipAllFaceDownDefenseNoFlipEffects,
                        SuppressFlipEffects = true,
                        ActivationCondition = "AnyMonsterOnField",
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.GainLifePoints,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 1, 400),
                        ScaleAmountByFaceUpEffectMonsters = true,
                        MakesChainLink = true
                    }
                }),

            // Dark Cat with White Tail (c8634636): Lua FLIP with EFFECT_FLAG_CARD_TARGET — 2 of the opponent's monsters and
            // 1 of yours (IsAbleToHand); without both groups it takes no targets and does nothing. Targets are chosen as
            // the FLIP effect activates (FlipMassAndOpponentPicksRuntime.ActivateFlipTargets).
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Return (\d+) Monster Cards? on your opponent" + Apos + @"s side of the field and (\d+) " +
                @"Monster Cards? on your side of the field to their owners" + Apos + @" hands" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ReturnOpponentAndOwnTargetsToOwnersHands,
                    Side = EffectSide.Both,
                    OpponentTargetCount = Parse(m, 1, 2),
                    ControllerTargetCount = Parse(m, 2, 1),
                    MakesChainLink = true
                }),

            // Huge Revolution (c65396880): Lua s.condition — the turn player's Main Phase with face-up "People Running
            // About", "Oppressed People" and "United Resistance" on their field; s.activate sends the opponent's hand to
            // the GY, then destroys every card they control.
            new AtomRegistration.Atom(
                Start + @"This card can only be activated during your Main Phase when " +
                Q + @"([^""“”]+)" + Q + @",\s*" + Q + @"([^""“”]+)" + Q + @",?\s*and\s*" + Q + @"([^""“”]+)" + Q +
                @" are face-up on your side of the field\.\s*" +
                @"Send all cards in your opponent" + Apos + @"s hand to the Graveyard and destroy all cards your " +
                @"opponent controls on the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SendOpponentHandToGyAndDestroyAllOpponentCards,
                    Side = EffectSide.Opponent,
                    ActivationCondition = FlipMassAndOpponentPicksRuntime.YourMainPhaseWithNamedFaceUp,
                    ConditionName = m.Groups[1].Value.Trim() + "|" + m.Groups[2].Value.Trim() + "|" +
                                    m.Groups[3].Value.Trim(),
                    MakesChainLink = true
                }),

            // Magical Merchant (c32362575): Lua FLIP — excavate down to the first Spell/Trap, add it, send the cards above
            // it to the GY; with no Spell/Trap in the Deck, reveal the Deck and shuffle it.
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Excavate cards from the top of your Deck until you excavate a Spell/Trap(?: Card)?,\s*" +
                @"then add that card to your hand,\s*also send the remaining cards to the (?:GY|Graveyard)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ExcavateUntilSpellTrapAddItSendRestToGy,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),

            // Pixie Knight (c35429292): Lua SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED, in the GY by battle; the OPPONENT
            // selects (targets) 1 Spell in its controller's GY, which goes on top of that player's Deck.
            new AtomRegistration.Atom(
                Start + @"When this card is sent to the Graveyard as a result of battle,\s*your opponent selects 1 Spell " +
                @"Card from your Graveyard,?\s*and places that card on the top of your Deck" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.OpponentPlacesSpellFromYourGyOnTopOfYourDeck,
                    Side = EffectSide.Controller,
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),

            // Pineapple Blast (c90669991): Lua ACTIVATE EVENT_SUMMON_SUCCESS with ep==tp (your own Normal Summon) while
            // the opponent controls more monsters; the opponent keeps as many monsters as you control and the rest are
            // destroyed.
            new AtomRegistration.Atom(
                Start + @"When you Normal Summon a monster,\s*if your opponent controls more monsters than you do:\s*" +
                @"Destroy your opponent" + Apos + @"s monsters so that your opponent controls the same number of " +
                @"monsters as you \(your opponent chooses which of their monsters are destroyed\)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.OpponentNormalOrFlipSummon,
                    Action = EffectActionKind.DestroyOpponentMonstersToMatchYourCountOpponentChooses,
                    Side = EffectSide.Opponent,
                    AnswersControllerSummon = true,
                    ActivationCondition = FlipMassAndOpponentPicksRuntime.YourNormalSummonOpponentControlsMore,
                    MakesChainLink = true
                }),

            // Dimension Jar (c73414375): FLIP, each player (turn player first) can banish up to 3 from their opponent's
            // GY. Printed text: "monsters" (the Lua filter takes any card; ARGON follows the print, see the notes).
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Both players can remove from play up to (\d+) monsters from their opponent" + Apos +
                @"s Graveyard" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.EachPlayerMayBanishUpToMonstersFromOpponentGy,
                    Side = EffectSide.Both,
                    Amount = Parse(m, 1, 3),
                    MakesChainLink = true
                }),
        };

        static int Parse(System.Text.RegularExpressions.Match m, int group, int fallback)
        {
            if (m == null || group >= m.Groups.Count) return fallback;
            var g = m.Groups[group];
            if (g == null || !g.Success || !int.TryParse(g.Value, out var v)) return fallback;
            return v;
        }
    }
}
