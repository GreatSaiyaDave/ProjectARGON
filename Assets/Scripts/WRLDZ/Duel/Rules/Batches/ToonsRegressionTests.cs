using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Toons batch (v142 curriculum, SRL): Manga Ryu-Ran, Blue-Eyes Toon Dragon, Toon Mermaid and Toon Summoned
    /// Skull — the Toon Special Summon procedure from the hand while you control "Toon World" (with its Tributes,
    /// summon-negation window and "Must first be" revival gate), "Cannot attack the turn it is Special Summoned",
    /// the 500 LP attack cost, destruction when "Toon World" on the field is destroyed, and the direct-attack /
    /// Toon attack-target rule, the procedure's Tribute locks (Ojama Tokens yes, Archfiend's Roar no) and the AI's
    /// Battle Phase with a Toon-limited attacker. Compile shapes, mini-duels through the real engine entry points
    /// (TrySpecialSummonProcedure, ToggleTribute, TryActivateSpellTrap, TrySelectEffectTarget, TryEnterBattlePhase,
    /// TryAttack, RulesValidator, LegalIntentService) and fail-closed near misses. Runtime: <see cref="ToonsRuntime"/>.
    /// </summary>
    public static class ToonsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int ToonWorld = 15259703;          // Continuous Spell
        const int MangaRyuRan = 38369349;        // Lv7 2200/2600, Tribute 2
        const int BlueEyesToonDragon = 53183600; // Lv8 3000/2500, Tribute 2
        const int ToonMermaid = 65458948;        // Lv4 1400/1500, no Tribute
        const int ToonSummonedSkull = 91842653;  // Lv6 2500/1200, Tribute 1
        const int ToonGeminiElf = 42386471;      // "Cannot attack the turn it is Summoned." (not this batch)

        const int Celtic = 91152256;             // Lv4 1400/1200
        const int MysticalElf = 15025844;        // Lv4 800/2000
        const int DarkMagician = 46986414;       // Lv7 2500/2100
        const int Sangan = 26202165;             // field → GY: search
        const int Mst = 5318639;
        const int GiantTrunade = 42703248;
        const int MonsterReborn = 83764718;
        const int Solemn = 41420027;
        const int OjamaTrio = 29843091;          // Trap: 3 Ojama Tokens "cannot be Tributed for a Tribute Summon"
        const int ArchfiendsRoar = 56246017;     // Trap: revived Archfiend "cannot be Tributed" at all
        const int ArchfiendSoldier = 49881766;   // Lv4 Archfiend

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("Toons: " + name, ok, detail ?? "");

            void Guard(string label, Action body)
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    Check($"{label} (no exception)", false, ex.GetType().Name + ": " + ex.Message);
                }
            }

            Guard("compile shapes", () => CompileShapes(db, Check));
            Guard("procedure", () => ProcedureDuels(db, pDeck, aDeck, Check));
            Guard("tributes", () => TributeDuels(db, pDeck, aDeck, Check));
            Guard("tribute locks", () => TributeLockDuels(db, pDeck, aDeck, Check));
            Guard("ai battle", () => AiBattleDuels(db, pDeck, aDeck, Check));
            Guard("attack rule", () => AttackDuels(db, pDeck, aDeck, Check));
            Guard("toon world destroyed", () => ToonWorldDestroyedDuels(db, pDeck, aDeck, Check));
            Guard("revival gate", () => RevivalDuels(db, pDeck, aDeck, Check));
            Guard("summon negation", () => SummonNegationDuels(db, pDeck, aDeck, Check));
            Guard("near misses", () => NearMisses(db, Check));
        }

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static string State(DuelEngine e) =>
            $"phase={e.Phase} turn={e.TurnNumber} resp={e.PendingResponse?.Timing} pend={(e.PendingActivation != null)} " +
            $"chain={e.Chain.Count} awaiting={e.SummonAwaitingSuccess?.Name ?? "-"} " +
            $"P.LP={e.Player.LifePoints} O.LP={e.Opponent.LifePoints}";

        /// <summary>The player's own Main Phase 1 on a turn where a Battle Phase is allowed (turn 3+).</summary>
        static bool ReachPlayerMain1(DuelEngine engine)
        {
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
        }

        /// <summary>A fresh duel at the player's Main Phase 1 (turn 3+), boards, hands and GYs empty, 8000 LP each.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachPlayerMain1(engine);
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        /// <summary>A fresh duel at the opponent's (AI) Main Phase, boards, hands and GYs empty, 8000 LP each.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        static CardInstance PlaceFaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        /// <summary>Pass every open response window and finish any open battle (bounded).</summary>
        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 12; i++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                else if (engine.HasDeclaredAttack) engine.ResolveDeclaredAttack();
                else engine.RecoverStuckCombat();
            }
        }

        static bool OnField(DuelistState who, CardInstance c) => c != null && who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            void Shape(int id, string label, int tributes)
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                bool Has(Func<EffectClause, bool> f) => prog != null && prog.ClauseList.Exists(c => c != null && f(c));
                var ok = prog != null && prog.FullyCompiled &&
                         Has(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
                                  c.Amount == tributes && c.RequiresFaceUpName == "Toon World" && !c.MakesChainLink) &&
                         Has(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.CannotAttackTurnSpecialSummoned) &&
                         Has(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.PayLpToDeclareAttack && c.Amount == 500) &&
                         Has(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.DestroyThisWhenNamedDestroyed &&
                                  c.NamedCard == "Toon World" && !c.MakesChainLink) &&
                         Has(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                  c.Action == EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon) &&
                         !Has(c => c.Timing == EffectTiming.Activate) &&
                         CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
                Check($"{label} FullyCompiled: procedure (Tribute {tributes}, Toon World), no attack the SS turn, " +
                      "500 LP attack cost, destroyed with Toon World, Toon attack rule — Implemented",
                    ok, Describe(prog));
            }

            Shape(MangaRyuRan, "Manga Ryu-Ran", 2);
            Shape(BlueEyesToonDragon, "Blue-Eyes Toon Dragon", 2);
            Shape(ToonSummonedSkull, "Toon Summoned Skull", 1);
            Shape(ToonMermaid, "Toon Mermaid", 0);

            var ryuRan = CardTextEffectCompiler.Compile(db.Get(MangaRyuRan));
            Check("Manga Ryu-Ran keeps its \"always treated as a Toon card\" clause",
                ryuRan != null && ryuRan.ClauseList.Exists(c => c != null &&
                                                                 c.Action == EffectActionKind.AlwaysTreatedAsName &&
                                                                 c.TreatedAsName == "Toon"),
                Describe(ryuRan));
            var tss = CardTextEffectCompiler.Compile(db.Get(ToonSummonedSkull));
            Check("Toon Summoned Skull keeps its \"always treated as an Archfiend card\" clause",
                tss != null && tss.ClauseList.Exists(c => c != null &&
                                                           c.Action == EffectActionKind.AlwaysTreatedAsName &&
                                                           c.TreatedAsName == "Archfiend"),
                Describe(tss));

            var misses = SentenceAtomMisses(db, new[] { MangaRyuRan, BlueEyesToonDragon, ToonSummonedSkull, ToonMermaid },
                c => c.Action == EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon);
            Check("Toon attack-rule sentence family: all four Toons compile it", misses.Count == 0,
                string.Join(",", misses));
        }

        // ───────────────────────────── the procedure ─────────────────────────────

        static void ProcedureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Toon Mermaid: no Tribute. Needs a face-up Toon World you control; never Normal Summoned/Set.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mermaid = PutInHand(e, p, ToonMermaid);
                Check("Toon Mermaid: cannot be Normal Summoned/Set; no procedure without Toon World",
                    reached && !e.CanNormalSummonOrSet(p, mermaid) && !e.CanSpecialSummonProcedure(p, mermaid) &&
                    !e.TrySpecialSummonProcedure(p, mermaid) && p.Hand.Contains(mermaid),
                    State(e));

                // The opponent's Toon World does not count ("while you control").
                var oppWorld = PlaceFaceUpSpell(e, o, ToonWorld, 0);
                Check("Toon Mermaid: the opponent's Toon World does not open the procedure",
                    !e.CanSpecialSummonProcedure(p, mermaid), State(e));
                o.SpellTrapZones[0].Occupant = null;

                // A face-down (Set) Toon World does not count either.
                var setWorld = PlaceSetTrap(e, p, ToonWorld, 1);
                Check("Toon Mermaid: a Set Toon World does not open the procedure",
                    !e.CanSpecialSummonProcedure(p, mermaid), State(e));
                p.SpellTrapZones[1].Occupant = null;

                var world = PlaceFaceUpSpell(e, p, ToonWorld, 0);
                var snap = LegalIntentService.Build(e, p);
                Check("Toon Mermaid: with a face-up Toon World the Special Summon is offered (not Normal Summon)",
                    e.CanSpecialSummonProcedure(p, mermaid) &&
                    snap.HasKind(mermaid, LegalIntentService.LegalKind.SpecialSummon) &&
                    !snap.HasKind(mermaid, LegalIntentService.LegalKind.NormalSummonAtk),
                    State(e));
                var ok = e.TrySpecialSummonProcedure(p, mermaid);
                Settle(e);
                Check("Toon Mermaid: Special Summoned from the hand in face-up Attack Position, properly summoned, " +
                      "Normal Summon unused",
                    ok && OnField(p, mermaid) && mermaid.FaceUp && mermaid.Position == BattlePosition.Attack &&
                    mermaid.ProcedureCompleted && !p.Hand.Contains(mermaid) && !p.NormalSummonUsed &&
                    world.FaceUp && p.TryFindSpellTrap(world, out _),
                    $"ok={ok} {State(e)}");

                var entered = e.TryEnterBattlePhase(p);
                var attacked = e.TryAttack(p, mermaid, null);
                Settle(e);
                Check("Toon Mermaid: cannot attack the turn it is Special Summoned (no LP paid)",
                    entered && !e.CanAttack(p, mermaid) && !attacked && o.LifePoints == 8000 && p.LifePoints == 8000,
                    $"entered={entered} attacked={attacked} {State(e)}");
                _ = oppWorld;
                _ = setWorld;
            }

            // A full field: Toon Mermaid (no Tribute) has no zone.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceFaceUpSpell(e, p, ToonWorld, 0);
                for (var i = 0; i < p.MonsterZones.Length; i++)
                    PlaceMonster(e, p, Celtic, i, BattlePosition.Attack, true);
                var mermaid = PutInHand(e, p, ToonMermaid);
                Check("Toon Mermaid: no procedure while every Monster Zone is full",
                    reached && !e.CanSpecialSummonProcedure(p, mermaid), State(e));
            }

            // "Cannot attack the turn it is Special Summoned" applies to any Special Summon, and ends with the turn.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var skull = PlaceMonster(e, p, ToonSummonedSkull, 0, BattlePosition.Attack, true);
                skull.CannotAttackDuringTurnNumber = e.TurnNumber - 1;  // summoned last turn
                var entered = e.TryEnterBattlePhase(p);
                var ok = entered && e.CanAttack(p, skull);
                Check("Toon Summoned Skull: a Toon Special Summoned on an earlier turn may attack",
                    reached && ok, State(e));
                _ = o;
            }
        }

        // ───────────────────────────── Tributes ─────────────────────────────

        static void TributeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Manga Ryu-Ran (human): 3 candidates for 2 Tributes — the player taps them, then Special Summons again.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceFaceUpSpell(e, p, ToonWorld, 0);
                var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, false);
                var c = PlaceMonster(e, p, DarkMagician, 2, BattlePosition.Attack, true);
                var ryuRan = PutInHand(e, p, MangaRyuRan);
                var can = e.CanSpecialSummonProcedure(p, ryuRan);
                var first = e.TrySpecialSummonProcedure(p, ryuRan);
                Check("Manga Ryu-Ran (player): with a choice of Tributes, nothing is paid until they are picked",
                    reached && can && !first && p.Hand.Contains(ryuRan) && OnField(p, a) && OnField(p, b) &&
                    OnField(p, c) && p.Graveyard.Count == 0,
                    $"can={can} first={first} {State(e)}");
                e.ToggleTribute(p, a);
                e.ToggleTribute(p, b);
                var ok = e.TrySpecialSummonProcedure(p, ryuRan);
                Settle(e);
                Check("Manga Ryu-Ran (player): Tributes the 2 tapped monsters (a face-down one too) and is Special Summoned",
                    ok && OnField(p, ryuRan) && ryuRan.FaceUp && ryuRan.ProcedureCompleted &&
                    p.Graveyard.Contains(a) && p.Graveyard.Contains(b) && OnField(p, c) && !p.NormalSummonUsed &&
                    e.PendingTributes.Count == 0,
                    $"ok={ok} {State(e)}");
            }

            // Blue-Eyes Toon Dragon: exactly 2 monsters — no choice, both are Tributed. 1 monster is not enough.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceFaceUpSpell(e, p, ToonWorld, 0);
                var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var betd = PutInHand(e, p, BlueEyesToonDragon);
                Check("Blue-Eyes Toon Dragon: 1 monster cannot pay 2 Tributes",
                    reached && !e.CanSpecialSummonProcedure(p, betd), State(e));
                var b = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
                var ok = e.TrySpecialSummonProcedure(p, betd);
                Settle(e);
                Check("Blue-Eyes Toon Dragon: its only 2 monsters are Tributed and it is Special Summoned",
                    ok && OnField(p, betd) && p.Graveyard.Contains(a) && p.Graveyard.Contains(b) &&
                    betd.ProcedureCompleted,
                    $"ok={ok} {State(e)}");
            }

            // Toon Summoned Skull (AI): Tributes its lowest-ATK monster.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                PlaceFaceUpSpell(e, o, ToonWorld, 0);
                var strong = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var weak = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, true);
                var skull = PutInHand(e, o, ToonSummonedSkull);
                var wants = ToonsRuntime.AiWantsHandProcedure(e, o, skull);
                var ok = e.TrySpecialSummonProcedure(o, skull);
                Settle(e);
                Check("Toon Summoned Skull (AI): wants the procedure, Tributes its lowest-ATK monster",
                    reached && wants && ok && OnField(o, skull) && o.Graveyard.Contains(weak) && OnField(o, strong),
                    $"wants={wants} ok={ok} {State(e)}");
            }

            // The Tributes' "sent to the GY" triggers wait for the Summon (Sangan), and the Summon gets a
            // summon-negation window the player may pass.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var solemn = PlaceSetTrap(e, p, Solemn, 0);
                PlaceFaceUpSpell(e, o, ToonWorld, 0);
                var sangan = PlaceMonster(e, o, Sangan, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, o, ToonSummonedSkull);
                var handBefore = o.HandCount;
                var deckBefore = o.Deck.Count;
                var searchable = o.Deck.Any(id => db.TryGet(id, out var d) && d != null && d.IsMonster &&
                                                  !d.IsExtraDeck && d.atk >= 0 && d.atk <= 1500);
                var ok = reached && e.TrySpecialSummonProcedure(o, skull);
                var window = e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.SummonNegation &&
                             e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(solemn) &&
                             e.SummonAwaitingSuccess == skull && o.Graveyard.Contains(sangan) &&
                             o.HandCount == handBefore - 1 && o.Deck.Count == deckBefore;
                Check("Toon Summoned Skull (AI) Tributing Sangan: the summon-negation window opens first; " +
                      "Sangan's search waits",
                    ok && window, $"ok={ok} hand={o.HandCount}/{handBefore} {State(e)}");
                if (e.IsAwaitingResponse) e.PassResponse();
                Settle(e);
                var searched = o.Deck.Count == deckBefore - 1 && o.HandCount == handBefore;
                Check("Toon Summoned Skull (AI): the player passes — the Summon succeeds, then Sangan's search activates",
                    OnField(o, skull) && skull.ProcedureCompleted && e.SummonAwaitingSuccess == null &&
                    (searchable ? searched : o.Deck.Count == deckBefore) && !solemn.FaceUp,
                    $"searchable={searchable} deck={o.Deck.Count}/{deckBefore} hand={o.HandCount}/{handBefore} {State(e)}");
            }
        }

        // ───────────────────────────── Tribute locks (review fix) ─────────────────────────────

        /// <summary>
        /// The procedure is a non-Summon Tribute (ygopro SelectReleaseGroup): "cannot be Tributed for a Tribute Summon"
        /// (EFFECT_UNRELEASABLE_SUM: Ojama / Sheep / Kuriboh Tokens, Fox Fire) does not stop it; "cannot be Tributed"
        /// (Archfiend's Roar, EFFECT_UNRELEASABLE_NONSUM too) does.
        /// </summary>
        static void TributeLockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Ojama Trio against a Toon deck: the AI Special Summons Toon Summoned Skull by Tributing an Ojama Token.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceFaceUpSpell(e, o, ToonWorld, 0);
                var trio = PlaceSetTrap(e, p, OjamaTrio, 0);
                var trioProg = CompiledEffectCache.GetOrCompile(trio.Def);
                TextEffectRuntime.TryResolveActivation(e, p, trio, false, trioProg, true);
                Settle(e);
                var tokens = o.MonstersOnField().Where(m => m != null && m.IsToken).ToList();
                var skull = PutInHand(e, o, ToonSummonedSkull);
                var can = e.CanSpecialSummonProcedure(o, skull);
                var wants = ToonsRuntime.AiWantsHandProcedure(e, o, skull);
                var ok = can && e.TrySpecialSummonProcedure(o, skull);
                Settle(e);
                var tokensLeft = o.MonstersOnField().Count(m => m != null && m.IsToken);
                Check("Toon Summoned Skull (AI): an Ojama Token (cannot be Tributed for a Tribute Summon) pays the procedure",
                    reached && tokens.Count == 3 && tokens.All(t => t.CannotBeTributedForSummon) && can && wants && ok &&
                    OnField(o, skull) && skull.ProcedureCompleted && tokensLeft == 2,
                    $"tokens={tokens.Count} can={can} wants={wants} ok={ok} left={tokensLeft} {State(e)}");
                Check("Ojama Token: still cannot be Tributed for a Tribute Summon",
                    tokens.Count > 0 && tokens.Where(t => o.TryFindMonster(t, out _))
                        .All(t => !TcgRules.CanBeTributedForSummon(o, t)),
                    State(e));
            }

            // Archfiend's Roar: the revived Archfiend cannot be Tributed — not even for the procedure.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceFaceUpSpell(e, p, ToonWorld, 0);
                var soldier = e.CreateCardInstance(ArchfiendSoldier);
                p.Graveyard.Add(soldier);
                var roar = PlaceSetTrap(e, p, ArchfiendsRoar, 1);
                var roarProg = CompiledEffectCache.GetOrCompile(roar.Def);
                TextEffectRuntime.TryResolveActivation(e, p, roar, false, roarProg, true);
                if (e.IsAwaitingEffectTarget) TextEffectRuntime.TryResolveTextTarget(e, soldier);
                Settle(e);
                var skull = PutInHand(e, p, ToonSummonedSkull);
                var canWithRoarOnly = e.CanSpecialSummonProcedure(p, skull);
                Check("Toon Summoned Skull: a monster revived by Archfiend's Roar cannot pay the procedure's Tribute",
                    reached && OnField(p, soldier) && soldier.CannotBeTributed && !canWithRoarOnly,
                    $"soldierOnField={OnField(p, soldier)} lock={soldier.CannotBeTributed} can={canWithRoarOnly} {State(e)}");
                var freeZone = e.FirstEmpty(p.MonsterZones);
                var celtic = PlaceMonster(e, p, Celtic, freeZone, BattlePosition.Attack, true);
                var ok = e.TrySpecialSummonProcedure(p, skull);
                Settle(e);
                Check("Toon Summoned Skull: with Celtic too, only Celtic can be (and is) Tributed — no choice to make",
                    ok && OnField(p, skull) && p.Graveyard.Contains(celtic) && OnField(p, soldier),
                    $"ok={ok} skull={OnField(p, skull)} celticGy={p.Graveyard.Contains(celtic)} soldier={OnField(p, soldier)} " +
                    $"mon=[{string.Join(",", p.MonstersOnField().Select(m => m.Name))}] gy=[{string.Join(",", p.Graveyard.Select(m => m.Name))}] {State(e)}");
            }
        }

        // ───────────────────────────── AI battle (review fix) ─────────────────────────────

        /// <summary>Step SimpleAi's turn synchronously; the player passes every window (ChainRulesRegressionTests' driver).</summary>
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

        static void AiBattleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI's strongest attacker is a Toon limited to the player's stronger face-up Toon: it holds back, and
            // the weaker Celtic still attacks the 800-ATK Mystical Elf.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ryuRan = PlaceMonster(e, o, MangaRyuRan, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var skull = PlaceMonster(e, p, ToonSummonedSkull, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
                var done = RunAiTurn(e, 400);
                Check("AI: a Toon with no good Toon target does not stop its weaker monsters from attacking",
                    reached && done && p.Graveyard.Contains(elf) && OnField(p, skull) && OnField(o, ryuRan) &&
                    o.LifePoints == 8000,
                    $"done={done} celticAttacks={celtic.AttacksDeclaredThisTurn} {State(e)}");
            }
        }

        // ───────────────────────────── attack rule / cost ─────────────────────────────

        static void AttackDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // No Toon on the other side: it may attack directly even though the opponent controls monsters, and pays
            // 500 LP to declare. It may still attack a monster instead.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mermaid = PlaceMonster(e, p, ToonMermaid, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var faceDownToon = PlaceMonster(e, o, ToonMermaid, 1, BattlePosition.Defense, false);
                var entered = e.TryEnterBattlePhase(p);
                var direct = e.CanAttackDirectly(p, mermaid);
                var vCeltic = RulesValidator.ValidateAttack(e, p, mermaid, celtic);
                var vDirect = RulesValidator.ValidateAttack(e, p, mermaid, null);
                var snap = LegalIntentService.Build(e, p);
                Check("Toon Mermaid: no face-up Toon opposite (a face-down one does not count) — may attack directly " +
                      "or attack a monster",
                    reached && entered && direct && vDirect.Legal && vCeltic.Legal &&
                    snap.HasKind(mermaid, LegalIntentService.LegalKind.DirectAttack) &&
                    snap.Entries.Exists(x => x.Card == mermaid && x.Kind == LegalIntentService.LegalKind.Attack &&
                                             x.Target == celtic),
                    $"direct={direct} vDirect={vDirect.Reason} vCeltic={vCeltic.Reason} {State(e)}");
                var ok = e.TryAttack(p, mermaid, null);
                Settle(e);
                Check("Toon Mermaid: direct attack over the opponent's monsters — pays 500 LP, deals 1400",
                    ok && p.LifePoints == 7500 && o.LifePoints == 6600 && OnField(o, celtic),
                    $"ok={ok} {State(e)}");
                _ = faceDownToon;
            }

            // The opponent controls a face-up Toon: no direct attack, only a face-up Toon may be attacked.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var betd = PlaceMonster(e, p, BlueEyesToonDragon, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(e, o, ToonSummonedSkull, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var faceDown = PlaceMonster(e, o, ToonMermaid, 2, BattlePosition.Defense, false);
                var entered = e.TryEnterBattlePhase(p);
                var direct = e.CanAttackDirectly(p, betd);
                var tryDirect = e.TryAttack(p, betd, null);
                var tryCeltic = e.TryAttack(p, betd, celtic);
                var tryFaceDown = e.TryAttack(p, betd, faceDown);
                var vCeltic = RulesValidator.ValidateAttack(e, p, betd, celtic);
                var vDirect = RulesValidator.ValidateAttack(e, p, betd, null);
                var snap = LegalIntentService.Build(e, p);
                var offered = snap.Entries.Where(x => x.Card == betd &&
                                                      x.Kind == LegalIntentService.LegalKind.Attack)
                    .Select(x => x.Target).ToList();
                Check("Blue-Eyes Toon Dragon vs a face-up Toon: no direct attack, no non-Toon or face-down target " +
                      "(nothing paid)",
                    reached && entered && !direct && !tryDirect && !tryCeltic && !tryFaceDown && !vCeltic.Legal &&
                    !vDirect.Legal && !snap.HasKind(betd, LegalIntentService.LegalKind.DirectAttack) &&
                    offered.Count == 1 && offered[0] == skull && p.LifePoints == 8000 && !e.HasDeclaredAttack,
                    $"direct={direct} offered={offered.Count} {State(e)}");
                var ok = e.TryAttack(p, betd, skull);
                Settle(e);
                Check("Blue-Eyes Toon Dragon attacks the Toon: pays 500 LP, destroys Toon Summoned Skull (500 damage)",
                    ok && p.LifePoints == 7500 && o.LifePoints == 7500 && o.Graveyard.Contains(skull) && OnField(p, betd),
                    $"ok={ok} {State(e)}");
            }

            // 500 LP attack cost: not payable → cannot attack.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var skull = PlaceMonster(e, p, ToonSummonedSkull, 0, BattlePosition.Attack, true);
                var entered = e.TryEnterBattlePhase(p);
                p.LifePoints = 400;
                var blocked = !e.CanAttack(p, skull) && !e.TryAttack(p, skull, null);
                p.LifePoints = 500;
                var can = e.CanAttack(p, skull);
                Check("Toon Summoned Skull: cannot declare an attack with less than 500 LP; 500 LP is payable",
                    reached && entered && blocked && can && o.LifePoints == 8000, $"{State(e)}");
            }
        }

        // ───────────────────────────── Toon World destroyed ─────────────────────────────

        static void ToonWorldDestroyedDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // MST destroys the opponent's Toon World: every face-up Toon on the field is destroyed (both sides);
            // a face-down Toon and a non-Toon stay.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var world = PlaceFaceUpSpell(e, o, ToonWorld, 0);
                var ryuRan = PlaceMonster(e, o, MangaRyuRan, 0, BattlePosition.Attack, true);
                var faceDownToon = PlaceMonster(e, o, ToonMermaid, 1, BattlePosition.Defense, false);
                var celtic = PlaceMonster(e, o, Celtic, 2, BattlePosition.Attack, true);
                var myToon = PlaceMonster(e, p, BlueEyesToonDragon, 0, BattlePosition.Attack, true);
                var mst = PutInHand(e, p, Mst);
                var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                var picked = act && (!e.IsAwaitingEffectTarget ||
                                     (e.IsLegalEffectTarget(world) && e.TrySelectEffectTarget(world)));
                Settle(e);
                Check("MST on Toon World: Manga Ryu-Ran and the player's Blue-Eyes Toon Dragon are destroyed with it",
                    reached && picked && o.Graveyard.Contains(world) && o.Graveyard.Contains(ryuRan) &&
                    p.Graveyard.Contains(myToon) && !OnField(o, ryuRan) && !OnField(p, myToon),
                    $"act={act} picked={picked} {State(e)}");
                Check("MST on Toon World: a face-down Toon and a non-Toon monster stay",
                    OnField(o, faceDownToon) && OnField(o, celtic), State(e));
            }

            // Toon World leaves without being destroyed (Giant Trunade): the Toons stay.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var world = PlaceFaceUpSpell(e, p, ToonWorld, 0);
                var mermaid = PlaceMonster(e, p, ToonMermaid, 0, BattlePosition.Attack, true);
                var trunade = PutInHand(e, p, GiantTrunade);
                var act = e.TryActivateSpellTrap(p, trunade, fromHand: true);
                Settle(e);
                Check("Giant Trunade returns Toon World to the hand: not destroyed, Toon Mermaid stays",
                    reached && act && p.Hand.Contains(world) && OnField(p, mermaid),
                    $"act={act} {State(e)}");
            }

            // Toon World destroyed while it is face-down: nothing else is destroyed.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var setWorld = PlaceSetTrap(e, o, ToonWorld, 0);
                var faceUpWorld = PlaceFaceUpSpell(e, p, ToonWorld, 1);
                var mermaid = PlaceMonster(e, p, ToonMermaid, 0, BattlePosition.Attack, true);
                var mst = PutInHand(e, p, Mst);
                var act = e.TryActivateSpellTrap(p, mst, fromHand: true);
                var picked = act && (!e.IsAwaitingEffectTarget ||
                                     (e.IsLegalEffectTarget(setWorld) && e.TrySelectEffectTarget(setWorld)));
                Settle(e);
                Check("MST on a Set Toon World: the Toons stay",
                    reached && picked && o.Graveyard.Contains(setWorld) && OnField(p, mermaid) &&
                    p.TryFindSpellTrap(faceUpWorld, out _),
                    $"act={act} picked={picked} {State(e)}");
            }
        }

        // ───────────────────────────── "Must first be" revival gate ─────────────────────────────

        static void RevivalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A Toon that was never properly summoned (discarded) cannot be revived; Monster Reborn does not offer it.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var discarded = PutInHand(e, p, ToonMermaid);
                e.SendCardToGrave(p, discarded);
                var celticGy = PutInHand(e, p, Celtic);
                e.SendCardToGrave(p, celticGy);
                var reborn = PutInHand(e, p, MonsterReborn);
                var act = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                var offered = e.PendingActivation?.LegalTargets?.ToList();
                var mermaidOffered = offered != null && offered.Contains(discarded);
                if (e.IsAwaitingEffectTarget)
                    e.TrySelectEffectTarget(celticGy);
                Settle(e);
                Check("Monster Reborn: a Toon Mermaid that was never properly summoned is not a legal target",
                    reached && act && !discarded.ProcedureCompleted && !mermaidOffered &&
                    p.Graveyard.Contains(discarded) && OnField(p, celticGy),
                    $"act={act} offered={offered?.Count} names={string.Join(",", offered?.Select(x => x.Name) ?? new string[0])} {State(e)}");

                // The engine gate: no effect Special Summons a hand copy either.
                var inHand = PutInHand(e, p, ToonMermaid);
                PlaceFaceUpSpell(e, p, ToonWorld, 3);
                p.Hand.Remove(inHand);
                var placed = e.SpecialSummonToField(p, inHand, BattlePosition.Attack, true);
                Check("Toon Mermaid: an effect cannot Special Summon a hand copy (only its own procedure)",
                    !placed && !OnField(p, inHand), State(e));
            }

            // A properly summoned Toon destroyed and revived by Monster Reborn: legal, and it cannot attack that turn.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceFaceUpSpell(e, p, ToonWorld, 0);
                var mermaid = PutInHand(e, p, ToonMermaid);
                var summoned = e.TrySpecialSummonProcedure(p, mermaid);
                Settle(e);
                e.DestroyMonsterPublic(p, mermaid);
                var reborn = PutInHand(e, p, MonsterReborn);
                var act = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                var picked = act && (!e.IsAwaitingEffectTarget ||
                                     (e.IsLegalEffectTarget(mermaid) && e.TrySelectEffectTarget(mermaid)));
                Settle(e);
                var entered = e.TryEnterBattlePhase(p);
                Check("Monster Reborn revives a properly summoned Toon Mermaid; it cannot attack that turn",
                    reached && summoned && picked && OnField(p, mermaid) && mermaid.FaceUp && entered &&
                    !e.CanAttack(p, mermaid),
                    $"summoned={summoned} act={act} picked={picked} {State(e)}");
            }
        }

        // ───────────────────────────── summon negation ─────────────────────────────

        static void SummonNegationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // AI Solemn Judgment negates the player's Toon Summoned Skull: the Tribute stays paid, the Toon was never
            // properly summoned (Monster Reborn cannot revive it).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var solemn = PlaceSetTrap(e, o, Solemn, 0);
                PlaceFaceUpSpell(e, p, ToonWorld, 0);
                var tribute = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, p, ToonSummonedSkull);
                var ok = e.TrySpecialSummonProcedure(p, skull);
                Settle(e);
                var negated = !OnField(p, skull) && p.Graveyard.Contains(skull) && o.Graveyard.Contains(solemn);
                Check("Solemn Judgment (AI) negates the Toon procedure: Toon Summoned Skull destroyed, Tribute paid, " +
                      "not properly summoned",
                    reached && ok && negated && p.Graveyard.Contains(tribute) && !skull.ProcedureCompleted &&
                    o.LifePoints == 4000 && e.SummonAwaitingSuccess == null,
                    $"ok={ok} {State(e)}");
                var reborn = PutInHand(e, p, MonsterReborn);
                var act = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                var offered = e.PendingActivation?.LegalTargets?.ToList();
                var skullOffered = offered != null && offered.Contains(skull);
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                Settle(e);
                Check("Monster Reborn cannot revive a Toon whose own Summon was negated",
                    negated && !skullOffered && !OnField(p, skull),
                    $"act={act} offered={offered?.Count} {State(e)}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static CompiledCardProgram ToonText(int id, string name, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = name,
                type = "Toon Monster",
                race = "Dragon",
                frameType = "effect",
                attribute = "LIGHT",
                level = 7,
                atk = 2000,
                def = 2000,
                desc = desc
            });

        static void NearMisses(CardDatabase db, Chk Check)
        {
            bool Has(CompiledCardProgram prog, EffectActionKind k) =>
                prog != null && prog.ClauseList.Exists(c => c != null && c.Action == k);

            // Toon Gemini Elf: "Cannot attack the turn it is Summoned." is not the Special-Summon-only sentence; the
            // card stays unimplemented (its other sentences are not done here).
            var gemini = CardTextEffectCompiler.Compile(db.Get(ToonGeminiElf));
            Check("Near miss: Toon Gemini Elf's \"the turn it is Summoned\" does not compile as the SS-turn lock; " +
                  "the card stays not FullyCompiled",
                gemini != null && !gemini.FullyCompiled &&
                !Has(gemini, EffectActionKind.CannotAttackTurnSpecialSummoned) &&
                CardEffectStatus.Classify(db.Get(ToonGeminiElf)) != CardEffectStatusKind.Implemented,
                Describe(gemini));

            // The procedure sentence with an extra condition is not the procedure.
            var extra = ToonText(90142001, "Near-miss Toon procedure",
                "Cannot be Normal Summoned/Set. Must first be Special Summoned (from your hand) by Tributing 2 " +
                "monsters, while you control \"Toon World\" and your opponent controls a monster.");
            Check("Near miss: a procedure sentence with an extra condition does not compile as the Toon procedure",
                !Has(extra, EffectActionKind.SpecialSummonFromHandProcedure), Describe(extra));

            // Gate Guardian's named Tributes are not "N monsters, while you control".
            var gate = CardTextEffectCompiler.Compile(db.Get(25833572));
            Check("Near miss: Gate Guardian's named-Tribute procedure does not compile as the Toon procedure",
                !Has(gate, EffectActionKind.SpecialSummonFromHandProcedure), Describe(gate));

            // A destroy-with rider on a longer sentence.
            var rider = ToonText(90142002, "Near-miss Toon destroy rider",
                "If \"Toon World\" on the field is destroyed, destroy this card, and if you do, draw 1 card.");
            Check("Near miss: \"If X on the field is destroyed, destroy this card, and …\" stays uncompiled",
                !Has(rider, EffectActionKind.DestroyThisWhenNamedDestroyed) && !(rider?.FullyCompiled ?? false),
                Describe(rider));

            // Half of the attack-rule sentence.
            var half = ToonText(90142003, "Near-miss Toon direct",
                "Can attack your opponent directly, unless they control a Toon monster.");
            Check("Near miss: half of the Toon attack-rule sentence does not compile the rule",
                !Has(half, EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon) &&
                !(half?.FullyCompiled ?? false),
                Describe(half));

            // The SS-turn lock inside a longer sentence.
            var longer = ToonText(90142004, "Near-miss SS-turn lock",
                "Cannot attack the turn it is Special Summoned, unless you control \"Toon World\".");
            Check("Near miss: the SS-turn lock with an \"unless\" does not compile",
                !Has(longer, EffectActionKind.CannotAttackTurnSpecialSummoned), Describe(longer));
        }
    }
}
