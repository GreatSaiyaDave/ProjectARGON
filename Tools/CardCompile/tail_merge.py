#!/usr/bin/env python3
"""Resolve the *.rej files `git apply --reject` leaves when two card groups appended at the same tail.

Most integration conflicts are two pure insertions at one anchor: an enum tail, the BatchAtomSets array,
CardBatchRegressionTests.Run, a switch's last arm, an EffectVocabulary list. git refuses the second group's hunk
because the lines after its anchor are now the first group's lines. This tool re-applies such a hunk the way an
integrator would by hand: keep both, earlier group first.

Only PURE-INSERTION hunks are touched (no '-' lines; "-X" + "+X," — a list's last item gaining a comma — counts as
context, and the merged block then puts a comma on the list's current last item). Context compares ignore trailing
whitespace and commas. For each one:
  1. exact:  the hunk's leading + trailing context still sit next to each other (git just could not place it) →
             insert between them (unique match, or the one nearest the hunk's line number);
  2. after:  the leading context matches, and the trailing context follows within a window, with the lines in between
             (another group's insertion) balanced in {} () [] — so the block lands at the same nesting level, after
             the other group's lines;
  3. already: the inserted block is already in the file at that spot → nothing to do.
  If the full 3+3 context fails (another group edited an outer context line), shorter contexts are tried; those must
  match exactly one place.
Anything else (a '-' line, ambiguous anchors, unbalanced gap) stays in the .rej for a human/agent to merge.
The file is written only when every hunk of that .rej is resolved, unless --partial; then the .rej keeps only the
hunks still open. Always re-run the gate afterwards (headless --quiet, csc_full, enum_guard): this tool does not compile.

Usage:
  python3 tail_merge.py <repo> [--check] [--partial] [--window N] [file.rej ...]
    (no .rej given → every *.rej under <repo>/Assets and <repo>/Tools)
Exit 0 when no .rej is left open, 1 otherwise. Prints one line per hunk.
"""
import argparse
import os
import re
import sys

HUNK_RE = re.compile(r'^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@')


def parse_rej(path):
    """Return (header_lines, hunks). Each hunk = dict(header, lines=[(tag, text)], new_start)."""
    with open(path, encoding='utf-8', errors='surrogateescape') as f:
        raw = f.read().split('\n')
    if raw and raw[-1] == '':
        raw.pop()
    header, hunks, cur = [], [], None
    for line in raw:
        m = HUNK_RE.match(line)
        if m:
            cur = {'header': line, 'lines': [], 'new_start': int(m.group(3))}
            hunks.append(cur)
        elif cur is None:
            header.append(line)
        elif line.startswith('\\'):
            continue  # "\ No newline at end of file"
        else:
            tag, text = (line[:1] or ' '), line[1:]
            cur['lines'].append((tag, text))
    return header, hunks


def target_of(rej_path, header):
    for line in header:
        if line.startswith('+++ '):
            p = line[4:].strip()
            if p.startswith('b/'):
                p = p[2:]
            return p
    return rej_path[:-4]


def balanced(lines):
    """True when {} () [] are balanced over these lines, ignoring // comments and string/char literals (roughly)."""
    depth = {'{': 0, '(': 0, '[': 0}
    pairs = {'}': '{', ')': '(', ']': '['}
    for ln in lines:
        s = re.sub(r'"(?:\\.|[^"\\])*"', '""', ln)
        s = re.sub(r"'(?:\\.|[^'\\])'", "''", s)
        s = s.split('//', 1)[0]
        for ch in s:
            if ch in depth:
                depth[ch] += 1
            elif ch in pairs:
                depth[pairs[ch]] -= 1
                if depth[pairs[ch]] < 0:
                    return False
    return all(v == 0 for v in depth.values())


def key(line):
    """Comparison key: trailing whitespace and a trailing comma do not matter (enum / array / argument tails)."""
    return line.rstrip().rstrip(',').rstrip()


