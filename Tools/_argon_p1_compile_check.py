#!/usr/bin/env python3
"""Lightweight FullyCompiled estimate for Deck-Grok P1 cards (key templates only)."""
import json,re
from pathlib import Path
root=Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')
db=json.loads((root/'Assets/StreamingAssets/Cards/cards_db.json').read_text())
idx={int(c['id']):c for c in db['cards']}

def N(s):
    s=s.replace('\r',' ').replace('\n',' ')
    return re.sub(r'\s+',' ',s).strip()

ATTR=set('DARK LIGHT EARTH WATER FIRE WIND DIVINE'.split())

# Core recruiter / searcher / cheap templates
RX=[]
RX.append(('battle_filter_ss', re.compile(
    r'(?:If|When) this card is destroyed by battle and sent to the (?:GY|Graveyard):\s*'
    r'(?:You can )?Special Summon 1 (\w+)(?:-Type)? monster with (\d+) or less (ATK|DEF) from your Deck'
    r'(?:, in (?:face-up )?Attack Position)?\.?', re.I)))
RX.append(('battle_add_named', re.compile(
    r'(?:If|When) this card is destroyed by battle and sent to the (?:GY|Graveyard):\s*'
    r'(?:You can )?add 1 "([^"]+)" from your Deck to your hand\.?', re.I)))
RX.append(('flip_ss_series', re.compile(
    r'FLIP:\s*(?:You can )?Special Summon 1 "([^"]+)" monster with (\d+) or less ATK from your Deck\.?', re.I)))
RX.append(('rota', re.compile(
    r'Add 1 Level (\d+) or lower (\w+)(?:-Type)? monster from your Deck to your hand\.?', re.I)))
RX.append(('add_named', re.compile(
    r'Add 1 "([^"]+)"(?: card)? from your Deck to your hand(?:\. Your Deck is then shuffled)?\.?', re.I)))
RX.append(('thunder', re.compile(
    r'(?:You can )?discard this card;\s*add up to (\d+) "([^"]+)" from your Deck to your hand\.?', re.I)))
RX.append(('chick', re.compile(
    r'(?:You can )?send this (?:face-up )?card (?:you control )?to the (?:GY|Graveyard);\s*'
    r'Special Summon 1 "([^"]+)" from your hand\.?', re.I)))
RX.append(('book_moon', re.compile(
    r'Target 1 face-up monster on the field;\s*change that target to face-down Defense Position\.?', re.I)))
RX.append(('torrential', re.compile(
    r'When a monster\(s\) is Summoned:\s*Destroy all monsters on the field\.?', re.I)))
RX.append(('call_haunt', re.compile(
    r'Activate this card by targeting 1 monster in your (?:GY|Graveyard);\s*'
    r'Special Summon that target in Attack Position\.\s*'
    r'When this card leaves the field, destroy that monster\.\s*'
    r'When that monster is destroyed, destroy this card\.?', re.I)))
RX.append(('toon_world', re.compile(
    r'Activate this card by paying (\d+) (?:LP|Life Points)\.?\s*$', re.I)))
RX.append(('snatch', re.compile(
    r'Equip only to a monster your opponent controls\.\s*Take control of the equipped monster\.?', re.I)))
RX.append(('rite', re.compile(
    r'Target 1 "([^"]+)" monster in your (?:GY|Graveyard);\s*Special Summon that target\.?', re.I)))
RX.append(('winged_aura', re.compile(
    r'All ([\w]+(?:\s+[\w]+)?) monsters(?: on the field)? gain (\d+) ATK/DEF\.?', re.I)))
RX.append(('series_aura', re.compile(
    r'All "([^"]+)" monsters(?: on the field)? gain (\d+) ATK(?: and DEF|/DEF)?\.?', re.I)))
RX.append(('book_life', re.compile(
    r'Target 1 (\w+) monster in your (?:GY|Graveyard) and 1 monster in your opponent\'s (?:GY|Graveyard);\s*'
    r'Special Summon the first target, also banish the second target\.?', re.I)))
RX.append(('pinch', re.compile(
    r'When this card you control is sent to your (?:GY|Graveyard):\s*'
    r'(?:You can )?Special Summon 1 (\w+) monster from your hand\.?', re.I)))
RX.append(('mustering', re.compile(
    r'If you control a face-up "([^"]+)":\s*(?:You can )?Special Summon any number of "([^"]+)" monsters from your hand', re.I)))
RX.append(('snatch_lp', re.compile(
    r'During each of your opponent\'s Standby Phases:\s*They gain (\d+) Life Points\.?', re.I)))
RX.append(('necro_rest', re.compile(
    r'Cards in the Graveyard cannot be banished|Negate any card effect that would move a card in the Graveyard', re.I)))
RX.append(('hunt_trigger', re.compile(
    r'If any "Harpie Lady".*Normal or Special Summoned', re.I)))
RX.append(('rite_necro', re.compile(
    r'This card\'s activation and effect are unaffected by "Necrovalley"\.?', re.I)))

ids=[
83011278,97017120,95956346,60806437,57839750,77044671,93107608,39191307,84834865,
32807846,31786629,24317029,45547649,89997728,36262024,26185991,68191243,
14087893,53582587,2204140,30450531,47355498,75782277,15259703,45986603
]
for i in ids:
    c=idx[i]; text=N(c['desc']); name=c['name']
    hits=[n for n,rx in RX if rx.search(text)]
    # rough coverage: mask matched spans
    spans=[]
    for n,rx in RX:
        m=rx.search(text)
        if m: spans.append((m.start(), m.end()-m.start()))
    chars=list(text)
    for s,l in spans:
        for j in range(s,s+l):
            if j<len(chars): chars[j]=' '
    rem=re.sub(r'\s+',' ',''.join(chars)).strip(' .')
    rem=re.sub(r'\s*\.\s*','. ',rem).strip(' .')
    print(f'{i} {name}')
    print(f'  hits={hits}')
    print(f'  rem={rem!r}' if rem else '  rem=<empty> => likely FullyCompiled if kinds exist')
