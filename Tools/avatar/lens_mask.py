"""Lens masks for eyewear sculpts (2026-10-19, owner: "Please clear Weevil's lenses ... a floating asset on the lenses
that moves that represents reflection/refraction"). A Meshy eyewear sculpt is one mesh whose lens is painted white in a
chaotic UV atlas, so the lens is found per TRIANGLE in 3D: a triangle is lens when most of its seven paint samples
(corners, edge midpoints, centroid) are white; lens triangles are grouped by shared vertices and only the large
components are kept (stray white specks on the frame are dropped). The kept triangles are rasterised into the item's UV
space with a 1.5 px apron in true pixels (the face-flatten tool's lesson: an edge-function apron leaves one-texel slivers
at every UV seam). The sculpt itself is not touched: AvatarRecolor discards the mask's texels (_LensMap) and the
WRLDZ/AvatarLensGlass pass draws the glass, its rim and the anime glare over them (OverworldAvatar3d.GraftCloth).

  blender -b --python Tools/avatar/lens_mask.py -- <item id> <sex> [--out <dir>] [--clothes <Clothes dir>] [--install]
  e.g. blender -b --python Tools/avatar/lens_mask.py -- face_weevil_3d male --install
Outputs lens_<id>_<sex>.png (R8 2048^2, white = lens) and a review overlay; --install copies the mask into
Assets/Resources/WRLDZ/Avatar3D/Clothes/ (the import rule makes lens_ textures linear + readable).
"""
import sys, os, json, time
import numpy as np
try:
    import bpy
except ImportError:
    bpy = None

PROJ = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
CLOTH = f'{PROJ}/Assets/Resources/WRLDZ/Avatar3D/Clothes'
if '--clothes' in sys.argv: CLOTH = sys.argv[sys.argv.index('--clothes') + 1]   # e.g. the owner's checkout, where the Meshy sculpts live
N = 2048
T0 = time.time()
def log(*a): print('[lens %5.1fs]' % (time.time() - T0), *a, flush=True)

def tri_cover(uv0, uv1, uv2, pad):
    """Texels a UV triangle covers, grown by pad PIXELS past its edges: (x0, y0, inside) or None."""
    pts = np.array([uv0, uv1, uv2], dtype=float) * (N - 1)
    pts[:, 1] = (N - 1) - pts[:, 1]
    m = int(np.ceil(abs(pad))) + 1
    x0, y0 = np.floor(pts.min(0)).astype(int) - m; x1, y1 = np.ceil(pts.max(0)).astype(int) + m
    x0 = max(x0, 0); y0 = max(y0, 0); x1 = min(x1, N - 1); y1 = min(y1, N - 1)
    if x1 < x0 or y1 < y0: return None
    xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    def edge(a, b, px, py): return (px - a[0]) * (b[1] - a[1]) - (py - a[1]) * (b[0] - a[0])
    def length(a, b): return max(float(np.hypot(b[0] - a[0], b[1] - a[1])), 1e-9)
    e0 = edge(pts[1], pts[2], xs, ys); e1 = edge(pts[2], pts[0], xs, ys); e2 = edge(pts[0], pts[1], xs, ys)
    area = edge(pts[0], pts[1], pts[2][0], pts[2][1])
    if abs(area) < 1e-9: return None
    sgn = 1.0 if area > 0 else -1.0
    d0 = e0 * sgn / length(pts[1], pts[2]); d1 = e1 * sgn / length(pts[2], pts[0]); d2 = e2 * sgn / length(pts[0], pts[1])
    return x0, y0, (d0 >= -pad) & (d1 >= -pad) & (d2 >= -pad)

