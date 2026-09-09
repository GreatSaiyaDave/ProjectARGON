import json
from pathlib import Path
db=json.loads(Path("Assets/StreamingAssets/Cards/cards_db.json").read_text())
idx={int(c["id"]):c for c in db["cards"]}
for i in [76812113,24317029,47355498]:
    c=idx[i]
    print(i, c["name"], "arch=", c.get("archetype"), "race=", c.get("race"), "atk", c.get("atk"), "def", c.get("def"))
