using System;
using UnityEngine;
using WRLDZ.Duel.Ocg;

namespace WRLDZ.EditorTools
{
    public static class OcgNativeResponseLoopTests
    {
        public static void BothPlayersDecideOnce()
        {
            if (!OcgPreflightState.TryUseNative(out var why))
                throw new Exception("native not gated: " + why);

            var player = OcgLabDecks.ExpandMain("ocg_lab_player.json");
            var ai = OcgLabDecks.ExpandMain("ocg_lab_ai.json");
            var pEx = OcgLabDecks.ExpandExtra("ocg_lab_player.json");
            var aEx = OcgLabDecks.ExpandExtra("ocg_lab_ai.json");
            var seen0 = false;
            var seen1 = false;
            using (var core = new NativeOcgDuelCore())
            {
                core.CreateDuel(1, new OcgDuelStartInfo
                {
                    Seed = new uint[] { 1, 0, 0, 0 },
                    PlayerMain = player,
                    OpponentMain = ai,
                    PlayerExtra = pEx,
                    OpponentExtra = aEx
                });

                OcgMessage waiting = null;
                for (var step = 0; step < 48; step++)
                {
                    var msgs = core.Process();
                    foreach (var m in msgs)
                    {
                        if (m.MsgId == OcgMessageIds.Retry)
                            throw new Exception("MSG_RETRY at step " + step);
                        if (m.MsgId == OcgMessageIds.SelectIdleCmd ||
                            m.MsgId == OcgMessageIds.SelectBattleCmd ||
                            m.MsgId == OcgMessageIds.SelectChain ||
                            m.MsgId == OcgMessageIds.SelectCard ||
                            m.MsgId == OcgMessageIds.SelectYesNo ||
                            m.MsgId == OcgMessageIds.SelectPlace ||
                            m.MsgId == OcgMessageIds.SelectDisfield ||
                            m.MsgId == OcgMessageIds.SelectPosition ||
                            m.MsgId == OcgMessageIds.SelectTribute ||
                            m.MsgId == OcgMessageIds.SelectUnselectCard ||
                            m.MsgId == OcgMessageIds.SelectEffectYn ||
                            m.MsgId == OcgMessageIds.SelectOption ||
                            m.MsgId == OcgMessageIds.AnnounceRace ||
                            m.MsgId == OcgMessageIds.AnnounceAttrib ||
                            m.MsgId == OcgMessageIds.AnnounceNumber)
                            waiting = m;
                    }
                    if (!core.IsWaiting)
                    {
                        if (core.Status == OcgDuelStatus.End) break;
                        continue;
                    }
                    if (waiting == null)
                    {
                        foreach (var m in msgs)
                            Debug.Log("[WRLDZ OCG] wait dump id=" + m.MsgId + " len=" + (m.Payload?.Length ?? 0));
                        throw new Exception("waiting without idle/battle/chain MSG id=" +
                                            (msgs != null && msgs.Count > 0 ? msgs[msgs.Count - 1].MsgId : -1));
                    }

                    var who = core.WaitingPlayer;
                    byte[] buf = null;
                    if (waiting.MsgId == OcgMessageIds.SelectIdleCmd &&
                        OcgMessageDecoder.TryIdle(waiting, out var idle) && idle.ToEp)
                        buf = OcgResponseEncoder.Idle(7, 0);
                    else if (waiting.MsgId == OcgMessageIds.SelectBattleCmd &&
                             OcgMessageDecoder.TryBattle(waiting, out var battle) && battle.ToEp)
                        buf = OcgResponseEncoder.Battle(3, 0);
                    else if (!OcgOpponentPolicy.TryRespond(waiting, out buf, out var pause) || buf == null)
                        throw new Exception("p" + who + " cannot answer " + waiting.MsgId + " " + pause);
                    if (who == 0) seen0 = true;
                    else seen1 = true;
                    core.SetResponse(buf);
                    waiting = null;
                    if (seen0 && seen1) break;
                }
            }
            if (!seen0 || !seen1)
                throw new Exception("need both players to decide once, p0=" + seen0 + " p1=" + seen1);
            Debug.Log("[WRLDZ OCG] native response loop: both players decided");
        }
    }
}
