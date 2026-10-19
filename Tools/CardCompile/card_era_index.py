#!/usr/bin/env python3
"""Era index for cards outside the 15 curriculum sets (status set "-").

The curriculum file (Assets/StreamingAssets/WRLDZ/eras/in_game_set_cards.json) names a set only for LOB..TLM, so the
~4,700 cards added to cards_db on 2026-10-13 have no set code and no order. This tool reads the local YGOPRODeck dump
that prelink_pool.py keeps (Tools/CardCompile/cache/ygoprodeck_cardinfo.json; no network) and writes
Tools/CardCompile/card_era_index.json:

    { "<id>": { "date": "YYYY-MM-DD", "set": "CRV", "era": "gx" }, ... }

- date: the card's first TCG date, else its OCG date.
- set:  the earliest core booster of its era that prints it (the CORE list below, in release order), else the set-code
        prefix of its first listed printing.
- era:  the eraz_eras.json band whose window holds the date.
worklist.py reads it, so `--sets CRV,EEN` selects those cards and lists them in release order.

usage: python3 Tools/CardCompile/card_era_index.py [--check]
"""
import argparse
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CACHE = os.path.join(ROOT, "Tools/CardCompile/cache/ygoprodeck_cardinfo.json")
ERAS = os.path.join(ROOT, "Assets/StreamingAssets/WRLDZ/eras/eraz_eras.json")
OUT = os.path.join(ROOT, "Tools/CardCompile/card_era_index.json")

# Core boosters in TCG release order, era by era (the curriculum continues past TLM in this order).
CORE = (
    "LOB MRD SRL PSV LON LOD PGD MFC DCR IOC AST SOD RDS FET "
    "TLM CRV EEN SOI EOJ POTD CDIP STON FOTB TAEV GLAS PTDN LODT "
    "TDGS CSOC CRMS RGBT ANPR SOVR ABPF TSHD DREV STBL STOR EXVC GENF PHSW ORCS GAOV REDU ABYR CBLZ LTGY JOTL "
    "SHSP LVAL PRIO DUEA NECH SECE CROS CORE DOCS BOSH SHVI TDIL INOV RATE MACR COTD CIBR EXFO FLOD CYHO SOFU SAST "
    "DANE RIRA CHIM IGAS ETCO ROTD PHRA BLVO LIOV DAMA BODE"
).split()
ORDER = {code: i for i, code in enumerate(CORE)}


def first_date(card):
    misc = (card.get("misc_info") or [{}])[0]
    return misc.get("tcg_date") or misc.get("ocg_date") or ""


def set_code(card):
    prefixes = []
    for s in card.get("card_sets") or []:
        code = (s.get("set_code") or "").split("-", 1)[0]
        if code and code not in prefixes:
            prefixes.append(code)
    cores = sorted((p for p in prefixes if p in ORDER), key=ORDER.get)
    if cores:
        return cores[0]
    return prefixes[0] if prefixes else "-"


def era_of(date, bands):
    for b in bands:  # bands are ordered; the first whose last core date is on or after the card's date wins
        if date and date <= b.get("lastCoreDate", "9999"):
            return b["id"]
    return bands[-1]["id"] if bands else "-"


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="print a summary, write nothing")
    a = ap.parse_args()
    with open(CACHE, encoding="utf-8") as f:
        dump = json.load(f)
    cards = dump.get("data", dump)
    with open(ERAS, encoding="utf-8") as f:
        bands = sorted(json.load(f)["bands"], key=lambda b: b.get("order", 0))
    index = {}
    for c in cards:
        date = first_date(c)
        index[str(c["id"])] = {"date": date, "set": set_code(c), "era": era_of(date, bands)}
    by_era = {}
    for v in index.values():
        by_era[v["era"]] = by_era.get(v["era"], 0) + 1
    print(f"card_era_index: {len(index)} cards; by era {dict(sorted(by_era.items()))}")
    if not a.check:
        with open(OUT, "w", encoding="utf-8") as f:
            json.dump(index, f, sort_keys=True, separators=(",", ":"))
        print(f"wrote {os.path.relpath(OUT, ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
