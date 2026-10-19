"""The second Duel Disk on the owner's system as a game asset (2026-10-19, owner: "There should be 2 duel disk assets on
my system for that menu"): the 3D-print "Yu-Gi-Oh! Duel Disk" assembly kit in AssetLibrary/3d_print/duel_disk
(sections 1-9, 49 STLs, Manual.pdf). The first disk is the Battle City V2 kit the game already wears
(Assets/Models/WRLDZ/SpiritDueler/BattleCityDuelDisk.obj, the "yugioh duel disk v37" model).

The kit's STLs are stored ASSEMBLED in one millimetre frame (x across, y up out of the plate, z along the forearm toward
the hand), except the parts that are not on the worn disk (D1 the pivot plate inside the hub, G1/G2 the alternative
short sleeve, H1 the display stand), and F2, the hand-held grip of the cosplay kit (the anime disk has none; it would
cut through the wearer's fingers): those are skipped. Every part keeps the colour its file name gives (the print's
colour), sampled from the kit's own cover render (Manual.pdf page 1); the 4000 LP display is one mesh whose raised digits
print white (a colour change at 182.8 mm), so faces above that height are white.

Written in the CANONICAL FOREARM FRAME the game mounts disks in (NpcDuelDisk / ArOpponentDuelist): origin at the wrist on
the forearm's axis (the arm sleeve's channel, measured from the sleeve), +Y toward the hand, +Z out of the back of the
forearm (the plate's up), +X the wearer's outside (the blade's side). Metres, life size. Unity is left-handed: the map
(x, z, y) has determinant -1, so the triangle winding is reversed.

Blade B swings on its pin in the hub (Manual step 18: "slide it in all the way, NO GLUE"); its parts (D2-D11) and the
field zone in its tip (E1) are a second object with its pivot on the pin's axis (the hub's centre, from the hub ring).

  blender -b --python Tools/avatar/disk_kit_build.py -- <3d_print/duel_disk dir> <out dir> [--render <png dir>]
Writes <out>/disk_classic.bytes: WKIT v1 = "WKIT", u32 version, u32 objects; per object: name (u16 length + utf8),
pivot (3 f32), u32 vertex count, vertices (pos 3 f32, normal 3 f32), u32 submesh count; per submesh: material name
(u16 + utf8), u32 index count, u32 indices.
"""
import sys, os, glob, math, struct
import numpy as np
import bpy, bmesh

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[0], argv[1]
RENDER = argv[argv.index('--render') + 1] if '--render' in argv else None
os.makedirs(OUT, exist_ok=True)

SKIP = ('D1_', 'F2_', 'G1_', 'G2_', 'H1_', 'B4_LP Display Off')
BLADE_B = ('D2_', 'D3_', 'D4_', 'D5_', 'D6_', 'D7_', 'D8_', 'D9_', 'D10_', 'D11_', 'E1_')
# print colours (file-name suffix) -> material names the game paints; the hex values the game uses live in
# AvatarSpiritDisk.ClassicPalette (sampled from Manual.pdf page 1)
COLOURS = {'White': 'kit_White', 'Black': 'kit_Black', 'Light Blue': 'kit_LightBlue', 'Red': 'kit_Red',
           'Light Red': 'kit_LightRed', 'Translucent': 'kit_Translucent', 'Light Brown': 'kit_LightBrown',
           'Brown': 'kit_Brown'}
DIGIT_TOP = 182.9   # mm: the LP display's raised digits (above the display face) print white

bpy.ops.wm.read_factory_settings(use_empty=True)
parts = []
for p in sorted(glob.glob(os.path.join(SRC, '*', '*.STL'))):
    stem = os.path.splitext(os.path.basename(p))[0]
    if stem.startswith(SKIP): continue
    if os.path.basename(os.path.dirname(p))[0] not in '1234569': continue   # 7 (short sleeve) and 8 (stand) are not on the worn disk
    suffix = stem.rsplit('_', 1)[1].strip()
    bpy.ops.wm.stl_import(filepath=p)
    o = bpy.context.selected_objects[0]
    o.name = stem
    parts.append((o, suffix, stem.startswith(BLADE_B)))
print('[kit] parts', len(parts), flush=True)

def verts_mm(o):
    return np.array([o.matrix_world @ v.co for v in o.data.vertices])

def fit_circle(xy):
    # algebraic least squares: x^2 + y^2 + D x + E y + F = 0
    A = np.c_[xy[:, 0], xy[:, 1], np.ones(len(xy))]
    b = -(xy[:, 0] ** 2 + xy[:, 1] ** 2)
    D, E, F = np.linalg.lstsq(A, b, rcond=None)[0]
    cx, cy = -D / 2, -E / 2
    return cx, cy, math.sqrt(max(cx * cx + cy * cy - F, 0.0))

byname = {o.name: o for o, _, _ in parts}
# the forearm's axis: the arm sleeve's channel (F3), a circle fitted across x / y (the sleeve runs along z)
sleeve = verts_mm(byname['F3_Arm Sleeve_White'])
ring = sleeve[sleeve[:, 1] < 100.0]
ax, ay, ar = fit_circle(ring[:, [0, 1]])
# inner surface only: the points nearer the axis than the median
d = np.hypot(ring[:, 0] - ax, ring[:, 1] - ay)
inner = ring[d < np.median(d)]
ax, ay, ar = fit_circle(inner[:, [0, 1]])
wrist_z = float(sleeve[:, 2].max()) + 15.0   # the wrist just past the sleeve's hand end
print('[kit] forearm axis x %.1f y %.1f r %.1f mm, sleeve z %.1f..%.1f, wrist z %.1f' % (ax, ay, ar, sleeve[:, 2].min(), sleeve[:, 2].max(), wrist_z), flush=True)
# the hub: the main body ring (A1) fitted across x / z; blade B's pin is its centre (Manual steps 2 and 18)
hub = verts_mm(byname['A1_Mainbody1_White'])
hx, hz, hr = fit_circle(hub[:, [0, 2]])
hy = float(hub[:, 1].max())
print('[kit] hub centre x %.1f z %.1f r %.1f mm at y %.1f' % (hx, hz, hr, hy), flush=True)

