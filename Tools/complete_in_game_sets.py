#!/usr/bin/env python3
"""Complete in-game TCG booster sets in cards_db.json + CardArt.

In-game constructed sets (SetOrbService):
  Starter: LOB, MRD, SRL
  Unlock ladder: PSV → LON → LOD → PGD → MFC → DCR → IOC → AST → SOD → RDS → FET → TLM

Source: YGOPRODeck API v7 (same pipeline as CardArt). Does not hotlink at runtime.
Does not include CRV or later.
"""
from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CARDS_DB = ROOT / "Assets" / "StreamingAssets" / "Cards" / "cards_db.json"
CARD_ART = ROOT / "Assets" / "StreamingAssets" / "CardArt"
ERA_FILE = ROOT / "Assets" / "StreamingAssets" / "WRLDZ" / "eras" / "pre_link_sets.json"
SET_CARDS = ROOT / "Assets" / "StreamingAssets" / "WRLDZ" / "eras" / "in_game_set_cards.json"

API = "https://db.ygoprodeck.com/api/v7"
USER_AGENT = "WRLDZ-ARGON-complete-sets/1.0 (local cache; no hotlink)"
REQUEST_SLEEP = 0.25
IMAGE_SLEEP = 0.08

# Exact YGOPRODeck set names (not 25th Anniversary / Sneak Peek / Special Edition).
IN_GAME_SETS: list[tuple[str, str, int, str]] = [
    ("LOB", "Legend of Blue Eyes White Dragon", 1, "foundation"),
    ("MRD", "Metal Raiders", 2, "next_set_1"),
    ("SRL", "Spell Ruler", 3, "next_set_2"),
    ("PSV", "Pharaoh's Servant", 4, "unlock"),
    ("LON", "Labyrinth of Nightmare", 5, "unlock"),
    ("LOD", "Legacy of Darkness", 6, "unlock"),
    ("PGD", "Pharaonic Guardian", 7, "unlock"),
    ("MFC", "Magician's Force", 8, "unlock"),
    ("DCR", "Dark Crisis", 9, "unlock"),
    ("IOC", "Invasion of Chaos", 10, "unlock"),
    ("AST", "Ancient Sanctuary", 11, "unlock"),
    ("SOD", "Soul of the Duelist", 12, "unlock"),
    ("RDS", "Rise of Destiny", 13, "unlock"),
    ("FET", "Flaming Eternity", 14, "unlock"),
    ("TLM", "The Lost Millennium", 15, "unlock"),
]


def http_get_json(url: str, params: dict[str, str] | None = None):
    if params:
        url = f"{url}?{urllib.parse.urlencode(params)}"
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", errors="replace")
        raise SystemExit(f"HTTP {e.code} {url}\n{detail}") from e


def http_download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=60) as resp:
        dest.write_bytes(resp.read())


def slim_card(raw: dict) -> dict:
    atk = raw.get("atk")
    defense = raw.get("def")
    level = raw.get("level")
    return {
        "id": int(raw["id"]),
        "name": raw.get("name") or "",
        "type": raw.get("type") or "",
        "frameType": raw.get("frameType") or "",
        "desc": raw.get("desc") or "",
        "atk": int(atk) if atk is not None else -1,
        "def": int(defense) if defense is not None else -1,
        "level": int(level) if level is not None else 0,
        "race": raw.get("race") or "",
        "attribute": raw.get("attribute") or "",
        "archetype": raw.get("archetype") or "",
    }


def skip_card(raw: dict) -> bool:
    t = (raw.get("type") or "").lower()
    if "skill" in t or "strategy" in t:
        return True
    return False


def small_image_url(raw: dict) -> str | None:
    images = raw.get("card_images") or []
    if not images:
        return None
    entry = images[0]
    return entry.get("image_url_small") or entry.get("image_url")


