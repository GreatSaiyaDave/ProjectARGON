using System;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>DuelIntent → packed <c>OCG_DuelSetResponse</c> bytes. Never guesses an index.</summary>
    public static class OcgResponseEncoder
    {
        public static byte[] Idle(int type, int index) =>
            BitConverter.GetBytes((index << 16) | (type & 0xffff));

        public static byte[] Battle(int type, int index) => Idle(type, index);

        public static byte[] ChainPass() => BitConverter.GetBytes(-1);

        public static byte[] ChainIndex(int i) => BitConverter.GetBytes(i);

        public static byte[] SelectCardIndices(int[] indices)
        {
            var n = indices?.Length ?? 0;
            var buf = new byte[8 + n * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, buf, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(n), 0, buf, 4, 4);
            for (var i = 0; i < n; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(indices[i]), 0, buf, 8 + i * 4, 4);
            return buf;
        }

        public static byte[] SelectCardCancel() => BitConverter.GetBytes(-1);

        public static byte[] YesNo(bool yes) => BitConverter.GetBytes(yes ? 1 : 0);

        public static byte[] Place(byte player, byte location, byte sequence) =>
            new[] { player, location, sequence };

        public static byte[] Position(int pos) => BitConverter.GetBytes(pos);

        public static byte[] Option(int index) => BitConverter.GetBytes(index);

        public static byte[] AnnounceBits32(int bits) => BitConverter.GetBytes(bits);

        public static byte[] AnnounceBits64(ulong bits) => BitConverter.GetBytes(bits);

        public static bool TryIdleEnd(OcgIdleCommand idle, out byte[] buf)
        {
            buf = null;
            if (idle == null || !idle.ToEp) return false;
            buf = Idle(7, 0);
            return true;
        }

        public static bool TryIdleBattle(OcgIdleCommand idle, out byte[] buf)
        {
            buf = null;
            if (idle == null || !idle.ToBp) return false;
            buf = Idle(6, 0);
            return true;
        }
    }
}
