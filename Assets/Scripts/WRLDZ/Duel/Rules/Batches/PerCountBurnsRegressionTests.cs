using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// PerCountBurns batch (v141 curriculum): burns that count at resolution — Princess of Tsurugi (Flip,
    /// x the opponent's Spell/Trap Zone cards), D.D. Dynamite (x their banished cards), Cemetary Bomb (x their GY),
    /// Thunder Crash (destroy all your monsters, x the monsters actually destroyed) and Wave-Motion Cannon
    /// (face-up Continuous Spell ignition, x your Standby Phases since it was activated). Compile shapes,
    /// mini-duels through the real engine entry points and fail-closed near misses.
    /// Runtime: <see cref="PerCountBurnsRuntime"/>.
    /// </summary>
    public static class PerCountBurnsRegressionTests
    {
        const int Princess = 51371017;
        const int DdDynamite = 8628798;
        const int WaveMotion = 38992735;
        const int CemetaryBomb = 51394546;
        const int ThunderCrash = 69196160;

        const int Celtic = 91152256;      // Level 4 EARTH 1400/1200
        const int Kuriboh = 40640057;     // Level 1 DARK 300/200
        const int Kayest = 9633505;       // Guardian Kay'est: unaffected by Spell effects
        const int Waboku = 12607053;
        const int Mountain = 50913601;    // Field Spell
        const int MagicJammer = 77414722; // Counter Trap: "When a Spell Card is activated: … negate the activation"
        const int Healer = 2130625;       // Numinous Healer: opens a "you take damage" window
        const int PotOfGreed = 55144522;
        const int BigWaveSmallWave = 51562916;
        const int IslandTurtle = 4042268;   // WATER Normal Lv4 — the card's own Attribute
        const int SkullMariner = 5265750;    // WATER Normal Lv4 — a second distinct copy
        const int Hyosube = 2118022;        // WATER Normal Lv4, a third distinct copy
        const int CelticGuardian = 91152256; // EARTH — never destroyed by this card

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            PrincessDuels(db, pDeck, aDeck, Check);
            DdDynamiteDuels(db, pDeck, aDeck, Check);
            CemetaryBombDuels(db, pDeck, aDeck, Check);
            ThunderCrashDuels(db, pDeck, aDeck, Check);
            BigWaveSmallWaveDuels(db, pDeck, aDeck, Check);
            WaveMotionDuels(db, pDeck, aDeck, Check);
            WaveMotionChainDuels(db, pDeck, aDeck, Check);
        }

        delegate void Chk(string name, bool ok, string detail = "");

        static string Left(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        // ── Compile shapes ──

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var princess = CardTextEffectCompiler.Compile(db.Get(Princess));
            Check("PerCountBurns: Princess of Tsurugi FullyCompiled Flip burn 500 x opponent's Spell/Trap Zone cards",
                princess != null && princess.FullyCompiled && princess.ClauseList.Count == 1 &&
                princess.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Flip &&
                                                c.Action == EffectActionKind.InflictDamageToOpponent &&
                                                c.Amount == 500 &&
                                                c.CountSource == PerCountBurnsRuntime.OppSpellTrapCards &&
                                                string.IsNullOrEmpty(c.ActivationCondition)),
                Left(princess));

            var dd = CardTextEffectCompiler.Compile(db.Get(DdDynamite));
            Check("PerCountBurns: D.D. Dynamite FullyCompiled burn 300 x their banished cards, needs one to activate",
                dd != null && dd.FullyCompiled && dd.ClauseList.Count == 1 &&
                dd.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                          c.Action == EffectActionKind.InflictDamageToOpponent &&
                                          c.Amount == 300 &&
                                          c.CountSource == PerCountBurnsRuntime.OppBanishedCards &&
                                          c.ActivationCondition == PerCountBurnsRuntime.OppBanishedAtLeast &&
                                          c.ConditionAmount == 1 &&
                                          !c.ScaleAmountByOpponentHandCards && !c.ScaleAmountByOpponentMonsterCount &&
                                          !c.ScaleAmountByOpponentHandAndControlledCards),
                Left(dd));

            var bomb = CardTextEffectCompiler.Compile(db.Get(CemetaryBomb));
            Check("PerCountBurns: Cemetary Bomb FullyCompiled burn 100 x cards in their GY, needs one to activate",
                bomb != null && bomb.FullyCompiled && bomb.ClauseList.Count == 1 &&
                bomb.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                            c.Action == EffectActionKind.InflictDamageToOpponent &&
                                            c.Amount == 100 &&
                                            c.CountSource == PerCountBurnsRuntime.OppGyCards &&
                                            c.ActivationCondition == PerCountBurnsRuntime.OppGyAtLeast),
                Left(bomb));

            var crash = CardTextEffectCompiler.Compile(db.Get(ThunderCrash));
            var crashOk = crash != null && crash.FullyCompiled && crash.ClauseList.Count == 2;
            if (crashOk)
            {
                var d = crash.ClauseList[0];
                var b = crash.ClauseList[1];
                crashOk = d != null && d.Timing == EffectTiming.Activate && d.Action == EffectActionKind.Destroy &&
                          d.Zone == EffectZoneFilter.FieldMonsters && d.Side == EffectSide.Controller &&
                          !d.RequiresTargetChoice &&
                          d.ActivationCondition == PerCountBurnsRuntime.YouControlMonstersAtLeast &&
                          b != null && b.Timing == EffectTiming.Activate &&
                          b.Action == EffectActionKind.InflictDamageToOpponent && b.Amount == 300 &&
                          b.CountSource == PerCountBurnsRuntime.PreviousClauseHits && b.RequiresPreviousClauseHit;
            }

            Check("PerCountBurns: Thunder Crash FullyCompiled destroy all you control, then 300 x monsters destroyed",
                crashOk, Left(crash));

            var wave = CardTextEffectCompiler.Compile(db.Get(WaveMotion));
            Check("PerCountBurns: Wave-Motion Cannon FullyCompiled face-up ignition, send self (cost), 1000 x turn counter",
                wave != null && wave.FullyCompiled && wave.ClauseList.Count == 1 &&
                !wave.HasTiming(EffectTiming.Activate) &&
                wave.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.FaceUpFieldIgnition &&
                                            c.Action == EffectActionKind.InflictDamageToOpponent &&
                                            c.Amount == 1000 && c.RequiresSendThisToGy &&
                                            c.CountSource == PerCountBurnsRuntime.ThisCardTurnCounter),
                Left(wave));
            Check("PerCountBurns: all five are Implemented",
                new[] { Princess, DdDynamite, CemetaryBomb, ThunderCrash, WaveMotion }.All(id =>
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented));
        }

        // ── Near misses: look-alike sentences must not compile to these shapes ──

        static CardDef Synthetic(CardDef like, string desc)
        {
            return new CardDef
            {
                id = 0,
                name = "PerCountBurns near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };
        }

        static bool CompilesToCount(CardDef def, string countSource)
        {
            var prog = CardTextEffectCompiler.Compile(def);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null && c.CountSource == countSource);
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var princess = db.Get(Princess);
            Check("PerCountBurns near miss: a longer Flip sentence (… then destroy them) stays uncompiled",
                !CompilesToCount(Synthetic(princess,
                    "FLIP: Inflict 500 damage to your opponent for each Spell and Trap Card on your opponent's side " +
                    "of the field, then destroy them."), PerCountBurnsRuntime.OppSpellTrapCards));
            Check("PerCountBurns near miss: \"on your side of the field\" is not the opponent's count",
                !CompilesToCount(Synthetic(princess,
                    "FLIP: Inflict 500 damage to your opponent for each Spell and Trap Card on your side of the field."),
                    PerCountBurnsRuntime.OppSpellTrapCards));

            var dd = db.Get(DdDynamite);
            Check("PerCountBurns near miss: \"removed from play monsters\" is not D.D. Dynamite",
                !CompilesToCount(Synthetic(dd,
                    "Inflict 300 damage to your opponent for each of their removed from play monsters."),
                    PerCountBurnsRuntime.OppBanishedCards));

            var bomb = db.Get(CemetaryBomb);
            Check("PerCountBurns near miss: \"each card in your Graveyard\" is not Cemetary Bomb",
                !CompilesToCount(Synthetic(bomb,
                    "Inflict 100 damage to your opponent for each card in your Graveyard."),
                    PerCountBurnsRuntime.OppGyCards));
            Check("PerCountBurns near miss: Cemetary Bomb's sentence with a rider stays uncompiled",
                !CompilesToCount(Synthetic(bomb,
                    "Inflict 100 damage to your opponent for each card in their Graveyard. Then, banish those cards."),
                    PerCountBurnsRuntime.OppGyCards));

            var crash = db.Get(ThunderCrash);
            Check("PerCountBurns near miss: \"monsters your opponent controls\" is not Thunder Crash",
                !CompilesToCount(Synthetic(crash,
                    "Destroy all monsters your opponent controls, and if you do, inflict 300 damage to your opponent " +
                    "for each monster destroyed."), PerCountBurnsRuntime.PreviousClauseHits));
            var crashRider = CardTextEffectCompiler.Compile(Synthetic(crash,
                "Destroy all monsters you control, and if you do, inflict 300 damage to your opponent for each " +
                "monster destroyed. You cannot conduct your Battle Phase this turn."));
            Check("PerCountBurns near miss: Thunder Crash's sentence plus an extra sentence is not FullyCompiled",
                crashRider == null || !crashRider.FullyCompiled, Left(crashRider));

            // A sentence that only starts like an atom (no period right after the counted thing) must not
            // compile to the count with its tail silently dropped.
            var prefixes = new (CardDef like, string text, string count)[]
            {
                (bomb, "Inflict 100 damage to your opponent for each card in their GY x 2.",
                    PerCountBurnsRuntime.OppGyCards),
                (bomb, "Inflict 100 damage to your opponent for each card in their Graveyard and hand.",
                    PerCountBurnsRuntime.OppGyCards),
                (dd, "Inflict 300 damage to your opponent for each of their banished cards and each card in their hand.",
                    PerCountBurnsRuntime.OppBanishedCards),
                (crash, "Destroy all monsters you control, and if you do, inflict 300 damage to your opponent for each " +
                        "monster destroyed by battle this turn.", PerCountBurnsRuntime.PreviousClauseHits),
                (bomb, "If your opponent controls a monster: Inflict 100 damage to your opponent for each card in " +
                       "their Graveyard.", PerCountBurnsRuntime.OppGyCards),
                (princess, "FLIP: Inflict 500 damage to your opponent for each Spell and Trap Card on your opponent's " +
                           "side of the field x 2.", PerCountBurnsRuntime.OppSpellTrapCards),
            };
            foreach (var (like, text, count) in prefixes)
                Check($"PerCountBurns near miss: a sentence that only starts like the atom stays uncompiled: {text}",
                    !CompilesToCount(Synthetic(like, text), count),
                    Left(CardTextEffectCompiler.Compile(Synthetic(like, text))));

            var wave = db.Get(WaveMotion);
            Check("PerCountBurns near miss: \"your opponent's Standby Phases\" is not Wave-Motion Cannon",
                !CompilesToCount(Synthetic(wave,
                    "During your Main Phase: You can send this face-up card to the GY; inflict 1000 damage to your " +
                    "opponent for each of your opponent's Standby Phases that have passed since this card was activated."),
                    PerCountBurnsRuntime.ThisCardTurnCounter));
        }

        // ── Princess of Tsurugi ──

        static void PrincessDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSetTrap(engine, o, Waboku, 0);
                PlaceSetTrap(engine, o, Waboku, 1);
                var field = engine.CreateCardInstance(Mountain);
                field.FaceUp = true;
                o.FieldSpellZone.Occupant = field;
                PlaceSetTrap(engine, p, Waboku, 0); // your own Spell/Trap does not count
                var princess = PlaceMonster(engine, p, Princess, 2, BattlePosition.Defense, true);
                var lp = o.LifePoints;
                var myLp = p.LifePoints;
                MonsterEffects.OnFlipSummoned(engine, p, princess);
                DrainCombat(engine);
                Check("Princess of Tsurugi: Flip inflicts 500 x 3 (2 Set cards + the Field Spell they control)",
                    o.LifePoints == lp - 1500 && p.LifePoints == myLp,
                    $"lp={o.LifePoints} (was {lp}) mine={p.LifePoints} (was {myLp})");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSetTrap(engine, p, Waboku, 0);
                var princess = PlaceMonster(engine, p, Princess, 2, BattlePosition.Defense, true);
                var lp = o.LifePoints;
                MonsterEffects.OnFlipSummoned(engine, p, princess);
                DrainCombat(engine);
                Check("Princess of Tsurugi: no Spell/Trap on the opponent's side → no damage (no flat 500)",
                    o.LifePoints == lp, $"lp={o.LifePoints} (was {lp})");
            }
        }

        // ── D.D. Dynamite ──

        static void DdDynamiteDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Banished.Clear();
                p.Banished.Clear();
                for (var i = 0; i < 2; i++) o.Banished.Add(engine.CreateCardInstance(Celtic));
                var faceDown = engine.CreateCardInstance(Kuriboh);
                faceDown.FaceUp = false;
                o.Banished.Add(faceDown);
                for (var i = 0; i < 2; i++) p.Banished.Add(engine.CreateCardInstance(Celtic)); // yours: not counted
                var trap = PlaceSetTrap(engine, p, DdDynamite, 0);
                var lp = o.LifePoints;
                var cast = engine.TryActivateSpellTrap(p, trap, false);
                DrainCombat(engine);
                Check("D.D. Dynamite: 300 x 3 of their banished cards (face-down too; yours do not count)",
                    cast && o.LifePoints == lp - 900 && p.Graveyard.Contains(trap),
                    $"cast={cast} lp={o.LifePoints} (was {lp})");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Banished.Clear();
                p.Banished.Clear();
                p.Banished.Add(engine.CreateCardInstance(Celtic));
                var trap = PlaceSetTrap(engine, p, DdDynamite, 0);
                Check("D.D. Dynamite: cannot be activated while the opponent has no banished cards",
                    !engine.CanActivateSpellTrap(p, trap, false));
            }
        }

        // ── Cemetary Bomb ──

        static void CemetaryBombDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Graveyard.Clear();
                p.Graveyard.Clear();
                for (var i = 0; i < 4; i++) o.Graveyard.Add(engine.CreateCardInstance(Celtic));
                for (var i = 0; i < 3; i++) p.Graveyard.Add(engine.CreateCardInstance(Celtic)); // yours: not counted
                var trap = PlaceSetTrap(engine, p, CemetaryBomb, 0);
                var lp = o.LifePoints;
                var cast = engine.TryActivateSpellTrap(p, trap, false);
                DrainCombat(engine);
                Check("Cemetary Bomb: 100 x 4 cards in their GY (yours do not count)",
                    cast && o.LifePoints == lp - 400, $"cast={cast} lp={o.LifePoints} (was {lp})");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Graveyard.Clear();
                p.Graveyard.Clear();
                p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                var trap = PlaceSetTrap(engine, p, CemetaryBomb, 0);
                Check("Cemetary Bomb: cannot be activated while the opponent's GY is empty",
                    !engine.CanActivateSpellTrap(p, trap, false));
            }
        }

        // ── Thunder Crash ──

        static void ThunderCrashDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, p, Kuriboh, 1, BattlePosition.Defense, false); // face-down counts
                var theirs = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var spell = PutInHand(engine, p, ThunderCrash);
                var lp = o.LifePoints;
                var myLp = p.LifePoints;
                var cast = engine.TryActivateSpellTrap(p, spell, true);
                DrainCombat(engine);
                Check("Thunder Crash: destroys all monsters you control (face-down too) and inflicts 300 x 2",
                    cast && p.MonsterCount == 0 && p.Graveyard.Contains(a) && p.Graveyard.Contains(b) &&
                    o.LifePoints == lp - 600 && p.LifePoints == myLp,
                    $"cast={cast} mine={p.MonsterCount} lp={o.LifePoints} (was {lp})");
                Check("Thunder Crash: the opponent's monsters are untouched",
                    o.TryFindMonster(theirs, out _), $"oppMonsters={o.MonsterCount}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var guard = PlaceMonster(engine, p, Kayest, 2, BattlePosition.Attack, true);
                var spell = PutInHand(engine, p, ThunderCrash);
                var lp = o.LifePoints;
                var cast = engine.TryActivateSpellTrap(p, spell, true);
                DrainCombat(engine);
                Check("Thunder Crash: counts only the monsters actually destroyed (Spell-unaffected Kay'est stays)",
                    cast && p.TryFindMonster(guard, out _) && p.MonsterCount == 1 && o.LifePoints == lp - 600,
                    $"cast={cast} mine={p.MonsterCount} lp={o.LifePoints} (was {lp})");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var spell = PutInHand(engine, p, ThunderCrash);
                Check("Thunder Crash: cannot be activated while you control no monster",
                    !engine.CanActivateSpellTrap(p, spell, true));
            }
        }

        static void BigWaveSmallWaveDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Two WATER monsters are destroyed, then exactly two WATER monsters from the hand are Special Summoned.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var mine1 = PlaceMonster(engine, p, IslandTurtle, 0, BattlePosition.Attack, true);
                var mine2 = PlaceMonster(engine, p, SkullMariner, 1, BattlePosition.Attack, true);
                var mineEarth = PlaceMonster(engine, p, CelticGuardian, 2, BattlePosition.Attack, true);
                var hand1 = PutInHand(engine, p, Hyosube);
                var hand2 = PutInHand(engine, p, IslandTurtle);
                var hand3 = PutInHand(engine, p, SkullMariner);
                var handEarth = PutInHand(engine, p, CelticGuardian);
                engine.NotifyPublic();
                var cast = engine.TryActivateSpellTrap(p, PutInHand(engine, p, BigWaveSmallWave), true);
                DrainCombat(engine);
                Check("Big Wave Small Wave: destroys every face-up WATER you control and Special Summons that many " +
                      "WATER monsters from the hand",
                    cast && p.Graveyard.Contains(mine1) && p.Graveyard.Contains(mine2) &&
                    p.TryFindMonster(mineEarth, out _) &&
                    (p.TryFindMonster(hand1, out _) || p.TryFindMonster(hand2, out _) ||
                     p.TryFindMonster(hand3, out _)) &&
                    p.Hand.Contains(handEarth) && p.MonsterCount <= 3,
                    $"cast={cast} handWater={p.Hand.Count(c => c?.Def != null && c.Def.attribute == "WATER")} " +
                    $"earthOn={p.TryFindMonster(mineEarth, out _)} earthHand={p.Hand.Contains(handEarth)} " +
                    $"monsters={p.MonsterCount}");
            }

            // Only one WATER monster is destroyed, so only one WATER monster can come from the hand.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var mine = PlaceMonster(engine, p, IslandTurtle, 0, BattlePosition.Attack, true);
                var hand1 = PutInHand(engine, p, Hyosube);
                var hand2 = PutInHand(engine, p, SkullMariner);
                engine.NotifyPublic();
                var cast = engine.TryActivateSpellTrap(p, PutInHand(engine, p, BigWaveSmallWave), true);
                DrainCombat(engine);
                Check("Big Wave Small Wave: the Special Summon is capped by the number actually destroyed",
                    cast && p.Graveyard.Contains(mine) &&
                    ((p.TryFindMonster(hand1, out _) && !p.TryFindMonster(hand2, out _) && p.Hand.Contains(hand2)) ||
                     (!p.TryFindMonster(hand1, out _) && p.TryFindMonster(hand2, out _) && p.Hand.Contains(hand1))),
                    $"cast={cast} gy={p.Graveyard.Contains(mine)} h1={p.TryFindMonster(hand1, out _)} " +
                    $"h2={p.TryFindMonster(hand2, out _)}");
            }

            // No face-up WATER monster you control: the Spell cannot be activated (Lua s.target chk).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                engine.NotifyPublic();
                Check("Big Wave Small Wave: cannot be activated while you control no face-up WATER monster",
                    !engine.CanActivateSpellTrap(p, PutInHand(engine, p, BigWaveSmallWave), true));
            }

            // Destroyed WATER monsters that survive (unaffected by Spell effects) are not counted.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var guard = PlaceMonster(engine, p, Kayest, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, IslandTurtle, 1, BattlePosition.Attack, true);
                var hand1 = PutInHand(engine, p, Hyosube);
                var hand2 = PutInHand(engine, p, SkullMariner);
                engine.NotifyPublic();
                var cast = engine.TryActivateSpellTrap(p, PutInHand(engine, p, BigWaveSmallWave), true);
                DrainCombat(engine);
                Check("Big Wave Small Wave: counts only the monsters actually destroyed (Spell-unaffected Kay'est stays)",
                    cast && p.TryFindMonster(guard, out _) && p.Graveyard.Contains(mine) &&
                    ((p.TryFindMonster(hand1, out _) && !p.TryFindMonster(hand2, out _)) ||
                     (!p.TryFindMonster(hand1, out _) && p.TryFindMonster(hand2, out _))),
                    $"cast={cast} guardOn={p.TryFindMonster(guard, out _)} mineGy={p.Graveyard.Contains(mine)}");
            }
        }

        // ── Wave-Motion Cannon ──

        /// <summary>End turns until it is the player's Main Phase 1 of a later turn than <paramref name="afterTurn"/>.</summary>
        static bool AdvanceToPlayerMain(DuelEngine engine, int afterTurn)
        {
            bool There() => engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1 &&
                            engine.TurnNumber > afterTurn;

            for (var t = 0; t < 12 && !There(); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (There()) break;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return There();
        }

        static CardInstance ActivateFromHand(DuelEngine engine, DuelistState who, int id, out bool cast)
        {
            var card = PutInHand(engine, who, id);
            cast = engine.TryActivateSpellTrap(who, card, true);
            DrainCombat(engine);
            return card;
        }

        static bool RunAiTurn(DuelEngine e, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
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

        static void WaveMotionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // One of your Standby Phases → 1000; the opponent's Standby does not count; Main Phase only.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var startTurn = engine.TurnNumber;
                var wave = ActivateFromHand(engine, p, WaveMotion, out var cast);
                var onField = p.TryFindSpellTrap(wave, out _) && wave.FaceUp;
                Check("Wave-Motion Cannon: activates from the hand and stays face-up with its counter at 0",
                    cast && onField && wave.TurnCounter == 0,
                    $"cast={cast} onField={onField} counter={wave.TurnCounter}");
                Check("Wave-Motion Cannon: its ignition cannot be used before any of your Standby Phases passed",
                    !engine.CanActivateSpellTrap(p, wave, false));

                var oppMain = ReachOpponentMain(engine);
                Check("Wave-Motion Cannon: the opponent's Standby Phase does not count",
                    oppMain && wave.TurnCounter == 0, $"oppMain={oppMain} counter={wave.TurnCounter}");
                wave.TurnCounter = 1;
                Check("Wave-Motion Cannon: its ignition cannot be activated during the opponent's turn",
                    !engine.CanActivateSpellTrap(p, wave, false));
                wave.TurnCounter = 0;

                var mine = AdvanceToPlayerMain(engine, startTurn);
                Check("Wave-Motion Cannon: your next Standby Phase counts 1",
                    mine && wave.TurnCounter == 1 && p.TryFindSpellTrap(wave, out _),
                    $"mine={mine} counter={wave.TurnCounter} turn={engine.TurnNumber}");

                var bp = engine.TryEnterBattlePhase(p);
                DrainCombat(engine);
                Check("Wave-Motion Cannon: not in the Battle Phase",
                    bp && engine.Phase == DuelPhase.Battle && !engine.CanActivateSpellTrap(p, wave, false),
                    $"bp={bp} phase={engine.Phase}");
                var mp2 = engine.TryEnterMainPhase2(p);
                DrainCombat(engine);

                var lp = o.LifePoints;
                var can = engine.CanActivateSpellTrap(p, wave, false);
                var fired = can && engine.TryActivateSpellTrap(p, wave, false);
                DrainCombat(engine);
                Check("Wave-Motion Cannon: in Main Phase 2, send it to the GY (cost) → 1000 x 1 Standby Phase",
                    mp2 && engine.Phase == DuelPhase.Main2 && can && fired && o.LifePoints == lp - 1000 && p.Graveyard.Contains(wave) &&
                    !p.TryFindSpellTrap(wave, out _) && !engine.Chain.HasLinks,
                    $"phase={engine.Phase} can={can} fired={fired} lp={o.LifePoints} (was {lp}) " +
                    $"gy={p.Graveyard.Contains(wave)}");
            }

            // Two of your Standby Phases → 2000.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var wave = ActivateFromHand(engine, p, WaveMotion, out var cast);
                var first = AdvanceToPlayerMain(engine, engine.TurnNumber);
                var second = first && AdvanceToPlayerMain(engine, engine.TurnNumber);
                var lp = o.LifePoints;
                var fired = second && engine.TryActivateSpellTrap(p, wave, false);
                DrainCombat(engine);
                Check("Wave-Motion Cannon: two of your Standby Phases → 1000 x 2",
                    cast && fired && o.LifePoints == lp - 2000 && p.Graveyard.Contains(wave),
                    $"cast={cast} second={second} counter={wave.TurnCounter} lp={o.LifePoints} (was {lp})");
            }

            // A Set copy does not count while face-down, and its activation starts from 0.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var set = PlaceSetTrap(engine, p, WaveMotion, 1);
                set.TurnCounter = 4; // stale count from an earlier trip to the field
                var mine = AdvanceToPlayerMain(engine, engine.TurnNumber);
                var counterWhileSet = set.TurnCounter;
                var cast = mine && engine.TryActivateSpellTrap(p, set, false);
                DrainCombat(engine);
                Check("Wave-Motion Cannon: a Set copy does not count; activating it resets the counter to 0",
                    mine && counterWhileSet == 4 && cast && set.FaceUp && p.TryFindSpellTrap(set, out _) &&
                    set.TurnCounter == 0 && !engine.CanActivateSpellTrap(p, set, false),
                    $"mine={mine} whileSet={counterWhileSet} cast={cast} counter={set.TurnCounter}");
            }

            // Negated: no activation.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var wave = ActivateFromHand(engine, p, WaveMotion, out _);
                wave.TurnCounter = 2;
                var ready = engine.CanActivateSpellTrap(p, wave, false);
                wave.IsNegated = true;
                var negated = engine.CanActivateSpellTrap(p, wave, false);
                wave.IsNegated = false;
                Check("Wave-Motion Cannon: ready with a count, but not while its effects are negated",
                    ready && !negated, $"ready={ready} negated={negated}");
            }

            // AI: fires at 3 counted (not at 1 unless lethal).
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var reached = ReachOpponentMain(engine);
                o.Hand.Clear();
                var wave = engine.CreateCardInstance(WaveMotion);
                wave.FaceUp = true;
                o.SpellTrapZones[0].Occupant = wave;
                wave.TurnCounter = 1;
                var holdsAtOne = !PerCountBurnsRuntime.AiWantsFaceUpFieldIgnition(engine, o, wave);
                p.LifePoints = 1000;
                var lethalAtOne = PerCountBurnsRuntime.AiWantsFaceUpFieldIgnition(engine, o, wave);
                p.LifePoints = 8000;
                wave.TurnCounter = 3;
                var lp = p.LifePoints;
                var done = reached && RunAiTurn(engine, 200);
                Check("Wave-Motion Cannon AI: holds at 1 (unless lethal), fires at 3 → 3000",
                    reached && holdsAtOne && lethalAtOne && done && p.LifePoints == lp - 3000 &&
                    o.Graveyard.Contains(wave),
                    $"reached={reached} hold={holdsAtOne} lethal={lethalAtOne} done={done} lp={p.LifePoints} (was {lp})");
            }
        }

        // ── Wave-Motion Cannon: the ignition is a Spell Speed 1 Chain Link (review fixes) ──

        static CardInstance PlaceFaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static void WaveMotionChainDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The opponent may chain to the ignition (EFFECT_TYPE_IGNITION starts a chain). The cost is paid at
            // activation, so Cemetary Bomb chained to it counts the Cannon in the GY and resolves first; the
            // damage fixed at activation resolves after. Magic Jammer cannot answer it: an effect of a face-up
            // Spell is not "a Spell Card is activated".
            {
                var engine = Fresh(db, pDeck, aDeck);
                engine.HumanVsHuman = true;
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var wave = PlaceFaceUpSpell(engine, p, WaveMotion, 0);
                wave.TurnCounter = 1;
                var bomb = PlaceSetTrap(engine, o, CemetaryBomb, 3);
                var jammer = PlaceSetTrap(engine, o, MagicJammer, 4);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh); // Magic Jammer's discard cost is payable
                p.Graveyard.Clear();
                p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var can = engine.CanActivateSpellTrap(p, wave, false);
                var fired = can && engine.TryActivateSpellTrap(p, wave, false);
                var pr = engine.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o;
                var costPaid = p.Graveyard.Contains(wave) && !p.TryFindSpellTrap(wave, out _);
                var notYet = o.LifePoints == oLp;
                var linkSeated = engine.Chain.Count == 1 && engine.Chain.Last?.Card == wave;
                var bombOffered = window && pr.LegalCards.Contains(bomb);
                var jammerOffered = window &&
                                    (pr.LegalCards.Contains(jammer) || engine.CanActivateSpellTrap(o, jammer, false));
                var chained = bombOffered && engine.TryActivateSpellTrap(o, bomb, false);
                DrainCombat(engine);
                Check("Wave-Motion Cannon: its ignition is a chain link the opponent can answer (Cemetary Bomb first, " +
                      "counting the Cannon sent as cost → 300; then 1000); Magic Jammer cannot negate it",
                    fired && window && costPaid && notYet && linkSeated && bombOffered && !jammerOffered && chained &&
                    p.LifePoints == pLp - 300 && o.LifePoints == oLp - 1000 && !engine.Chain.HasLinks &&
                    o.Graveyard.Contains(bomb) && !jammer.FaceUp,
                    $"can={can} fired={fired} window={window} costPaid={costPaid} notYet={notYet} seated={linkSeated} " +
                    $"bomb={bombOffered} jammer={jammerOffered} chained={chained} pLp={p.LifePoints} (was {pLp}) " +
                    $"oLp={o.LifePoints} (was {oLp}) chain={engine.Chain.Count}");
            }

            // The AI opponent with a Set Magic Jammer (and a card to discard) negates a Spell Card's activation
            // (control), but not the Cannon's ignition: the effect link resolves for 1000 and Magic Jammer stays Set.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var jammer = PlaceSetTrap(engine, o, MagicJammer, 4);
                o.Hand.Clear();
                PutInHand(engine, o, Kuriboh);
                PutInHand(engine, o, Celtic);
                p.Hand.Clear();
                var pot = PutInHand(engine, p, PotOfGreed);
                var potCast = engine.TryActivateSpellTrap(p, pot, true);
                DrainCombat(engine);
                var controlNegated = potCast && o.Graveyard.Contains(jammer) && p.Graveyard.Contains(pot) &&
                                     p.Hand.Count == 0;
                Check("Wave-Motion Cannon control: the AI's Set Magic Jammer negates a Spell Card activation (Pot of Greed)",
                    controlNegated,
                    $"cast={potCast} jammerGy={o.Graveyard.Contains(jammer)} potGy={p.Graveyard.Contains(pot)} " +
                    $"hand={p.Hand.Count}");

                var jammer2 = PlaceSetTrap(engine, o, MagicJammer, 3);
                var wave = PlaceFaceUpSpell(engine, p, WaveMotion, 0);
                wave.TurnCounter = 1;
                var oLp = o.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, wave, false);
                DrainCombat(engine);
                Check("Wave-Motion Cannon: the AI's Magic Jammer cannot negate the ignition (an effect, not a card " +
                      "activation) → 1000",
                    fired && o.LifePoints == oLp - 1000 && !jammer2.FaceUp && o.SpellTrapZones[3].Occupant == jammer2 &&
                    p.Graveyard.Contains(wave) && !engine.Chain.HasLinks,
                    $"fired={fired} oLp={o.LifePoints} (was {oLp}) jammerFaceUp={jammer2.FaceUp} " +
                    $"jammerSet={o.SpellTrapZones[3].Occupant == jammer2} chain={engine.Chain.Count}");
            }

            // The opponent passes: the fixed damage resolves, the chain is empty, and a second ignition is not
            // offered while the first one's link waits (Spell Speed 1 only starts a chain).
            {
                var engine = Fresh(db, pDeck, aDeck);
                engine.HumanVsHuman = true;
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var wave = PlaceFaceUpSpell(engine, p, WaveMotion, 0);
                wave.TurnCounter = 2;
                var other = PlaceFaceUpSpell(engine, p, WaveMotion, 1);
                other.TurnCounter = 1;
                PlaceSetTrap(engine, o, Waboku, 3); // keeps a chain window open for the opponent
                var oLp = o.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, wave, false);
                var window = engine.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                             engine.PendingResponse.Responder == o;
                var secondWhileOpen = engine.CanActivateSpellTrap(p, other, false);
                var passed = window && engine.PassResponse();
                DrainCombat(engine);
                var secondAfter = engine.CanActivateSpellTrap(p, other, false);
                Check("Wave-Motion Cannon: opponent passes → 1000 x 2 resolves, chain empty; no second Speed 1 " +
                      "ignition while the chain is open",
                    fired && window && passed && !secondWhileOpen && secondAfter &&
                    o.LifePoints == oLp - 2000 && !engine.Chain.HasLinks && p.Graveyard.Contains(wave),
                    $"fired={fired} window={window} second(open)={secondWhileOpen} second(after)={secondAfter} " +
                    $"oLp={o.LifePoints} (was {oLp}) chain={engine.Chain.Count}");
            }

            // Human vs Human: the damage opens the opponent's "you take damage" window (Numinous Healer); after
            // they pass, no chain link is left behind: you can still attack and Pot of Greed still resolves.
            {
                var engine = Fresh(db, pDeck, aDeck);
                engine.HumanVsHuman = true;
                var mine = AdvanceToPlayerMain(engine, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSetTrap(engine, o, Healer, 2);
                var atk = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var wave = PlaceFaceUpSpell(engine, p, WaveMotion, 0);
                wave.TurnCounter = 1;
                var oLp = o.LifePoints;
                var fired = engine.TryActivateSpellTrap(p, wave, false);
                var sawDamageWindow = false;
                for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++)
                {
                    if (engine.PendingResponse.Timing == ResponseTiming.YouTakeDamage &&
                        engine.PendingResponse.Responder == o)
                        sawDamageWindow = true;
                    engine.PassResponse();
                }

                var links = engine.Chain.Count;
                var bp = engine.TryEnterBattlePhase(p);
                var canAtk = engine.CanAttack(p, atk);
                var mp2 = engine.TryEnterMainPhase2(p);
                p.Hand.Clear();
                var pot = PutInHand(engine, p, PotOfGreed);
                var potOk = engine.TryActivateSpellTrap(p, pot, true);
                DrainCombat(engine);
                Check("Wave-Motion Cannon: a passed damage window leaves no chain link (attack, then Pot of Greed)",
                    mine && fired && sawDamageWindow && o.LifePoints == oLp - 1000 && links == 0 && bp && canAtk &&
                    mp2 && potOk && p.Hand.Count == 2 && p.Graveyard.Contains(pot),
                    $"mine={mine} fired={fired} dmgWindow={sawDamageWindow} oLp={o.LifePoints} (was {oLp}) " +
                    $"links={links} bp={bp} canAtk={canAtk} mp2={mp2} pot={potOk} hand={p.Hand.Count}");
            }

            // AI fires it at a human with a Set Numinous Healer: after the human passes, no link is left and the
            // AI can still attack.
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var reached = ReachOpponentMain(engine);
                PlaceSetTrap(engine, p, Healer, 2);
                var atk = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var wave = PlaceFaceUpSpell(engine, o, WaveMotion, 0);
                wave.TurnCounter = 2;
                var lp = p.LifePoints;
                var fired = engine.TryActivateSpellTrap(o, wave, false);
                var window = engine.PendingResponse?.Timing == ResponseTiming.YouTakeDamage &&
                             engine.PendingResponse.Responder == p;
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var links = engine.Chain.Count;
                var bp = engine.TryEnterBattlePhase(o);
                var canAtk = engine.CanAttack(o, atk);
                Check("Wave-Motion Cannon AI: after the human passes the damage window, no link is left; it can attack",
                    reached && fired && window && p.LifePoints == lp - 2000 && links == 0 && bp && canAtk,
                    $"reached={reached} fired={fired} window={window} lp={p.LifePoints} (was {lp}) links={links} " +
                    $"bp={bp} canAtk={canAtk}");
            }

            // Whole AI turn: 3 counted → 3000, then its 1400 attacker still attacks directly (with or without a
            // Set Numinous Healer the human passes).
            foreach (var withHealer in new[] { false, true })
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                var reached = ReachOpponentMain(engine);
                o.Hand.Clear();
                if (withHealer) PlaceSetTrap(engine, p, Healer, 2);
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var wave = PlaceFaceUpSpell(engine, o, WaveMotion, 0);
                wave.TurnCounter = 3;
                var lp = p.LifePoints;
                var done = reached && RunAiTurn(engine, 300);
                Check($"Wave-Motion Cannon AI turn (Healer={withHealer}): 3000 burn, then a 1400 direct attack",
                    done && p.LifePoints == lp - 3000 - 1400 && o.Graveyard.Contains(wave) && !engine.Chain.HasLinks,
                    $"reached={reached} done={done} lp={p.LifePoints} (was {lp}) chain={engine.Chain.Count}");
            }
        }
    }
}
