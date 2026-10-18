export const meta = {
  name: 'compile-card-batch',
  description: 'Compile the next Yu-Gi-Oh! cards into the ARGON engine: worklist, parallel isolated batches, rules+engine review, sequential integration, seed export, report',
  whenToUse: 'Project ARGON: the owner asks to compile more / the next batch of / specific cards into the engine. Playbook: Tools/CardCompile/README.md',
  phases: [
    { title: 'Scout', detail: 'baseline suite, status dump, worklist, shape groups, isolated engine copies' },
    { title: 'Implement', detail: 'one implementer per group, each in its own engine copy' },
    { title: 'Review', detail: 'rules-accuracy and engine-safety reviewers per group' },
    { title: 'Fix', detail: 'resolve confirmed findings; cards that cannot be made right fail closed' },
    { title: 'Integrate', detail: 'apply groups one at a time to the real checkout, suite green after each' },
    { title: 'Audit', detail: 'cross-group review + long AI-duel stress on the integrated engine' },
    { title: 'Finalize', detail: 'compiler version note, seed export, full verification, batch report + docs' },
  ],
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────
// Arguments (all optional):
//   repo        — the Unity project root (default: the owner's ProjectARGON checkout)
//   tag         — run name, used for the run folder and the report (default 'batch')
//   runDir      — where copies / patches / logs go (default ~/.cache/wrldz-card-compile/<tag>[-N])
//   sets        — curriculum set codes to draw from, e.g. ['LON','LOD'] (default: earliest sets with gaps)
//   cards       — explicit card names or ids (overrides sets)
//   targetCards — how many cards to take when choosing sets automatically (default 30)
//   engineLast  — integrate the card groups first, then the ENGINE groups (strong integrator adapts the card code)
//   maxCards    — cap on the cards the planner puts in groups (the rest: skipped as "next batch"); use it with sets
//   groups      — explicit groups [{id:'PascalCase', title, cards:[names], notes}] (skips auto-grouping)
//   dryRun      — stop after grouping (no copies, no edits)
//   integrate   — false: stop after Fix, leaving reviewed patches in <runDir>/patches for a human
//   stressDuels — AI-vs-AI duels in the final audit (default 250)
//   holdGroups  — group ids to leave for a later run: not implemented, reviewed or integrated now (their copies stay).
//                 Use it on a resume after a usage-limit stop so finished groups land before new implementers take the
//                 agent slots; the prompts of every other agent are unchanged, so finished agents still replay from cache.
//   priorTrees  — {groupId: path} an earlier run's copy holding partial work for that group; the new implementer ports it.
//   extraGroups — [{id,title,cards,notes}] ENGINE-SUBSYSTEM groups built in the same batch (forward-looking engine work:
//                 the cards they unblock are the acceptance tests). The planner keeps them and adds worklist cards that
//                 need those subsystems to them instead of skipping those cards.
//   foreignFails — substrings of suite check names that fail in the real checkout because of OTHER sessions' uncommitted
//                 work (e.g. ['SeedFreshness', 'Toons: AI']); the baseline and integration gates accept exactly those.
//   prefilled   — {groupId: <implementer result>} finished implementer results to reuse on a resume (from journal.jsonl
//                 'result' lines). Without this, a finished implementer that comes after a failed one in the call order
//                 is re-run, because the resume cache only replays the unbroken prefix of calls (~0.5–1M tokens each).
// Peer sessions that land in the real checkout while a run is open: append what they changed (files, lines to keep,
// intended --allow ids) to <runDir>/PEER_LANDINGS.md. Integrators, the auditor and the finalizer read it.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
const A = args || {}
const REPO = A.repo || '/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON'
const HOME = REPO.split('/').slice(0, 3).join('/')
const TAG = A.tag || 'batch'
const RUN_HINT = A.runDir || `${HOME}/.cache/wrldz-card-compile/${TAG}`
const TOOLS = `${REPO}/Tools/CardCompile`
const STRESS = A.stressDuels || 250
const HELD = new Set(A.holdGroups || [])
const PRIOR = A.priorTrees || {}
const PRE = A.prefilled || {}
const FOREIGN = A.foreignFails || []

const CONTEXT = `
You are working on Project ARGON (Unity 6 Yu-Gi-Oh! TCG rules engine, C#) at ${REPO}.
READ FIRST from ${TOOLS}/README.md (the card-compilation playbook) only the sections "Rules that keep the engine honest"
and "Gotchas already paid for" (grep -n '^## ' to find them). Everything below assumes them.

TOKEN BUDGET (the owner pays per turn; past batches hit the spend limit, so this is a hard rule):
- Never cat or Read a whole engine file: TextEffectRuntime.cs is ~500 KB and DuelEngine.cs ~250 KB. Use grep -n, then
  sed -n 'A,Bp' on ranges of at most ~150 lines.
- Batch independent shell commands into one call. Pipe long output through tail / grep (suite: ... --quiet 2>&1 | tail -3).
- Iterate with headless.sh <tree> --card "<name>". Run the full --quiet suite only to gate (about 3 times per agent).
- Do not re-derive what the notes, patch or this prompt already state. Stop when the job is done; do not polish.

ENGINE MAP: read ${TOOLS}/ENGINE_MAP.md first (only the sections for your cards' shapes): verified symbols, how-to recipes per
mechanic (atom, action, cost, target, choice, trigger, window, lock, procedure, equip), test helpers and known gaps. It
replaces most exploration. Quick index (Assets/Scripts/WRLDZ/Duel/TextEffects unless noted):
- CardTextEffectCompiler.cs — Compile() entry, PSCT sentence grammar, the Version constant.
- Templates: AtomRegistration.cs (whole-sentence atoms; BatchAtomSets = one line per batch), SharedPsctAtomTemplates.cs,
  LegacyTextTemplates.cs, MonsterTriggerTemplates.cs, ClassicEraTemplates.cs, ContinuousRestrictionTemplates.cs,
  PhaseTriggerTemplates.cs, IgnitionTemplates.cs, AdvancedEffectTemplates.cs, ProtectionTemplates.cs, ArchfiendTemplates.cs.
- CompiledCardProgram.cs — EffectClause fields; enums EffectActionKind / EffectTiming / EffectZoneFilter / EffectSide
  (APPEND ONLY — stored as ints); CompiledCardProgram.FullyCompiled / CanResolveAny.
- EffectVocabulary.cs — ResolutionOf (map every new action), IsUniqueException.
- TextEffectRuntime.cs (~10.5k lines: grep it, never read it whole) — ApplyClause switch, CollectTargets, MapTargetKind,
  AutoPick, trigger firing (Fire*/Notify*/TryTrigger*).
- ContinuousProtections.cs; ../FieldSpellEffects.cs (continuous auras on each RefreshBoard); ../SpellTrapEffects.cs
  (activation, legacy target mirror MapCompiledTargetKind / CollectLegalTargets, AiAutoRespond); ../DuelEngine.cs
  (rules core: summons, battle, chain, response windows); ../DuelModels.cs (CardInstance, DuelistState);
  ../CardEffectStatus.cs (Classify); ../OfficialEffectRegistry.cs; ../MonsterEffects.cs.
- Tests: ../Rules/InteractionRegressionTests.cs (internal helpers Fresh, ClearBoard, PlaceMonster, PlaceSetTrap, PutInHand,
  DrainCombat, ReachOpponentMain, ReachOpponentBattle, SentenceAtomMisses), ../Rules/TcgRegressionTests.cs (corpus guards,
  some assert a card stays NOT FullyCompiled), ../Rules/CardBatchRegressionTests.cs (one call line per batch).
References (read-only): printed text + how a card compiles now = bash ${TOOLS}/headless.sh <tree> --card "<name or id>" (or
the argon-thrifty MCP tool "card"); NEVER read cards_db.json or eras/prelink_monsters.json (huge). The official ygopro script
for a card = Assets/StreamingAssets/OcgCore/scripts/official~/c<id>.lua (authoritative for timing, costs, targets,
conditions); rules = .cursor/skills/ygo-gamedev/SKILL.md and references/psct-and-timing.md; branch hints =
${TOOLS}/branch_refs.json (read a commit with: git -C ${REPO} show <sha> -- <path>; never merge or check out a branch).

Hard rules:
- Fail closed: a card is Implemented only when every sentence compiled to a template that does exactly what it says.
  Never compile a partial effect, invent a rider, or flatten a "for each". A card that cannot be done right stays as it was.
- No card-id branches (if (cardId == …)) outside EffectVocabulary.IsUniqueException; template the SENTENCE.
- Whole-sentence atoms only; new enum members only at the END of their enum, each with a trailing comma (keep one after the last).
- Never change CardTextEffectCompiler.Version (the integration run owns it).
- In ${REPO}: no git stash / reset / checkout / clean / commit / branch; no Unity Editor or \`unity\` CLI commands
  (other sessions share that Editor). Do not edit UI, Presentation or Editor files (other sessions own them): a card that
  needs a UI flow the engine does not already drive through PendingActivation / TargetKind must be deferred.
- Tools: bash ${TOOLS}/headless.sh <tree> --quiet | --card "<name>" | --status-dump ; python3 ${TOOLS}/status_diff.py ;
  python3 ${TOOLS}/enum_guard.py --repo <tree> ; python3 ${TOOLS}/handler_guard.py --repo <tree> (every emitted action has a
  runtime reader, every batch Atoms / RegressionTests file is registered) ; python3 ${TOOLS}/worklist.py <status.tsv> --cards "A;B" --md .
${FOREIGN.length ? `- KNOWN FOREIGN FAILURES: the real checkout's suite fails these checks because of OTHER sessions' uncommitted work (never
  fix their files): ${FOREIGN.map(x => JSON.stringify(x)).join(', ')}. Wherever a gate says "→ PASS" / "result=PASS", read it as:
  every FAIL line contains one of those strings and nothing else fails (copies made from the working tree may show them too).
` : ''}`

const PREP_SCHEMA = {
  type: 'object',
  properties: {
    runDir: { type: 'string' },
    compilerVersion: { type: 'integer' },
    suitePass: { type: 'boolean' },
    unitPass: { type: 'integer' },
    cscOk: { type: 'boolean' },
    cscDetail: { type: 'string' },
    worklistPath: { type: 'string' },
    worklistCount: { type: 'integer' },
    sets: { type: 'array', items: { type: 'string' } },
    abort: { type: 'boolean' },
    abortReason: { type: 'string' },
  },
  required: ['runDir', 'compilerVersion', 'suitePass', 'worklistPath', 'worklistCount', 'abort'],
}

const GROUP_SCHEMA = {
  type: 'object',
  properties: {
    groups: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          title: { type: 'string' },
          cards: { type: 'array', items: { type: 'string' } },
          notes: { type: 'string' },
        },
        required: ['id', 'title', 'cards', 'notes'],
      },
    },
    skipped: {
      type: 'array',
      items: { type: 'object', properties: { name: { type: 'string' }, reason: { type: 'string' } }, required: ['name', 'reason'] },
    },
  },
  required: ['groups', 'skipped'],
}

