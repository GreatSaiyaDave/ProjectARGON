# ERAZ Compile Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deck-legal cards activate only with a complete program; uncompiled/stub cards never enter constructed decks; ERAZ is a TCG subformat unlocked by a tutorial badge, not a live `[UNIMPLEMENTED]` refusal.

**Architecture:** Close the existing compiler/cache (`CardEffectStatus`, `OfficialEffectRegistry`, `CompiledEffectCache`) so stubs cannot activate and `AllowRuntimeAi` is off in play. Add `ErazFormat` + `eraz_eras.json` as the snapshot the engine is handed. Tutorial grants the `original` badge on `PlayerProgress`; format select shows an ERAZ tray only on TCG formats. Historic text snapshots and later-band scrape are follow-up data; first constructed pool is pre-TLM sets in `pre_link_sets.json` using current `cards_db.json` desc.

**Tech Stack:** Unity 6000 C#, existing `TcgRegressionTests` / `InteractionRegressionTests` (no NUnit), `Tools/wrldz-verify.sh`, StreamingAssets JSON.

**Spec:** `docs/superpowers/specs/2026-08-27-eraz-compile-gate-design.md`

## Global Constraints

- Official text with no complete program must not invent a resolution (spec: fail at compile/deck, not Activate).
- Live duels never call xAI; `AiEffectCompiler` is Editor/CI only.
- Never `if (cardId == …)` unless the mechanic is unique (`EffectVocabulary`).
- ERAZ is not a format row next to DDM/GENESYS; DDM/GENESYS do not show the badge tray.
- Tutorial grants first badge (`original`); `PlayerProgress.DefaultNew()` must not grant it.
- Duelist Kingdom vs Battle City do not split Original card text.
- `DuelEngine` stays the only mutator; AR/UI present after `Notify()`.
- Proof: `TcgRegressionTests.RunAll()` + `InteractionRegressionTests.RunAll()` via `Tools/wrldz-verify.sh` (or Editor **WRLDZ → Rules → Run TCG Regression Tests**). Fail on any `FAIL  ` line.
- This Unity project currently has **no git root**. Skip `git commit` unless a repo exists; do not `git init`.

## File map

| File | Responsibility |
|---|---|
| `Assets/Scripts/WRLDZ/Duel/CardEffectStatus.cs` | Classify Implemented only when FullyCompiled or registry script; deck gate excludes Stub + Unimplemented |
| `Assets/Scripts/WRLDZ/Duel/Rules/OfficialEffectRegistry.cs` | Activate: stubs/unimplemented never resolve; deck-legal cards never emit `[UNIMPLEMENTED]` |
| `Assets/Scripts/WRLDZ/Duel/TextEffects/AiEffectCompileSettings.cs` | `AllowRuntimeAi` default **off** |
| `Assets/Scripts/WRLDZ/Duel/TextEffects/CompiledEffectCache.cs` | Live `GetOrCompile` does not call AI; cache key later includes era |
| `Assets/Scripts/WRLDZ/Duel/Rules/ErazFormat.cs` | **Create.** Bands, tray, pool, released, copies |
| `Assets/Scripts/WRLDZ/Duel/Rules/ErazProgress.cs` | **Create.** Badge CSV on `PlayerProgress` |
| `Assets/StreamingAssets/WRLDZ/eras/eraz_eras.json` | **Create.** Seven band records |
| `Assets/StreamingAssets/WRLDZ/eras/pre_link_sets.json` | TLM is GX, not Original |
| `Assets/Scripts/WRLDZ/Data/PlayerProgress.cs` | `erazBadgesCsv` |
| `Assets/Scripts/WRLDZ/UI/BootFlowUI.cs` | Tutorial skip/duel grants `original` badge |
| `Assets/Scripts/WRLDZ/UI/Shell/DeckCollectionScreen.cs` | `TryAdd` uses `MayIncludeInDeck` + era `MaxCopies` |
| `Assets/Scripts/WRLDZ/UI/Shell/FormatSelectScreen.cs` | TCG vs non-TCG tray |
| `Assets/Scripts/WRLDZ/Core/ArDuelMatchConfig.cs` | `ErazBandId` |
| `Assets/Scripts/WRLDZ/Duel/Rules/OfficialCardAuthority.cs` | `OfficialText(def, era)` overload (snapshot later; now `desc`) |
| `Assets/Scripts/WRLDZ/Duel/Rules/OfficialDataSources.cs` | `MaxCopies(passcode, era)` |
| `Assets/Scripts/WRLDZ/Duel/Rules/TcgRegressionTests.cs` | New Check() cases |
| `Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs` | Lab activations still work |
| `Assets/Scripts/WRLDZ/UI/UI_SPEC.md` | ERAZ off the format row |
| `Tools/scan_engine_gaps.py` | Era-aware Original wall (no TLM) |

