using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Lab path only. <see cref="IsActive"/> is the single branch bit.</summary>
    public sealed class OcgLabDuelHost : IDisposable
    {
        public static OcgLabDuelHost Current { get; private set; }
        public static bool IsActive => Current != null;

        public IOcgDuelCore Core { get; }
        public OcgBoardView Board { get; }
        public DuelEngine ViewEngine { get; }
        public OcgMessage WaitingMessage { get; private set; }
        public int WaitingPlayer { get; private set; }
        public bool IsAwaitingChain { get; private set; }
        public float ChainWindowSeconds { get; private set; }
        public float ChainOpenedUnscaled { get; private set; }

        readonly CardDatabase _db;

        OcgLabDuelHost(IOcgDuelCore core, CardDatabase db)
        {
            Core = core;
            _db = db;
            Board = new OcgBoardView(db);
            ViewEngine = new DuelEngine();
            ViewEngine.AdoptExternalView(Board.Player, Board.Opponent);
        }

        public static OcgLabDuelHost Start(CardDatabase db, IOcgDuelCore core, OcgDuelStartInfo info)
        {
            Current?.Dispose();
            OcgSpeedTable.LoadFromStreaming();
            var host = new OcgLabDuelHost(core, db);
            Current = host;
            core.CreateDuel(info?.Seed != null && info.Seed.Length > 0 ? info.Seed[0] : 1u, info);
            SeedExtra(host.Board.Player, info?.PlayerExtra);
            SeedExtra(host.Board.Opponent, info?.OpponentExtra);
            host.Pump();
            return host;
        }

        public static OcgLabDuelHost StartStub(CardDatabase db)
        {
            var stub = StubOcgDuelCore.FromStreamingTape("OcgCore/replays/lab_dark_hole_vs_ox.json");
            return Start(db, stub, new OcgDuelStartInfo());
        }

        public static OcgLabDuelHost StartNative(CardDatabase db)
        {
            var core = new NativeOcgDuelCore();
            var info = new OcgDuelStartInfo
            {
                Seed = new uint[] { 1, 0, 0, 0 },
                PlayerMain = OcgLabDecks.ExpandMain("ocg_lab_player.json"),
                OpponentMain = OcgLabDecks.ExpandMain("ocg_lab_ai.json"),
                PlayerExtra = OcgLabDecks.ExpandExtra("ocg_lab_player.json"),
                OpponentExtra = OcgLabDecks.ExpandExtra("ocg_lab_ai.json")
            };
            return Start(db, core, info);
        }

        public void Pump()
        {
            const int guard = 48;
            for (var n = 0; n < guard; n++)
            {
                var msgs = Core.Process();
                Apply(msgs);
                if (Core.Status == OcgDuelStatus.End)
                {
                    WaitingMessage = null;
                    IsAwaitingChain = false;
                    SyncEngine();
                    return;
                }
                if (Core.IsWaiting)
                {
                    WaitingMessage = FindWaiting(msgs);
                    WaitingPlayer = Core.WaitingPlayer;
                    if (TryAutoPlaceOrPosition(WaitingMessage, out var autoBuf))
                    {
                        Core.SetResponse(autoBuf);
                        continue;
                    }
                    BeginWaitWindow();
                    MaybeAutoOpponent();
                    SyncEngine();
                    return;
                }
                if (msgs == null || msgs.Count == 0)
                    break;
            }
            SyncEngine();
        }

        public DuelCommandResult TryExecute(DuelistState who, DuelIntent intent)
        {
            if (intent == null)
                return DuelCommandResult.Fail(intent, "No move declared.");
            if (!Core.IsWaiting || WaitingMessage == null)
                return DuelCommandResult.Fail(intent, "Core is not waiting.");

            if (intent.Kind == DuelIntentKind.PassResponse)
            {
                if (WaitingMessage.MsgId != OcgMessageIds.SelectChain)
                    return DuelCommandResult.Fail(intent, "No response window is open.");
                Core.SetResponse(OcgResponseEncoder.ChainPass());
                IsAwaitingChain = false;
                Pump();
                return DuelCommandResult.Success(intent, "Pass.");
            }

            if (OcgMessageDecoder.TryIdle(WaitingMessage, out var idle))
            {
                byte[] buf = null;
                if (intent.Kind == DuelIntentKind.EnterBattlePhase)
                {
                    if (!OcgResponseEncoder.TryIdleBattle(idle, out buf))
                        return DuelCommandResult.Fail(intent, "Cannot enter Battle Phase now (first turn or wrong phase).");
                }
                else if (intent.Kind == DuelIntentKind.EndTurn)
                {
                    if (!OcgResponseEncoder.TryIdleEnd(idle, out buf))
                        return DuelCommandResult.Fail(intent, "Cannot end turn right now.");
                }
                else if (intent.Kind == DuelIntentKind.EnterMainPhase2)
                    return DuelCommandResult.Fail(intent, "Cannot enter Main Phase 2 now.");
                else if (intent.Kind == DuelIntentKind.NormalSummonAtk)
                {
                    buf = MatchIdle(idle.Summonable, intent.Card, 0);
                    if (buf == null)
                        buf = MatchIdle(idle.SpSummonable, intent.Card, 1);
                }
                else if (intent.Kind == DuelIntentKind.SetMonsterDef)
                    buf = MatchIdle(idle.Msetable, intent.Card, 3);
                else if (intent.Kind == DuelIntentKind.SetSpellTrap)
                    buf = MatchIdle(idle.Ssetable, intent.Card, 4);
                else if (intent.Kind == DuelIntentKind.Activate)
                {
                    buf = MatchIdle(idle.Activate, intent.Card, 5);
                    if (buf == null)
                        buf = MatchIdle(idle.SpSummonable, intent.Card, 1);
                }
                else if (intent.Kind == DuelIntentKind.ChangePosition || intent.Kind == DuelIntentKind.FlipSummon)
                    buf = MatchIdle(idle.Repositionable, intent.Card, 2);

                if (buf == null)
                    return DuelCommandResult.Fail(intent, "That action is not on the core idle list.");
                Core.SetResponse(buf);
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (OcgMessageDecoder.TryBattle(WaitingMessage, out var battle))
            {
                byte[] buf = null;
                if (intent.Kind == DuelIntentKind.EnterMainPhase2)
                    buf = battle.ToM2 ? OcgResponseEncoder.Battle(2, 0) : null;
                else if (intent.Kind == DuelIntentKind.EndTurn)
                    buf = battle.ToEp ? OcgResponseEncoder.Battle(3, 0) : null;
                else if (intent.Kind == DuelIntentKind.Activate)
                    buf = MatchIdle(battle.Activate, intent.Card, 0);
                else if (intent.Kind == DuelIntentKind.Attack || intent.Kind == DuelIntentKind.DirectAttack)
                    buf = MatchIdle(battle.Attackable, intent.Card, 1);
                if (buf == null)
                    return DuelCommandResult.Fail(intent, "That action is not on the core battle list.");
                Core.SetResponse(buf);
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (OcgMessageDecoder.TryChain(WaitingMessage, out var chain))
            {
                if (intent.Kind == DuelIntentKind.PassResponse || intent.Kind == DuelIntentKind.EndTurn)
                {
                    if (chain.Forced)
                        return DuelCommandResult.Fail(intent, "Must respond.");
                    Core.SetResponse(OcgResponseEncoder.ChainPass());
                    IsAwaitingChain = false;
                    Pump();
                    return DuelCommandResult.Success(intent, "Pass.");
                }
                var idx = IndexOf(chain.Chains, intent.Card);
                if (idx < 0)
                    return DuelCommandResult.Fail(intent, "That card is not a legal chain link.");
                Core.SetResponse(OcgResponseEncoder.ChainIndex(idx));
                IsAwaitingChain = false;
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            OcgSelectCard pick = null;
            if (OcgMessageDecoder.TrySelectCard(WaitingMessage, out pick) ||
                OcgMessageDecoder.TrySelectTribute(WaitingMessage, out pick))
            {
                if (intent.Kind == DuelIntentKind.PassResponse || intent.Kind == DuelIntentKind.CancelTarget)
                {
                    if (!pick.Cancelable)
                        return DuelCommandResult.Fail(intent, "Cannot cancel this selection.");
                    Core.SetResponse(OcgResponseEncoder.SelectCardCancel());
                    Pump();
                    return DuelCommandResult.Success(intent, "Cancel.");
                }
                var idx = new List<int>();
                var a = IndexOf(pick.Cards, intent.Card);
                if (a >= 0) idx.Add(a);
                var b = IndexOf(pick.Cards, intent.Target);
                if (b >= 0 && b != a) idx.Add(b);
                if (idx.Count < pick.Min)
                    return DuelCommandResult.Fail(intent, "Select " + pick.Min + " card(s).");
                if (idx.Count > pick.Max && pick.Max > 0)
                    idx.RemoveRange(pick.Max, idx.Count - pick.Max);
                Core.SetResponse(OcgResponseEncoder.SelectCardIndices(idx.ToArray()));
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (OcgMessageDecoder.TrySelectUnselect(WaitingMessage, out var un))
            {
                if (intent.Kind == DuelIntentKind.PassResponse || intent.Kind == DuelIntentKind.CancelTarget)
                {
                    if (!un.Cancelable && !un.Finishable)
                        return DuelCommandResult.Fail(intent, "Cannot cancel this selection.");
                    Core.SetResponse(OcgResponseEncoder.SelectCardCancel());
                    Pump();
                    return DuelCommandResult.Success(intent, "Cancel.");
                }
                var uidx = new List<int>();
                var ua = IndexOf(un.Cards, intent.Card);
                if (ua >= 0) uidx.Add(ua);
                var ub = IndexOf(un.Cards, intent.Target);
                if (ub >= 0 && ub != ua) uidx.Add(ub);
                if (uidx.Count < un.Min)
                    return DuelCommandResult.Fail(intent, "Select " + un.Min + " card(s).");
                if (uidx.Count > un.Max && un.Max > 0)
                    uidx.RemoveRange(un.Max, uidx.Count - un.Max);
                Core.SetResponse(OcgResponseEncoder.SelectCardIndices(uidx.ToArray()));
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (OcgMessageDecoder.TrySelectYesNo(WaitingMessage, out _) ||
                OcgMessageDecoder.TrySelectEffectYn(WaitingMessage, out _))
            {
                var yes = intent.Kind == DuelIntentKind.Activate || intent.Kind == DuelIntentKind.NormalSummonAtk;
                if (intent.Kind == DuelIntentKind.PassResponse || intent.Kind == DuelIntentKind.CancelTarget)
                    yes = false;
                Core.SetResponse(OcgResponseEncoder.YesNo(yes));
                Pump();
                return DuelCommandResult.Success(intent, yes ? "Yes." : "No.");
            }

            if (OcgMessageDecoder.TrySelectOption(WaitingMessage, out var opt))
            {
                var idx = intent.ZoneIndex >= 0 ? intent.ZoneIndex : 0;
                if (idx >= opt.Descs.Count)
                    return DuelCommandResult.Fail(intent, "Option index out of range.");
                Core.SetResponse(OcgResponseEncoder.Option(idx));
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (OcgMessageDecoder.TryAnnounceRace(WaitingMessage, out var race))
            {
                var bits = OcgMessageDecoder.FirstSetBits(race.Available, race.Count);
                if (bits == 0)
                    return DuelCommandResult.Fail(intent, "No race bits available.");
                Core.SetResponse(OcgResponseEncoder.AnnounceBits64(bits));
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (OcgMessageDecoder.TryAnnounceAttrib(WaitingMessage, out var attr))
            {
                var bits = OcgMessageDecoder.FirstSetBits(attr.Available, attr.Count);
                if (bits == 0)
                    return DuelCommandResult.Fail(intent, "No attribute bits available.");
                Core.SetResponse(OcgResponseEncoder.AnnounceBits32((int)bits));
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (OcgMessageDecoder.TryAnnounceNumber(WaitingMessage, out var num))
            {
                var idx = intent.ZoneIndex >= 0 ? intent.ZoneIndex : 0;
                if (idx >= num.Values.Count)
                    return DuelCommandResult.Fail(intent, "Number index out of range.");
                Core.SetResponse(OcgResponseEncoder.Option(idx));
                Pump();
                return DuelCommandResult.Success(intent, "OK");
            }

            if (WaitingMessage != null)
            {
                OcgOpponentPolicy.LogUnsupported(WaitingMessage, WaitingPlayer);
                return DuelCommandResult.Fail(intent, "Core is waiting on unsupported MSG " + WaitingMessage.MsgId);
            }
            return DuelCommandResult.Fail(intent, "Unhandled core wait.");
        }

        public void PassChainIfExpired()
        {
            if (!IsAwaitingChain || !Core.IsWaiting) return;
            Core.SetResponse(OcgResponseEncoder.ChainPass());
            IsAwaitingChain = false;
            Pump();
        }

        void BeginWaitWindow()
        {
            IsAwaitingChain = WaitingMessage != null && WaitingMessage.MsgId == OcgMessageIds.SelectChain;
            if (!IsAwaitingChain) return;
            ChainWindowSeconds = OcgSpeedTable.WindowSeconds(CombatAnimTimings.DefaultAttackImpact, _lastChainCode);
            ChainOpenedUnscaled = Time.unscaledTime;
        }

        int _lastChainCode;

        void Apply(IReadOnlyList<OcgMessage> msgs)
        {
            if (msgs == null) return;
            foreach (var m in msgs)
            {
                if (m != null && m.MsgId == OcgMessageIds.Chaining && m.Payload != null && m.Payload.Length >= 4)
                    _lastChainCode = BitConverter.ToInt32(m.Payload, 0);
                Board.Apply(m);
            }
        }

        void MaybeAutoOpponent()
        {
            if (!Core.IsWaiting || WaitingPlayer == 0) return;
            var stub = Core as StubOcgDuelCore;
            var batch = stub?.CurrentBatch;
            if (batch != null && batch.autoOpponent)
            {
                if (batch.autoChainPass)
                    Core.SetResponse(OcgResponseEncoder.ChainPass());
                else
                    Core.SetResponse(OcgResponseEncoder.Idle(batch.autoType, batch.autoIndex));
                Pump();
                return;
            }
            if (WaitingMessage == null)
            {
                OcgOpponentPolicy.LogUnsupported(null, WaitingPlayer);
                return;
            }
            if (!OcgOpponentPolicy.TryRespond(WaitingMessage, out var buf, out var pause))
            {
                OcgOpponentPolicy.LogUnsupported(WaitingMessage, WaitingPlayer);
                if (!string.IsNullOrEmpty(pause))
                    Debug.LogWarning("[WRLDZ OCG] opponent pause: " + pause);
                return;
            }
            Core.SetResponse(buf);
            Pump();
        }

        void SyncEngine()
        {
            ViewEngine.SyncViewMeta(Board.Phase, Math.Max(1, Board.TurnNumber), Board.TurnPlayer, false, null);
            ViewEngine.NotifyPublic();
        }

        static OcgMessage FindWaiting(IReadOnlyList<OcgMessage> msgs)
        {
            if (msgs == null) return null;
            for (var i = msgs.Count - 1; i >= 0; i--)
            {
                var id = msgs[i].MsgId;
                if (id == OcgMessageIds.SelectIdleCmd || id == OcgMessageIds.SelectBattleCmd ||
                    id == OcgMessageIds.SelectChain || id == OcgMessageIds.SelectCard ||
                    id == OcgMessageIds.SelectPlace || id == OcgMessageIds.SelectDisfield ||
                    id == OcgMessageIds.SelectEffectYn || id == OcgMessageIds.SelectYesNo ||
                    id == OcgMessageIds.SelectOption || id == OcgMessageIds.SelectPosition ||
                    id == OcgMessageIds.SelectTribute ||
                    id == OcgMessageIds.SelectUnselectCard ||
                    id == OcgMessageIds.AnnounceRace || id == OcgMessageIds.AnnounceAttrib ||
                    id == OcgMessageIds.AnnounceNumber)
                    return msgs[i];
            }
            return msgs.Count > 0 ? msgs[msgs.Count - 1] : null;
        }

        static void SeedExtra(DuelistState who, int[] codes)
        {
            if (who == null) return;
            who.ExtraDeck ??= new List<int>();
            who.ExtraDeck.Clear();
            if (codes == null) return;
            foreach (var id in codes)
                who.ExtraDeck.Add(id);
        }

        static byte[] MatchIdle(List<OcgCardRef> list, CardInstance card, int type)
        {
            var i = IndexOf(list, card);
            return i < 0 ? null : OcgResponseEncoder.Idle(type, i);
        }

        static int IndexOf(List<OcgCardRef> list, CardInstance card)
        {
            if (list == null || card == null) return -1;
            for (var i = 0; i < list.Count; i++)
                if (list[i].Code == card.CardId) return i;
            return -1;
        }

        static bool TryAutoPlaceOrPosition(OcgMessage waiting, out byte[] buf)
        {
            buf = null;
            if (waiting == null) return false;
            if ((waiting.MsgId == OcgMessageIds.SelectPlace ||
                 waiting.MsgId == OcgMessageIds.SelectDisfield) &&
                OcgMessageDecoder.TrySelectPlace(waiting, out var place) &&
                OcgMessageDecoder.TryFirstPlace(place, out var p, out var loc, out var seq))
            {
                buf = OcgResponseEncoder.Place(p, loc, seq);
                return true;
            }
            if (waiting.MsgId == OcgMessageIds.SelectPosition &&
                OcgMessageDecoder.TrySelectPosition(waiting, out var pos))
            {
                var bit = OcgMessageDecoder.FirstPosition(pos.Positions);
                if (bit == 0) return false;
                buf = OcgResponseEncoder.Position(bit);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (ReferenceEquals(Current, this))
                Current = null;
            Core?.Dispose();
        }
    }
}
