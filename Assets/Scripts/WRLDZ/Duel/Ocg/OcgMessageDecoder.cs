using System.Collections.Generic;

namespace WRLDZ.Duel.Ocg
{
    public sealed class OcgIdleCommand
    {
        public int Player;
        public readonly List<OcgCardRef> Summonable = new();
        public readonly List<OcgCardRef> SpSummonable = new();
        public readonly List<OcgCardRef> Repositionable = new();
        public readonly List<OcgCardRef> Msetable = new();
        public readonly List<OcgCardRef> Ssetable = new();
        public readonly List<OcgCardRef> Activate = new();
        public bool ToBp;
        public bool ToEp;
        public bool CanShuffle;
    }

    public sealed class OcgBattleCommand
    {
        public int Player;
        public readonly List<OcgCardRef> Activate = new();
        public readonly List<OcgCardRef> Attackable = new();
        public readonly List<bool> Direct = new();
        public bool ToM2;
        public bool ToEp;
    }

    public sealed class OcgChainSelect
    {
        public int Player;
        public bool Forced;
        public readonly List<OcgCardRef> Chains = new();
    }

    public sealed class OcgSelectCard
    {
        public int Player;
        public bool Cancelable;
        public int Min;
        public int Max;
        public readonly List<OcgCardRef> Cards = new();
    }

    public sealed class OcgSelectPlace
    {
        public int Player;
        public int Count;
        public uint Flag;
    }

    public sealed class OcgSelectPosition
    {
        public int Player;
        public int Code;
        public int Positions;
    }

    public sealed class OcgSelectUnselect
    {
        public int Player;
        public bool Finishable;
        public bool Cancelable;
        public int Min;
        public int Max;
        public readonly List<OcgCardRef> Cards = new();
    }

    public sealed class OcgSelectOption
    {
        public int Player;
        public readonly List<ulong> Descs = new();
    }

    public sealed class OcgSelectEffectYn
    {
        public int Player;
        public int Code;
        public OcgLoc Loc;
        public ulong Desc;
    }

    public sealed class OcgAnnounceBits
    {
        public int Player;
        public int Count;
        public ulong Available;
    }

    public sealed class OcgAnnounceNumber
    {
        public int Player;
        public readonly List<int> Values = new();
    }

    public static class OcgMessageDecoder
    {
        public static bool TryNewPhase(OcgMessage msg, out int phaseBits)
        {
            phaseBits = 0;
            if (msg == null || msg.MsgId != OcgMessageIds.NewPhase) return false;
            var r = new OcgReader(msg.Payload);
            phaseBits = r.U16();
            return true;
        }

        public static bool TryNewTurn(OcgMessage msg, out int player)
        {
            player = 0;
            if (msg == null || msg.MsgId != OcgMessageIds.NewTurn) return false;
            player = new OcgReader(msg.Payload).U8();
            return true;
        }

        public static bool TryDraw(OcgMessage msg, out int player, out int[] codes)
        {
            player = 0;
            codes = System.Array.Empty<int>();
            if (msg == null || msg.MsgId != OcgMessageIds.Draw) return false;
            var r = new OcgReader(msg.Payload);
            player = r.U8();
            var n = r.U32();
            if (n < 0) n = 0;
            codes = new int[n];
            for (var i = 0; i < n; i++)
                codes[i] = r.U32();
            return true;
        }

        public static bool TryMove(OcgMessage msg, out int code, out OcgLoc from, out OcgLoc to, out int reason)
        {
            code = 0;
            from = default;
            to = default;
            reason = 0;
            if (msg == null || msg.MsgId != OcgMessageIds.Move) return false;
            var r = new OcgReader(msg.Payload);
            code = r.U32();
            from = r.Loc();
            to = r.Loc();
            reason = r.U32();
            return true;
        }

        public static bool TryIdle(OcgMessage msg, out OcgIdleCommand cmd)
        {
            cmd = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectIdleCmd) return false;
            var r = new OcgReader(msg.Payload);
            cmd = new OcgIdleCommand { Player = r.U8() };
            ReadListU32(r, cmd.Summonable);
            ReadListU32(r, cmd.SpSummonable);
            ReadListU8(r, cmd.Repositionable);
            ReadListU32(r, cmd.Msetable);
            ReadListU32(r, cmd.Ssetable);
            var actN = r.U32();
            for (var i = 0; i < actN; i++)
            {
                var c = r.CardU32Seq();
                r.U64();
                r.U8();
                cmd.Activate.Add(c);
            }
            cmd.ToBp = r.U8() != 0;
            cmd.ToEp = r.U8() != 0;
            cmd.CanShuffle = r.U8() != 0;
            return true;
        }

