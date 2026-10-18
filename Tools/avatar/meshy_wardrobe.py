"""Meshy wardrobe v2: integrate the owner's Meshy image-to-3D pieces (Assets/Models/Characters/**.blend, ~/Downloads)
onto the ArgonAvatarRig bodies as skinned wardrobe items (Clothes/, Hair/) for OverworldAvatar3d + AvatarCustomizerUI
WITHOUT changing how they look (owner, 2026-10-18: "perfect them as they are and integrate them into the game";
"do not keep the mannequin parts").

  blender -b --python Tools/avatar/meshy_wardrobe.py -- --out <dir> [--only <substr,..>] [--sex male|female] [--list]
                                                        [--no-render] [--no-hide]

What this pass does to a sculpt — and nothing else:
  * places it: ONE uniform scale + translation (+ a yaw for shoes) chosen from body landmarks (shoulder width, hip
    width / leg length, foot length, an ICP fit of our face / skull into a hairstyle or hat);
  * articulates it: tops / bottoms are bound with the body's arms / legs posed along the sculpted sleeves / legs and
    un-posed through the rig (inverse linear-blend skinning), so the piece is exactly the sculpt whenever the limb is
    in the sculpted pose;
  * skins it: bone weights from the nearest body surface, restricted to the slot's bones, smoothed;
  * removes the Meshy mannequin (the bust / face a hairstyle or hat was generated on): faces painted in the
    mannequin's colour that lie on our aligned skin, plus whatever sits inside our body — never a hair-coloured face;
  * hides the body underneath: a body-UV hide map (the shells' 3 cm coverage rule, generous: 3 cm either side + 1.5
    cm nearest, per texel on hem faces) for garments; the scalp under hair and hats; the whole head under a head
    preset. Where a sculpt is narrower than the body the skin is hidden inside it instead of the sculpt being pushed.
  * exports the mesh at its full density with its own smooth normals (the Meshy custom normals are those same smooth
    normals to 0.04 deg) and writes the packed base-colour / normal-map JPEGs byte for byte (4096^2).
No decimation, no texture downscale, no non-uniform scaling, no vertex pushes, no face drops.

Output per piece: argon_<id>_<sex>.fbx (hair: argon_hair_<id>_<sex>.fbx), albedo_<id>.jpg / normal_<id>.jpg (hair:
Hair_<id>.jpg / normal_<id>.jpg), hide_<id>_<sex>.png, thumb_<id>[_<sex>].png, review_<id>_<sex>_{front,q,side,back,alone}.png,
<id>_<sex>.json (fit report). Then: python3 Tools/avatar/meshy_wardrobe_install.py <dir>.
Bodies: ~/argon-avatar/build/custom/argon_avatar_<sex>.glb = the meshes Unity's argon_base_<sex>.fbx were exported from
(verified identical to 0.0000 mm, same rig), so the grafts bind by bone name.
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

# ---------------------------------------------------------------------------------------------------------- pieces
# (file substring, item id, kind, bodies, display name, blurb, flags)
# kind: top | bottoms | shoes | hair | hat | head | glasses | neck | glove.   bodies: male | female | both
# flags: hides_hair, price (player shop items), full (full-length trousers: placed by leg length, not hip width),
#        long (hair reaching below the neck: the bust test may not use proximity there), fit_mode / fit_dz /
#        fit_scale_fix (head mounts: ICP mode and hand corrections, metres / factor), mann_rgb (mannequin colour
#        override 0..1), cut_below (drop everything below this height over the floor: a bust under a hat), shoulder_frac
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
    P('kaiba_hair', 'seto_kaiba_3d', 'hair', 'both', 'Seto Kaiba', 'Seto Kaiba'),
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
    P('mokuba_spiky_mane_wig', 'mokuba_kaiba_3d', 'hair', 'both', 'Mokuba Kaiba', 'Mokuba Kaiba', long=True, cut_below_neck=0.10),   # the hair reaches the shoulders; the bust it sits on wears a dark top
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
    P('tea_gardner_hair', 'tea_gardner_3d', 'hair', 'both', 'Téa Gardner', 'Téa Gardner', fit_mode='cavity'),   # no mannequin face under the fringe, only a bust
    P('tea_uniform_shoes', 'shoes_tea_3d', 'shoes', 'female', "Téa's loafers", 'Brown loafers with white socks.'),
    P('tea_uniform_skirt', 'bottoms_tea_3d', 'bottoms', 'female', "Téa's skirt", 'Blue pleated uniform skirt.'),
    P('tea_uniform_top', 'shirt_tea_3d', 'top', 'female', "Téa's uniform", 'Pink Domino High blazer with a blue bow.'),
    P('tristan_taylor_hair', 'tristan_quiff_3d', 'hair', 'both', 'Tristan Taylor', 'Tristan Taylor', mann_rgb=(0.99, 0.99, 0.99), mann_tol=0.6),   # a white head under a dark quiff: every light face is the head
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
EASE = dict(top=0.016, bottoms=0.012)
HR = 2048            # hide maps: the body atlas resolution
RAY = 0.03           # hide rule: garment within 3 cm above or below the skin, or 1.5 cm around it
NEAR = 0.015

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
        self.tri_center = self.P[self.tris].mean(1)
        e1 = self.P[self.tris[:, 1]] - self.P[self.tris[:, 0]]; e2 = self.P[self.tris[:, 2]] - self.P[self.tris[:, 0]]
        n = np.cross(e1, e2); self.tri_normal = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
        # per-vertex bone-family weights -> per-triangle means (the hide maps' keep / hands / feet / arms / legs rules)
        def fam(pred):
            cols = [i for i, nm in enumerate(self.bones) if pred(nm)]
            return W[:, cols].sum(1)[self.tris].mean(1)
        self.tri_keep = fam(lambda nm: 'Head' in nm or 'Neck' in nm)
        self.tri_hands = fam(lambda nm: 'Hand' in nm or 'Finger' in nm or 'Thumb' in nm)
        self.tri_feet = fam(lambda nm: 'Foot' in nm or 'ToeBase' in nm)
        self.tri_arms = fam(lambda nm: ('Arm' in nm or 'Shoulder' in nm) and 'Hand' not in nm)
        self.tri_legs = fam(lambda nm: ('UpLeg' in nm or nm.endswith('Leg') or 'Hips' in nm) and 'Foot' not in nm)
        self.tri_torso = fam(lambda nm: 'Spine' in nm or 'Hips' in nm or 'Shoulder' in nm)   # clavicles: the upper back / chest under a top
        vn = np.zeros((len(self.P), 3))
        for k in range(3): np.add.at(vn, self.tris[:, k], self.tri_normal)
        self.vnormal = vn / np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-9)
        self.landmarks()
        # scalp = the hair-bearing part of the head: above the brow line, or the back half above the ear line
        L = self.L; cz = self.tri_center
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
        m = ((R == 'legL') | (R == 'legR')) & (np.abs(P[:, 2] - L['ankle_z'] - 0.03) < 0.015)
        L['ankle_hw'] = float(np.percentile(np.abs(P[m, 0] - np.sign(P[m, 0]) * np.abs(P[m, 0]).mean()), 95)) if m.sum() > 5 else 0.04
        prof = {}
        for z in np.arange(0.9, L['shoulder_top_z'] + 0.02, 0.01):
            mm = torso & (P[:, 2] >= z) & (P[:, 2] < z + 0.01)
            prof[round(float(z), 2)] = float(np.abs(P[mm, 0]).max()) if mm.sum() > 3 else 0.0
        L['torso_prof'] = prof
        for s in 'LR':
            f = R == 'foot' + s
            L['foot_len' if s == 'L' else 'foot_len_R'] = float(P[f, 1].max() - P[f, 1].min())
            L['foot_heel_y_' + s] = float(P[f, 1].max()); L['foot_x_' + s] = float(P[f, 0].mean())
            L['foot_w_' + s] = float(P[f, 0].max() - P[f, 0].min())
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
        L['neck_z'] = float(self.hp('Neck')[2])
        self.face_idx = np.where(head & (P[:, 1] < L['head_y'] - 0.005) & (P[:, 2] > L['chin_z'] - 0.005) & (P[:, 2] < L['brow_z'] + 0.015))[0]
        self.skull_idx = np.where(head & (P[:, 2] > L['brow_z']))[0]
        self.head_idx = np.where(head)[0]
        self.neck_idx = np.where(torso & (P[:, 2] > L['neck_z'] - 0.03) & (P[:, 2] < L['chin_z']) & (np.abs(P[:, 0]) < 0.08))[0]
        m = torso & (P[:, 2] > L['neck_z'] - 0.02) & (P[:, 2] < L['neck_z'] + 0.02)
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
        if P is not None and P is not self.P:
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
    limb's bone axis than the skin point it is nearest to is inside. `axes` = (seg_a, seg_b) per BVH triangle."""
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

def write_png_gray(path, arr):
    """8-bit greyscale PNG written byte-exact, no colour management (hide maps: 255 = hidden)."""
    h, w = arr.shape
    raw = b''.join(b'\x00' + arr[y].astype(np.uint8).tobytes() for y in range(h))
    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 0, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    open(path, 'wb').write(png)

def mesh_coords(ob):
    n = len(ob.data.vertices); a = np.zeros(n * 3, dtype=np.float32)
    ob.data.vertices.foreach_get('co', a); return a.reshape(n, 3).astype(np.float64)
def set_coords(ob, P):
    ob.data.vertices.foreach_set('co', P.reshape(-1).astype(np.float32)); ob.data.update()

def edges_of(ob):
    me = ob.data; e = np.zeros(len(me.edges) * 2, dtype=np.int64); me.edges.foreach_get('vertices', e)
    return e.reshape(-1, 2)

def face_centers(ob):
    me = ob.data; c = np.zeros(len(me.polygons) * 3, dtype=np.float32); me.polygons.foreach_get('center', c)
    n = np.zeros(len(me.polygons) * 3, dtype=np.float32); me.polygons.foreach_get('normal', n)
    return c.reshape(-1, 3).astype(np.float64), n.reshape(-1, 3).astype(np.float64)

