"""Meshy wardrobe: fit the owner's Meshy image-to-3D pieces (Assets/Models/Characters/**.blend) onto the ArgonAvatarRig
bodies as skinned wardrobe items (Clothes/, Hair/) for OverworldAvatar3d + AvatarCustomizerUI.

  blender -b --python Tools/avatar/meshy_wardrobe.py -- --out <dir> [--only <substr>] [--sex male|female] [--list]

Per piece: append the Meshy mesh -> decimate -> fit to the body region of its slot (tops / bottoms also pose the body's
arms / legs to the garment's sleeves / legs, bind there and un-pose with inverse linear-blend skinning) -> skin weights
from the nearest body surface of the matching region -> restrict to the slot's bones -> push out of the skin -> write a
downscaled base-colour PNG -> FBX (mesh + rig, the slot_clothes.py export settings) -> review renders -> report JSON.
Then: python3 Tools/avatar/meshy_wardrobe_install.py <dir>   (copies into Resources, hide maps, thumbs, hair catalogue).

Hair / hats: the Meshy mannequin (head or bust) is removed after an ICP fit of OUR face onto it; head presets (Para / Dox)
replace the body's head (the install step writes a hide map covering the head). Bodies: build/custom/argon_avatar_<sex>.glb
(the meshes the Unity bases were exported from; identical rest skeleton, so the grafts bind by bone name).
"""
import bpy, bmesh, sys, os, json, math, time, zlib, struct, glob
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

T0 = time.time()
def log(*a): print('[wardrobe %6.1fs]' % (time.time() - T0), *a, flush=True)

PROJ = '/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON'
MODELS = f'{PROJ}/Assets/Models/Characters'
EXTRA = os.path.expanduser('~/Downloads')
BODY_GLB = '/home/greatsaiyadave/argon-avatar/build/custom/argon_avatar_{sex}.glb'
SCALP_MASK = f'{PROJ}/Assets/Resources/WRLDZ/Avatar3D/Textures/scalp_{{sex}}_mask.png'

# ---------------------------------------------------------------------------------------------------------- pieces
# (file substring, item id, kind, bodies, display name, blurb, flags)
# kind: top | bottoms | shoes | hair | hat | head | glasses | neck | glove.   bodies: male | female | both
# flags: hides_hair, price (player shop items), full (full-length trousers: hem stretched to the ankle)
P = lambda f, i, k, s, n, b, **fl: dict(file=f, id=i, kind=k, sex=s, name=n, blurb=b, **fl)
PIECES = [
    P('bakura_canvas_sneaker', 'shoes_bakura_3d', 'shoes', 'male', "Bakura's canvas sneakers", 'Blue-and-white canvas sneakers.'),
    P('bakura_denim_pants', 'bottoms_bakura_3d', 'bottoms', 'male', "Bakura's jeans", 'Blue denim jeans.', full=True),
    P('bakura_sweater_top', 'shirt_bakura_3d', 'top', 'male', "Bakura's sweater", 'Cream V-neck sweater over a green collared shirt.'),
    P('bandit_keith_bandana', 'hat_keith_3d', 'hat', 'male', "Keith's bandana", 'Stars-and-stripes bandana over his hair.', hides_hair=True),
    P('bandit_keith_cargo', 'bottoms_keith_3d', 'bottoms', 'male', "Keith's cargo pants", 'Khaki cargo trousers.', full=True),
    P('bandit_keith_combat', 'shoes_keith_3d', 'shoes', 'male', "Keith's combat boots", 'Black combat boots.'),
    P('bandit_keith_vest', 'shirt_keith_3d', 'top', 'male', "Keith's vest", 'Studded navy vest over a red tee.'),
    P('bonz_hoodie', 'shirt_bonz_3d', 'top', 'male', "Bonz's hoodie", 'Olive hoodie, hood down.'),
    P('bonz_padded_sneaker', 'shoes_bonz_3d', 'shoes', 'male', "Bonz's sneakers", 'Blue padded high-tops.'),
    P('bonz_purple_shorts', 'bottoms_bonz_3d', 'bottoms', 'male', "Bonz's shorts", 'Violet shorts.'),
    P('bonz_spiky_blue_wig', 'bonz_3d', 'hair', 'both', 'Bonz', 'Bonz'),
    P('player_crimson_black', 'shirt_crimson_3d', 'top', 'both', 'Crimson tech jacket', 'Black jacket with crimson panels.', price=1400),
    # red_duelist_glove: Meshy sculpted it as a closed fist, so it cannot wrap the animated open hand; left out until an
    # open-hand generation exists (the 'glove' kind below still handles one).
    P('techwear_cargo_pants', 'bottoms_techwear_3d', 'bottoms', 'both', 'Techwear cargo pants', 'Black cargo trousers with red trim.', price=900, full=True),
    P('techwear_sneaker', 'shoes_techwear_3d', 'shoes', 'both', 'Techwear sneakers', 'Black-and-red high-top sneakers.', price=380),
    P('gong_green_tunic', 'shirt_dox_3d', 'top', 'male', "Dox's tunic", 'Green tunic with black sleeves.'),
    P('mei_orange_tunic', 'shirt_para_3d', 'top', 'male', "Para's tunic", 'Orange tunic with black sleeves.'),
    P('gong_head_preset', 'hat_dox_head_3d', 'head', 'male', "Dox's head", 'Dox: bald head, 宮 on the brow.', hides_hair=True),
    P('mei_head_preset', 'hat_para_head_3d', 'head', 'male', "Para's head", 'Para: bald head, 迷 on the brow.', hides_hair=True),
    P('joey_casual_loafers', 'shoes_joey_3d', 'shoes', 'male', "Joey's loafers", 'Black loafers.'),
    P('joey_jacket_top', 'shirt_joey_3d', 'top', 'male', "Joey's jacket", 'Green jacket over a white tee.'),
    P('joey_wheeler_hair', 'joey_wheeler_3d', 'hair', 'both', 'Joey Wheeler', 'Joey Wheeler'),
    P('kaiba_hair', 'seto_kaiba_3d', 'hair', 'both', 'Seto Kaiba', 'Seto Kaiba', fit_mode='cavity', fit_dz=-0.045),   # black face on a black bust: no face to match
    P('kaiba_trench_shoes', 'shoes_kaiba_3d', 'shoes', 'male', "Kaiba's shoes", 'Black dress shoes.'),
    P('kaiba_trench_top', 'shirt_kaiba_3d', 'top', 'male', "Kaiba's trench coat", 'Indigo trench coat over the teal shirt.'),
    P('mai_hair', 'mai_valentine_3d', 'hair', 'both', 'Mai Valentine', 'Mai Valentine', long=True),
    P('mai_jacket_corset', 'shirt_mai_3d', 'top', 'female', "Mai's jacket", 'Purple jacket over the white corset top.'),
    P('mai_miniskirt', 'bottoms_mai_3d', 'bottoms', 'female', "Mai's skirt", 'Purple mini skirt with belt.'),
    P('mai_thigh_high_boots', 'shoes_mai_3d', 'shoes', 'female', "Mai's boots", 'Purple thigh-high boots.'),
    P('mako_accurate_long', 'mako_3d', 'hair', 'both', 'Mako Tsunami', 'Mako Tsunami', long=True),
    P('mako_cropped_denim', 'bottoms_mako_3d', 'bottoms', 'male', "Mako's jeans", 'Cropped navy jeans.'),
    P('mako_cuffed_fisherman', 'shoes_mako_3d', 'shoes', 'male', "Mako's boots", 'Brown cuffed fisherman boots.'),
    P('millennium_key', 'neck_mkey_3d', 'neck', 'male', 'Millennium Key', 'The Millennium Key, worn at the chest.'),
    P('millennium_ring', 'neck_mring_3d', 'neck', 'both', 'Millennium Ring', 'The Millennium Ring on its cord.'),
    P('mokuba_layered_top', 'shirt_mokuba_3d', 'top', 'male', "Mokuba's top", 'Red top, striped sleeves, blue bandana.'),
    P('mokuba_skate_sneaker', 'shoes_mokuba_3d', 'shoes', 'male', "Mokuba's sneakers", 'Blue-and-black skate sneakers.'),
    P('mokuba_spiky_mane_wig', 'mokuba_kaiba_3d', 'hair', 'both', 'Mokuba Kaiba', 'Mokuba Kaiba'),
    P('mokuba_teal_trousers', 'bottoms_mokuba_3d', 'bottoms', 'male', "Mokuba's trousers", 'Teal trousers.', full=True),
    P('panik_ankle_strap', 'shoes_panik_3d', 'shoes', 'male', "PaniK's boots", 'Black ankle-strap boots.'),
    P('panik_cap_winged_wig', 'hat_panik_3d', 'hat', 'male', "PaniK's cap", 'Navy cap with his winged hair.', hides_hair=True),
    P('panik_dark_trousers', 'bottoms_panik_3d', 'bottoms', 'male', "PaniK's trousers", 'Black trousers.', full=True),
    P('panik_trench_coat', 'shirt_panik_3d', 'top', 'male', "PaniK's coat", 'Dark trench coat with silver trim.'),
    P('pegasus_flat_dress', 'shoes_pegasus_3d', 'shoes', 'male', "Pegasus's shoes", 'White dress shoes.'),
    P('pegasus_hair', 'pegasus_mane_3d', 'hair', 'both', 'Maximillion Pegasus', 'Maximillion Pegasus', long=True),
    P('pegasus_red_slacks', 'bottoms_pegasus_3d', 'bottoms', 'male', "Pegasus's slacks", 'Red suit trousers.', full=True),
    P('pegasus_suit_jacket', 'shirt_pegasus_3d', 'top', 'male', "Pegasus's suit", 'Red suit jacket with a ruffled cravat.'),
    P('rex_raptor_accurate', 'shoes_rex_3d', 'shoes', 'male', "Rex's sneakers", 'Blue-and-white sneakers.'),
    P('ryo_bakura_hair', 'ryou_bakura_3d', 'hair', 'both', 'Ryou Bakura', 'Ryou Bakura', long=True),
    P('shadi_turban', 'hat_shadi_3d', 'hat', 'male', "Shadi's turban", 'White turban.', hides_hair=True),
    P('shadi_white_ankle', 'shoes_shadi_3d', 'shoes', 'male', "Shadi's shoes", 'White ankle shoes.'),
    P('shadi_white_robe', 'shirt_shadi_3d', 'top', 'male', "Shadi's robe", 'White hooded robe, full length.'),
    P('tea_gardner_hair', 'tea_gardner_3d', 'hair', 'both', 'Téa Gardner', 'Téa Gardner'),
    P('tea_uniform_shoes', 'shoes_tea_3d', 'shoes', 'female', "Téa's loafers", 'Brown loafers with white socks.'),
    P('tea_uniform_skirt', 'bottoms_tea_3d', 'bottoms', 'female', "Téa's skirt", 'Blue pleated uniform skirt.'),
    P('tea_uniform_top', 'shirt_tea_3d', 'top', 'female', "Téa's uniform", 'Pink Domino High blazer with a blue bow.'),
    P('tristan_taylor_hair', 'tristan_quiff_3d', 'hair', 'both', 'Tristan Taylor', 'Tristan Taylor'),
    P('weevil_bowl_cut_wig', 'weevil_bowl_3d', 'hair', 'both', 'Weevil Underwood', 'Weevil Underwood', fit_dz=0.02),
    P('weevil_dark_trousers', 'bottoms_weevil_3d', 'bottoms', 'male', "Weevil's trousers", 'Black trousers.', full=True),
    P('weevil_glasses_prop', 'face_weevil_3d', 'glasses', 'male', "Weevil's glasses", 'Round gold glasses with the horned bridge.'),
    P('weevil_green_tailcoat', 'shirt_weevil_3d', 'top', 'male', "Weevil's tailcoat", 'Green tailcoat with a red bow tie.'),
    P('yami_bakura_hair', 'yami_bakura_3d', 'hair', 'both', 'Yami Bakura', 'Yami Bakura', long=True),
    P('yugi_hair_volumetric', 'yugi_muto_3d', 'hair', 'both', 'Yugi Muto', 'Yugi Muto'),
    P('yugi_jacket_no_straps', 'shirt_yugi_3d', 'top', 'male', "Yugi's jacket", 'Blue Domino High jacket over the black shirt.'),
    P('female_yugi_jacket_buttoned', 'shirt_yugi_3d', 'top', 'female', "Yugi's jacket", 'Blue Domino High jacket, buttoned.'),
    P('yugi_necklace_strap', 'neck_yugi_3d', 'neck', 'both', 'Puzzle cord', 'Leather cord for the Millennium Puzzle.'),
    P('yugi_uniform_pants', 'bottoms_yugi_3d', 'bottoms', 'male', "Yugi's trousers", 'Blue Domino High trousers.', full=True),
    P('yugi_uniform_shoes', 'shoes_yugi_3d', 'shoes', 'male', "Yugi's sneakers", 'Navy-and-white sneakers.'),
]
TRIS = dict(top=12000, bottoms=10000, shoes=5000, hair=14000, hat=10000, head=9000, glasses=3000, neck=2500, glove=4000)
TEX = dict(top=2048, bottoms=2048, shoes=1024, hair=2048, hat=2048, head=2048, glasses=1024, neck=1024, glove=1024)
EASE = dict(top=0.016, bottoms=0.012)
CLEAR = dict(top=0.005, bottoms=0.005, shoes=0.004, hair=0.002, hat=0.002, glasses=0.002, neck=0.003)

