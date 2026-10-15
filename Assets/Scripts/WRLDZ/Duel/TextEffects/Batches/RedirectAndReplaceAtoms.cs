namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RedirectAndReplace" (v142): target redirection, same-name destruction and GY replacement. Each
    /// atom is the card's whole text, anchored at both ends, so a longer or reworded card stays uncompiled.
    /// <list type="bullet">
    /// <item>Fairy's Hand Mirror (17653779), ygopro c17653779: EFFECT_TYPE_ACTIVATE on EVENT_CHAINING — the
    /// opponent's activation of a Spell Card (EFFECT_TYPE_ACTIVATE, IsSpellEffect) whose chain info holds exactly
    /// 1 target card, a monster in a Monster Zone; it targets another monster that the chained effect could target
    /// (Duel.CheckChainTarget) and, if that card is still related, Duel.ChangeTargetCard.</item>
    /// <item>Shift (59560625), ygopro c59560625: the same on EVENT_CHAINING for an opponent's Spell/Trap Card
    /// activation targeting exactly 1 monster you control (new target: another monster you control), plus
    /// EVENT_BE_BATTLE_TARGET — an opponent's monster attacks a monster you control; target another monster you
    /// control the attacker could attack (GetAttackableTarget) and Duel.ChangeAttackTarget, unless the attacker
    /// is unaffected by Shift.</item>
    /// <item>Kotodama (19406822), ygopro c19406822: EFFECT_TYPE_FIELD+CONTINUOUS EVENT_ADJUST — destroys every
    /// face-up monster not in its last-seen group that shares a code with another face-up monster (not during
    /// the Damage Step before damage calculation, nor during damage calculation).</item>
    /// <item>Banisher of the Light (61528025), ygopro c61528025: EFFECT_TYPE_FIELD EFFECT_TO_GRAVE_REDIRECT with
    /// SET_AVAILABLE + IGNORE_RANGE + IGNORE_IMMUNE, range MZONE, target range every location of both players, value
    /// LOCATION_REMOVED — while it is face-up in a Monster Zone, any card (face-down ones and the unaffected included)
    /// that would be sent to the GY is banished instead.</item>
    /// </list>
    /// Runtime: <see cref="RedirectAndReplaceRuntime"/>. Tailor of the Fickle is in <see cref="EquipModelAtoms"/>.
    /// </summary>
    public static class RedirectAndReplaceAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Fairy's Hand Mirror (17653779).
            new AtomRegistration.Atom(
                @"^\s*When your opponent activates a Spell Card that targets exactly 1 monster \(and no other cards\) " +
                @"on the field: Target another card that would be an appropriate target; that Spell now targets the " +
                @"new target\.?\s*$",
                _ => Redirect(RedirectAndReplaceRuntime.OpponentSpellTargetsOneMonster,
                    RedirectAndReplaceRuntime.AnswersSpell, EffectSide.Both)),
            // Shift (59560625).
            new AtomRegistration.Atom(
                @"^\s*When your opponent targets exactly 1 monster you control \(and no other cards\) for an attack, " +
                @"or with a Spell/Trap effect: Target another monster you control that would be an appropriate " +
                @"target; that attack/Spell/Trap now targets the new target\.?\s*$",
                _ => Redirect(RedirectAndReplaceRuntime.OpponentTargetsOneMonsterYouControl,
                    RedirectAndReplaceRuntime.AnswersSpellOrTrap, EffectSide.Controller)),
            // Astral Barrier (37053871). Continuous Trap: e2 is EFFECT_TYPE_FIELD TRIGGER_O on EVENT_ATTACK_ANNOUNCE
            // with s.atkcon (the attacker is the opponent's, the attack target is yours) and Duel.ChangeAttackTarget(nil)
            // — the attack becomes a direct attack with no replay. StaysOnField because it is a Continuous Trap that
            // answers any number of declared attacks.
            new AtomRegistration.Atom(
                @"^\s*If your opponent's monster attacks a monster you control: You can make the attack a " +
                @"direct attack instead\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.AttackDeclared,
                    Action = EffectActionKind.MakeDeclaredAttackDirect,
                    Side = EffectSide.Controller,
                    IsOptional = true,
                    ActivationCondition = RedirectAndReplaceRuntime.OpponentAttacksYourMonster,
                    CheckedAt = ConditionCheckedAt.Activation,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Kotodama (19406822): the reminder text in parentheses is part of the card's text.
            new AtomRegistration.Atom(
                @"^\s*If there are face-up monsters with the same name on the field, destroy those monsters\. " +
                @"\(If a monster is Summoned or flipped face-up that has the same name as a monster that is already " +
                @"face-up on the field, the new face-up monster is destroyed\. If they are Summoned or flipped " +
                @"face-up at the same time, they are all destroyed\.\)\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DestroyNewFaceUpMonstersSharingAName,
                    Side = EffectSide.Both,
                    MakesChainLink = false
                }),
            // Banisher of the Light (61528025).
            new AtomRegistration.Atom(
                @"^\s*Any card sent to the (?:Graveyard|GY) is banished instead\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.BanishCardsSentToGyInstead,
                    Side = EffectSide.Both,
                    MakesChainLink = false
                }),
        };

        /// <summary>
        /// Activate (Normal Trap, Spell Speed 2): legal only while the window named by
        /// <paramref name="condition"/> is open; targets 1 card from
        /// <see cref="EffectZoneFilter.AnotherAppropriateTarget"/> and resolves by making it the new target.
        /// </summary>
        static EffectClause Redirect(string condition, string answers, EffectSide side) => new EffectClause
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.RedirectToAnotherAppropriateTarget,
            Zone = EffectZoneFilter.AnotherAppropriateTarget,
            Side = side,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            ActivationCondition = condition,
            ConditionName = answers,
            CheckedAt = ConditionCheckedAt.Activation,
            MakesChainLink = true
        };
    }
}
