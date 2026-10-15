using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// FlipDownAndPeek batch (v162): 8-Claws Scorpion, Royal Keeper, Wandering Mummy, Giant Axe Mummy, Cobraman Sakuzy, A Man
    /// with Wdjat and Trap Dustshoot. Compile shapes, mini-duels through the real engine entry points (TryActivateSpellTrap for
    /// the flip-down ignitions and the Trap, TryNormalSummon / TryFlipSummon, TryAttack and the Damage Step, EndTurn into the
    /// Standby Phase, TrySelectEffectTarget) and near misses. Runtime: <see cref="FlipDownAndPeekRuntime"/>.
    /// </summary>
    public static class FlipDownAndPeekRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int EightClaws = 14261867;     // Insect Lv2 300/200
        const int RoyalKeeper = 16509093;    // Zombie Lv4 1600/1700
        const int WanderingMummy = 42994702; // Zombie Lv4 1500/1500
        const int GiantAxeMummy = 78266168;  // Zombie Lv5 1700/2000
        const int Cobraman = 75109441;       // Reptile Lv3 800/1400
        const int Wdjat = 51351302;          // Spellcaster Lv4 1600/1600
        const int Dustshoot = 64697231;      // Normal Trap

        const int Celtic = 91152256;         // Lv4 1400/1200
        const int MysticalElf = 15025844;    // Lv4 800/2000
        const int BlueEyes = 89631139;       // Lv8 3000/2500
        const int PotOfGreed = 55144522;
        const int MonsterReborn = 83764718;

        const int FushiohRichie = 39711336;
        const int ShiftingShadows = 59237154;
        const int NightmarePenguin = 81306586;
        const int MindHaxorz = 75392615;
        const int ForcefulSentry = 42829885;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("FlipDownAndPeek: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            FlipDownIgnitions(db, pDeck, aDeck, Check);
            EightClawsDuels(db, pDeck, aDeck, Check);
            RoyalKeeperDuels(db, pDeck, aDeck, Check);
            WanderingMummyDuels(db, pDeck, aDeck, Check);
            GiantAxeDuels(db, pDeck, aDeck, Check);
            CobramanDuels(db, pDeck, aDeck, Check);
            WdjatDuels(db, pDeck, aDeck, Check);
            DustshootDuels(db, pDeck, aDeck, Check);
            NearMisses(db, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Shape(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(c => c == null ? "null" : $"{c.Timing}:{c.Action}:{c.Zone}"))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static EffectClause One(CompiledCardProgram prog, EffectActionKind action) =>
            prog?.ClauseList.FirstOrDefault(c => c != null && c.Action == action);

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "FlipDownAndPeek near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };

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

        /// <summary>The opponent's Battle Phase (turn 4+), boards and hands empty.</summary>
        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var r0);
            reached = r0 && ReachOpponentBattle(engine);
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

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        /// <summary>A monster placed this turn that should count as already face-up at the start of the turn.</summary>
        static CardInstance PlaceSettled(DuelEngine engine, DuelistState who, int id, int zone, BattlePosition pos,
            bool faceUp)
        {
            var c = PlaceMonster(engine, who, id, zone, pos, faceUp);
            c.FaceUpStampAtTurnStart = c.FaceUpStamp;
            return c;
        }

        static List<string> Capture(DuelEngine engine)
        {
            var lines = new List<string>();
            engine.OnLog += s => lines.Add(s);
            return lines;
        }

        static bool Looked(List<string> log, params string[] names) =>
            log.Exists(l => l.Contains("looks at") && names.All(l.Contains));

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>Activate a monster's flip-face-down ignition from the field and settle the chain.</summary>
        static bool FlipDown(DuelEngine engine, DuelistState who, CardInstance monster)
        {
            var can = engine.CanActivateSpellTrap(who, monster, fromHand: false);
            var ok = can && engine.TryActivateSpellTrap(who, monster, fromHand: false);
            Settle(engine);
            return ok;
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, name, thenShuffle) in new[]
                     {
                         (EightClaws, "8-Claws Scorpion", false), (RoyalKeeper, "Royal Keeper", false),
                         (WanderingMummy, "Wandering Mummy", true), (GiantAxeMummy, "Giant Axe Mummy", false),
                         (Cobraman, "Cobraman Sakuzy", false)
                     })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                var c = One(prog, EffectActionKind.SetThisFaceDownDefense);
                Check($"{name}: once-per-turn flip-face-down ignition" + (thenShuffle ? " then rearrange" : "") +
                      "; FullyCompiled and Implemented",
                    prog != null && prog.FullyCompiled && Implemented(db, id) && c != null &&
                    c.Timing == EffectTiming.Activate && c.OncePerTurn && c.IsOptional &&
                    c.ThenShuffleFaceDownMonstersInMainZones == thenShuffle &&
                    prog.ClauseList.Count(x => x?.Action == EffectActionKind.SetThisFaceDownDefense) == 1,
                    Shape(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(EightClaws));
                var c = One(prog, EffectActionKind.AtkBecomesAmountWhenAttackingFaceDownDefenseThisCalculation);
                Check("8-Claws Scorpion: damage-calculation ATK 2400 marker vs face-down Defense",
                    c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Amount == 2400 &&
                    prog.ClauseList.Count == 2, Shape(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(RoyalKeeper));
                var c = One(prog, EffectActionKind.GainAtkDefUntilEndOfTurn);
                Check("Royal Keeper: flipped face-up → +300 ATK/DEF until end of turn (flip only, not Normal Summon)",
                    c != null && c.Timing == EffectTiming.ThisCardSummoned && c.Amount == 300 && c.DefAmount == 300 &&
                    c.RequiresThisFlipSummoned && c.AlsoWhenFlippedFaceUp && !c.IsOptional && !c.RequiresTargetChoice &&
                    prog.ClauseList.Count == 2, Shape(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(WanderingMummy));
                Check("Wandering Mummy: one clause (the rearrangement rides on the flip-down)",
                    prog != null && prog.ClauseList.Count == 1, Shape(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(GiantAxeMummy));
                var c = One(prog, EffectActionKind.DestroyAttackerWithLowerAtkThanThisDefWhenAttackedFaceDown);
                Check("Giant Axe Mummy: end-of-Damage-Step destruction of a lower-ATK attacker",
                    c != null && c.Timing == EffectTiming.EndOfDamageStep && prog.ClauseList.Count == 2, Shape(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Cobraman));
                var c = One(prog, EffectActionKind.LookAtOpponentSetSpellTraps);
                Check("Cobraman Sakuzy: flipped face-up → look at the opponent's Set Spells/Traps (mandatory)",
                    c != null && c.Timing == EffectTiming.ThisCardSummoned && c.RequiresThisFlipSummoned &&
                    c.AlsoWhenFlippedFaceUp && !c.IsOptional && !c.RequiresTargetChoice, Shape(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Wdjat));
                var ns = prog?.ClauseList.FirstOrDefault(c => c?.Timing == EffectTiming.ThisCardSummoned);
                var sb = prog?.ClauseList.FirstOrDefault(c => c?.Timing == EffectTiming.StandbyPhase);
                Check("A Man with Wdjat: Normal Summon target + each own Standby Phase, 1 face-down card the opponent controls",
                    prog != null && prog.FullyCompiled && Implemented(db, Wdjat) && prog.ClauseList.Count == 2 &&
                    ns != null && ns.Action == EffectActionKind.LookAtTargetSetCard &&
                    ns.Zone == EffectZoneFilter.OppAnyCardOnField && ns.RequiresTargetFaceDown && ns.RequiresTargetChoice &&
                    ns.IsPsctTarget && ns.RequiresThisNormalSummoned && !ns.IsOptional &&
                    sb != null && sb.Action == EffectActionKind.LookAtTargetSetCard && sb.RequiresTargetFaceDown &&
                    !sb.OpponentTurnOnly && !sb.IsOptional,
                    Shape(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Dustshoot));
                var c = One(prog, EffectActionKind.LookAtOpponentHandReturnChosenMonsterToDeck);
                Check("Trap Dustshoot: opponent holds 4+ → look at the hand, 1 monster back to the Deck (no target)",
                    prog != null && prog.FullyCompiled && Implemented(db, Dustshoot) && prog.ClauseList.Count == 1 &&
                    c != null && c.Timing == EffectTiming.Activate && c.RequiresOpponentHandAtLeast == 4 &&
                    !c.RequiresTargetChoice, Shape(prog));
            }
        }

        // ───────────────────────────── flip-down ignitions ─────────────────────────────

        static void FlipDownIgnitions(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, name) in new[]
                     {
                         (EightClaws, "8-Claws Scorpion"), (GiantAxeMummy, "Giant Axe Mummy"), (Cobraman, "Cobraman Sakuzy"),
                         (RoyalKeeper, "Royal Keeper")
                     })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var m = PlaceSettled(engine, p, id, 0, BattlePosition.Attack, true);
                var log = Capture(engine);
                var flipped = FlipDown(engine, p, m);
                var again = engine.CanActivateSpellTrap(p, m, fromHand: false);
                Check($"{name}: its ignition flips it into face-down Defense Position; not again while face-down",
                    reached && flipped && !m.FaceUp && m.Position == BattlePosition.Defense && !again &&
                    p.TryFindMonster(m, out _) && !log.Exists(l => l.Contains("looks at")),
                    $"reached={reached} flipped={flipped} faceUp={m.FaceUp} pos={m.Position} again={again}");
            }

            {
                // Once per turn: the flag survives being Set — flipped face-up again by an effect, it cannot repeat.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var m = PlaceSettled(engine, p, GiantAxeMummy, 0, BattlePosition.Attack, true);
                var flipped = FlipDown(engine, p, m);
                m.FaceUp = true;
                m.Position = BattlePosition.Defense;
                var again = engine.CanActivateSpellTrap(p, m, fromHand: false);
                Check("Giant Axe Mummy: once per turn — face-up again the same turn, the ignition is not offered",
                    reached && flipped && !again, $"flipped={flipped} again={again}");
            }
        }

        // ───────────────────────────── 8-Claws Scorpion ─────────────────────────────

        static void EightClawsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // A Set Mystical Elf (DEF 2000): at 2400 ATK the Scorpion destroys it; back to 300 afterwards.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var claws = PlaceSettled(engine, p, EightClaws, 0, BattlePosition.Attack, true);
                var elf = PlaceSettled(engine, o, MysticalElf, 0, BattlePosition.Defense, false);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var log = Capture(engine);
                var attacked = Attack(engine, p, claws, elf);
                Check("8-Claws Scorpion: attacking a face-down Defense monster, its ATK is 2400 in damage calculation",
                    reached && attacked && o.Graveyard.Contains(elf) && p.LifePoints == pLp && o.LifePoints == oLp &&
                    claws.CurrentAtk == 300 && p.TryFindMonster(claws, out _),
                    $"attacked={attacked} elfGy={o.Graveyard.Contains(elf)} p={pLp}->{p.LifePoints} atk={claws.CurrentAtk} " +
                    $"log={string.Join(" / ", log)}");
            }

            {
                // A face-up Defense Mystical Elf: ATK stays 300 — the Scorpion bounces off (1700 damage).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var claws = PlaceSettled(engine, p, EightClaws, 0, BattlePosition.Attack, true);
                var elf = PlaceSettled(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var pLp = p.LifePoints;
                var attacked = Attack(engine, p, claws, elf);
                Check("8-Claws Scorpion: a face-up Defense target — no ATK change (300 vs DEF 2000)",
                    reached && attacked && o.TryFindMonster(elf, out _) && p.LifePoints == pLp - 1700,
                    $"attacked={attacked} p={pLp}->{p.LifePoints}");
            }

            {
                // Attacked while face-up (the opponent's Set monster is not involved): no change when it is the target.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var claws = PlaceSettled(engine, p, EightClaws, 0, BattlePosition.Attack, true);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var attacked = engine.TryAttack(o, celtic, claws);
                DrainCombat(engine);
                Check("8-Claws Scorpion: attacked, it keeps 300 ATK (destroyed by Celtic Guardian, 1100 damage)",
                    reached && attacked && p.Graveyard.Contains(claws) && p.LifePoints == pLp - 1100,
                    $"reached={reached} attacked={attacked} p={pLp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Royal Keeper ─────────────────────────────

        static void RoyalKeeperDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var rk = PlaceSettled(engine, p, RoyalKeeper, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, rk);
                Settle(engine);
                Check("Royal Keeper: Flip Summoned → 1900/2000 until the end of the turn",
                    reached && flipped && rk.FaceUp && rk.CurrentAtk == 1900 && rk.CurrentDef == 2000 &&
                    !engine.IsAwaitingEffectTarget,
                    $"flipped={flipped} stats={rk.CurrentAtk}/{rk.CurrentDef}");
                EndTurn(engine);
                Check("Royal Keeper: the gain ends with the turn",
                    rk.CurrentAtk == 1600 && rk.CurrentDef == 1700, $"stats={rk.CurrentAtk}/{rk.CurrentDef}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var rk = PutInHand(engine, p, RoyalKeeper);
                var ns = engine.TryNormalSummon(p, rk, false);
                Settle(engine);
                Check("Royal Keeper: Normal Summoned face-up is not \"flipped face-up\" — no gain",
                    reached && ns && rk.CurrentAtk == 1600 && rk.CurrentDef == 1700,
                    $"ns={ns} stats={rk.CurrentAtk}/{rk.CurrentDef}");
            }

            {
                // Attacked face-down: the gain applies after damage calculation (DEF 1700 vs 1400 → 300 damage).
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var rk = PlaceSettled(engine, p, RoyalKeeper, 0, BattlePosition.Defense, false);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var attacked = engine.TryAttack(o, celtic, rk);
                DrainCombat(engine);
                Check("Royal Keeper: flipped by an attack → +300 after damage calculation (300 damage, then 1900/2000)",
                    reached && attacked && rk.FaceUp && o.LifePoints == oLp - 300 && rk.CurrentAtk == 1900 &&
                    rk.CurrentDef == 2000,
                    $"reached={reached} attacked={attacked} o={oLp}->{o.LifePoints} stats={rk.CurrentAtk}/{rk.CurrentDef}");
            }

            {
                // Flipped face-up by a card effect: the trigger follows that effect.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var rk = PlaceSettled(engine, p, RoyalKeeper, 0, BattlePosition.Defense, false);
                var changed = engine.ApplyEffectPositionChange(rk, BattlePosition.Attack, faceUp: true);
                engine.NotifyPublic();
                Settle(engine);
                Check("Royal Keeper: flipped face-up by a card effect → +300 ATK/DEF",
                    reached && changed && rk.CurrentAtk == 1900 && rk.CurrentDef == 2000,
                    $"changed={changed} stats={rk.CurrentAtk}/{rk.CurrentDef}");
            }
        }

        // ───────────────────────────── Wandering Mummy ─────────────────────────────

        static void WanderingMummyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var mummy = PlaceSettled(engine, p, WanderingMummy, 0, BattlePosition.Attack, true);
                var celtic = PlaceSettled(engine, p, Celtic, 1, BattlePosition.Defense, false);
                var elf = PlaceSettled(engine, p, MysticalElf, 3, BattlePosition.Defense, false);
                var faceUp = PlaceSettled(engine, p, BlueEyes, 4, BattlePosition.Attack, true);
                var log = Capture(engine);
                var flipped = FlipDown(engine, p, mummy);
                var inSet = new[] { 0, 1, 3 }.Select(i => p.MonsterZones[i].Occupant).ToList();
                var same = inSet.Contains(mummy) && inSet.Contains(celtic) && inSet.Contains(elf) &&
                           inSet.TrueForAll(m => m != null && !m.FaceUp && m.Position == BattlePosition.Defense);
                Check("Wandering Mummy: flipped face-down, then the face-down monsters are rearranged among their zones",
                    reached && flipped && !mummy.FaceUp && same && p.MonsterZones[2].Occupant == null &&
                    ReferenceEquals(p.MonsterZones[4].Occupant, faceUp) && faceUp.FaceUp &&
                    log.Exists(l => l.Contains("are rearranged")),
                    $"flipped={flipped} zones={string.Join(",", p.MonsterZones.Select(z => z.Occupant?.Name ?? "-"))}");
            }

            {
                // Already face-down at resolution (the activation was answered): no flip, so no rearrangement.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var mummy = PlaceSettled(engine, p, WanderingMummy, 0, BattlePosition.Attack, true);
                PlaceSettled(engine, p, Celtic, 1, BattlePosition.Defense, false);
                var log = Capture(engine);
                var prog = CardTextEffectCompiler.Compile(db.Get(WanderingMummy));
                mummy.FaceUp = false;
                mummy.Position = BattlePosition.Defense;
                var dummy = false;
                TextEffectRuntime.ApplyClause(engine, p, mummy, prog.ClauseList[0], null, ref dummy, ref dummy);
                Check("Wandering Mummy: already face-down when it resolves → nothing is rearranged",
                    reached && !log.Exists(l => l.Contains("rearranged")) &&
                    log.Exists(l => l.Contains("already face-down")),
                    string.Join(" / ", log));
            }
        }

        // ───────────────────────────── Giant Axe Mummy ─────────────────────────────

        static void GiantAxeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var axe = PlaceSettled(engine, p, GiantAxeMummy, 0, BattlePosition.Defense, false);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var attacked = engine.TryAttack(o, celtic, axe);
                DrainCombat(engine);
                Check("Giant Axe Mummy: attacked face-down by a 1400 ATK monster (DEF 2000) → the attacker is destroyed",
                    reached && attacked && o.Graveyard.Contains(celtic) && p.TryFindMonster(axe, out _) && axe.FaceUp &&
                    o.LifePoints == oLp - 600,
                    $"reached={reached} attacked={attacked} celticGy={o.Graveyard.Contains(celtic)} o={oLp}->{o.LifePoints}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var axe = PlaceSettled(engine, p, GiantAxeMummy, 0, BattlePosition.Defense, true);
                var celtic = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var attacked = engine.TryAttack(o, celtic, axe);
                DrainCombat(engine);
                Check("Giant Axe Mummy: attacked in face-up Defense Position → the attacker survives",
                    reached && attacked && o.TryFindMonster(celtic, out _) && p.TryFindMonster(axe, out _),
                    $"reached={reached} attacked={attacked}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var axe = PlaceSettled(engine, p, GiantAxeMummy, 0, BattlePosition.Defense, false);
                var bewd = PlaceSettled(engine, o, BlueEyes, 0, BattlePosition.Attack, true);
                var attacked = engine.TryAttack(o, bewd, axe);
                DrainCombat(engine);
                Check("Giant Axe Mummy: an attacker with ATK ≥ its DEF is not destroyed (the Mummy is)",
                    reached && attacked && o.TryFindMonster(bewd, out _) && p.Graveyard.Contains(axe),
                    $"reached={reached} attacked={attacked} axeGy={p.Graveyard.Contains(axe)}");
            }

            {
                // Giant Axe Mummy attacking a Set monster: the effect is the attack target's only.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var axe = PlaceSettled(engine, p, GiantAxeMummy, 0, BattlePosition.Attack, true);
                var elf = PlaceSettled(engine, o, MysticalElf, 0, BattlePosition.Defense, false);
                var attacked = Attack(engine, p, axe, elf);
                Check("Giant Axe Mummy: as the attacker nothing extra happens (1700 vs DEF 2000)",
                    reached && attacked && p.TryFindMonster(axe, out _) && o.TryFindMonster(elf, out _),
                    $"attacked={attacked}");
            }
        }

        // ───────────────────────────── Cobraman Sakuzy ─────────────────────────────

        static void CobramanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var pot = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var reborn = PlaceSetTrap(engine, o, MonsterReborn, 2);
                var cobra = PlaceSettled(engine, p, Cobraman, 0, BattlePosition.Defense, false);
                var log = Capture(engine);
                var flipped = engine.TryFlipSummon(p, cobra);
                Settle(engine);
                Check("Cobraman Sakuzy: Flip Summoned → looks at the opponent's Set Spells/Traps; they stay Set",
                    reached && flipped && Looked(log, "Pot of Greed", "Monster Reborn") && !pot.FaceUp && !reborn.FaceUp &&
                    o.TryFindSpellTrap(pot, out _) && o.TryFindSpellTrap(reborn, out _),
                    $"flipped={flipped} log={string.Join(" / ", log.Where(l => l.Contains("Cobraman")))}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                var cobra = PlaceSettled(engine, p, Cobraman, 0, BattlePosition.Defense, false);
                var elf = PlaceSettled(engine, o, MysticalElf, 0, BattlePosition.Attack, true);
                var log = Capture(engine);
                var attacked = engine.TryAttack(o, elf, cobra);
                DrainCombat(engine);
                Check("Cobraman Sakuzy: flipped face-up by an attack → looks at the attacking player's Set cards",
                    reached && attacked && cobra.FaceUp && Looked(log, "Pot of Greed"),
                    $"reached={reached} attacked={attacked} log={string.Join(" / ", log.Where(l => l.Contains("Cobraman")))}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSetTrap(engine, o, PotOfGreed, 0);
                var cobra = PutInHand(engine, p, Cobraman);
                var log = Capture(engine);
                var ns = engine.TryNormalSummon(p, cobra, false);
                Settle(engine);
                Check("Cobraman Sakuzy: Normal Summoned face-up → no look",
                    reached && ns && !log.Exists(l => l.Contains("looks at")), string.Join(" / ", log));
            }
        }

        // ───────────────────────────── A Man with Wdjat ─────────────────────────────

        static void WdjatDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Normal Summon: the human targets 1 of the opponent's Set cards (monster or Spell/Trap), not a face-up one.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var setMon = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Defense, false);
                var faceUp = PlaceSettled(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var pot = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var wdjat = PutInHand(engine, p, Wdjat);
                var log = Capture(engine);
                var ns = engine.TryNormalSummon(p, wdjat, false);
                Settle(engine);
                var pend = engine.PendingActivation;
                var legal = pend?.LegalTargets.ToList() ?? new List<CardInstance>();
                var offered = engine.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(setMon) &&
                              legal.Contains(pot) && !legal.Contains(faceUp);
                var picked = offered && engine.TrySelectEffectTarget(pot);
                Settle(engine);
                Check("A Man with Wdjat: Normal Summoned → target 1 Set card the opponent controls → look; it stays Set",
                    reached && ns && offered && picked && Looked(log, "Pot of Greed") && !pot.FaceUp &&
                    o.TryFindSpellTrap(pot, out _) && !setMon.FaceUp && !engine.IsAwaitingEffectTarget,
                    $"ns={ns} legal={string.Join(",", legal.Select(t => t.Name))} picked={picked}");
            }

            {
                // A lone Set card: no prompt. No Set card: nothing happens.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var setMon = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Defense, false);
                var wdjat = PutInHand(engine, p, Wdjat);
                var log = Capture(engine);
                var ns = engine.TryNormalSummon(p, wdjat, false);
                Settle(engine);
                // A mandatory summon trigger with one legal target still shows the picker to a human: confirm it.
                var lone = !engine.IsAwaitingEffectTarget ||
                           (engine.PendingActivation.LegalTargets.Count == 1 &&
                            engine.PendingActivation.Prompt.Contains("look at") &&
                            engine.TrySelectEffectTarget(setMon));
                Settle(engine);
                Check("A Man with Wdjat: a lone Set card is the only choice; it is looked at and stays Set",
                    reached && ns && lone && Looked(log, "Celtic Guardian") && !setMon.FaceUp &&
                    !engine.IsAwaitingEffectTarget,
                    string.Join(" / ", log));

                var e2 = PlayerMain1(db, pDeck, aDeck, out _);
                PlaceSettled(e2, e2.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var w2 = PutInHand(e2, e2.Player, Wdjat);
                var log2 = Capture(e2);
                var ns2 = e2.TryNormalSummon(e2.Player, w2, false);
                Settle(e2);
                Check("A Man with Wdjat: no Set card → no pick, nothing looked at",
                    ns2 && !e2.IsAwaitingEffectTarget && !log2.Exists(l => l.Contains("looks at")),
                    string.Join(" / ", log2));
            }

            {
                // Flip Summoned: not a Normal Summon → no trigger.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSettled(engine, o, Celtic, 0, BattlePosition.Defense, false);
                var wdjat = PlaceSettled(engine, p, Wdjat, 0, BattlePosition.Defense, false);
                var log = Capture(engine);
                var flipped = engine.TryFlipSummon(p, wdjat);
                Settle(engine);
                Check("A Man with Wdjat: Flip Summoned → no look (Normal Summon only)",
                    reached && flipped && !engine.IsAwaitingEffectTarget && !log.Exists(l => l.Contains("looks at")),
                    string.Join(" / ", log));
            }

            {
                // The player's Standby Phase: 2 Set cards → the human picks (the Standby Phase waits), then Main Phase 1.
                // The opponent's Standby Phase does not trigger it.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSettled(engine, p, Wdjat, 0, BattlePosition.Attack, true);
                var setMon = PlaceSettled(engine, o, Celtic, 0, BattlePosition.Defense, false);
                var pot = PlaceSetTrap(engine, o, PotOfGreed, 0);
                var log = Capture(engine);
                EndTurn(engine); // the opponent's turn
                var oppStandbyQuiet = engine.TurnPlayer == o && !log.Exists(l => l.Contains("looks at")) &&
                                      engine.PendingActivation == null;
                EndTurn(engine); // the player's Standby Phase
                var standby = engine.Phase == DuelPhase.Standby && engine.TurnPlayer == p;
                var pend = engine.PendingActivation;
                var legal = pend?.LegalTargets.ToList() ?? new List<CardInstance>();
                var picked = legal.Contains(setMon) && legal.Contains(pot) && engine.TrySelectEffectTarget(setMon);
                Settle(engine);
                Check("A Man with Wdjat: not in the opponent's Standby Phase; in its controller's, the human picks a Set card",
                    reached && oppStandbyQuiet && standby && legal.Count == 2 && picked && Looked(log, "Celtic Guardian") &&
                    !setMon.FaceUp && engine.Phase == DuelPhase.Main1 && engine.PendingActivation == null,
                    $"quiet={oppStandbyQuiet} standby={standby} legal={string.Join(",", legal.Select(t => t.Name))} " +
                    $"picked={picked} phase={engine.Phase}");
            }

            {
                // The AI's Wdjat in its Standby Phase: no prompt, the turn goes on.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceSettled(engine, o, Wdjat, 0, BattlePosition.Attack, true);
                PlaceSettled(engine, p, Celtic, 0, BattlePosition.Defense, false);
                PlaceSetTrap(engine, p, PotOfGreed, 0);
                var log = Capture(engine);
                EndTurn(engine); // the AI's Standby Phase
                Check("A Man with Wdjat (AI): looks at one of the player's Set cards, no prompt; Main Phase 1",
                    reached && engine.TurnPlayer == o && engine.Phase == DuelPhase.Main1 &&
                    engine.PendingActivation == null && log.Exists(l => l.Contains("looks at")),
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} log={string.Join(" / ", log.Where(l => l.Contains("Wdjat")))}");
            }
        }

        // ───────────────────────────── Trap Dustshoot ─────────────────────────────

        static void DustshootDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                for (var i = 0; i < 3; i++) PutInHand(engine, o, Celtic);
                var trap = PlaceSetTrap(engine, p, Dustshoot, 0);
                var can3 = engine.CanActivateSpellTrap(p, trap, fromHand: false);
                PutInHand(engine, o, PotOfGreed);
                var can4 = engine.CanActivateSpellTrap(p, trap, fromHand: false);
                Check("Trap Dustshoot: only when the opponent holds 4 or more cards",
                    reached && !can3 && can4, $"can3={can3} can4={can4}");
            }

            {
                // Two monsters among 4 cards: the human picks one of the monsters; it is shuffled into the Deck.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PutInHand(engine, o, Celtic);
                var elf = PutInHand(engine, o, MysticalElf);
                var pot = PutInHand(engine, o, PotOfGreed);
                PutInHand(engine, o, MonsterReborn);
                var deckBefore = o.Deck.Count;
                var elvesBefore = o.Deck.Count(x => x == MysticalElf);
                var trap = PlaceSetTrap(engine, p, Dustshoot, 0);
                var log = Capture(engine);
                var act = engine.TryActivateSpellTrap(p, trap, fromHand: false);
                Settle(engine);
                var pend = engine.PendingActivation;
                var legal = pend == null ? "" : string.Join(",", pend.LegalTargets.Select(t => t.Name).OrderBy(n => n));
                var potRefused = !engine.TrySelectEffectTarget(pot);
                var picked = engine.TrySelectEffectTarget(elf);
                Settle(engine);
                Check("Trap Dustshoot: looks at the hand; the human picks 1 of its monsters → shuffled into the Deck",
                    reached && act && pend != null && pend.TargetKind == EffectTargetKind.CardInOppHand &&
                    legal == "Celtic Guardian,Mystical Elf" && potRefused && picked && !o.Hand.Contains(elf) &&
                    o.Hand.Contains(celtic) && o.Hand.Count == 3 && o.Deck.Count == deckBefore + 1 &&
                    o.Deck.Count(x => x == MysticalElf) == elvesBefore + 1 && Looked(log, "Pot of Greed", "Monster Reborn") &&
                    p.Graveyard.Contains(trap) && !engine.IsAwaitingEffectTarget,
                    $"act={act} legal={legal} picked={picked} hand={o.Hand.Count} deck={deckBefore}->{o.Deck.Count}");
            }

            {
                // No monster in the hand: it still activates; the hand is looked at, nothing returns.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                for (var i = 0; i < 4; i++) PutInHand(engine, o, PotOfGreed);
                var deckBefore = o.Deck.Count;
                var trap = PlaceSetTrap(engine, p, Dustshoot, 0);
                var log = Capture(engine);
                var act = engine.TryActivateSpellTrap(p, trap, fromHand: false);
                Settle(engine);
                Check("Trap Dustshoot: no monster in the hand → activates, looks, nothing returns",
                    reached && act && o.Hand.Count == 4 && o.Deck.Count == deckBefore && Looked(log, "Pot of Greed") &&
                    !engine.IsAwaitingEffectTarget,
                    $"act={act} hand={o.Hand.Count} deck={deckBefore}->{o.Deck.Count}");
            }

            {
                // The AI's Dustshoot: it returns the player's highest-ATK monster with no prompt.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                var celtic = PutInHand(engine, p, Celtic);
                var bewd = PutInHand(engine, p, BlueEyes);
                PutInHand(engine, p, PotOfGreed);
                PutInHand(engine, p, PotOfGreed);
                var trap = PlaceSetTrap(engine, o, Dustshoot, 0);
                var act = engine.TryActivateSpellTrap(o, trap, fromHand: false);
                Settle(engine);
                Check("Trap Dustshoot (AI): returns the player's Blue-Eyes (highest ATK), no prompt for the human",
                    reached && act && !p.Hand.Contains(bewd) && p.Hand.Contains(celtic) && p.Hand.Count == 3 &&
                    !engine.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} hand={string.Join(",", p.Hand.Select(h => h.Name))}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var batchActions = new[]
            {
                EffectActionKind.AtkBecomesAmountWhenAttackingFaceDownDefenseThisCalculation,
                EffectActionKind.DestroyAttackerWithLowerAtkThanThisDefWhenAttackedFaceDown,
                EffectActionKind.LookAtOpponentSetSpellTraps, EffectActionKind.LookAtTargetSetCard,
                EffectActionKind.LookAtOpponentHandReturnChosenMonsterToDeck
            };
            var cases = new (int like, string text)[]
            {
                // 8-Claws against a face-up Defense monster, or without "during damage calculation only".
                (EightClaws, "Once per turn, you can flip this card into face-down Defense Position. When this card attacks " +
                             "an opponent's face-up Defense Position monster, this card's ATK becomes 2400 during damage " +
                             "calculation only."),
                (EightClaws, "Once per turn, you can flip this card into face-down Defense Position. When this card attacks " +
                             "an opponent's face-down Defense Position monster, this card's ATK becomes 2400."),
                // Royal Keeper gaining ATK only, or on a Normal Summon too.
                (RoyalKeeper, "Once per turn, you can flip this card into face-down Defense Position. When this card is " +
                              "flipped face-up, it gains 300 ATK until the end of the turn. (If attacked, this effect " +
                              "resolves after damage calculation.)"),
                (RoyalKeeper, "Once per turn, you can flip this card into face-down Defense Position. When this card is " +
                              "Normal Summoned or flipped face-up, it gains 300 ATK and DEF until the end of the turn. (If " +
                              "attacked, this effect resolves after damage calculation.)"),
                // Wandering Mummy rearranging every monster.
                (WanderingMummy, "You can flip this card into face-down Defense Position once per turn during your Main " +
                                 "Phase. After you use this effect, rearrange the monsters in your Main Monster Zones."),
                // Giant Axe attacked in face-up Defense Position, or destroying the attacker in any position.
                (GiantAxeMummy, "You can flip this card into face-down Defense Position once per turn during your Main " +
                                "Phase. If the ATK of a monster on your opponent's side of the field that attacks this card " +
                                "in face-up Defense Position is lower than the DEF of this monster, the attacking monster is " +
                                "destroyed."),
                // Cobraman destroying, or looking at the whole field.
                (Cobraman, "You can flip this card into face-down Defense Position once per turn during your Main Phase. " +
                           "When this card is flipped face-up, look at all Set Spell and Trap Cards on your opponent's side " +
                           "of the field and destroy them."),
                // Wdjat on the Normal Summon only, or on the opponent's Standby Phases too.
                (Wdjat, "When you Normal Summon this card, select 1 Set card on your opponent's side of the field. Pick it " +
                        "up and look at it, then return it to its original position."),
                (Wdjat, "When you Normal Summon this card, and during each Standby Phase, select 1 Set card on your " +
                        "opponent's side of the field. Pick it up and look at it, then return it to its original position."),
                // Dustshoot with no hand-size condition, or any card.
                (Dustshoot, "Look at your opponent's hand, select 1 Monster Card in it, and return that card to its owner's " +
                            "Deck."),
                (Dustshoot, "Activate only if your opponent has 4 or more cards in their hand. Look at your opponent's hand, " +
                            "select 1 card in it, and return that card to its owner's Deck."),
            };
            foreach (var (like, text) in cases)
            {
                var prog = CardTextEffectCompiler.Compile(Synthetic(db.Get(like), text));
                Check($"near miss stays not FullyCompiled: {text}", prog == null || !prog.FullyCompiled, Shape(prog));
            }

            {
                // TargetNegationsAndHandQuickEffects batch (v163) implemented Fushioh Richie's other sentences (its negation
                // and its flip-up Zombie revival): it keeps this batch's flip-down ignition and gets none of its clauses.
                var prog = CardTextEffectCompiler.Compile(db.Get(FushiohRichie));
                Check("near miss: Fushioh Richie (more sentences) keeps only the flip-down ignition of this batch",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Count(c => c?.Action == EffectActionKind.SetThisFaceDownDefense && c.OncePerTurn) == 1 &&
                    prog.ClauseList.Exists(c =>
                        c?.Action == EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy) &&
                    !prog.ClauseList.Exists(c => c != null && batchActions.Contains(c.Action)),
                    Shape(prog));
            }

            foreach (var (id, name) in new[]
                     {
                         (ShiftingShadows, "Shifting Shadows")
                     })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                Check($"near miss: {name} stays not FullyCompiled / not Implemented",
                    prog != null && !prog.FullyCompiled && !Implemented(db, id) &&
                    !prog.ClauseList.Exists(c => c != null && batchActions.Contains(c.Action)),
                    Shape(prog));
            }

            {
                // Nightmare Penguin (c81306586) is a batch card now, not a near miss: its FLIP effect returns a card
                // the opponent controls to the hand. The face-up WATER aura is a different batch's clause and must not
                // leak into this one's near-miss set.
                var penguin = CardTextEffectCompiler.Compile(db.Get(NightmarePenguin));
                Check("Nightmare Penguin: FullyCompiled with the flip-return clause (no near miss)",
                    penguin != null && penguin.FullyCompiled && Implemented(db, NightmarePenguin) &&
                    penguin.ClauseList.Exists(c =>
                        c?.Action == EffectActionKind.ReturnToHand &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisFlipSummoned && c.AlsoWhenFlippedFaceUp &&
                        c.Zone == EffectZoneFilter.OppAnyCardOnField && c.TargetCount == 1) &&
                    !penguin.ClauseList.Exists(c => c != null && batchActions.Contains(c.Action)),
                    Shape(penguin));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(ForcefulSentry));
                Check("near miss: The Forceful Sentry keeps its any-card return (no monster filter, no condition)",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c => c?.Action == EffectActionKind.ReturnChosenToDeckShuffle) &&
                    !prog.ClauseList.Exists(c => c != null && batchActions.Contains(c.Action)),
                    Shape(prog));
            }
        }
    }
}
