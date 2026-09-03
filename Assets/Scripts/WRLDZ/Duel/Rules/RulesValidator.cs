using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Pre-action validation for disk snap / UI. Engine is sole authority;
    /// AR holograms must not update until validation + commit succeed.
    /// </summary>
    public static class RulesValidator
    {
        public struct ActionVerdict
        {
            public bool Legal;
            public string Reason;
            public string OfficialText;
            public SummonKind SummonKind;
            public SpellSpeed Speed;
        }

        public static ActionVerdict ValidateDiskPlacement(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            RulesZoneKind zoneKind,
            int zoneIndex,
            bool preferSet)
        {
            var v = new ActionVerdict { Legal = false };
            if (engine == null || who == null || card?.Def == null)
            {
                v.Reason = "Invalid context.";
                return v;
            }

            if (engine.GameOver)
            {
                v.Reason = "Duel is over.";
                return v;
            }

            if (engine.Chain.IsResolving)
            {
                v.Reason = "Cannot place cards while a chain is resolving.";
                return v;
            }

            v.OfficialText = OfficialCardAuthority.OfficialText(card);

            switch (zoneKind)
            {
                case RulesZoneKind.Monster:
                {
                    if (card.Def.IsEquipSpell)
                    {
                        if (preferSet)
                        {
                            v.Reason = "Equip Spells are activated, not Set on a monster.";
                            return v;
                        }

                        if (!OfficialEffectRegistry.CanActivateOfficial(engine, who, card, fromHand: true,
                                out var eqWhy))
                        {
                            v.Reason = string.IsNullOrEmpty(eqWhy)
                                ? "Cannot activate this Equip Spell now."
                                : eqWhy;
                            return v;
                        }

                        if (WRLDZ.Duel.TextEffects.LegacyTextTemplates.EquipTargetsOpponent(card.Def))
                        {
                            v.Legal = true;
                            v.Reason = "OK — Equip (target an opponent's monster)";
                            v.Speed = SpellSpeed.Speed1;
                            return v;
                        }

                        if (zoneIndex < 0 || zoneIndex >= who.MonsterZones.Length)
                        {
                            v.Reason = "Invalid Monster Zone.";
                            return v;
                        }

                        var host = who.MonsterZones[zoneIndex].Occupant;
                        if (host == null || !host.FaceUp || host.Def == null || !host.Def.IsMonster)
                        {
                            v.Reason = "Equip Spells target a face-up monster you control.";
                            return v;
                        }

                        v.Legal = true;
                        v.Reason = "OK — Equip";
                        v.Speed = SpellSpeed.Speed1;
                        return v;
                    }

                    var check = SummonProcedures.CheckNormalOrTribute(engine, who, card, preferSet);
                    v.Legal = check.Legal;
                    v.Reason = check.Reason;
                    v.SummonKind = check.Kind;
                    if (v.Legal && zoneIndex >= 0)
                    {
                        if (zoneIndex >= who.MonsterZones.Length)
                        {
                            v.Legal = false;
                            v.Reason = "Invalid Monster Zone.";
                            return v;
                        }

                        var occ = who.MonsterZones[zoneIndex].Occupant;
                        if (occ != null && occ != card)
                        {
                            // Tribute Summon/Set may occupy a zone whose monster is tributed.
                            // Face-down Sets are legal tributes.
                            if (check.TributesNeeded <= 0 ||
                                !TcgRules.CanBeTributedForSummon(who, occ))
                            {
                                v.Legal = false;
                                v.Reason = "That Monster Zone is occupied.";
                                return v;
                            }

                            v.Reason = "OK — tribute this monster";
                        }
                    }

                    return v;
                }
                case RulesZoneKind.SpellTrap:
                {
                    if (!(card.Def.IsSpell || card.Def.IsTrap))
                    {
                        v.Reason = "Only Spells/Traps in Spell & Trap Zones.";
                        return v;
                    }

                    if (card.Def.IsFieldSpell)
                    {
                        v.Reason = "Field Spells must go to the Field Spell Zone.";
                        return v;
                    }

                    if (zoneIndex >= 0)
                    {
                        if (zoneIndex >= who.SpellTrapZones.Length)
                        {
                            v.Reason = "Invalid Spell & Trap Zone.";
                            return v;
                        }

                        if (!who.SpellTrapZones[zoneIndex].IsEmpty)
                        {
                            v.Reason = "That Spell & Trap Zone is occupied.";
                            return v;
                        }
                    }

                    if (preferSet || card.Def.IsTrap)
                    {
                        if (!engine.CanSetSpellTrap(who, card))
                        {
                            v.Reason = "Cannot Set Spell/Trap now (Main Phase, free zone, turn player).";
                            return v;
                        }

                        v.Legal = true;
                        v.Reason = "OK — Set";
                        v.Speed = SpellSpeed.None;
                        return v;
                    }

                    if (!OfficialEffectRegistry.CanActivateOfficial(engine, who, card, fromHand: true,
                            out var ar))
                    {
                        v.Reason = ar;
                        return v;
                    }

                    v.Legal = true;
                    v.Reason = "OK — Activate";
                    v.Speed = OfficialEffectRegistry.SpeedOf(card.Def);
                    return v;
                }
                case RulesZoneKind.FieldSpell:
                {
                    if (!card.Def.IsFieldSpell)
                    {
                        v.Reason = "Only Field Spells.";
                        return v;
                    }

                    if (engine.TurnPlayer != who || !engine.InMainPhase)
                    {
                        v.Reason = "Field Spell only in your Main Phase.";
                        return v;
                    }

                    if (!who.Hand.Contains(card))
                    {
                        v.Reason = "Not in hand.";
                        return v;
                    }

                    v.Legal = true;
                    v.Reason = "OK — Field Zone";
                    return v;
                }
                case RulesZoneKind.PendulumLeft:
                case RulesZoneKind.PendulumRight:
                {
                    if (card.Def.type == null ||
                        card.Def.type.IndexOf("Pendulum", System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        v.Reason = "Only Pendulum Monsters.";
                        return v;
                    }

                    if (engine.TurnPlayer != who || !engine.InMainPhase)
                    {
                        v.Reason = "Pendulum Zone placement only in your Main Phase.";
                        return v;
                    }

                    var pIdx = zoneKind == RulesZoneKind.PendulumLeft ? 0 : 1;
                    if (who.PendulumZones == null || pIdx >= who.PendulumZones.Length ||
                        !who.PendulumZones[pIdx].IsEmpty)
                    {
                        v.Reason = "That Pendulum Zone is occupied.";
                        return v;
                    }

                    v.Legal = true;
                    v.Reason = "OK — Pendulum Scale";
                    return v;
                }
                default:
                    v.Reason = "Unknown zone.";
                    return v;
            }
        }

        public static ActionVerdict ValidateActivation(DuelEngine engine, DuelistState who,
            CardInstance card, bool fromHand)
        {
            var v = new ActionVerdict
            {
                OfficialText = OfficialCardAuthority.OfficialText(card)
            };
            v.Legal = OfficialEffectRegistry.CanActivateOfficial(engine, who, card, fromHand, out v.Reason);
            if (v.Legal)
                v.Speed = OfficialEffectRegistry.SpeedOf(card?.Def);
            return v;
        }

        public static ActionVerdict ValidateAttack(DuelEngine engine, DuelistState who,
            CardInstance attacker, CardInstance targetOrNull)
        {
            var v = new ActionVerdict();
            if (engine != null && engine.OpponentHasSwordsOfRevealingLight(who))
            {
                v.Reason = "Cannot declare an attack — opponent controls Swords of Revealing Light.";
                return v;
            }

            if (engine != null && engine.ContinuousCannotAttackBlocks(who, attacker))
            {
                v.Reason = "Cannot declare an attack — a face-up card forbids it.";
                return v;
            }

            if (!engine.CanAttack(who, attacker))
            {
                v.Reason = "Illegal attack declaration (Battle Phase, ATK position, not yet attacked, etc.).";
                return v;
            }

            var opp = engine.OpponentOf(who);
            if (targetOrNull == null)
            {
                if (HasFaceUpOrSetMonsters(opp) && !engine.GrantsDirectAttack(attacker) &&
                    !WRLDZ.Duel.TextEffects.ContinuousProtections.AllOpponentMonstersAllowDirect(engine, opp))
                {
                    v.Reason = "Cannot attack directly — opponent controls a monster.";
                    return v;
                }
            }
            else if (!opp.TryFindMonster(targetOrNull, out _))
            {
                v.Reason = "Attack target not on opponent's field.";
                return v;
            }
            else if (who.MustAttackDirectlyThisTurn)
            {
                v.Reason = "Attacks become direct attacks this turn — cannot attack a monster.";
                return v;
            }
            else if (WRLDZ.Duel.TextEffects.ContinuousProtections.CannotBeAttackTarget(engine, targetOrNull))
            {
                v.Reason = $"{targetOrNull.Name} cannot be targeted for attacks.";
                return v;
            }

            if (engine.BattleStep != BattleStep.BattleStep && engine.BattleStep != BattleStep.StartStep &&
                engine.Phase == DuelPhase.Battle && engine.BattleStep != BattleStep.None)
            {
                // Allow declaration only in Battle Step (after Start Step completes)
                if (engine.BattleStep == BattleStep.DamageStep || engine.BattleStep == BattleStep.EndStep)
                {
                    v.Reason = "Cannot declare an attack during Damage Step or End Step.";
                    return v;
                }
            }

            v.Legal = true;
            v.Reason = "OK";
            return v;
        }

        static bool HasFaceUpOrSetMonsters(DuelistState who)
        {
            if (who == null) return false;
            foreach (var z in who.MonsterZones)
                if (z?.Occupant != null) return true;
            return false;
        }
    }
}
