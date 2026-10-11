using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SpiritMonsters batch: Asura Priest, Inaba White Rabbit, Fushi No Tori, Great Long Nose, Susa Soldier, Otohime
    /// (both prints) and Maharaghi — LOD Spirits that each add one sentence to the shared Spirit text. Compile shapes,
    /// mini-duels through the real engine entry points (TryNormalSummon, TryFlipSummon, TryAttack and the Damage Step,
    /// EndTurn and the next turn's Draw Phase, SimpleAi's turn) that also check the End Phase return, and near misses.
    /// Runtime: <see cref="SpiritMonstersRuntime"/>.
    /// </summary>
    public static class SpiritMonstersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int AsuraPriest = 2134346;        // Spirit Lv4 1700/1200
        const int InabaWhiteRabbit = 77084837;  // Spirit Lv3 700/500
        const int FushiNoTori = 38538445;       // Spirit Lv4 1200/0
        const int GreatLongNose = 2356994;      // Spirit Lv5 1900/1700
        const int SusaSoldier = 40473581;       // Spirit Lv4 2000/1600
        const int Otohime = 39751093;           // Spirit Lv3 0/100
        const int OtohimeAlt = 39751094;
        const int Maharaghi = 40695128;         // Spirit Lv4 1200/1700

        const int BerserkDragon = 85605684;     // same attack-all sentence, rest uncompiled
        const int ShadowknightArchfiend = 9603356; // "… inflicts to your opponent is halved."
        const int DiffusionWaveMotion = 87880531;
        const int EnemyController = 98045062;
        const int Ooguchi = 58861941;           // "This monster may attack your opponent's Life Points directly."
        const int YataGarasu = 3078576;

        const int Celtic = 91152256;            // Lv4 1400/1200
        const int OjamaGreen = 12482652;        // Lv2 0/1000
        const int BlueEyes = 89631139;          // Lv8 3000/2500
        const int RobbinGoblin = 88279736;      // Continuous Trap: battle damage → opponent discards 1 random
        const int PotOfGreed = 55144522;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SpiritMonsters: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            AsuraDuels(db, pDeck, aDeck, Check);
            InabaDuels(db, pDeck, aDeck, Check);
            FushiDuels(db, pDeck, aDeck, Check);
            GreatLongNoseDuels(db, pDeck, aDeck, Check);
            SusaDuels(db, pDeck, aDeck, Check);
            OtohimeDuels(db, pDeck, aDeck, Check);
            MaharaghiDuels(db, pDeck, aDeck, Check);
            NearMisses(db, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static bool Has(CompiledCardProgram prog, Func<EffectClause, bool> shape) =>
            prog != null && prog.ClauseList.Exists(c => c != null && shape(c));

        static bool SpiritReturnShape(EffectClause c) =>
            c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.ReturnToHand &&
            c.RequiresSummonedOrFlippedThisTurn && !c.RequiresTargetChoice;

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        /// <summary>The player's Main Phase 1 on turn 3+ (a Battle Phase is allowed), boards and hands empty.</summary>
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
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                      engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
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

        /// <summary>Drive a declared attack until a target pick opens (or the battle is over).</summary>
        static void DriveUntilPick(DuelEngine engine)
        {
            for (var i = 0; i < 12 && !engine.IsAwaitingEffectTarget; i++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                else if (engine.HasDeclaredAttack) engine.ResolveDeclaredAttack();
                else break;
            }
        }

        /// <summary>A monster placed this turn that should count as already face-up at the start of the turn.</summary>
        static CardInstance PlaceSettled(DuelEngine engine, DuelistState who, int id, int zone, BattlePosition pos,
            bool faceUp)
        {
            var c = PlaceMonster(engine, who, id, zone, pos, faceUp);
            c.FaceUpStampAtTurnStart = c.FaceUpStamp;
            return c;
        }

        /// <summary>The opponent's Main Phase 1 (turn 4+), boards and hands empty.</summary>
        static DuelEngine OpponentMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var r1);
            EndTurn(engine);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = r1 && engine.TurnPlayer == engine.Opponent && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>Step SimpleAi's turn coroutine synchronously; the player passes every window.</summary>
        static bool RunSimpleAiTurn(DuelEngine engine, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(engine, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (engine.PendingResponse != null && engine.PendingResponse.Responder == engine.Player)
                        engine.PassResponse();
                    if (!it.MoveNext()) return true;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => CardTextEffectCompiler.Compile(db.Get(id));

            var asura = P(AsuraPriest);
            Check("Asura Priest: Spirit return + continuous attack-all marker; Implemented",
                asura != null && asura.FullyCompiled && asura.ClauseList.Count == 2 && Has(asura, SpiritReturnShape) &&
                Has(asura, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.AttackAllOpponentMonstersOnceEach) &&
                Implemented(db, AsuraPriest),
                Describe(asura));

            var inaba = P(InabaWhiteRabbit);
            Check("Inaba White Rabbit: Spirit return + direct attack + cannot select a monster; Implemented",
                inaba != null && inaba.FullyCompiled && inaba.ClauseList.Count == 3 && Has(inaba, SpiritReturnShape) &&
                Has(inaba, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.CanAttackDirectly &&
                                string.IsNullOrEmpty(c.RequiresFaceUpName)) &&
                Has(inaba, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.CannotSelectMonsterAsAttackTarget) &&
                Implemented(db, InabaWhiteRabbit),
                Describe(inaba));

            var fushi = P(FushiNoTori);
            Check("Fushi No Tori: Spirit return + battle damage → gain LP equal to that damage; Implemented",
                fushi != null && fushi.FullyCompiled && fushi.ClauseList.Count == 2 && Has(fushi, SpiritReturnShape) &&
                Has(fushi, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                c.Action == EffectActionKind.GainLifePoints && c.AmountIsEquippedBattleDamage &&
                                c.Side == EffectSide.Controller && !c.RequiresDirectAttack) &&
                Implemented(db, FushiNoTori),
                Describe(fushi));

            var nose = P(GreatLongNose);
            Check("Great Long Nose: Spirit return + battle damage → opponent skips next Battle Phase; Implemented",
                nose != null && nose.FullyCompiled && nose.ClauseList.Count == 2 && Has(nose, SpiritReturnShape) &&
                Has(nose, c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                               c.Action == EffectActionKind.OpponentSkipsNextBattlePhase) &&
                !Has(nose, c => c.Action == EffectActionKind.SkipOpponentNextDrawPhase) &&
                Implemented(db, GreatLongNose),
                Describe(nose));

            var susa = P(SusaSoldier);
            Check("Susa Soldier: Spirit return + continuous halved battle damage marker; Implemented",
                susa != null && susa.FullyCompiled && susa.ClauseList.Count == 2 && Has(susa, SpiritReturnShape) &&
                Has(susa, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                               c.Action == EffectActionKind.HalveBattleDamageThisInflictsToOpponent) &&
                Implemented(db, SusaSoldier),
                Describe(susa));

            foreach (var id in new[] { Otohime, OtohimeAlt })
            {
                var oto = P(id);
                Check($"Otohime ({id}): Spirit return + optional targeted position change on Normal Summon or flip; " +
                      "no Activate clause; Implemented",
                    oto != null && oto.FullyCompiled && oto.ClauseList.Count == 2 && Has(oto, SpiritReturnShape) &&
                    Has(oto, c => c.Timing == EffectTiming.ThisCardSummoned &&
                                  c.Action == EffectActionKind.ChangeBattlePosition &&
                                  c.Zone == EffectZoneFilter.OppFaceUpMonsters && c.RequiresTargetChoice &&
                                  c.IsOptional && c.RequiresThisNormalOrFlipSummoned && c.AlsoWhenFlippedFaceUp &&
                                  !c.ForceAttackPosition && !c.ForceDefensePosition) &&
                    !Has(oto, c => c.Timing == EffectTiming.Activate) &&
                    Implemented(db, id),
                    Describe(oto));
            }

            var maha = P(Maharaghi);
            Check("Maharaghi: Spirit return + mandatory Normal Summon / flip registration of the pre-draw look; Implemented",
                maha != null && maha.FullyCompiled && maha.ClauseList.Count == 2 && Has(maha, SpiritReturnShape) &&
                Has(maha, c => c.Timing == EffectTiming.ThisCardSummoned &&
                               c.Action == EffectActionKind.LookAtTopCardAtNextDrawPhaseTopOrBottom &&
                               c.RequiresThisNormalOrFlipSummoned && c.AlsoWhenFlippedFaceUp && !c.IsOptional) &&
                Implemented(db, Maharaghi),
                Describe(maha));

            var berserk = P(BerserkDragon);
            // NamedSummonPairs batch: the End Phase -500 ATK sentence now compiles too (Implemented).
            Check("Berserk Dragon: the same attack-all sentence compiles; with its End Phase -500 ATK it is Implemented",
                berserk != null && berserk.FullyCompiled &&
                Has(berserk, c => c.Action == EffectActionKind.AttackAllOpponentMonstersOnceEach) &&
                Has(berserk, c => c.Timing == EffectTiming.EndPhase &&
                                  c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp && c.Amount == -500) &&
                CardEffectStatus.Classify(db.Get(BerserkDragon)) == CardEffectStatusKind.Implemented,
                Describe(berserk));

            var enemy = P(EnemyController);
            Check("Enemy Controller keeps its Activate position change (the Otohime guard is only \"can target\")",
                enemy != null && Has(enemy, c => c.Timing == EffectTiming.Activate &&
                                                 c.Action == EffectActionKind.ChangeBattlePosition &&
                                                 c.Zone == EffectZoneFilter.OppFaceUpMonsters),
                Describe(enemy));
        }

        // ───────────────────────────── Asura Priest ─────────────────────────────

        static void AsuraDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // No monsters: one ordinary direct attack, then no second (no direct follow-up).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var asura = PutInHand(engine, p, AsuraPriest);
                var ns = engine.TryNormalSummon(p, asura, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var lpO = o.LifePoints;
                var first = AttackAndDrain(engine, p, asura, null);
                var canAgain = engine.CanAttack(p, asura);
                var directAgain = engine.TryAttack(p, asura, null);
                Check("Asura Priest: the opponent controls no monster → one direct attack (1700), no second attack",
                    reached && ns && first && o.LifePoints == lpO - 1700 && !canAgain && !directAgain,
                    $"reached={reached} ns={ns} first={first} lp={lpO}->{o.LifePoints} canAgain={canAgain} direct2={directAgain}");
                EndTurn(engine);
                Check("Asura Priest: Normal Summoned → returns to the hand during the End Phase",
                    p.Hand.Contains(asura) && !p.TryFindMonster(asura, out _), $"inHand={p.Hand.Contains(asura)}");
            }

            {
                // Three monsters: each one once; never the same one twice, never directly after its first attack.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var asura = PutInHand(engine, p, AsuraPriest);
                var ns = engine.TryNormalSummon(p, asura, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var wall = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Defense, true);
                var ojama = PlaceMonster(engine, o, OjamaGreen, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var lpP = p.LifePoints;
                var lpO = o.LifePoints;
                var a1 = AttackAndDrain(engine, p, asura, wall);
                var same = engine.TryAttack(p, asura, wall);
                var direct = engine.TryAttack(p, asura, null);
                var canDirect = engine.CanAttackDirectly(p, asura);
                var a2 = AttackAndDrain(engine, p, asura, ojama);
                var a3 = AttackAndDrain(engine, p, asura, celtic);
                var can4 = engine.CanAttack(p, asura);
                var again = engine.TryAttack(p, asura, wall);
                Check("Asura Priest: attacks all 3 monsters once each (Blue-Eyes survives in Defense, 800 to its owner)",
                    reached && ns && a1 && a2 && a3 && o.Graveyard.Contains(ojama) && o.Graveyard.Contains(celtic) &&
                    o.TryFindMonster(wall, out _) && o.LifePoints == lpO - 1700 - 300 && p.LifePoints == lpP - 800 &&
                    asura.AttacksDeclaredThisTurn == 3,
                    $"a1={a1} a2={a2} a3={a3} lpO={lpO}->{o.LifePoints} lpP={lpP}->{p.LifePoints} attacks={asura.AttacksDeclaredThisTurn}");
                Check("Asura Priest: never the same monster twice in a Battle Phase, never a direct follow-up attack",
                    !same && !direct && !canDirect && !can4 && !again,
                    $"same={same} direct={direct} canDirect={canDirect} can4={can4} again={again}");
                EndTurn(engine);
                var next = engine.TurnPlayer == o;
                Check("Asura Priest: returns to the hand at the End Phase after its attacks",
                    next && p.Hand.Contains(asura), $"next={next} inHand={p.Hand.Contains(asura)}");
            }

            {
                // A new turn (here: a second copy placed face-up on a later turn) starts a clean record.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var asura = PlaceSettled(engine, p, AsuraPriest, 0, BattlePosition.Attack, true);
                var wall = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Defense, true);
                var a1 = AttackAndDrain(engine, p, asura, wall);
                EndTurn(engine);
                EndTurn(engine);
                var mine = engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1;
                var a2 = AttackAndDrain(engine, p, asura, wall);
                Check("Asura Priest: the monster it attacked last turn can be attacked again this turn",
                    reached && a1 && mine && a2 && p.TryFindMonster(asura, out _),
                    $"a1={a1} mine={mine} a2={a2}");
            }

            {
                // SimpleAi uses it: one attack per player monster.
                var engine = OpponentMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var asura = PlaceSettled(engine, o, AsuraPriest, 0, BattlePosition.Attack, true);
                var ojama = PlaceSettled(engine, p, OjamaGreen, 0, BattlePosition.Attack, true);
                var celtic = PlaceSettled(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var ran = RunSimpleAiTurn(engine, 600);
                Check("Asura Priest: SimpleAi attacks both of the player's monsters with it",
                    reached && ran && p.Graveyard.Contains(ojama) && p.Graveyard.Contains(celtic),
                    $"reached={reached} ran={ran} ojamaGy={p.Graveyard.Contains(ojama)} celticGy={p.Graveyard.Contains(celtic)} attacks={asura.AttacksDeclaredThisTurn}");
            }
        }

        // ───────────────────────────── Inaba White Rabbit ─────────────────────────────

        static void InabaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var inaba = PutInHand(engine, p, InabaWhiteRabbit);
                var ns = engine.TryNormalSummon(p, inaba, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var onMonster = AttackAndDrain(engine, p, inaba, celtic);
                var canDirect = engine.CanAttackDirectly(p, inaba);
                var legal = LegalIntentService.Build(engine, p).Entries
                    .Where(e => e.Card == inaba).ToList();
                var direct = AttackAndDrain(engine, p, inaba, null);
                Check("Inaba White Rabbit: cannot attack a monster; attacks directly while the opponent controls one",
                    reached && ns && !onMonster && canDirect && direct && o.LifePoints == lpO - 700 &&
                    o.TryFindMonster(celtic, out _),
                    $"ns={ns} onMonster={onMonster} canDirect={canDirect} direct={direct} lp={lpO}->{o.LifePoints}");
                Check("Inaba White Rabbit: the legal intents offer only its direct attack",
                    legal.Exists(e => e.Kind == LegalIntentService.LegalKind.DirectAttack) &&
                    !legal.Exists(e => e.Kind == LegalIntentService.LegalKind.Attack),
                    string.Join(",", legal.Select(e => e.Kind.ToString())));
                EndTurn(engine);
                Check("Inaba White Rabbit: returns to the hand during the End Phase",
                    p.Hand.Contains(inaba) && !p.TryFindMonster(inaba, out _), $"inHand={p.Hand.Contains(inaba)}");
            }

            {
                // SimpleAi attacks directly with it rather than giving up on a stronger board.
                var engine = OpponentMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSettled(engine, o, InabaWhiteRabbit, 0, BattlePosition.Attack, true);
                var celtic = PlaceSettled(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var lpP = p.LifePoints;
                var ran = RunSimpleAiTurn(engine, 600);
                Check("Inaba White Rabbit: SimpleAi attacks the player directly with it (700)",
                    reached && ran && p.LifePoints == lpP - 700 && p.TryFindMonster(celtic, out _),
                    $"reached={reached} ran={ran} lp={lpP}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Fushi No Tori ─────────────────────────────

        static void FushiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fushi = PutInHand(engine, p, FushiNoTori);
                var ns = engine.TryNormalSummon(p, fushi, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var lpP = p.LifePoints;
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, fushi, null);
                Check("Fushi No Tori: a direct attack inflicts 1200 and its controller gains 1200",
                    reached && ns && attacked && o.LifePoints == lpO - 1200 && p.LifePoints == lpP + 1200,
                    $"attacked={attacked} lpO={lpO}->{o.LifePoints} lpP={lpP}->{p.LifePoints}");
                EndTurn(engine);
                Check("Fushi No Tori: returns to the hand during the End Phase",
                    p.Hand.Contains(fushi) && !p.TryFindMonster(fushi, out _), $"inHand={p.Hand.Contains(fushi)}");
            }

            {
                // Battle damage from destroying an Attack Position monster: gain that damage (1200 − 0).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fushi = PlaceSettled(engine, p, FushiNoTori, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                celtic.UntilEndOfTurnAtk -= 400; // 1000 ATK: 200 battle damage
                var lpP = p.LifePoints;
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, fushi, celtic);
                Check("Fushi No Tori: gains exactly the battle damage it inflicted (200)",
                    reached && attacked && o.LifePoints == lpO - 200 && p.LifePoints == lpP + 200 &&
                    o.Graveyard.Contains(celtic),
                    $"attacked={attacked} lpO={lpO}->{o.LifePoints} lpP={lpP}->{p.LifePoints} atk={celtic.CurrentAtk}");
            }

            {
                // No battle damage (a Defense Position monster with DEF equal to its ATK): no gain.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fushi = PlaceSettled(engine, p, FushiNoTori, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Defense, true);
                var lpP = p.LifePoints;
                var attacked = AttackAndDrain(engine, p, fushi, o.MonstersOnField().First());
                Check("Fushi No Tori: no battle damage to the opponent → no Life Points gained",
                    reached && attacked && p.LifePoints == lpP, $"attacked={attacked} lpP={lpP}->{p.LifePoints}");
            }

            {
                // Attacked by a weaker monster on the opponent's turn: the damage it inflicts to the attacker's
                // controller counts too.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var reached = r0 && ReachOpponentBattle(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var fushi = PlaceSettled(engine, p, FushiNoTori, 0, BattlePosition.Attack, true);
                var ojama = PlaceSettled(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var lpP = p.LifePoints;
                var lpO = o.LifePoints;
                var attacked = engine.TryAttack(o, ojama, fushi);
                DrainCombat(engine);
                Check("Fushi No Tori: battle damage it inflicts as the attack target (1200) is also gained",
                    reached && attacked && o.LifePoints == lpO - 1200 && p.LifePoints == lpP + 1200,
                    $"reached={reached} attacked={attacked} lpO={lpO}->{o.LifePoints} lpP={lpP}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Great Long Nose ─────────────────────────────

        static void GreatLongNoseDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                engine.ToggleTribute(p, PlaceSettled(engine, p, Celtic, 0, BattlePosition.Attack, true));
                var nose = PutInHand(engine, p, GreatLongNose);
                var ns = engine.TryNormalSummon(p, nose, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, nose, null);
                var flagged = o.SkipNextBattlePhase;
                EndTurn(engine);
                var oppTurn = engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1;
                var canBp = engine.CanConductBattlePhase;
                var enter = engine.TryEnterBattlePhase(o);
                Check("Great Long Nose: 1900 battle damage → the opponent's next turn has no Battle Phase",
                    reached && ns && attacked && o.LifePoints == lpO - 1900 && flagged && oppTurn && !canBp && !enter &&
                    !o.SkipNextBattlePhase,
                    $"ns={ns} attacked={attacked} lp={lpO}->{o.LifePoints} flagged={flagged} oppTurn={oppTurn} canBp={canBp} enter={enter}");
                Check("Great Long Nose: returned to the hand during the End Phase",
                    p.Hand.Contains(nose) && !p.TryFindMonster(nose, out _), $"inHand={p.Hand.Contains(nose)}");
                EndTurn(engine);
                EndTurn(engine);
                var later = engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1 && engine.CanConductBattlePhase;
                Check("Great Long Nose: only that one Battle Phase is skipped", later,
                    $"turnO={engine.TurnPlayer == o} phase={engine.Phase} canBp={engine.CanConductBattlePhase}");
            }

            {
                // Inflicted during the opponent's turn (it is attacked by a weaker monster): their current Battle Phase
                // goes on, and the Battle Phase of their next turn is skipped.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var reached = r0 && ReachOpponentBattle(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var nose = PlaceSettled(engine, p, GreatLongNose, 0, BattlePosition.Attack, true);
                var ojama = PlaceSettled(engine, o, OjamaGreen, 0, BattlePosition.Attack, true);
                var celtic = PlaceSettled(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var attacked = engine.TryAttack(o, ojama, nose);
                DrainCombat(engine);
                var flagged = o.SkipNextBattlePhase;
                var stillBattle = engine.Phase == DuelPhase.Battle && engine.CanAttack(o, celtic);
                EndTurn(engine);
                var myTurn = engine.TurnPlayer == p && engine.CanConductBattlePhase;
                EndTurn(engine);
                var oppTurn = engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1;
                Check("Great Long Nose: damage on the opponent's turn skips the Battle Phase of their next turn",
                    reached && attacked && o.LifePoints == lpO - 1900 && flagged && stillBattle && myTurn && oppTurn &&
                    !engine.CanConductBattlePhase && !engine.TryEnterBattlePhase(o),
                    $"reached={reached} attacked={attacked} lp={lpO}->{o.LifePoints} flagged={flagged} stillBattle={stillBattle} myTurn={myTurn} oppTurn={oppTurn}");
            }

            {
                // No battle damage to the opponent → no skip.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var nose = PlaceSettled(engine, p, GreatLongNose, 0, BattlePosition.Attack, true);
                var wall = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Defense, true);
                var attacked = AttackAndDrain(engine, p, nose, wall);
                Check("Great Long Nose: no battle damage to the opponent → no skip",
                    reached && attacked && !o.SkipNextBattlePhase, $"attacked={attacked} flag={o.SkipNextBattlePhase}");
            }
        }

        // ───────────────────────────── Susa Soldier ─────────────────────────────

        static void SusaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var susa = PutInHand(engine, p, SusaSoldier);
                var ns = engine.TryNormalSummon(p, susa, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var lpO = o.LifePoints;
                var attacked = AttackAndDrain(engine, p, susa, null);
                Check("Susa Soldier: a 2000 direct attack inflicts 1000",
                    reached && ns && attacked && o.LifePoints == lpO - 1000,
                    $"ns={ns} attacked={attacked} lp={lpO}->{o.LifePoints}");
                EndTurn(engine);
                Check("Susa Soldier: returns to the hand during the End Phase",
                    p.Hand.Contains(susa) && !p.TryFindMonster(susa, out _), $"inHand={p.Hand.Contains(susa)}");
            }

            {
                // The damage its controller takes is not halved.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var susa = PlaceSettled(engine, p, SusaSoldier, 0, BattlePosition.Attack, true);
                var be = PlaceMonster(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
                var lpP = p.LifePoints;
                var attacked = AttackAndDrain(engine, p, susa, be);
                Check("Susa Soldier: the battle damage its controller takes is not halved (1000)",
                    reached && attacked && p.LifePoints == lpP - 1000, $"attacked={attacked} lpP={lpP}->{p.LifePoints}");
            }

            {
                // As the attack target: in Attack Position (2000 vs 1400 → 300) and in Defense Position (DEF 1600 vs
                // 0 ATK → 800).
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var reached = r0 && ReachOpponentBattle(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var susa = PlaceSettled(engine, p, SusaSoldier, 0, BattlePosition.Attack, true);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var lpO = o.LifePoints;
                var a1 = engine.TryAttack(o, celtic, susa);
                DrainCombat(engine);
                var afterAtk = o.LifePoints;
                susa.Position = BattlePosition.Defense;
                var ojama = PlaceSettled(engine, o, OjamaGreen, 1, BattlePosition.Attack, true);
                var a2 = engine.TryAttack(o, ojama, susa);
                DrainCombat(engine);
                Check("Susa Soldier: damage it inflicts as the attack target is halved (600 → 300, 1600 → 800)",
                    reached && a1 && a2 && afterAtk == lpO - 300 && o.LifePoints == afterAtk - 800 &&
                    p.TryFindMonster(susa, out _),
                    $"reached={reached} a1={a1} a2={a2} lp={lpO}->{afterAtk}->{o.LifePoints}");
            }

            foreach (var bonus in new[] { 199, 198 })
            {
                // Defense-hold gap: 1599 ATK vs DEF 1600 halves 1 to 0, so no LP change and no battle-damage event
                // (ocgcore raises no EVENT_BATTLE_DAMAGE for 0) and Robbin' Goblin does not make the opponent
                // discard. Control: 1598 ATK halves 2 to 1, which is battle damage, so Robbin' Goblin fires.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var reached = r0 && ReachOpponentBattle(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var susa = PlaceSettled(engine, p, SusaSoldier, 0, BattlePosition.Defense, true);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                celtic.AtkModifier = bonus;
                var gob = engine.CreateCardInstance(RobbinGoblin);
                gob.FaceUp = true;
                gob.SetThisTurn = false;
                p.SpellTrapZones[0].Occupant = gob;
                o.Hand.Clear();
                var h = PutInHand(engine, o, Celtic);
                var lpO = o.LifePoints;
                var atk = celtic.CurrentAtk;
                var a1 = engine.TryAttack(o, celtic, susa);
                DrainCombat(engine);
                var expected = (1600 - 1400 - bonus) / 2;
                Check($"Susa Soldier: a {1600 - 1400 - bonus}-point defense-hold gap halves to {expected}; " +
                      (expected == 0 ? "no battle-damage trigger fires" : "Robbin' Goblin fires"),
                    reached && a1 && atk == 1400 + bonus && o.LifePoints == lpO - expected &&
                    o.Hand.Contains(h) == (expected == 0),
                    $"reached={reached} a1={a1} atk={atk} lp={lpO}->{o.LifePoints} oppHand={o.Hand.Count}");
            }
        }

        // ───────────────────────────── Otohime ─────────────────────────────

        static void OtohimeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var id in new[] { Otohime, OtohimeAlt })
            {
                // Normal Summon: the human taps the target → it changes position.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var oto = PutInHand(engine, p, id);
                var ns = engine.TryNormalSummon(p, oto, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var offered = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(celtic);
                var picked = offered && engine.TrySelectEffectTarget(celtic);
                Check($"Otohime ({id}): Normal Summoned → target the opponent's face-up Attack monster → Defense",
                    reached && ns && offered && picked && celtic.Position == BattlePosition.Defense && celtic.FaceUp &&
                    !engine.IsAwaitingEffectTarget,
                    $"ns={ns} offered={offered} picked={picked} pos={celtic.Position}");
                EndTurn(engine);
                Check($"Otohime ({id}): returns to the hand during the End Phase",
                    p.Hand.Contains(oto) && !p.TryFindMonster(oto, out _), $"inHand={p.Hand.Contains(oto)}");
            }

            {
                // Optional: CANCEL changes nothing.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var oto = PutInHand(engine, p, Otohime);
                engine.TryNormalSummon(p, oto, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var offered = engine.IsAwaitingEffectTarget;
                engine.CancelEffectTargeting();
                Check("Otohime: \"You can\" — CANCEL leaves the monster in its position",
                    reached && offered && !engine.IsAwaitingEffectTarget && celtic.Position == BattlePosition.Attack,
                    $"offered={offered} pos={celtic.Position}");
            }

            {
                // No face-up monster the opponent controls (a face-down one is not a legal target): nothing is asked.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var setMon = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Defense, false);
                var oto = PutInHand(engine, p, Otohime);
                var ns = engine.TryNormalSummon(p, oto, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Otohime: no face-up monster to target → no pick, the face-down monster is untouched",
                    reached && ns && !engine.IsAwaitingEffectTarget && !setMon.FaceUp &&
                    setMon.Position == BattlePosition.Defense,
                    $"ns={ns} pending={engine.IsAwaitingEffectTarget} faceUp={setMon.FaceUp}");
            }

            {
                // Flip Summon.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Defense, true);
                var oto = PlaceSettled(engine, p, Otohime, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, oto);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var offered = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(celtic);
                var picked = offered && engine.TrySelectEffectTarget(celtic);
                Check("Otohime: Flip Summoned → face-up Defense target → Attack Position",
                    reached && flipped && offered && picked && celtic.Position == BattlePosition.Attack,
                    $"flipped={flipped} offered={offered} picked={picked} pos={celtic.Position}");
            }

            {
                // Flipped face-up by battle (attacked face-down on the opponent's turn): it activates after damage
                // calculation; the battle then finishes. It returns to the hand in that turn's End Phase.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var reached = r0 && ReachOpponentBattle(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var oto = PlaceSettled(engine, p, Otohime, 0, BattlePosition.Defense, false);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var attacked = engine.TryAttack(o, celtic, oto);
                DriveUntilPick(engine);
                var offered = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(celtic);
                var picked = offered && engine.TrySelectEffectTarget(celtic);
                DrainCombat(engine);
                Check("Otohime: flipped by an attack → changes the attacker's position; the battle finishes",
                    reached && attacked && offered && picked && celtic.Position == BattlePosition.Defense &&
                    p.Graveyard.Contains(oto) && !engine.IsAwaitingEffectTarget && !engine.HasDeclaredAttack,
                    $"reached={reached} attacked={attacked} offered={offered} picked={picked} pos={celtic.Position} otoGy={p.Graveyard.Contains(oto)}");
            }

            {
                // Flipped face-up by a card effect: the trigger is queued until play is idle, then asked.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var oto = PlaceSettled(engine, p, Otohime, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var changed = engine.ApplyEffectPositionChange(oto, BattlePosition.Attack, faceUp: true);
                engine.NotifyPublic();
                var offered = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(celtic);
                var picked = offered && engine.TrySelectEffectTarget(celtic);
                Check("Otohime: flipped face-up by a card effect → its trigger, after that effect",
                    reached && changed && offered && picked && celtic.Position == BattlePosition.Defense,
                    $"changed={changed} offered={offered} picked={picked} pos={celtic.Position}");
                EndTurn(engine);
                Check("Otohime: flipped face-up by an effect this turn → returns to the hand during the End Phase",
                    p.Hand.Contains(oto), $"inHand={p.Hand.Contains(oto)}");
            }

            {
                // The AI's Otohime: it uses the effect on the player's face-up monster.
                var engine = OpponentMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceSettled(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var oto = PutInHand(engine, o, Otohime);
                var ns = engine.TryNormalSummon(o, oto, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Otohime: the AI's Normal Summon changes the player's face-up monster's position (no prompt)",
                    reached && ns && celtic.Position == BattlePosition.Defense && !engine.IsAwaitingEffectTarget,
                    $"reached={reached} ns={ns} pos={celtic.Position} pending={engine.IsAwaitingEffectTarget}");
            }
        }

        // ───────────────────────────── Maharaghi ─────────────────────────────

        static void MaharaghiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human taps the top card → it goes to the bottom, and the draw takes the next one.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var maha = PutInHand(engine, p, Maharaghi);
                var ns = engine.TryNormalSummon(p, maha, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var registered = p.LookAtTopCardAtNextDrawPhase;
                EndTurn(engine);
                var returned = p.Hand.Contains(maha);
                p.Deck.Insert(0, Celtic);
                p.Deck.Insert(0, PotOfGreed);
                var hand0 = p.Hand.Count;
                EndTurn(engine);
                var held = engine.TurnPlayer == p && engine.Phase == DuelPhase.Draw && engine.IsAwaitingEffectTarget &&
                           engine.PendingActivation.LegalTargets.Count == 1 &&
                           engine.PendingActivation.LegalTargets[0].CardId == PotOfGreed && p.Hand.Count == hand0;
                var picked = held && engine.TrySelectEffectTarget(engine.PendingActivation.LegalTargets[0]);
                Check("Maharaghi: Normal Summoned → next Draw Phase holds on the top card before the draw",
                    reached && ns && registered && returned && held,
                    $"ns={ns} registered={registered} returned={returned} turnP={engine.TurnPlayer == p} phase={engine.Phase} pending={engine.IsAwaitingEffectTarget}");
                Check("Maharaghi: top card to the bottom → the next card is drawn; the turn goes on to Main Phase 1",
                    picked && p.Hand.Count == hand0 + 1 && p.Hand[p.Hand.Count - 1].CardId == Celtic &&
                    p.Deck[p.Deck.Count - 1] == PotOfGreed && engine.Phase == DuelPhase.Main1 &&
                    !p.LookAtTopCardAtNextDrawPhase && !engine.IsAwaitingEffectTarget,
                    $"picked={picked} hand={hand0}->{p.Hand.Count} drew={p.Hand.LastOrDefault()?.Name} bottom={p.Deck.LastOrDefault()} phase={engine.Phase}");
            }

            {
                // CANCEL leaves it on top: it is the card drawn.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var maha = PutInHand(engine, p, Maharaghi);
                engine.TryNormalSummon(p, maha, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                EndTurn(engine);
                p.Deck.Insert(0, Celtic);
                p.Deck.Insert(0, PotOfGreed);
                EndTurn(engine);
                var held = engine.Phase == DuelPhase.Draw && engine.IsAwaitingEffectTarget;
                engine.CancelEffectTargeting();
                Check("Maharaghi: CANCEL leaves the top card on top — it is drawn",
                    reached && held && p.Hand.LastOrDefault()?.CardId == PotOfGreed && p.Deck[0] == Celtic &&
                    engine.Phase == DuelPhase.Main1,
                    $"held={held} drew={p.Hand.LastOrDefault()?.Name} top={p.Deck.FirstOrDefault()} phase={engine.Phase}");
            }

            {
                // The look is a sentence template: its prompt names the card that set it up, not a fixed card name.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var src = engine.CreateCardInstance(Celtic);
                SpiritMonstersRuntime.ScheduleTopCardLook(engine, p, src);
                EndTurn(engine);
                EndTurn(engine);
                var held = engine.Phase == DuelPhase.Draw && engine.IsAwaitingEffectTarget;
                var prompt = held ? engine.PendingActivation.Prompt ?? "" : "";
                engine.CancelEffectTargeting();
                Check("Top-card look: the prompt names its source card",
                    reached && held && prompt.StartsWith(src.Name + ":") && !prompt.Contains("Maharaghi") &&
                    engine.Phase == DuelPhase.Main1 && p.LookAtTopCardSource == null,
                    $"held={held} prompt='{prompt}' phase={engine.Phase}");
            }

            {
                // The Draw Phase is skipped by a card effect: no look (EVENT_PREDRAW never happens); it ends.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var maha = PutInHand(engine, p, Maharaghi);
                engine.TryNormalSummon(p, maha, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                EndTurn(engine);
                p.SkipNextDrawPhase = true;
                EndTurn(engine);
                Check("Maharaghi: a skipped Draw Phase ends the look with no prompt",
                    reached && engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1 &&
                    !engine.IsAwaitingEffectTarget && !p.LookAtTopCardAtNextDrawPhase,
                    $"phase={engine.Phase} pending={engine.IsAwaitingEffectTarget} flag={p.LookAtTopCardAtNextDrawPhase}");
            }

            {
                // Flipped face-up by battle on the opponent's turn: the look is registered for its controller's next
                // Draw Phase.
                var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
                var reached = r0 && ReachOpponentBattle(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var maha = PlaceSettled(engine, p, Maharaghi, 0, BattlePosition.Defense, false);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var attacked = engine.TryAttack(o, celtic, maha);
                DrainCombat(engine);
                Check("Maharaghi: flipped face-up by an attack → the look is registered",
                    reached && attacked && maha.FaceUp && p.LookAtTopCardAtNextDrawPhase &&
                    !o.LookAtTopCardAtNextDrawPhase,
                    $"reached={reached} attacked={attacked} faceUp={maha.FaceUp} flag={p.LookAtTopCardAtNextDrawPhase}");
            }

            foreach (var (top, keep) in new[] { (Celtic, true), (BlueEyes, false) })
            {
                // The AI decides at once: a monster it can summon stays on top, a Level 8 with no Tributes goes down.
                var engine = OpponentMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                var maha = PutInHand(engine, o, Maharaghi);
                var ns = engine.TryNormalSummon(o, maha, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                EndTurn(engine);
                o.Deck.Insert(0, OjamaGreen);
                o.Deck.Insert(0, top);
                var oppMonsters = o.MonsterCount;
                EndTurn(engine);
                var drew = o.Hand.LastOrDefault()?.CardId;
                Check($"Maharaghi (AI): {(keep ? "keeps a summonable monster on top" : "puts an unsummonable Level 8 on the bottom")}",
                    reached && ns && oppMonsters == 0 && engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1 &&
                    !engine.IsAwaitingEffectTarget &&
                    (keep ? drew == top : drew == OjamaGreen && o.Deck[o.Deck.Count - 1] == top),
                    $"ns={ns} monsters={oppMonsters} drew={drew} bottom={o.Deck.LastOrDefault()} phase={engine.Phase}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            CompiledCardProgram Syn(int id, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id, name = "Near miss " + id, type = "Effect Monster", frameType = "effect",
                    race = "Fiend", attribute = "DARK", level = 4, desc = desc
                });

            var twice = Syn(90600001, "This card can attack all monsters your opponent controls twice each.");
            Check("Near miss: \"twice each\" is not the attack-all marker",
                twice == null || !Has(twice, c => c.Action == EffectActionKind.AttackAllOpponentMonstersOnceEach),
                Describe(twice));

            var diffusion = CardTextEffectCompiler.Compile(db.Get(DiffusionWaveMotion));
            Check("Near miss: Diffusion Wave-Motion (\"must attack all monsters\") does not get the attack-all marker",
                diffusion == null ||
                !Has(diffusion, c => c.Action == EffectActionKind.AttackAllOpponentMonstersOnceEach),
                Describe(diffusion));

            var ooguchi = CardTextEffectCompiler.Compile(db.Get(Ooguchi));
            Check("Near miss: Ooguchi (\"may attack … directly\") can still attack monsters",
                ooguchi != null && Has(ooguchi, c => c.Action == EffectActionKind.CanAttackDirectly) &&
                !Has(ooguchi, c => c.Action == EffectActionKind.CannotSelectMonsterAsAttackTarget),
                Describe(ooguchi));

            var fixedGain = Syn(90600002,
                "When this card inflicts Battle Damage to your opponent's Life Points, increase your Life Points by 500.");
            Check("Near miss: a fixed LP gain on battle damage is not the battle-damage-amount gain",
                fixedGain == null || !Has(fixedGain, c => c.AmountIsEquippedBattleDamage),
                Describe(fixedGain));

            var noseDraw = Syn(90600003,
                "If this card inflicts Battle Damage to your opponent's Life Points, your opponent skips their next " +
                "Draw Phase.");
            Check("Near miss: \"skips their next Draw Phase\" is not the Battle Phase skip",
                noseDraw == null || !Has(noseDraw, c => c.Action == EffectActionKind.OpponentSkipsNextBattlePhase),
                Describe(noseDraw));
            var yata = CardTextEffectCompiler.Compile(db.Get(YataGarasu));
            Check("Yata-Garasu keeps its skip-Draw clause, no Battle Phase skip",
                yata != null && yata.FullyCompiled &&
                Has(yata, c => c.Action == EffectActionKind.SkipOpponentNextDrawPhase) &&
                !Has(yata, c => c.Action == EffectActionKind.OpponentSkipsNextBattlePhase),
                Describe(yata));

            var doubled = Syn(90600004, "The Battle Damage this card inflicts to your opponent's Life Points is doubled.");
            Check("Near miss: \"doubled\" is not the halving marker",
                doubled == null || !Has(doubled, c => c.Action == EffectActionKind.HalveBattleDamageThisInflictsToOpponent),
                Describe(doubled));
            var shadowknight = CardTextEffectCompiler.Compile(db.Get(ShadowknightArchfiend));
            // ArchfiendRiders batch: Shadowknight Archfiend's "… to your opponent is halved" now compiles to the same marker
            // (its own wording atom); the guard pins that shape.
            Check("Shadowknight Archfiend (\"… to your opponent is halved\", ArchfiendRiders batch) compiles the halving marker",
                shadowknight != null && shadowknight.FullyCompiled &&
                Has(shadowknight, c => c.Action == EffectActionKind.HalveBattleDamageThisInflictsToOpponent &&
                                       c.Timing == EffectTiming.ContinuousWhileFaceUp),
                Describe(shadowknight));

            var nsOnly = Syn(90600005,
                "When this card is Normal Summoned: You can target 1 face-up monster your opponent controls; change " +
                "that target's battle position.");
            Check("Near miss: a Normal-Summon-only trigger does not fire on flips (not the Otohime atom)",
                nsOnly == null || !Has(nsOnly, c => c.AlsoWhenFlippedFaceUp),
                Describe(nsOnly));
            var otohimeRider = Syn(90600006,
                "When this card is Normal Summoned or flipped face-up: You can target 1 face-up monster your opponent " +
                "controls; change that target's battle position, then draw 1 card.");
            Check("Near miss: Otohime's sentence with a rider does not compile (fail closed)",
                otohimeRider == null || !otohimeRider.FullyCompiled,
                Describe(otohimeRider));

            var lookThree = Syn(90600007,
                "If this card is Normal Summoned or flipped face-up, look at 3 cards on the top of your Deck during " +
                "your next Draw Phase (before you draw), and return the cards to the top or the bottom of your Deck.");
            Check("Near miss: a 3-card look is not Maharaghi's atom",
                lookThree == null ||
                !Has(lookThree, c => c.Action == EffectActionKind.LookAtTopCardAtNextDrawPhaseTopOrBottom),
                Describe(lookThree));
        }
    }
}
