import json
from pathlib import Path
db=json.loads(Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/StreamingAssets/Cards/cards_db.json').read_text())
for c in db['cards']:
    n=(c.get('name') or '').lower()
    if 'hat' in n or 'multiply' in n or 'crush card' in n or 'time wizard' in n:
        print(c['id'], c['name'])
