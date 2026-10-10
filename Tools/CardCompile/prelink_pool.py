#!/usr/bin/env python3
"""Build the pre-Link monster catalog the headless compiler reads.

Pool: YGOPRODeck v7 monsters whose tcg_date or ocg_date is on or before
2017-05-05 (Maximum Crisis). Link, Token, Skill, and Strategy cards are
out. A passcode is kept only when cards.cdb has that id as a TCG or OCG
monster. Official desc comes from the API. Pendulum scales come from the
cdb level word. The raw dump stays in Tools/CardCompile/cache/.

    python3 Tools/CardCompile/prelink_pool.py
    python3 Tools/CardCompile/prelink_pool.py --refresh
"""
from __future__ import annotations

import argparse
import json
import sqlite3
import urllib.request
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CDB = ROOT / "Assets/StreamingAssets/OcgCore/cards.cdb"
OUT = ROOT / "Assets/StreamingAssets/WRLDZ/eras/prelink_monsters.json"
CACHE = Path(__file__).resolve().parent / "cache" / "ygoprodeck_cardinfo.json"
API = "https://db.ygoprodeck.com/api/v7/cardinfo.php?misc=yes"
USER_AGENT = "WRLDZ-ARGON-prelink-pool/1.0 (local cache; no hotlink)"
WALL = "2017-05-05"

TYPE_MONSTER = 0x1
TYPE_TOKEN = 0x4000
TYPE_LINK = 0x4000000
TYPE_PENDULUM = 0x1000000
OT_TCG_OR_OCG = 0x3

# Pins the headless run repeats. Dark Magician is in. Decode Talker is Link.
# Nibiru's earliest TCG/OCG date is 2019.
PIN_IN = 46986414
PIN_LINK = 1861629
PIN_AFTER = 27204311