def canon(p):   # kit mm -> canonical forearm frame, metres
    return np.array([(p[0] - ax), (p[2] - wrist_z), (p[1] - ay)]) / 1000.0

def canon_dir(n):
    return np.array([n[0], n[2], n[1]])

objects = {'KitBody': {}, 'KitBladeB': {}}
for o, suffix, is_b in parts:
    me = o.data
    # weld the STL's loose triangles, then smooth by angle so curved parts shade round and hard edges stay hard
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.005)
    bm.to_mesh(me); bm.free()
    me.shade_smooth()
    try:
        me.set_sharp_from_angle(angle=math.radians(32))
    except AttributeError:
        pass
    me.calc_loop_triangles()
    mw = o.matrix_world; nw = mw.to_3x3()
    corner_n = None
    try:
        corner_n = [nw @ cn.vector for cn in me.corner_normals]
    except AttributeError:
        pass
    group = objects['KitBladeB' if is_b else 'KitBody']
    for t in me.loop_triangles:
        mat = COLOURS.get(suffix, 'kit_White')
        if suffix == 'Black&White':
            ys = [(mw @ me.vertices[v].co)[1] for v in t.vertices]
            mat = 'kit_White' if min(ys) > DIGIT_TOP else 'kit_Black'
        tri = []
        for li in t.loops:
            v = me.loops[li].vertex_index
            pos = canon(mw @ me.vertices[v].co)
            n = corner_n[li] if corner_n is not None else (nw @ t.normal)
            n = canon_dir(np.array(n)); n = n / max(np.linalg.norm(n), 1e-9)
            tri.append((pos, n))
        group.setdefault(mat, []).append(tri)

pivot_b = canon((hx, hy, hz))
with open(os.path.join(OUT, 'disk_classic.bytes'), 'wb') as f:
    f.write(b'WKIT'); f.write(struct.pack('<II', 1, len(objects)))
    for name, mats in objects.items():
        pivot = pivot_b if name == 'KitBladeB' else np.zeros(3)
        verts = []; index = {}; subs = []
        for mat, tris in mats.items():
            idx = []
            for tri in tris:
                ids = []
                for pos, n in tri:
                    key = (round(pos[0], 6), round(pos[1], 6), round(pos[2], 6), round(n[0], 3), round(n[1], 3), round(n[2], 3))
                    if key not in index:
                        index[key] = len(verts); verts.append((*(pos - pivot), *n))
                    ids.append(index[key])
                idx += [ids[0], ids[2], ids[1]]   # reversed: the frame map is a reflection (left-handed Unity)
            subs.append((mat, idx))
        nb = name.encode(); f.write(struct.pack('<H', len(nb))); f.write(nb)
        f.write(struct.pack('<3f', *pivot))
        V = np.array(verts, dtype=np.float32)
        f.write(struct.pack('<I', len(V))); f.write(V.tobytes())
        f.write(struct.pack('<I', len(subs)))
        for mat, idx in subs:
            mb = mat.encode(); f.write(struct.pack('<H', len(mb))); f.write(mb)
            f.write(struct.pack('<I', len(idx))); f.write(np.array(idx, dtype=np.uint32).tobytes())
        P = V[:, :3] + pivot.astype(np.float32)
        print('[kit] %s: %d verts, %d tris, bounds %s .. %s m, pivot %s' % (name, len(V), sum(len(i) for _, i in subs) // 3,
              P.min(0).round(3), P.max(0).round(3), pivot.round(4)), flush=True)

if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    cols = {'White': (0.90, 0.91, 0.93), 'Black': (0.17, 0.17, 0.17), 'Light Blue': (0.28, 0.45, 0.69), 'Red': (0.76, 0.23, 0.27),
            'Light Red': (0.96, 0.68, 0.79), 'Translucent': (0.84, 0.85, 0.87), 'Light Brown': (0.47, 0.33, 0.24), 'Brown': (0.30, 0.19, 0.18),
            'Black&White': (0.15, 0.15, 0.16)}
    for o, suffix, _ in parts:
        o.color = (*cols.get(suffix, (0.9, 0.9, 0.9)), 1.0)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = 1400; scene.render.resolution_y = 1000
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
    centre = np.mean([verts_mm(o).mean(0) for o, _, _ in parts], axis=0)
    from mathutils import Vector
    c = Vector(centre)
    for tag, eye in {'cover': (-0.55, 0.85, -0.45), 'top': (0.0, 1.0, 0.0001), 'side': (-1.0, 0.25, 0.0), 'under': (0.2, -1.0, 0.1)}.items():
        e = Vector(eye).normalized()
        # Blender is z-up; the kit's mm frame was imported as-is (its y is Blender's y): look from the eye direction
        cam.location = c + e * 1100.0
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y' if tag != 'top' else 'Z').to_euler()
        cam.data.lens = 50; cam.data.clip_end = 10000
        scene.render.filepath = os.path.join(RENDER, 'kit_%s.png' % tag)
        bpy.ops.render.render(write_still=True)
    print('[kit] renders in', RENDER, flush=True)
