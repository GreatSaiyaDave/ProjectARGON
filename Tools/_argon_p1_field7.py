from pathlib import Path
p = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Scripts/WRLDZ/Duel/TextEffects/LegacyTextTemplates.cs')
t = p.read_text(encoding='utf-8')
old = 'RxEquipIncreaseTyped.IsMatch(text) || RxEquipOnlyKind.IsMatch(text) ||\n                 RxEquipOnlyKindLoseDef.IsMatch(text)'
new = 'RxEquipIncreaseTyped.IsMatch(text) || RxEquipOnlyKind.IsMatch(text) ||\n                 RxEquipOnlyNamedOr.IsMatch(text) ||\n                 RxEquipOnlyKindLoseDef.IsMatch(text)'
n = t.count(old)
print('count', n)
if n != 1:
    raise SystemExit('count fail')
p.write_text(t.replace(old, new, 1), encoding='utf-8')
print('OK ExpectedActions named-or')
