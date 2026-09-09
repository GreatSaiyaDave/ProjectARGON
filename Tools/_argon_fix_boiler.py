from pathlib import Path
p=Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs')
t=p.read_text(encoding='utf-8')
idx=t.find('// Rite of Spirit')
print('idx', idx)
print(repr(t[idx:idx+280]))
# Replace mangled block with clean C#
import re
pat=re.compile(r'            // Rite of Spirit / similar: Necrovalley interaction reminder, not a separate effect\.\n            if \(f\.Contains\([^\n]+\)(?: \|\|\n                f\.Contains\([^\n]+\))? return true;\n')
m=pat.search(t)
print('match', bool(m))
if m:
    print('matched repr', repr(m.group(0)))
    clean=('            // Rite of Spirit / similar: Necrovalley interaction reminder, not a separate effect.\n'
           '            if (f.Contains("unaffected by \\"necrovalley\\"")) return true;\n')
    t2=pat.sub(clean, t, count=1)
    p.write_text(t2, encoding='utf-8')
    idx=t2.find('// Rite of Spirit')
    print('fixed', repr(t2[idx:idx+180]))
