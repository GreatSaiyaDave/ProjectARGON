using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// CrvDestroyedTriggersAndFloats batch (CRV / EEN): destroyed and sent-to-GY triggers — Poison Draw Frog's face-up draw,
    /// T.A.D.P.O.L.E.'s any-number search, Hydrogeddon's battle-destroying float, Wroughtweiler's two GY targets and Giant
    /// Kozaky's self-destruction and burn. UFOroid is the shared MonsterTriggerTemplates RxBattleGyFilterSsDeck sentence
    /// (its comma form); Winged Kuriboh is MfcLabCards2Atoms (both prints). Every atom is anchored to whole sentences.
    /// Runtime: <see cref="CrvDestroyedTriggersAndFloatsRuntime"/>.
    /// </summary>
    public static class CrvDestroyedTriggersAndFloatsAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"(?:\.(?=\s|$)|\s*$)";
        const string Gy = @"(?:GY|Graveyard)";
        const string Q = @"[""“”]";
        const string Apos = @"['’]";

        static int Pos(Match m, int g) =>
            int.TryParse(m.Groups[g].Value, out var n) && n > 0 ? n : 1;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Poison Draw Frog (c56840658): SINGLE TRIGGER_O EVENT_TO_GRAVE (DAMAGE_STEP), condition IsPreviousLocation(ONFIELD)
            // and IsPreviousPosition(POS_FACEUP) — any send (destroyed, Tributed, a cost); Draw(tp, 1). The printed "unless it
            // was attacked while face-down and destroyed by battle" excludes the battle whose Damage Step flipped it.
            new AtomRegistration.Atom(
                Start + @"When this face-up card on the field is sent to the " + Gy + @" \(unless it was attacked while " +
                @"face-down and destroyed by battle\), you can draw (\d+) cards?" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Pos(m, 1),
                    RequiresLeftFieldFaceUp = true,
                    RequiresNotAttackedFaceDownAndDestroyedByBattle = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // — 60161788 Blizzed, Defender of the Ice Barrier: When this card is destroyed by battle and sent to the GY, draw 1 card.
            new AtomRegistration.Atom(
                Start + @"When this card is destroyed by battle and sent to the (?:GY|Graveyard), draw (\d+) card" +
                @"(?:s)?" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Pos(m, 1),
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),
            // — 12694768 Abaki: When this card is destroyed and sent to the Graveyard by battle, both players take 500 damage.
            new AtomRegistration.Atom(
                Start + @"When this card is destroyed (?:and sent to the (?:GY|Graveyard) )?by battle, both players take (\d+) damage" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.InflictDamageToBothPlayers,
                    Side = EffectSide.Both,
                    Amount = Pos(m, 1),
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),

            // T.A.D.P.O.L.E. (c10456559): SINGLE TRIGGER_O EVENT_BATTLE_DESTROYED, condition IsLocation(GRAVE), tp ==
            // GetPreviousControler() and REASON_BATTLE; SelectMatchingCard(IsCode(id), DECK, 1..3), SendtoHand.
            new AtomRegistration.Atom(
                Start + @"When this card you control is destroyed by battle and sent to your " + Gy + @", you can add any " +
                Q + @"([^""“”]+)" + Q + @"\(s\) from your Deck to your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.AddAnyNumberOfNamedFromDeckToHand,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value.Trim(),
                    FromDeck = true,
                    RequiresThisDestroyedByBattle = true,
                    RequiresSentToPreviousControllersGy = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Hydrogeddon (c22587018): SINGLE TRIGGER_O EVENT_BATTLE_DESTROYING, aux.bdogcon (still on the field, the
            // destroyed monster in the GY); GetFirstMatchingCard(IsCode(id), DECK), SpecialSummon POS_FACEUP.
            new AtomRegistration.Atom(
                Start + @"When this card destroys an opponent" + Apos + @"s monster by battle and sends it to the " + Gy +
                @": You can Special Summon 1 " + Q + @"([^""“”]+)" + Q + @" from your Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.SpecialSummonNamedFromDeckInChosenFaceUpPosition,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value.Trim(),
                    FromDeck = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Wroughtweiler (c6480253): SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED (CARD_TARGET), condition IsLocation(GRAVE);
            // SelectUnselectGroup of exactly 1 SET_ELEMENTAL_HERO card + 1 CARD_POLYMERIZATION in the GY (none: no target,
            // nothing happens); SendtoHand both targets, ConfirmCards.
            new AtomRegistration.Atom(
                Start + @"When this card is destroyed by battle and sent to the " + Gy + @": Target 1 " + Q +
                @"([^""“”]+)" + Q + @" card and 1 " + Q + @"([^""“”]+)" + Q + @" in your " + Gy +
                @"; add both those targets to your hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.AddTargetedSeriesCardAndNamedCardFromYourGyToHand,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value.Trim(),
                    NamedCardIsSeries = true,
                    AltNamedCard = m.Groups[2].Value.Trim(),
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),

            // Giant Kozaky (c58185394), sentence 1: SINGLE EFFECT_SELF_DESTROY in the Monster Zone while no face-up
            // "Kozaky" (IsCode 99171160) is on either field, not while being destroyed by battle
            // (CrvDestroyedTriggersAndFloatsRuntime.SweepSelfDestroy).
            new AtomRegistration.Atom(
                Start + @"If there is no face-up " + Q + @"([^""“”]+)" + Q + @" on the field, destroy this card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SelfDestroyUnlessNamedFaceUp,
                    Side = EffectSide.Controller,
                    RequiresFaceUpName = m.Groups[1].Value.Trim(),
                    MakesChainLink = false
                }),

            // Giant Kozaky, sentence 2: SINGLE TRIGGER_F EVENT_LEAVE_FIELD, REASON_DESTROY and IsPreviousPosition(FACEUP)
            // (wherever it went: GY, banished, Deck); Damage(GetPreviousControler(), 2500 = its printed ATK).
            new AtomRegistration.Atom(
                Start + @"If this face-up card is destroyed, inflict damage equal to the original ATK of this card to " +
                @"its current controller" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.PreviousControllerTakesThisOriginalAtkAsDamage,
                    Side = EffectSide.Controller,
                    RequiresDestroyed = true,
                    RequiresLeftFieldFaceUp = true,
                    MakesChainLink = true
                }),
        };
    }
}