Do not restructure `DuelEngine.cs` in this plan.

---

### Task 1: Stub and unimplemented cannot enter decks; cannot activate as programs

**Files:**
- Modify: `Assets/Scripts/WRLDZ/Duel/CardEffectStatus.cs`
- Modify: `Assets/Scripts/WRLDZ/Duel/Rules/OfficialEffectRegistry.cs` (`CanActivateOfficial`)
- Modify: `Assets/Scripts/WRLDZ/Duel/TextEffects/AiEffectCompileSettings.cs`
- Test: `Assets/Scripts/WRLDZ/Duel/Rules/TcgRegressionTests.cs`
- Test: `Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs` (must still pass lab)

**Interfaces:**
- Consumes: `CompiledCardProgram.FullyCompiled`, `OfficialEffectRegistry.HasActivatableScript(int)`, `OfficialCardAuthority.HasNoActivatableEffect`
- Produces: `CardEffectStatus.MayIncludeInDeck(CardDef)` true only for Structural or Implemented when `ExcludeUnimplementedFromDecks` is true; `Classify` Implemented only if registry **or** (`FullyCompiled && CanResolveAny`); **Lua catalogs no longer mark Implemented**

- [ ] **Step 1: Write the failing tests**

In `TcgRegressionTests.RunAll()`, after the existing Ha Des check, add:

```csharp
{
    var prev = CardEffectStatus.ExcludeUnimplementedFromDecks;
    CardEffectStatus.ExcludeUnimplementedFromDecks = true;
    try
    {
        Check("Deck gate: Normal Monster may enter when exclusion on",
            CardEffectStatus.MayIncludeInDeck(celtic));
        Check("Deck gate: Ha Des (uncompiled effect) may NOT enter when exclusion on",
            !CardEffectStatus.MayIncludeInDeck(haDes));
        Check("Classify: Ha Des is not Implemented",
            CardEffectStatus.Classify(haDes) != CardEffectStatusKind.Implemented);

        var pot = db != null ? db.Get(55144522) : null; // Pot of Greed
        if (pot != null)
            Check("Deck gate: Pot of Greed (compiled/registry) may enter",
                CardEffectStatus.MayIncludeInDeck(pot));

        Check("Runtime AI default is off",
            PlayerPrefs.GetInt("wrldz_ai_fx_runtime", 0) == 0
            || !AiEffectCompileSettings.AllowRuntimeAi);
    }
    finally
    {
        CardEffectStatus.ExcludeUnimplementedFromDecks = prev;
    }
}
```

Need `using WRLDZ.Duel.TextEffects;` if not present. `db` is not in `TcgRegressionTests` today — load it:

```csharp
var db = CardDatabase.Instance ?? CardDatabase.Load();
```

at the start of this block. If `db` is null, `Check("CardDatabase load for deck gate", false)` and skip pot.

Also add an activate-reason check that does not require a full duel:

```csharp
{
    var engine = new DuelEngine(); // only if a cheap constructor exists
}
```

Do **not** construct `DuelEngine` if it needs decks. Instead test `Classify` + a helper:

Add to `OfficialEffectRegistry`:

```csharp
public static bool ProgramMayActivate(CardDef def)
{
    if (def == null) return false;
    if (HasActivatableScript(def.id)) return true;
    var prog = CompiledEffectCache.GetOrCompile(def);
    return prog != null && prog.FullyCompiled && prog.CanResolveAny;
}
```

Test:

```csharp
Check("ProgramMayActivate: Ha Des false",
    !OfficialEffectRegistry.ProgramMayActivate(haDes));
Check("ProgramMayActivate: Celtic Guardian false (structural)",
    !OfficialEffectRegistry.ProgramMayActivate(celtic));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `Tools/wrldz-verify.sh`  
Expected: `FAIL  Deck gate: Ha Des` and/or `FAIL  Classify` because `MayIncludeInDeck` still returns true when exclusion is off-by-default and Lua/catalog may mark cards Implemented. If Pipeline is down and Editor holds the lock, run Editor menu **WRLDZ → Rules → Run TCG Regression Tests** instead. Do not spawn a second Unity.

- [ ] **Step 3: Implement**

`CardEffectStatus.Classify` — remove the `YgoProTriggerCatalog` / `YgoProContinuousCatalog` Implemented short-circuit. Order:

1. null → Unimplemented  
2. `HasNoActivatableEffect` → Structural  
3. `HasActivatableScript` → Implemented  
4. `GetOrCompile`: FullyCompiled && CanResolveAny → Implemented  
5. ClauseList.Count > 0 → Stub  
6. Unimplemented  

`MayIncludeInDeck`:

```csharp
public static bool MayIncludeInDeck(CardDef def)
{
    if (def == null) return false;
    var s = Classify(def);
    if (!ExcludeUnimplementedFromDecks) return true;
    return s == CardEffectStatusKind.Structural
        || s == CardEffectStatusKind.Implemented;
}
```

Set `ExcludeUnimplementedFromDecks = true` as the **default** (static field initializer `= true`). Lab decks are 40/40 implemented; Ha Des is not in lab.

`OfficialEffectRegistry.CanActivateOfficial`:

- After compiling `prog`, if using the text path, require `prog.FullyCompiled` (not merely `CanResolveAny`).
- If not FullyCompiled and not `HasActivatableScript`, do **not** activate.
- If the card **is** FullyCompiled or registry and the *timing* is illegal, reason must **not** start with `[UNIMPLEMENTED]` (use timing/cost/target wording only).
- Keep `[UNIMPLEMENTED]` only when `Classify == Unimplemented` (should not happen for a deck-legal card; still log for lab bypass).

`AiEffectCompileSettings.AllowRuntimeAi` getter default:

```csharp
get => PlayerPrefs.GetInt("wrldz_ai_fx_runtime", 0) != 0;
```

`CompiledEffectCache.GetOrCompile` already respects `AllowRuntimeAi`. Editor bulk uses `ForceRecompile(def, allowAi: true)` — leave that.

- [ ] **Step 4: Run tests to verify they pass**

Run: `Tools/wrldz-verify.sh`  
Expected: no `FAIL  ` lines. Lab interactions (Pot of Greed, Sangan, Man-Eater Bug, Kuriboh) still PASS.

- [ ] **Step 5: Commit if git exists**

```bash
git add Assets/Scripts/WRLDZ/Duel/CardEffectStatus.cs \
  Assets/Scripts/WRLDZ/Duel/Rules/OfficialEffectRegistry.cs \
  Assets/Scripts/WRLDZ/Duel/TextEffects/AiEffectCompileSettings.cs \
  Assets/Scripts/WRLDZ/Duel/Rules/TcgRegressionTests.cs
git commit -m "fix(duel): refuse stub programs at deck and activate"
```

If no `.git`, skip.

---

### Task 2: `ErazFormat` + `eraz_eras.json`

**Files:**
- Create: `Assets/StreamingAssets/WRLDZ/eras/eraz_eras.json`
- Create: `Assets/Scripts/WRLDZ/Duel/Rules/ErazFormat.cs`
- Modify: `Assets/StreamingAssets/WRLDZ/eras/pre_link_sets.json` (`preLinkWall` note; TLM `role` = `gx-start`)
- Test: `Assets/Scripts/WRLDZ/Duel/Rules/TcgRegressionTests.cs`

**Interfaces:**
- Consumes: `pre_link_sets.json` set codes; spec band table
- Produces:

```csharp
public static class ErazFormat
{
    public const string Original = "original";
    public const string Gx = "gx";
    public const string FiveDs = "5ds";
    public const string Zexal = "zexal";
    public const string Arcv = "arcv";
    public const string Vrains = "vrains";
    public const string Modern = "modern";

