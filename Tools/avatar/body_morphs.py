"""Body and face morphs for the ArgonAvatarRig bases (2026-10-18): procedural blend shapes that take the two bodies from
gaunt (Bonz) to plump (Chumley) and big (Mr Huffington), plus face shapes (nose, mouth, jaw, chin, cheeks, brow).

  blender -b --python Tools/avatar/body_morphs.py -- [--out <dir>] [--render] [--install]

Reads build/custom/argon_avatar_<sex>.glb (the mesh the Unity bases were exported from; keeps Weight_Thin / Weight_Heavy),
adds the shape keys below to the body mesh only (vertex order, UVs, skinning untouched: hide maps and grafts still bind),
exports argon_base_<sex>.fbx (the Unity base, same export settings as the wardrobe tools) and argon_avatar_<sex>_morphs.glb
(for bake_body_morphs.py, which transfers every shape onto the installed garments and hair).

Build shapes inflate radially from each bone axis in proportion to the local radius (a bigger body stays body-shaped);
face shapes are Gaussian bulges around landmarks found on the head (nose tip, mouth, chin, jaw). All are 0..1 sliders;
they add, so a look can mix them.
"""
import bpy, sys, os, json, math, time
import numpy as np
from mathutils import Vector, Matrix

T0 = time.time()
def log(*a): print('[morphs %5.1fs]' % (time.time() - T0), *a, flush=True)
R = '/home/greatsaiyadave/argon-avatar'
PROJ = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
UNIT = f'{PROJ}/Assets/Resources/WRLDZ/Avatar3D'
B = 'mixamorig:'

# radial factors per region for the build shapes (1 = unchanged). belly_fwd = extra forward bulge at the belly (m).
BUILDS = {
    'Build_Plump': dict(torso=1.34, belly=1.55, chest=1.22, neck=1.22, upper_arm=1.28, forearm=1.14, thigh=1.30, calf=1.14, head=1.0, jaw=1.10, belly_fwd=0.045, chin_fwd=0.012, shoulder_out=0.0),
    'Build_Big': dict(torso=1.26, belly=1.30, chest=1.28, neck=1.28, upper_arm=1.36, forearm=1.22, thigh=1.28, calf=1.20, head=1.03, jaw=1.08, belly_fwd=0.020, chin_fwd=0.006, shoulder_out=0.03),
    'Build_Gaunt': dict(torso=0.86, belly=0.82, chest=0.90, neck=0.90, upper_arm=0.84, forearm=0.86, thigh=0.86, calf=0.88, head=1.0, jaw=0.95, belly_fwd=-0.01, chin_fwd=0.0, shoulder_out=-0.008),
}
# face shapes: (landmark, radius m, displacement vector in metres at the centre, falloff) — direction 'fwd' = -y, 'down' = -z, 'out' = ±x
FACES = {
    'Face_NoseLong': dict(at='nose', r=0.022, move=(0, -0.009, -0.004)),
    'Face_NoseWide': dict(at='nose', r=0.020, move=('out', 0.006)),
    'Face_MouthWide': dict(at='mouth', r=0.024, move=('out', 0.008)),
    'Face_LipsFull': dict(at='mouth', r=0.014, move=(0, -0.0045, 0)),
    'Face_JawWide': dict(at='jaw', r=0.035, move=('out', 0.009)),
    'Face_ChinLong': dict(at='chin', r=0.022, move=(0, -0.002, -0.009)),
    'Face_CheeksFull': dict(at='cheeks', r=0.030, move=('out', 0.007)),
    'Face_BrowHeavy': dict(at='brow', r=0.026, move=(0, -0.005, 0)),
}

def load(sex):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=f'{R}/build/custom/argon_avatar_{sex}.glb')
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('ArgonBody'))
    parts = [o for o in bpy.data.objects if o.type == 'MESH' and o is not body]
    return arm, body, parts

def weights(body, arm):
    me = body.data; names = [b.name for b in arm.data.bones]; bi = {n: i for i, n in enumerate(names)}
    W = np.zeros((len(me.vertices), len(names)))
    gi = {g.index: g.name for g in body.vertex_groups}
    for v in me.vertices:
        for g in v.groups:
            n = gi.get(g.group)
            if n in bi: W[v.index, bi[n]] = g.weight
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    return W, names, bi

