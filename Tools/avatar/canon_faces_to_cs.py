#!/usr/bin/env python3
"""Tools/avatar/canon_faces.json -> the FaceCatalog.Characters entries (between CANON_FACES_BEGIN / END).

The json is the measurement record of every canon duelist's living face: eye / brow / mouth / nose parameters measured
from the sourced art (Faces/<id>.png bands built from official Duel Links art and series frames, checked against the
owner's episode library) by Tools/avatar/canon_faces workflow agents and an independent re-measurement each. Edit the
json, run this, commit both.   usage: python3 Tools/avatar/canon_faces_to_cs.py [--check]
"""
import json, os, sys
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
JSON = os.path.join(ROOT, 'Tools', 'avatar', 'canon_faces.json')
CS = os.path.join(ROOT, 'Assets', 'Scripts', 'WRLDZ', 'Data', 'FaceCatalog.cs')
BEGIN, END = '// CANON_FACES_BEGIN', '// CANON_FACES_END'

def f(v): return ('%.4g' % float(v)).replace('e-0', 'e-') + 'f' if not float(v).is_integer() else '%df' % int(v)
def s(v): return '"' + str(v).replace('"', '\\"') + '"'

def entry(c):
    e, b, m, n, sp = c['eye'], c['brow'], c['mouth'], c['nose'], c.get('special', {})
    hide = {'none': 0, 'left': 1, 'right': 2}.get(sp.get('hideEye', 'none'), 0)
    lines = [
        f"            new() {{ FaceId = {s(c['faceId'])}, Name = {s(c['displayName'])}, Source = {s(c.get('sourceLine', 'Faces/' + c['faceId'] + '.png'))},",
        f"                Eye = new EyeShape {{ Id = {s('canon_' + c['faceId'])}, W = {f(e['W'])}, H = {f(e['H'])}, Tilt = {f(e['Tilt'])}, Round = {f(e['Round'])}, TopBulge = {f(e['TopBulge'])}, BotBulge = {f(e['BotBulge'])}, Lash = {f(e['Lash'])}, LashOuter = {f(e['LashOuter'])}, LowerLash = {f(e['LowerLash'])}, Iris = {f(e['Iris'])}, IrisScaleY = {f(e['IrisScaleY'])}, Pupil = {f(e['Pupil'])}, RestLid = {f(e['RestLid'])} }},",
        f"                IrisHex = {s(c['irisHex'])},",
        f"                Brow = new BrowShape {{ Id = {s('canon_' + c['faceId'])}, Thick = {f(b['Thick'])}, Arch = {f(b['Arch'])}, Tilt = {f(b['Tilt'])} }}, BrowPeak = {f(b.get('Peak', 0.25))},{(' BrowHex = ' + s(b['hex']) + ',') if b.get('hex') else ''}",
        f"                Nose = new NoseShape {{ Id = {s('canon_' + c['faceId'])}, Style = {int(n['Style'])}, Size = {f(n['Size'])}, Shade = {f(n['Shade'])} }},",
        f"                Mouth = new MouthShape {{ Id = {s('canon_' + c['faceId'])}, W = {f(m['W'])}, LipLine = {f(m['LipLine'])}, LowerLip = {f(m['LowerLip'])}, RestCorner = {f(m['RestCorner'])}, OpenShape = {f(m['OpenShape'])} }},",
        f"                HideEye = {hide}, KeepDecal = {'true' if sp.get('keepDecal') else 'false'} }},",
    ]
    return '\n'.join(lines)

def main():
    data = json.load(open(JSON))
    chars = data['characters'] if isinstance(data, dict) else data
    body = '\n'.join(entry(c) for c in chars)
    src = open(CS).read()
    a = src.index(BEGIN) + len(BEGIN); b = src.index(END)
    head = src[:a]; tail = src[b:]
    # keep the BEGIN line's trailing comment text up to the newline
    nl = head.rfind('\n'); first = src[a:src.index('\n', a)]
    new = src[:a] + first + '\n' + body + '\n            ' + tail
    if '--check' in sys.argv:
        print('up to date' if new == src else 'OUT OF DATE'); sys.exit(0 if new == src else 1)
    open(CS, 'w').write(new); print('wrote', len(chars), 'canon faces into', os.path.relpath(CS, ROOT))

if __name__ == '__main__':
    main()