def face_uv_centroids(ob):
    me = ob.data; uvl = me.uv_layers.active
    uv = np.zeros(len(me.loops) * 2, dtype=np.float32); uvl.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
    ls = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_start', ls)
    lt = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_total', lt)
    s = np.add.reduceat(uv, ls, axis=0)
    return s / lt[:, None]

def components(ob):
    n = len(ob.data.vertices); parent = np.arange(n)
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for a, b in edges_of(ob):
        ra, rb = find(int(a)), find(int(b))
        if ra != rb: parent[ra] = rb
    return np.array([find(i) for i in range(n)])

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
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    # the sculpt's custom split normals ARE its smooth vertex normals (0.04 deg at the 99.9th percentile); dropping them
    # lets Blender keep the normals right through the articulation below and export the same smooth shading
    if ob.data.has_custom_normals:
        try: bpy.ops.mesh.customdata_custom_splitnormals_clear()
        except Exception: pass
    bpy.ops.object.shade_smooth()
    return ob

def piece_images(ob):
    """The Meshy material's images by role: base (Base Color), normal (Normal Map), mr (Separate Color), emission."""
    out = {}
    for ms in ob.material_slots:
        m = ms.material
        if not m or not m.node_tree: continue
        for n in m.node_tree.nodes:
            if n.type != 'TEX_IMAGE' or not n.image: continue
            for l in n.outputs[0].links:
                tn, sock = l.to_node.type, l.to_socket.name
                if tn == 'BSDF_PRINCIPLED' and sock == 'Base Color': out['base'] = n.image
                elif tn == 'BSDF_PRINCIPLED' and 'Emission' in sock: out['emission'] = n.image
                elif tn == 'NORMAL_MAP': out['normal'] = n.image
                elif tn == 'SEPARATE_COLOR': out['mr'] = n.image
    if 'base' not in out:
        imgs = [im for im in bpy.data.images if im.size[0] > 0 and im.packed_file]
        if imgs: out['base'] = max(imgs, key=lambda im: im.size[0])
    return out

def save_packed(img, stem):
    """Write the packed texture byte for byte; returns the path written (extension from the data's own format)."""
    if img is None or not img.packed_file: return None
    data = bytes(img.packed_file.data)
    ext = '.png' if data[:4] == b'\x89PNG' else ('.jpg' if data[:2] == b'\xff\xd8' else '.bin')
    path = stem + ext
    open(path, 'wb').write(data)
    return path

def image_pixels(img):
    w, h = img.size
    px = np.zeros(w * h * 4, dtype=np.float32); img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)[:, :, :3]

def face_colours(ob, px):
    """Base-colour texture sampled at each face's UV centroid, (nfaces, 3) in 0..1 (float, no colour management)."""
    if px is None: return None
    h, w = px.shape[:2]
    uv = face_uv_centroids(ob)
    x = np.clip((uv[:, 0] % 1.0) * w, 0, w - 1).astype(int); y = np.clip((uv[:, 1] % 1.0) * h, 0, h - 1).astype(int)
    return px[y, x]

def slice_hw(P, z0, z1, pct=98):
    m = (P[:, 2] >= z0) & (P[:, 2] < z1)
    return float(np.percentile(np.abs(P[m, 0]), pct)) if m.sum() > 3 else 0.0

def delete_faces(ob, kill):
    """Delete the flagged faces (bool per polygon) and any loose vertices / edges left behind."""
    me = ob.data
    if not kill.any(): return 0
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.where(kill)[0]], context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.delete_loose(); bpy.ops.object.mode_set(mode='OBJECT')
    return int(kill.sum())

