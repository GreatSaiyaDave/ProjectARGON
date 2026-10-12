using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellEventsAndHandSendTriggers" whole-card atoms, each anchored at both ends so a longer or reworded
    /// card stays uncompiled:
    /// <list type="bullet">
    /// <item>Elephant Statue of Blessing (c85166216) / Elephant Statue of Disaster (c12160911) / Regenerating Mummy
    /// (c70821187): SINGLE TRIGGER_F on EVENT_TO_GRAVE, previous location hand, rp~=tp, REASON_EFFECT — discarded, or sent /
    /// destroyed in the hand, by an effect of a card the opponent controls: gain 2000 LP / 2000 damage to the opponent /
    /// this card returns from the GY to the hand (<see cref="EffectTiming.SentFromHandToGyByOpponentEffect"/>).</item>
    /// <item>Curse of Darkness (c84970821): Continuous Trap; each time a Spell Card's activation resolves, its activator
    /// takes 1000 damage (<see cref="EffectActionKind.InflictDamageToSpellActivatorAfterResolve"/>).</item>
    /// <item>Tower of Babel (c94256039): Continuous Trap; 1 Spell Counter when each Spell Card's activation resolves; the
    /// 4th destroys it and deals 3000 damage to that Spell's activator
    /// (<see cref="EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator"/>).</item>
    /// <item>Spirit of the Pot of Greed (c4896788): the activator of "Pot of Greed" may draw 1 more card
    /// (<see cref="EffectActionKind.ActivatorOfNamedCardMayDrawWhileAttackPosition"/>).</item>
    /// <item>Fiend's Hand Mirror (c58607704): the Spell/Trap twin of Fairy's Hand Mirror
    /// (<see cref="EffectZoneFilter.AnotherAppropriateSpellTrapTarget"/>).</item>
    /// </list>
    /// Registered with one line in <see cref="AtomRegistration"/>'s BatchAtomSets.
    /// Runtime: <see cref="SpellEventsAndHandSendTriggersRuntime"/>.
    /// </summary>
    public static class SpellEventsAndHandSendTriggersAtoms
    {
        /// <summary>Either PSCT wording of "an effect of a card controlled by your opponent".</summary>
        const string ByOpponentEffect =
            @"by (?:an effect of a card controlled by your opponent|(?:an|your) opponent's card effect)";

        /// <summary>"When this card is sent from your hand to your GY by an opponent's card effect: " (not "discarded").</summary>
        const string SentFromHandByOpponent =
            @"^\s*(?:When|If) this card is sent from your hand to (?:your|the) (?:GY|Graveyard) " + ByOpponentEffect +
            @"(?:, |:\s*)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Elephant Statue of Blessing. Lua: Duel.Recover(tp, 2000).
            new AtomRegistration.Atom(
                SentFromHandByOpponent + @"(?:you )?gain (\d+) (?:LP|Life Points)\.?\s*$",
                m => HandSend(EffectActionKind.GainLifePoints, EffectSide.Controller, Parse(m, 1, 2000))),
            // Elephant Statue of Disaster. Lua: Duel.Damage(1-tp, 2000).
            new AtomRegistration.Atom(
                SentFromHandByOpponent +
                @"inflict (\d+) (?:points of )?damage to your opponent(?:'s Life Points)?\.?\s*$",
                m => HandSend(EffectActionKind.InflictDamageToOpponent, EffectSide.Opponent, Parse(m, 1, 2000))),
            // Regenerating Mummy. Lua: SendtoHand(this card) if it is still related (still in the GY).
            new AtomRegistration.Atom(
                SentFromHandByOpponent + @"return this card to (?:the|your) hand\.?\s*$",
                _ => HandSend(EffectActionKind.ReturnThisFromGyToHand, EffectSide.Controller, 0)),
            // Curse of Darkness. Not a Chain Link (Lua EFFECT_TYPE_CONTINUOUS on EVENT_CHAIN_SOLVED).
            new AtomRegistration.Atom(
                @"^\s*Each time a Spell Card is activated, immediately after it resolves, the player that activated it " +
                @"takes (\d+) damage\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageToSpellActivatorAfterResolve,
                    Side = EffectSide.Both,
                    Amount = Parse(m, 1, 1000),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Tower of Babel. The whole text is matched here; its first sentence's counter clause (PlaceSpellCounters with
            // PlaceCounterOnSpellActivate, like Royal Magical Library's) comes from the shared AdvancedEffectTemplates
            // RxPlaceOnSpell pass, which reads the raw text; this atom adds the 4th-counter trigger.
            new AtomRegistration.Atom(
                @"^\s*Each time a Spell Card is activated, place 1 Spell Counter on this card when that Spell Card " +
                @"resolves\. If the (\d+)(?:st|nd|rd|th) Spell Counter is placed on this card: Destroy this card, and if " +
                @"you do, the player who activated the Spell Card at that time takes (\d+) damage\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator,
                    Side = EffectSide.Both,
                    ConditionAmount = Parse(m, 1, 4),
                    Amount = Parse(m, 2, 3000),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Spirit of the Pot of Greed. The trigger is mandatory; the draw is the activator's choice.
            new AtomRegistration.Atom(
                @"^\s*While this card is in face-up Attack Position on the field, if a player activates ""([^""]+)"", " +
                @"they can draw (1|one) more card\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ActivatorOfNamedCardMayDrawWhileAttackPosition,
                    Side = EffectSide.Both,
                    NamedCard = m.Groups[1].Value,
                    Amount = 1,
                    MakesChainLink = false
                }),
            // Fiend's Hand Mirror. Normal Trap: answers the opponent's Spell Card activation whose only target is a
            // Spell/Trap Card on the field; targets another appropriate Spell/Trap (RedirectAndReplaceRuntime).
            new AtomRegistration.Atom(
                @"^\s*Switch the opponent's Spell Card effect that targets 1 Spell or Trap Card to another correct " +
                @"target\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.RedirectToAnotherAppropriateTarget,
                    Zone = EffectZoneFilter.AnotherAppropriateSpellTrapTarget,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    ActivationCondition = RedirectAndReplaceRuntime.OpponentSpellTargetsOneSpellTrap,
                    ConditionName = RedirectAndReplaceRuntime.AnswersSpell,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };

        /// <summary>A mandatory trigger of a card sent from its owner's hand to the GY by an opponent's card effect.</summary>
        static EffectClause HandSend(EffectActionKind action, EffectSide side, int amount) => new EffectClause
        {
            Timing = EffectTiming.SentFromHandToGyByOpponentEffect,
            Action = action,
            Side = side,
            Amount = amount,
            MakesChainLink = true
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