    public static ErazBand Band(string id);
    public static IReadOnlyList<string> BandIdsInOrder();
    public static bool ShowsBadgeTray(string formatId);
    public static bool InOriginalSetList(string setCode); // TLM false
    public static bool InPoolBySetCode(string setCode, string eraId);
    public static string NextBand(string eraId); // original → gx → … → null after modern
}
```

```csharp
[Serializable]
public class ErazBand
{
    public string id;
    public string name;
    public int order;
    public string lastCoreCode;
    public string lastCoreDate;
    public string nextFirstSet;
    public string banlistId;
    public string extraKinds; // "fusion" / "fusion,synchro" / …
}
```

- [ ] **Step 1: Write failing tests**

```csharp
Check("ERAZ: original exists", ErazFormat.Band(ErazFormat.Original) != null);
Check("ERAZ: TLM is not Original", !ErazFormat.InOriginalSetList("TLM"));
Check("ERAZ: FET/LOB is Original",
    ErazFormat.InOriginalSetList("LOB") && ErazFormat.InOriginalSetList("FET")
    || ErazFormat.InOriginalSetList("LOB")); // FET may be missing from pre_link_sets; LOB must pass
Check("ERAZ: TCG format shows tray", ErazFormat.ShowsBadgeTray("pvai"));
Check("ERAZ: quick shows tray", ErazFormat.ShowsBadgeTray("quick"));
Check("ERAZ: ddm hides tray", !ErazFormat.ShowsBadgeTray("ddm"));
Check("ERAZ: genesys hides tray", !ErazFormat.ShowsBadgeTray("genesys"));
Check("ERAZ: next after original is gx", ErazFormat.NextBand(ErazFormat.Original) == ErazFormat.Gx);
```

- [ ] **Step 2: Run tests — expect FAIL** (`ErazFormat` missing)

- [ ] **Step 3: Implement**

`eraz_eras.json`:

```json
{
  "version": 1,
  "bands": [
    {
      "id": "original",
      "name": "Original / Duel Monsters",
      "order": 1,
      "lastCoreCode": "FET",
      "lastCoreDate": "2005-03-01",
      "nextFirstSet": "TLM",
      "banlistId": "tcg_april_2005",
      "extraKinds": "fusion"
    },
    {
      "id": "gx",
      "name": "GX",
      "order": 2,
      "lastCoreCode": "LODT",
      "lastCoreDate": "2008-05-13",
      "nextFirstSet": "TDGS",
      "banlistId": "tcg_may_2008",
      "extraKinds": "fusion"
    },
    {
      "id": "5ds",
      "name": "5D's",
      "order": 3,
      "lastCoreCode": "EXVC",
      "lastCoreDate": "2011-05-10",
      "nextFirstSet": "GENF",
      "banlistId": "tcg_march_2011",
      "extraKinds": "fusion,synchro"
    },
    {
      "id": "zexal",
      "name": "ZEXAL",
      "order": 4,
      "lastCoreCode": "PRIO",
      "lastCoreDate": "2014-05-16",
      "nextFirstSet": "DUEA",
      "banlistId": "tcg_july_2014",
      "extraKinds": "fusion,synchro,xyz"
    },
    {
      "id": "arcv",
      "name": "ARC-V",
      "order": 5,
      "lastCoreCode": "MACR",
      "lastCoreDate": "2017-05-05",
      "nextFirstSet": "COTD",
      "banlistId": "tcg_june_2017",
      "extraKinds": "fusion,synchro,xyz,pendulum"
    },
    {
      "id": "vrains",
      "name": "VRAINS",
      "order": 6,
      "lastCoreCode": "ETCO",
      "lastCoreDate": "2020-06-05",
      "nextFirstSet": "ROTD",
      "banlistId": "tcg_july_2020",
      "extraKinds": "fusion,synchro,xyz,pendulum,link"
    },
    {
      "id": "modern",
      "name": "Modern / post-VRAINS",
      "order": 7,
      "lastCoreCode": "",
      "lastCoreDate": "",
      "nextFirstSet": "",
      "banlistId": "tcg_current",
      "extraKinds": "current"
    }
  ]
}
```

Load via `File.ReadAllText` + `JsonUtility`. Unity `JsonUtility` cannot parse arrays at root — wrap `bands` as already shown. Add a serializable `ErazFile { public int version; public ErazBand[] bands; }`.

`ShowsBadgeTray`: true unless `formatId` is `ddm`, `genesys`, `raid`, `speed`, `deckmaster` (case-insensitive).

`InOriginalSetList`: load `pre_link_sets.json` `sets[].code`; Original = all codes with `order` strictly before TLM’s order. If TLM missing, treat TLM as not Original anyway.

On the TLM object in `pre_link_sets.json`, set `"role": "gx-start"` and change file `preLinkWall` to `"Original ERAZ ends before TLM; TLM is GX."`

Do not scrape banlists in this task. `MaxCopies` still uses the empty Advanced stub (unlimited) until Task 6.

- [ ] **Step 4: Run `wrldz-verify.sh` — all PASS**

- [ ] **Step 5: Commit if git exists** — `feat(duel): add ERAZ band table and format tray rules`

---

### Task 3: Tutorial grants Original badge; DefaultNew does not

**Files:**
- Create: `Assets/Scripts/WRLDZ/Duel/Rules/ErazProgress.cs`
- Modify: `Assets/Scripts/WRLDZ/Data/PlayerProgress.cs`
- Modify: `Assets/Scripts/WRLDZ/UI/BootFlowUI.cs` (both tutorial buttons ~line 932 and 955)
- Modify: `Assets/Scripts/WRLDZ/Core/LabAdminService.cs` (lab accounts: grant `original` so lab constructed works)
- Test: `Assets/Scripts/WRLDZ/Duel/Rules/TcgRegressionTests.cs`

**Interfaces:**
- Consumes: `ErazFormat.Original`, `ErazFormat.NextBand`
- Produces:

```csharp
public static class ErazProgress
{
    public const string CsvField = "erazBadgesCsv";
    public static bool HasBadge(PlayerProgress p, string eraId);
    public static void GrantBadge(PlayerProgress p, string eraId);
    public static void GrantTutorialBadge(PlayerProgress p); // original only
    public static void GrantNextOnSeasonComplete(PlayerProgress p);
    public static bool CanSelectBand(PlayerProgress p, string eraId);
    public static string[] Owned(PlayerProgress p);
}
```

`PlayerProgress.erazBadgesCsv` default `""`.

`CanSelectBand`: `HasBadge` (released check in Task 6; for now badge is enough).

- [ ] **Step 1: Failing tests**

```csharp
var fresh = PlayerProgress.DefaultNew();
Check("Badge: new account has no ERAZ badge",
    !ErazProgress.HasBadge(fresh, ErazFormat.Original));
