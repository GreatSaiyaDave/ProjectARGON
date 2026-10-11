# ENGINE_MAP: where to put card code (Project ARGON rules engine)

Read this before you explore `Assets/Scripts/WRLDZ/Duel`. It names the symbols to touch for each kind of card work and
the closest precedent. Every symbol was checked with `grep -n` on 2026-10-04 (compiler Version 159). Line numbers drift,
so search by name: `grep -n "SymbolName" <file>`, then `sed -n 'a,bp'`. Never read a whole engine file.

Paths below are relative to `Assets/Scripts/WRLDZ/Duel/`. Big files: `TextEffects/TextEffectRuntime.cs` (~650 KB),
`DuelEngine.cs` (~310 KB), `TextEffects/CardTextEffectCompiler.cs` (~240 KB), `SpellTrapEffects.cs` (~170 KB),
`TextEffects/CompiledCardProgram.cs` (~155 KB: EffectClause and the enums). Group work goes in its own files:
`TextEffects/Batches/<Group>Atoms.cs`, `TextEffects/Batches/<Group>Runtime.cs`, `Rules/Batches/<Group>RegressionTests.cs`.
The notes of earlier groups are in `~/.cache/wrldz-card-compile/*/patches/<Group>.md` (sections "Shared-code changes" and
"Deferred"). Read the notes of the closest precedent group before you write code.

---------------------------------------------------------------------------------------------------------------------
## 1. Pipeline

1. `CardTextEffectCompiler.Compile(def[, eraId])`. Whole-text special cases (`SharedPsctAtomTemplates.TryMatch*`) run
   first. Then the template `Collect` passes run in this order: `ArchfiendTemplates`, `ProtectionTemplates`,
   `ClassicEraTemplates`, `LegacyTextTemplates` (which calls `AtomRegistration.Collect`, the base `Atoms` and then every
   `BatchAtomSets` entry), `AdvancedEffectTemplates`, `PhaseTriggerTemplates`, `ContinuousRestrictionTemplates`,
   `MonsterTriggerTemplates`. Matched spans are absorbed (`MarkAbsorbed`).
2. The remaining text goes through `PsctGrammar.Parse` and then `CompilePsctSentence`, sentence by sentence.
   - `PsctConditionGate.IsRecognized(sent.Condition)` drops a sentence with an unknown "if/when ...:" condition (fail closed).
   - `IgnitionTemplates.TryCompile` runs next (`ParseCost` for costs, `ParseResolution`).
   - The generic fallbacks follow. `IsWholeResolution` requires each fallback to match the whole resolution text.
     `IsUnrecognizedTriggerAsActivate` and `HasUnparsedActivationCost` refuse the sentence.
   - `ParseActivationTarget` turns the target phrase into a Zone.
3. The output is a `CompiledCardProgram` (`ClauseList` of `EffectClause`, `FullyCompiled`, `CanResolveAny`, `TextHash`,
   `CompilerVersion`). Each clause has `Timing`, `Action`, `Side`, `Zone`, `Amount` and many flags.
4. `EffectVocabulary.ResolutionOf(action)` maps each `EffectActionKind` to an `EffectResolutionKind`; `CostOf` describes
   costs; `IsUniqueException` lists the named one-card actions.
5. At runtime, `TextEffectRuntime` does the work:
   - `CanActivate` and `ActivationConditionsMet` decide legality;
   - `TryResolveActivation` handles activation and `StartIgnition` handles monster ignitions;
   - `CollectTargets`, `TargetPicks` and `AutoPick` handle targets;
   - `ApplyClause` (a large `switch` on `clause.Action`) does the effect;
   - triggers: `TryResolveFlip`, `TryResolveThisCardSummoned`, `TryResolveSentToGy`, `FirePhaseTriggers`, `FireAttackDeclared`
     and the `Notify*` family.
6. `DuelEngine` owns turns, phases, summons, battle, the chain (`ResolveChainStack`, `TryActivateChainResponse`) and the
   windows (`OpenResponseWindow`, `OpenChainResponseWindow`, `PassResponse`, `ContinueAfterResponseActivation`).
   `SpellTrapEffects` is the older Spell/Trap path. It holds `PendingActivation`, `CancelPending`,
   `IsLegalResponseCard`, `AiAutoRespond`, `BeginDeferredChainActivation`, and the legacy target mirror
   (`MapCompiledTargetKind`, `CollectLegalTargets`, `AutoPickTarget`).
7. `FieldSpellEffects.RefreshBoard` re-applies every continuous effect on each board refresh
   (`ApplyCompiledContinuous`, `ApplyEquipStats`, the batch `Refresh`/`ApplyBoard` hooks).
8. Status comes from `CardEffectStatus.Classify(def)`:
   - Structural: no activatable effect, or FullyCompiled with no clause plus `HasLiveSummonPath`;
   - Implemented: `OfficialEffectRegistry.HasActivatableScript`, or `FullyCompiled && CanResolveAny`;
   - Stub: some clauses compiled;
   - Unimplemented: nothing compiled.
   `CardEffectStatus.ExcludeUnimplementedFromDecks` keeps Stub and Unimplemented cards out of decks.
9. Seed: `CompiledEffectCache.GetOrCompile` uses a cached program only when its `TextHash` and
   `CompilerVersion == CardTextEffectCompiler.Version` match. Any change to compiler output needs a Version bump and
   `headless.sh . --export-seed`. In a scratch copy, never bump.

---------------------------------------------------------------------------------------------------------------------
## 2. How to add ...

### A whole-sentence atom (start here for most cards)
- File: `TextEffects/Batches/<Group>Atoms.cs` with `public static readonly AtomRegistration.Atom[] Atoms`.
- One sentence compiles to one clause: `new Atom(pattern, m => clause)`. To several clauses, in order:
  `Atom.Sequence(pattern, m => new[]{...})`. Precedents: `PerCountBurnsAtoms`, `TakeControlAtoms`.
- Register it with ONE line at the end of `AtomRegistration.BatchAtomSets`.
- Anchor the regex to the whole sentence, or a longer card turns into a Stub. Check: `status_diff.py` shows only
  intended cards changed. A card's whole remaining text may be absorbed only when every sentence is honoured.
- `LegacyTextTemplates.ExpectedActions` and `AtomRegistration.ExpectedActions` feed the corpus "missing action" checks.
  Make sure the atom's clauses produce the actions its words imply.
- Fail-closed atoms: an atom that matches and then refuses keeps the card unparsed. See `PartialCompilesAtoms` and the
  v151 `WholeResolution` tests.
- Where a template generalization is legitimate, edit the shared template (example: "A"/"An" in
  `LegacyTextTemplates.RxEquipSplit`; the Spirit regex in `PhaseTriggerTemplates`). Otherwise use a batch atom.

### A new EffectActionKind and its ApplyClause arm
- Append the member at the END of `EffectActionKind` in `TextEffects/CompiledCardProgram.cs`, with a doc comment that
  names the group. `enum_guard.py` enforces this.
- Map it in `EffectVocabulary.ResolutionOf`. For a one-card rule set, list it in `IsUniqueException` instead
  (precedent: DestinyBoard, WinDuelWithNamedSetInHand).
- Add `case EffectActionKind.X:` in `TextEffectRuntime.ApplyClause` and call `<Group>Runtime.Apply...` for any
  non-trivial body. `DestroyCard` is internal and takes an optional `byPlayer`; use it rather than removing cards directly.
- Keep per-engine state in the Runtime class in a `ConditionalWeakTable` (17 batch runtimes do this; see
  `BattlePhaseEndRuntime`). Only per-card flags go on `CardInstance` in `DuelModels.cs`; clear them in `ResetOnFieldEntry`.
- A clause that only marks a trigger (the action happens elsewhere) gets a no-op arm. Precedents:
  `SpecialSummonThisFromHandByOwnProcedure`, `DrawWhenMonsterSpecialSummonedFromYourGy`.
- New clause fields go at the END of `EffectClause`. Precedents: `RequiresSendEquipSpellOnThisToGy`,
  `OnlyDuringMainPhase1`, `ThenDiscardCount`, `EffectChoiceIndex`.

### An activation condition
- The compiler sets `clause.ActivationCondition` (a string). `TextEffectRuntime.ActivationConditionsMet` switches on it.
- Precedent pattern: expose a `const string` in your Runtime, add a `case` (see `BattleWindowsRuntime.MonsterSentFromFieldToYourGy`),
  or add a `<Group>Runtime.TryActivationCondition(engine, who, c, out ok, out reason)` to the default chain. That chain
  already has PerCountBurns, TypeDeclaration, EquipFamily, LocksAndZones, HandDisruption, PositionLocks,
  TargetedClassics, RuleLocks and NegationsAndGySummons.