def find_all(F, seq, lo=0, hi=None):
    hi = len(F) if hi is None else hi
    n = len(seq)
    if n == 0:
        return []
    ks = [key(x) for x in seq]
    return [i for i in range(lo, max(lo, hi - n + 1)) if [key(x) for x in F[i:i + n]] == ks]


def comma_pairs(lines):
    """'-X' directly followed by '+X,' (the last enum member / array item gaining a comma) becomes context 'X' with a
    comma_fix flag: the merged block then gets a comma on whatever line ends the list now. Returns (lines, fix)."""
    out, fix, i = [], False, 0
    while i < len(lines):
        t, x = lines[i]
        if (t == '-' and i + 1 < len(lines) and lines[i + 1][0] == '+'
                and lines[i + 1][1].rstrip() == x.rstrip() + ','):
            out.append((' ', x))
            fix = True
            i += 2
            continue
        out.append((t, x))
        i += 1
    return out, fix


def split_blocks(lines):
    """Split a pure-insertion hunk into (pre_ctx, added, post_ctx, offset) tuples, one per contiguous '+' block."""
    out = []
    idx = 0
    n = len(lines)
    while idx < n:
        if lines[idx][0] != '+':
            idx += 1
            continue
        start = idx
        while idx < n and lines[idx][0] == '+':
            idx += 1
        pre = []
        j = start - 1
        while j >= 0 and lines[j][0] == ' ' and len(pre) < 3:
            pre.insert(0, lines[j][1])
            j -= 1
        post = []
        j = idx
        while j < n and lines[j][0] == ' ' and len(post) < 3:
            post.append(lines[j][1])
            j += 1
        # line offset of this block inside the hunk (new-file numbering), for nearest-match tie breaks
        offset = sum(1 for t, _ in lines[:start] if t in ' +')
        out.append((pre, [t for _, t in lines[start:idx]], post, offset))
    return out


def _code(line):
    return line.split('//', 1)[0].rstrip()


def add_list_comma(F, at):
    """Give the last code line before index `at` a trailing comma (comma_fix). Skips blanks, comments, attributes."""
    i = at - 1
    while i >= 0:
        st = F[i].strip()
        if not st or st.startswith(('//', '/*', '*', '[')):
            i -= 1
            continue
        code = _code(F[i])
        if code.endswith((',', '{', '(')):
            return
        rest = F[i][len(code):]
        F[i] = code + ',' + rest
        return


def _place(F, pre, post, hint, window, strict):
    """Insertion index for one (pre, post) context pair, or (None, note)."""
    exact = find_all(F, pre + post) if pre and post else []
    if exact:
        best = min(exact, key=lambda i: abs(i + len(pre) - hint))
        if len(exact) == 1 or (not strict and abs(best + len(pre) - hint) < 40):
            return best + len(pre), 'exact', f'line {best + len(pre) + 1}'
        return None, None, f'{len(exact)} exact anchors'
    if not pre or not post:
        return None, None, 'one-sided context'
    cands = []
    for p in find_all(F, pre):
        a = p + len(pre)
        for q in find_all(F, post, a, min(len(F), a + window + len(post))):
            if balanced(F[a:q]):
                cands.append((p, q))
                break
    if not cands:
        return None, None, 'no balanced gap between the anchors'
    if len(cands) > 1:
        cands.sort(key=lambda pq: abs(pq[0] + len(pre) - hint))
        if strict or abs(cands[0][0] + len(pre) - hint) >= 40:
            return None, None, f'{len(cands)} anchors'
    p, q = cands[0]
    return q, 'after', f'line {q + 1} (after {q - p - len(pre)} lines of another group)'