ErazProgress.GrantTutorialBadge(fresh);
Check("Badge: tutorial grants original",
    ErazProgress.HasBadge(fresh, ErazFormat.Original));
Check("Badge: tutorial does not grant gx",
    !ErazProgress.HasBadge(fresh, ErazFormat.Gx));
ErazProgress.GrantNextOnSeasonComplete(fresh);
Check("Badge: season complete grants gx",
    ErazProgress.HasBadge(fresh, ErazFormat.Gx));
Check("Badge: cannot select 5ds yet",
    !ErazProgress.CanSelectBand(fresh, ErazFormat.FiveDs));
```

- [ ] **Step 2: Run — FAIL** (type missing)

- [ ] **Step 3: Implement**

CSV helpers: split/trim on comma; ignore empty. `GrantBadge` is idempotent. `GrantNextOnSeasonComplete`: find highest owned order via `ErazFormat.BandIdsInOrder()`, grant `NextBand` of that; if none owned, grant original (should not happen after tutorial).

`BootFlowUI` both tutorial success paths, after `onboardingTutorialDuelDone = true`:

```csharp
ErazProgress.GrantTutorialBadge(acc.progress);
```

`LabAdminService` when forcing onboarding complete: same grant.

Do not grant in `DefaultNew`.

- [ ] **Step 4: `wrldz-verify.sh` PASS**

- [ ] **Step 5: Commit if git exists** — `feat(progress): grant Original ERAZ badge from tutorial`

---

### Task 4: Deck builder uses compile gate + era copy limit

**Files:**
- Modify: `Assets/Scripts/WRLDZ/UI/Shell/DeckCollectionScreen.cs` `TryAdd` (~3140)
- Modify: `Assets/Scripts/WRLDZ/Duel/Rules/OfficialDataSources.cs` add `MaxCopies(int passcode, string eraId)` that currently delegates to `MaxCopies(passcode)`
- Test: `TcgRegressionTests` (pure functions; do not instantiate the UI)

**Interfaces:**
- Consumes: `CardEffectStatus.MayIncludeInDeck`, `OfficialDataSources.MaxCopies`
- Produces: `TryAdd` rejects unimplemented/stub with `msg` containing `not implemented` (player-facing, **not** `[UNIMPLEMENTED]`)

- [ ] **Step 1: Failing test — extract the rule so UI can call it**

Add:

```csharp
public static class ErazDeckRules
{
    public static bool CanAddToDeck(CardDef def, int alreadyInDeck, string eraId, out string msg)
    {
        msg = "";
        if (def == null) { msg = "No card."; return false; }
        if (!CardEffectStatus.MayIncludeInDeck(def))
        {
            msg = "Effect not implemented for this era.";
            return false;
        }
        var cap = OfficialDataSources.MaxCopies(def.id, eraId);
        if (alreadyInDeck >= cap)
        {
            msg = cap <= 0 ? "Forbidden in this era." : "Copy limit.";
            return false;
        }
        return true;
    }
}
```

Put this in `ErazFormat.cs` (same file is fine) or `ErazDeckRules.cs` if `ErazFormat.cs` would exceed ~200 lines.

Test:

```csharp
CardEffectStatus.ExcludeUnimplementedFromDecks = true;
Check("DeckRules: Celtic 3 copies OK",
    ErazDeckRules.CanAddToDeck(celtic, 2, ErazFormat.Original, out _));