def download(dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(".json.part")
    req = urllib.request.Request(API, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=600) as resp:
        tmp.write_bytes(resp.read())
    tmp.replace(dest)


def load_api(refresh: bool) -> list:
    if refresh or not CACHE.is_file():
        print(f"downloading {API}", flush=True)
        download(CACHE)
    else:
        print(f"cache {CACHE}", flush=True)
    data = json.loads(CACHE.read_text(encoding="utf-8"))
    if isinstance(data, dict):
        data = data.get("data")
    if not isinstance(data, list) or not data:
        raise SystemExit("cardinfo cache has no data array")
    return data


def load_cdb() -> dict[int, tuple[int, int, int, int]]:
    """id -> (ot, type, level, alias). alias 0 is the card's own passcode."""
    con = sqlite3.connect(f"file:{CDB}?mode=ro", uri=True)
    rows = {}
    for cid, ot, typ, level, alias in con.execute(
        "SELECT id, ot, type, level, alias FROM datas"
    ):
        rows[int(cid)] = (int(ot or 0), int(typ or 0), int(level or 0), int(alias or 0))
    con.close()
    return rows


def canonical_id(raw: dict, cdb: dict[int, tuple[int, int, int, int]]) -> int | None:
    """Passcode of this API object.

    Alternate artworks share one object whose id may be an alias row. The
    image list then contains the alias-zero passcode. A different card that
    is only *treated as* another (Harpie Lady 1) keeps its own id: none of
    its image ids have alias 0.
    """
    ids = [int(raw["id"])]
    for image in raw.get("card_images") or []:
        if isinstance(image, dict) and image.get("id") is not None:
            ids.append(int(image["id"]))
    zeros: list[int] = []
    seen: set[int] = set()
    for cid in ids:
        if cid in seen:
            continue
        seen.add(cid)
        row = cdb.get(cid)
        if row is not None and row[3] == 0:
            zeros.append(cid)
    if len(zeros) == 1:
        return zeros[0]
    api_id = int(raw["id"])
    if api_id in cdb:
        return api_id
    return None


def dates_of(raw: dict) -> tuple[str, str]:
    tcg: list[str] = []
    ocg: list[str] = []
    for misc in raw.get("misc_info") or []:
        if not isinstance(misc, dict):
            continue
        if misc.get("tcg_date"):
            tcg.append(str(misc["tcg_date"]))
        if misc.get("ocg_date"):
            ocg.append(str(misc["ocg_date"]))
    return (min(tcg) if tcg else ""), (min(ocg) if ocg else "")


def is_link(raw: dict, cdb_type: int | None) -> bool:
    typ = (raw.get("type") or "").lower()
    frame = (raw.get("frameType") or "").lower()
    if "link" in typ or "link" in frame:
        return True
    return cdb_type is not None and (cdb_type & TYPE_LINK) != 0


def slim(raw: dict, scale_left: int, scale_right: int) -> dict:
    atk = raw.get("atk")
    defense = raw.get("def")
    level = raw.get("level")
    desc = raw.get("desc") or ""
    desc = desc.replace("\r\n", "\n").replace("\r", "\n")
    return {
        "id": int(raw["id"]),
        "name": raw.get("name") or "",
        "type": raw.get("type") or "",
        "frameType": raw.get("frameType") or "",
        "desc": desc,
        "atk": int(atk) if atk is not None else -1,
        "def": int(defense) if defense is not None else -1,
        "level": int(level) if level is not None else 0,
        "race": raw.get("race") or "",
        "attribute": raw.get("attribute") or "",
        "archetype": raw.get("archetype") or "",
        "scaleLeft": scale_left,
        "scaleRight": scale_right,
    }


def scales_of(type_bits: int, level_word: int) -> tuple[int, int]:
    if (type_bits & TYPE_PENDULUM) == 0:
        return 0, 0
    return (level_word >> 24) & 0xFF, (level_word >> 16) & 0xFF


def write_meta(json_path: Path) -> None:
    meta = json_path.with_name(json_path.name + ".meta")
    if meta.exists():
        return
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {uuid.uuid4().hex}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n",
        encoding="utf-8",
    )


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--refresh", action="store_true", help="re-download the API dump")
    args = ap.parse_args()

    api = load_api(args.refresh)
    cdb = load_cdb()
    kept: list[dict] = []
    seen: set[int] = set()
    excluded_link = 0
    excluded_after = 0
    excluded_undated: list[int] = []
    excluded_missing = 0
    excluded_token = 0

    for raw in api:
        if not isinstance(raw, dict):
            continue
        typ = (raw.get("type") or "").lower()
        if "monster" not in typ:
            continue
        if "token" in typ or "skill" in typ or "strategy" in typ:
            excluded_token += 1
            continue
        cid = canonical_id(raw, cdb)
        if cid is not None:
            if cid in seen:
                continue
            seen.add(cid)
        row = cdb.get(cid) if cid is not None else None
        cdb_type = row[1] if row else None
        if is_link(raw, cdb_type):
            excluded_link += 1
            continue
        tcg, ocg = dates_of(raw)
        if not tcg and not ocg:
            excluded_undated.append(cid if cid is not None else int(raw["id"]))
            continue
        if not ((tcg and tcg <= WALL) or (ocg and ocg <= WALL)):
            excluded_after += 1
            continue
        if (
            row is None
            or (row[0] & OT_TCG_OR_OCG) == 0
            or (row[1] & TYPE_MONSTER) == 0
            or (row[1] & TYPE_TOKEN) != 0
            or (row[1] & TYPE_LINK) != 0
        ):
            excluded_missing += 1
            continue
        left, right = scales_of(row[1], row[2])
        card = slim(raw, left, right)
        card["id"] = cid
        kept.append(card)

    kept.sort(key=lambda c: c["id"])
    ids = {c["id"] for c in kept}
    if PIN_IN not in ids:
        raise SystemExit(f"pin failed: {PIN_IN} Dark Magician not in the pool")
    if PIN_LINK in ids:
        raise SystemExit(f"pin failed: Link monster {PIN_LINK} is in the pool")
    if PIN_AFTER in ids:
        raise SystemExit(f"pin failed: post-wall monster {PIN_AFTER} is in the pool")

    undated_ids = sorted(set(excluded_undated))
    payload = {
        "wall": WALL,
        "source": "ygoprodeck-v7",
        "excludedLink": excluded_link,
        "excludedAfterWall": excluded_after,
        "excludedUndated": len(excluded_undated),
        "excludedUndatedIds": undated_ids,
        "excludedMissingCdb": excluded_missing,
        "excludedToken": excluded_token,
        "cards": kept,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    write_meta(OUT)

    print(f"kept={len(kept)}")
    print(f"excludedLink={excluded_link}")
    print(f"excludedAfterWall={excluded_after}")
    print(f"excludedUndated={len(excluded_undated)}")
    print(f"excludedMissingCdb={excluded_missing}")
    print(f"excludedToken={excluded_token}")
    print(f"wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