- `CorpusTriggerStressTests.NeedsHardBoard` / `Provision`: provision a board for your condition, or skip it for a
  window-only card. Otherwise "Corpus: FullyCompiled Traps are legal in their window" fails.

### A cost
- Costs are paid at activation; effects happen at resolution. A cost must be legal before activation (`CanActivate`,
  `IgnitionCostLegal`).
- Monster ignitions and Spell/Trap costs that need a pick: `HasStructuredIgnitionCost`, `IgnitionCostLegal`,
  `CollectIgnitionCostCards`, `PayOneIgnitionCost` (Core), `PayAutoIgnitionCosts` (AI or a lone choice), `StartIgnition`
  (opens `PendingActivation.AwaitingIgnitionCost` when 2+ choices exist).
  - `BattleWindowsRuntime.BeginCost` / `EndCost` wrap the payment so monster sends count as cost sends.
- Clause fields: `RequiresDiscardCost`/`DiscardCostCount`/`DiscardCostRandom`, `RequiresTributeThis`/`RequiresTributeCount`,
  `RequiresSendThisToGy`, `RequiresSendOtherYouControl`, `RequiresSendNamedToGy`, `RequiresSendRaceFromDeck`,
  `RequiresSendEquipSpellOnThisToGy`, `BanishFromGyCount`/`BanishFromGyUpTo`, `CostBanishRandomHandCards`,
  `CostBanishTopDeckCards`, `PayLpAmount`, `PayHalfLpCost`, `RequiresLpCostMultiple`.
- LP costs: `ActivationLpCostRuntime.LpCostClause` / `PayOnActivation`.
  - "Activate by paying N" with no resolution is refused at compile time (v143, `LpCostOnlyActivationRegressionTests`).
- Tribute costs: `CollectTributeCost` (forced targets, full-field rule `CostFreesMonsterZone`).
- Equip sent as a cost: `SendToGraveAsCost`. It suppresses the optional "When" GY effect of the sent card (missing the
  timing). Precedent: Guardian Ceal, `EquipSpellCostRegressionTests`.
- Response-window costs: `SeatResponseOnChain` pays discards via `ContinueResponseDiscardPick`;
  `ResponseDiscardPayable` gates them. Cancel refunds them with `RefundResponseDiscards` (from `CancelPending`).
  - Counter Traps: `DuelEngine.TryActivateChainResponse` pays the half-LP cost and `RequiresTributeThis`; a human picks the
    discard (`EngineResidualsRuntime.AskChainAnswerDiscard`, Cancel = not activated), the AI keeps `PickLowestValue`.
  - Free-chain cards chained to a link: a plain "Discard 1 card" cost (`EngineResidualsRuntime.IsChainableDiscardCost`:
    Raigeki Break, Spell Purification, Malice Dispersion) is paid before the link is seated (`PayChainDiscard`); the
    link gets `SeatedFrom = ChainResponse` so `TryResolveSeatedResponse` resolves it without paying again; Cancel at the
    target refunds the discard (`PendingActivation.PaidDiscards`).
  - Monster ignitions (their own "manual" link): Cancel at the cost or target pick undoes the activation
    (`EngineResidualsRuntime.UndoMonsterIgnition` from `CancelPending`: link dropped, discards / LP refunded).
  - Hand-action tolls: `SpellTrapEffects.RefundHandActionToll` (Chain Energy).
- Cancel = the activation never happened: drop the link (`DuelEngine.DropUnresolvedLink`), the Set card goes back
  face-down (`PendingActivation.WasSetOnField`), and every paid cost is refunded.
- Add the cost to `EffectVocabulary.CostOf` and to `ActivationLpCostRuntime.HasNonLpCost` when relevant.

### A target kind / zone filter (TWO mirrors)
- Append the member to `EffectZoneFilter`. The compiler sets it in `ParseActivationTarget` or in the atom.
- Text path: `TextEffectRuntime.CollectTargets` (a case for the zone plus the batch filters at its end:
  `TakeControlRuntime.FilterTargets`, `TributeSummonsRuntime.FilterTargets`; keep every one), `MapTargetKind` (zone to
  `EffectTargetKind`; a monster kind makes the picker tag YOURS/OPP) and `AutoPick` (the AI's choice).
- Legacy Spell/Trap path: `SpellTrapEffects.MapCompiledTargetKind`, `CollectLegalTargets` and `AutoPickTarget`.
  `SpellTrapEffects.UsesCompiledActivationPending` decides which path a card takes. Map and filter the zone in BOTH, or
  leave the legacy map at None so the text path picks (precedent: `FieldFaceUpFusionMonsters`). Precedents: OppMonsters
  (v152), `DeclaredMonsterTypes`, the Kycoo filter, ExceptThisCard in SpellTrapOnField.
- Count: `TextEffectRuntime.TargetPicks(c, out upTo)` / `AmountIsUpToTargetCount`. Amount is NOT a target count, except
  for the listed actions. For an exact N, use `TargetCount` / `ControllerTargetCount` / `TargetUpTo`
  (`TargetedClassicsRuntime.ExactTargetCount`, `AllExactTargetsStillThere`).
- Second target: `RequiresSecondTarget`, `SecondZone`, `ApplySecondTarget`, `CollectTargetsForZone`, `TryContinueDualOrUpTo`.
- Face-up requirement: `RequiresTargetFaceUp`. "Monster your opponent controls" includes face-down monsters (v152).
- A lone legal target resolves with no prompt.

### A player choice (picks, declarations, options)
- **New code: `ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest { … })`**
  (EngineResiduals v163). Candidates / Kind / Prompt / AiChoose / Finish, plus `Cancel` (null = mandatory, Cancel makes
  the AI's choice; set = decline or undo), `AskWithOneCandidate` (a yes/no tap), `HoldsChain` (default true),
  `HoldsStandby` (default: the chooser's own live Standby Phase; `DuelEngine.BeginTurn` pauses and
  `ResumeStandbyAfterUpkeep` then runs the queued upkeeps) and `DuringActivation` (a cost pick while activating: nothing
  held or resumed). It queues, routes taps and Cancel itself (no new `PendingActivation` dictionary, no new
  `TryResolvePick` / `TryCancelPick` hook) and answers the AI / a lone card at once. Users: FlipDownAndPeek, the coin /
  die redo (CoinDiceInterrupts), Card Shuffle, Magic Jammer's and chained free-chain cards' discards.
- `SpellTrapEffects.PendingActivation` carries the state. Existing flows the UI already drives:
  - card targets (`LegalTargets`);
  - `AwaitingIgnitionCost`, `AwaitingLpCost`/`LpCostChoices` (also used as Yes/Cancel, e.g. `AwaitingUpkeepChoice`);
  - `AwaitingCoinCall`, `AwaitingDrawThenDiscard`, `AwaitingForcedDiscardChoice`;
  - `EffectChoice` (`EffectChoiceRuntime`: choose 1 of 2 bullets through the target picker);
  - `PromptOverride` for your own prompt text.
  Reuse one of these. A new kind of UI widget means a DuelUI change, which is outside the card-engine scope.
- Routing: `TextEffectRuntime.TryResolveTextTarget` (Spell/Trap picks), `TryResolveMonsterTextTarget` (monster picks) and
  `SpellTrapEffects.TryResolveWithTarget`. Each has batch hooks at the top; add yours as `<Group>Runtime.TryResolvePick`.
  - Cancel: `SpellTrapEffects.CancelPending` hook (`TryCancelPick`).
  - A Special Summon placement pick: `PendingTargetIsSpecialSummon` (`FlipMassEffectsRuntime.TryPickAsksPlacement`).
  - Set and clear the pending only through `DuelEngine.SetPendingActivation` / `ClearPendingActivation`.
- Type/Attribute declaration: `TypeDeclarationRuntime` (`RecordDeclaredType`, the `EffectTargetKind` members
  `DeclareMonsterType` / `DeclareAttribute`). The live Type is `CardInstance.Race` (`TypeDeclarationRuntime.CurrentRace`),
  and the live Attribute is `CardInstance.Attribute` (`EquipFamilyRuntime.CurrentAttribute`). Read these, never `Def.race`,
  for field cards.
- Holding the chain while a link waits on a choice: `PendingActivation.HoldsChainResolution`, then
  `DuelEngine.ResumeChainAfterChoice`.
- AI: answer every pick on the AI path (auto-pick helper in your Runtime). `SimpleAi` never answers a pick whose
  controller is the player. Coins and dice in tests: `engine.Rng.QueueCoin(bool)` / `QueueDie(n)`.

### A trigger
- Trigger effects as real Chain Links (TriggerChainResponses, v163): `TriggerChainResponsesRuntime.TrySeat(engine, seats,
  afterChain)` seats activated triggers (costs paid, targets chosen by the caller; `Seat.Resolve` = what the link does)
  on `engine.Chain` as Spell Speed 1 `EffectClass.Trigger` links (`EffectKey` = `TriggerLinkKey`) and opens the Chain
  response window of the opponent of the last link's controller — only when play is open (`CanSeatNow`) and that
  player has a legal response (probed with the links in place, then taken back: false = resolve as before, nothing
  changed). `DuelEngine.ResolveChainStack` resolves each link through `TryResolveLink` before the negation check: a
  negated link does nothing; a Spell/Trap effect that triggered in the S/T Zone is negated while a face-up negator covers
  its card (Royal Decree, `ContinuousNegationRuntime.NegatedBy`); a pick its resolution opens holds the Chain (resumed by
  `Flush` from `Notify`). `afterChain` (and `RunAfterChain`) runs once the Chain is over (`AfterChainResolved`, which
  also flushes queued summon windows). Counter Traps that negate a card activation cannot answer a trigger link
  (`TextEffectRuntime.CanActivateChainResponse`); the AI's "use it or lose it" answer reads the link's targeted clause
  (`LinkThreatensCard`: Armed Ninja does not threaten a Set Trap). Converted sites: every `SimultaneousTriggersRuntime`
  Chain stepped while play is open (FLIPs flipped by an effect, queued triggers, held GY triggers) gets a response stage
  (`Stage.EngineChain`); a Flip Summon's FLIP (`SimultaneousTriggersRuntime.TryChainFlipSummoned`, from
  `CompleteFlipSummon`; its summon window opens after the Chain); an "If" sent-to-GY trigger (`MakesChainLink`, not a
  missable "When … you can") of a monster sent while a Chain resolves waits for that Chain and goes on a new one
  (`TryDeferSentToGy`; held triggers in `RunHeld` likewise, keeping `MissesWhenTiming`) — only when play opens after
  that Chain (`DuelEngine.OpenPlayAfterChain`) and the opponent may respond (`OpponentMayRespond`); Freed's Draw Phase
  search (`NegationMonstersRuntime.TrySeatSearch`; the draw is replaced at activation); Life Absorbing Machine's Standby
  effect (`ContinuousTrapsAndSpiritSupportRuntime.TrySeatStandbyGain`; the Standby Phase tail is
  `DuelEngine.ContinueStandbyAfterTriggers`); Spirit's Invitation's return trigger (the End Phase holds through
  `_endTurnPausedFor` / `ResumeEndTurnAfterTrigger`). `SimpleAi` waits for the player's answer in its Draw / Standby / End
  Phase. Triggers that happen at the same time go on ONE Chain (CL1 … n): `TriggerChainResponsesRuntime.BeginCollect` /
  `EndCollect` (nesting) open a collect pass — `TextEffectRuntime.FirePhaseTriggers` (each Standby / End Phase pass),
  `DuelEngine.EndTurn` (End Phase triggers + EngineHygiene / Return of the Doomed hooks) and
  `ContinuousTrapsAndSpiritSupportRuntime.FireSpiritReturnTriggers` (every Invitation on one return); inside one,
  `TrySeatOrCollect(engine, seat, inlineLater)` holds the seat and the outermost `EndCollect` seats all of them once the
  pass's other triggers have resolved (turn player's first), or runs each `inlineLater` when no window can open — so no
  trigger resolves, or opens a pick, while an earlier one's response window is open (v163 review: two Life Absorbing
  Machines + Royal Decree, two Invitations). A new trigger site: build a `Seat`, call `TrySeatOrCollect` (or `TrySeat`
  for a lone event), and resolve inline when it returns false.
