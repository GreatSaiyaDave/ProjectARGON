"""Face-flatten masks for the ArgonAvatarRig bases (2026-10-18, the animated face): the Meshy skin albedo paints eyes,
brows, a nose shadow and a mouth on the face. AvatarAnimeFace draws living ones on top, so those painted texels are
turned into plain skin in the body's own UV space (seam-free, follows every head morph) by AvatarRecolor's _FaceFlatMap.

  blender -b --python Tools/avatar/face_flat_mask.py -- [--out <dir>] [--install]

Per body: the face = the Head-weighted vertices of build/custom/argon_avatar_<sex>.glb from the chin to above the
brows, reaching 2 cm behind the skull's middle plane (the outer eye corners and brow tails sit at the temples). A face
vertex is a FEATURE when the region map (Textures/Meshy_<sex>_Body_mask.png, R = skin; the bases carry eye whites and
lashes as G / B regions) or the albedo paints a non-skin texel at one of its UV corners, and every triangle holding such
a texel is flagged whole (brow strokes thinner than a triangle). The short-hair cap (scalp_<sex>_mask.png) is hair, not
a feature. The flatten weight lives on the mesh - 1 within 14 mm of a feature vertex along the surface, eased out over
10 mm more - and is rasterised through the triangles, so it is seam-free however the UV islands are cut. The face's
median skin colour is written next to it (the flat colour the shader paints). Outputs
face_flat_<sex>_mask.png (R8, 2048²), face_flat_<sex>.json (skin colour, UV bounds of the features) and a review
overlay face_flat_<sex>_review.png (the albedo with the mask in magenta). --install copies the masks + json into
Assets/Resources/WRLDZ/Avatar3D/Textures/.
"""
import sys, os, json, time
import numpy as np
try:
    import bpy
except ImportError:
    bpy = None

T0 = time.time()
def log(*a): print('[facemask %5.1fs]' % (time.time() - T0), *a, flush=True)
R = '/home/greatsaiyadave/argon-avatar'
PROJ = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
UNIT = f'{PROJ}/Assets/Resources/WRLDZ/Avatar3D/Textures'
B = 'mixamorig:'
N = 2048

def load(sex):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=f'{R}/build/custom/argon_avatar_{sex}.glb')
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('ArgonBody'))
    return arm, body

def head_weights(body, arm):
    me = body.data
    gi = {g.index: g.name for g in body.vertex_groups}
    w = np.zeros(len(me.vertices))
    for v in me.vertices:
        for g in v.groups:
            if gi.get(g.group) == B + 'Head': w[v.index] = g.weight
    return w

def tri_cover(uv0, uv1, uv2, pad):
    """The texels a UV triangle covers, grown (pad > 0) or shrunk (pad < 0) by that many PIXELS past its edges:
    (x0, y0, x1, y1, inside, e0, e1, e2, area) or None for a degenerate / off-atlas triangle."""
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
    # signed distance (px) to each edge, positive inside
    d0 = e0 * sgn / length(pts[1], pts[2]); d1 = e1 * sgn / length(pts[2], pts[0]); d2 = e2 * sgn / length(pts[0], pts[1])
    inside = (d0 >= -pad) & (d1 >= -pad) & (d2 >= -pad)
    return x0, y0, x1, y1, inside, e0, e1, e2, area

def raster_tri(mask, uv0, uv1, uv2, value=1.0, w=None, pad=1.5):
    """Fill one UV triangle into mask (N x N, v up = row 0 at the top like PIL); w = per-corner weights to interpolate.
    pad = the apron in PIXELS past each edge, so a sample at an island border never reads the empty atlas next to it
    (the first cut compared the raw edge function - distance x edge length - with a constant: a long edge got no apron
    at all and every UV seam on the face showed as a one-texel sliver of the painted feature under it)."""
    t = tri_cover(uv0, uv1, uv2, pad)
    if t is None: return
    x0, y0, x1, y1, inside, e0, e1, e2, area = t
    if w is None:
        val = np.full(inside.shape, value)
    else:
        b0 = np.clip(e0 / area, 0, 1); b1 = np.clip(e1 / area, 0, 1); b2 = np.clip(e2 / area, 0, 1)
        ssum = np.maximum(b0 + b1 + b2, 1e-6)
        val = (b0 * w[0] + b1 * w[1] + b2 * w[2]) / ssum
    sub = mask[y0:y1 + 1, x0:x1 + 1]
    sub[inside] = np.maximum(sub[inside], val[inside])