def write_jpg_meta(jpg: Path) -> None:
    meta = jpg.with_suffix(".jpg.meta")
    if meta.exists():
        return
    guid = uuid.uuid4().hex
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def fetch_set(name: str) -> list[dict]:
    print(f"  Fetch {name!r} …", flush=True)
    payload = http_get_json(f"{API}/cardinfo.php", {"cardset": name})
    batch = payload.get("data") or []
    time.sleep(REQUEST_SLEEP)
    print(f"    {len(batch)} cards")
    return batch


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--no-images", action="store_true")
    args = ap.parse_args()

    db = json.loads(CARDS_DB.read_text())
    existing = {int(c["id"]): c for c in db.get("cards") or []}
    print(f"cards_db: {len(existing)}")

    by_set: dict[str, list[dict]] = {}
    union: dict[int, dict] = {}
    for code, name, _order, _role in IN_GAME_SETS:
        batch = [c for c in fetch_set(name) if not skip_card(c)]
        by_set[code] = batch
        for c in batch:
            union[int(c["id"])] = c

    print("\n== coverage ==")
    missing_raw: dict[int, dict] = {}
    set_report = []
    for code, name, order, role in IN_GAME_SETS:
        ids = sorted({int(c["id"]) for c in by_set[code]})
        have = [i for i in ids if i in existing]
        miss = [i for i in ids if i not in existing]
        for i in miss:
            missing_raw[i] = union[i]
        line = f"{code:4} {len(have):4}/{len(ids):<4} missing {len(miss):4}  {name}"
        print(line)
        set_report.append(
            {
                "code": code,
                "name": name,
                "order": order,
                "role": role,
                "apiCount": len(ids),
                "inDatabase": len(have),
                "missing": len(miss),
                "passcodes": ids,
            }
        )

    print(f"\nunique cards across in-game sets: {len(union)}")
    print(f"already in cards_db: {sum(1 for i in union if i in existing)}")
    print(f"to add: {len(missing_raw)}")

    catalog = {
        "version": 1,
        "source": "YGOPRODeck cardinfo.php?cardset= (original booster names, not 25th Anniversary)",
        "preLinkWall": "Cybernetic Revolution / CRV — not included",
        "sets": [
            {
                "code": r["code"],
                "name": r["name"],
                "order": r["order"],
                "role": r["role"],
                "passcodes": r["passcodes"],
            }
            for r in set_report
        ],
    }

    if args.dry_run:
        print("dry-run: not writing cards_db / art / catalogs")
        names = [missing_raw[i]["name"] for i in sorted(missing_raw)]
        print("sample missing:", ", ".join(names[:20]), "…" if len(names) > 20 else "")
        return 0

    added = []
    for cid, raw in sorted(missing_raw.items()):
        existing[cid] = slim_card(raw)
        added.append(cid)

    slim = [existing[i] for i in sorted(existing)]
    CARDS_DB.write_text(json.dumps({"cards": slim}, separators=(",", ":")) + "\n")
    print(f"wrote {CARDS_DB}  ({len(slim)} cards, +{len(added)})")

    SET_CARDS.parent.mkdir(parents=True, exist_ok=True)
    SET_CARDS.write_text(json.dumps(catalog, indent=2) + "\n")
    print(f"wrote {SET_CARDS}")

    # Expand pre_link_sets.json to the full in-game ladder with complete passcodes.
    era = {
        "version": 2,
        "preLinkWall": "Code of the Duelist / Link era — not required for v1. Constructed wall is TLM.",
        "sets": [
            {
                "code": r["code"],
                "name": r["name"],
                "order": r["order"],
                "role": r["role"],
                "notes": f"Complete YGOPRODeck booster list ({r['apiCount']} unique passcodes).",
                "priorityPasscodes": r["passcodes"],
            }
            for r in set_report
        ],
    }
    ERA_FILE.write_text(json.dumps(era, indent=2) + "\n")
    print(f"wrote {ERA_FILE}")

    if args.no_images:
        return 0

    need_art = []
    for cid, raw in union.items():
        dest = CARD_ART / f"{cid}.jpg"
        if dest.is_file() and dest.stat().st_size > 0:
            continue
        url = small_image_url(raw)
        if not url:
            print(f"  no image URL for {cid} {raw.get('name')}")
            continue
        need_art.append((cid, url, dest, raw.get("name")))

    print(f"images to download: {len(need_art)}")
    ok = fail = 0
    for i, (cid, url, dest, name) in enumerate(need_art, 1):
        try:
            http_download(url, dest)
            write_jpg_meta(dest)
            ok += 1
            if i % 25 == 0 or i == len(need_art):
                print(f"  art {i}/{len(need_art)}")
        except Exception as e:
            fail += 1
            print(f"  FAIL {cid} {name}: {e}")
        time.sleep(IMAGE_SLEEP)
    print(f"art downloaded={ok} failed={fail}")
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
