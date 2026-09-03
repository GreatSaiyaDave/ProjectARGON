using System;
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
            ClearModifiers(engine.Player);
            ClearModifiers(engine.Opponent);
            ApplyFaceUpField(engine, engine.Player.FieldSpellZone?.Occupant);
            ApplyFaceUpField(engine, engine.Opponent.FieldSpellZone?.Occupant);
            ApplyFaceUpMonsterAuras(engine, engine.Player);
            ApplyFaceUpMonsterAuras(engine, engine.Opponent);
            ApplyCatalogSpellTraps(engine, engine.Player);
            ApplyCatalogSpellTraps(engine, engine.Opponent);
            DestroyNamedLeavesContinuous(engine, engine.Player);
            DestroyNamedLeavesContinuous(engine, engine.Opponent);
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

        static void ClearOne(CardInstance c)
        {
            if (c == null) return;
            c.AtkModifier = 0;
            c.DefModifier = 0;
            c.LevelModifier = 0;
        }

        static void ApplyFaceUpField(DuelEngine engine, CardInstance field)
        {
            if (engine == null || field?.Def == null || !field.FaceUp) return;

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
            var equipped = host.Equips[0];
            var prog = CompiledEffectCache.GetOrCompile(host.Def);
            if (prog != null)
            {
                foreach (var c in prog.ClauseList)
                {
                    if (c == null || c.Action != EffectActionKind.EquipTargetToThis) continue;
                    var eatk = equipped.Def != null ? Math.Max(0, equipped.Def.atk) : 0;
                    var edef = equipped.Def != null ? Math.Max(0, equipped.Def.def) : 0;
                    host.AtkModifier += eatk - (host.Def != null ? host.Def.atk : 0);
                    host.DefModifier += edef - (host.Def != null ? host.Def.def : 0);
                    return;
                }
            }

            foreach (var eq in host.Equips)
            {
                var ep = eq?.Def != null ? CompiledEffectCache.GetOrCompile(eq.Def) : null;
                if (ep == null) continue;
                foreach (var c in ep.ClauseList)
                {
                    if (c != null && (c.EquipAtkBonus != 0 || c.EquipDefBonus != 0))
                    {
                        var atk = c.EquipAtkBonus;
                        var def = c.EquipDefBonus;
                        if (c.ScaleAmountByControllerMonsters || c.ScaleAmountByControllerSpellTraps)
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
                    ApplyLevelMod(engine, clause.AttributeFilter, -Math.Abs(clause.Amount),
                        applyToHand: clause.ApplyToHand, applyToField: clause.ApplyToField || !clause.ApplyToHand);
                    any = true;
                }
            }

            return any;
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

        static void ApplyFaceUpMonsterAuras(DuelEngine engine, DuelistState who)
        {
            if (who == null) return;
            foreach (var m in who.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp || m.IsNegated) continue;
                if (ApplyCatalogAuras(engine, m, "MZONE")) continue;
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
                ApplyCatalogAuras(engine, st, "SZONE");
                ApplyCompiledContinuous(engine, st);
            }
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
            var controllerOnly = clause.Side == EffectSide.Controller;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                if (controllerOnly && who != controller) continue;
                foreach (var m in who.MonstersOnField())
                {
                    if (!IsFaceUpMonster(m)) continue;
                    if (!MatchesAttribute(m, clause.AttributeFilter)) continue;
                    if (!MatchesRace(m, clause.RaceFilter)) continue;
                    m.AtkModifier += clause.Amount;
                    m.DefModifier += clause.DefAmount;
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

        static bool MatchesAttribute(CardInstance c, string attribute)
        {
            if (string.IsNullOrEmpty(attribute) || c?.Def == null) return true;
            return c.Def.attribute != null &&
                   c.Def.attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase);
        }

        static bool MatchesRace(CardInstance c, string race)
        {
            if (string.IsNullOrEmpty(race) || c?.Def == null) return true;
            return c.Def.race != null &&
                   c.Def.race.IndexOf(race, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ControlsFaceUpNamed(DuelistState who, string name)
        {
            if (who == null || string.IsNullOrEmpty(name)) return false;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (st != null && st.FaceUp && st.IsNamed(name)) return true;
            }

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
            if (ControlsFaceUpNamed(engine.Player, name) ||
                ControlsFaceUpNamed(engine.Opponent, name))
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
        /// Face-up Field Spell in either Field Zone (blocks Maiden of the Aqua).
        /// </summary>
        public static bool FaceUpFieldSpellExists(DuelEngine engine)
        {
            if (engine == null) return false;
            var a = engine.Player?.FieldSpellZone?.Occupant;
            var b = engine.Opponent?.FieldSpellZone?.Occupant;
            return (a != null && a.FaceUp) || (b != null && b.FaceUp);
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
                if (prog == null) continue;
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
        public static bool ControllerAvoidsBattleDamage(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp || st.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.PreventControllerBattleDamage)
                        continue;
                    if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                        !NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                        continue;
                    return true;
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
                if (st == null || !st.FaceUp || st.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null) continue;
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
                engine.Log($"{st.Name} is destroyed ({st.Def?.name} required a named card that left).");
                engine.SendCardToGrave(who, st);
            }
        }
    }
}
