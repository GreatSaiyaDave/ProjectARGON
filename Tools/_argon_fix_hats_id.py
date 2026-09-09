from pathlib import Path
p=Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs')
t=p.read_text(encoding='utf-8')
old='CheckParked(81210440, "Magical Hats");'
new='CheckParked(81210420, "Magical Hats");'
if old not in t: raise SystemExit('not found')
p.write_text(t.replace(old,new,1), encoding='utf-8')
print('OK Magical Hats id')