def seg(arm, name):
    """(head, tail) of a bone in world space; a missing end bone (HeadTop_End) falls back to its parent's tail."""
    if B + name not in arm.data.bones:
        parent = {'HeadTop_End': 'Head', 'LeftToe_End': 'LeftToeBase', 'RightToe_End': 'RightToeBase'}.get(name, 'Head')
        b = arm.data.bones[B + parent]; t = np.array(arm.matrix_world @ b.tail_local); return t, t + (t - np.array(arm.matrix_world @ b.head_local))
    b = arm.data.bones[B + name]; return np.array(arm.matrix_world @ b.head_local), np.array(arm.matrix_world @ b.tail_local)

def radial(P, a, b):
    ab = b - a; t = np.clip(((P - a) * ab).sum(1) / max(float(ab @ ab), 1e-9), 0, 1)[:, None]
    q = a + t * ab; d = P - q; r = np.linalg.norm(d, axis=1)
    return d / np.maximum(r, 1e-9)[:, None], r

def smooth(D, nb, iters):
    for _ in range(iters):
        D2 = D.copy()
        for i, ns in enumerate(nb):
            if ns: D2[i] = 0.5 * D[i] + 0.5 * D[ns].mean(0)
        D = D2
    return D

def adjacency(me):
    nb = [[] for _ in range(len(me.vertices))]
    for e in me.edges:
        a, b = e.vertices; nb[a].append(b); nb[b].append(a)
    return nb

def build_shape(P, W, bi, arm, spec, nb):
    n = len(P); D = np.zeros((n, 3))
    def col(*names): return [bi[B + x] for x in names if B + x in bi]
    regions = {
        'torso': (col('Hips', 'Spine', 'Spine1', 'Spine2'), [('Hips', 'Spine2')]),
        'neck': (col('Neck'), [('Neck', 'Head')]),
        'head': (col('Head', 'HeadTop_End'), [('Head', 'HeadTop_End')]),
        'upper_arm': (col('LeftArm', 'RightArm'), [('LeftArm', 'LeftForeArm'), ('RightArm', 'RightForeArm')]),
        'forearm': (col('LeftForeArm', 'RightForeArm'), [('LeftForeArm', 'LeftHand'), ('RightForeArm', 'RightHand')]),
        'thigh': (col('LeftUpLeg', 'RightUpLeg'), [('LeftUpLeg', 'LeftLeg'), ('RightUpLeg', 'RightLeg')]),
        'calf': (col('LeftLeg', 'RightLeg'), [('LeftLeg', 'LeftFoot'), ('RightLeg', 'RightFoot')]),
    }
    for reg, (cols, segs) in regions.items():
        k = spec.get(reg, 1.0)
        if not cols or abs(k - 1) < 1e-6: continue
        w = W[:, cols].sum(1)
        # radial from the nearest of the region's bone segments
        best_d = None; best_r = None
        for a_n, b_n in segs:
            a, _ = seg(arm, a_n); bpos, _ = seg(arm, b_n)
            d, r = radial(P, a, bpos)
            if best_r is None: best_d, best_r = d, r
            else:
                m = r < best_r; best_d[m] = d[m]; best_r[m] = r[m]
        D += (best_d * ((k - 1) * best_r)[:, None]) * w[:, None]
    # chest vs belly: split the torso front by height (chest above Spine2 head, belly between Spine and Spine1)
    tcols = col('Hips', 'Spine', 'Spine1', 'Spine2')
    wt = W[:, tcols].sum(1)
    z_belly0, _ = seg(arm, 'Spine'); z_belly1, _ = seg(arm, 'Spine2'); z_chest1, _ = seg(arm, 'Neck')
    front = (P[:, 1] < 0.0).astype(float)
    belly = np.clip(1 - np.abs(P[:, 2] - (z_belly0[2] + z_belly1[2]) / 2) / (0.5 * (z_belly1[2] - z_belly0[2]) + 0.04), 0, 1)
    chest = np.clip((P[:, 2] - z_belly1[2]) / max(z_chest1[2] - z_belly1[2], 1e-3), 0, 1) * np.clip((z_chest1[2] + 0.02 - P[:, 2]) / 0.06, 0, 1)
    if spec.get('belly', 1.0) != 1.0:
        a, bpos = seg(arm, 'Spine')[0], seg(arm, 'Spine2')[0]
        d, r = radial(P, a, bpos)
        D += (d * ((spec['belly'] - spec.get('torso', 1.0)) * r)[:, None]) * (wt * belly)[:, None]
    if spec.get('belly_fwd', 0.0):
        D[:, 1] -= spec['belly_fwd'] * wt * belly * front
    if spec.get('chest', 1.0) != 1.0:
        a, bpos = seg(arm, 'Spine1')[0], seg(arm, 'Neck')[0]
        d, r = radial(P, a, bpos)
        D += (d * ((spec['chest'] - spec.get('torso', 1.0)) * r)[:, None]) * (wt * chest)[:, None]
    if spec.get('shoulder_out', 0.0):
        sc = col('LeftShoulder', 'LeftArm'); ws = W[:, sc].sum(1)
        D[:, 0] += spec['shoulder_out'] * ws * (P[:, 0] > 0)
        sc = col('RightShoulder', 'RightArm'); ws = W[:, sc].sum(1)
        D[:, 0] -= spec['shoulder_out'] * ws * (P[:, 0] < 0)
    # jaw / double chin
    hcols = col('Head'); wh = W[:, hcols].sum(1)
    head, _ = seg(arm, 'Head')
    low = np.clip((head[2] + 0.045 - P[:, 2]) / 0.05, 0, 1) * (P[:, 1] < head[1] + 0.02)
    if spec.get('jaw', 1.0) != 1.0:
        d, r = radial(P, head - np.array([0, 0, 0.1]), head + np.array([0, 0, 0.1]))
        D += (d * ((spec['jaw'] - 1) * r)[:, None]) * (wh * low)[:, None]
    if spec.get('chin_fwd', 0.0):
        D[:, 1] -= spec['chin_fwd'] * wh * low
        D[:, 2] -= 0.5 * spec['chin_fwd'] * wh * low
    return smooth(D, nb, 10)

