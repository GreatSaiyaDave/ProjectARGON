using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "ZoneCapsAndPhaseWindows": the opponent's Standby window (Thunder of Ruler), the safe jump to the End Phase
    /// (Neko Mane King) and the usable-zone caps every placement path reads (Kaiser Colosseum, Spatial Collapse).
    /// </summary>
    public static class ZoneCapsAndPhaseWindowsRegressionTests
    {
        const int ThunderOfRuler = 91781589;
        const int NekoManeKing = 11021521;
        const int KaiserColosseum = 35059553;
        const int SpatialCollapse = 20644748;

        const int DarkHole = 53129443;
        const int Raigeki = 12580477;
        const int Scapegoat = 73915051;
        const int ChangeOfHeart = 4031928;
        const int OjamaTrio = 29843091;
        const int PotOfGreed = 55144522;
        const int Yami = 59197169;
        const int SoulExchange = 68005187;
        const int Dimensionhole = 22959079;

        const int Celtic = 91152256;        // EARTH Warrior Lv4 1400/1200
        const int Gaia = 6368038;           // EARTH Warrior Lv7 2300/2100
        const int MysticalElf = 15025844;   // LIGHT Spellcaster Lv4 800/2000
        const int GiantSoldier = 13039848;  // EARTH Rock Lv3 1300/2000
        const int SummonedSkull = 70781052; // DARK Fiend Lv6 2500/1200
        const int BattleOx = 5053103;       // EARTH Beast-Warrior Lv4 1700/1000

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("ZoneCapsAndPhaseWindows: " + name, ok, detail);

            Guard(Check, "compile shapes", () => CompileShapes(db, Check));
            Guard(Check, "Thunder of Ruler", () => ThunderOfRulerDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Neko Mane King", () => NekoManeKingDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Kaiser Colosseum", () => KaiserColosseumDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Spatial Collapse", () => SpatialCollapseDuels(db, pDeck, aDeck, Check));
            Guard(Check, "AI under zone caps", () => AiUnderCaps(db, pDeck, aDeck, Check));
            Guard(Check, "Kaiser Colosseum Tributes", () => KaiserTributeDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Neko Mane King (AI turn)", () => NekoInAiTurn(db, pDeck, aDeck, Check));
            Guard(Check, "Banished return under a cap", () => ReturnUnderCap(db, pDeck, aDeck, Check));
            Guard(Check, "v178 review fixes", () => ReviewFixes(db, pDeck, aDeck, Check));
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

        static void ClearPiles(DuelEngine e)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 12 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>The player's own Main Phase 1 of game turn 2 or later (the Battle Phase is legal).</summary>
        static bool ReachPlayerMain(DuelEngine e)
        {
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 2); t++)
            {
                Settle(e);
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 2) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            Settle(e);
            return e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1;
        }

        static bool StandbyWindowFor(DuelEngine e, DuelistState who) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.StandbyResponse &&
            e.PendingResponse.Responder == who && e.Phase == DuelPhase.Standby;

        static CompiledCardProgram CompileText(string text, string type) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990178, name = "Near-miss Card", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = type.Contains("Monster") ? "Beast" : "Normal", desc = text
            });

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static int Cards(DuelistState who) => ZoneCapsRuntime.FieldCards(who);

        /// <summary>Run SimpleAi's whole turn synchronously (bounded: a step cap instead of waiting forever).</summary>
        static bool RunAiTurn(DuelEngine e, int maxSteps = 4000)
        {
            var prev = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var r = SimpleAi.TakeTurnRoutine(e, null);
                for (var i = 0; i < maxSteps; i++)
                    if (!r.MoveNext())
                        return true;
                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prev;
            }
        }

        static string Tail(List<string> log, int n = 10) => string.Join(" | ", log.Skip(Math.Max(0, log.Count - n)));

        // ───────────────────────── compile shape ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var def = db.Get(ThunderOfRuler);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Thunder of Ruler: Implemented — one Activate clause, opponent's Standby Phase, no Battle Phase",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented && prog.FullyCompiled &&
                    prog.ClauseList.Count == 1 && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.ForbidBattlePhaseThisTurn &&
                    c.ActivationCondition == StandbyWindowRuntime.OpponentStandbyPhase &&
                    !c.RequiresTargetChoice && StandbyWindowRuntime.AnswersWindow(prog),
                    $"status={CardEffectStatus.Classify(def)} fc={prog?.FullyCompiled} n={prog?.ClauseList.Count}");
                var near = CompileText(def.desc.Replace("your opponent's Standby Phase", "your Standby Phase"), "Trap Card");
                Check("Near miss: a \"During your Standby Phase\" text does not compile to the opponent's Standby window",
                    near == null || !near.FullyCompiled || !StandbyWindowRuntime.AnswersWindow(near));
                var near2 = CompileText(def.desc.Replace("this turn", "next turn"), "Trap Card");
                Check("Near miss: \"no Battle Phase next turn\" does not compile to this turn's ban",
                    near2 == null || !near2.FullyCompiled ||
                    !near2.ClauseList.Exists(x => x.Action == EffectActionKind.ForbidBattlePhaseThisTurn));
            }

            {
                var def = db.Get(KaiserColosseum);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Kaiser Colosseum: Implemented — one continuous zone-cap clause",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented && prog.FullyCompiled &&
                    prog.ClauseList.Count == 1 && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.OpponentMonstersCappedByYourMonsterCount,
                    $"status={CardEffectStatus.Classify(def)} fc={prog?.FullyCompiled} n={prog?.ClauseList.Count}");
                var near = CompileText(def.desc.Replace("cannot place a monster on the field", "cannot Special Summon a monster"),
                    "Spell Card");
                Check("Near miss: a Kaiser text limiting only Special Summons does not compile to the zone cap",
                    near == null || !near.FullyCompiled ||
                    !near.ClauseList.Exists(x => x.Action == EffectActionKind.OpponentMonstersCappedByYourMonsterCount));
            }

            {
                var def = db.Get(SpatialCollapse);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Spatial Collapse: Implemented — the 5-card cap with its activation condition",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented && prog.FullyCompiled &&
                    prog.ClauseList.Count == 1 && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.EachPlayerFieldCardsCappedAtFive &&
                    c.ActivationCondition == ZoneCapsRuntime.BothPlayersFiveOrFewerFieldCards,
                    $"status={CardEffectStatus.Classify(def)} fc={prog?.FullyCompiled} n={prog?.ClauseList.Count}");
                var near = CompileText(def.desc.Replace("is 5.", "is 4."), "Trap Card");
                Check("Near miss: a 4-card Spatial Collapse text does not compile to the 5-card cap",
                    near == null || !near.FullyCompiled ||
                    !near.ClauseList.Exists(x => x.Action == EffectActionKind.EachPlayerFieldCardsCappedAtFive));
            }

            {
                var def = db.Get(NekoManeKing);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Neko Mane King: Implemented — a mandatory sent-to-GY trigger with the opponent's-effect condition",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented && prog.FullyCompiled &&
                    prog.ClauseList.Count == 1 && c.Timing == EffectTiming.SentToGyFromAnywhere &&
                    c.Action == EffectActionKind.PhaseJumpToEndPhase && c.RequiresSentByOpponentEffectOnTheirTurn,
                    $"status={CardEffectStatus.Classify(def)} fc={prog?.FullyCompiled} n={prog?.ClauseList.Count}");
                var near = CompileText(def.desc.Replace("by an opponent's card effect", "by a card effect"),
                    "Effect Monster");
                Check("Near miss: a Neko Mane King text sent by any card effect does not compile",
                    near == null || !near.FullyCompiled ||
                    !near.ClauseList.Exists(x => x.Action == EffectActionKind.PhaseJumpToEndPhase));
            }
        }

        // ───────────────────────── Thunder of Ruler ─────────────────────────

        static void ThunderOfRulerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player answers the AI's Standby Phase: the AI cannot conduct its Battle Phase this turn.
                var e = Fresh(db, pDeck, aDeck);
                var log = new List<string>();
                e.OnLog += m => log.Add(m);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var thunder = PlaceSetTrap(e, p, ThunderOfRuler, 0);
                PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var inOwnMain = e.CanActivateSpellTrap(p, thunder, fromHand: false);
                e.TryEndTurnSafe(p);
                var window = StandbyWindowFor(e, p);
                var turn = e.TurnNumber;
                var act = window && e.TryActivateSpellTrap(p, thunder, fromHand: false);
                Settle(e);
                var bp = e.TryEnterBattlePhase(o);
                Check("Thunder of Ruler: not activatable in your own Main Phase",
                    reached && !inOwnMain, $"reached={reached} own={inOwnMain}");
                Check("Thunder of Ruler (player): the opponent's Standby window opens before their Main Phase 1",
                    window, $"pr={e.PendingResponse?.Timing} phase={e.Phase} log={Tail(log)}");
                Check("Thunder of Ruler (player): it resolves, the turn goes on to Main Phase 1 with no Battle Phase",
                    act && e.TurnPlayer == o && e.TurnNumber == turn && e.Phase == DuelPhase.Main1 &&
                    o.BattlePhaseForbiddenTurn == turn && !e.CanConductBattlePhase && !bp &&
                    p.Graveyard.Contains(thunder) && !e.IsAwaitingResponse,
                    $"act={act} tp={e.TurnPlayer?.Name} phase={e.Phase} forb={o.BattlePhaseForbiddenTurn}/{turn} bp={bp} " +
                    $"log={Tail(log)}");
            }

            {
                // The player passes: the AI's turn goes on with its Battle Phase; Thunder stays Set and cannot be activated
                // in the AI's Main Phase 1.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var thunder = PlaceSetTrap(e, p, ThunderOfRuler, 0);
                PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                e.TryEndTurnSafe(p);
                var window = StandbyWindowFor(e, p);
                if (window) e.PassResponse();
                var main = e.TurnPlayer == o && e.Phase == DuelPhase.Main1;
                Settle(e);
                var inMain = e.CanActivateSpellTrap(p, thunder, fromHand: false);
                Check("Thunder of Ruler (player passes): Main Phase 1 follows, the Battle Phase stays legal",
                    reached && window && main && e.CanConductBattlePhase && !thunder.FaceUp && !inMain,
                    $"window={window} main={main} canBp={e.CanConductBattlePhase} inMain={inMain}");
            }

            {
                // The AI answers the player's Standby Phase while the player controls a monster.
                var e = Fresh(db, pDeck, aDeck);
                var log = new List<string>();
                e.OnLog += m => log.Add(m);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var thunder = PlaceSetTrap(e, o, ThunderOfRuler, 0);
                PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                e.TryEndTurnSafe(o);
                Settle(e);
                var turn = e.TurnNumber;
                Check("Thunder of Ruler (AI): answers the player's Standby Phase — no Battle Phase for the player this turn",
                    reached && e.TurnPlayer == p && e.Phase == DuelPhase.Main1 && o.Graveyard.Contains(thunder) &&
                    p.BattlePhaseForbiddenTurn == turn && !e.CanConductBattlePhase && !e.TryEnterBattlePhase(p),
                    $"reached={reached} tp={e.TurnPlayer?.Name} phase={e.Phase} gy={o.Graveyard.Contains(thunder)} " +
                    $"forb={p.BattlePhaseForbiddenTurn}/{turn} log={Tail(log)}");
            }

            {
                // No answer, no window: the AI's turn reaches Main Phase 1 at once.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                PlaceSetTrap(e, p, PotOfGreed, 0);
                e.NotifyPublic();
                e.TryEndTurnSafe(p);
                var noWindow = e.TurnPlayer == e.Opponent && e.Phase == DuelPhase.Main1 &&
                               e.PendingResponse?.Timing != ResponseTiming.StandbyResponse;
                Check("Opponent's Standby window: not opened when the responder has no legal answer",
                    reached && noWindow, $"tp={e.TurnPlayer?.Name} phase={e.Phase} pr={e.PendingResponse?.Timing}");
            }
        }

        // ───────────────────────── Neko Mane King ─────────────────────────

        static void NekoManeKingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player's Dark Hole destroys both Neko Mane Kings in the player's Main Phase 1: only the AI's triggers
                // (the player's own copy was destroyed by its controller's effect on its controller's turn) — the player's
                // turn goes to its End Phase and the AI's turn begins.
                var e = Fresh(db, pDeck, aDeck);
                var log = new List<string>();
                e.OnLog += m => log.Add(m);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var oppNeko = PlaceMonster(e, o, NekoManeKing, 0, BattlePosition.Attack, true);
                var myNeko = PlaceMonster(e, p, NekoManeKing, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Gaia, 1, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                e.NotifyPublic();
                var turn = e.TurnNumber;
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                var ended = log.Any(l => l.Contains("it becomes the End Phase"));
                var endPhaseLogged = log.Any(l => l.Contains($"[{p.Name}] End Phase"));
                Check("Neko Mane King: destroyed by the opponent's Dark Hole on their turn — their turn jumps to the End Phase",
                    reached && act && o.Graveyard.Contains(oppNeko) && p.Graveyard.Contains(myNeko) && ended &&
                    endPhaseLogged && e.TurnNumber == turn + 1 && e.TurnPlayer == o && p.BattlePhaseForbiddenTurn == turn,
                    $"act={act} ended={ended} endPhase={endPhaseLogged} turn={turn}->{e.TurnNumber} tp={e.TurnPlayer?.Name} " +
                    $"log={Tail(log, 14)}");
                var countNeko = log.Count(l => l.Contains("it becomes the End Phase"));
                Check("Neko Mane King: the copy its own controller's effect destroyed does not trigger",
                    countNeko == 1, $"count={countNeko}");
            }

            {
                // Raigeki in the player's Battle Phase-free Main Phase 1 with a Set monster to Summon afterwards: the turn
                // ends before the player can act again (no Main Phase 2, no Battle Phase).
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, o, NekoManeKing, 2, BattlePosition.Defense, false);
                var raigeki = PutInHand(e, p, Raigeki);
                var celtic = PutInHand(e, p, Celtic);
                e.NotifyPublic();
                var turn = e.TurnNumber;
                var act = e.TryActivateSpellTrap(p, raigeki, fromHand: true);
                Settle(e);
                var summon = e.TurnPlayer == p && e.TryNormalSummon(p, celtic, asSet: false);
                Check("Neko Mane King (face-down): destroyed by Raigeki — the player's turn is over, no Normal Summon after it",
                    reached && act && e.TurnNumber == turn + 1 && e.TurnPlayer == o && !summon && p.Hand.Contains(celtic),
                    $"act={act} turn={turn}->{e.TurnNumber} tp={e.TurnPlayer?.Name} summon={summon}");
            }

            {
                // Near miss: destroyed by battle — no trigger, the turn goes on.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var neko = PlaceMonster(e, o, NekoManeKing, 0, BattlePosition.Attack, true);
                var gaia = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var turn = e.TurnNumber;
                var bp = e.TryEnterBattlePhase(p);
                Settle(e);
                var atk = bp && e.TryAttack(p, gaia, neko);
                Settle(e);
                DrainCombat(e);
                Check("Neko Mane King: destroyed by battle — no End Phase jump",
                    reached && atk && o.Graveyard.Contains(neko) && e.TurnNumber == turn && e.TurnPlayer == p &&
                    e.Phase == DuelPhase.Battle,
                    $"bp={bp} atk={atk} gy={o.Graveyard.Contains(neko)} turn={turn}->{e.TurnNumber} phase={e.Phase}");
            }

            {
                // The jump from an idle Battle Phase ends it through its End Step; requested in the AI's own turn for the
                // AI's card, nothing happens.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var gaia = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
                var src = PlaceMonster(e, o, NekoManeKing, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var turn = e.TurnNumber;
                var bp = e.TryEnterBattlePhase(p);
                Settle(e);
                PhaseJumpRuntime.Request(e, o, src);
                e.NotifyPublic();
                Check("Phase jump: from an idle Battle Phase the turn goes through its End Phase to the next turn",
                    reached && bp && e.TurnNumber == turn + 1 && e.TurnPlayer == o,
                    $"bp={bp} turn={turn}->{e.TurnNumber} tp={e.TurnPlayer?.Name} phase={e.Phase}");
                var turn2 = e.TurnNumber;
                PhaseJumpRuntime.Request(e, o, src); // the AI's own turn: not "your opponent's turn"
                e.NotifyPublic();
                Check("Phase jump: a request in its source controller's own turn does nothing",
                    e.TurnNumber == turn2 && e.TurnPlayer == o && e.Phase == DuelPhase.Main1 &&
                    !PhaseJumpRuntime.Pending(e, o) && gaia != null,
                    $"turn={turn2}->{e.TurnNumber} phase={e.Phase}");
            }
        }

        // ───────────────────────── Kaiser Colosseum ─────────────────────────

        static void KaiserColosseumDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player activates Kaiser Colosseum with 1 monster; on the AI's turn the AI (1 monster) cannot Normal
                // Summon a 2nd, but may Tribute Summon (1 Tribute leaves room for 1).
                var e = Fresh(db, pDeck, aDeck);
                var log = new List<string>();
                e.OnLog += m => log.Add(m);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var soldier = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Defense, true);
                var kaiser = PutInHand(e, p, KaiserColosseum);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, kaiser, fromHand: true);
                Settle(e);
                var faceUp = p.TryFindSpellTrap(kaiser, out _) && kaiser.FaceUp;
                e.TryEndTurnSafe(p);
                Settle(e);
                var ox = PutInHand(e, o, BattleOx);
                var skull = PutInHand(e, o, SummonedSkull);
                var aiTurn = e.TurnPlayer == o && e.Phase == DuelPhase.Main1;
                var oxLegal = e.CanNormalSummonOrSet(o, ox);
                var oxDone = e.TryNormalSummon(o, ox, asSet: false);
                var skullLegal = e.CanNormalSummonOrSet(o, skull);
                e.ToggleTribute(o, soldier);
                var skullDone = e.TryNormalSummon(o, skull, asSet: false);
                Settle(e);
                Check("Kaiser Colosseum: activated from the hand, it stays face-up",
                    reached && act && faceUp, $"act={act} faceUp={faceUp} log={Tail(log)}");
                Check("Kaiser Colosseum: the opponent cannot Normal Summon past its controller's monster count",
                    aiTurn && !oxLegal && !oxDone && o.Hand.Contains(ox) && o.MonsterCount == 1,
                    $"aiTurn={aiTurn} oxLegal={oxLegal} oxDone={oxDone} n={o.MonsterCount}");
                Check("Kaiser Colosseum: a Tribute Summon that keeps the count within the cap is legal",
                    skullLegal && skullDone && o.TryFindMonster(skull, out _) && o.Graveyard.Contains(soldier) &&
                    o.MonsterCount == 1, $"legal={skullLegal} done={skullDone} n={o.MonsterCount} log={Tail(log)}");
            }

            {
                // Above the cap already (2 vs 1): a 1-Tribute Summon would still exceed it; a 2-Tribute Summon does not.
                // With no monster on the Kaiser controller's field there is no cap.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceFaceUpSpellTrap(e, p, KaiserColosseum, 0);
                PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Defense, true);
                PlaceMonster(e, o, BattleOx, 1, BattlePosition.Attack, true);
                var skull = PutInHand(e, o, SummonedSkull);
                var gaia = PutInHand(e, o, Gaia);
                var elf = PutInHand(e, o, MysticalElf);
                e.NotifyPublic();
                var oneTribute = e.CanNormalSummonOrSet(o, skull);
                var twoTributes = e.CanNormalSummonOrSet(o, gaia);
                var plain = e.CanNormalSummonOrSet(o, elf);
                p.MonsterZones[0].Occupant = null; // the Kaiser controller has no monster now
                p.Graveyard.Add(celtic);
                e.NotifyPublic();
                var uncapped = e.CanNormalSummonOrSet(o, elf);
                Check("Kaiser Colosseum: above the cap, a 1-Tribute Summon is refused, a 2-Tribute Summon allowed",
                    reached && !oneTribute && twoTributes && !plain,
                    $"one={oneTribute} two={twoTributes} plain={plain}");
                Check("Kaiser Colosseum: no cap while its controller controls no monster",
                    uncapped, $"uncapped={uncapped}");
            }

            {
                // Exemptions (Lua s.value): a control change is not limited (Change of Heart), and Tokens the Kaiser
                // controller's own effect places on the opponent's field are not limited (Ojama Trio).
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, true);
                var theirs = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceFaceUpSpellTrap(e, o, KaiserColosseum, 0);
                var coh = PutInHand(e, p, ChangeOfHeart);
                var ox = PutInHand(e, p, BattleOx);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, coh, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(theirs))
                    e.TrySelectEffectTarget(theirs);
                Settle(e);
                var took = p.TryFindMonster(theirs, out _);
                var oxLegal = e.CanNormalSummonOrSet(p, ox);
                Check("Kaiser Colosseum: a control change is not limited (Change of Heart takes the Kaiser side's monster)",
                    reached && act && took && p.MonsterCount == 2, $"act={act} took={took} n={p.MonsterCount}");
                Check("Kaiser Colosseum: its controller now controls no monster — no cap, the player may Normal Summon a 3rd",
                    oxLegal, $"oxLegal={oxLegal}");
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceFaceUpSpellTrap(e, p, KaiserColosseum, 0);
                var ojama = PlaceSetTrap(e, p, OjamaTrio, 1);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, ojama, fromHand: false);
                Settle(e);
                Check("Kaiser Colosseum: Ojama Tokens its controller's effect places on the opponent's field are not limited",
                    reached && act && o.MonsterCount == 3 && o.MonstersOnField().All(m => m.IsToken),
                    $"act={act} n={o.MonsterCount}");
            }
        }

        // ───────────────────────── Spatial Collapse ─────────────────────────

        static void SpatialCollapseDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Activated with 3 cards: a Summon and a Set fill the field to 5; then no Spell from the hand, no Field
                // Spell, no Set, no Monster Zone is usable.
                var e = Fresh(db, pDeck, aDeck);
                var log = new List<string>();
                e.OnLog += m => log.Add(m);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var spatial = PlaceSetTrap(e, p, SpatialCollapse, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, OjamaTrio, 1);
                PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Defense, true);
                var ox = PutInHand(e, p, BattleOx);
                var trap2 = PutInHand(e, p, OjamaTrio);
                var trap3 = PutInHand(e, p, OjamaTrio);
                var pot = PutInHand(e, p, PotOfGreed);
                var yami = PutInHand(e, p, Yami);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, spatial, fromHand: false);
                Settle(e);
                var applies = spatial.FaceUp && p.TryFindSpellTrap(spatial, out _);
                var summoned = e.TryNormalSummon(p, ox, asSet: false);
                var set = e.TrySetSpellTrap(p, trap2);
                var five = Cards(p) == 5;
                var potLegal = e.CanActivateSpellTrap(p, pot, fromHand: true);
                var yamiLegal = e.CanActivateSpellTrap(p, yami, fromHand: true);
                var setLegal = e.CanSetSpellTrap(p, trap3);
                var mz = e.UsableMonsterZoneCount(p);
                Check("Spatial Collapse: activated with 5 or fewer cards each; it applies once resolved",
                    reached && act && applies, $"act={act} applies={applies} log={Tail(log)}");
                Check("Spatial Collapse: a Summon and a Set are legal up to 5 cards",
                    summoned && set && five, $"summoned={summoned} set={set} cards={Cards(p)}");
                Check("Spatial Collapse: at 5 cards no Spell from the hand, no Field Spell, no Set, no Monster Zone",
                    !potLegal && !yamiLegal && !setLegal && mz == 0,
                    $"pot={potLegal} yami={yamiLegal} set={setLegal} mz={mz}");
            }

            {
                // Not activatable while a player has 6 cards on the field.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var spatial = PlaceSetTrap(e, p, SpatialCollapse, 0);
                for (var z = 0; z < 4; z++) PlaceMonster(e, o, GiantSoldier, z, BattlePosition.Defense, true);
                PlaceSetTrap(e, o, OjamaTrio, 0);
                PlaceSetTrap(e, o, OjamaTrio, 1);
                e.NotifyPublic();
                var six = e.CanActivateSpellTrap(p, spatial, fromHand: false);
                o.SpellTrapZones[1].Occupant = null;
                e.NotifyPublic();
                var five = e.CanActivateSpellTrap(p, spatial, fromHand: false);
                Check("Spatial Collapse: not activatable while the opponent has 6 cards; activatable at 5",
                    reached && !six && five, $"six={six} five={five}");
            }

            {
                // The adjust: the AI above 5 sends the excess (a Spell / Trap first); the player chooses theirs.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                for (var z = 0; z < 4; z++) PlaceMonster(e, o, GiantSoldier, z, BattlePosition.Defense, true);
                var oSet1 = PlaceSetTrap(e, o, OjamaTrio, 0);
                var oSet2 = PlaceSetTrap(e, o, OjamaTrio, 1);
                PlaceFaceUpSpellTrap(e, p, SpatialCollapse, 0);
                e.NotifyPublic();
                Check("Spatial Collapse (adjust, AI): 6 cards → the AI sends 1 Set card to the GY, 5 remain",
                    reached && Cards(o) == 5 && o.MonsterCount == 4 &&
                    (o.Graveyard.Contains(oSet1) ^ o.Graveyard.Contains(oSet2)),
                    $"cards={Cards(o)} monsters={o.MonsterCount} gy={o.Graveyard.Count}");
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, true);
                PlaceMonster(e, p, BattleOx, 2, BattlePosition.Attack, true);
                PlaceMonster(e, p, Gaia, 3, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, OjamaTrio, 1);
                PlaceSetTrap(e, p, OjamaTrio, 2);
                PlaceFaceUpSpellTrap(e, p, SpatialCollapse, 0);
                e.NotifyPublic();
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                            e.PendingActivation.LegalTargets.Contains(a);
                var first = asked && e.TrySelectEffectTarget(a);
                var askedAgain = e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                                 e.PendingActivation.LegalTargets.Contains(b) &&
                                 !e.PendingActivation.LegalTargets.Contains(a);
                var second = askedAgain && e.TrySelectEffectTarget(b);
                Settle(e);
                Check("Spatial Collapse (adjust, player): 7 cards → the player chooses 2, both go to the GY together",
                    reached && asked && first && askedAgain && second && Cards(p) == 5 && p.Graveyard.Contains(a) &&
                    p.Graveyard.Contains(b) && !e.IsAwaitingEffectTarget,
                    $"asked={asked} again={askedAgain} cards={Cards(p)} pending={e.PendingActivation?.TargetKind}");
            }
        }

        // ───────────────────────── AI under the caps ─────────────────────────

        static void AiUnderCaps(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // SimpleAi plays its whole turn under the player's Kaiser Colosseum: no Summon past the cap, the turn ends.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, true);
                PlaceFaceUpSpellTrap(e, p, KaiserColosseum, 0);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PutInHand(e, o, BattleOx);
                PutInHand(e, o, MysticalElf);
                e.NotifyPublic();
                var turn = e.TurnNumber;
                var done = RunAiTurn(e);
                Check("AI (Kaiser Colosseum): plays its turn within the cap and ends it",
                    reached && done && e.TurnNumber > turn && o.MonsterCount <= 1,
                    $"done={done} turn={turn}->{e.TurnNumber} n={o.MonsterCount}");
            }

            {
                // SimpleAi under its own Spatial Collapse: never above 5 cards, the turn ends.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var o = e.Opponent;
                PlaceFaceUpSpellTrap(e, o, SpatialCollapse, 0);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, GiantSoldier, 1, BattlePosition.Defense, true);
                PlaceSetTrap(e, o, OjamaTrio, 1);
                PutInHand(e, o, BattleOx);
                PutInHand(e, o, MysticalElf);
                PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, OjamaTrio);
                e.NotifyPublic();
                var turn = e.TurnNumber;
                var done = RunAiTurn(e);
                Check("AI (Spatial Collapse): plays its turn without passing 5 cards and ends it",
                    reached && done && e.TurnNumber > turn && Cards(o) <= 5,
                    $"done={done} turn={turn}->{e.TurnNumber} cards={Cards(o)}");
            }
        }

        // ───────────────────────── Kaiser Colosseum: Tributes from either field ─────────────────────────

        static bool ActivateSoulExchange(DuelEngine e, DuelistState who, CardInstance target)
        {
            var card = PutInHand(e, who, SoulExchange);
            if (!e.TryActivateSpellTrap(who, card, fromHand: true)) return false;
            if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(target))
                e.TrySelectEffectTarget(target);
            Settle(e);
            return TributeSummonsRuntime.IsForcedTribute(who, target);
        }

        static void KaiserTributeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Soul Exchange takes one of the Kaiser controller's 2 monsters as a Tribute: that lowers the cap to 1, so a
                // 1-Tribute Summon by a player who already controls 1 monster would exceed it — refused before any Tribute
                // leaves (Lua EFFECT_UNRELEASABLE_SUM).
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var target = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Defense, true);
                PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, true);
                PlaceFaceUpSpellTrap(e, o, KaiserColosseum, 0);
                e.NotifyPublic();
                var granted = ActivateSoulExchange(e, p, target);
                var skull = PutInHand(e, p, SummonedSkull);
                var done = e.TryNormalSummon(p, skull, asSet: false);
                Settle(e);
                Check("Kaiser Colosseum + Soul Exchange: a Tribute of the controller's monster that would break the cap is refused",
                    reached && granted && !done && p.Hand.Contains(skull) && o.TryFindMonster(target, out _) &&
                    p.TryFindMonster(mine, out _) && p.MonsterCount == 1 && o.MonsterCount == 2,
                    $"granted={granted} done={done} p={p.MonsterCount} o={o.MonsterCount}");
            }

            {
                // The same Tribute with no monster of the player's own: 1 vs 1 afterwards — legal.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var target = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Defense, true);
                PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, true);
                PlaceFaceUpSpellTrap(e, o, KaiserColosseum, 0);
                e.NotifyPublic();
                var granted = ActivateSoulExchange(e, p, target);
                var skull = PutInHand(e, p, SummonedSkull);
                var done = e.TryNormalSummon(p, skull, asSet: false);
                Settle(e);
                Check("Kaiser Colosseum + Soul Exchange: the Tribute Summon that ends 1 vs 1 is legal",
                    reached && granted && done && p.TryFindMonster(skull, out _) && o.Graveyard.Contains(target) &&
                    p.MonsterCount == 1 && o.MonsterCount == 1,
                    $"granted={granted} done={done} p={p.MonsterCount} o={o.MonsterCount}");
            }
        }

        // ───────────────────────── Neko Mane King in the AI's turn ─────────────────────────

        static void NekoInAiTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI's Raigeki destroys the player's Neko Mane King in the AI's Main Phase 1: the AI's turn jumps to its End
            // Phase (no Battle Phase, no more plays), its turn routine stops, and the player's turn begins.
            var e = Fresh(db, pDeck, aDeck);
            var log = new List<string>();
            e.OnLog += m => log.Add(m);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            ClearPiles(e);
            var p = e.Player;
            var o = e.Opponent;
            var neko = PlaceMonster(e, p, NekoManeKing, 0, BattlePosition.Attack, true);
            PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var raigeki = PutInHand(e, o, Raigeki);
            var ox = PutInHand(e, o, BattleOx);
            e.NotifyPublic();
            var turn = e.TurnNumber;
            var lp = p.LifePoints;
            var act = e.TryActivateSpellTrap(o, raigeki, fromHand: true);
            Settle(e);
            var jumped = e.TurnNumber == turn + 1 && e.TurnPlayer == p;
            var aiDone = RunAiTurn(e, 200); // the AI's routine sees it is no longer its turn
            Settle(e);
            Check("Neko Mane King (AI turn): the AI's Raigeki sends it — the AI's turn ends, no Battle Phase, no Summon",
                reached && act && p.Graveyard.Contains(neko) && jumped && aiDone && e.TurnPlayer == p &&
                o.Hand.Contains(ox) && p.LifePoints == lp && o.BattlePhaseForbiddenTurn == turn,
                $"act={act} jumped={jumped} aiDone={aiDone} tp={e.TurnPlayer?.Name} turn={turn}->{e.TurnNumber} " +
                $"lp={lp}->{p.LifePoints} log={Tail(log, 12)}");
        }

        // ───────────────────────── a banished monster returning under a cap ─────────────────────────

        static void ReturnUnderCap(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Dimensionhole banishes the player's monster until their next Standby Phase; meanwhile the AI's Kaiser
            // Colosseum caps the player at 1 monster (the AI controls 1). The monster cannot return to the field, so it is
            // sent to the GY (ygopro Duel.ReturnToField with no usable zone) — never lost between piles.
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachPlayerMain(e);
            ClearBoard(e);
            ClearPiles(e);
            var p = e.Player;
            var o = e.Opponent;
            var banished = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
            PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, true);
            var hole = PutInHand(e, p, Dimensionhole);
            e.NotifyPublic();
            var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
            if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(banished))
                e.TrySelectEffectTarget(banished);
            Settle(e);
            var away = p.Banished.Contains(banished);
            PlaceMonster(e, o, BattleOx, 0, BattlePosition.Attack, true);
            PlaceFaceUpSpellTrap(e, o, KaiserColosseum, 0);
            e.NotifyPublic();
            e.TryEndTurnSafe(p);
            Settle(e);
            e.TryEndTurnSafe(o);
            if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
            Settle(e);
            Check("Cap: a banished monster that cannot return under Kaiser Colosseum goes to the GY",
                reached && act && away && e.TurnPlayer == p && p.Graveyard.Contains(banished) &&
                !p.Banished.Contains(banished) && !p.TryFindMonster(banished, out _) && p.MonsterCount == 1,
                $"act={act} away={away} tp={e.TurnPlayer?.Name} gy={p.Graveyard.Contains(banished)} " +
                $"banished={p.Banished.Contains(banished)} n={p.MonsterCount}");
        }

        // ───────────────────────── v178 review fixes (v209 port) ─────────────────────────

        static void ReviewFixes(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Review (medium): the Standby window closes while a pick is still open (as a trigger of the Chain that
                // answered it would leave one). The AI's Standby Phase holds, its turn routine waits (it used to force End
                // Turn from the Standby Phase), and once the pick is answered the turn goes on to Main Phase 1.
                var e = Fresh(db, pDeck, aDeck);
                var log = new List<string>();
                e.OnLog += m => log.Add(m);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var o = e.Opponent;
                var thunder = PlaceSetTrap(e, p, ThunderOfRuler, 0);
                var a = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, true);
                e.NotifyPublic();
                e.TryEndTurnSafe(p);
                var window = StandbyWindowFor(e, p);
                CardInstance picked = null;
                if (window)
                    ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(e, p, thunder, new CardPickRequest
                    {
                        Candidates = () => new List<CardInstance> { a, b },
                        Kind = EffectTargetKind.AnyCardOnField,
                        Prompt = "Test: a pick still open when the Standby window closes.",
                        AiChoose = l => l.FirstOrDefault(),
                        Finish = c => picked = c,
                        HoldsChain = false,
                        HoldsStandby = false
                    });
                var asked = e.IsAwaitingEffectTarget;
                if (window) e.PassResponse();
                var held = e.TurnPlayer == o && e.Phase == DuelPhase.Standby && StandbyWindowRuntime.FinishHeld(e) &&
                           e.StandbyPausedFor == o && e.IsAwaitingEffectTarget;
                var prev = SimpleAi.StepDelay;
                SimpleAi.StepDelay = 0f;
                var finished = false;
                bool waited, answered, main;
                try
                {
                    var ai = SimpleAi.TakeTurnRoutine(e, null);
                    for (var i = 0; i < 40; i++) ai.MoveNext();
                    waited = e.TurnPlayer == o && e.Phase == DuelPhase.Standby &&
                             !log.Exists(m => m.Contains("Forcing End Turn"));
                    answered = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(a);
                    main = e.TurnPlayer == o && e.Phase == DuelPhase.Main1 && e.StandbyPausedFor == null &&
                           !StandbyWindowRuntime.FinishHeld(e);
                    for (var i = 0; i < 4000 && !finished; i++)
                        finished = !ai.MoveNext();
                }
                finally
                {
                    SimpleAi.StepDelay = prev;
                }

                Check("Standby window (pick still open): the Standby Phase holds and the AI's turn routine waits",
                    reached && window && asked && held && waited,
                    $"window={window} asked={asked} held={held} waited={waited} tp={e.TurnPlayer?.Name} phase={e.Phase} " +
                    $"log={Tail(log)}");
                Check("Standby window (pick answered): Main Phase 1 follows and the AI plays its turn to the end",
                    answered && picked == a && main && finished && e.TurnPlayer == p &&
                    !log.Exists(m => m.Contains("Forcing End Turn")),
                    $"answered={answered} picked={picked?.Name} main={main} finished={finished} tp={e.TurnPlayer?.Name} " +
                    $"phase={e.Phase} log={Tail(log)}");
            }

            {
                // Review (low): the AI controls Spatial Collapse and has 6 cards — it keeps the Spatial Collapse (the lowest
                // Spell & Trap Zone) and sends its other Spell / Trap Card.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var o = e.Opponent;
                for (var z = 0; z < 4; z++) PlaceMonster(e, o, GiantSoldier, z, BattlePosition.Defense, true);
                var spatial = PlaceFaceUpSpellTrap(e, o, SpatialCollapse, 0);
                var oSet = PlaceSetTrap(e, o, OjamaTrio, 1);
                e.NotifyPublic();
                Check("Spatial Collapse (adjust, AI controls it): the AI keeps its Spatial Collapse and sends its other card",
                    reached && Cards(o) == 5 && o.Graveyard.Contains(oSet) && !o.Graveyard.Contains(spatial) &&
                    o.SpellTrapZones[0].Occupant == spatial && o.MonsterCount == 4,
                    $"cards={Cards(o)} gy={string.Join(",", o.Graveyard.Select(c => c.Name))}");
            }

            {
                // v209 port: the v178 engine hooks' board refresh counted the Field Spell as a 6th Spell & Trap Card and sent
                // one to the GY with no cap card on the field. With no Kaiser Colosseum / Spatial Collapse nothing is capped.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerMain(e);
                ClearBoard(e);
                ClearPiles(e);
                var p = e.Player;
                var sets = new List<CardInstance>();
                for (var z = 0; z < 5; z++) sets.Add(PlaceSetTrap(e, p, PotOfGreed, z));
                var field = e.CreateCardInstance(Yami);
                field.FaceUp = true;
                p.FieldSpellZone.Occupant = field;
                e.NotifyPublic();
                Check("No zone-cap card: 5 Spell & Trap Cards and a Field Spell all stay on the field",
                    reached && sets.All(c => p.SpellTrapZones.Any(z => z.Occupant == c)) && p.FieldSpellZone.Occupant == field &&
                    p.Graveyard.Count == 0,
                    $"gy={string.Join(",", p.Graveyard.Select(c => c.Name))} field={p.FieldSpellZone.Occupant?.Name}");
            }
        }
    }
}
