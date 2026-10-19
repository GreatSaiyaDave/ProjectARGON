using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Engine fix (2026-10-18): a card with no "sent to the GY" effect gets no Chain Link when it is sent together with
    /// other cards (a Summon's Tributes, a mass destruction). Found at seed 3: the AI Tribute Summoned Judge Man by
    /// Tributing Blue-Eyes White Dragon (both Normal Monsters) and the log seated "CL1 Blue-Eyes White Dragon's effect",
    /// opening the player's Chain response window (the player had a Set Trap). Root cause: SimultaneousTriggersRuntime.RunHeld
    /// made a SEGOC entry for every held send, whether or not the card had a field → GY trigger. Mini-duels through the real
    /// entry points (SimpleAi.TakeTurnRoutine, ToggleTribute + TryNormalSummon, TryActivateSpellTrap) and positive controls
    /// (Sangan's real trigger is still seated, alone, when it is Tributed with a vanilla).
    /// </summary>
    public static class PhantomChainLinkRegressionTests
    {
        const int Bewd = 89631139;          // Normal, Level 8, 3000/2500
        const int JudgeMan = 30113682;      // Normal, Level 6, 2200/1500
        const int Celtic = 91152256;        // Normal, Level 4, 1400/1200
        const int GiantSoldier = 13039848;  // Normal, Level 3, 1300/2000
        const int Sangan = 26202165;        // If this card is sent from the field to the GY: search
        const int Reinforcements = 17814387; // Normal Trap: a legal Chain response
        const int DarkHole = 53129443;
        const int Swords = 72302403;        // Swords of Revealing Light: flips the opponent's face-down monsters
        const int ManEaterBug = 54652250;   // FLIP: destroy 1 monster
        const int MysticTomato = 83011277;  // When this card is destroyed by battle and sent to the GY: Special Summon
        const int ShiningAngel = 95956346;  // When this card is destroyed by battle and sent to the GY: Special Summon
        const int BlackLusterRitual = 55761792;
        const int BlackLusterSoldier = 5405694;

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("PhantomChainLink: " + name, ok, detail);

            Guard(Check, "AI Tribute Summon", () => AiTributeSummon(db, pDeck, aDeck, Check));
            Guard(Check, "Player Tribute Summon", () => PlayerTributeSummon(db, pDeck, aDeck, Check));
            Guard(Check, "Mass destruction", () => MassDestruction(db, pDeck, aDeck, Check));
            Guard(Check, "Flipped by an effect", () => FlippedByEffect(db, pDeck, aDeck, Check));
            Guard(Check, "Real triggers", () => RealTriggers(db, pDeck, aDeck, Check));
            Guard(Check, "Real FLIP / mass triggers", () => RealFlipAndMassTriggers(db, pDeck, aDeck, Check));
            Guard(Check, "Conditional triggers", () => ConditionalTriggers(db, pDeck, aDeck, Check));
        }

        static void Guard(Chk Check, string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Check($"{label}: no exception", false, ex.GetType().Name + ": " + ex.Message + " @ " +
                                                       (ex.StackTrace ?? "").Split('\n').FirstOrDefault());
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        static bool ReachMain(DuelEngine engine, DuelistState who, int minTurn)
        {
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn)
                    return true;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn;
        }

        /// <summary>A fresh duel at <paramref name="who"/>'s Main Phase 1 (turn 3+): empty board, hands and GYs.</summary>
        static DuelEngine MainOf(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool player, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachMain(e, player ? e.Player : e.Opponent, 3);
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static CardInstance SetTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var t = PlaceSetTrap(e, who, id, zone);
            t.SetThisTurn = false;
            return t;
        }

        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            who.Deck.AddRange(ids);
        }

        /// <summary>Log lines that seat or name a Chain Link of <paramref name="cardName"/>'s effect.</summary>
        static List<string> LinksOf(List<string> log, string cardName) =>
            log.Where(l => l != null && l.Contains(cardName + "'s effect") ||
                           l != null && l.StartsWith("Chain:") && l.Contains(cardName))
                .ToList();

        static string Tail(List<string> log, int n = 12) =>
            string.Join(" / ", log.Skip(Math.Max(0, log.Count - n)));

        // ───────────────────────── scenarios ─────────────────────────

        static void AiTributeSummon(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI Tribute Summons a vanilla Level 6 by Tributing a vanilla while the player has a Set Trap.
            var e = MainOf(db, pDeck, aDeck, false, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Defense, true);
            var judge = PutInHand(e, opp, JudgeMan);
            SetTrap(e, p, Reinforcements, 0);
            SetDeck(opp, Celtic, Celtic, Celtic, Celtic, Celtic);
            var log = new List<string>();
            e.OnLog += l => log.Add(l);
            e.NotifyPublic();
            var startTurn = e.TurnNumber;
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            var done = false;
            var chainWhileAi = false;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < 400; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.TurnPlayer == opp && e.Chain.HasLinks) chainWhileAi = true;
                    if (e.PendingResponse != null && e.PendingResponse.Responder == p) e.PassResponse();
                    if (!it.MoveNext())
                    {
                        done = true;
                        break;
                    }
                }
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }

            var summoned = opp.MonstersOnField().Contains(judge);
            var phantom = LinksOf(log, "Celtic Guardian");
            Check("AI: Tributing a vanilla for a vanilla Tribute Summon seats no Chain Link and opens no response window",
                reached && summoned && opp.Graveyard.Contains(celtic) && phantom.Count == 0 && !chainWhileAi &&
                done && e.TurnPlayer == p && e.TurnNumber > startTurn,
                $"reached={reached} summoned={summoned} celticGy={opp.Graveyard.Contains(celtic)} " +
                $"phantom=[{string.Join(" | ", phantom)}] chainWhileAi={chainWhileAi} done={done} " +
                $"turn={e.TurnPlayer?.Name} log: {Tail(log)}");
        }

        static void PlayerTributeSummon(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The seed-3 pair, through the player's real entry point: Tribute Blue-Eyes for Judge Man.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                var judge = PutInHand(e, p, JudgeMan);
                SetTrap(e, opp, Reinforcements, 0);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                e.ToggleTribute(p, bewd);
                var ok = e.TryNormalSummon(p, judge, asSet: false);
                var chainAfter = e.Chain.HasLinks;
                var chainWindow = e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse;
                var phantom = LinksOf(log, "Blue-Eyes White Dragon");
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Player: Tributing Blue-Eyes White Dragon for Judge Man seats no Chain Link (no 'Blue-Eyes' effect')",
                    reached && ok && p.MonstersOnField().Contains(judge) && p.Graveyard.Contains(bewd) &&
                    phantom.Count == 0 && !chainAfter && !chainWindow,
                    $"reached={reached} ok={ok} judge={p.MonstersOnField().Contains(judge)} bewdGy={p.Graveyard.Contains(bewd)} " +
                    $"phantom=[{string.Join(" | ", phantom)}] chain={chainAfter} chainWindow={chainWindow} log: {Tail(log)}");
            }

            {
                // Two vanilla Tributes for a vanilla Level 8: neither gets a link.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, false);
                var bewd = PutInHand(e, p, Bewd);
                SetTrap(e, opp, Reinforcements, 0);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                e.ToggleTribute(p, a);
                e.ToggleTribute(p, b);
                var ok = e.TryNormalSummon(p, bewd, asSet: false);
                var chainAfter = e.Chain.HasLinks;
                var phantom = LinksOf(log, "Celtic Guardian").Concat(LinksOf(log, "Giant Soldier of Stone")).ToList();
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Player: 2 vanilla Tributes (one face-down) for Blue-Eyes White Dragon seat no Chain Link",
                    reached && ok && p.MonstersOnField().Contains(bewd) && p.Graveyard.Contains(a) &&
                    p.Graveyard.Contains(b) && phantom.Count == 0 && !chainAfter,
                    $"reached={reached} ok={ok} phantom=[{string.Join(" | ", phantom)}] chain={chainAfter} log: {Tail(log)}");
            }
        }

        static void MassDestruction(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Dark Hole destroys only vanillas while the opponent has a Set Trap: no deferred "sent to the GY" Chain.
            var e = MainOf(db, pDeck, aDeck, true, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var theirs = PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
            var hole = PutInHand(e, p, DarkHole);
            SetTrap(e, opp, Reinforcements, 0);
            var log = new List<string>();
            e.OnLog += l => log.Add(l);
            e.NotifyPublic();
            var cast = e.TryActivateSpellTrap(p, hole, fromHand: true);
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
            DrainCombat(e);
            var phantom = LinksOf(log, "Celtic Guardian").Concat(LinksOf(log, "Blue-Eyes White Dragon")).ToList();
            var deferred = log.Where(l => l.Contains("activate once the current Chain has resolved")).ToList();
            Check("Dark Hole on vanillas: no deferred 'sent to the GY' Chain and no Chain Link for a vanilla",
                reached && cast && p.Graveyard.Contains(mine) && opp.Graveyard.Contains(theirs) &&
                phantom.Count == 0 && deferred.Count == 0 && !e.Chain.HasLinks,
                $"reached={reached} cast={cast} phantom=[{string.Join(" | ", phantom)}] deferred={deferred.Count} " +
                $"chain={e.Chain.HasLinks} log: {Tail(log)}");
        }

        static void FlippedByEffect(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI's Swords of Revealing Light flips the player's 2 face-down vanillas: no FLIP "Chain Link" for them,
            // and no "choose the one to put on the Chain next" pick for effects that do not exist.
            var e = MainOf(db, pDeck, aDeck, false, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, false);
            var b = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, false);
            var swords = PutInHand(e, opp, Swords);
            SetTrap(e, p, Reinforcements, 0);
            var log = new List<string>();
            e.OnLog += l => log.Add(l);
            e.NotifyPublic();
            var cast = e.TryActivateSpellTrap(opp, swords, fromHand: true);
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
            DrainCombat(e);
            var flipLines = log.Where(l => l.Contains("FLIP effect") || l.Contains("activate at the same time")).ToList();
            var pick = e.PendingActivation;
            Check("Swords flips 2 face-down vanillas: no FLIP Chain Link, no order pick, no Chain",
                reached && cast && a.FaceUp && b.FaceUp && flipLines.Count == 0 && pick == null && !e.Chain.HasLinks,
                $"reached={reached} cast={cast} up={a.FaceUp}/{b.FaceUp} flip=[{string.Join(" | ", flipLines)}] " +
                $"pick={pick?.Prompt} chain={e.Chain.HasLinks} log: {Tail(log)}");
        }

        static void RealFlipAndMassTriggers(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Positive control: Swords flips Man-Eater Bug (registered FLIP) and a vanilla — only Man-Eater Bug's
                // FLIP effect activates, and there is no order pick (one effect).
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bug = PlaceMonster(e, p, ManEaterBug, 0, BattlePosition.Defense, false);
                var vanilla = PlaceMonster(e, p, Celtic, 1, BattlePosition.Defense, false);
                PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
                var swords = PutInHand(e, opp, Swords);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                var cast = e.TryActivateSpellTrap(opp, swords, fromHand: true);
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                DrainCombat(e);
                var bugFlip = log.Exists(l => l.Contains("Man-Eater Bug's FLIP effect"));
                var vanillaFlip = log.Exists(l => l.Contains("Celtic Guardian's FLIP effect"));
                var orderPick = log.Exists(l => l.Contains("activate at the same time"));
                Check("Swords flips Man-Eater Bug + a vanilla: Man-Eater Bug's FLIP activates, alone (no order pick)",
                    reached && cast && bug.FaceUp && vanilla.FaceUp && bugFlip && !vanillaFlip && !orderPick,
                    $"reached={reached} cast={cast} bugFlip={bugFlip} vanillaFlip={vanillaFlip} orderPick={orderPick} " +
                    $"log: {Tail(log)}");
            }

            {
                // Positive control: Dark Hole destroys Sangan and a vanilla — Sangan's trigger waits for Dark Hole's Chain
                // and then applies; the vanilla is no Chain Link beside it (baseline: "CL1 Sangan, CL2 Celtic Guardian").
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, p, Sangan, 0, BattlePosition.Attack, true);
                PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                SetTrap(e, opp, Reinforcements, 0);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                var cast = e.TryActivateSpellTrap(p, hole, fromHand: true);
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                DrainCombat(e);
                var seatLines = log.Where(l => l.StartsWith("Chain: CL")).ToList();
                var sanganRan = log.Exists(l => l.StartsWith("Sangan:"));
                Check("Dark Hole on Sangan + a vanilla: Sangan's trigger applies after Dark Hole, the vanilla gets no link",
                    reached && cast && sanganRan && !seatLines.Exists(l => l.Contains("CL2")) &&
                    LinksOf(log, "Celtic Guardian").Count == 0,
                    $"reached={reached} cast={cast} seat=[{string.Join(" | ", seatLines)}] log: {Tail(log)}");
            }
        }

        /// <summary>
        /// Verifier objection (2026-10-18): a card whose "sent to the GY" trigger has a condition the send does not meet
        /// (Mystic Tomato / Shining Angel: "destroyed by battle") got a Chain Link when it was Tributed, used as a Ritual
        /// material or destroyed by Dark Hole, with the opponent's response window, and the link resolved to nothing.
        /// </summary>
        static void ConditionalTriggers(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (id, name) in new[] { (MysticTomato, "Mystic Tomato"), (ShiningAngel, "Shining Angel") })
            {
                // Tributed (not destroyed by battle) for Judge Man while the opponent has a Set Trap.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var trib = PlaceMonster(e, p, id, 0, BattlePosition.Attack, true);
                var judge = PutInHand(e, p, JudgeMan);
                SetTrap(e, opp, Reinforcements, 0);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                e.ToggleTribute(p, trib);
                var ok = e.TryNormalSummon(p, judge, asSet: false);
                var chainAfter = e.Chain.HasLinks;
                var chainWindow = e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse;
                var phantom = LinksOf(log, name);
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check($"Player: Tributing {name} (only 'destroyed by battle') for Judge Man seats no Chain Link",
                    reached && ok && p.MonstersOnField().Contains(judge) && p.Graveyard.Contains(trib) &&
                    phantom.Count == 0 && !chainAfter && !chainWindow,
                    $"reached={reached} ok={ok} phantom=[{string.Join(" | ", phantom)}] chain={chainAfter} " +
                    $"chainWindow={chainWindow} log: {Tail(log)}");
            }

            {
                // Dark Hole destroys Mystic Tomato (by a card effect, not by battle) and Blue-Eyes: nothing is deferred.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var tomato = PlaceMonster(e, p, MysticTomato, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                SetTrap(e, opp, Reinforcements, 0);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                var cast = e.TryActivateSpellTrap(p, hole, fromHand: true);
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                DrainCombat(e);
                var phantom = LinksOf(log, "Mystic Tomato");
                var deferred = log.Where(l => l.Contains("activate once the current Chain has resolved")).ToList();
                Check("Dark Hole on Mystic Tomato + a vanilla: no deferred 'sent to the GY' Chain, no Mystic Tomato link",
                    reached && cast && p.Graveyard.Contains(tomato) && opp.Graveyard.Contains(theirs) &&
                    phantom.Count == 0 && deferred.Count == 0 && !e.Chain.HasLinks &&
                    p.MonstersOnField().Count() == 0,
                    $"reached={reached} cast={cast} phantom=[{string.Join(" | ", phantom)}] deferred={deferred.Count} " +
                    $"chain={e.Chain.HasLinks} log: {Tail(log)}");
            }

            {
                // Black Luster Ritual Tributes 2 Mystic Tomatoes from the field: no "2 effects go on one Chain".
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var a = PlaceMonster(e, p, MysticTomato, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, MysticTomato, 1, BattlePosition.Attack, true);
                PutInHand(e, p, BlackLusterSoldier);
                var ritual = PutInHand(e, p, BlackLusterRitual);
                SetTrap(e, opp, Reinforcements, 0);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                var cast = e.TryActivateSpellTrap(p, ritual, fromHand: true);
                for (var k = 0; k < 8; k++)
                {
                    if (e.PendingActivation != null && e.PendingActivation.LegalTargets.Count > 0)
                        e.TrySelectEffectTarget(e.PendingActivation.LegalTargets[0]);
                    if (e.IsAwaitingResponse) e.PassResponse();
                }
                DrainCombat(e);
                var bls = p.MonstersOnField().Any(m => m.CardId == BlackLusterSoldier);
                var phantom = LinksOf(log, "Mystic Tomato");
                var grouped = log.Where(l => l.Contains("effects go on one Chain")).ToList();
                Check("Black Luster Ritual Tributes 2 Mystic Tomatoes: no Mystic Tomato Chain Link, no grouped Chain",
                    reached && cast && bls && p.Graveyard.Contains(a) && p.Graveyard.Contains(b) &&
                    phantom.Count == 0 && grouped.Count == 0 && !e.Chain.HasLinks,
                    $"reached={reached} cast={cast} bls={bls} phantom=[{string.Join(" | ", phantom)}] " +
                    $"grouped={grouped.Count} log: {Tail(log)}");
            }

            {
                // Positive control: Mystic Tomato and the opponent's Celtic Guardian (1400 each) destroy each other — a
                // held event, and Mystic Tomato WAS destroyed by battle: its trigger still activates.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var tomato = PlaceMonster(e, p, MysticTomato, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                SetDeck(p, Sangan, Sangan, Sangan);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                if (e.Phase != DuelPhase.Battle) e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var atk = e.TryAttack(p, tomato, celtic);
                var pickSeen = false;
                for (var k = 0; k < 8; k++)
                {
                    DrainCombat(e);
                    var pa = e.PendingActivation;
                    if (pa != null && ReferenceEquals(pa.Card, tomato))
                    {
                        pickSeen = true;
                        if (pa.LegalTargets.Count > 0) e.TrySelectEffectTarget(pa.LegalTargets[0]);
                    }
                    if (e.IsAwaitingResponse) e.PassResponse();
                }
                var activated = pickSeen || LinksOf(log, "Mystic Tomato").Count > 0 ||
                                p.MonstersOnField().Any(m => m.CardId == Sangan);
                Check("Control: Mystic Tomato destroyed by battle (both monsters destroyed): its trigger still activates",
                    reached && atk && p.Graveyard.Contains(tomato) && opp.Graveyard.Contains(celtic) && activated,
                    $"reached={reached} atk={atk} tomatoGy={p.Graveyard.Contains(tomato)} celticGy={opp.Graveyard.Contains(celtic)} " +
                    $"pick={pickSeen} field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} log: {Tail(log, 16)}");
            }
        }

        static void RealTriggers(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Positive control: Sangan Tributed with a vanilla for Blue-Eyes — Sangan's trigger is Chain Link 1, alone.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var sangan = PlaceMonster(e, p, Sangan, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var bewd = PutInHand(e, p, Bewd);
                SetTrap(e, opp, Reinforcements, 0);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                e.ToggleTribute(p, sangan);
                e.ToggleTribute(p, celtic);
                var ok = e.TryNormalSummon(p, bewd, asSet: false);
                var seatLines = log.Where(l => l.StartsWith("Chain: CL")).ToList();
                var sanganSeated = seatLines.Exists(l => l.Contains("CL1 Sangan's effect"));
                var celticLinks = LinksOf(log, "Celtic Guardian");
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                Check("Sangan Tributed with a vanilla: Sangan's trigger is seated as CL1, the vanilla gets no link",
                    reached && ok && sanganSeated && !seatLines.Exists(l => l.Contains("CL2")) && celticLinks.Count == 0,
                    $"reached={reached} ok={ok} seat=[{string.Join(" | ", seatLines)}] celtic=[{string.Join(" | ", celticLinks)}] " +
                    $"log: {Tail(log)}");
            }

            {
                // Sangan alone, no response possible: its search still resolves at once (no Chain, as before).
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var sangan = PlaceMonster(e, p, Sangan, 0, BattlePosition.Attack, true);
                var judge = PutInHand(e, p, JudgeMan);
                SetDeck(p, Celtic, Celtic, Celtic);
                var log = new List<string>();
                e.OnLog += l => log.Add(l);
                e.NotifyPublic();
                e.ToggleTribute(p, sangan);
                var ok = e.TryNormalSummon(p, judge, asSet: false);
                // The search is the player's pick (or resolved at once): either way Sangan's effect ran.
                var pick = e.PendingActivation;
                var ran = pick != null && ReferenceEquals(pick.Card, sangan) ||
                          p.Hand.Exists(h => h?.CardId == Celtic) ||
                          log.Exists(l => l.StartsWith("Sangan:"));
                Check("Sangan Tributed with nobody able to respond: its trigger still applies (no Chain Link needed)",
                    reached && ok && ran && !e.Chain.HasLinks,
                    $"reached={reached} ok={ok} pick={pick?.Card?.Name} hand={string.Join(",", p.Hand.Select(h => h.Name))} " +
                    $"log: {Tail(log)}");
            }
        }
    }
}
