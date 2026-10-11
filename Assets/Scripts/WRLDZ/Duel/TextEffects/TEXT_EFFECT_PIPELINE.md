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
            → remember in memory (marked dirty)
       └─ persistentDataPath/WRLDZ/wrldz_compiled_effects_v1.json is written by
          CompiledEffectCache.Flush() — at duel end, on focus loss and on quit — never once
          per cache miss mid-duel (compact JSON; SaveDiskNow / ExportSeed stay pretty for tooling)
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
Note (merge 2026-09-24): the seed is regenerated at `CardTextEffectCompiler.Version` **106** with
`Tools/HeadlessEngine/run.sh --export-seed` (the QC DR-15 note about a stale `"version": 1` seed is resolved).

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
Flute · Polymerization (registered recipes) · Ritual Summon (named Greater/Equal, attribute Equal) · Relinquished absorb (OPT equip + ATK copy + battle substitute; damage-reflect leftover absorbed, not resolved) · Enemy Controller (position mode) ·  
FLIP destroy · FLIP Spell from GY · Sangan search · Cyber Jar · Lord of D. ·  
A Legendary Ocean continuous · Abyss Soldier ignition ·  
Cannot be destroyed by battle (this-card continuous) · Premature Burial pay-LP GY-SS equip ·  
Tribute Summoned: destroy target monster (Zaborg / Monarch shape) ·  
After damage calc: return attacker to hand (Wall of Illusion) · End of Damage Step bounce if survived (Hyper Hammerhead) ·  
After damage calc: banish battling monster + this (D.D. Warrior Lady / Assailant) ·  
Tribute this → destroy target (Exiled Force) · Tribute N → inflict damage (Cannon Soldier / Amazoness Archer) ·  
Summon-restriction-only Effect Monsters FullyCompiled structural (Harpie Lady Sisters / Wall Shadow) ·  
FLIP reveal-Set Spell (Armed Ninja) / Trap (Reaper of the Cards) · multi-race Field +200 (Forest / Mountain / Wasteland) ·  
Gravedigger Ghoul GY banish (modern and “remove from play”) · Two-Pronged Attack 2+1 destroy ·  
Dragon Capture Jar Dragon Defense lock · Exodia named hand-win.

**Shared atoms added this pass:**

| Atom | Shape | Cards it lights up |
|------|-------|--------------------|
| Controller name gate | `If you control "X":` → `RequiresFaceUpName` + `RequiresControllerNamedCard` | Thousand Knives · Dark Magic Attack · Sage's Stone |
| `DestroyAllOppSpellTraps` | `Destroy all Spells and Traps your opponent controls.` | Dark Magic Attack |
| `PlaceTargetOnTopOfDeck` | `… ; return/place that target to/on the top of (your) Deck.` | A Feather of the Phoenix · Phoenix Wing Wind Blast · Back to Square One |
| `ControllerGyCards` zone | `target 1 card in your GY` (own GY, not either) | A Feather of the Phoenix |
| `OppAnyCardOnField` target | `target 1 card your opponent controls` | Phoenix Wing Wind Blast |
| `AmountFromTargetOriginalAtk` | battle-destroy LP gain = destroyed monster's printed ATK | Guardian Angel Joan |
| `ScaleAmountByTargetLevel` | battle-destroy LP / damage = destroyed Level × N | Absorbing Kid from the Sky · KA-2 Des Scissors · Needle Burrower |
| Battle-damage draw | `When this card inflicts Battle Damage …, draw N` | Masked Sorcerer |

`NotifyDestroyedOpponentByBattle` now carries the destroyed monster, but hands it to a
clause **only** when that clause measures off it — every other battle-destroy clause
still resolves with a null target.

**Shared atoms added in the second pass:**

| Atom | Shape | Cards it lights up |
|------|-------|--------------------|
| Attribute/race field aura with a DEF penalty | `All FIRE monsters gain 500 ATK and lose 400 DEF.` | Molten Destruction · Rising Air Current · Gaia Power · Luminous Spark |
| Ritual searcher | `When this card is Normal or Flip Summoned: You can add 1 Ritual Monster/Spell from your Deck to your hand.` | Senju · Sonic Bird · Manju |
| `StartOfDamageStep` + `Destroy` | `At the start of the Damage Step, if this card attack(s\|ed) a face-up/down Defense Position monster: Destroy that monster.` | Sasuke Samurai · Ninja Grandmaster Sasuke · Mystic Swordsman LV2/LV4 · Paladin of White Dragon |

`StartOfDamageStep` fires in `DuelEngine.ResolveDeclaredAttack` **before** the face-down
flip and before damage calculation, matching the official ruling: the target is destroyed
without being flipped, so no Flip effect applies and no battle damage is dealt. It destroys
*by effect*, so it does not satisfy "destroyed a monster by battle" riders (the Mystic
Swordsman LV evolution lines). Mystic Swordsman LV6 keeps its leftover on purpose — its
"and if you do, place it on top of the Deck" rider is not implemented.

`PsctGrammar.SuggestTiming` fixes: `start of the damage step` is now checked before the
generic `damage step`, and `When this card is Normal or Flip Summoned` no longer falls into
the opponent's-summon (Trap Hole) window — only `your opponent Normal or Flip Summons` does.

**Shared atoms added in the third pass (v96):**

