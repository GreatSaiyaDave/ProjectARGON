# Card compilation playbook (Project ARGON engine)

How to compile the next Yu-Gi-Oh! cards into the rules engine: fast, in parallel, without breaking
the cards that already work. Two ways to run it:

- **Automated.** In Claude Code, run the saved workflow `.claude/workflows/compile-card-batch.js`.
  Say *"compile the next batch of cards"*. Claude calls
  `Workflow({name: "compile-card-batch", args: {...}})`, or, if the name is not registered in that session,
  `Workflow({scriptPath: "<repo>/.claude/workflows/compile-card-batch.js", args: {...}})`. The arguments are
  listed at the top of the script, for example `{tag: "v142-lon", sets: ["LON"]}`, or `{cards: ["Jinzo"]}`,
  or explicit `groups`. The first run (v141) had about 80 cards in 18 groups, took hours of agent time, and
  wrote its report to `Docs/CURRICULUM_BATCH_v141-mrd-srl-psv.md`. It scouts the worklist and groups cards by shape. Each group
  is implemented in its own isolated engine copy, reviewed by rules and engine reviewers, and fixed. Groups are
  then integrated into the real checkout one at a time, with the suite green after each. Last, it bumps the
  compiler version, re-exports the seed and writes the batch report.
  If a run stops on a usage limit, resume it after the limit resets with
  `Workflow({scriptPath: …, resumeFromRunId: "<run id>", args: <the same args>})`. Finished agents replay from
  cache. An integrator that died mid-apply leaves `<runDir>/snapshots/<group>.INTEGRATING`; the next integrator
  restores that snapshot first.
  **Land finished groups first.** Workflows run at most `CPUs - 2` agents at once (6 on the owner's 8-core box).
  On a plain resume, the unstarted implementers take every slot, and the finished groups wait hours for review.
  If the limit hits again, nothing lands. So resume with `holdGroups: [<every group whose implementer never
  finished>]` added to the same args. Finished implementers still replay from cache, and their groups are
  reviewed, integrated and finalized. Then start a NEW run (new tag, Version +1) for the held groups. Pass
  `priorTrees: {<group>: "<old runDir>/trees/<group>"}` for any held group whose old copy holds partial work
  (`git -C <tree> status`). A usage-limit stop costs about 750k tokens per implementer; the v141 run hit the
  limit twice with 6 implementers running.
  **The resume cache follows the call order.** A finished agent replays only if the resumed run makes the same
  `agent()` calls in the same order; its key changes when earlier calls change. That is why the script runs
  Implement / Review / Fix as barriers in group order and integrates in group order, not as a pipeline. Keep it
  that way. Never add or remove an `agent()` call before the stages a resume should replay. A group whose
  reviewer or fixer died is held, never integrated unfixed. A run with held groups returns `{paused: true}`
  before Audit/Finalize.
  **Peer sessions landing while a run is open:** record each landing (files, lines to keep, intended --allow ids)
  in `<runDir>/PEER_LANDINGS.md`. Integrators, the auditor and the finalizer read it, so they merge on top of peer
  changes and do not revert their intended fail-closes. Ask peers to land only between batches or before
  integration starts; integrators snapshot and restore shared files. A finished call that comes AFTER a failed call in the sequence does not replay (seen
  2026-09-29: RevealHands re-ran). So a resume reuses the finished prefix plus finished calls that sit before the
  first failure in their own stage.
- **By hand.** Follow the same loop with the tools below.

Everything here runs **headless**: no Unity Editor, no `unity cmd`. The owner's Editor is shared by other
sessions.

## Tools

| Tool | What it does |
|------|--------------|
| `headless.sh <tree> [args]` | Builds and runs `Tools/HeadlessEngine` in `<tree>` with a private, wiped compiled-program cache. `<tree>` is `.` for the real checkout. Args: `--quiet` (full suite: ~2,400 unit checks plus 40 AI duels, ~15 s), `--card "<name or id>"`, `--prelink-card "<name or id>"` (one monster from `eras/prelink_monsters.json`), `--status-dump`, `--coverage`, `--gaps`, `--export-seed`, `--prelink` (compile `eras/prelink_monsters.json` and write `Docs/PRELINK_MONSTER_COMPILE.md`). Falls back to the .NET 8 SDK bundled with Unity when there is no system `dotnet`. |
| `prelink_pool.py` | Writes `Assets/StreamingAssets/WRLDZ/eras/prelink_monsters.json`: every TCG/OCG monster with a YGOPRODeck `tcg_date` or `ocg_date` on or before 2017-05-05. Link, Token, Skill, and Strategy cards are left out. Passcodes are checked against `cards.cdb`. The raw API dump stays in `Tools/CardCompile/cache/` (gitignored). |
| `scratch_tree.sh <dest> [ref]` | Makes an isolated engine copy (~30 MB, 0.2 s) from the working tree or a git ref. The seed is left out, so every card compiles fresh. The copy is its own git repo with a baseline commit, so `git -C <dest> diff` *is* the batch patch. |
| `worklist.py status.tsv [--sets LON,LOD] [--cards "A;B"] [--md\|--json]` | Lists the Unimplemented and Stub cards in curriculum set order, with printed text, the reference Lua script, and branch hints. |
| `status_diff.py before.tsv after.tsv [--allow ids]` | Shows every card whose status changed. Exits 1 if a working card got worse. |
| `enum_guard.py [--repo <tree>] [--base-ref <ref>]` | Append-only check on the engine enums (see "Enums"). Exits 1 on a mid-enum insert, removal or renumber in `Duel/TextEffects`. |
| `headless.sh <tree> --quiet --seed N` | Reproducible runs: every unseeded test engine draws its seed from a sequence based on N (default 20261018), and an explicit `--seed` also moves the 250 stress duels. To find luck-dependent checks, sweep several seeds, one tree copy per parallel run. In October 2026, Timeater's check failed at about 1 seed in 11. |
| `handler_guard.py [--repo <tree>]` | HOLLOW actions (emitted by an atom, read by no runtime) and batch Atoms / RegressionTests files nobody registered. Exits 1 on either. |
| `seed_diff.py old.json new.json` | Semantic seed diff: programs added, removed and actually changed, ignoring timestamps and new-field defaults. |
| `version_bump.py <N> "<note>"` / `--show` | Bumps `CardTextEffectCompiler.Version` and prepends its history note. |
| `tail_merge.py <repo> [--check] [--partial] [file.rej …]` | After `git apply --reject`: merges the rejected hunks that are pure tail appends (two groups adding at one enum / array / switch / registration tail) the way an integrator would, both kept, earlier group first. Context compares ignore trailing commas; anchors must be unique or nearest the hunk. Hunks that edit existing lines stay in the .rej. Replaying v161's nine patches on v160 it merged 54 of 60 rejected hunks, the same placements the integrators chose by hand (order aside). Does not compile: re-run the gate. |
| `snapshot.sh save <dir> <patch>` / `restore <dir>` | Integration rollback for exactly the files one patch touches, plus the Version file and the seed. |
| `branch_refs.json` | Cards some unmerged GitHub branch already compiled, with the commits to read (and the ones **not** to port). |
| `FX_MAP.md` | Duel arena FX coverage: the `SummonKind` → hologram seam, which effect families have a look, and the per-group FX checklist. A group ships its FX with its cards. |

## The loop (one batch)

**Where it happens (2026-10-18).** The workflow's default `integrateIn: 'tree'` makes every copy from HEAD, and runs
integration, audit and finalize in `<runDir>/trees/_integration`. A lander then applies one combined patch to the real
checkout (rebased first if main moved) and merges around peers' uncommitted edits; the hub commits it with
`git apply --cached`. The shared checkout usually holds other sessions' work in progress, and in October 2026 that
included random-choice AI edits that made the suite flaky, so it is no place to gate a batch.

1. **Baseline.** Run `headless.sh . --quiet`; it must PASS. Then save
   `headless.sh . --status-dump > before.tsv` and `version_bump.py --show`.
2. **Worklist.** Run `worklist.py before.tsv --sets <next sets> --md`. Curriculum order is LOB → MRD → SRL →
   PSV → LON → LOD → PGD → MFC → DCR → IOC → AST → SOD → RDS → FET → TLM
   (`Assets/StreamingAssets/WRLDZ/eras/pre_link_sets.json`). Past TLM it continues by era through the core boosters
   (GX: CRV → EEN → SOI → EOJ → POTD → CDIP → STON → FOTB → TAEV → GLAS → PTDN → LODT, then 5D's from TDGS …).
   `card_era_index.json` (from `card_era_index.py`, built from the local YGOPRODeck dump) gives those cards a set, so
   `worklist.py before.tsv --sets CRV,EEN` works the same way.
3. **Group by shape** into 3–6 cards: same action family, same trigger window, same engine subsystem.
   Shared atoms light up whole families.
3a. **Check the arena FX.** For each group, read the family table in `FX_MAP.md`. Summon procedures are
   already wired (`SummonKind` → `ArArenaCombatFx.PlaySummonProcedure`), but an effect family — mill, excavate,
   tribute, banish, chain, LP change, Standby growth — may still have no look. Fill the row in the same change
   or note the gap in the batch report. A card that resolves with no visual is not finished.
4. **Implement each group in its own copy.** Create it with `T=$(scratch_tree.sh <dir>/trees/<Group>)`.
   - Read each card's printed text with `headless.sh <tree> --card "<name>"` (never cards_db.json) and its official script,
     `Assets/StreamingAssets/OcgCore/scripts/official~/c<id>.lua` (read-only, in the real checkout).
   - Read similar implemented cards: `headless.sh "$T" --card "<similar card>"`, and grep the templates.
   - Put new sentence atoms in `Duel/TextEffects/Batches/<Group>Atoms.cs`, as
     `public static readonly AtomRegistration.Atom[] Atoms` with its own `Parse` helper. Register them with ONE
     line in `AtomRegistration.BatchAtomSets`. Extend a shared template file only when the change is a real
     generalization (for example, "A"/"An" in `LegacyTextTemplates.RxEquipSplit`).
   - Append new `EffectActionKind` / `EffectZoneFilter` / `EffectTiming` members at the END of their enum,
     each with a doc comment. Map new actions in `EffectVocabulary.ResolutionOf`.
   - Add the runtime in a `TextEffectRuntime.ApplyClause` case arm. Put a large body in
     `Duel/TextEffects/Batches/<Group>Runtime.cs` and call it from the arm.
   - Put tests in `Duel/Rules/Batches/<Group>RegressionTests.cs` with
     `public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string,bool,string> check)`.
     Register them with ONE line in `Rules/CardBatchRegressionTests.Run`. Every implemented card needs a
     compile-shape check (FullyCompiled, and the clause fields that matter) and a mini-duel that exercises the
     effect, using `InteractionRegressionTests.Fresh / ClearBoard / PlaceMonster / PlaceSetTrap / PutInHand /
     DrainCombat / ReachOpponentMain / ReachOpponentBattle`. A fail-closed near miss gets a check too.
   - **Ship the arena FX with the cards.** A group that introduces a new effect family, or that uses a Summon
     procedure for a new reason, must also fill the matching row of `FX_MAP.md` (or record in the report why
     the generic look is acceptable). See "The loop" step 3a.
   - Do **not** touch `CardTextEffectCompiler.Version` in the copy: the copy has no seed.
   - Gate:
     - `headless.sh "$T" --quiet` → PASS;
     - `headless.sh "$T" --status-dump > after.tsv && status_diff.py before.tsv after.tsv` → no losses, and only
       intended cards changed;
     - `enum_guard.py --repo "$T"` → 0 errors.
   - Patch: `git -C "$T" add -A && git -C "$T" diff --cached --binary > <Group>.patch`.
5. **Review** every group twice, independently:
   - rules accuracy against the printed text, the Lua script and the TCG rulings;
   - engine safety: regressions, over-matching regexes, shared-path changes, AI, serialization.

   Fix what is confirmed. A card that cannot be made right is removed from the patch and stays unimplemented.
6. **Integrate one group at a time** into the real checkout. Before the first group, bump the Version above
   every number ever used (see "Versions"), so the old seed is ignored while integrating. For each group:
   `snapshot.sh save`, apply the patch (`git apply --reject` or `patch -p1 --merge`), then resolve conflicts.
   They are almost always two appends at the same enum or array tail, or two one-line registrations: keep
   both. Then:
   - run `headless.sh . --quiet`, `csc_overlay.sh .` and `csc_overlay.sh . --release` (whole Unity project,
     editor and Android defines), `enum_guard.py` and `status_diff.py`;
   - if the group cannot be made green, `snapshot.sh restore` it and report why.
7. **Finalize.**
   - Run `headless.sh . --export-seed` and `seed_diff.py`, a final `--quiet`, and `csc_overlay.sh .` plus `--release`.
   - Write the batch report `Docs/CURRICULUM_BATCH_<tag>.md` (implemented, deferred with reasons, status
     table) and add a pass row to `Duel/TextEffects/TEXT_EFFECT_PIPELINE.md`.
   - Remove finished cards from `branch_refs.json`, and update the Engine version line in `AGENTS.md`.

## Checking a copy or a commit against the whole project

`Tools/CardCompile/csc_overlay.sh <tree> [--release]` compiles the whole Unity project (the old untracked `tools/csc_full.sh` is gone since the 10-10 restore; pass `.` for the real checkout), with
`Assets/Scripts/WRLDZ` taken from `<tree>`. Use it before landing a patch built in a scratch copy, or to prove a commit
stands on its own: `scratch_tree.sh <dir> HEAD`, then `headless.sh <dir> --quiet` and `csc_overlay.sh <dir>` (+ `--release`).
Commit only the card-engine scope (Duel/, the seed, Tools/CardCompile, the workflow, batch docs). Other sessions own
UI / Presentation / avatar files, so leave theirs uncommitted.

## Token economy (2026-10-04)

The owner's usage limits stopped batches seven times between 09-28 and 09-30. Measured on the v142 run (59 agents):
implementers used 2.86M output tokens over ~4,600 turns (~270 turns each); reviewers 2.26M over 3,300 turns, because there
were two per group; fixers 1.10M; everything else under 0.4M. The workflow now:
- runs ONE combined reviewer per group (`args.lenses = 'both'` restores two), at effort medium;
- runs prepare, make copies, integrators and the finalizer on the smaller model at low/medium effort;
- asks the planner for groups of 5–8 cards, because each implementer pays a fixed cost to learn the engine;
- gives every agent a TOKEN BUDGET: never read a whole engine file (grep -n + sed ranges), batch commands, tail long
  output, iterate with --card, and run the full suite only to gate.
On a resume where some implementers finished and others died, pass `prefilled: {<group>: <its result from
journal.jsonl>}`. Otherwise every finished implementer that comes after the first failed call is re-run, because the
resume cache only replays the unbroken prefix of calls.
For landed-but-unreviewed work (e.g. other bots editing the checkout), use one lean "harden" agent per unit
(review + tests + fix or fail closed in one pass) instead of a full batch.

## Rules that keep the engine honest

- **Fail closed.** A card is Implemented only when *every* sentence of its text compiled to a template that
  does exactly what it says (`CardEffectStatus.Classify`: FullyCompiled and CanResolveAny). Never compile
  a partial effect, invent a rider, or turn "for each" into a flat number. A card we cannot do right stays
  Unimplemented, which the deck gate keeps out of decks.
- **No card-id branches** (`if (cardId == …)`) except named unique exceptions
  (`EffectVocabulary.IsUniqueException`). Write the template for the *sentence* so every card with that
  sentence works.
- **Whole-sentence atoms only.** A regex that matches part of a longer card's text turns that card into a
  Stub. After adding an atom, the status diff must show no other card changed that shouldn't. Anchor it
  (`^…$`, or the `Start`/`End` helpers): matching is case-insensitive, so an unanchored `Draw (\d+) cards\.?$`
  added a bare Activate draw to every text ending "…draw 3 cards." (3-Hump Lacooda, 2026-10-18).
- **Every emitted action has a reader.** An action that only an atom writes and only `EffectVocabulary` names
  makes the card Implemented and inert. `python3 Tools/CardCompile/handler_guard.py` lists such HOLLOW actions
  and the batch Atoms / RegressionTests files nobody registered (a half-landed batch). Both must be empty.
- **The seed must equal a fresh compile.** `--export-seed` recompiles every card (v207), and
  `SeedFreshnessRegressionTests` fails when a same-Version seed differs from the compiler. Any change to
  compiler output therefore needs a Version bump and a re-export.
- **Git.** No git stash, reset, checkout, clean or commit in the real checkout. Other sessions keep uncommitted
  work in it, and the owner decides what gets committed or pushed.

## Enums (append only)

The seed and the on-device cache store `EffectActionKind`, `EffectTiming`, `EffectZoneFilter`, `EffectSide`
and the rest as **ints**. A member inserted mid-enum shifts every later value in every stored program. The v120
A Feint Plan insert did this at index 82 and was fixed in v139. New members go at the end. `enum_guard.py`
enforces it.

Keep a trailing comma after the LAST member of every shared enum (`EffectActionKind`, `EffectTiming`, `EffectZoneFilter`,
`EffectSide`, `EffectTargetKind`, `ResponseTiming`, `EffectResolutionKind`, `EffectCostKind`; added 2026-10-04). Then an
append is a pure insertion that `tail_merge.py` merges; without it every group edits the same last line and conflicts.

## Versions

The cache key is TextHash + CompilerVersion, so any change to compiler output needs a bump and a seed
re-export. Numbers ever used, as of 2026-10-07:

- v21–v50: early local and cloud lines;
- v51–v55: GitHub main;
- v57–v78: Sept-10 cursor branches;
- v100–v106: local line and merges;
- v107–v137: Grok bots;
- v138–v140: recovery line;
- v141–v142: workflow card batches (v141-mrd-srl-psv, v142-mrd-srl-psv-b);
- v143: LpCostOnlyActivation fail-close;
- v144: PartialCompiles fail-close (Assault on GHQ, Dark Scorpion choose-1);
- v145: PsctConditionGate; v146: Graceful Charity DrawThenDiscard; v147: no-colon trigger fail-close (+ Card of Safe
  Return); v148: SpiritReturnPsct; v149: FlipDefCheck (Acid Trap Hole); v150: Toons (parked from v142); v151: WholeResolution
  fail-close (32 cards); v152: OppMonsterTargets (face-down 'monster your opponent controls' targets); v153: the abandoned
  v153 batch run; v154–v157: Grok bots' shared atoms (09-30..10-02, unreviewed); v158: hardening of those cards. All from the
  2026-09-29 post-v142 landing queue (one change per number, bumped by the queue hub where the author could not).
- v159–v168: workflow card batches (v159-lon-b, v160-lod, v161-lod-b, v162-pgd, v163-pgd-mfc, v164/v165-mfc
  SpellCounter, v166 SpellCounter QC, v167 Union, v168 MFC — see AGENTS.md / `Docs/CURRICULUM_BATCH_*`);
- v169: LP cache poisoning (append-only enum fix, no compiler-chain note); v170–v171: Mecha-Dog Marron and its
  both-players atom fix; v172: MfcSimpleSpells v168 audit (fabricated atoms removed, honest pins).
  Top number as of 2026-10-07: **172**.

Always bump above the highest number seen anywhere, including branches and uncommitted work.

## Gotchas already paid for

- The headless harness compiles only `Data/**`, `Duel/**` and a few listed `Core/` files. When another session adds a
  `Data/` file that uses a `Core/` type, the harness stops building and the batch's baseline gate aborts, although the
  Unity project (`csc_overlay.sh .`) is fine. Add that `Core/` file to `Tools/HeadlessEngine/HeadlessEngine.csproj` if it is
  plain C# (the shim covers Color / Mathf / Vector). On 2026-09-30 MapZoneCatalog.cs was added for StreetLpBand.

- A Spell/Trap target clause can be routed by the **legacy** `SpellTrapEffects.MapCompiledTargetKind` /
  `CollectLegalTargets` mirror instead of `TextEffectRuntime.CollectTargets`. A new zone must be mapped in
  both, and filtered in both (see `OppSpellTraps`, Spiritualism, v140).
- `FieldSpellEffects.ApplyCompiledContinuous` runs every `ContinuousWhileFaceUp` clause on each board refresh.
  Continuous position locks set `PositionLockedByEffect`, which `ClearOne` resets on each refresh.
- A lone legal target resolves with no prompt: tests must accept `!engine.IsAwaitingEffectTarget`.
- The AI opponent answers chains in tests (it chained Waboku to protect its own targeted Set). Pick targets
  it cannot chain away, or empty its responses.
- `engine.Rng.QueueDie(n)` / `QueueCoin(bool)` make die and coin tests deterministic.
- `TcgRegressionTests` has "leftover unique is not FullyCompiled" corpus guards. Implementing such a card
  means rewriting its guard to assert the new compiled shape.
- The Unity Editor compiles `Assets/**` with Unity's own Roslyn. The headless harness only compiles
  Data + Duel, so run `csc_overlay.sh .` after integrating.