const TREES_SCHEMA = {
  type: 'object',
  properties: {
    treesCreated: { type: 'array', items: { type: 'string' } },
    oldVersion: { type: 'integer' },
    newVersion: { type: 'integer' },
    seedBeforePath: { type: 'string' },
    notes: { type: 'string' },
  },
  required: ['treesCreated', 'newVersion', 'seedBeforePath'],
}

const CARD_DONE = {
  type: 'object',
  properties: { id: { type: 'integer' }, name: { type: 'string' }, how: { type: 'string' } },
  required: ['name', 'how'],
}
const CARD_DEFER = {
  type: 'object',
  properties: { id: { type: 'integer' }, name: { type: 'string' }, reason: { type: 'string' } },
  required: ['name', 'reason'],
}
const IMPL_PROPS = {
  group: { type: 'string' },
  implemented: { type: 'array', items: CARD_DONE },
  deferred: { type: 'array', items: CARD_DEFER },
  suitePass: { type: 'boolean' },
  unitPass: { type: 'integer' },
  unitFail: { type: 'integer' },
  statusDiffOk: { type: 'boolean' },
  otherStatusChanges: { type: 'array', items: { type: 'string' } },
  enumGuardOk: { type: 'boolean' },
  newEnumMembers: { type: 'array', items: { type: 'string' } },
  newClauseFields: { type: 'array', items: { type: 'string' } },
  sharedCodeChanges: { type: 'array', items: { type: 'string' } },
  tests: { type: 'array', items: { type: 'string' } },
  patchPath: { type: 'string' },
  notesPath: { type: 'string' },
}
const IMPL_SCHEMA = {
  type: 'object',
  properties: IMPL_PROPS,
  required: ['group', 'implemented', 'deferred', 'suitePass', 'statusDiffOk', 'enumGuardOk', 'patchPath', 'notesPath'],
}
const FIX_SCHEMA = {
  type: 'object',
  properties: {
    ...IMPL_PROPS,
    resolutions: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          title: { type: 'string' },
          resolution: { type: 'string', enum: ['fixed', 'disputed', 'card-deferred', 'not-applicable'] },
          evidence: { type: 'string' },
        },
        required: ['title', 'resolution', 'evidence'],
      },
    },
  },
  required: ['group', 'implemented', 'deferred', 'suitePass', 'statusDiffOk', 'enumGuardOk', 'patchPath', 'notesPath', 'resolutions'],
}
const REVIEW_SCHEMA = {
  type: 'object',
  properties: {
    lens: { type: 'string' },
    cards: {
      type: 'array',
      items: { type: 'object', properties: { name: { type: 'string' }, correct: { type: 'boolean' }, notes: { type: 'string' } }, required: ['name', 'correct'] },
    },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          card: { type: 'string' },
          severity: { type: 'string', enum: ['high', 'medium', 'low'] },
          title: { type: 'string' },
          scenario: { type: 'string' },
          location: { type: 'string' },
          suggestedFix: { type: 'string' },
        },
        required: ['severity', 'title', 'scenario'],
      },
    },
  },
  required: ['lens', 'cards', 'findings'],
}
const INTEGRATE_SCHEMA = {
  type: 'object',
  properties: {
    group: { type: 'string' },
    applied: { type: 'boolean' },
    integratedCards: { type: 'array', items: { type: 'string' } },
    droppedCards: { type: 'array', items: CARD_DEFER },
    conflicts: { type: 'array', items: { type: 'string' } },
    suitePass: { type: 'boolean' },
    unitPass: { type: 'integer' },
    cscOk: { type: 'boolean' },
    cscReleaseOk: { type: 'boolean' },
    enumGuardOk: { type: 'boolean' },
    statusDiffOk: { type: 'boolean' },
    statusPath: { type: 'string' },
    rolledBack: { type: 'boolean' },
    notes: { type: 'string' },
  },
  required: ['group', 'applied', 'integratedCards', 'suitePass', 'rolledBack', 'notes'],
}
const AUDIT_SCHEMA = {
  type: 'object',
  properties: {
    issues: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          severity: { type: 'string', enum: ['high', 'medium', 'low'] },
          title: { type: 'string' },
          detail: { type: 'string' },
          fixed: { type: 'boolean' },
        },
        required: ['severity', 'title', 'detail', 'fixed'],
      },
    },
    stress: { type: 'string' },
    suitePass: { type: 'boolean' },
    cardsDropped: { type: 'array', items: CARD_DEFER },
  },
  required: ['issues', 'stress', 'suitePass'],
}
const FINAL_SCHEMA = {
  type: 'object',
  properties: {
    version: { type: 'integer' },
    seedPrograms: { type: 'integer' },
    suitePass: { type: 'boolean' },
    unitPass: { type: 'integer' },
    cscOk: { type: 'boolean' },
    cscReleaseOk: { type: 'boolean' },
    enumGuardOk: { type: 'boolean' },
    statusTable: { type: 'string' },
    implementedCards: { type: 'array', items: { type: 'string' } },
    reportPath: { type: 'string' },
    docsUpdated: { type: 'array', items: { type: 'string' } },
    notes: { type: 'string' },
  },
  required: ['version', 'suitePass', 'statusTable', 'implementedCards', 'reportPath', 'docsUpdated'],
}

