# StreamingFiles — the one StreamingAssets loader

`Assets/Scripts/WRLDZ/Core/StreamingFiles.cs` is the **only** read path into `Assets/StreamingAssets`
for runtime code. The shipping target is an **Android XR glasses APK** (the Galaxy S23 is the dev
testbed). On Android, `Application.streamingAssetsPath` is `jar:file://…/base.apk!/assets`, and
`System.IO` (`File.Exists`, `File.ReadAllText`, `Directory.GetFiles`) cannot open it. Before this
facade, every loader failed on device: no card DB, decks, sprites, models or audio.
QC findings closed: core-data-security F1, ui-overworld F1, ar-presentation F4, perf-mobile PERF-01,
asset-integrity AI-01, docs-drift-deadcode C1.

## Rule

- Runtime code passes **StreamingAssets-relative** paths (`"Cards/cards_db.json"`, `"Decks/" + file`)
  to `StreamingFiles`. Do not add `Application.streamingAssetsPath` + `System.IO` reads anywhere.
- Writes never go through here. `persistentDataPath`, `temporaryCachePath` and Editor-only
  exporters stay on `System.IO`.
- `grep -rn streamingAssetsPath Assets/Scripts` should find only `StreamingFiles.cs` and the two
  documented **Editor write targets**: `CompiledEffectCache.SeedPath` (used by ExportSeed) and
  `CardModelCatalog.CardsDir` (used by GlbShellSmoke).

## API (`WRLDZ.Core.StreamingFiles`)

| Call | Notes |
|------|-------|
| `bool Exists(rel)` / `bool DirectoryExists(rel)` | |
| `byte[] ReadAllBytes(rel)` / `string ReadAllText(rel)` | Throw `FileNotFoundException` when the file is missing, like System.IO. Text is UTF-8 with BOM detection. |
| `bool TryReadAllBytes(rel, out bytes)` / `bool TryReadAllText(rel, out text)` | The usual loader pattern: one call, no separate Exists. |
| `IEnumerable<string> ReadLines(rel)` | Same line split as `File.ReadLines`. |
| `string[] GetFiles(relDir, pattern = "*", recursive = false)` | Returns **relative** paths with `/`, sorted ordinally. `*`/`?` globbing; matching is case-insensitive on the APK. |
| `string FullPathForLog(rel)` | A real path on desktop, `…/base.apk!/assets/rel` on Android. For logs only. |
| `string UrlFor(rel)` | For UnityWebRequest-only decoders (audio). `file://` on desktop, the jar URL on Android. |
| `string MaterializeToFile(rel)` | A real file for APIs that need a path (SQLite `cards.cdb`). On Android it copies once to `temporaryCachePath/StreamingFiles/`, with a stamp of build GUID + entry size. |
| `string Normalize(rel)` / `string ToRelative(absPath)` | Converts slashes and `.`/`..`, and turns an absolute path under StreamingAssets into a relative one. |
| `SelfTestReport SelfTest()` | Lists `Decks/`, parses the card DB, counts `CardArt/*.jpg`, and checks `OcgCore/cards.cdb`. |

## How it works

- **Desktop / Editor / iOS:** plain `System.IO` on `streamingAssetsPath`. Behaviour is unchanged.
  The Linux Editor working-directory pin (`EditorWorkingDirectory.Pin`), which each loader used to
  do by hand, now happens inside the facade after every IO call, on the main thread only.
- **Android (the streamingAssetsPath starts with `jar:`):** the facade opens the APK once as a
  `System.IO.Compression.ZipArchive` (netstandard 2.1) and indexes every `assets/…` entry,
  excluding Unity's own `assets/bin/Data/`. The index is a case-sensitive dictionary plus a set of
  directories. Reads are synchronous zip-entry reads. StreamingAssets are stored uncompressed, and
  deflated entries also work.
  - On the first miss, the facade also indexes `Application.dataPath` (if it differs) and every
    `*.apk` beside the base APK. That covers app-bundle installs whose StreamingAssets sit in a
    split such as `split_UnityStreamingAssetsPack.apk`. It logs once.
  - It is thread-safe: one lock covers all archive access, so worker threads may read.
  - Small text files (64 KB or less, 48 entries) sit in an LRU. It is used only in APK mode, where
    content can never go stale.
- Paths are captured on the main thread at `SubsystemRegistration`, or lazily on first use.

## Self-test

- **Desktop Lab:** the **STREAMING FILES SELF-TEST** row. It also works in an Android
  *development* build and is the quickest on-device proof.
- **Editor:** **WRLDZ → Lab → Streaming Files Self-Test**.
- The report is written to the Console. `PASS` needs `Decks/*.json` and at least one parsed card.

Offline verification (done 2026-09-23): the facade was compiled into a console harness with Unity
stubs and run against (a) the real `Assets/StreamingAssets` folder (1717 cards, 62 decks,
13,426 Lua scripts) and (b) a synthetic APK. The APK had stored and deflated entries, a BOM file,
`assets/bin/Data`, and a split APK beside it, and it was read by 400 parallel threads. All checks
passed. **No real APK has been built or run on a device yet.**

## Converted loaders (2026-09-23)

CardDatabase (card DB, art lookup, decks), StreamingSprite, ImagineAssets (via StreamingSprite),
FreeUiKit, VendorKit, YgoCardFrames, EldritchAssets, WrldzAudio (Exists + `UrlFor`),
OpponentCatalog, StoryCampaignService, ArtifactCatalog, GoogleSignIn, ErazFormat,
CardEraCurriculum, OfficialDataSources (banlist), OfficialCardAuthority (era text), TcgLegalPool,
YgoProTriggerCatalog, YgoProContinuousCatalog, AiHeuristicPolicy (the shipped candidate),
CompiledEffectCache (seed read), OcgScriptStore (Lua index), OcgCardCatalog (JSON, and SQLite via
`MaterializeToFile`), OcgPreflightState (the fingerprint is byte-identical), OcgSpeedTable,
OcgReplayCompare, StubOcgDuelCore, OcgLabDecks, NativeOcgDuelCore (PROVENANCE), FreeModelCatalog,
ReferobotActor, CardModelCatalog (GLB/OBJ/shells.json), HubPropView, SpiritDuelerDiskView,
DuelDiskField, DuelStageView, ArDuelDiskRig.
`ObjMeshLoader.LoadFromStreaming` and `GlbShellLoader.LoadFromStreaming` are the StreamingAssets
entry points. `LoadFromFile` remains for absolute Editor paths.
The Editor tests (OcgLabManifestTests, OcgCardCatalogTests, OcgCoreReplayTests) read through the
facade too.

## Known Android gaps (not solved by this facade)

- **Native plugins:** only `x86_64` builds of `ocgcore` and `e_sqlite3` ship. On Android,
  `SQLitePCL` init fails (it is caught), so OcgCardCatalog falls back to `cards_datas.json`, and the
  native OCG core is unavailable. That needs arm64 builds of both.
- **`OcgCore/scripts/official~`:** Unity treats folders ending in `~` as hidden. Check whether the
  player build copies that folder into the APK. If it does not, the Lua corpus is missing on every
  player platform, not only Android. The self-test does not cover it.
- **Size:** everything in StreamingAssets ships in the APK. The owner's move-out plan (about
  285 MB of unreferenced content) runs separately, with the Editor closed.
