using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Runtime for shared protection kinds: unaffected-by, cannot-be-attack-target,
    /// cannot-be-targeted-by-effects. Reads compiled programs — no cardId branches.
    /// </summary>
    public static class ContinuousProtections
    {
        public const int LordOfD = 17985575;

        public static bool CannotBeAttackTarget(DuelEngine engine, CardInstance monster)
        {
            if (monster == null) return false;
            // EquipLocksAndLeaveField batch: Raregold Armor — only its equipped monster can be selected as an attack
            // target (face-down monsters of that player included).
            if (EquipLocksAndLeaveFieldRuntime.OnlyEquippedHostMayBeAttacked(engine, monster)) return true;
            // A Feint Plan: neither player can attack face-down monsters this turn.
            // Face-down targets are otherwise never ContinuousCannotBeAttackTarget hosts.
            if (!monster.FaceUp)
            {
                var atk = engine?.TurnPlayer;
                return atk != null && atk.CannotAttackFaceDownMonstersThisTurn;
            }
            // EndOfDamageStepRiders batch: Prickle Fairy — the opponent cannot attack its controller's face-up monsters of a
            // Type (EFFECT_CANNOT_SELECT_BATTLE_TARGET of the source: this monster's own negation does not matter).
            if (EndOfDamageStepRidersRuntime.CannotBeAttackTarget(engine, monster)) return true;
            // Its own "cannot be attacked" needs its effects; another monster's field rule (Marauding Captain, Amazoness
            // Tiger: EFFECT_CANNOT_SELECT_BATTLE_TARGET of the source) does not care whether this one is negated.
            if (!monster.IsNegated)
                foreach (var c in ClausesOn(monster, EffectActionKind.CannotBeAttackTarget))
                {
                    if (c.ExceptThisCard) continue;
                    // AttackShieldsAndDamageZero batch: "while you control another face-up <Type> monster" (Solar Flare
                    // Dragon, Goblin King, Soul-Absorbing Bone Tower), a face-up Plant (Arcane Archer of the Forest) or a
                    // face-up Level 3 or lower Normal Monster (Emissary of the Oasis) — read live on each check.
                    if (ConditionMet(engine, c) &&
                        AttackShieldsAndDamageZeroRuntime.WhileConditionMet(engine, monster, c))
                        return true;
                }

            foreach (var src in FaceUpMonsters(engine))
            {
                if (src == null || src.IsNegated || src == monster) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.CannotBeAttackTarget))
                {
                    if (!c.ExceptThisCard) continue;
                    // "Except this one" protects the controller's matching
                    // monsters (Marauding Captain), never the other side.
                    if (!SameMonsterController(engine, src, monster)) continue;
                    if (!ConditionMet(engine, c)) continue;
                    if (!string.IsNullOrEmpty(c.RaceFilter))
                    {
                        if (monster.Race == null ||
                            !string.Equals(monster.Race, c.RaceFilter,
                                StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    // SummonLocksAndStatRules batch — Amazoness Tiger: face-up "Amazoness" monsters, except this one.
                    // ArchfiendRiders batch — Vilepawn Archfiend: "treated as" series members count (Toon Summoned Skull).
                    if (!string.IsNullOrEmpty(c.NamedCard) && c.NamedCardIsSeries &&
                        !SummonLocksAndStatRulesRuntime.IsSeriesMember(monster, c.NamedCard) &&
                        !TextEffectRuntime.CardMatchesSeries(monster.Def, c.NamedCard))
                        continue;
                    // ArchfiendRiders batch — Vilepawn Archfiend: except cards with that name (Lua: not IsCode(id)).
                    if (!string.IsNullOrEmpty(c.ExceptNamedCard) && monster.IsNamed(c.ExceptNamedCard))
                        continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when every monster the opponent controls cannot be attack-targeted
        /// and at least one of them allows a direct attack (Fisherman / Kay'est).
        /// </summary>
        public static bool AllOpponentMonstersAllowDirect(DuelEngine engine, DuelistState opp)
        {
            if (opp == null) return false;
            var any = false;
            var allows = false;
            foreach (var m in opp.MonstersOnField())
            {
                if (m == null) continue;
                any = true;
                if (!CannotBeAttackTarget(engine, m))
                    return false;
                if (AllowsDirectWhileProtected(engine, m))
                    allows = true;
            }

            return any && allows;
        }

        public static bool AllowsDirectWhileProtected(DuelEngine engine, CardInstance monster)
        {
            if (monster == null || !monster.FaceUp) return false;
            foreach (var c in ClausesOn(monster, EffectActionKind.CannotBeAttackTarget))
                if (ConditionMet(engine, c) && c.AllowsDirectAttackWhileProtected)
                    return true;
            return false;
        }

        public static bool IsUnaffectedBy(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (victim == null || source?.Def == null) return false;
            // MonsterNegationAndControlLocks batch: Non-Spellcasting Area (a field effect, not the victim's own effect).
            if (MonsterNegationAndControlLocksRuntime.ShieldsFromSpell(engine, victim, source)) return true;
            if (!victim.FaceUp || victim.IsNegated) return false;
            foreach (var c in ClausesOn(victim, EffectActionKind.UnaffectedByCardEffects))
            {
                if (!ConditionMet(engine, c)) continue;
                // RdsRedoBattle batch: Silent Swordsman LV5 — only the opponent's effects (te:GetOwnerPlayer()~=handler).
                if (c.UnaffectedOnlyByOpponentEffects && !RdsRedoBattleRuntime.SourceIsOpponents(engine, victim, source))
                    continue;
                if (FilterMatches(c.UnaffectedByFilter, source.Def))
                    return true;
            }

            // UnionRiders batch (v175): Metallizing Parasite - Lunatite — its equipped monster is unaffected by the
            // opponent's Spell effects.
            if (UnionRidersRuntime.ShieldsFromOpponentSpell(engine, victim, source))
                return true;

            return false;
        }

        /// <summary>
        /// Resolution-time guard for an effect that selected a field monster.
        /// </summary>
        public static bool TargetedEffectBlocked(DuelEngine engine, CardInstance target,
            CardInstance source)
        {
            if (target == null) return true;
            return CannotBeTargetedByEffects(engine, target) ||
                   IsUnaffectedBy(engine, target, source);
        }

        public static bool CannotBeTargetedByEffects(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster?.Def == null || !monster.FaceUp) return false;

            foreach (var c in ClausesOn(monster, EffectActionKind.CannotBeTargetedByEffects))
            {
                if (!ConditionMet(engine, c)) continue;
                if (string.IsNullOrEmpty(c.NamedCard) && string.IsNullOrEmpty(c.RaceFilter))
                    return true;
            }

            if (IsDragon(monster) && LordOfDFaceUp(engine))
                return true;

            foreach (var src in FaceUpMonsters(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.CannotBeTargetedByEffects))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (!string.IsNullOrEmpty(c.NamedCard) && monster.IsNamed(c.NamedCard))
                        return true;
                    if (!string.IsNullOrEmpty(c.RaceFilter) && MatchesRace(monster, c.RaceFilter))
                        return true;
                }

                foreach (var c in ClausesOn(src, EffectActionKind.ContinuousCannotTargetDragons))
                {
                    if (ConditionMet(engine, c) && IsDragon(monster))
                        return true;
                }
            }

            return false;
        }

        static bool SameMonsterController(DuelEngine engine, CardInstance first,
            CardInstance second)
        {
            if (engine?.Player == null || engine.Opponent == null ||
                first == null || second == null)
                return false;

            return (engine.Player.TryFindMonster(first, out _) &&
                    engine.Player.TryFindMonster(second, out _)) ||
                   (engine.Opponent.TryFindMonster(first, out _) &&
                    engine.Opponent.TryFindMonster(second, out _));
        }


        public static bool ConditionMet(DuelEngine engine, EffectClause c)
        {
            if (c == null) return false;
            if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                return false;
            return true;
        }

        static bool FilterMatches(string filter, Data.CardDef def)
        {
            if (def == null) return false;
            if (string.IsNullOrEmpty(filter) ||
                filter.Equals("all", StringComparison.OrdinalIgnoreCase))
                return true;
            if (filter.Equals("Spell", StringComparison.OrdinalIgnoreCase))
                return def.IsSpell && !def.IsTrap;
            if (filter.Equals("Trap", StringComparison.OrdinalIgnoreCase))
                return def.IsTrap;
            if (filter.Equals("Monster", StringComparison.OrdinalIgnoreCase))
                return def.IsMonster;
            if (filter.Equals("SpellTrap", StringComparison.OrdinalIgnoreCase))
                return def.IsSpell || def.IsTrap;
            return false;
        }

        static IEnumerable<EffectClause> ClausesOn(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClauseList)
            {
                if (c != null && c.Action == action &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp)
                    yield return c;
            }
        }

        /// <summary>
        /// Face-up monsters whose continuous effects apply. A monster whose Summon is still in its
        /// summon-negation window is skipped: it is not on the field until that Summon succeeds.
        /// </summary>
        static IEnumerable<CardInstance> FaceUpMonsters(DuelEngine engine)
        {
            if (engine?.Player != null)
                foreach (var m in engine.Player.MonstersOnField())
                    if (m != null && m.FaceUp && !engine.IsSummonPending(m)) yield return m;
            if (engine?.Opponent != null)
                foreach (var m in engine.Opponent.MonstersOnField())
                    if (m != null && m.FaceUp && !engine.IsSummonPending(m)) yield return m;
        }

        static bool LordOfDFaceUp(DuelEngine engine)
        {
            foreach (var m in FaceUpMonsters(engine))
                if (m.CardId == LordOfD && !m.IsNegated) return true;
            return false;
        }

        static bool IsDragon(CardInstance m) =>
            m?.Race != null &&
            m.Race.IndexOf("Dragon", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool MatchesRace(CardInstance m, string race) =>
            m?.Race != null &&
            !string.IsNullOrEmpty(race) &&
            ClassicEraTemplates.RaceMatches(m.Race, race);

        /// <summary>Necrovalley: neither player can banish cards currently in a GY.</summary>
        public static bool CannotBanishFromGraveyard(DuelEngine engine) => CannotBanishFromGraveyard(engine, null);

        /// <summary>
        /// Necrovalley: cards in <paramref name="gyOwner"/>'s GY cannot be banished, unless that player's GY is unaffected
        /// by the lock card (Gravekeeper's Chief, SummonTriggersAndTributes batch v163; Lua e2a/e2b condition per GY owner).
        /// A null owner asks whether the lock applies at all.
        /// </summary>
        public static bool CannotBanishFromGraveyard(DuelEngine engine, DuelistState gyOwner)
        {
            foreach (var src in FaceUpSpellTraps(engine))
            {
                if (src == null || src.IsNegated) continue;
                if (gyOwner != null && SummonTriggersAndTributesRuntime.GyUnaffectedBy(engine, gyOwner, src)) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.CannotBanishFromGraveyard))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Necrovalley: cards in the GY cannot be targeted, except by ExceptNamedCard
        /// (the effect of "Necrovalley") or a source whose program is unaffected by that name
        /// (Rite of Spirit).
        /// </summary>
        public static bool CannotTargetCardInGy(DuelEngine engine, CardInstance source,
            CardInstance gyCard)
        {
            if (engine == null || gyCard == null) return false;
            // SummonTriggersAndTributes batch (v163): Gravekeeper's Chief — its controller's GY is unaffected by Necrovalley
            // (ygopro EFFECT_NECRO_VALLEY_IM; the lock's conditions are per GY owner).
            var gyOwner = SummonTriggersAndTributesRuntime.GyOwnerOf(engine, gyCard);
            foreach (var lockCard in FaceUpSpellTraps(engine))
            {
                if (lockCard == null || lockCard.IsNegated) continue;
                if (gyOwner != null && SummonTriggersAndTributesRuntime.GyUnaffectedBy(engine, gyOwner, lockCard)) continue;
                foreach (var c in ClausesOn(lockCard, EffectActionKind.CannotTargetCardsInGraveyard))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (source != null && source == lockCard) continue;
                    if (source != null && !string.IsNullOrEmpty(c.ExceptNamedCard) &&
                        source.IsNamed(c.ExceptNamedCard))
                        continue;
                    if (SourceIgnoresNamedLock(source, lockCard))
                        continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Shared Necrovalley leave-GY guard. The current runtime models the official
        /// GY lock through CannotTargetCardInGy, including named exceptions such as Rite
        /// of Spirit; callers use this name for every GY-origin movement resolution.
        /// </summary>
        public static bool GyMovementLocked(DuelEngine engine, CardInstance source,
            CardInstance gyCard) => CannotTargetCardInGy(engine, source, gyCard);

        /// <summary>
        /// Mask of Restrict: neither player can Tribute cards while a face-up non-negated
        /// ContinuousWhileFaceUp NeitherPlayerCanTribute host is on the field.
        /// Soft gates Tribute Summon/Set, Ritual Tribute, cost Tribute, and effect Tribute.
        /// </summary>
        public static bool NeitherPlayerCanTribute(DuelEngine engine)
        {
            foreach (var src in FaceUpSpellTraps(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.NeitherPlayerCanTribute))
                    if (ConditionMet(engine, c))
                        return true;
            }
            // Soft residual: also honor face-up monster ContinuousWhileFaceUp hosts
            // (new-card rule / synthetic Continuous hosts).
            foreach (var src in FaceUpMonsters(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.NeitherPlayerCanTribute))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// The Dark Door: only 1 monster may attack per Battle Phase while a face-up
        /// non-negated ContinuousWhileFaceUp OnlyOneMonsterMayAttackPerBattlePhase host
        /// is on the field. Soft DE MaxAttacksThisTurn Soft-clamps other monsters to 0.
        /// </summary>
        public static bool OnlyOneMonsterMayAttackPerBattlePhase(DuelEngine engine)
        {
            foreach (var src in FaceUpSpellTraps(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OnlyOneMonsterMayAttackPerBattlePhase))
                    if (ConditionMet(engine, c))
                        return true;
            }
            // Soft residual: also honor face-up monster ContinuousWhileFaceUp hosts.
            foreach (var src in FaceUpMonsters(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OnlyOneMonsterMayAttackPerBattlePhase))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Infinite Cards: neither player has a hand size limit while a face-up
        /// non-negated ContinuousWhileFaceUp NoHandSizeLimit host is on the field.
        /// Soft DE HandSizeLimitEndPhase residual skips End Phase discard to 6.
        /// </summary>
        public static bool NoHandSizeLimit(DuelEngine engine)
        {
            foreach (var src in FaceUpSpellTraps(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.NoHandSizeLimit))
                    if (ConditionMet(engine, c))
                        return true;
            }
            // Soft residual: also honor face-up monster ContinuousWhileFaceUp hosts.
            foreach (var src in FaceUpMonsters(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.NoHandSizeLimit))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Soft DE HandSizeLimit residual for Enervating Mist (and twins): End Phase
        /// hand size limit for <paramref name="who"/>. Infinite Cards NoHandSizeLimit
        /// is checked separately and skips discard entirely (bars remapping).
        /// While a face-up non-negated ContinuousWhileFaceUp OpponentHandSizeLimitBecomes
        /// host is controlled by the OTHER player, <paramref name="who"/>'s limit =
        /// clause.Amount (5). Controller of the mist is unaffected (default 6).
        /// Soft residual OK Soft DE HandSizeLimit override for opponent only.
        /// </summary>
        public static int HandSizeLimitEndPhaseFor(DuelEngine engine, DuelistState who)
        {
            var limit = TcgRules.HandSizeLimitEndPhase;
            if (engine == null || who == null) return limit;
            // PgdSpellTrapBasics batch (v162): Hieroglyph Lithograph — "your hand size limit becomes 7" for the rest of
            // the Duel. Two "becomes N" limits: the one applied later wins. A Mist already applying when the Lithograph
            // resolved is overridden by it (PgdSpellTrapBasicsRuntime.AppliedBeforeLithograph); one applied after it
            // overrides it. Infinite Cards (NoHandSizeLimit, checked by the caller) still removes every limit.
            if (who.HandSizeLimitByEffect > 0)
                limit = who.HandSizeLimitByEffect;

            // Mist on the OTHER player's field applies to `who` (Side=Opponent).
            DuelistState other = null;
            if (who == engine.Player) other = engine.Opponent;
            else if (who == engine.Opponent) other = engine.Player;
            if (other == null) return limit;

            int? mistLimit = null;
            foreach (var src in other.SpellTrapsOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                if (PgdSpellTrapBasicsRuntime.AppliedBeforeLithograph(who, src)) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OpponentHandSizeLimitBecomes))
                {
                    if (!ConditionMet(engine, c)) continue;
                    var amt = c.Amount > 0 ? c.Amount : 5;
                    mistLimit = mistLimit.HasValue
                        ? System.Math.Min(mistLimit.Value, amt)
                        : amt;
                }
            }
            // Soft residual: also honor face-up monster ContinuousWhileFaceUp hosts.
            foreach (var src in other.MonstersOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                if (PgdSpellTrapBasicsRuntime.AppliedBeforeLithograph(who, src)) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OpponentHandSizeLimitBecomes))
                {
                    if (!ConditionMet(engine, c)) continue;
                    var amt = c.Amount > 0 ? c.Amount : 5;
                    mistLimit = mistLimit.HasValue
                        ? System.Math.Min(mistLimit.Value, amt)
                        : amt;
                }
            }
            return mistLimit ?? limit;
        }

        /// <summary>
        /// Spell Economics: controller does not pay Life Points to activate Spell Cards
        /// while a face-up non-negated ContinuousWhileFaceUp
        /// ControllerDoesNotPayLpToActivateSpells host THEY control is on the field.
        /// Soft TextEffectRuntime Spell activate PayLp skip (Mask Soft surgical). Fail-closed:
        /// Trap LP costs, opponent's Spell costs, maintenance / Standby pays still apply.
        /// Soft residual OK Soft ContinuousProtections / Soft Spell activate PayLp skip.
        /// </summary>
        public static bool ControllerDoesNotPayLpToActivateSpells(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            foreach (var src in who.SpellTrapsOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.ControllerDoesNotPayLpToActivateSpells))
                    if (ConditionMet(engine, c))
                        return true;
            }
            // Soft residual: also honor face-up monster ContinuousWhileFaceUp hosts.
            foreach (var src in who.MonstersOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.ControllerDoesNotPayLpToActivateSpells))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Soft residual helper: true when <paramref name="card"/> is a Spell (not Trap)
        /// being activated by <paramref name="who"/> and Spell Economics Soft gate applies.
        /// </summary>
        public static bool SkipsSpellActivatePayLp(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (card?.Def == null || !card.Def.IsSpell || card.Def.IsTrap) return false;
            return ControllerDoesNotPayLpToActivateSpells(engine, who);
        }

        /// <summary>
        /// The Emperor's Holiday: negate all Equip Card effects on the field while a
        /// face-up non-negated ContinuousWhileFaceUp NegateAllEquipCardEffectsOnField
        /// host is on the field (either player). Soft FieldSpellEffects Equip-apply
        /// skip (Mask Soft surgical). Fail-closed: Field/Continuous Spell/Trap and
        /// monster effects stay live; destroy Equips / Really Eternal Rest / Decree /
        /// Imperial Order / Spell Canceller are separate atoms.
        /// Soft residual OK Soft ContinuousProtections Equip-effect skip.
        /// </summary>
        public static bool NegateAllEquipCardEffectsOnField(DuelEngine engine)
        {
            if (engine == null) return false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var src in who.SpellTrapsOnField())
                {
                    if (src == null || !src.FaceUp || src.IsNegated) continue;
                    foreach (var c in ClausesOn(src, EffectActionKind.NegateAllEquipCardEffectsOnField))
                        if (ConditionMet(engine, c))
                            return true;
                }
                // Soft residual: also honor face-up monster ContinuousWhileFaceUp hosts.
                foreach (var src in who.MonstersOnField())
                {
                    if (src == null || !src.FaceUp || src.IsNegated) continue;
                    foreach (var c in ClausesOn(src, EffectActionKind.NegateAllEquipCardEffectsOnField))
                        if (ConditionMet(engine, c))
                            return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Soft residual helper: true when <paramref name="card"/> is an Equip Spell
        /// whose effects are Soft-negated by Emperor's Holiday ContinuousProtections.
        /// Does not gate Field/Continuous Spells/Traps or monster effects.
        /// </summary>
        public static bool EquipCardEffectsAreNegated(DuelEngine engine, CardInstance card)
        {
            // EquipModel batch: every Equip Card, not only Equip Spells — an Equip Trap or a monster acting as an Equip
            // Card (Union, Kiseitai, Dark Necrofear) is TYPE_EQUIP on the field too (Lua distarget c:IsType(TYPE_EQUIP)).
            if (engine == null || card?.Def == null) return false;
            if (!card.Def.IsEquipSpell && !EquipModelRuntime.IsEquipCardShape(card)) return false;
            if (!card.FaceUp) return false;
            // Imperial Order / Spell Canceller negate this Equip Spell itself (ContinuousNegation batch).
            if (card.IsNegated) return true;
            // Armored Glass (EquipModel batch): Equip Cards negated through this turn, checked live (an Equip Card
            // equipped since the last board refresh, before its NegatedByTurnFamily flag is set).
            if (TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, card)) return true;
            return NegateAllEquipCardEffectsOnField(engine);
        }

        /// <summary>
        /// Soft BP attack-count helper: true when another of <paramref name="who"/>'s
        /// monsters already declared an attack this Battle Phase (so Dark Door bars this attacker).
        /// Same-monster ExtraAttacks remain OK (attacker itself already counted). The attack count
        /// restarts for a second Battle Phase (Weather Report); AttackedThisTurn does not, so it is not read.
        /// </summary>
        public static bool AnotherMonsterAlreadyAttackedThisBattlePhase(
            DuelistState who, CardInstance attacker)
        {
            if (who == null || attacker == null) return false;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null) continue;
                if (ReferenceEquals(m, attacker)) continue;
                if (attacker.InstanceId != 0 && m.InstanceId == attacker.InstanceId) continue;
                if (m.AttacksDeclaredThisTurn > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Protector of the Sanctuary: true when <paramref name="who"/> is the opponent
        /// of a face-up non-negated ContinuousWhileFaceUp
        /// OpponentCannotDrawExceptDuringDrawPhases host (i.e. the OTHER player controls
        /// Protector). Does NOT check Phase — Soft DE Draw() allows Draw Phase draws.
        /// Controller of the host is unrestricted. Soft residual OK: searches /
        /// AddToHand / AddFromDeckToHand ≠ draw (not gated here).
        /// </summary>
        public static bool OpponentCannotDrawExceptDuringDrawPhases(
            DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            DuelistState other = null;
            if (who == engine.Player) other = engine.Opponent;
            else if (who == engine.Opponent) other = engine.Player;
            if (other == null) return false;

            // Prefer monster hosts (official print is an Effect Monster); Soft residual
            // also honors face-up Spell/Trap ContinuousWhileFaceUp hosts.
            foreach (var src in other.MonstersOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OpponentCannotDrawExceptDuringDrawPhases))
                    if (ConditionMet(engine, c))
                        return true;
            }
            foreach (var src in other.SpellTrapsOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OpponentCannotDrawExceptDuringDrawPhases))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Soft DE Draw residual helper: true when <paramref name="who"/> cannot draw
        /// right now (Protector gate live AND Phase is not Draw). Draw Phase draws OK.
        /// </summary>
        public static bool BlocksDrawOutsideDrawPhase(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            if (engine.Phase == DuelPhase.Draw) return false;
            return OpponentCannotDrawExceptDuringDrawPhases(engine, who);
        }

        /// <summary>
        /// Swords of Concealing Light: true when <paramref name="monster"/> is controlled
        /// by the opponent of a face-up non-negated ContinuousWhileFaceUp
        /// OpponentCannotChangeBattlePositions host. Controller's monsters unrestricted.
        /// Gates Soft DE CanChangePosition (face-up manual BP) and Soft DE CanFlipSummon
        /// (manual Flip Summon IS a battle-position change — Fanbot mid-QC). Face-down
        /// included so Flip Summon Soft underprint cannot leave the path open.
        /// Soft residual OK: card-effect BP changes (Enemy Controller Soft path /
        /// ApplyEffectPositionChange) not gated; new summons Soft residual no auto-flip
        /// (resolve-only Activate flip; FU ATK Soft-OK).
        /// </summary>
        public static bool OpponentCannotChangeBattlePositions(
            DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster == null) return false;
            var ctrl = engine.ControllerOf(monster);
            if (ctrl == null) return false;
            DuelistState other = null;
            if (ctrl == engine.Player) other = engine.Opponent;
            else if (ctrl == engine.Opponent) other = engine.Player;
            if (other == null) return false;

            // Prefer Spell/Trap Continuous hosts (official print is Continuous Spell);
            // Soft residual also honors face-up monster ContinuousWhileFaceUp hosts.
            foreach (var src in other.SpellTrapsOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OpponentCannotChangeBattlePositions))
                    if (ConditionMet(engine, c))
                        return true;
            }
            foreach (var src in other.MonstersOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OpponentCannotChangeBattlePositions))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Soft residual helper: true when <paramref name="who"/> controls a face-up
        /// OpponentCannotChangeBattlePositions / SetAllOppMonstersFaceDownDefense host
        /// with ContinuousTurnsRemaining &gt; 0 (Standby self-destroy ticker live).
        /// </summary>
        public static bool HasConcealingLightStandbyTicker(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            foreach (var src in who.SpellTrapsOnField())
            {
                if (src == null || !src.FaceUp || src.IsNegated) continue;
                if (src.ContinuousTurnsRemaining <= 0) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.OpponentCannotChangeBattlePositions))
                    if (ConditionMet(engine, c))
                        return true;
                foreach (var c in ClausesOn(src, EffectActionKind.SetAllOppMonstersFaceDownDefense))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }


        // ── Soft residual Fake Trap destroy-replacement redirect ───────────────
        // Prefer NO DE rewrite: Soft surgical Soft ContinuousProtections + Soft
        // TextEffectRuntime.DestroyCard hook. Soft ≠ hard.

        /// <summary>
        /// Soft residual: true when <paramref name="link"/> is an opponent Spell/Trap/
        /// Effect Monster activation whose Activate (or Flip) text would destroy a Trap
        /// Card(s) <paramref name="who"/> controls. Fail-closed vs banish/send/return.
        /// Soft look-confirm Soft-OK (caller Soft-logs face-down). Soft ≠ hard.
        /// </summary>
        public static bool OppEffectWouldDestroyYourTrap(DuelEngine engine, DuelistState who,
            ChainLink link)
        {
            if (engine == null || who == null || link == null || link.Negated) return false;
            if (link.Controller == null || link.Controller == who) return false;
            var def = link.Card?.Def;
            if (def == null) return false;
            // Spell, Trap, or Effect Monster only (card bar Soft Fanbot locked).
            if (!def.IsSpell && !def.IsTrap && !(def.IsMonster && TextEffectRuntime.IsTrueEffectMonster(def)))
                return false;
            var would = CollectWouldBeDestroyedControllerTraps(engine, who, link);
            return would != null && would.Count > 0;
        }


        /// <summary>
        /// Soft residual: Trap Cards <paramref name="who"/> controls that the opponent
        /// chain link would destroy. Fail-closed: Destroy family only (≠ Banish/ReturnToHand).
        /// Soft residual only your Traps (Spells not Soft-protected). Soft ≠ hard.
        /// </summary>
        public static System.Collections.Generic.List<CardInstance> CollectWouldBeDestroyedControllerTraps(
            DuelEngine engine, DuelistState who, ChainLink link)
        {
            var list = new System.Collections.Generic.List<CardInstance>();
            if (engine == null || who == null || link?.Card?.Def == null) return list;
            if (link.Controller == who) return list;
            var prog = CompiledEffectCache.GetOrCompile(link.Card.Def);
            if (prog == null) return list;

            bool destroyClause(EffectClause c)
            {
                if (c == null) return false;
                // Fail-closed: only Destroy family (≠ Banish / ReturnToHand / send).
                if (c.Action == EffectActionKind.Destroy ||
                    c.Action == EffectActionKind.DestroyAllOppSpellTraps)
                    return true;
                return false;
            }

            bool clauseHitsControllerTraps(EffectClause c)
            {
                if (!destroyClause(c)) return false;
                var seated = new System.Collections.Generic.List<CardInstance>();
                if (link.Target != null) seated.Add(link.Target);
                if (link.Targets != null)
                {
                    foreach (var tg in link.Targets)
                        if (tg != null && !seated.Contains(tg)) seated.Add(tg);
                }
                // Seated targets are authority (TCG: target at activation, then respond).
                // Fail-closed: MST targeting a Spell you control is NOT "would destroy a Trap".
                if (seated.Count > 0)
                {
                    foreach (var tg in seated)
                    {
                        if (tg?.Def == null || !tg.Def.IsTrap) continue;
                        if (engine.ControllerOf(tg) == who) return true;
                    }
                    return false;
                }
                // Soft residual: targeted Destroy with no seated pick yet Soft-OK if a Trap
                // you control could be a legal target. Soft ≠ hard.
                if (c.RequiresTargetChoice)
                {
                    if (c.Zone == EffectZoneFilter.FieldSpellTraps ||
                        c.Zone == EffectZoneFilter.FaceDownSpellTraps ||
                        c.Zone == EffectZoneFilter.OppAnyCardOnField ||
                        (!string.IsNullOrEmpty(c.DestroyIfType) &&
                         c.DestroyIfType.Equals("Trap", System.StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var st in who.SpellTrapsOnField())
                            if (st?.Def != null && st.Def.IsTrap) return true;
                    }
                    return false;
                }

                // Non-targeting mass Destroy of Spell/Traps (Heavy Storm / Feather Duster).
                if (c.Action == EffectActionKind.DestroyAllOppSpellTraps)
                {
                    foreach (var st in who.SpellTrapsOnField())
                        if (st?.Def != null && st.Def.IsTrap) return true;
                    return false;
                }
                if (c.Action == EffectActionKind.Destroy &&
                    (c.Zone == EffectZoneFilter.FieldSpellTraps ||
                     c.Zone == EffectZoneFilter.AnyCardOnField ||
                     c.Zone == EffectZoneFilter.AllOtherCardsOnField ||
                     c.Zone == EffectZoneFilter.OppAnyCardOnField))
                {
                    // Side Both / Opponent Soft: hits your Traps when Side includes them.
                    if (c.Side == EffectSide.Controller) return false;
                    foreach (var st in who.SpellTrapsOnField())
                        if (st?.Def != null && st.Def.IsTrap) return true;
                }
                return false;
            }

            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                if (c.Timing != EffectTiming.Activate && c.Timing != EffectTiming.Flip &&
                    c.Timing != EffectTiming.ContinuousWhileFaceUp)
                    continue;
                if (!clauseHitsControllerTraps(c)) continue;
                // Collect concrete Soft would-be-destroyed Trap set.
                var seatedCollect = new System.Collections.Generic.List<CardInstance>();
                if (link.Target != null) seatedCollect.Add(link.Target);
                if (link.Targets != null)
                {
                    foreach (var tg in link.Targets)
                        if (tg != null && !seatedCollect.Contains(tg)) seatedCollect.Add(tg);
                }
                if (seatedCollect.Count > 0)
                {
                    foreach (var tg in seatedCollect)
                    {
                        if (tg?.Def == null || !tg.Def.IsTrap) continue;
                        if (engine.ControllerOf(tg) != who) continue;
                        if (!list.Contains(tg)) list.Add(tg);
                    }
                }
                else if (c.RequiresTargetChoice &&
                    (c.Zone == EffectZoneFilter.FieldSpellTraps ||
                     c.Zone == EffectZoneFilter.FaceDownSpellTraps ||
                     (!string.IsNullOrEmpty(c.DestroyIfType) &&
                      c.DestroyIfType.Equals("Trap", System.StringComparison.OrdinalIgnoreCase))))
                {
                    foreach (var st in who.SpellTrapsOnField())
                        if (st?.Def != null && st.Def.IsTrap && !list.Contains(st))
                            list.Add(st);
                }
                else
                {
                    foreach (var st in who.SpellTrapsOnField())
                        if (st?.Def != null && st.Def.IsTrap && !list.Contains(st))
                            list.Add(st);
                }
            }
            return list;
        }

        /// <summary>
        /// Soft residual Fake Trap resolve: Soft-register destroy-replacement protect for
        /// would-be-destroyed Traps vs the answered source; Soft look-confirm Soft-OK.
        /// Soft ≠ hard. Prefer NO DE rewrite.
        /// </summary>
        public static void RegisterFakeTrapSubstitute(DuelEngine engine, DuelistState who,
            CardInstance fakeTrap, ChainLink answered)
        {
            if (engine == null || who == null || fakeTrap == null) return;
            var prey = CollectWouldBeDestroyedControllerTraps(engine, who, answered);
            if (prey == null || prey.Count == 0) return;
            if (who.SoftFakeTrapProtectedTrapIds == null)
                who.SoftFakeTrapProtectedTrapIds = new System.Collections.Generic.HashSet<int>();
            if (who.SoftFakeTrapAgainstSourceIds == null)
                who.SoftFakeTrapAgainstSourceIds = new System.Collections.Generic.HashSet<int>();
            var sourceId = answered?.Card != null ? answered.Card.InstanceId : 0;
            if (sourceId != 0)
                who.SoftFakeTrapAgainstSourceIds.Add(sourceId);
            foreach (var st in prey)
            {
                if (st == null || st == fakeTrap) continue;
                who.SoftFakeTrapProtectedTrapIds.Add(st.InstanceId);
                // Soft residual look-confirm Soft-OK: face-down Soft-log reveal.
                if (!st.FaceUp)
                    engine.Log(
                        $"{fakeTrap.Name}: Soft look-confirm Soft residual — face-down " +
                        $"{st.Name} would have been destroyed (Soft-OK).");
            }
            engine.Log(
                $"{fakeTrap.Name}: Soft destroy-replacement Soft-protects {prey.Count} Trap(s) " +
                $"(Soft residual Soft ContinuousProtections redirect).");
        }

        /// <summary>
        /// Soft residual Fake Trap: true when Destroy of <paramref name="card"/> by
        /// <paramref name="source"/> must Soft-skip (Fake Trap already Soft-died instead).
        /// Fail-closed: only Trap Cards Soft-protected vs Soft-registered sources.
        /// Soft ≠ hard. Prefer NO DE rewrite.
        /// </summary>
        public static bool TrySoftFakeTrapRedirectDestroy(DuelEngine engine, CardInstance card,
            CardInstance source)
        {
            if (engine == null || card?.Def == null || !card.Def.IsTrap) return false;
            var owner = engine.ControllerOf(card);
            if (owner?.SoftFakeTrapProtectedTrapIds == null ||
                owner.SoftFakeTrapAgainstSourceIds == null)
                return false;
            if (!owner.SoftFakeTrapProtectedTrapIds.Contains(card.InstanceId))
                return false;
            // Soft residual: if source is null Soft-skip when Soft-protect live Soft-OK;
            // Prefer Soft match against source InstanceId when present.
            if (source != null &&
                !owner.SoftFakeTrapAgainstSourceIds.Contains(source.InstanceId))
                return false;
            engine.Log(
                $"{card.Name} is not destroyed (Soft Fake Trap destroy-replacement Soft residual).");
            return true;
        }

        /// <summary>
        /// Soft residual: clear Fake Trap Soft-protect after chain settles Soft-OK.
        /// Soft ≠ hard.
        /// </summary>
        public static void ClearFakeTrapSubstitute(DuelistState who)
        {
            if (who == null) return;
            who.SoftFakeTrapProtectedTrapIds?.Clear();
            who.SoftFakeTrapAgainstSourceIds?.Clear();
        }

        static bool SourceIgnoresNamedLock(CardInstance source, CardInstance lockCard)
        {

            if (source?.Def == null || lockCard == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(source.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || string.IsNullOrEmpty(c.UnaffectedByNamedCard)) continue;
                if (lockCard.IsNamed(c.UnaffectedByNamedCard))
                    return true;
            }
            return false;
        }

        static System.Collections.Generic.IEnumerable<CardInstance> FaceUpSpellTraps(DuelEngine engine)
        {
            var player = engine?.Player;
            if (player != null)
                foreach (var st in player.SpellTrapsOnField())
                    if (st != null && st.FaceUp) yield return st;

            var opponent = engine?.Opponent;
            if (opponent != null)
                foreach (var st in opponent.SpellTrapsOnField())
                    if (st != null && st.FaceUp) yield return st;
        }
    }
}