def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    item, sex = argv[0], argv[1]
    out = argv[argv.index('--out') + 1] if '--out' in argv else f'{PROJ}/Temp/LensMask'
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f'{CLOTH}/argon_{item}_{sex}.fbx')
    obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    me = obj.data; me.calc_loop_triangles()
    alb = None
    for stem in (f'albedo_{item}_{sex}', f'albedo_{item}'):
        for ext in ('.jpg', '.png'):
            if os.path.exists(f'{CLOTH}/{stem}{ext}'): alb = f'{CLOTH}/{stem}{ext}'; break
        if alb: break
    img = bpy.data.images.load(alb)
    W, H = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)[::-1, :, :3]
    log(item, sex, 'tris', len(me.loop_triangles), 'albedo', os.path.basename(alb), (W, H))
    uvl = me.uv_layers.active.data
    def sample(uv):
        x = min(max(int(uv[0] * (W - 1) + 0.5), 0), W - 1); y = min(max(int((1 - uv[1]) * (H - 1) + 0.5), 0), H - 1)
        return px[y, x]
    def white(c): return c.min() > 0.80 and (c.max() - c.min()) < 0.12
    tris = list(me.loop_triangles)
    lens = np.zeros(len(tris), dtype=bool)
    for k, t in enumerate(tris):
        u = [np.array(uvl[l].uv[:]) for l in t.loops]
        pts = u + [(u[0] + u[1]) / 2, (u[1] + u[2]) / 2, (u[2] + u[0]) / 2, (u[0] + u[1] + u[2]) / 3]
        lens[k] = sum(white(sample(p)) for p in pts) >= 4
    log('white triangles', int(lens.sum()), 'of', len(tris))
    # components of lens triangles by shared vertices
    parent = {}
    def find(a):
        while parent.setdefault(a, a) != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb: parent[ra] = rb
    co = np.array([obj.matrix_world @ v.co for v in me.vertices])
    for k, t in enumerate(tris):
        if not lens[k]: continue
        vs = list(t.vertices); union(vs[0], vs[1]); union(vs[1], vs[2])
    comp = {}
    for k, t in enumerate(tris):
        if not lens[k]: continue
        r = find(t.vertices[0])
        a, b, c = co[t.vertices[0]], co[t.vertices[1]], co[t.vertices[2]]
        comp.setdefault(r, [0.0, []]); comp[r][0] += 0.5 * np.linalg.norm(np.cross(b - a, c - a)); comp[r][1].append(k)
    ranked = sorted(comp.values(), key=lambda x: -x[0])
    big = ranked[0][0] if ranked else 0
    keep = [c for c in ranked if c[0] >= 0.05 * big]
    log('lens components', len(ranked), 'kept', len(keep), 'areas cm2', [round(c[0] * 1e4, 2) for c in ranked[:6]])
    kept = np.zeros(len(tris), dtype=bool)
    for c in keep: kept[c[1]] = True
    mask = np.zeros((N, N), dtype=np.float32)
    for k in np.nonzero(kept)[0]:
        t = tris[k]
        r = tri_cover(uvl[t.loops[0]].uv, uvl[t.loops[1]].uv, uvl[t.loops[2]].uv, 1.5)
        if r is None: continue
        x0, y0, inside = r
        sub = mask[y0:y0 + inside.shape[0], x0:x0 + inside.shape[1]]
        sub[inside] = 1.0
    # the frame's own triangles must stay: clear texels inside non-lens triangles (no apron) so a lens apron never eats rim
    for k in np.nonzero(~kept)[0]:
        t = tris[k]
        r = tri_cover(uvl[t.loops[0]].uv, uvl[t.loops[1]].uv, uvl[t.loops[2]].uv, -0.5)
        if r is None: continue
        x0, y0, inside = r
        sub = mask[y0:y0 + inside.shape[0], x0:x0 + inside.shape[1]]
        sub[inside] = 0.0
    # triangles that straddle the rim and the lens (they touch a kept lens triangle): decided per texel from the paint,
    # so no ring of opaque white stays inside the rim
    lens_verts = set(np.array([tris[k].vertices for k in np.nonzero(kept)[0]]).ravel().tolist()) if kept.any() else set()
    edge_tris = [k for k in np.nonzero(~kept)[0] if any(v in lens_verts for v in tris[k].vertices)]
    for k in edge_tris:
        t = tris[k]
        r = tri_cover(uvl[t.loops[0]].uv, uvl[t.loops[1]].uv, uvl[t.loops[2]].uv, 0.0)
        if r is None: continue
        x0, y0, inside = r
        ys, xs = np.nonzero(inside)
        ax = np.clip(((xs + x0 + 0.5) / N * W).astype(int), 0, W - 1); ay = np.clip(((ys + y0 + 0.5) / N * H).astype(int), 0, H - 1)
        c = px[ay, ax]
        w = (c.min(1) > 0.80) & ((c.max(1) - c.min(1)) < 0.12)
        mask[ys[w] + y0, xs[w] + x0] = 1.0
    log('rim/lens edge triangles', len(edge_tris))
    lc = co[np.unique(np.array([tris[k].vertices for k in np.nonzero(kept)[0]]).ravel())] if kept.any() else np.zeros((0, 3))
    info = dict(item=item, sex=sex, lensTriangles=int(kept.sum()), components=len(keep), maskTexels=int((mask > 0.5).sum()),
                lensBoundsMin=lc.min(0).round(4).tolist() if len(lc) else None, lensBoundsMax=lc.max(0).round(4).tolist() if len(lc) else None)
    log(info)
    np.save(f'{out}/lens_{item}_{sex}.npy', (mask * 255).astype(np.uint8))
    rev = (np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)[::-1, :, :3] * 255).astype(np.uint8)
    step = max(1, W // N)
    rev = rev[::step, ::step][:N, :N].copy()
    m = mask[:rev.shape[0], :rev.shape[1]] > 0.5
    rev[m] = (0.4 * rev[m] + 0.6 * np.array([255, 0, 255])).astype(np.uint8)
    np.save(f'{out}/lens_{item}_{sex}_review.npy', rev)
    json.dump(info, open(f'{out}/lens_{item}_{sex}.json', 'w'), indent=1)

def make_pngs(out, item, sex, install):
    from PIL import Image
    import shutil
    m8 = np.load(f'{out}/lens_{item}_{sex}.npy'); rev = np.load(f'{out}/lens_{item}_{sex}_review.npy')
    Image.fromarray(m8, 'L').save(f'{out}/lens_{item}_{sex}.png')
    Image.fromarray(rev, 'RGB').save(f'{out}/lens_{item}_{sex}_review.png')
    if install:
        shutil.copy(f'{out}/lens_{item}_{sex}.png', f'{CLOTH}/lens_{item}_{sex}.png'); print('installed', f'lens_{item}_{sex}.png')

if __name__ == '__main__':
    if bpy is not None and hasattr(bpy, 'ops'):
        main()
    else:
        a = sys.argv[1:]
        out = a[a.index('--out') + 1] if '--out' in a else f'{PROJ}/Temp/LensMask'
        make_pngs(out, a[0], a[1], '--install' in a)