- FLIP: `TextEffectRuntime.TryResolveFlip`. It pays FLIP costs (FlipCostsAndFusion). The Royal Command gate is
  `RuleLocksRuntime.SkipsFlipMonsterClause`. A FLIP flip during battle is `MonsterEffects.OnFlipSummoned`. A FLIP flipped
  by an effect mid-chain is queued with `DuelEngine.QueueFlipEffectAfterResolution` into the SEGOC queue
  (`SimultaneousTriggersRuntime`, v160): once play is idle (`DuelEngine.PlayIdleForTriggerChain`, the Standby pause
  counts as idle) all queued FLIPs form ONE Chain (turn player's mandatory, optional, then the opponent's), activate in
  Chain order (costs, targets; human picks via `PendingActivation`, a human orders 2+ of their own) and resolve in
  reverse on the chosen targets, even after their monster left (only "this card" parts need it: `NeedsSourceFaceUp`).
  Registered FLIP scripts are split by `MonsterEffects.TryRegisteredFlipTargets` / `ResolveRegisteredFlip`. A mass
  position change wraps its loop in `SimultaneousTriggersRuntime.Capture` so "position changed" triggers join the Chain.
- Summoned: `TryResolveThisCardSummoned`, `NotifyTributeSummoned`, `NotifyEitherPlayerNormalOrFlipSummoned`,
  `NotifyTributedForSummon`. The SS tail is `DuelEngine.CompleteSpecialSummon`.
- Phases:
  - `FirePhaseTriggers` / `FirePhaseTriggersFor`. Upkeep costs are `IsUpkeepCost`; a negated card still pays them.
  - Standby holds: `DuelEngine._standbyPausedFor`, `StandbyPausedFor`, `CanContinueAfterStandby` and
    `ContinueAfterStandby` (the UI reaches it through `DuelCommandService` EnterMainPhase1), and
    `ContinueStandbyAfterUpkeepChoices`.
  - End Phase picks: `DuelEngine.ResumeEndTurnAfterTrigger` offers queued picks (`EquipFamilyRuntime.OfferQueuedPick`,
    DestinyBoard).
  - Turn and phase hooks: `BeginTurn`, `OnStandbyPhase`, `OnMainPhase1Start`, `OnDrawPhaseStart`.
- Battle:
  - `FireAttackDeclared`;
  - `BattlePhaseEndRuntime.FireBeforeDamageCalculation` / `FireEndOfBattlePhase`;
  - `NotifyAfterDamageCalculation`;
  - `NotifyDestroyedOpponentByBattle`;
  - the end-of-Damage-Step window `DuelEngine.OfferEndOfDamageStepWindow`;
  - `EffectTiming` members `StartOfDamageStep`, `DamageCalculation`, `AfterDamageCalculation`, `EndOfDamageStep`,
    `EndOfBattlePhase`.
- Sent to the GY or destroyed:
  - `TryResolveSentToGy` (takes an optional `only` predicate);
  - `MonsterEffects.OnSentFromFieldToGy`;
  - post-event windows: `DuelEngine.OfferMonsterSentWindow` (Michizure) and `OfferSpellTrapDestroyedWindow` (Gust);
  - destroyer records: `NoteDestroyer`;
  - GY sends go through `RedirectAndReplaceRuntime.PutInGraveyard`, which applies Banisher of the Light;
  - "by opponent's effect": `DiscardTriggersRuntime` (DiscardedFromHandByOpponentEffect,
    SentFromDeckToGyByOpponentEffect; the queue is flushed in `ResolveChainStack`).
- Draws: `NotifyDrew` / `NotifyDraw`; HandDisruption's `OpponentDrewOutsideDrawPhase` window.
- Control change: `NotifyControlChanged`.

### A response window / chain answer
- `ResponseTiming` (`ResponseWindow.cs`, runtime only) has: AttackDeclared, MonsterSummoned, DamageCalculation,
  OpponentOpenState, YouTakeDamage, ChainResponse, SummonNegation, SpellTrapDestroyedByOpponent,
  OpponentDrewOutsideDrawPhase, EndOfDamageStep, MonsterSentFromFieldToYourGy, MonsterEquipped, YouDiscardedFromHand,
  MonstersReturnedToHandByOpponent (v163, Major Riot).
