import json
from pathlib import Path
db=json.loads(Path("Assets/StreamingAssets/Cards/cards_db.json").read_text())
idx={int(c["id"]):c for c in db["cards"]}
ids=[47355498,75782277,18605135,2204140,63224564,32268901,77007920,25769732,51267887,83764719,83764718,30450531]
for i in ids:
    c=idx.get(i)
    if not c:
        print("MISSING", i)
        continue
    print("="*60)
    print(i, c.get("name"), "type=", c.get("type"), "atk", c.get("atk"), "def", c.get("def"))
    print(c.get("desc",""))
