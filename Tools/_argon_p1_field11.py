from pathlib import Path
p = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs')
t = p.read_text(encoding='utf-8')
old = 'CollectBanishGy(who, c)'
print('count', t.count(old))
t = t.replace(old, 'CollectBanishGy(engine, who, c)')
p.write_text(t, encoding='utf-8')
print('replaced')
