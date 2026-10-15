#!/usr/bin/env python3
"""Scan the local anime for character BACK views — the missing input for Meshy multi-image-to-3D.

The Duelist Kingdom sprite sets came from GravekeepersBrewer's DA gallery, which is front-facing only,
so every set carries the same front render in all 12 slots and a 3D reconstruction has no rear
information to work from. The episodes have it.

The scan is CHARACTER-INDEPENDENT: it walks each episode once, cuts every person-shaped shot out and
caches the cutout, its geometry and its CLIP embedding. Scoring a character afterwards is instant, so
one pass over Season 1 serves all sixteen.

  analyse (default)  one frame per shot (ffmpeg scene detect) -> isnet-anime cutout -> geometry + skin
                     + CLIP embedding, cached in <out>/<episode>/. ~2 min per episode, never repeated.
  --select           re-read the caches, score against a character's references, apply the gates, rank
                     and write the contact sheet. Instant, so thresholds can be tuned freely.

    python3 scan_anime_backviews.py --episodes dm_s1e001,dm_s1e003 --out DIR          # one pass
    python3 scan_anime_backviews.py --character yugi --out DIR --select               # any character

Signals per frame (index.json; the sheet prints the first three):
  sim     cosine between the frame's CLIP embedding and the closest reference of the TARGET
  margin  sim minus the closest reference of any OTHER character — identity, not just composition.
          Needed because a title card scores 0.85 against everything: CLIP keys on composition, so only
          the relative comparison means anything. Pools are capped to the same size per character, or
          the side with more references wins on the maximum alone.
  skin    fraction of the head band (top 28% of the person's box) that is skin-toned. A face is a large
          skin blob; from behind the head is hair. This is the back-view signal.
  aspect, fill, h   box height/width, mask area over box area, box height as a fraction of the frame.
          A standing figure measures ~2.9 / ~0.5 (the DA renders); logos and text cards ~1.3 / ~0.9.

Candidates rank by sim * (1 - skin) * min(1, h/0.6): recognisable, seen from behind, cut cleanly.
The owner picks from the sheet — the scan cannot see the image, so it shortlists and shows.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

sys.path.insert(0, "/tmp/opencode")
import emblib as E  # noqa: E402  (local ONNX CLIP encoder)
from rembg import new_session, remove  # noqa: E402

ANIME = Path("/home/greatsaiyadave/Downloads/YouTube Downloader")
REPO = Path(__file__).resolve().parents[3]
SPRITES = REPO / "Assets/Resources/WRLDZ/AnimeCharacters"
META = Path("/home/greatsaiyadave/argon-avatar/research/anime_variants/episode_meta.json")
WIKI = Path("/tmp/opencode/wiki_chars")
REFS = Path("/tmp/opencode/anime_refs")

FPS = 24000 / 1001   # -frame_pts writes the source FRAME INDEX (the last frame of a 20.7 min episode
                     # lands on 29702 of ~29835), so seconds = index / fps
ALIAS = {"yugi": ["yami_yugi"], "yami_yugi": ["yugi"]}   # same body: each is a positive for the other
SKIN = dict(cr=(133, 178), cb=(77, 130))
HEAD_BAND = 0.28
POOL_CAP = 6         # references kept per character, so the max is not won by the biggest pool

LOOSE = dict(h=0.22, aspect=1.3, fill=0.9)      # anything a person could be gets cached
GATES = dict(min_sim=0.62, min_margin=-0.10, min_height=0.28, min_aspect=1.6, max_fill=0.80)


# ---------------------------------------------------------------- episodes

def episode_video(key: str) -> Path | None:
    m = re.match(r"dm_s(\d+)e(\d+)", key)
    if not m:
        return None
    pat = re.compile(rf"Season\s*{int(m.group(1))}\s*Episode\s*0*{int(m.group(2))}\b", re.I)
    for p in sorted(ANIME.iterdir()):
        if p.suffix.lower() in (".mp4", ".mkv", ".webm") and pat.search(p.name):
            return p
    return None


def harvest(video: Path, work: Path, scene: float, width: int) -> list[tuple[float, Path]]:
    frames = work / "frames"
    frames.mkdir(parents=True, exist_ok=True)
    if not any(frames.glob("*.jpg")):
        subprocess.run(
            ["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", str(video),
             "-vf", f"select='gt(scene,{scene})',scale={width}:-2",
             "-fps_mode", "vfr", "-frame_pts", "1", "-q:v", "3", str(frames / "%06d.jpg")],
            check=True)
    return [(int(p.stem) / FPS, p) for p in sorted(frames.glob("*.jpg"))]


# ---------------------------------------------------------------- per-frame signals

def largest_blob(alpha: np.ndarray) -> np.ndarray:
    lab, n = ndimage.label(alpha)
    if n <= 1:
        return alpha
    sizes = ndimage.sum(alpha, lab, range(1, n + 1))
    return lab == (int(np.argmax(sizes)) + 1)


def skin_fraction(frame_rgb: np.ndarray, mask: np.ndarray) -> float:
    y0, y1 = np.where(mask.any(1))[0][[0, -1]]
    band = np.zeros_like(mask)
    band[y0:y0 + max(1, int((y1 - y0) * HEAD_BAND))] = True
    sel = mask & band
    if sel.sum() < 50:
        return 1.0
    r = frame_rgb[:, :, 0].astype(np.float32)
    g = frame_rgb[:, :, 1].astype(np.float32)
    b = frame_rgb[:, :, 2].astype(np.float32)
    y = 0.299 * r + 0.587 * g + 0.114 * b
    cr = (r - y) * 0.713 + 128.0
    cb = (b - y) * 0.564 + 128.0
    skin = (cr >= SKIN["cr"][0]) & (cr <= SKIN["cr"][1]) & (cb >= SKIN["cb"][0]) & (cb <= SKIN["cb"][1])
    return float((skin & sel).sum() / sel.sum())


def embed(im: Image.Image) -> np.ndarray:
    return E.embed(im, "clip", flip=True)


def character_pools(extra: list[str]) -> dict[str, list[np.ndarray]]:
    """CLIP embeddings per character (DA front render + wiki stills + anime refs), capped for fairness."""
    out: dict[str, list[np.ndarray]] = {}
    for c in sorted(set(E.CH) | {"yugi"}):
        got: list[np.ndarray] = []
        for d in (c, c + "_da"):
            p = SPRITES / d / f"{d}_front.png"
            if p.exists():
                got.append(embed(Image.open(p)))
        for base in (WIKI, REFS):
            d = base / c
            if not d.is_dir():
                continue
            for p in sorted(d.glob("*")):
                if p.suffix.lower() in (".png", ".jpg", ".jpeg", ".webp"):
                    try:
                        got.append(embed(Image.open(p)))
                    except Exception:
                        pass
        if got:
            out[c] = got[:POOL_CAP]
    for g in extra:
        for p in sorted(Path("/").glob(g.lstrip("/"))):
            try:
                out.setdefault("extra", []).append(embed(Image.open(p)))
            except Exception:
                pass
    return out


# ---------------------------------------------------------------- phases

def analyse(key: str, out: Path, sess, args) -> None:
    """One pass over an episode: cutouts, geometry/skin, CLIP embeddings. Character-independent."""
    work = out / key
    sig, emb = work / "signals.jsonl", work / "emb.npy"
    if sig.exists() and emb.exists():
        print(f"  {key}: cached ({sum(1 for _ in sig.open())} frames)")
        return
    video = episode_video(key)
    if video is None:
        print(f"  {key}: no local file")
        return
    frames = harvest(video, work, args.scene, args.width)
    dur = max(t for t, _ in frames) if frames else 0
    frames = [f for f in frames if args.skip_head <= f[0] <= dur - args.skip_tail]
    if args.max_frames:
        frames = frames[::max(1, len(frames) // args.max_frames)]
    print(f"  {key}: {video.name[:46]} · {len(frames)} shots in window")
    (out / "cutouts").mkdir(parents=True, exist_ok=True)

    rows: list[dict] = []
    vecs: list[np.ndarray] = []
    for t, fp in frames:
        im = Image.open(fp).convert("RGB")
        try:
            cut = remove(im.convert("RGBA"), session=sess)
        except Exception:
            continue
        raw = np.asarray(cut)
        if (raw[:, :, 3] > 128).sum() < im.width * im.height * 0.004:
            continue
        mask = largest_blob(raw[:, :, 3] > 128)
        ys, xs = np.where(mask)
        hh, ww = ys.max() - ys.min() + 1, xs.max() - xs.min() + 1
        h, aspect, fill = hh / im.height, hh / ww, float(mask.sum() / (hh * ww))
        if h < LOOSE["h"] or aspect < LOOSE["aspect"] or fill > LOOSE["fill"]:
            continue
        name = f"{key}_{int(t):05d}.png"
        rgba = np.dstack([raw[:, :, :3], (mask * 255).astype(np.uint8)])
        Image.fromarray(rgba).save(out / "cutouts" / name)
        vecs.append(embed(Image.fromarray(rgba)))
        rows.append(dict(ep=key, t=int(t), h=round(h, 3), aspect=round(aspect, 2), fill=round(fill, 2),
                         skin=round(skin_fraction(np.asarray(im), mask), 3), cutout=f"cutouts/{name}"))
    sig.write_text("\n".join(json.dumps(r) for r in rows))
    np.save(emb, np.stack(vecs) if vecs else np.zeros((0, 512), np.float32))
    print(f"      person-shaped shots cached: {len(rows)}")


def load(out: Path, eps: list[str]) -> list[dict]:
    rows: list[dict] = []
    for key in eps:
        sig, emb = out / key / "signals.jsonl", out / key / "emb.npy"
        if not (sig.exists() and emb.exists()):
            continue
        r = [json.loads(l) for l in sig.read_text().splitlines() if l.strip()]
        v = np.load(emb)
        for row, vec in zip(r, v):
            row["vec"] = vec
            rows.append(row)
    return rows


def score(rows: list[dict], character: str, pools: dict[str, list[np.ndarray]]) -> None:
    positives = [character] + ALIAS.get(character, [])
    pos = np.stack([e for c in positives for e in pools.get(c, [])])
    neg = np.stack([e for c in pools if c not in positives for e in pools[c]])
    for r in rows:
        v = r["vec"]
        sim = float(np.max(pos @ v))
        r["sim"] = round(sim, 3)
        r["margin"] = round(sim - float(np.max(neg @ v)), 3)


def funnel(rows: list[dict], g: dict) -> str:
    steps = [("person-shaped", rows)]
    for name, pred in (("h", lambda r: r["h"] >= g["min_height"]),
                       ("aspect", lambda r: r["aspect"] >= g["min_aspect"]),
                       ("fill", lambda r: r["fill"] <= g["max_fill"]),
                       ("sim", lambda r: r["sim"] >= g["min_sim"]),
                       ("margin", lambda r: r["margin"] >= g["min_margin"])):
        rows = [r for r in rows if pred(r)]
        steps.append((name, rows))
    return "  funnel: " + " -> ".join(f"{n} {len(v)}" for n, v in steps)


def sheet(rows: list[dict], out: Path, name: str, cols: int = 5, cell: int = 250) -> None:
    rows = rows[:cols * 4]
    if not rows:
        return
    im = Image.new("RGB", (cols * cell, math.ceil(len(rows) / cols) * (cell + 26)), (32, 32, 38))
    d = ImageDraw.Draw(im)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 14)
    except Exception:
        font = ImageFont.load_default()
    for i, r in enumerate(rows):
        cx, cy = (i % cols) * cell, (i // cols) * (cell + 26)
        cut = Image.open(out / r["cutout"]).convert("RGBA")
        cut.thumbnail((cell - 8, cell - 8))
        bg = Image.new("RGB", (cell - 8, cell - 8), (58, 58, 66))
        bg.paste(cut, ((cell - 8 - cut.width) // 2, (cell - 8 - cut.height) // 2), cut)
        # the source frame, so the pose can be judged and not just the silhouette
        src = out / r["ep"] / "frames" / f"{int(r['t'] * FPS):06d}.jpg"
        if src.exists():
            thumb = Image.open(src).convert("RGB")
            thumb.thumbnail((cell // 2 - 8, cell // 2 - 8))
            bg.paste(thumb, (cell - 12 - thumb.width, cell - 12 - thumb.height))
        im.paste(bg, (cx + 4, cy + 4))
        t = f"{r['t']//60}:{r['t']%60:02d}"
        d.text((cx + 6, cy + cell - 4),
               f"{r['ep'][-3:]} {t} sim{r['sim']:.2f} m{r['margin']:+.2f} skin{r['skin']*100:.0f}%",
               fill=(235, 235, 245), font=font)
    im.save(out / name, quality=88)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--character", default="", help="required for --select")
    ap.add_argument("--episodes", default="auto", help="comma list of dm_sNeNNN, or auto (cast_strong)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--select", action="store_true", help="score the caches only (no model runs)")
    ap.add_argument("--scene", type=float, default=0.22)
    ap.add_argument("--width", type=int, default=960)
    ap.add_argument("--min-sim", type=float, default=GATES["min_sim"])
    ap.add_argument("--min-margin", type=float, default=GATES["min_margin"])
    ap.add_argument("--min-height", type=float, default=GATES["min_height"])
    ap.add_argument("--min-aspect", type=float, default=GATES["min_aspect"])
    ap.add_argument("--max-fill", type=float, default=GATES["max_fill"])
    ap.add_argument("--skip-head", type=float, default=45.0)
    ap.add_argument("--skip-tail", type=float, default=45.0)
    ap.add_argument("--max-frames", type=int, default=0)
    ap.add_argument("--top", type=int, default=20)
    ap.add_argument("--ref", action="append", default=[])
    args = ap.parse_args()
    g = dict(min_sim=args.min_sim, min_margin=args.min_margin, min_height=args.min_height,
             min_aspect=args.min_aspect, max_fill=args.max_fill)

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    if args.episodes == "auto":
        meta = json.loads(META.read_text())
        eps = sorted(k for k, v in meta.items() if k.startswith("dm_s1"))
        if args.character:
            eps = [k for k in eps if args.character in meta[k].get("cast_strong", [])] or eps
    else:
        eps = [e.strip() for e in args.episodes.split(",") if e.strip()]

    if not args.select:
        sess = new_session("isnet-anime")
        print(f"analysing {len(eps)} episodes")
        for key in eps:
            analyse(key, out, sess, args)
        if not args.character:
            print("pass done — score any character with --select")
            return 0

    if not args.character:
        print("--select needs --character", file=sys.stderr)
        return 2
    pools = character_pools(args.ref)
    positives = [args.character] + ALIAS.get(args.character, [])
    print(f"character {args.character} · pools {len(pools)} · positives {[c for c in positives if c in pools]}")
    rows = load(out, eps)
    if not rows:
        print("no cached frames — run the analyse pass first", file=sys.stderr)
        return 2
    score(rows, args.character, pools)
    print(funnel(rows, g))
    keep = [r for r in rows if r["h"] >= g["min_height"] and r["aspect"] >= g["min_aspect"]
            and r["fill"] <= g["max_fill"] and r["sim"] >= g["min_sim"] and r["margin"] >= g["min_margin"]]
    for r in keep:
        r["score"] = round(r["sim"] * (1 - r["skin"]) * min(1.0, r["h"] / 0.6), 3)
    keep.sort(key=lambda r: -r["score"])
    for r in keep:
        r.pop("vec", None)
    (out / f"index_{args.character}.json").write_text(json.dumps(
        dict(character=args.character, episodes=eps, gates=g, candidates=len(keep), scanned=len(rows),
             frames=keep), indent=1))
    sheet(keep, out, f"{args.character}_backview_candidates.jpg")
    print(f"candidates {len(keep)} of {len(rows)} · sheet {out / (args.character + '_backview_candidates.jpg')}")
    for r in keep[:args.top]:
        print(f"  {r['score']:.3f}  {r['ep'][-3:]} {r['t']//60:3d}:{r['t']%60:02d}  sim {r['sim']:.2f} m {r['margin']:+.2f} "
              f"skin {r['skin']*100:3.0f}% h {r['h']*100:3.0f}% asp {r['aspect']:.1f} fill {r['fill']:.2f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
