namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleAndPositionRecords": battle triggers (destroyed / destroys / battle confirm / battle damage),
    /// battle-partner locks and battle-position locks printed in Ancient Sanctuary wordings. Every atom is a card's whole
    /// text, anchored at both ends, so a longer or reworded card stays uncompiled. Runtime:
    /// <see cref="BattleAndPositionRecordsRuntime"/>.
    /// <list type="bullet">
    /// <item>Atomic Firefly (c87340664): SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED (REASON_BATTLE, previously face-up) — the
    /// reason player takes 1000 damage.</item>
    /// <item>Disc Fighter (c19612721): SINGLE TRIGGER_F EVENT_BATTLE_CONFIRM — attacker, the attack target is in Defense
    /// Position with 2000 or more DEF: destroy it (no damage calculation follows).</item>
    /// <item>Ghost Knight of Jackal (c13386503): SINGLE TRIGGER_O EVENT_BATTLE_DESTROYING, aux.bdogcon — target the destroyed
    /// monster in the GY, Special Summon it to your field in face-up Defense Position.</item>
    /// <item>Legacy Hunter (c87010442): SINGLE TRIGGER_F EVENT_BATTLE_DESTROYING — the battle target's battle position was
    /// face-down Defense: the opponent returns 1 random hand card to the Deck (SEQ_DECKSHUFFLE).</item>
    /// <item>Vampire Lady (c26495087): SINGLE TRIGGER_F EVENT_BATTLE_DAMAGE to the opponent — Vampire Lord's clause
    /// (declare a card type; the opponent sends 1 card of that type from their Deck to the GY).</item>
    /// <item>The Unhappy Girl (c27618634): battle protection in face-up Attack Position; EVENT_BATTLED record of the battle
    /// target, which cannot attack or change its position while this card stays in face-up Attack Position.</item>
    /// <item>Judgment of the Desert (c4869446): Continuous Trap; monsters flipped face-up after its activation cannot change
    /// their battle positions by hand while it stays face-up.</item>
    /// <item>Curse of Anubis (c66742250): Normal Trap; face-up Attack Position Effect Monsters → Defense, original DEF 0 and
    /// position-locked until the end of the turn.</item>
    /// </list>
    /// </summary>
    public static class BattleAndPositionRecordsAtoms
    {
        const string Gy = @"(?:GY|Graveyard)";
        const string Apos = @"['’]";

        static int Int(System.Text.RegularExpressions.Match m, string group) =>
            int.TryParse(m.Groups[group].Value, out var n) ? n : 0;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Atomic Firefly (c87340664), its whole text. Lua: SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED, condition
            // IsReason(REASON_BATTLE) and IsPreviousPosition(POS_FACEUP) (attacking or attacked, a battle-flipped one is face-up
            // when destroyed); label = GetReasonPlayer(); Damage(that player, 1000). No GY needed (a banished Firefly still burns).
            new AtomRegistration.Atom(
                @"^\s*When this face-up card on the field attacks or is attacked and it is destroyed as a result of battle, " +
                @"the player who destroyed it takes (?<n>\d+) points of damage\.\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.InflictDamageToPlayerWhoDestroyedThis,
                    Side = EffectSide.Opponent,
                    Amount = Int(m, "n"),
                    RequiresThisDestroyedByBattle = true,
                    ImplicitTargetIsBattleDestroyer = true,
                    RequiresTargetChoice = false,
                    MakesChainLink = true
                }),

            // Disc Fighter (c19612721), its whole text. Lua: SINGLE TRIGGER_F EVENT_BATTLE_CONFIRM (after a face-down target is
            // flipped, before damage calculation); chk: GetAttacker()==c, the attack target IsDefensePos and
            // IsDefenseAbove(2000); op: still related to the battle and not in Attack Position → Destroy(REASON_EFFECT). The
            // target leaves the field, so the attack stops before damage calculation.
            new AtomRegistration.Atom(
                @"^\s*If this card attacks a Defense Position monster with DEF (?<n>\d+) or more, destroy the monster with " +
                @"this card" + Apos + @"s effect without applying damage calculation\.\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.BeforeDamageCalculation,
                    Action = EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation,
                    Side = EffectSide.Opponent,
                    Amount = Int(m, "n"),
                    MakesChainLink = true
                }),

            // Ghost Knight of Jackal (c13386503), its whole text. Lua: SINGLE TRIGGER_O EVENT_BATTLE_DESTROYING, aux.bdogcon
            // (this card still relates to the battle; the battle target is a monster in the GY); chk: a free Monster Zone and
            // bc:IsCanBeSpecialSummoned(…, POS_FACEUP_DEFENSE); SetTargetCard(bc); op: SpecialSummon(tc, tp, tp,
            // POS_FACEUP_DEFENSE) if it still relates to the effect.
            new AtomRegistration.Atom(
                @"^\s*You can Special Summon an opponent" + Apos + @"s monster that is destroyed and sent to the " + Gy +
                @" by this card as a result of battle to your side of the field in face-up Defense Position\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.SpecialSummonBattleDestroyedMonsterToYourFieldInDefense,
                    Side = EffectSide.Controller,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Legacy Hunter (c87010442), its whole text. Lua: SINGLE TRIGGER_F EVENT_BATTLE_DESTROYING; condition: the battle
            // target's GetBattlePosition()==POS_FACEDOWN_DEFENSE, this card relates to the battle and is face-up; op: no hand
            // card → nothing; else RandomSelect 1 and SendtoDeck(SEQ_DECKSHUFFLE). The printed "sends it to the Graveyard" is
            // kept (RedirectAndReplaceRuntime.NeedsGyButWasBanished: a destroyed monster banished instead does not trigger it).
            new AtomRegistration.Atom(
                @"^\s*When this card attacks a face-down Defense Position monster and destroys it and sends it to the " + Gy +
                @" as a result of battle, your opponent returns 1 random card from their hand to their Deck\.\s*" +
                @"Their Deck is then shuffled\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.OpponentReturnsRandomHandCardToDeckShuffle,
                    Side = EffectSide.Opponent,
                    Amount = 1,
                    MakesChainLink = true
                }),

            // Vampire Lady (c26495087), its whole text. Lua: SINGLE TRIGGER_F EVENT_BATTLE_DAMAGE, ep==1-tp; SelectOption
            // Monster / Spell / Trap; the opponent selects 1 card of that type in their Deck (IsAbleToGrave) and sends it to the
            // GY. Vampire Lord's clause (MfcLabCards3Atoms) in this wording.
            new AtomRegistration.Atom(
                @"^\s*When this card inflicts battle damage to your opponent:\s*Declare 1 card type \(Monster, Spell, or " +
                @"Trap\);\s*your opponent sends 1 card of that type from their Deck to the " + Gy + @"\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck,
                    Side = EffectSide.Opponent,
                    Amount = 1,
                    MakesChainLink = true
                }),

            // The Unhappy Girl (c27618634), its whole text. Lua: e1 SINGLE EFFECT_INDESTRUCTABLE_BATTLE while POS_FACEUP_ATTACK
            // ("Damage calculation is applied normally" is its reminder); e2 SINGLE CONTINUOUS EVENT_BATTLED records the battle
            // target while this card is in face-up Attack Position (flag reset on EVENT_CHANGE_POS / RESETS_STANDARD); e3/e4
            // FIELD EFFECT_CANNOT_ATTACK / EFFECT_CANNOT_CHANGE_POSITION, target range (0, LOCATION_MZONE), on the recorded
            // monsters still flagged (RESETS_STANDARD: they left the field or were turned face-down).
            AtomRegistration.Atom.Sequence(
                @"^\s*While this card is in face-up Attack Position on the field, this card is not destroyed as a result of " +
                @"battle\.\s*\(Damage calculation is applied normally\.\)\s*A monster that battled with this card cannot " +
                @"change its battle position, except with a card effect, or attack while this card is in face-up Attack " +
                @"Position on the field\.\s*$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition,
                        Side = EffectSide.Controller
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition,
                        Side = EffectSide.Opponent
                    }
                }),

            // Judgment of the Desert (c4869446), its whole text. Lua: e1 ACTIVATE (free chain, no operation); e2 FIELD
            // CONTINUOUS EVENT_CHANGE_POS in the S/T Zone: each face-up monster whose previous position was face-down becomes a
            // card target of this card; e3 FIELD EFFECT_CANNOT_CHANGE_POSITION on both Monster Zones for those targets. The
            // Activate clause starts the record when the activation resolves (the effects apply from then on).
            AtomRegistration.Atom.Sequence(
                @"^\s*The Battle Positions of face-up monsters that are flipped face-up after activation of this card cannot " +
                @"be changed, except with a card effect\.\s*$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition,
                        Side = EffectSide.Both,
                        StaysOnField = true
                    }
                }),

            // Curse of Anubis (c66742250), its whole text. Lua: ACTIVATE free chain; filter face-up Attack Position
            // TYPE_EFFECT monsters on both fields (1+ to activate); ChangePosition to face-up Defense, then on each of them
            // EFFECT_SET_BASE_DEFENSE 0 and EFFECT_CANNOT_CHANGE_POSITION (RESETS_STANDARD_PHASE_END).
            new AtomRegistration.Atom(
                @"^\s*Change all Effect Monsters on the field to Defense Position\.\s*Until the end of this turn, the " +
                @"original DEF of those monsters becomes 0, also their battle positions cannot be changed\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeFaceUpAttackEffectMonstersToDefenseOriginalDefZeroLock,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldMonsters,
                    ActivationCondition = BattleAndPositionRecordsRuntime.FaceUpAttackEffectMonsterOnField,
                    MakesChainLink = true
                }),
        };
    }
}
