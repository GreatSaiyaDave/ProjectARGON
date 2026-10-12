using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Field Spell structural play + registered continuous application.
    /// Placement is Rulebook (Field Zone, Speed 1). Stat/name clauses apply only from
    /// official text programs or the hardcoded A Legendary Ocean script (passcode 295517).
    /// Yugipedia / Konami: Field Spells are not Traps and cannot be Set in Spell &amp; Trap Zones.
    /// </summary>
    public static class FieldSpellEffects
    {
        public const int ALegendaryOcean = 295517;
        public const int MaidenOfTheAqua = 17214465;
        public const string UmiName = "Umi";

        public static bool IsFieldSpell(CardDef def) => def != null && def.IsFieldSpell;

        /// <summary>Rule condition: this card's name is always treated as X (even in Deck/GY/hand).</summary>
        public static void ApplyRuleConditions(CardInstance card)
        {
            if (card?.Def == null) return;
            if (card.CardId == ALegendaryOcean)
            {
                card.TreatedAsName = UmiName;
                return;
            }

            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null) return;
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                if (c.Action == EffectActionKind.CannotBeTributedForSummon)
                    card.CannotBeTributedForSummon = true;
                if (c.Action == EffectActionKind.CannotBeDestroyedByBattle)
                    card.CannotBeDestroyedByBattle = true;
                if (c.Action == EffectActionKind.PiercingBattleDamage)
                    card.HasPiercing = true;
            }
            foreach (var c in prog.ClauseList)
            {
                if (c == null || c.Action != EffectActionKind.AlwaysTreatedAsName) continue;
                if (!string.IsNullOrEmpty(c.TreatedAsName))
                {
                    card.TreatedAsName = c.TreatedAsName;
                    return;
                }
            }
        }

        /// <summary>
        /// Recompute ATK/DEF/Level modifiers from face-up Field Spells AND
        /// face-up monster continuous auras (Star Boy, etc.).
        /// Called from <see cref="DuelEngine"/> Notify — does not Notify itself.
        /// </summary>
        public static void RefreshBoard(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            // World Suppression / Mystic Probe / Metal Detector: mark this turn's negated card families
            // before anything below reads IsNegated.
            TurnNegationTrapsRuntime.Refresh(engine);
            // Jinzo / Royal Decree / Imperial Order: IsNegated on face-up Spells/Traps (ContinuousNegation batch).
            // First, before any modifier is cleared: a negated Snatch Steal hands its monster back, and that
            // control change may refresh the board again.
            ContinuousNegationRuntime.Refresh(engine);
            // EquipModel batch: a control Equip negated by Armored Glass / The Emperor's Holiday gives its monster back
            // (and takes it again once the negation ends), before the auras below are computed.
            EquipModelRuntime.SyncNegatedControlEquips(engine);
            // Charmers (TakeControl batch): control bound to a source that left or turned face-down returns
            // first, so the auras below are computed for each monster's real controller. After the negation
            // refreshes above, so a negated Remove Brainwashing or control Equip is seen as negated.
            TextEffects.TakeControlRuntime.SweepBoundControl(engine);
            // Dimensionhole (LocksAndZones batch): a zone stays unusable only while its monster stays banished.
            TextEffects.LocksAndZonesRuntime.Refresh(engine);
            // NegationMonsters batch (v161): a Ha Des / Twin-Headed Wolf negation ends once its card left the GY.
            TextEffects.NegationMonstersRuntime.Refresh(engine);
            // Spiritual Energy Settle Machine (ContinuousTrapsAndSpiritSupport batch): a Machine that left the field
            // returns the face-up Spirits before the auras below are computed.
            TextEffects.ContinuousTrapsAndSpiritSupportRuntime.SweepLeftMachines(engine);
            ClearModifiers(engine.Player);
            ClearModifiers(engine.Opponent);
            // Cost Down (SimpleTargetUtility batch v173): ClearModifiers zeroed every LevelModifier, so
            // re-apply this turn's hand/field Level stamps before the auras below are computed.
            TextEffects.SimpleTargetUtilityRuntime.ApplyLevelStamps(engine);
            ApplyFaceUpField(engine, engine.Player.FieldSpellZone?.Occupant);
            ApplyFaceUpField(engine, engine.Opponent.FieldSpellZone?.Occupant);
            // Only Field Spells have written ATK/DEF since the clear — snapshot
            // their share for the AR aura before monster/equip auras stack on.
            RecordFieldDeltas(engine.Player);
            RecordFieldDeltas(engine.Opponent);
            ApplyFaceUpMonsterAuras(engine, engine.Player);
            ApplyFaceUpMonsterAuras(engine, engine.Opponent);
            ApplyCatalogSpellTraps(engine, engine.Player);
            ApplyCatalogSpellTraps(engine, engine.Opponent);
            // Castle of Dark Illusions / Pumpking growth and Mirror Wall halving (StandbyGrowthAndMaintenance, v142).
            TextEffects.StandbyGrowthAndMaintenanceRuntime.ApplyBoard(engine);
            // Fairy Box (BattleWindows batch): "ATK becomes 0 until the end of the Battle Phase".
            TextEffects.BattleWindowsRuntime.ApplyBoard(engine);
            // Winged Minion's lasting gain, Twin-Headed Behemoth's "ATK/DEF become" (SpecialSummonEffects, v161).
            TextEffects.SpecialSummonEffectsRuntime.ApplyBoard(engine);
            // DerivedAtkMonsters batch (v176): Great Maju Garzett / Maju Garzett — the Tribute-derived "ATK becomes".
            TextEffects.DerivedAtkMonstersRuntime.ApplyBoard(engine);
            // Spirit Ryu (BattleBoosts batch, v161): "+1000 ATK/DEF until the end of the Battle Phase".
            TextEffects.BattleBoostsRuntime.ApplyBoard(engine);
            // Cat's Ear Tribe (DamageStepRules batch): the battler's Damage Step original ATK ends with the Damage Step.
            TextEffects.DamageStepRulesRuntime.ApplyBoard(engine);
            DestroyNamedLeavesContinuous(engine, engine.Player);
            DestroyNamedLeavesContinuous(engine, engine.Opponent);
            // EquipAndSpellProtection batch: Heart of Clear Water's self-destruction; stale Magic Reflector counters.
            TextEffects.EquipAndSpellProtectionRuntime.Refresh(engine);
            // ContinuousLocksAndIgnitions batch (v163): Buster Rancher's equip limit (ATK 1000 or less except during damage
            // calculation) is re-checked; a broken limit destroys the Equip Card.
            TextEffects.ContinuousLocksAndIgnitionsRuntime.SweepEquipLimits(engine);
            TextEffects.TextEffectRuntime.SweepClassicStateChecks(engine);
            // Kotodama (RedirectAndReplace batch, v142): face-up monsters sharing a name are destroyed.
            TextEffects.RedirectAndReplaceRuntime.SweepSameNameDestruction(engine);
            // SummonTriggersAndTributes batch (v163): "You can only control 1 face-up X" — a duplicate is destroyed.
            TextEffects.SummonTriggersAndTributesRuntime.SweepUniqueOnField(engine);
            // SummonLocksAndStatRules batch: Amazoness Spellcaster's switch resets; Rivalry of Warlords' one-Type rule.
            TextEffects.SummonLocksAndStatRulesRuntime.Refresh(engine);
            // NegationMonsters batch (v161): Freed / Frontier Wiseman / Tyrant Dragon destroy a Continuous Spell (Trap)
            // that keeps targeting a monster they protect (ygopro EFFECT_SELF_DESTROY).
            TextEffects.NegationMonstersRuntime.SweepPersistentTargets(engine);
            // Thunder Nyan Nyan (BattleRules batch): destroyed while its controller controls a non-LIGHT monster.
            TextEffects.BattleRulesRuntime.SweepSelfDestroy(engine);
            // NamedSummonPairs batch: Exodia Necross is destroyed while a Forbidden One piece is missing from the GY.
            TextEffects.NamedSummonPairsRuntime.SweepSelfDestroy(engine);
            // SpellCounterSpenders batch: Pitch-Black Power Stone is destroyed once its last Spell Counter is removed.
            TextEffects.SpellCounterSpendersRuntime.SweepSelfDestroy(engine);
            // Skull Invitation: cards that reached a GY since the last checkpoint (StandbyGrowthAndMaintenance, v142).
            TextEffects.StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint(engine);
        }

        static void ClearModifiers(DuelistState who)
        {
            if (who == null) return;
            foreach (var m in who.MonstersOnField())
                ClearOne(m);
            if (who.Hand != null)
            {
                foreach (var c in who.Hand)
                    if (c != null) c.LevelModifier = 0;
            }
        }

        static void RecordFieldDeltas(DuelistState who)
        {
            if (who == null) return;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null) continue;
                m.FieldAtkDelta = m.AtkModifier;
                m.FieldDefDelta = m.DefModifier;
            }
        }

        static void ClearOne(CardInstance c)
        {
            if (c == null) return;
            c.AtkModifier = 0;
            c.DefModifier = 0;
            c.LevelModifier = 0;
            c.PositionLockedByEffect = false;
            // DerivedAtkMonsters batch (v176): a self-set ATK/DEF is recomputed below (ApplySelfScale / grants).
            c.SelfSetAtk = int.MinValue;
            c.SelfSetDef = int.MinValue;
        }

        static void ApplyFaceUpField(DuelEngine engine, CardInstance field)
        {
            if (engine == null || field?.Def == null || !field.FaceUp || field.IsNegated) return;
            // MonsterNegationAndControlLocks batch: Non-Spellcasting Area — a Field Spell's aura leaves shielded monsters as they were.
            using var nsa = MonsterNegationAndControlLocksRuntime.ShieldStats(engine, field);

            // Hardcoded ALO first so compiler + script never double-apply +200 / −1.
            if (field.CardId == ALegendaryOcean)
            {
                ApplyLegendaryOcean(engine);
                return;
            }

            if (!ApplyCompiledContinuous(engine, field))
                ApplyCatalogAuras(engine, field, "FZONE");
        }

        /// <returns>True if at least one ATK/DEF or Level continuous clause ran.</returns>
        static void ApplySpellCounterAtk(DuelEngine engine, CardInstance m)
        {
            if (m == null || m.Counters <= 0) return;
            var prog = CompiledEffectCache.GetOrCompile(m.Def);
            if (prog == null) return;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (c == null || c.Action != EffectActionKind.GainAtkPerSpellCounter) continue;
                m.AtkModifier += c.Amount * m.Counters;
            }
        }

        static void ApplyEquipStats(DuelEngine engine, CardInstance host)
        {
            if (host?.Equips == null || host.Equips.Count == 0) return;
            // Relinquished family: only the monster it equipped with its own effect sets its ATK/DEF (Ignis
            // s.adcon / s.eqfilter). With none, any other Equip Card on it (an Equip Spell, Kiseitai) is an ordinary
            // Equip Card below.
            var equipped = TextEffectRuntime.AbsorbedEquipOf(host);
            var prog = equipped != null ? CompiledEffectCache.GetOrCompile(host.Def) : null;
            if (prog != null)
            {
                foreach (var c in prog.ClauseList)
                {
                    if (c == null || c.Action != EffectActionKind.EquipTargetToThis) continue;
                    // A face-down monster equipped this way stays face-down, and this card's ATK/DEF become 0 (OCG
                    // ruling; Ignis s.atkval / s.defval: IsFacedown() → 0).
                    var shown = equipped.Def != null && equipped.FaceUp;
                    var eatk = shown ? Math.Max(0, equipped.Def.atk) : 0;
                    var edef = shown ? Math.Max(0, equipped.Def.def) : 0;
                    host.AtkModifier += eatk - (host.Def != null ? host.Def.atk : 0);
                    host.DefModifier += edef - (host.Def != null ? host.Def.def : 0);
                    return;
                }
            }

            foreach (var eq in host.Equips)
            {
                // BothSideAndMonsterEquip (v161): a monster Card equipped as an Equip Spell with only a granted effect
                // (Sword Hunter) gives that effect's ATK; its own text (a Union's bonus) does not apply.
                if (eq != null && eq.IsEquipSpellWithGrantedEffectOnly)
                {
                    host.AtkModifier += BothSideAndMonsterEquipRuntime.GrantedAtkOf(engine, eq, host);
                    continue;
                }
                // Soft residual: The Emperor's Holiday — Equip Card effects negated while face-up.
                if (ContinuousProtections.EquipCardEffectsAreNegated(engine, eq))
                    continue;
                // A negated Equip / equipped Trap (Royal Decree, Imperial Order): no stat change.
                if (eq != null && eq.IsNegated)
                    continue;
                // MonsterNegationAndControlLocks batch: Non-Spellcasting Area — an Equip Spell does not affect a shielded host.
                if (eq != null && MonsterNegationAndControlLocksRuntime.ShieldsFromSpell(engine, host, eq))
                    continue;
                // UnionRiders batch (v175): a Union not in its Union state (taken by Union Rider) gives no Union bonus.
                if (UnionRidersRuntime.UnionTextOff(eq))
                    continue;
                var ep = eq?.Def != null ? CompiledEffectCache.GetOrCompile(eq.Def) : null;
                if (ep == null) continue;
                // Germ Infection / Stim-Pack: ATK lost per Standby Phase counted on the Equip.
                host.AtkModifier -= TextEffectRuntime.EquipStandbyDecay(eq);
                // Megamorph: ATK becomes double / half its original by LP comparison.
                host.AtkModifier += TextEffectRuntime.LpComparisonAtkDelta(engine, eq, host);
                foreach (var c in ep.ClauseList)
                {
                    if (c != null && (c.EquipAtkBonus != 0 || c.EquipDefBonus != 0))
                    {
                        var atk = c.EquipAtkBonus;
                        var def = c.EquipDefBonus;
                        if (c.ChooseAtkOrDef)
                        {
                            // One stat, chosen when the equip was activated. Neither, until that choice exists.
                            var stat = PoisonMeteorCestusRuntime.ChosenStat(eq);
                            if (stat == "DEF") atk = 0;
                            else if (stat == "ATK") def = 0;
                            else { atk = 0; def = 0; }
                        }
                        else if (c.ScaleAmountByControllerMonsters || c.ScaleAmountByControllerSpellTraps)
                        {
                            var ctrl = engine.ControllerOf(host) ?? engine.ControllerOf(eq);
                            var n = 0;
                            if (c.ScaleAmountByControllerMonsters)
                            {
                                if (ctrl != null)
                                    foreach (var m in ctrl.MonstersOnField())
                                        if (m != null && m.FaceUp) n++;
                            }
                            else if (ctrl != null)
                            {
                                foreach (var st in ctrl.SpellTrapsOnField())
                                    if (st != null && st.FaceUp) n++;
                            }

                            atk *= n;
                            def *= n;
                        }

                        host.AtkModifier += atk;
                        host.DefModifier += def;
                    }
                }
            }
        }

        static bool ApplyCompiledContinuous(DuelEngine engine, CardInstance field)
        {
            var prog = CompiledEffectCache.GetOrCompile(field.Def);
            if (prog == null) return false;
            var any = false;
            foreach (var clause in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (clause == null) continue;
                if (clause.Action == EffectActionKind.ContinuousGainAtkDef)
                {
                    ApplyStatClause(engine, field, clause);
                    any = true;
                }
                else if (clause.Action == EffectActionKind.ContinuousReduceLevel)
                {
                    // EquipLocksAndLeaveField batch: Demotion — the Level change applies to the equipped monster only.
                    if (TextEffects.EquipLocksAndLeaveFieldRuntime.ApplyEquippedLevelChange(engine, field, clause))
                    {
                        any = true;
                        continue;
                    }
                    ApplyLevelMod(engine, clause.AttributeFilter, -Math.Abs(clause.Amount),
                        applyToHand: clause.ApplyToHand, applyToField: clause.ApplyToField || !clause.ApplyToHand);
                    any = true;
                }
                else if (clause.Action == EffectActionKind.ChangeBattlePosition &&
                         clause.ForceDefensePosition)
                {
                    foreach (var who in new[] { engine.Player, engine.Opponent })
                    {
                        if (who == null) continue;
                        foreach (var m in who.MonstersOnField())
                        {
                            if (!IsFaceUpMonster(m)) continue;
                            if (!MatchesRace(m, clause.RaceFilter)) continue;
                            // MonsterNegationAndControlLocks batch: Non-Spellcasting Area shields from a Spell's change.
                            if (MonsterNegationAndControlLocksRuntime.ShieldsFromSpell(engine, m, field)) continue;
                            if (clause.AmountIsLevel && clause.Amount > 0)
                            {
                                // Level Limit - Area B: "Level N or higher" (Xyz have Ranks, not Levels).
                                if (IsXyz(m) || m.Level < clause.Amount) continue;
                                engine.ApplyEffectPositionChange(m, BattlePosition.Defense, true);
                                // Continuous: it cannot be changed back to Attack while this stays face-up.
                                m.PositionLockedByEffect = true;
                                continue;
                            }
                            engine.ApplyEffectPositionChange(m, BattlePosition.Defense, true);
                        }
                    }
                    any = true;
                }
                else if (clause.Action == EffectActionKind.ContinuousForceDefenseLockPosition)
                {
                    ApplyPositionLock(engine, clause);
                    any = true;
                }
                else if (clause.Action == EffectActionKind.GainSelfAtkPerCount)
                {
                    ApplySelfScale(engine, field, clause);
                    any = true;
                }
                // ContinuousLocksAndIgnitions batch (v163): Arsenal Bug — "ATK and DEF become N" unless another face-up
                // monster of the Type is controlled.
                else if (ContinuousLocksAndIgnitionsRuntime.ApplySelfStatSet(engine, field, clause))
                    any = true;
                // MonsterNegationAndControlLocks batch: Final Attack Orders — every face-up monster in Attack Position, locked.
                else if (MonsterNegationAndControlLocksRuntime.ApplyContinuous(engine, field, clause))
                    any = true;
                // EndOfDamageStepRiders batch: Insect Princess — the opponent's monsters of a Type in face-up Attack Position.
                else if (EndOfDamageStepRidersRuntime.ApplyContinuous(engine, field, clause))
                    any = true;
            }

            return any;
        }

        /// <summary>
        /// Dragon Capture Jar: every face-up matching monster on the field is in Defense
        /// Position and cannot change its battle position while the lock is face-up.
        /// Continuous — monsters flipped or Summoned later are changed too.
        /// </summary>
        static void ApplyPositionLock(DuelEngine engine, EffectClause clause)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                {
                    if (!IsFaceUpMonster(m)) continue;
                    if (!MatchesAttribute(m, clause.AttributeFilter)) continue;
                    if (!MatchesRace(m, clause.RaceFilter)) continue;
                    m.Position = BattlePosition.Defense;
                    m.PositionLockedByEffect = true;
                }
            }
        }

        /// <summary>
        /// Battleguards / Shadow Ghoul / Muka Muka: this face-up monster gains Amount ATK
        /// (DefAmount DEF) per counted thing, recomputed on every board refresh.
        /// </summary>
        static void ApplySelfScale(DuelEngine engine, CardInstance self, EffectClause clause)
        {
            if (!IsFaceUpMonster(self)) return;
            var who = engine.ControllerOf(self);
            if (who == null) return;
            var n = 0;
            switch (clause.CountSource)
            {
                case "NamedYouControl":
                    foreach (var m in who.MonstersOnField())
                        if (m != null && m.FaceUp && m.IsNamed(clause.NamedCard)) n++;
                    foreach (var st in who.SpellTrapsOnField())
                        if (st != null && st.FaceUp && st.IsNamed(clause.NamedCard)) n++;
                    break;
                case "MonstersInYourGy":
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster) n++;
                    break;
                case "CardsInYourHand":
                    n = who.Hand?.Count ?? 0;
                    break;
                case "EquipsOnThis":
                    n = self.Equips?.Count(eq => eq?.Def != null && (eq.Def.IsEquipSpell || eq.Def.IsMonster)) ?? 0; // Equip Spells + Union monsters
                    break;
                case "OppControlsAnyMonster":
                    n = engine.OpponentOf(who)?.MonsterCount > 0 ? 1 : 0;
                    break;
                case TextEffects.DerivedAtkMonstersRuntime.YourBanishedCards:
                    // Gren Maju Da Eiza: every card in your banishment, face-down included (GetFieldGroupCount REMOVED).
                    n = who.Banished?.Count ?? 0;
                    break;
                case TextEffects.SelfStatMonstersRuntime.OppMonsterCount:
                    // Every monster the opponent controls, face-down included. Not the flat "any monster" count.
                    n = engine.OpponentOf(who)?.MonsterCount ?? 0;
                    break;
                case "OppRaceFaceUpAndGy":
                {
                    // Buster Blader: face-up matching monsters the opponent controls + their GY.
                    var opp = engine.OpponentOf(who);
                    if (opp == null) break;
                    foreach (var m in opp.MonstersOnField())
                        if (m != null && m.FaceUp && MatchesRace(m, clause.RaceFilter)) n++;
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && MatchesRace(g, clause.RaceFilter)) n++;
                    break;
                }
            }

            // DerivedAtkMonsters batch (v176): "This card's ATK (ATK/DEF) is / are each equal to … x N" (Chaos Necromancer,
            // Gren Maju Da Eiza) sets the value every other modifier adds to (ygopro EFFECT_SET_ATTACK / _DEFENSE),
            // a printed "?" included.
            if (clause.SelfStatIsSet)
            {
                self.SelfSetAtk = Math.Max(0, clause.Amount * n);
                if (clause.DefAmount != 0) self.SelfSetDef = Math.Max(0, clause.DefAmount * n);
                return;
            }

            self.AtkModifier += clause.Amount * n;
            self.DefModifier += clause.DefAmount * n;
        }

        /// <summary>
        /// Official text (cards_db / Yugipedia):
        /// (This card's name is always treated as "Umi".)
        /// All WATER monsters on the field gain 200 ATK/DEF.
        /// Reduce the Level of all WATER monsters in both players' hands and on the field by 1.
        /// YGOPro c295517.lua: ATK/DEF on MZONE, Level on HAND+MZONE, both sides.
        /// </summary>
        static void ApplyLegendaryOcean(DuelEngine engine)
        {
            ApplyStatMod(engine, "WATER", 200, 200);
            ApplyLevelMod(engine, "WATER", -1, applyToHand: true, applyToField: true);
        }

        static bool CompiledOwnsStats(CardInstance m)
        {
            if (m?.Def == null || !m.Def.IsMonster) return false;
            var prog = CompiledEffectCache.GetOrCompile(m.Def);
            return prog != null && prog.FullyCompiled;
        }

        static bool HasCompiledConditionalSelfStat(CardInstance m)
        {
            var prog = CompiledEffectCache.GetOrCompile(m.Def);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null &&
                                               (c.Action == EffectActionKind.GainAtkWhenAttackingMatching ||
                                                c.Action == EffectActionKind.GainSelfAtkPerCount));
        }

        static void ApplyFaceUpMonsterAuras(DuelEngine engine, DuelistState who)
        {
            if (who == null) return;
            foreach (var m in who.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp) continue;
                // MonsterNegationAndControlLocks batch: a negated monster (Skill Drain) loses its own continuous effects, not
                // what its Equip Cards grant it (an Equip Card's effect is the Equip Card's).
                if (m.IsNegated)
                {
                    if (!engine.IsSummonPending(m)) ApplyEquipStats(engine, m);
                    continue;
                }
                // A monster whose Summon is still in its summon-negation window is not on the field yet: its
                // continuous effects do not apply (SummonNegation batch, v141).
                if (engine.IsSummonPending(m)) continue;
                // A FullyCompiled monster owns its ATK/DEF. The Lua seed stays a lint and must not add a
                // second permanent aura on top of the printed effect. The Lua-extracted catalog also drops
                // conditions (Insect Soldiers of the Sky became a permanent +1000, Boar Soldier a permanent
                // -1000): official text wins for those (HasCompiledConditionalSelfStat is a subset of
                // CompiledOwnsStats; both are kept for the two lineages' call sites).
                if (!CompiledOwnsStats(m) && !HasCompiledConditionalSelfStat(m) && ApplyCatalogAuras(engine, m, "MZONE")) continue;
                ApplySpellCounterAtk(engine, m);
                ApplyEquipStats(engine, m);
                if (ApplyCompiledContinuous(engine, m)) continue;
                TryHardcodedAttributeBooster(engine, m);
            }
        }

        static void ApplyCatalogSpellTraps(DuelEngine engine, DuelistState who)
        {
            if (who == null) return;
            var field = who.FieldSpellZone?.Occupant;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (st == null || st == field || !st.FaceUp || st.IsNegated) continue;
                // Soft residual: The Emperor's Holiday — skip Equip Card effects only
                // (Field/Continuous Spell/Trap and monster effects stay live).
                if (ContinuousProtections.EquipCardEffectsAreNegated(engine, st))
                    continue;
                // MonsterNegationAndControlLocks batch: Non-Spellcasting Area — a Spell's aura leaves shielded monsters as they were.
                using var nsa = MonsterNegationAndControlLocksRuntime.ShieldStats(engine, st);
                // A FullyCompiled stat aura (Banner of Courage) owns its ATK/DEF; the Lua
                // catalog must not stack a second, unconditional copy on top of it.
                if (!CompiledOwnsSpellTrapStats(st))
                    ApplyCatalogAuras(engine, st, "SZONE");
                ApplyCompiledContinuous(engine, st);
            }
        }

        static bool CompiledOwnsSpellTrapStats(CardInstance st)
        {
            if (st?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(st.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == EffectActionKind.ContinuousGainAtkDef) return true;
            return false;
        }

        static bool ApplyCatalogAuras(DuelEngine engine, CardInstance source, string zone)
        {
            if (engine == null || source == null) return false;
            var any = false;
            var controller = engine.ControllerOf(source);
            foreach (var aura in YgoProContinuousCatalog.AurasFor(source.CardId))
            {
                if (aura == null) continue;
                if (!string.IsNullOrEmpty(zone) &&
                    !string.Equals(aura.sourceZone, zone, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(aura.sourceZone, "GRAVE", StringComparison.OrdinalIgnoreCase))
                    continue;
                any = true;
                ApplyOneCatalogAura(engine, source, controller, aura);
            }

            return any;
        }

        static void ApplyOneCatalogAura(DuelEngine engine, CardInstance source,
            DuelistState controller, YgoProContinuousCatalog.Aura aura)
        {
            if (aura.selfOnly)
            {
                if (!IsFaceUpMonster(source)) return;
                source.AtkModifier += aura.atk;
                source.DefModifier += aura.def;
                return;
            }

            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                if (aura.controllerOnly && who != controller) continue;
                if (aura.opponentOnly && who == controller) continue;
                foreach (var m in who.MonstersOnField())
                {
                    if (!IsFaceUpMonster(m)) continue;
                    if (!MatchesAttribute(m, aura.attribute)) continue;
                    if (!MatchesRace(m, aura.race)) continue;
                    m.AtkModifier += aura.atk;
                    m.DefModifier += aura.def;
                }
            }
        }

        /// <summary>
        /// YGOPro c8201910 / family: FACE-UP continuous ATK auras if the compiler missed the text.
        /// </summary>
        static void TryHardcodedAttributeBooster(DuelEngine engine, CardInstance source)
        {
            if (source == null) return;
            switch (source.CardId)
            {
                case MonsterEffects.StarBoy: // 8201910
                    ApplyStatMod(engine, "WATER", 500, 0);
                    ApplyStatMod(engine, "FIRE", -400, 0);
                    break;
                case 7489323: // Milus Radiant
                    ApplyStatMod(engine, "EARTH", 500, 0);
                    ApplyStatMod(engine, "WIND", -400, 0);
                    break;
                case 28470714: // Bladefly
                    ApplyStatMod(engine, "WIND", 500, 0);
                    ApplyStatMod(engine, "EARTH", -400, 0);
                    break;
                case 80741828: // Witch's Apprentice
                    ApplyStatMod(engine, "DARK", 500, 0);
                    ApplyStatMod(engine, "LIGHT", -400, 0);
                    break;
                case 68658728: // Little Chimera
                    ApplyStatMod(engine, "FIRE", 500, 0);
                    ApplyStatMod(engine, "WATER", -400, 0);
                    break;
                case 67629977: // Hoshiningen
                    ApplyStatMod(engine, "LIGHT", 500, 0);
                    ApplyStatMod(engine, "DARK", -400, 0);
                    break;
            }
        }

        static void ApplyStatClause(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (engine == null || clause == null) return;
            var controller = engine.ControllerOf(source);

            if (clause.ScaleThisAtkByEquippedEquipCount)
            {
                // Maha Vailo: this card only × Equip Cards equipped to it.
                // Unequipped → 0. Never flat Amount. Never ExtraAttacks.
                if (source != null && IsFaceUpMonster(source))
                {
                    var n = source.Equips != null ? source.Equips.Count : 0;
                    source.AtkModifier += clause.Amount * n;
                    source.DefModifier += clause.DefAmount * n;
                }

                return;
            }

            if (clause.ScaleThisAtkByMatchingCount)
            {
                var n = 0;
                foreach (var who in new[] { engine.Player, engine.Opponent })
                {
                    if (who == null) continue;
                    if (clause.Side == EffectSide.Controller && who != controller) continue;
                    if (clause.Side == EffectSide.Opponent && who == controller) continue;

                    // Muka Muka / Flash Assailant: every card in that player's hand.
                    if (clause.ScaleCountHandCards)
                    {
                        n += who.Hand != null ? who.Hand.Count : 0;
                        continue;
                    }

                    // Shadow Ghoul / Mudora: monsters in that player's GY, never the field.
                    if (clause.ScaleCountGraveyardOnly)
                    {
                        if (who.Graveyard == null) continue;
                        foreach (var g in who.Graveyard)
                        {
                            if (g?.Def == null || !g.Def.IsMonster) continue;
                            if (!MatchesAttribute(g, clause.AttributeFilter)) continue;
                            if (!MatchesRace(g, clause.RaceFilter)) continue;
                            n++;
                        }

                        continue;
                    }

                    foreach (var m in who.MonstersOnField())
                    {
                        if (!IsFaceUpMonster(m)) continue;
                        if (!MatchesAttribute(m, clause.AttributeFilter)) continue;
                        if (!MatchesRace(m, clause.RaceFilter)) continue;
                        if (!string.IsNullOrEmpty(clause.NamedCard) &&
                            !CardMatchesNamed(m, clause.NamedCard, clause.NamedCardIsSeries))
                            continue;
                        n++;
                    }

                    if (clause.ScaleCountIncludesGraveyard && who.Graveyard != null)
                    {
                        foreach (var m in who.Graveyard)
                        {
                            if (m?.Def == null || !m.Def.IsMonster) continue;
                            if (!MatchesAttribute(m, clause.AttributeFilter)) continue;
                            if (!MatchesRace(m, clause.RaceFilter)) continue;
                            if (!string.IsNullOrEmpty(clause.NamedCard) &&
                                !CardMatchesNamed(m, clause.NamedCard, clause.NamedCardIsSeries))
                                continue;
                            n++;
                        }
                    }
                }

                if (source != null && IsFaceUpMonster(source))
                {
                    source.AtkModifier += clause.Amount * n;
                    source.DefModifier += clause.DefAmount * n;
                }

                return;
            }

            // Banner of Courage: only during the controller's own Battle Phase. RefreshBoard runs
            // on every Notify, so the aura appears and disappears with the phase.
            if (clause.RequiresBattlePhase && engine.Phase != DuelPhase.Battle) return;
            if (clause.ControllerTurnOnly && engine.TurnPlayer != controller) return;
            // During the opponent's turn only (your opponent's / their Battle Phase).
            if (clause.OpponentTurnOnly && (controller == null || engine.TurnPlayer == controller)) return;

            // This card's own ATK/DEF, not every monster on its side.
            if (clause.AffectsThisCardOnly)
            {
                if (source != null && IsFaceUpMonster(source) && controller != null &&
                    controller.TryFindMonster(source, out _))
                {
                    source.AtkModifier += clause.Amount;
                    source.DefModifier += clause.DefAmount;
                }

                return;
            }

            var controllerOnly = clause.Side == EffectSide.Controller;
            var opponentOnly = clause.OpponentIsSubject;
            // SimpleSpellTraps batch: The A. Forces — Amount per face-up monster of the counted Types you control
            // (a live count, recomputed on every refresh). 1 for every other aura.
            var perCount = TextEffects.SimpleSpellTrapsRuntime.AuraMultiplier(engine, controller, clause);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                if (controllerOnly && who != controller) continue;
                if (opponentOnly && (controller == null || who == controller)) continue;
                foreach (var m in who.MonstersOnField())
                {
                    if (!IsFaceUpMonster(m)) continue;
                    if (!MatchesAttribute(m, clause.AttributeFilter)) continue;
                    if (!MatchesRace(m, clause.RaceFilter)) continue;
                    if (!string.IsNullOrEmpty(clause.NamedCard) &&
                        !CardMatchesNamed(m, clause.NamedCard, clause.NamedCardIsSeries))
                        continue;
                    if (clause.AffectsDefensePositionOnly && m.Position != BattlePosition.Defense) continue;
                    m.AtkModifier += clause.Amount * perCount;
                    m.DefModifier += clause.DefAmount * perCount;
                }
            }
        }

        static void ApplyStatMod(DuelEngine engine, string attribute, int atk, int def)
        {
            ApplyToBoth(engine, m =>
            {
                if (!IsFaceUpMonster(m) || !MatchesAttribute(m, attribute)) return;
                m.AtkModifier += atk;
                m.DefModifier += def;
            });
        }

        static void ApplyLevelMod(DuelEngine engine, string attribute, int delta,
            bool applyToHand, bool applyToField)
        {
            if (applyToField)
            {
                ApplyToBoth(engine, m =>
                {
                    if (!IsFaceUpMonster(m) || !MatchesAttribute(m, attribute)) return;
                    m.LevelModifier += delta;
                });
            }

            if (applyToHand)
            {
                foreach (var who in new[] { engine.Player, engine.Opponent })
                {
                    if (who?.Hand == null) continue;
                    foreach (var c in who.Hand)
                    {
                        if (c?.Def == null || !c.Def.IsMonster) continue;
                        if (!MatchesAttribute(c, attribute)) continue;
                        c.LevelModifier += delta;
                    }
                }
            }
        }

        static void ApplyToBoth(DuelEngine engine, Action<CardInstance> fn)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                    fn(m);
            }
        }

        static bool IsFaceUpMonster(CardInstance m) =>
            m?.Def != null && m.Def.IsMonster && m.FaceUp;

        static bool IsXyz(CardInstance m) =>
            m?.Def?.type != null && m.Def.type.IndexOf("XYZ", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool MatchesAttribute(CardInstance c, string attribute)
        {
            if (string.IsNullOrEmpty(attribute) || c?.Def == null) return true;
            // Live Attribute on the field (Scroll of Bewitchment, EquipFamily batch); printed elsewhere.
            var a = c.Attribute;
            return a != null && a.Equals(attribute, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Exact Type match; the filter may list several Types ("Fish,Aqua").</summary>
        static bool MatchesRace(CardInstance c, string race)
        {
            if (string.IsNullOrEmpty(race) || c?.Def == null) return true;
            return ClassicEraTemplates.RaceMatches(c.Race, race);
        }

        public static bool ControlsFaceUpNamed(DuelistState who, string name) =>
            ControlsFaceUpNamed(who, name, activatedOnly: false);

        /// <param name="activatedOnly">Skip a card whose own activation is still an unresolved Chain Link
        /// (<see cref="CardInstance.ActivationUnresolvedOnChain"/>): "while X is on the field" (ygopro
        /// IsEnvironment) needs X's activation to have resolved; "you control X" does not.</param>
        static bool ControlsFaceUpNamed(DuelistState who, string name, bool activatedOnly)
        {
            if (who == null || string.IsNullOrEmpty(name)) return false;
            foreach (var monster in who.MonstersOnField())
            {
                if (monster != null && monster.FaceUp && CardMatchesNamed(monster, name, series: false))
                    return true;
            }

            foreach (var st in who.SpellTrapsOnField())
                if (st != null && st.FaceUp && !(activatedOnly && st.ActivationUnresolvedOnChain) &&
                    CardMatchesNamed(st, name, series: false)) return true;

            var field = who.FieldSpellZone?.Occupant;
            if (field != null && field.FaceUp && !(activatedOnly && field.ActivationUnresolvedOnChain) &&
                CardMatchesNamed(field, name, series: false))
                return true;

            return false;
        }

        /// <summary>
        /// Falling Down: "unless you control an Archfiend card" — monsters, S/T, Field.
        /// Series match: printed name, treated-as, or archetype contains the quoted string.
        /// </summary>
        public static bool ControllerHasNamedCard(DuelistState who, string name, bool series)
        {
            if (who == null || string.IsNullOrEmpty(name)) return false;
            foreach (var m in who.MonstersOnField())
                if (m != null && m.FaceUp && CardMatchesNamed(m, name, series)) return true;
            foreach (var st in who.SpellTrapsOnField())
                if (st != null && st.FaceUp && CardMatchesNamed(st, name, series)) return true;
            var field = who.FieldSpellZone?.Occupant;
            if (field != null && field.FaceUp && CardMatchesNamed(field, name, series)) return true;
            return false;
        }

        static bool CardMatchesNamed(CardInstance c, string name, bool series)
        {
            if (c == null || string.IsNullOrEmpty(name)) return false;
            ApplyRuleConditions(c);
            if (c.IsNamed(name)) return true;
            if (!series) return false;
            if (c.Name != null &&
                c.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (c.RulesName != null &&
                c.RulesName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool NamedCardIsFaceUpOnField(DuelEngine engine, string name)
        {
            if (engine == null || string.IsNullOrEmpty(name)) return false;
            if (ControlsFaceUpNamed(engine.Player, name, activatedOnly: true) ||
                ControlsFaceUpNamed(engine.Opponent, name, activatedOnly: true))
                return true;
            // Maiden of the Aqua / EFFECT_CHANGE_ENVIRONMENT: the field is treated as "Umi".
            if (string.Equals(name, UmiName, StringComparison.OrdinalIgnoreCase) &&
                MonsterTreatsFieldAs(engine, UmiName))
                return true;
            return false;
        }

        public static bool UmiIsOnField(DuelEngine engine) =>
            NamedCardIsFaceUpOnField(engine, UmiName);

        /// <summary>
        /// An "Archfiend" monster (archetype match or always-treated-as-Archfiend text): the shared
        /// die-roll targeting protection's scope. Pandemonium's waiver and search read their compiled
        /// clauses (ArchfiendRidersRuntime).
        /// </summary>
        public static bool IsArchfiendMonster(CardDef def)
        {
            if (def == null || !def.IsMonster) return false;
            if (!string.IsNullOrEmpty(def.archetype) &&
                def.archetype.IndexOf("Archfiend", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return def.desc != null &&
                   def.desc.IndexOf("treated as an \"Archfiend\"",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Face-up Field Spell in either Field Zone whose activation has resolved (blocks Maiden of the
        /// Aqua; ygopro IsEnvironment only counts a Field Spell with STATUS_EFFECT_ENABLED).
        /// </summary>
        public static bool FaceUpFieldSpellExists(DuelEngine engine)
        {
            if (engine == null) return false;
            var a = engine.Player?.FieldSpellZone?.Occupant;
            var b = engine.Opponent?.FieldSpellZone?.Occupant;
            return (a != null && a.FaceUp && !a.ActivationUnresolvedOnChain) ||
                   (b != null && b.FaceUp && !b.ActivationUnresolvedOnChain);
        }

        /// <summary>
        /// YGOPro EFFECT_CHANGE_ENVIRONMENT (Maiden of the Aqua): while face-up and no
        /// Field Spell is active, the field is treated as TreatedAsName (Umi). Does not
        /// apply Umi's ATK/DEF.
        /// </summary>
        public static bool MonsterTreatsFieldAs(DuelEngine engine, string name)
        {
            if (engine == null || string.IsNullOrEmpty(name)) return false;
            if (FaceUpFieldSpellExists(engine)) return false;
            return MonsterTreatsFieldAsSide(engine.Player, name) ||
                   MonsterTreatsFieldAsSide(engine.Opponent, name);
        }

        static bool MonsterTreatsFieldAsSide(DuelistState who, string name)
        {
            if (who == null) return false;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null || !m.FaceUp || m.Def == null) continue;
                if (m.CardId == MaidenOfTheAqua &&
                    string.Equals(name, UmiName, StringComparison.OrdinalIgnoreCase))
                    return true;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.FieldTreatedAsName) continue;
                    if (string.Equals(c.TreatedAsName, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Tornado Wall / Waboku-style: controller takes no battle damage.</summary>
        public static bool ControllerAvoidsBattleDamage(DuelEngine engine, DuelistState who) =>
            ControllerAvoidsBattleDamage(engine, who, null, null);

        /// <summary>
        /// Same, for a specific battle: Spirit Barrier needs a monster on the field, and a
        /// monster-borne clause (Amazoness Fighter) only covers battles that monster is in.
        /// </summary>
        public static bool ControllerAvoidsBattleDamage(DuelEngine engine, DuelistState who,
            CardInstance attacker, CardInstance defender)
        {
            if (engine == null || who == null) return false;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp || st.IsNegated || st.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.PreventControllerBattleDamage)
                        continue;
                    if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                        !NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                        continue;
                    if (c.RequiresControllerHasMonster && who.MonsterCount <= 0)
                        continue;
                    return true;
                }
            }

            foreach (var m in who.MonstersOnField())
            {
                if (m == null || !m.FaceUp || m.IsNegated || m.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.PreventControllerBattleDamage ||
                        !c.OnlyBattlesInvolvingThis)
                        continue;
                    if (m == attacker || m == defender) return true;
                }
            }

            return false;
        }

        static void DestroyNamedLeavesContinuous(DuelEngine engine, DuelistState who)
        {
            if (who == null) return;
            var doomed = new System.Collections.Generic.List<CardInstance>();
            foreach (var st in who.SpellTrapsOnField())
            {
                // "Destroy this card when X leaves the field" is this card's own effect: not while negated.
                if (st == null || !st.FaceUp || st.IsNegated || st.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.SelfDestroyUnlessNamedFaceUp)
                        continue;
                    if (string.IsNullOrEmpty(c.RequiresFaceUpName)) continue;
                    var ok = c.RequiresControllerNamedCard
                        ? ControllerHasNamedCard(who, c.RequiresFaceUpName, c.NamedCardIsSeries)
                        : NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName);
                    if (ok) continue;
                    doomed.Add(st);
                    break;
                }
            }

            foreach (var st in doomed)
            {
                // EquipAndSpellProtection batch: a Magic Reflector counter is removed instead (EFFECT_SELF_DESTROY).
                if (TextEffects.EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, st)) continue;
                engine.Log($"{st.Name} is destroyed ({st.Def?.name} required a named card that left).");
                engine.SendCardToGrave(who, st);
            }
        }
    }
}
