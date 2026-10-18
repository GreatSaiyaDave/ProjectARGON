#!/usr/bin/env python3
"""Install the output of meshy_wardrobe.py into the Unity project (v2, 2026-10-18: full-density sculpts).

  python3 Tools/avatar/meshy_wardrobe_install.py <out_dir> [--project <repo root>] [--only <substr>] [--catalogue-only]

- clothes pieces -> Assets/Resources/WRLDZ/Avatar3D/Clothes/argon_<id>_<sex>.fbx, albedo_<id>.<jpg|png>, normal_<id>.*,
                    emission_<id>.* (when the sculpt had one), hide_<id>_<sex>.png (body-UV hide map), thumb_<id>_<sex>.png
- hair           -> Assets/Resources/WRLDZ/Avatar3D/Hair/argon_hair_<id>_<sex>.fbx, Hair_<id>.*, normal_<id>.*, hide_<id>_<sex>.png
                    (the scalp under the hair), thumb_<id>.png, and a style entry (pattern "original": the texture shows
                    as painted) in Hair/hair_catalog.json
- a texture that changes extension (the first pass wrote downscaled PNGs; the sculpt's own data is JPEG) has its old
  file and .meta removed so Resources.Load("albedo_<id>") stays unambiguous.
The Unity Editor picks the files up on its next refresh (AvatarImportPostprocessor: 4096 px, normal maps, readable
full-density meshes).
"""
import glob, json, os, shutil, sys
from PIL import Image

argv = sys.argv[1:]
OUT = argv[0]
def opt(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
PROJ = opt('--project', os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
ONLY = opt('--only')
RES = f'{PROJ}/Assets/Resources/WRLDZ/Avatar3D'
CLOTHES, HAIR = f'{RES}/Clothes', f'{RES}/Hair'

def log(*a): print('[install]', *a, flush=True)

def put(src, dst):
    """Copy src over dst; any sibling of dst with the same stem but another extension (and its .meta) goes."""
    stem, ext = os.path.splitext(dst)
    for old in glob.glob(stem + '.*'):
        if old.endswith('.meta'): continue
        if os.path.splitext(old)[1] != ext:
            os.remove(old); log('removed stale', os.path.basename(old))
            if os.path.exists(old + '.meta'): os.remove(old + '.meta')
    shutil.copy(src, dst)

reports = []
for j in sorted(glob.glob(f'{OUT}/*_male.json') + glob.glob(f'{OUT}/*_female.json')):
    r = json.load(open(j))
    if 'error' in r or 'fbx' not in r: continue
    if ONLY and ONLY not in r['id']: continue
    reports.append(r)
log(len(reports), 'fitted pieces')

hair_ids = {}
CAT_ONLY = '--catalogue-only' in argv     # only hair_catalog.json (a code checkout that never holds the binaries)
for r in reports:
    pid, sex, kind = r['id'], r['sex'], r['kind']
    dest = HAIR if kind == 'hair' else CLOTHES
    if CAT_ONLY:
        if kind == 'hair': hair_ids[pid] = r['name']
        continue
    put(f'{OUT}/{r["fbx"]}', f'{dest}/{r["fbx"]}')
    for key in ('albedo', 'normal', 'emission'):
        if r.get(key): put(f'{OUT}/{r[key]}', f'{dest}/{r[key]}')
    if r.get('hide'): put(f'{OUT}/{r["hide"]}', f'{dest}/{r["hide"]}')
    else:
        for stale in (f'{dest}/hide_{pid}_{sex}.png',):
            if os.path.exists(stale): os.remove(stale); log('removed stale', os.path.basename(stale))
            if os.path.exists(stale + '.meta'): os.remove(stale + '.meta')
    if kind == 'hair':
        if os.path.exists(f'{OUT}/thumb_{pid}.png'): put(f'{OUT}/thumb_{pid}.png', f'{HAIR}/thumb_{pid}.png')
        hair_ids[pid] = r['name']
    elif os.path.exists(f'{OUT}/thumb_{pid}_{sex}.png'):
        put(f'{OUT}/thumb_{pid}_{sex}.png', f'{CLOTHES}/thumb_{pid}_{sex}.png')
log('copied FBX, textures, hide maps, thumbnails')

if hair_ids:
    cat_path = f'{HAIR}/hair_catalog.json'
    cat = json.load(open(cat_path))
    have = {s['id'] for s in cat['styles']}
    for hid, name in hair_ids.items():
        # the painted root colour for the scalp strip: the median of the texture's mid-bright pixels
        root = '#3A3A3A'
        tex = sorted(glob.glob(f'{HAIR}/Hair_{hid}.*')) + sorted(glob.glob(f'{OUT}/Hair_{hid}.*'))
        tex = [t for t in tex if not t.endswith('.meta')]
        if tex:
            im = Image.open(tex[0]).convert('RGB').resize((128, 128))
            px = [p for p in im.getdata() if 25 < sum(p) / 3 < 235]
            if px:
                px.sort(key=lambda p: sum(p)); p = px[len(px) // 2]; root = '#%02X%02X%02X' % p
        entry = dict(id=hid, name=name + ' (sculpt)', inspiredBy=name + ' — Meshy image-to-3D sculpt, 2026-10-17',
                     series='DM', sex='any', length='medium',
                     palette=dict(name=name + ' (sculpt)', pattern='original', root=root, tip=root, fringe=root, streak=root, side=root))
        if hid in have:
            cat['styles'] = [dict(s, **entry) if s['id'] == hid else s for s in cat['styles']]
        else:
            cat['styles'].append(entry)
    json.dump(cat, open(cat_path, 'w'), indent=1, ensure_ascii=False)
    log('hair catalogue:', len(hair_ids), 'styles', sorted(hair_ids))
log('done')