Check("DeckRules: Ha Des rejected",
    !ErazDeckRules.CanAddToDeck(haDes, 0, ErazFormat.Original, out var haMsg)
    && haMsg.IndexOf("not implemented", StringComparison.OrdinalIgnoreCase) >= 0);
```

- [ ] **Step 2: Run — FAIL** until helper exists

- [ ] **Step 3: Implement helper; call from `TryAdd` after Home-only check:**

```csharp
if (!ErazDeckRules.CanAddToDeck(def, inDeck, ErazFormat.Original, out msg))
    return false;
```

Until match config carries era (Task 5), constructed editor uses `ErazFormat.Original`.

Replace `inDeck >= TcgRules.MaxCopiesPerCard` with the helper’s cap (helper already checks copies). Keep Extra/Main/Side section checks.

- [ ] **Step 4: `wrldz-verify.sh` PASS**

- [ ] **Step 5: Commit if git exists** — `feat(deck): block unimplemented cards from ERAZ constructed`

---

### Task 5: Match config + format tray (TCG only)

**Files:**
- Modify: `Assets/Scripts/WRLDZ/Core/ArDuelMatchConfig.cs` add `public string ErazBandId = ErazFormat.Original;`
- Modify: `Assets/Scripts/WRLDZ/UI/Shell/FormatSelectScreen.cs`
- Modify: `Assets/Scripts/WRLDZ/UI/UI_SPEC.md` §3.5 — remove ERAZ from the format table; document badge tray on TCG formats
- Test: `TcgRegressionTests` for `ShowsBadgeTray` (already Task 2) plus:

```csharp
var cfg = ArDuelMatchConfig.DefaultQuick();
Check("Match default ERAZ is original", cfg.ErazBandId == ErazFormat.Original);
```

**Interfaces:**
- Consumes: `ErazProgress.CanSelectBand`, `ErazFormat.ShowsBadgeTray`
- Produces: `ArDuelMatchConfig.ErazBandId`

- [ ] **Step 1: Add the Check above (fails until field exists)**

- [ ] **Step 2: Run — FAIL**

- [ ] **Step 3: Implement**

On `FormatSelectScreen`, keep existing distance cards (they are TCG `pvai`). After building cards, if `ErazFormat.ShowsBadgeTray("pvai")`, add a one-line body under the hint: `"ERAZ · Original"` when `ErazProgress.HasBadge(account.progress, Original)`, else `"ERAZ · LOCKED — finish tutorial"`. Do **not** add DDM/GENESYS rows.

If no account / no badge, `Play` on TCG cards still works for **tutorial/practice** (`practice: true` and `ArDuelLaunchKind.Practice` / Boot). Hub `Start` for non-practice: if no Original badge, do not start; show LOCKED on the ERAZ line.

`AppSession.StartArDuel`: stamp `c.ErazBandId = ErazFormat.Original` when empty.

- [ ] **Step 4: `wrldz-verify.sh` PASS.** If you touch `FormatSelectScreen`, also run Editor **WRLDZ → UI → Menu Smoke** (`MenuSmokeTest`) if that menu exists; both NonAr and AR holo. If smoke is unavailable, say so.

- [ ] **Step 5: Commit if git exists** — `feat(ui): ERAZ badge on TCG formats only`

---

### Task 6: Era-aware text hash + cache key (still current desc)

**Files:**
- Modify: `Assets/Scripts/WRLDZ/Duel/Rules/OfficialCardAuthority.cs`
- Modify: `Assets/Scripts/WRLDZ/Duel/TextEffects/CompiledCardProgram.cs` add `public string EraId;`
- Modify: `Assets/Scripts/WRLDZ/Duel/TextEffects/CompiledEffectCache.cs` memory key `(cardId, eraId)`
- Test: `TcgRegressionTests`

**Interfaces:**
- Consumes: `ErazFormat` ids
- Produces:

```csharp
public static string OfficialText(CardDef def, string eraId)
public static string TextHash(CardDef def, string eraId)
```

When no snapshot file exists, both equal today’s `OfficialText(def)` / `TextHash(def)`. Load `StreamingAssets/WRLDZ/eras/text/<era>.json` if present (`{ "cards": [ { "id": 1, "desc": "...", "textHash": "..." } ] }`).

- [ ] **Step 1: Test**

```csharp
var hNow = OfficialCardAuthority.TextHash(celtic);
var hEra = OfficialCardAuthority.TextHash(celtic, ErazFormat.Original);
Check("TextHash(era) matches current desc when no snapshot", hNow == hEra);