- A new window needs:
  - `DuelEngine.OpenResponseWindow` (profile + prompt);
  - branches in `PassResponse` / `ContinueAfterResponseActivation`;
  - `RecoverStuckCombat` if it is inside battle;
  - `SpellTrapEffects.IsLegalResponseCard`;
  - `AiAutoRespond`;
  - a `TextEffectRuntime.CanActivate` response branch;
  - a `TryResolveActivation` seat branch.
  Precedent: BattleWindows (two windows) or SpellTrapDestroyedResponse.
- Seating: `TextEffectRuntime.SeatResponseOnChain` (pays LP and discard costs, then `OpenSeatedResponseChainWindow`);
  `TryResolveSeatedResponse` resolves the link. Inline answers: `BeginInlineResponseTargetPick`.
  `ClauseMakesChainLink` decides whether an answer makes a chain link.
- Chains: `CanActivateChainResponse`, `CanActivateFreeChainOnChain` / `ActivateFreeChainOnChain` (Set free-chain cards,
  and `IsContinuousTrapPlacementOnly` Continuous Traps), `SpellTrapEffects.CollectLegalResponseCards`,
  `DuelEngine.TryActivateChainResponse` (Counter Traps; `ApplyActivationNegation`; `ActivationNegatable`).
- Compiled-pending Spells/Traps (`UsesCompiledActivationPending`) open a real chain window through
  `CompiledActivationChainRuntime.Activate` / `TryResolveLink`. Costs and targets happen at activation, and the effect is
  replayed at resolution.
- Summon negation: `SummonNegationRuntime`, `EffectTiming.MonsterWouldBeSummoned`, `EffectActionKind.NegateSummon`.
- Card-effect links (not card activations): `ChainLink.EffectKey`; see `PerCountBurnsRuntime.TryResolveFaceUpFieldIgnitionLink`.
- Monster ignition from a face-up Spell/Trap (Spell Speed 1, own Main Phase): `EffectTiming.FaceUpFieldIgnition`
  (`PerCountBurnsRuntime.TryFaceUpFieldIgnition`, Wave-Motion Cannon). There is no Spell Speed 2 equivalent (see Gaps).

### A continuous effect or lock
- `ContinuousWhileFaceUp` clauses are re-applied on every refresh by `FieldSpellEffects.ApplyCompiledContinuous`
  (inside `RefreshBoard`). Position locks set `PositionLockedByEffect`; `ClearOne` resets it on each refresh.
- `RefreshBoard` runs these hooks in order (keep the order): `ContinuousNegationRuntime.Refresh` first,
  `TurnNegationTrapsRuntime.Refresh`, `TakeControlRuntime.SweepBoundControl`, `LocksAndZonesRuntime.Refresh`,
  StandbyGrowth `ApplyBoard` / `GySendCheckpoint`, `BattleWindowsRuntime.ApplyBoard`.
- Negation:
  - `ContinuousNegationRuntime`: `ActivationLocked`, `NegatesResolvingClause`, `ChainLinkEffectNegated`,
    `SpellEffectsOnFieldNegatedBy`;
  - `CardInstance.IsNegated` (a property);
  - `ContinuousProtections.EquipCardEffectsAreNegated`.
- Rule locks: `RuleLocksRuntime` (`SpecialSummonLocked`, `NormalSummonLocked`, `FlipSummonLocked`, `BanishFromGyLocked`,
  `HandActionLpCost`). Every summon path must ask it: `DuelEngine.SpecialSummonToField`, `CanSpecialSummonProcedure`,
  `SummonProcedures.CheckNormalOrTribute`, `RitualProcedures`.
- Protections and continuous checks: `ContinuousProtections`, `DuelEngine.ContinuousCannotAttackBlocks`,
  `FieldSpellEffects.ControllerAvoidsBattleDamage`.
- Precedents: ContinuousNegation (Jinzo, Imperial Order), RuleLocks, LocksAndZones (Cold Wave, `FieldZone.UnusableFor`).

### A hand Special Summon procedure
- Runtime: `ToonsRuntime.HandProcedureOf(card)` / `HasHandProcedure`. The engine side is
  `DuelEngine.CanSpecialSummonProcedure` / `TrySpecialSummonProcedure` (Gilasaurus is checked first; the RuleLocks
  summon lock applies).
- `UI/DuelUI.cs` reads `HandProcedureOf(...).Amount` for the hand Special Summon button, so a new procedure must be
  expressible through it. Otherwise the human cannot summon it and the card is AI-only, which is a fail-closed reason.
- AI: `NegationsAndGySummonsRuntime.AiWantsOwnHandProcedure`. Sets `CardInstance.SpecialSummonedByOwnProcedure` /
  `ProcedureCompleted` (the Semi-Nomi rule).

### Equip mechanics
- Equip from an activation: the `EffectActionKind.EquipThisToTarget` arm. `EquipHostName` restricts the host
  (Cyclon Laser). Linking: `TextEffectRuntime.EquipCardTo` (calls `NoteEquipped`), `DuelEngine.UnlinkEquips`,
  `UnlinkEquipsForZoneMove`, `DestroyEquipsOfFaceDownHost`.
- Equip limit model: `EquipFamilyRuntime.IsCorrectEquipTarget(engine, equip, monster)` and `MoveEquip`. Unknown limit
  shapes fail closed.
- Stats: `FieldSpellEffects.ApplyEquipStats`. Absorbed monsters: `AbsorbedBy`. Control equips:
  `ReapplyEquipTakeControl`, `RevertEquipTakeControlPublic`. Monster as an equip (Kiseitai): `CardIsKind` /
  `CardMatchesTypeWord` treat it as a Spell.
- Monster Card equipped as an Equip Spell with a granted effect (Sword Hunter, v161): `CardInstance.GrantedEquipHost` /
  `GrantedEquipAtk`; `IsEquipSpellWithGrantedEffectOnly` makes `IsNegated` true, so every S/T Zone program scan skips its
  monster text; `ApplyEquipStats` adds `GrantedAtkOf`. Sent from the S/T Zone to the GY, it fires its OWNER's
  `OnSentFromFieldToGy`. Spell/Equip destroy protection and replacement (Heart of Clear Water, Magic Reflector):
  `EquipAndSpellProtectionRuntime.ReplacesDestruction`; a new destroy path must ask it.
- Precedents: EquipFamily (v159), SpiritsAndMisc (Kiseitai), GrokEquipStandby, BothSideAndMonsterEquip (v161).

### Per-card counters, turn markers, stamps
- `CardInstance` fields: `Counters`, `SpellCounterMax`, `TurnCounter` (Standby count while face-up),
  `ContinuousTurnsRemaining`, `EquipTurnCounter`, `FieldEntryStamp`, `FaceUpStamp`, `FlippedFaceUpThisTurn`,
  `SummonedThisTurn`, `NormalOrFlipSummonedTurn`, `SetThisTurn`, `AtkZeroUntilEndOfBattlePhase`.
- `MarkTurnStartFaceState` sets the turn baseline. Reset new fields in `DuelEngine.ResetOnFieldEntry`.
- Spell Counters: `TryPlaceSpellCounterOnSpell`, `RequiresRemoveSpellCounters`.

### Win conditions
- `DuelEngine.EndGameByCardEffect(winner|null, reason)`. Hand wins: `TextEffectRuntime.CheckHandWinConditions` /
  `HasHandWin`. Board-state wins are checked in `Notify` after `RefreshBoard` (`DestinyBoardRuntime.OnBoardSettled`).

### Simultaneous and group events
- Wrap a mass destroy, multi-Tribute or group summon in `FlipMassEffectsRuntime.Simultaneously(engine, () => ...)`, so
  sent-to-GY triggers fire once for the whole event (`TryHoldSentToGy`). The held triggers then go on one SEGOC Chain at once
  (`SimultaneousTriggersRuntime.RunHeld`: the opponent's resolve first, each player's own in event order; sent by a
  Chain Link that is not the last to resolve, optional "When … you can" ones miss the timing). Battle: `ApplyBattleDestructions` destroys both
  battlers in one event.
- Group Special Summons with player picks: the `FlipMassEffectsRuntime` plans (`IsMassSummonPick`, `Optional`,
  `FaceUpOnly`). "Can be Special Summoned" helpers: `CanBeSpecialSummonedByThis`, `HandOrDeckCopyCanBeSpecialSummoned`,
  `CanSpecialSummonFromGyTarget` (they are separate; see Gaps).