// ── Scout ──────────────────────────────────────────────────────────────────────────────────────────
phase('Scout')
const cardsArg = A.cards && A.cards.length ? A.cards : (A.groups ? A.groups.flatMap(g => g.cards) : null)
const prep = await agent(`${CONTEXT}
ROLE: batch preparer (run "${TAG}"). You set up the run; you edit nothing in ${REPO}.
1. Run folder: use ${RUN_HINT}. If it already exists and is non-empty, use ${RUN_HINT}-2, -3, … (first free one).
   Create <runDir>/{trees,patches,snapshots,logs}.
2. Baseline gate in the real checkout: bash ${TOOLS}/headless.sh ${REPO} --quiet > <runDir>/logs/baseline_suite.log 2>&1.
   It must end with result=PASS. If it FAILS, set abort=true and put the failing check names in abortReason: a batch
   must never start on a red engine. Also run bash ${TOOLS}/csc_overlay.sh ${REPO} (whole Unity project compile): report
   cscOk and, if it fails, which files (another session may be mid-edit outside Duel/ — that is not an abort).
3. bash ${TOOLS}/headless.sh ${REPO} --status-dump > <runDir>/status_before.tsv ; python3 ${TOOLS}/version_bump.py --show.
   python3 ${TOOLS}/enum_guard.py and python3 ${TOOLS}/handler_guard.py --repo ${REPO} (report both).
4. Worklist → <runDir>/worklist.json and worklist.md (python3 ${TOOLS}/worklist.py <runDir>/status_before.tsv …):
   ${cardsArg ? `--cards "${cardsArg.join(';')}" (explicit cards; report any name not found)` :
     A.sets ? `--sets ${A.sets.join(',')}` :
     `choose the earliest curriculum sets (order in the playbook) that still have Unimplemented/Stub cards, adding sets
      until the worklist has at least ${A.targetCards || 30} cards; pass them with --sets`}
   Write both --json and --md outputs.
Return the structured result (runDir = the folder you actually used).`, { label: 'prepare', phase: 'Scout', schema: PREP_SCHEMA, model: 'sonnet', effort: 'low' })

