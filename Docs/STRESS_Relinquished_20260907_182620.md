# Stress run — Relinquished

Stamp: 20260907_182620
Exit: 1
Log: `/home/greatsaiyadave/backup-bot/context/eval/stress/stress_Relinquished_20260907_182620.log`

## Card-focused lines
```
/home/greatsaiyadave/backup-bot/context/eval/stress/stress_Relinquished_20260907_182620.log
[Licensing::Module] Error: Access token is unavailable; failed to update
Starting: /home/greatsaiyadave/Unity/Hub/Editor/6000.5.10f1/Editor/Data/Tools/BuildPipeline/bee_backend --ipc --defer-dag-verification --dagfile="Library/Bee/2400b0aE.dag" --continue-on-failure --profile="Library/Bee/backend1.traceevents" ScriptAssemblies
PASS  ATK 1800 vs DEF 2000: defender lives
PASS  ATK 1800 vs DEF 2000: attacker takes 200
PASS  ATK 1800 vs DEF 2000: no damage to defender
PASS  ATK 1800 vs DEF 600: destroy defender
PASS  ATK 1800 vs DEF 600: 0 battle damage
PASS  ATK 1800 vs DEF 600: attacker lives
PASS  Face-down uses defense stat
PASS  Face-down DEF 1500 vs ATK 1200: lives
PASS  Face-down DEF 1500 vs ATK 1200: dmg 300 to attacker
PASS  Equal ATK: both destroyed
PASS  Equal ATK: 0 damage
PASS  ATK 1500 vs ATK 2000: destroy attacker
PASS  ATK 1500 vs ATK 2000: 500 to attacker controller
PASS  ATK 2500 vs ATK 2000: destroy defender
PASS  ATK 2500 vs ATK 2000: 500 to defender controller
PASS  ATK = DEF: nothing destroyed
PASS  ATK = DEF: 0 damage
PASS  Direct: 1600 damage
PASS  Piercing: destroy DEF
PASS  Piercing: 1200 damage
PASS  Waboku defender: no destroy
PASS  Waboku defender: no damage
PASS  Sanitize clears illegal DEF destroy
PASS  Lv4 needs 0 tributes
PASS  Lv5 needs 1 tribute
PASS  Lv6 needs 1 tribute
PASS  Lv7 needs 2 tributes
PASS  Lv8 needs 2 tributes
PASS  Starting LP 8000
PASS  Starting hand 5
PASS  Hand limit End Phase 6
PASS  Monster zones 5
PASS  Spell/Trap zones 5
PASS  Default response window 5s
PASS  Registry: Raigeki
PASS  Registry: Pot of Greed
PASS  Registry: Mirror Force
PASS  Registry: Swords
PASS  Registry: Trap Hole
PASS  Registry: Ring of Destruction
PASS  Registry: Waboku
PASS  Unregistered ID refused
PASS  Fusion: Gaia the Dragon Champion registered
PASS  Fusion: Black Skull Dragon registered
PASS  Ritual: Black Luster Soldier is a Ritual Monster (NS lock)
PASS  Ritual: Relinquished is a Ritual Monster (NS lock)
PASS  Ritual: BLS has summon procedure gate
PASS  Ritual: Relinquished has summon procedure gate
PASS  Ritual: Relinquished absorb is FullyCompiled (ProgramMayActivate)
PASS  Ritual: Black Luster Ritual compiles named Greater 8
PASS  Ritual: Black Illusion Ritual compiles named Greater 1
PASS  Ritual: Earth Chant compiles EARTH Equal
PASS  Ritual: Contract with the Abyss compiles DARK Equal
PASS  Ritual: Black Luster Ritual spec Greater named
PASS  Ritual: Earth Chant spec Equal EARTH
PASS  Ritual: named Equal 8 compiles (AddProcEqual)
PASS  Ritual math: Greater 8 auto-picks two Lv4 (sum 8)
PASS  Ritual math: Greater 8 fails when sum is 7
PASS  Ritual math: Greater 1 picks the Lv1
PASS  Ritual math: Equal 8 prefers one Lv8 over 4+4+3
PASS  Ritual math: Equal 8 fails when no exact set
PASS  Ritual math: Greater 8 accepts 5+4 overshoot (sum 9)
PASS  Ritual math: Equal 8 refuses 5+4 overshoot
PASS  PSCT Abyss Soldier: one chain sentence
PASS  PSCT Abyss Soldier: colon + semicolon
PASS  PSCT Abyss Soldier: makes a Chain Link
PASS  PSCT Abyss Soldier: Once per turn ignition
PASS  PSCT Abyss Soldier: condition is Once per turn
PASS  PSCT Abyss Soldier: resolution is return to hand
PASS  PSCT Sangan: colon, no semicolon
PASS  PSCT Sangan: field→GY timing
PASS  PSCT Raigeki: Spell with no colon still chains (card activation)
PASS  PSCT inherent SS: monster with no colon does NOT start a chain
PASS  PSCT inherent SS: Cyber Dragon text does not block Normal Summon
PASS  PSCT ALO: parenthetical name is not a Chain Link
PASS  PSCT ALO: Field Spell ATK/Level sentences are continuous, not extra chains
PASS  PSCT MK-3: continuous, no Chain Link
PASS  PSCT MK-3: compiles CanAttackDirectly while Umi
PASS  PSCT Star Boy: two continuous ATK auras, no Chain Link
PASS  PSCT Star Boy: WATER +500 ATK-only
PASS  PSCT Star Boy: FIRE −400 ATK-only
PASS  YGOPro bulk catalog loaded (Lua auras)
PASS  YGOPro catalog: Star Boy WATER +500 and FIRE −400
PASS  YGOPro catalog: MK-3 direct while Umi
PASS  YGOPro catalog loaded extra-attack grants
PASS  YGOPro catalog: Mermaid Knight extra attack while Umi
PASS  YGOPro catalog: Twinheaded Beast unconditional extra attack
PASS  PSCT Mermaid Knight: ExtraAttacks while Umi, no Chain Link
PASS  PSCT Twinheaded Beast: ExtraAttacks unconditional
PASS  PSCT Gray Wing: ignition this-turn is NOT continuous ExtraAttacks
PASS  PSCT Bark of Dark Ruler: Damage Step pay-LP ATK/DEF loss
PASS  PSCT Sakuretsu Armor: AttackDeclared destroy attacker
PASS  PSCT Magic Cylinder: negate attack + damage equal to ATK
PASS  PSCT Draining Shield: negate attack + gain LP equal to ATK
PASS  YGOPro trigger catalog has Bark + Sakuretsu + Cylinder
PASS  Master spec: Normal Monster is structural
PASS  Vocabulary: Exiled Force is Tribute → Destroy (shared kind, not a unique card)
PASS  Vocabulary: Time Wizard is UniqueException (call-wrong is not a shared kind)
PASS  Vocabulary: Thunder Dragon is Discard → Search
PASS  Vocabulary: shared kinds do not include UniqueException
PASS  Master spec: Bark of Dark Ruler is implemented
PASS  Master spec: Sakuretsu Armor is implemented
PASS  PSCT Sakuretsu 'When' is checked at activation
PASS  Master spec: uncompiled Effect Monster is stub or unimplemented (never silent vanilla)
PASS  Deck gate: Normal Monster may enter when exclusion on
PASS  Deck gate: Ha Des (uncompiled effect) may NOT enter when exclusion on
PASS  Classify: Ha Des is not Implemented
PASS  Deck gate: Pot of Greed (compiled/registry) may enter
PASS  Runtime AI default is off
PASS  ProgramMayActivate: Ha Des false
PASS  ProgramMayActivate: Celtic Guardian false (structural)
PASS  PSCT Granadora: summon +1000 LP and destroyed-to-GY 2000 damage
PASS  PSCT Maiden of the Aqua: field treated as Umi
PASS  PSCT Tornado Wall: Umi activate + no battle damage + self-destroy
PASS  PSCT Necrovalley: GK aura + GY cannot banish/target
PASS  Older Necrovalley leftover GY-move/type negate compiles as GY cannot-target
PASS  PSCT Harpies' Hunting Ground: Winged Beast aura + named NS/SS destroy S/T
PASS  PSCT ignition: Exiled Force tributes itself and destroys a target
PASS  PSCT ignition: Cannon Soldier tributes for 500 damage
PASS  PSCT after-dmg: Wall of Illusion returns attacker to hand
PASS  PSCT after-dmg: D.D. Warrior Lady banishes both (shared with Assailant)
PASS  PSCT summon-restrict-only: Harpie Lady Sisters FullyCompiled structural
PASS  PSCT ignition: Chaos Sorcerer banishes a face-up monster
PASS  PSCT ignition: Des Lacooda sets itself face-down
PASS  PSCT ignition: Thunder Dragon discards itself from hand and searches
PASS  PSCT Time Wizard: call coin, destroy opp or self
PASS  PSCT Breaker: NS counter + ATK per counter + remove to destroy ST
PASS  PSCT Cyber-Stein: pay 5000, SS Fusion from Extra
PASS  PSCT Lekunga: banish 2 WATER, SS token
PASS  PSCT Possessed Dark Soul: tribute this, take control LV≤3
PASS  PSCT Lava Golem: Standby 1000 damage
PASS  PSCT Lava Golem: printed text blocks Normal Summon/Set
PASS  PSCT Daedalus: send face-up named Umi to GY, destroy all other cards
PASS  PSCT Little Chimera: FIRE +500 and WATER −400
PASS  PSCT Command Knight: Warrior you-control +400 ATK compiles
PASS  PSCT fragment: Once per turn target+destroy compiles
PASS  Normal Trap Speed 2
PASS  Counter Trap Speed 3
PASS  Quick-Play Speed 2
PASS  Normal Spell Speed 1
PASS  Field Spell Speed 1
PASS  Field Spell is not a Trap
PASS  Field Spell detection uses race=Field not type
PASS  Normal Monster has no effect
PASS  Normal Monster HasNoActivatableEffect
PASS  Black Skull Dragon is effectless Extra
PASS  Black Skull Dragon is not Normal Monster type
PASS  Gaia the Dragon Champion is effectless Extra
PASS  Effect Monster not structural-only flavor
PASS  Replay: empty after empty direct = no new monsters
PASS  Replay: monster appeared after direct declaration
PASS  Man-Eater Bug is Flip-effect monster
PASS  Magician of Faith is Flip-effect monster
PASS  Lord of D. is Dragon-target protector ID
PASS  Kuriboh-style noDmgDef: 0 damage on direct
PASS  Without Kuriboh: direct deals ATK
PASS  Kuriboh legal in DamageCalculation vs opponent attack
PASS  Kuriboh illegal at AttackDeclared
PASS  Kuriboh illegal if you are the attacker
PASS  Kuriboh is registered monster effect
PASS  Kuriboh discard removes from hand
PASS  Kuriboh discard places in GY
PASS  Corpus: cards_db loaded
PASS  Corpus: entire cards_db is classified (not just lab decks)
PASS  Corpus: why-not-FullyCompiled diagnostics do not throw
PASS  Corpus: shared-kind texts in cards_db compile (new cards with these shapes work)
PASS  Corpus: Legendary Fisherman compiles Protection (Umi lock)
PASS  New-card rule: Fisherman-shaped text compiles without a cardId branch
PASS  Corpus: Hayabusa Knight compiles ExtraAttacks (second attack wording)
PASS  Corpus: Book of Moon compiles SetTargetFaceDownDefense
PASS  Corpus: Dragon Treasure compiles Equip +300/+300
PASS  Corpus: Salamandra FullyCompiled Equip only FIRE +700
PASS  Corpus: Legendary Sword FullyCompiled Equip only Warrior +300/+300
PASS  Corpus: Axe of Despair FullyCompiled Equip +1000 and GY to top of Deck
PASS  Corpus: United We Stand FullyCompiled +800 ATK/DEF per face-up monster
PASS  Corpus: Mage Power FullyCompiled +500 ATK/DEF per Spell/Trap
PASS  New-card rule: per-monster Equip ATK/DEF compiles without a cardId branch
PASS  New-card rule: Equip-only FIRE +ATK compiles without a cardId branch
PASS  Corpus: Falling Down FullyCompiled opponent-equip take-control
PASS  New-card rule: Falling Down-shaped text compiles without a cardId branch
PASS  Corpus: Monster Reincarnation FullyCompiled discard + GY monster add
PASS  Corpus: Ectoplasmer FullyCompiled End Phase turn-player tribute + half original ATK
PASS  New-card rule: End Phase turn-player tribute + half original ATK compiles without a cardId branch
PASS  Corpus: Level Limit - Area B leftover unique is not FullyCompiled
PASS  Corpus: Jam Breeding Machine leftover unique is not FullyCompiled
PASS  Corpus: Toon World FullyCompiled pay-1000 Activate
PASS  New-card rule: pay-LP Continuous Activate compiles without a cardId branch
PASS  Corpus: Labyrinth of Nightmare FullyCompiled End Phase turn-player positions
PASS  Corpus: Burning Land FullyCompiled destroy Field Spells + turn-player Standby damage
PASS  Corpus: Gravity Bind FullyCompiled Level 4+ cannot attack
PASS  Corpus: Insect Barrier FullyCompiled opponent Insect cannot attack
PASS  Corpus: Messenger of Peace FullyCompiled ATK≥1500 lock + pay 100 or destroy
PASS  Corpus: Call of the Haunted FullyCompiled GY SS + leave-field destroy
PASS  Corpus: Soul Resurrection FullyCompiled Normal Monster GY SS in Defense
PASS  Corpus: Skill Drain leftover unique is not FullyCompiled
PASS  Corpus: The Warrior Returning Alive FullyCompiled Warrior GY add
PASS  Corpus: Mask of Darkness FullyCompiled Flip Trap GY add
```