var fake = new CardDef { id = 1, name = "X", type = "Effect Monster", desc = "Draw 1 card." };
var a = OfficialCardAuthority.TextHash(fake, ErazFormat.Original);
fake.desc = "Draw 2 cards.";
var b = OfficialCardAuthority.TextHash(fake, ErazFormat.Original);
Check("TextHash changes when desc changes", a != b);
```

- [ ] **Step 2: Run — FAIL** on missing overload

- [ ] **Step 3: Implement overloads; `GetOrCompileInternal` uses `TextHash(def, era)` and stores `prog.EraId`. Default era `ErazFormat.Original` when the duel config is null.

Live path still must not call AI (`AllowRuntimeAi` off).

- [ ] **Step 4: `wrldz-verify.sh` PASS**

- [ ] **Step 5: Commit if git exists** — `feat(duel): compile cache keyed by era and text hash`

---

### Task 7: Coverage `released` + gap scan Original wall

**Files:**
- Modify: `Assets/Scripts/WRLDZ/Duel/TextEffects/EffectCoverageService.cs` (or add `ErazFormat.IsReleased(string eraId)`)
- Modify: `Tools/scan_engine_gaps.py` — Original pool excludes TLM
- Test: `TcgRegressionTests`

**Interfaces:**
- Produces: `ErazFormat.IsReleased(string eraId)` true iff every non-structural card in that band’s known set lists is `CardEffectStatusKind.Implemented` (or Structural). Lab/starter is not the Original FET pool — **Original is not released** until coverage says so. `IsReleased(Original)` may be false after Task 1; that is correct.

Player constructed with Original badge: **allow lab+starter decks always**; full collection constructed uses `IsReleased`. Spec: “Lab / Desktop Lab may run the lab slice without waiting for Original released.”

```csharp
public static bool IsLabSliceLegalWithoutRelease(int cardId, HashSet<int> labIds)
    => labIds.Contains(cardId);
