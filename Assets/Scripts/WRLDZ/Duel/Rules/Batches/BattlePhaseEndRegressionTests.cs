using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// BattlePhaseEnd batch (v142 curriculum): end-of-Battle-Phase and battle-aftermath effects — Goblin Attack Force
    /// and Giant Orc (Defense after attacking + position lock until the End Phase of your next turn), Steel Scorpion
    /// (a non-Machine attacker is destroyed at the End Phase of the opponent's 2nd turn after the attack) and Reflect
    /// Bounder (burn the attacker's ATK before damage calculation, then destroy itself). Compile shapes, mini-duels
    /// through the real engine entry points (TryAttack, the Battle Phase exits, EndTurn) and fail-closed near misses.
    /// Vampire Baby was deferred after review; its checks assert it stays uncompiled. Sword Hunter, deferred here too,
    /// landed with the BothSideAndMonsterEquip batch (v161); its checks were rewritten to its compiled shape.
    /// Runtime: <see cref="BattlePhaseEndRuntime"/>.
    /// </summary>
    public static class BattlePhaseEndRegressionTests
    {
        const int GoblinAttackForce = 78658564;
        const int GiantOrc = 73698349;
        const int SwordHunter = 51345461;
        const int VampireBaby = 56387350;
        const int SteelScorpion = 13599884;
        const int ReflectBounder = 2851070;

        const int Celtic = 91152256;          // Warrior EARTH 1400/1200
        const int Bokoichi = 8715625;         // Machine Normal 500/500
        const int OjamaGreen = 12482652;      // Beast Normal 0/1000
        const int LusterDragon = 11091375;    // Dragon Normal 1900/1600
        const int Mechanicalchaser = 7359741; // Machine Normal 1850/800
        const int Penumbral = 64751286;       // Penumbral Soldier Lady: +1000 ATK vs LIGHT during the Damage Step
        const int NegateAttack = 14315573;
        const int TorrentialTribute = 53582587;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            GoblinDuels(db, pDeck, aDeck, Check);
            DeferredDuels(db, pDeck, aDeck, Check);
            SteelScorpionDuels(db, pDeck, aDeck, Check);
            ReflectBounderDuels(db, pDeck, aDeck, Check);
        }

        delegate void Chk(string name, bool ok, string detail = "");

        static string Left(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        // ── Compile shapes ──

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, name) in new[] { (GoblinAttackForce, "Goblin Attack Force"), (GiantOrc, "Giant Orc") })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                Check($"BattlePhaseEnd: {name} FullyCompiled end-of-Battle-Phase defense + position lock",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.EndOfBattlePhase &&
                                                c.Action == EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition &&
                                                !c.MakesChainLink),
                    Left(prog));
            }

            // Sword Hunter was deferred here; the BothSideAndMonsterEquip batch (v161) added the "monster Card treated
            // as an Equip Spell Card with a granted effect" state it needed (its own text no longer applies from the
            // Spell & Trap Zone, Spell negation reaches it). Guard rewritten to its compiled shape.
            {
                var hunterProg = CardTextEffectCompiler.Compile(db.Get(SwordHunter));
                Check("BattlePhaseEnd: Sword Hunter (v161) compiles to its end-of-Battle-Phase equip clause",
                    hunterProg != null && hunterProg.FullyCompiled && hunterProg.ClauseList.Count == 1 &&
                    hunterProg.ClauseList[0].Timing == EffectTiming.EndOfBattlePhase &&
                    hunterProg.ClauseList[0].Action == EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells,
                    Left(hunterProg));
            }

            // Deferred here after review; rewritten for the SummonPromptsAndCosts batch (v163), which adds the optional
            // end-of-Battle-Phase prompt where its controller picks which monsters and Attack or Defense Position.
            foreach (var (id, name) in new[] { (VampireBaby, "Vampire Baby") })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                Check($"BattlePhaseEnd: {name} (v163) compiles to its optional end-of-Battle-Phase Special Summon clause",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.EndOfBattlePhase &&
                    prog.ClauseList[0].Action == EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn &&
                    prog.ClauseList[0].IsOptional &&
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented,
                    Left(prog) + $" status={CardEffectStatus.Classify(db.Get(id))}");
            }

            var scorpion = CardTextEffectCompiler.Compile(db.Get(SteelScorpion));
            Check("BattlePhaseEnd: Steel Scorpion FullyCompiled after-damage-calculation delayed destroy (non-Machine, 2nd turn)",
                scorpion != null && scorpion.FullyCompiled && scorpion.ClauseList.Count == 1 &&
                scorpion.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.AfterDamageCalculation &&
                                                c.Action == EffectActionKind.DestroyAttackerAtOpponentsLaterEndPhase &&
                                                c.Zone == EffectZoneFilter.AttackingMonster &&
                                                c.ExcludeRaceFilter == "Machine" && c.Amount == 2 &&
                                                c.RequiresThisIsAttackTarget),
                Left(scorpion));

            var bounder = CardTextEffectCompiler.Compile(db.Get(ReflectBounder));
            Check("BattlePhaseEnd: Reflect Bounder FullyCompiled before-damage-calculation burn + destroy after",
                bounder != null && bounder.FullyCompiled && bounder.ClauseList.Count == 1 &&
                bounder.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.BeforeDamageCalculation &&
                                               c.Action == EffectActionKind.InflictDamageEqualToAtk &&
                                               c.Zone == EffectZoneFilter.AttackingMonster &&
                                               c.RequiresThisIsAttackTarget && c.RequiresThisAttackPosition &&
                                               c.DestroyThisAfterDamageCalculation),
                Left(bounder));

            Check("BattlePhaseEnd: all four are Implemented",
                new[] { GoblinAttackForce, GiantOrc, SteelScorpion, ReflectBounder }.All(id =>
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented));
        }

        // ── Near misses: look-alike sentences must not compile to these shapes ──

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "BattlePhaseEnd near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };

        static bool CompilesTo(CardDef def, Func<EffectClause, bool> shape)
        {
            var prog = CardTextEffectCompiler.Compile(def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && shape(c));
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            bool Lock(EffectClause c) => c.Action == EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition;
            bool Delayed(EffectClause c) => c.Action == EffectActionKind.DestroyAttackerAtOpponentsLaterEndPhase;
            bool Reflect(EffectClause c) => c.Timing == EffectTiming.BeforeDamageCalculation;

            var goblin = db.Get(GoblinAttackForce);
            var scorpion = db.Get(SteelScorpion);
            var bounder = db.Get(ReflectBounder);
            var cases = new (CardDef like, string text, Func<EffectClause, bool> shape)[]
            {
                // Spear Dragon's wording: the end of the Damage Step, no lock.
                (goblin, "If this card attacks, it is changed to Defense Position at the end of the Damage Step.", Lock),
                (goblin, "If this card attacks, it is changed to Defense Position at the end of the Battle Phase, and " +
                         "its battle position cannot be changed until the End Phase of your opponent's next turn.", Lock),
                (goblin, "If this card attacks, it is changed to Defense Position at the end of the Battle Phase, and " +
                         "its battle position cannot be changed until the End Phase of your next turn, or until this " +
                         "card leaves the field.", Lock),
                // Electric Lizard's wording is a different effect.
                (scorpion, "A non Machine-Type monster attacking \"Steel Scorpion\" cannot attack on its following turn.",
                    Delayed),
                (scorpion, "A non Machine-Type Monster attacking \"Steel Scorpion\" will be destroyed at the End Phase " +
                           "of your opponent's 2nd turn after the attack, and you take 500 damage.", Delayed),
                (bounder, "Before damage calculation, if this card is being attacked by an opponent's monster: Inflict " +
                          "damage to your opponent equal to the attacking monster's ATK. If you do, after damage " +
                          "calculation: Destroy this card.", Reflect),
                (bounder, "Before damage calculation, if this card is being attacked by an opponent's monster, and was " +
                          "in face-up Attack Position at the start of the Damage Step: Inflict damage to your opponent " +
                          "equal to the attacking monster's ATK.", Reflect),
                (bounder, "Before damage calculation, if this card is being attacked by an opponent's monster, and was " +
                          "in face-up Attack Position at the start of the Damage Step: Inflict damage to your opponent " +
                          "equal to the attacking monster's ATK. If you do, after damage calculation: Destroy this card " +
                          "and the attacking monster.", Reflect),
            };
            foreach (var (like, text, shape) in cases)
                Check($"BattlePhaseEnd near miss stays uncompiled: {text}",
                    !CompilesTo(Synthetic(like, text), shape),
                    Left(CardTextEffectCompiler.Compile(Synthetic(like, text))));

            var spear = CardTextEffectCompiler.Compile(db.Get(31553716)); // Spear Dragon
            Check("BattlePhaseEnd near miss: Spear Dragon (end of the Damage Step) gets no end-of-Battle-Phase clause",
                spear == null || !spear.HasTiming(EffectTiming.EndOfBattlePhase), Left(spear));
            var lizard = CardTextEffectCompiler.Compile(db.Get(55875323)); // Electric Lizard
            Check("BattlePhaseEnd near miss: Electric Lizard keeps its attack seal, no delayed destroy",
                lizard != null && lizard.FullyCompiled && !lizard.ClauseList.Exists(c => c != null && Delayed(c)) &&
                lizard.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SealNonRaceAttackerNextTurn),
                Left(lizard));
        }

        // ── Helpers ──

        static bool ReachPlayerBattle(DuelEngine engine)
        {
            for (var t = 0; t < 8 && !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.TurnPlayer != engine.Player) return false;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1)
                engine.TryEnterBattlePhase(engine.Player);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == engine.Player;
        }

        /// <summary>A fresh duel in the opponent's Battle Phase with an empty board and no Set cards.</summary>
        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            reached = ReachOpponentBattle(engine);
            ClearBoard(engine);
            return engine;
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void ToMainPhase2(DuelEngine engine, DuelistState who)
        {
            engine.TryEnterMainPhase2(who);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        /// <summary>End the current turn and play on until it is <paramref name="who"/>'s Main Phase 1 of turn <paramref name="turn"/>.</summary>
        static bool AdvanceToMain(DuelEngine engine, DuelistState who, int turn)
        {
            for (var i = 0; i < 12 && !(engine.TurnPlayer == who && engine.TurnNumber == turn &&
                                         engine.Phase == DuelPhase.Main1); i++)
            {
                for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            for (var k = 0; k < 4 && engine.IsAwaitingResponse; k++) engine.PassResponse();
            return engine.TurnPlayer == who && engine.TurnNumber == turn && engine.Phase == DuelPhase.Main1;
        }

        // ── Goblin Attack Force / Giant Orc ──

        static void GoblinDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, name) in new[] { (GoblinAttackForce, "Goblin Attack Force"), (GiantOrc, "Giant Orc") })
            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var goblin = PlaceMonster(engine, o, id, 0, BattlePosition.Attack, true);
                var prey = PlaceMonster(engine, p, OjamaGreen, 0, BattlePosition.Attack, true);
                var turn = engine.TurnNumber;
                var attacked = reached && Attack(engine, o, goblin, prey);
                var inBattle = goblin.Position == BattlePosition.Attack;
                ToMainPhase2(engine, o);
                Check($"{name}: after attacking it is changed to Defense Position at the end of the Battle Phase",
                    attacked && inBattle && p.Graveyard.Contains(prey) && goblin.FaceUp &&
                    goblin.Position == BattlePosition.Defense && engine.Phase == DuelPhase.Main2,
                    $"reached={reached} attacked={attacked} inBattle={inBattle} pos={goblin.Position} phase={engine.Phase}");

                var next = AdvanceToMain(engine, o, turn + 2);
                Check($"{name}: its battle position cannot be changed during its controller's next turn",
                    next && o.TryFindMonster(goblin, out _) && !engine.CanChangePosition(o, goblin) &&
                    BattlePhaseEndRuntime.ManualPositionChangeLocked(engine, goblin),
                    $"next={next} turn={engine.TurnNumber} can={engine.CanChangePosition(o, goblin)}");

                var after = AdvanceToMain(engine, o, turn + 4);
                Check($"{name}: the lock ends with the End Phase of that next turn",
                    after && engine.CanChangePosition(o, goblin),
                    $"after={after} turn={engine.TurnNumber} can={engine.CanChangePosition(o, goblin)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                var goblin = PlaceMonster(engine, o, GoblinAttackForce, 0, BattlePosition.Attack, true);
                ToMainPhase2(engine, o);
                Check("Goblin Attack Force: a Goblin that did not attack stays in Attack Position and is not locked",
                    reached && goblin.Position == BattlePosition.Attack && engine.CanChangePosition(o, goblin),
                    $"reached={reached} pos={goblin.Position} can={engine.CanChangePosition(o, goblin)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                var goblin = PlaceMonster(engine, o, GoblinAttackForce, 0, BattlePosition.Attack, true);
                var attacked = reached && Attack(engine, o, goblin, null);
                var turn = engine.TurnNumber;
                engine.TryEndTurnSafe(o); // straight from the Battle Phase: its End Step still happens
                Check("Goblin Attack Force: ending the turn from the Battle Phase still changes it to Defense Position",
                    attacked && engine.TurnNumber == turn + 1 && goblin.Position == BattlePosition.Defense,
                    $"attacked={attacked} turn={engine.TurnNumber} pos={goblin.Position}");
                // Its End Step effect opens no window or choice (DuelEngine.EndTurn does not wait for one).
                Check("Goblin Attack Force: its End Step effect leaves no window open into the next turn",
                    engine.TurnPlayer == engine.Player && !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget,
                    $"tp={engine.TurnPlayer?.Name} awaiting={engine.IsAwaitingResponse} timing={engine.PendingResponse?.Timing} " +
                    $"target={engine.IsAwaitingEffectTarget}");
            }

            {
                // Review fix: its only attack is negated by Negate Attack, which ends the Battle Phase while the
                // attack is being negated (before the engine records the attack as resolved). Lua poscon is
                // GetAttackedCount() > 0: a negated attack still counts, so it goes to Defense and is locked.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var reached = ReachPlayerBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var goblin = PlaceMonster(engine, p, GoblinAttackForce, 0, BattlePosition.Attack, true);
                var negate = PlaceSetTrap(engine, o, NegateAttack, 0);
                var lp = o.LifePoints;
                var declared = reached && engine.TryAttack(p, goblin, null);
                DrainCombat(engine);
                var negated = !o.TryFindSpellTrap(negate, out _) && o.LifePoints == lp;
                Check("Goblin Attack Force: an attack negated by Negate Attack (Battle Phase ended) still changes it to Defense and locks it",
                    declared && negated && engine.Phase == DuelPhase.Main2 && goblin.FaceUp &&
                    goblin.Position == BattlePosition.Defense &&
                    BattlePhaseEndRuntime.ManualPositionChangeLocked(engine, goblin) && !engine.CanChangePosition(p, goblin),
                    $"reached={reached} declared={declared} negated={negated} phase={engine.Phase} pos={goblin.Position} " +
                    $"attacked={goblin.AttackedThisTurn} can={engine.CanChangePosition(p, goblin)}");
            }

            {
                // The Player's Goblin attacked on the Player's turn; after the lock it is changed back to Attack and a
                // later Battle Phase it does not attack in leaves it there (the opponent's Battle Phases never apply it).
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var reached = ReachPlayerBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var goblin = PlaceMonster(engine, p, GoblinAttackForce, 0, BattlePosition.Attack, true);
                var attacked = reached && Attack(engine, p, goblin, null);
                var turn = engine.TurnNumber;
                ToMainPhase2(engine, p);
                var defense = goblin.Position == BattlePosition.Defense;
                var back = AdvanceToMain(engine, p, turn + 4);
                var can = back && engine.CanChangePosition(p, goblin);
                if (can) engine.TryChangePosition(p, goblin);
                engine.TryEnterBattlePhase(p);
                ToMainPhase2(engine, p);
                Check("Goblin Attack Force: back in Attack Position after the lock, a Battle Phase it does not attack in leaves it",
                    attacked && defense && can && goblin.Position == BattlePosition.Attack,
                    $"attacked={attacked} defense={defense} back={back} can={can} pos={goblin.Position}");
            }
        }

        // ── Deferred: Sword Hunter / Vampire Baby do nothing (fail closed) ──

        static void DeferredDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hunter = PlaceMonster(engine, o, SwordHunter, 0, BattlePosition.Attack, true);
                var prey = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var attacked = reached && Attack(engine, o, hunter, prey);
                ToMainPhase2(engine, o);
                // Rewritten for the BothSideAndMonsterEquip batch (v161): the kill is now equipped to Sword Hunter.
                Check("BattlePhaseEnd: Sword Hunter (v161) equips its kill from the GY as an Equip Spell Card (+200 ATK)",
                    attacked && !p.Graveyard.Contains(prey) && prey.EquippedTo == hunter && hunter.Equips.Contains(prey) &&
                    o.TryFindSpellTrap(prey, out _) && hunter.CurrentAtk == 2650,
                    $"reached={reached} attacked={attacked} inGy={p.Graveyard.Contains(prey)} atk={hunter.CurrentAtk}");
            }

            {
                // Review scenario: the Player's Vampire Baby destroys Ojama Green, the Player holds a Set Torrential
                // Tribute and ends the turn straight from the Battle Phase. Rewritten for the SummonPromptsAndCosts batch
                // (v163): the turn stops for the offer before its End Phase; declined, the turn ends and no Summon window
                // can reach the opponent's turn.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var reached = ReachPlayerBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var baby = PlaceMonster(engine, p, VampireBaby, 0, BattlePosition.Attack, true);
                var prey = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                PlaceSetTrap(engine, p, TorrentialTribute, 0);
                var attacked = reached && Attack(engine, p, baby, prey);
                var turn = engine.TurnNumber;
                engine.TryEndTurnSafe(p);
                var offered = engine.IsAwaitingEffectTarget && engine.TurnNumber == turn &&
                              engine.PendingActivation.LegalTargets.Contains(prey);
                if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                Check("BattlePhaseEnd: Vampire Baby (v163) declined — nothing is summoned; the held turn ends with no window open",
                    offered && attacked && o.Graveyard.Contains(prey) && !p.TryFindMonster(prey, out _) &&
                    engine.TurnNumber == turn + 1 && !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget,
                    $"reached={reached} attacked={attacked} inGy={o.Graveyard.Contains(prey)} turn={engine.TurnNumber} (was {turn}) " +
                    $"awaiting={engine.IsAwaitingResponse} timing={engine.PendingResponse?.Timing}");
            }
        }

        // ── Steel Scorpion ──

        static void SteelScorpionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var scorpion = PlaceMonster(engine, p, SteelScorpion, 0, BattlePosition.Attack, true);
                var attacker = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var turn = engine.TurnNumber;
                var attacked = reached && Attack(engine, o, attacker, scorpion);
                var scorpionGone = p.Graveyard.Contains(scorpion);
                var first = AdvanceToMain(engine, o, turn + 2);
                var alive1 = o.TryFindMonster(attacker, out _);
                var second = AdvanceToMain(engine, o, turn + 4);
                var alive2 = o.TryFindMonster(attacker, out _);
                var after = AdvanceToMain(engine, p, turn + 5);
                Check("Steel Scorpion: the non-Machine attacker survives until the opponent's 2nd turn after the attack",
                    attacked && scorpionGone && first && alive1 && second && alive2,
                    $"reached={reached} attacked={attacked} scorpionGone={scorpionGone} first={first} alive1={alive1} second={second} alive2={alive2}");
                Check("Steel Scorpion: … and is destroyed at that turn's End Phase, though Steel Scorpion left the field",
                    after && !o.TryFindMonster(attacker, out _) && o.Graveyard.Contains(attacker),
                    $"after={after} turn={engine.TurnNumber} onField={o.TryFindMonster(attacker, out _)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var scorpion = PlaceMonster(engine, p, SteelScorpion, 0, BattlePosition.Attack, true);
                var machine = PlaceMonster(engine, o, Mechanicalchaser, 0, BattlePosition.Attack, true);
                var turn = engine.TurnNumber;
                var attacked = reached && Attack(engine, o, machine, scorpion);
                var after = AdvanceToMain(engine, p, turn + 5);
                Check("Steel Scorpion: a Machine-Type attacker is not destroyed",
                    attacked && p.Graveyard.Contains(scorpion) && after && o.TryFindMonster(machine, out _),
                    $"attacked={attacked} after={after} onField={o.TryFindMonster(machine, out _)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var scorpion = PlaceMonster(engine, p, SteelScorpion, 0, BattlePosition.Attack, true);
                var attacker = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var turn = engine.TurnNumber;
                var attacked = reached && Attack(engine, o, attacker, scorpion);
                // Turned face-down (Book of Moon) and back: the countdown ends (ygopro RESET_TURN_SET).
                attacker.FaceUp = false;
                attacker.Position = BattlePosition.Defense;
                attacker.FaceUp = true;
                attacker.Position = BattlePosition.Attack;
                var after = AdvanceToMain(engine, p, turn + 5);
                Check("Steel Scorpion: the countdown ends when the attacker is turned face-down",
                    attacked && p.Graveyard.Contains(scorpion) && after && o.TryFindMonster(attacker, out _),
                    $"attacked={attacked} after={after} onField={o.TryFindMonster(attacker, out _)}");
            }

            {
                // Steel Scorpion attacking: its effect needs it to be attacked.
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var reached = ReachPlayerBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var scorpion = PlaceMonster(engine, p, SteelScorpion, 0, BattlePosition.Attack, true);
                var wall = PlaceMonster(engine, o, OjamaGreen, 0, BattlePosition.Defense, true);
                var turn = engine.TurnNumber;
                var attacked = reached && Attack(engine, p, scorpion, wall);
                var after = AdvanceToMain(engine, o, turn + 5);
                Check("Steel Scorpion: a monster it attacks is not marked",
                    attacked && after && o.TryFindMonster(wall, out _),
                    $"attacked={attacked} after={after} onField={o.TryFindMonster(wall, out _)}");
            }
        }

        // ── Reflect Bounder ──

        static void ReflectBounderDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bounder = PlaceMonster(engine, p, ReflectBounder, 0, BattlePosition.Attack, true);
                var attacker = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                var myLp = p.LifePoints;
                var attacked = reached && Attack(engine, o, attacker, bounder);
                Check("Reflect Bounder: attacked in Attack Position — 1400 effect damage + 300 battle damage to the attacker's controller",
                    attacked && o.LifePoints == lp - 1700 && p.LifePoints == myLp,
                    $"reached={reached} attacked={attacked} lp={o.LifePoints} (was {lp}) mine={p.LifePoints} (was {myLp})");
                Check("Reflect Bounder: it destroys itself after damage calculation; the weaker attacker is destroyed by battle",
                    p.Graveyard.Contains(bounder) && o.Graveyard.Contains(attacker),
                    $"bounderGy={p.Graveyard.Contains(bounder)} attackerGy={o.Graveyard.Contains(attacker)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bounder = PlaceMonster(engine, p, ReflectBounder, 0, BattlePosition.Attack, true);
                var attacker = PlaceMonster(engine, o, LusterDragon, 0, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                var myLp = p.LifePoints;
                var attacked = reached && Attack(engine, o, attacker, bounder);
                Check("Reflect Bounder: a stronger attacker — 1900 effect damage, 200 battle damage to Bounder's controller",
                    attacked && o.LifePoints == lp - 1900 && p.LifePoints == myLp - 200,
                    $"attacked={attacked} lp={o.LifePoints} (was {lp}) mine={p.LifePoints} (was {myLp})");
                Check("Reflect Bounder: destroyed by its own effect, not by battle (the attacker did not destroy it)",
                    p.Graveyard.Contains(bounder) && !bounder.WasDestroyedByBattle &&
                    o.TryFindMonster(attacker, out _) && !attacker.DestroyedByBattleThisTurn,
                    $"gy={p.Graveyard.Contains(bounder)} byBattle={bounder.WasDestroyedByBattle} killer={attacker.DestroyedByBattleThisTurn}");
            }

            {
                // Penumbral Soldier Lady gains 1000 ATK during the Damage Step against a LIGHT monster.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bounder = PlaceMonster(engine, p, ReflectBounder, 0, BattlePosition.Attack, true);
                var attacker = PlaceMonster(engine, o, Penumbral, 0, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                var attacked = reached && Attack(engine, o, attacker, bounder);
                Check("Reflect Bounder: the damage is the attacker's ATK before damage calculation (Damage Step boosts count)",
                    attacked && o.LifePoints == lp - 3100 && p.Graveyard.Contains(bounder),
                    $"attacked={attacked} lp={o.LifePoints} (was {lp})");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bounder = PlaceMonster(engine, p, ReflectBounder, 0, BattlePosition.Defense, true);
                var attacker = PlaceMonster(engine, o, Bokoichi, 0, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                var attacked = reached && Attack(engine, o, attacker, bounder);
                Check("Reflect Bounder: in face-up Defense Position — no effect damage, it stays (500 battle damage only)",
                    attacked && o.LifePoints == lp - 500 && p.TryFindMonster(bounder, out _),
                    $"attacked={attacked} lp={o.LifePoints} (was {lp}) onField={p.TryFindMonster(bounder, out _)}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bounder = PlaceMonster(engine, p, ReflectBounder, 0, BattlePosition.Defense, false);
                var attacker = PlaceMonster(engine, o, Bokoichi, 0, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                var attacked = reached && Attack(engine, o, attacker, bounder);
                Check("Reflect Bounder: Set and attacked (flipped to face-up Defense) — no effect damage, it stays",
                    attacked && o.LifePoints == lp - 500 && p.TryFindMonster(bounder, out _) && bounder.FaceUp,
                    $"attacked={attacked} lp={o.LifePoints} (was {lp}) onField={p.TryFindMonster(bounder, out _)}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var reached = ReachPlayerBattle(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var bounder = PlaceMonster(engine, p, ReflectBounder, 0, BattlePosition.Attack, true);
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var myLp = p.LifePoints;
                var lp = o.LifePoints;
                var attacked = reached && Attack(engine, p, bounder, target);
                Check("Reflect Bounder: attacking — no effect (only when it is attacked); 300 battle damage, it stays",
                    attacked && o.LifePoints == lp - 300 && p.LifePoints == myLp && p.TryFindMonster(bounder, out _),
                    $"reached={reached} attacked={attacked} lp={o.LifePoints} (was {lp}) mine={p.LifePoints} (was {myLp})");
            }
        }
    }
}