| Atom | Shape | Cards it lights up |
|------|-------|--------------------|
| Self-aura per hand card — `ScaleThisAtkByMatchingCount` + `ScaleCountHandCards` | `This card gains N ATK (and DEF) for each card in your hand.` / `Decrease the ATK and DEF of this card by N points for each card in your hand.` | Muka Muka · Enraged Muka Muka · Flash Assailant |
| Self-aura per GY monster — `ScaleCountGraveyardOnly` (+ `RaceFilter`) | `This card gains N ATK for each (Race) monster in your Graveyard.` | Shadow Ghoul · Mudora |
| Opponent draws — `Draw` with `Side = Opponent` (runtime arm now honors Side) | `When this card inflicts Battle Damage to your opponent, your opponent draws N cards.` | The Bistro Butcher |
| Ritual procedure-only monsters are **structural** | `This monster can only be Ritual Summoned with the Ritual Spell Card, "X".` + `You must also offer/Tribute monsters whose total Level Stars equal N or more …` (and the modern `You can Ritual Summon this card with "X".`) | Crab Turtle · Skull Guardian · Hungry Burger · Performance of Sword · Dokurorider · Black Luster Soldier · Fortress Whale · The Masked Beast |

The Ritual rule mirrors Harpie Lady Sisters ("Must first be Special Summoned with X"): the
NS/Set lock is `IsRitualMonster`, and the procedure itself is the paired Ritual Spell's compiled
`RitualSummon` clause (`RitualProcedures.TryGetSpec`). The fail-closed half lives in
`InteractionRegressionTests`: every zero-clause FullyCompiled Ritual Monster must have a Ritual
Spell in `cards_db` that compiles to `RitualSummon` naming it. Ritual Effect Monsters whose other
sentences do not compile (Legendary Flame Lord, Shinato, Reshef, Doriado) stay leftover.

**Sentence atoms added in the fourth pass (v101, `AtomRegistration`):**

| Atom | Shape | Cards it lights up |
|------|-------|--------------------|
| Tribute-count cost → LP / damage | `Tribute 2 monsters on your side of the field to increase your Life Points by N points.` · `By Tributing 1 (Race)-Type monster … inflict N points of damage …` · `By Tributing 1 Normal Monster (except a Token) …` (`TributeNormalMonsterOnly` + `TributeExceptTokens`) | Hysteric Fairy · Anti-Aircraft Flower · Man-Thro' Tro' |
| Tribute-count cost → targeted destroy, `RequiresTargetDefLeqSourceAtk` | `Tribute 1 Warrior-Type monster … to destroy 1 face-up monster on the field whose DEF is equal to or less than the ATK of this card.` | Throwstone Unit |
| `CountsAsTwoTributesForAttribute` (ContinuousWhileFaceUp, never activated) | `This card can be treated as 2 Tributes for the Tribute Summon of a DARK monster.` · `When Tribute Summoning a WATER monster, you can treat this 1 monster as 2 Tributes.` | Double Coston · Flame Ruler · Unshaven Angler · Whirlwind Prodigy · The Trojan Horse |
| `ReturnToHand` on `FieldSpellTraps`, `Side = Both` | `Return all Spell and Trap Cards on the field to the hand.` | Giant Trunade |
| `SkipOpponentNextDrawPhase` as an activation | `Skip the Draw Phase of your opponent's next turn.` | Time Seal |
| DEF-only until-End-Phase tweak (`Amount = 0`, `DefAmount = N`; the `LoseAtkDefUntilEndOfTurn` arm now has a DEF-only path) | `Increase 1 face-up monster's DEF by N points until the end of this turn.` · `Decrease 1 selected monster's DEF by N points during the turn this card is activated.` | The Reliable Guardian · Snake Fang |
| Mass position toggle | `Change the battle positions of all face-up monsters on the field.` | Zero Gravity |
| `Draw` on `ThisCardSummoned` gated by `RequiresThisSpecialSummoned` / `RequiresThisSpecialSummonedFromGy` | `If this card is Special Summoned: Draw 1 card.` · `When this card is Special Summoned from the Graveyard: Draw 1 card.` | Sacred Crane · Molten Zombie |
| `ContinuousCannotAttack` + `RequiresSummonedOrFlippedThisTurn` (Spell-borne `Side = Both`, monster-borne `Side = Opponent`) | `Monsters cannot attack the turn they are Summoned.` · `Monsters your opponent controls cannot attack the turn they are Summoned.` | Vengeful Bog Spirit · Big-Tusked Mammoth |

