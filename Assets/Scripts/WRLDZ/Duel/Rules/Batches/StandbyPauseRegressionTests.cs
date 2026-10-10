using System;
using WRLDZ.Data;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// The Standby Phase that <see cref="DuelEngine"/> holds for a human's standby-only ignition (Blast Juggler).
    /// Before <see cref="DuelIntentKind.EnterMainPhase1"/> nothing outside the engine called
    /// <see cref="DuelEngine.ContinueAfterStandby"/>: BATTLE / MAIN 2 refused the Standby Phase, so a human with a
    /// face-up Blast Juggler could only type "end turn" and lost their whole Main Phase, and the pause outlived
    /// that turn. Every check here goes through <see cref="DuelCommandService"/>, the gateway the UI and voice use.
    /// </summary>
    public static class StandbyPauseRegressionTests
    {
        const int BlastJuggler = 70138455;   // Lv3; Standby: Tribute this card, destroy 2 face-up monsters ≤1000 ATK
        const int ElectricLizard = 55875323; // 850 ATK
        const int YadoKaru = 29380133;       // 900 ATK
        const int Celtic = 91152256;         // 1400 ATK

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("Standby pause: " + name, ok, detail ?? "");

            MainPhase1LeavesThePause(db, pDeck, aDeck, Check);
            JugglerThenMainPhase1(db, pDeck, aDeck, Check);
            VoiceLeavesThePause(db, pDeck, aDeck, Check);
            EndTurnClearsThePause(db, pDeck, aDeck, Check);
            AiTurnNeverPauses(db, pDeck, aDeck, Check);
            HotseatPlayerTwoPauses(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Start(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman)
        {
            var e = new DuelEngine { HumanVsHuman = humanVsHuman };
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            return e;
        }

        /// <summary>Ends turn 1 (Player) and, when <paramref name="toPlayerTurn3"/>, turn 2 (Opponent).</summary>
        static void EndTurns(DuelEngine e, bool toPlayerTurn3)
        {
            e.TryEndTurnSafe(e.Player);
            if (e.IsAwaitingResponse) e.PassResponse();
            if (!toPlayerTurn3) return;
            e.TryEndTurnSafe(e.Opponent);
            if (e.IsAwaitingResponse) e.PassResponse();
        }

        static DuelCommandResult Do(DuelEngine e, DuelistState who, DuelIntentKind kind, CardInstance card = null) =>
            DuelCommandService.Execute(e, who, DuelIntent.Of(kind, card));

        static string State(DuelEngine e) =>
            $"turn={e.TurnNumber} tp={e.TurnPlayer?.Name} phase={e.Phase} paused={e.StandbyPausedFor?.Name ?? "-"}";

        // ───────────────────────────── checks ─────────────────────────────

        /// <summary>Juggler cannot be used (1 target): MAIN 1 is the way on, and the turn keeps its Battle Phase.</summary>
        static void MainPhase1LeavesThePause(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, false);
            var p = e.Player;
            PlaceMonster(e, p, BlastJuggler, 0, BattlePosition.Attack, true);
            PlaceMonster(e, e.Opponent, Celtic, 2, BattlePosition.Attack, true);
            EndTurns(e, toPlayerTurn3: true);
            var paused = e.Phase == DuelPhase.Standby && e.TurnPlayer == p && e.StandbyPausedFor == p &&
                         e.CanContinueAfterStandby(p);
            var battleRefused = !Do(e, p, DuelIntentKind.EnterBattlePhase).Ok && e.Phase == DuelPhase.Standby;
            var r = Do(e, p, DuelIntentKind.EnterMainPhase1);
            var inMain1 = r.Ok && e.Phase == DuelPhase.Main1 && e.StandbyPausedFor == null;
            if (e.IsAwaitingResponse) e.PassResponse();
            var again = Do(e, p, DuelIntentKind.EnterMainPhase1);
            var battle = Do(e, p, DuelIntentKind.EnterBattlePhase);
            Check("MAIN 1 moves a paused Standby Phase to Main Phase 1, then Battle is legal",
                paused && battleRefused && inMain1 && !again.Ok && battle.Ok && e.Phase == DuelPhase.Battle,
                $"paused={paused} battleRefused={battleRefused} main1='{r.Message}' again='{again.Message}' " +
                $"battle='{battle.Message}' {State(e)}");
        }

        /// <summary>Juggler resolves inside the pause; MAIN 1 waits for its targets, then works.</summary>
        static void JugglerThenMainPhase1(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, false);
            var p = e.Player;
            var o = e.Opponent;
            var juggler = PlaceMonster(e, p, BlastJuggler, 0, BattlePosition.Attack, true);
            var mine = PlaceMonster(e, p, ElectricLizard, 1, BattlePosition.Attack, true);
            var theirs = PlaceMonster(e, o, YadoKaru, 1, BattlePosition.Attack, true);
            PlaceMonster(e, o, Celtic, 2, BattlePosition.Attack, true);
            EndTurns(e, toPlayerTurn3: true);
            var activated = e.StandbyPausedFor == p && Do(e, p, DuelIntentKind.Activate, juggler).Ok;
            var midPick = activated && e.IsAwaitingEffectTarget;
            var heldWhilePicking = midPick && !Do(e, p, DuelIntentKind.EnterMainPhase1).Ok &&
                                   e.Phase == DuelPhase.Standby;
            e.TrySelectEffectTarget(mine);
            e.TrySelectEffectTarget(theirs);
            if (e.IsAwaitingResponse) e.PassResponse();
            var resolved = p.Graveyard.Contains(juggler) && p.Graveyard.Contains(mine) &&
                           o.Graveyard.Contains(theirs);
            var stillStandby = e.Phase == DuelPhase.Standby && e.CanContinueAfterStandby(p);
            var r = Do(e, p, DuelIntentKind.EnterMainPhase1);
            Check("Blast Juggler resolves in the pause, then MAIN 1 reaches Main Phase 1",
                activated && heldWhilePicking && resolved && stillStandby && r.Ok && e.Phase == DuelPhase.Main1,
                $"activated={activated} heldWhilePicking={heldWhilePicking} resolved={resolved} " +
                $"stillStandby={stillStandby} main1='{r.Message}' {State(e)}");
        }

        /// <summary>Voice: "pass" declines the Juggler window, "main phase 1" names it.</summary>
        static void VoiceLeavesThePause(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var line in new[] { "pass", "main phase 1" })
            {
                var e = Start(db, pDeck, aDeck, false);
                var p = e.Player;
                PlaceMonster(e, p, BlastJuggler, 0, BattlePosition.Attack, true);
                EndTurns(e, toPlayerTurn3: true);
                var paused = e.StandbyPausedFor == p;
                var r = DuelCommandService.Announce(e, p, line, "voice");
                Check($"voice \"{line}\" leaves the pause for Main Phase 1",
                    paused && r.Ok && e.Phase == DuelPhase.Main1 && e.StandbyPausedFor == null,
                    $"paused={paused} r='{r.Message}' {State(e)}");
            }
        }

        /// <summary>"End turn" from the pause (the only way out before) leaves no stale pause behind.</summary>
        static void EndTurnClearsThePause(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, false);
            var p = e.Player;
            PlaceMonster(e, p, BlastJuggler, 0, BattlePosition.Attack, true);
            EndTurns(e, toPlayerTurn3: true);
            var paused = e.StandbyPausedFor == p;
            var ended = Do(e, p, DuelIntentKind.EndTurn).Ok;
            Check("End Turn from the pause clears it",
                paused && ended && e.TurnPlayer == e.Opponent && e.StandbyPausedFor == null &&
                !e.CanContinueAfterStandby(p),
                $"paused={paused} ended={ended} {State(e)}");
        }

        /// <summary>The AI's Blast Juggler never holds its Standby Phase (SimpleAi starts in Main Phase 1).</summary>
        static void AiTurnNeverPauses(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, false);
            var o = e.Opponent;
            PlaceMonster(e, o, BlastJuggler, 0, BattlePosition.Attack, true);
            EndTurns(e, toPlayerTurn3: false);
            var mainOne = e.TurnPlayer == o && e.Phase == DuelPhase.Main1 && e.StandbyPausedFor == null;
            var refused = !Do(e, e.Player, DuelIntentKind.EnterMainPhase1).Ok && e.TurnPlayer == o;
            Check("an AI turn with Blast Juggler goes straight to Main Phase 1",
                mainOne && refused, $"mainOne={mainOne} refused={refused} {State(e)}");
        }

        /// <summary>Hotseat: Player 2 is human, so their Blast Juggler holds their Standby Phase too.</summary>
        static void HotseatPlayerTwoPauses(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, true);
            var o = e.Opponent;
            PlaceMonster(e, o, BlastJuggler, 0, BattlePosition.Attack, true);
            EndTurns(e, toPlayerTurn3: false);
            var paused = e.TurnPlayer == o && e.Phase == DuelPhase.Standby && e.StandbyPausedFor == o;
            var notP1 = !Do(e, e.Player, DuelIntentKind.EnterMainPhase1).Ok && e.Phase == DuelPhase.Standby;
            var r = Do(e, o, DuelIntentKind.EnterMainPhase1);
            Check("hotseat Player 2 pauses in Standby and MAIN 1 is theirs",
                paused && notP1 && r.Ok && e.Phase == DuelPhase.Main1,
                $"paused={paused} notP1={notP1} main1='{r.Message}' {State(e)}");
        }
    }
}
