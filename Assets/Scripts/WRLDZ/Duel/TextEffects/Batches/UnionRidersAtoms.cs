namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "UnionRiders" (v175): the Union riders the v167 Union shell left unparsed, a Union with no named host,
    /// and Union Rider.
    /// <list type="bullet">
    /// <item>Kiryu (c84814897): the +900 ATK/DEF half of its rider is read by the shell (RxUnionBonusIncrease); the
    /// sentence's tail is a Union-state ignition — Tribute this card (cost), the equipped monster can attack directly
    /// this turn.</item>
    /// <item>Second Goblin (c19086954): once per turn, a Union-state ignition that changes the equipped monster's battle
    /// position (face-up Attack ⇄ face-up Defense).</item>
    /// <item>Burning Beast / Freezing Beast (c59364406 / c85359414): each time the equipped monster inflicts battle damage
    /// to the opponent, target and destroy 1 face-up Spell/Trap on the field / 1 face-down card in a Spell &amp; Trap Zone
    /// (mandatory).</item>
    /// <item>Metallizing Parasite - Lunatite (c7369217): the Union procedure with no named host (any face-up monster you
    /// control), and the equipped monster is unaffected by the opponent's Spell effects.</item>
    /// <item>Union Rider (c11743119): take control of 1 face-up Union Monster the opponent controls and equip it to this
    /// card; that Union is not in its Union state, so it cannot unequip itself.</item>
    /// </list>
    /// Runtime: <see cref="UnionRidersRuntime"/>.
    /// </summary>
    public static class UnionRidersAtoms
    {
        const string SentenceStart = @"(?<=^|\.\s|\)\s)";
        const string SentenceEnd = @"(?:\.|$)(?=\s|$)";
        const string WhileUnion = @"While equipped to a monster by this card's effect, ";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Kiryu (c84814897 e1/e2/e3): the ATK/DEF increase is set on the Union equip clause by the shell's
            // RxUnionBonusIncrease; this whole sentence adds the Tribute ignition (cost: Release this card; the equipped
            // monster gains EFFECT_DIRECT_ATTACK until the End Phase).
            new AtomRegistration.Atom(
                SentenceStart + WhileUnion + @"increase the ATK/DEF of the equipped monster by \d+ points, and you can " +
                @"Tribute this card to allow the equipped monster to attack your opponent's Life Points directly this turn" +
                SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.UnionStateIgnition,
                    Action = EffectActionKind.TributeThisSoEquippedMonsterAttacksDirectly,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Second Goblin (c19086954 e1): ignition from the S/T Zone, SetCountLimit(1), Union state;
            // Duel.ChangePosition(equip target, POS_FACEUP_DEFENSE, 0, POS_FACEUP_ATTACK, 0).
            new AtomRegistration.Atom(
                SentenceStart + WhileUnion + @"you can change the equipped monster's battle position once per turn" +
                SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.UnionStateIgnition,
                    Action = EffectActionKind.ChangeEquippedMonsterBattlePosition,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Burning Beast (c59364406) / Freezing Beast (c85359414): TRIGGER_F, CARD_TARGET, EVENT_BATTLE_DAMAGE to the
            // opponent by the equipped monster. Burning: a face-up Spell/Trap on the field (LOCATION_ONFIELD, either side);
            // Freezing: a face-down card in a Spell & Trap Zone (LOCATION_SZONE, either side).
            new AtomRegistration.Atom(
                SentenceStart + WhileUnion + @"each time the equipped monster inflicts Battle Damage to your opponent, " +
                @"destroy 1 (face-up|face-down) Spell or Trap Card on the field" + SentenceEnd,
                m =>
                {
                    var faceUp = m.Groups[1].Value.StartsWith("face-up", System.StringComparison.OrdinalIgnoreCase);
                    return new EffectClause
                    {
                        Timing = EffectTiming.EquippedMonsterInflictsBattleDamage,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Both,
                        Zone = faceUp ? EffectZoneFilter.FieldSpellTraps : EffectZoneFilter.FaceDownSpellTraps,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = faceUp,
                        RequiresTargetFaceDown = !faceUp,
                        Amount = 1,
                        MakesChainLink = true
                    };
                }),

            // Metallizing Parasite - Lunatite (c7369217): aux.AddUnionProcedure(c,nil,true) — the classic Union procedure
            // with no named host. Same two clauses as AdvancedEffectTemplates.AddUnionProcedure; the equip may go to any
            // face-up monster its controller controls (not itself; the 1-Union-per-monster rule still applies).
            AtomRegistration.Atom.Sequence(
                SentenceStart + @"Once per turn, during your Main Phase, if you control this (?:monster|card) on the field, " +
                @"you can equip it to a face-up monster on your side of the field as an Equip Spell Card, OR unequip the " +
                @"Union equipment and Special Summon this card in face-up Attack Position" + SentenceEnd,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        Zone = EffectZoneFilter.ControllerMonsters,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        UnionEquipsAnyFaceUpMonster = true,
                        OncePerTurn = true,
                        IsOptional = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.UnequipThisSpecialSummon,
                        OncePerTurn = true,
                        IsOptional = true,
                        MakesChainLink = true
                    }
                }),

            // Metallizing Parasite - Lunatite (c7369217 e1): EFFECT_IMMUNE_EFFECT on the equipped monster while in the
            // Union state; efilter: te:GetOwnerPlayer() ~= this card's controller and te:IsSpellEffect().
            new AtomRegistration.Atom(
                SentenceStart + WhileUnion + @"that monster will be unaffected by the effects of Spell Cards controlled by " +
                @"your opponent" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.EquippedUnaffectedByOpponentSpellEffects,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Union Rider (c11743119): ignition, SetCountLimit(1), CARD_TARGET; eqcon: no Union equipped by this effect
            // (max. 1); target 1 Union Monster the opponent controls; the equip (EquipByEffectAndLimitRegister) never sets
            // the Union state, so that Union cannot use its own effect to become a monster again (the second sentence).
            new AtomRegistration.Atom(
                SentenceStart + @"You can take control of up to 1 Union Monster that is currently a monster on your " +
                @"opponent's side of the field, and equip it to this card\.\s*The Union Monster that is equipped to this " +
                @"card cannot be changed back to a monster using its own effect" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true,
                    ActivationCondition = UnionRidersRuntime.NoUnionEquippedByThisEffect
                }),

            // Combination Attack (c8964854): Quick-Play, CARD_TARGET, condition Duel.IsBattlePhase() (no Damage Step flag);
            // target 1 monster on either field that declared an attack this turn and has a Union in its Union state that
            // can be Special Summoned; those Unions are Special Summoned (POS_FACEUP, to their controller's field), then
            // the target gets EFFECT_EXTRA_ATTACK equal to its attacks declared this turn.
            new AtomRegistration.Atom(
                SentenceStart + @"During a Battle Phase, you can use this effect on 1 monster that has attacked this turn " +
                @"and is equipped with a Union Monster\.\s*Change the Union Monster back to a monster in face-up Attack or " +
                @"Defense Position\.\s*Also, the monster that was equipped with the Union Monster may attack again this " +
                @"turn" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonTargetsUnionsThenItAttacksAgain,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    RequiresBattlePhase = true,
                    MakesChainLink = true
                }),
        };
    }
}