if (!prep || prep.abort) {
  return { stopped: 'baseline', prep }
}
const RUN = prep.runDir
log(`Run ${RUN}: compiler v${prep.compilerVersion}, baseline suite ${prep.suitePass ? 'PASS' : 'FAIL'}, ${prep.worklistCount} cards on the worklist`)

let groups = A.groups
let skipped = []
if (!groups) {
  const g = await agent(`${CONTEXT}
ROLE: batch planner. Group the worklist into implementation groups; you edit nothing.
Worklist: ${prep.worklistPath} (also ${RUN}/worklist.md). Current statuses: ${RUN}/status_before.tsv.
For every card: read its printed text and its official Lua script, and grep the engine for existing primitives that already
do most of it (similar sentences in the templates, EffectActionKind members, runtime arms). Then build groups of 5–8 cards (fewer, larger groups: each implementer pays a fixed cost to learn the engine)
that share a SHAPE (same action family, trigger window, or engine subsystem) so one implementer builds shared atoms once.
Each group gets: id (PascalCase, unique, a valid C# identifier fragment, e.g. "SummonNegation"), title, cards (exact names),
and notes (what exists already, what is new, the risky rules points, branch_refs hints, similar implemented cards to copy).
Put a card in skipped (with a precise reason) only if it needs a whole missing subsystem (e.g. a UI flow the engine cannot
drive, revealing hands on screen, a mechanic from a later era), or it is marked DO NOT PORT in branch_refs.json.
Order groups easiest-first. Cover every worklist card exactly once (in a group or in skipped).${A.maxCards ? `
BATCH CAP: put at most ${A.maxCards} cards in groups (engine-group cards included). Take them in curriculum set order
(earliest set first), keeping whole shapes together; every other card goes in skipped with the reason
"over this batch's cap — next batch" (do not research those beyond their shape).` : ''}${A.extraGroups && A.extraGroups.length ? `
ENGINE-SUBSYSTEM GROUPS are built in this same batch (each adds a missing engine subsystem; its cards are the acceptance
tests). Put each of them in your output UNCHANGED (same id, title, cards and notes, first in the list). When a worklist
card needs one of these subsystems, ADD it to that group's cards (and append one line to its notes) instead of skipping
it or making another group:
${JSON.stringify(A.extraGroups.map(x => ({ id: x.id, title: x.title, cards: x.cards, subsystem: (x.notes || '').slice(0, 400) })))}` : ''}`,
    { label: 'plan groups', phase: 'Scout', schema: GROUP_SCHEMA })
  if (!g) return { stopped: 'grouping failed', prep }
  groups = g.groups
  skipped = g.skipped || []
  for (const x of (A.extraGroups || [])) {
    const got = groups.find(y => y.id === x.id)
    if (!got) groups.unshift(x)
    else got.notes = `${x.notes}${got.notes && got.notes !== x.notes && !x.notes.includes(got.notes) ? ` [planner] ${got.notes}` : ''}`
  }
}
log(`${groups.length} groups: ${groups.map(g => `${g.id} (${g.cards.length})`).join(', ')}${skipped.length ? `; ${skipped.length} skipped` : ''}`)
if (A.dryRun) {
  return { dryRun: true, runDir: RUN, prep, groups, skipped }
}

const trees = await agent(`${CONTEXT}
ROLE: tree maker. Run folder ${RUN}.
1. For each group id below, create its isolated engine copy FROM THE CURRENT WORKING TREE, all from the same baseline, before
   anything is integrated: bash ${TOOLS}/scratch_tree.sh ${RUN}/trees/<id>
   Group ids: ${groups.map(g => g.id).join(', ')}
   Check each copy builds: bash ${TOOLS}/headless.sh ${RUN}/trees/<first id> --card "Pot of Greed" (one is enough).
2. Save the current seed: cp ${REPO}/Assets/StreamingAssets/WRLDZ/compiled_effects_seed_v1.json ${RUN}/seed_before.json
3. Open the integration: bump the compiler Version in the REAL checkout above every number ever used (README "Versions";
   also grep the working tree's Version line history notes for the highest vNNN mentioned) — normally current+1 — with
   python3 ${TOOLS}/version_bump.py <N> "v<N>: card batch ${TAG} (integration in progress; the finalizer rewrites this note)".
   RESUME CHECK FIRST: if the Version line's leading note already says "card batch ${TAG} (integration in progress", an
   earlier tree maker of this run bumped it and then died. Do NOT bump again; reuse that number (python3 ${TOOLS}/version_bump.py --show).
   The copies keep the old number and have no seed; the real checkout's old seed is now ignored until the finalizer
   re-exports it. This version bump is the ONLY edit you make to ${REPO}.
Return the structured result.`, { label: 'make copies', phase: 'Scout', schema: TREES_SCHEMA, model: 'sonnet', effort: 'low' })
if (!trees) return { stopped: 'tree creation failed', prep, groups }
log(`copies ready; compiler v${trees.oldVersion || prep.compilerVersion} → v${trees.newVersion} (integration open)`)

// ── Implement → Review → Fix → Integrate (pipelined; integration serialized) ────────────────────────
let integrationChain = Promise.resolve()
let lastStatus = `${RUN}/status_before.tsv`
const integrated = []
// An integrator that dies (usage limit, crash) may leave the real checkout half-applied: stop integrating, and say which
// snapshot to restore. Its marker file <RUN>/snapshots/<id>.INTEGRATING lets the next run restore it first.
let integrationHalted = null

