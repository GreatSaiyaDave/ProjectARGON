#!/usr/bin/env python3
from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def replace_once(path: Path, old: str, new: str, label: str):
    t = path.read_text(encoding='utf-8')
    if old not in t:
        raise SystemExit(f'FAIL {label}: old not found')
    n = t.count(old)
    if n != 1:
        raise SystemExit(f'FAIL {label}: count={n}')
    path.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)

ev = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/EffectVocabulary.cs'
replace_once(ev,
'''                    EffectActionKind.PiercingBattleDamage =>
                    EffectResolutionKind.Protection,
''',
'''                    EffectActionKind.PiercingBattleDamage or
                    EffectActionKind.CannotBanishFromGraveyard or
                    EffectActionKind.CannotTargetCardsInGraveyard =>
                    EffectResolutionKind.Protection,
''',
    'EffectVocabulary GY locks')

val = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/EffectProgramValidator.cs'
replace_once(val,
'CannotBeDestroyedByBattle|PiercingBattleDamage|GainLifePoints',
'CannotBeDestroyedByBattle|PiercingBattleDamage|CannotBanishFromGraveyard|CannotTargetCardsInGraveyard|GainLifePoints',
    'validator GY lock actions')

# Cyber Shield named-or-named equip
leg = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/LegacyTextTemplates.cs'
replace_once(leg,
'''        static readonly Regex RxEquipOnlyKind = new(
            EquipOnlyPrefix + @"It gains (\\d+) ATK(?:/DEF| and DEF)?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
'''        static readonly Regex RxEquipOnlyKind = new(
            EquipOnlyPrefix + @"It gains (\\d+) ATK(?:/DEF| and DEF)?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Cyber Shield: Equip only to "Harpie Lady" or "Harpie Lady Sisters". It gains 500 ATK.</summary>
        static readonly Regex RxEquipOnlyNamedOr = new(
            @"Equip only to ""([^""]+)"" or ""([^""]+)""\\.\\s*It gains (\\d+) ATK(?:/DEF| and DEF)?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
    'RxEquipOnlyNamedOr')

replace_once(leg,
'''                var onlyLose = RxEquipOnlyKindLoseDef.Match(text);
                if (onlyLose.Success)
                    Add(onlyLose, EquipClause(onlyLose.Groups[1].Value,
                        Parse(onlyLose, 2, 400), -Parse(onlyLose, 3, 200)));
                else
                {
                    var only = RxEquipOnlyKind.Match(text);
                    if (only.Success)
                    {
                        var n = Parse(only, 2, 300);
                        var slash = only.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0
                                    || only.Value.IndexOf("and DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                        Add(only, EquipClause(only.Groups[1].Value, n, slash ? n : 0));
                    }
                }
''',
'''                var namedOr = RxEquipOnlyNamedOr.Match(text);
                if (namedOr.Success)
                {
                    var n = Parse(namedOr, 3, 500);
                    var slash = namedOr.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0
                                || namedOr.Value.IndexOf("and DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                    var eq = EquipClause("", n, slash ? n : 0);
                    eq.EquipHostName = namedOr.Groups[1].Value;
                    eq.AltNamedCard = namedOr.Groups[2].Value;
                    Add(namedOr, eq);
                }

                var onlyLose = RxEquipOnlyKindLoseDef.Match(text);
                if (onlyLose.Success)
                    Add(onlyLose, EquipClause(onlyLose.Groups[1].Value,
                        Parse(onlyLose, 2, 400), -Parse(onlyLose, 3, 200)));
                else if (!namedOr.Success)
                {
                    var only = RxEquipOnlyKind.Match(text);
                    if (only.Success)
                    {
                        var n = Parse(only, 2, 300);
                        var slash = only.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0
                                    || only.Value.IndexOf("and DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                        Add(only, EquipClause(only.Groups[1].Value, n, slash ? n : 0));
                    }
                }
''',
    'Collect EquipOnlyNamedOr')

replace_once(leg,
'''                 RxEquipOnlyKind.IsMatch(text) ||
                 RxEquipOnlyKindLoseDef.IsMatch(text) || RxEquippedGainsAtkDef.IsMatch(text) ||
''',
'''                 RxEquipOnlyKind.IsMatch(text) || RxEquipOnlyNamedOr.IsMatch(text) ||
                 RxEquipOnlyKindLoseDef.IsMatch(text) || RxEquippedGainsAtkDef.IsMatch(text) ||
''',
    'ExpectedActions EquipOnlyNamedOr')

print('vocab/validator/cyber shield done')