def landmarks(P, W, bi, arm):
    head, _ = seg(arm, 'Head'); wh = W[:, bi[B + 'Head']] > 0.5
    hy = P[wh, 1].mean()
    eye_z = head[2] + 0.031
    front = wh & (P[:, 1] < hy - 0.03)
    chin_z = P[front, 2].min()
    nose_band = wh & (P[:, 2] > eye_z - 0.045) & (P[:, 2] < eye_z - 0.008) & (np.abs(P[:, 0]) < 0.015)
    nose = P[nose_band][np.argmin(P[nose_band, 1])] if nose_band.any() else np.array([0, hy - 0.08, eye_z - 0.03])
    mouth_band = wh & (P[:, 2] > chin_z + 0.012) & (P[:, 2] < chin_z + 0.036) & (np.abs(P[:, 0]) < 0.012)
    mouth = P[mouth_band][np.argmin(P[mouth_band, 1])] if mouth_band.any() else np.array([0, hy - 0.07, chin_z + 0.024])
    chin = P[front][np.argmin(P[front, 2])]
    brow = np.array([0, nose[1] + 0.012, eye_z + 0.024])
    return dict(nose=nose, mouth=mouth, chin=chin, brow=brow, eye_z=eye_z, chin_z=chin_z, head_y=hy)

def face_shape(P, W, bi, arm, spec, lm, nb):
    wh = W[:, bi[B + 'Head']]
    n = len(P); D = np.zeros((n, 3))
    at = spec['at']; r = spec['r']; move = spec['move']
    if at == 'jaw':
        centres = [np.array([s * 0.045, lm['mouth'][1] + 0.025, lm['chin_z'] + 0.02]) for s in (1, -1)]
    elif at == 'cheeks':
        centres = [np.array([s * 0.05, lm['nose'][1] + 0.03, lm['nose'][2] - 0.005]) for s in (1, -1)]
    else:
        centres = [lm[at]]
    for c in centres:
        g = np.exp(-0.5 * (np.linalg.norm(P - c, axis=1) / r) ** 2) * wh
        if isinstance(move[0], str):   # 'out': away from the midline
            D[:, 0] += move[1] * np.sign(P[:, 0]) * g * (np.abs(P[:, 0]) > 0.004)
        else:
            D += np.array(move)[None, :] * g[:, None]
    return smooth(D, nb, 2)

def add_shape(body, name, D):
    me = body.data
    if me.shape_keys is None: body.shape_key_add(name='Basis', from_mix=False)
    if name in me.shape_keys.key_blocks: body.shape_key_remove(me.shape_keys.key_blocks[name])
    kb = body.shape_key_add(name=name, from_mix=False)
    basis = me.shape_keys.key_blocks['Basis']
    inv = body.matrix_world.inverted().to_3x3()
    for i, v in enumerate(me.vertices):
        kb.data[i].co = basis.data[i].co + inv @ Vector(D[i])
    kb.value = 0.0

