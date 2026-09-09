#!/usr/bin/env python3
from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def replace_once(path: Path, old: str, new: str, label: str):
    t = path.read_text(encoding='utf-8')
    if old not in t:
        raise SystemExit(f'FAIL {label}: old not found in {path}')
    n = t.count(old)
    if n != 1:
        raise SystemExit(f'FAIL {label}: old count={n} in {path}')
    path.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)

de = root/'Assets/Scripts/WRLDZ/Duel/DuelEngine.cs'
replace_once(de,
'''        DuelistState _queuedOwnSummonResponder;
        DuelistState _queuedOwnSummoner;
        CardInstance _queuedOwnSummoned;
''',
'''        DuelistState _queuedOwnSummonResponder;
        DuelistState _queuedOwnSummoner;
        CardInstance _queuedOwnSummoned;
        DuelistState _fieldSummonTriggerSummoner;
        CardInstance _fieldSummonTriggerSummoned;
''',
    'field trigger remember fields')

st = root/'Assets/Scripts/WRLDZ/Duel/SpellTrapEffects.cs'
replace_once(st,
'''        public bool ResumeDamageCalculation;
        public bool AlsoAffectsDef;

        public string Prompt
''',
'''        public bool ResumeDamageCalculation;
        public bool AlsoAffectsDef;
        /// <summary>Pending from a face-up Field Spell summon trigger (Harpies' Hunting Ground).</summary>
        public bool FromSummonWindow;

        public string Prompt
''',
    'PendingActivation.FromSummonWindow')

replace_once(st,
'''            if (clause.RequiresSummonedIsToken && !summoned.IsToken) return false;
            if (clause.RequiresSummonedIsFusion &&
                (summoned.Def == null || summoned.Def.type == null ||
                 summoned.Def.type.IndexOf("Fusion", System.StringComparison.OrdinalIgnoreCase) < 0))
                return false;
''',
'''            if (clause.RequiresSummonedIsToken && !summoned.IsToken) return false;
            if (clause.RequiresSummonedIsFusion &&
                (summoned.Def == null || summoned.Def.type == null ||
                 summoned.Def.type.IndexOf("Fusion", System.StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (!string.IsNullOrEmpty(clause.NamedCard) || !string.IsNullOrEmpty(clause.AltNamedCard))
            {
                FieldSpellEffects.ApplyRuleConditions(summoned);
                var ok = (!string.IsNullOrEmpty(clause.NamedCard) && summoned.IsNamed(clause.NamedCard)) ||
                         (!string.IsNullOrEmpty(clause.AltNamedCard) && summoned.IsNamed(clause.AltNamedCard));
                if (!ok) return false;
            }
''',
    'SummonWindowClauseLegal named filter')

replace_once(st,
'''                case EffectTargetKind.MonsterInEitherGy:
                    foreach (var c in who.Graveyard)
                        if (IsLegalGyMonster(c)) list.Add(c);
                    foreach (var c in opp.Graveyard)
                        if (IsLegalGyMonster(c)) list.Add(c);
                    break;
''',
'''                case EffectTargetKind.MonsterInEitherGy:
                    foreach (var c in who.Graveyard)
                        if (IsLegalGyMonster(c) &&
                            !TextEffects.ContinuousProtections.CannotTargetCardInGy(engine, exceptCard, c))
                            list.Add(c);
                    foreach (var c in opp.Graveyard)
                        if (IsLegalGyMonster(c) &&
                            !TextEffects.ContinuousProtections.CannotTargetCardInGy(engine, exceptCard, c))
                            list.Add(c);
                    break;
''',
    'CollectLegalTargets GY lock')

# EquipHostName AltNamedCard in TextEffectRuntime CollectTargets
rt = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs'
replace_once(rt,
'''                        if (!string.IsNullOrEmpty(c.EquipHostName) && !m.IsNamed(c.EquipHostName) &&
                            (m.Name == null ||
                             !string.Equals(m.Name, c.EquipHostName, System.StringComparison.OrdinalIgnoreCase)))
                            continue;
                        list.Add(m);
''',
'''                        if (!string.IsNullOrEmpty(c.EquipHostName))
                        {
                            var hostOk = m.IsNamed(c.EquipHostName) ||
                                         (m.Name != null &&
                                          string.Equals(m.Name, c.EquipHostName,
                                              System.StringComparison.OrdinalIgnoreCase)) ||
                                         (!string.IsNullOrEmpty(c.AltNamedCard) &&
                                          (m.IsNamed(c.AltNamedCard) ||
                                           (m.Name != null &&
                                            string.Equals(m.Name, c.AltNamedCard,
                                                System.StringComparison.OrdinalIgnoreCase))));
                            if (!hostOk) continue;
                        }
                        list.Add(m);
''',
    'EquipHostName or AltNamedCard')

# GY CollectTargets filter after building list, before RequiresAtkLeqCost
replace_once(rt,
'''            if (c.RequiresAtkLeqCost && costNumeric > 0)
                list.RemoveAll(t => t == null || t.CurrentAtk > costNumeric);
''',
'''            if (c.Zone == EffectZoneFilter.EitherGyMonsters ||
                c.Zone == EffectZoneFilter.ControllerGyMonsters ||
                c.Zone == EffectZoneFilter.ControllerGySpells ||
                c.Zone == EffectZoneFilter.ControllerGyTraps)
                list.RemoveAll(t =>
                    ContinuousProtections.CannotTargetCardInGy(engine, except, t));

            if (c.RequiresAtkLeqCost && costNumeric > 0)
                list.RemoveAll(t => t == null || t.CurrentAtk > costNumeric);
''',
    'CollectTargets GY lock filter')

print('engine fields + ST + runtime target filters done')
