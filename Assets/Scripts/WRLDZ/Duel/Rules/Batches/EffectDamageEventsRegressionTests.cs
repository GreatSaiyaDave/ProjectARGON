using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// EffectDamageEvents batch (v162): the central effect-damage event — Barrel Behind the Door, Trap of Board Eraser, Dark
    /// Room of Nightmare, Gravekeeper's Vassal and Coffin Seller. Compile shapes, mini-duels through the real engine entry
    /// points (TryActivateSpellTrap in open state and chain windows, the AI's chain answers, TrySelectEffectTarget for a
    /// held discard pick, TryAttack), chain-window legality and near misses.
    /// Runtime: <see cref="EffectDamageEventsRuntime"/>.
    /// </summary>
    public static class EffectDamageEventsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Barrel = 78783370;
        const int BoardEraser = 3055837;
        const int DarkRoom = 85562745;
        const int Vassal = 99690140;
        const int CoffinSeller = 65830223;

        const int Hinotama = 46130346;          // 500 to the opponent
        const int TremendousFire = 46918794;    // 1000 to the opponent, 500 to you
        const int RingOfDestruction = 83555666; // opponent's turn; both take the target's ATK
        const int PotOfGreed = 55144522;
        const int DarkHole = 53129443;
        const int Celtic = 91152256;            // 1400/1200
        const int Kuriboh = 40640057;           // 300/200
        const int Mst = 5318639;
        const int MagicCylinder = 62279055;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("EffectDamageEvents: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            BarrelDuels(db, pDeck, aDeck, Check);
            BoardEraserDuels(db, pDeck, aDeck, Check);
            ChainLegality(db, pDeck, aDeck, Check);
            DarkRoomDuels(db, pDeck, aDeck, Check);
            VassalDuels(db, pDeck, aDeck, Check);
            CoffinSellerDuels(db, pDeck, aDeck, Check);
            NearMisses(Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DuelEngine, System.Collections.Generic.List<string>>
            s_logs = new();

        static DuelEngine Watch(DuelEngine e)
        {
            var lines = s_logs.GetValue(e, _ => new System.Collections.Generic.List<string>());
            e.OnLog += lines.Add;
            return e;
        }

        static string State(DuelEngine e)
        {
            var lines = s_logs.TryGetValue(e, out var l) ? l : new System.Collections.Generic.List<string>();
            return $"pLP={e.Player.LifePoints} oLP={e.Opponent.LifePoints} chain={e.Chain.Count} " +
                   $"resp={e.PendingResponse?.Timing} pend={(e.PendingActivation != null)} " +
                   $"log={string.Join(" / ", lines.Skip(Math.Max(0, lines.Count - 8)))}";
        }

        /// <summary>The player's Main Phase 1 on turn 3+ (a Battle Phase is allowed), boards and hands empty.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
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

        /// <summary>The AI's Main Phase (turn 2+), boards and hands empty.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
            engine.NotifyPublic();
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static bool ChainWindowOffers(DuelEngine e, DuelistState who, CardInstance card) =>
            e.PendingResponse?.Timing == ResponseTiming.ChainResponse && e.PendingResponse.Responder == who &&
            e.PendingResponse.LegalCards.Contains(card);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, label, timing, action, amount) in new[]
                     {
                         (Barrel, "Barrel Behind the Door", EffectTiming.ChainLinkActivated,
                             EffectActionKind.ReflectEffectDamageOfAnsweredLink, 0),
                         (BoardEraser, "Trap of Board Eraser", EffectTiming.ChainLinkActivated,
                             EffectActionKind.NegateEffectDamageOfAnsweredLinkThenOpponentDiscards, 0),
                         (DarkRoom, "Dark Room of Nightmare", EffectTiming.ContinuousWhileFaceUp,
                             EffectActionKind.InflictDamageEachTimeOpponentTakesEffectDamage, 300),
                         (Vassal, "Gravekeeper's Vassal", EffectTiming.ContinuousWhileFaceUp,
                             EffectActionKind.BattleDamageThisInflictsIsEffectDamage, 0),
                         (CoffinSeller, "Coffin Seller", EffectTiming.ContinuousWhileFaceUp,
                             EffectActionKind.InflictDamageEachTimeMonsterSentToOpponentGy, 300),
                     })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check($"{label}: one whole-text clause {timing} / {action}",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == timing && c.Action == action &&
                    c.Amount == amount && (timing != EffectTiming.ChainLinkActivated || c.ChainResponseOnly),
                    Describe(prog));
                var status = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
                Check($"{label}: Implemented and deck-legal",
                    status == CardEffectStatusKind.Implemented && CardEffectStatus.MayIncludeInDeck(def),
                    status.ToString());
            }
        }

        // ───────────────────────────── Barrel Behind the Door ─────────────────────────────

        static void BarrelDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's Barrel answers the AI's Tremendous Fire (1000 to the player, 500 to the AI): the player's 1000
            // goes to the AI, which takes both.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var barrel = PlaceSetTrap(e, p, Barrel, 0);
                var fire = PutInHand(e, o, TremendousFire);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, fire, fromHand: true);
                var offered = act && ChainWindowOffers(e, p, barrel);
                var answered = offered && e.TryActivateSpellTrap(p, barrel, fromHand: false);
                Settle(e);
                Check("Barrel (player) vs Tremendous Fire: the player takes nothing, the AI takes 1000 + 500",
                    answered && p.LifePoints == pLp && o.LifePoints == oLp - 1500 && p.Graveyard.Contains(barrel) &&
                    o.Graveyard.Contains(fire) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"act={act} offered={offered} answered={answered} {State(e)}");
            }

            // The AI's Barrel answers the player's Ring of Destruction (the AI's turn): the AI's damage goes to the player,
            // who takes the ATK twice.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ring = PlaceSetTrap(e, p, RingOfDestruction, 0);
                var barrel = PlaceSetTrap(e, o, Barrel, 0);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(p, ring, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(celtic);
                Settle(e);
                Check("Barrel (AI) vs Ring of Destruction: the AI chains it and the player takes 1400 twice",
                    act && o.Graveyard.Contains(barrel) && o.Graveyard.Contains(celtic) &&
                    p.LifePoints == pLp - 2800 && o.LifePoints == oLp && !e.Chain.HasLinks,
                    $"act={act} {State(e)}");
            }

            // The AI's Barrel answers the player's Hinotama: the player takes the 500.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var barrel = PlaceSetTrap(e, o, Barrel, 0);
                var hino = PutInHand(e, p, Hinotama);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(p, hino, fromHand: true);
                Settle(e);
                Check("Barrel (AI) vs Hinotama: the player takes the 500 instead",
                    act && o.Graveyard.Contains(barrel) && p.LifePoints == pLp - 500 && o.LifePoints == oLp &&
                    !e.Chain.HasLinks,
                    $"act={act} {State(e)}");
            }

            // The AI's Barrel answers the player's Magic Cylinder (a Trap seated in the attack window): the AI's attacker's
            // ATK goes back to the player.
            {
                var e = Watch(Fresh(db, pDeck, aDeck));
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var cylinder = PlaceSetTrap(e, p, MagicCylinder, 0);
                var barrel = PlaceSetTrap(e, o, Barrel, 0);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var atk = reached && e.TryAttack(o, celtic, null);
                var offered = atk && e.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                              e.PendingResponse.LegalCards.Contains(cylinder);
                var answered = offered && e.TryActivateSpellTrap(p, cylinder, fromHand: false);
                Settle(e);
                DrainCombat(e);
                Check("Barrel (AI) vs Magic Cylinder: the 1400 goes back to the player",
                    answered && o.Graveyard.Contains(barrel) && p.LifePoints == pLp - 1400 && o.LifePoints == oLp &&
                    !e.Chain.HasLinks,
                    $"atk={atk} offered={offered} answered={answered} {State(e)}");
            }

            // The AI keeps its Barrel against a card that inflicts no damage to it (Pot of Greed).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var barrel = PlaceSetTrap(e, o, Barrel, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                var act = reached && e.TryActivateSpellTrap(p, pot, fromHand: true);
                Settle(e);
                Check("Barrel (AI): not used against Pot of Greed",
                    act && o.TryFindSpellTrap(barrel, out _) && !barrel.FaceUp && !e.Chain.HasLinks,
                    $"act={act} {State(e)}");
            }

            // Outside a chain it cannot be activated at all.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var barrel = PlaceSetTrap(e, e.Player, Barrel, 0);
                var can = reached && e.CanActivateSpellTrap(e.Player, barrel, fromHand: false);
                Check("Barrel: no activation in the open state", reached && !can && !barrel.FaceUp, $"can={can}");
            }
        }

        // ───────────────────────────── Trap of Board Eraser ─────────────────────────────

        static void BoardEraserDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's Board Eraser vs the AI's Hinotama: no damage, the AI discards 1 card (its choice).
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var eraser = PlaceSetTrap(e, p, BoardEraser, 0);
                var hino = PutInHand(e, o, Hinotama);
                PutInHand(e, o, Celtic);
                PutInHand(e, o, Mst);
                var pLp = p.LifePoints;
                var oGy = o.Graveyard.Count;
                var act = reached && e.TryActivateSpellTrap(o, hino, fromHand: true);
                var offered = act && ChainWindowOffers(e, p, eraser);
                var answered = offered && e.TryActivateSpellTrap(p, eraser, fromHand: false);
                Settle(e);
                Check("Board Eraser (player) vs Hinotama: no damage, the AI discards 1 of its 2 cards",
                    answered && p.LifePoints == pLp && o.Hand.Count == 1 && o.Graveyard.Count == oGy + 2 &&
                    o.Graveyard.Contains(hino) && p.Graveyard.Contains(eraser) && !e.Chain.HasLinks &&
                    !e.IsAwaitingEffectTarget,
                    $"act={act} offered={offered} answered={answered} hand={o.Hand.Count} {State(e)}");
            }

            // The AI's Board Eraser vs the player's Hinotama: the chain waits for the player's discard pick, then the 500 to
            // the AI is negated.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var eraser = PlaceSetTrap(e, o, BoardEraser, 0);
                var hino = PutInHand(e, p, Hinotama);
                var celtic = PutInHand(e, p, Celtic);
                var pot = PutInHand(e, p, PotOfGreed);
                var oLp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(p, hino, fromHand: true);
                for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
                var held = e.PendingActivation != null && e.PendingActivation.AwaitingForcedDiscardChoice &&
                           e.PendingActivation.Controller == p && e.Chain.HasLinks && o.LifePoints == oLp;
                var picked = held && e.TrySelectEffectTarget(celtic);
                Settle(e);
                Check("Board Eraser (AI) vs Hinotama: the player picks the discard, then the damage is negated",
                    held && picked && p.Graveyard.Contains(celtic) && p.Hand.Contains(pot) && o.LifePoints == oLp &&
                    o.Graveyard.Contains(eraser) && p.Graveyard.Contains(hino) && !e.Chain.HasLinks &&
                    !e.IsAwaitingEffectTarget,
                    $"act={act} held={held} picked={picked} {State(e)}");
            }
        }

        // ───────────────────────────── chain-window legality ─────────────────────────────

        static void ChainLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var barrel = PlaceSetTrap(e, p, Barrel, 0);
            var eraser = PlaceSetTrap(e, p, BoardEraser, 1);

            System.Collections.Generic.List<CardInstance> LegalAgainst(DuelistState controller, int cardId)
            {
                e.Chain.Clear();
                var c = e.CreateCardInstance(cardId);
                c.FaceUp = true;
                controller.SpellTrapZones[4].Occupant = c;
                e.Chain.BeginBuilding();
                var link = e.Chain.AddLink(controller, c, OfficialEffectRegistry.SpeedOf(c.Def),
                    OfficialEffectRegistry.ClassOfActivation(c.Def, false), CardLocation.Hand, true, false, "t");
                e.SetPendingResponse(new PendingResponse
                    { Timing = ResponseTiming.ChainResponse, Responder = p, ChainTarget = link });
                var legal = SpellTrapEffects.CollectLegalResponseCards(e, p, ResponseTiming.ChainResponse, null);
                e.ClearPendingResponse();
                e.Chain.Clear();
                controller.SpellTrapZones[4].Occupant = null;
                return legal;
            }

            PutInHand(e, o, Celtic);
            var oppBurn = LegalAgainst(o, Hinotama);
            Check("legality: the AI's Hinotama (damage to you) — Barrel and Board Eraser answer",
                oppBurn.Contains(barrel) && oppBurn.Contains(eraser), string.Join(",", oppBurn.Select(c => c.Name)));
            var ownBurn = LegalAgainst(p, Hinotama);
            Check("legality: your own Hinotama (damage to the opponent) — neither answers",
                !ownBurn.Contains(barrel) && !ownBurn.Contains(eraser), string.Join(",", ownBurn.Select(c => c.Name)));
            var ownFire = LegalAgainst(p, TremendousFire);
            Check("legality: your own Tremendous Fire (500 to you) — both answer",
                ownFire.Contains(barrel) && ownFire.Contains(eraser), string.Join(",", ownFire.Select(c => c.Name)));
            var noBurn = LegalAgainst(o, PotOfGreed);
            Check("legality: the AI's Pot of Greed (no damage) — neither answers",
                !noBurn.Contains(barrel) && !noBurn.Contains(eraser), string.Join(",", noBurn.Select(c => c.Name)));
            o.Hand.Clear();
            var emptyHand = LegalAgainst(o, Hinotama);
            Check("legality: the AI's Hinotama with the AI's hand empty — Barrel answers, Board Eraser does not",
                emptyHand.Contains(barrel) && !emptyHand.Contains(eraser),
                string.Join(",", emptyHand.Select(c => c.Name)));
        }

        // ───────────────────────────── Dark Room of Nightmare ─────────────────────────────

        static void DarkRoomDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Activated from the hand it only stays on the field (no damage); then Hinotama's 500 triggers it once (300),
            // and its own 300 does not trigger it again.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var room = PutInHand(e, p, DarkRoom);
                var hino = PutInHand(e, p, Hinotama);
                var oLp = o.LifePoints;
                var placed = reached && e.TryActivateSpellTrap(p, room, fromHand: true);
                Settle(e);
                var onField = p.TryFindSpellTrap(room, out _) && room.FaceUp && o.LifePoints == oLp;
                var act = onField && e.TryActivateSpellTrap(p, hino, fromHand: true);
                Settle(e);
                DrainCombat(e);
                Check("Dark Room: activation places it with no damage",
                    placed && onField, $"placed={placed} {State(e)}");
                Check("Dark Room: Hinotama 500 + its trigger 300, never on its own damage",
                    act && o.LifePoints == oLp - 800 && p.TryFindSpellTrap(room, out _) && !e.Chain.HasLinks,
                    $"act={act} {State(e)}");
            }

            // A face-down copy, and battle damage, do nothing.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, DarkRoom, 0);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var bp = reached && e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var atk = bp && e.TryAttack(p, celtic, null);
                DrainCombat(e);
                Settle(e);
                Check("Dark Room: battle damage does not trigger it",
                    atk && o.LifePoints == oLp - 1400, $"bp={bp} atk={atk} {State(e)}");
            }

            // The opponent's own card effect damaging them still triggers it (ep ~= tp only).
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, DarkRoom, 0);
                var fire = PutInHand(e, o, TremendousFire);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = reached && e.TryActivateSpellTrap(o, fire, fromHand: true);
                Settle(e);
                DrainCombat(e);
                Check("Dark Room: the AI's Tremendous Fire hurting the AI (500) triggers it (300)",
                    act && p.LifePoints == pLp - 1000 && o.LifePoints == oLp - 800, $"act={act} {State(e)}");
            }
        }

        // ───────────────────────────── Gravekeeper's Vassal ─────────────────────────────

        static void VassalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Its direct attack is effect damage (the effect-damage event records it) — and Dark Room of Nightmare sees it.
            foreach (var withRoom in new[] { false, true })
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                if (withRoom) PlaceFaceUpSpellTrap(e, p, DarkRoom, 0);
                var vassal = PlaceMonster(e, p, Vassal, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var bp = reached && e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var atk = bp && e.TryAttack(p, vassal, null);
                DrainCombat(e);
                Settle(e);
                DrainCombat(e);
                var last = EffectDamageEventsRuntime.LastEffectDamage(e);
                if (!withRoom)
                    Check("Gravekeeper's Vassal: its 700 battle damage is effect damage",
                        atk && o.LifePoints == oLp - 700 && last != null && last.Victim == o && last.Amount == 700 &&
                        last.Source == vassal,
                        $"atk={atk} last={last?.Source?.Name}:{last?.Amount} {State(e)}");
                else
                    Check("Gravekeeper's Vassal + Dark Room: 700 + 300",
                        atk && o.LifePoints == oLp - 1000, $"atk={atk} {State(e)}");
            }

            // Attacked in Defense Position by a weaker monster: the DEF − ATK it inflicts to the attacker is effect damage.
            {
                var e = Watch(Fresh(db, pDeck, aDeck));
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var vassal = PlaceMonster(e, p, Vassal, 0, BattlePosition.Defense, true);
                var kuri = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                var atk = reached && e.TryAttack(o, kuri, vassal);
                DrainCombat(e);
                var last = EffectDamageEventsRuntime.LastEffectDamage(e);
                Check("Gravekeeper's Vassal: attacked by Kuriboh (300 vs DEF 500), the 200 is effect damage",
                    atk && o.LifePoints == oLp - 200 && last != null && last.Victim == o && last.Amount == 200 &&
                    last.Source == vassal,
                    $"atk={atk} last={last?.Source?.Name}:{last?.Amount} {State(e)}");
            }

            // Negated or face-down, it is ordinary battle damage (no effect-damage event).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var vassal = PlaceMonster(e, p, Vassal, 0, BattlePosition.Attack, true);
                vassal.IsNegated = true;
                var oLp = o.LifePoints;
                var bp = reached && e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var atk = bp && e.TryAttack(p, vassal, null);
                DrainCombat(e);
                var last = EffectDamageEventsRuntime.LastEffectDamage(e);
                Check("Gravekeeper's Vassal negated: ordinary battle damage",
                    atk && o.LifePoints == oLp - 700 && (last == null || last.Source != vassal),
                    $"atk={atk} last={last?.Source?.Name} {State(e)}");
            }
        }

        // ───────────────────────────── Coffin Seller ─────────────────────────────

        static void CoffinSellerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Activated from a Set it only stays on the field; Dark Hole sends 2 of the AI's monsters (and 1 of the player's)
            // to the GYs in one event: one trigger, 300.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var seller = PlaceSetTrap(e, p, CoffinSeller, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                var oLp = o.LifePoints;
                var pLp = p.LifePoints;
                var placed = reached && e.TryActivateSpellTrap(p, seller, fromHand: false);
                Settle(e);
                var onField = seller.FaceUp && p.TryFindSpellTrap(seller, out _) && o.LifePoints == oLp;
                var act = onField && e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                DrainCombat(e);
                Check("Coffin Seller: activation places it with no damage", placed && onField,
                    $"placed={placed} {State(e)}");
                Check("Coffin Seller: Dark Hole (2 of the AI's monsters at once) — one trigger, 300",
                    act && o.LifePoints == oLp - 300 && p.LifePoints == pLp && o.MonsterCount == 0,
                    $"act={act} {State(e)}");

                // A Spell sent to the AI's GY does nothing; a monster (another event) triggers again.
                var lp = o.LifePoints;
                o.Graveyard.Add(e.CreateCardInstance(PotOfGreed));
                e.NotifyPublic();
                Settle(e);
                var spellQuiet = o.LifePoints == lp;
                o.Graveyard.Add(e.CreateCardInstance(Celtic));
                e.NotifyPublic();
                Settle(e);
                Check("Coffin Seller: a Spell in the AI's GY does nothing; a later monster triggers again",
                    spellQuiet && o.LifePoints == lp - 300, $"spellQuiet={spellQuiet} {State(e)}");

                // A monster sent to its controller's own GY does nothing.
                lp = o.LifePoints;
                var myLp = p.LifePoints;
                p.Graveyard.Add(e.CreateCardInstance(Celtic));
                e.NotifyPublic();
                Settle(e);
                Check("Coffin Seller: a monster in its controller's GY does nothing",
                    o.LifePoints == lp && p.LifePoints == myLp, State(e));
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static CompiledCardProgram CompileText(int id, string type, string race, string frame, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "EffectDamageEvents probe", type = type, frameType = frame, race = race,
                attribute = "DARK", level = 3, desc = text
            });

        static void NearMisses(Chk Check)
        {
            foreach (var (type, race, frame, text) in new[]
                     {
                         ("Trap Card", "Counter", "trap",
                             "Activate only when a card's effect that would inflict damage to you is activated. Your " +
                             "opponent takes the damage instead. Then draw 1 card."),
                         ("Trap Card", "Counter", "trap",
                             "Activate only when a card's effect that would inflict damage to your opponent is activated. " +
                             "You take the damage instead."),
                         ("Trap Card", "Counter", "trap",
                             "You can only activate this card when an effect that inflicts damage to your Life Points is " +
                             "activated (except Battle Damage). Negate the Effect Damage you receive."),
                         ("Spell Card", "Continuous", "spell",
                             "Each time your opponent takes damage from a card effect, except \"Dark Room of Nightmare\", " +
                             "inflict 300 damage to your opponent. You can only control 1 \"Dark Room of Nightmare\"."),
                         ("Spell Card", "Continuous", "spell",
                             "Each time your opponent takes battle damage, inflict 300 damage to your opponent."),
                         ("Effect Monster", "Spellcaster", "effect",
                             "Any battle damage this card inflicts to your opponent is treated as effect damage instead. " +
                             "This card cannot be destroyed by battle."),
                         ("Trap Card", "Continuous", "trap",
                             "Each time a monster(s) is sent to your GY: Inflict 300 damage to your opponent."),
                         ("Trap Card", "Continuous", "trap",
                             "Each time a monster(s) is sent to your opponent's GY: Inflict 300 damage to your opponent. " +
                             "Destroy this card during your 3rd End Phase."),
                     })
            {
                var prog = CompileText(990162, type, race, frame, text);
                Check($"near miss stays not FullyCompiled: \"{text.Substring(0, Math.Min(70, text.Length))}…\"",
                    prog == null || !prog.FullyCompiled, Describe(prog));
            }
        }
    }
}
