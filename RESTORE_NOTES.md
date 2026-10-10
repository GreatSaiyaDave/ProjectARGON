# Project ARGON Restored — reconstruction manifest

Date: 2026-10-09. Built by full-reconstruction procedure after the original checkout
was deleted and replaced by a fresh GitHub clone (Oct 8, 12:05) at a different path.

## What this folder is

`ProjectARGON` (live checkout, HEAD 25126017 = origin/main Sep 24) **plus** everything
the Oct 8 reset wiped that post-reset sessions never re-created:

- **223 never-tracked source files** restored byte-identical from `stash@{0}^3`
  (`68297960`); `cmp`-verified, 0 mismatches.
- **86 tracked files** whose pre-deletion modifications the `reset --hard HEAD` wiped
  and nothing re-created (live tree == HEAD, mtime frozen at the reset second):
  restored byte-identical from `stash@{0}` — including the pre-deletion `.gitignore`
  and the full 504-line `Core/ProgressionService.cs`. List:
  `~/argon-restore-snapshots/silent-losses-86.txt`.
- **3 session-edited core files merged** (Oct 8-evening refactor had removed APIs the
  restored files still call; pre-deletion semantics win — no shipped data depends on
  either variant, `artifacts.json` carries no shard/piece ids):
  - `Core/ArDuelMatchConfig.cs`: re-added `Difficulty`, `RulesFormatId`,
    `PlayerDeckBoxIndex`, `PreDuelSetupDone`, `StoryStageId`, `DkOverlay`,
    `IsSameDeviceHotseat`, `IsPracticeOrLab`, `AllowsRestart`, `DuelResultId`,
    `BeginNewDuel()`, the `Summary()` rules segment, `using WRLDZ.Duel;`
    (stash text verbatim; the refactor's relocations/new docs kept).
  - `Core/ArtifactService.cs`: `ErazPieceId` reverted to the pre-deletion
    `"eraz.<band>.piece"` scheme; re-added `SoulFragment`
    (`"soul.fracture_piece"`), `IsErazPieceId`, `FormatId`, `FormatPieceId`,
    `IsFormatPieceId`, badge-gated `CanEarnSetEnergy`, and `TryFuseSoulFragments`
    (+ its 2 `Grant` call sites) — all stash text verbatim.
  - `UI/DuelUI.cs`: re-added the `Match` auto-property (single writer:
    `DuelBootstrap:405`; stash doc kept).
- **2 whole-file saves that needed no merge**: `Core/DuelFormatCatalog.cs` (only its
  `FirstPlayerMethod` enum duplicated the relocated one — enum block deleted) and
  `UI/AvatarCustomizerUI.cs` (the stash version is already `partial`, pairing with
  the restored `AvatarCustomizerUI.Body3d.cs` whose 19 methods exist nowhere else).

Nothing else was changed: every other file keeps its current (post-reset session) content.
The 8 files whose content diverged after the stash (2 regenerated `.meta` GUIDs,
`ArDiskDuelButton.cs`, 5 v174/DiscardTriggers test/registration/atom edits) keep their
newer in-tree versions.

## Provenance of the 223 restored files

Source: `stash@{0}` (`e8e69e05`, captured Oct 8 17:03:32, one second before the two
`reset --hard HEAD`), third parent `68297960` (the stashed untracked files).
Extraction: `git archive "stash@{0}^3" -- <223 paths>` — byte-verified with `cmp`
against the stash blobs after extraction (0 mismatches).

Why they were missing: the Oct 8 copy-over of the old tree onto the fresh clone was
partial for never-tracked files (all 164 tracked modifications survived; `D = 0`
worktree-vs-stash). The 8 untracked files whose content diverged after the stash
(2 regenerated `.meta` GUIDs, `ArDiskDuelButton.cs`, 5 v174/DiscardTriggers
test/registration/atom edits) were deliberately kept at their newer in-tree versions.

Full missing-file list: `~/argon-restore-snapshots/stash-untracked-missing-223.txt`
(diverged-8 list: `~/argon-restore-snapshots/stash-untracked-differ.txt`).

## Safety net (all local, nothing pushed)

- `~/argon-restore-snapshots/argon-pre-restore-20261009-063636.tgz` (1.1 GB, tree minus Library/Temp)
- `~/argon-restore-snapshots/argon-refs-20261009-063636.bundle` (verified, 47 refs incl. stash)
- Live-repo branch `backup/pre-restore-20261009` (`2f96b100`, full tree state incl. untracked)

## First-open notes

- `Library/` was excluded (4.8 GB, machine-generated, contains absolute paths) and
  `Temp/` was excluded: Unity reimports on first open. Expect a long first import.
- `.git` is included, remote `origin` still points at GitHub. The copied index shows
  the pre-reconstruction staged state; `git status` here is informational only.
- The 2 `.meta` files whose GUIDs differ from the pre-deletion snapshot
  (`Core/EditorWorkingDirectory`, `Core/StreamingFiles`) keep their newer GUIDs; if
  any scene/prefab references the old GUIDs, remap them in the Editor.