def find_file(sub):
    for root in (MODELS, EXTRA):
        for f in sorted(glob.glob(f'{root}/**/*.blend', recursive=True)):
            if sub in os.path.basename(f): return f
    return None

# ---------------------------------------------------------------------------------------------------------- body
B = 'mixamorig:'
REGIONS = {
    'torso': ('Hips', 'Spine', 'Spine1', 'Spine2', 'Neck'),
    'head': ('Head', 'HeadTop_End'),
    'armL': ('LeftShoulder', 'LeftArm', 'LeftForeArm'), 'armR': ('RightShoulder', 'RightArm', 'RightForeArm'),
    'legL': ('LeftUpLeg', 'LeftLeg'), 'legR': ('RightUpLeg', 'RightLeg'),
    'footL': ('LeftFoot', 'LeftToeBase', 'LeftToe_End'), 'footR': ('RightFoot', 'RightToeBase', 'RightToe_End'),
}

class Body:
    def __init__(self, sex):
        self.sex = sex
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=BODY_GLB.format(sex=sex))
        self.arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
        self.body = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('ArgonBody'))
        for o in list(bpy.data.objects):
            if o.type == 'MESH' and o is not self.body: bpy.data.objects.remove(o, do_unlink=True)
        self.keep = set(bpy.data.objects)
        me = self.body.data
        self.MW = self.body.matrix_world.copy()
        self.P = np.array([self.MW @ v.co for v in me.vertices])
        self.bones = [b.name for b in self.arm.data.bones]
        self.bi = {n: i for i, n in enumerate(self.bones)}
        W = np.zeros((len(me.vertices), len(self.bones)))
        gi = {g.index: g.name for g in self.body.vertex_groups}
        for v in me.vertices:
            for g in v.groups:
                n = gi.get(g.group)
                if n in self.bi: W[v.index, self.bi[n]] = g.weight
        W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
        self.W = W
        self.head_pos = {b.name: np.array(self.arm.matrix_world @ b.head_local) for b in self.arm.data.bones}
        self.tail_pos = {b.name: np.array(self.arm.matrix_world @ b.tail_local) for b in self.arm.data.bones}
        rs = {}
        for r, names in REGIONS.items():
            cols = [self.bi[B + n] for n in names if B + n in self.bi]
            rs[r] = W[:, cols].sum(1)
        for s in 'LR':
            cols = [i for i, n in enumerate(self.bones) if (('LeftHand' if s == 'L' else 'RightHand') in n)]
            rs['hand' + s] = W[:, cols].sum(1)
        self.region_names = list(rs)
        R = np.stack([rs[r] for r in self.region_names], 1)
        self.region = np.array(self.region_names)[R.argmax(1)]
        me.calc_loop_triangles()
        self.tris = np.array([[l.vertices[0], l.vertices[1], l.vertices[2]] for l in me.loop_triangles])
        uv = me.uv_layers.active.data
        self.tri_uv = np.array([[uv[l.loops[k]].uv[:] for k in range(3)] for l in me.loop_triangles])
        self.tri_region = self.region[self.tris[:, 0]]
        self.landmarks()
        # scalp = the hair-bearing part of the head: above the brow line, or the back half above the ear line
        # (Textures/scalp_<sex>_mask.png marks the whole head island, face included, so it cannot tell them apart)
        L = self.L; cz = self.P[self.tris].mean(1)
        self.tri_scalp = (self.tri_region == 'head') & ((cz[:, 2] > L['brow_z'] + 0.012) | ((cz[:, 1] > L['head_y'] + 0.015) & (cz[:, 2] > L['eye_z'] - 0.03)))
        log(f'body {sex}: {len(self.P)} verts, {len(self.tris)} tris, shoulder joint x={self.L["x_joint"]:.3f} '
            f'top z={self.L["shoulder_top_z"]:.3f} hw={self.L["shoulder_hw"]:.3f}, waist z={self.L["waist_z"]:.3f} '
            f'hip hw={self.L["hip_hw"]:.3f}, head hw={self.L["head_hw"]:.3f} face y={self.L["face_front_y"]:.3f} '
            f'chin z={self.L["chin_z"]:.3f}, foot len={self.L["foot_len"]:.3f}')

    def hp(self, n): return self.head_pos[B + n]
    def tp(self, n): return self.tail_pos[B + n]

    def landmarks(self):
        P, R = self.P, self.region
        L = {}
        xj, yj, zj = self.hp('LeftArm')
        L['x_joint'], L['z_joint'] = float(xj), float(zj)
        m = (np.abs(P[:, 0]) > 0.10) & (np.abs(P[:, 0]) < 0.21) & ((R == 'armL') | (R == 'armR') | (R == 'torso')) & (P[:, 2] > zj - 0.05)
        L['shoulder_top_z'] = float(np.percentile(P[m, 2], 98))
        m = (P[:, 2] > zj - 0.02) & (P[:, 2] < zj + 0.06)
        L['shoulder_hw'] = float(np.abs(P[m, 0]).max())
        torso = R == 'torso'
        m = torso & (P[:, 2] > zj - 0.20) & (P[:, 2] < zj - 0.08)
        L['chest_y'] = float(P[m, 1].mean()); L['chest_hw'] = float(np.percentile(np.abs(P[m, 0]), 97))
        L['chest_front_y'] = float(np.percentile(P[m, 1], 1))
        hz = self.hp('Hips')[2]; s1 = self.hp('Spine1')[2]
        best = (9, 0)
        for z in np.arange(hz, s1, 0.01):
            mm = torso & (P[:, 2] >= z) & (P[:, 2] < z + 0.01)
            if mm.sum() > 5:
                hw = np.abs(P[mm, 0]).max()
                if hw < best[0]: best = (hw, z)
        L['waist_hw'], L['waist_z'] = float(best[0]), float(best[1])
        m = (torso | (R == 'legL') | (R == 'legR')) & (P[:, 2] > hz - 0.12) & (P[:, 2] < hz + 0.02)
        L['hip_hw'] = float(np.abs(P[m, 0]).max()); L['hip_y'] = float(P[m, 1].mean())
        L['crotch_z'] = float(self.hp('LeftUpLeg')[2] - 0.03)
        L['ankle_z'] = float(self.hp('LeftFoot')[2])
        prof = {}
        for z in np.arange(0.9, L['shoulder_top_z'] + 0.02, 0.01):
            mm = torso & (P[:, 2] >= z) & (P[:, 2] < z + 0.01)
            prof[round(float(z), 2)] = float(np.abs(P[mm, 0]).max()) if mm.sum() > 3 else 0.0
        L['torso_prof'] = prof
        for s in 'LR':
            f = R == 'foot' + s
            L['foot_len' if s == 'L' else 'foot_len_R'] = float(P[f, 1].max() - P[f, 1].min())
            L['foot_heel_y_' + s] = float(P[f, 1].max()); L['foot_x_' + s] = float(P[f, 0].mean())
            d = self.tp(('Left' if s == 'L' else 'Right') + 'ToeBase') - self.hp(('Left' if s == 'L' else 'Right') + 'Foot')
            L['foot_yaw_' + s] = float(math.atan2(d[0], -d[1]))
        hd = self.hp('Head'); L['head_z'] = float(hd[2])
        L['eye_z'] = float(hd[2] + 0.031); L['brow_z'] = float(hd[2] + 0.055); L['hairline_z'] = float(hd[2] + 0.10)
        head = R == 'head'
        L['head_y'] = float(P[head, 1].mean())
        m = head & (np.abs(P[:, 2] - L['eye_z']) < 0.015)
        L['head_hw'] = float(np.abs(P[m, 0]).max()); L['face_front_y'] = float(P[m, 1].min())
        mt = head & (np.abs(P[:, 2] - L['brow_z']) < 0.012) & (P[:, 1] < L['head_y'] - 0.01)
        L['temple_hw'] = float(np.abs(P[mt, 0]).max()) if mt.sum() else L['head_hw'] - 0.015
        front = head & (P[:, 1] < L['head_y'] - 0.03)
        L['chin_z'] = float(P[front, 2].min())
        L['crown_z'] = float(P[head, 2].max())
        self.face_idx = np.where(head & (P[:, 1] < L['head_y'] - 0.005) & (P[:, 2] > L['chin_z'] - 0.005) & (P[:, 2] < L['brow_z'] + 0.015))[0]
        self.skull_idx = np.where(head & (P[:, 2] > L['brow_z']))[0]
        self.head_idx = np.where(head)[0]
        m = torso & (P[:, 2] > self.hp('Neck')[2] - 0.02) & (P[:, 2] < self.hp('Neck')[2] + 0.02)
        L['neck_front_y'] = float(P[m, 1].min()) if m.sum() else L['chest_front_y']
        self.L = L

    def pose_bone(self, name, R3):
        """Rotate pose bone `name` by the world-space 3x3 rotation R3 about its head."""
        pb = self.arm.pose.bones[B + name]
        MW = self.arm.matrix_world
        head_w = MW @ pb.bone.head_local
        Rw = Matrix.Translation(head_w) @ Matrix(R3.tolist()).to_4x4() @ Matrix.Translation(-head_w)
        pb.matrix = MW.inverted() @ Rw @ MW @ pb.bone.matrix_local
        bpy.context.view_layer.update()

    def reset_pose(self):
        for pb in self.arm.pose.bones: pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()

    def posed_coords(self):
        dg = bpy.context.evaluated_depsgraph_get()
        ev = self.body.evaluated_get(dg)
        me = ev.to_mesh()
        P = np.array([self.MW @ v.co for v in me.vertices])
        ev.to_mesh_clear()
        return P

    def skin_matrices(self):
        MW = self.arm.matrix_world; MWi = MW.inverted()
        M = np.zeros((len(self.bones), 4, 4))
        for i, n in enumerate(self.bones):
            pb = self.arm.pose.bones[n]
            M[i] = np.array(MW @ pb.matrix @ pb.bone.matrix_local.inverted() @ MWi)
        return M

    def bvh(self, P, regions=None):
        sel = np.ones(len(self.tris), bool) if regions is None else np.isin(self.tri_region, list(regions))
        tris = self.tris[sel]
        return BVHTree.FromPolygons([Vector(p) for p in P], [tuple(int(i) for i in t) for t in tris]), tris, np.where(sel)[0]

    def axes_for(self, tri_sel, P=None):
        """Bone segment (a, b) of each selected triangle's dominant bone, in the given (posed) coordinates."""
        if not hasattr(self, '_tri_bone'):
            self._tri_bone = np.argmax(self.W[self.tris[:, 0]] + self.W[self.tris[:, 1]] + self.W[self.tris[:, 2]], axis=1)
        heads = np.array([self.head_pos[n] for n in self.bones]); tails = np.array([self.tail_pos[n] for n in self.bones])
        if P is not None and P is not self.P:   # posed: move the segments with the skin matrices
            M = self.skin_matrices()
            hh = np.einsum('bij,bj->bi', M, np.concatenate([heads, np.ones((len(heads), 1))], 1))[:, :3]
            tt = np.einsum('bij,bj->bi', M, np.concatenate([tails, np.ones((len(tails), 1))], 1))[:, :3]
            heads, tails = hh, tt
        bones = self._tri_bone[tri_sel]
        return heads[bones], tails[bones]