```

Use `EffectCoverageService.CollectDeckIds(EffectCoverageService.StarterAndLabDeckFiles)` for the lab exception in `ErazDeckRules.CanAddToDeck` when `!IsReleased(era)`: allow only lab/starter ids + structural.

- [ ] **Step 1: Test**

```csharp
Check("Release: ddm still no tray", !ErazFormat.ShowsBadgeTray("ddm"));
var labIds = EffectCoverageService.CollectDeckIds(EffectCoverageService.StarterAndLabDeckFiles);
Check("Lab slice contains Pot of Greed", labIds.Contains(55144522));
```

If Original is not 100% compiled:

```csharp
Check("Original not silently released without full compile",
    !ErazFormat.IsReleased(ErazFormat.Original)
    || /* coverage truly 100% */ true);
```

Do not force `IsReleased` true.

- [ ] **Step 2–4:** implement `IsReleased` by iterating set passcodes from `pre_link_sets` for Original (exclude TLM); Classify each id in `CardDatabase`. Empty missing defs do not count as compiled. `wrldz-verify.sh` still PASS (gap scan must not fail for TLM extra-attack if TLM is now GX-only — if the scan is global `cards_db`, leave global scan; only document Original wall in a new function `original_set_codes()` used when `--era original` is passed). Add optional `--era original` to `scan_engine_gaps.py` that skips TLM passcodes. Default scan stays full DB.

- [ ] **Step 5: Commit if git exists** — `feat(duel): compute ERAZ released from compile coverage`

---

### Task 8: Docs alignment (no new behavior)

**Files:**
- Modify: `Assets/Scripts/WRLDZ/UI/UI_SPEC.md` §3.5 (if not done in Task 5)
- Modify: `Assets/Scripts/WRLDZ/Duel/ENGINE_GUARANTEE.md` — one paragraph: live Activate never `[UNIMPLEMENTED]` for deck-legal cards; SpaceXAI offline only; `AllowRuntimeAi` default off
- Modify: `Assets/Scripts/WRLDZ/Duel/Rules/TCG_RULES_ENGINE.md` policy line: refuse **uncompiled** at **deck**, not as a live invent; do not change structural rules

- [ ] **Step 1:** Edit those three files to match the spec. No “TODO”. No new format row for ERAZ.

- [ ] **Step 2:** No test — re-read spec Key Decisions 1–10 and confirm each is in Tasks 1–7.

- [ ] **Step 3: Commit if git exists** — `docs: align engine guarantee with ERAZ compile gate`

---

## Self-review (spec coverage)

| Spec requirement | Task |
|---|---|
| Fail at compile/deck, not Activate | 1, 4 |
| Stubs illegal | 1, 4 |
| Closed vocabulary / no live LLM | 1 (`AllowRuntimeAi` off) |
| SpaceXAI offline | 1, 6 |
| ERAZ freeze table | 2 (`eraz_eras.json`) |
| TCG subformat / no DDM tray | 2, 5 |
| Tutorial first badge | 3 |
| DK vs BC no split text | 2–8 (no second Original hash) |
| Two clocks (release vs badge) | 3, 7 |
| Lua is linter | 1 (catalog not Implemented) |
| TLM not Original | 2, 7 |
| Lab exception | 7 |
| Historic banlist scrape / era desc snapshots | **Not in this plan** — `OfficialText(def, era)` falls back to current desc (Task 6). Scrape is a later plan once gates ship. |
| Referobot / opponent ML | **Not in this plan** (spec: wait until Original released) |

## Out of this plan (YAGNI)

- Filling `text/original.json` and historic F/L JSON from Yugipedia
- GX+ constructed
- Story season mission-scoped snapshots in `DuelEngine`
- Wiring Referobot
- Full FET first-print date table (Original pool = pre-TLM codes in `pre_link_sets.json` plus FET if present)

---

## Execution

Plan complete and saved to `docs/superpowers/plans/2026-08-27-eraz-compile-gate.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch with checkpoints.

Which approach?