# ---------------------------------------------------------------------------------------------------------- placement
def shoulder_line(P, frac=None):
    """z and half-width of the garment's shoulder line (the knee of the width curve scanning down from the collar)."""
    zmax, zmin = P[:, 2].max(), P[:, 2].min(); H = zmax - zmin
    if frac is not None:
        z = zmax - frac * H
        return z, slice_hw(P, z - 0.01 * H, z + 0.01 * H)
    step = 0.01 * H
    zs = np.arange(zmax - step, zmax - 0.45 * H, -step)
    hw = np.array([slice_hw(P, z - step, z) for z in zs])
    hw = np.convolve(np.pad(hw, 1, mode='edge'), [0.25, 0.5, 0.25], 'valid')
    slope = np.diff(hw) / step
    peak = int(np.argmax(slope[:len(slope) // 2]))
    for i in range(peak + 1, len(slope)):
        if slope[i] < 0.8: return float(zs[i]), float(hw[i])
    i = min(peak + 3, len(hw) - 1)
    return float(zs[i]), float(hw[i])

def place_top(P, body, s, z_sh, piece, rep):
    """Uniform scale s about the origin, then the shoulder line on the body's shoulder top and the chest centred."""
    L = body.L
    P = P * s
    P[:, 2] += (L['shoulder_top_z'] - 0.01) - z_sh * s
    hem = P[:, 2].min()
    if hem < 0.02:
        # a floor-length robe placed by its shoulders would run through the floor: the whole piece shrinks (uniformly)
        # until its hem clears the floor, and is re-hung from the shoulder line
        k = (L['shoulder_top_z'] - 0.01 - 0.02) / (L['shoulder_top_z'] - 0.01 - hem)
        P = P * k; s *= k
        P[:, 2] += (L['shoulder_top_z'] - 0.01) - z_sh * s
        rep['floor_rescale'] = round(float(k), 3)
    m = (P[:, 2] > L['shoulder_top_z'] - 0.26) & (P[:, 2] < L['shoulder_top_z'] - 0.10) & (np.abs(P[:, 0]) < L['chest_hw'])
    P[:, 1] += L['chest_y'] - (np.median(P[m, 1]) if m.sum() > 10 else np.median(P[:, 1]))
    return P, s

def fit_top(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    z_sh, hw_sh = shoulder_line(P, piece.get('shoulder_frac'))
    target_hw = L['shoulder_hw'] + EASE['top']
    s = target_hw / hw_sh * piece.get('scale_fix', 1.0)
    P, s = place_top(P, body, s, z_sh, piece, rep)
    set_coords(ob, P)
    sleeve, poses, tops = classify_sleeves(body, P, rep)
    if tops:
        # a sleeve's outer top is the shoulder point whatever the collar or hood does: re-scale (uniformly) on it
        z_sh2 = float(np.mean(tops)); hw_sh2 = slice_hw(P, z_sh2 - 0.025, z_sh2 + 0.005)
        s2 = target_hw / hw_sh2 if hw_sh2 > 0.05 else 1.0
        if 0.55 < s2 < 1.4 and abs(s2 - 1) > 0.01:
            P, s2b = place_top(P, body, s2, z_sh2, piece, rep); s *= s2b
            set_coords(ob, P)
            rep['sleeve_rescale'] = round(float(s2), 3)
            sleeve, poses, tops = classify_sleeves(body, P, rep)
    rep.update(scale=round(float(s), 4), hem_z=round(float(P[:, 2].min()), 3), top_z=round(float(P[:, 2].max()), 3))
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
                top_ok = float(P[cls, 2].max()) > L['shoulder_top_z'] - 0.12
                if length > 0.2 and spread < 0.06 and ang < 30 and top_ok:
                    poses[s_] = (ax, d); rep[f'sleeve_{s_}_deg'] = round(ang, 1)
                else:
                    cls = np.zeros(len(P), bool); rep[f'sleeve_{s_}_rejected'] = [round(length, 2), round(spread, 3), round(ang, 1)]
        if cls.sum() < 60:
            cls = side_ok & (perp < 0.085) & (t > 0.03) & (P[:, 2] < L['shoulder_top_z'] - 0.02)
            if cls.sum() >= 60: rep[f'sleeve_{s_}_cone'] = int(cls.sum())
        sleeve[s_] = cls
    return sleeve, poses, tops

def fit_bottoms(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    zmax, zmin = P[:, 2].max(), P[:, 2].min(); H = zmax - zmin
    hw0 = slice_hw(P, zmax - 0.14 * H, zmax - 0.02 * H, 97)
    top = L['waist_z'] + 0.02
    if piece.get('full'):
        # full-length trousers: ONE scale puts the hem at the ankle; the hips then land where the sculpt's proportions
        # put them (reported), the skin inside is hidden by the map
        s = (top - (L['ankle_z'] + 0.015)) / H
    else:
        s = (L['hip_hw'] + EASE['bottoms']) / hw0
    P *= s
    P[:, 2] += top - P[:, 2].max()
    if not piece.get('full') and P[:, 2].min() < 0.02:
        k = (top - 0.02) / (top - P[:, 2].min()); P = top + (P - top) * k if False else P   # (never squashed: shorts / skirts end above the floor anyway)
    m = P[:, 2] > L['waist_z'] - 0.10
    P[:, 1] += L['hip_y'] - np.median(P[m, 1])
    set_coords(ob, P)
    rep.update(scale=round(float(s), 4), hem_z=round(float(P[:, 2].min()), 3),
               hip_hw_ratio=round(float(hw0 * s / (L['hip_hw'] + EASE['bottoms'])), 3))
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
        hem = P[P[:, 2] < P[:, 2].min() + 0.03]
        if len(hem) > 10:
            side = hem[hem[:, 0] > 0]
            if len(side) > 5: rep['hem_hw_vs_ankle'] = round(float((side[:, 0].max() - side[:, 0].min()) / 2 / max(L['ankle_hw'], 0.01)), 2)
    return P, legs, poses

def foot_flat_pose(body):
    """Pose both Foot bones so the foot axis (ankle -> toe base) is horizontal: the GLB rest pose stands on tiptoe
    (the ankle 15 cm up), the game's clips put the sole on the ground. Shoes are placed and bound in this pose."""
    for pre in ('Left', 'Right'):
        d = body.hp(pre + 'ToeBase') - body.hp(pre + 'Foot')
        flat = np.array([d[0], d[1], 0.0])
        if np.linalg.norm(flat) < 1e-6 or abs(d[2]) < 1e-4: continue
        body.pose_bone(pre + 'Foot', rot_between(d, flat))

def Pb_bone(body, name):
    """A bone head in the current pose (world)."""
    pb = body.arm.pose.bones[B + name]
    return np.array(body.arm.matrix_world @ pb.head)

def fit_shoes(body, ob, piece, rep, Pb):
    """One sculpt is either a pair (two big components, side by side) or ONE shoe (one component: Keith's boot, Rex's
    sneaker ...), which is mirrored for the other foot. Each shoe's length axis is its longest horizontal extent
    (PCA), its heel the taller end; it is turned to the foot's axis, scaled by the foot's length and set on the
    ground under the POSED (flat) foot. Nothing else changes."""
    L = body.L; P = mesh_coords(ob); me = ob.data
    comp = components(ob)
    cids, cnt = np.unique(comp, return_counts=True)
    big = [c for c, n in zip(cids, cnt) if n > max(1000, 0.1 * len(P))]
    if len(big) >= 2:
        cx = {c: P[comp == c, 0].mean() for c in big}
        order = sorted(big, key=lambda c: cx[c])
        right_c, left_c = order[0], order[-1]       # the body's left foot is at +x
        sides = {'L': comp == left_c, 'R': comp == right_c}
        rep['pair'] = 'two components'
    else:
        # one shoe: duplicate the mesh and mirror the copy in x for the other foot
        n0 = len(me.vertices)
        bm = bmesh.new(); bm.from_mesh(me)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        dup = bmesh.ops.duplicate(bm, geom=geom)
        new_verts = [g for g in dup['geom'] if isinstance(g, bmesh.types.BMVert)]
        for v in new_verts: v.co.x = -v.co.x
        new_faces = [g for g in dup['geom'] if isinstance(g, bmesh.types.BMFace)]
        bmesh.ops.reverse_faces(bm, faces=new_faces)   # a mirrored shell faces inward until its faces are flipped
        bm.to_mesh(me); bm.free(); me.update()
        P = mesh_coords(ob)
        sides = {'L': np.arange(len(P)) < n0, 'R': np.arange(len(P)) >= n0}
        rep['pair'] = 'one shoe, mirrored'
    out = P.copy()
    for s_, mask in sides.items():
        Q = P[mask]
        pre = 'Left' if s_ == 'L' else 'Right'
        c2, d2, sv = pca_axis(Q[:, :2])             # the shoe's length axis in the ground plane
        ang = math.atan2(d2[1], d2[0])
        rz = math.pi / 2 - ang                      # -> along y
        R = np.array([[math.cos(rz), -math.sin(rz), 0], [math.sin(rz), math.cos(rz), 0], [0, 0, 1]])
        ctr = Q.mean(0); Q = (Q - ctr) @ R.T + ctr
        ymin, ymax = Q[:, 1].min(), Q[:, 1].max(); span = ymax - ymin
        front_h = Q[Q[:, 1] < ymin + 0.25 * span][:, 2].max(); back_h = Q[Q[:, 1] > ymax - 0.25 * span][:, 2].max()
        if front_h > back_h:                        # the collar (heel end) is the taller end: make it +y
            Q[:, 1] = 2 * ctr[1] - Q[:, 1]; Q[:, 0] = 2 * ctr[0] - Q[:, 0]; rep[f'flipped_{s_}'] = True
        fidx = np.where(body.region == 'foot' + s_)[0]
        F = Pb[fidx]
        a0 = Pb_bone(body, pre + 'Foot'); a1 = Pb_bone(body, pre + 'ToeBase')
        ax = a1 - a0; ax[2] = 0; ax /= max(np.linalg.norm(ax), 1e-9)
        along = (F - a0) @ ax
        foot_len = float(along.max() - along.min())
        length = Q[:, 1].max() - Q[:, 1].min()
        sc = (foot_len + 0.03) / length
        Q = Q * sc
        side_ax = np.array([ax[1], -ax[0], 0.0])
        rep[f'width_ratio_{s_}'] = round(float((Q[:, 0].max() - Q[:, 0].min()) / max(float(np.ptp(F @ side_ax)), 0.03)), 2)
        yaw = math.atan2(ax[0], -ax[1]); cs, sn = math.cos(yaw), math.sin(yaw)   # the shoe's toe (-y) onto the foot's axis
        Ry = np.array([[cs, -sn, 0], [sn, cs, 0], [0, 0, 1]])
        ctr = Q.mean(0); Q = (Q - ctr) @ Ry.T + ctr
        Q[:, 2] += (F[:, 2].min() - 0.004) - Q[:, 2].min()
        heel = float(((F - a0) @ ax).min())        # the heel is the foot's rearmost point along its axis
        back = (Q - a0) @ ax; Q += ax * ((heel - 0.012) - back.min())
        Q += side_ax * (float((F @ side_ax).mean()) - float((Q @ side_ax).mean()))
        out[mask] = Q
        rep[f'scale_{s_}'] = round(float(sc), 3)
    set_coords(ob, out)
    rep['top_z'] = round(float(out[:, 2].max()), 3)
    return out, sides

def kd_of(P):
    kd = KDTree(len(P))
    for i, p in enumerate(P): kd.insert(Vector(p), i)
    kd.balance(); return kd

def icp_similarity(sets, s0, t0, iters=30, trim=0.7, clip=(2.5, 9.0)):
    """Move our points onto the piece with x' = s*x + t (no rotation). `sets` = [(src points (m), KD-tree, weight)]."""
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
        s_new = float(np.clip(num / den if den > 0 else s, clip[0], clip[1]))
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
CLUSTER_TILE = None    # set to the output dir to also render the colour-cluster review tiles

def fit_headmount(body, ob, piece, rep):
    """Hair / hats / head presets: ICP our face (or skull cap for a faceless hat) onto the piece's mannequin, then map
    the piece onto our head with the inverse (uniform scale + translation)."""
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    # the ICP samples every 4th vertex of a dense sculpt (a 900k-vertex KD-tree is slow to build and gains nothing)
    step = max(1, len(P) // 200000)
    kd = kd_of(P[::step])
    zmax, zmin = P[:, 2].max(), P[:, 2].min(); H = zmax - zmin; yc = float(np.median(P[:, 1]))
    N = np.zeros((len(P), 3), dtype=np.float32); ob.data.vertices.foreach_get('normal', N.reshape(-1))
    cen = P.mean(0); inward = ((cen - P) * N).sum(1) > 0
    kd_in = kd_of(P[inward][::step]) if inward.sum() > 200 else kd
    cands = []
    wf, ws = piece.get('fit_w', (0.7, 1.2) if piece['kind'] == 'hair' else (1.0, 0.5))
    face = (body.P[body.face_idx], kd, wf); skull = (body.P[body.skull_idx], kd_in, ws)
    mode = piece.get('fit_mode') or ('cavity' if piece['kind'] == 'hat' else ('face' if piece['kind'] == 'head' else 'face+cavity'))
    sets = [skull] if mode == 'cavity' else ([face] if mode == 'face' else [face, skull])
    anchor = np.array([0, L['head_y'], L['brow_z']]) if mode == 'cavity' else np.array([0, L['face_front_y'], L['eye_z']])
    crown = body.P[body.head_idx][:, 2].max(); hw_head = L['head_hw']
    prior = FIT_PRIOR.get(piece['id'])
    if prior:
        # the other body: start where the male fit ended (same piece, the anchor moved with the body), scale within a
        # quarter of it (a free restart diverged to the clip limit on Tristan's quiff)
        pm = prior
        t0 = pm['t'] + pm['s'] * (pm['anchor'] - anchor)
        s, t, rms = icp_similarity(sets, pm['s'], t0, iters=0)                                  # the male fit, moved with the anchor
        cands.append((rms * (1 + 0.03 * s), mode, s, t, rms, pm['s'], 'prior'))
        s, t, rms = icp_similarity(sets, pm['s'], t0, clip=(pm['s'], pm['s']))                 # translation only
        cands.append((rms * (1 + 0.03 * s), mode, s, t, rms, pm['s'], 'prior-t'))
        for k in (0.92, 1.0, 1.08):
            s, t, rms = icp_similarity(sets, pm['s'] * k, t0, clip=(pm['s'] * 0.8, pm['s'] * 1.25))
            cands.append((rms * (1 + 0.03 * s), mode, s, t, rms, pm['s'] * k, 'prior-st'))
    else:
        for s0 in (2.6, 3.3, 4.1, 5.0, 6.0, 7.2, 8.4):
            for frac in (0.06, 0.18, 0.30, 0.42, 0.55):
                band = P[(P[:, 2] > zmax - (frac + 0.1) * H) & (P[:, 2] < zmax - frac * H)]
                ty = float(np.percentile(band[:, 1], 3)) if (mode != 'cavity' and len(band)) else yc
                t0 = np.array([0.0, ty, zmax - frac * H]) - anchor * s0
                s, t, rms = icp_similarity(sets, s0, t0)
                top_b = (zmax - t[2]) / s; hw_b = (P[:, 0].max() - P[:, 0].min()) / (2 * s)
                penalty = (0.02 if top_b < crown - 0.015 else 0) + (0.02 if hw_b < hw_head * 0.9 else 0) \
                          + (0.02 if hw_b > hw_head * 3.5 else 0) + (0.02 if top_b > crown + 0.45 else 0)
                score = rms * (1 + 0.03 * s) + penalty
                cands.append((score, mode, s, t, rms, s0, frac))
    cands.sort(key=lambda c: c[0])
    score, mode, s, t, rms, s0, frac = cands[0]
    if body.sex == 'male': FIT_PRIOR[piece['id']] = dict(s=s, t=t.copy(), anchor=anchor.copy())
    rep.update(fit_mode=mode, fit_scale=round(float(s), 3), fit_rms_mm=round(rms * 1000, 1), fit_start=[s0, frac])
    P = (P - t) / s
    if piece.get('fit_scale_fix'):
        hd = body.hp('Head'); P = hd + (P - hd) * piece['fit_scale_fix']
    if piece.get('fit_dz'): P[:, 2] += piece['fit_dz']
    set_coords(ob, P)
    return P

def fit_glasses(body, ob, piece, rep):
    L = body.L; P = mesh_coords(ob)
    P[:, 0] -= (P[:, 0].min() + P[:, 0].max()) / 2
    width = P[:, 0].max() - P[:, 0].min()
    s = (2 * L['temple_hw'] + 0.012) / width
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
        P[:, 2] += (L['neck_z'] + 0.012) - P[:, 2].max()
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

# ---------------------------------------------------------------------------------------------------------- mannequin
def kmeans(X, k=3, iters=12):
    if len(X) < k: return X.copy()
    rng = np.random.default_rng(0)
    C = X[rng.choice(len(X), k, replace=False)]
    for _ in range(iters):
        d = ((X[:, None, :] - C[None]) ** 2).sum(2); lab = d.argmin(1)
        for j in range(k):
            if (lab == j).any(): C[j] = X[lab == j].mean(0)
    return C

def face_adjacency(ob):
    """(a, b) pairs of faces that share an edge."""
    me = ob.data
    ei = np.zeros(len(me.loops), dtype=np.int64); me.loops.foreach_get('edge_index', ei)
    ls = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_start', ls)
    lt = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_total', lt)
    fi = np.repeat(np.arange(len(me.polygons)), lt)
    order = np.argsort(ei, kind='stable'); e_s = ei[order]; f_s = fi[order]
    same = e_s[1:] == e_s[:-1]
    return np.stack([f_s[:-1][same], f_s[1:][same]], 1)

def smooth_mask(kill, adj, passes=2):
    """Majority vote over face neighbours: a lone killed face among live ones lives, a lone live face among killed
    ones dies (texture noise never punches single-face holes in hair or leaves single-face mannequin specks)."""
    n = len(kill)
    deg = np.bincount(adj.reshape(-1), minlength=n)
    for _ in range(passes):
        k = kill.astype(np.int32)
        acc = np.zeros(n, np.int32)
        np.add.at(acc, adj[:, 0], k[adj[:, 1]]); np.add.at(acc, adj[:, 1], k[adj[:, 0]])
        maj_kill = acc * 2 > deg; maj_live = acc * 2 < deg
        kill = np.where(deg >= 2, np.where(maj_kill, True, np.where(maj_live, False, kill)), kill)
    return kill

def remove_mannequin(body, ob, piece, rep, cols):
    """Delete the Meshy mannequin (the bust / face a hairstyle or hat was generated on) and nothing of the hair.
    The piece's faces are clustered by painted colour. A cluster is the mannequin when it is neutral or skin-toned,
    owns a good share of the faces lying on OUR aligned face (where the mannequin's face is after the ICP fit) and
    hardly reaches above the brow (a white turban on a white bust shares its colour with the mannequin: then only
    geometry decides, inside the zones no hairstyle reaches). No such cluster = no mannequin in this sculpt (Keith's
    bandana, Weevil's bowl): nothing is removed. A face dies when it is painted nearer the mannequin's colour than any
    hair colour AND lies on / in our skin, in the face column, or on the bust; the kill mask is majority-smoothed over
    face neighbours and leftover islands on the face go with it."""
    me = ob.data; L = body.L; kind = piece['kind']
    cent, fn = face_centers(ob)
    bvh, tris, sel = body.bvh(body.P, None)
    sd, loc, n, idx = signed_dist(bvh, cent, axes=body.axes_for(sel))
    aligned = (fn * n).sum(1) > 0.85
    reg = body.tri_region[sel[np.clip(idx, 0, len(sel) - 1)]]; on_head = reg == 'head'
    x, y, z = cent[:, 0], cent[:, 1], cent[:, 2]
    front_half = y < L['head_y'] - 0.01
    long_hair = piece.get('long', False)
    face_zone = front_half & on_head & (z < L['brow_z'] + 0.005) & (z > L['chin_z'] - 0.03) & (sd < 0.02) & (sd > -0.03)
    neck_zone = (z < L['chin_z']) & (z > L['neck_z'] - 0.06) & (np.abs(x) < 0.09) & (sd < 0.03) & (sd > -0.06)
    column = (np.abs(x) < np.where(z < L['chin_z'] + 0.01, 0.075, 0.06)) & (y < L['head_y'] + 0.02) & (z > L['neck_z'] - 0.06) & (z < L['eye_z'] - 0.035) & (sd < 0.14) & (sd > -0.06)
    below_neck = z < L['neck_z'] - 0.02
    inside_face = (sd < -0.004) & front_half & (z < L['brow_z'] + 0.01) & on_head
    inside_neck = (sd < -0.004) & (z < L['chin_z']) & ~on_head
    below_cut = z < piece['cut_below'] if piece.get('cut_below') is not None else np.zeros(len(z), bool)
    kill = below_cut.copy()
    rule = 'none'
    table = []
    mann_c, hair_c = [], []
    # is there a mannequin at all? Its face covers OUR face (within 2 cm either side of every face vertex); a fringe
    # covers the forehead and eyes at most (Weevil's bowl), a hat's hair hangs beside the face (Keith), a turban
    # leaves it open (Shadi)
    bvh_p = BVHTree.FromPolygons([Vector(p) for p in mesh_coords(ob)], [tuple(pp.vertices) for pp in me.polygons])
    def coverage(idx):
        cov = 0
        for vi in idx:
            q = Vector(body.P[vi]); nq = Vector(body.vnormal[vi])
            if bvh_p.ray_cast(q - nq * 0.06, nq, 0.08)[0] is not None: cov += 1   # a mannequin face sunk up to 6 cm into ours counts
        return cov / max(len(idx), 1)
    face_cover = coverage(body.face_idx); neck_cover = coverage(body.neck_idx)
    rep['face_cover'] = round(face_cover, 3); rep['neck_cover'] = round(neck_cover, 3)
    has_mannequin = max(face_cover, neck_cover) >= 0.45 or bool(piece.get('mann_rgb')) or bool(piece.get('has_mannequin'))
    if piece.get('no_mannequin'): has_mannequin = False
    if piece.get('cut_below_neck') is not None: kill |= z < L['neck_z'] - piece['cut_below_neck']
    ear_zone = (np.abs(x) > 0.055) & (np.abs(y - L['head_y']) < 0.045) & (z > L['eye_z'] - 0.045) & (z < L['brow_z'] + 0.005) & (np.abs(sd) < 0.03)
    plate_box = (np.abs(x) < 0.09) & (y > L['face_front_y'] - 0.06) & (y < L['head_y'] + 0.05) & (z > L['chin_z'] - 0.03) & (z < L['crown_z'])
    # whatever sits 1.5 cm or deeper inside our face is the mannequin's face sunk into ours: no hairstyle is there
    face_box = (y < L['head_y'] + 0.02) & (np.abs(x) < 0.07) & on_head & (z < L['brow_z'] + 0.01) & (z > L['chin_z'] - 0.03)
    deep_face = (sd < -0.015) & face_box
    if cols is not None:
        # a neutral (grey / white / black) surface just under our face skin is a mannequin plate, never hair
        neutral_face = (cols.max(1) - cols.min(1)) < 0.15
        deep_face |= (sd < -0.004) & face_box & neutral_face
        # ...and so is any neutral surface ENCLOSED by our head (a ray through it hits our skull both ways): the
        # featureless mannequin head a cap or bandana was generated on (Keith, PaniK). Not when the hair itself is
        # neutral (white hair sits inside a bigger skull too).
        far_neutral = float(neutral_face[sd > 0.04].mean()) if (sd > 0.04).sum() > 200 else 1.0
        rep['far_neutral'] = round(far_neutral, 2)
        if far_neutral < 0.5:
            bvh_h, _, _ = body.bvh(body.P, {'head'})
            cand = np.where(neutral_face & (np.abs(x) < 0.09) & (z > L['chin_z'] - 0.03) & (z < L['crown_z']) & (sd < 0.02))[0]
            enclosed = np.zeros(len(cent), bool)
            for i in cand:
                c = Vector(cent[i])
                if bvh_h.ray_cast(c, Vector((0, 1, 0)), 0.25)[0] is not None and bvh_h.ray_cast(c, Vector((0, -1, 0)), 0.25)[0] is not None:
                    enclosed[i] = True
            rep['enclosed_faces'] = int(enclosed.sum())
            deep_face |= enclosed
    kill |= deep_face
    if cols is not None:
        K = 8
        C = kmeans(cols[::max(1, len(cols) // 40000)], K)
        D = np.stack([np.linalg.norm(cols - C[k], axis=1) for k in range(len(C))], 1)
        lab = D.argmin(1)
        fz = face_zone.sum(); nz = neck_zone.sum()
        plates = []
        for k in range(len(C)):
            m = lab == k; nk = int(m.sum())
            if nk < 100: continue
            c = C[k]; sat = float(c.max() - c.min()); light = float(c.mean())
            neutral = sat < 0.12 and light >= 0.3            # a dark neutral cluster could as well be dark hair: never by colour
            skin = bool(c[0] >= c[1] - 0.02 and c[1] >= c[2] - 0.02 and sat < 0.5 and c[0] > 0.3)
            share = max(float((m & face_zone).sum() / max(fz, 1)), float((m & neck_zone).sum() / max(nz, 1)))
            above = float((m & (z > L['brow_z'] + 0.02) & (sd > 0.03)).sum() / nk)     # in the hat / hair volume, not a mannequin forehead
            # a flat plate filling the face opening (Keith's bandana, PaniK's cap): one planar cluster in the face box.
            # Planarity = the normals' scatter matrix has one dominant direction (a two-sided slab averages to ~0).
            nn = fn[m]; ev = np.linalg.eigvalsh(nn.T @ nn / max(len(nn), 1))
            coh = float(ev[-1] / max(ev.sum(), 1e-9))
            in_box = float((m & plate_box).sum() / nk)
            is_plate = coh > 0.9 and in_box >= 0.5 and sat < 0.25 and nk > 300
            is_mann = has_mannequin and (neutral or skin) and share >= 0.2 and above <= 0.35
            table.append(dict(rgb=[round(float(v), 3) for v in c], n=nk, share=round(share, 3), above_brow=round(above, 3), neutral=neutral, skin=skin, coherence=round(coh, 2), mannequin=bool(is_mann), plate=bool(is_plate)))
            if is_plate: plates.append(k); kill |= m
            (mann_c if is_mann else hair_c).append(k)
        if not plates:
            # the plate shares its colour with the rest: the box's modal normal direction, if thousands of faces share it
            bx = np.where(plate_box & (np.abs(fn[:, 2]) < 0.5))[0]
            if len(bx) > 3000:
                nn = fn[bx] * np.where(fn[bx, 1:2] < 0, 1, -1)          # fold to one hemisphere (two-sided slab)
                ev, evec = np.linalg.eigh(nn.T @ nn / len(nn)); nref = evec[:, -1]
                par = bx[np.abs(fn[bx] @ nref) > 0.985]
                if len(par) > 1500:
                    off = cent[par] @ nref
                    core = par[np.abs(off - np.median(off)) < 0.012]   # within 1.2 cm of one plane
                    if len(core) > 1500 and float((cols[core].max(1) - cols[core].min(1)).mean()) > 0.15: core = core[:0]   # painted like hair: a fringe, not a plate
                    if len(core) > 1500:
                        kill[core] = True; rep['plate_faces'] = int(len(core)); rep['plate_rule'] = 'box normal'
        else: rep['plate_faces'] = int(np.isin(lab, plates).sum()); rep['plate_rule'] = 'cluster'
        if mann_c:
            mann_light = max(float(C[j].mean()) for j in mann_c)
            zone_hn = (on_head & (z < L['brow_z'] + 0.01) & (sd < 0.06) & (sd > -0.06)) | neck_zone
            for k in list(hair_c):
                m = lab == k; nk = int(m.sum())
                if nk < 100: continue
                c = C[k]; sat = float(c.max() - c.min()); light = float(c.mean())
                own = float((m & zone_hn).sum() / nk)
                above = float((m & (z > L['brow_z'] + 0.02) & (sd > 0.03)).sum() / nk)
                # a shadowed shade of the mannequin's own colour (an ear, the nape): neutral, darker, on the head / neck
                if sat < 0.12 and light < mann_light - 0.08 and own >= 0.3 and above <= 0.15 and any(float(C[j].max() - C[j].min()) < 0.12 for j in mann_c):
                    hair_c.remove(k); mann_c.append(k)
                    for t in table:
                        if t['n'] == nk and t['rgb'] == [round(float(v), 3) for v in c]: t['mannequin'] = True; t['shade'] = True
        rep['colour_clusters'] = table
        if piece.get('mann_rgb'):
            mc = np.array(piece['mann_rgb'], float)
            d_m = np.linalg.norm(cols - mc, axis=1); d_h = D[:, hair_c].min(1) if hair_c else np.full(len(cols), 9.0)
            if piece.get('mann_tol'): d_h = np.maximum(d_h, piece['mann_tol'] + 0.02)   # everything within the tolerance is mannequin
            mann_c = ['override']
        elif mann_c:
            d_m = D[:, mann_c].min(1); d_h = D[:, hair_c].min(1) if hair_c else np.full(len(cols), 9.0)
        if mann_c:
            rule = 'colour'
            mcol = d_m < d_h - 0.02                                   # painted as the mannequin, not as hair
            head_zone = on_head & (z < L['brow_z'] + 0.01) & (sd < 0.06) & (sd > -0.06)      # face, ears, temples
            near = head_zone | ((np.abs(sd) < 0.03) & aligned) | (sd < -0.004) | face_zone
            near |= (z < L['chin_z'] + 0.01) & (z > L['neck_z'] - 0.06) & (sd < 0.08) & (sd > -0.06)   # the neck all round
            near |= below_neck if not long_hair else (below_neck & (sd < 0.025))   # a short style has nothing below the neck base
            kill |= mcol & near
            kill |= mcol & on_head & aligned & (np.abs(sd) < 0.03)     # the mannequin's exposed scalp / temples (a quiff covers only the top)
            if piece.get('mann_rgb'): kill |= mcol & on_head & (np.abs(sd) < 0.06)   # an explicit mannequin colour: everything in it near the head
            kill |= column                                              # no hair hangs in front of the mouth / throat
            kill |= (inside_face | inside_neck) & (d_m < d_h + 0.05)
            if not long_hair: kill |= ear_zone & (d_m < d_h + 0.05)
    if rule == 'none' and has_mannequin:
        # colour cannot tell (or no texture): geometry alone, limited to where no hairstyle reaches
        geo_zone = face_zone & aligned & (np.abs(sd) < 0.015)
        geo_zone |= column & (sd < 0.09)
        geo_zone |= inside_face | inside_neck
        if not long_hair: geo_zone |= ear_zone
        if not long_hair:
            geo_zone |= (z < L['chin_z'] + 0.01) & (z > L['neck_z'] - 0.06) & (sd < 0.08) & (sd > -0.06)   # a short style never reaches the neck
            geo_zone |= below_neck
        else:
            geo_zone |= neck_zone & aligned & (np.abs(sd) < 0.015)
            geo_zone |= below_neck & (sd < 0.010) & aligned
        rule = 'geometry'; kill |= geo_zone
    rep['mannequin_rule'] = rule
    if table and CLUSTER_TILE:
        # a false-colour review tile: every face in its cluster's colour, killed faces black (before deletion)
        palette = np.array([[1, 0, 0], [0, 1, 0], [0, 0, 1], [1, 1, 0], [1, 0, 1], [0, 1, 1], [1, 0.5, 0], [0.5, 0.5, 0.5]], float)
        fc = palette[lab % len(palette)]; fc[kill] = 0
        ca = me.color_attributes.get('cluster') or me.color_attributes.new('cluster', 'FLOAT_COLOR', 'CORNER')
        lt = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_total', lt)
        loopcol = np.repeat(fc, lt, axis=0); loopcol = np.concatenate([loopcol, np.ones((len(loopcol), 1))], 1)
        ca.data.foreach_set('color', loopcol.reshape(-1).astype(np.float32))
        sc, cam = setup_render(); sc.display.shading.color_type = 'VERTEX'
        sc.render.resolution_x = 360; sc.render.resolution_y = 520
        P_ = cent; lo, hi = P_.min(0), P_.max(0); z0 = min(lo[2], L['chin_z'] - 0.12) - 0.05; z1 = hi[2] + 0.05
        cam.data.ortho_scale = max(z1 - z0, (hi[0] - lo[0]) * 1.3, 0.3) * 1.08
        body.body.hide_render = True
        for tag, (px, py, rz) in {'cfront': (0, -6, 0), 'cside': (6, 0, math.pi / 2)}.items():
            cam.location = (px, py, (z0 + z1) / 2); cam.rotation_euler = (math.pi / 2, 0, rz)
            sc.render.filepath = f'{CLUSTER_TILE}/review_{piece["id"]}_{body.sex}_{tag}.png'; bpy.ops.render.render(write_still=True)
        body.body.hide_render = False; sc.display.shading.color_type = 'TEXTURE'
        me.color_attributes.remove(ca)
    before = int(kill.sum())
    adj = face_adjacency(ob)
    if before and before < len(kill) * 0.95:
        kill = smooth_mask(kill, adj, passes=2)
        # islands: after the skin goes, whatever small bits remain on the face (eyes, lips, an ear) are mannequin too
        alive = np.where(~kill)[0]
        if len(alive):
            parent = np.arange(len(kill))
            def find(a):
                while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
                return a
            ea = adj[~kill[adj[:, 0]] & ~kill[adj[:, 1]]]
            for a, b in ea:
                ra, rb = find(int(a)), find(int(b))
                if ra != rb: parent[ra] = rb
            lab2 = np.array([find(int(f)) for f in alive])
            u, cnt = np.unique(lab2, return_counts=True)
            small = set(u[cnt < max(40, int(0.004 * len(alive)))].tolist())
            drop = np.zeros(len(alive), bool)
            if small:
                is_small = np.isin(lab2, list(small))
                zone = (sd[alive] < 0.05) & (on_head[alive] | neck_zone[alive] | below_neck[alive])
                drop = is_small & zone
                if rule == 'colour': drop &= ~(d_h[alive] < d_m[alive] - 0.02)
            if rule == 'colour':
                # an island painted as the mannequin (an ear, a jaw) may be big: up to 3 % of the piece
                mid = set(u[cnt < max(40, int(0.03 * len(alive)))].tolist())
                is_mid = np.isin(lab2, list(mid))
                frac_m = {}
                mcol_a = (d_m[alive] < d_h[alive] - 0.02).astype(float)
                for cid, v in zip(lab2, mcol_a): frac_m.setdefault(cid, []).append(v)
                frac_m = {cid: float(np.mean(v)) for cid, v in frac_m.items()}
                mostly_m = np.array([frac_m[cid] > 0.6 for cid in lab2])
                zone2 = (sd[alive] < 0.08) & (on_head[alive] | neck_zone[alive] | below_neck[alive])
                drop |= is_mid & mostly_m & zone2
            # a whole component buried inside our head or neck (the mannequin's eyes under a fringe) is never hair
            if has_mannequin:
                comp_max = {}
                for cid, sdv in zip(lab2, sd[alive]): comp_max[cid] = max(comp_max.get(cid, -9.0), float(sdv))
                buried = np.array([comp_max[cid] < -0.002 for cid in lab2])
                buried &= (on_head[alive] | neck_zone[alive]) & (z[alive] < L['brow_z'] + 0.02)
                drop |= buried
                rep['buried_faces'] = int(buried.sum())
            kill[alive[drop]] = True
            rep['island_faces'] = int(drop.sum())
    rep['mannequin_faces_removed'] = int(kill.sum()); rep['mannequin_faces_rule'] = before
    if rule == 'colour':
        left = ~kill & (d_m < d_h - 0.02) & ((np.abs(sd) < 0.06) | below_neck)
        rep['mannequin_coloured_left'] = int(left.sum())
        if left.any():
            rep['left_zones'] = dict(head=int((left & on_head & (z < L['brow_z'] + 0.01)).sum()), scalp=int((left & on_head & (z >= L['brow_z'] + 0.01)).sum()),
                                     neck=int((left & neck_zone).sum()), below=int((left & below_neck).sum()), ear=int((left & ear_zone).sum()),
                                     sd_mm=[round(float(v) * 1000, 1) for v in np.percentile(sd[left], [5, 50, 95])])
    if kill.sum() >= len(kill) * 0.95:
        rep['mannequin_abort'] = 'rule would remove the whole piece'; return
    delete_faces(ob, kill)
    rep['faces_left'] = len(me.polygons)

def garment_mannequin_report(body, ob, rep, cols):
    """Garments: anything deep inside our body is suspect (a mannequin neck / arm / hand the sculpt grew on). Reported,
    and removed only when it is a skin-coloured cluster well inside the skin."""
    cent, fn = face_centers(ob)
    bvh, tris, sel = body.bvh(body.P, None)
    sd, loc, n, idx = signed_dist(bvh, cent, axes=body.axes_for(sel))
    deep = sd < -0.015
    rep['deep_faces'] = int(deep.sum()); rep['deepest_mm'] = round(-float(sd.min()) * 1000, 1)
    if cols is not None and deep.sum() > 50:
        c = np.median(cols[deep], axis=0); rep['deep_rgb'] = [round(float(x), 3) for x in c]
        outer = np.median(cols[sd > 0.0], axis=0); rep['outer_rgb'] = [round(float(x), 3) for x in outer]
    return sd

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

def transfer_weights(body, Pbody, garment_pts, groups, cols):
    """groups: list of (mask over garment verts, body region set). Nearest-surface barycentric weights, over the
    allowed bone columns only (dense (n, len(cols)))."""
    W = np.zeros((len(garment_pts), len(cols)), dtype=np.float32)
    BW = body.W[:, cols]
    for mask, regions in groups:
        if mask.sum() == 0: continue
        bvh, tris, _ = body.bvh(Pbody, regions)
        loc, n, idx, d = nearest_all(bvh, garment_pts[mask])
        idx = np.clip(idx, 0, len(tris) - 1)
        t = tris[idx]
        bc = bary(loc, Pbody[t[:, 0]], Pbody[t[:, 1]], Pbody[t[:, 2]])
        W[mask] = bc[:, 0:1] * BW[t[:, 0]] + bc[:, 1:2] * BW[t[:, 1]] + bc[:, 2:3] * BW[t[:, 2]]
    return W

def finish_weights(body, W, cols, P, edges, iters=2):
    """Smooth over the mesh edges, fill vertices that got no allowed bone from the nearest allowed segment, keep the
    four strongest bones, normalise."""
    n = len(W)
    deg = np.bincount(edges.reshape(-1), minlength=n).astype(np.float32)[:, None]
    for _ in range(iters):
        acc = np.zeros_like(W)
        np.add.at(acc, edges[:, 0], W[edges[:, 1]]); np.add.at(acc, edges[:, 1], W[edges[:, 0]])
        W = np.where(deg > 0, 0.5 * W + 0.5 * acc / np.maximum(deg, 1), W)
    zero = W.sum(1) < 1e-6
    if zero.any():
        segs = [(j, body.head_pos[body.bones[c]], body.tail_pos[body.bones[c]]) for j, c in enumerate(cols)]
        Z = P[zero]; best_d = np.full(len(Z), 1e9); best_j = np.zeros(len(Z), int)
        for j, a, b in segs:
            ab = b - a; t = np.clip(((Z - a) @ ab) / max(float(ab @ ab), 1e-9), 0, 1)[:, None]
            d = np.linalg.norm(Z - (a + t * ab), axis=1)
            better = d < best_d; best_d[better] = d[better]; best_j[better] = j
        W[np.where(zero)[0], best_j] = 1
    if W.shape[1] > 4:
        k = np.argsort(-W, axis=1)[:, 4:]
        np.put_along_axis(W, k, 0, axis=1)
    return W / np.maximum(W.sum(1, keepdims=True), 1e-9)

def apply_groups(body, ob, W, cols):
    for vg in list(ob.vertex_groups): ob.vertex_groups.remove(vg)
    for j, c in enumerate(cols):
        col = W[:, j]; idx = np.where(col > 1e-4)[0]
        if len(idx) == 0: continue
        vg = ob.vertex_groups.new(name=body.bones[c])
        q = np.clip(np.round(col[idx] * 255), 1, 255).astype(int)
        for qv in np.unique(q):
            vg.add(idx[q == qv].tolist(), float(qv / 255.0), 'REPLACE')
    md = ob.modifiers.new('Armature', 'ARMATURE'); md.object = body.arm
    ob.parent = body.arm

def inverse_lbs(body, W, cols, P):
    M = body.skin_matrices()[cols]                                   # (k, 4, 4)
    Mv = np.einsum('nk,kij->nij', W.astype(np.float64), M)
    Mi = np.linalg.inv(Mv)
    Ph = np.concatenate([P, np.ones((len(P), 1))], 1)
    return np.einsum('nij,nj->ni', Mi, Ph)[:, :3]

# ---------------------------------------------------------------------------------------------------------- hide maps
REACH = dict(top=(0.08, 0.10), bottoms=(0.06, 0.06), shoes=(0.03, 0.03), hair=(0.03, 0.07), hat=(0.03, 0.07), glove=(0.03, 0.03))

def covered_once(bvh, pt, nrm, reach=(RAY, RAY)):
    """Generous coverage (the authored-piece rule of rebuild_hide_maps.py): the piece within `reach[0]` straight above
    or `reach[1]` below this skin point, or within 1.5 cm around it. A sculpt is never pushed off the skin, so it may
    sit either side: a loose sleeve 8 cm out, a hairstyle whose core sits 7 cm inside our bigger cranium."""
    if bvh.ray_cast(pt + nrm * 0.001, nrm, reach[0])[0] is not None: return True
    if bvh.ray_cast(pt - nrm * 0.001, -nrm, reach[1])[0] is not None: return True
    return bvh.find_nearest(pt, NEAR)[0] is not None

def hide_map(body, ob, mode, piece, rep, PB=None):
    """Body-UV mask of the skin this piece covers (255 = hidden). mode: clothes | shoes | glove | hair | head.
    Faces whose three corners agree are filled or skipped whole; a face the edge crosses is tested per texel.
    A sculpt keeps its mannequin's proportions, so where it is slimmer than our body its surface lies inside the skin
    (Yugi's inner shirt 9 cm inside the chest, trouser legs inside the thighs): under a top the torso is hidden
    outright between the hem and the collar, under trousers / a skirt the legs and hips between hem and waist, under
    shoes the feet and the leg below the shoe top (the region rule of rebuild_hide_maps.py for authored pieces); the
    arms, and everything else, by the generous 3 cm / 1.5 cm coverage test."""
    bm = bmesh.new(); bm.from_mesh(ob.data); bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bvh = BVHTree.FromBMesh(bm); bm.free()
    hide = np.zeros((HR, HR), bool); covered = 0; refined = 0
    tris, uvs = body.tris, body.tri_uv
    if PB is None: PB = body.P
    kind = piece['kind']
    G = mesh_coords(ob); zlo, zhi = float(G[:, 2].min()), float(G[:, 2].max())
    region = None
    if mode == 'clothes' and kind == 'top':
        over_torso = body.tri_torso.copy()
        over_torso[(body.tri_arms > 0.5) & (np.abs(body.tri_center[:, 0]) < body.L['shoulder_hw'] + 0.05)] = 1.0   # deltoid / shoulder-blade skin under a top
        region = (over_torso, zlo + 0.005, zhi)
    elif mode == 'clothes' and kind == 'bottoms': region = (body.tri_legs, zlo - 0.01, zhi)
    elif mode == 'clothes' and kind == 'shoes': region = (body.tri_legs, -1.0, zhi - 0.01)
    rep['hide_region'] = [kind, round(zlo, 3), round(zhi, 3)] if region else None
    reach = REACH.get(kind, (RAY, RAY))
    def covered_pt(pt, nv, f):
        if region is not None and region[0][f] > 0.5 and region[1] <= pt.z <= region[2]: return True
        return covered_once(bvh, pt, nv, reach)
    for f in range(len(tris)):
        nv = Vector(body.tri_normal[f])
        if mode == 'head':
            states = (True, True, True) if (body.tri_region[f] == 'head' or (body.tri_region[f] == 'torso' and body.tri_center[f, 2] > body.L['neck_z'] - 0.01)) else (False, False, False)
        elif mode == 'hair':
            if not body.tri_scalp[f]: continue
            states = tuple(covered_once(bvh, Vector(PB[i]), nv, reach) for i in tris[f])
        elif mode == 'glove':
            states = (True, True, True) if body.tri_hands[f] > 0.35 else (False, False, False)
        else:
            if body.tri_keep[f] > 0.35: continue
            if body.tri_hands[f] > 0.35: continue
            if kind in ('bottoms', 'shoes') and body.tri_arms[f] > 0.35: continue
            if kind == 'shoes' and body.tri_feet[f] > 0.3: states = (True, True, True)
            else:
                states = tuple(covered_pt(Vector(PB[i]), nv, f) for i in tris[f])
        if not any(states): continue
        pts = np.array([(u * HR, (1 - v) * HR) for u, v in uvs[f]])
        x0, y0 = np.floor(pts.min(0)).astype(int) - 1; x1, y1 = np.ceil(pts.max(0)).astype(int) + 1
        if x1 < 0 or y1 < 0 or x0 >= HR or y0 >= HR: continue
        a, b, c3 = pts
        xs = np.arange(max(x0, 0), min(x1, HR - 1) + 1) + 0.5; ys = np.arange(max(y0, 0), min(y1, HR - 1) + 1) + 0.5
        if len(xs) == 0 or len(ys) == 0: continue
        gx, gy = np.meshgrid(xs, ys)
        den = (b[1] - c3[1]) * (a[0] - c3[0]) + (c3[0] - b[0]) * (a[1] - c3[1])
        if abs(den) < 1e-9: continue
        l1 = ((b[1] - c3[1]) * (gx - c3[0]) + (c3[0] - b[0]) * (gy - c3[1])) / den
        l2 = ((c3[1] - a[1]) * (gx - c3[0]) + (a[0] - c3[0]) * (gy - c3[1])) / den
        l3 = 1 - l1 - l2
        inside = (l1 >= -0.06) & (l2 >= -0.06) & (l3 >= -0.06)
        ys0, ys1, xs0, xs1 = max(y0, 0), min(y1, HR - 1) + 1, max(x0, 0), min(x1, HR - 1) + 1
        if all(states):
            covered += 1; hide[ys0:ys1, xs0:xs1] |= inside; continue
        refined += 1
        P0, P1, P2 = (PB[i] for i in tris[f])
        sub = np.zeros_like(inside)
        strict = (l1 >= 0) & (l2 >= 0) & (l3 >= 0)
        yy, xx = np.nonzero(strict)
        for k in range(len(yy)):
            w1 = float(l1[yy[k], xx[k]]); w2 = float(l2[yy[k], xx[k]]); w3 = 1.0 - w1 - w2
            if covered_pt(Vector(P0 * w1 + P1 * w2 + P2 * w3), nv, f): sub[yy[k], xx[k]] = True
        if sub.any():
            covered += 1; hide[ys0:ys1, xs0:xs1] |= sub
    rep['hide_faces'] = covered; rep['hide_hem_faces'] = refined; rep['hide_cover'] = round(float(hide.mean()), 4)
    return hide

def penetration(body, ob, rep, region_mask):
    """How far the piece's inner surface sits inside our skin over a body region (hair: the skull), in mm."""
    P = mesh_coords(ob)
    bvh, tris, sel = body.bvh(body.P, None)
    step = max(1, len(P) // 150000)
    sd, loc, n, idx = signed_dist(bvh, P[::step], axes=body.axes_for(sel))
    reg = region_mask[sel[np.clip(idx, 0, len(sel) - 1)]]
    inside = (sd < 0) & reg
    rep['inside_skull_mm'] = round(-float(sd[inside].min()) * 1000, 1) if inside.any() else 0.0
    rep['inside_skull_frac'] = round(float(inside.sum() / max(reg.sum(), 1)), 3)

# ---------------------------------------------------------------------------------------------------------- output
def export_fbx(ob, arm, path, name):
    ob.name = name; ob.data.name = name
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, add_leaf_bones=False, bake_anim=False,
                             use_mesh_modifiers=False, mesh_smooth_type='FACE', path_mode='STRIP',
                             embed_textures=False, axis_forward='-Z', axis_up='Y')

def setup_render():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_backface_culling = False; sc.display.shading.show_cavity = True
    sc.view_settings.view_transform = 'Standard'
    sc.render.resolution_percentage = 100
    cam = bpy.data.objects.get('ReviewCam')
    if cam is None:
        cd = bpy.data.cameras.new('ReviewCam'); cam = bpy.data.objects.new('ReviewCam', cd); sc.collection.objects.link(cam)
    sc.camera = cam; cam.data.type = 'ORTHO'
    return sc, cam

def hidden_body_copy(body, hide):
    """A render-only copy of the body without the faces the hide map hides (3 of a triangle's 4 UV samples hidden),
    so a review render shows what Unity shows: the skin under the sculpt gone, the sculpt's inside visible."""
    if hide is None: return None
    h = hide.shape[0]
    def hidden_at(u, v):
        return hide[int(np.clip((1 - v) * h, 0, h - 1)), int(np.clip(u * h, 0, h - 1))]
    drop = np.zeros(len(body.tris), bool)
    for f in range(len(body.tris)):
        uv = body.tri_uv[f]; c = uv.mean(0)
        n_hidden = int(hidden_at(c[0], c[1])) + sum(int(hidden_at(u, v)) for u, v in uv)
        drop[f] = n_hidden >= 3
    if not drop.any(): return None
    me = bpy.data.meshes.new('BodyHiddenRender')
    keep = body.tris[~drop]
    me.from_pydata([tuple(p) for p in body.P], [], [tuple(int(i) for i in t) for t in keep])
    me.update()
    uvl = me.uv_layers.new(name='UVMap')
    uvs = body.tri_uv[~drop].reshape(-1, 2)
    uvl.data.foreach_set('uv', uvs.reshape(-1).astype(np.float32))
    for m in body.body.data.materials: me.materials.append(m)
    ob = bpy.data.objects.new('BodyHiddenRender', me); bpy.context.scene.collection.objects.link(ob)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.shade_smooth()
    return ob

def render_review(body, ob, path, kind, P, hide=None):
    sc, cam = setup_render()
    proxy = hidden_body_copy(body, hide)
    sc.render.resolution_x = 360; sc.render.resolution_y = 520; sc.render.film_transparent = False
    lo, hi = P.min(0), P.max(0)
    z0, z1 = lo[2] - 0.08, hi[2] + 0.08
    if kind in ('shoes', 'glove'): z1 = hi[2] + 0.12
    if kind in ('hair', 'hat', 'head', 'glasses'): z0 = min(z0, body.L['chin_z'] - 0.12)
    ext = max((z1 - z0), (hi[0] - lo[0]) * 1.3, 0.3)
    cam.data.ortho_scale = ext * 1.08
    cz = (z0 + z1) / 2
    for tag, (px, py, rz) in {'front': (0, -6, 0), 'side': (6, 0, math.pi / 2), 'q': (4.2, -4.2, math.pi / 4), 'back': (0, 6, math.pi), 'alone': (0, -6, 0)}.items():
        body.body.hide_render = tag == 'alone' or proxy is not None
        if proxy is not None: proxy.hide_render = tag == 'alone'
        cam.location = (px, py, cz); cam.rotation_euler = (math.pi / 2, 0, rz)
        sc.render.filepath = f'{path}_{tag}.png'
        bpy.ops.render.render(write_still=True)
    body.body.hide_render = False
    if proxy is not None:
        me = proxy.data; bpy.data.objects.remove(proxy, do_unlink=True); bpy.data.meshes.remove(me)

def render_thumb(body, ob, path, kind, P):
    """256^2 transparent tile, framed like tools/custom/clothes_thumbs.py (fixed frame per slot) on a dark ghost body."""
    sc, cam = setup_render()
    sc.render.resolution_x = sc.render.resolution_y = 256; sc.render.film_transparent = True; sc.render.image_settings.color_mode = 'RGBA'
    BP = body.P; z_lo, z_hi = float(BP[:, 2].min()), float(BP[:, 2].max()); H = z_hi - z_lo
    slot = {'top': 'shirt', 'bottoms': 'bottoms', 'shoes': 'shoes', 'hat': 'hat', 'head': 'hat', 'glasses': 'face', 'neck': 'neck', 'glove': 'hands', 'hair': 'hair'}[kind]
    SLOT_Z = {'shirt': (z_lo + 0.58 * H, z_lo + 0.90 * H), 'bottoms': (z_lo + 0.02 * H, z_lo + 0.70 * H), 'shoes': (z_lo - 0.01 * H, z_lo + 0.30 * H),
              'hat': (z_lo + 0.86 * H, z_hi + 0.02 * H), 'face': (z_lo + 0.86 * H, z_hi + 0.02 * H)}
    lo, hi = P.min(0), P.max(0); c = (lo + hi) / 2
    if slot == 'hair':
        body.body.hide_render = True
        cam.data.ortho_scale = max(hi[2] - lo[2], hi[0] - lo[0]) * 1.25 + 0.04
        cam.location = (3.5, -4.5, (lo[2] + hi[2]) / 2 + 0.01); cam.rotation_euler = (math.pi / 2 - 0.12, 0, math.radians(38))
        sc.render.filepath = path; bpy.ops.render.render(write_still=True)
        body.body.hide_render = False; sc.render.film_transparent = False
        return
    ghost = bpy.data.materials.get('thumb_ghost')
    if ghost is None:
        ghost = bpy.data.materials.new('thumb_ghost'); ghost.use_nodes = True
        ghost.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.16, 0.17, 0.21, 1); ghost.diffuse_color = (0.16, 0.17, 0.21, 1)
    saved = [m for m in body.body.data.materials]
    body.body.data.materials.clear(); body.body.data.materials.append(ghost)
    if slot == 'face':
        c = np.array((0.0, c[1], c[2])); ext = 0.20; a = math.radians(12); elev = 0.15
    elif slot in SLOT_Z:
        z0, z1 = SLOT_Z[slot]; c = np.array((c[0], c[1], (z0 + z1) / 2)); ext = (z1 - z0) * 1.25; a = math.radians(35); elev = 0.35
    else:
        ext = max(float(max(hi - lo)) * 1.35, 0.14); a = math.radians(80 if slot == 'hands' else 35); elev = 0.35
    d = 4.0
    cam.location = (c[0] + d * math.sin(a), c[1] - d * math.cos(a), c[2] + elev)
    cam.data.ortho_scale = ext
    direction = Vector((c[0], c[1], c[2])) - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    cam.data.clip_start = 0.01; cam.data.clip_end = 100.0
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
    body.body.data.materials.clear()
    for m in saved: body.body.data.materials.append(m)
    cam.rotation_euler = (math.pi / 2, 0, 0); sc.render.film_transparent = False

# ---------------------------------------------------------------------------------------------------------- main
def process(body, piece, path, out, render=True, hides=True):
    sex = body.sex; kind = piece['kind']; is_hair = kind == 'hair'
    rep = dict(id=piece['id'], kind=kind, sex=sex, file=os.path.basename(path), name=piece['name'])
    for o in list(bpy.data.objects):
        if o not in body.keep and o.type == 'MESH': bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.orphans_purge(do_recursive=True)
    body.reset_pose()
    ob = append_piece(path)
    rep['tris'] = len(ob.data.polygons); rep['verts'] = len(ob.data.vertices)
    imgs = piece_images(ob)
    # textures: the packed JPEG / PNG bytes, untouched (base colour and normal map; the metallic / roughness map is
    # not used by the cel shader; an emission map is kept only when it is not black)
    stem_base = f'{out}/{"Hair_" if is_hair else "albedo_"}{piece["id"]}'
    first = sex == 'male' or piece['sex'] == 'female'
    if first or not glob.glob(stem_base + '.*'):
        rep['albedo'] = os.path.basename(save_packed(imgs.get('base'), stem_base) or '')
        if imgs.get('normal'): rep['normal'] = os.path.basename(save_packed(imgs['normal'], f'{out}/normal_{piece["id"]}') or '')
        if imgs.get('emission'):
            em = image_pixels(imgs['emission']); rep['emission_mean'] = round(float(em.mean()), 4)
            if em.mean() > 0.02: rep['emission'] = os.path.basename(save_packed(imgs['emission'], f'{out}/emission_{piece["id"]}') or '')
    rep['texture'] = list(imgs['base'].size) if imgs.get('base') else None
    px = image_pixels(imgs['base']) if (imgs.get('base') and kind in ('hair', 'hat', 'top', 'bottoms', 'shoes', 'head')) else None
    cols = face_colours(ob, px) if px is not None else None
    del px
    edges = edges_of(ob)
    cols_idx = allowed_cols(body, kind)
    W = None
    if kind == 'top':
        P, sleeve, poses = fit_top(body, ob, piece, rep)
        for s_, (ax, d) in poses.items():
            body.pose_bone(('Left' if s_ == 'L' else 'Right') + 'Arm', rot_between(ax, d))
        Pb = body.posed_coords()
        rest = ~(sleeve['L'] | sleeve['R'])
        groups = [(sleeve['L'], {'armL'}), (sleeve['R'], {'armR'}), (rest, {'torso', 'head'})]
        W = finish_weights(body, transfer_weights(body, Pb, P, groups, cols_idx), cols_idx, P, edges)
        if poses:
            P = inverse_lbs(body, W, cols_idx, P); set_coords(ob, P)
        body.reset_pose()
        apply_groups(body, ob, W, cols_idx)
        garment_mannequin_report(body, ob, rep, cols)
    elif kind == 'bottoms':
        P, legs, poses = fit_bottoms(body, ob, piece, rep)
        for s_, (ax, d) in poses.items():
            body.pose_bone(('Left' if s_ == 'L' else 'Right') + 'UpLeg', rot_between(ax, d))
        Pb = body.posed_coords()
        rest = ~(legs['L'] | legs['R'])
        groups = [(legs['L'], {'legL', 'footL'}), (legs['R'], {'legR', 'footR'}), (rest, {'torso', 'legL', 'legR'})]
        W = finish_weights(body, transfer_weights(body, Pb, P, groups, cols_idx), cols_idx, P, edges)
        if poses:
            P = inverse_lbs(body, W, cols_idx, P); set_coords(ob, P)
        body.reset_pose()
        apply_groups(body, ob, W, cols_idx)
        garment_mannequin_report(body, ob, rep, cols)
    elif kind == 'shoes':
        foot_flat_pose(body)
        Pb = body.posed_coords()
        P, sides = fit_shoes(body, ob, piece, rep, Pb)
        edges = edges_of(ob)
        groups = [(sides['L'], {'legL', 'footL'}), (sides['R'], {'legR', 'footR'})]
        W = finish_weights(body, transfer_weights(body, Pb, P, groups, cols_idx), cols_idx, P, edges)
        # the hide map in this pose (the shoe and the posed body agree here), then the shoe is un-posed with the foot
        if hides:
            hm_posed = hide_map(body, ob, 'clothes', piece, rep, PB=Pb)
        P = inverse_lbs(body, W, cols_idx, P); set_coords(ob, P)
        body.reset_pose()
        apply_groups(body, ob, W, cols_idx)
    elif kind in ('hair', 'hat', 'head'):
        P = fit_headmount(body, ob, piece, rep)
        if kind == 'head':
            # a head preset replaces our head: the mannequin shoulders it was generated with go (below the neck base)
            cent, fn = face_centers(ob)
            cut = cent[:, 2] < body.L['neck_z'] - 0.03
            rep['mannequin_faces_removed'] = delete_faces(ob, cut); rep['faces_left'] = len(ob.data.polygons)
        else:
            remove_mannequin(body, ob, piece, rep, cols)
        edges = edges_of(ob); P = mesh_coords(ob)
        penetration(body, ob, rep, body.tri_scalp if kind != 'head' else (body.tri_region == 'head'))
        all_ = np.ones(len(P), bool)
        W = finish_weights(body, transfer_weights(body, body.P, P, [(all_, {'head', 'torso'})], cols_idx), cols_idx, P, edges)
        apply_groups(body, ob, W, cols_idx)
    elif kind == 'glasses':
        P = fit_glasses(body, ob, piece, rep)
        W = np.zeros((len(P), len(cols_idx)), dtype=np.float32); W[:, 0] = 1
        apply_groups(body, ob, W, cols_idx)
    elif kind == 'neck':
        P = fit_neck(body, ob, piece, rep)
        all_ = np.ones(len(P), bool)
        W = finish_weights(body, transfer_weights(body, body.P, P, [(all_, {'torso', 'head'})], cols_idx), cols_idx, P, edges)
        apply_groups(body, ob, W, cols_idx)
    elif kind == 'glove':
        P, sides = fit_glove(body, ob, piece, rep)
        groups = [(sides['L'], {'handL', 'armL'}), (sides['R'], {'handR', 'armR'})]
        W = finish_weights(body, transfer_weights(body, body.P, P, groups, cols_idx), cols_idx, P, edges)
        apply_groups(body, ob, W, cols_idx)
    P = mesh_coords(ob)
    tot = W.sum(0); top = np.argsort(-tot)[:6]
    rep['bones'] = {body.bones[cols_idx[i]].replace(B, ''): round(float(tot[i]) / len(P), 3) for i in top if tot[i] > 0}
    rep['bbox'] = [[round(float(v), 3) for v in P.min(0)], [round(float(v), 3) for v in P.max(0)]]
    rep['tris_out'] = len(ob.data.polygons)
    stem = f'{out}/{piece["id"]}_{sex}'
    fbx = f'{out}/argon_{"hair_" if is_hair else ""}{piece["id"]}_{sex}.fbx'
    export_fbx(ob, body.arm, fbx, ('ArgonHair_' + piece['id']) if is_hair else 'ArgonCloth')
    rep['fbx'] = os.path.basename(fbx); rep['fbx_mb'] = round(os.path.getsize(fbx) / 1e6, 1)
    hm = None
    if hides and kind == 'shoes':
        hm = hm_posed
    elif hides and kind not in ('glasses', 'neck'):
        mode = {'head': 'head', 'hair': 'hair', 'hat': 'hair', 'glove': 'glove'}.get(kind, 'clothes')
        hm = hide_map(body, ob, mode, piece, rep)
        if hm.any(): write_png_gray(f'{out}/hide_{piece["id"]}_{sex}.png', (hm * 255).astype(np.uint8)); rep['hide'] = f'hide_{piece["id"]}_{sex}.png'
        else: hm = None
    if render:
        render_review(body, ob, f'{out}/review_{piece["id"]}_{sex}', kind, P, hide=hm)
        if is_hair:
            if first: render_thumb(body, ob, f'{out}/thumb_{piece["id"]}.png', kind, P)
        else:
            render_thumb(body, ob, f'{out}/thumb_{piece["id"]}_{sex}.png', kind, P)
    json.dump(rep, open(f'{stem}.json', 'w'), indent=1)
    keys = ('scale', 'floor_rescale', 'sleeve_rescale', 'hip_hw_ratio', 'hem_hw_vs_ankle', 'fit_mode', 'fit_scale', 'fit_rms_mm', 'sleeve_L_deg', 'sleeve_R_deg', 'leg_L_deg',
            'colour_rule', 'colour_sep', 'mannequin_faces_removed', 'island_faces', 'faces_left', 'inside_skull_mm', 'deep_faces', 'deepest_mm', 'hide_cover', 'fbx_mb')
    log(f'{piece["id"]:22s} {sex:6s} tris {rep["tris"]} ' + ' '.join(f'{k}={rep[k]}' for k in keys if k in rep))
    return rep

def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = argv[argv.index('--out') + 1] if '--out' in argv else f'{PROJ}/Temp/MeshyWardrobe'
    only = argv[argv.index('--only') + 1] if '--only' in argv else None
    sex_f = argv[argv.index('--sex') + 1] if '--sex' in argv else None
    os.makedirs(out, exist_ok=True)
    global CLUSTER_TILE
    if '--clusters' in argv: CLUSTER_TILE = out
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
                reports.append(process(body, pc, f, out, render='--no-render' not in argv, hides='--no-hide' not in argv))
            except Exception as e:
                import traceback; traceback.print_exc()
                log('FAILED', pc['id'], sex, repr(e))
                reports.append(dict(id=pc['id'], sex=sex, error=repr(e)))
    json.dump(reports, open(f'{out}/reports_{int(time.time())}.json', 'w'), indent=1)
    log('done', len(reports), 'pieces')

if __name__ == '__main__':
    main()