### Shared engine events (EngineHygiene, v160)
- Discards: every discard path raises `DiscardEvents` (`Record` / `Begin` / `End` / `Retract`; `MonsterEffects.DiscardFromHandByInstance`
  records; a "send from your hand to the GY" uses `SendFromHandToGraveByInstance`). Window `ResponseTiming.YouDiscardedFromHand`
  (Forced Requisition), queued triggers fired when idle (`DuelEngine.Notify`).
- Tributes: call `ReleaseEvents.Record(engine, card, reasonPlayer)` right before a Tribute leaves (every current site does).
- "Can be Special Summoned": `SpecialSummonLegality.CanBeSpecialSummoned(engine, who, card, source, SummonOrigin, ...)` knows
  Nomi / Semi-Nomi / Ritual / Extra Deck conditions, Necrovalley, RuleLocks, Chain Energy's toll and per-card summon costs
  (`RegisterSummonCost`, paid in `DuelEngine.SpecialSummonToField`; for Tyrant Dragon-style EFFECT_SPSUMMON_COST).
- Hand procedure "banishing N ATTRIBUTE monster(s) from your GY": a Toons procedure clause with `BanishFromGyCount` /
  `AttributeFilter` (`EngineHygieneRuntime.SummonByBanishProcedure`, human pick + Cancel).
- End Phase: `EngineHygieneRuntime.OnEndPhase` (Ekibyo Drakmord countdown, Super Rejuvenation draw).

### Coin tosses and die rolls (CoinDiceInterrupts, v161)
- Every card-effect toss or roll goes through `CoinDiceInterruptsRuntime.TossCoins` / `TossCoin` / `RollDice` / `RollDie`
  `(engine, performer, source, goodForPerformer, apply)`; the outcome code is the `apply` continuation, in tail position.
  Never throw `engine.Rng` directly: Second Coin Toss and Dice Re-Roll must be offered before the result applies.

### Other shared hooks added by v161
- LP costs: `DuelEngine.PayLifePoints(..., isCost)` / `PayLifePointCost` record a cost (Life Absorbing Machine counts
  them); a toll handed back must call `ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost`.
- Attacking player's battle windows (monster Quick Effects in the Battle Step and damage calculation):
  `BattleBoostsRuntime.CanActivateQuick`; they open only when such an effect is legal.
- Battle-destroyed negation (Dark Ruler Ha Des, Twin-Headed Wolf): `CardInstance.EffectsNegatedAfterBattle`,
  `NegationMonstersRuntime.NegatedInGy`; banish instead of GY (Lesser Fiend): `BanishesBattleDestroyed` in `DestroyMonster`.
  Spell/Trap effects that target are negated in `ResolveChainStack` (`TryNegateTargetingLink`, Freed / Tyrant).
- Field-to-Deck event: `DuelEngine.ReturnCardToDeck` → `DeckReturnsAndMassEffectsRuntime.NoteReturnedToDeck`. Queue an
  extra SEGOC trigger: `SimultaneousTriggersRuntime.QueueTrigger(engine, controller, card, optional, run)`.