def export_fbx(objs, arm, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, add_leaf_bones=False, bake_anim=False,
                             use_mesh_modifiers=False, mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False,
                             axis_forward='-Z', axis_up='Y')

def render_sheet(body, arm, out, sex, parts=(), eye_z=1.6):
    for o in parts: o.hide_render = True
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'SINGLE'; sc.display.shading.single_color = (0.6, 0.6, 0.62)
    sc.render.resolution_x = 300; sc.render.resolution_y = 560
    cd = bpy.data.cameras.new('c'); cam = bpy.data.objects.new('c', cd); sc.collection.objects.link(cam); sc.camera = cam
    cd.type = 'ORTHO'; cd.ortho_scale = 1.9
    names = ['Basis'] + list(BUILDS) + list(FACES)
    for nm in names:
        for kb in body.data.shape_keys.key_blocks: kb.value = 1.0 if kb.name == nm else 0.0
        for tag, (px, py, rz, oz) in {'front': (0, -6, 0, 0.9), 'side': (6, 0, math.pi / 2, 0.9), 'face': (0, -6, 0, eye_z - 0.035)}.items():
            if nm.startswith('Face') and tag != 'face': continue
            cam.location = (px, py, oz); cam.rotation_euler = (math.pi / 2, 0, rz)
            cd.ortho_scale = 0.28 if tag == 'face' else 1.9
            sc.render.filepath = f'{out}/morph_{sex}_{nm}_{tag}.png'; bpy.ops.render.render(write_still=True)
    for kb in body.data.shape_keys.key_blocks: kb.value = 0.0
    for o in parts: o.hide_render = False

def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = argv[argv.index('--out') + 1] if '--out' in argv else f'{PROJ}/Temp/BodyMorphs'
    os.makedirs(out, exist_ok=True)
    report = {}
    for sex in ('male', 'female'):
        arm, body, parts = load(sex)
        W, names, bi = weights(body, arm)
        P = np.array([body.matrix_world @ v.co for v in body.data.vertices])
        nb = adjacency(body.data)
        lm = landmarks(P, W, bi, arm)
        log(sex, 'landmarks', {k: (np.round(v, 3).tolist() if isinstance(v, np.ndarray) else round(float(v), 3)) for k, v in lm.items()})
        stats = {}
        for name, spec in BUILDS.items():
            D = build_shape(P, W, bi, arm, spec, nb); add_shape(body, name, D)
            stats[name] = dict(max_mm=round(float(np.linalg.norm(D, axis=1).max()) * 1000, 1), mean_mm=round(float(np.linalg.norm(D, axis=1).mean()) * 1000, 1))
        for name, spec in FACES.items():
            D = face_shape(P, W, bi, arm, spec, lm, nb); add_shape(body, name, D)
            stats[name] = dict(max_mm=round(float(np.linalg.norm(D, axis=1).max()) * 1000, 1))
        log(sex, stats)
        report[sex] = dict(landmarks={k: (np.round(v, 4).tolist() if isinstance(v, np.ndarray) else float(v)) for k, v in lm.items()}, shapes=stats,
                           keys=[kb.name for kb in body.data.shape_keys.key_blocks])
        if '--render' in argv: render_sheet(body, arm, out, sex, parts, lm['eye_z'])
        # the GLB with the new keys (for the garment bake) and the Unity base FBX
        for o in bpy.data.objects:
            if o.type == 'CAMERA': bpy.data.objects.remove(o, do_unlink=True)
        bpy.ops.object.select_all(action='DESELECT')
        bpy.ops.export_scene.gltf(filepath=f'{out}/argon_avatar_{sex}_morphs.glb', export_format='GLB', export_apply=False, export_morph=True, export_skins=True, export_animations=False)
        export_fbx([body] + parts, arm, f'{out}/argon_base_{sex}.fbx')
        if '--install' in argv:
            import shutil
            shutil.copy(f'{out}/argon_base_{sex}.fbx', f'{UNIT}/argon_base_{sex}.fbx')
            log('installed', f'{UNIT}/argon_base_{sex}.fbx')
    json.dump(report, open(f'{out}/morphs_report.json', 'w'), indent=1)
    log('done')

if __name__ == '__main__':
    main()
