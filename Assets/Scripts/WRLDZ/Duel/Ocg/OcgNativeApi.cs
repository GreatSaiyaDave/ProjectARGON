using System;
using System.Runtime.InteropServices;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>P/Invoke of edo9300 ocgapi.h at fd2a557167f9e0fe19b867c325e67a3b3f9dca11.</summary>
    public static class OcgNativeApi
    {
        public const string Lib = "ocgcore";

        public const int CreationSuccess = 0;
        public const int NativeStatusEnd = 0;
        public const int NativeStatusAwaiting = 1;
        public const int NativeStatusContinue = 2;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DataReader(IntPtr payload, uint code, IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DataReaderDone(IntPtr payload, IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int ScriptReader(IntPtr payload, IntPtr duel, IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LogHandler(IntPtr payload, IntPtr str, int type);

        [StructLayout(LayoutKind.Sequential)]
        public struct Player
        {
            public uint startingLP;
            public uint startingDrawCount;
            public uint drawCountPerTurn;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DuelOptions
        {
            public ulong seed0, seed1, seed2, seed3;
            public ulong flags;
            public Player team1;
            public Player team2;
            public DataReader cardReader;
            public IntPtr payload1;
            public ScriptReader scriptReader;
            public IntPtr payload2;
            public LogHandler logHandler;
            public IntPtr payload3;
            public DataReaderDone cardReaderDone;
            public IntPtr payload4;
            public byte enableUnsafeLibraries;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CardData
        {
            public uint code;
            public uint alias;
            public IntPtr setcodes;
            public uint type;
            public uint level;
            public uint attribute;
            public ulong race;
            public int attack;
            public int defense;
            public uint lscale;
            public uint rscale;
            public uint link_marker;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct NewCardInfo
        {
            [FieldOffset(0)] public byte team;
            [FieldOffset(1)] public byte duelist;
            [FieldOffset(4)] public uint code;
            [FieldOffset(8)] public byte con;
            [FieldOffset(12)] public uint loc;
            [FieldOffset(16)] public uint seq;
            [FieldOffset(20)] public uint pos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct QueryInfo
        {
            public uint flags;
            public byte con;
            public uint loc;
            public uint seq;
            public uint overlay_seq;
        }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_GetVersion(out int major, out int minor);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int OCG_CreateDuel(out IntPtr duel, ref DuelOptions options);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_DestroyDuel(IntPtr duel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_DuelNewCard(IntPtr duel, in NewCardInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_StartDuel(IntPtr duel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int OCG_DuelProcess(IntPtr duel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr OCG_DuelGetMessage(IntPtr duel, out uint length);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_DuelSetResponse(IntPtr duel, byte[] buffer, uint length);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int OCG_LoadScript(IntPtr duel, byte[] buffer, uint length,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr OCG_DuelQueryLocation(IntPtr duel, out uint length, in QueryInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint OCG_DuelQueryCount(IntPtr duel, byte team, uint loc);
    }
}
