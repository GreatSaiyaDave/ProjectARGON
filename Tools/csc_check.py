#!/usr/bin/env python3
"""Offline compile of this checkout's Assembly-CSharp (and -Editor) with Roslyn, reusing the live Editor's own compiler
arguments (Library/Bee/artifacts/*/Assembly-CSharp.rsp of the project that has a Library/). ~15 s, no Editor round trip.

  python3 Tools/csc_check.py [--lib <project with Library>] [--editor] [--only <substring of a source path>]
                             [--exclude <substr,substr>]   (drop sources, e.g. a peer's file that needs their uncommitted ones)

Sources come from THIS checkout (every .cs the Editor's list names that exists here, plus any .cs under Assets/ that the
list lacks), references and defines from the Editor's list. Errors are printed as file(line,col): CSxxxx message; with
--only, only those whose path contains the substring (other sessions' half-landed files are not your problem).
"""
import glob, os, re, subprocess, sys
HERE = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
argv = sys.argv[1:]
LIB = argv[argv.index('--lib') + 1] if '--lib' in argv else '/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON'
ONLY = argv[argv.index('--only') + 1] if '--only' in argv else None
EXCLUDE = argv[argv.index('--exclude') + 1].split(',') if '--exclude' in argv else []   # drop sources (another session's half-landed files)
EDITOR = '--editor' in argv
DOTNET = os.path.expanduser('~/.dotnet/dotnet')
CSC = sorted(glob.glob(os.path.expanduser('~/.dotnet/sdk/*/Roslyn/bincore/csc.dll')))[-1]
dag = sorted(glob.glob(f'{LIB}/Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp'), key=os.path.getmtime)[-1]
dagdir = os.path.dirname(dag)
OUT = '/tmp/csc_check'; os.makedirs(OUT, exist_ok=True)

def build(name, rsp_path, extra_refs=()):
    lines = open(rsp_path).read().splitlines()
    out = []
    srcs = []
    for l in lines:
        s = l.strip()
        if not s: continue
        if s.startswith(('-out:', '-refout:', '-additionalfile:', '-analyzer:', '-generatedfilesout:')): continue
        m = re.match(r'^(-r:|-reference:)"?([^"]+)"?$', s)
        if m:
            p = m.group(2)
            if not os.path.isabs(p): p = os.path.join(LIB, p)
            if 'Assembly-CSharp' in os.path.basename(p) and extra_refs:   # the editor assembly: use our fresh build
                continue
            out.append(f'-r:"{p}"'); continue
        if s.endswith('.cs') or s.endswith('.cs"'):
            srcs.append(s.strip('"')); continue
        out.append(s)
    for r in extra_refs: out.append(f'-r:"{r}"')
    # this checkout's sources: the Editor's list where it exists here, plus files the list lacks
    have = set(srcs)
    is_editor = name.endswith('Editor')
    for f in glob.glob(f'{HERE}/Assets/**/*.cs', recursive=True):
        rel = os.path.relpath(f, HERE)
        if rel in have: continue
        parts = rel.split(os.sep)
        ed = 'Editor' in parts[:-1]
        if ed == is_editor and 'Plugins' not in parts: srcs.append(rel)
    srcs = [s for s in srcs if os.path.exists(os.path.join(HERE, s)) and not any(x in s for x in EXCLUDE)]
    out.append(f'-out:"{OUT}/{name}.dll"')
    out += [f'"{s}"' for s in srcs]
    rsp = f'{OUT}/{name}.rsp'; open(rsp, 'w').write('\n'.join(out) + '\n')
    r = subprocess.run([DOTNET, CSC, '-nologo', f'@{rsp}'], cwd=HERE, capture_output=True, text=True)
    errs = [l for l in r.stdout.splitlines() + r.stderr.splitlines() if ': error ' in l]
    warn = len([l for l in r.stdout.splitlines() if ': warning ' in l])
    shown = [e for e in errs if not ONLY or ONLY in e]
    print(f'[{name}] {len(srcs)} sources, {len(errs)} errors ({len(shown)} shown), {warn} warnings, exit {r.returncode}')
    for e in shown[:60]: print('  ' + e)
    return r.returncode == 0, errs

ok, errs = build('Assembly-CSharp', dag)
if EDITOR:
    build('Assembly-CSharp-Editor', os.path.join(dagdir, 'Assembly-CSharp-Editor.rsp'), extra_refs=[f'{OUT}/Assembly-CSharp.dll'])
