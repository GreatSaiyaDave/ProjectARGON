using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WRLDZ.Duel.Ocg
{
    public sealed class NativeOcgDuelCore : IOcgDuelCore
    {
        public bool IsWaiting { get; private set; }
        public int WaitingPlayer { get; private set; }
        public OcgDuelStatus Status { get; private set; } = OcgDuelStatus.End;

        IntPtr _duel;
        OcgNativeApi.DataReader _cardReader;
        OcgNativeApi.DataReaderDone _cardReaderDone;
        OcgNativeApi.ScriptReader _scriptReader;
        OcgNativeApi.LogHandler _logHandler;
        readonly List<IntPtr> _setcodeAllocs = new List<IntPtr>();
        int _lastScriptLoad;
        int _cardsRegistered;
        int _scriptReadOk;
        int _scriptReadFail;
        int _scriptLoadZero;

        public int CardsRegistered => _cardsRegistered;
        public int LastScriptLoadResult => _lastScriptLoad;
        public int ScriptReadOk => _scriptReadOk;
        public int ScriptReadFail => _scriptReadFail;
        public int ScriptLoadZero => _scriptLoadZero;

        public void CreateDuel(uint seed, OcgDuelStartInfo info)
        {
            if (!OcgNative.TryLoad(out var err))
                throw new NotSupportedException(err);
            if (!OcgCardCatalog.SqliteReady)
                throw new NotSupportedException("sqlite/CDB required for native ocgcore");

            Dispose();
            _cardReader = CardReader;
            _cardReaderDone = CardReaderDone;
            _scriptReader = ScriptReader;
            _logHandler = LogHandler;

            var opts = new OcgNativeApi.DuelOptions
            {
                seed0 = seed == 0 ? 1u : seed,
                seed1 = info?.Seed != null && info.Seed.Length > 1 ? info.Seed[1] : 0,
                seed2 = info?.Seed != null && info.Seed.Length > 2 ? info.Seed[2] : 0,
                seed3 = info?.Seed != null && info.Seed.Length > 3 ? info.Seed[3] : 0,
                flags = 0,
                team1 = Player(info),
                team2 = Player(info),
                cardReader = _cardReader,
                scriptReader = _scriptReader,
                logHandler = _logHandler,
                cardReaderDone = _cardReaderDone,
                enableUnsafeLibraries = 1
            };

            var rc = OcgNativeApi.OCG_CreateDuel(out _duel, ref opts);
            if (rc != OcgNativeApi.CreationSuccess || _duel == IntPtr.Zero)
                throw new InvalidOperationException("OCG_CreateDuel failed: " + rc);

            if (OcgScriptStore.LoadGlobal(_duel, "constant.lua") == 0)
                throw new InvalidOperationException("failed to load constant.lua");
            if (OcgScriptStore.LoadGlobal(_duel, "utility.lua") == 0)
                throw new InvalidOperationException("failed to load utility.lua");
            if (OcgScriptStore.LoadGlobal(_duel, "procedure.lua") == 0)
                throw new InvalidOperationException("failed to load procedure.lua");

            _cardsRegistered = 0;
            _scriptReadOk = 0;
            _scriptReadFail = 0;
            _scriptLoadZero = 0;
            RegisterPile(0, info?.PlayerMain, (uint)OcgLocation.Deck);
            RegisterPile(1, info?.OpponentMain, (uint)OcgLocation.Deck);
            RegisterPile(0, info?.PlayerExtra, (uint)OcgLocation.Extra);
            RegisterPile(1, info?.OpponentExtra, (uint)OcgLocation.Extra);

            var c0 = OcgNativeApi.OCG_DuelQueryCount(_duel, 0, (uint)OcgLocation.Deck);
            var c1 = OcgNativeApi.OCG_DuelQueryCount(_duel, 1, (uint)OcgLocation.Deck);
            var e0 = OcgNativeApi.OCG_DuelQueryCount(_duel, 0, (uint)OcgLocation.Extra);
            var e1 = OcgNativeApi.OCG_DuelQueryCount(_duel, 1, (uint)OcgLocation.Extra);
            var want0 = info?.PlayerMain?.Length ?? 0;
            var want1 = info?.OpponentMain?.Length ?? 0;
            var wantE0 = info?.PlayerExtra?.Length ?? 0;
            var wantE1 = info?.OpponentExtra?.Length ?? 0;
            if (c0 != want0 || c1 != want1)
                throw new InvalidOperationException(
                    "OCG_DuelQueryCount mismatch deck0=" + c0 + "/" + want0 + " deck1=" + c1 + "/" + want1);
            if (e0 != wantE0 || e1 != wantE1)
                throw new InvalidOperationException(
                    "OCG_DuelQueryCount mismatch extra0=" + e0 + "/" + wantE0 + " extra1=" + e1 + "/" + wantE1);

            if (info == null || info.Start)
            {
                OcgNativeApi.OCG_StartDuel(_duel);
                Status = OcgDuelStatus.Continue;
            }
            else
                Status = OcgDuelStatus.End;
            IsWaiting = false;
        }

        public int LoadScriptByName(string name)
        {
            if (_duel == IntPtr.Zero)
                throw new InvalidOperationException("no duel");
            return OcgScriptStore.LoadGlobal(_duel, name);
        }

        public uint QueryCount(byte team, uint loc)
        {
            if (_duel == IntPtr.Zero) return 0;
            return OcgNativeApi.OCG_DuelQueryCount(_duel, team, loc);
        }

        public IReadOnlyList<OcgMessage> Process()
        {
            if (_duel == IntPtr.Zero) return Array.Empty<OcgMessage>();
            var flag = OcgNativeApi.OCG_DuelProcess(_duel);
            Status = MapStatus(flag);
            IsWaiting = Status == OcgDuelStatus.Awaiting;
            var msgs = ReadMessages();
            WaitingPlayer = GuessWaitingPlayer(msgs);
            return msgs;
        }

        public void SetResponse(byte[] buf)
        {
            if (_duel == IntPtr.Zero || buf == null) return;
            OcgNativeApi.OCG_DuelSetResponse(_duel, buf, (uint)buf.Length);
        }

        public byte[] QueryLocation(int player, int location, int queryFlag)
        {
            if (_duel == IntPtr.Zero) return Array.Empty<byte>();
            var q = new OcgNativeApi.QueryInfo
            {
                flags = (uint)queryFlag,
                con = (byte)player,
                loc = (uint)location
            };
            var ptr = OcgNativeApi.OCG_DuelQueryLocation(_duel, out var len, in q);
            if (ptr == IntPtr.Zero || len == 0) return Array.Empty<byte>();
            var buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, (int)len);
            return buf;
        }

        public void Dispose()
        {
            if (_duel != IntPtr.Zero)
            {
                OcgNativeApi.OCG_DestroyDuel(_duel);
                _duel = IntPtr.Zero;
            }
            foreach (var p in _setcodeAllocs)
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            _setcodeAllocs.Clear();
            IsWaiting = false;
            Status = OcgDuelStatus.End;
        }

        void RegisterPile(byte team, int[] codes, uint loc)
        {
            if (codes == null) return;
            for (var i = 0; i < codes.Length; i++)
            {
                var info = new OcgNativeApi.NewCardInfo
                {
                    team = team,
                    duelist = 0,
                    code = (uint)codes[i],
                    con = team,
                    loc = loc,
                    seq = 1,
                    pos = (uint)OcgPos.FaceDown
                };
                OcgNativeApi.OCG_DuelNewCard(_duel, in info);
                _cardsRegistered++;
            }
        }

        static OcgNativeApi.Player Player(OcgDuelStartInfo info) => new OcgNativeApi.Player
        {
            startingLP = (uint)(info != null && info.StartingLp > 0 ? info.StartingLp : 8000),
            startingDrawCount = (uint)(info != null && info.StartingDraw > 0 ? info.StartingDraw : 5),
            drawCountPerTurn = (uint)(info != null && info.DrawPerTurn > 0 ? info.DrawPerTurn : 1)
        };

        void CardReader(IntPtr payload, uint code, IntPtr dataPtr)
        {
            var native = new OcgNativeApi.CardData();
            if (OcgCardCatalog.TryGetFromSqlite(code, out var src) && src != null && src.Code != 0)
            {
                native.code = src.Code;
                native.alias = src.Alias;
                native.type = src.Type;
                native.level = src.Level;
                native.attribute = src.Attribute;
                native.race = src.Race;
                native.attack = src.Attack;
                native.defense = src.Defense;
                native.lscale = src.Lscale;
                native.rscale = src.Rscale;
                native.link_marker = src.LinkMarker;
                var sc = src.Setcodes ?? Array.Empty<ushort>();
                var n = sc.Length + 1;
                var p = Marshal.AllocHGlobal(n * 2);
                for (var i = 0; i < sc.Length; i++)
                    Marshal.WriteInt16(p, i * 2, (short)sc[i]);
                Marshal.WriteInt16(p, sc.Length * 2, 0);
                native.setcodes = p;
                _setcodeAllocs.Add(p);
            }
            else
            {
                Debug.LogWarning("[WRLDZ OCG] cardReader missing " + code);
            }
            Marshal.StructureToPtr(native, dataPtr, false);
        }

        void CardReaderDone(IntPtr payload, IntPtr dataPtr)
        {
            if (dataPtr == IntPtr.Zero) return;
            var native = Marshal.PtrToStructure<OcgNativeApi.CardData>(dataPtr);
            if (native.setcodes != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(native.setcodes);
                _setcodeAllocs.Remove(native.setcodes);
            }
        }

        int ScriptReader(IntPtr payload, IntPtr duel, IntPtr namePtr)
        {
            var name = Marshal.PtrToStringUTF8(namePtr) ?? Marshal.PtrToStringAnsi(namePtr);
            if (!OcgScriptStore.TryRead(name, out var bytes, out var err))
            {
                Debug.LogWarning("[WRLDZ OCG] scriptReader " + name + ": " + err);
                _lastScriptLoad = 0;
                _scriptReadFail++;
                return 0;
            }
            _scriptReadOk++;
            _lastScriptLoad = OcgNativeApi.OCG_LoadScript(duel, bytes, (uint)bytes.Length, name);
            if (_lastScriptLoad == 0)
                _scriptLoadZero++;
            return _lastScriptLoad;
        }

        static void LogHandler(IntPtr payload, IntPtr str, int type)
        {
            var s = str == IntPtr.Zero ? "" : (Marshal.PtrToStringUTF8(str) ?? Marshal.PtrToStringAnsi(str));
            Debug.Log("[ocgcore " + type + "] " + s);
        }

        List<OcgMessage> ReadMessages()
        {
            var list = new List<OcgMessage>();
            var ptr = OcgNativeApi.OCG_DuelGetMessage(_duel, out var length);
            if (ptr == IntPtr.Zero || length == 0) return list;
            var buf = new byte[length];
            Marshal.Copy(ptr, buf, 0, (int)length);
            var o = 0;
            while (o + 4 <= buf.Length)
            {
                var size = BitConverter.ToInt32(buf, o);
                if (size <= 0 || o + 4 + size > buf.Length)
                    break;
                o += 4;
                var payload = new byte[size];
                Buffer.BlockCopy(buf, o, payload, 0, size);
                o += size;
                list.Add(new OcgMessage
                {
                    MsgId = payload.Length > 0 ? payload[0] : 0,
                    Payload = payload.Length > 1 ? Slice(payload, 1) : Array.Empty<byte>()
                });
            }
            if (list.Count == 0 && buf.Length > 0)
            {
                list.Add(new OcgMessage
                {
                    MsgId = buf[0],
                    Payload = buf.Length > 1 ? Slice(buf, 1) : Array.Empty<byte>()
                });
            }
            return list;
        }

        static byte[] Slice(byte[] src, int start)
        {
            var n = src.Length - start;
            var a = new byte[n];
            Buffer.BlockCopy(src, start, a, 0, n);
            return a;
        }

        static int GuessWaitingPlayer(IReadOnlyList<OcgMessage> msgs)
        {
            if (msgs == null || msgs.Count == 0) return 0;
            var last = msgs[msgs.Count - 1];
            if (last.Payload != null && last.Payload.Length > 0)
                return last.Payload[0];
            return 0;
        }

        static OcgDuelStatus MapStatus(int native)
        {
            if (native == OcgNativeApi.NativeStatusEnd) return OcgDuelStatus.End;
            if (native == OcgNativeApi.NativeStatusAwaiting) return OcgDuelStatus.Awaiting;
            return OcgDuelStatus.Continue;
        }
    }

    public static class OcgNative
    {
        public const string LinuxLib = "ocgcore";
        public const string WindowsLib = "ocgcore";

        public static string ResolvePluginFile()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return Path.Combine(Application.dataPath, "Plugins", "x86_64", "ocgcore.dll");
#else
            return Path.Combine(Application.dataPath, "Plugins", "x86_64", "libocgcore.so");
#endif
        }

        public static bool TryLoad(out string error)
        {
            error = null;
            var path = ResolvePluginFile();
            if (!File.Exists(path))
            {
                error = "missing plugin " + path;
                return false;
            }
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            if (LddHasMissing(path, out var miss))
            {
                error = "ldd missing dependency: " + miss;
                return false;
            }
#endif
            var hash = Sha256Hex(path);
            var pin = ExpectedHash();
            if (!string.IsNullOrEmpty(pin) &&
                !string.Equals(hash, pin, StringComparison.OrdinalIgnoreCase))
            {
                error = "plugin sha256 drift got=" + hash + " pin=" + pin;
                return false;
            }
            try
            {
                OcgNativeApi.OCG_GetVersion(out var major, out var minor);
                if (major != 11)
                {
                    error = "OCG_GetVersion major=" + major + " minor=" + minor + " (need 11)";
                    return false;
                }
            }
            catch (DllNotFoundException ex)
            {
                error = "DllNotFoundException: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "OCG_GetVersion failed: " + ex.Message;
                return false;
            }
            return true;
        }

        public static string Sha256Hex(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(fs);
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static string ExpectedHash()
        {
            var p = Path.Combine(Application.streamingAssetsPath, "OcgCore", "PROVENANCE.json");
            if (!File.Exists(p)) return null;
            var json = File.ReadAllText(p);
            var key =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                "windowsDll";
#else
                "linuxSo";
#endif
            var marker = "\"" + key + "\"";
            var i = json.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            var s = json.IndexOf("\"sha256\"", i, StringComparison.Ordinal);
            if (s < 0) return null;
            var q1 = json.IndexOf('"', json.IndexOf(':', s) + 1);
            var q2 = json.IndexOf('"', q1 + 1);
            if (q1 < 0 || q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        static bool LddHasMissing(string path, out string detail)
        {
            detail = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ldd",
                    Arguments = "\"" + path + "\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using (var proc = Process.Start(psi))
                {
                    var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                    proc.WaitForExit(5000);
                    if (output.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        detail = output;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ OCG] ldd failed: " + ex.Message);
            }
            return false;
        }
    }

    public static class OcgLabDecks
    {
        public static int[] ExpandMain(string fileName) => Expand(fileName, extra: false);

        public static int[] ExpandExtra(string fileName) => Expand(fileName, extra: true);

        static int[] Expand(string fileName, bool extra)
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Decks", fileName);
            var deck = JsonUtility.FromJson<WRLDZ.Data.DeckFile>(File.ReadAllText(path));
            var src = extra ? deck?.extra : deck?.main;
            var list = new List<int>();
            if (src == null) return Array.Empty<int>();
            foreach (var e in src)
            {
                for (var i = 0; i < e.qty; i++)
                    list.Add(e.id);
            }
            return list.ToArray();
        }
    }
}
