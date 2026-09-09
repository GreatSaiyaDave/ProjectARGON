from pathlib import Path
p = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs')
t = p.read_text(encoding='utf-8')
old = '                else if (!who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)'
new = '                else if (who != null && !who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)'
if old not in t:
    print('MISSING nre guard')
else:
    p.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK nre guard')