- Tribute Summon materials event: `SpecialSummonEffectsRuntime.NotifyTributeSummonMaterials` (Skull Knight #2);
  "only by its own procedure": `OnlyByOwnProcedure` in `SpecialSummonToField`.
- Declared Types: Spell/Trap declarations use `TypeDeclarationRuntime.RecordDeclaredType`; The Hunter with 7 Weapons
  keeps its own table (`BattleBoostsRuntime`). Lasting ATK grants live in SpecialSummonEffects, BattleBoosts and
  BothSideAndMonsterEquip with different lifetimes. Both are candidates for one shared primitive.

### Effect damage (EffectDamageEvents, v162)
- Every effect damage goes through `DuelEngine.ApplyEffectDamage(target, dmg, sourceName, source)` /
  `ApplyEffectDamageBoth(first, d1, second, d2, sourceName, source)`; pass `source` (the card) when you know it. Battle
  damage is `ApplyDamage` (no effect-damage event); LP loss (`LoseLifePoints`) and costs (`PayLifePoints`) are not damage.
- Order inside (ygopro `field::damage`): `EffectDamageEventsRuntime.ModifyEffectDamage` (reflect to the other player, then
  negate → 0, both keyed to ONE Chain Link and only for damage from that link's card; cleared by `OnChainEnded`), the Crush
  Card Virus guard, the LP change, then `AfterEffectDamage` (records `LastEffectDamage`; queues "each time your opponent takes
  damage from a card effect" triggers on the SEGOC queue).
- A new damage modifier: add a `ModifierKind`, register it from the resolving link (`TryResolveLink`, called in
  `ResolveChainStack` before the negation arms). The resolving link is tracked by `NoteResolving`.
- "Would this link inflict damage to X" (ygopro CATEGORY_DAMAGE operation info): `EffectDamageEventsRuntime.LinkWouldDamage`
  derives it from the link's activation clauses (`Victims` maps each burn action to the player it hits; an LP gain under
  Bad Reaction to Simochi counts). A new burn action must be added to `Victims`, or Barrel / Board Eraser will not answer it.
  A face-up Spell/Trap's effect link (Wave-Motion Cannon, Quick Effects) is not a card activation.
- A Counter Trap answering a damage link: a ChainLinkActivated clause whose action `IsDamageAnswer`; legality
  `CanAnswerDamageLink` (TextEffectRuntime.CanActivateChainResponse + DuelEngine.TryActivateChainResponse guard).
- "Each time a monster(s) is sent to your opponent's GY" (one trigger per send event): `EffectDamageEventsRuntime.GySendCheckpoint`
  (called from the Skull Invitation checkpoint). Mandatory face-up triggers use `QueueBurn` → `SimultaneousTriggersRuntime.QueueTrigger`
  and re-check the card at resolution (same `FieldEntryStamp`, face-up, not negated).
- Battle damage treated as effect damage (Gravekeeper's Vassal, EFFECT_BATTLE_DAMAGE_TO_EFFECT):
  `EffectDamageEventsRuntime.BattleDamageAsEffect` before `BattleMechanics.Calculate` — the converted damage skips every
  battle-damage change/prevention (Waboku, Kuriboh flags), is dealt with `ApplyEffectDamage`, and fires no battle-damage trigger.
- Discard pick inside a resolving link: `PendingActivation.AwaitingForcedDiscardChoice` + `HoldsChainResolution`;
  `DiscardTriggersRuntime.FinishForcedDiscardChoice` now calls `ResumeChainAfterChoice` (no-op when nothing is held).

---------------------------------------------------------------------------------------------------------------------
### v162 shared hooks (card batch v162-pgd)
- Generic human card pick: `ContinuousTrapsAndSpiritSupportRuntime.AskCardPick` — see section 2 "A player choice" (the
  `CardPickRequest` overload, v163). It holds Standby (`StandbyHold`) and resumes through `DuelEngine.ResumeStandbyAfterUpkeep`,
  which runs `TypeDeclarationRuntime.RunStandbyUpkeep` first.
- `FlipDownAndPeekRuntime.LogLook` logs a look at hidden cards; `PgdSpellTrapBasicsRuntime` bumps `FieldEntryStamp` when a Spell/Trap enters from the hand.
- `EffectDamageEventsRuntime`: effect-damage events and Gravekeeper's Vassal battle-to-effect damage conversion (all 3 battle-damage sites skip both battle-damage notifications when converted).
- `DiscardTriggersRuntime.NoteSentFromDeck` records the effect reason player in `GyEntryStamps` (feeds `SentByOpponentThisTurn`).
- `BattleOutcomeTriggersRuntime`, `StandbyBurnsAndDiceRuntime`, `BattleDestroyedGyRecoveryRuntime` (+ `*Atoms.cs`): per-group entry points; tests in `Rules/Batches/`.

### End-of-Battle-Phase prompts and summon costs with a pick (SummonPromptsAndCosts, v163)
- Optional end-of-Battle-Phase choice for a human (Vampire Baby): queue it from the `BattlePhaseEndRuntime.ResolveEndOfBattlePhase`
  arm (the AI decides inline) and open it from `DuelEngine.Notify` once play is idle and the Battle Phase is over
  (`SummonPromptsAndCostsRuntime.OnNotify`). `TryEnterMainPhase2` / `ForceEndBattlePhase` land in Main Phase 2 first;
  `DuelEngine.EndTurn` stops before the End Phase (`HoldsEndTurn`, the phase reads Main Phase 2) and the turn ends once the
  choice is answered (`ContinueEndTurn`, retried from Notify while a window is open). `SimpleAi` waits for an open player
  choice after entering Main Phase 2 and before `EndTurn`.
- 1..N GY picks with placement: `PendingActivation` `MonsterInEitherGy` + `IsMonsterEffect` with a
  `SpellTrapEffects.PendingTargetIsSpecialSummon` hook (`TryPickAsksPlacement`); CANCEL before the first tap declines, after it
  summons the ones chosen. "Destroyed a monster by battle" flag per field presence: `NoteBattleDestroying`
  (`NotifyDestroyedOpponentByBattle`), reset by `ResetBattleDestroyingFlags` at the end of every Battle Phase.
- Special Summon cost with a pick (ygopro EFFECT_SPSUMMON_COST, Tyrant Dragon): a `SpecialSummonLegality.SummonCost` registered in
  the `SpecialSummonLegality` static constructor (`GyTributeSummonCost`). `CanPay` gates every legality check (both GY target
  mirrors also call `CanPaySummonCosts` with the summoner); `Pay` runs inside `SpecialSummonToField` and uses the human's
  recorded pick. The pick is asked before the Summon: `DuelEngine.ResolveChainStack` calls `HoldBeforeLinkResolves` on the next
  link (a GY "Special Summon it" target clause, or Monster Reborn's script) and holds the Chain (`_chainHeldForChoice`);
  a flow that summons inside its own continuation calls `AskTributePick(engine, who, card, then)` first. No CANCEL (auto-pick).
  Residual: a GY Summon chosen inside another resolution or SEGOC trigger (e.g. Spear Cretin's pick, FlipMassEffects) Tributes the
  lowest-ATK candidate automatically.
- GY Summon origin: `SpecialSummonToField` knows a GY Summon only from `NextSpecialSummonFromGraveyard`; every GY path sets it.

### Banish-from-GY substitution (Spirit Elimination, ForcedActivationAndSubstitution v163)
- `SpiritEliminationRuntime.Affects(engine, player)`: the player's GY monsters cannot be banished this turn; a monster they
  control is banished instead (Lua `aux.SpElimFilter`; GY Spells/Traps are not affected). Every banish-from-GY collector
  asks it and swaps in `Substitutes(engine, player, except, mustBeFaceUp, matches)` (face-up with the property when the
  requirement names an Attribute / Type) or `SubstituteInList(engine, list, ..., gyOwners)` for target lists:
  `TextEffectRuntime.CollectBanishGy` (costs `BanishFromGyCount`; the source is excluded when its effect needs it on the
  field: `SourceMustStay`), `EngineHygieneRuntime.ProcedureBanishCandidates`, `CollectTargets` for a clause that banishes
  its GY targets (`BanishesGyTargets`, Book of Life's second target too; only zones whose Lua filters use SpElimFilter,
  `SubstitutesTargetsIn`: monster GY zones + Soul Release's `EitherGyCards`, not Disappear's `OppGyCards`; the source is
  a substitute too unless `SourceMustStay`, `TargetSubstituteExcept`: Witch Doctor can target itself), the legacy mirror
  `SpellTrapEffects.CollectLegalTargets` (`SubstituteLegacyGyTargets`), `FaceUpQuickEffectsRuntime.GyTargets` /
  `BanishableGyMonsters` (Soul Demolition, Skull Lair) and Fiend Comedian. A new banish-from-GY path must ask it too.
- Substitutes are on the field: GY locks (Necrovalley `GyMovementLocked`, Kycoo) apply only to cards still in a GY
  (`SpiritEliminationRuntime.InAGraveyard`); `DuelEngine.BanishCard` moves a field monster like any other.

### Bounce events and the "returned to your hand by your opponent's effect" window (v163)
- `DuelEngine.ReturnCardToHand(card, by)`: pass the reason player (the player whose card effect returns it; every current
  caller does). A Token leaves the field (never reaches the hand). A monster an opponent's effect returns to a hand is
  recorded by `MajorRiotRuntime.NoteReturnedToHand`; once idle `DuelEngine.OfferMonstersReturnedWindow` opens
  `ResponseTiming.MonstersReturnedToHandByOpponent` (turn player first; Damage Step / earlier phase returns are missed).
  Activation condition `MajorRiotRuntime.YourMonstersReturnedToHandByOpponent`; AI `MajorRiotRuntime.AiWantsToAnswer`.
- Both players Special Summon N from their own hand: `FlipMassEffectsRuntime.ResolveEachPlayerSummonsFromHandFaceDown`
  (PlanKind `OwnHandFaceDown`, `Seat.Limit`): face-down Defense, Level `SummonLevelMax` or lower Normal Summonable monsters
  that can be Special Summoned; a human picks through the plan's pending (`MonsterInHandOrDeck`), the AI auto-picks.

### Per-copy Deck identity (PerCopyDeck, v163)
- `DuelistState.Deck` is a `DeckPile` (`Duel/DeckPile.cs`): the `List<int>` surface (Count, indexer, Add, Insert, AddRange,
  Remove, RemoveAt, RemoveAll, RemoveRange, Clear, Contains, IndexOf, LINQ; `who.Deck = someList` converts) plus per-copy
  `DeckCopyInfo` (Owner, FaceUp, DrawTrigger) aligned with the ids. A plain id is an ordinary copy (owned, face-down).
  It is not an `IList<int>`: shuffle it only with `engine.Rng.Shuffle(who.Deck)` (the `DuelRng.Shuffle(DeckPile)`
  overload makes the list shuffle's RNG calls and moves the metadata). Read metadata with `InfoAt(i)`; add with
  `Add(id, info)` / `Insert(i, id, info)`. A copy taken out by `RemoveAt` and put straight back keeps its metadata.
- Into a Deck: `DuelEngine.ReturnCardToDeck(card, onTop, intoDeckOf, copyInfo)` — another player's Deck (the card stays
  its owner's) and/or face-up; the caller shuffles.
- Out of a Deck: `DuelEngine.CreateInstance` claims the copy just removed (`PerCopyDeckRuntime.ClaimDeckCopy`): a card held
  by a player who does not own it carries its owner in `CardInstance.ControlOwner` (as after a control change), so
  `PileOwner` (field exits, banish) and `RedirectAndReplaceRuntime.PutInGraveyard` (hand exits: `PerCopyDeckRuntime.OwnersPile`)
  send it to its owner's piles. A draw reports the copy to `PerCopyDeckRuntime.NoteDrawn` (face-up log, and the
  `EffectTiming.DrawnFaceUpFromDeck` trigger queued on the SEGOC Chain for a `DrawTrigger` copy).
- A path that made its instance before the removal (a pick from a Deck candidate list) must take the copy with
  `PerCopyDeckRuntime.TakeFromDeck(holder, index, pick)`: it removes the copy, gives the pick the owner and returns the
  `DeckCopyInfo`, so a failed move puts the same copy back with `Deck.Insert(index, id, info)`. A path that takes several
  copies out and puts them back (look at the top N) carries each one's info: `Deck.TakeAt(i)` + `Insert(0, id, info)`.
  An unclaimed `RemoveAt` record is forgotten at the next `CreateInstance`. Banish from a Deck into
  `PerCopyDeckRuntime.OwnersPile(holder, inst).Banished` (or `PileOwner` in DuelEngine), not `holder.Banished`.
- A card in your hand owned by the opponent (Graverobber): set `ControlOwner` to the owner; `PerCopyDeckRuntime.OnSpellResolved`
  (from `TextEffectRuntime.NotifySpellResolved`, which now takes the Spell) sees its activation resolve.
- A Spell/Trap whose own resolution moves it somewhere other than the GY: `PerCopyDeckRuntime.ConsumeWentToDeckInstead`
  in `TextEffectRuntime.FinishSpellTrap` (ygopro CancelToGrave; Pharaoh's Treasure).
- Residual: hand-to-Deck paths (shuffle the hand into the Deck) add plain ids, so a foreign card in a hand goes to the
  holder's Deck.

### v175 shared hooks (card batch v175-mfc-dcr; all under `TextEffects/Batches/<Group>Runtime.cs`)
- `DamageStepRulesRuntime`: `BeginBattle` / `EndBattle` (from `ContinueBeforeDamageCalculation`, `FinishDamageStep`), `FireEndOfDamageStep` (from `NotifyEndOfDamageStep`), `TryApplyDestroyAttacker`, `ReflectBattleDamage` (after the halving), `ApplyBoard`; piercing OR `TurnGrantsPiercing`; `BattleIndestructible` before `BattleMechanics.Calculate`; `NegationMonstersRuntime.MatchesCardKind` ("Effect" = any non-Token effect monster).
- `EquipLocksAndLeaveFieldRuntime`: `FilterTargets` in `CollectTargets` + `SpellTrapEffects.CollectLegalTargets` wrapper (Metalsilver: use `ShieldedFromSource(source)` in every NEW own-pick candidate list), `ApplyEquippedLevelChange` (host-scoped `ContinuousReduceLevel`), `BanishHostWhenThisLeaves` in `UnlinkCardFromHostOnLeave`.
- `DcrEquipSpellsRuntime`: `GrantsDirectAttack` / `ExtraAttacksAllowed` / `ChangeBattleDamage` (Equip grants, valid only while the Equip is face-up, equipped, un-negated), `TryAutoPick` for `AiEquipsOwnMonster`, `DiscardCostIsSend` (a send cost on `RequiresDiscardCost`), `ReturnToHand`.
- `SpellCounterSpendersRuntime`: holder predicate `IsSpellCounterHolder` / `CanAddSpellCounter`, side-wide cost `RemoveSpellCountersOnYourSide` (`TryPayOneIgnitionPick`, `BeginSeatCost` / `TryPaySeatPick` / `RefundOnCancel` for response seats), `PlaceOnActivation` (`PlaceSpellCountersAsActivated`), `CollectHolderTargets` (filters `ShieldedFromSource`).
- `ChainLinkAnswersRuntime`: `TryCanAnswer` / `TryActivateAnswer` / `TryResolveLink` (answers incl. hand Quick-Play via `CollectHandAnswers`, `AiWorthAnswer`), `CanAnswerChainLinkWithClause`, `NegatesRegisteredMonsterTarget` (Chaos Command Magician: call it from any inline targeted effect), `CostSendSpellsFromHandCount`; `NegateRequiresWouldDestroyMonsterOnField`.
- `FlipMassAndOpponentPicksRuntime`: `ActivateFlipTargets` (untargeted FLIP picks taken at activation on a SEGOC Chain), `ActivateSentToGyTargets`, `TryActivationCondition` (`YourMainPhaseWithNamedFaceUp`), `SummonAnswerLegal`; `GainLifePoints` `ScaleAmountByFaceUpEffectMonsters`.
- `PhaseTriggersAndIgnitionCostsRuntime`: `OnEndPhase` (from `FinishEndTurnAfterTriggers`), Mass Driver branches in `PerCountBurnsRuntime` face-up ignition hooks (`RequiresTributeCount`), `OppFaceUpTargets`, `SelfStatMonstersRuntime.ForbidsDirectAttack`.
- `UnionRidersRuntime`: `TryCanActivate` / `TryActivate` (Union-state ignitions), `InUnionState`, `SummonableUnions`, timing `EquippedMonsterInflictsBattleDamage`; `HostAlreadyHasUnion` counts only Unions in Union state; `TryDestroyEquipInsteadOfBattle` substitutes for every compiled Union in Union state.
- `SummonLocksAndStatRulesRuntime`: `BlocksFaceUpSummon(engine, ctrl, card)` (Rivalry, unique face-up rule; call it in any NEW summon candidate filter), `FilterTargets`, `Refresh`, `EndOfTurn`; `SpecialSummonLegality.CanBeSpecialSummoned(..., faceUp)` skips face-up-only rules for `faceUp: false`; `ContinuousProtections.CannotBeAttackTarget` supports `ExceptThisCard` + NamedCard series.

## 3. Tests

- Location: `Rules/Batches/<Group>RegressionTests.cs` with
  `public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string,bool,string> check)`.
  Register it with ONE line at the END of `Rules/CardBatchRegressionTests.Run`. That method runs inside
  `InteractionRegressionTests.RunAll`, so the headless harness and the Editor both run it. Add the `.meta` for every new file.
- Helpers in `Rules/InteractionRegressionTests.cs` (internal static): `Fresh(db, p, a)`, `ClearBoard(engine)`,
  `PutInHand(engine, who, id)`, `PlaceMonster(engine, who, id, zone, ...)`, `PlaceSetTrap(engine, who, id, zone)`,
  `ReachOpponentMain(engine)`, `ReachOpponentBattle(engine)`, `DrainCombat(engine)`.
- Every Implemented card needs:
  - a compile-shape check (`CardTextEffectCompiler.Compile(def)`: `FullyCompiled`, `Action`, `Zone`, key flags);
  - a mini-duel that drives the effect;
  - a near-miss card that stays not FullyCompiled.
  Every deferred card gets a check that it is not FullyCompiled and not Implemented.
- Corpus guards that pin cards as NOT FullyCompiled:
  - `Rules/TcgRegressionTests.cs` ("leftover unique is not FullyCompiled", e.g. Skill Drain);
  - `LocksAndZonesRegressionTests` (Ground Collapse; Curse of Fiend now pins its v160 compiled shape);
  - `NegationsAndGySummonsRegressionTests` (Type Zero, Skull Lair);
  - `TurnNegationTraps` (Armored Glass);
  - `RedirectAndReplace` / `EquipFamily` (Tailor).
  When you implement such a card, rewrite its guard to assert the new compiled shape. Do not delete the guard.
- Corpus stress: `Rules/CorpusTriggerStressTests.cs` (`NeedsHardBoard`, `Provision`, `TryTrap`) runs every FullyCompiled
  card in its window.
- Test gotchas:
  - a lone legal target resolves with no prompt (`!engine.IsAwaitingEffectTarget`);
  - the AI answers chains, so pick targets it cannot chain away;
  - use the queued RNG for coins and dice.
- Commands:
  - iterate: `headless.sh <tree> --card "<name>"`;
  - gate: `headless.sh <tree> --quiet`;
  - status: `--status-dump` + `status_diff.py`;
  - enums: `enum_guard.py --repo <tree>`;
  - whole Unity project: `csc_overlay.sh <tree>` (+ `--release`).
  A `--clause-dump` probe exists only as a scratch patch: `~/.cache/wrldz-card-compile/fix-opp-monster-targets/probe/mkprobe.sh`.

---------------------------------------------------------------------------------------------------------------------
## 4. Known engine gaps (missing subsystems; the blocked cards stay fail-closed)

| Gap | What is missing | Cards blocked |
|-----|-----------------|---------------|
| Card-name declaration picker (UI) + forbidden-card rules | No searchable name picker; no EFFECT_FORBIDDEN gate across summon/set/activate/apply paths | Prohibition |
| Empty-zone picker (UI) | `PendingActivation` picks cards, not zones; the zone picker is DuelUI-only | Ground Collapse |
| Revealed-hand state (UI) | No hand-public state; the UI cannot show the opponent's hand face-up | Ceremonial Bell, Respect Play, The Eye of Truth |
| Either-player (both-side) activation (EFFECT_FLAG_BOTH_SIDE) | Activating the effect of a face-up card on the OPPONENT's field. DuelUI taps only own S/T cards; `CollectLegalResponseCards`, `IsLegalResponseCard`, `LegalIntentService`, `DuelCommandService.DoActivate` and `FaceUpQuickEffectsRuntime.TryActivate` assume the activator controls the card. Needs UI coordination | Royal Oppression (also a face-up card answering the SummonNegation window), Fusion Gate (also a Fusion that banishes the materials) |
| Relinquished damage reflect | `MarkAbsorbed(RxRelinquishedDamageReflect)` with no runtime; ATK SET hides Equip UPDATE | latent: Thousand-Eyes Restrict (Structural); Relinquished is Implemented without the reflect |
| Forced Set-Trap activation mid-resolution | Activate a Set Trap for its controller inside another card's resolution, with no response window: `TryActivateSpellTrap` / `BeginDeferredChainActivation` seat a chainable link; the inline `TryResolveActivation` lineage misses Continuous placement-only Traps (`IsCompiledFreeChain` is false without an Activate clause, e.g. Gravity Bind) and its human cost / target picks open after the resolving card is gone with no chain hold. Also "free chain" (Lua EVENT_FREE_CHAIN) is not recorded per program (window-only Activate clauses look free-chain), and "shuffle into the Deck instead of the GY" for the resolving card itself | Bait Doll |
| Face-up Deck state | Per-copy face-up flags exist (`DeckCopyInfo.FaceUp`, v163), but no whole-Deck-reversed state or top-card display | Convulsion of Nature |
| ~~Draw result / lone-monster win / mass send / forced attack~~ | **Closed v163** (`LastTurnRuntime` + `DuelEngine`): a card effect declares the DRAW (`EndReason.CardEffect`, `DuelEngine` 2494-2499), the End Phase lone-monster check (`LastTurnRuntime.OnEndPhase`, 6501), the field+hands mass send, and the forced battle (`DuelEngine.ForceAttack` / `EndForcedBattle`, 5542-5574). Left over: no *natural* DRAW result when both players reach 0 LP in the same damage step — that is a rules question, not a card gap. | (none — Last Turn is Implemented) |
| Card-name declaration (extends the picker row) | Also needs an information lock ("cannot check the GY") | Dark Designator, Question |
| Phase jump ("it becomes the End Phase") | No gated transition points (Draw->Standby, Standby->Main1, post-Chain idle, end of Damage Step) and no Battle Phase termination; `EndTurn` is safe only from an idle Main/Battle Phase | Neko Mane King |
| Second target filtered by the first (Union bullet) + Union-in-S/T zone | Spell/Trap target paths support one target or independent groups; no "Union in Union state in your S/T Zone" zone in both target mirrors | Formation Union |
| Max-monster-zone cap (EFFECT_MAX_MZONE) | ~58 `FirstEmpty` placement/legality sites carry neither placing player nor reason; no forced-Tribute hook for EFFECT_UNRELEASABLE_SUM | Kaiser Colosseum |
| Must-attack obligation (EFFECT_MUST_ATTACK) | Only `MustAttackDirectlyThisTurn` restricts targets; nothing stops leaving the Battle Phase (DuelUI End Battle / End Turn must refuse) and SimpleAi has no forced-attack duty | Amazoness Archers, Diffusion Wave-Motion, Staunch Defender |
| Copy another card's effect | No path resolves another card's compiled program as the activator's own link | Double Spell |
| Standby Phase window for a Set Normal Trap | `ResponseTiming` has no Standby window; `StandbyPause` holds only the turn player's upkeeps | Thunder of Ruler |
| Simultaneous hidden pick + reveal (UI) | extends "Revealed-hand state" | Ante |

Open residuals (fail-soft, not fixed):
- Tribute, discard and banish cost paths still fire optional "When" GY effects of the cost card inline.
- Magic Reflector cannot target a monster Card acting as an Equip Spell (Sword Hunter, Unions); fails soft.
- Blast with Chain (and Reinforcements, Rush Recklessly) cannot be activated in the Damage Step: no Damage Step window
  for ordinary free-chain Traps.
- Trigger sites still resolved inline, with no Chain window (TriggerChainResponses, v163): triggers in the Damage Step
  (battle-destroyed GY triggers, a FLIP flipped by an attack), triggers during an attack or a paused Summon / phase, sent-to-GY triggers of costs (Tribute, discard), "When
  … you can" GY triggers, monster summon triggers (`TryResolveThisCardSummoned`), FLIPs flipped mid-Chain without
  `QueueFlipEffectAfterResolution` (`TextEffectRuntime` position-change helper, `FlipDefCheckRuntime`), Spirit's
  Invitation for a Spirit returned mid-Chain, every other Standby / End Phase trigger (only Life Absorbing Machine is
  seated), and a SEGOC Chain while another one resolves. Held / "position changed" triggers still activate as they
  resolve (a responder answers the link before it activates).
- Royal Command negates only FLIP activations made while it is face-up (Lua EVENT_CHAIN_ACTIVATING): a FLIP link that
  activated before it was chained resolves (`SimultaneousTriggersRuntime.ResolvesActivatedFlip` lifts
  `RuleLocksRuntime.SkipsFlipMonsterClause` for that resolution).
- Life Absorbing Machine's payments resolve as one link (ygopro: one activation per payment).
- `CanActivateChainResponse` refuses every `ChainLinkActivated` answer against a trigger link ("a card effect, not a card
  activation"): right for every compiled answer (all answer a Spell/Trap card's activation: Solemn Judgment, Seven Tools,
  Magic Jammer). A "When a monster effect is activated" answer (Divine Wrath, RDS, Unimplemented; no clause flag yet) must
  add its own flag and be exempted there so it can negate a FLIP / monster trigger link (v163 review residual).
- `OppMonsters` siblings are face-up-only in `IgnitionTemplates` / `MonsterTriggerTemplates` (list in
  `fix-opp-monster-targets/NOTES.md`).
- Inspection's PAY/CANCEL prompt carries into the Battle Phase when Timeater skips Main Phase 1 (no soft-lock).
- Older per-batch pick dictionaries still route through their own `TryResolvePick` / `TryCancelPick` hooks (v162:
  BattleOutcomeTriggers, BattleDestroyedGyRecovery, StandbyBurnsAndDice Inspection; earlier batches too). Their flows are
  multi-step (placement, LP choice, queued Inspection) — move one onto `AskCardPick` only with a test proving it unchanged.
- Cancel at a monster ignition's target pick after a Tribute / banish / send cost (Exiled Force, Freed the Brave
  Wanderer) drops the link but keeps that cost paid (only discards and LP are refunded).
- v175 audit residuals: Physical Double ignores Rivalry of Warlords in its target check (Token summon refused, no soft-lock); `AiEquipsOwnMonster` `|| ` arm in the FieldAnyMonster `AutoPick` case is dead (DcrEquipSpells `TryAutoPick` runs first); two send-from-hand cost primitives (`DiscardCostIsSend`, `CostSendSpellsFromHandCount`) and `PayChainDiscard` ignores the former; `ReflectBattleDamage` before `ChangeBattleDamage` not checked against ocgcore; My Body as a Shield ignores `ExceptThisCard`; Union-state ignitions resolve with no opponent chain window.


---------------------------------------------------------------------------------------------------------------------
## 5. Traps already paid for

- Enums are append-only. The seed stores `EffectActionKind`/`EffectTiming`/`EffectZoneFilter`/`EffectSide` as ints; a
  mid-enum insert (v120) shifted every program. Append at the END; `enum_guard.py`.
- Version + seed: any change to compiler output needs a bump above every number ever used (README "Versions") plus
  `--export-seed`. Mid-batch peer landings do not bump. A scratch copy never bumps.
- Stub vs Structural: a FullyCompiled program with no clauses is Structural only with `HasLiveSummonPath`; otherwise it
  is Unimplemented (the Sarcophagus cards). One unparsed sentence makes a Stub; it is never Implemented.
- Two target mirrors: `CollectTargets`/`MapTargetKind`/`AutoPick` and the legacy
  `MapCompiledTargetKind`/`CollectLegalTargets`/`AutoPickTarget`. Change both or neither.
- Amount is not a target count. Only `TargetPicks` / `AmountIsUpToTargetCount` turn it into picks. Amount is also ATK,
  Level cap and coin heads.
- "Monster your opponent controls" includes face-down monsters (v152). Printed "face-up" uses `OppFaceUpMonsters` /
  `RequiresTargetFaceUp`. The DuelUI picker hides Sets (`HiddenSetZone`).
- Cost vs effect: the cost is paid at activation, re-checked nowhere, and refunded on Cancel. The effect re-checks its
  targets at resolution (`AllExactTargetsStillThere`).
- Missing the timing: a card sent as a cost misses its optional "When ... you can" effect (`SendToGraveAsCost`); mandatory
  effects still apply.
- The generic PSCT path drops conditions silently unless `PsctConditionGate` / `IsWholeResolution` catch them. After any
  change, compare clause shapes and check that no unrelated card changed status.
- `TextEffectRuntime.DestroyCard` is internal with an optional `byPlayer`. Pass `who` so the "destroyed by opponent"
  records are right.
- `CardInstance.Race` / `Attribute` are live values; `Def.race` is the printed one. Field reads use the live value.
- A Continuous S/T destroyed in response to its own activation is not re-seated (StandbyGrowth
  `ContinuousActivationLeftField` guard in `ResolveChainStack`).
- The headless harness compiles only `Data/**`, `Duel/**` and listed `Core/` files. UI changes need `csc_overlay.sh`.
- Shared-file conflicts at integration are almost always two tail appends (enum, `BatchAtomSets`,
  `CardBatchRegressionTests.Run`, the `CollectTargets` filter tail, the `ActivationConditionsMet` default chain). Keep both.
