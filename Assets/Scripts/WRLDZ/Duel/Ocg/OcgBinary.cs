using System;
using System.Collections.Generic;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Little-endian Ignis buffers (non-compat mode: u32 sequence / position).</summary>
    public sealed class OcgWriter
    {
        readonly List<byte> _b = new();
        public void U8(int v) => _b.Add((byte)v);
        public void U16(int v)
        {
            _b.Add((byte)v);
            _b.Add((byte)(v >> 8));
        }
        public void U32(int v)
        {
            _b.Add((byte)v);
            _b.Add((byte)(v >> 8));
            _b.Add((byte)(v >> 16));
            _b.Add((byte)(v >> 24));
        }
        public void U64(ulong v)
        {
            U32((int)v);
            U32((int)(v >> 32));
        }
        public void Loc(OcgLoc loc)
        {
            U8(loc.Controller);
            U8(loc.Location);
            U32(loc.Sequence);
            U32(loc.Position);
        }
        public byte[] ToArray() => _b.ToArray();
    }

    public sealed class OcgReader
    {
        readonly byte[] _b;
        int _i;
        public OcgReader(byte[] b) { _b = b ?? Array.Empty<byte>(); }
        public int Remaining => _b.Length - _i;
        public bool Ok => _i <= _b.Length;
        public byte U8()
        {
            if (_i >= _b.Length) return 0;
            return _b[_i++];
        }
        public int U16()
        {
            var a = U8();
            var c = U8();
            return a | (c << 8);
        }
        public int U32()
        {
            var a = U8();
            var c = U8();
            var d = U8();
            var e = U8();
            return a | (c << 8) | (d << 16) | (e << 24);
        }
        public ulong U64()
        {
            var lo = (uint)U32();
            var hi = (uint)U32();
            return lo | ((ulong)hi << 32);
        }
        public OcgLoc Loc()
        {
            return new OcgLoc
            {
                Controller = U8(),
                Location = U8(),
                Sequence = U32(),
                Position = U32()
            };
        }
        public OcgCardRef CardU32Seq()
        {
            return new OcgCardRef
            {
                Code = U32(),
                Controller = U8(),
                Location = U8(),
                Sequence = U32()
            };
        }
        public OcgCardRef CardU8Seq()
        {
            return new OcgCardRef
            {
                Code = U32(),
                Controller = U8(),
                Location = U8(),
                Sequence = U8()
            };
        }
    }
}