const implement = (g) => agent(`${CONTEXT}
ROLE: implementer for card group "${g.id}" — ${g.title}.
Cards: ${g.cards.join('; ')}
Group notes: ${g.notes || '(none)'}
Your isolated engine copy: T=${RUN}/trees/${g.id} — a git repo whose HEAD is the baseline. Work ONLY inside $T. The real
checkout ${REPO} is read-only for you (Lua scripts, git show of branch commits). The copy may already hold partial work
from an earlier implementer of this group that was interrupted (git -C $T status / git -C $T diff): review it critically,
keep what is right, fix or discard the rest (inside the copy, git checkout / clean are fine).${PRIOR[g.id] ? `
An earlier run's copy of this group also holds partial work: ${PRIOR[g.id]} (git -C ${PRIOR[g.id]} diff shows it against
that copy's own baseline, which is OLDER than yours). Port what is right into $T hunk by hunk; never copy whole files over.` : ''}
Card details: python3 ${TOOLS}/worklist.py ${RUN}/status_before.tsv --cards "${g.cards.join(';')}" --md
Steps:
1. For each card decide exactly what it does: printed text + official Lua script + rulings (activation condition, cost vs
   effect, targets and whether they target, face-up/face-down, whose field, timing and chain speed, duration, "cannot be
   negated", once per turn, what happens if the target is gone). Check how it compiles now:
   bash ${TOOLS}/headless.sh $T --card "<name>".
2. Find the closest existing primitives (grep templates and runtime for similar sentences and actions; --card on similar
   implemented cards). Reuse or generalize before adding new actions / zones / fields.
3. Implement with the playbook's file conventions: TextEffects/Batches/${g.id}Atoms.cs (+ one line in
   AtomRegistration.BatchAtomSets), TextEffects/Batches/${g.id}Runtime.cs for large runtime bodies, Rules/Batches/
   ${g.id}RegressionTests.cs (+ one call line in CardBatchRegressionTests.Run). New enum members at the END of their enum
   with a doc comment naming the batch; map new actions in EffectVocabulary.ResolutionOf. Make sure the AI can use or
   answer the card without a soft-lock (AiAutoRespond, AutoPick) and that any player choice goes through an existing
   PendingActivation / TargetKind flow (both target mirrors, see the playbook gotchas).
4. Tests per implemented card: a compile-shape check, a mini-duel through real engine entry points (TryActivateSpellTrap,
   MonsterEffects.OnFlipSummoned, summons, ReachOpponentBattle + TryAttack, response windows) that asserts the actual
   outcome, and a near-miss / fail-closed check where the sentence family has look-alikes.
5. Gate in $T, all must hold:
   - bash ${TOOLS}/headless.sh $T --quiet → result=PASS;
   - bash ${TOOLS}/headless.sh $T --status-dump > $T/../${g.id}.status.tsv &&
     python3 ${TOOLS}/status_diff.py ${RUN}/status_before.tsv $T/../${g.id}.status.tsv → no losses; any card outside this
     group whose status changed must be listed with why it is correct (otherwise tighten your regex);
   - python3 ${TOOLS}/enum_guard.py --repo $T → 0 errors; python3 ${TOOLS}/handler_guard.py --repo $T → no HOLLOW action and no
     UNREGISTERED file of this group (a hollow action = a card Implemented that does nothing).
6. A card that cannot be made right without a whole missing subsystem stays exactly as it was (fail closed), with a precise
   reason. Deferring beats a wrong card — but try: most cards need one modest action + runtime arm.
7. Patch and notes (do NOT commit): cd $T && git add -A && git diff --cached --binary > ${RUN}/patches/${g.id}.patch ;
   write ${RUN}/patches/${g.id}.md: each implemented card with 1–3 lines on how it works, deferred cards with reasons, new
   enum members, new clause fields, shared-code changes, tests added.
Return the structured result.`, { label: `implement ${g.id}`, phase: 'Implement', schema: IMPL_SCHEMA })

const LENSES_BOTH = {
  rules: `RULES ACCURACY. For each implemented card, work out the correct TCG behavior from the printed text (headless.sh --card), the
official Lua script and rulings, then read the implementation (atoms, runtime, engine hooks) and its tests. Look for:
wrong activation condition or timing window, cost treated as effect (or the reverse), targeting vs non-targeting, wrong
side or face-up/face-down filter, wrong duration or cleanup, missing "cannot be negated" / once-per-turn handling, wrong
chain speed (Counter Trap = 3, Quick-Play = 2), Damage Step legality, what happens when the target leaves, "for each"
flattened, a partial effect presented as FullyCompiled, and look-alike cards the regex wrongly captures or misses.`,
  engine: `ENGINE SAFETY. Look for: regressions to other cards (compare the status TSV the implementer wrote beside the copy
with ${RUN}/status_before.tsv using ${TOOLS}/status_diff.py; export the copy's seed (headless.sh <copy> --export-seed) and run seed_diff.py against ${RUN}/seed_before.json: every program
the new regexes changed shows up); changes to
shared paths (CollectTargets, DuelEngine summon/battle/chain flow, FieldSpellEffects refresh, ContinuousProtections) that
alter existing cards; card-id branches; enum or serialization hazards (run ${TOOLS}/enum_guard.py --repo <copy>; new
EffectClause fields must be public, JsonUtility-serializable and default-safe); null / empty-zone / token / face-down
crashes; AI paths that soft-lock or never use the card; tests that are vacuous (always pass) or bypass real entry points;
playbook convention violations (Batches files, one-line registrations, Version untouched).`,
}
// One combined reviewer per group by default: two lenses doubled the review cost (v142: 36 reviewers, about as many
// tokens as the implementers) for little extra. Pass args.lenses = 'both' for a high-risk batch.
const LENSES = A.lenses === 'both' ? LENSES_BOTH : {
  combined: `RULES ACCURACY and ENGINE SAFETY, in one pass. Rules: work out each implemented card's correct TCG
behavior from its printed text and official Lua. Check activation condition / timing window, cost vs effect, targeting vs
non-targeting, face-up vs face-down, duration and cleanup, chain speed, what happens when the target leaves, "for each"
flattened, and a partial effect presented as FullyCompiled. Engine: no status losses outside the group (status_diff of the
implementer's TSV vs ${RUN}/status_before.tsv), no shared-path change that alters other cards, no card-id branches, enum
appends only at the end, no AI soft-lock, and no vacuous tests. Write at most 2 probe tests; report only real defects.`,
}

const review = (impl, g) => parallel(Object.entries(LENSES).map(([lens, brief]) => () => agent(`${CONTEXT}
ROLE: ${lens} reviewer for card group "${g.id}". Be skeptical: assume something is wrong until the code and a scenario show
otherwise — but report only REAL defects, each with a concrete failing scenario (board state + action → wrong result) and
file:line.
Implementer's copy: ${RUN}/trees/${g.id} — do NOT edit it and do NOT build or run inside it (its owner may still be using
it). To run anything, first copy it: rsync -a --exclude bin/ --exclude obj/ ${RUN}/trees/${g.id}/ ${RUN}/trees/${g.id}.rv-${lens}/
and run ${TOOLS}/headless.sh on your copy (a failing test you write there is the best evidence).
Patch: ${impl.patchPath}. Notes: ${impl.notesPath}.
Implemented cards: ${impl.implemented.map(c => c.name).join('; ')}. Deferred: ${(impl.deferred || []).map(c => c.name).join('; ') || 'none'}.
Also judge each DEFERRED card's reason: if it is actually easy with existing primitives, say so as a medium finding.
Reviewer budget (reviews were the costliest role in v161, ~150k tokens each): about 25 tool calls. Start from the notes
and the patch (grep -n its hunks; do not read it whole), read engine code only around the hooks the patch touches, and
build/run your copy once, only for a probe test you wrote.
Lens — ${brief}
Severity: high = plays wrong in a normal situation, crash, soft-lock, or breaks another card; medium = rules edge case
wrong, or an untested path that is likely wrong; low = cosmetic / docs.`,
  { label: `review ${g.id} (${lens})`, phase: 'Review', schema: REVIEW_SCHEMA, effort: 'medium' })))

const fix = (impl, reviews, g) => agent(`${CONTEXT}
ROLE: fixer for card group "${g.id}". The copy ${RUN}/trees/${g.id} is yours again (the reviewers worked on their own copies).
It may also hold partial edits from an earlier fixer that died (compare git -C <copy> diff with ${RUN}/patches/${g.id}.patch):
review them critically, keep what is right, discard the rest.
Your earlier result: ${JSON.stringify({ implemented: impl.implemented, deferred: impl.deferred })}
Review findings (two independent reviewers): ${JSON.stringify(reviews.flatMap(r => (r.findings || []).map(f => ({ lens: r.lens, ...f }))))}
For each finding: verify it, ideally with a test that fails before your fix. Real → fix it and keep the test. Wrong → keep
the code and give evidence (a passing test or the Lua/ruling). If a card cannot be made right, revert that card's parts so
it fails closed exactly as before, and move it to deferred. Deferred cards a reviewer showed are easy: implement them.
Then re-run the whole gate (suite PASS, status diff with no losses, enum guard) and regenerate the patch and notes at the
same paths (${RUN}/patches/${g.id}.patch / .md, via git add -A && git diff --cached --binary).
Return the structured result, with one resolution per finding.`, { label: `fix ${g.id}`, phase: 'Fix', schema: FIX_SCHEMA })

// ENGINE groups (title starts with "ENGINE") change shared semantics. With A.engineLast they integrate after the card
// groups, and their integrator runs on the session model at high effort: it must adapt the card groups' fresh code to
// the new subsystem, not just append.
const isEngine = g => /^ENGINE\b/.test((g && g.title) || '')
const integrate = (res, g) => {
  // Serialized: each integration starts after the previous one settled, whatever its outcome.
  integrationChain = integrationChain.catch(() => null).then(async () => {
    if (integrationHalted) {
      const halted = { group: g.id, applied: false, integratedCards: [], suitePass: false, rolledBack: false,
        notes: `not attempted: integration halted after ${integrationHalted} died` }
      integrated.push(halted)
      return halted
    }
    const prevStatus = lastStatus
    const r = await agent(`${CONTEXT}
ROLE: integrator for card group "${g.id}" into the REAL checkout ${REPO}. Groups are integrated one at a time: you are the
only agent editing ${REPO} now. This is the one role allowed to edit ${REPO} (only the files in the patch).
Patch: ${res.patchPath} (made against the batch baseline; earlier groups may already be in, so expect small conflicts).
Notes: ${res.notesPath}. Cards it implements: ${res.implemented.map(c => c.name).join('; ')}.${isEngine(g) ? `
ENGINE GROUP: this patch changes shared engine semantics, and the groups integrated before you were built on the OLD
engine. After applying, find their new code that the subsystem now covers (grep their runtimes for the paths the notes
say changed) and adapt it so it uses the new subsystem, keeping their tests' intent; if a test encoded the old behaviour
and the new one is the correct rule, update the test and say why. List every adaptation in the notes file.` : ''}
0. Interrupted earlier integration? If any ${RUN}/snapshots/*.INTEGRATING marker exists, an integrator died mid-apply:
   restore that snapshot first (bash ${TOOLS}/snapshot.sh restore ${RUN}/snapshots/<that id>), delete its marker, and
   confirm headless.sh ${REPO} --quiet is green before you start.
0b. Other sessions may have landed changes in ${REPO} after the copies were made. If ${RUN}/PEER_LANDINGS.md exists,
   read it first: merge on top of those changes and keep every line they added (registrations, enum members, fields);
   their intended status changes (the --allow ids listed there) are not losses of this group.
1. bash ${TOOLS}/snapshot.sh save ${RUN}/snapshots/${g.id} ${res.patchPath} && touch ${RUN}/snapshots/${g.id}.INTEGRATING
2. cd ${REPO} && git apply --reject --whitespace=nowarn ${res.patchPath} ; python3 ${TOOLS}/tail_merge.py ${REPO} --partial
   tail_merge.py merges the tail-append conflicts itself (an enum, the Atoms / BatchAtomSets arrays,
   CardBatchRegressionTests.Run, the ApplyClause switch, an EffectVocabulary list: both kept, earlier group first; it
   prints one MERGED / OPEN line per hunk). Resolve only the OPEN hunks left in *.rej by hand (real edits of the same
   lines: keep both intents), then delete the .rej files. Never drop or rewrite another group's code. The patch must not
   touch CardTextEffectCompiler.Version; if it does, discard that hunk.
3. Verify in ${REPO}: bash ${TOOLS}/headless.sh ${REPO} --quiet → PASS; bash ${TOOLS}/csc_overlay.sh ${REPO} → OK and
   bash ${TOOLS}/csc_overlay.sh ${REPO} --release → OK (if either fails ONLY in files this patch does not touch, another session is
   mid-edit: note it, do not fix their files); python3 ${TOOLS}/enum_guard.py → 0 errors; python3 ${TOOLS}/handler_guard.py
   --repo ${REPO} → nothing new for this group (no HOLLOW action, its Atoms in BatchAtomSets, its tests in CardBatchRegressionTests.Run);
   bash ${TOOLS}/headless.sh ${REPO} --status-dump > ${RUN}/status_after_${g.id}.tsv &&
   python3 ${TOOLS}/status_diff.py ${prevStatus} ${RUN}/status_after_${g.id}.tsv → no losses, and this group's cards Implemented
   (pass --allow with the ids PEER_LANDINGS.md lists for landings made since ${prevStatus} was written).
4. A small fix inside this group's own code is fine. If it cannot be made green without rewriting other groups' code:
   bash ${TOOLS}/snapshot.sh restore ${RUN}/snapshots/${g.id}, re-run the suite to confirm it is green again, and report
   applied=false with the reason. Dropping one card (reverting just its parts) is better than dropping the group.
5. Last step either way (applied or restored): rm -f ${RUN}/snapshots/${g.id}.INTEGRATING
Return the structured result (statusPath = the status file you wrote when applied).`,
      { label: `integrate ${g.id}`, phase: 'Integrate', schema: INTEGRATE_SCHEMA,
        ...(isEngine(g) ? { effort: 'high' } : { model: 'sonnet', effort: 'medium' }) })
    if (r && r.applied && r.statusPath) lastStatus = r.statusPath
    if (!r) {
      integrationHalted = g.id
      log(`INTEGRATOR FOR ${g.id} DIED — halting integration. Restore with: bash ${TOOLS}/snapshot.sh restore ${RUN}/snapshots/${g.id} (marker ${RUN}/snapshots/${g.id}.INTEGRATING)`)
      const dead = { group: g.id, applied: false, integratedCards: [], suitePass: false, rolledBack: false,
        notes: `integrator died; the real checkout may be half-applied — restore ${RUN}/snapshots/${g.id}` }
      integrated.push(dead)
      return dead
    }
    integrated.push(r)
    return r
  })
  return integrationChain
}

if (HELD.size) log(`held for a later run: ${groups.filter(g => HELD.has(g.id)).map(g => g.id).join(', ')}`)
// The stages run as barriers, and each stage calls its agents in group order. A pipeline would be faster, but a resume
// replays a finished agent only when it makes the same agent() calls in the same order: the cache key follows the call
// sequence. In a pipeline, a cached stage that returns instantly moves later calls earlier, so every agent after it
// re-runs (the v141 run lost ~10 finished reviews that way). Usage-limit stops are routine for batches this big.
const impls = await parallel(groups.map(g => () =>
  HELD.has(g.id) ? Promise.resolve({ held: true }) : PRE[g.id] ? Promise.resolve(PRE[g.id]) : implement(g)))
const results = groups.map((g, i) => {
  const impl = impls[i]
  if (impl && impl.held) return { group: g.id, failed: 'held for a later run (holdGroups)' }
  if (!impl) return { group: g.id, failed: 'implementer died' }
  if (!impl.implemented || impl.implemented.length === 0) return { group: g.id, impl, reviews: [], final: impl }
  return { group: g.id, impl }
})
const byGroup = groups.map((g, i) => ({ g, st: results[i] }))

// Review every implemented group; a reviewer that died must not wave the patch through half-reviewed.
const toReview = byGroup.filter(({ st }) => st.impl && !st.final)
const reviewed = await parallel(toReview.map(({ g, st }) => () => review(st.impl, g)))
toReview.forEach(({ st }, k) => {
  st.reviews = (reviewed[k] || []).filter(Boolean)
  if (st.reviews.length < Object.keys(LENSES).length)
    st.hold = 'a reviewer died (usage limit?) — resume the run to finish the review'
})

// Fix only when a reviewer found something above cosmetic (including "this deferred card is easy").
const toFix = toReview.filter(({ st }) => !st.hold && st.reviews.flatMap(r => r.findings || []).some(f => f.severity !== 'low'))
toReview.filter(x => !x.st.hold && !toFix.includes(x)).forEach(({ st }) => { st.final = st.impl })
const fixedAll = await parallel(toFix.map(({ g, st }) => () => fix(st.impl, st.reviews, g)))
toFix.forEach(({ st }, k) => {
  // Never integrate the unfixed patch when the fixer died: its serious findings are still open.
  if (!fixedAll[k]) { st.final = st.impl; st.hold = 'the fixer died (usage limit?) — resume the run to fix, then integrate' }
  else { st.final = fixedAll[k]; st.fixed = true }
})

// Integrate one group at a time, in group order (with A.engineLast: card groups first, then ENGINE groups).
const integrationOrder = A.engineLast ? [...byGroup.filter(x => !isEngine(x.g)), ...byGroup.filter(x => isEngine(x.g))] : byGroup
for (const { g, st } of integrationOrder) {
  const fin = st.final
  if (st.hold) {
    log(`${g.id}: not integrating — ${st.hold}`)
    st.integration = { applied: false, notes: st.hold }
    continue
  }
  if (A.integrate === false || !fin || !fin.implemented || !fin.implemented.length) continue
  if (!fin.suitePass || !fin.enumGuardOk || !fin.statusDiffOk) {
    log(`${g.id}: gate not green after fixes — not integrating`)
    st.integration = { applied: false, notes: 'gate not green in the copy' }
    continue
  }
  st.integration = await integrate(fin, g)
}
await integrationChain.catch(() => null)

const landed = integrated.filter(r => r && r.applied)
log(`integrated ${landed.length}/${groups.length} groups, ${landed.flatMap(r => r.integratedCards).length} cards`)

// Agents that died (usage limit) paused some groups: do not audit or finalize a half-finished batch. The finalizer
// would write the Version note (or, with nothing landed, roll the Version back) under work that a resume still adds.
const paused = results.filter(Boolean).filter(st => st.hold || st.failed === 'implementer died')
if (paused.length || integrationHalted) {
  log(`PAUSED: ${paused.map(st => `${st.group} (${st.hold || st.failed})`).join('; ')}${integrationHalted ? `; integration halted at ${integrationHalted}` : ''}. Resume the run with the same args once agents can run again.`)
  return {
    paused: true,
    runDir: RUN,
    integrationHalted,
    landed: landed.map(r => ({ group: r.group, cards: r.integratedCards })),
    pausedGroups: paused.map(st => ({ group: st.group, why: st.hold || st.failed })),
  }
}

// ── Audit ──────────────────────────────────────────────────────────────────────────────────────────
let audit = null
if (landed.length) {
  phase('Audit')
  audit = await agent(`${CONTEXT}
ROLE: cross-group auditor of the integrated batch in the REAL checkout ${REPO} (integration is finished; you are the only
agent editing ${REPO}). Integrated: ${JSON.stringify(landed.map(r => ({ group: r.group, cards: r.integratedCards })))}.
Patches: ${RUN}/patches/*.patch; per-group notes: ${RUN}/patches/*.md.
1. Look for cross-group interference: two groups changing the same shared path in different ways, duplicate primitives
   that should be one, a later group's regex or runtime change altering an earlier group's card, test-helper misuse.
2. Stress: bash ${TOOLS}/headless.sh ${REPO} --quiet --duels ${STRESS} → PASS with no soft-locks or exceptions.
3. python3 ${TOOLS}/status_diff.py ${RUN}/status_before.tsv <a fresh --status-dump> → no losses, except the intended
   status changes of peer landings listed in ${RUN}/PEER_LANDINGS.md (if it exists): pass them with --allow and never
   revert a peer's change.
Fix real high/medium issues directly in ${REPO} (smallest change, suite green after), or revert the offending card so it
fails closed. Report every issue (fixed or not) and the stress numbers.`,
    { label: 'audit batch', phase: 'Audit', schema: AUDIT_SCHEMA })
}

// ── Finalize ───────────────────────────────────────────────────────────────────────────────────────
phase('Finalize')
const deferredAll = results.filter(Boolean).flatMap(st => ((st.final && st.final.deferred) || []).map(d => ({ group: st.group, ...d })))
const final = await agent(`${CONTEXT}
ROLE: finalizer of card batch "${TAG}" in the REAL checkout ${REPO} (you are the only agent editing it now).
Integrated groups: ${JSON.stringify(landed.map(r => ({ group: r.group, cards: r.integratedCards, dropped: r.droppedCards })))}
Not integrated: ${JSON.stringify(results.filter(Boolean).filter(st => !(st.integration && st.integration.applied)).map(st => ({ group: st.group, why: (st.integration && st.integration.notes) || st.failed || 'nothing implemented' })))}
Deferred cards: ${JSON.stringify(deferredAll)}
Skipped by the planner: ${JSON.stringify(skipped)}
Audit: ${JSON.stringify(audit)}
Compiler: opened at v${trees.newVersion} (was v${trees.oldVersion || prep.compilerVersion}).
1. Rewrite the Version line's leading note (keep the older notes after it) so it lists what v${trees.newVersion} added, e.g.
   "/* v${trees.newVersion}: card batch ${TAG} — <group>: <cards>; … */". ${landed.length ? '' : 'Nothing landed: set the Version back to the old number instead (python3 edit), since compiler output did not change, and restore the seed from ' + RUN + '/seed_before.json.'}
2. ${landed.length ? `bash ${TOOLS}/headless.sh ${REPO} --export-seed ; python3 ${TOOLS}/seed_diff.py ${RUN}/seed_before.json ${REPO}/Assets/StreamingAssets/WRLDZ/compiled_effects_seed_v1.json > ${RUN}/seed_diff.txt` : 'skip the seed export'}
3. Final gate: bash ${TOOLS}/headless.sh ${REPO} --quiet → PASS; bash ${TOOLS}/csc_overlay.sh ${REPO} and --release → OK;
   python3 ${TOOLS}/enum_guard.py → 0 errors; python3 ${TOOLS}/handler_guard.py --repo ${REPO} → nothing from this batch;
   bash ${TOOLS}/headless.sh ${REPO} --status-dump > ${RUN}/status_after.tsv ;
   python3 ${TOOLS}/status_diff.py ${RUN}/status_before.tsv ${RUN}/status_after.tsv (put its table in statusTable; peer
   landings listed in ${RUN}/PEER_LANDINGS.md, if it exists, go in --allow and in the report as "landed by other sessions").
4. Docs:
   - Docs/CURRICULUM_BATCH_${TAG}.md: what landed (card, set, how it works in one line), what was deferred or skipped and
     exactly why (so the next batch can pick it up), the status table, the compiler version, how to verify.
   - Assets/Scripts/WRLDZ/Duel/TextEffects/TEXT_EFFECT_PIPELINE.md: add this pass (find the existing pass / batch table or
     list and follow its format; if none, add a "Passes" section at the end).
   - ${TOOLS}/branch_refs.json: remove entries whose card is now Implemented.
   - ${TOOLS}/ENGINE_MAP.md (the map the next batch reads first; keep it short and exact): in section 2 add a recipe or
     one line per NEW shared subsystem or hook this batch added (its runtime class and entry points, as in the existing
     entries); in section 4 delete gaps this batch closed, add the gaps that blocked this batch's deferred / Stub cards
     (what is missing + the cards), and add audit issues left unfixed to "Open residuals".
   - AGENTS.md, the "Engine" section: the CardTextEffectCompiler.Version sentence → the new number and today's batch doc;
     make sure it mentions the playbook Tools/CardCompile/README.md and the workflow .claude/workflows/compile-card-batch.js.
     Its "last card batch" and "Last batch:" pointers → this batch doc, with "previous"/"before it" → the prior batch doc.
   Touch nothing else. Do not commit.
Return the structured result.`, { label: 'finalize', phase: 'Finalize', schema: FINAL_SCHEMA, model: 'sonnet', effort: 'medium' })

return {
  runDir: RUN,
  integrationHalted,
  version: final && final.version,
  final,
  audit,
  groups: results.filter(Boolean).map(st => ({
    group: st.group,
    implemented: ((st.final && st.final.implemented) || []).map(c => c.name),
    deferred: ((st.final && st.final.deferred) || []).map(c => `${c.name}: ${c.reason}`),
    reviewFindings: (st.reviews || []).flatMap(r => (r.findings || []).map(f => `${r.lens}/${f.severity}: ${f.title}`)),
    integration: st.integration ? { applied: st.integration.applied, cards: st.integration.integratedCards, notes: st.integration.notes } : null,
  })),
  skipped,
}
