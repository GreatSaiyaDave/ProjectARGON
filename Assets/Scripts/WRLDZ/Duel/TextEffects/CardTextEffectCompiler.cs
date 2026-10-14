using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Compiles official Konami/Yugipedia card text (<see cref="CardDef.desc"/>) into a
    /// <see cref="CompiledCardProgram"/> the first time a card is played.
    ///
    /// Approach: PSCT template matching (not free-form LLM). Only clauses that fully match
    /// a known template are stored — never invent partial effects.
    /// Reference style: open-source YGOPro Lua scripts (timing + operation), Unity-native C#.
    /// </summary>
    public static class CardTextEffectCompiler
    {
        public const int Version = 181; /* SOD simple weapon group (Fusion Weapon/Ritual Weapon) unique equip exceptions */ /* v180: coverage families (integration in progress; the finalizer rewrites this note) */ /* v179: LvChain batch (Horus LV4/LV6, Ultimate Insect LV1, Dark Mimic LV3, The Graveyard in the Fourth Dimension) + GyHand summon-event triggers; new LvChainRuntime (LV progression, WasSummonedThisTurn gate), phase-trigger ActivationCondition enforcement, DrawAmountWhenSpecialSummonedByNamed clause; tests Rules/Batches/LvChainRegressionTests.cs */ /* v177: card batch v177-ioc-ast (55 cards Implemented in 8 groups; 61 in the batch with 6 already Implemented — ConditionalSpellsAndTraps: Blasting the Ruins, Chaos End, Chaos Greed, Self-Destruct Button, Ojama Delta Hurricane!!, Gryphon's Feather Duster, Destruction Ring, D.D. Borderline; EndOfDamageStepRiders: Getsu Fuhma, Ryu Kokki, Prickle Fairy, Insect Princess, Enraged Battle Ox, Drillago, Two Thousand Needles, Legendary Jujitsu Master; BattleDamageEvents: Begone, Knave!, Robbin' Zombie, Sasuke Samurai #3, Aswan Apparition, Nubian Guard, Des Counterblow, Piranha Army, Stone Statue of the Aztecs; SpellEventsAndHandSendTriggers: Elephant Statue of Blessing/Disaster, Regenerating Mummy, Curse of Darkness, Tower of Babel, Spirit of the Pot of Greed, Fiend's Hand Mirror; RecoveryAndPhaseCosts: Dimension Distortion, Dimension Fusion, Salvage, Recycle, Reload, Coach Goblin, Manticore of Darkness; TokensDeclarationsAndPicks: Stray Lambs, Scapegoat, Multiplication of Ants, DNA Transplant, Jade Insect Whistle, A Hero Emerges, Wild Nature's Release, Balloon Lizard; ChaosBanishSummons: Chaos Sorcerer, Black Luster Soldier - Envoy of the Beginning, Silpheed, Strike Ninja, Chaosrider Gustaph, Soul Absorption, Primal Seed; TargetingTruth: Fissure, Terrorking Archfiend, Vilepawn Archfiend, Different Dimension Dragon, Metalsilver Armor, Chaos Command Magician, Gora Turtle of Illusion, Big Burn) */ /* v176: card batch v176-dcr-ioc (59 cards Implemented in 9 groups — SimpleTriggersAndSwarm: Tsukuyomi, Burning Algae, Outstanding Dog Marron, Little-Winguard, Don Turtle, Mustering of the Dark Scorpions, Dark Scorpion Combination; DcrSpellTrapUtility: Ray of Hope, Spell Reproduction, Frozen Soul, Arsenal Robber, Interdimensional Matter Transporter, Dragged Down into the Grave, Final Countdown, Different Dimension Gate; BattleDestroyedTriggers: Kelbek, Keldo, Agido, Kaiser Glider, Different Dimension Dragon, Shinato King of a Higher Plane, Des Kangaroo; GuardiansAndEquipEvents: Guardian Elma, Chopman the Desperate Outlaw, Guardian Grarl, Guardian Baou, Fuhma Shuriken, Morale Boost; DerivedAtkMonsters: Great Maju Garzett, Maju Garzett, Guardian Tryce, Chaos Necromancer, Gren Maju Da Eiza; NamedSummonPairs: A Deal with Dark Ruler, Berserk Dragon, Contract with Exodia, Exodia Necross, Mirage Knight, Dedication through Light and Darkness; ArchfiendRiders: Shadowknight Archfiend, Terrorking Archfiend, Vilepawn Archfiend, Infernalqueen Archfiend, Desrook Archfiend, Checkmate, Pandemonium, Pandemonium Watchbear; SpellTrapAnswersAndActivationLocks: Judgment of Anubis, Spell Vanishing, Cursed Seal of the Forbidden Spell, Trap Jammer, Sasuke Samurai #2, Invader of Darkness; MonsterNegationAndControlLocks: Skill Drain, Final Attack Orders, Blindly Loyal Goblin, Mataza the Zapper, Stumbling, Non-Spellcasting Area; deferred Goblin of Greed) */ /* v175: card batch v175-mfc-dcr (57 cards Implemented in 9 groups — DamageStepRules: D.D. Crazy Beast, Thousand Needles, Continuous Destruction Punch, Ultimate Obedient Fiend, Meteorain, Kishido Spirit, Cat's Ear Tribe, Amazoness Swords Woman; EquipLocksAndLeaveField: Demotion, Big Bang Shot, Autonomous Action Unit, Raregold Armor, Metalsilver Armor; DcrEquipSpells: Shooting Star Bow - Ceal, Twin Swords of Flashing Light - Tryce, Wicked-Breaking Flamberge - Baou, Rod of the Mind's Eye, Rod of Silence - Kay'est, Butterfly Dagger - Elma; SpellCounterSpenders: Magical Plant Mandragola, Mega Ton Magical Cannon, Miracle Restoring, Anti-Spell, Apprentice Magician, Armor Exe, Legendary Flame Lord, Pitch-Black Power Stone; ChainLinkAnswers: Jar Robber, Spell Shield Type-8, My Body as a Shield, Chaos Command Magician, Dramatic Rescue; FlipMassAndOpponentPicks: The Spell Absorbing Life, Dark Cat with White Tail, Huge Revolution, Magical Merchant, Pixie Knight, Pineapple Blast, Dimension Jar; PhaseTriggersAndIgnitionCosts: Amazoness Blowpiper, Helping Robo for Combat, Mass Driver, Senri Eye, Magical Scientist, Roulette Barrel, Physical Double; UnionRiders: Kiryu, Second Goblin, Burning Beast, Freezing Beast, Metallizing Parasite - Lunatite, Union Rider, Combination Attack; SummonLocksAndStatRules: Gyaku-Gire Panda, Amazoness Tiger, Amazoness Spellcaster, Rivalry of Warlords; deferred Neko Mane King, Formation Union, Kaiser Colosseum) */ /* v174: card batch v174-mfc (2 cards Implemented: Emblem of Dragon Destroyer, Hidden Spellbook; Thunder of Ruler stays fail-closed — no activation path for a Set Normal Trap on pure StandbyPhase timing); new EffectActionKind AddNamedFromDeckOrGyToHand, ShuffleGySpellsIntoDeck, ForbidBattlePhaseThisTurn (reserved); new EffectZoneFilter ControllerDeckOrGyNamed; EffectVocabulary Search/ReturnToDeck mappings; corpus Provision seats the named Deck card; tests Rules/Batches/MfcSimpleSpellsBRegressionTests.cs */ /* v173: card batch v173-dcr (integration in progress; the finalizer rewrites this note) */ /* v172: MfcSimpleSpells v168 audit — the six v168 atoms were written against invented card texts and their test against placeholder ids absent from cards_db (HEAD ran 5839/6); the fake atoms are removed and Rules/Batches/MfcSimpleSpellsRegressionTests.cs now pins the real ids: Dark Magician Girl Implemented via MfcLabCards3Atoms' real-text atom fixed to the printed 'Gains 300 ATK for every ... in the GY' (ATK only, DefAmount 0), Anti-Spell and Mass Driver stay fail-closed per WholeResolutionRegressionTests/SpellCounterRegressionTests, Emblem of Dragon Destroyer, Hidden Spellbook and Thunder of Ruler stay fail-closed queued for the next run; seed reissued */ /* v171: Mecha-Dog Marron atom fix — RxBattleGyInflictBoth now emits ONE SentFromFieldToGy InflictDamageToBothPlayers clause (Side=Both, RequiresThisDestroyedByBattle); the redundant TakeEffectDamage Controller clause from v170 burned the controller twice (4000 vs the expected 3000 total against Blue-Eyes); BattleOutcomeTriggersRegressionTests compile-shape check expects 2 clauses; seed reissued so the shipped program matches fresh compile */ /* v170: Mecha-Dog Marron — added RxBattleGyInflictBoth (battle-destroy inflict-damage to both players, two-clause atom) and RxDestroyedFieldExceptBattleInflict (destroyed except by battle → opponent damage); added RequiresNotDestroyedByBattle flag; added EffectActionKind.InflictDamageToBothPlayers */ /* v168: MFC batch */ /* v167: Union equip/unequip subsystem */ /* v167: Union equip/unequip subsystem */ /* /* v166: SpellCounter group QC follow-up — Marionette's 'target 1 monster on the field' now compiles to EffectZoneFilter.FieldAnyMonster; FieldMonsters (the controller's own) made the clause unactivatable, since the engine found no legal target and refused every activation. Discovered by Rules/Batches/SpellCounterDuelRegressionTests.cs, which drives the four Implemented cards through a real duel instead of only checking clause shape */ /* v165: card batch v164-mfc SpellCounter group, final — AdvancedEffectTemplates Spell Counter anchors span their whole sentence (leading 'You can'/'This card' and the trailing period used to survive as residue and left the card a Stub); RxRemoveCounterDestroyMonster added for the monster-target cost (Magical Marionette); RxPlaceOnSpell's (max. N) made optional; PrintedMax() returns 0 for a card that prints no maximum so the compiler cannot invent a cap (v164 exported a pre-fix cached program that clamped Marionette at 3); 4 cards Implemented (Royal Magical Library, Magical Marionette, Skilled Dark Magician, Skilled White Magician), Blast Magician + Tower of Babel Unimplemented -> Stub; fail-closed preserved for Anti-Spell, Miracle Restoring, Blast Magician; tests Rules/Batches/SpellCounterRegressionTests.cs; report Docs/CURRICULUM_BATCH_v164-mfc.md */ /* v164: card batch v164-mfc SpellCounter group (4 cards Implemented: Royal Magical Library, Magical Marionette, Skilled Dark Magician, Skilled White Magician; Blast Magician + Tower of Babel Unimplemented -> Stub from the shared counter sentence) — AdvancedEffectTemplates Spell Counter anchors now span their whole sentence (the 'You can' / 'This card' prefix and the trailing period used to survive as residue and left the card a Stub), RxRemoveCounterDestroyMonster added for the monster-target cost (Marionette), RxPlaceOnSpell's (max. N) made optional (Marionette prints none); fail-closed preserved for Anti-Spell, Miracle Restoring, Blast Magician; tests Rules/Batches/SpellCounterRegressionTests.cs; report Docs/CURRICULUM_BATCH_v164-mfc.md */ /* v163: card batch v163-pgd-mfc (41 cards Implemented, 748 -> 789, 9 Stubs resolved, 0 losses; seed 853 -> 885 programs) — ContinuousLocksAndIgnitions: Arsenal Bug, Buster Rancher, Call of the Mummy, Frontline Base, Gora Turtle, Non Aggression Area, Timidity; PhaseTimersAndDeckTop: A Cat of Ill Omen, An Owl of Luck, Bottomless Shifting Sand, Different Dimension Capsule, Mirage of Nightmare, Pyro Clock of Destiny, Royal Tribute; CallsDiceAndSetTriggers: Cobra Jar, Dark Coffin, Dice Jar, Ordeal of a Traveler, Reasoning, Reversal Quiz, Statue of the Wicked; TargetNegationsAndHandQuickEffects: Charm of Shabti, Curse of Royal, Fushioh Richie, Gravekeeper's Watcher, Great Dezard; SummonTriggersAndTributes: Byser Shock, Dark Dust Spirit, Gravekeeper's Chief, Jowls of Dark Demise, King Tiger Wanghu, Metamorphosis, Moisture Creature; SummonPromptsAndCosts: Tyrant Dragon, Vampire Baby; ForcedActivationAndSubstitution: Major Riot, Spirit Elimination; engine-only groups, 0 cards each: EngineResiduals (generic human card pick CardPickRequest + ContinuousTrapsAndSpiritSupportRuntime.AskCardPick, Cancel answer, human Chain-answer discard cost), LastTurn (DRAW duel result, End Phase lone-monster check), TriggerChainResponses (trigger sites seat a real engine.Chain link and open a response window), PerCopyDeck (Duel/DeckPile.cs: DuelistState.Deck is a DeckPile keeping the List<int> surface, per-copy ownership / face-up state via DeckCopyInfo); deferred: Narrow Pass, Bait Doll (forced Set-Trap activation mid-resolution) — report Docs/CURRICULUM_BATCH_v163-pgd-mfc.md */ /* v162: card batch v162-pgd (44 cards) — EffectDamageEvents: Barrel Behind the Door, Trap of Board Eraser, Dark Room of Nightmare, Gravekeeper's Vassal, Coffin Seller; PgdSpellTrapBasics: Book of Taiyou, Curse of Aging, D. Tribe, Pyramid Energy, Secret Pass to the Treasures, Needle Ceiling, Hieroglyph Lithograph, Card Shuffle; BattleOutcomeTriggers: Zombyra the Dark, Timeater, Kryuel, Mystical Knight of Jackal, Winged Sage Falcos, Dark Scorpion Burglars, Don Zaloog, Mucus Yolk; FlipDownAndPeek: 8-Claws Scorpion, Royal Keeper, Wandering Mummy, Giant Axe Mummy, Cobraman Sakuzy, A Man with Wdjat, Trap Dustshoot; StandbyBurnsAndDice: Blind Destruction, Needle Wall, Graverobber's Retribution, Dark Snake Syndrome, Mask of Dispel, Nightmare Wheel, The Unfriendly Amazon, Inspection; BattleDestroyedGyRecovery: Crimson Sentry, Lady Panther, The Forgiving Maiden, Fairy Guardian, Return of the Doomed, Revival Jam, Helpoemer, Rope of Life */ /* v161: card batch v161-lod-b (43 cards) — BothSideAndMonsterEquip: Sword Hunter; CoinDiceInterrupts: Second Coin Toss, Dice Re-Roll; SpecialSummonEffects: Skull Knight #2, Twin-Headed Behemoth, Winged Minion, The Puppet Magic of Dark Ruler, Gradius' Option; ContinuousTrapsAndSpiritSupport: Fatal Abacus, Life Absorbing Machine, Ominous Fortunetelling, Fengsheng Mirror, Spirit's Invitation, Spiritual Energy Settle Machine; EquipAndSpellProtection: Fusion Sword Murasame Blade, Heart of Clear Water, Smoke Grenade of the Thief, Blast with Chain, Woodland Sprite, Magic Reflector; DeckReturnsAndMassEffects: Fiber Jar, Mysterious Guard, Serpentine Princess, Creature Swap, Bubble Crash; BattleRules: Cave Dragon, Thunder Nyan Nyan, Array of Revealing Light, After the Struggle, The Secret of the Bandit, Patrician of Darkness; BattleBoosts: Spear Dragon, Dragon's Rage, Gray Wing, The Hunter with 7 Weapons, Injection Fairy Lily, Spirit Ryu; NegationMonsters: Freed the Matchless General, Frontier Wiseman, Dark Ruler Ha Des, Twin-Headed Wolf, Lesser Fiend, Double Snare (Tyrant Dragon Stub); deferred: Royal Oppression, Fusion Gate (both-side activation UI), Tyrant Dragon GY-summon Tribute cost — report Docs/CURRICULUM_BATCH_v161-lod-b.md */ /* v160: card batch v160-lod (28 printings) — FaceUpQuickEffects: Type Zero Magic Crusher, Skull Lair, Soul Demolition, The Dragon's Bead; EquipModel: Armored Glass, Tailor of the Fickle; SimultaneousTriggers: Curse of Fiend; EngineHygiene: Spirit of Flames, Inferno, Ekibyo Drakmord, Forced Requisition, Super Rejuvenation; SimpleSpellTraps: A Wingbeat of Giant Dragon, Burst Breath, Dragon's Gunfire, Ready for Intercepting, Fiend Comedian, Emergency Provisions, The A. Forces; SpiritMonsters: Asura Priest, Inaba White Rabbit, Fushi No Tori, Great Long Nose, Susa Soldier, Otohime (both printings), Maharaghi; deferred: Royal Oppression, Sword Hunter — report Docs/CURRICULUM_BATCH_v160-lod.md */ /* v159: card batch v159-lon-b (26 cards) — FlipCostsAndFusion: Fire Sorcerer, Lady Assailant of Flames, Supply, Summoner of Illusions, De-Fusion; EquipFamily: Dragonic Attack, Scroll of Bewitchment, Collected Power, Dark Necrofear; BattleWindows: Fairy Box, Jam Defender, Destruction Punch, Michizure, Dreamsprite, Dark Spirit of the Silent; RuleLocks: Jowgen the Spiritualist, Jam Breeding Machine, Royal Command, Kycoo the Ghost Destroyer, Chain Energy; DestinyBoard: Destiny Board, Spirit Message I/N/A/L; NegationsAndGySummons: Maryokutai, Riryoku Field, Gilasaurus, Card of Safe Return, Chosen One; deferred: Tailor of the Fickle, Armored Glass, Type Zero Magic Crusher, Skull Lair — report Docs/CURRICULUM_BATCH_v159-lon-b.md */ /* v158: hardening of the Grok-landed cards. Bots compiled 25 cards in the real checkout 09-30..10-02 with no review or tests: v153 batch groups TargetedClassics / PositionLocks / EquipRiders / SelfStatMonsters / StandbyPhaseEffects / BattleDestroyedRecovery park, and v154-v157 shared atoms. Reviewed and tested: 98 checks in Rules/Batches/Grok*HardeningRegressionTests.cs. Fixed: Tribe-Infecting Virus resolves after leaving the field; Cyclon Laser equips only Gradius; Mask of the Accursed Standby burn; Token Thanksgiving needs a Token; Poison of the Old Man and Rock Bombardment as seated responses; Stamping Destruction needs a face-up Dragon. Failed closed: Curse of Fiend (simultaneous FLIP), Spirit of Flames (no summon procedure), Ekibyo Drakmord (no runtime, Stub). Covers the peer landings OppFaceDownSiblings and EquipSpellCost (Guardian Ceal). The v153 batch run was abandoned; its 9 unstarted groups go to the next batch. */ /* v157: the face-up Attribute monster(s) destroyed-and-sent sentence is not compiled (Backfire stays unparsed; no off-chain 500). */ /* v156: shared sentence atoms — send 1 Type monster from your Deck to the GY as cost then inflict N; a face-up Attribute monster(s) you control destroyed and sent to the GY inflicts N once per simultaneous event; discard 1 and declare 1 Type then destroy all face-up of that Type; destroy all tokens then gain N LP per token destroyed. */ /* v155: the control-a-Type condition is ConditionName, not RaceFilter, so a Spell/Trap target is not filtered by that Type. */ /* v154: shared sentence atoms for an untargeted two-bullet Activate choice (Gain N LP / Inflict M damage), opponent LP higher than N, an equip's battle-damage LP, destroy-then-damage to its controller, and an ATK-or-DEF equip choice. RxTargetStDestroy no longer consumes a destroy followed by a comma. */ /* v153: card batch v153-lob-srl-psv-lon (integration in progress; the finalizer rewrites this note) */ /* v152: 'Target 1 monster your opponent controls' includes face-down monsters: CardTextEffectCompiler.ParseActivationTarget maps it to OppMonsters (Swarm of Scarabs, Thousand Knives, Golem Sentry; Lua SelectTarget(aux.TRUE, 0, LOCATION_MZONE)), a printed 'face-up' keeps OppFaceUpMonsters, 'target 1 face-up monster on the field' sets RequiresTargetFaceUp (Dark Core), and the zone follows the matched phrase; the legacy SpellTrapEffects mirror maps OppMonsters to the face-down-inclusive EffectTargetKind.OppMonster list (new CollectLegalTargets case; Spellbinding Circle can bind a Set monster). 4 programs change, status +0; tests Rules/Batches/OppMonsterTargetsRegressionTests.cs */ /* v151: fail closed: every generic PSCT resolution fallback in CompilePsctSentence must be the whole resolution (IsWholeResolution), an 'it'/'that target' with no parsed target is refused instead of guessing a zone, 'Each time …:' is an unrecognized trigger, an unparsed Spell Counter cost refuses the sentence, and ', even during the Damage Step' is not a timing. Implemented -> Unimplemented: Anti-Spell, Armor Break, Curse of Royal, Remove Trap, Granmarg the Rock Monarch, White Ninja, Nobleman of Crossout, Summoner of Illusions, Solar Ray, Mass Driver, Fire Sorcerer, Type Zero Magic Crusher, Graverobber's Retribution, Revival Jam, Miracle Restoring, Ready for Intercepting; 16 Stubs lose their only (partial) clause */ /* v150: Toons (parked from v142, landed by the landing-queue hub): Manga Ryu-Ran, Blue-Eyes Toon Dragon, Toon Mermaid, Toon Summoned Skull — hand Special Summon procedure (Tribute N while you control Toon World; SpecialSummonFromHandProcedure), cannot attack the turn it is Special Summoned, pay 500 LP to declare an attack, destroyed when Toon World is destroyed (DestroyThisWhenNamedDestroyed), direct attack unless the opponent controls a Toon (CanAttackDirectlyUnlessOpponentControlsToon); the DuelUI hand Special Summon reads the Tribute count from ToonsRuntime.HandProcedureOf. */ /* v149: FlipDefCheck — Acid Trap Hole compiles to FlipFaceUpDestroyIfDefLeqElseSetFaceDown (target 1 face-down monster, flip it face-up; destroy it if its DEF is 2000 or less, otherwise return it face-down; new zone FieldFaceDownMonsters); the generic 'destroy (that target|it)' fallback refuses 'destroy it if …' / 'then destroy it' (fail closed): De-Spell Stub -> Unimplemented. Landed, bumped and seeded by the landing-queue hub for the Acid Trap Hole session. */ /* v148: SpiritReturnPsct — the PSCT Spirit-return prints ("During the End Phase of the turn this card is Normal Summoned or flipped face-up: Return it to the hand." — Asura Priest, Otohime ×2, Dark Dust Spirit — and Tsukuyomi's "Once per turn, during the End Phase, if this card was Normal Summoned or flipped face-up this turn: Return it to the hand.") compile via whole-sentence atoms to the shared Spirit return (EndPhase ReturnToHand, RequiresSummonedOrFlippedThisTurn); Asura Priest Unimplemented → Stub. Landed by the D.D. Scout Plane session; bump and seed by the landing-queue hub. */ /* v147: CompilePsctSentence refuses no-colon trigger text (When/If/Each time/Every time) before the generic and draw fallbacks: Dark Room of Nightmare, Poison Fangs, Card of Safe Return -> Unimplemented; Spirit's Invitation -> Stub; "7", The First Sarcophagus, Pitch-Black Power Stone, Goblin Fan, Ominous Fortunetelling Stub -> Unimplemented. Queue slot 7 (the no-colon triggers + Card of Safe Return sessions), landed, bumped and seeded by the landing-queue hub. */ /* v146: Graceful Charity: whole-text atom 'Draw N cards, then discard M cards.' → Activate DrawThenDiscard (EffectClause.ThenDiscardCount; activation needs IsPlayerCanDraw N; the human picks the discards via PendingActivation.AwaitingDrawThenDiscard, Cancel completes; the AI discards its lowest DiscardPriority cards; discard only if all N were drawn). The PSCT draw fallback and IgnitionTemplates.ParseResolution refuse a draw followed by ', then …' (fail closed); no status changes. Landed by the Graceful Charity session; bump and seed by the landing-queue hub. */ /* v145: PsctConditionGate — CompilePsctSentence and its ignition templates fail closed on conditions before the colon they cannot compile (Cipher Soldier, Coffin Seller, Gearfried the Swordmaster, Michizure, Rope of Life, Z-Metal Tank, Heavy Mech Support Platform; Stubs Asura Priest, Dark Magician of Chaos, Dark Scorpion - Chick, Great Dezard, Kunai with Chain, Silent Swordsman LV5, Spiral Spear Strike), RxDirectAttackUnconditional skips 'if' riders (Drillago, Alligator's Sword Dragon), and Gear Golem's 'during your Main Phase 1' is compiled and enforced (EffectClause.OnlyDuringMainPhase1). Landed by the D.D. Scout Plane session; bump and seed by the landing-queue hub. */ /* v144: partial compiles made whole or fail closed — Assault on GHQ: targeted Destroy of a monster you control (face-down too), then mill 2 only if it was destroyed (EffectClause.ThenMillOpponentDeck; seated through the YourMonster mirror like Dimensionhole, so the opponent may chain); Dark Scorpion - Cliff the Trap Remover / Gorg the Strong: "you can activate 1 of these effects" (EffectClause.EffectChoiceIndex, EffectChoiceRuntime: existing picker, this card = the untargeted bullet, CANCEL = neither; AI prefers hitting the opponent; Gorg returns the target to its owner's Deck top via DuelEngine.ReturnCardToDeck(onTop) and never the monster its battle destroyed); IsBoilerplate no longer drops a leftover "●" bullet (Dark Scorpion - Chick the Yellow Implemented -> Stub); PSCT fallback fails closed on "destroy it, then …" and "both players / each player draw N" (Dragged Down into the Grave Implemented -> Unimplemented) */ /* v143: fail closed — an Activate sentence that compiles no action is refused even when an LP cost was read from it (the generic PSCT path's bare-payment fallback is gone): Chain Energy, Royal Oppression, Weapon Change, Inspection Implemented -> Unimplemented; Card Shuffle, Senri Eye, Soul Demolition Stub -> Unimplemented; Toon World unchanged (ContinuousRestrictionTemplates) */ /* v142: card batch v142-mrd-srl-psv-b (36 cards) — DiscardTriggers: Electric Snake, Minar, Penguin Knight, Delinquent Duo (EffectTiming.DiscardedFromHandByOpponentEffect / SentFromDeckToGyByOpponentEffect, ShuffleGraveyardIntoDeck, OpponentDiscardsRandomThenOneOfTheirChoice); BattlePhaseEnd: Goblin Attack Force, Giant Orc, Steel Scorpion, Reflect Bounder (EffectTiming.EndOfBattlePhase / BeforeDamageCalculation, ChangeThisToDefenseAfterAttackingLockPosition, DestroyAttackerAtOpponentsLaterEndPhase); SpellTrapDestroyedResponse: Gust, Driving Snow, Weather Report (YourCardsDestroyedByOpponentEffect window, ConductBattlePhaseTwice; Swords of Revealing Light now queues FLIP effects); TypeDeclaration: DNA Surgery, The Regulation of Tribe, Earthshaker (DeclareMonsterType, AllFaceUpMonstersBecomeDeclaredType, StandbyTributeOrDestroyThis, DestroyAllFaceUpOfAttributeOpponentPicksFromTwo, zone DeclaredMonsterTypes; CardInstance.Race is live); LocksAndZones: Cold Wave, Dimensionhole, Light of Intervention (LockSpellTrapPlayAndSetUntilYourNextTurn, BanishTargetUntilYourNextStandbyPhase, MonstersCannotBeSetOrTurnedFaceDown, StartOfMainPhase1); HandDisruption: Gamble, Appropriate, Painful Choice (CoinCallDrawUntilOrSkipNextTurn, DrawWhenOpponentDrawsOutsideDrawPhase, RevealDeckCardsOpponentPicksOneToHand); FlipMassEffects: Cyber Jar, Morphing Jar #2, Jigen Bakudan, Spear Cretin (CyberJarStyle templated, RecordThisCardFlipped, TributeThisDestroyYourMonstersBurnHalfAtk, EachPlayerSpecialSummonsOneFromOwnGy, ShuffleFieldMonstersIntoDeckExcavateSummon); RedirectAndReplace: Fairy's Hand Mirror, Shift, Kotodama, Banisher of the Light (RedirectToAnotherAppropriateTarget, DestroyNewFaceUpMonstersSharingAName, BanishCardsSentToGyInstead, zone AnotherAppropriateTarget); StandbyGrowthAndMaintenance: Castle of Dark Illusions, Pumpking the King of Ghosts, Mirror Wall, Skull Invitation (GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp, GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby, HalveAtkOfOpponentAttackersWhileThisOnField, InflictDamageToOwnerPerCardSentToGy; UpkeepIsOptional makes pay-or-destroy upkeeps a real choice); SpiritsAndMisc: Yata-Garasu, Hino-Kagu-Tsuchi, Yamata Dragon, Kiseitai (DrawUntilHandSize, DiscardOpponentHandAtNextDrawPhase, BecomeEquipOnFaceDownAttacker, GainLpHalfEquippedMonsterAtk; Spirit return follows proc_spirit); deferred: Toons x4 parked (no hand Special Summon UI at batch time), Sword Hunter, Vampire Baby, Prohibition, Ground Collapse, Curse of Fiend, Forced Requisition, Graverobber, Parasite Paracide, Tailor of the Fickle, 7 Completed, Major Riot, Ceremonial Bell, Respect Play, The Eye of Truth — report Docs/CURRICULUM_BATCH_v142-mrd-srl-psv-b.md */ /* v141: card batch v141-mrd-srl-psv — PerCountBurns: Princess of Tsurugi, D.D. Dynamite, Cemetary Bomb, Thunder Crash, Wave-Motion Cannon (counted at resolution; EffectTiming.FaceUpFieldIgnition, multi-clause Atom.Sequence); SummonNegation: Solemn Judgment, Horn of Heaven, Magic Drain (MonsterWouldBeSummoned window, NegateSummon); ContinuousNegation: Jinzo, Royal Decree, Imperial Order, Amplifier, Spell Canceller; TurnNegationTraps: Mystic Probe, Metal Detector, World Suppression (NegateCardFamilyOnFieldThisTurn); TakeControl: Brain Control, Eria / Hiita / Aussa / Wynn the Charmers, Invader of the Throne (TakeControlWhileSourceFaceUp, SwitchControlWithThis); TributeSummons: Tribute Doll, Insect Imitation, Magical Labyrinth, Soul Exchange (TributeTargetAsIfControlledThisTurn), Wall Shadow Structural; deferred: Armored Glass (no post-equip window) */ /* v140: cards compiled only on the unmerged Sept-10 cloud branches, re-implemented here — Windstorm of Etaqua (opp-only mass position toggle), Level Limit - Area B (Level-filtered continuous force-DEF + lock), Invigoration ("An EARTH" equip), Spiritualism (OppSpellTraps bounce, cannot be negated), Graceful / Skull Dice (ModifyAllAtkDefByDieUntilEndOfTurn) */ /* v139: the Grok v107-v137 card line below, integrated on top of v138 — CannotAttackFaceDownMonstersThisTurn moved from mid-enum (index 82) to the end of EffectActionKind so every v106/v138 int keeps its meaning; seed reissued */ /* v138: flat burn fallback skips "for each" texts (fail closed) + one seed key for no-era/Original */ /* v137: Spirit Barrier Continuous Trap Soft AtomRegistration RxSpiritBarrier Soft VERIFY existing Soft ContinuousWhileFaceUp PreventControllerBattleDamage RequiresControllerHasMonster Side=Controller (Soft FieldSpellEffects.ControllerAvoidsBattleDamage Soft MonsterCount gate Soft-OK + Soft DE battle-calc Soft-OK; Prefer NO DE rewrite); fail-closed vs Amazoness Fighter OnlyBattlesInvolvingThis / Tornado Wall named-Umi / Waboku this-turn / Nutrient Z DamageCalculation; Soft residual 0 monsters Soft no protection Soft-OK; effect damage Soft residual Soft-OK (printed battle only Soft ≠ invent no-effect-damage); Soft residual Soft FieldSpellEffects Soft ≠ hard */ /* v136: Spellbinding Circle Continuous Trap Soft ContinuousRestrictionTemplates.RxSpellbindingCircle Soft VERIFY existing Soft Activate ContinuousCannotAttack Zone=OppMonsters RequiresTargetChoice Soft bind (Soft TextEffectRuntime ContinuousCannotAttack→EquippedTo Soft-OK) + ContinuousWhileFaceUp ContinuousCannotAttack Soft bound-host Soft AlsoCannotChangeBattlePosition Soft DestroyThisWhenBoundHostDestroyed Soft-OK (Soft DE ContinuousCannotAttackBlocks EquippedTo Soft gate Soft-OK Soft DE BoundTrapLocksBattlePosition Soft Soft DE UnlinkEquips Soft residual Soft-OK Soft ≠ invent destroy-on-leave Soft banish/return Soft residual Soft-OK; Prefer NO DE rewrite); fail-closed vs Nightmare Wheel·Shadow Spell leave-field Soft ≠ destroy Soft / Gravity Bind Level Soft / Messenger ATK Soft / Swords of Concealing Light Opp-wide Soft OpponentCannotChangeBattlePositions Soft / ClassicEra LinkThisToTarget+EquippedCannotAttack Soft arm Soft ≠ preferred Soft bars Soft DestroyThisWhenBoundHostDestroyed Soft; Soft residual Soft FullyCompiled Soft-OK Soft ≠ hard */ /* v135: Solemn Wishes ContinuousWhileFaceUp GainLpWhenControllerDraws Amount=500 Side=Controller (Soft AtomRegistration Solemn Wishes Soft VERIFY existing Soft TextEffectRuntime.NotifyDrew + Soft DE Draw hook Soft-OK; Prefer NO DE rewrite); fail-closed vs Fire Princess InflictDamageWhenControllerGainsLp / Heart of the Underdog ControllerNormalDrawInDrawPhase / Drop Off OpponentNormalDrawInDrawPhase / Spring of Rebirth GainLpWhenMonsterReturnsToOwnerHand / ClassicEra ControllerDraws+GainLifePoints arm Soft ≠ preferred Soft bars; Soft residual multi-draw Soft 500×N Soft-OK (once per draw event Soft ≠ invent ×cards) / Draw Phase Soft-OK / opponent draws Soft residual Soft-OK (printed "you"); Soft residual Soft NotifyDrew Soft ≠ hard */ /* v134: Messenger of Peace ContinuousWhileFaceUp ContinuousCannotAttack Amount=1500 Side=Both (Soft ContinuousRestrictionTemplates.RxCannotAttackAtk Soft VERIFY existing Soft DE ContinuousCannotAttackBlocks ATK≥Amount gate Soft-OK; Prefer NO DE rewrite) + StandbyPhase PayLpOrDestroyThis PayLpAmount=100 (Soft RxPayOrDestroyStandby Soft VERIFY existing Soft TextEffectRuntime ResolvePayLpOrDestroyThis Soft-OK); ATK≤1499 may attack Soft-OK; fail-closed vs Gravity Bind Level≥4 AmountIsLevel / Insect Barrier Race / The Dark Door only-one / Threatening Roar this-turn; Soft residual face-down ATK Soft-OK (declare requires FaceUp Attack) / pay timing Soft-OK (controller Standby auto-pay if LP≥100 else destroy; Soft ≠ invent choice UI); Soft residual Soft DE ATK gate Soft ≠ hard */ /* v133: Gravity Bind ContinuousWhileFaceUp ContinuousCannotAttack Amount=4 AmountIsLevel Side=Both (Soft ContinuousRestrictionTemplates.RxGravityBind Soft VERIFY existing Soft DE ContinuousCannotAttackBlocks Level≥Amount gate Soft-OK; Prefer NO DE rewrite); Level≤3 may attack Soft-OK; no-Level Soft residual Soft-OK (Xyz/Link Level0 Soft-OK Fanbot/Dilbot Soft ≠ invent ban); fail-closed vs Messenger ATK≥1500 / Insect Barrier Race / The Dark Door only-one / Threatening Roar this-turn; Soft residual Soft DE Level gate Soft ≠ hard */ /* v132: Fake Trap Activate DestroyThisInsteadOfControllerTraps (Soft ContinuousProtections destroy-replacement redirect Soft residual look-confirm Soft-OK; ActivationCondition OppEffectWouldDestroyYourTrap; Prefer NO DE rewrite); fail-closed vs banish/send/return / Spell-only / battle substitute / continuous CannotBeDestroyed; Soft residual only your Traps Soft ≠ hard */ /* v131: Lightforce Sword Activate BanishRandomFromOppHandFaceDown (Soft random Soft FD Soft Banish from opp hand; Amount=4 Soft ContinuousTurnsRemaining opp 4th Standby return ticker Soft TextEffectRuntime — Mask Soft surgical Soft residual Soft Banish FaceUp overwrite after BanishCard + Soft return from Banished; Prefer NO DE rewrite); fail-closed vs choose/look-at-hand / face-up banish / discard-random / your·next·3rd Standby / End Phase / Disappear GY / Confiscation; Soft residual Soft Banish OK */ /* v130: Swords of Concealing Light Continuous Spell dual+Standby SetAllOppMonstersFaceDownDefense (Activate resolve-only mass opp FD Defense) + ContinuousWhileFaceUp OpponentCannotChangeBattlePositions (opp-only; Soft ContinuousProtections + Soft DE CanChangePosition / CanFlipSummon — Mask Soft surgical; Flip Summon IS BP change Fanbot) + Soft ContinuousTurnsRemaining=2 controller 2nd Standby self-destroy ticker (Soft TextEffectRuntime Standby tick — Mask Soft surgical); fail-closed vs Swords of Revealing Light (FU flip+attack lock+opp EP) / Book of Moon single-target / Dragon Capture Jar force-DEF continuous / Spellbinding Circle bound / Ceasefire FD→FU / neither-player BP / Activate one-shot / destroy·bounce; Soft residual OK Soft ContinuousProtections + Soft DE BP/FlipSummon gate + Soft Standby ticker; card-effect BP Soft-OK (Enemy Controller Soft path); new summons Soft residual no auto-flip (resolve-only flip; FU ATK Soft-OK) */ /* v129: Protector of the Sanctuary ContinuousWhileFaceUp OpponentCannotDrawExceptDuringDrawPhases (opp-only; Soft ContinuousProtections + Soft DE Draw() gate — Mask Soft surgical); controller unrestricted; Draw Phase draws still OK; fail-closed vs Time Seal skip-Draw / searches·AddToHand ≠ draw / neither-player·controller-cannot-draw / Activate one-shot / destroy·bounce; Soft residual OK Soft ContinuousProtections + Soft DE Draw gate; non-draw AddToHand / Deck search remain Soft residual (not gated) */ /* v128: The Emperor's Holiday ContinuousWhileFaceUp NegateAllEquipCardEffectsOnField (both; Soft ContinuousProtections + Soft FieldSpellEffects Equip-apply skip + Soft TextEffectRuntime EquipThisToTarget TakeControlOfTarget gate — Mask Soft surgical); fail-closed vs Royal Decree (negate Traps) / Spell Canceller·Imperial Order (negate Spells generally) / destroy Equips·Really Eternal Rest / Activate one-shot / destroy·bounce; Soft residual OK Soft ContinuousProtections Equip-effect skip (ApplyEquipStats + ApplyCatalogSpellTraps) + Soft EquipThisToTarget TakeControlOfTarget gated; Equip piercing·magnetism·position-lock / Snatch Steal existing control / Equip Standby decay tick remain Soft residual */ /* v127: Spell Economics ContinuousWhileFaceUp ControllerDoesNotPayLpToActivateSpells (controller-only; Soft ContinuousProtections + Soft TextEffectRuntime Spell activate PayLp skip — Mask Soft surgical); fail-closed vs Trap LP costs / opponent's Spell costs / maintenance·Standby pays (Messenger) / Activate one-shot / destroy·bounce; Soft residual OK Soft ContinuousProtections / Soft Spell activate PayLp skip */ /* v126: Enervating Mist ContinuousWhileFaceUp OpponentHandSizeLimitBecomes (opp-only Amount=5; Soft ContinuousProtections + Soft DE HandSizeLimitEndPhase override — opp EP discard to 5 while face-up; controller unaffected); fail-closed vs Infinite Cards NoHandSizeLimit (bars remapping) / Hieroglyph your-limit-becomes-N / Max hand draw·draw-until / Activate one-shot / destroy·bounce / default hand-size-6; Soft residual OK Soft DE HandSizeLimit override for opponent only */ /* v125: Infinite Cards ContinuousWhileFaceUp NoHandSizeLimit (both; Soft ContinuousProtections + Soft DE HandSizeLimitEndPhase override — EP discard skipped while face-up); fail-closed vs Enervating Mist opp-only limit-5 / Hieroglyph your-limit-becomes-N / Max hand draw·draw-until / Activate one-shot / destroy·bounce; Soft residual OK Soft DE HandSizeLimitEndPhase override */ /* v124: The Dark Door ContinuousWhileFaceUp OnlyOneMonsterMayAttackPerBattlePhase (both; Soft ContinuousProtections BP attack-count gate — Soft DE MaxAttacksThisTurn Soft-clamp); fail-closed vs Gravity Bind Level4+ / Messenger ATK≥1500 / Threatening Roar·Feint Plan this-turn locks / Extra Attacks·can-attack-twice / Activate one-shot / destroy·bounce; Soft residual OK Soft DE MaxAttacks / BP attack-count gate */ /* v123: Mask of Restrict ContinuousWhileFaceUp NeitherPlayerCanTribute (both; Soft ContinuousProtections tribute gate — Summon/Ritual/cost/effect); fail-closed vs Soul Exchange force-tribute / Lava Golem SS Tribute-bypass / per-monster CannotBeTributed / Activate one-shot / destroy·bounce; Soft residual OK DE ToggleTribute·ResolveTributes Soft check / Lava Golem inherent-SS Soft */ /* v122: Remove Brainwashing ContinuousWhileFaceUp ReturnControlToOwners snap-back (cache invalidate from Activate one-shot); Soft Fanbot aliases; fail-closed bars unchanged; Soft residual OK face-down·Token·zone-full */ /* v121: Remove Brainwashing ContinuousWhileFaceUp ReturnControlToOwners (both sides; snap-back on control-change while face-up; Soft Fanbot return-control / their-owners aliases); fail-closed vs Change of Heart·Brain Control·Enemy Controller take-until-EP / Snatch Steal Equip continuous / Creature Swap exchange / single-target return / destroy / bounce; Soft residual OK face-down·Token·zone-full */ /* v120: A Feint Plan Activate CannotAttackFaceDownMonstersThisTurn (both players; Soft neither-player alias); fail-closed vs Threatening Roar any-attack / Waboku / Swords continuous / Negate Attack·End BP / Messenger·Gravity Bind face-up continuous; Soft residual OK attack-target UX */ /* v119: Disappear Activate Banish exact TargetCount=1 OppGyCards (Soft Remove-from-play→Banish); fail-closed vs Gravedigger Ghoul monsters-only/up-to-N / Soul Release any-GY/up-to-N / Chain Disappearance summon+same-name hand·Deck / destroy / your GY */ /* v118: Dark Factory of Mass Production Activate AddFromGyToHand exact TargetCount=2 RequiresNormalMonster ControllerGy (not TargetUpTo; no ATK/GY≥N gate); fail-closed vs Backup Soldier up-to-3 non-Effect ≤1500 GY≥5 / Monster Reborn·Premature SS / 1-target / Effect Monsters / opp GY */ /* v117: Morphing Jar Flip BothPlayersDiscardAndRedraw Amount=5 (fixed draw, not redraw=discarded); fail-closed vs Card Destruction Activate redraw=count / Cyber Jar destroy+mill/SS / Drop Off / Heart of the Underdog / wipe-without-redraw / controller-only draw-5 */ /* v116: The Shallow Grave Activate SpecialSummonFromGy each-player own GY face-down Defense (RequiresSecondTarget OppGy + SummonFaceDown + SummonToTargetsOwner); fail-closed vs Monster Reborn either-GY face-up / Call of the Haunted Attack continuous / both from one GY / face-up SS */ /* v115: Ceasefire Activate FlipAllFaceDownDefenseNoFlipEffects + Inflict × FaceUp Effect Monsters (FaceDownDefOrFaceUpEffect); fail-closed vs Swords continuous reveal / Flip-activate / burn-per-any-monster / Just Desserts opp-monster count */ /* v114: Backup Soldier Activate AddFromGyToHand TargetUpTo 3 non-Effect ≤1500 ATK while GY≥5 monsters (YourGyMonstersAtLeast+RequiresNonEffectMonster+AmountIsAtkMax); fail-closed vs Effect Monsters / ATK>1500 / GY<5 / Monster Reborn·Call of the Haunted SS / Extra Deck as hand */ /* v113: Mysterious Puppeteer ContinuousWhileFaceUp GainLpWhenEitherPlayerNormalOrFlipSummons (either player Normal/Flip Summon → +500 LP); fail-closed vs Special Summon / Spring of Rebirth return-to-hand / Dian Keto Activate / controller-only summons */ /* v112: Banner of Courage ContinuousWhileFaceUp ContinuousGainAtkDef +200 ATK your monsters during your Battle Phase only (RequiresBattlePhase+ControllerTurnOnly, DefAmount=0); fail-closed vs always-on aura / Yellow Luster Shield DEF / United We Stand Equip scale / opp Battle Phase */ /* v111: Spring of Rebirth ContinuousWhileFaceUp GainLpWhenMonsterReturnsToOwnerHand (field→owner hand → +500 LP); fail-closed vs Dian Keto Activate / Nutrient Z damage-calc / GY-send / bounce-destroy */ /* v110: Heart of the Underdog ControllerNormalDrawInDrawPhase reveal Normal Monster just-drawn then draw 1 more (optional); fail-closed vs opp draw / Effect Monster / ControllerDraws any-draw / always-draw-2 / Jar-Card Destruction */ /* v109: Nutrient Z DamageCalculation Gain 4000 LP first if about to take 2000+ battle damage; fail-closed vs AfterDamageCalculation / any-damage / heal=damage / Dian Keto / Spring of Rebirth */ /* v108: Drop Off OpponentNormalDrawInDrawPhase discard just-drawn card; fail-closed vs Time Seal / any-draw / random wipe / Card Destruction / Morphing Jar */ /* v107: Widespread Ruin AttackDeclared destroy opp highest-ATK Attack Position (choice if tied); fail-closed vs Mirror Force / Hammer Shot / Sakuretsu / Cylinder / Draining Shield / Negate Attack */ /* v103-v105 (main checkout, ported onto v106): Des Koala Flip inflict x opp hand (ScaleAmountByOpponentHandCards), Just Desserts inflict x opp monsters (ScaleAmountByOpponentMonsterCount), Secret Barrel inflict x (opp hand + cards they control) (ScaleAmountByOpponentHandAndControlledCards) */ /* v106: merge of the local v100-v102 lineage with the GitHub v51-v55 card batches (PRs #47-#50) — above both lines and the main checkout's uncommitted v103-v105; union enums renumbered, so every older int-serialized program (seed, disk cache) must recompile */ /* trap seals, mass defense, equipped-monster destroy, discard destroy face-up */ /* position change, own-attack cost and coin, standby tribute, flip reorder, magnetism, gate guardian */ /* swordsman tribute-atk, cheerful coffin, share the pain, shield and sword, damage-step attribute atk */ /* mrd named-control atk, battle-phase end, field-gy lp loss, controller battle-damage discard, equip standby / non-race lock */ /* take-control-until-ep soul-release-any-gy crimson-ninja-trap-reveal v-bump */ /* lob-remaining-gravedigger-legacy v-bump */ /* threatening-roar-atom v-bump */ /* sangan-witch-name-lock-atom v-bump */ /* maha-vailo-atk-per-equip-atom v-bump */ /* magic-cylinder-atom v-bump */ /* draining-shield-atom v-bump */ /* gy-target-atoms + attr-field-aura + ritual-search + start-of-damage-step v-bump */ /* per-count self-aura + ritual-procedure-structural + opp-draws v-bump */ /* v101: tribute-cost lp/damage/destroy, two-for-one tributes, trunade, time seal, def-only tweaks, zero gravity, special-summon draws, summoned-this-turn attack locks */ /* v102: tribute-summon riders (Zolga / Precious Cards), hand shuffles, Micro Ray / Energy Drain, deck peeks, Gravekeeper's Servant, Banner of Courage, Solomon's Lawbook, Fire Princess / Solemn Wishes, Dark Jeroid, Mobius, Spirit Barrier / Amazoness Fighter, Ultra Evolution Pill, Exhausting Spell */

        static readonly Regex RxDraw = new(
            @"(?:^|[.!?]\s+)Draw (\d+) cards?\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Reckless Greed family: Draw N and skip your next M Draw Phases.
        /// Compound only — Draw alone must not FullyCompile this print (Perfect-O).
        /// </summary>
        static readonly Regex RxDrawAndSkipControllerDrawPhases = new(
            @"Draw (\d+) cards? and skip your next (\d+) Draw Phases?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Offerings to the Doomed family: destroy face-up field target, also skip next N Draw Phase(s).
        /// Compound only — Destroy alone must not FullyCompile this print (Perfect-O).
        /// </summary>
        static readonly Regex RxDestroyFaceUpAlsoSkipControllerDrawPhases = new(
            @"Target 1 face-up monster on the field;\s*destroy that target,\s*also skip your next(?: (\d+))? Draw Phases?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Upstart Goblin family: Draw N, then opponent gains M LP.
        /// Compound only — bare Draw-1 must not FullyCompile this print (Perfect-O).
        /// Controller-gains-LP near-miss must not match.
        /// </summary>
        static readonly Regex RxDrawThenOpponentGainsLp = new(
            @"Draw (\d+) cards?,\s*then your opponent gains (\d+) (?:LP|Life Points)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);



        /// <summary>
        /// Compulsory Evacuation Device family: activate target 1 monster; return to hand.
        /// Reuses ReturnToHand + FieldAnyMonster (same atom as Flip bounce / discard-bounce).
        /// </summary>
        static readonly Regex RxTargetMonsterReturnHand = new(
            @"^(?:Target|Select) 1 monster on the field;\s*return that target to (?:the |its owner's |the owner's )?hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllOppMonsters = new(
            @"(?<!If you call it right, )Destroy all monsters your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // SpellCounterSpenders batch: "… destroy all monsters on the field except this card" (Legendary Flame Lord) is not
        // a Dark Hole: the card itself survives (its whole-sentence batch atom, Destroy + ExceptThisCard).
        static readonly Regex RxDestroyAllMonsters = new(
            @"Destroy all monsters on the field(?! except this card)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllST = new(
            @"Destroy all Spell and Trap Cards on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllOppST = new(
            @"Destroy all Spell and Trap Cards your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTargetStDestroy = new(
            @"Target 1 Spell/?Trap on the field;\s*destroy that target(?!\s*,)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Dust Tornado family: Target opp Spell/Trap; destroy; then optional Set 1 ST from hand.
        /// Compound only — destroy-only (MST / Locusts) and bare Set must not FullyCompile as Dust.
        /// Reuses opp-ST Side+Zone on Destroy; Set rider is optional hand Set only.
        /// </summary>
        static readonly Regex RxOppStDestroyThenOptionalSetStFromHand = new(
            @"Target 1 (?:Spell/?Trap|Spell or Trap)(?:\s+Card)? your opponent controls;\s*destroy that target,\s*then you can Set 1 (?:Spell/?Trap|Spell or Trap)(?:\s+Card)? from your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Beast Soul Swap family: Target 1 Race you control; bounce; and if you do,
        /// SS 1 same Race from hand with the same Level as the returned monster.
        /// Compound only — bounce-only / SS-only / missing Level / Type mismatch must not FullyCompile.
        /// No Freed higher-ATK filter (separate park).
        /// </summary>
        static readonly Regex RxBeastSoulSwapBounceThenSsSameLevel = new(
            @"Target 1 (\w+)(?:-Type)? monster you control;\s*" +
            @"return it to the hand,\s*and if you do,\s*" +
            @"Special Summon 1 \1(?:-Type)? monster from your hand " +
            @"with the same Level as the monster that was returned to the hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Freed the Brave Wanderer family: banish N ATTR from GY to target 1 face-up
        /// monster with higher ATK than this face-up card; destroy that target.
        /// Compound only — missing cost / no higher-ATK / face-down-any / ≤ ATK must not match.
        /// Reuses BanishFromGyCount + AttributeFilter cost shape (no new cost atom).
        /// </summary>
        static readonly Regex RxFreedBanishAttrGyDestroyHigherAtk = new(
            @"(?:Once per turn:\s*)?You can banish (\d+) (\w+) monsters? from your (?:GY|Graveyard) " +
            @"to target 1 face-up monster on the field with higher ATK than this face-up card;\s*" +
            @"destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);




        static readonly Regex RxMonsterReborn = new(
            @"Target 1 monster in either GY;\s*Special Summon it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // —— Ritual Spells ——
        /// <summary>'...used to Ritual Summon "X"...' → target Ritual Monster name.</summary>
        static readonly Regex RxRitualNamed = new(
            @"used to Ritual Summon (?:1 )?""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>'...used to Ritual Summon any DARK/EARTH/... Ritual Monster' → attribute.</summary>
        static readonly Regex RxRitualAnyAttr = new(
            @"used to Ritual Summon any (\w+) Ritual Monster",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Required total Tribute Level ('total Levels equal N or more' / 'Level is N or more').</summary>
        static readonly Regex RxRitualLevel = new(
            @"(?:total Level(?:s| Stars)? equal|whose Level is|whose total Level(?:s| Stars)? equal) (\d+) or more",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipDestroyMonster = new(
            @"FLIP:\s*Target 1 monster on the field;\s*destroy it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Old Vindictive Magician / Night Assailant Flip destroy (opp only, face-down included: Lua
        /// SelectTarget(aux.TRUE / nil, 0, LOCATION_MZONE)).</summary>
        static readonly Regex RxFlipDestroyOppMonster = new(
            @"FLIP:\s*Target 1 monster your opponent controls;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Armed Ninja family: Flip target a Spell on the field (Set cards legal; reveal).</summary>
        static readonly Regex RxFlipDestroySpellOnField = new(
            @"FLIP:\s*Target 1 Spell(?: Card)? on the field;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipRevealSetIfType = new(
            @"\(If the target is Set, reveal it, and destroy it if it is a (?:Spell|Trap) Card\. Otherwise, return it to its original position\.\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Crimson Ninja family: Flip target a Trap on the field (Set cards legal; reveal).</summary>
        static readonly Regex RxFlipDestroyTrapOnField = new(
            @"FLIP:\s*Target 1 Trap(?: Card)? on the field;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Reaper of the Cards family: Flip select a Trap; Set cards are revealed.</summary>
        static readonly Regex RxFlipDestroyTrapOnFieldReveal = new(
            @"FLIP:\s*Select 1 Trap Card on the field and destroy it\.\s*" +
            @"If the selected card is Set, pick up and see the card\.\s*" +
            @"If it is a Trap Card, it is destroyed\.\s*" +
            @"If it is a Spell Card, return it to its original position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Change of Heart family: take control of 1 opponent monster until the End Phase.</summary>
        static readonly Regex RxTakeControlUntilEndPhase = new(
            @"(?:Target|Select) 1 monster your opponent controls;\s*" +
            @"take control of (?:it|that target) until the End Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Shadow Tamer family: Flip take control of 1 Race opponent monster until EP.</summary>
        static readonly Regex RxFlipTakeControlUntilEndPhase = new(
            @"FLIP:\s*Target 1 (\w+)(?:-Type)? monster your opponent controls;\s*" +
            @"take control of (?:it|that target) until the End Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Dragon Manipulator family: Flip take 1 face-up Race until end of End Phase.</summary>
        static readonly Regex RxFlipTakeControlFaceUpRaceUntilEndPhase = new(
            @"FLIP:\s*Take control of 1 face-up (\w+)(?:-Type)? monster " +
            @"on your opponent's side of the field until the end of the End Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soul Release family: banish up to N cards from either GY.</summary>
        static readonly Regex RxBanishUpToCardsAnyGy = new(
            @"Target up to (\d+) cards in any GY(?:\(s\))?;\s*banish them\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNotTreatedAsNamed = new(
            @"\(This card is not treated as an? ""([^""]+)"" card\.\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxExodiaWin = new(
            @"If you have ""([^""]+)"", ""([^""]+)"", ""([^""]+)"" and ""([^""]+)"" " +
            @"in addition to this card in your hand, you win the Duel\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDragonCaptureJar = new(
            @"Change all face-up (\w+)(?:-Type)? monsters on the field to Defense Position, " +
            @"also they cannot change their battle positions\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGravediggerGhoul = new(
            @"(?:Target up to (\d+) monsters in your opponent's (?:GY|Graveyard);\s*banish them|" +
            @"Select up to (\d+) Monster Card(?:s|\(s\)) from your opponent's Graveyard\.\s*" +
            @"Remove the selected card(?:s|\(s\)) from play)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTwoProngedAttack = new(
            @"Select and destroy (\d+) of your monsters and (\d+) of your opponent's monsters\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipSpellToHand = new(
            @"FLIP:\s*Target 1 Spell in your GY;\s*add that target to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Mask of Darkness: Trap in GY to hand (Magician of Faith sibling).</summary>
        static readonly Regex RxFlipTrapToHand = new(
            @"FLIP:\s*Target 1 Trap in your GY;\s*add that target to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTrapHole = new(
            @"When your opponent Normal or Flip Summons 1 monster with (\d+) or more ATK:\s*Target that monster;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Bottomless Trap Hole family: opponent Summons (NS/FS/SS) ATK ≥ N,
        /// destroy that monster and banish it (skip GY). Not a target.
        /// </summary>
        static readonly Regex RxSummonDestroyBanish = new(
            @"When your opponent Summons a monster\(s\) with (\d+) or more ATK:\s*" +
            @"Destroy that monster\(s\) with \1 or more ATK, and if you do, banish it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Adhesion Trap Hole: opponent Summons (any) → halve original ATK.</summary>
        static readonly Regex RxAdhesionTrapHole = new(
            @"When your opponent Summons a monster\(s\):\s*Halve that monster\(s\)['’]s original ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Torrential Tribute: a monster is Summoned (either player, including Set/SS) → destroy all.</summary>
        static readonly Regex RxTorrentialTribute = new(
            @"When a monster\(s\) is Summoned:\s*Destroy all monsters on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Eatgaboon: opponent NS/FS ATK ≤ N destroy (excludes SS).</summary>
        static readonly Regex RxOppNsFsAtkLeqDestroy = new(
            @"If the ATK of a monster summoned by your opponent \(excluding Special Summon\) is (\d+) points or less, the monster is destroyed\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>House of Adhesive Tape: opponent NS/FS DEF ≤ N destroy (excludes SS).</summary>
        static readonly Regex RxOppNsFsDefLeqDestroy = new(
            @"If the DEF of a monster summoned by your opponent \(excluding Special Summon\) is (\d+) points or less, the monster is destroyed\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Token Feastevil: Token SS → destroy tokens, 300 damage each.</summary>
        static readonly Regex RxTokenFeastevil = new(
            @"When a Token\(s\) is Special Summoned:\s*Destroy as many Tokens on the field as possible, and if you do, inflict (\d+) damage to your opponent for each Token destroyed\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Mispolymerization: Fusion SS → bounce all face-up Fusions to Extra.</summary>
        static readonly Regex RxMispolymerization = new(
            @"Activate only when a Fusion Monster is Special Summoned\.\s*Return all face-up Fusion Monsters to their respective Extra Decks\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Chain Disappearance: summoned ATK ≤ N is banished, then opponent same-name from hand/Deck.</summary>
        static readonly Regex RxChainDisappearance = new(
            @"When a monster\(s\) with (\d+) or less ATK is Summoned:\s*" +
            @"Banish that monster\(s\) with \1 or less ATK, then your opponent banishes all cards with the same name as that card\(s\) from their hand and Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Chain Destruction: summoned ATK ≤ N, target 1, destroy same name in that controller's hand/Deck.</summary>
        static readonly Regex RxChainDestruction = new(
            @"When a monster\(s\) with (\d+) or less ATK is Summoned:\s*" +
            @"Target 1 of them;\s*destroy all cards with that name in its controller's hand and Main Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Blast Held by a Tribute: Tribute Summoned attacker → wipe opp ATK, 1000 damage.</summary>
        static readonly Regex RxBlastHeldByTribute = new(
            @"When an opponent's monster that was Tribute Summoned declares an attack:\s*" +
            @"Destroy as many face-up Attack Position monsters they control as possible, and if you do, inflict (\d+) damage to your opponent\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDisarmament = new(
            @"Destroy all Equip Cards on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEternalRest = new(
            @"Destroy all monsters equipped with Equip Cards\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Widespread Ruin: AttackDeclared destroy the opp Attack Position with highest ATK
        /// (your choice, if tied). Exact print -- not Mirror Force (all), Hammer Shot (Activate/both),
        /// Sakuretsu (attacking monster), Magic Cylinder / Draining Shield / Negate Attack.
        /// </summary>
        static readonly Regex RxWidespreadRuin = new(
            @"When an opponent's monster declares an attack:\s*" +
            @"Destroy the Attack Position monster your opponent controls with the highest ATK" +
            @"(?:\s*\(your choice, if tied\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Drop Off: Opp Draw Phase normal draw → discard that just-drawn card.
        /// Exact print — not Time Seal / Card Destruction / Morphing Jar / random discard / any-draw.
        /// </summary>
        static readonly Regex RxDropOff = new(
            @"When your opponent draws for their normal draw in their Draw Phase:\s*" +
            @"Your opponent discards 1 card they just drew\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Nutrient Z: During damage calculation, when about to take 2000+ battle damage → Gain 4000 LP first.
        /// Exact compound — not AfterDamageCalculation, any-damage, heal=damage, Dian Keto / Spring of Rebirth.
        /// </summary>
        static readonly Regex RxNutrientZ = new(
            @"During damage calculation,\s*" +
            @"(?:when|if) you (?:are about to take|would take) (\d+) or more battle damage:\s*" +
            @"Gain (\d+) LP first\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Heart of the Underdog: your Draw Phase Normal Monster draw → optional reveal → draw N more.
        /// Exact compound — not Drop Off (opp), Solemn Wishes any-draw, Pot of Greed always-draw-2,
        /// Morphing Jar / Card Destruction reload.
        /// </summary>
        static readonly Regex RxHeartOfTheUnderdog = new(
            @"During your Draw Phase,\s*" +
            @"when you draw a Normal Monster\(s\):\s*" +
            @"You can reveal it;\s*draw (\d+) more cards?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Spring of Rebirth: Continuous — each time monster(s) return field→owner's hand → gain N LP.
        /// Exact compound — not Dian Keto Activate heal, Nutrient Z damage-calc, GY-send, bounce-destroy.
        /// </summary>
        static readonly Regex RxSpringOfRebirth = new(
            @"^(?:Increase your Life Points by (\d+) points every time monsters return " +
            @"from the field to the owner's hand\.?|" +
            @"Each time a monster\(s\) returns from the field to the owner's hand:\s*" +
            @"Gain (\d+) LP\.?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Banner of Courage: Continuous — all monsters you control gain N ATK during your Battle Phase only.
        /// Exact compound — not always-on aura, DEF boost (Yellow Luster Shield), United We Stand Equip scale.
        /// </summary>
        static readonly Regex RxBannerOfCourage = new(
            @"^All monsters you control gain (\d+) ATK during your Battle Phase only\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Mysterious Puppeteer: Continuous — each time either player Normal/Flip Summons → gain N LP.
        /// Exact compound — not Special Summon, Spring bounce LP, Dian Keto Activate, controller-only.
        /// </summary>
        static readonly Regex RxMysteriousPuppeteer = new(
            @"^(?:Each time you or your opponent Normal Summons or Flip Summons a monster,\s*" +
            @"increase your Life Points by (\d+) points?\.?" +
            @"|Each time (?:a |either )?player Normal (?:Summons or Flip Summons|or Flip Summons) a monster(?:\(s\))?:\s*" +
            @"Gain (\d+) LP\.?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Backup Soldier: Activate while GY≥N monsters — target up to K non-Effect ≤ATK in your GY; add to hand.
        /// Exact compound — not Monster Reborn / Call of the Haunted SS, not Effect Monsters, not ATK above cap.
        /// </summary>
        static readonly Regex RxBackupSoldier = new(
            @"^While there are (\d+) or more monsters in your (?:GY|Graveyard):\s*" +
            @"Target up to (\d+) non-Effect Monsters with (\d+) or less ATK in your (?:GY|Graveyard);\s*" +
            @"add them to your hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Dark Factory of Mass Production: Target exactly N Normal Monsters in your GY; add to hand.
        /// Exact compound — not Backup Soldier (up-to / non-Effect / ATK / GY≥N), not Reborn/Premature SS,
        /// not 1-target, not Effect Monsters, not opp GY.
        /// </summary>
        static readonly Regex RxDarkFactory = new(
            @"^Target (\d+) Normal Monsters in your (?:GY|Graveyard);\s*" +
            @"add (?:them|those targets) to your hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Disappear: Target exactly 1 card in opponent's GY; banish that target.
        /// Soft residual: legacy "Remove from play 1 card from your opponent's Graveyard."
        /// Exact compound — not Gravedigger (monsters/up-to), Soul Release (any GY/up-to),
        /// Chain Disappearance (summon + same-name), destroy, your GY.
        /// </summary>
        static readonly Regex RxDisappear = new(
            @"^(?:Target 1 card in your opponent's (?:GY|Graveyard);\s*banish that target\.?|" +
            @"Remove from play 1 card from your opponent's Graveyard\.?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Ceasefire: face-down DEF or Effect Monster on field → flip all face-down DEF face-up DEF
        /// (no Flip effects) + inflict N × face-up Effect Monsters. Exact compound — not Swords,
        /// not Flip-activate, not Just Desserts / burn-per-any-monster.
        /// </summary>
        static readonly Regex RxCeasefire = new(
            @"^If a face-down Defense Position monster or an Effect Monster is on the field:\s*" +
            @"Change all face-down Defense Position monsters on the field to face-up Defense Position " +
            @"\(Flip monsters' effects are not activated at this time\), also inflict (\d+) damage " +
            @"to your opponent for each Effect Monster on the field\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The Shallow Grave: each player targets 1 in their own GY; each SS face-down Defense.
        /// Exact compound — not Monster Reborn / Call of the Haunted / either-GY / face-up.
        /// </summary>
        static readonly Regex RxShallowGrave = new(
            @"^Each player targets 1 monster in their own (?:GY|Graveyard);\s*" +
            @"each(?: player)? Special Summons (?:the|that) (?:target(?:s|ed monster)?|targeted monster)" +
            @"(?: from their (?:GY|Graveyard))? in face-down Defense Position(?: to their field)?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMirrorForce = new(
            @"When an opponent's monster declares an attack:\s*Destroy all your opponent's Attack Position monsters\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNegateAttack = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*negate the attack, then end the Battle Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxWaboku = new(
            @"You take no battle damage from your opponent's monsters this turn\.\s*Your monsters cannot be destroyed by battle this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Threatening Roar: Your opponent cannot declare an attack this turn.
        /// Exact print only — not continuous / Negate Attack / Battle Phase skip.
        /// </summary>
        static readonly Regex RxOppCannotDeclareAttackThisTurn = new(
            @"Your opponent cannot declare an attack this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// A Feint Plan: A player cannot attack face-down monsters during this turn.
        /// Soft: Neither player … this turn. Exact — not Roar / Waboku / Swords /
        /// Negate Attack / Messenger / Gravity Bind.
        /// </summary>
        static readonly Regex RxFeintPlan = new(
            @"^(?:A player cannot attack face-down monsters during this turn|" +
            @"Neither player can(?:not)? attack face-down monsters(?: during)? this turn)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Remove Brainwashing: The control of all monsters on the field returns to the owners.
        /// Soft: Return control … / their owners. Exact — not take-until-EP / Equip steal /
        /// Creature Swap / single-target / destroy / bounce.
        /// </summary>
        static readonly Regex RxRemoveBrainwashing = new(
            @"^(?:The control of all monsters on the field returns to (?:the|their) owners|" +
            @"(?:Activate:\s*)?Return(?:s)? control of all monsters on the field to (?:the|their) owners)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        /// <summary>
        /// Mask of Restrict: Neither player can Tribute cards.
        /// Soft: Players cannot Tribute / monsters. Exact — not Soul Exchange /
        /// Lava Golem SS-tribute / per-monster cannot-be-Tributed / destroy / bounce.
        /// </summary>
        static readonly Regex RxMaskOfRestrict = new(
            @"^(?:Neither player can|Players? cannot) Tribute (?:cards?|monsters?)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        /// <summary>
        /// The Dark Door: Only 1 monster can attack during each Battle Phase.
        /// Soft: only-one / may-attack. Exact — not Gravity Bind / Messenger /
        /// Roar / Feint / Extra Attacks / destroy / bounce.
        /// </summary>
        static readonly Regex RxDarkDoor = new(
            @"^(?:Only|Just) (?:1|one) monsters? (?:can|may) attack during each Battle Phase\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Infinite Cards: There is no limit to the number of cards in players' hands.
        /// Soft: no-hand-size-limit / either-player. Exact — not Enervating Mist /
        /// Hieroglyph / Max hand draw / destroy / bounce.
        /// </summary>
        static readonly Regex RxInfiniteCards = new(
            @"^(?:There is no limit to the number of cards in players?'? hands?|" +
            @"Neither player has (?:a )?hand size limit|" +
            @"Players? (?:have|has) no hand size limit|" +
            @"There is no hand size limit(?: for (?:either|both) players?)?|" +
            @"No hand size limit for (?:either|both) players?)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Enervating Mist: Your opponent's hand size limit becomes 5.
        /// Soft: opponent hand-size-becomes-N. Exact — not Infinite Cards /
        /// Hieroglyph your-limit / Max hand draw / destroy / bounce.
        /// </summary>
        static readonly Regex RxEnervatingMist = new(
            @"^(?:Your )?opponent'?s? hand size limit becomes (\d+)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Spell Economics: You do not pay Life Points to activate Spell Cards.
        /// Soft: do-not-pay-LP-to-activate-Spell. Exact — not Trap LP / opponent /
        /// Standby maintenance / Activate one-shot destroy / bounce.
        /// </summary>
        static readonly Regex RxSpellEconomics = new(
            @"^(?:You )?do not pay (?:LP|Life Points) to activate Spell Cards?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The Emperor's Holiday: Negate all Equip Card effects on the field.
        /// Soft: negate-Equip-Card-effects. Exact — not Royal Decree / Imperial Order /
        /// Spell Canceller / destroy Equips / Really Eternal Rest / destroy / bounce.
        /// </summary>
        static readonly Regex RxEmperorsHoliday = new(
            @"^Negate all Equip Card effects on the field\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Protector of the Sanctuary: Your opponent cannot draw cards except during Draw Phases.
        /// Soft: cannot-draw-except-Draw-Phase. Exact — not Time Seal / search / neither-player /
        /// controller-cannot-draw / destroy / bounce.
        /// </summary>
        static readonly Regex RxProtectorOfTheSanctuary = new(
            @"^(?:Your )?opponent(?:'s)? cannot draw cards except during Draw Phases?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Swords of Concealing Light: Destroy during your 2nd Standby + resolve flip all
        /// opp monsters FD Defense + ContinuousWhileFaceUp opp cannot change battle positions.
        /// Soft: Fanbot compound. Exact — not Revealing Light / Book of Moon / Jar / Spellbinding /
        /// Ceasefire / destroy / bounce.
        /// </summary>
        static readonly Regex RxSwordsOfConcealingLight = new(
            @"Destroy this card during your 2nd Standby Phase after activation\.\s*" +
            @"When this card resolves, change all monsters your opponent controls to face-down Defense Position\.\s*" +
            @"Monsters your opponent controls cannot change their battle positions\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Lightforce Sword: Banish 1 random from opp hand face-down + return on opp 4th Standby.
        /// Soft: Fanbot compound. Exact — not choose/look / discard / face-up / your·next Standby /
        /// Disappear GY / Confiscation.
        /// </summary>
        static readonly Regex RxLightforceSword = new(
            @"Banish 1 random card from your opponent's hand, face-down\.\s*" +
            @"During your opponent's 4th Standby Phase after this card's activation, " +
            @"return that card to their hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxFakeTrap = new(
            @"Activate only when your opponent activates a Spell, Trap, or Effect Monster(?:'s)? effect that would destroy a Trap Card\(s\) you control\.\s*" +
            @"Destroy this card instead\.?" +
            @"(?:\s*\(If the cards that would have been destroyed are face-down, you can look at them to confirm\.\))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Morphing Jar: FLIP discard-all then each draws N (Amount lock).
        /// Exact fixed-draw print — not Card Destruction (redraw=discarded), Cyber Jar,
        /// Drop Off / Heart of the Underdog, wipe-without-redraw, controller-only draw-5.
        /// </summary>
        static readonly Regex RxMorphingJar = new(
            @"FLIP:\s*Both players discard as many cards as possible from their hands,\s*" +
            @"then each player draws (\d+) cards?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCardDestruction = new(
            @"Both players discard as many cards as possible from their hands, then each player draws the same number of cards they discarded\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Sangan / Witch of the Black Forest: field→GY Deck search (ATK|DEF ≤ N) PLUS
        /// name-lock rider. Fail-closed — search without the lock must not FullyCompile.
        /// </summary>
        static readonly Regex RxSangan = new(
            @"If this card is sent from the field to the GY:\s*" +
            @"Add 1 monster with (\d+) or less (ATK|DEF) from your Deck to your hand,\s*" +
            @"but you cannot activate cards,\s*or the effects of cards,\s*" +
            @"with that name for the rest of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        static readonly Regex RxFlute = new(
            @"Special Summon up to (\d+) Dragon monsters? from your hand\.\s*""Lord of D\.?"" must be on the field",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPolymerization = new(
            @"Fusion Summon 1 Fusion Monster from your Extra Deck, using monsters from your hand or field as Fusion Material\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Named Greater (Black Luster Ritual / Contract with the Dark Master):
        /// "This card is used to Ritual Summon "NAME". You must also Tribute monsters
        /// from your hand or field whose total Levels equal N or more."
        /// </summary>
        static readonly Regex RxRitualNamedGreater = new(
            @"This card is used to Ritual Summon ""([^""]+)""\.\s*" +
            @"You must also (?:Tribute|offer) monsters(?: whose total Level(?:s| Stars)? equal (\d+) or more(?: as a Tribute)? from the field or your hand| from your hand or field whose total Levels equal (\d+) or more)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Named Greater, singular (Black Illusion Ritual):
        /// "Tribute a monster from your hand or field whose Level is N or more."
        /// </summary>
        static readonly Regex RxRitualNamedGreaterOne = new(
            @"This card is used to Ritual Summon ""([^""]+)""\.\s*" +
            @"You must also Tribute a monster from your hand or field whose Level is (\d+) or more\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Named Equal: "total Levels exactly equal N".
        /// </summary>
        static readonly Regex RxRitualNamedEqual = new(
            @"This card is used to Ritual Summon ""([^""]+)""\.\s*" +
            @"You must also Tribute monsters from your hand or field whose total Levels exactly equal (\d+)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attribute Equal (Earth Chant / Contract with the Abyss):
        /// "Ritual Summon any ATTR Ritual Monster" + exact Level of that monster.
        /// </summary>
        static readonly Regex RxRitualAttributeEqual = new(
            @"This card can be used to Ritual Summon any (\w+) Ritual Monster\.\s*" +
            @"You must also Tribute monsters from your hand or field whose total Levels exactly equal the Level of the Ritual Monster you Ritual Summon\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxLordOfD = new(
            @"Neither player can target Dragon monsters on the field with card effects\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwords = new(
            @"After this card's activation, it remains on the field, but you must destroy it during the End Phase of your opponent's (\d+)(?:rd|nd|th|st)? turn\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwordsAttack = new(
            @"While this card is face-up on the field, your opponent's monsters cannot declare an attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwordsFlip = new(
            @"When this card is activated:\s*If your opponent controls a face-down monster, flip all monsters they control face-up\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxRing = new(
            @"During your opponent's turn:\s*Target 1 face-up monster your opponent controls whose ATK is less than or equal to their LP;\s*destroy that face-up monster, and if you do, take damage equal to its original ATK, then inflict damage to your opponent, equal to the damage you took",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Enemy Controller's first bullet (an activated Spell's own target). Not the optional "…: You can target 1 …"
        /// of a monster trigger (Otohime, SpiritMonsters batch; Gravekeeper's Assailant has its own regex below).
        /// </summary>
        static readonly Regex RxEnemyControllerPos = new(
            @"(?<!\bcan )Target 1 face-up monster your opponent controls;\s*change that target's battle position",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attack-declared named-field gate (Gravekeeper's Assailant family).
        /// </summary>
        static readonly Regex RxAttackNamedChangePosition = new(
            @"When this card declares an attack while ""([^""]+)"" is on the field:\s*" +
            @"You can target 1 face-up monster your opponent controls;\s*change that target's battle position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Kuriboh-style hand QE at damage calculation:
        /// "During damage calculation, if your opponent's monster attacks (Quick Effect):
        ///  You can discard this card; you take no battle damage from that battle."
        /// </summary>
        /// <summary>
        /// Name condition (A Legendary Ocean, latest PSCT + prior errata):
        /// "(This card's name is always treated as "Umi".)" /
        /// "(This card is always treated as "Umi".)"
        /// </summary>
        static readonly Regex RxAlwaysTreatedAsName = new(
            @"\(This card(?:'s name)? is always treated as (?:an? )?""([^""]+)""(?: card)?\.?\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNameTreatedAs = new(
            @"This card's name is treated as ""([^""]+)""\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>All WATER monsters on the field gain 200 ATK/DEF (slash form — both stats).</summary>
        static readonly Regex RxAllAttrGainAtkDef = new(
            @"All (\w+) monsters(?: on the field)? gain (\d+) ATK/DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Star Boy / Milus Radiant / Bladefly / Witch's Apprentice:
        /// increase the ATK of all WATER monsters by 500 points and decrease the ATK of all FIRE monsters by 400 points.
        /// </summary>
        static readonly Regex RxIncDecAtk = new(
            @"increase the ATK of all (\w+)(?:-Type)? monsters by (\d+) points and decrease the ATK of all (\w+)(?:-Type)? monsters by (\d+) points\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Hoshiningen / Little Chimera / Harpie Lady 1:
        /// All LIGHT monsters on the field gain 500 ATK, also all DARK monsters on the field lose 400 ATK.
        /// </summary>
        static readonly Regex RxAllGainAtkMaybeLose = new(
            @"All (\w+)(?:-Type)? monsters(?: on the field)? gain (\d+) ATK(?:, also all (\w+)(?:-Type)? monsters(?: on the field)? lose (\d+) ATK)?\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAllLoseAtk = new(
            @"All (\w+)(?:-Type)? monsters(?: on the field)? lose (\d+) ATK\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Command Knight: All Warrior monsters you control gain 400 ATK.</summary>
        static readonly Regex RxAllYouControlGainAtk = new(
            @"All (\w+)(?:-Type)? monsters you control gain (\d+) ATK(?:/?DEF| and DEF)?\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxIncreasesAtkDefOfAll = new(
            @"Increases? the ATK and DEF of all (\w+) monsters(?: on the field)? by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Reduce the Level of all WATER monsters in both players' hands and on the field by 1.
        /// </summary>
        static readonly Regex RxReduceLevelHandsAndField = new(
            @"Reduce the Level of all (\w+) monsters in both players?' hands and on the field by (\d+)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDowngradeLevelHandsAndField = new(
            @"Downgrade all (\w+) monsters in both player'?s?'? hands and on the field by (\d+) Level\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Abyss Soldier (and same-shape ignition):
        /// "Once per turn: You can discard 1 WATER monster to the Graveyard to target 1 card on the field; return it to the hand."
        /// </summary>
        static readonly Regex RxDiscardAttrBounceField = new(
            @"Once per turn:\s*You can discard 1 (\w+) monster to the (?:GY|Graveyard) to target 1 card on the field;\s*return it to the hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDiscardAttrBounceFieldPsct = new(
            @"Once per turn:\s*You can discard 1 (\w+) monster(?: to the (?:GY|Graveyard))?;\s*target 1 card on the field;\s*return (?:it|that target) to the hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxKuribohStyle = new(
            @"During damage calculation, if your opponent's monster attacks(?:\s*\(Quick Effect\))?:\s*" +
            @"You can discard this card;\s*you take no battle damage from that battle\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Amphibious Bugroth MK-3 / Yugipedia: continuous, no Chain Link.
        /// "While "Umi" is face-up on the field, this card can attack your opponent's Life Points directly."
        /// Older: "As long as "Umi" remains face-up on the field, …"
        /// </summary>
        static readonly Regex RxDirectAttackWhileNamed = new(
            @"(?:While|As long as) ""([^""]+)"" (?:is|remains)(?: face-up)? on the field, " +
            @"this card can attack (?:your opponent(?:'s Life Points)? )?directly\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Shadowslayer / Black Tyranno / Rocket Jumper: If all monsters your opponent controls
        /// are in Defense Position (or the only cards are Defense Position monsters), this card
        /// can attack directly. Fail-closed vs Drillago ATK-gate and empty-field-only prints.
        /// Must Take before RxDirectAttackUnconditional (trailing "this card can attack directly"
        /// near-miss).
        /// </summary>
        static readonly Regex RxAllOppMonstersDefenseCanAttackDirectly = new(
            @"If (?:all monsters your opponent controls are in Defense Position|" +
            @"the only cards (?:your opponent controls|on your opponent's side of the field) are Defense Position monsters), " +
            @"this card can attack (?:your opponent(?:'s Life Points)? )?directly\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        /// <summary>
        /// Unconditional direct attackers (Ooguchi, Jinzo #7, Nightmare Horse, …).
        /// Requires "This card/monster can|may" so Toon "Can attack … unless" does not match.
        /// Negative lookahead so "cannot attack directly" (Zombyra) is not a grant.
        /// </summary>
        static readonly Regex RxDirectAttackUnconditional = new(
            @"(?<!, )This (?:card|monster) (?:may|can(?!not)) attack (?:your opponent(?:'s Life Points)? )?directly" +
            @"(?: even if there is a monster on your opponent's side of the field)?(?! this turn)(?! if\b)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Mermaid Knight: While "Umi" is face-up on the field, this card can attack twice
        /// during the same Battle Phase.
        /// </summary>
        static readonly Regex RxExtraAttackWhileNamed = new(
            @"(?:While|As long as) ""([^""]+)"" (?:is|remains)(?: face-up)? on the field, " +
            @"this card can attack twice during the same Battle Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxExtraAttackUnconditional = new(
            @"This card can attack twice during the same Battle Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSakuretsu = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Fissure: destroy 1 face-up opp monster with the lowest ATK (ties: choose).</summary>
        static readonly Regex RxDestroyLowestAtkOpp = new(
            @"Destroy the 1 face-up monster your opponent controls that has the lowest ATK" +
            @"(?:\s*\(your choice, if tied\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Smashing Ground: destroy 1 face-up opp monster with the highest DEF (ties: choose).</summary>
        static readonly Regex RxDestroyHighestDefOpp = new(
            @"Destroy the 1 face-up monster your opponent controls that has the highest DEF" +
            @"(?:\s*\(your choice, if tied\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Hammer Shot: destroy 1 face-up Attack Position monster with the highest ATK.</summary>
        static readonly Regex RxDestroyHighestAtkFaceUpAtk = new(
            @"Destroy 1 face-up Attack Position monster with the highest ATK" +
            @"(?:\.\s*\(If it is a tie, you get to choose\.\)|\s*\(your choice, if tied\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Castle Walls (legacy): Increase the DEF of 1 face-up monster … until EOT.</summary>
        static readonly Regex RxIncreaseDefUntilEot = new(
            @"Increase the DEF of 1 face-up monster(?: on the field)? by (\d+) points? until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Stop Defense (legacy Select wording).</summary>
        static readonly Regex RxStopDefense = new(
            @"Select 1 Defense Position monster(?: on your opponent's side of the field| your opponent controls)?;?\s*" +
            @"and change it to Attack Position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Block Attack: face-up ATK opp monster → face-up Defense (Stop Defense sibling).</summary>
        static readonly Regex RxBlockAttack = new(
            @"Target 1 face-up Attack Position monster your opponent controls;\s*" +
            @"change that target to face-up Defense Position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Order to Charge: Tribute a targeted non-Token Normal you control, then destroy
        /// 1 opponent monster.
        /// </summary>
        static readonly Regex RxOrderToCharge = new(
            @"Target 1 non-Token Normal Monster you control;\s*" +
            @"Tribute that Normal Monster, and if you do,\s*" +
            @"destroy 1 monster your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Enchanted Javelin (legacy): Select attacking monster → gain LP = ATK.</summary>
        static readonly Regex RxEnchantedJavelin = new(
            @"Select 1 attacking monster\.?\s*Gain Life Points equal to its ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Elegant Egotist: If "X" is on the field: SS 1 "X" or "Y" from hand or Deck.</summary>
        static readonly Regex RxElegantEgotist = new(
            @"If ""([^""]+)"" is(?: face-up)? on the field:\s*" +
            @"Special Summon 1 ""([^""]+)"" or ""([^""]+)"" from your hand or Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Umi family: All Fish, Sea Serpent, Thunder, and Aqua … gain N ATK/DEF,
        /// also all Machine and Pyro … lose N ATK/DEF.
        /// </summary>
        static readonly Regex RxMultiRaceGainLoseAtkDef = new(
            @"All (.+?) monsters on the field gain (\d+) ATK/DEF,\s*" +
            @"also all (.+?) monsters on the field lose (\d+) ATK/DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Forest / Mountain / Wasteland: All A, B, and C monsters gain N ATK/DEF (no lose clause).</summary>
        static readonly Regex RxMultiRaceGainAtkDef = new(
            @"All (.+?,.+) monsters(?: on the field)? gain (\d+) ATK(?:/DEF| and DEF)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Necrovalley family: All "Gravekeeper's" monsters (on the field) gain N ATK/DEF
        /// or "ATK and DEF". Quoted series, not a race.
        /// </summary>
        static readonly Regex RxQuotedSeriesGainAtkDef = new(
            @"All ""([^""]+)"" monsters(?: on the field)? gain (\d+) ATK(?:/DEF| and DEF)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attribute/race field aura with a DEF penalty — modern wording of the
        /// legacy "Increase the ATK … and decrease their DEF …" shape:
        /// All FIRE monsters gain 500 ATK and lose 400 DEF.
        /// (Molten Destruction / Rising Air Current / Gaia Power / Luminous Spark.)
        /// </summary>
        static readonly Regex RxAllGainAtkLoseDef = new(
            @"All ([A-Za-z]+(?:[ -][A-Za-z]+)*)(?:-Type)? monsters(?: on the field)? " +
            @"gain (\d+) ATK and lose (\d+) DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Harpies' Hunting Ground / Mountain-shape single or multi-word race:
        /// All Winged Beast monsters gain 200 ATK/DEF.
        /// </summary>
        static readonly Regex RxRaceGainAtkDef = new(
            @"All ([A-Za-z]+(?:[ -][A-Za-z]+)*)(?:-Type)? monsters(?: on the field)? gain (\d+) ATK(?:/DEF| and DEF)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Harpies' Hunting Ground: If any "Harpie Lady" or "Harpie Lady Sisters" is
        /// Normal or Special Summoned: target 1 S/T; destroy it.
        /// </summary>
        static readonly Regex RxNamedOrNamedSummonDestroySt = new(
            @"If any ""([^""]+)"" or ""([^""]+)"" is Normal or Special Summoned:\s*" +
            @"(?:The player who conducted (?:the|that) Summon targets 1 Spell/?Trap on the field;\s*" +
            @"that player destroys that target|" +
            @"Target 1 Spell/?Trap on the field;\s*destroy (?:that target|it))\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Rite of Spirit: This card's activation and effect are unaffected by "Necrovalley".</summary>
        static readonly Regex RxUnaffectedByNamed = new(
            @"(?:This card'?s )?activation and effect are unaffected by ""([^""]+)""\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMagicCylinder = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*negate the attack, and if you do, inflict damage to your opponent equal to its ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDrainingShield = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*negate that attack, and if you do, gain LP equal to that target's ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Secret Barrel: Inflict N damage × (opp hand + cards they control).
        /// Exact compound phrase — not Just Desserts (monster-only), Des Koala (Flip hand-only),
        /// Ceasefire (Effect Monster), or flat Poison Mummy / Raimei.
        /// </summary>
        static readonly Regex RxSecretBarrel = new(
            @"Inflict (\d+) (?:points of )?damage to your opponent for each card in their hand and for each card they control\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Just Desserts: Inflict N damage to your opponent for each monster they control.
        /// Exact scale phrase — not Secret Barrel (hand+field), Ceasefire (Effect Monster),
        /// Des Koala (hand), flat burns, or Gift all-field LP.
        /// </summary>
        static readonly Regex RxJustDesserts = new(
            @"Inflict (\d+) (?:points of )?damage to your opponent for each monster they control\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDarkMirrorForce = new(
            @"When an opponent's monster declares an attack:\s*Banish all Defense Position monsters your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBarkOfDarkRuler = new(
            @"If a (\w+)(?:-Type)? monster you control battles, during the Damage Step:\s*" +
            @"Pay LP \(in multiples of (\d+) points\), then target the opponent's battling monster;\s*" +
            @"that opponent's monster loses that much ATK and DEF, until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMaskOfWeakness = new(
            @"Target 1 attacking monster;\s*that target loses (\d+) ATK until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Maiden of the Aqua: the field is treated as "Umi" while face-up (no Umi ATK/DEF).
        /// </summary>
        static readonly Regex RxFieldTreatedAs = new(
            @"(?:As long as this card remains face-up on the field|While this card is face-up on the field), " +
            @"the field is treated as ""([^""]+)""" +
            @"(?: \(however there is no increasing or decreasing of ATK/DEF due to ""[^""]+""'s effect\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNoFieldSpellBlocks = new(
            @"If there is an active Field Spell Card on the field, this effect is not applied\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxActivateOnlyWhileNamed = new(
            @"Activate only (?:while|if) ""([^""]+)"" is(?: face-up)? on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNoBattleDamageWhileNamed = new(
            @"While ""([^""]+)"" is(?: face-up)? on the field, you take no Battle Damage(?: from attacking monsters)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyWhenNamedLeaves = new(
            @"(?:Destroy this card when ""([^""]+)"" leaves the field|If ""([^""]+)"" is not on the field, destroy this card)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Falling Down: Destroy this card unless you control an "Archfiend" card.</summary>
        static readonly Regex RxDestroyUnlessYouControlNamed = new(
            @"Destroy this card unless you control an? ""([^""]+)"" card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSummonGainLp = new(
            @"When this (?:monster|card) is Normal Summoned, Flip Summoned or Special Summoned, " +
            @"increase your Life Points by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Rain of Mercy / PSCT siblings: both players gain the same LP amount.</summary>
        static readonly Regex RxBothPlayersGainLp = new(
            @"(?:(?:increase|increases) the Life Points of both players by (\d+) points?|" +
            @"both players gain (\d+) (?:LP|Life Points))\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        static readonly Regex RxConvertOpponentLpGainToDamage = new(
            @"As long as this card remains face-up on the field, any effect of increasing your opponent's Life Points " +
            @"is changed to inflict the same amount of points in damage to your opponent's Life Points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyedToGyDamage = new(
            @"When this card is destroyed and sent to the Graveyard, you take (\d+) points of damage\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Levia-Dragon - Daedalus / same PSCT family:
        /// You can send 1 face-up "Umi" you control to the GY; destroy all other cards on the field.
        /// Cost is a named card you control (A Legendary Ocean is always treated as Umi).
        /// Maiden of the Aqua is environment, not a named Umi, so she is not a legal cost.
        /// </summary>
        static readonly Regex RxSendNamedDestroyAllOther = new(
            @"(?:You can )?send 1 face-up ""([^""]+)"" you control to the (?:GY|Graveyard);\s*" +
            @"destroy all other cards on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSendNamedDestroyExceptThis = new(
            @"(?:You can )?send 1 face-up ""([^""]+)"" you control to the (?:GY|Graveyard);\s*" +
            @"destroy all cards on the field except this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Not in this regex: "This card can only be placed on the field by the effect of
        // \"X\"" (Spirit Message / Sarcophagus) is absorbed whole by
        // DestinyBoardAtoms.IsPlacementLockSentence (DestinyBoard batch). The program is
        // FullyCompiled with no clause, like "Must first be Special Summoned with X";
        // CardEffectStatus calls the card Structural only while X compiles to a placement
        // that names it (DestinyBoardRuntime.HasLivePlacementPath), so a card whose placer
        // is not implemented (The Second / Third Sarcophagus) stays Unimplemented.
        static readonly Regex RxStructuralSummonText = new(@"cannot be normal summoned(?:/set)?|must (?:first )?be special summoned|must be fusion summoned|can only be special summoned", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxRitualProcedureText = new(@"You can Ritual Summon this card with ""[^""]+""\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>
        /// Pre-PSCT Ritual procedure lines (Crab Turtle / Skull Guardian / Hungry Burger):
        /// "This monster can only be Ritual Summoned with the Ritual Spell Card, "X"." and
        /// "You must also offer/Tribute monsters whose total Level Stars equal N or more …".
        /// Both restate the paired Ritual Spell's compiled RitualSummon procedure, so they
        /// are absorbed exactly like the modern wording above.
        /// </summary>
        static readonly Regex RxRitualLegacyOnlyWith = new(@"This (?:card|monster) can only be Ritual Summoned with the Ritual Spell Card, ""[^""]+""\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // SpellCounterSpenders batch: also the DCR word order, "You must also Tribute monsters from your hand or field whose
        // total Levels equal 7 or more." (Legendary Flame Lord; its Ritual Spell, Incandescent Ordeal, prints the same).
        static readonly Regex RxRitualLegacyTributeLevels = new(@"You must also (?:offer|Tribute) monsters (?:whose total Level(?: Stars|s)? equal \d+ or more(?: as a Tribute)? from (?:the field or your hand|your hand or field)|from (?:the field or your hand|your hand or field) whose total Level(?: Stars|s)? equal \d+ or more)\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxRelinquishedBattleSubstitute = new(@"If this card would be destroyed by battle, destroy that equipped monster instead\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxRelinquishedDamageReflect = new(@"While equipped with that monster, any battle damage you take from battles involving this card inflicts equal effect damage to your opponent\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>Element monsters: conditional bullet effects keyed on Attributes on the field.</summary>
        static readonly Regex RxConditionalAttributeBullets = new(
            @"(?:gets|gains) the following effect\(s\) while there (?:is|are) (?:a )?monster\(s\) with the following Attribute\(s\) on the field",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Compile official text into a program (does not touch cache).</summary>
        public static CompiledCardProgram Compile(CardDef def) =>
            Compile(def, eraId: null);

        public static CompiledCardProgram Compile(CardDef def, string eraId)
        {
            // Two template lineages cover overlapping card text since the v106 merge: the local
            // v100-v102 templates (Legacy/Atom/Advanced/Phase/Continuous/MonsterTrigger) and the GitHub
            // v51-v55 ClassicEraTemplates. Compile with the local templates first; fall back to the
            // ClassicEra-first order when that leaves text unparsed (a local template matched only part
            // of a sentence ClassicEra owns whole, e.g. Giant Germ) or double-compiled one effect
            // (the same timing + action twice where ClassicEra-first has it once, e.g. Nimble Momonga).
            var localFirst = CompileOrdered(def, eraId, classicEraFirst: false);
            if (def == null || !ClassicEraTemplates.MayMatch(Normalize(localFirst.SourceText ?? ""), def))
                return localFirst;
            var classicFirst = CompileOrdered(def, eraId, classicEraFirst: true);
            if (!classicFirst.FullyCompiled) return localFirst;
            classicFirst.CompileSource = ClassicEraCompileSource;
            if (def.name != null && ClassicEraArmCards.Contains(def.name)) return classicFirst;
            if (!localFirst.FullyCompiled) return classicFirst;
            return DuplicateEffectCount(localFirst) > DuplicateEffectCount(classicFirst)
                ? classicFirst
                : localFirst;
        }

        /// <summary>
        /// GitHub-lineage templates (ClassicEra / Archfiend) name the affected player with
        /// <see cref="EffectClause.OpponentIsSubject"/> and leave <see cref="EffectClause.Side"/> at its
        /// default (Opponent); the local-lineage runtime reads Side for Draw / Gain LP / Pay LP. Make
        /// Side say the same thing as OpponentIsSubject on those clauses (Both stays Both).
        /// </summary>
        static void NormalizeGitHubSubject(List<EffectClause> clauses, int from)
        {
            for (var i = from; i < clauses.Count; i++)
            {
                var c = clauses[i];
                if (c == null || c.Side == EffectSide.Both) continue;
                if (c.Action != EffectActionKind.Draw && c.Action != EffectActionKind.GainLifePoints &&
                    c.Action != EffectActionKind.PayLifePoints)
                    continue;
                c.Side = c.OpponentIsSubject ? EffectSide.Opponent : EffectSide.Controller;
            }
        }

        /// <summary><see cref="CompiledCardProgram.CompileSource"/> of a program compiled ClassicEra-first.</summary>
        public const string ClassicEraCompileSource = "regex:classic-era";

        /// <summary>
        /// Cards both lineages compile where the GitHub (ClassicEra) arm is kept even though the local arm
        /// also fully compiles: the local arm was narrower than the printed text (Paralyzing Potion /
        /// Germ Infection could only equip your own monsters and did not refuse Machines).
        /// </summary>
        static readonly HashSet<string> ClassicEraArmCards = new(StringComparer.OrdinalIgnoreCase)
        {
            "Paralyzing Potion", "Germ Infection",
        };

        static string FirstNonTypeRaceFilter(List<EffectClause> clauses)
        {
            foreach (var c in clauses)
            {
                if (c == null || string.IsNullOrEmpty(c.RaceFilter)) continue;
                foreach (var part in c.RaceFilter.Split(',', '|'))
                {
                    var t = part.Trim();
                    if (t.EndsWith("-Type")) t = t.Substring(0, t.Length - 5);
                    if (!ClassicEraTemplates.MonsterTypes.Contains(t)) return c.RaceFilter;
                }
            }

            return null;
        }

        /// <summary>Clauses repeating an earlier clause's Timing + Action (a double-compiled effect).</summary>
        static int DuplicateEffectCount(CompiledCardProgram prog)
        {
            if (prog?.ClauseList == null) return 0;
            var seen = new HashSet<(EffectTiming, EffectActionKind)>();
            var dup = 0;
            foreach (var c in prog.ClauseList)
                if (c != null && !seen.Add((c.Timing, c.Action))) dup++;
            return dup;
        }

        static CompiledCardProgram CompileOrdered(CardDef def, string eraId, bool classicEraFirst)
        {
            var source = string.IsNullOrEmpty(eraId)
                ? OfficialCardAuthority.OfficialText(def)
                : OfficialCardAuthority.OfficialText(def, eraId);
            var prog = new CompiledCardProgram
            {
                CardId = def?.id ?? 0,
                CardName = def?.name ?? "",
                TextHash = string.IsNullOrEmpty(eraId)
                    ? OfficialCardAuthority.TextHash(def)
                    : OfficialCardAuthority.TextHash(def, eraId),
                SourceText = source,
                CompiledUtc = DateTime.UtcNow.ToString("o"),
                CompilerVersion = Version,
                CompileSource = "regex"
            };

            if (def == null)
                return prog;

            var text = Normalize(prog.SourceText ?? "");
            var matchedSpans = new List<(int start, int length)>();
            var clauses = new List<EffectClause>();
            var unparsed = new List<string>();

            void Take(Match m, EffectClause clause)
            {
                if (m == null || !m.Success) return;
                clause.SourceSnippet = m.Value.Trim();
                clauses.Add(clause);
                matchedSpans.Add((m.Index, m.Length));
            }

            // —— Ritual Spells (race "Ritual"): the whole text is the Ritual Summon
            // instruction, so consume it all and emit one RitualSummon clause. ——
            if (def.IsSpell &&
                string.Equals(def.race, "Ritual", StringComparison.OrdinalIgnoreCase))
            {
                var named = RxRitualNamed.Match(text);
                var anyAttr = RxRitualAnyAttr.Match(text);
                if (named.Success || anyAttr.Success)
                {
                    var lvlM = RxRitualLevel.Match(text);
                    // "...whose total Levels exactly equal 8." (named Equal procedure).
                    if (!lvlM.Success)
                        lvlM = Regex.Match(text, @"total Levels? exactly equal (\d+)", RegexOptions.IgnoreCase);
                    var clause = new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.RitualSummon,
                        // The real requirement is the summoned monster's Level (resolved at
                        // runtime); the printed number, when present, equals it. 0 = derive.
                        Amount = lvlM.Success ? ParseInt(lvlM, 1, 0) : 0,
                        // "...exactly equal the Level of the Ritual Monster..." (Chant cards).
                        RitualExactLevel = Regex.IsMatch(text, @"exactly equal", RegexOptions.IgnoreCase),
                        // The Ritual Monster is Special Summoned from the hand (local RitualProcedures reads this).
                        FromHand = true,
                        SourceSnippet = text.Trim(),
                    };
                    if (named.Success)
                        clause.NamedCard = named.Groups[1].Value;
                    else
                        clause.AttributeFilter = anyAttr.Groups[1].Value.ToUpperInvariant();
                    clauses.Add(clause);
                    matchedSpans.Add((0, text.Length));
                }
            }

            // Order: multi-sentence templates first, then short ones
            {
                // Monster Quick Effects (Gardna / Paladin / Horus) and Counter Traps
                // (Seven Tools / Magic Jammer). Not Normal Traps, not SS/position atoms.
                var isCounterTrap = def.IsTrap &&
                                    OfficialEffectRegistry.SpeedOf(def) == SpellSpeed.Speed3;
                if (def.IsMonster || isCounterTrap)
                {
                var neg = SharedPsctAtomTemplates.MatchNegateActivation(text);
                // ChainLinkAnswers batch: one bullet of an "Activate 1 of these effects" text is not the card's effect (Spell
                // Shield Type-8); the bullets compile together (ChainLinkAnswersAtoms) or not at all.
                if (neg.Success && !Regex.IsMatch(text, @"^\s*Activate 1 of these effects", RegexOptions.IgnoreCase))
                {
                    var hasDestroy = Regex.IsMatch(neg.Value, "destroy it", RegexOptions.IgnoreCase);
                    var hasDiscard = Regex.IsMatch(neg.Value, "discard 1 card", RegexOptions.IgnoreCase);
                    var pay = Regex.Match(neg.Value, @"pay (\d+) (?:LP|Life Points)", RegexOptions.IgnoreCase);
                    var kind = neg.Groups["kind"].Success ? neg.Groups["kind"].Value : "Spell";
                    var answersSpell = kind.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0;
                    var answersTrap = kind.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0;
                    Take(neg, new EffectClause
                    {
                        Timing = EffectTiming.ChainLinkActivated,
                        Action = EffectActionKind.NegateActivation,
                        ChainResponseOnly = true,
                        DestroyNegatedCard = hasDestroy,
                        FlipSelfFaceUpDefense = Regex.IsMatch(neg.Value, "change this card to face-up defense position", RegexOptions.IgnoreCase),
                        RequiresDiscardCost = hasDiscard,
                        DiscardCostAttribute = hasDiscard ? "*" : null,
                        DiscardCostCount = hasDiscard ? 1 : 0,
                        PayLpAmount = pay.Success && int.TryParse(pay.Groups[1].Value, out var lp) ? lp : 0,
                        NegateRespondsToSpell = answersSpell,
                        NegateRespondsToTrap = answersTrap
                    });
                }
                }
            }
            // Cyber Jar: its whole text is one FlipMassEffects batch atom (Batches/FlipMassEffectsAtoms.cs, v142).

            Take(RxSwords.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ApplySwordsOfRevealingLight,
                Amount = ParseInt(RxSwords.Match(text), 1, 3),
                StaysOnField = true
            });

            Take(RxConvertOpponentLpGainToDamage.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ConvertOpponentLpGainToDamage,
                StaysOnField = true,
                MakesChainLink = false
            });
            // Swords sub-clauses absorbed into ApplySwordsOfRevealingLight
            MarkAbsorbed(text, RxSwordsAttack, matchedSpans);
            MarkAbsorbed(text, RxSwordsFlip, matchedSpans);

            Take(RxRing.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.EffectDamageBothFromOriginalAtk,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                OpponentTurnOnly = true
            });

            // Threatening Roar: opponent cannot declare an attack this turn (only).
            // Fail-closed vs ContinuousCannotAttack / NegateAttack / Waboku / self-lock.
            {
                var roar = RxOppCannotDeclareAttackThisTurn.Match(text);
                if (roar.Success)
                    Take(roar, SharedPsctAtomTemplates.MakeOppCannotDeclareAttackThisTurn());
            }

            // A Feint Plan: neither player can attack face-down monsters this turn.
            // Fail-closed vs Roar any-attack / Waboku / Swords / Negate Attack / Messenger / Gravity Bind.
            {
                var feint = RxFeintPlan.Match(text);
                if (feint.Success)
                    Take(feint, SharedPsctAtomTemplates.MakeCannotAttackFaceDownMonstersThisTurn());
            }

            // Remove Brainwashing: ContinuousWhileFaceUp mass return/snap control to owners.
            // Fail-closed vs take-until-EP / Equip steal / Creature Swap / single-target / destroy / bounce.
            {
                var rb = RxRemoveBrainwashing.Match(text);
                if (rb.Success && SharedPsctAtomTemplates.TryMatchRemoveBrainwashing(text, out _))
                    Take(rb, SharedPsctAtomTemplates.MakeReturnControlToOwners());
            }

            // Mask of Restrict: ContinuousWhileFaceUp NeitherPlayerCanTribute (both).
            // Fail-closed vs Soul Exchange / Lava Golem SS-tribute / per-monster / destroy / bounce.
            {
                var maskRestrict = RxMaskOfRestrict.Match(text);
                if (maskRestrict.Success && SharedPsctAtomTemplates.TryMatchMaskOfRestrict(text, out _))
                    Take(maskRestrict, SharedPsctAtomTemplates.MakeNeitherPlayerCanTribute());
            }

            // The Dark Door: ContinuousWhileFaceUp OnlyOneMonsterMayAttackPerBattlePhase (both).
            // Fail-closed vs Gravity Bind / Messenger / Roar / Feint / Extra Attacks / destroy / bounce.
            {
                var darkDoor = RxDarkDoor.Match(text);
                if (darkDoor.Success && SharedPsctAtomTemplates.TryMatchDarkDoor(text, out _))
                    Take(darkDoor, SharedPsctAtomTemplates.MakeOnlyOneMonsterMayAttackPerBattlePhase());
            }

            // Infinite Cards: ContinuousWhileFaceUp NoHandSizeLimit (both).
            // Fail-closed vs Enervating Mist / Hieroglyph / Max hand draw / destroy / bounce.
            {
                var infiniteCards = RxInfiniteCards.Match(text);
                if (infiniteCards.Success && SharedPsctAtomTemplates.TryMatchInfiniteCards(text, out _))
                    Take(infiniteCards, SharedPsctAtomTemplates.MakeNoHandSizeLimit());
            }

            // Enervating Mist: ContinuousWhileFaceUp OpponentHandSizeLimitBecomes (opp-only Amount).
            // Fail-closed vs Infinite Cards / Hieroglyph / Max hand draw / destroy / bounce.
            {
                var enervatingMist = RxEnervatingMist.Match(text);
                if (enervatingMist.Success && SharedPsctAtomTemplates.TryMatchEnervatingMist(text, out _))
                {
                    var amt = 5;
                    if (int.TryParse(enervatingMist.Groups[1].Value, out var parsed) && parsed > 0)
                        amt = parsed;
                    Take(enervatingMist, SharedPsctAtomTemplates.MakeOpponentHandSizeLimitBecomes(amt));
                }
            }

            // Spell Economics: ContinuousWhileFaceUp ControllerDoesNotPayLpToActivateSpells (controller).
            // Fail-closed vs Trap LP / opponent Spell costs / Standby maintenance / destroy / bounce.
            {
                var spellEconomics = RxSpellEconomics.Match(text);
                if (spellEconomics.Success && SharedPsctAtomTemplates.TryMatchSpellEconomics(text, out _))
                    Take(spellEconomics, SharedPsctAtomTemplates.MakeControllerDoesNotPayLpToActivateSpells());
            }

            // The Emperor's Holiday: ContinuousWhileFaceUp NegateAllEquipCardEffectsOnField (both).
            // Fail-closed vs Royal Decree / Imperial Order / Spell Canceller / destroy Equips / bounce.
            {
                var emperorsHoliday = RxEmperorsHoliday.Match(text);
                if (emperorsHoliday.Success && SharedPsctAtomTemplates.TryMatchEmperorsHoliday(text, out _))
                    Take(emperorsHoliday, SharedPsctAtomTemplates.MakeNegateAllEquipCardEffectsOnField());
            }

            // Protector of the Sanctuary: ContinuousWhileFaceUp OpponentCannotDrawExceptDuringDrawPhases (opp).
            // Fail-closed vs Time Seal / search AddToHand / neither-player / destroy / bounce.
            {
                var protector = RxProtectorOfTheSanctuary.Match(text);
                if (protector.Success && SharedPsctAtomTemplates.TryMatchProtectorOfTheSanctuary(text, out _))
                    Take(protector, SharedPsctAtomTemplates.MakeOpponentCannotDrawExceptDuringDrawPhases());
            }

            // Swords of Concealing Light: Activate SetAllOppMonstersFaceDownDefense + ContinuousWhileFaceUp
            // OpponentCannotChangeBattlePositions + Soft Standby ticker (Amount=2).
            // Fail-closed vs Revealing Light / Book of Moon / Jar / Spellbinding / Ceasefire / bounce.
            {
                if (SharedPsctAtomTemplates.TryMatchSwordsOfConcealingLight(text, out var sclMatch) &&
                    sclMatch.Success)
                {
                    Take(sclMatch, SharedPsctAtomTemplates.MakeSetAllOppMonstersFaceDownDefense(2));
                    Take(sclMatch, SharedPsctAtomTemplates.MakeOpponentCannotChangeBattlePositions());
                    // Soft residual: MarkAbsorbed companion sentences if Fanbot order differs.
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"Destroy this card during your 2nd Standby Phase after activation\.?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"When this card resolves, change all monsters your opponent controls to face-down Defense Position\.?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"Monsters your opponent controls cannot change their battle positions\.?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                }
                else
                {
                    var scl = RxSwordsOfConcealingLight.Match(text);
                    if (scl.Success)
                    {
                        Take(scl, SharedPsctAtomTemplates.MakeSetAllOppMonstersFaceDownDefense(2));
                        Take(scl, SharedPsctAtomTemplates.MakeOpponentCannotChangeBattlePositions());
                    }
                }
            }

            // Lightforce Sword: Activate BanishRandomFromOppHandFaceDown Amount=4 Soft Standby return.
            // Fail-closed vs choose/look / discard / face-up / your·next Standby / Disappear GY.
            {
                if (SharedPsctAtomTemplates.TryMatchLightforceSword(text, out var lfsMatch) &&
                    lfsMatch.Success)
                {
                    Take(lfsMatch, SharedPsctAtomTemplates.MakeBanishRandomFromOppHandFaceDown(4));
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"Banish 1 random card from your opponent's hand, face-down\.?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"During your opponent's 4th Standby Phase after this card's activation, " +
                            @"return that card to their hand\.?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                }
                else
                {
                    var lfs = RxLightforceSword.Match(text);
                    if (lfs.Success)
                        Take(lfs, SharedPsctAtomTemplates.MakeBanishRandomFromOppHandFaceDown(4));
                }
            }



            // Fake Trap: Activate DestroyThisInsteadOfControllerTraps Soft destroy-replacement.
            // Fail-closed vs banish/send/return / Spell-only / battle substitute.
            // Soft residual look-confirm Soft-OK (parenthetical Soft-absorbed).
            {
                if (SharedPsctAtomTemplates.TryMatchFakeTrap(text, out var ftMatch) &&
                    ftMatch.Success)
                {
                    Take(ftMatch, SharedPsctAtomTemplates.MakeDestroyThisInsteadOfControllerTraps());
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"Activate only when your opponent activates a Spell, Trap, or Effect Monster(?:'s)? effect that would destroy a Trap Card\(s\) you control\.?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"Destroy this card instead\.?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                    MarkAbsorbed(text,
                        new System.Text.RegularExpressions.Regex(
                            @"\(If the cards that would have been destroyed are face-down, you can look at them to confirm\.\)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        matchedSpans);
                }
                else
                {
                    var ft = RxFakeTrap.Match(text);
                    if (ft.Success)
                        Take(ft, SharedPsctAtomTemplates.MakeDestroyThisInsteadOfControllerTraps());
                }
            }

            // Secret Barrel: Inflict N × (opp hand + cards they control) — compound only.
            // Fail-closed vs Just Desserts (monster-only) / Des Koala (Flip hand) /
            // Ceasefire Effect-monster / flat Poison Mummy / Raimei.
            {
                var barrel = RxSecretBarrel.Match(text);
                if (barrel.Success)
                    Take(barrel, SharedPsctAtomTemplates.MakeInflictDamageScaledByOppHandAndControlled(
                        ParseInt(barrel, 1, 200)));
            }

            // Just Desserts: Inflict N × opponent monsters they control (only).
            // Fail-closed vs Secret Barrel / Ceasefire Effect-monster / Des Koala hand /
            // flat Poison Mummy / Raimei / Gift all-field LP.
            {
                var jd = RxJustDesserts.Match(text);
                if (jd.Success)
                    Take(jd, SharedPsctAtomTemplates.MakeInflictDamageScaledByOppMonsterCount(
                        ParseInt(jd, 1, 500)));
            }

            Take(RxWaboku.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ApplyWabokuStyle
            });

            Take(RxKuribohStyle.Match(text), new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.DiscardSelfNoBattleDamageThisBattle,
                Side = EffectSide.Controller
            });

            // Morphing Jar: Flip discard-all then each draws N (Amount lock).
            // Fail-closed vs Card Destruction redraw=discarded / Cyber Jar / Drop Off / Heart /
            // wipe-without-redraw / controller-only draw-5.
            {
                var morph = RxMorphingJar.Match(text);
                if (morph.Success)
                    Take(morph, SharedPsctAtomTemplates.MakeBothPlayersDiscardAndDrawFixed(
                        ParseInt(morph, 1, 5)));
            }

            Take(RxCardDestruction.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.BothPlayersDiscardAndRedraw
                // Amount 0 = redraw discarded count (Card Destruction). Morphing Jar uses Amount > 0.
            });

            Take(RxDestroyAllOppMonsters.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.FieldMonsters
            });

            // Dark Hole: all monsters — only if not already matched "opponent controls"
            // or a summon-window Torrential sentence that contains the same fragment.
            var darkHole = RxDestroyAllMonsters.Match(text);
            // Cyber Jar's "Destroy all monsters on the field, then …" belongs to its whole-text batch atom.
            // Blind Destruction's "destroy all monsters on the field with the same Level" belongs to its whole-text
            // batch atom (StandbyBurnsAndDice, v162): a die-roll Level wipe in the Standby Phase, not a Dark Hole.
            if (darkHole.Success && !ContainsSnippet(clauses, "your opponent controls") &&
                !RxTorrentialTribute.IsMatch(text) && !FlipMassEffectsAtoms.IsCyberJarText(text) &&
                !StandbyBurnsAndDiceAtoms.IsWholeTextAtom(text))
            {
                Take(darkHole, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldMonsters
                });
            }

            // A Wingbeat of Giant Dragon's "…, and if you do, destroy all Spell and Trap Cards on the field" belongs to
            // its whole-text batch atom (SimpleSpellTraps): the destroy happens only if the monster reached the hand.
            if (!SimpleSpellTrapsAtoms.IsWholeTextAtom(text))
                Take(RxDestroyAllST.Match(text), new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldSpellTraps
                });

            Take(RxDestroyAllOppST.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.FieldSpellTraps
            });


            // Dust Tornado: opp-ST destroy + optional Set 1 ST from hand (exact compound).

            // Beast Soul Swap: bounce controller Race + SS same Race from hand matching prior Level.

            // Freed: banish N ATTR from GY → destroy face-up higher-ATK-than-source (exact).
            var freedHigher = RxFreedBanishAttrGyDestroyHigherAtk.Match(text);
            if (freedHigher.Success)
            {
                var n = int.TryParse(freedHigher.Groups[1].Value, out var bn) ? bn : 2;
                var attr = freedHigher.Groups[2].Value;
                Take(freedHigher,
                    SharedPsctAtomTemplates.MakeBanishAttrFromGyThenDestroyHigherAtkThanSource(n, attr));
            }

            var beastSwap = RxBeastSoulSwapBounceThenSsSameLevel.Match(text);
            if (beastSwap.Success)
            {
                var race = beastSwap.Groups[1].Value;
                Take(beastSwap, SharedPsctAtomTemplates.MakeReturnControllerRaceMonsterToHand(race));
                Take(beastSwap, SharedPsctAtomTemplates.MakeSpecialSummonRaceFromHandMatchingPriorLevel(race));
            }

            var dustSet = RxOppStDestroyThenOptionalSetStFromHand.Match(text);
            if (dustSet.Success)
            {
                Take(dustSet, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                });
                Take(dustSet, SharedPsctAtomTemplates.MakeOptionalSetSpellTrapFromHand());
            }

            Take(RxTargetStDestroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true
            });

            Take(RxMonsterReborn.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFromGy,
                Zone = EffectZoneFilter.EitherGyMonsters,
                RequiresTargetChoice = true
            });

            // Reckless Greed: Draw N + controller skip next M Draw Phases (exact printed N/M).
            var drawSkip = RxDrawAndSkipControllerDrawPhases.Match(text);
            if (drawSkip.Success)
            {
                Take(drawSkip, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = ParseInt(drawSkip, 1, 2),
                    MakesChainLink = true
                });
                Take(drawSkip, SharedPsctAtomTemplates.MakeSkipControllerNextDrawPhases(
                    ParseInt(drawSkip, 2, 2)));
            }

            // Offerings to the Doomed: destroy face-up field target + controller skip next N Draw Phase(s).
            var destSkip = RxDestroyFaceUpAlsoSkipControllerDrawPhases.Match(text);
            if (destSkip.Success)
            {
                var skipN = destSkip.Groups[1].Success && int.TryParse(destSkip.Groups[1].Value, out var n) && n > 0
                    ? n : 1;
                Take(destSkip, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    // EngineResiduals (v163): "Target 1 face-up monster" (Lua: Card.IsFaceup at target and resolution).
                    RequiresTargetFaceUp = true,
                    MakesChainLink = true
                });
                Take(destSkip, SharedPsctAtomTemplates.MakeSkipControllerNextDrawPhases(skipN));
            }



            // Upstart Goblin: Draw N + opponent gains M LP (exact printed N/M).
            var drawOppLp = RxDrawThenOpponentGainsLp.Match(text);
            if (drawOppLp.Success)
            {
                Take(drawOppLp, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = ParseInt(drawOppLp, 1, 1),
                    MakesChainLink = true
                });
                Take(drawOppLp, SharedPsctAtomTemplates.MakeOpponentGainLifePoints(
                    ParseInt(drawOppLp, 2, 1000)));
            }

            Take(RxDraw.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Draw,
                Side = EffectSide.Controller,
                Amount = ParseInt(RxDraw.Match(text), 1, 2)
            });

            Take(RxTargetMonsterReturnHand.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ReturnToHand,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                Amount = 1,
                MakesChainLink = true
            });

            Take(RxFlipDestroyMonster.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true
            });

            Take(RxFlipDestroyOppMonster.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.OppMonsters,
                RequiresTargetChoice = true
            });

            Take(RxFlipDestroySpellOnField.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true,
                DestroyIfType = "Spell",
                // A face-up card of the wrong kind is not a legal target (GitHub lineage); a Set card
                // is, and is revealed on resolution (DestroyIfType).
                TargetCardKind = "Spell"
            });
            MarkAbsorbed(text, RxFlipRevealSetIfType, matchedSpans);

            Take(RxFlipDestroyTrapOnField.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true,
                DestroyIfType = "Trap",
                // A face-up card of the wrong kind is not a legal target (GitHub lineage); a Set card
                // is, and is revealed on resolution (DestroyIfType).
                TargetCardKind = "Trap"
            });

            Take(RxFlipDestroyTrapOnFieldReveal.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true,
                DestroyIfType = "Trap",
                // A face-up card of the wrong kind is not a legal target (GitHub lineage); a Set card
                // is, and is revealed on resolution (DestroyIfType).
                TargetCardKind = "Trap"
            });

            var exodia = RxExodiaWin.Match(text);
            Take(exodia, exodia.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ExodiaWinStyle,
                    NamedCard = string.Join("|",
                        exodia.Groups[1].Value, exodia.Groups[2].Value,
                        exodia.Groups[3].Value, exodia.Groups[4].Value),
                    MakesChainLink = false
                }
                : null);

            var jar = RxDragonCaptureJar.Match(text);
            Take(jar, jar.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Both,
                    RaceFilter = jar.Groups[1].Value,
                    ForceDefensePosition = true,
                    AlsoCannotChangeBattlePosition = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);
            if (jar.Success)
            {
                clauses.Add(new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Both,
                    RaceFilter = jar.Groups[1].Value,
                    ForceDefensePosition = true,
                    AlsoCannotChangeBattlePosition = true,
                    StaysOnField = true,
                    MakesChainLink = false,
                    SourceSnippet = jar.Value.Trim()
                });
            }

            var ghoul = RxGravediggerGhoul.Match(text);
            Take(ghoul, ghoul.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Banish,
                    Zone = EffectZoneFilter.OppGyMonsters,
                    RequiresTargetChoice = true,
                    Amount = ParseInt(ghoul, 1, ParseInt(ghoul, 2, 2)),
                    MakesChainLink = true
                }
                : null);

            var soulRel = RxBanishUpToCardsAnyGy.Match(text);
            Take(soulRel, soulRel.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Banish,
                    Zone = EffectZoneFilter.EitherGyCards,
                    RequiresTargetChoice = true,
                    Amount = ParseInt(soulRel, 1, 5),
                    MakesChainLink = true
                }
                : null);

            Take(RxTakeControlUntilEndPhase.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.TakeControlUntilEndPhase,
                Zone = EffectZoneFilter.OppMonsters,
                RequiresTargetChoice = true,
                Amount = 1,
                MakesChainLink = true
            });

            var flipCtrl = RxFlipTakeControlUntilEndPhase.Match(text);
            Take(flipCtrl, flipCtrl.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.TakeControlUntilEndPhase,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RaceFilter = flipCtrl.Groups[1].Value,
                    RequiresTargetChoice = true,
                    Amount = 1
                }
                : null);

            var flipCtrlFace = RxFlipTakeControlFaceUpRaceUntilEndPhase.Match(text);
            Take(flipCtrlFace, flipCtrlFace.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.TakeControlUntilEndPhase,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RaceFilter = flipCtrlFace.Groups[1].Value,
                    RequiresTargetChoice = true,
                    Amount = 1
                }
                : null);

            var prong = RxTwoProngedAttack.Match(text);
            Take(prong, prong.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    RequiresTargetChoice = true,
                    ControllerTargetCount = ParseInt(prong, 1, 2),
                    OpponentTargetCount = ParseInt(prong, 2, 1),
                    SecondZone = EffectZoneFilter.OppMonsters,
                    SecondAction = EffectActionKind.Destroy,
                    RequiresSecondTarget = true
                }
                : null);

            MarkAbsorbed(text, RxNotTreatedAsNamed, matchedSpans);

            Take(RxFlipSpellToHand.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.AddFromGyToHand,
                Zone = EffectZoneFilter.ControllerGySpells,
                RequiresTargetChoice = true
            });

            Take(RxFlipTrapToHand.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.AddFromGyToHand,
                Zone = EffectZoneFilter.ControllerGyTraps,
                RequiresTargetChoice = true
            });

            Take(RxTrapHole.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxTrapHole.Match(text), 1, 1000),
                RequiresTargetChoice = true
            });

            Take(RxSummonDestroyBanish.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxSummonDestroyBanish.Match(text), 1, 1500),
                RequiresTargetChoice = false,
                BanishIfDestroyed = true,
                AnswersSpecialSummon = true
            });

            Take(RxAdhesionTrapHole.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.HalveOriginalAtk,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = false,
                AnswersSpecialSummon = true
            });

            Take(RxTorrentialTribute.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldMonsters,
                RequiresTargetChoice = false,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            var hhg = RxNamedOrNamedSummonDestroySt.Match(text);
            Take(hhg, hhg.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.OpponentNormalOrFlipSummon,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    AnswersSpecialSummon = true,
                    AnswersControllerSummon = true,
                    NamedCard = hhg.Groups[1].Value,
                    AltNamedCard = hhg.Groups[2].Value,
                    NamedCardIsSeries = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            Take(RxOppNsFsAtkLeqDestroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxOppNsFsAtkLeqDestroy.Match(text), 1, 500),
                AmountIsAtkMax = true,
                RequiresTargetChoice = false
            });

            Take(RxOppNsFsDefLeqDestroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxOppNsFsDefLeqDestroy.Match(text), 1, 500),
                AmountIsDefMax = true,
                RequiresTargetChoice = false
            });

            Take(RxTokenFeastevil.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.DestroyTokensInflictPer,
                Amount = ParseInt(RxTokenFeastevil.Match(text), 1, 300),
                RequiresSummonedIsToken = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxMispolymerization.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.ReturnAllFaceUpFusionsToExtra,
                RequiresSummonedIsFusion = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxChainDisappearance.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.BanishThenSameNameFromOppHandDeck,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxChainDisappearance.Match(text), 1, 1000),
                AmountIsAtkMax = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxChainDestruction.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.DestroySameNameInControllerHandAndDeck,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxChainDestruction.Match(text), 1, 2000),
                AmountIsAtkMax = true,
                RequiresTargetChoice = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxBlastHeldByTribute.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.DestroyOppAttackThenDamage,
                Zone = EffectZoneFilter.OppAttackPositionMonsters,
                Amount = ParseInt(RxBlastHeldByTribute.Match(text), 1, 1000),
                RequiresAttackerTributeSummoned = true
            });

            Take(RxDisarmament.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.DestroyAllEquips
            });

            Take(RxEternalRest.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.DestroyAllEquippedMonsters
            });

            // Widespread Ruin: AttackDeclared destroy opp highest-ATK Attack Position (choice if tied).
            // Fail-closed vs Mirror Force / Hammer Shot / Sakuretsu / Cylinder / Draining Shield / Negate Attack.
            {
                var ruin = RxWidespreadRuin.Match(text);
                if (ruin.Success)
                    Take(ruin, SharedPsctAtomTemplates.MakeDestroyOppHighestAtkAttackPositionOnAttackDeclared());
            }

            // Drop Off: Opp Draw Phase normal draw → discard that just-drawn card.
            // Fail-closed vs Time Seal / any-draw / random wipe / Card Destruction / Morphing Jar.
            {
                var drop = RxDropOff.Match(text);
                if (drop.Success)
                    Take(drop, SharedPsctAtomTemplates.MakeDiscardJustDrawnOnOpponentNormalDraw());
            }

            // Nutrient Z: DamageCalculation gain LP first if about to take 2000+ battle damage.
            // Fail-closed vs AfterDamageCalculation / any-damage / heal=damage / Dian Keto / Spring of Rebirth.
            {
                var nutrient = RxNutrientZ.Match(text);
                if (nutrient.Success)
                {
                    var minDmg = ParseInt(nutrient, 1, 2000);
                    var gain = ParseInt(nutrient, 2, 4000);
                    Take(nutrient, SharedPsctAtomTemplates.MakeGainLpDuringDamageCalcIfBattleDamageAtLeast(
                        minDmg, gain));
                }
            }

            // Heart of the Underdog: your Draw Phase Normal Monster draw → optional reveal → draw N more.
            // Fail-closed vs opp draw / Effect Monster / ControllerDraws any-draw / always-draw-2 / Jar reload.
            {
                var heart = RxHeartOfTheUnderdog.Match(text);
                if (heart.Success)
                {
                    var drawAmt = ParseInt(heart, 1, 1);
                    Take(heart, SharedPsctAtomTemplates.MakeRevealJustDrawnNormalAndDrawOnControllerNormalDraw(
                        drawAmt));
                }
            }

            // Spring of Rebirth: Continuous field→owner hand bounce → gain N LP.
            // Fail-closed vs Dian Keto Activate / Nutrient Z damage-calc / GY-send / bounce-destroy.
            {
                var spring = RxSpringOfRebirth.Match(text);
                if (spring.Success)
                {
                    var gain = 500;
                    if (spring.Groups[1].Success && int.TryParse(spring.Groups[1].Value, out var g1) && g1 > 0)
                        gain = g1;
                    else if (spring.Groups[2].Success && int.TryParse(spring.Groups[2].Value, out var g2) && g2 > 0)
                        gain = g2;
                    Take(spring, SharedPsctAtomTemplates.MakeGainLpWhenMonsterReturnsToOwnerHand(gain));
                }
            }

            // Banner of Courage: Continuous +200 ATK to your monsters during your Battle Phase only.
            // Fail-closed vs always-on aura / Yellow Luster Shield DEF / United We Stand Equip scale / opp BP.
            {
                var banner = RxBannerOfCourage.Match(text);
                if (banner.Success)
                {
                    var atk = ParseInt(banner, 1, 200);
                    Take(banner, SharedPsctAtomTemplates.MakeContinuousGainAtkDuringControllerBattlePhase(atk));
                }
            }

            // Mysterious Puppeteer: Continuous either-player Normal/Flip Summon → gain N LP.
            // Fail-closed vs Special Summon / Spring bounce / Dian Keto Activate / controller-only.
            {
                var puppeteer = RxMysteriousPuppeteer.Match(text);
                if (puppeteer.Success)
                {
                    var gain = 500;
                    if (puppeteer.Groups[1].Success && int.TryParse(puppeteer.Groups[1].Value, out var g1) && g1 > 0)
                        gain = g1;
                    else if (puppeteer.Groups[2].Success && int.TryParse(puppeteer.Groups[2].Value, out var g2) && g2 > 0)
                        gain = g2;
                    Take(puppeteer, SharedPsctAtomTemplates.MakeGainLpWhenEitherPlayerNormalOrFlipSummons(gain));
                }
            }

            // Backup Soldier: Activate while GY≥N — up to K non-Effect ≤ATK from your GY to hand.
            // Fail-closed vs Effect Monsters / ATK>cap / GY<N / Monster Reborn·Call SS / Extra-as-hand.
            {
                var backup = RxBackupSoldier.Match(text);
                if (backup.Success)
                {
                    var gyMin = ParseInt(backup, 1, 5);
                    var targetCount = ParseInt(backup, 2, 3);
                    var atkMax = ParseInt(backup, 3, 1500);
                    Take(backup, SharedPsctAtomTemplates.MakeAddNonEffectGyMonstersAtkLeqToHand(
                        gyMin, targetCount, atkMax));
                }
            }

            // Dark Factory: Activate — exact N Normal Monsters from your GY to hand.
            // Fail-closed vs Backup Soldier TargetUpTo/non-Effect/ATK/GY≥N / Reborn·Premature SS /
            // 1-target / Effect Monsters / opp GY.
            {
                var factory = RxDarkFactory.Match(text);
                if (factory.Success)
                    Take(factory, SharedPsctAtomTemplates.MakeAddExactNormalGyMonstersToHand(
                        ParseInt(factory, 1, 2)));
            }

            // Disappear: Activate — exact 1 card in opponent's GY; banish that target.
            // Soft: Remove-from-play → Banish. Fail-closed vs Gravedigger / Soul Release /
            // Chain Disappearance / destroy / your GY / up-to / monsters-only.
            {
                var disappear = RxDisappear.Match(text);
                if (disappear.Success)
                    Take(disappear, SharedPsctAtomTemplates.MakeBanishExactOppGyCard(1));
            }

            // Ceasefire: flip all face-down DEF (no Flip effects) + inflict N × face-up Effect Monsters.
            // Fail-closed vs Swords continuous reveal / Flip-activate / Just Desserts opp-monster /
            // burn-per-any-monster.
            {
                var cease = RxCeasefire.Match(text);
                if (cease.Success)
                {
                    Take(cease, SharedPsctAtomTemplates.MakeFlipAllFaceDownDefenseNoFlipEffects());
                    Take(cease, SharedPsctAtomTemplates.MakeInflictDamageScaledByFaceUpEffectMonsters(
                        ParseInt(cease, 1, 500)));
                }
            }

            // The Shallow Grave: each player own GY → face-down Defense SS to their field.
            // Fail-closed vs Monster Reborn / Call of the Haunted / both from one GY / face-up SS.
            {
                var shallow = RxShallowGrave.Match(text);
                if (shallow.Success)
                    Take(shallow, SharedPsctAtomTemplates.MakeEachPlayerSsOwnGyFaceDownDefense());
            }

            Take(RxMirrorForce.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppAttackPositionMonsters
            });

            Take(RxNegateAttack.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.NegateAttack,
                Zone = EffectZoneFilter.AttackingMonster
            });
            // end battle phase folded into NegateAttack action

            {
                // Sangan / Witch: compound search + name-lock only (Shared Make).
                if (SharedPsctAtomTemplates.TryMatchSanganWitchSearchWithNameLock(text, out var sangLock))
                {
                    var sangDef = sangLock.Groups[2].Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                    Take(sangLock, SharedPsctAtomTemplates.MakeAddDeckMonsterStatLeqWithNameLock(
                        ParseInt(sangLock, 1, 1500), sangDef));
                }
                else
                {
                    // Legacy search-only RxSangan retained only if compound already present
                    // in older trees; modern prints require the lock (fail-closed).
                    var sang = RxSangan.Match(text);
                    var sangDef = sang.Success &&
                        sang.Groups[2].Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                    // Only Take when the match itself includes the name-lock span (compound Rx).
                    if (sang.Success &&
                        sang.Value.IndexOf("cannot activate cards", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Take(sang, SharedPsctAtomTemplates.MakeAddDeckMonsterStatLeqWithNameLock(
                            ParseInt(sang, 1, 1500), sangDef));
                    }
                }
            }


            Take(RxFlute.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFromHand,
                Zone = EffectZoneFilter.ControllerHandDragons,
                Amount = ParseInt(RxFlute.Match(text), 1, 2),
                RequiresLordOfDOnField = true
            });

            Take(RxPolymerization.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.FusionSummonRegistered
            });

            // Ritual Spells: the GitHub-lineage whole-text branch at the top of Compile already emitted
            // the one RitualSummon clause for race "Ritual" Spells. These local-lineage phrasings only
            // fill in when it did not (never two RitualSummon clauses → never two Ritual Summons).
            var ritualAlready = ContainsAction(clauses, EffectActionKind.RitualSummon);
            if (!ritualAlready)
            {
                var namedG = RxRitualNamedGreater.Match(text);
                if (!namedG.Success) namedG = RxRitualNamedGreaterOne.Match(text);
                Take(namedG, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.RitualSummon,
                    NamedCard = namedG.Success ? namedG.Groups[1].Value : null,
                    Amount = namedG.Success ? (ParseInt(namedG, 2, 0) > 0 ? ParseInt(namedG, 2, 0) : ParseInt(namedG, 3, 0)) : 0,
                    RitualExactLevel = false,
                    FromHand = true
                });
            }
            if (!ritualAlready)
            Take(RxRitualNamedEqual.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.RitualSummon,
                NamedCard = Group1(RxRitualNamedEqual.Match(text)),
                Amount = ParseInt(RxRitualNamedEqual.Match(text), 2, 0),
                RitualExactLevel = true,
                FromHand = true
            });
            if (!ritualAlready)
            Take(RxRitualAttributeEqual.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.RitualSummon,
                AttributeFilter = Group1(RxRitualAttributeEqual.Match(text)),
                RitualExactLevel = true,
                FromHand = true
            });

            Take(RxLordOfD.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousCannotTargetDragons
            });

            var attackNamedPos = RxAttackNamedChangePosition.Match(text);
            Take(attackNamedPos, new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.ChangeBattlePosition,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                RequiresFaceUpName = attackNamedPos.Success ? attackNamedPos.Groups[1].Value : null
            });

            if (!attackNamedPos.Success)
            {
                Take(RxEnemyControllerPos.Match(text), new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true
                });
            }

            Take(RxAlwaysTreatedAsName.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.AlwaysTreatedAsName,
                TreatedAsName = Group1(RxAlwaysTreatedAsName.Match(text)),
                StaysOnField = true
            });
            if (!ContainsAction(clauses, EffectActionKind.AlwaysTreatedAsName))
            {
                Take(RxNameTreatedAs.Match(text), new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AlwaysTreatedAsName,
                    TreatedAsName = Group1(RxNameTreatedAs.Match(text)),
                    StaysOnField = true
                });
            }

            Take(RxAllAttrGainAtkDef.Match(text), GainAtkDefClause(RxAllAttrGainAtkDef.Match(text)));
            if (!ContainsAction(clauses, EffectActionKind.ContinuousGainAtkDef))
                Take(RxIncreasesAtkDefOfAll.Match(text), GainAtkDefClause(RxIncreasesAtkDefOfAll.Match(text)));

            Take(RxDiscardAttrBounceField.Match(text),
                DiscardBounceClause(RxDiscardAttrBounceField.Match(text)));
            if (!ContainsAction(clauses, EffectActionKind.ReturnToHand))
            {
                Take(RxDiscardAttrBounceFieldPsct.Match(text),
                    DiscardBounceClause(RxDiscardAttrBounceFieldPsct.Match(text)));
            }

            Take(RxReduceLevelHandsAndField.Match(text),
                ReduceLevelClause(RxReduceLevelHandsAndField.Match(text)));
            if (!ContainsAction(clauses, EffectActionKind.ContinuousReduceLevel))
            {
                Take(RxDowngradeLevelHandsAndField.Match(text),
                    ReduceLevelClause(RxDowngradeLevelHandsAndField.Match(text)));
            }


            // All-opp-DEF → can attack directly (Shadowslayer / Black Tyranno / Rocket Jumper).
            // BEFORE unconditional direct — trailing "this card can attack directly" is a near-miss.
            if (!ContainsAction(clauses, EffectActionKind.CanAttackDirectly))
            {
                var allDefDirect = RxAllOppMonstersDefenseCanAttackDirectly.Match(text);
                if (allDefDirect.Success)
                    Take(allDefDirect, SharedPsctAtomTemplates.MakeCanAttackDirectlyIfAllOppMonstersDefense());
            }

            Take(RxDirectAttackWhileNamed.Match(text),
                DirectAttackClause(RxDirectAttackWhileNamed.Match(text), named: true));
            if (!ContainsAction(clauses, EffectActionKind.CanAttackDirectly))
            {
                Take(RxDirectAttackUnconditional.Match(text),
                    DirectAttackClause(RxDirectAttackUnconditional.Match(text), named: false));
            }

            Take(RxExtraAttackWhileNamed.Match(text),
                ExtraAttackClause(RxExtraAttackWhileNamed.Match(text), named: true));
            if (!ContainsAction(clauses, EffectActionKind.ExtraAttacks))
            {
                Take(RxExtraAttackUnconditional.Match(text),
                    ExtraAttackClause(RxExtraAttackUnconditional.Match(text), named: false));
            }

            Take(RxSakuretsu.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.AttackingMonster,
                CheckedAt = ConditionCheckedAt.Activation
            });

            Take(RxDestroyLowestAtkOpp.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                PickLowestAtk = true
            });
            Take(RxDestroyHighestDefOpp.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                PickHighestDef = true
            });
            Take(RxDestroyHighestAtkFaceUpAtk.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                PickHighestAtk = true,
                RequiresAttackPosition = true
            });

            var incDef = RxIncreaseDefUntilEot.Match(text);
            Take(incDef, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.GainAtkDefUntilEndOfTurn,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                Amount = 0,
                DefAmount = incDef.Success ? ParseInt(incDef, 1, 500) : 500
            });

            Take(RxStopDefense.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ChangeBattlePosition,
                Zone = EffectZoneFilter.OppDefensePositionMonsters,
                RequiresTargetChoice = true,
                ForceAttackPosition = true
            });

            Take(RxBlockAttack.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ChangeBattlePosition,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                RequiresAttackPosition = true,
                ForceDefensePosition = true,
                IsPsctTarget = true
            });

            Take(RxOrderToCharge.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.ControllerMonsters,
                RequiresTargetChoice = true,
                RequiresNormalMonster = true,
                TributeChosenTarget = true,
                RequiresSecondTarget = true,
                SecondAction = EffectActionKind.Destroy,
                SecondZone = EffectZoneFilter.OppMonsters,
                IsPsctTarget = true
            });

            Take(RxEnchantedJavelin.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.GainLpEqualToAtk,
                Zone = EffectZoneFilter.AttackingMonster,
                CheckedAt = ConditionCheckedAt.Activation
            });

            var ego = RxElegantEgotist.Match(text);
            Take(ego, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonNamed,
                RequiresFaceUpName = ego.Success ? ego.Groups[1].Value : null,
                NamedCard = ego.Success ? ego.Groups[2].Value : null,
                AltNamedCard = ego.Success ? ego.Groups[3].Value : null,
                FromHand = true,
                FromDeck = true
            });

            // Magic Cylinder compound: NegateThisAttack + InflictDamageEqualToAtk (Shared).
            // Fail-closed vs Negate Attack / Draining Shield / bare damage. Do NOT ship
            // Draining Shield gain-LP rider in this chip.
            {
                var cyl = RxMagicCylinder.Match(text);
                if (cyl.Success)
                {
                    Take(cyl, SharedPsctAtomTemplates.MakeNegateThisAttackAttackingMonster());
                    Take(cyl, SharedPsctAtomTemplates.MakeInflictDamageEqualToAtkAttackingMonster());
                }
            }

            // Draining Shield compound: NegateThisAttack + GainLpEqualToAtk (Shared).
            // Fail-closed vs Magic Cylinder inflict-damage / Negate Attack / bare negate.
            // Reuses Cylinder NegateThisAttack maker; gain-LP rider only (no Cylinder bleed).
            {
                var shield = RxDrainingShield.Match(text);
                if (shield.Success)
                {
                    Take(shield, SharedPsctAtomTemplates.MakeNegateThisAttackAttackingMonster());
                    Take(shield, SharedPsctAtomTemplates.MakeGainLpEqualToAtkAttackingMonster());
                }
            }

            Take(RxDarkMirrorForce.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.Banish,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppDefensePositionMonsters
            });

            var bark = RxBarkOfDarkRuler.Match(text);
            Take(bark, new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                Zone = EffectZoneFilter.OpponentBattlingMonster,
                RaceFilter = bark.Success ? bark.Groups[1].Value : "Fiend",
                RequiresLpCostMultiple = bark.Success ? ParseInt(bark, 2, 100) : 100,
                DefAmount = 1,
                CheckedAt = ConditionCheckedAt.Both
            });

            var mask = RxMaskOfWeakness.Match(text);
            Take(mask, new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                Zone = EffectZoneFilter.AttackingMonster,
                Amount = mask.Success ? ParseInt(mask, 1, 700) : 700,
                DefAmount = 0
            });

            var fieldAs = RxFieldTreatedAs.Match(text);
            Take(fieldAs, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.FieldTreatedAsName,
                TreatedAsName = fieldAs.Success ? fieldAs.Groups[1].Value : null,
                MakesChainLink = false
            });
            MarkAbsorbed(text, RxNoFieldSpellBlocks, matchedSpans);

            var actNamed = RxActivateOnlyWhileNamed.Match(text);
            Take(actNamed, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.PreventControllerBattleDamage,
                RequiresFaceUpName = actNamed.Success ? actNamed.Groups[1].Value : null,
                StaysOnField = true,
                MakesChainLink = true
            });
            var noBd = RxNoBattleDamageWhileNamed.Match(text);
            Take(noBd, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.PreventControllerBattleDamage,
                RequiresFaceUpName = noBd.Success ? noBd.Groups[1].Value : null,
                StaysOnField = true,
                MakesChainLink = false
            });
            var leave = RxDestroyWhenNamedLeaves.Match(text);
            Take(leave, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.SelfDestroyUnlessNamedFaceUp,
                RequiresFaceUpName = leave.Success ? (leave.Groups[1].Success ? leave.Groups[1].Value : leave.Groups[2].Value) : null,
                MakesChainLink = false
            });
            var unlessYou = RxDestroyUnlessYouControlNamed.Match(text);
            Take(unlessYou, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.SelfDestroyUnlessNamedFaceUp,
                RequiresFaceUpName = unlessYou.Success ? unlessYou.Groups[1].Value : null,
                RequiresControllerNamedCard = true,
                NamedCardIsSeries = true,
                MakesChainLink = false
            });

            var rec = RxSummonGainLp.Match(text);
            Take(rec, new EffectClause
            {
                Timing = EffectTiming.ThisCardSummoned,
                Action = EffectActionKind.GainLifePoints,
                Amount = rec.Success ? ParseInt(rec, 1, 1000) : 1000,
                Side = EffectSide.Controller,
                MakesChainLink = true
            });

            var bothGain = RxBothPlayersGainLp.Match(text);
            Take(bothGain, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.GainLifePoints,
                Amount = bothGain.Success
                    ? ParseInt(bothGain, 1, ParseInt(bothGain, 2, 1000))
                    : 1000,
                Side = EffectSide.Both,
                MakesChainLink = true
            });
            var gyDmg = RxDestroyedToGyDamage.Match(text);
            Take(gyDmg, new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.TakeEffectDamage,
                Amount = gyDmg.Success ? ParseInt(gyDmg, 1, 2000) : 2000,
                Side = EffectSide.Controller,
                RequiresDestroyed = true,
                MakesChainLink = true
            });

            var sendOther = RxSendNamedDestroyAllOther.Match(text);
            if (!sendOther.Success) sendOther = RxSendNamedDestroyExceptThis.Match(text);
            Take(sendOther, SendNamedDestroyOthersClause(sendOther));

            var ghFrom = clauses.Count;
            ArchfiendTemplates.Collect(text, def, clauses, matchedSpans);
            NormalizeGitHubSubject(clauses, ghFrom);
            ProtectionTemplates.Collect(text, def, clauses, matchedSpans);
            if (classicEraFirst)
            {
                ghFrom = clauses.Count;
                ClassicEraTemplates.Collect(text, def, clauses, matchedSpans);
                NormalizeGitHubSubject(clauses, ghFrom);
            }
            LegacyTextTemplates.Collect(text, def, clauses, matchedSpans);
            AdvancedEffectTemplates.Collect(text, def, clauses, matchedSpans);
            PhaseTriggerTemplates.Collect(text, def, clauses, matchedSpans);
            ContinuousRestrictionTemplates.Collect(text, def, clauses, matchedSpans);
            MonsterTriggerTemplates.Collect(text, def, clauses, matchedSpans);
            if (!classicEraFirst)
            {
                ghFrom = clauses.Count;
                ClassicEraTemplates.Collect(text, def, clauses, matchedSpans);
                NormalizeGitHubSubject(clauses, ghFrom);
            }
            MarkAbsorbed(text, RxRitualProcedureText, matchedSpans);
            MarkAbsorbed(text, RxRitualLegacyOnlyWith, matchedSpans);
            MarkAbsorbed(text, RxRitualLegacyTributeLevels, matchedSpans);
            // A Ritual Monster whose whole text is its summon procedure is structural: the
            // NS/Set lock comes from IsRitualMonster and the procedure itself is the paired
            // Ritual Spell's RitualSummon clause. Same principle as "Must first be Special
            // Summoned with X" monsters (Harpie Lady Sisters). Regression tests assert that
            // every such monster has a Ritual Spell in cards_db compiling to RitualSummon
            // that names it — that is the fail-closed half of this rule.
            var ritualProcedureOnly = def != null && def.IsRitualMonster &&
                                      (RxRitualProcedureText.IsMatch(text) ||
                                       RxRitualLegacyOnlyWith.IsMatch(text));
            if (ContainsAction(clauses, EffectActionKind.EquipTargetToThis)) {
                MarkAbsorbed(text, RxRelinquishedBattleSubstitute, matchedSpans);
                MarkAbsorbed(text, RxRelinquishedDamageReflect, matchedSpans);
            }
            // Official PSCT split (condition : cost/target ; resolution) for sentences
            // the whole-card regex did not absorb. This is how new mechanics get in
            // without a unique full-text template for every card.
            EffectClause pendingCost = null;
            var pendingCostStart = 0;
            var pendingCostLen = 0;
            var structuralOnlyText = false;
            foreach (var sent in PsctGrammar.Parse(text, def))
            {
                if (sent == null || sent.Length < 3) continue;
                if (SpanCovered(matchedSpans, sent.IndexInText, sent.Length)) continue;
                if (sent.IsSummonRestriction || RxStructuralSummonText.IsMatch(sent.Raw ?? string.Empty) ||
                    DestinyBoardAtoms.IsPlacementLockSentence(sent.Raw)) { structuralOnlyText = true; matchedSpans.Add((sent.IndexInText, sent.Length)); continue; }
                var costOnly = TryParseCostOnlySentence(sent);
                if (costOnly != null)
                {
                    pendingCost = costOnly;
                    pendingCostStart = sent.IndexInText;
                    pendingCostLen = sent.Length;
                    continue;
                }

                var auras = CompileAuraClauses(sent.Raw);
                if (auras.Count > 0)
                {
                    foreach (var a in auras)
                    {
                        a.SourceSnippet = sent.Raw.Trim();
                        clauses.Add(a);
                    }

                    matchedSpans.Add((sent.IndexInText, sent.Length));
                    continue;
                }

                var clause = CompilePsctSentence(sent, def);
                if (clause == null) continue;
                if (pendingCost != null)
                {
                    MergeActivationCost(clause, pendingCost);
                    matchedSpans.Add((pendingCostStart, pendingCostLen));
                    pendingCost = null;
                }
                clause.SourceSnippet = sent.Raw.Trim();
                clauses.Add(clause);
                matchedSpans.Add((sent.IndexInText, sent.Length));
            }

            // Fairy Meteor Crush family: Equip Spell whose text only describes the
            // equipped monster. Stamp after all Collect so GY-SS Equips (Premature Burial)
            // are not given a field-host Activate.
            if (def != null && def.IsEquipSpell &&
                !ContainsAction(clauses, EffectActionKind.EquipThisToTarget) &&
                !ContainsAction(clauses, EffectActionKind.SpecialSummonFromGy))
            {
                clauses.Add(new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    RequiresTargetChoice = true,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    StaysOnField = true,
                    MakesChainLink = true,
                    SourceSnippet = "(implicit Equip Spell activate)"
                });
            }

            var una = RxUnaffectedByNamed.Match(text);
            if (una.Success)
            {
                foreach (var cl in clauses)
                {
                    if (cl != null)
                        cl.UnaffectedByNamedCard = una.Groups[1].Value;
                }
                matchedSpans.Add((una.Index, una.Length));
            }

            // Unparsed remainder for audit
            var remaining = MaskMatched(text, matchedSpans);
            foreach (var frag in SplitSentences(remaining))
            {
                if (string.IsNullOrWhiteSpace(frag)) continue;
                // Ignore hard-once-per-turn / you can only activate 1 boilerplate
                if (IsBoilerplate(frag)) continue;
                unparsed.Add(frag.Trim());
            }

            // UI choice is not automatically PSCT targeting: Fissure, Smashing Ground,
            // and Hammer Shot keep RequiresTargetChoice but do not target.
            // Equip Spells always target the equipped monster (even pre-PSCT "gains ATK" text).
            foreach (var c in clauses)
            {
                if (c != null && c.Action == EffectActionKind.EquipThisToTarget)
                    c.IsPsctTarget = true;
                if (c != null &&
                    Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"\btarget(?:s|ed|ing)?\b", RegexOptions.IgnoreCase))
                    c.IsPsctTarget = true;
                // SpellCounterSpenders batch: "a card that you can place a Spell Counter on" describes the card (a counter
                // permit), it does not make the effect optional (Mandragola's FLIP, Apprentice Magician's mandatory trigger).
                if (c != null && Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"\byou can\b(?! place a Spell Counter on)",
                        RegexOptions.IgnoreCase))
                    c.IsOptional = true;
                if (c != null && Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"^\s*When\b", RegexOptions.IgnoreCase))
                    c.CheckedAt = ConditionCheckedAt.Activation;
                else if (c != null && Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"^\s*If\b", RegexOptions.IgnoreCase))
                    c.CheckedAt = ConditionCheckedAt.Both;
            }

            // Coverage family F1 (v180, AttributeBullets): "This monster gets the following effect(s) while there is a
            // monster(s) with the following Attribute(s) on the field: ● FIRE: …" (Element monsters). Each bullet body is
            // compiled to the same continuous clause it would produce standalone, stamped with RequiresFieldAttribute =
            // the bullet's Attribute; the engine honors the clause only while such a monster is face-up on the field
            // (AttributeBulletsRuntime.FieldAttributePresent). A bullet the parser does not recognise fails the whole
            // card closed instead of igniting unconditionally.
            var conditionalBullets = RxConditionalAttributeBullets.Match(text);
            if (conditionalBullets.Success)
            {
                var attributeClauses = AttributeBulletsAtoms.TryParse(text);
                clauses.Clear();
                unparsed.Clear();
                if (attributeClauses != null)
                    clauses.AddRange(attributeClauses);
                else
                    unparsed.Add(text.Trim());
            }

            // Shared rule, not a card list: a battle-destroyed recovery the engine cannot stamp
            // (a "during this turn" battle destruction, or this card returning on your next Standby)
            // has no runtime. Same fail-closed shape as the conditional-bullet reject: drop every
            // clause and keep the whole text unparsed so it is not FullyCompiled.
            if (BattleDestroyedRecoveryAtoms.IsFailClosedPark(text))
            {
                clauses.Clear();
                unparsed.Clear();
                unparsed.Add(text.Trim());
            }

            prog.SetClauses(clauses);
            prog.SetUnparsed(unparsed);
            prog.FullyCompiled = unparsed.Count == 0 && clauses.Count > 0
                                 || (clauses.Count == 0 && OfficialCardAuthority.HasNoActivatableEffect(def))
                                 || (clauses.Count == 0 && structuralOnlyText && unparsed.Count == 0)
                                 || (clauses.Count == 0 && ritualProcedureOnly && unparsed.Count == 0);

            // Exact Type matching (GitHub lineage rule): a RaceFilter must name printed monster Types
            // ("Machine", "Dinosaur,Zombie,Rock"). A phrase like "other Machine" / "face-up Rock" /
            // "Union" would silently match nothing at runtime, so such a program fails closed.
            if (prog.FullyCompiled)
            {
                var badRace = FirstNonTypeRaceFilter(clauses);
                if (badRace != null)
                {
                    unparsed.Add($"(RaceFilter \"{badRace}\" is not a printed monster Type)");
                    prog.SetUnparsed(unparsed);
                    prog.FullyCompiled = false;
                }
            }

            // FaceUpQuickEffects batch (v160): a Quick Effect used from a face-up Spell & Trap Zone needs a card that
            // stays face-up there (a Continuous Spell/Trap); on any other card the program fails closed.
            if (prog.FullyCompiled && !def.IsContinuousSpellOrTrap &&
                clauses.Exists(c => c != null && c.Timing == EffectTiming.FaceUpQuickEffect))
            {
                unparsed.Add("(a face-up Quick Effect needs a Continuous Spell/Trap Card)");
                prog.SetUnparsed(unparsed);
                prog.FullyCompiled = false;
            }

            return prog;
        }

        static string Normalize(string s)
        {
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        static int ParseInt(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }

        static bool SpanCovered(List<(int start, int length)> spans, int start, int length)
        {
            if (spans == null || length <= 0) return false;
            var end = start + length;
            var covered = 0;
            foreach (var (s, n) in spans)
            {
                var a = Math.Max(start, s);
                var b = Math.Min(end, s + n);
                if (b > a) covered += b - a;
            }

            return covered * 2 >= length; // majority of the sentence already matched
        }

        /// <summary>
        /// Compile one PSCT sentence from its condition / activation / resolution parts
        /// (Konami green / red / blue), using fragment templates — not a whole-card regex.
        /// </summary>
        static EffectClause CompilePsctSentence(PsctGrammar.Sentence sent, CardDef def)
        {
            if (sent == null) return null;
            if (sent.IsParenthetical)
            {
                var name = RxAlwaysTreatedAsName.Match(sent.Raw);
                if (!name.Success) name = RxNameTreatedAs.Match(sent.Raw);
                if (name.Success)
                    return Stamp(sent, new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.AlwaysTreatedAsName,
                        TreatedAsName = name.Groups[1].Value,
                        StaysOnField = true,
                        MakesChainLink = false
                    });
                return null;
            }

            if (!sent.MakesChainLink)
            {
                var auras = CompileAuraClauses(sent.Raw);
                if (auras.Count == 1)
                    return Stamp(sent, auras[0]);
                var gain = RxAllAttrGainAtkDef.Match(sent.Raw);
                if (!gain.Success) gain = RxIncreasesAtkDefOfAll.Match(sent.Raw);
                if (gain.Success)
                    return Stamp(sent, GainAtkDefClause(gain));
                var lv = RxReduceLevelHandsAndField.Match(sent.Raw);
                if (!lv.Success) lv = RxDowngradeLevelHandsAndField.Match(sent.Raw);
                if (lv.Success)
                    return Stamp(sent, ReduceLevelClause(lv));
                var dirAllDef = RxAllOppMonstersDefenseCanAttackDirectly.Match(sent.Raw);
                if (dirAllDef.Success)
                    return Stamp(sent, SharedPsctAtomTemplates.MakeCanAttackDirectlyIfAllOppMonstersDefense());
                var dirNamed = RxDirectAttackWhileNamed.Match(sent.Raw);
                if (dirNamed.Success)
                    return Stamp(sent, DirectAttackClause(dirNamed, named: true));
                var dirAny = RxDirectAttackUnconditional.Match(sent.Raw);
                if (dirAny.Success)
                    return Stamp(sent, DirectAttackClause(dirAny, named: false));
                var extraNamed = RxExtraAttackWhileNamed.Match(sent.Raw);
                if (extraNamed.Success)
                    return Stamp(sent, ExtraAttackClause(extraNamed, named: true));
                var extraAny = RxExtraAttackUnconditional.Match(sent.Raw);
                if (extraAny.Success)
                    return Stamp(sent, ExtraAttackClause(extraAny, named: false));
                return null;
            }

            // Fail closed: a condition before the colon that this path cannot compile must not vanish
            // (D.D. Scout Plane's "if … banished this turn", Coffin Seller's "Each time …").
            if (!PsctConditionGate.IsRecognized(sent.Condition))
                return null;

            var ign = IgnitionTemplates.TryCompile(sent, def);
            if (ign != null)
                return Stamp(sent, ign);

            var act = sent.Activation ?? "";
            var res = sent.Resolution ?? "";
            var combo = (act + " " + res).Trim();

            // Blue text that opens with "When / If / Each time / Every time" still holds its trigger or condition. The
            // sentence had no colon ("Each time your opponent takes damage from a card effect, except "Dark Room of
            // Nightmare", inflict 300 damage to your opponent."), so PsctGrammar left it whole in Resolution and
            // SuggestTiming only guessed a window. Every Resolution fallback below keeps the action and drops the
            // trigger, and the card fired on activation or on the wrong event (Dark Room of Nightmare, Poison Fangs,
            // Spirit's Invitation, Card of Safe Return). Fail closed. Such a sentence needs a whole-sentence atom in
            // the whole-text pass (AtomRegistration, or a Take like Mysterious Puppeteer's), which runs before this.
            if (Regex.IsMatch(res, @"^\s*(?:When|If|Each time|Every time)\b", RegexOptions.IgnoreCase))
                return null;

            var clause = new EffectClause
            {
                Timing = sent.SuggestedTiming,
                MakesChainLink = true
            };

            // ── Costs (red text) ──
            var discAttr = Regex.Match(act,
                @"discard 1 (\w+) monster", RegexOptions.IgnoreCase);
            var discCard = Regex.Match(act,
                @"discard (\d+) cards?(?: from your hand(?: to the (?:GY|Graveyard))?)?", RegexOptions.IgnoreCase);
            var sendNamed = Regex.Match(act,
                @"send 1 face-up ""([^""]+)"" you control to the (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (discAttr.Success)
            {
                clause.RequiresDiscardCost = true;
                clause.DiscardCostAttribute = discAttr.Groups[1].Value;
            }
            else if (discCard.Success)
            {
                clause.RequiresDiscardCost = true;
                clause.DiscardCostAttribute = "*";
                clause.DiscardCostCount = int.TryParse(discCard.Groups[1].Value, out var dn) ? dn : 1;
                if (clause.DiscardCostCount < 1) clause.DiscardCostCount = 1;
            }
            else if (sendNamed.Success)
            {
                clause.RequiresSendNamedToGy = true;
                clause.RequiresFaceUpName = sendNamed.Groups[1].Value;
            }

            var paySrc = string.IsNullOrEmpty(act) ? (sent.Raw ?? res) : act;
            var pay = Regex.Match(paySrc,
                @"(?:Activate (?:this card )?by paying|Pay) (\d+) (?:LP|Life Points)",
                RegexOptions.IgnoreCase);
            if (pay.Success)
                clause.PayLpAmount = int.TryParse(pay.Groups[1].Value, out var lp) ? lp : 0;

            // ── Targeting (red text, or "to target" glued onto the cost) ──
            ParseActivationTarget(act, clause);

            // ── Resolution (blue text) ──
            // "destroy it, then send 2 cards from the top of your opponent's Deck …" (Assault on GHQ) and
            // "…, then both players draw 1 card" (Dragged Down into the Grave): the Destroy / Draw fallbacks below
            // would keep one step and drop the rest. Fail closed; a whole-sentence atom has to own such a sentence.
            if (Regex.IsMatch(combo, @"\bdestroy (?:that target|it), then\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(res, @"\b(?:both players|each player) draws? \d+ cards?\b", RegexOptions.IgnoreCase))
                return null;
            // Every simple fallback below must match the WHOLE resolution (IsWholeResolution), not a phrase inside
            // it: "Remove 2 Spell Counters … to negate the activation of a Spell Card and destroy it." or "Inflict 600
            // points of damage … for each face-up LIGHT monster …" is not a plain Destroy / flat burn. And an "it" /
            // "that target" whose target the red text did not name (no Zone parsed) returns null instead of guessing
            // one (Granmarg's Set card, White Ninja's Defense Position monster, Revival Jam's "it" = this card).
            if (IsWholeResolution(res,
                    @"return (?:it|that target) to (?:its owner's hand|the owner's hand|the hand)"))
            {
                clause.Action = EffectActionKind.ReturnToHand;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (IsWholeResolution(res, @"negate (?:the|that) attack"))
            {
                clause.Action = EffectActionKind.NegateThisAttack;
                clause.Zone = EffectZoneFilter.AttackingMonster;
            }
            else if (IsWholeResolution(res,
                         @"destroy all other cards on the field|destroy all cards on the field except this card"))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.Zone = EffectZoneFilter.AllOtherCardsOnField;
                clause.Side = EffectSide.Both;
                clause.RequiresTargetChoice = false;
            }
            else if (IsWholeResolution(res, @"destroy (?:that target|it)"))
            {
                // "destroy it if …" (a condition) or "…, then destroy it" (an earlier step) is not a plain
                // destroy: compiling it here drops the condition and the other step (Acid Trap Hole compiled
                // to "destroy any monster"). Fail closed; a whole-sentence atom owns such a sentence.
                if (RxConditionalOrSequencedDestroy.IsMatch(combo))
                    return null;
                clause.Action = EffectActionKind.Destroy;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            // Flat draws only. A draw that goes on ("Draw 2 cards, then discard 1 card", Spiral Spear Strike) must
            // not compile as the bare draw: the rest would be dropped (Graceful Charity drew 3 and never discarded).
            // Such a card stays unparsed and fails closed unless a whole-text atom covers it (DrawThenDiscardAtoms).
            else if (IsWholeResolution(res, @"draw (\d+) cards?(?: from your Deck)?") &&
                     !Regex.IsMatch(res, @"draw \d+ cards?(?: from your Deck)?,?\s+then\b",
                         RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(res, @"draw (\d+) cards?", RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.Draw;
                clause.Side = EffectSide.Controller;
                clause.Amount = int.TryParse(m.Groups[1].Value, out var n) ? n : 1;
                // A trigger or condition in the blue text ("When a monster is Special Summoned from your Graveyard,
                // you can draw 1 card." has no colon, so nothing split it off) would be dropped and the card would
                // draw on activation (Card of Safe Return). Fail closed; a whole-sentence atom has to own it.
                if (Regex.IsMatch(res, @"^(?:When|If|Each time|Every time)\b", RegexOptions.IgnoreCase))
                    return null;
            }
            // Flat burns only. "... for each <X>" (Princess of Tsurugi, Cemetary Bomb, D.D. Dynamite, Solar Ray)
            // must not fall through to a flat amount: without a scaler the card stays unparsed and fails closed.
            else if (IsWholeResolution(res,
                         @"inflict (\d+) (?:points of )?damage to your opponent(?:'s Life Points)?"))
            {
                var m = Regex.Match(res, @"inflict (\d+) (?:points of )?damage to your opponent",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.InflictDamageToOpponent;
                clause.Amount = int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
            }
            else if (IsWholeResolution(res,
                         @"(?:it|that target) gains (\d+) (?:ATK|DEF|ATK/?DEF|ATK and DEF) until the end of this turn"))
            {
                var mAtk = Regex.Match(res,
                    @"(?:it|that target) gains (\d+) ATK(?:/?DEF| and DEF)? until the end of this turn",
                    RegexOptions.IgnoreCase);
                var mDefOnly = Regex.Match(res,
                    @"(?:it|that target) gains (\d+) DEF until the end of this turn",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.GainAtkDefUntilEndOfTurn;
                if (mAtk.Success)
                {
                    clause.Amount = int.TryParse(mAtk.Groups[1].Value, out var n) ? n : 0;
                    if (mAtk.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0)
                        clause.DefAmount = clause.Amount;
                }
                else if (mDefOnly.Success)
                {
                    clause.Amount = 0;
                    clause.DefAmount = int.TryParse(mDefOnly.Groups[1].Value, out var n) ? n : 0;
                }
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            // Permanent (lingering) gain only: "… during that damage calculation only" is not this.
            else if (IsWholeResolution(res, @"this card gains (\d+) ATK and DEF"))
            {
                var m = Regex.Match(res, @"this card gains (\d+) ATK and DEF", RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.ApplyLingeringAtkDef;
                clause.Amount = int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
                clause.DefAmount = clause.Amount;
            }
            else if (IsWholeResolution(res, @"the monster that destroyed it loses (\d+) ATK and DEF"))
            {
                var m = Regex.Match(res,
                    @"the monster that destroyed it loses (\d+) ATK and DEF",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.ApplyLingeringAtkDef;
                clause.Amount = -(int.TryParse(m.Groups[1].Value, out var n) ? n : 0);
                clause.DefAmount = clause.Amount;
                clause.ImplicitTargetIsBattleDestroyer = true;
                clause.RequiresTargetChoice = false;
            }
            else if (IsWholeResolution(res, @"destroy the monster that destroyed this card"))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.ImplicitTargetIsBattleDestroyer = true;
                clause.RequiresTargetChoice = false;
            }
            else if (IsWholeResolution(res,
                         @"special summon (?:it|that target)(?: in (?:face-up )?(?:Attack|Defense) Position)?"))
            {
                clause.Action = clause.Zone == EffectZoneFilter.ControllerDeckMonsters
                    ? EffectActionKind.SpecialSummonFromDeck
                    : clause.Zone == EffectZoneFilter.ControllerHandMonsters
                        ? EffectActionKind.SpecialSummonFromHand
                        : EffectActionKind.SpecialSummonFromGy;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
                var pos = Regex.Match(res,
                    @"special summon (?:it|that target) in (?:face-up )?(Attack|Defense) Position",
                    RegexOptions.IgnoreCase);
                if (pos.Success)
                {
                    if (pos.Groups[1].Value.Equals("Defense", StringComparison.OrdinalIgnoreCase))
                        clause.SummonInDefense = true;
                    else
                        clause.SummonInAttack = true;
                }
            }
            else if (IsWholeResolution(res, @"banish (?:it|that target|them)|remove (?:it|that target) from play"))
            {
                clause.Action = EffectActionKind.Banish;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (IsWholeResolution(res,
                         @"return all monsters your opponent controls to (?:the |their owner's |its owner's )?hand"))
            {
                clause.Action = EffectActionKind.ReturnToHand;
                clause.Side = EffectSide.Opponent;
                clause.Zone = EffectZoneFilter.FieldMonsters;
                clause.RequiresTargetChoice = false;
            }
            else if (SharedPsctAtomTemplates.TryCompileSpecialSummon(res, clause))
            {
            }
            else if (IsWholeResolution(res,
                         @"special summon 1 ""([^""]+)"" or ""([^""]+)"" from your (hand|deck|graveyard|gy)" +
                         @"(?: or (hand|deck|graveyard|gy))?"))
            {
                var altSs = Regex.Match(res,
                    @"special summon 1 ""([^""]+)"" or ""([^""]+)"" from your (hand|deck|graveyard|gy)" +
                    @"(?: or (hand|deck|graveyard|gy))?",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.SpecialSummonNamed;
                clause.NamedCard = altSs.Groups[1].Value;
                clause.AltNamedCard = altSs.Groups[2].Value;
                void OriginAlt(string loc)
                {
                    if (string.IsNullOrEmpty(loc)) return;
                    if (string.Equals(loc, "hand", StringComparison.OrdinalIgnoreCase))
                        clause.FromHand = true;
                    else if (string.Equals(loc, "deck", StringComparison.OrdinalIgnoreCase))
                        clause.FromDeck = true;
                    else
                        clause.FromGrave = true;
                }
                OriginAlt(altSs.Groups[3].Value);
                if (altSs.Groups.Count > 4 && altSs.Groups[4].Success)
                    OriginAlt(altSs.Groups[4].Value);
            }
            else if (IsWholeResolution(res,
                         @"special summon 1 ""([^""]+)"" from your (hand|deck|graveyard|gy)" +
                         @"(?: or (hand|deck|graveyard|gy))?(?: or (hand|deck|graveyard|gy))?"))
            {
                var namedSs = Regex.Match(res,
                    @"special summon 1 ""([^""]+)"" from your (hand|deck|graveyard|gy)" +
                    @"(?: or (hand|deck|graveyard|gy))?(?: or (hand|deck|graveyard|gy))?",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.SpecialSummonNamed;
                clause.NamedCard = namedSs.Groups[1].Value;
                void Origin(string loc)
                {
                    if (string.IsNullOrEmpty(loc)) return;
                    if (string.Equals(loc, "hand", StringComparison.OrdinalIgnoreCase))
                        clause.FromHand = true;
                    else if (string.Equals(loc, "deck", StringComparison.OrdinalIgnoreCase))
                        clause.FromDeck = true;
                    else
                        clause.FromGrave = true;
                }
                Origin(namedSs.Groups[2].Value);
                if (namedSs.Groups.Count > 3 && namedSs.Groups[3].Success)
                    Origin(namedSs.Groups[3].Value);
                if (namedSs.Groups.Count > 4 && namedSs.Groups[4].Success)
                    Origin(namedSs.Groups[4].Value);
            }
            else if (IsWholeResolution(res, @"special summon 1 (\w+)(?:-Type)? monster from your hand"))
            {
                var raceSs = Regex.Match(res,
                    @"special summon 1 (\w+)(?:-Type)? monster from your hand",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.SpecialSummonFromHand;
                clause.RaceFilter = raceSs.Groups[1].Value;
                clause.FromHand = true;
                clause.Amount = 1;
            }
            else if (IsWholeResolution(res, @"add (?:that target|it) to your hand"))
            {
                clause.Action = EffectActionKind.AddFromGyToHand;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            // A Feather of the Phoenix / Phoenix Wing Wind Blast / Back to Square One.
            // Owner's Deck — the zone parsed from the red text decides whose card it is.
            else if (Regex.IsMatch(res,
                         @"^(?:return|place) (?:that target|it) (?:to|on) the top of (?:your |the )?Deck\.?$",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.PlaceTargetOnTopOfDeck;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            // Dark Magic Attack: opponent-side mass Spell/Trap destruction (no target).
            else if (Regex.IsMatch(res,
                         @"^Destroy all Spells? and Traps? your opponent controls\.?$",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.DestroyAllOppSpellTraps;
                clause.Side = EffectSide.Opponent;
            }
            else if (SharedPsctAtomTemplates.TryCompileOppCannotDeclareAttackThisTurn(res, clause))
            {
            }
            else if (SharedPsctAtomTemplates.TryCompileFeintPlan(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileFeintPlan(sent.Raw ?? "", clause))
            {
                // A Feint Plan — Activate CannotAttackFaceDownMonstersThisTurn (both players).
            }
            else if (SharedPsctAtomTemplates.TryCompileRemoveBrainwashing(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileRemoveBrainwashing(sent.Raw ?? "", clause))
            {
                // Remove Brainwashing — ContinuousWhileFaceUp ReturnControlToOwners (both sides).
            }
            else if (SharedPsctAtomTemplates.TryCompileMaskOfRestrict(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileMaskOfRestrict(sent.Raw ?? "", clause))
            {
                // Mask of Restrict — ContinuousWhileFaceUp NeitherPlayerCanTribute (both).
            }
            else if (SharedPsctAtomTemplates.TryCompileDarkDoor(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileDarkDoor(sent.Raw ?? "", clause))
            {
                // The Dark Door — ContinuousWhileFaceUp OnlyOneMonsterMayAttackPerBattlePhase (both).
            }
            else if (SharedPsctAtomTemplates.TryCompileInfiniteCards(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileInfiniteCards(sent.Raw ?? "", clause))
            {
                // Infinite Cards — ContinuousWhileFaceUp NoHandSizeLimit (both).
            }
            else if (SharedPsctAtomTemplates.TryCompileEnervatingMist(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileEnervatingMist(sent.Raw ?? "", clause))
            {
                // Enervating Mist — ContinuousWhileFaceUp OpponentHandSizeLimitBecomes (opp-only).
            }
            else if (SharedPsctAtomTemplates.TryCompileSpellEconomics(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileSpellEconomics(sent.Raw ?? "", clause))
            {
                // Spell Economics — ContinuousWhileFaceUp ControllerDoesNotPayLpToActivateSpells (controller).
            }
            else if (SharedPsctAtomTemplates.TryCompileEmperorsHoliday(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileEmperorsHoliday(sent.Raw ?? "", clause))
            {
                // The Emperor's Holiday — ContinuousWhileFaceUp NegateAllEquipCardEffectsOnField (both).
            }
            else if (SharedPsctAtomTemplates.TryCompileProtectorOfTheSanctuary(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileProtectorOfTheSanctuary(sent.Raw ?? "", clause))
            {
                // Protector of the Sanctuary — ContinuousWhileFaceUp OpponentCannotDrawExceptDuringDrawPhases (opp).
            }
            else if (SharedPsctAtomTemplates.TryCompileSwordsOfConcealingLight(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileSwordsOfConcealingLight(sent.Raw ?? "", clause))
            {
                // Swords of Concealing Light — Activate flip / ContinuousWhileFaceUp BP-lock.
                // Standby destroy sentence is Collect-absorbed (ContinuousTurnsRemaining Soft tick);
                // alone it fail-closes (no invent flip).
            }
            else if (SharedPsctAtomTemplates.TryCompileLightforceSword(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileLightforceSword(
                         (string.IsNullOrEmpty(act) ? "" : act + " ") + res, clause) ||
                     SharedPsctAtomTemplates.TryCompileLightforceSword(sent.Raw ?? "", clause))
            {
                // Lightforce Sword — Activate BanishRandomFromOppHandFaceDown Amount=4 Soft return.
                // Standby-return sentence is Collect-absorbed (Soft ContinuousTurnsRemaining tick);
                // alone it fail-closes (no invent banish).
            }
            else if (SharedPsctAtomTemplates.TryCompileFakeTrap(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileFakeTrap(
                         (string.IsNullOrEmpty(act) ? "" : act + " ") + res, clause) ||
                     SharedPsctAtomTemplates.TryCompileFakeTrap(sent.Raw ?? "", clause))
            {
                // Fake Trap — Activate DestroyThisInsteadOfControllerTraps Soft redirect.
                // Look-confirm parenthetical Collect-absorbed (Soft residual Soft-OK);
                // alone it fail-closes (no invent activate-only gate).
            }
            else if (SharedPsctAtomTemplates.TryCompileInflictPerOppHandAndControlled(res, clause))
            {
            }
            else if (SharedPsctAtomTemplates.TryCompileInflictPerOppMonster(res, clause))
            {
            }
            else if (SharedPsctAtomTemplates.TryCompileWidespreadRuin(res, clause))
            {
                // Widespread Ruin compound -- AttackDeclared highest-ATK opp ATK-pos.
            }
            else if (SharedPsctAtomTemplates.TryCompileDropOff(res, clause))
            {
                // Drop Off — Opp Draw Phase normal draw discard just-drawn.
            }
            else if (SharedPsctAtomTemplates.TryCompileNutrientZ(res, clause))
            {
                // Nutrient Z — DamageCalculation Gain LP first if 2000+ battle damage.
            }
            else if (SharedPsctAtomTemplates.TryCompileHeartOfTheUnderdog(res, clause))
            {
                // Heart of the Underdog — ControllerNormalDrawInDrawPhase reveal Normal + draw.
            }
            else if (SharedPsctAtomTemplates.TryCompileSpringOfRebirth(res, clause))
            {
                // Spring of Rebirth — ContinuousWhileFaceUp GainLpWhenMonsterReturnsToOwnerHand.
            }
            else if (SharedPsctAtomTemplates.TryCompileBannerOfCourage(res, clause))
            {
                // Banner of Courage — ContinuousWhileFaceUp ContinuousGainAtkDef BP-only controller ATK.
            }
            else if (SharedPsctAtomTemplates.TryCompileMysteriousPuppeteer(res, clause))
            {
                // Mysterious Puppeteer — ContinuousWhileFaceUp GainLpWhenEitherPlayerNormalOrFlipSummons.
            }
            else if (SharedPsctAtomTemplates.TryCompileBackupSoldier(res, clause))
            {
                // Backup Soldier — Activate AddFromGyToHand TargetUpTo non-Effect ATK≤ + GY≥N gate.
            }
            else if (SharedPsctAtomTemplates.TryCompileDarkFactory(res, clause))
            {
                // Dark Factory — Activate AddFromGyToHand exact TargetCount Normal Monsters your GY.
            }
            else if (SharedPsctAtomTemplates.TryCompileDisappear(res, clause) ||
                     SharedPsctAtomTemplates.TryCompileDisappear(
                         (string.IsNullOrEmpty(act) ? "" : act + " ") + res, clause) ||
                     SharedPsctAtomTemplates.TryCompileDisappear(sent.Raw ?? "", clause))
            {
                // Disappear — Activate Banish exact TargetCount=1 OppGyCards (Soft Remove-from-play).
            }
            else if (SharedPsctAtomTemplates.TryCompileCeasefire(res, clause))
            {
                // Ceasefire — Activate FlipAllFaceDownDefenseNoFlipEffects (+ burn rider via Collect).
            }
            else if (SharedPsctAtomTemplates.TryCompileShallowGrave(res, clause))
            {
                // The Shallow Grave — each-player own GY face-down Defense SS (SummonToTargetsOwner).
            }
            else if (SharedPsctAtomTemplates.TryCompileMorphingJar(res, clause))
            {
                // Morphing Jar — Flip discard-all then fixed Amount draw (not Card Destruction).
            }
            else if (SharedPsctAtomTemplates.TryCompileChangePosition(res, clause))
            {
            }
            else if (IsWholeResolution(res, @"change (?:that target's|its) battle position"))
            {
                clause.Action = EffectActionKind.ChangeBattlePosition;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (IsWholeResolution(res, @"change that target to face-down Defense Position"))
            {
                clause.Action = EffectActionKind.SetTargetFaceDownDefense;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (IsWholeResolution(res, @"(?:it|that target) gains (\d+) (ATK|DEF) until the end of this turn"))
            {
                var m = Regex.Match(res,
                    @"(?:it|that target) gains (\d+) (ATK|DEF) until the end of this turn",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.GainAtkDefUntilEndOfTurn;
                var n = int.TryParse(m.Groups[1].Value, out var gStat) ? gStat : 0;
                if (m.Groups[2].Value.Equals("DEF", StringComparison.OrdinalIgnoreCase))
                    clause.DefAmount = n;
                else
                    clause.Amount = n;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (IsWholeResolution(res, @"destroy all cards on the field"))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.Zone = EffectZoneFilter.AllOtherCardsOnField;
                clause.Side = EffectSide.Both;
                clause.RequiresTargetChoice = false;
            }
            else if (IsWholeResolution(res, @"destroy all face-up Continuous (Spell|Trap) Cards"))
            {
                var m = Regex.Match(res,
                    @"destroy all face-up Continuous (Spell|Trap) Cards",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.Destroy;
                clause.Zone = EffectZoneFilter.FieldSpellTraps;
                clause.Side = EffectSide.Both;
                clause.RequiresTargetChoice = false;
                if (m.Groups[1].Value.Equals("Spell", StringComparison.OrdinalIgnoreCase))
                    clause.FaceUpContinuousSpellsOnly = true;
                else
                    clause.FaceUpContinuousTrapsOnly = true;
            }
            else if (IsWholeResolution(res, @"change (?:it|that target) to (?:face-up )?Attack Position"))
            {
                clause.Action = EffectActionKind.ChangeBattlePosition;
                clause.ForceAttackPosition = true;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (IsWholeResolution(res, @"change (?:it|that target) to (?:face-up )?Defense Position"))
            {
                clause.Action = EffectActionKind.ChangeBattlePosition;
                clause.ForceDefensePosition = true;
                clause.RequiresAttackPosition = true;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }

            // No action compiled: the sentence is not a program, even when an LP amount was read out of it. That
            // amount is the cost of an effect this path did not understand ("Each player must pay 500 Life Points
            // per card to Normal Summon …", Chain Energy; "Either player can pay 800 Life Points to negate …",
            // Royal Oppression; Weapon Change; Inspection), and keeping only the payment shipped cards that charge
            // LP and do nothing. A Continuous Spell whose whole activation is the payment ("Activate this card by
            // paying 1000 LP.", Toon World) is compiled by ContinuousRestrictionTemplates before the PSCT pass.
            if (clause.Action == EffectActionKind.None)
                return null;
            if (HasUnparsedActivationCost(act, clause))
                return null;

            // Activation condition: If "Name" is (face-up) on the field → RequiresFaceUpName.
            var cond = sent.Condition ?? "";
            if (string.IsNullOrEmpty(clause.RequiresFaceUpName))
            {
                var namedOnField = Regex.Match(cond,
                    @"^(?:If|When) ""([^""]+)"" is(?: face-up)? on the field\.?$",
                    RegexOptions.IgnoreCase);
                if (namedOnField.Success)
                    clause.RequiresFaceUpName = namedOnField.Groups[1].Value;
            }

            // "If you control (a face-up) "Name":" — controller-side gate only
            // (Thousand Knives / Dark Magic Attack). Never unlocked by the opponent's copy.
            if (string.IsNullOrEmpty(clause.RequiresFaceUpName))
            {
                var controlNamed = Regex.Match(cond,
                    @"^If you control (?:a |an )?(?:face-up )?""([^""]+)""\.?$",
                    RegexOptions.IgnoreCase);
                if (controlNamed.Success)
                {
                    clause.RequiresFaceUpName = controlNamed.Groups[1].Value;
                    clause.RequiresControllerNamedCard = true;
                }
            }

            Stamp(sent, clause);
            if (IsUnrecognizedTriggerAsActivate(sent, clause))
                return null;
            return clause;
        }

        /// <summary>
        /// True when <paramref name="pattern"/> is the whole resolution (blue text), give or take a leading bullet
        /// or "You can" and the closing period. The generic fallbacks in <see cref="CompilePsctSentence"/> each read
        /// one verb phrase; an unanchored match kept that phrase and silently dropped the rest of the sentence.
        /// </summary>
        static bool IsWholeResolution(string res, string pattern) =>
            Regex.IsMatch(res ?? string.Empty,
                @"^\s*(?:●\s*)?(?:You can\s+)?(?:" + pattern + @")\s*\.?\s*$", RegexOptions.IgnoreCase);

        /// <summary>
        /// "If/When … :" that we did not map to a real trigger window must not become
        /// a free Main Phase ignition (Lord Poison is destroyed-by-battle, not OPT).
        /// "Each time …:" (Coffin Seller) is a trigger too, never a one-shot on activation.
        /// </summary>
        static bool IsUnrecognizedTriggerAsActivate(PsctGrammar.Sentence sent, EffectClause clause)
        {
            if (sent == null || clause == null) return false;
            if (clause.Timing != EffectTiming.Activate) return false;
            if (sent.OncePerTurn) return false;
            var cond = (sent.Condition ?? "").Trim();
            if (cond.Length == 0) return false;
            if (cond.IndexOf("this card is activated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cond.IndexOf("this card resolves", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            // Recognized activation gate (Elegant Egotist): If "Name" is on the field.
            if (!string.IsNullOrEmpty(clause.RequiresFaceUpName) &&
                Regex.IsMatch(cond,
                    @"^(?:If|When) ""[^""]+"" is(?: face-up)? on the field\.?$",
                    RegexOptions.IgnoreCase))
                return false;
            // Recognized activation gate (Thousand Knives): If you control "Name".
            if (clause.RequiresControllerNamedCard &&
                !string.IsNullOrEmpty(clause.RequiresFaceUpName) &&
                Regex.IsMatch(cond,
                    @"^If you control (?:a |an )?(?:face-up )?""[^""]+""\.?$",
                    RegexOptions.IgnoreCase))
                return false;
            return cond.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("If ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("During ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("At ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("Each time ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("Every time ", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A destroy with a condition ("destroy it if its DEF is 2000 or less") or after another step
        /// ("flip it face-up, then destroy it"): the generic PSCT destroy fallback must not compile it.
        /// </summary>
        static readonly Regex RxConditionalOrSequencedDestroy = new(
            @"\bdestroy (?:that target|it) if\b|\bthen destroy (?:that target|it)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// PSCT red-text targeting. GY monster/Spell/Trap must be parsed here —
        /// "add it to your hand" must not guess a zone (Magician of Faith is Spell;
        /// Monster Reincarnation is a monster).
        /// </summary>
        static void ParseActivationTarget(string act, EffectClause clause)
        {
            if (string.IsNullOrEmpty(act) || clause == null) return;
            // Opp Spell/Trap only (Swarm of Locusts twin to Scarabs; Dust/Chiron destroy-half).
            // Fail-closed: Side=Opponent — do not unlock field-wide / controller ST from this atom.
            if (SharedPsctAtomTemplates.TryApplyOppSpellTrapTarget(act, clause))
                return;

            if (Regex.IsMatch(act, @"target 1 (?:spell/?trap|spell or trap)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.FieldSpellTraps;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 card on the field", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act, @"to target 1 card on the field", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.AnyCardOnField;
                return;
            }

            if (Regex.IsMatch(act, @"target the attacking monster", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act, @"target 1 attacking monster", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = false;
                clause.Zone = EffectZoneFilter.AttackingMonster;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 (?:a )?monster in (?:your|the) Deck", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerDeckMonsters;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 monster in either (?:GY|Graveyard)",
                    RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act,
                    @"select 1 monster card from you or your opponent's graveyard",
                    RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.EitherGyMonsters;
                return;
            }

            var gyNamed = Regex.Match(act,
                @"target 1 ""([^""]+)"" monster in (?:your|the) (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (gyNamed.Success)
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                clause.NamedCard = gyNamed.Groups[1].Value;
                clause.NamedCardIsSeries = true;
                return;
            }

            var gyMon = Regex.Match(act,
                @"target 1 (?:(\w+)(?:-Type)? )?monster in (?:your|the) (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (gyMon.Success)
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                if (gyMon.Groups[1].Success && gyMon.Groups[1].Length > 0)
                    clause.RaceFilter = gyMon.Groups[1].Value;
                var exceptNamed = Regex.Match(act, @"except ""([^""]+)""", RegexOptions.IgnoreCase);
                if (exceptNamed.Success)
                    clause.ExceptNamedCard = exceptNamed.Groups[1].Value;
                return;
            }

            // Any card in opponent's GY (Disappear) — not your GY, not monsters-only, not either GY.
            if (Regex.IsMatch(act,
                    @"target 1 card in your opponent's (?:GY|Graveyard)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.OppGyCards;
                return;
            }

            // Any card in your own GY (A Feather of the Phoenix) — not either GY.
            if (Regex.IsMatch(act, @"target 1 card in your (?:GY|Graveyard)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyCards;
                return;
            }

            // Any card the opponent controls (Phoenix Wing Wind Blast).
            if (Regex.IsMatch(act, @"target 1 card your opponent controls", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.OppAnyCardOnField;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 Spell in your (?:GY|Graveyard)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGySpells;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 Trap in your (?:GY|Graveyard)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyTraps;
                return;
            }

            // "Target 1 monster your opponent controls" includes face-down monsters (Swarm of Scarabs, Thousand
            // Knives, Golem Sentry: Lua SelectTarget(aux.TRUE, 0, LOCATION_MZONE)); only a printed "face-up" limits
            // the target. The zone follows the matched phrase, not any "opponent" elsewhere in the red text.
            var fieldMon = Regex.Match(act,
                @"target 1 (?<faceup>face-up )?monster (?:on the field|(?<opp>your opponent controls))",
                RegexOptions.IgnoreCase);
            if (fieldMon.Success)
            {
                var faceUp = fieldMon.Groups["faceup"].Success;
                clause.RequiresTargetChoice = true;
                if (fieldMon.Groups["opp"].Success)
                {
                    clause.Zone = faceUp ? EffectZoneFilter.OppFaceUpMonsters : EffectZoneFilter.OppMonsters;
                }
                else
                {
                    clause.Zone = EffectZoneFilter.FieldAnyMonster;
                    if (faceUp) clause.RequiresTargetFaceUp = true;
                }
            }
        }

        /// <summary>
        /// Activation text names a cost this compiler did not parse — refuse the
        /// sentence instead of shipping a partial program (Spell Reproduction).
        /// </summary>
        static bool HasUnparsedActivationCost(string act, EffectClause clause)
        {
            if (string.IsNullOrEmpty(act) || clause == null) return false;
            if (Regex.IsMatch(act, @"discard \d+", RegexOptions.IgnoreCase) &&
                !clause.RequiresDiscardCost && !clause.RequiresDiscardSelf)
                return true;
            if (Regex.IsMatch(act, @"send \d+", RegexOptions.IgnoreCase) &&
                !clause.RequiresSendNamedToGy && !clause.RequiresSendThisToGy &&
                !clause.RequiresSendHandToGy && !clause.RequiresSendOtherYouControl)
                return true;
            if (Regex.IsMatch(act, @"pay \d+", RegexOptions.IgnoreCase) &&
                clause.PayLpAmount <= 0 && clause.RequiresLpCostMultiple <= 0)
                return true;
            if (Regex.IsMatch(act, @"tribute (?:this|\d+)", RegexOptions.IgnoreCase) &&
                !clause.RequiresTributeThis && clause.RequiresTributeCount <= 0)
                return true;
            if (Regex.IsMatch(act, @"banish \d+", RegexOptions.IgnoreCase) &&
                clause.BanishFromGyCount <= 0)
                return true;
            if (Regex.IsMatch(act, @"banish the top", RegexOptions.IgnoreCase))
                return true;
            // Magical Marionette / Blast Magician / Miracle Restoring: nothing on this path removes counters.
            // SpellCounterSpenders batch: the side-wide cost ("on/from your side of the field") is a cost field too.
            if (Regex.IsMatch(act, @"remove (?:\d+|any number of) Spell Counters?", RegexOptions.IgnoreCase) &&
                clause.RequiresRemoveSpellCounters <= 0 && clause.RemoveSpellCountersOnYourSide <= 0)
                return true;
            return false;
        }

        static EffectClause TryParseCostOnlySentence(PsctGrammar.Sentence sent)
        {
            if (sent == null) return null;
            var raw = (sent.Raw ?? "").Trim();
            if (raw.Length < 3) return null;
            var disc = Regex.Match(raw,
                @"^Discard (\d+) cards?(?: from your hand(?: to the (?:GY|Graveyard))?)?\s*\.?\s*$",
                RegexOptions.IgnoreCase);
            if (disc.Success)
            {
                var n = int.TryParse(disc.Groups[1].Value, out var dn) ? dn : 1;
                if (n < 1) n = 1;
                return new EffectClause
                {
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = n
                };
            }

            var pay = Regex.Match(raw,
                @"^(?:Activate (?:this card )?by paying|Pay) (\d+) (?:LP|Life Points)\s*\.?\s*$",
                RegexOptions.IgnoreCase);
            if (!pay.Success) return null;
            var lp = int.TryParse(pay.Groups[1].Value, out var pv) ? pv : 0;
            if (lp <= 0) return null;
            return new EffectClause { PayLpAmount = lp };
        }

        static void MergeActivationCost(EffectClause dest, EffectClause cost)
        {
            if (dest == null || cost == null) return;
            if (cost.RequiresDiscardCost)
            {
                dest.RequiresDiscardCost = true;
                if (string.IsNullOrEmpty(dest.DiscardCostAttribute))
                    dest.DiscardCostAttribute = cost.DiscardCostAttribute;
                if (dest.DiscardCostCount < 1)
                    dest.DiscardCostCount = cost.DiscardCostCount > 0 ? cost.DiscardCostCount : 1;
            }

            if (cost.PayLpAmount > 0 && dest.PayLpAmount <= 0)
                dest.PayLpAmount = cost.PayLpAmount;
        }

        static EffectClause Stamp(PsctGrammar.Sentence sent, EffectClause clause)
        {
            if (clause == null) return null;
            clause.OncePerTurn = clause.OncePerTurn || sent.OncePerTurn;
            if (clause.OncePerTurn && clause.OptScope == OncePerTurnScope.None)
                clause.OptScope = OncePerTurnScope.PerInstance;
            clause.CheckedAt = sent.CheckedAt;
            clause.IsQuickEffect = clause.IsQuickEffect || sent.IsQuickEffect;
            clause.IsOptional = clause.IsOptional || sent.IsOptional;
            clause.MakesChainLink = sent.MakesChainLink;
            if (sent.IsFlip)
                clause.Timing = EffectTiming.Flip;
            else if (clause.Timing == EffectTiming.None)
                clause.Timing = sent.SuggestedTiming;
            var hay = (sent.Condition ?? "") + " " + (sent.Raw ?? "");
            if (hay.IndexOf("flip summoned", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresThisFlipSummoned = true;
            if (hay.IndexOf("this card is tribute summoned", StringComparison.OrdinalIgnoreCase) >= 0 ||
                hay.IndexOf("this monster is tribute summoned", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresThisTributeSummoned = true;
            if (hay.IndexOf("normal summoned", StringComparison.OrdinalIgnoreCase) >= 0 &&
                hay.IndexOf("flip summoned", StringComparison.OrdinalIgnoreCase) < 0 &&
                hay.IndexOf("special summoned", StringComparison.OrdinalIgnoreCase) < 0)
                clause.RequiresThisNormalSummoned = true;
            if (hay.IndexOf("by a direct attack", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresDirectAttack = true;
            if (Regex.IsMatch(sent.Condition ?? "", @"\bduring your Main Phase 1\b", RegexOptions.IgnoreCase))
                clause.OnlyDuringMainPhase1 = true;
            if (hay.IndexOf("destroyed by battle", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresThisDestroyedByBattle = true;
            else if (Regex.IsMatch(hay,
                         @"destroyed and sent from the field to the (?:GY|Graveyard)|this card is destroyed and sent",
                         RegexOptions.IgnoreCase))
                clause.RequiresDestroyed = true;
            if (hay.IndexOf("during your opponent's turn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                hay.IndexOf("during the opponent's turn", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.OpponentTurnOnly = true;
            return clause;
        }

        static EffectClause DiscardBounceClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ReturnToHand,
                Zone = EffectZoneFilter.AnyCardOnField,
                RequiresTargetChoice = true,
                OncePerTurn = true,
                RequiresDiscardCost = true,
                DiscardCostAttribute = m.Groups[1].Value
            };
        }

        static EffectClause SendNamedDestroyOthersClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.AllOtherCardsOnField,
                Side = EffectSide.Both,
                RequiresTargetChoice = false,
                RequiresSendNamedToGy = true,
                RequiresFaceUpName = m.Groups[1].Value,
                IsOptional = true,
                MakesChainLink = true
            };
        }

        static EffectClause ExtraAttackClause(Match m, bool named)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ExtraAttacks,
                Amount = 1,
                RequiresFaceUpName = named && m.Groups.Count > 1 ? m.Groups[1].Value : null,
                MakesChainLink = false
            };
        }

        static EffectClause DirectAttackClause(Match m, bool named)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CanAttackDirectly,
                RequiresFaceUpName = named && m.Groups.Count > 1 ? m.Groups[1].Value : null,
                MakesChainLink = false
            };
        }

        static EffectClause GainAtkDefClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            var n = ParseInt(m, 2, 0);
            var c = new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Amount = n,
                DefAmount = n,
                Side = EffectSide.Both,
                StaysOnField = true,
                MakesChainLink = false
            };
            FillTypeOrAttribute(c, m.Groups[1].Value);
            return c;
        }

        static readonly HashSet<string> AttributeWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "DARK", "LIGHT", "EARTH", "WATER", "FIRE", "WIND", "DIVINE"
        };

        static string StripContinuousWhile(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = Normalize(s);
            s = Regex.Replace(s,
                @"^(?:As long as this card remains face-up on the field,\s*|While this card is face-up on the field,\s*)",
                "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        /// <summary>
        /// Continuous ATK/DEF auras that do not use a colon (not a Chain Link).
        /// Star Boy family + modern "All X gain ATK, also all Y lose ATK" + "you control".
        /// Requires the aura to be the whole remaining sentence so Defense-only / Flip prefixes do not match.
        /// </summary>
        static List<EffectClause> CompileAuraClauses(string raw)
        {
            var list = new List<EffectClause>();
            var body = StripContinuousWhile(raw);
            if (string.IsNullOrEmpty(body)) return list;

            var multi = RxMultiRaceGainLoseAtkDef.Match(body);
            if (multi.Success && multi.Index == 0)
            {
                var gainN = ParseInt(multi, 2, 200);
                var loseN = ParseInt(multi, 4, 200);
                foreach (var race in SplitRaceList(multi.Groups[1].Value))
                    list.Add(StatAuraClause(race, gainN, gainN, EffectSide.Both));
                foreach (var race in SplitRaceList(multi.Groups[3].Value))
                    list.Add(StatAuraClause(race, -loseN, -loseN, EffectSide.Both));
                return list;
            }

            var multiGain = RxMultiRaceGainAtkDef.Match(body);
            if (multiGain.Success && multiGain.Index == 0)
            {
                var n = ParseInt(multiGain, 2, 200);
                var both = multiGain.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                foreach (var race in SplitRaceList(multiGain.Groups[1].Value))
                    list.Add(StatAuraClause(race, n, both ? n : 0, EffectSide.Both));
                return list;
            }

            var quoted = RxQuotedSeriesGainAtkDef.Match(body);
            if (quoted.Success && quoted.Index == 0)
            {
                var n = ParseInt(quoted, 2, 0);
                var both = quoted.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                var c = StatAuraClause(null, n, both ? n : 0, EffectSide.Both);
                c.NamedCard = quoted.Groups[1].Value;
                c.NamedCardIsSeries = true;
                list.Add(c);
                return list;
            }

            // Before the plain race/ATK auras: this shape carries a DEF penalty the
            // ATK-only patterns would silently drop, leaving the sentence unparsed.
            var gainLoseDef = RxAllGainAtkLoseDef.Match(body);
            if (gainLoseDef.Success && gainLoseDef.Index == 0)
            {
                list.Add(StatAuraClause(gainLoseDef.Groups[1].Value,
                    ParseInt(gainLoseDef, 2, 0), -ParseInt(gainLoseDef, 3, 0), EffectSide.Both));
                return list;
            }

            var raceGain = RxRaceGainAtkDef.Match(body);
            if (raceGain.Success && raceGain.Index == 0)
            {
                var n = ParseInt(raceGain, 2, 0);
                var both = raceGain.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                list.Add(StatAuraClause(raceGain.Groups[1].Value, n, both ? n : 0, EffectSide.Both));
                return list;
            }

            var inc = RxIncDecAtk.Match(body);
            if (inc.Success && inc.Index == 0)
            {
                list.Add(StatAuraClause(inc.Groups[1].Value, ParseInt(inc, 2, 0), 0, EffectSide.Both));
                list.Add(StatAuraClause(inc.Groups[3].Value, -ParseInt(inc, 4, 0), 0, EffectSide.Both));
                return list;
            }

            var gain = RxAllGainAtkMaybeLose.Match(body);
            if (gain.Success && gain.Index == 0)
            {
                list.Add(StatAuraClause(gain.Groups[1].Value, ParseInt(gain, 2, 0), 0, EffectSide.Both));
                if (gain.Groups.Count > 3 && gain.Groups[3].Success &&
                    !string.IsNullOrEmpty(gain.Groups[3].Value))
                    list.Add(StatAuraClause(gain.Groups[3].Value, -ParseInt(gain, 4, 0), 0,
                        EffectSide.Both));
                return list;
            }

            var lose = RxAllLoseAtk.Match(body);
            if (lose.Success && lose.Index == 0)
            {
                list.Add(StatAuraClause(lose.Groups[1].Value, -ParseInt(lose, 2, 0), 0, EffectSide.Both));
                return list;
            }

            var you = RxAllYouControlGainAtk.Match(body);
            if (you.Success && you.Index == 0)
            {
                var n = ParseInt(you, 2, 0);
                var bothStats = you.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                list.Add(StatAuraClause(you.Groups[1].Value, n, bothStats ? n : 0, EffectSide.Controller));
                return list;
            }

            return list;
        }

        static EffectClause StatAuraClause(string filter, int atk, int def, EffectSide side)
        {
            var c = new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Amount = atk,
                DefAmount = def,
                Side = side,
                StaysOnField = true,
                MakesChainLink = false
            };
            FillTypeOrAttribute(c, filter);
            return c;
        }

        /// <summary>
        /// "Fish, Sea Serpent, Thunder, and Aqua" → individual race tokens.
        /// Keeps multi-word races (Sea Serpent) intact.
        /// </summary>
        static List<string> SplitRaceList(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            var s = raw.Replace(", and ", ", ").Replace(" and ", ", ");
            foreach (var part in s.Split(','))
            {
                var race = part.Trim();
                if (race.Length > 0) list.Add(race);
            }
            return list;
        }

        static void FillTypeOrAttribute(EffectClause c, string word)
        {
            if (c == null || string.IsNullOrEmpty(word)) return;
            if (AttributeWords.Contains(word))
                c.AttributeFilter = word;
            else
                c.RaceFilter = word;
        }

        static EffectClause ReduceLevelClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousReduceLevel,
                AttributeFilter = m.Groups[1].Value,
                Amount = ParseInt(m, 2, 1),
                ApplyToHand = true,
                ApplyToField = true,
                StaysOnField = true
            };
        }

        static string Group1(Match m) =>
            m != null && m.Success && m.Groups.Count > 1 ? m.Groups[1].Value : "";

        static bool ContainsAction(List<EffectClause> clauses, EffectActionKind action)
        {
            foreach (var c in clauses)
                if (c != null && c.Action == action) return true;
            return false;
        }

        static bool ContainsSnippet(List<EffectClause> clauses, string needle)
        {
            foreach (var c in clauses)
                if (c.SourceSnippet != null &&
                    c.SourceSnippet.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static void MarkAbsorbed(string text, Regex rx, List<(int, int)> spans)
        {
            var m = rx.Match(text);
            if (m.Success) spans.Add((m.Index, m.Length));
        }

        static string MaskMatched(string text, List<(int start, int length)> spans)
        {
            if (spans.Count == 0) return text;
            var chars = text.ToCharArray();
            foreach (var (start, length) in spans)
            {
                for (var i = start; i < start + length && i < chars.Length; i++)
                    chars[i] = ' ';
            }

            return new string(chars);
        }

        static IEnumerable<string> SplitSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            foreach (var part in Regex.Split(text, @"(?<=[\.\!\?])\s+"))
            {
                var t = part.Trim();
                if (t.Length > 2) yield return t;
            }
        }

        static bool IsBoilerplate(string frag)
        {
            // Collapse mask holes before length checks only — do not invent away real riders/costs.
            var f = Regex.Replace(frag ?? "", @"\s+", " ").Trim().ToLowerInvariant();
            if (f.Contains("you can only activate 1")) return true;
            if (f.Contains("must be face-up on the field to activate and to resolve this effect")) return true;
            if (f.Contains("must control this face-up card to activate and to resolve this effect")) return true;
            if (f.Contains("cannot normal summon/set the turn you special summon this card")) return true;
            if (f.StartsWith("once per turn") && f.Length < 24) return true;
            if (f.Contains("gains 500 atk")) return true;
            if (f.StartsWith("if this card is attacked, change it to attack position")) return true;
            if (f.Contains("you can only use")) return true;
            // Strip the name-condition parenthetical only. Do not eat a glued GY/trigger rider
            // (Axe of Despair: always-treated + "When this card is sent to the GY…").
            if (f.Contains("this card is always treated as"))
            {
                f = Regex.Replace(f,
                    @"\(this card(?:'s name)? is always treated as [^)]+\)\.?",
                    " ").Trim();
                f = Regex.Replace(f, @"\s+", " ").Trim();
                if (f.Length < 8) return true;
            }
            if (f.Contains("tribute 1 monster, then target")) return true; // EC mode 2 deferred
            if (f.Contains("once while this card is face-up")) return true;
            if (f.Contains("union monster at a time")) return true;
            // Soft residual Fake Trap look-confirm Soft-OK (Collect-absorbed when Fake Trap matched).
            if (f.Contains("look at them to confirm")) return true;
            // Union / Relinquished battle substitute boilerplate — Fake Trap Collect-absorbs first.
            if (f.Contains("destroy this card instead")) return true;
            if (f.Contains("when that monster is destroyed, destroy this card")) return true;
            if (f.Contains("when this card leaves the field, destroy that monster")) return true;
            // Rite of Spirit / similar: Necrovalley interaction reminder, not a separate effect.
            if (f.Contains("unaffected by \"necrovalley\"")) return true;
            if (Regex.IsMatch(f, @"^""[^""]+""\s*\+\s*""[^""]+""")) return true;
            if (f.Length < 8) return true;
            return false;
        }
    }
}
