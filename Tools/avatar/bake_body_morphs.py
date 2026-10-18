"""Bake the bodies' blend shapes onto every installed garment and hairstyle so clothes follow the build and face sliders.

  blender -b --python Tools/avatar/bake_body_morphs.py -- --body <dir with argon_avatar_<sex>_morphs.glb> [--only <substr>] [--dry]

For each Clothes/argon_<item>_<sex>.fbx and Hair/argon_hair_<id>_<sex>.fbx: import, find the body of that sex (the GLB
written by body_morphs.py, which carries Weight_Thin / Weight_Heavy and the Build_* / Face_* keys), and for every key
give each garment vertex the displacement of its nearest body surface point (barycentric over the nearest triangle,
plus the same fraction of the body's normal change so a radial inflate keeps the garment outside the skin). The keys
are written as shape keys of the same names and the FBX is re-exported in place (same name, so Unity keeps the GUID).
OverworldAvatar3d sets every key on every skinned renderer that has it.
"""
import bpy, sys, os, glob, time
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

T0 = time.time()
def log(*a): print('[bake %5.1fs]' % (time.time() - T0), *a, flush=True)
PROJ = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
UNIT = f'{PROJ}/Assets/Resources/WRLDZ/Avatar3D'
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
BODY = argv[argv.index('--body') + 1] if '--body' in argv else f'{PROJ}/Temp/BodyMorphs'
ONLY = argv[argv.index('--only') + 1] if '--only' in argv else None
DRY = '--dry' in argv

def load_body(sex):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=f'{BODY}/argon_avatar_{sex}_morphs.glb')
    body = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('ArgonBody'))
    me = body.data; MW = body.matrix_world
    P = np.array([MW @ v.co for v in me.vertices])
    me.calc_loop_triangles()
    tris = np.array([[l.vertices[0], l.vertices[1], l.vertices[2]] for l in me.loop_triangles])
    keys = {}
    basis = me.shape_keys.key_blocks['Basis']
    for kb in me.shape_keys.key_blocks:
        if kb.name == 'Basis': continue
        keys[kb.name] = np.array([MW.to_3x3() @ (kb.data[i].co - basis.data[i].co) for i in range(len(me.vertices))])
    # keep only the body for the BVH; the GLB also holds the Meshy parts
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o is not body: bpy.data.objects.remove(o, do_unlink=True)
    bvh = BVHTree.FromPolygons([Vector(p) for p in P], [tuple(int(i) for i in t) for t in tris])
    normals = np.zeros_like(P)
    for t in tris:
        n = np.cross(P[t[1]] - P[t[0]], P[t[2]] - P[t[0]]); normals[t] += n
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-9)
    return dict(P=P, tris=tris, keys=keys, bvh=bvh, normals=normals, objects=set(bpy.data.objects))

def bary(p, a, b, c):
    v0 = b - a; v1 = c - a; v2 = p - a
    d00 = v0 @ v0; d01 = v0 @ v1; d11 = v1 @ v1; d20 = v2 @ v0; d21 = v2 @ v1
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-18: return np.array([1.0, 0.0, 0.0])
    v = (d11 * d20 - d01 * d21) / den; w = (d00 * d21 - d01 * d20) / den
    v = min(max(v, 0.0), 1.0); w = min(max(w, 0.0), 1.0); u = max(1.0 - v - w, 0.0)
    s = u + v + w; return np.array([u, v, w]) / s

def bake(fbx, body):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']; arms = [o for o in new if o.type == 'ARMATURE']
    if not meshes or not arms:
        log('skip (no mesh/rig)', os.path.basename(fbx)); return 0
    ob = meshes[0]; arm = arms[0]; me = ob.data
    MW = ob.matrix_world; inv3 = MW.inverted().to_3x3()
    G = np.array([MW @ v.co for v in me.vertices])
    # nearest body point per garment vertex: triangle + barycentric + offset along the body normal
    idxs = np.zeros(len(G), int); bcs = np.zeros((len(G), 3)); offs = np.zeros(len(G))
    for i, g in enumerate(G):
        loc, n, fi, d = body['bvh'].find_nearest(Vector(g))
        t = body['tris'][fi]; bc = bary(np.array(loc), body['P'][t[0]], body['P'][t[1]], body['P'][t[2]])
        idxs[i] = fi; bcs[i] = bc; offs[i] = float(np.dot(g - np.array(loc), np.array(n)))
    if me.shape_keys is None: ob.shape_key_add(name='Basis', from_mix=False)
    basis = me.shape_keys.key_blocks['Basis']
    written = 0
    for name, delta in body['keys'].items():
        t = body['tris'][idxs]
        D = bcs[:, 0:1] * delta[t[:, 0]] + bcs[:, 1:2] * delta[t[:, 1]] + bcs[:, 2:3] * delta[t[:, 2]]
        # a radial inflate also pushes the garment's stand-off outward: scale the offset by the local growth
        nrm = body['normals'][t[:, 0]]
        grow = (D * nrm).sum(1)
        D = D + nrm * (np.clip(grow, -0.05, 0.1) * 0.15)[:, None]
        if name in me.shape_keys.key_blocks: ob.shape_key_remove(me.shape_keys.key_blocks[name])
        kb = ob.shape_key_add(name=name, from_mix=False)
        for i in range(len(G)):
            kb.data[i].co = basis.data[i].co + inv3 @ Vector(D[i])
        kb.value = 0.0; written += 1
    if not DRY:
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, add_leaf_bones=False, bake_anim=False,
                                 use_mesh_modifiers=False, mesh_smooth_type='FACE', path_mode='STRIP',
                                 embed_textures=False, axis_forward='-Z', axis_up='Y')
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.orphans_purge(do_recursive=True)
    return written

def main():
    total = 0
    for sex in ('male', 'female'):
        files = sorted(glob.glob(f'{UNIT}/Clothes/argon_*_{sex}.fbx') + glob.glob(f'{UNIT}/Hair/argon_hair_*_{sex}.fbx') + glob.glob(f'{UNIT}/Outfits/argon_outfit_*_{sex}.fbx'))
        if ONLY: files = [f for f in files if ONLY in os.path.basename(f)]
        if not files: continue
        body = load_body(sex)
        log(sex, 'body keys', list(body['keys']), 'files', len(files))
        for f in files:
            n = bake(f, body); total += 1
            log(f'{os.path.basename(f):44s} keys {n}')
    log('done', total, 'files', '(dry)' if DRY else '')

if __name__ == '__main__':
    main()