Engine hooks for this pass: `TextEffectRuntime.TributeValueFor` / `TributesAvailableFor` feed
`SummonProcedures.CheckNormalOrTribute` and `DuelEngine.ResolveTributes`, so a lone Double Coston
(face-up or Set) satisfies a DARK Level 7 while a LIGHT Level 8 still needs two. Graveyard Special
Summons (compiled `SpecialSummonFromGy`, the recruiter path and legacy Monster Reborn) set
`DuelEngine.NextSpecialSummonFromGraveyard` right before `SpecialSummonToField`, which passes the
origin to `TryResolveThisCardSummoned`; a hand or Deck Special Summon never fires Molten Zombie.
`ContinuousCannotAttackBlocks` now also scans face-up monsters, but only for the summoned-this-turn
shape — other monster attack locks keep their existing paths. `CollectAllMatching` gained the
`OppFaceUpMonsters` zone (Lightning Vortex's mass destroy resolved to nothing before).

**Sentence atoms added in the fifth pass (v102, `AtomRegistration`):**

| Atom | Shape | Cards it lights up |
|------|-------|--------------------|
| `GainLpWhenTributedForSummon` / `DrawWhenTributeSummonedWithTwoPlus` (Tribute Summon riders, fired from `TryNormalSummonToZone`) | `When a monster is Tribute Summoned by Tributing this card: The owner of this card gains N Life Points.` · `When you Tribute Summon a monster using 2 or more Tributes: Draw N cards.` | Zolga · Precious Cards from Beyond |
| Typed Tribute → typed hand Special Summon | `Tribute 1 Reptile monster; Special Summon 1 Dinosaur monster from your hand.` | Ultra Evolution Pill |
| `RemoveAllSpellCounters` | `Remove all Spell Counters on both sides of the field.` | Exhausting Spell |
| `OpponentShufflesHandThenDraws` (+ `RequiresOpponentHandAtLeast` activation gate) | `Your opponent shuffles their entire hand into the Deck, then draws the same number of cards.` · `If your opponent has 8 or more cards in their hand: … then draw 2 cards.` | Disturbance Strategy · Heavy Slump |
| `SetDefToZero` / `ScaleAmountByOpponentHandCards` / `RequiresTargetFaceUp` | `… that target's DEF becomes 0 until the end of this turn.` · `… it gains 200 ATK/DEF for each card your opponent currently has in their hand …` | Micro Ray · Energy Drain |
| `ReorderTopOfOwnDeck` as an activation | `Look at the top 3 cards of your Deck and/then return them to the top of the Deck in any order.` | Fruits of Kozaky's Studies · Spellbook Organization |
| `MillToDeclareAttack` (paid in `PayAttackDeclarationCost`) | `Your opponent must send 1 card from the top of their Deck to the Graveyard to declare an attack.` | Gravekeeper's Servant |
| `ContinuousGainAtkDef` + `RequiresBattlePhase` + `ControllerTurnOnly` | `All monsters you control gain 200 ATK during your Battle Phase only.` | Banner of Courage |
| `SkipControllerNextStandbyPhase` (`DuelistState.SkipNextStandbyPhase`) | `Skip your next Standby Phase.` | Solomon's Lawbook |
| `InflictDamageWhenControllerGainsLp` (hook in `ApplyLifePointGain`) / `GainLpWhenControllerDraws` (hook in `DuelEngine.Draw`, once per draw event) | `Each time you gain Life Points, inflict 500 damage to your opponent.` · `Increase your Life Points by 500 points each time you draw a card (or cards).` | Fire Princess · Solemn Wishes |
| `ApplyLingeringAtkDef` on any Summon | `If this card is Summoned: Target 1 face-up monster on the field; it loses 800 ATK.` | Dark Jeroid |
| Tribute-Summon trigger with "up to 2" targets (`ThisCardSummoned` pending now carries `TargetPicksRemaining`) | `When this card is Tribute Summoned: You can target up to 2 Spell/Trap Cards on the field; destroy those targets.` | Mobius the Frost Monarch |
| `PreventControllerBattleDamage` + `RequiresControllerHasMonster` / monster-borne `OnlyBattlesInvolvingThis` | `While you control a monster, you take no battle damage.` · `You take no battle damage from attacks involving this card.` | Spirit Barrier · Amazoness Fighter |

Side fixes in this pass: `ApplyCatalogSpellTraps` no longer stacks the Lua catalog aura on a
Spell/Trap whose compiled program already owns a `ContinuousGainAtkDef` clause (Banner of Courage
was +400), and `SpellTrapEffects.MapCompiledTargetKind` hands a `FieldAnyMonster` target with
`RequiresTargetFaceUp` to the text path, because the legacy `AnyMonsterOnField` list includes Sets.
A lone legal target resolves without a prompt (Micro Ray with one face-up monster).

**Batch atoms added in the sixth pass (v141, card batch `v141-mrd-srl-psv`, `Batches/<Group>Atoms.cs`, one `BatchAtomSets` line each):**

Run with the playbook `Tools/CardCompile/README.md` and the workflow `.claude/workflows/compile-card-batch.js`: each group was
built in its own engine copy, reviewed for rules and engine safety, fixed, then integrated one at a time. Report:
`Docs/CURRICULUM_BATCH_v141-mrd-srl-psv.md`. 26 cards Implemented (554 → 580), Wall Shadow Structural, Armored Glass deferred.

| Atom / group | Shape | Cards it lights up |
|--------------|-------|--------------------|
| PerCountBurns: `InflictDamageToOpponent` + `CountSource` (`OppSpellTrapCards` / `OppBanishedCards` / `OppGyCards` / `PreviousClauseHits` / `ThisCardTurnCounter`), counted at resolution; multi-clause `Atom.Sequence` | `FLIP: Inflict 500 damage to your opponent for each Spell and Trap Card on your opponent's side of the field.` · `Inflict 300 damage to your opponent for each of their removed from play cards.` · `Destroy all monsters you control, and if you do, inflict 300 damage … for each monster destroyed.` · `During your Main Phase: You can send this face-up card to the GY; inflict 1000 damage … for each of your Standby Phases that have passed since this card was activated.` (`EffectTiming.FaceUpFieldIgnition`, a Spell Speed 1 Chain Link) | Princess of Tsurugi · D.D. Dynamite · Cemetary Bomb · Thunder Crash · Wave-Motion Cannon |
| SummonNegation: `MonsterWouldBeSummoned` / `NegateSummon` (new `ResponseTiming.SummonNegation` window before a Normal / Tribute / Flip / procedure Special Summon succeeds) + `ChainLinkActivated` / `NegateActivation` with `NegateCardActivationOnly`, `NegateOpponentActivationOnly`, `PayHalfLpCost`, `OpponentMayDiscardSpellToNegateThis` | `When a monster(s) would be Summoned, OR a Spell/Trap Card is activated: Pay half your LP; negate the Summon or activation, and if you do, destroy that card.` · `When a monster(s) would be Summoned: Tribute 1 monster; negate the Summon …` · `When your opponent activates a Spell Card: They can discard 1 Spell Card to negate this card's effect, otherwise …` | Solemn Judgment · Horn of Heaven · Magic Drain |
| ContinuousNegation: `ContinuousCannotActivateTraps` / `ContinuousNegateTrapEffectsOnField` (and the Spell pair), `ExceptThisCard`, `EachStandbyPhase` upkeep, named-host Equip that spares its controller's Traps | `Trap Cards, and their effects on the field, cannot be activated. Negate all Trap effects on the field.` (and the Spell version) · `Negate all other Trap effects on the field.` · `Negate all Spell effects on the field.` + the 700 LP Standby upkeep · Amplifier's whole text (one `EquipThisToTarget` clause) | Jinzo · Spell Canceller · Royal Decree · Imperial Order · Amplifier |
| TurnNegationTraps: `NegateCardFamilyOnFieldThisTurn` + `ActivationCondition = ChainLinkActivatesCardFamily` | `You can only activate this card when a Continuous Spell / Continuous Trap / Field Spell Card is activated. Negate all Continuous Spell Cards (… the Field Spell Card) during the turn this card is activated.` | Mystic Probe · Metal Detector · World Suppression |
| TakeControl: `TakeControlUntilEndPhase` + `TargetMustBeNormalSummonable`; `TakeControlWhileSourceFaceUp` (Attribute filter, bound control that returns to the covered controller); `SwitchControlWithThis` + `CannotActivateInBattlePhase` | `Pay 800 LP, then target 1 face-up monster your opponent controls that can be Normal Summoned/Set; take control of that target until the End Phase.` · `FLIP: Target 1 face-up WATER monster your opponent controls; take control of that target while this card is face-up on the field.` (and the older `FLIP: While this card is face-up on the field, take control of 1 EARTH monster …`) · `FLIP: Select 1 monster on your opponent's side of the field and switch control of it with this card. This effect cannot be activated during the Battle Phase.` | Brain Control · Eria / Hiita / Aussa / Wynn the Charmers · Invader of the Throne |
| TributeSummons: `SpecialSummonFromHand` / `SpecialSummonFromDeck` with `RequiresExactLevel`, `RequiresCanBeNormalSummonedOrSet`, `SummonCannotAttackThisTurn`, `MatchTributedLevel` + `TributedLevelOffset`, `RequiresTributeEquippedMonster`; `TributeTargetAsIfControlledThisTurn` + `ForbidsBattlePhaseThisTurn` (an oath, applied before any negation) | `Tribute 1 monster; Special Summon 1 Level 7 monster from your hand that can be Normal Summoned/Set. It cannot attack this turn.` · `Tribute 1 monster. Special Summon 1 Insect-Type monster from your Deck whose Level is 1 higher than the Tributed monster's.` · `Equip only to "Labyrinth Wall". You can Tribute the equipped monster; Special Summon "Wall Shadow" from your Deck.` · `Target 1 monster your opponent controls; this turn, if you Tribute a monster, you must Tribute that target, as if you controlled it. …` | Tribute Doll · Insect Imitation · Magical Labyrinth · Soul Exchange (Wall Shadow Structural) |

Engine hooks for this pass (details in the batch report): a summon-negation window in `DuelEngine` whose success steps
run in `CompleteNormalSummon` / `CompleteFlipSummon` / `CompleteSpecialSummon`, with the Tributes' field-to-GY triggers
held until the Summon ends; `CardInstance.IsNegated` became a property (stored flag, this turn's family negation, or
an activation still unresolved on the chain); Field Spell activations open a chain window; Continuous Traps with only
continuous clauses (Royal Decree, Imperial Order, Gravity Bind …) can be chained; `FieldSpellEffects.RefreshBoard` runs
`TurnNegationTrapsRuntime.Refresh`, `TakeControlRuntime.SweepBoundControl` and `ContinuousNegationRuntime.Refresh` before
`ClearModifiers`; upkeep costs (`IsUpkeepCost`) still fire for a negated card. Cross-group fixes found by the audit
(`Rules/Batches/V141CrossGroupRegressionTests.cs`): no stale Chain Link after an attack-window Trap negated by Royal
Decree; Soul Exchange's oath still applies under Imperial Order; Imperial Order chained to Wave-Motion Cannon's link
negates the damage; a negated Magical Labyrinth cannot use its Tribute ignition.

**Batch atoms added in the seventh pass (v142, card batch `v142-mrd-srl-psv-b`, `Batches/<Group>Atoms.cs`, one `BatchAtomSets` line each):**

Part 2 of the MRD / SRL / PSV run: the 12 groups held back from v141, with the same playbook and workflow. Report:
`Docs/CURRICULUM_BATCH_v142-mrd-srl-psv-b.md`. 36 cards Implemented (580 → 616) from 10 groups. Toons (4 cards) is parked as a
reviewed patch until it lands after the UI's hand Special Summon action. RevealHands (3) and 11 other cards are deferred, fail closed.

| Atom / group | Shape | Cards it lights up |
|--------------|-------|--------------------|
| DiscardTriggers: new timings `DiscardedFromHandByOpponentEffect` / `SentFromDeckToGyByOpponentEffect` (opt-in `DiscardTriggersRuntime.NoteDiscard` / `NoteSentFromDeck` on every effect discard and mill path, fired after the moving effect and the chain); `ShuffleGraveyardIntoDeck`; `OpponentDiscardsRandomThenOneOfTheirChoice` (the victim picks the second discard) | `When this card is discarded from your hand to the Graveyard by an effect of a card controlled by your opponent, draw 2 cards …` · `When this card is sent directly from your Deck to the Graveyard by an effect … unite your Graveyard cards and your own Deck …` · `Pay 1000 LP; your opponent discards 1 random card, and if they have any other cards in their hand, discard 1 more card of their choice.` | Electric Snake · Minar · Penguin Knight · Delinquent Duo |
| BattlePhaseEnd: new timings `EndOfBattlePhase` / `BeforeDamageCalculation`; `ChangeThisToDefenseAfterAttackingLockPosition`; `DestroyAttackerAtOpponentsLaterEndPhase` (+ `ExcludeRaceFilter`); `InflictDamageEqualToAtk` + `DestroyThisAfterDamageCalculation` | `If this card attacks, it is changed to Defense Position at the end of the Battle Phase, and its battle position cannot be changed until the End Phase of your next turn.` (and Giant Orc's two-sentence print) · `A non Machine-Type Monster attacking "Steel Scorpion" will be destroyed at the End Phase of your opponent's 2nd turn after the attack.` · Reflect Bounder's whole text | Goblin Attack Force · Giant Orc · Steel Scorpion · Reflect Bounder |
| SpellTrapDestroyedResponse: Activate `Destroy` with `ActivationCondition = YourCardsDestroyedByOpponentEffect` (new `ResponseTiming.SpellTrapDestroyedByOpponent` window, missed timing fails closed); FLIP `Destroy` named face-up Spell + `ConductBattlePhaseTwice` (`RequiresPreviousClauseHit`) | `You can activate this card when 1 or more of your Spell (Trap) Cards are destroyed and sent from the field to the Graveyard by a card effect your opponent controls. Destroy 1 Spell or Trap Card on the field.` · Weather Report's whole text (`Atom.Sequence`) | Gust · Driving Snow · Weather Report |
| TypeDeclaration: `DeclareMonsterType` + `AllFaceUpMonstersBecomeDeclaredType` (`CardInstance.Race` is now live); `ContinuousCannotAttack` + `RaceFilterIsDeclared`; `StandbyTributeOrDestroyThis`; `DestroyAllFaceUpOfAttributeOpponentPicksFromTwo` (`FaceUpMonstersShowTwoAttributes`); zone `DeclaredMonsterTypes` | `Activate this card by declaring 1 Monster Type. All face-up monsters on the field become that Type.` · The Regulation of Tribe's whole text · `Select 2 Monster Card Attributes. Your opponent then selects 1 of the 2 Attributes and destroys all face-up monsters with that Attribute on the field.` | DNA Surgery · The Regulation of Tribe · Earthshaker |
| LocksAndZones: `LockSpellTrapPlayAndSetUntilYourNextTurn` (`ActivationCondition = StartOfMainPhase1`, a game-state fingerprint); `BanishTargetUntilYourNextStandbyPhase` (`FieldZone.UnusableFor`, `DuelEngine.PlaceReturnedMonster`); `MonstersCannotBeSetOrTurnedFaceDown` | `This card can only be activated at the start of Main Phase 1. Until your next turn, you and your opponent cannot play or Set any Spell or Trap Cards.` · Dimensionhole's whole text · `While this card is on the field, Monster Cards cannot be Set or flipped face-down. …` | Cold Wave · Dimensionhole · Light of Intervention |
| HandDisruption: `CoinCallDrawUntilOrSkipNextTurn` (`HandSizesOppAtLeastYoursAtMost`, `DuelistState.SkipNextTurn`); `DrawWhenOpponentDrawsOutsideDrawPhase` (new `ResponseTiming.OpponentDrewOutsideDrawPhase`, when-timing checked against a post-draw snapshot); `RevealDeckCardsOpponentPicksOneToHand` | Gamble's whole text · `Activate only when your opponent draws a card(s) outside of either Draw Phase. After that, each time …, immediately draw 2 cards.` · `Select 5 cards from your Deck and show them to your opponent. Your opponent selects 1 card among them. …` | Gamble · Appropriate · Painful Choice |
| FlipMassEffects: `CyberJarStyle` from one whole-text atom (+ `SummonLevelMax`, `SummonFaceUpAttackOrFaceDownDefense`; the hardcoded `MonsterEffects.ResolveCyberJar` is gone); `ShuffleFieldMonstersIntoDeckExcavateSummon`; `RecordThisCardFlipped` + `TributeThisDestroyYourMonstersBurnHalfAtk`; `RecordThisCardFlipped` + `EachPlayerSpecialSummonsOneFromOwnGy`; mass sends inside `FlipMassEffectsRuntime.Simultaneously` | The four FLIP texts, each one whole-text atom or `Atom.Sequence` | Cyber Jar · Morphing Jar #2 · Jigen Bakudan · Spear Cretin |
| RedirectAndReplace: `RedirectToAnotherAppropriateTarget` (`OpponentSpellTargetsOneMonster` / `OpponentTargetsOneMonsterYouControl`, zone `AnotherAppropriateTarget`, attack retarget via `DuelEngine.ChangeDeclaredAttackTarget`); `DestroyNewFaceUpMonstersSharingAName`; `BanishCardsSentToGyInstead` (every GY send asks `RedirectAndReplaceRuntime.SendsToBanishedInstead`) | `When your opponent activates a Spell Card that targets exactly 1 monster …: Target another card that would be an appropriate target; …` · Shift's whole text · Kotodama's whole text · `Any card sent to the Graveyard is banished instead.` | Fairy's Hand Mirror · Shift · Kotodama · Banisher of the Light |
| StandbyGrowthAndMaintenance: `GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp` / `GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby` (`StandbyGrowthAmount`, `StandbyGrowthSteps`); `HalveAtkOfOpponentAttackersWhileThisOnField` (`CardInstance.AtkHalvings`); `InflictDamageToOwnerPerCardSentToGy` (GY checkpoints); `UpkeepIsOptional` makes "pay N LP or destroy this card" a real choice | Castle of Dark Illusions' and Pumpking's whole texts · Mirror Wall's first sentence + the existing `PayLpOrDestroyThis` · `Each time a card(s) is sent to the Graveyard, inflict 300 damage to its owner for each card sent.` | Castle of Dark Illusions · Pumpking the King of Ghosts · Mirror Wall · Skull Invitation |
| SpiritsAndMisc: `SkipOpponentNextDrawPhase` on `ThisCardInflictsBattleDamage`; `DiscardOpponentHandAtNextDrawPhase`; `DrawUntilHandSize`; `BecomeEquipOnFaceDownAttacker` + `GainLpHalfEquippedMonsterAtk`; `RxSpiritBounce` accepts "the turn it is Normal Summoned or flipped face-up" | The three Spirit texts (structural Spirit lock and return + one atom each) · Kiseitai's whole text (`Atom.Sequence`) | Yata-Garasu · Hino-Kagu-Tsuchi · Yamata Dragon · Kiseitai |

Engine hooks for this pass (details in the batch report): the Spirit End Phase return follows `proc_spirit.lua` (every End
Phase, `CardInstance.FlippedFaceUpThisTurn`); Swords of Revealing Light queues the FLIP effects of the monsters it flips
(`DuelEngine.QueueFlipEffectAfterResolution`); `ChainStack.ActivationCount` feeds Cold Wave's start-of-phase check;
`FieldZone.IsEmpty` now means "no card and usable"; `DuelEngine.BeginTurn` holds the Regulation of Tribe Tribute pick,
then offers each optional upkeep (`ResumeStandbyAfterUpkeep` → `ContinueStandbyAfterUpkeepChoices`). Cross-group fixes
found by the audit (`Rules/Batches/CrossGroupV142RegressionTests.cs`): Steel Scorpion and Castle of Dark Illusions read
the current Type (`CardInstance.Race`), so DNA Surgery counts; Mirror Wall's optional upkeep is no longer auto-paid while
The Regulation of Tribe's pick is open; Earthshaker's mass destroy is one simultaneous event (Spear Cretin's trigger waits).

> ### ⚠ `EffectActionKind` / `EffectTiming` / `EffectZoneFilter` are append-only
> Clause `Action`, `Timing` and `Zone` are serialized as **integers** in
> `compiled_effects_seed_v1.json` and in `persistentDataPath/WRLDZ/wrldz_compiled_effects_v1.json`.
> Inserting a member mid-enum silently re-maps every cached clause after it.
> **Always append new members at the end, and bump `CardTextEffectCompiler.Version`**
> whenever compiler output changes — the cache is keyed on `TextHash + CompilerVersion`,
> so without a bump a stale program is served and new templates never take effect.

**Deliberately still a gap:** `This card can only be placed on the field by the effect of "X"`
(Spirit Message / Sarcophagus). Marking that structural would report cards that cannot be
activated at all as covered — the Continuous S/T activation stress test rejects it.

**PARK (UniqueException / optional ● / unique):** Amazoness Swords Woman · Kunai with Chain · Magical Arm Shield · Blast Sphere · Time Wizard (compiled UniqueException) · Metalmorph.

**Implemented unique / chain-negate (this pass):** Seven Tools · Magic Jammer · Magical Hats · Multiply · Crush Card Virus · Cocoon of Evolution / Larvae / Great / Perfectly Ultimate Great Moth.

AI may map additional cards **only** onto the same `EffectActionKind` / `EffectTiming` enums (no free-form invent).

## Pass v159 (card batch v159-lon-b)

Compiler v159; 26 cards Implemented (607 to 633), 4 Spirit Message cards Structural, 0 losses. Report `Docs/CURRICULUM_BATCH_v159-lon-b.md`.
Groups: FlipCostsAndFusion (Fire Sorcerer, Lady Assailant of Flames, Supply, Summoner of Illusions, De-Fusion), EquipFamily (Dragonic Attack, Scroll of Bewitchment, Collected Power, Dark Necrofear), BattleWindows (Fairy Box, Jam Defender, Destruction Punch, Michizure, Dreamsprite, Dark Spirit of the Silent), RuleLocks (Jowgen, Jam Breeding Machine, Royal Command, Kycoo, Chain Energy), DestinyBoard (Destiny Board, Spirit Message I/N/A/L), NegationsAndGySummons (Maryokutai, Riryoku Field, Gilasaurus, Card of Safe Return, Chosen One).
Runtimes live in `TextEffects/Batches/*Runtime.cs`; tests in `Rules/Batches/`. Deferred: Tailor of the Fickle, Armored Glass, Type Zero Magic Crusher, Skull Lair.

## Pass v160 (card batch v160-lod)

Compiler v160; 27 cards Implemented (633 to 660), Berserk Dragon Unimplemented to Stub, 0 losses. Report `Docs/CURRICULUM_BATCH_v160-lod.md`.
Groups: FaceUpQuickEffects (Type Zero Magic Crusher, Skull Lair, Soul Demolition, The Dragon's Bead), EquipModel (Armored Glass, Tailor of the Fickle), SimultaneousTriggers (Curse of Fiend), EngineHygiene (Spirit of Flames, Inferno, Ekibyo Drakmord, Forced Requisition, Super Rejuvenation), SimpleSpellTraps (A Wingbeat of Giant Dragon, Burst Breath, Dragon's Gunfire, Ready for Intercepting, Fiend Comedian, Emergency Provisions, The A. Forces), SpiritMonsters (Asura Priest, Inaba White Rabbit, Fushi No Tori, Great Long Nose, Susa Soldier, Otohime, Maharaghi).
Runtimes live in `TextEffects/Batches/*Runtime.cs`; tests in `Rules/Batches/`. Deferred: Royal Oppression, Sword Hunter.

## Pass v161 (card batch v161-lod-b)

Compiler v161; 43 cards Implemented (660 to 703), Tyrant Dragon Unimplemented to Stub, 0 losses. Report `Docs/CURRICULUM_BATCH_v161-lod-b.md`.
Groups: BothSideAndMonsterEquip (Sword Hunter), CoinDiceInterrupts (Second Coin Toss, Dice Re-Roll), SpecialSummonEffects (Skull Knight #2, Twin-Headed Behemoth, Winged Minion, The Puppet Magic of Dark Ruler, Gradius' Option), ContinuousTrapsAndSpiritSupport (Fatal Abacus, Life Absorbing Machine, Ominous Fortunetelling, Fengsheng Mirror, Spirit's Invitation, Spiritual Energy Settle Machine), EquipAndSpellProtection (Fusion Sword Murasame Blade, Heart of Clear Water, Smoke Grenade of the Thief, Blast with Chain, Woodland Sprite, Magic Reflector), DeckReturnsAndMassEffects (Fiber Jar, Mysterious Guard, Serpentine Princess, Creature Swap, Bubble Crash), BattleRules (Cave Dragon, Thunder Nyan Nyan, Array of Revealing Light, After the Struggle, The Secret of the Bandit, Patrician of Darkness), BattleBoosts (Spear Dragon, Dragon's Rage, Gray Wing, The Hunter with 7 Weapons, Injection Fairy Lily, Spirit Ryu), NegationMonsters (Freed the Matchless General, Frontier Wiseman, Dark Ruler Ha Des, Twin-Headed Wolf, Lesser Fiend, Double Snare).
Runtimes live in `TextEffects/Batches/*Runtime.cs`; tests in `Rules/Batches/`. Deferred: Royal Oppression, Fusion Gate (both-side activation), Tyrant Dragon (GY Special Summon Tribute cost).

## Pass v162 (card batch v162-pgd)

Compiler v162; 44 cards Implemented (703 to 748 with the second Forgiving Maiden id), Fushioh Richie Unimplemented to Stub, 0 losses. Report `Docs/CURRICULUM_BATCH_v162-pgd.md`.
Groups: EffectDamageEvents (Barrel Behind the Door, Trap of Board Eraser, Dark Room of Nightmare, Gravekeeper's Vassal, Coffin Seller), PgdSpellTrapBasics (Book of Taiyou, Curse of Aging, D. Tribe, Pyramid Energy, Secret Pass to the Treasures, Needle Ceiling, Hieroglyph Lithograph, Card Shuffle), BattleOutcomeTriggers (Zombyra the Dark, Timeater, Kryuel, Mystical Knight of Jackal, Winged Sage Falcos, Dark Scorpion Burglars, Don Zaloog, Mucus Yolk), FlipDownAndPeek (8-Claws Scorpion, Royal Keeper, Wandering Mummy, Giant Axe Mummy, Cobraman Sakuzy, A Man with Wdjat, Trap Dustshoot), StandbyBurnsAndDice (Blind Destruction, Needle Wall, Graverobber's Retribution, Dark Snake Syndrome, Mask of Dispel, Nightmare Wheel, The Unfriendly Amazon, Inspection), BattleDestroyedGyRecovery (Crimson Sentry, Lady Panther, The Forgiving Maiden, Fairy Guardian, Return of the Doomed, Revival Jam, Helpoemer, Rope of Life).
Runtimes live in `TextEffects/Batches/*Runtime.cs`; tests in `Rules/Batches/`. Skipped: see the batch doc (revealed hand, per-copy Deck, name picker, both-side activation, others).

## Pass v163 (card batch v163-pgd-mfc)

Compiler v163; 41 cards Implemented (748 to 789), 9 Stubs resolved, 0 losses. Report `Docs/CURRICULUM_BATCH_v163-pgd-mfc.md`.
Card groups: ContinuousLocksAndIgnitions (Arsenal Bug, Buster Rancher, Call of the Mummy, Frontline Base, Gora Turtle, Non Aggression Area, Timidity), PhaseTimersAndDeckTop (A Cat of Ill Omen, An Owl of Luck, Bottomless Shifting Sand, Different Dimension Capsule, Mirage of Nightmare, Pyro Clock of Destiny, Royal Tribute), CallsDiceAndSetTriggers (Cobra Jar, Dark Coffin, Dice Jar, Ordeal of a Traveler, Reasoning, Reversal Quiz, Statue of the Wicked), TargetNegationsAndHandQuickEffects (Charm of Shabti, Curse of Royal, Fushioh Richie, Gravekeeper's Watcher, Great Dezard), SummonTriggersAndTributes (Byser Shock, Dark Dust Spirit, Gravekeeper's Chief, Jowls of Dark Demise, King Tiger Wanghu, Metamorphosis, Moisture Creature), SummonPromptsAndCosts (Tyrant Dragon, Vampire Baby), ForcedActivationAndSubstitution (Major Riot, Spirit Elimination).
Engine-only groups (0 cards each, landed after the card groups): EngineResiduals (generic human card pick `CardPickRequest` + `ContinuousTrapsAndSpiritSupportRuntime.AskCardPick`, with a Cancel answer and a human Chain-answer discard cost), LastTurn (DRAW duel result, End Phase lone-monster check), TriggerChainResponses (trigger sites seat a real `engine.Chain` link and open a response window), PerCopyDeck (`Duel/DeckPile.cs`: `DuelistState.Deck` becomes a `DeckPile` keeping the `List<int>` surface, plus per-copy ownership / face-up state via `DeckCopyInfo`).
Seed 853 to 885 programs (added 32, removed 0, changed 11). Runtimes live in `TextEffects/Batches/*Runtime.cs`; tests in `Rules/Batches/`. Deferred: Narrow Pass, Bait Doll (forced Set-Trap activation mid-resolution) — see the batch doc and the narrowed `Tools/CardCompile/ENGINE_MAP.md` rows.

## Pass v167 (Union equip / unequip)

Compiler v167; Union procedure is now playable (monster moves into a Spell/Trap Zone). 0 Implemented-count bump — this is a shell for cards whose sentences already compiled. Report `Docs/CURRICULUM_BATCH_v167-union.md`.
Seed 887 → 895 programs (added 8 including Z-Metal Tank; removed 0). Tests `Rules/Batches/UnionEquipRegressionTests.cs`. Duel checks: S/T-zone landing, one Union per host, once-per-turn, unequip SS, battle substitute, Z-Metal alt host. Arena: generic SpellCast / SpecialSummon; gap in `FX_MAP.md`.

## Pass v176 (card batch v176-dcr-ioc)

Compiler v176; 59 cards Implemented (DCR and IOC), 0 losses; suite 6536/0. Report `Docs/CURRICULUM_BATCH_v176-dcr-ioc.md`.
Groups: SimpleTriggersAndSwarm, DcrSpellTrapUtility, BattleDestroyedTriggers, GuardiansAndEquipEvents, DerivedAtkMonsters, NamedSummonPairs, ArchfiendRiders, SpellTrapAnswersAndActivationLocks, MonsterNegationAndControlLocks. Deferred: Goblin of Greed (engine-wide "cannot discard as a cost").

## Pass v175 (card batch v175-mfc-dcr)

Compiler v175; 57 cards Implemented (MFC 47, DCR 10), 0 losses; suite 6211/0. Report `Docs/CURRICULUM_BATCH_v175-mfc-dcr.md`.
Groups: DamageStepRules, EquipLocksAndLeaveField, DcrEquipSpells, SpellCounterSpenders, ChainLinkAnswers, FlipMassAndOpponentPicks, PhaseTriggersAndIgnitionCosts, UnionRiders, SummonLocksAndStatRules (each `Batches/<Group>Atoms.cs` + `Runtime.cs`, tests `Rules/Batches/<Group>RegressionTests.cs`). Deferred: Neko Mane King (phase jump), Formation Union (second target), Kaiser Colosseum (EFFECT_MAX_MZONE).

## Pass v174 (card batch v174-mfc)

Compiler v174; 2 cards Implemented (MFC 36 → 38 of 108), 0 losses. Report `Docs/CURRICULUM_BATCH_v174-mfc.md`.
Group: MfcSimpleSpellsB (Emblem of Dragon Destroyer, Hidden Spellbook). New kinds: AddNamedFromDeckOrGyToHand (+ ControllerDeckOrGyNamed zone), ShuffleGySpellsIntoDeck, ForbidBattlePhaseThisTurn (reserved). Tests `Rules/Batches/MfcSimpleSpellsBRegressionTests.cs` (7 checks) + corpus named-card Provision. Deferred: Thunder of Ruler stays fail-closed (no activation path for a Set Normal Trap on pure StandbyPhase timing); Mass Driver, Anti-Spell (earlier gates). Arena: generic looks; gaps recorded in the batch report.

## Two template lineages (v106 merge)

The local v100–v102 compiler line and the GitHub v51–v55 card batches (PRs #47–#50) were merged at
`CardTextEffectCompiler.Version` **106**. The enums are the **union** of both lines (renumbered, hence
the version bump that invalidates every int-serialized seed / disk program). Same-name members that
both lines added independently (`RitualSummon`, `SkipControllerNextStandbyPhase`,
`SwapOriginalAtkDefUntilEndOfTurn`, `TakeControlUntilEndPhase`, `ThisCardDeclaresAttack`,
`ThisCardInflictsBattleDamage`, `DeckRitualMonsters`, `DeckRitualSpells`, `OppGyMonsters`,
`EffectTargetKind.CardInEitherGy`) are one member with one runtime arm; concept pairs with different
names (`PayLpToDeclareAttack`/`AttackCostLp`, `MillToDeclareAttack`/`AttackCostMillForOpponent`,
`CallCoinLoseHalfLpIfWrong`/`CoinCallWrongLoseHalfLp`, …) keep both members and both arms.

`Compile` runs the template collectors in two orders and keeps one program:

1. **local first** — `ClassicEraTemplates` (GitHub) runs after the local collectors;
2. **ClassicEra first** — only if a ClassicEra template matches the text at all.

The local-first program wins unless it left text unparsed, or double-compiled one effect (same
timing + action twice) where ClassicEra-first did not; `ClassicEraArmCards` forces the GitHub arm for
cards whose local arm was narrower than the printed text. A ClassicEra-first program is marked
`CompileSource = "regex:classic-era"`. GitHub-lineage clauses name the affected player with
`OpponentIsSubject`; `NormalizeGitHubSubject` mirrors it into `Side` for Draw / Gain LP / Pay LP,
which the local runtime reads. A program whose `RaceFilter` is not a printed monster Type fails closed.

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