        public static bool TryBattle(OcgMessage msg, out OcgBattleCommand cmd)
        {
            cmd = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectBattleCmd) return false;
            var r = new OcgReader(msg.Payload);
            cmd = new OcgBattleCommand { Player = r.U8() };
            var actN = r.U32();
            for (var i = 0; i < actN; i++)
            {
                var c = r.CardU32Seq();
                r.U64();
                r.U8();
                cmd.Activate.Add(c);
            }
            var atkN = r.U32();
            for (var i = 0; i < atkN; i++)
            {
                cmd.Attackable.Add(r.CardU8Seq());
                cmd.Direct.Add(r.U8() != 0);
            }
            cmd.ToM2 = r.U8() != 0;
            cmd.ToEp = r.U8() != 0;
            return true;
        }

        public static bool TryChain(OcgMessage msg, out OcgChainSelect sel)
        {
            sel = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectChain) return false;
            var r = new OcgReader(msg.Payload);
            sel = new OcgChainSelect { Player = r.U8() };
            r.U8();
            sel.Forced = r.U8() != 0;
            r.U32();
            r.U32();
            var n = r.U32();
            for (var i = 0; i < n; i++)
            {
                var code = r.U32();
                var loc = r.Loc();
                r.U64();
                r.U8();
                sel.Chains.Add(new OcgCardRef
                {
                    Code = code,
                    Controller = loc.Controller,
                    Location = loc.Location,
                    Sequence = loc.Sequence
                });
            }
            return true;
        }

        public static bool TrySelectCard(OcgMessage msg, out OcgSelectCard sel)
        {
            sel = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectCard) return false;
            var r = new OcgReader(msg.Payload);
            sel = new OcgSelectCard
            {
                Player = r.U8(),
                Cancelable = r.U8() != 0,
                Min = r.U32(),
                Max = r.U32()
            };
            var n = r.U32();
            if (n < 0 || n > 128) return false;
            for (var i = 0; i < n; i++)
            {
                var code = r.U32();
                var loc = r.Loc();
                sel.Cards.Add(new OcgCardRef
                {
                    Code = code,
                    Controller = loc.Controller,
                    Location = loc.Location,
                    Sequence = loc.Sequence
                });
            }
            return r.Ok;
        }

        public static bool TrySelectYesNo(OcgMessage msg, out int player)
        {
            player = 0;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectYesNo) return false;
            player = new OcgReader(msg.Payload).U8();
            return true;
        }

        public static bool TrySelectEffectYn(OcgMessage msg, out OcgSelectEffectYn yn)
        {
            yn = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectEffectYn) return false;
            var r = new OcgReader(msg.Payload);
            yn = new OcgSelectEffectYn
            {
                Player = r.U8(),
                Code = r.U32(),
                Loc = r.Loc(),
                Desc = r.U64()
            };
            return r.Ok;
        }

        public static bool TrySelectOption(OcgMessage msg, out OcgSelectOption opt)
        {
            opt = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectOption) return false;
            var r = new OcgReader(msg.Payload);
            opt = new OcgSelectOption { Player = r.U8() };
            var n = r.U8();
            if (n < 1 || n > 64) return false;
            for (var i = 0; i < n; i++)
                opt.Descs.Add(r.U64());
            return r.Ok && opt.Descs.Count > 0;
        }

        public static bool TryAnnounceRace(OcgMessage msg, out OcgAnnounceBits bits)
        {
            bits = null;
            if (msg == null || msg.MsgId != OcgMessageIds.AnnounceRace) return false;
            var r = new OcgReader(msg.Payload);
            bits = new OcgAnnounceBits
            {
                Player = r.U8(),
                Count = r.U8(),
                Available = r.U64()
            };
            if (bits.Count < 1) bits.Count = 1;
            return r.Ok && bits.Available != 0;
        }

        public static bool TryAnnounceAttrib(OcgMessage msg, out OcgAnnounceBits bits)
        {
            bits = null;
            if (msg == null || msg.MsgId != OcgMessageIds.AnnounceAttrib) return false;
            var r = new OcgReader(msg.Payload);
            bits = new OcgAnnounceBits
            {
                Player = r.U8(),
                Count = r.U8(),
                Available = (uint)r.U32()
            };
            if (bits.Count < 1) bits.Count = 1;
            return r.Ok && bits.Available != 0;
        }

        public static bool TryAnnounceNumber(OcgMessage msg, out OcgAnnounceNumber num)
        {
            num = null;
            if (msg == null || msg.MsgId != OcgMessageIds.AnnounceNumber) return false;
            var r = new OcgReader(msg.Payload);
            num = new OcgAnnounceNumber { Player = r.U8() };
            var n = r.U8();
            if (n < 1 || n > 64) return false;
            for (var i = 0; i < n; i++)
                num.Values.Add((int)(r.U64() & 0xffffffff));
            return r.Ok && num.Values.Count > 0;
        }

        public static ulong FirstSetBits(ulong available, int count)
        {
            if (count < 1) count = 1;
            ulong chosen = 0;
            var n = 0;
            for (var i = 0; i < 64 && n < count; i++)
            {
                var bit = 1UL << i;
                if ((available & bit) == 0) continue;
                chosen |= bit;
                n++;
            }
            return chosen;
        }

        public static bool TrySelectTribute(OcgMessage msg, out OcgSelectCard sel)
        {
            sel = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectTribute) return false;
            var r = new OcgReader(msg.Payload);
            sel = new OcgSelectCard
            {
                Player = r.U8(),
                Cancelable = r.U8() != 0,
                Min = r.U32(),
                Max = r.U32()
            };
            var n = r.U32();
            if (n < 0 || n > 128) return false;
            for (var i = 0; i < n; i++)
            {
                var code = r.U32();
                var loc = r.Loc();
                r.U32();
                sel.Cards.Add(new OcgCardRef
                {
                    Code = code,
                    Controller = loc.Controller,
                    Location = loc.Location,
                    Sequence = loc.Sequence
                });
            }
            return r.Ok;
        }

        public static bool TrySelectPlace(OcgMessage msg, out OcgSelectPlace place)
        {
            place = null;
            if (msg == null || (msg.MsgId != OcgMessageIds.SelectPlace &&
                                msg.MsgId != OcgMessageIds.SelectDisfield))
                return false;
            var r = new OcgReader(msg.Payload);
            place = new OcgSelectPlace
            {
                Player = r.U8(),
                Count = r.U8(),
                Flag = (uint)r.U32()
            };
            if (place.Count == 0) place.Count = 1;
            return r.Ok;
        }

        public static bool TryFirstPlace(OcgSelectPlace place, out byte player, out byte location, out byte sequence)
        {
            player = 0;
            location = 0;
            sequence = 0;
            if (place == null) return false;
            var flag = place.Flag;
            var ask = (byte)place.Player;
            for (var bit = 0; bit < 32; bit++)
            {
                if ((flag & (1u << bit)) != 0) continue;
                if (bit < 8)
                {
                    player = ask;
                    location = (byte)OcgLocation.Mzone;
                    sequence = (byte)bit;
                }
                else if (bit < 16)
                {
                    player = ask;
                    location = (byte)OcgLocation.Szone;
                    sequence = (byte)(bit - 8);
                }
                else if (bit < 24)
                {
                    player = (byte)(1 - ask);
                    location = (byte)OcgLocation.Mzone;
                    sequence = (byte)(bit - 16);
                }
                else
                {
                    player = (byte)(1 - ask);
                    location = (byte)OcgLocation.Szone;
                    sequence = (byte)(bit - 24);
                }
                return true;
            }
            return false;
        }

        public static bool TrySelectPosition(OcgMessage msg, out OcgSelectPosition pos)
        {
            pos = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectPosition) return false;
            var r = new OcgReader(msg.Payload);
            pos = new OcgSelectPosition
            {
                Player = r.U8(),
                Code = r.U32(),
                Positions = r.U8()
            };
            return r.Ok && pos.Positions != 0;
        }

        public static int FirstPosition(int mask)
        {
            if ((mask & OcgPos.FaceUpAttack) != 0) return OcgPos.FaceUpAttack;
            if ((mask & OcgPos.FaceDownAttack) != 0) return OcgPos.FaceDownAttack;
            if ((mask & OcgPos.FaceUpDefense) != 0) return OcgPos.FaceUpDefense;
            if ((mask & OcgPos.FaceDownDefense) != 0) return OcgPos.FaceDownDefense;
            return 0;
        }

        public static bool TrySelectUnselect(OcgMessage msg, out OcgSelectUnselect sel)
        {
            sel = null;
            if (msg == null || msg.MsgId != OcgMessageIds.SelectUnselectCard) return false;
            var r = new OcgReader(msg.Payload);
            sel = new OcgSelectUnselect
            {
                Player = r.U8(),
                Finishable = r.U8() != 0,
                Cancelable = r.U8() != 0,
                Min = r.U32(),
                Max = r.U32()
            };
            var n = r.U32();
            if (n < 0 || n > 128) return false;
            for (var i = 0; i < n; i++)
            {
                var code = r.U32();
                var loc = r.Loc();
                sel.Cards.Add(new OcgCardRef
                {
                    Code = code,
                    Controller = loc.Controller,
                    Location = loc.Location,
                    Sequence = loc.Sequence
                });
            }
            return r.Ok;
        }

        public static DuelPhase PhaseFromBits(int bits)
        {
            if ((bits & OcgPhaseBits.Draw) != 0) return DuelPhase.Draw;
            if ((bits & OcgPhaseBits.Standby) != 0) return DuelPhase.Standby;
            if ((bits & OcgPhaseBits.Main1) != 0) return DuelPhase.Main1;
            if ((bits & OcgPhaseBits.Battle) != 0) return DuelPhase.Battle;
            if ((bits & OcgPhaseBits.Main2) != 0) return DuelPhase.Main2;
            if ((bits & OcgPhaseBits.End) != 0) return DuelPhase.End;
            return DuelPhase.Main1;
        }

        static void ReadListU32(OcgReader r, List<OcgCardRef> dst)
        {
            var n = r.U32();
            for (var i = 0; i < n; i++)
                dst.Add(r.CardU32Seq());
        }

        static void ReadListU8(OcgReader r, List<OcgCardRef> dst)
        {
            var n = r.U32();
            for (var i = 0; i < n; i++)
                dst.Add(r.CardU8Seq());
        }
    }
}
