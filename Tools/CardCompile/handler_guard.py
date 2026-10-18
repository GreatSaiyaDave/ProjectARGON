#!/usr/bin/env python3
"""Hollow-card guard: what the compiler emits must be read by the engine, and every batch must be registered.

A clause's action only does something if engine code reads it (`case EffectActionKind.X:`, `== EffectActionKind.X`,
`is EffectActionKind.X`, a list of handled kinds, ...). Writing it (`Action = EffectActionKind.X` in an atom/template)
and naming it in EffectVocabulary.cs (classification only) do not. An action that is emitted but never read compiles a
card to Implemented that does nothing in a duel (2026-10-18: Xing Zhen Hu, Triangle Ecstasy Spark, Harpie Lady 3,
Riryoku, ...). A batch whose Atoms / RegressionTests files exist but are not registered in AtomRegistration.BatchAtomSets
/ CardBatchRegressionTests.Run is a half-landed batch (2026-10-18: v178 NormalMonsterSpells, SpellTrapSealsAndImmunity).

usage: python3 Tools/CardCompile/handler_guard.py [--repo R] [--list]
Exit 1 on a hollow action or an unregistered batch file. READ_ELSEWHERE lists actions read by timing or flags on purpose.
"""
import argparse
import os
import re
import sys

# Actions whose clauses are read by their timing or flags, not by the action name. Add one only with the reader named.
READ_ELSEWHERE = {
    # ArchfiendRidersRuntime reads prog.ClausesFor(EffectTiming.HandQuickEffectWhenNamedDestroyed).
    'SpecialSummonDestroyedNamedFromGyBySendingThisFromHand',
    # ChainLinkAnswersRuntime / TextEffectRuntime.CanAnswer read NegateRespondsToMonsterEffect + DestroyNegatedCard.
    'NegateMonsterEffectActivationAndDestroy',
}

ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
ap.add_argument('--repo', default='.')
ap.add_argument('--list', action='store_true', help='also print the readers found per emitted action')
a = ap.parse_args()
duel = os.path.join(a.repo, 'Assets/Scripts/WRLDZ/Duel')


def read(p):
    with open(p, encoding='utf-8', errors='replace') as f:
        return f.read()


prog = read(os.path.join(duel, 'TextEffects/CompiledCardProgram.cs'))
body = re.search(r'\benum\s+EffectActionKind\b[^{]*\{(.*?)\n\s*\}', prog, re.S).group(1)
members = set(re.findall(r'^\s*([A-Za-z_]\w*)\s*(?:=\s*[^,]+)?,', re.sub(r'//.*', '', body), re.M))
# Non-test sources without comments: an Atoms class is live when other engine code names it (BatchAtomSets or a direct
# TryParse call from the compiler, e.g. AttributeBulletsAtoms).
code_by_file = {}
for root, _, files in os.walk(duel):
    for f in files:
        if f.endswith('.cs') and not f.endswith('Tests.cs'):
            q = os.path.join(root, f)
            code_by_file[q] = re.sub(r'//.*', '', read(q))

ref = re.compile(r'EffectActionKind\.([A-Za-z_]\w*)\b')
write = re.compile(r'\bAction\s*[:=]\s*EffectActionKind\.([A-Za-z_]\w*)\b')
emitted, readers, unregistered = {}, {}, []
for root, _, files in os.walk(duel):
    for f in sorted(files):
        if not f.endswith('.cs'):
            continue
        p = os.path.join(root, f)
        rel = os.path.relpath(p, duel)
        stem = f[:-3]
        test = f.endswith('Tests.cs')
        vocab = f in ('EffectVocabulary.cs', 'CompiledCardProgram.cs', 'EffectProgramValidator.cs')
        live_atoms = True
        if ('/Batches/' in p.replace(os.sep, '/') and f.endswith('Atoms.cs') and
                not any(re.search(r'\b' + stem + r'\b', t) for q, t in code_by_file.items() if q != p)):
            unregistered.append(f'{rel} (not in AtomRegistration.BatchAtomSets, and nothing else calls it)')
            live_atoms = False
        if '/Rules/Batches/' in p.replace(os.sep, '/') and f.endswith('RegressionTests.cs'):
            others = any(re.search(r'\b' + stem + r'\.Run\b', read(os.path.join(r2, f2)))
                         for r2, _, fs2 in os.walk(duel) for f2 in fs2 if f2.endswith('.cs') and f2 != f)
            if not others:
                unregistered.append(f'{rel} (Run is never called)')
        for i, line in enumerate(read(p).split('\n'), 1):
            code = line.split('//', 1)[0]
            ws = write.findall(code)
            if not test and live_atoms:
                for x in ws:
                    emitted.setdefault(x, f'{rel}:{i}')
            if test or vocab:
                continue
            refs = ref.findall(code)
            for x in refs:
                if x in ws and len(refs) == 1:
                    continue  # the write itself
                readers.setdefault(x, []).append(f'{rel}:{i}')

# EffectVocabulary.HasNoRuntime (v208) lists retired actions: the compiler fails any program using one closed, so an
# atom that still emits it cannot make a card Implemented.
vocab_src = read(os.path.join(duel, 'TextEffects/EffectVocabulary.cs'))
mm = re.search(r'static bool HasNoRuntime\b[^=]*=>(.*?);', vocab_src, re.S)
retired = set(ref.findall(mm.group(1))) if mm else set()
hollow = sorted(x for x in emitted if x in members and x != 'None' and x not in readers and x not in READ_ELSEWHERE
                and x not in retired)
print(f'handler_guard: {len(members)} actions, {len(emitted)} emitted by live templates, '
      f'{len(hollow)} with no runtime reader, {len(unregistered)} unregistered batch files'
      f'{f", {len(retired)} retired (EffectVocabulary.HasNoRuntime)" if retired else ""}')
for x in hollow:
    print(f'  HOLLOW {x}  (emitted at {emitted[x]}): add its runtime arm, or retire it in EffectVocabulary.HasNoRuntime')
for u in unregistered:
    print(f'  UNREGISTERED {u}')
if a.list:
    for x in sorted(emitted):
        print(f'  {x}: {len(readers.get(x, []))} reader(s) ' + ' '.join(readers.get(x, [])[:3]))
sys.exit(1 if hollow or unregistered else 0)
