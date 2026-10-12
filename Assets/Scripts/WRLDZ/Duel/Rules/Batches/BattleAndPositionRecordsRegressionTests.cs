using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// BattleAndPositionRecords batch: Atomic Firefly, Disc Fighter, Ghost Knight of Jackal, Legacy Hunter, Vampire Lady (and
    /// Vampire Lord, whose shared clause now declares), The Unhappy Girl, Judgment of the Desert, Curse of Anubis. Compile
    /// shapes through the real compiler; battles, summons and Trap activations through the engine entry points (TryAttack,
    /// TryFlipSummon, TryActivateSpellTrap, TrySelectEffectTarget, CanChangePosition, CanAttack); look-alike sentences that
    /// must stay uncompiled. Turn 3 is the Player's, turn 4 the AI's. Runtime: <see cref="BattleAndPositionRecordsRuntime"/>.
    /// </summary>
    public static class BattleAndPositionRecordsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int AtomicFirefly = 87340664;       // Insect Lv1 100/200
        const int DiscFighter = 19612721;         // Warrior Lv4 1000/1000
        const int GhostKnight = 13386503;         // Ghost Knight of Jackal, Beast-Warrior Lv5 1700/1600
        const int LegacyHunter = 87010442;        // Warrior Lv4 1000/1500
        const int VampireLady = 26495087;         // Zombie Lv4 1550/1550
        const int VampireLord = 53839837;         // Zombie Lv5 2000/1500
        const int UnhappyGirl = 27618634;         // The Unhappy Girl, Spellcaster Lv2 400/300
        const int JudgmentOfTheDesert = 4869446;  // Continuous Trap
        const int CurseOfAnubis = 66742250;       // Normal Trap

        const int AxeRaider = 48305365;           // Normal 1700/1150
        const int CelticGuardian = 91152256;      // Normal 1400/1200
        const int GeminiElf = 69140098;           // Normal 1900/900
        const int GiantSoldier = 13039848;        // Normal 1300/2000
        const int MysticalElf = 15025844;         // Normal 800/2000
        const int Sangan = 26202165;              // Effect 1000/600
        const int Witch = 78010363;               // Witch of the Black Forest, Effect 1100/1200
        const int ManEaterBug = 54652250;         // FLIP Effect 450/600
        const int PotOfGreed = 55144522;          // Normal Spell
        const int Raigeki = 12580477;             // Normal Spell
        const int TrapHole = 4206964;             // Normal Trap
        const int Mst = 5318639;                  // Quick-Play: target 1 Spell/Trap; destroy it

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("BattleAndPositionRecords: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            AtomicFireflyDuels(db, pDeck, aDeck, Check);
            DiscFighterDuels(db, pDeck, aDeck, Check);
            GhostKnightDuels(db, pDeck, aDeck, Check);
            LegacyHunterDuels(db, pDeck, aDeck, Check);
            VampireDuels(db, pDeck, aDeck, Check);
            UnhappyGirlDuels(db, pDeck, aDeck, Check);
            JudgmentOfTheDesertDuels(db, pDeck, aDeck, Check);
            CurseOfAnubisDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 of turn 3, empty boards, hands and GYs.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            engine.Player.Graveyard.Clear();
            engine.Opponent.Graveyard.Clear();
            return engine;
        }

        /// <summary>End the current turn; true once the next turn's Main Phase 1 is reached.</summary>
        static bool NextTurnMain(DuelEngine engine)
        {
            var turn = engine.TurnNumber;
            for (var i = 0; i < 4 && engine.TurnNumber == turn; i++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnNumber == turn + 1 && engine.Phase == DuelPhase.Main1;
        }

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>The AI's Battle Phase (turn 4): <paramref name="attacker"/> attacks <paramref name="target"/>.</summary>
        static bool OpponentAttacks(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (!ReachOpponentBattle(engine)) return false;
            engine.Opponent.Hand.Clear();
            var ok = engine.TryAttack(engine.Opponent, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            foreach (var id in ids) who.Deck.Add(id);
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);
        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);
        static bool GyHasId(DuelistState who, int id) => who.Graveyard.Any(c => c != null && c.CardId == id);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} amt={c.Amount} opt={c.IsOptional} " +
                    $"bat={c.RequiresThisDestroyedByBattle} destroyer={c.ImplicitTargetIsBattleDestroyer} " +
                    $"cond={c.ActivationCondition} stays={c.StaysOnField} chain={c.MakesChainLink}")) + "]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Is(int id, CardEffectStatusKind k) => CardEffectStatus.Classify(db.Get(id)) == k;

            bool One(CompiledCardProgram p, EffectTiming t, EffectActionKind a) =>
                p != null && p.FullyCompiled && p.ClauseList.Count == 1 && p.ClauseList[0].Timing == t &&
                p.ClauseList[0].Action == a;

            var firefly = P(AtomicFirefly);
            Check("Atomic Firefly: destroyed by battle → the player who destroyed it takes 1000",
                One(firefly, EffectTiming.SentFromFieldToGy, EffectActionKind.InflictDamageToPlayerWhoDestroyedThis) &&
                firefly.ClauseList[0].Amount == 1000 && firefly.ClauseList[0].RequiresThisDestroyedByBattle &&
                firefly.ClauseList[0].ImplicitTargetIsBattleDestroyer && !firefly.ClauseList[0].IsOptional &&
                Is(AtomicFirefly, CardEffectStatusKind.Implemented), Describe(firefly));

            var disc = P(DiscFighter);
            Check("Disc Fighter: battle confirm vs a 2000+ DEF Defense Position monster → destroy it",
                One(disc, EffectTiming.BeforeDamageCalculation,
                    EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation) &&
                disc.ClauseList[0].Amount == 2000 && Is(DiscFighter, CardEffectStatusKind.Implemented), Describe(disc));

            var ghost = P(GhostKnight);
            Check("Ghost Knight of Jackal: optional — Special Summon the monster it destroyed to your field in Defense",
                One(ghost, EffectTiming.ThisCardDestroysByBattle,
                    EffectActionKind.SpecialSummonBattleDestroyedMonsterToYourFieldInDefense) &&
                ghost.ClauseList[0].IsOptional && Is(GhostKnight, CardEffectStatusKind.Implemented), Describe(ghost));

            var hunter = P(LegacyHunter);
            Check("Legacy Hunter: destroys a face-down Defense monster → 1 random hand card back to the Deck",
                One(hunter, EffectTiming.ThisCardDestroysByBattle, EffectActionKind.OpponentReturnsRandomHandCardToDeckShuffle) &&
                !hunter.ClauseList[0].IsOptional && Is(LegacyHunter, CardEffectStatusKind.Implemented), Describe(hunter));

            var lady = P(VampireLady);
            var lord = P(VampireLord);
            Check("Vampire Lady: Vampire Lord's battle-damage declaration clause",
                One(lady, EffectTiming.ThisCardInflictsBattleDamage, EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck) &&
                !lady.ClauseList[0].IsOptional && Is(VampireLady, CardEffectStatusKind.Implemented) &&
                lord != null && lord.FullyCompiled &&
                lord.ClauseList.Exists(c => c.Action == EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck),
                Describe(lady) + " / lord " + Describe(lord));

            var girl = P(UnhappyGirl);
            Check("The Unhappy Girl: Attack Position battle protection + the battle-partner locks",
                girl != null && girl.FullyCompiled && girl.ClauseList.Count == 2 &&
                girl.ClauseList.All(c => c.Timing == EffectTiming.ContinuousWhileFaceUp) &&
                girl.ClauseList[0].Action == EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition &&
                girl.ClauseList[1].Action == EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition &&
                Is(UnhappyGirl, CardEffectStatusKind.Implemented), Describe(girl));

            var judgment = P(JudgmentOfTheDesert);
            Check("Judgment of the Desert: activation starts the flip record + the continuous position lock",
                judgment != null && judgment.FullyCompiled && judgment.ClauseList.Count == 2 &&
                judgment.ClauseList.All(c =>
                    c.Action == EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition && c.StaysOnField) &&
                judgment.ClauseList[0].Timing == EffectTiming.Activate &&
                judgment.ClauseList[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                Is(JudgmentOfTheDesert, CardEffectStatusKind.Implemented), Describe(judgment));

            var curse = P(CurseOfAnubis);
            Check("Curse of Anubis: face-up Attack Effect Monsters → Defense, original DEF 0, locked this turn",
                One(curse, EffectTiming.Activate, EffectActionKind.ChangeFaceUpAttackEffectMonstersToDefenseOriginalDefZeroLock) &&
                curse.ClauseList[0].ActivationCondition == BattleAndPositionRecordsRuntime.FaceUpAttackEffectMonsterOnField &&
                Is(CurseOfAnubis, CardEffectStatusKind.Implemented), Describe(curse));

            // Look-alike sentences: a longer, reworded or differently-numbered text never reaches these clauses.
            var near = new (string Desc, Func<EffectClause, bool> Leak)[]
            {
                ("When this face-up card on the field attacks or is attacked and it is destroyed as a result of battle, the " +
                 "player who destroyed it takes 1000 points of damage. Then draw 1 card.",
                    c => c.Action == EffectActionKind.InflictDamageToPlayerWhoDestroyedThis),
                ("If this card attacks a Defense Position monster with DEF 2000 or more, destroy the monster with this " +
                 "card's effect without applying damage calculation, and inflict 500 damage to your opponent.",
                    c => c.Action == EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation),
                ("You can Special Summon an opponent's monster that is destroyed and sent to the Graveyard by this card as a " +
                 "result of battle to your side of the field in face-up Attack Position.",
                    c => c.Action == EffectActionKind.SpecialSummonBattleDestroyedMonsterToYourFieldInDefense),
                ("When this card attacks a face-down Defense Position monster and destroys it and sends it to the Graveyard " +
                 "as a result of battle, your opponent returns 2 random cards from their hand to their Deck. Their Deck is " +
                 "then shuffled.",
                    c => c.Action == EffectActionKind.OpponentReturnsRandomHandCardToDeckShuffle),
                ("When this card inflicts battle damage to your opponent: Declare 1 card type (Monster, Spell, or Trap); your " +
                 "opponent sends 2 cards of that type from their Deck to the Graveyard.",
                    c => c.Action == EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck),
                ("While this card is in face-up Attack Position on the field, this card is not destroyed as a result of " +
                 "battle. (Damage calculation is applied normally.) A monster that battled with this card cannot attack " +
                 "while this card is in face-up Attack Position on the field.",
                    c => c.Action == EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition ||
                         c.Action == EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition),
                ("The Battle Positions of face-up monsters that are flipped face-up after activation of this card cannot be " +
                 "changed.",
                    c => c.Action == EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition),
                ("Change all Effect Monsters on the field to Defense Position. Until the end of this turn, the original ATK of " +
                 "those monsters becomes 0, also their battle positions cannot be changed.",
                    c => c.Action == EffectActionKind.ChangeFaceUpAttackEffectMonstersToDefenseOriginalDefZeroLock),
            };
            var leaked = near.Where((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90717800 + i, name = "Synthetic BattleAndPositionRecords near miss " + i,
                    type = n.Desc.StartsWith("The Battle") || n.Desc.StartsWith("Change all") ? "Trap Card" : "Effect Monster",
                    race = n.Desc.StartsWith("The Battle") ? "Continuous" : n.Desc.StartsWith("Change all") ? "Normal" : "Warrior",
                    desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c => n.Leak(c)));
            }).Select(n => n.Desc.Substring(0, Math.Min(48, n.Desc.Length))).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(" | ", leaked));
        }

        // ───────────────────────────── Atomic Firefly ─────────────────────────────

        static void AtomicFireflyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var firefly = PlaceMonster(engine, o, AtomicFirefly, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, axe, firefly);
                Check("Atomic Firefly (AI, attacked): destroyed by battle — the Player, who destroyed it, takes 1000",
                    reached && ok && InGy(o, firefly) && p.LifePoints == pLp - 1000 && o.LifePoints == oLp - 1600,
                    $"ok={ok} gy={InGy(o, firefly)} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var firefly = PlaceMonster(engine, p, AtomicFirefly, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, firefly, celtic);
                Check("Atomic Firefly (Player, attacking): destroyed by battle — the AI takes 1000, the Player 1300 battle damage",
                    reached && ok && InGy(p, firefly) && OnField(o, celtic) && o.LifePoints == oLp - 1000 &&
                    p.LifePoints == pLp - 1300,
                    $"ok={ok} gy={InGy(p, firefly)} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var firefly = PlaceMonster(engine, o, AtomicFirefly, 0, BattlePosition.Defense, false);
                var axe = PlaceMonster(engine, p, AxeRaider, 0, BattlePosition.Attack, true);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, axe, firefly);
                Check("Atomic Firefly (AI, Set): flipped by the attack and destroyed — the Player still takes 1000",
                    reached && ok && InGy(o, firefly) && p.LifePoints == pLp - 1000 && o.LifePoints == oLp,
                    $"ok={ok} gy={InGy(o, firefly)} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var firefly = PlaceMonster(engine, o, AtomicFirefly, 0, BattlePosition.Attack, true);
                var raigeki = PutInHand(engine, p, Raigeki);
                engine.NotifyPublic();
                int pLp = p.LifePoints;
                var act = engine.TryActivateSpellTrap(p, raigeki, fromHand: true);
                PassAll(engine);
                Check("Atomic Firefly (near miss): destroyed by a card effect (Raigeki) — no damage",
                    reached && act && InGy(o, firefly) && p.LifePoints == pLp,
                    $"act={act} gy={InGy(o, firefly)} pLp={pLp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Disc Fighter ─────────────────────────────

        static void DiscFighterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, faceUp, label) in new[]
                     {
                         (GiantSoldier, true, "a face-up Defense Position 2000 DEF monster"),
                         (MysticalElf, false, "a Set 2000 DEF monster (flipped first)"),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var disc = PlaceMonster(engine, p, DiscFighter, 0, BattlePosition.Attack, true);
                var wall = PlaceMonster(engine, o, id, 0, BattlePosition.Defense, faceUp);
                int pLp = p.LifePoints, oLp = o.LifePoints;
                var ok = AttackAndDrain(engine, p, disc, wall);
                Check("Disc Fighter: attacking " + label + " destroys it with no damage calculation",
                    reached && ok && InGy(o, wall) && OnField(p, disc) && p.LifePoints == pLp && o.LifePoints == oLp &&
                    !disc.DestroyedByBattleThisTurn,
                    $"ok={ok} wallGy={InGy(o, wall)} disc={OnField(p, disc)} pLp={pLp}->{p.LifePoints} oLp={oLp}->{o.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var disc = PlaceMonster(engine, p, DiscFighter, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Defense, true);
                int pLp = p.LifePoints;
                var ok = AttackAndDrain(engine, p, disc, celtic);
                Check("Disc Fighter (near miss): a 1200 DEF Defense Position monster — normal damage calculation",
                    reached && ok && OnField(o, celtic) && OnField(p, disc) && p.LifePoints == pLp - 200,
                    $"ok={ok} celtic={OnField(o, celtic)} pLp={pLp}->{p.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var disc = PlaceMonster(engine, p, DiscFighter, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(engine, o, GiantSoldier, 0, BattlePosition.Attack, true);
                int pLp = p.LifePoints;
                var ok = AttackAndDrain(engine, p, disc, soldier);
                Check("Disc Fighter (near miss): a 2000 DEF monster in Attack Position — normal battle",
                    reached && ok && OnField(o, soldier) && InGy(p, disc) && p.LifePoints == pLp - 300,
                    $"ok={ok} soldier={OnField(o, soldier)} discGy={InGy(p, disc)} pLp={pLp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Ghost Knight of Jackal ─────────────────────────────

        static void GhostKnightDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var accept in new[] { true, false })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ghost = PlaceMonster(engine, p, GhostKnight, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, ghost, celtic);
                var asked = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(celtic);
                var answered = asked && (accept ? engine.TrySelectEffectTarget(celtic) : engine.CancelEffectTargeting());
                DrainCombat(engine);
                Check(accept
                        ? "Ghost Knight of Jackal (Player): yes — the destroyed monster is Special Summoned to the Player's field in face-up Defense"
                        : "Ghost Knight of Jackal (Player): Cancel — the destroyed monster stays in the AI's GY",
                    reached && ok && asked && answered && !engine.IsAwaitingEffectTarget &&
                    (accept
                        ? OnField(p, celtic) && celtic.FaceUp && celtic.Position == BattlePosition.Defense &&
                          celtic.ControlOwner == o && !InGy(o, celtic)
                        : InGy(o, celtic) && !OnField(p, celtic)),
                    $"ok={ok} asked={asked} answered={answered} field={OnField(p, celtic)} pos={celtic.Position} " +
                    $"gy={InGy(o, celtic)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ghost = PlaceMonster(engine, o, GhostKnight, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, ghost, celtic);
                Check("Ghost Knight of Jackal (AI): it summons the Player's destroyed monster to its field in face-up Defense",
                    ok && OnField(o, celtic) && celtic.FaceUp && celtic.Position == BattlePosition.Defense &&
                    !InGy(p, celtic) && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} field={OnField(o, celtic)} pos={celtic.Position} gy={InGy(p, celtic)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var ghost = PlaceMonster(engine, p, GhostKnight, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 0, BattlePosition.Attack, true);
                for (var z = 1; z < p.MonsterZones.Length; z++)
                    PlaceMonster(engine, p, AxeRaider, z, BattlePosition.Defense, true);
                var ok = AttackAndDrain(engine, p, ghost, celtic);
                Check("Ghost Knight of Jackal (near miss): no free Monster Zone — no prompt, the monster stays in the GY",
                    reached && ok && InGy(o, celtic) && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} gy={InGy(o, celtic)} awaiting={engine.IsAwaitingEffectTarget}");
            }
        }

        // ───────────────────────────── Legacy Hunter ─────────────────────────────

        static void LegacyHunterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (faceUp, label) in new[] { (false, "a Set monster"), (true, "a face-up Defense Position monster") })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hunter = PlaceMonster(engine, p, LegacyHunter, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Defense, faceUp);
                SetDeck(o, AxeRaider, AxeRaider, AxeRaider);
                var pot = PutInHand(engine, o, PotOfGreed);
                var raigeki = PutInHand(engine, o, Raigeki);
                engine.Rng.QueuePick(1);
                var ok = AttackAndDrain(engine, p, hunter, elf);
                var returned = o.Hand.Count == 1 && o.Hand.Contains(pot) && !o.Hand.Contains(raigeki) &&
                               o.Deck.Count == 4 && o.Deck.Contains(Raigeki);
                var untouched = o.Hand.Count == 2 && o.Deck.Count == 3;
                Check(faceUp
                        ? "Legacy Hunter (near miss): destroying " + label + " returns nothing"
                        : "Legacy Hunter: destroying " + label + " — 1 random AI hand card returns to the Deck",
                    reached && ok && InGy(o, elf) && (faceUp ? untouched : returned),
                    $"ok={ok} elfGy={InGy(o, elf)} hand={o.Hand.Count} deck={o.Deck.Count}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hunter = PlaceMonster(engine, p, LegacyHunter, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, GeminiElf, 0, BattlePosition.Defense, false);
                var deck = o.Deck.Count;
                var ok = AttackAndDrain(engine, p, hunter, elf);
                Check("Legacy Hunter: the AI's hand is empty — nothing happens",
                    reached && ok && InGy(o, elf) && o.Hand.Count == 0 && o.Deck.Count == deck,
                    $"ok={ok} hand={o.Hand.Count} deck={deck}->{o.Deck.Count}");
            }
        }

        // ───────────────────────────── Vampire Lady / Vampire Lord ─────────────────────────────

        static CardInstance Token(DuelEngine engine, string name) =>
            engine.PendingActivation?.LegalTargets.FirstOrDefault(t => t?.Def?.name == name);

        static void VampireDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, declare, sentId, label) in new[]
                     {
                         (VampireLady, "Spell", PotOfGreed, "Vampire Lady (Player): declares Spell — the AI sends its Spell"),
                         (VampireLord, "Trap", TrapHole, "Vampire Lord (Player): declares Trap — the AI sends its Trap"),
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var vamp = PlaceMonster(engine, p, id, 0, BattlePosition.Attack, true);
                SetDeck(o, AxeRaider, AxeRaider, PotOfGreed, TrapHole);
                var ok = AttackAndDrain(engine, p, vamp, null);
                var asked = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Count == 3;
                var token = Token(engine, declare);
                var answered = asked && token != null && engine.TrySelectEffectTarget(token);
                DrainCombat(engine);
                Check(label,
                    reached && ok && asked && answered && GyHasId(o, sentId) && o.Deck.Count == 3 &&
                    !o.Deck.Contains(sentId) && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} asked={asked} answered={answered} gy={string.Join(",", o.Graveyard.Select(c => c.Name))} " +
                    $"deck={o.Deck.Count}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lady = PlaceMonster(engine, o, VampireLady, 0, BattlePosition.Attack, true);
                SetDeck(p, PotOfGreed, Raigeki, Raigeki, AxeRaider);
                var ok = ReachOpponentBattle(engine);
                o.Hand.Clear();
                ok = ok && engine.TryAttack(o, lady, null);
                DrainCombat(engine);
                var asked = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Count == 2 &&
                            engine.PendingActivation.LegalTargets.All(c => c.Def.IsSpell);
                var pot = engine.PendingActivation?.LegalTargets.FirstOrDefault(c => c.CardId == PotOfGreed);
                var answered = asked && pot != null && engine.TrySelectEffectTarget(pot);
                DrainCombat(engine);
                Check("Vampire Lady (AI): declares Spell (most of the Player's Deck) — the Player picks which Spell is sent",
                    ok && asked && answered && GyHasId(p, PotOfGreed) && p.Deck.Count == 3 && !p.Deck.Contains(PotOfGreed),
                    $"ok={ok} asked={asked} answered={answered} gy={string.Join(",", p.Graveyard.Select(c => c.Name))} " +
                    $"deck={p.Deck.Count}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lady = PlaceMonster(engine, p, VampireLady, 0, BattlePosition.Attack, true);
                SetDeck(o, AxeRaider, AxeRaider);
                var ok = AttackAndDrain(engine, p, lady, null);
                var token = Token(engine, "Trap");
                var answered = token != null && engine.TrySelectEffectTarget(token);
                DrainCombat(engine);
                Check("Vampire Lady (Player): declares Trap, the AI's Deck has none — nothing is sent",
                    reached && ok && answered && o.Graveyard.Count == 0 && o.Deck.Count == 2 && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} answered={answered} gy={o.Graveyard.Count} deck={o.Deck.Count}");
            }
        }

        // ───────────────────────────── The Unhappy Girl ─────────────────────────────

        static void UnhappyGirlDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var girl = PlaceMonster(engine, p, UnhappyGirl, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, CelticGuardian, 1, BattlePosition.Attack, true);
                int pLp = p.LifePoints;
                var ok = AttackAndDrain(engine, p, girl, axe);
                var survived = OnField(p, girl) && OnField(o, axe) && p.LifePoints == pLp - 1300;
                var t4 = NextTurnMain(engine);
                var axeLocked = !engine.CanChangePosition(o, axe) && engine.ContinuousCannotAttackBlocks(o, axe);
                var celticFree = engine.CanChangePosition(o, celtic) && !engine.ContinuousCannotAttackBlocks(o, celtic);
                var t5 = NextTurnMain(engine);
                var changed = engine.TryChangePosition(p, girl);
                var axeFreed = !engine.ContinuousCannotAttackBlocks(o, axe) && !engine.IsBattlePositionLocked(axe);
                Check("The Unhappy Girl: not destroyed attacking; the battled AI monster cannot attack or change position " +
                      "while she stays in face-up Attack Position, and is freed once she changes to Defense",
                    reached && ok && survived && t4 && axeLocked && celticFree && t5 && changed && axeFreed,
                    $"ok={ok} survived={survived} t4={t4} axeLocked={axeLocked} celticFree={celticFree} t5={t5} " +
                    $"changed={changed} axeFreed={axeFreed} pLp={pLp}->{p.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var girl = PlaceMonster(engine, p, UnhappyGirl, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ok = OpponentAttacks(engine, axe, girl);
                var t5 = NextTurnMain(engine);
                var axeFree = !engine.IsBattlePositionLocked(axe) && !engine.ContinuousCannotAttackBlocks(o, axe);
                Check("The Unhappy Girl (near miss): attacked in face-up Defense Position — destroyed, no record",
                    ok && InGy(p, girl) && OnField(o, axe) && t5 && axeFree,
                    $"ok={ok} gy={InGy(p, girl)} t5={t5} axeFree={axeFree}");
            }
        }

        // ───────────────────────────── Judgment of the Desert ─────────────────────────────

        static void JudgmentOfTheDesertDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var judgment = PlaceSetTrap(engine, p, JudgmentOfTheDesert, 0);
            var gemini = PlaceMonster(engine, p, GeminiElf, 0, BattlePosition.Defense, false);
            var soldier = PlaceMonster(engine, p, GiantSoldier, 1, BattlePosition.Defense, false);
            var celtic = PlaceMonster(engine, p, CelticGuardian, 2, BattlePosition.Attack, true);
            var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, false);
            var witch = PlaceMonster(engine, o, Witch, 1, BattlePosition.Attack, true);
            engine.NotifyPublic();

            var before = engine.TryFlipSummon(p, gemini);
            PassAll(engine);
            var act = engine.TryActivateSpellTrap(p, judgment, fromHand: false);
            PassAll(engine);
            var after = engine.TryFlipSummon(p, soldier);
            PassAll(engine);
            var attacked = AttackAndDrain(engine, p, celtic, elf);
            var elfFlipped = OnField(o, elf) && elf.FaceUp;
            var t4 = NextTurnMain(engine);
            var elfLocked = !engine.CanChangePosition(o, elf) && engine.IsBattlePositionLocked(elf);
            var witchFree = engine.CanChangePosition(o, witch);
            var t5 = NextTurnMain(engine);
            var soldierLocked = !engine.CanChangePosition(p, soldier);
            var geminiFree = engine.CanChangePosition(p, gemini);
            var celticFree = engine.CanChangePosition(p, celtic);
            Check("Judgment of the Desert: monsters flipped after it resolved (Flip Summon, flipped by an attack) cannot " +
                  "change position by hand; one flipped before it and one never flipped can",
                reached && before && act && judgment.FaceUp && after && attacked && elfFlipped && t4 && elfLocked &&
                witchFree && t5 && soldierLocked && geminiFree && celticFree,
                $"before={before} act={act} up={judgment.FaceUp} after={after} attacked={attacked} elfUp={elfFlipped} " +
                $"t4={t4} elfLocked={elfLocked} witchFree={witchFree} t5={t5} soldierLocked={soldierLocked} " +
                $"geminiFree={geminiFree} celticFree={celticFree}");

            var byEffect = engine.ApplyEffectPositionChange(soldier, BattlePosition.Defense, faceUp: true);
            engine.ApplyEffectPositionChange(soldier, BattlePosition.Attack, faceUp: true);
            var mst = PutInHand(engine, p, Mst);
            engine.NotifyPublic();
            var mstAct = engine.TryActivateSpellTrap(p, mst, fromHand: true);
            if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(judgment);
            PassAll(engine);
            var freed = InGy(p, judgment) && engine.CanChangePosition(p, soldier);
            Check("Judgment of the Desert: a card effect still changes a recorded monster; once the Trap leaves, it is free",
                byEffect && mstAct && freed,
                $"byEffect={byEffect} mstAct={mstAct} judgmentGy={InGy(p, judgment)} soldierFree={engine.CanChangePosition(p, soldier)}");
        }

        // ───────────────────────────── Curse of Anubis ─────────────────────────────

        static void CurseOfAnubisDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var curse = PlaceSetTrap(engine, p, CurseOfAnubis, 0);
                var sangan = PlaceMonster(engine, p, Sangan, 0, BattlePosition.Attack, true);
                var witch = PlaceMonster(engine, o, Witch, 0, BattlePosition.Attack, true);
                var witchDef = PlaceMonster(engine, o, Witch, 1, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 2, BattlePosition.Attack, true);
                var bug = PlaceMonster(engine, o, ManEaterBug, 3, BattlePosition.Defense, false);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, curse, fromHand: false);
                PassAll(engine);
                var changed = sangan.FaceUp && sangan.Position == BattlePosition.Defense && sangan.CurrentDef == 0 &&
                              witch.FaceUp && witch.Position == BattlePosition.Defense && witch.CurrentDef == 0;
                var untouched = axe.Position == BattlePosition.Attack && !bug.FaceUp && witchDef.CurrentDef == 1200 &&
                                !engine.IsBattlePositionLocked(witchDef) && !engine.IsBattlePositionLocked(axe);
                var locked = !engine.CanChangePosition(p, sangan) && engine.IsBattlePositionLocked(witch);
                var t4 = NextTurnMain(engine);
                var restored = witch.CurrentDef == 1200 && sangan.CurrentDef == 600 && engine.CanChangePosition(o, witch);
                Check("Curse of Anubis: face-up Attack Effect Monsters → Defense, original DEF 0 and locked this turn; " +
                      "Normal, Set and Defense monsters untouched; next turn DEF and positions are back",
                    reached && act && changed && untouched && locked && t4 && restored,
                    $"act={act} changed={changed} sangan={sangan.Position}/{sangan.CurrentDef} witch={witch.Position}/" +
                    $"{witch.CurrentDef} untouched={untouched} locked={locked} t4={t4} restored={restored} " +
                    $"witchDef={witch.CurrentDef}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var curse = PlaceSetTrap(engine, p, CurseOfAnubis, 0);
                PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, Witch, 1, BattlePosition.Defense, true);
                PlaceMonster(engine, p, ManEaterBug, 0, BattlePosition.Defense, false);
                engine.NotifyPublic();
                var act = engine.TryActivateSpellTrap(p, curse, fromHand: false);
                PassAll(engine);
                Check("Curse of Anubis (near miss): no face-up Attack Position Effect Monster — it cannot be activated",
                    reached && !act && !curse.FaceUp, $"act={act} up={curse.FaceUp}");
            }
        }
    }
}