def dilate(m, r):
    out = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy > r * r: continue
            out = np.maximum(out, np.roll(np.roll(m, dy, 0), dx, 1))
    return out

def erode(m, r):
    return 1.0 - dilate(1.0 - m, r)

def blur(m, r):
    out = m.astype(float)
    for _ in range(2):
        acc = np.zeros_like(out); n = 0
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy > r * r: continue
                acc += np.roll(np.roll(out, dy, 0), dx, 1); n += 1
        out = acc / n
    return out

def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = argv[argv.index('--out') + 1] if '--out' in argv else f'{PROJ}/Temp/FaceFlat'
    os.makedirs(out, exist_ok=True)
    for sex in ('male', 'female'):
        arm, body = load(sex)
        me = body.data
        wh = head_weights(body, arm)
        P = np.array([body.matrix_world @ v.co for v in me.vertices])
        base, _ = (np.array(arm.matrix_world @ arm.data.bones[B + 'Head'].head_local), None)
        hy = P[wh > 0.5, 1].mean()
        # the FACE: head vertices between the chin and the hairline, the whole way round the head (no depth bound: the
        # brow tails and lash tips run into the big temple triangles - v5 / v6 boxes that stopped at the skull's middle
        # plane left the female's as thin dark strokes; the hair cap is excluded below and the ears carry no features)
        eye_z = base[2] + 0.031
        face_v = (wh > 0.5) & (P[:, 2] > base[2] - 0.02) & (P[:, 2] < eye_z + 0.075)
        def face_tri(tri):
            # one Head-weighted corner under the hairline is enough; every corner above the chin (the neck's undersuit
            # paint must never count as a face feature)
            vs = list(tri.vertices)
            return (wh[vs] > 0.5).any() and (P[vs, 2] > base[2] - 0.02).all() and (P[vs, 2] < eye_z + 0.075).any()
        uvl = me.uv_layers.active.data
        foot = np.zeros((N, N))
        me.calc_loop_triangles()
        for tri in me.loop_triangles:
            if not face_tri(tri): continue
            raster_tri(foot, uvl[tri.loops[0]].uv, uvl[tri.loops[1]].uv, uvl[tri.loops[2]].uv, pad=0.0)
        log(sex, 'face footprint texels', int(foot.sum()))
        # the albedo
        img = bpy.data.images.load(f'{R}/build/custom/unity_textures/Meshy_{sex}_Body.png')
        px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)[::-1, :, :3]   # row 0 at the top
        if px.shape[0] != N:
            from PIL import Image  # noqa (only if the albedo is not 2048)
            raise SystemExit('albedo is not %d px' % N)
        # linear -> display-ish: the median skin of the face footprint, measured in the file's own encoding
        fp = foot > 0.5
        skin = np.median(px[fp], axis=0)
        d = np.abs(px - skin[None, None, :]).sum(2)
        lum = px @ np.array([0.2126, 0.7152, 0.0722])
        skinLum = float(skin @ np.array([0.2126, 0.7152, 0.0722]))
        # a feature texel: far from the skin colour (ink, sclera, iris, lip) or much darker (shadowed socket / nostril)
        # features = the region map's non-skin texels on the face (the bases carry eye whites / lashes as undersuit and
        # "other" regions: Textures/Meshy_<sex>_Body_mask.png, R = skin) plus the albedo's ink / sclera / iris / lip and
        # the brow strokes; then the socket goes flat: a dilation inside the face footprint
        rm = bpy.data.images.load(f'{UNIT}/Meshy_{sex}_Body_mask.png')
        rpx = np.array(rm.pixels[:], dtype=np.float32).reshape(rm.size[1], rm.size[0], 4)[::-1, :, :3]
        RN = rpx.shape[0]
        # the short-hair cap the region map paints in G on the head (scalp_<sex>_mask.png, scripts/scalp_maps.py) is
        # hair, not a face feature: it stays, hidden under the sculpted hairstyle, painted in the hair colour under a hat
        # ... but only its solid blob: the painted hairstyle's thin strands over the face (side locks down to the brow
        # tails and eye corners, fringe tips) are features like the lashes - an opening of ~5 mm keeps the cap, drops them
        sm = bpy.data.images.load(f'{UNIT}/scalp_{sex}_mask.png')
        spx = np.array(sm.pixels[:], dtype=np.float32).reshape(sm.size[1], sm.size[0], 4)[::-1, :, 0]
        spx = dilate(erode((spx > 0.5).astype(np.float32), 6), 6)
        SN = spx.shape[0]
        log(sex, 'cap core texels', int((spx > 0.5).sum()))
        # FEATURE VERTICES: a face vertex is a feature when the region map (R = skin) or the albedo paints a feature at
        # any of its UV corners; the flatten weight then lives on the MESH (grown 14 mm along the surface, eased over
        # 10 mm more) and is rasterised through the triangles - seam-free, however the UV islands are cut
        def texel(img, uv, n):
            x = min(max(int(uv[0] * (n - 1) + 0.5), 0), n - 1); y = min(max(int((1 - uv[1]) * (n - 1) + 0.5), 0), n - 1)
            return img[y, x]
        isFeat = np.zeros(len(me.vertices), dtype=bool)
        for tri in me.loop_triangles:
            if not face_tri(tri): continue
            for li, vi in zip(tri.loops, tri.vertices):
                uv = uvl[li].uv
                if texel(spx, uv, SN) > 0.5: continue
                r = texel(rpx, uv, RN)
                a = texel(px, uv, N)
                if r[0] < 0.6 or np.abs(a - skin).sum() > 0.22 or (a @ np.array([0.2126, 0.7152, 0.0722])) < skinLum * 0.62:
                    isFeat[vi] = True
        # strokes thinner than a triangle (brows, lash tails) pass between the vertices: rasterise the region map's
        # non-skin texels and flag every triangle that contains one
        regionFeat = np.zeros((N, N), dtype=bool)
        rr = rpx[:, :, 0] if RN == N else np.repeat(np.repeat(rpx[:, :, 0], N // RN, 0), N // RN, 1)
        ss = spx if SN == N else np.repeat(np.repeat(spx, N // SN, 0), N // SN, 1)
        regionFeat = fp & ((rr < 0.6) | (d > 0.22) | (lum < skinLum * 0.62)) & (ss < 0.5)
        hits = 0
        for tri in me.loop_triangles:
            vs = tri.vertices
            if not face_tri(tri): continue
            t = tri_cover(uvl[tri.loops[0]].uv, uvl[tri.loops[1]].uv, uvl[tri.loops[2]].uv, -0.5)
            if t is None: continue
            x0, y0, x1, y1, inside = t[:5]
            if (regionFeat[y0:y1 + 1, x0:x1 + 1] & inside).any():
                for vi in vs: isFeat[vi] = True
                hits += 1
        fv = np.nonzero(isFeat)[0]
        log(sex, 'feature vertices', len(fv), 'feature triangles', hits)
        wv = np.zeros(len(me.vertices))
        if len(fv):
            FP = P[fv]
            cand = np.nonzero((wh > 0.2) | isFeat)[0]   # every head vertex may carry the eased weight
            for vi in cand:
                dmin = np.min(np.linalg.norm(FP - P[vi], axis=1))
                wv[vi] = np.clip(1.0 - (dmin - 0.014) / 0.010, 0, 1)
        mask = np.zeros((N, N))
        for tri in me.loop_triangles:
            vs = tri.vertices
            if wv[list(vs)].max() <= 0: continue
            raster_tri(mask, uvl[tri.loops[0]].uv, uvl[tri.loops[1]].uv, uvl[tri.loops[2]].uv, w=[wv[vs[0]], wv[vs[1]], wv[vs[2]]])
        mask = np.clip(mask, 0, 1)
        log(sex, 'flattened texels', int((mask > 0.5).sum()))
        # self-check: the head's non-skin texels (cap aside) the mask still lets through - ideally none
        hfoot = np.zeros((N, N))
        for tri in me.loop_triangles:
            if (wh[list(tri.vertices)] > 0.5).sum() < 2: continue
            raster_tri(hfoot, uvl[tri.loops[0]].uv, uvl[tri.loops[1]].uv, uvl[tri.loops[2]].uv, pad=-0.5)
        leak = (hfoot > 0.5) & (rr < 0.6) & (ss < 0.5) & (mask < 0.9)
        ly, lx = np.nonzero(leak)
        log(sex, 'head non-skin texels still showing (cap aside)', int(leak.sum()),
            'uv bounds', (round(float(lx.min()) / N, 3), round(float(lx.max()) / N, 3), round(1 - float(ly.max()) / N, 3), round(1 - float(ly.min()) / N, 3)) if len(lx) else None)
        ys, xs = np.nonzero(mask > 0.5)
        bounds = dict(u0=float(xs.min() / N), u1=float(xs.max() / N), v0=float(1 - ys.max() / N), v1=float(1 - ys.min() / N)) if len(xs) else None
        log(sex, 'skin', [round(float(c), 4) for c in skin], 'feature texels', int((mask > 0.5).sum()), 'bounds', bounds)
        # outputs: the arrays (Blender's python has no PIL); make_pngs() below turns them into PNGs with the system python
        rev = (px * 255).astype(np.uint8).copy()
        rev[mask > 0.5] = (0.5 * rev[mask > 0.5] + 0.5 * np.array([255, 0, 255])).astype(np.uint8)
        np.save(f'{out}/face_flat_{sex}_mask.npy', (mask * 255).astype(np.uint8))
        np.save(f'{out}/face_flat_{sex}_review.npy', rev)
        json.dump(dict(skin=[float(c) for c in skin], skinLum=skinLum, bounds=bounds), open(f'{out}/face_flat_{sex}.json', 'w'), indent=1)
    log('done')

def make_pngs(out, install=False):
    """System python (PIL): the PNGs from the arrays; --install copies mask + json into the Unity Textures folder."""
    from PIL import Image
    import shutil
    for sex in ('male', 'female'):
        m8 = np.load(f'{out}/face_flat_{sex}_mask.npy'); rev = np.load(f'{out}/face_flat_{sex}_review.npy')
        Image.fromarray(m8, 'L').save(f'{out}/face_flat_{sex}_mask.png')
        Image.fromarray(rev, 'RGB').save(f'{out}/face_flat_{sex}_review.png')
        b = json.load(open(f'{out}/face_flat_{sex}.json'))['bounds']
        if b:
            Image.fromarray(rev, 'RGB').crop((int(b['u0'] * N) - 40, int((1 - b['v1']) * N) - 40, int(b['u1'] * N) + 40, int((1 - b['v0']) * N) + 40)).save(f'{out}/face_flat_{sex}_crop.png')
        if install:
            os.makedirs(UNIT, exist_ok=True)
            shutil.copy(f'{out}/face_flat_{sex}_mask.png', f'{UNIT}/face_flat_{sex}_mask.png')
            shutil.copy(f'{out}/face_flat_{sex}.json', f'{UNIT}/face_flat_{sex}.json')
            print('installed', sex)

if __name__ == '__main__':
    if 'bpy' in sys.modules and hasattr(bpy, 'ops'):
        main()
    else:
        a = sys.argv[1:]
        make_pngs(a[a.index('--out') + 1] if '--out' in a else f'{PROJ}/Temp/FaceFlat', '--install' in a)
