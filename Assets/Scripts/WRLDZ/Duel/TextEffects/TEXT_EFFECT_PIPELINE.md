# Text-first effect pipeline

## Idea

When a card is **played** (activated, Flip Summoned, or sent field→GY with a trigger), the engine:

1. Reads official text from `cards_db.json` (`CardDef.desc`)
2. Looks up a remembered program (StreamingAssets seed / disk / memory)
3. If missing, compiles matching PSCT templates into a `CompiledCardProgram` (regex)
4. If incomplete **and** SpaceXAI is allowed + keyed (`AllowRuntimeAi`, **default off**), compiles once via schema-validated AI — player builds skip this
5. **Remembers** the program (memory + disk cache; optional StreamingAssets seed)
6. Reuses it on every later play — **no LLM on live resolution**

No invented effects: validators reject unknown enums / empty AI invents. Only clauses that map to known `EffectActionKind` values are stored.

## Flow

```
Play card
  → CompiledEffectCache.GetOrCompile(def)
       ├─ cache hit (seed / disk / memory; same TextHash + CompilerVersion) → return
       ├─ CardTextEffectCompiler.Compile(official text)   // regex templates
       └─ if !FullyCompiled && AllowRuntimeAi && XAI_API_KEY
            → AiEffectCompiler (SpaceXAI chat.completions)
            → EffectProgramValidator (schema)
            → keep AI only if PreferProgram (full or more clauses)
            → save memory + persistentDataPath/WRLDZ/wrldz_compiled_effects_v1.json
  → TextEffectRuntime.CanActivate / TryResolve*
  → if unusable: legacy SpellTrapEffects / MonsterEffects hardcodes
  → if still no: refuse activation (honest)
```

**Live path never calls the model** — only `TextEffectRuntime` + cached programs.

## Offline bulk (Editor)

Menu **WRLDZ → Text Effects**:

| Menu item | Purpose |
|-----------|---------|
| Regex Compile Lab Decks + Export Seed | Lab decks, regex only → seed |
| AI Bulk Compile Lab Decks + Export Seed | Lab decks, regex + SpaceXAI → seed |
| Regex Compile All Cards (no AI) | Full DB regex |
| AI Bulk Compile Incomplete Only | Full DB AI only where not full |
| Export Current Cache → StreamingAssets Seed | Write seed without recompile |
| Show AI Key Status | Env / prefs / config check |

Seed path: `StreamingAssets/WRLDZ/compiled_effects_seed_v1.json`  
Load order: **seed → persistent disk** (disk wins).

## SpaceXAI settings

| Source | Detail |
|--------|--------|
| Env | `XAI_API_KEY` |
| PlayerPrefs | `wrldz_xai_api_key` |
| File | `persistentDataPath/WRLDZ/ai_config.json` → `{"apiKey":"..."}` |
| Model | default `grok-4.5` (`wrldz_ai_fx_model`) |
| First-play AI | `AllowRuntimeAi` (PlayerPrefs `wrldz_ai_fx_runtime`, **default off**) |
| API | `https://api.x.ai/v1/chat/completions` |

Never commit real keys. Prefer offline bulk + ship seed for S23 lab builds.

## Files

| File | Role |
|------|------|
| `CompiledCardProgram.cs` | Clause model + program |
| `CardTextEffectCompiler.cs` | PSCT template regex compiler |
| `CompiledEffectCache.cs` | First-play learn + remember + seed |
| `TextEffectRuntime.cs` | Execute remembered clauses (no LLM) |
| `AiEffectCompileSettings.cs` | Key / model / runtime flag |
| `AiEffectCompiler.cs` | SpaceXAI HTTP compile |
| `EffectProgramValidator.cs` | Schema validate + AI JSON DTO |
| `Editor/.../TextEffectBulkCompileMenu.cs` | Offline bulk |
| `EffectCoverageService.cs` | Phase 1: starter+lab bulk compile + coverage report |
| `Editor/.../EffectCoverageMenu.cs` | Menu + batch `RunPhase1Batch` |

## Covered templates

**PSCT grammar (v4, `PsctGrammar`)** — Konami Part 3:

`CONDITIONS : ACTIVATION ; RESOLUTION`

- `:` = when / how often. `;` = costs + targeting at activation. After `;` = resolution.
- Colon or semicolon ⇒ Chain Link. Monster text with neither ⇒ no chain.
- Spell/Trap **card** activation always chains; Field/Continuous leftover stat lines do not.
- Fragments then compile cost (`discard 1 …`), targeting (`target 1 card on the field`), and resolution (`destroy it` / `return it to the hand` / `draw N`) without a unique whole-card regex.

**Whole-card regex (staples):** Draw N · Raigeki · Dark Hole · Heavy Storm · MST · Monster Reborn ·  
Mirror Force · Negate Attack · Trap Hole · Waboku · Card Destruction · Swords · Ring ·  
Flute · Polymerization (registered recipes) · Enemy Controller (position mode) ·  
FLIP destroy · FLIP Spell from GY · Sangan search · Cyber Jar · Lord of D. ·  
A Legendary Ocean continuous · Abyss Soldier ignition.

AI may map additional cards **only** onto the same `EffectActionKind` / `EffectTiming` enums (no free-form invent).

## Extending

1. Prefer a **PSCT fragment** (cost / target / resolution) in `CardTextEffectCompiler.CompilePsctSentence` so every card using that grammar lights up.
2. Add a whole-card regex only for multi-sentence staples the fragments cannot see (Cyber Jar, Swords).
3. Then an `ApplyClause` arm in `TextEffectRuntime`.
4. Optionally extend validator schema enums + AI prompt when adding new action kinds.
5. Optional reference: `~/ygopro-scripts/c{passcode}.lua` for timing intent — still compile from **our** official text string.

## Cache locations

| Kind | Path |
|------|------|
| Runtime learn | `Application.persistentDataPath/WRLDZ/wrldz_compiled_effects_v1.json` |
| Shipped seed | `StreamingAssets/WRLDZ/compiled_effects_seed_v1.json` |

## Remembering card text (the seed)

The shipped seed is how the engine **remembers** compiled card text instead of
recompiling every card at boot. `CompiledEffectCache` skips any seed whose
`version` ≠ `CardTextEffectCompiler.Version`, so **whenever you bump the compiler
`Version` (add/adjust a template), you must re-export the seed** or it is silently
ignored and the game recompiles at runtime.

Re-export (headless, no Editor needed):

```bash
Tools/HeadlessEngine/run.sh --export-seed
```

This compiles every card and writes the programs that carry clauses (the actual
learned knowledge; 0-clause vanilla/gap cards recompile trivially and are skipped)
to the seed, stamped with the current compiler `Version`. In the Editor the same
export is available via **WRLDZ → Text Effects → Export Seed**.
