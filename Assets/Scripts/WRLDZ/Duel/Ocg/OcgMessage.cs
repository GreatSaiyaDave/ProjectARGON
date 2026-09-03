namespace WRLDZ.Duel.Ocg
{
    /// <summary>Ignis ocgapi_constants.h — message ids used this pass.</summary>
    public static class OcgMessageIds
    {
        public const int Retry = 1;
        public const int Win = 5;
        public const int SelectBattleCmd = 10;
        public const int SelectIdleCmd = 11;
        public const int SelectEffectYn = 12;
        public const int SelectYesNo = 13;
        public const int SelectOption = 14;
        public const int SelectCard = 15;
        public const int SelectChain = 16;
        public const int SelectPlace = 18;
        public const int SelectPosition = 19;
        public const int SelectTribute = 20;
        public const int SortChain = 21;
        public const int SelectCounter = 22;
        public const int SelectSum = 23;
        public const int SelectDisfield = 24;
        public const int SortCard = 25;
        public const int SelectUnselectCard = 26;
        public const int NewTurn = 40;
        public const int NewPhase = 41;
        public const int Move = 50;
        public const int Set = 54;
        public const int Summoning = 60;
        public const int Summoned = 61;
        public const int Chaining = 70;
        public const int Chained = 71;
        public const int ChainSolving = 72;
        public const int ChainSolved = 73;
        public const int ChainEnd = 74;
        public const int Draw = 90;
        public const int Damage = 91;
        public const int Recover = 92;
        public const int LpUpdate = 94;
        public const int TossCoin = 130;
        public const int TossDice = 131;
        public const int RockPaperScissors = 132;
        public const int AnnounceRace = 140;
        public const int AnnounceAttrib = 141;
        public const int AnnounceCard = 142;
        public const int AnnounceNumber = 143;
    }

    public static class OcgLocation
    {
        public const int Deck = 0x01;
        public const int Hand = 0x02;
        public const int Mzone = 0x04;
        public const int Szone = 0x08;
        public const int Grave = 0x10;
        public const int Removed = 0x20;
        public const int Extra = 0x40;
    }

    public static class OcgPos
    {
        public const int FaceUpAttack = 0x1;
        public const int FaceDownAttack = 0x2;
        public const int FaceUpDefense = 0x4;
        public const int FaceDownDefense = 0x8;
        public const int FaceDown = 0xA;
    }

    public static class OcgPhaseBits
    {
        public const int Draw = 0x01;
        public const int Standby = 0x02;
        public const int Main1 = 0x04;
        public const int Battle = 0x80;
        public const int Main2 = 0x100;
        public const int End = 0x200;
    }

    public sealed class OcgMessage
    {
        public int MsgId;
        public byte[] Payload = System.Array.Empty<byte>();
    }

    public struct OcgLoc
    {
        public int Controller;
        public int Location;
        public int Sequence;
        public int Position;
    }

    public struct OcgCardRef
    {
        public int Code;
        public int Controller;
        public int Location;
        public int Sequence;
    }
}
