"""Does every canon look row's referenced asset actually exist?

A look row names a hair, a face decal and six clothing ids. GraftCloth / ApplyFace resolve them by
name at runtime; a miss is silent:
  * a missing cloth mesh falls back to that slot's DEFAULT generic shell (a random generic tee /
    trouser / shoe wherever a character has no piece of their own) - "bits of clothing all over the
    model";
  * a missing face decal leaves the base face blank (no albedo ships for it) - "the face is
    unreadable";
  * a missing hair mesh leaves the built-in fallback hairstyle or baldness - "the hair isn't placed
    right".

Run: python3 Tools/anime/look_assets_audit.py     (repo root)
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
AV = os.path.join(ROOT, "Assets", "Resources", "WRLDZ", "Avatar3D")
CLOTHES, FACES, HAIR = f"{AV}/Clothes", f"{AV}/Faces", f"{AV}/Hair"
LOOKS = os.path.join(ROOT, "Assets", "Scripts", "WRLDZ", "Data", "CharacterLooks.cs")

FIELD = {"hair": 4, "face": 5, "shirt": 8, "bottoms": 9, "shoes": 10, "hands": 11}
NAMED = ("facial", "hat", "hairHex", "skin", "hairPattern")


def rows():
    """L(id, name, match, body, hair, face, height, weight, shirt, bottoms, shoes, hands, facial:, hat:, ...)"""
    src = open(LOOKS, encoding="utf8").read()
    out = []
    for m in re.finditer(r'\bL\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*(\d)', src):
        start = m.start()
        # the call ends at the matching close paren of "L("
        depth, i = 0, start + 1
        while i < len(src):
            if src[i] == "(":
                depth += 1
            elif src[i] == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        args = split_args(src[start + 2:i])
        if len(args) < 12:
            continue
        rec = {"id": m.group(1), "name": m.group(2), "body": m.group(4)}
        for field, idx in FIELD.items():
            v = args[idx].strip()
            if field in ("shirt", "bottoms", "shoes", "hands") and v in ("null", ""):
                continue          # a character with no hands / facial piece is fine
            if field in ("hat", "facial"):
                continue
            if v not in ("null", ""):
                rec[field] = v.strip('"')
        for extra in ("facial", "hat"):
            mm = re.search(extra + r'\s*:\s*"([^"]+)"', src[start:i])
            if mm:
                rec[extra] = mm.group(1)
        out.append(rec)
    return out


def split_args(s):
    out, depth, cur, q = [], 0, "", False
    for ch in s:
        if q:
            cur += ch
            if ch == '"':
                q = False
            continue
        if ch == '"':
            q = True
            cur += ch
        elif ch in "([{":
            depth += 1
            cur += ch
        elif ch in ")]}":
            depth -= 1
            cur += ch
        elif ch == "," and depth == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    out.append(cur)
    return out


def exists(p):
    return os.path.exists(p)


def main():
    bad = []
    pretty = []
    for r in rows():
        sex = "female" if r["body"] == "1" else "male"
        miss = []
        # clothing ids -> Clothes/argon_<id>_<sex>.fbx
        for slot in ("shirt", "bottoms", "shoes", "hands", "facial", "hat"):
            v = r.get(slot)
            if not v:
                continue
            if exists(f"{CLOTHES}/argon_{v}_{sex}.fbx"):
                continue
            if exists(f"{CLOTHES}/argon_{v}_male.fbx") or exists(f"{CLOTHES}/argon_{v}_female.fbx"):
                continue
            miss.append(f"{slot}={v} (no FBX in either sex)")
        # hair -> Hair/argon_hair_<id>_<sex>.fbx
        h = r.get("hair")
        if h and h != "none" and not exists(f"{HAIR}/argon_hair_{h}_{sex}.fbx"):
            miss.append(f"hair={h} (no FBX)")
        # face decal -> Faces/<id>.png
        f = r.get("face")
        if f and not exists(f"{FACES}/{f}.png"):
            miss.append(f"face={f} (no decal png)")
        pretty.append((r["id"], r["name"], miss))
        if miss:
            bad.append((r["id"], miss))

    print("%-16s %-28s %s" % ("look", "name", "missing assets"))
    print("-" * 78)
    for rid, name, miss in pretty:
        print("%-16s %-28s %s" % (rid, name, "OK" if not miss else "; ".join(miss)))
    print()
    print("look rows: %d   rows with a missing asset: %d" % (len(pretty), len(bad)))
    for rid, miss in bad:
        print("  %-16s %s" % (rid, "; ".join(miss)))
    # the orphan direction: a 0.56-aspect screencap is not a decal, it is unprocessed source art
    print()
    for fn in sorted(os.listdir(FACES)):
        if not fn.endswith(".png") or fn.endswith((".meta",)):
            continue
        p = os.path.join(FACES, fn)
        if fn.endswith(("_mouth.png", "_body.png", "_forehead.png")):
            continue
        try:
            with open(p, "rb") as fh:
                head = fh.read(64)
            # IHDR width/height
            w = int.from_bytes(head[16:20], "big")
            h = int.from_bytes(head[20:24], "big")
        except Exception:
            continue
        used = any(rid and r.get("face") == fn[:-4] for rid, name, miss in [] for r in [])  # noqa
        if abs(w / max(h, 1) - 1.9) > 0.15:
            print("decals with an odd aspect (a raw screencap, not a calibrated eyes patch): %s %dx%d" % (fn, w, h))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
