using System;
using System.Collections.Generic;

namespace WRLDZ.Duel.Ocg
{
    public enum OcgDuelStatus
    {
        Continue = 0,
        End = 1,
        Awaiting = 2
    }

    public sealed class OcgDuelStartInfo
    {
        public uint[] Seed = { 1, 0, 0, 0 };
        public int StartingLp = 8000;
        public int StartingDraw = 5;
        public int DrawPerTurn = 1;
        public int[] PlayerMain = Array.Empty<int>();
        public int[] OpponentMain = Array.Empty<int>();
        public int[] PlayerExtra = Array.Empty<int>();
        public int[] OpponentExtra = Array.Empty<int>();
        public bool Start = true;
        public string ScriptDir;
        public string CdbPath;
    }

    public interface IOcgDuelCore : IDisposable
    {
        void CreateDuel(uint seed, OcgDuelStartInfo info);
        IReadOnlyList<OcgMessage> Process();
        void SetResponse(byte[] buf);
        bool IsWaiting { get; }
        int WaitingPlayer { get; }
        OcgDuelStatus Status { get; }
        byte[] QueryLocation(int player, int location, int queryFlag);
    }
}
