import json, re
from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')
db=json.loads((root/'Assets/StreamingAssets/Cards/cards_db.json').read_text())
idx={int(c['id']):c for c in db['cards']}

rx_quoted = re.compile(r'All "([^"]+)" monsters(?: on the field)? gain (\d+) ATK(?:/DEF| and DEF)\.?', re.I)
rx_race = re.compile(r'All ([A-Za-z]+(?:[ -][A-Za-z]+)*)(?:-Type)? monsters(?: on the field)? gain (\d+) ATK(?:/DEF| and DEF)\.?', re.I)
rx_banish = re.compile(r'(?:Cards in the (?:GY|Graveyard)s? cannot be banished|Neither player can banish (?:cards? )?from (?:the )?(?:GYs?|Graveyards?))\.?', re.I)
rx_target = re.compile(r'Cards in the (?:GY|Graveyard)s? cannot be targeted(?:, except by (?:the effect of )?"([^"]+)")?\.?', re.I)
rx_hhg = re.compile(
    r'If any "([^"]+)" or "([^"]+)" is Normal or Special Summoned:\s*'
    r'(?:The player who conducted (?:the|that) Summon targets 1 Spell/?Trap on the field;\s*that player destroys that target|'
    r'Target 1 Spell/?Trap on the field;\s*destroy (?:that target|it))\.?', re.I)
rx_tornado_act = re.compile(r'Activate only while "([^"]+)" is(?: face-up)? on the field\.?', re.I)
rx_tornado_bd = re.compile(r'While "([^"]+)" is face-up on the field, you take no Battle Damage(?: from attacking monsters)?\.?', re.I)
rx_tornado_leave = re.compile(r'Destroy this card when "([^"]+)" leaves the field\.?', re.I)
rx_equip = re.compile(r'Equip only to (?:an? )?([A-Za-z]+(?:-(?!Type)[A-Za-z]+)*)(?:-Type)? monster\.?\s*It gains (\d+) ATK(?:/DEF| and DEF)?\.?', re.I)
rx_equip_or = re.compile(r'Equip only to "([^"]+)" or "([^"]+)"\.?\s*It gains (\d+) ATK(?:/DEF| and DEF)?\.?', re.I)
rx_una = re.compile(r'This card\'s activation and effect are unaffected by "([^"]+)"\.?', re.I)
rx_book = re.compile(r'Target 1 Zombie monster in your GY and 1 monster in your opponent\'s GY', re.I)

def leftover(desc, spans):
    chars=list(desc)
    for i,l in spans:
        for k in range(i, min(i+l, len(chars))):
            chars[k]=' '
    rem=re.sub(r'\s+',' ',' '.join(''.join(chars).split())).strip()
    # sentences
    parts=[p.strip() for p in re.split(r'(?<=[\.\!\?])\s+', rem) if p.strip() and len(p.strip())>2]
    boiler=[]
    keep=[]
    for p in parts:
        f=p.lower()
        if 'unaffected by "necrovalley"' in f or "unaffected by 'necrovalley'" in f:
            boiler.append(p)
        elif len(f)<8:
            boiler.append(p)
        else:
            keep.append(p)
    return keep

ids=[47355498,75782277,18605135,2204140,30450531,63224564,32268901,77007920,25769732,51267887,22702055]
for i in ids:
    c=idx[i]
    d=c['desc']
    print('='*60)
    print(i, c['name'])
    print(d)
    spans=[]
    hits=[]
    for name,rx in [('quoted',rx_quoted),('race',rx_race),('banish',rx_banish),('gytarget',rx_target),
                    ('hhg',rx_hhg),('t_act',rx_tornado_act),('t_bd',rx_tornado_bd),('t_leave',rx_tornado_leave),
                    ('equip',rx_equip),('equip_or',rx_equip_or),('una',rx_una),('book_dual',rx_book)]:
        m=rx.search(d)
        if m:
            hits.append((name, m.groups(), m.group(0)[:80]))
            spans.append((m.start(), m.end()-m.start()))
    print('HITS', hits)
    keep=leftover(d, spans)
    print('LEFTOVER', keep if keep else '<empty> => FullyCompiled if clauses exist')
