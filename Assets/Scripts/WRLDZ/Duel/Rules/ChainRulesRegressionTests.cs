using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.Ocg;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Chain / response-window / game-over regressions from the 2026-09-23 QC fix pass (DR-01 … DR-16,
    /// chain-legal Quick-Play / Trap responses, forfeit). Runs inside
    /// <see cref="InteractionRegressionTests.RunAll"/>, so every existing runner (Editor menu, batch,
    /// headless harness) covers it.
    /// </summary>
    public static class ChainRulesRegressionTests
    {
        const int PotOfGreed = 55144522;
        const int MagicJammer = 77414722;
        const int SevenTools = 3819470;
        const int SolemnJudgment = 41420027;
        const int Mst = 5318639;
        const int DarkElf = 21417692;
        const int Celtic = 91152256;
        const int GeminiElf = 69140098;
        const int MirrorForce = 44095762;
        const int TrapHole = 4206964;
        const int RingOfDestruction = 83555666;
        const int DarkMagician = 46986414;
        const int Kuriboh = 40640057;
        const int RingOfMagnetism = 20436034;
        const int HeavyStorm = 19613556;
        const int DustTornado = 60082869;
        const int AbyssSoldier = 18318842;
        const int ExodiaHead = 33396948;
        const int BigShieldGardna = 65240384;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> check)
        {
            void Guard(string name, Action body)
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    check(name + " (threw)", false, ex.GetType().Name + ": " + ex.Message);
                }
            }

            // ── DR-02: a negated negation link does not negate its own target ──
            Guard("DR-02 chain", () =>
            {
                var e = Fresh(db, pDeck, aDeck, hvh: true);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 8000;
                var pog = Hand(e, p, PotOfGreed);
                var jam = SetSt(e, o, MagicJammer, 0);
                Hand(e, o, Celtic);
                var tools = SetSt(e, p, SevenTools, 0);
                var deckBefore = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pog, true);
                var jOk = e.PendingResponse != null && e.TryActivateChainResponse(o, jam);
                var tOk = e.PendingResponse != null && e.TryActivateChainResponse(p, tools);
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                var drew = deckBefore - p.Deck.Count;
                check("DR-02: CL1 Pot of Greed ← CL2 Magic Jammer ← CL3 Seven Tools → Pot of Greed still draws 2",
                    act && jOk && tOk && drew == 2 && o.Graveyard.Contains(jam) && p.LifePoints == 7000 &&
                    !e.Chain.HasLinks && e.PendingResponse == null,
                    $"act={act} jam={jOk} tools={tOk} drew={drew} jamGy={o.Graveyard.Contains(jam)} lp={p.LifePoints}");
            });

            Guard("DR-02 stack", () =>
            {
                var e = Fresh(db, pDeck, aDeck, hvh: true);
                ClearBoard(e);
                var pog = e.CreateCardInstance(PotOfGreed);
                pog.FaceUp = true;
                e.Player.SpellTrapZones[0].Occupant = pog;
                var sj = e.CreateCardInstance(SolemnJudgment);
                sj.FaceUp = true;
                e.Opponent.SpellTrapZones[0].Occupant = sj;
                var st = e.CreateCardInstance(SevenTools);
                st.FaceUp = true;
                e.Player.SpellTrapZones[1].Occupant = st;
                var l1 = e.Chain.AddLink(e.Player, pog, SpellSpeed.Speed1, EffectClass.Ignition,
                    CardLocation.SpellTrapZone, true, false, "t");
                var l2 = e.Chain.AddLink(e.Opponent, sj, SpellSpeed.Speed3, EffectClass.Quick,
                    CardLocation.SpellTrapZone, false, true, "t");
                l2.TargetLink = l1;
                l2.NegatesActivation = true;
                l2.DestroyNegatedCard = true;
                var l3 = e.Chain.AddLink(e.Player, st, SpellSpeed.Speed3, EffectClass.Quick,
                    CardLocation.SpellTrapZone, false, true, "t");
                l3.TargetLink = l2;
                l3.NegatesActivation = true;
                l3.DestroyNegatedCard = true;
                var deckBefore = e.Player.Deck.Count;
                e.Chain.StartResolution();
                e.ResolveChainStackForActivation();
                var solemnGyCount = e.Opponent.Graveyard.Count(c => c == sj);
                check("DR-02: ResolveChainStack — CL3 negates CL2, so CL1 resolves; the negated card goes to the GY once",
                    l2.Negated && !l1.Negated && deckBefore - e.Player.Deck.Count == 2 && solemnGyCount == 1,
                    $"CL2.Negated={l2.Negated} CL1.Negated={l1.Negated} drew={deckBefore - e.Player.Deck.Count} solemnInGy={solemnGyCount}");
            });

            // ── Fast Effect Timing: pass / pass closes the chain window ──
            Guard("pass-pass", () =>
            {
                var e = Fresh(db, pDeck, aDeck, hvh: true);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var pog = Hand(e, p, PotOfGreed);
                var jam = SetSt(e, o, MagicJammer, 0);
                Hand(e, o, Celtic);
                var deckBefore = p.Deck.Count;
                e.TryActivateSpellTrap(p, pog, true);
                var offered = e.PendingResponse?.Responder == o && e.PendingResponse.LegalCards.Contains(jam);
                e.PassResponse();
                var drew = deckBefore - p.Deck.Count;
                check("Chain window: the opponent passes, the activator has nothing to chain → the chain resolves (no re-prompt loop)",
                    offered && drew == 2 && e.PendingResponse == null && !e.Chain.HasLinks && !jam.FaceUp,
                    $"offered={offered} drew={drew} pending={e.PendingResponse?.Timing} chain={e.Chain.Describe()}");
            });

            // ── DR-01: MST in the opponent's open-state window takes a target (player) ──
            Guard("DR-01 open", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var mst = SetSt(e, p, Mst, 0);
                var reached = ReachMain(e, o);
                var v1 = SetSt(e, o, MirrorForce, 0);
                var v2 = SetSt(e, o, TrapHole, 1);
                var mine = SetSt(e, p, MirrorForce, 1);
                var opened = e.OpenResponseWindow(p, ResponseTiming.OpponentOpenState, null, null, null, null, null);
                var cmd = DuelCommandService.Execute(e, p, DuelIntent.Of(DuelIntentKind.Activate, mst));
                var awaiting = e.IsAwaitingEffectTarget;
                var sel = awaiting && e.TrySelectEffectTarget(v1);
                check("DR-01: MST in the opponent's open-state window asks for a target and destroys only it",
                    reached && opened && cmd.Ok && awaiting && sel && o.Graveyard.Contains(v1) &&
                    o.TryFindSpellTrap(v2, out _) && p.TryFindSpellTrap(mine, out _) && p.Graveyard.Contains(mst) &&
                    e.PendingResponse == null && !e.Chain.HasLinks,
                    $"reached={reached} opened={opened} cmd={cmd.Ok} awaiting={awaiting} sel={sel} v1Gy={o.Graveyard.Contains(v1)} v2Set={o.TryFindSpellTrap(v2, out _)} mineSet={p.TryFindSpellTrap(mine, out _)}");
            });

            Guard("DR-01 cancel", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var mst = SetSt(e, p, Mst, 0);
                ReachMain(e, o);
                var v1 = SetSt(e, o, MirrorForce, 0);
                e.OpenResponseWindow(p, ResponseTiming.OpponentOpenState, null, null, null, null, null);
                DuelCommandService.Execute(e, p, DuelIntent.Of(DuelIntentKind.Activate, mst));
                var awaiting = e.IsAwaitingEffectTarget;
                e.CancelEffectTargeting();
                check("DR-01: cancelling the MST target pick puts it back face-down and re-offers the window",
                    awaiting && !mst.FaceUp && p.TryFindSpellTrap(mst, out _) &&
                    e.PendingResponse?.Timing == ResponseTiming.OpponentOpenState && !e.Chain.HasLinks &&
                    o.TryFindSpellTrap(v1, out _) && !e.IsAwaitingEffectTarget,
                    $"awaiting={awaiting} faceUp={mst.FaceUp} window={e.PendingResponse?.Timing} chain={e.Chain.Describe()}");
                if (e.PendingResponse != null) e.PassResponse();
            });

            // ── DR-01: MST in the AttackDeclared window (player picks), attack continues ──
            Guard("DR-01 attack", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var mst = SetSt(e, p, Mst, 0);
                ReachMain(e, o);
                var v1 = SetSt(e, o, MirrorForce, 0);
                var v2 = SetSt(e, o, TrapHole, 1);
                var atk = Mon(e, o, GeminiElf, 0);
                var def = Mon(e, p, Kuriboh, 0);
                e.TryEnterBattlePhase(o);
                if (e.PendingResponse != null) e.PassResponse(); // Battle Phase start window
                var declared = e.TryAttack(o, atk, def);
                var window = e.PendingResponse?.Timing;
                var cmd = DuelCommandService.Execute(e, p, DuelIntent.Of(DuelIntentKind.Activate, mst));
                var awaiting = e.IsAwaitingEffectTarget;
                var sel = awaiting && e.TrySelectEffectTarget(v1);
                Drain(e);
                var destroyed = (o.Graveyard.Contains(v1) ? 1 : 0) + (o.Graveyard.Contains(v2) ? 1 : 0);
                check("DR-01: MST answering an attack destroys exactly the picked card; the attack then resolves",
                    declared && window == ResponseTiming.AttackDeclared && cmd.Ok && awaiting && sel &&
                    destroyed == 1 && o.Graveyard.Contains(v1) && !e.HasDeclaredAttack &&
                    p.Graveyard.Contains(def),
                    $"declared={declared} window={window} cmd={cmd.Ok} awaiting={awaiting} sel={sel} destroyed={destroyed} attackOpen={e.HasDeclaredAttack} kuribohGy={p.Graveyard.Contains(def)}");
            });

            // ── DR-01: the AI's inline MST auto-picks one target (never "destroy all") ──
            Guard("DR-01 ai", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var p1 = SetSt(e, p, MirrorForce, 0);
                var p2 = SetSt(e, p, TrapHole, 1);
                var omst = SetSt(e, o, Mst, 0);
                var pm = Mon(e, p, Celtic, 0);
                var opened = e.OpenResponseWindow(o, ResponseTiming.AttackDeclared, pm, null, p, null, null);
                var ok = SpellTrapEffects.BeginOrResolveManual(e, o, omst, false, autoPickTarget: true);
                var destroyed = (p.Graveyard.Contains(p1) ? 1 : 0) + (p.Graveyard.Contains(p2) ? 1 : 0);
                check("DR-01: an auto-picked MST in a response window destroys one card, not every Set card",
                    opened && ok && destroyed == 1 && o.Graveyard.Contains(omst) && e.PendingResponse == null,
                    $"opened={opened} ok={ok} destroyed={destroyed} mstGy={o.Graveyard.Contains(omst)}");
            });

            // ── DR-01 + DR-06: Ring of Destruction in the open-state window, both players to 0 → Draw ──
            Guard("DR-06 ring", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var ring = SetSt(e, p, RingOfDestruction, 0);
                ReachMain(e, o);
                var dm = Mon(e, o, DarkMagician, 0);
                p.LifePoints = 2500;
                o.LifePoints = 2500;
                var opened = e.OpenResponseWindow(p, ResponseTiming.OpponentOpenState, null, null, null, null, null);
                var cmd = DuelCommandService.Execute(e, p, DuelIntent.Of(DuelIntentKind.Activate, ring));
                var sel = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(dm);
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                check("DR-06: Ring of Destruction bringing both players to 0 LP is a Draw",
                    opened && cmd.Ok && sel && o.Graveyard.Contains(dm) && e.GameOver && e.IsDraw &&
                    e.Winner == null && e.EndReason == DuelEndReason.Draw,
                    $"opened={opened} cmd={cmd.Ok} sel={sel} dmGy={o.Graveyard.Contains(dm)} LP {p.LifePoints}/{o.LifePoints} over={e.GameOver} reason={e.EndReason}");
            });

            Guard("DR-06 both", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                e.Player.LifePoints = 1000;
                e.Opponent.LifePoints = 500;
                e.ApplyEffectDamageBoth(e.Player, 1000, e.Opponent, 800, "test");
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                var drawOk = e.GameOver && e.IsDraw && e.Winner == null;
                var e2 = Fresh(db, pDeck, aDeck);
                ClearBoard(e2);
                e2.Opponent.LifePoints = 500;
                e2.ApplyEffectDamage(e2.Opponent, 800, "test");
                for (var i = 0; i < 4 && e2.PendingResponse != null; i++) e2.PassResponse();
                check("DR-06: simultaneous damage to 0 is a Draw; one player at 0 is a normal win",
                    drawOk && e2.GameOver && e2.Winner == e2.Player && e2.EndReason == DuelEndReason.LifePoints,
                    $"draw={drawOk} single over={e2.GameOver} winner={(e2.Winner == null ? "none" : e2.Winner.Name)} reason={e2.EndReason}");
            });

            // ── DR-05: paying the last LP as a chain cost ends the duel when the chain resolves ──
            Guard("DR-05", () =>
            {
                var e = Fresh(db, pDeck, aDeck, hvh: true);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var pog = Hand(e, p, PotOfGreed);
                var jam = SetSt(e, o, MagicJammer, 0);
                Hand(e, o, Celtic);
                var tools = SetSt(e, p, SevenTools, 2);
                p.LifePoints = 1000;
                e.TryActivateSpellTrap(p, pog, true);
                var j = e.PendingResponse != null && e.TryActivateChainResponse(o, jam);
                var t = e.PendingResponse != null && e.TryActivateChainResponse(p, tools);
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                check("DR-05: Seven Tools paid with exactly 1000 LP → 0 LP → the duel is lost",
                    j && t && p.LifePoints == 0 && e.GameOver && e.Winner == o,
                    $"jam={j} tools={t} LP={p.LifePoints} over={e.GameOver} winner={(e.Winner == null ? "none" : e.Winner.Name)}");
            });

            // ── DR-04: an illegal attack declaration pays no cost ──
            Guard("DR-04", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                ReachMain(e, o);
                ReachMain(e, p);
                var elf = Mon(e, p, DarkElf, 0);
                var foe = Mon(e, o, Celtic, 0);
                e.TryEnterBattlePhase(p);
                if (e.PendingResponse != null) e.PassResponse();
                var lp0 = p.LifePoints;
                var illegal = e.TryAttack(p, elf, null);
                var lp1 = p.LifePoints;
                var legal = e.TryAttack(p, elf, foe);
                Drain(e);
                check("DR-04: Dark Elf's refused direct attack costs nothing; a legal attack pays 1000 LP",
                    !illegal && lp1 == lp0 && legal && p.LifePoints == lp0 - 1000,
                    $"illegal={illegal} LP {lp0}→{lp1}; legal={legal} LP now {p.LifePoints}");
            });

            // ── DR-09: per-turn monster flags reset for both players each turn ──
            Guard("DR-09", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var m = Mon(e, e.Player, Celtic, 0);
                m.EffectUsedThisTurn = true;
                ReachMain(e, e.Opponent);
                var reset = !m.EffectUsedThisTurn;
                var e2 = Fresh(db, pDeck, aDeck);
                ClearBoard(e2);
                ReachMain(e2, e2.Opponent);
                var soldier = Mon(e2, e2.Opponent, AbyssSoldier, 0);
                soldier.EffectUsedThisTurn = true;
                ReachMain(e2, e2.Player);
                var took = e2.TryTakeControl(e2.Player, soldier);
                check("DR-09: once-per-turn flags reset at every turn start, for the non-turn player's monsters too",
                    reset && took && !soldier.EffectUsedThisTurn,
                    $"reset={reset} took={took} stolenFlag={soldier.EffectUsedThisTurn}");
            });

            // ── DR-11: ending the turn from the Battle Phase runs the End Step (Magical Hats) ──
            Guard("DR-11", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                ReachMain(e, e.Opponent);
                ReachMain(e, e.Player);
                var dummy = Mon(e, e.Opponent, Celtic, 1, BattlePosition.Defense, false);
                dummy.MagicalHatDummy = true;
                var bp = e.TryEnterBattlePhase(e.Player);
                if (e.PendingResponse != null) e.PassResponse();
                e.EndTurn(e.Player);
                check("DR-11: a Magical Hats dummy is destroyed when the turn ends straight from the Battle Phase",
                    bp && !e.Opponent.TryFindMonster(dummy, out _),
                    $"bp={bp} dummyOnField={e.Opponent.TryFindMonster(dummy, out _)}");
            });

            // ── DR-03: the live AI battle loop ends when its preferred target is illegal ──
            Guard("DR-03", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                var host = Mon(e, p, DarkMagician, 2);
                Mon(e, p, Kuriboh, 3);
                var ring = Hand(e, p, RingOfMagnetism);
                var armed = e.TryActivateSpellTrap(p, ring, true) && e.TrySelectEffectTarget(host);
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                ReachMain(e, o);
                Mon(e, o, Celtic, 0);
                o.Hand.Clear();
                var finished = RunAiTurn(e, 5000, out var steps);
                check("DR-03: SimpleAi's turn ends when Ring of Magnetism makes its chosen attack illegal",
                    armed && finished,
                    $"armed={armed} finished={finished} steps={steps} phase={e.Phase} tp={e.TurnPlayer?.Name}");
            });

            // ── DR-10: the AI plans against face-down monsters with a public estimate ──
            Guard("DR-10", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                ReachMain(e, o);
                ReachMain(e, p);
                Mon(e, p, BigShieldGardna, 0, BattlePosition.Defense, false); // real DEF 2600, hidden
                ReachMain(e, o);
                var dm = Mon(e, o, DarkMagician, 0);
                o.Hand.Clear();
                var finished = RunAiTurn(e, 5000, out _);
                var attacked = e.GameEvents.Events.Any(ev =>
                    ev.Kind == DuelGameEventKind.DeclaredAttack && ev.Card == dm);
                check("DR-10: the AI does not read a face-down monster's DEF (2500 ATK attacks a hidden 2600 DEF, estimate 1500)",
                    finished && attacked,
                    $"finished={finished} attacked={attacked} estimate={AiHeuristicPolicy.FaceDownDefEstimate}");
            });

            // ── Chain rule: the AI chains a Set MST that Heavy Storm would destroy ──
            Guard("chain ai", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var hs = Hand(e, p, HeavyStorm);
                SetSt(e, p, MirrorForce, 1);
                var omst = SetSt(e, o, Mst, 0);
                var oTrap = SetSt(e, o, TrapHole, 1);
                var act = e.TryActivateSpellTrap(p, hs, true);
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                var mstActivated = e.GameEvents.Events.Any(ev =>
                    ev.Kind == DuelGameEventKind.Activated && ev.Card == omst);
                check("Chain rule: the AI chains its Set MST in response to Heavy Storm (Spell Speed 2 onto 1)",
                    act && mstActivated && o.Graveyard.Contains(omst) && o.Graveyard.Contains(oTrap) &&
                    p.Graveyard.Contains(hs) && !e.Chain.HasLinks && e.PendingResponse == null,
                    $"act={act} mstActivated={mstActivated} mstGy={o.Graveyard.Contains(omst)} trapGy={o.Graveyard.Contains(oTrap)}");
            });

            // ── Chain rule: the player chains a Set MST to the AI's Pot of Greed and picks its target ──
            Guard("chain player", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var pmst = SetSt(e, p, Mst, 0);
                ReachMain(e, o);
                var oSet = SetSt(e, o, MirrorForce, 0);
                var pog = Hand(e, o, PotOfGreed);
                var oDeck = o.Deck.Count;
                var act = e.TryActivateSpellTrap(o, pog, true);
                var offered = e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                              e.PendingResponse.Responder == p && e.PendingResponse.LegalCards.Contains(pmst);
                var chained = offered && e.TryActivateSpellTrap(p, pmst, false);
                var awaiting = e.IsAwaitingEffectTarget;
                var sel = awaiting && e.TrySelectEffectTarget(oSet);
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                check("Chain rule: the player chains a Set MST to the opponent's Pot of Greed; MST resolves first, then PoG",
                    act && offered && chained && awaiting && sel && o.Graveyard.Contains(oSet) &&
                    p.Graveyard.Contains(pmst) && oDeck - o.Deck.Count == 2 && !e.Chain.HasLinks,
                    $"act={act} offered={offered} chained={chained} awaiting={awaiting} sel={sel} oSetGy={o.Graveyard.Contains(oSet)} oDrew={oDeck - o.Deck.Count}");
            });

            Guard("chain cancel", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var pmst = SetSt(e, p, Mst, 0);
                ReachMain(e, o);
                SetSt(e, o, MirrorForce, 0);
                var pog = Hand(e, o, PotOfGreed);
                var oDeck = o.Deck.Count;
                e.TryActivateSpellTrap(o, pog, true);
                e.TryActivateSpellTrap(p, pmst, false);
                var awaiting = e.IsAwaitingEffectTarget;
                e.CancelEffectTargeting();
                var reoffered = e.PendingResponse?.Timing == ResponseTiming.ChainResponse &&
                                e.PendingResponse.Responder == p && e.Chain.Count == 1;
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                check("Chain rule: cancelling the chained MST's target takes its link back; passing then resolves PoG",
                    awaiting && reoffered && !pmst.FaceUp && p.TryFindSpellTrap(pmst, out _) &&
                    oDeck - o.Deck.Count == 2 && !e.Chain.HasLinks,
                    $"awaiting={awaiting} reoffered={reoffered} faceUp={pmst.FaceUp} oDrew={oDeck - o.Deck.Count}");
            });

            // ── Chain rule legality: hand Quick-Play on your own turn, Spell Speed, own-link answers only ──
            Guard("chain legality", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                SetSt(e, o, TrapHole, 1);
                var oCard = SetSt(e, o, DustTornado, 0);
                oCard.FaceUp = true;
                e.Chain.BeginBuilding();
                var link = e.Chain.AddLink(o, oCard, SpellSpeed.Speed2, EffectClass.Quick,
                    CardLocation.SpellTrapZone, false, true, "t");
                var handMst = Hand(e, p, Mst);
                var setMst = SetSt(e, p, Mst, 1);
                e.SetPendingResponse(new PendingResponse
                    { Timing = ResponseTiming.ChainResponse, Responder = p, ChainTarget = link });
                var legal = SpellTrapEffects.CollectLegalResponseCards(e, p, ResponseTiming.ChainResponse, null);
                var handOk = legal.Contains(handMst) && legal.Contains(setMst);

                e.SetPendingResponse(new PendingResponse
                    { Timing = ResponseTiming.ChainResponse, Responder = p, ChainTarget = link, ChainAnswersOnly = true });
                var ownOnly = SpellTrapEffects.CollectLegalResponseCards(e, p, ResponseTiming.ChainResponse, null);

                var sj = e.CreateCardInstance(SolemnJudgment);
                sj.FaceUp = true;
                o.SpellTrapZones[2].Occupant = sj;
                var link3 = e.Chain.AddLink(o, sj, SpellSpeed.Speed3, EffectClass.Quick,
                    CardLocation.SpellTrapZone, false, true, "t");
                e.SetPendingResponse(new PendingResponse
                    { Timing = ResponseTiming.ChainResponse, Responder = p, ChainTarget = link3 });
                var onSpeed3 = SpellTrapEffects.CollectLegalResponseCards(e, p, ResponseTiming.ChainResponse, null);
                e.ClearPendingResponse();
                e.Chain.Clear();
                check("Chain rule legality: Set and hand (own turn) Quick-Plays can chain; not onto your own link; not onto Speed 3",
                    handOk && ownOnly.Count == 0 && !onSpeed3.Contains(setMst) && !onSpeed3.Contains(handMst),
                    $"legal=[{string.Join(",", legal.Select(c => c.Name))}] ownOnly={ownOnly.Count} onSpeed3=[{string.Join(",", onSpeed3.Select(c => c.Name))}]");
            });

            // ── AI response that makes a chain link (Seat path) resolves ──
            Guard("ai seat", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                ReachMain(e, o);
                ReachMain(e, p);
                SetSt(e, o, MirrorForce, 0);
                var elf = Mon(e, p, GeminiElf, 0);
                Mon(e, o, Kuriboh, 0, BattlePosition.Defense, true);
                e.TryEnterBattlePhase(p);
                if (e.PendingResponse != null) e.PassResponse();
                var declared = e.TryAttack(p, elf, o.MonstersOnField().First());
                Drain(e);
                check("AI response: the AI's Mirror Force answering an attack resolves (link seated, not lost)",
                    declared && p.Graveyard.Contains(elf) && !e.Chain.HasLinks && e.PendingResponse == null,
                    $"declared={declared} elfGy={p.Graveyard.Contains(elf)} chain={e.Chain.Describe()}");
            });

            // ── The AI's seated (chain-link) response resolves: the link is added when missing ──
            Guard("ai seat link", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                // Empty hands: a randomly dealt hand could hold a legal chain answer, which opens the
                // player's response window and leaves CL1 waiting (the test failed ~1 run in 3).
                p.Hand.Clear();
                o.Hand.Clear();
                var p1 = SetSt(e, p, MirrorForce, 0);
                var p2 = SetSt(e, p, TrapHole, 1);
                var odt = SetSt(e, o, DustTornado, 0);
                var pm = Mon(e, p, Celtic, 0);
                var opened = e.OpenResponseWindow(o, ResponseTiming.AttackDeclared, pm, null, p, null, null);
                var chainBefore = e.Chain.Count;
                var ok = SpellTrapEffects.BeginOrResolveManual(e, o, odt, false, autoPickTarget: true);
                var destroyed = (p.Graveyard.Contains(p1) ? 1 : 0) + (p.Graveyard.Contains(p2) ? 1 : 0);
                check("AI response: a chain-link answer (Dust Tornado) from the AI path is seated and resolves on one target",
                    opened && chainBefore == 0 && ok && destroyed == 1 && o.Graveyard.Contains(odt) &&
                    !e.Chain.HasLinks && e.PendingResponse == null,
                    $"opened={opened} ok={ok} destroyed={destroyed} dtGy={o.Graveyard.Contains(odt)} chain={e.Chain.Describe()}");
            });

            // ── Seated targeted response (Dust Tornado) answering an attack: the player picks ──
            Guard("seat player", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var dt = SetSt(e, p, DustTornado, 0);
                ReachMain(e, o);
                var v1 = SetSt(e, o, MirrorForce, 0);
                var v2 = SetSt(e, o, TrapHole, 1);
                var atk = Mon(e, o, GeminiElf, 0);
                var def = Mon(e, p, Kuriboh, 0);
                e.TryEnterBattlePhase(o);
                if (e.PendingResponse != null) e.PassResponse();
                var declared = e.TryAttack(o, atk, def);
                var cmd = DuelCommandService.Execute(e, p, DuelIntent.Of(DuelIntentKind.Activate, dt));
                var awaiting = e.IsAwaitingEffectTarget;
                var sel = awaiting && e.TrySelectEffectTarget(v1);
                Drain(e);
                var destroyed = (o.Graveyard.Contains(v1) ? 1 : 0) + (o.Graveyard.Contains(v2) ? 1 : 0);
                check("Seated response: Dust Tornado answering an attack destroys the picked card, then the attack resolves",
                    declared && cmd.Ok && awaiting && sel && destroyed == 1 && o.Graveyard.Contains(v1) &&
                    !e.HasDeclaredAttack && p.Graveyard.Contains(def) && !e.Chain.HasLinks,
                    $"declared={declared} cmd={cmd.Ok} awaiting={awaiting} sel={sel} destroyed={destroyed} attackOpen={e.HasDeclaredAttack} kuribohGy={p.Graveyard.Contains(def)}");
            });

            // ── DR-07: End Phase hand limit discards the lowest-value cards ──
            Guard("DR-07", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                p.Hand.Clear();
                for (var i = 0; i < 6; i++) Hand(e, p, Celtic);
                var pog = Hand(e, p, PotOfGreed);
                var head = Hand(e, p, ExodiaHead);
                var gyBefore = p.Graveyard.Count;
                e.EndTurn(p);
                check("DR-07: hand-size discards take the lowest-value cards (not the newest draw) and go to the GY",
                    p.Hand.Count == TcgRules.HandSizeLimitEndPhase && p.Hand.Contains(pog) && p.Hand.Contains(head) &&
                    p.Graveyard.Count - gyBefore == 2 && p.Graveyard.Skip(gyBefore).All(c => c.CardId == Celtic),
                    $"hand={p.Hand.Count} pogKept={p.Hand.Contains(pog)} headKept={p.Hand.Contains(head)} discarded={p.Graveyard.Count - gyBefore}");
            });

            // ── DR-08: Magic Jammer's discard cost takes the lowest-value card ──
            Guard("DR-08", () =>
            {
                var e = Fresh(db, pDeck, aDeck, hvh: true);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                var pog = Hand(e, p, PotOfGreed);
                var jam = SetSt(e, o, MagicJammer, 0);
                o.Hand.Clear();
                var head = Hand(e, o, ExodiaHead);
                var celtic = Hand(e, o, Celtic);
                e.TryActivateSpellTrap(p, pog, true);
                var j = e.PendingResponse != null && e.TryActivateChainResponse(o, jam);
                for (var i = 0; i < 4 && e.PendingResponse != null; i++) e.PassResponse();
                var sentEvent = e.GameEvents.Events.Any(ev => ev.Kind == DuelGameEventKind.SentToGy && ev.Card == celtic);
                check("DR-08: Magic Jammer discards the lowest-value card (Exodia piece kept) as a real send to the GY",
                    j && o.Graveyard.Contains(celtic) && o.Hand.Contains(head) && sentEvent,
                    $"jam={j} celticGy={o.Graveyard.Contains(celtic)} headKept={o.Hand.Contains(head)} event={sentEvent}");
            });

            // ── Forfeit ──
            Guard("concede", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var fired = 0;
                e.OnGameOver += () => fired++;
                var pm = Mon(e, e.Player, GeminiElf, 0);
                SetSt(e, e.Opponent, MirrorForce, 0);
                e.OpenResponseWindow(e.Opponent, ResponseTiming.AttackDeclared, pm, null, e.Player, null, null);
                var first = e.Concede(e.Player, "left via Map");
                var second = e.Concede(e.Opponent, "again");
                check("Concede: a forfeit is one loss through the normal game-over path; a second call is ignored",
                    first && !second && fired == 1 && e.GameOver && e.Winner == e.Opponent &&
                    e.EndReason == DuelEndReason.Forfeit && e.ConcededBy == e.Player && e.PendingResponse == null,
                    $"first={first} second={second} fired={fired} winner={(e.Winner == null ? "none" : e.Winner.Name)} reason={e.EndReason}");
            });

            // ── DR-16: DuelRng reseeding reproduces coin / die results ──
            Guard("DR-16", () =>
            {
                var a = new DuelRng();
                var b = new DuelRng();
                a.Reseed(1017);
                b.Reseed(1017);
                var same = true;
                for (var i = 0; i < 64; i++)
                {
                    if (a.TossCoin() != b.TossCoin()) same = false;
                    if (a.RollDie() != b.RollDie()) same = false;
                }

                check("DR-16: DuelRng.Reseed makes coin tosses and die rolls reproducible", same, "");
            });

            // ── Pacer: a cancelled activation gives back its read hold ──
            Guard("pacer", () =>
            {
                DuelPresentationPacer.Clear();
                const int id = 990424242;
                DuelPresentationPacer.HoldSpellActivate("Release Test", opponentCard: false, instanceId: id);
                var released = DuelPresentationPacer.ReleaseSpellActivateHold(id);
                var again = DuelPresentationPacer.ReleaseSpellActivateHold(id);
                DuelPresentationPacer.Clear();
                check("Pacer: ReleaseSpellActivateHold undoes a cancelled activation's hold once",
                    released && !again, $"released={released} again={again}");
            });

            // ── PERF-05: script presence is an index lookup ──
            Guard("PERF-05", () =>
            {
                if (OcgScriptStore.IndexedCount == 0)
                {
                    check("PERF-05: OcgScriptStore.HasCardScript (skipped: no OCG scripts indexed)", true, "");
                    return;
                }

                check("PERF-05: OcgScriptStore.HasCardScript answers from the index",
                    OcgScriptStore.HasCardScript(PotOfGreed) && !OcgScriptStore.HasCardScript(990000999),
                    $"indexed={OcgScriptStore.IndexedCount}");
            });
        }

        // ── fixtures ──

        static DuelEngine Fresh(CardDatabase db, DeckFile p, DeckFile a, bool hvh = false)
        {
            var e = new DuelEngine { HumanVsHuman = hvh };
            e.StartDuel(db, p, a, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            return e;
        }

        static void ClearBoard(DuelEngine engine)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                for (var i = 0; i < who.MonsterZones.Length; i++) who.MonsterZones[i].Occupant = null;
                for (var i = 0; i < who.SpellTrapZones.Length; i++) who.SpellTrapZones[i].Occupant = null;
                if (who.FieldSpellZone != null) who.FieldSpellZone.Occupant = null;
                who.NormalSummonUsed = false;
            }

            engine.ClearPendingActivation();
            engine.ClearPendingResponse();
            engine.PendingTributes.Clear();
        }

        static CardInstance Mon(DuelEngine e, DuelistState w, int id, int zone,
            BattlePosition pos = BattlePosition.Attack, bool faceUp = true)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = faceUp;
            c.Position = pos;
            c.SetThisTurn = false;
            c.SummonedThisTurn = false;
            w.MonsterZones[zone].Occupant = c;
            return c;
        }

        static CardInstance SetSt(DuelEngine e, DuelistState w, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = false;
            c.SetThisTurn = false;
            w.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance Hand(DuelEngine e, DuelistState w, int id)
        {
            var c = e.CreateCardInstance(id);
            w.Hand.Add(c);
            return c;
        }

        /// <summary>End turns (passing every window) until <paramref name="who"/> is in a Main Phase.</summary>
        static bool ReachMain(DuelEngine e, DuelistState who)
        {
            for (var t = 0; t < 8; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                if (e.TurnPlayer == who && e.InMainPhase && !e.IsAwaitingResponse) return true;
                if (e.TurnPlayer == who && e.InMainPhase) continue;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnPlayer == who && e.InMainPhase;
        }

        /// <summary>
        /// Step SimpleAi's turn coroutine synchronously. Presentation holds are cleared each step: tests run
        /// inside one frame, so a read hold would otherwise never end. The player passes every window.
        /// </summary>
        static bool RunAiTurn(DuelEngine e, int maxSteps, out int steps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            steps = 0;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                while (steps < maxSteps)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    if (!it.MoveNext()) return true;
                    steps++;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        static void Drain(DuelEngine e)
        {
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else e.RecoverStuckCombat();
            }
        }
    }
}