def place_block(F, pre, add, post, hint, window):
    """Return (index_to_insert_at | -1 already | None, strategy, note)."""
    if not pre and not post:
        return None, 'open', 'no context'
    # already applied right there? (both contexts must match for a one-line block)
    for i in find_all(F, add):
        before_ok = bool(pre) and find_all(F, pre, max(0, i - len(pre)), i) != []
        after_ok = bool(post) and find_all(F, post, i + len(add), i + len(add) + len(post)) != []
        if (before_ok and after_ok) or (len(add) >= 2 and (before_ok or after_ok)):
            return -1, 'already', f'line {i + 1}'
    # full context first (nearest to the hunk's line wins), then shrinking context (must be unique): another group's
    # edit may have touched one of the outer context lines.
    at, how, note = _place(F, pre, post, hint, window, strict=False)
    if at is not None:
        return at, how, note
    first_note = note
    tries = []
    for k in range(len(pre), 0, -1):
        for m in range(len(post), 0, -1):
            if (k, m) != (len(pre), len(post)) and k + m >= 2:
                tries.append((k + m, k, m))
    for _, k, m in sorted(tries, reverse=True):
        at, how, note = _place(F, pre[len(pre) - k:], post[:m], hint, window, strict=True)
        if at is not None:
            return at, how + f'(ctx {k}+{m})', note
    return None, 'open', first_note


def resolve(repo, rej, check, partial, window):
    header, hunks = parse_rej(rej)
    target = os.path.join(repo, target_of(rej, header))
    if not os.path.exists(target):
        print(f'OPEN    {rej}: target {target} missing')
        return False
    with open(target, encoding='utf-8', errors='surrogateescape', newline='') as f:
        text = f.read()
    eol = '\r\n' if '\r\n' in text else '\n'
    trailing_nl = text.endswith(eol)
    F = text.split(eol)
    if trailing_nl:
        F.pop()
    rel = os.path.relpath(target, repo)
    open_hunks = []
    shift = 0  # lines inserted so far, for hints
    for h in hunks:
        lines, comma_fix = comma_pairs(h['lines'])
        if any(t == '-' for t, _ in lines):
            print(f'OPEN    {rel} {h["header"]}: hunk removes lines (merge by hand)')
            open_hunks.append(h)
            continue
        ok = True
        planned = []
        G = list(F)
        for pre, add, post, off in split_blocks(lines):
            hint = h['new_start'] - 1 + off + shift
            at, how, note = place_block(G, pre, add, post, hint, window)
            if at is None:
                ok = False
                print(f'OPEN    {rel} {h["header"]}: {note}')
                break
            if at >= 0:
                if comma_fix:
                    add_list_comma(G, at)
                G[at:at] = add
                shift += len(add)
            planned.append(f'{how} {note} (+{len(add)})')
        if ok:
            F = G
            print(f'MERGED  {rel} {h["header"]}: ' + '; '.join(planned))
        else:
            open_hunks.append(h)
    resolved_all = not open_hunks
    if check:
        return resolved_all
    if resolved_all or partial:
        if len(open_hunks) < len(hunks):
            with open(target, 'w', encoding='utf-8', errors='surrogateescape', newline='') as f:
                f.write(eol.join(F) + (eol if trailing_nl else ''))
        if resolved_all:
            os.remove(rej)
        else:
            with open(rej, 'w', encoding='utf-8', errors='surrogateescape') as f:
                out = header[:]
                for h in open_hunks:
                    out.append(h['header'])
                    out.extend(t + s for t, s in h['lines'])
                f.write('\n'.join(out) + '\n')
    return resolved_all


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('repo')
    ap.add_argument('rej', nargs='*')
    ap.add_argument('--check', action='store_true', help='report only, write nothing')
    ap.add_argument('--partial', action='store_true', help='apply the resolvable hunks even if others stay open')
    ap.add_argument('--window', type=int, default=400, help='max lines of another group between the anchors')
    a = ap.parse_args()
    rejs = a.rej
    if not rejs:
        rejs = []
        for top in ('Assets', 'Tools', 'Docs'):
            for root, _, files in os.walk(os.path.join(a.repo, top)):
                rejs += [os.path.join(root, f) for f in files if f.endswith('.rej')]
    left = 0
    for r in sorted(rejs):
        if not resolve(a.repo, r, a.check, a.partial, a.window):
            left += 1
    print(f'tail_merge: {len(rejs) - left}/{len(rejs)} .rej resolved' + (' (check only)' if a.check else ''))
    sys.exit(1 if left else 0)


if __name__ == '__main__':
    main()
