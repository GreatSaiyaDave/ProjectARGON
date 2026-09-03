using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Duel.Ocg
{
    [Serializable]
    public class OcgTapeFile
    {
        public OcgTapeBatch[] batches;
    }

    [Serializable]
    public class OcgTapeBatch
    {
        public string wait;
        public int waitingPlayer;
        public OcgTapeMsg[] messages;
        public bool autoOpponent;
        public int autoType;
        public int autoIndex;
        public bool autoChainPass;
    }

    [Serializable]
    public class OcgTapeMsg
    {
        public string kind;
        public int player;
        public int phase;
        public int[] codes;
        public OcgTapeCard[] summonable;
        public OcgTapeCard[] activate;
        public int toBp;
        public int toEp;
        public int code;
        public OcgTapeLoc from;
        public OcgTapeLoc to;
        public int seq;
        public int loc;
        public int pos;
    }

    [Serializable]
    public class OcgTapeCard
    {
        public int code;
        public int controller;
        public int location;
        public int sequence;
    }

    [Serializable]
    public class OcgTapeLoc
    {
        public int controller;
        public int location;
        public int sequence;
        public int position;
    }

    public static class OcgTapeCodec
    {
        public static OcgTapeFile Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new OcgTapeFile { batches = Array.Empty<OcgTapeBatch>() };
            var file = JsonUtility.FromJson<OcgTapeFile>(json);
            if (file.batches == null) file.batches = Array.Empty<OcgTapeBatch>();
            return file;
        }

        public static List<OcgMessage> BuildMessages(OcgTapeBatch batch)
        {
            var list = new List<OcgMessage>();
            if (batch?.messages == null) return list;
            foreach (var m in batch.messages)
            {
                if (m == null || string.IsNullOrEmpty(m.kind)) continue;
                list.Add(BuildOne(m));
            }
            return list;
        }

        static OcgMessage BuildOne(OcgTapeMsg m)
        {
            var w = new OcgWriter();
            int id;
            switch (m.kind)
            {
                case "new_turn":
                    id = OcgMessageIds.NewTurn;
                    w.U8(m.player);
                    break;
                case "new_phase":
                    id = OcgMessageIds.NewPhase;
                    w.U16(m.phase);
                    break;
                case "draw":
                    id = OcgMessageIds.Draw;
                    w.U8(m.player);
                    var codes = m.codes ?? Array.Empty<int>();
                    w.U32(codes.Length);
                    foreach (var c in codes) w.U32(c);
                    break;
                case "idle":
                    id = OcgMessageIds.SelectIdleCmd;
                    WriteIdle(w, m);
                    break;
                case "summoning":
                    id = OcgMessageIds.Summoning;
                    w.U32(m.code);
                    w.U8(m.player);
                    w.U8(m.loc != 0 ? m.loc : OcgLocation.Mzone);
                    w.U32(m.seq);
                    w.U32(m.pos != 0 ? m.pos : OcgPos.FaceUpAttack);
                    break;
                case "summoned":
                    id = OcgMessageIds.Summoned;
                    break;
                case "move":
                    id = OcgMessageIds.Move;
                    w.U32(m.code);
                    WriteLoc(w, m.from);
                    WriteLoc(w, m.to);
                    w.U32(0);
                    break;
                case "chaining":
                    id = OcgMessageIds.Chaining;
                    w.U32(m.code);
                    break;
                case "select_chain":
                    id = OcgMessageIds.SelectChain;
                    w.U8(m.player);
                    w.U8(0);
                    w.U8(0);
                    w.U32(0);
                    w.U32(0);
                    w.U32(0);
                    break;
                case "chain_solving":
                    id = OcgMessageIds.ChainSolving;
                    w.U8(0);
                    break;
                case "chain_solved":
                    id = OcgMessageIds.ChainSolved;
                    w.U8(0);
                    break;
                case "chain_end":
                    id = OcgMessageIds.ChainEnd;
                    break;
                default:
                    id = 0;
                    break;
            }
            return new OcgMessage { MsgId = id, Payload = w.ToArray() };
        }

        static void WriteIdle(OcgWriter w, OcgTapeMsg m)
        {
            w.U8(m.player);
            WriteCardList(w, m.summonable, u32Seq: true);
            WriteCardList(w, null, true);
            WriteCardList(w, null, false);
            WriteCardList(w, null, true);
            WriteCardList(w, null, true);
            var act = m.activate ?? Array.Empty<OcgTapeCard>();
            w.U32(act.Length);
            foreach (var c in act)
            {
                if (c == null) continue;
                w.U32(c.code);
                w.U8(c.controller);
                w.U8(c.location);
                w.U32(c.sequence);
                w.U64(0);
                w.U8(0);
            }
            w.U8(m.toBp);
            w.U8(m.toEp);
            w.U8(0);
        }

        static void WriteCardList(OcgWriter w, OcgTapeCard[] cards, bool u32Seq)
        {
            cards ??= Array.Empty<OcgTapeCard>();
            w.U32(cards.Length);
            foreach (var c in cards)
            {
                if (c == null) continue;
                w.U32(c.code);
                w.U8(c.controller);
                w.U8(c.location);
                if (u32Seq) w.U32(c.sequence);
                else w.U8(c.sequence);
            }
        }

        static void WriteLoc(OcgWriter w, OcgTapeLoc loc)
        {
            loc ??= new OcgTapeLoc();
            w.Loc(new OcgLoc
            {
                Controller = loc.controller,
                Location = loc.location,
                Sequence = loc.sequence,
                Position = loc.position
            });
        }
    }
}
