#!/usr/bin/env python3
"""Install the output of meshy_wardrobe.py into the Unity project.

  python3 Tools/avatar/meshy_wardrobe_install.py <out_dir> [--project <repo root>] [--only <substr>]
                                                 [--skip-hides] [--skip-thumbs] [--tools <dir with rebuild_hide_maps.py, clothes_thumbs.py>]

- clothes pieces -> Assets/Resources/WRLDZ/Avatar3D/Clothes/argon_<id>_<sex>.fbx + albedo_<id>.png
- hair           -> Assets/Resources/WRLDZ/Avatar3D/Hair/argon_hair_<id>_<sex>.fbx + Hair_<id>.png + thumb_<id>.png,
                    and a style entry (pattern "original": the texture shows as painted) in Hair/hair_catalog.json
- hide maps      -> rebuild_hide_maps.py --only _3d (shirts / bottoms / shoes: the 3 cm coverage rule), then the
                    head presets' and gloves' maps are rasterised here from the body UV triangles in the fit report
                    (the whole head / the hands), 2048 px, 255 = hidden
- thumbnails     -> clothes_thumbs.py both --only _3d (Clothes/thumb_<id>_<sex>.png)
The Unity Editor picks the files up on its next refresh (AvatarImportPostprocessor sets the texture flags).
"""
import glob, json, os, shutil, subprocess, sys
from PIL import Image, ImageDraw

argv = sys.argv[1:]
OUT = argv[0]
def opt(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
PROJ = opt('--project', os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
ONLY = opt('--only')
TOOLS = opt('--tools', os.path.expanduser('~/argon-avatar/tools/custom'))
RES = f'{PROJ}/Assets/Resources/WRLDZ/Avatar3D'
CLOTHES, HAIR = f'{RES}/Clothes', f'{RES}/Hair'
HR = 2048

def log(*a): print('[install]', *a, flush=True)

def hide_png(tris, path):
    img = Image.new('L', (HR, HR), 0); d = ImageDraw.Draw(img)
    for tri in tris:
        pts = [(u * HR, (1 - v) * HR) for u, v in tri]
        d.polygon(pts, fill=255, outline=255)
    img.save(path)

reports = []
for j in sorted(glob.glob(f'{OUT}/*_male.json') + glob.glob(f'{OUT}/*_female.json')):
    r = json.load(open(j))
    if 'error' in r or 'fbx' not in r: continue
    if ONLY and ONLY not in r['id']: continue
    reports.append(r)
log(len(reports), 'fitted pieces')

hair_ids = {}
for r in reports:
    pid, sex, kind = r['id'], r['sex'], r['kind']
    if kind == 'hair':
        shutil.copy(f'{OUT}/{r["fbx"]}', f'{HAIR}/argon_hair_{pid}_{sex}.fbx')
        if os.path.exists(f'{OUT}/Hair_{pid}.png'): shutil.copy(f'{OUT}/Hair_{pid}.png', f'{HAIR}/Hair_{pid}.png')
        if os.path.exists(f'{OUT}/thumb_{pid}.png'): shutil.copy(f'{OUT}/thumb_{pid}.png', f'{HAIR}/thumb_{pid}.png')
        hair_ids[pid] = r['name']
    else:
        shutil.copy(f'{OUT}/{r["fbx"]}', f'{CLOTHES}/argon_{pid}_{sex}.fbx')
        if os.path.exists(f'{OUT}/albedo_{pid}.png'): shutil.copy(f'{OUT}/albedo_{pid}.png', f'{CLOTHES}/albedo_{pid}.png')
log('copied FBX + textures')

if '--skip-hides' not in argv:
    # the coverage-rule maps for the garments (skirts / robes included), never for hats, eyewear or neck pieces
    cmd = ['blender', '-b', '--python', f'{TOOLS}/rebuild_hide_maps.py', '--', '--only', ONLY or '_3d']
    log('hide maps:', ' '.join(cmd))
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=1800)
    for r in reports:
        pid, sex, kind = r['id'], r['sex'], r['kind']
        p = f'{CLOTHES}/hide_{pid}_{sex}.png'
        if kind in ('hat', 'glasses', 'neck') and os.path.exists(p): os.remove(p)
        if kind in ('head', 'glove') and r.get('hide_uv_tris'):
            hide_png(r['hide_uv_tris'], p); log('wrote', os.path.basename(p), len(r['hide_uv_tris']), 'tris')
    n = len([r for r in reports if os.path.exists(f'{CLOTHES}/hide_{r["id"]}_{r["sex"]}.png')])
    log(n, 'hide maps present for the fitted pieces')

if '--skip-thumbs' not in argv:
    cmd = ['blender', '-b', '--python', f'{TOOLS}/clothes_thumbs.py', '--', 'both', '--only', ONLY or '_3d']
    log('thumbs:', ' '.join(cmd))
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=1800)
    n = len([r for r in reports if r['kind'] != 'hair' and os.path.exists(f'{CLOTHES}/thumb_{r["id"]}_{r["sex"]}.png')])
    log(n, 'clothes thumbnails present')

if hair_ids:
    cat_path = f'{HAIR}/hair_catalog.json'
    cat = json.load(open(cat_path))
    have = {s['id'] for s in cat['styles']}
    for hid, name in hair_ids.items():
        # the painted root colour for the scalp strip: the median of the texture's mid-bright pixels
        root = '#3A3A3A'
        tex = f'{HAIR}/Hair_{hid}.png'
        if os.path.exists(tex):
            im = Image.open(tex).convert('RGB').resize((128, 128))
            px = [p for p in im.getdata() if 25 < sum(p) / 3 < 235]
            if px:
                px.sort(key=lambda p: sum(p)); p = px[len(px) // 2]; root = '#%02X%02X%02X' % p
        entry = dict(id=hid, name=name + ' (sculpt)', inspiredBy=name + ' — Meshy image-to-3D sculpt, 2026-10-17',
                     series='DM', sex='any', length='medium',
                     palette=dict(name=name + ' (sculpt)', pattern='original', root=root, tip=root, fringe=root, streak=root, side=root))
        if hid in have:
            cat['styles'] = [entry if s['id'] == hid else s for s in cat['styles']]
        else:
            cat['styles'].append(entry)
    json.dump(cat, open(cat_path, 'w'), indent=1, ensure_ascii=False)
    log('hair catalogue:', len(hair_ids), 'styles', sorted(hair_ids))
log('done')