# ---------------------------------------------------------------------------------------------------------- helpers
def nearest_all(bvh, pts):
    out = np.zeros((len(pts), 3)); nrm = np.zeros((len(pts), 3)); idx = np.zeros(len(pts), int); dist = np.zeros(len(pts))
    for i, p in enumerate(pts):
        loc, n, fi, d = bvh.find_nearest(Vector(p))
        if loc is None: idx[i] = -1; dist[i] = 1e9; continue
        out[i] = loc; nrm[i] = n; idx[i] = fi; dist[i] = d
    return out, nrm, idx, dist

def signed_dist(bvh, pts, far=0.2, axes=None):
    """Signed distance to the body surface. Within 5 mm the nearest face's normal decides the sign; further out the
    'local origin' rule does (the body is not one closed shell, so ray parity fails in creases): a point nearer to the
    limb's bone axis than the skin point it is nearest to is inside. `axes` = (seg_a, seg_b) per BVH triangle. Without
    axes, anything beyond 3 cm counts as outside; `far` is only a sanity cap."""
    loc, n, idx, d = nearest_all(bvh, pts)
    s = np.sign(((pts - loc) * n).sum(1)); s[s == 0] = 1
    if axes is not None:
        a, b = axes[0][np.clip(idx, 0, len(axes[0]) - 1)], axes[1][np.clip(idx, 0, len(axes[1]) - 1)]
        def dist_seg(p):
            ab = b - a; t = np.clip(((p - a) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-9), 0, 1)[:, None]
            return np.linalg.norm(p - (a + t * ab), axis=1)
        inside_axis = dist_seg(pts) < dist_seg(loc)
        mid = d >= 0.005
        s[mid] = np.where(inside_axis[mid], -1, 1)
    else:
        s[d > 0.03] = 1
    s[d > far] = 1
    return d * s, loc, n, idx

def bary(p, a, b, c):
    v0 = b - a; v1 = c - a; v2 = p - a
    d00 = (v0 * v0).sum(1); d01 = (v0 * v1).sum(1); d11 = (v1 * v1).sum(1); d20 = (v2 * v0).sum(1); d21 = (v2 * v1).sum(1)
    den = d00 * d11 - d01 * d01; den[np.abs(den) < 1e-18] = 1e-18
    v = (d11 * d20 - d01 * d21) / den; w = (d00 * d21 - d01 * d20) / den
    v = np.clip(v, 0, 1); w = np.clip(w, 0, 1); u = np.clip(1 - v - w, 0, 1)
    s = np.maximum(u + v + w, 1e-9)
    return np.stack([u / s, v / s, w / s], 1)

def pca_axis(pts):
    c = pts.mean(0); u, s, vt = np.linalg.svd(pts - c, full_matrices=False)
    return c, vt[0], s

def rot_between(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    v = np.cross(a, b); c = float(np.dot(a, b))
    if np.linalg.norm(v) < 1e-9: return np.eye(3)
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx * (1 / (1 + c))

def write_png(path, arr):
    """8-bit PNG (grey, RGB or RGBA by channel count) written byte-exact, no colour management."""
    if arr.ndim == 2: arr = arr[:, :, None]
    h, w, ch = arr.shape
    ctype = {1: 0, 3: 2, 4: 6}[ch]
    raw = b''.join(b'\x00' + arr[y].astype(np.uint8).tobytes() for y in range(h))
    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, ctype, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b'')
    open(path, 'wb').write(png)

def mesh_coords(ob):
    n = len(ob.data.vertices); a = np.zeros(n * 3, dtype=np.float32)
    ob.data.vertices.foreach_get('co', a); return a.reshape(n, 3).astype(np.float64)
def set_coords(ob, P):
    ob.data.vertices.foreach_set('co', P.reshape(-1).astype(np.float32)); ob.data.update()

def adjacency(ob):
    n = len(ob.data.vertices); nb = [[] for _ in range(n)]
    for e in ob.data.edges:
        a, b = e.vertices; nb[a].append(b); nb[b].append(a)
    return nb

def smooth_field(D, nb, iters=1):
    for _ in range(iters):
        D2 = D.copy()
        for i, ns in enumerate(nb):
            if ns: D2[i] = max(D[i], float(np.mean(D[ns])))
        D = D2
    return D