## wrldz_stress_report.txt (tail)
```
PASS  Necrovalley: Field Spell activate legal
PASS  Necrovalley: activates to Field Zone
PASS  Necrovalley: Gravekeeper's Spy +500/+500
PASS  Necrovalley: GY banish blocked
PASS  Necrovalley: Monster Reborn cannot target GY
PASS  GY summon restrictions: Monster Reborn refuses Ha Des and hard Nomi
PASS  GY summon restrictions: direct generic SpecialSummonToField refuses hard Nomi
FAIL  Rite of Spirit still legal under Necrovalley
PASS  HHG: Field Spell activate legal
PASS  HHG: activates to Field Zone
PASS  HHG: Harpie Lady Winged Beast +200/+200
PASS  HHG: NS Harpie Lady
PASS  HHG: summon trigger opens S/T target
PASS  HHG: destroy targeted S/T
PASS  Tornado setup: Umi activates
PASS  Tornado Wall activates while Umi is up
PASS  Tornado Wall destroyed when Umi leaves
PASS  Toon Table: Activate legal with Toon card in Deck
PASS  Toon Table: add Toon World (series, not exact name Toon)
PASS  Airknight Parshath compiles PiercingBattleDamage
PASS  Dark Driceratops FullyCompiled piercing
PASS  Yata-Garasu summon-restriction absorbed
PASS  Final Destiny compiles discard-5 + destroy all
PASS  New card without cardId: until-EOT ATK fragment
PASS  Airknight HasPiercing from compiled piercing
FAIL  Fissure: Activate opens lowest-ATK targets only
FAIL  Fissure: destroy Celtic (lowest ATK)
FAIL  Fissure vs Reaper: non-targeted resolution does not notify Reaper
PASS  Rush Recklessly: Activate targets your monster
FAIL  Rush Recklessly: +700 ATK until EOT — atk 1400 expected 2100
FAIL  Stop Defense: Activate legal vs DEF monster
PASS  Umi: activate into Field Zone
PASS  Umi: Aqua +200 ATK/DEF
PASS  Umi: Machine −200 ATK/DEF
PASS  Elegant Egotist: Activate legal with Harpie Lady on field
PASS  Elegant Egotist: resolve SS from Deck
PASS  Call of the Haunted: Activate from set
PASS  Call of the Haunted: opens GY target
FAIL  Call of the Haunted: SS Celtic, trap stays linked
FAIL  Call of the Haunted: destroy Call also destroys SS'd monster — gyMon=True callGy=False
PASS  MST: vs opponent S/T does not pick itself
PASS  MST: own other backrow stays legal (self hidden)
PASS  MST: sole S/T still offers self (legal)
PASS  MST: pref on, self is legal
PASS  DefaultResponseSeconds is 5s
PASS  Simochi: compiles shared continuous LP replacement
PASS  Simochi: activates and stays face-up
PASS  Simochi: Rain of Mercy compiles both-player LP gain
PASS  Simochi: Rain of Mercy controller gains, opponent takes damage
PASS  Simochi: controller own LP gain is unchanged
PASS  Simochi: reach opponent Main Phase
PASS  Simochi: opponent +600 LP becomes 600 effect damage
PASS  FirstEmpty order is 2,1,3
--- 844 passed, 61 failed ---

── Unit: CorpusTriggerStressTests ──
PASS  Corpus: cards_db classified
PASS  Corpus: unimplemented/stub cannot ProgramMayActivate
PASS  Corpus: unimplemented/stub refuse Activate (fail loud)
PASS  Corpus: FullyCompiled Field Spells Activate
FAIL  Corpus: FullyCompiled Spells Activate on a legal board — Book of Life, White Dragon Ritual, Doriado's Blessing, Revival of Dokurorider, Incandescent Ordeal, Book of Taiyou, The Puppet Magic of Dark Ruler, Black Illusion Ritual, Commencement Dance, Novox's Prayer (+16)
PASS  Corpus: FullyCompiled Traps are legal in their window
PASS  Corpus: FullyCompiled Flip monsters Flip Summon
PASS  Corpus: FullyCompiled ignition monsters Activate from field
PASS  Corpus: FullyCompiled Standby/aura monsters do not throw
PASS  Corpus: implemented pool is non-empty
corpus n=1717 structural=407 implemented=354 stub=158 unimplemented=798
--- 9 passed, 1 failed ---

── Text: lab deck compile (regex, no AI) ──
PASS  lab unique=36 full=34 partial=2 emptyEffect=0

── Stress: lab AI vs AI ──
duels played=2 completed=2 turnCap=0 softLocks=0 exceptions=0
wins: player=1 opp=1 draw/cap=0

── Fuzz: battle math ──
PASS  200 battle fuzz cases

── RESULT: unitPass=1171 unitFail=64 exceptions=0 softLocks=0 ok=NO elapsedMs=5919 ──
```
