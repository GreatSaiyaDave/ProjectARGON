using UnityEngine;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Generic opponent answers. No card names. Unknown waits return null (pause).</summary>
    public static class OcgOpponentPolicy
    {
        public static bool TryRespond(OcgMessage waiting, out byte[] response, out string pauseReason)
        {
            response = null;
            pauseReason = null;
            if (waiting == null)
            {
                pauseReason = "no waiting message";
                return false;
            }

            if (waiting.MsgId == OcgMessageIds.SelectChain)
            {
                if (OcgMessageDecoder.TryChain(waiting, out var chain) && chain.Forced)
                    response = OcgResponseEncoder.ChainIndex(0);
                else
                    response = OcgResponseEncoder.ChainPass();
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectIdleCmd &&
                OcgMessageDecoder.TryIdle(waiting, out var idle))
            {
                if (idle.Summonable.Count > 0)
                    response = OcgResponseEncoder.Idle(0, 0);
                else if (idle.Activate.Count > 0)
                    response = OcgResponseEncoder.Idle(5, 0);
                else if (idle.SpSummonable.Count > 0)
                    response = OcgResponseEncoder.Idle(1, 0);
                else if (idle.ToEp)
                    response = OcgResponseEncoder.Idle(7, 0);
                else if (idle.ToBp)
                    response = OcgResponseEncoder.Idle(6, 0);
                else
                {
                    pauseReason = "idle has no generic move";
                    return false;
                }
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectCard &&
                OcgMessageDecoder.TrySelectCard(waiting, out var sel))
            {
                var n = sel.Min;
                if (n < 1) n = 1;
                if (n > sel.Cards.Count) n = sel.Cards.Count;
                if (n < 1 && sel.Cancelable)
                {
                    response = OcgResponseEncoder.SelectCardCancel();
                    return true;
                }
                if (n < 1)
                {
                    pauseReason = "select card empty";
                    return false;
                }
                var idx = new int[n];
                for (var i = 0; i < n; i++) idx[i] = i;
                response = OcgResponseEncoder.SelectCardIndices(idx);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectYesNo)
            {
                response = OcgResponseEncoder.YesNo(false);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectTribute &&
                OcgMessageDecoder.TrySelectTribute(waiting, out var trib))
            {
                var tn = trib.Min;
                if (tn < 1) tn = 1;
                if (tn > trib.Cards.Count) tn = trib.Cards.Count;
                if (tn < 1 && trib.Cancelable)
                {
                    response = OcgResponseEncoder.SelectCardCancel();
                    return true;
                }
                if (tn < 1)
                {
                    pauseReason = "tribute empty";
                    return false;
                }
                var tidx = new int[tn];
                for (var i = 0; i < tn; i++) tidx[i] = i;
                response = OcgResponseEncoder.SelectCardIndices(tidx);
                return true;
            }

            if ((waiting.MsgId == OcgMessageIds.SelectPlace ||
                 waiting.MsgId == OcgMessageIds.SelectDisfield) &&
                OcgMessageDecoder.TrySelectPlace(waiting, out var place) &&
                OcgMessageDecoder.TryFirstPlace(place, out var p, out var loc, out var seq))
            {
                response = OcgResponseEncoder.Place(p, loc, seq);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectPosition &&
                OcgMessageDecoder.TrySelectPosition(waiting, out var pos))
            {
                var bit = OcgMessageDecoder.FirstPosition(pos.Positions);
                if (bit == 0)
                {
                    pauseReason = "position mask empty";
                    return false;
                }
                response = OcgResponseEncoder.Position(bit);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectUnselectCard &&
                OcgMessageDecoder.TrySelectUnselect(waiting, out var un))
            {
                if (un.Finishable || (un.Cancelable && un.Min <= 0))
                {
                    response = OcgResponseEncoder.SelectCardCancel();
                    return true;
                }
                var unN = un.Min;
                if (unN < 1) unN = 1;
                if (unN > un.Cards.Count) unN = un.Cards.Count;
                if (unN < 1)
                {
                    pauseReason = "unselect empty";
                    return false;
                }
                var uidx = new int[unN];
                for (var i = 0; i < unN; i++) uidx[i] = i;
                response = OcgResponseEncoder.SelectCardIndices(uidx);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectEffectYn)
            {
                response = OcgResponseEncoder.YesNo(false);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectOption &&
                OcgMessageDecoder.TrySelectOption(waiting, out var opt) && opt.Descs.Count > 0)
            {
                response = OcgResponseEncoder.Option(0);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.AnnounceRace &&
                OcgMessageDecoder.TryAnnounceRace(waiting, out var race))
            {
                var bits = OcgMessageDecoder.FirstSetBits(race.Available, race.Count);
                if (bits == 0)
                {
                    pauseReason = "announce race empty";
                    return false;
                }
                response = OcgResponseEncoder.AnnounceBits64(bits);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.AnnounceAttrib &&
                OcgMessageDecoder.TryAnnounceAttrib(waiting, out var attr))
            {
                var bits = OcgMessageDecoder.FirstSetBits(attr.Available, attr.Count);
                if (bits == 0)
                {
                    pauseReason = "announce attrib empty";
                    return false;
                }
                response = OcgResponseEncoder.AnnounceBits32((int)bits);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.AnnounceNumber &&
                OcgMessageDecoder.TryAnnounceNumber(waiting, out var num) && num.Values.Count > 0)
            {
                response = OcgResponseEncoder.Option(0);
                return true;
            }

            if (waiting.MsgId == OcgMessageIds.SelectBattleCmd &&
                OcgMessageDecoder.TryBattle(waiting, out var battle))
            {
                if (battle.ToM2)
                    response = OcgResponseEncoder.Battle(2, 0);
                else if (battle.ToEp)
                    response = OcgResponseEncoder.Battle(3, 0);
                else if (battle.Attackable.Count > 0)
                    response = OcgResponseEncoder.Battle(1, 0);
                else
                {
                    pauseReason = "battle has no generic move";
                    return false;
                }
                return true;
            }

            pauseReason = "unsupported wait msg " + waiting.MsgId;
            return false;
        }

        public static void LogUnsupported(OcgMessage msg, int player)
        {
            var len = msg?.Payload != null ? msg.Payload.Length : 0;
            var hex = SanitizeHex(msg?.Payload, 64);
            Debug.LogWarning(
                "[WRLDZ OCG] unsupported MSG id=" + (msg != null ? msg.MsgId : -1) +
                " player=" + player + " len=" + len + " hex=" + hex + " — paused, not auto-pass");
        }

        static string SanitizeHex(byte[] buf, int cap)
        {
            if (buf == null || buf.Length == 0) return "";
            var n = buf.Length < cap ? buf.Length : cap;
            var chars = new char[n * 2];
            const string hex = "0123456789abcdef";
            for (var i = 0; i < n; i++)
            {
                chars[i * 2] = hex[buf[i] >> 4];
                chars[i * 2 + 1] = hex[buf[i] & 0xf];
            }
            return new string(chars) + (buf.Length > cap ? "…" : "");
        }
    }
}