# ---------------------------------------------------------------------------------------------------------- garment
def append_piece(path):
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = [n for n in src.objects]
    new = [o for o in dst.objects if o is not None and o.type == 'MESH']
    for o in new: bpy.context.scene.collection.objects.link(o)
    for o in dst.objects:
        if o is not None and o.type != 'MESH': bpy.data.objects.remove(o, do_unlink=True)
    if len(new) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for o in new: o.select_set(True)
        bpy.context.view_layer.objects.active = new[0]
        bpy.ops.object.join()
    ob = new[0]
    ob.matrix_world = Matrix.Identity(4)
    return ob

def base_colour_image(ob):
    for ms in ob.material_slots:
        m = ms.material
        if not m or not m.node_tree: continue
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                inp = n.inputs.get('Base Color')
                if inp and inp.is_linked and inp.links[0].from_node.type == 'TEX_IMAGE':
                    return inp.links[0].from_node.image
    imgs = [im for im in bpy.data.images if im.size[0] > 0]
    return max(imgs, key=lambda im: im.size[0]) if imgs else None

def decimate(ob, target):
    n = len(ob.data.polygons)
    ratio = min(1.0, max(0.01, target / max(n, 1)))
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    if ratio < 0.999:
        md = ob.modifiers.new('dec', 'DECIMATE'); md.ratio = ratio; md.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=md.name)
    ob.data.validate(); bpy.ops.object.shade_smooth()
    return n, len(ob.data.polygons)

def slice_hw(P, z0, z1, pct=98):
    m = (P[:, 2] >= z0) & (P[:, 2] < z1)
    return float(np.percentile(np.abs(P[m, 0]), pct)) if m.sum() > 3 else 0.0

def components(ob):
    n = len(ob.data.vertices); parent = np.arange(n)
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for e in ob.data.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b: parent[a] = b
    return np.array([find(i) for i in range(n)])

