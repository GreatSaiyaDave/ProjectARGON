namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleWindows": attack-declaration responses and Damage Step events. Each atom is a whole
    /// sentence (or the card's whole text, anchored at both ends), so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="BattleWindowsRuntime"/>.
    /// <list type="bullet">
    /// <item>Fairy Box (c21598948), first two sentences: a mandatory trigger of the face-up Continuous Trap on every
    /// attack declaration of an opponent's monster (EVENT_ATTACK_ANNOUNCE, no per-turn limit); its controller calls a
    /// coin (Duel.CallCoin) and, if right, the attacker's ATK becomes 0 until the end of the Battle Phase
    /// (EFFECT_SET_ATTACK_FINAL). The third sentence (the 500 LP upkeep) compiles through the shared upkeep
    /// template.</item>
    /// <item>Jam Defender (c21558682): an optional trigger of the face-up Continuous Trap when an opponent's monster
    /// attacks a monster you control; it targets a face-up "Revival Jam" you control that the attacker could attack and
    /// switches the attack to it (Duel.ChangeAttackTarget).</item>
    /// <item>Destruction Punch (c5616412): a Normal Trap activated at the end of the Damage Step
    /// (EVENT_DAMAGE_STEP_END) when the opponent's attacker battled your Defense Position monster with a higher DEF;
    /// it destroys the attacker (not targeted).</item>
    /// <item>Michizure (c37580756): a Normal Trap activated when a monster is sent from the field to your GY
    /// (EVENT_TO_GRAVE, EFFECT_FLAG_DAMAGE_STEP); it targets 1 monster on the field and destroys it.</item>
    /// <item>Dreamsprite (c8687195): a mandatory trigger when this card is attacked by an opponent's monster
    /// (EVENT_BATTLE_CONFIRM); it targets another monster you control and the attacker battles it instead.</item>
    /// <item>Dark Spirit of the Silent (c93599951): a Normal Trap in the attack-declaration window during the
    /// opponent's turn; it targets the attacker and another face-up monster the opponent controls, negates the attack,
    /// and that monster battles the original attack target instead (Duel.CalculateDamage).</item>
    /// </list>
    /// </summary>
    public static class BattleWindowsAtoms
    {
        /// <summary>Sentence start: text start or after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: a full stop before whitespace or the end of the text.</summary>
        const string E = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Fairy Box: e2 (EFFECT_TYPE_FIELD+TRIGGER_F, EVENT_ATTACK_ANNOUNCE, range SZONE, condition: the attacker
            // is controlled by the opponent). A marker of the face-up card, fired by the runtime at each declaration.
            new AtomRegistration.Atom(
                S + @"When your opponent['’]s monster declares an attack: Toss a coin and call it\. " +
                @"If you call it right, the attacking monster['’]s ATK becomes 0 until the end of the Battle Phase" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Jam Defender: e2 (TRIGGER_O, EVENT_ATTACK_ANNOUNCE, EFFECT_FLAG_CARD_TARGET; condition: the opponent's
            // turn and the attack has a target). The named card is the only legal new target.
            new AtomRegistration.Atom(
                @"^\s*When an opponent['’]s monster declares an attack on a monster you control: You can target 1 " +
                @"""([^""]+)"" you control; switch the attack target to that target\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Destruction Punch: EFFECT_TYPE_ACTIVATE on EVENT_DAMAGE_STEP_END; condition checked at activation.
            new AtomRegistration.Atom(
                @"^\s*If the ATK of an attacking monster your opponent controls is lower than the DEF of the attacked " +
                @"Defense Position monster you control, destroy the attacking monster at the end of the Damage Step\. " +
                @"\(Damage calculation is applied normally\.\)\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyAttackerAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    ActivationCondition = BattleWindowsRuntime.AttackerAtkBelowYourDefenderDefAtEndOfDamageStep,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Michizure: EFFECT_TYPE_ACTIVATE on EVENT_TO_GRAVE (a monster you own, previously in a Monster Zone),
            // EFFECT_FLAG_CARD_TARGET + DAMAGE_STEP; target any monster on the field.
            new AtomRegistration.Atom(
                @"^\s*When a monster is sent from the field to your (?:GY|Graveyard), even during the Damage Step: " +
                @"Target 1 monster on the field; destroy it\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    ActivationCondition = BattleWindowsRuntime.MonsterSentFromFieldToYourGy,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Dreamsprite: SINGLE+TRIGGER_F on EVENT_BATTLE_CONFIRM, condition: this card is the attack target of an
            // opponent's monster; it targets another monster you control (face-down allowed).
            new AtomRegistration.Atom(
                @"^\s*When attacked by your opponent['’]s monster, select another 1 of your Monster Cards and designate " +
                @"it as the attack['’]s target, then calculate damage\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.BeforeDamageCalculation,
                    Action = EffectActionKind.BattleAnotherOwnMonsterInstead,
                    Side = EffectSide.Controller,
                    RequiresThisIsAttackTarget = true,
                    MakesChainLink = true
                }),
            // Dark Spirit of the Silent: EFFECT_TYPE_ACTIVATE during the opponent's turn while their monster attacks;
            // targets the attacker (SetTargetCard) and another face-up monster they control (the pick).
            new AtomRegistration.Atom(
                @"^\s*Activate only when an opponent['’]s monster declares an attack\. Negate that attack, and select " +
                @"another face-up monster your opponent controls\. That monster attacks instead\. \(If it is in " +
                @"face-up Defense Position, change it to Attack Position\.\)\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.AttackDeclared,
                    Action = EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };
    }
}