# ---------------------------------------------------------------------------------------------------------- fits
def shoulder_line(P, frac=None):
    """z and half-width of the garment's shoulder line. Scanning down from the collar the silhouette widens steeply
    along the shoulder slope, then slowly (the sleeves hang at 10-35 deg): the knee of the width curve is the shoulder
    point. `frac` (height fraction under the top) overrides the search for odd collars / hoods."""
    zmax, zmin = P[:, 2].max(), P[:, 2].min(); H = zmax - zmin
    if frac is not None:
        z = zmax - frac * H
        return z, slice_hw(P, z - 0.01 * H, z + 0.01 * H)
    step = 0.01 * H
    zs = np.arange(zmax - step, zmax - 0.45 * H, -step)
    hw = np.array([slice_hw(P, z - step, z) for z in zs])
    hw = np.convolve(np.pad(hw, 1, mode='edge'), [0.25, 0.5, 0.25], 'valid')
    slope = np.diff(hw) / step                       # widening per unit height, going down
    peak = int(np.argmax(slope[:len(slope) // 2]))   # the shoulder slope is the steepest widening in the upper half
    for i in range(peak + 1, len(slope)):
        if slope[i] < 0.8: return float(zs[i]), float(hw[i])
    i = min(peak + 3, len(hw) - 1)
    return float(zs[i]), float(hw[i])

def fit_top(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    z_sh, hw_sh = shoulder_line(P, piece.get('shoulder_frac'))
    target_hw = L['shoulder_hw'] + EASE['top']
    s = target_hw / hw_sh * piece.get('scale_fix', 1.0)
    P *= s; z_sh *= s
    P[:, 2] += (L['shoulder_top_z'] - 0.01) - z_sh
    rep['shoulder_frac'] = round(float((P[:, 2].max() - (L['shoulder_top_z'] - 0.01)) / (P[:, 2].max() - P[:, 2].min())), 3)
    zmin = P[:, 2].min()
    if zmin < 0.03:
        k = (L['shoulder_top_z'] - 0.03) / (L['shoulder_top_z'] - zmin)
        P[:, 2] = L['shoulder_top_z'] + (P[:, 2] - L['shoulder_top_z']) * k
        rep['shortened'] = round(float(k), 3)
    m = (P[:, 2] > L['shoulder_top_z'] - 0.26) & (P[:, 2] < L['shoulder_top_z'] - 0.10) & (np.abs(P[:, 0]) < L['chest_hw'])
    P[:, 1] += L['chest_y'] - (np.median(P[m, 1]) if m.sum() > 10 else np.median(P[:, 1]))
    set_coords(ob, P)
    rep.update(scale=round(float(s), 4), hem_z=round(float(P[:, 2].min()), 3), top_z=round(float(P[:, 2].max()), 3))
    sleeve, poses, tops = classify_sleeves(body, P, rep)
    if tops:
        # a sleeve's outer top is the shoulder point whatever the collar or hood does: re-scale and re-place on it
        z_sh2 = float(np.mean(tops)); hw_sh2 = slice_hw(P, z_sh2 - 0.025, z_sh2 + 0.005)
        s2 = target_hw / hw_sh2 if hw_sh2 > 0.05 else 1.0
        if 0.55 < s2 < 1.4:
            P *= s2; s *= s2; z_sh2 *= s2
            P[:, 2] += (L['shoulder_top_z'] - 0.01) - z_sh2
            m = (P[:, 2] > L['shoulder_top_z'] - 0.26) & (P[:, 2] < L['shoulder_top_z'] - 0.10) & (np.abs(P[:, 0]) < L['chest_hw'])
            P[:, 1] += L['chest_y'] - (np.median(P[m, 1]) if m.sum() > 10 else np.median(P[:, 1]))
            if P[:, 2].min() < 0.03:
                k = (L['shoulder_top_z'] - 0.03) / (L['shoulder_top_z'] - P[:, 2].min())
                P[:, 2] = L['shoulder_top_z'] + (P[:, 2] - L['shoulder_top_z']) * k
            set_coords(ob, P)
            rep.update(scale=round(float(s), 4), hem_z=round(float(P[:, 2].min()), 3), top_z=round(float(P[:, 2].max()), 3), sleeve_rescale=round(float(s2), 3))
            sleeve, poses, tops = classify_sleeves(body, P, rep)
    rep['sleeve_verts'] = int(sleeve['L'].sum() + sleeve['R'].sum())
    return P, sleeve, poses

def classify_sleeves(body, P, rep):
    """Sleeve vertices per side: the outer sleeve (well outside the torso's own width, within arm's reach of the body's
    arm) gives the sleeve axis by PCA; everything within 9 cm of that axis is sleeve. Returns the masks, the arm poses
    (body arm direction -> sleeve direction) and the sleeve tops (shoulder points)."""
    L = body.L; prof = L['torso_prof']
    thw = np.array([prof.get(round(float(np.clip(z, 0.9, L['shoulder_top_z'])), 2), L['chest_hw']) for z in P[:, 2]])
    sleeve = {}; poses = {}; tops = []
    for s_, sign in (('L', 1), ('R', -1)):
        pre = 'Left' if s_ == 'L' else 'Right'
        a0 = body.hp(pre + 'Arm'); a1 = body.hp(pre + 'Hand'); ax = (a1 - a0) / np.linalg.norm(a1 - a0)
        rel = P - a0; t = rel @ ax; perp = np.linalg.norm(rel - np.outer(t, ax), axis=1)
        side_ok = P[:, 0] * sign > 0
        outer = side_ok & (np.abs(P[:, 0]) > thw + 0.05) & (P[:, 2] < L['shoulder_top_z'] - 0.03) & (P[:, 2] > L['shoulder_top_z'] - 0.60) & (perp < 0.16)
        cls = np.zeros(len(P), bool)
        if outer.sum() > 80:
            c, d, sv = pca_axis(P[outer])
            if d[2] > 0: d = -d
            for _ in range(2):
                relg = P - c; tg = relg @ d; perpg = np.linalg.norm(relg - np.outer(tg, d), axis=1)
                cls = side_ok & (perpg < 0.09) & (tg > -0.08) & (P[:, 2] < L['shoulder_top_z'] + 0.01)
                if cls.sum() < 60: break
                c, d, sv = pca_axis(P[cls])
                if d[2] > 0: d = -d
            if cls.sum() >= 60:
                length = float(np.ptp(P[cls] @ d))
                relg = P[cls] - c; spread = float(np.sqrt((np.linalg.norm(relg - np.outer(relg @ d, d), axis=1) ** 2).mean()))
                ang = math.degrees(math.acos(float(np.clip(np.dot(d, ax), -1, 1))))
                tops.append(float(P[outer, 2].max()))
                # a real sleeve: long, thin, roughly along the arm and starting at the shoulder; anything else (a
                # vest's side panel, a coat flap) is left unposed, bound through the arm cone below
                top_ok = float(P[cls, 2].max()) > L['shoulder_top_z'] - 0.12
                if length > 0.2 and spread < 0.06 and ang < 30 and top_ok:
                    poses[s_] = (ax, d); rep[f'sleeve_{s_}_deg'] = round(ang, 1)
                else:
                    cls = np.zeros(len(P), bool); rep[f'sleeve_{s_}_rejected'] = [round(length, 2), round(spread, 3), round(ang, 1)]
        if cls.sum() < 60:
            # sleeves hanging tight against the torso: no outer-sleeve evidence, so take the body's own arm cone
            # (no pose: the sleeve already lies on the arm) rather than leaving sleeve vertices on torso bones
            cls = side_ok & (perp < 0.085) & (t > 0.03) & (P[:, 2] < L['shoulder_top_z'] - 0.02)
            if cls.sum() >= 60: rep[f'sleeve_{s_}_cone'] = int(cls.sum())
        sleeve[s_] = cls
    return sleeve, poses, tops

def fit_bottoms(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    zmax, zmin = P[:, 2].max(), P[:, 2].min(); H = zmax - zmin
    hw0 = slice_hw(P, zmax - 0.14 * H, zmax - 0.02 * H, 97)
    s = (L['hip_hw'] + EASE['bottoms']) / hw0
    P *= s
    top = L['waist_z'] + 0.02
    P[:, 2] += top - P[:, 2].max()
    zmin = P[:, 2].min()
    if piece.get('full'):
        want = L['ankle_z'] + 0.015
        k = (top - want) / (top - zmin); P[:, 2] = top + (P[:, 2] - top) * k
        rep['leg_stretch'] = round(float(k), 3)
    elif zmin < 0.02:
        k = (top - 0.02) / (top - zmin); P[:, 2] = top + (P[:, 2] - top) * k
    m = P[:, 2] > L['waist_z'] - 0.10
    P[:, 1] += L['hip_y'] - np.median(P[m, 1])
    set_coords(ob, P)
    rep.update(scale=round(float(s), 4), hem_z=round(float(P[:, 2].min()), 3))
    crotch = None
    for z in np.arange(L['waist_z'] - 0.02, P[:, 2].min(), -0.01):
        m = (P[:, 2] >= z) & (P[:, 2] < z + 0.01)
        if m.sum() > 3 and (np.abs(P[m, 0]) < 0.015).sum() == 0: crotch = z; break
    legs = {'L': np.zeros(len(P), bool), 'R': np.zeros(len(P), bool)}; poses = {}
    if crotch is not None and crotch - P[:, 2].min() > 0.15:
        rep['crotch_z'] = round(float(crotch), 3)
        for s_, sign in (('L', 1), ('R', -1)):
            pre = 'Left' if s_ == 'L' else 'Right'
            cls = (P[:, 2] < crotch - 0.04) & (P[:, 0] * sign > 0)
            legs[s_] = cls
            if cls.sum() > 60:
                c, d, sv = pca_axis(P[cls])
                if d[2] > 0: d = -d
                a0 = body.hp(pre + 'UpLeg'); a1 = body.hp(pre + 'Foot'); ax = (a1 - a0) / np.linalg.norm(a1 - a0)
                ang = math.degrees(math.acos(float(np.clip(np.dot(d, ax), -1, 1))))
                if 1.0 < ang < 35: poses[s_] = (ax, d); rep[f'leg_{s_}_deg'] = round(ang, 1)
    return P, legs, poses

def fit_shoes(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    comp = components(ob); xs = P[:, 0]
    cids = np.unique(comp); cx = {c: xs[comp == c].mean() for c in cids}
    left = np.isin(comp, [c for c in cids if cx[c] > 0]); right = ~left
    if left.sum() < 10 or right.sum() < 10:
        left = xs > xs.mean(); right = ~left
    out = P.copy()
    for s_, mask in (('L', left), ('R', right)):
        Q = P[mask]
        # the collar (heel end) is the taller end: make it +y (the body's heels point +y)
        ymin, ymax = Q[:, 1].min(), Q[:, 1].max(); span = ymax - ymin
        front_h = Q[Q[:, 1] < ymin + 0.25 * span][:, 2].max(); back_h = Q[Q[:, 1] > ymax - 0.25 * span][:, 2].max()
        if front_h > back_h:
            cy = (ymin + ymax) / 2; Q[:, 1] = 2 * cy - Q[:, 1]; rep[f'flipped_{s_}'] = True
        length = Q[:, 1].max() - Q[:, 1].min()
        s = (L['foot_len' if s_ == 'L' else 'foot_len_R'] + 0.03) / length
        Q = Q * s
        F = body.P[body.region == 'foot' + s_]
        # a Meshy shoe is slimmer than the foot: widen it to the foot plus 2 cm (the radial push does the rest)
        fw = (F[:, 0].max() - F[:, 0].min()) + 0.02; qw = Q[:, 0].max() - Q[:, 0].min()
        if 0 < qw < fw:
            cx = Q[:, 0].mean(); Q[:, 0] = cx + (Q[:, 0] - cx) * (fw / qw); rep[f'widen_{s_}'] = round(float(fw / qw), 3)
        yaw = L['foot_yaw_' + s_]; c, sn = math.cos(yaw), math.sin(yaw)
        ctr = Q.mean(0); R = np.array([[c, -sn, 0], [sn, c, 0], [0, 0, 1]])
        Q = (Q - ctr) @ R.T + ctr
        Q[:, 2] += (F[:, 2].min() - 0.004) - Q[:, 2].min()
        Q[:, 1] += (F[:, 1].max() + 0.012) - Q[:, 1].max()
        Q[:, 0] += F[:, 0].mean() - Q[:, 0].mean()
        out[mask] = Q
        rep[f'scale_{s_}'] = round(float(s), 3)
    set_coords(ob, out)
    rep['top_z'] = round(float(out[:, 2].max()), 3)
    return out, {'L': left, 'R': right}

def kd_of(P):
    kd = KDTree(len(P))
    for i, p in enumerate(P): kd.insert(Vector(p), i)
    kd.balance(); return kd

def icp_similarity(sets, s0, t0, iters=30, trim=0.7):
    """Move our points onto the piece with x' = s*x + t (no rotation). `sets` = [(src points (m), KD-tree, weight)]:
    the face against every piece vertex, the skull cap against the inward-facing (cavity) vertices. Returns s, t and the
    trimmed rms (m) of the first set."""
    s, t = s0, np.array(t0, float)
    def corr():
        xs_, ys_, ws_ = [], [], []
        for src, kd, w in sets:
            X = src * s + t
            Y = np.array([kd.find(Vector(x))[0][:] for x in X])
            d = np.linalg.norm(Y - X, axis=1)
            keep = d <= np.percentile(d, trim * 100)
            xs_.append(src[keep]); ys_.append(Y[keep]); ws_.append(np.full(keep.sum(), w))
        return np.concatenate(xs_), np.concatenate(ys_), np.concatenate(ws_)
    for it in range(iters):
        xs, ys, ws = corr()
        xm = (xs * ws[:, None]).sum(0) / ws.sum(); ym = (ys * ws[:, None]).sum(0) / ws.sum()
        num = (ws[:, None] * (ys - ym) * (xs - xm)).sum(); den = (ws[:, None] * (xs - xm) ** 2).sum()
        s_new = float(np.clip(num / den if den > 0 else s, 2.5, 9.0))   # a Meshy head is 0.4-1.3 units wide in a ~1.9 box
        t_new = ym - s_new * xm
        done = abs(s_new - s) < 1e-5 and np.linalg.norm(t_new - t) < 1e-5
        s, t = s_new, t_new
        if done: break
    src, kd, w = sets[0]
    X = src * s + t
    Y = np.array([kd.find(Vector(x))[0][:] for x in X])
    d = np.linalg.norm(Y - X, axis=1) / s
    keep = d <= np.percentile(d, trim * 100)
    return s, t, float(np.sqrt((d[keep] ** 2).mean()))

FIT_PRIOR = {}

def fit_headmount(body, ob, piece, rep):
    """Hair / hats / head presets: ICP our face (or skull cap for a faceless hat) onto the piece, then map the piece onto
    our head with the inverse transform."""
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    set_coords(ob, P)
    kd = kd_of(P)
    zmax, zmin = P[:, 2].max(), P[:, 2].min(); H = zmax - zmin; yc = float(np.median(P[:, 1]))
    N = np.zeros((len(P), 3), dtype=np.float32); ob.data.vertices.foreach_get('normal', N.reshape(-1))
    cen = P.mean(0); inward = ((cen - P) * N).sum(1) > 0
    kd_in = kd_of(P[inward]) if inward.sum() > 200 else kd
    cands = []
    # hair and head presets expose the mannequin's face (fitted against every vertex) while the skull cap is fitted into
    # the cavity (inward-facing vertices) so the cranium size follows the hair's inner shell; a hat has no face.
    face = (body.P[body.face_idx], kd, 1.0); skull = (body.P[body.skull_idx], kd_in, 0.5)
    mode = piece.get('fit_mode') or ('cavity' if piece['kind'] == 'hat' else ('face' if piece['kind'] == 'head' else 'face+cavity'))
    sets = [skull] if mode == 'cavity' else ([face] if mode == 'face' else [face, skull])
    anchor = np.array([0, L['head_y'], L['brow_z']]) if mode == 'cavity' else np.array([0, L['face_front_y'], L['eye_z']])
    crown = body.P[body.head_idx][:, 2].max(); hw_head = L['head_hw']
    prior = FIT_PRIOR.get(piece['id'])
    s_starts = tuple(prior * k for k in (0.9, 1.0, 1.12)) if prior else (2.6, 3.3, 4.1, 5.0, 6.0, 7.2, 8.4)
    for s0 in s_starts:
        for frac in (0.06, 0.18, 0.30, 0.42, 0.55):
            band = P[(P[:, 2] > zmax - (frac + 0.1) * H) & (P[:, 2] < zmax - frac * H)]
            ty = float(np.percentile(band[:, 1], 3)) if (mode != 'cavity' and len(band)) else yc
            t0 = np.array([0.0, ty, zmax - frac * H]) - anchor * s0
            s, t, rms = icp_similarity(sets, s0, t0)
            # a head covering reaches over the crown, is at least as wide as the head, and is no wider than ~3.5 heads
            top_b = (zmax - t[2]) / s; hw_b = (P[:, 0].max() - P[:, 0].min()) / (2 * s)
            penalty = (0.02 if top_b < crown - 0.015 else 0) + (0.02 if hw_b < hw_head * 0.9 else 0) \
                      + (0.02 if hw_b > hw_head * 3.5 else 0) + (0.02 if top_b > crown + 0.45 else 0)
            score = rms * (1 + 0.03 * s) + penalty
            cands.append((score, mode, s, t, rms, s0, frac))
    if prior:   # stay within a quarter of the male fit's scale
        cands = [c for c in cands if prior * 0.75 < c[2] < prior * 1.3] or cands
    cands.sort(key=lambda c: c[0])
    score, mode, s, t, rms, s0, frac = cands[0]
    if body.sex == 'male': FIT_PRIOR[piece['id']] = s
    rep.update(fit_mode=mode, fit_scale=round(float(s), 3), fit_rms_mm=round(rms * 1000, 1), fit_start=[s0, frac])
    P = (P - t) / s
    # per-piece hand corrections after review: fit_dz lifts (m), fit_scale_fix grows about the head bone
    if piece.get('fit_scale_fix'):
        hd = body.hp('Head'); P = hd + (P - hd) * piece['fit_scale_fix']
    if piece.get('fit_dz'): P[:, 2] += piece['fit_dz']
    set_coords(ob, P)
    return P

def face_colours(ob, img):
    """Base-colour texture (already scaled in place by export_texture) sampled at each face's UV centroid,
    (nfaces, 3) in 0..1."""
    if img is None: return None
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[:, :, :3]
    me = ob.data; uv = me.uv_layers.active.data
    out = np.zeros((len(me.polygons), 3), dtype=np.float32)
    for p in me.polygons:
        u = v = 0.0
        for li in p.loop_indices: u += uv[li].uv[0]; v += uv[li].uv[1]
        u /= len(p.loop_indices); v /= len(p.loop_indices)
        out[p.index] = px[int(np.clip(v * h, 0, h - 1)), int(np.clip(u * w, 0, w - 1))]
    return out

def remove_mannequin(body, ob, bvh_head, rep, img=None, long_hair=False):
    """Delete piece faces that are the Meshy mannequin: inside our body, or lying on our skin where there is no scalp,
    or (short hair) anything hugging the body below the neck, or anything painted in the mannequin's own colour."""
    me = ob.data
    P = mesh_coords(ob)
    cent = np.array([P[list(p.vertices)].mean(0) for p in me.polygons])
    sd, loc, n, idx = signed_dist(bvh_head[0], cent, axes=body.axes_for(bvh_head[2]))
    tri_idx = bvh_head[2][np.clip(idx, 0, len(bvh_head[2]) - 1)]
    scalp = body.tri_scalp[tri_idx]; region = body.tri_region[tri_idx]
    on_head = region == 'head'
    below_brow = cent[:, 2] < body.L['brow_z'] + 0.01
    # inside the skull / stuffing; on the face, ears or neck skin (a mannequin face differs from ours by up to 7 mm;
    # a fringe hangs lower than the brow but stands further off); on the bust. Faces over the scalp stay unless they
    # are clearly inside: the mannequin's own scalp and the hair's inner shell both sit there, under the hair.
    # (push_clear ran first, so skin-hugging mannequin faces sit 2-3 mm over our skin; hair proper stands further off.)
    kill = (sd < -0.003) | ((sd < 0.0045) & ~scalp & on_head) | ((sd < 0.007) & ~scalp & on_head & below_brow) | ((sd < 0.012) & ~on_head)
    # a mannequin face larger than ours sits up to 2.5 cm off our face as a shell PARALLEL to it; a fringe hanging
    # there is made of strands whose normals point every way
    fn = np.array([p.normal[:] for p in me.polygons])
    aligned = (fn * n).sum(1) > 0.8
    kill |= (sd < 0.025) & ~scalp & on_head & below_brow & aligned
    in_front = cent[:, 1] < body.L['face_front_y'] - 0.005
    kill |= (sd < 0.04) & ~scalp & on_head & below_brow & aligned & in_front
    # a bigger mannequin's nose / mouth / chin floats up to 7 cm in front of ours; no hairstyle covers the centre of
    # the face below the eyes (a fringe stops at the eyes, side locks stay past the cheeks)
    face_centre = (cent[:, 1] < body.L['face_front_y'] + 0.005) & (cent[:, 2] > body.L['chin_z'] - 0.015) & (cent[:, 2] < body.L['eye_z'] - 0.008) & (np.abs(cent[:, 0]) < 0.05)
    kill |= face_centre & (sd < 0.07)
    if not long_hair:
        # a short style never reaches below the neck base: whatever sits near the body there is the bust (a Meshy
        # bust is bigger than our shoulders, so allow 6 cm)
        kill |= (cent[:, 2] < body.hp('Neck')[2] - 0.02) & (sd < 0.06)
    cols = face_colours(ob, img)
    if cols is not None:
        # the mannequin's colour: faces in front of OUR face between chin and eyes (a fringe hangs higher); the hair's
        # colour: faces far from the body. When they differ, everything in the mannequin's colour near the body goes.
        L = body.L
        face_front = (cent[:, 1] < L['face_front_y'] + 0.01) & (cent[:, 2] > L['chin_z'] - 0.01) & (cent[:, 2] < L['eye_z']) & (np.abs(cent[:, 0]) < 0.06) & (sd < 0.06)
        far = sd > 0.04
        sat = cols.max(1) - cols.min(1)
        neutral_front = face_front & (sat < 0.25)      # a mannequin is white / grey / tan / black; a fringe is not
        if neutral_front.sum() > 20 and far.sum() > 50:
            mann = np.median(cols[neutral_front], axis=0); hair = np.median(cols[far], axis=0)
            rep['mannequin_rgb'] = [round(float(c), 2) for c in mann]; rep['hair_rgb'] = [round(float(c), 2) for c in hair]
            rep['mannequin_colour_rule'] = bool(np.linalg.norm(mann - hair) > 0.25)
            if np.linalg.norm(mann - hair) > 0.25:
                kill |= (np.linalg.norm(cols - mann, axis=1) < 0.16) & (sd < 0.08)
    if kill.sum() and kill.sum() < len(kill) * 0.95:
        bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.where(kill)[0]], context='FACES')
        bm.to_mesh(me); bm.free(); me.update()
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.delete_loose(); bpy.ops.object.mode_set(mode='OBJECT')
    rep['mannequin_faces_removed'] = int(kill.sum()); rep['faces_left'] = len(me.polygons)

def fit_glasses(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    width = P[:, 0].max() - P[:, 0].min()
    s = (2 * L['temple_hw'] + 0.012) / width     # frame as wide as the face at the temples (the ears sit further back)
    P *= s
    front = P[:, 1] < np.percentile(P[:, 1], 20)
    P[:, 2] += L['eye_z'] - P[front, 2].mean()
    P[:, 1] += (L['face_front_y'] - 0.012) - P[:, 1].min()
    set_coords(ob, P); rep['scale'] = round(float(s), 3)
    return P

def fit_neck(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    width = P[:, 0].max() - P[:, 0].min(); height = P[:, 2].max() - P[:, 2].min()
    if 'key' in piece['file']:
        s = 0.18 / height; P *= s
        P[:, 2] += (body.hp('Spine2')[2] + 0.06) - P[:, 2].max()
        P[:, 1] += (L['chest_front_y'] - 0.012) - P[:, 1].max()
    else:
        s = 0.12 / width; P *= s
        P[:, 2] += (body.hp('Neck')[2] + 0.012) - P[:, 2].max()
        P[:, 1] += (L['chest_front_y'] - 0.008) - float(np.percentile(P[:, 1], 50))
    set_coords(ob, P); rep['scale'] = round(float(s), 3)
    return P

def fit_glove(body, ob, piece, rep):
    P = mesh_coords(ob)
    comp = components(ob); cids = np.unique(comp); cx = {c: P[comp == c, 0].mean() for c in cids}
    left = np.isin(comp, [c for c in cids if cx[c] > 0])
    if left.sum() < 10 or (~left).sum() < 10: left = P[:, 0] > P[:, 0].mean()
    out = P.copy()
    for s_, mask in (('L', left), ('R', ~left)):
        pre = 'Left' if s_ == 'L' else 'Right'
        Q = P[mask]; c, d, sv = pca_axis(Q)
        ext = float(np.ptp(Q @ d))
        s = 0.115 / ext; Q = (Q - c) * s
        h0 = body.hp(pre + 'Hand')
        h1 = body.tp(pre + 'HandMiddle4') if B + pre + 'HandMiddle4' in body.head_pos else body.tp(pre + 'Hand')
        ax = (h1 - h0) / np.linalg.norm(h1 - h0)
        if d[2] > 0: d = -d
        R = rot_between(d, ax)
        Q = Q @ R.T + (h0 + ax * 0.065)
        out[mask] = Q; rep[f'scale_{s_}'] = round(float(s), 3)
    set_coords(ob, out)
    return out, {'L': left, 'R': ~left}

# ---------------------------------------------------------------------------------------------------------- skinning
ALLOWED = {
    'top': ('Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'LeftShoulder', 'LeftArm', 'LeftForeArm', 'RightShoulder', 'RightArm', 'RightForeArm'),
    'bottoms': ('Hips', 'Spine', 'LeftUpLeg', 'LeftLeg', 'RightUpLeg', 'RightLeg'),
    'shoes': ('LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', 'LeftToe_End', 'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase', 'RightToe_End'),
    'hair': ('Head', 'Neck', 'Spine2', 'Spine1'), 'hat': ('Head', 'Neck', 'Spine2'), 'head': ('Head', 'Neck'),
    'glasses': ('Head',), 'neck': ('Neck', 'Spine2', 'Spine1'),
}
def allowed_cols(body, kind):
    if kind == 'glove':
        return [i for i, n in enumerate(body.bones) if 'Hand' in n or 'ForeArm' in n]
    return [body.bi[B + n] for n in ALLOWED[kind] if B + n in body.bi]

def transfer_weights(body, Pbody, garment_pts, groups):
    """groups: list of (mask over garment verts, body region set). Nearest-surface barycentric weights."""
    W = np.zeros((len(garment_pts), len(body.bones)))
    for mask, regions in groups:
        if mask.sum() == 0: continue
        bvh, tris, _ = body.bvh(Pbody, regions)
        loc, n, idx, d = nearest_all(bvh, garment_pts[mask])
        idx = np.clip(idx, 0, len(tris) - 1)
        t = tris[idx]
        bc = bary(loc, Pbody[t[:, 0]], Pbody[t[:, 1]], Pbody[t[:, 2]])
        W[mask] = bc[:, 0:1] * body.W[t[:, 0]] + bc[:, 1:2] * body.W[t[:, 1]] + bc[:, 2:3] * body.W[t[:, 2]]
    return W

def restrict(body, W, kind, P):
    cols = allowed_cols(body, kind)
    keep = np.zeros(W.shape[1], bool); keep[cols] = True
    W = W * keep
    zero = W.sum(1) < 1e-6
    if zero.any():
        segs = [(c, body.head_pos[body.bones[c]], body.tail_pos[body.bones[c]]) for c in cols]
        for i in np.where(zero)[0]:
            best = (1e9, cols[0])
            for c, a, b in segs:
                ab = b - a; t = np.clip(np.dot(P[i] - a, ab) / max(np.dot(ab, ab), 1e-9), 0, 1)
                d = np.linalg.norm(P[i] - (a + t * ab))
                if d < best[0]: best = (d, c)
            W[i, best[1]] = 1
    W = W / W.sum(1, keepdims=True)
    k = np.argsort(-W, axis=1)[:, 4:]
    np.put_along_axis(W, k, 0, axis=1)
    return W / W.sum(1, keepdims=True)

def smooth_weights(W, nb, iters=2):
    for _ in range(iters):
        W2 = W.copy()
        for i, ns in enumerate(nb):
            if ns: W2[i] = 0.5 * W[i] + 0.5 * W[ns].mean(0)
        W = W2 / np.maximum(W2.sum(1, keepdims=True), 1e-9)
    return W

def apply_groups(body, ob, W):
    for vg in list(ob.vertex_groups): ob.vertex_groups.remove(vg)
    for j, n in enumerate(body.bones):
        col = W[:, j]; idx = np.where(col > 1e-4)[0]
        if len(idx) == 0: continue
        vg = ob.vertex_groups.new(name=n)
        for i in idx: vg.add([int(i)], float(col[i]), 'REPLACE')
    md = ob.modifiers.new('Armature', 'ARMATURE'); md.object = body.arm
    ob.parent = body.arm

def inverse_lbs(body, W, P):
    M = body.skin_matrices()
    Mv = np.einsum('nb,bij->nij', W, M)
    Mi = np.linalg.inv(Mv)
    Ph = np.concatenate([P, np.ones((len(P), 1))], 1)
    return np.einsum('nij,nj->ni', Mi, Ph)[:, :3]

def subdivide_long_edges(ob, max_len, passes=7):
    """Split garment edges longer than max_len (m): a decimated triangle spanning a limb cuts through it however far
    its corners are pushed out. Decimation leaves slivers as long as a trouser leg, so each pass halves until none
    is left (50 cm -> 3 cm takes five)."""
    for _ in range(passes):
        bm = bmesh.new(); bm.from_mesh(ob.data)
        long = [e for e in bm.edges if (e.verts[0].co - e.verts[1].co).length > max_len]
        if not long: bm.free(); break
        bmesh.ops.subdivide_edges(bm, edges=long, cuts=1, use_grid_fill=True)
        bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return len(ob.data.vertices)

def push_dirs(body, P, n, idx, axes, sel_idx):
    """Outward direction per vertex: radial from the nearest triangle's bone axis (the local origin), from the head
    centre for the head. Pushing along the nearest face normal folds a garment through a limb; radial inflates it."""
    a, b = axes[0][idx], axes[1][idx]
    ab = b - a; t = np.clip(((P - a) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-9), 0, 1)[:, None]
    d = P - (a + t * ab)
    reg = body.tri_region[sel_idx[np.clip(idx, 0, len(sel_idx) - 1)]]
    head_c = (body.hp('Head') + body.tp('Head')) / 2 + np.array([0, 0, 0.02])
    hm = reg == 'head'
    d[hm] = (P - head_c)[hm]
    L = np.linalg.norm(d, axis=1)
    d = d / np.maximum(L, 1e-9)[:, None]
    small = L < 1e-4
    d[small] = n[small]
    return d

def drop_deep_faces(body, ob, bvh, axes, depth, rep):
    """Delete faces with a corner deeper than `depth` inside the body: a garment's inner structure (collar
    linings, inner shirts under the shoulder mass) that no camera can see, and that a push would only tear."""
    P = mesh_coords(ob); me = ob.data
    sd, loc, n, idx = signed_dist(bvh, P, axes=axes)
    deep_v = sd < -depth
    kill = [p.index for p in me.polygons if any(deep_v[v] for v in p.vertices)]
    if kill:
        bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in kill], context='FACES')
        bm.to_mesh(me); bm.free(); me.update()
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.delete_loose(); bpy.ops.object.mode_set(mode='OBJECT')
    rep['deep_faces_dropped'] = len(kill)
    return len(kill)

def push_clear(body, ob, Pbody, regions, min_d, nb, rep, iters=8, cap=0.02, drop_deeper=None):
    bvh, tris, sel = body.bvh(Pbody, regions)
    axes = body.axes_for(sel)
    if drop_deeper and drop_deep_faces(body, ob, bvh, axes, drop_deeper, rep):
        nb = adjacency(ob)
    P = mesh_coords(ob)
    first = True
    for it in range(iters):
        sd, loc, n, idx = signed_dist(bvh, P, axes=axes)
        if first:
            rep['inside_before_mm'] = round(-float(sd.min()) * 1000, 1); rep['inside_count_before'] = int((sd < min_d).sum()); first = False
        need = np.clip(min_d - sd, 0, cap)
        if (need > 1e-5).sum() == 0: break
        need = smooth_field(need, nb, 1)
        P = P + push_dirs(body, P, n, idx, axes, sel) * need[:, None]
    sd, loc, n, idx = signed_dist(bvh, P, axes=axes)
    rep['inside_after_mm'] = round(-float(sd.min()) * 1000, 1); rep['inside_count_after'] = int((sd < min_d - 0.001).sum())
    set_coords(ob, P)
    # face centroids too: a vertex-only test misses triangles that cut through a bulge
    me = ob.data
    cent = np.array([P[list(p.vertices)].mean(0) for p in me.polygons])
    sdc, _, _, _ = signed_dist(bvh, cent, axes=axes)
    rep['faces_inside_after'] = int((sdc < 0).sum()); rep['faces'] = len(me.polygons)
    return P

# ---------------------------------------------------------------------------------------------------------- output
def export_texture(img, path, size, write=True):
    """Scale the piece's base colour in place to `size` (the mannequin colour test samples it afterwards) and write it
    byte-exact as PNG."""
    if img is None: return None
    w, h = img.size
    if max(w, h) > size: img.scale(size, size)
    w, h = img.size
    if not write: return [w, h]
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    rgb = np.clip(np.round(px[::-1, :, :3] * 255), 0, 255).astype(np.uint8)
    write_png(path, rgb)
    return [w, h]

def export_fbx(ob, arm, path, name):
    ob.name = name; ob.data.name = name
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, add_leaf_bones=False, bake_anim=False,
                             use_mesh_modifiers=False, mesh_smooth_type='FACE', path_mode='STRIP',
                             embed_textures=False, axis_forward='-Z', axis_up='Y')

def render_review(body, ob, path, kind, P, thumb=None):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_backface_culling = False
    sc.render.resolution_x = 360; sc.render.resolution_y = 520; sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    cam = bpy.data.objects.get('ReviewCam')
    if cam is None:
        cd = bpy.data.cameras.new('ReviewCam'); cam = bpy.data.objects.new('ReviewCam', cd); sc.collection.objects.link(cam)
    sc.camera = cam; cam.data.type = 'ORTHO'
    lo, hi = P.min(0), P.max(0)
    z0, z1 = lo[2] - 0.08, hi[2] + 0.08
    if kind == 'shoes' or kind == 'glove': z1 = hi[2] + 0.12
    if kind in ('hair', 'hat', 'head', 'glasses'): z0 = min(z0, body.L['chin_z'] - 0.12)
    ext = max((z1 - z0), (hi[0] - lo[0]) * 1.3, 0.3)
    cam.data.ortho_scale = ext * 1.08
    cz = (z0 + z1) / 2
    for tag, (px, py, rz) in {'front': (0, -6, 0), 'side': (6, 0, math.pi / 2), 'q': (4.2, -4.2, math.pi / 4), 'alone': (0, -6, 0)}.items():
        body.body.hide_render = tag == 'alone'
        cam.location = (px, py, cz); cam.rotation_euler = (math.pi / 2, 0, rz)
        sc.render.filepath = f'{path}_{tag}.png'
        bpy.ops.render.render(write_still=True)
    body.body.hide_render = False
    if thumb:
        sc.render.film_transparent = True
        sc.render.resolution_x = 256; sc.render.resolution_y = 256
        cam.data.ortho_scale = max(hi[2] - lo[2], hi[0] - lo[0]) * 1.25 + 0.04
        cam.location = (3.5, -4.5, (lo[2] + hi[2]) / 2 + 0.01); cam.rotation_euler = (math.pi / 2 - 0.12, 0, math.radians(38))
        sc.render.filepath = thumb
        bpy.ops.render.render(write_still=True)
        sc.render.film_transparent = False

# ---------------------------------------------------------------------------------------------------------- main
def process(body, piece, path, out):
    sex = body.sex
    rep = dict(id=piece['id'], kind=piece['kind'], sex=sex, file=os.path.basename(path), name=piece['name'])
    for o in list(bpy.data.objects):
        if o not in body.keep and o.type == 'MESH': bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.orphans_purge(do_recursive=True)
    body.reset_pose()
    ob = append_piece(path)
    img = base_colour_image(ob)
    n0, n1 = decimate(ob, TRIS[piece['kind']])
    rep.update(tris_in=n0, tris=n1)
    kind = piece['kind']
    nb = adjacency(ob)
    is_hair = kind == 'hair'
    tex_path = f'{out}/{"Hair_" if is_hair else "albedo_"}{piece["id"]}.png'
    rep['texture'] = export_texture(img, tex_path, TEX[kind], write=(sex == 'male' or piece['sex'] == 'female' or not os.path.exists(tex_path)))
    # clothes: fit (the scale is unknown before), split edges longer than 3 cm so no decimated triangle can span a
    # limb, then fit again on the refined mesh (the fits are idempotent on an already fitted garment)
    if kind == 'top':
        fit_top(body, ob, piece, rep); rep['scale_fit'] = rep.get('scale')
        rep['verts_subdiv'] = subdivide_long_edges(ob, 0.03); nb = adjacency(ob)
        P, sleeve, poses = fit_top(body, ob, piece, rep)
        for s_, (ax, d) in poses.items():
            body.pose_bone(('Left' if s_ == 'L' else 'Right') + 'Arm', rot_between(ax, d))
        Pb = body.posed_coords()
        rest = ~(sleeve['L'] | sleeve['R'])
        groups = [(sleeve['L'], {'armL'}), (sleeve['R'], {'armR'}), (rest, {'torso', 'head'})]
        W = transfer_weights(body, Pb, P, groups)
        W = restrict(body, smooth_weights(W, nb), kind, P)
        if poses:
            P = inverse_lbs(body, W, P); set_coords(ob, P)
        body.reset_pose()
        apply_groups(body, ob, W)
        P = push_clear(body, ob, body.P, None, CLEAR[kind], nb, rep, drop_deeper=(None if kind == 'shoes' else 0.05))
    elif kind == 'bottoms':
        fit_bottoms(body, ob, piece, rep); rep['scale_fit'] = rep.get('scale')
        rep['verts_subdiv'] = subdivide_long_edges(ob, 0.03); nb = adjacency(ob)
        P, legs, poses = fit_bottoms(body, ob, piece, rep)
        for s_, (ax, d) in poses.items():
            body.pose_bone(('Left' if s_ == 'L' else 'Right') + 'UpLeg', rot_between(ax, d))
        Pb = body.posed_coords()
        rest = ~(legs['L'] | legs['R'])
        groups = [(legs['L'], {'legL', 'footL'}), (legs['R'], {'legR', 'footR'}), (rest, {'torso', 'legL', 'legR'})]
        W = transfer_weights(body, Pb, P, groups)
        W = restrict(body, smooth_weights(W, nb), kind, P)
        if poses:
            P = inverse_lbs(body, W, P); set_coords(ob, P)
        body.reset_pose()
        apply_groups(body, ob, W)
        P = push_clear(body, ob, body.P, None, CLEAR[kind], nb, rep, drop_deeper=(None if kind == 'shoes' else 0.05))
    elif kind == 'shoes':
        fit_shoes(body, ob, piece, rep)
        rep['verts_subdiv'] = subdivide_long_edges(ob, 0.02); nb = adjacency(ob)
        P, sides = fit_shoes(body, ob, piece, rep)
        groups = [(sides['L'], {'legL', 'footL'}), (sides['R'], {'legR', 'footR'})]
        W = restrict(body, smooth_weights(transfer_weights(body, body.P, P, groups), nb), kind, P)
        apply_groups(body, ob, W)
        P = push_clear(body, ob, body.P, None, CLEAR[kind], nb, rep, drop_deeper=(None if kind == 'shoes' else 0.05))
    elif kind in ('hair', 'hat', 'head'):
        P = fit_headmount(body, ob, piece, rep)
        if kind != 'head':
            # first lift everything out of our skull (a cranium larger than the mannequin's would swallow the hair's
            # roots), then drop the mannequin's face / neck / bust, which now lie just over our skin
            P = push_clear(body, ob, body.P, None, CLEAR[kind], nb, rep, iters=3, cap=0.02)
            bvh_head = body.bvh(body.P, None)
            remove_mannequin(body, ob, bvh_head, rep, img=img, long_hair=piece.get('long', False))
            nb = adjacency(ob); P = mesh_coords(ob)
        all_ = np.ones(len(P), bool)
        W = restrict(body, smooth_weights(transfer_weights(body, body.P, P, [(all_, {'head', 'torso'})]), nb), kind, P)
        apply_groups(body, ob, W)
    elif kind == 'glasses':
        P = fit_glasses(body, ob, piece, rep)
        W = np.zeros((len(P), len(body.bones))); W[:, body.bi[B + 'Head']] = 1
        apply_groups(body, ob, W)
    elif kind == 'neck':
        P = fit_neck(body, ob, piece, rep)
        all_ = np.ones(len(P), bool)
        W = restrict(body, smooth_weights(transfer_weights(body, body.P, P, [(all_, {'torso', 'head'})]), nb), kind, P)
        apply_groups(body, ob, W)
        P = push_clear(body, ob, body.P, None, CLEAR[kind], nb, rep, iters=2, cap=0.02)
    elif kind == 'glove':
        P, sides = fit_glove(body, ob, piece, rep)
        groups = [(sides['L'], {'handL', 'armL'}), (sides['R'], {'handR', 'armR'})]
        W = restrict(body, smooth_weights(transfer_weights(body, body.P, P, groups), nb), kind, P)
        apply_groups(body, ob, W)
    tot = W.sum(0); top = np.argsort(-tot)[:6]
    rep['bones'] = {body.bones[i].replace(B, ''): round(float(tot[i]) / len(P), 3) for i in top if tot[i] > 0}
    rep['bbox'] = [[round(float(v), 3) for v in P.min(0)], [round(float(v), 3) for v in P.max(0)]]
    stem = f'{out}/{piece["id"]}_{sex}'
    render_review(body, ob, f'{out}/review_{piece["id"]}_{sex}', kind, P, thumb=f'{out}/thumb_{piece["id"]}.png' if is_hair and (sex == 'male' or piece['sex'] == 'female') else None)
    fbx = f'{out}/argon_{"hair_" if is_hair else ""}{piece["id"]}_{sex}.fbx'
    export_fbx(ob, body.arm, fbx, ('ArgonHair_' + piece['id']) if is_hair else 'ArgonCloth')
    rep['fbx'] = os.path.basename(fbx)
    if kind == 'head':
        sel = (body.tri_region == 'head') | ((body.tri_region == 'torso') & (body.P[body.tris[:, 0], 2] > body.hp('Neck')[2] - 0.01))
        rep['hide_uv_tris'] = body.tri_uv[sel].round(5).tolist()
    if kind == 'glove':
        sel = np.isin(body.tri_region, ['handL', 'handR'])
        rep['hide_uv_tris'] = body.tri_uv[sel].round(5).tolist()
    json.dump(rep, open(f'{stem}.json', 'w'), indent=1)
    keys = ('scale', 'fit_mode', 'fit_scale', 'fit_rms_mm', 'sleeve_L_deg', 'sleeve_R_deg', 'leg_L_deg', 'inside_before_mm', 'inside_after_mm', 'inside_count_after', 'mannequin_faces_removed', 'faces_left')
    log(f'{piece["id"]:22s} {sex:6s} tris {n0}->{n1} ' + ' '.join(f'{k}={rep[k]}' for k in keys if k in rep))
    return rep

def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = argv[argv.index('--out') + 1] if '--out' in argv else f'{PROJ}/Temp/MeshyWardrobe'
    only = argv[argv.index('--only') + 1] if '--only' in argv else None
    sex_f = argv[argv.index('--sex') + 1] if '--sex' in argv else None
    os.makedirs(out, exist_ok=True)
    jobs = []
    for pc in PIECES:
        if only and not any(o in pc['id'] or o in pc['file'] for o in only.split(',')): continue
        f = find_file(pc['file'])
        if not f: log('MISSING', pc['file']); continue
        for sex in (('male', 'female') if pc['sex'] == 'both' else (pc['sex'],)):
            if sex_f and sex != sex_f: continue
            jobs.append((sex, pc, f))
    if '--list' in argv:
        for sex, pc, f in jobs: print(sex, pc['id'], pc['kind'], os.path.basename(f))
        return
    reports = []
    for sex in ('male', 'female'):
        js = [j for j in jobs if j[0] == sex]
        if not js: continue
        body = Body(sex)
        for _, pc, f in js:
            try:
                reports.append(process(body, pc, f, out))
            except Exception as e:
                import traceback; traceback.print_exc()
                log('FAILED', pc['id'], sex, repr(e))
                reports.append(dict(id=pc['id'], sex=sex, error=repr(e)))
    json.dump(reports, open(f'{out}/reports_{int(time.time())}.json', 'w'), indent=1)
    log('done', len(reports), 'pieces')

if __name__ == '__main__':
    main()
